// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using static GenerallyPositive.Enums;
using System.Runtime.CompilerServices;

namespace GenerallyPositive
{
    public class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>
    /// Clone an object into another object, does not have to be the same type, but same named fields/properties will be set
    /// Use GPAL.Converter.WithInput(class).SaveTo(ref outClass) to access functionality
    /// </summary>
    internal static class ObjectCopier
    {
        public static T DeepCopyObject<T>(dynamic source, T destination, DeepCopy deepCopy, HashSet<object> visited = null)
        {
            if (source == null || destination == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Source and/or destination must not be null. Returning whichever is not null or default(T).", null, GPALObjectType.None);
                return null != source ? source : null != destination ? destination : default(T);
            }

            visited = visited ?? new HashSet<object>(new ReferenceEqualityComparer());
            if (!visited.Add(source))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Circular reference detected in DeepCopyObject @ [{source}].", source, GPALObjectType.None);
                return destination;
            }

            Type sourceType = source.GetType();

            // Handle dictionaries
            if (typeof(IDictionary).IsAssignableFrom(sourceType))
            {
                if (!typeof(IDictionary).IsAssignableFrom(destination.GetType()))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Destination [{destination.GetType()}] is not a compatible [{sourceType}] dictionary type.", source, GPALObjectType.None);
                    return destination;
                }
                CopyDictionary((IDictionary)source, (IDictionary)destination, deepCopy, visited);
            }
            // Handle enumerables (lists, arrays, etc.)
            else if (typeof(IEnumerable).IsAssignableFrom(sourceType) && !IsImmutableType(sourceType))
            {
                if (!typeof(IList).IsAssignableFrom(destination.GetType()))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Destination [{destination.GetType()}] is not a compatible list type.", source, GPALObjectType.None);
                    return destination;
                }
                CopyObjects((IEnumerable)source, (IList)destination, deepCopy, visited);
            }
            // Handle single objects
            else
            {
                destination = CopyMembers(source, destination, deepCopy, visited);
            }

            return destination;
        }

        static void CopyDictionary(IDictionary sourceDict, IDictionary destinationDict, DeepCopy deepCopy, HashSet<object> visited)
        {
            destinationDict.Clear(); // Clear the destination to avoid duplicate entries

            foreach (var key in sourceDict.Keys)
            {
                // Sanitize dynamic key to prevent misinterpretation as culture identifier
                object safeKey;
                if (key is string strKey)
                    safeKey = strKey;
                else if (key is int intKey)
                    safeKey = intKey;
                else if (key is null)
                    safeKey = null;
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Unsupported dynamic key type [{key.GetType().Name}]. Using ToString as fallback.", key, GPALObjectType.None);
                    safeKey = key.ToString();
                }

                var sourceValue = sourceDict[key];
                if (sourceValue == null)
                {
                    destinationDict[safeKey] = null;
                    continue;
                }

                Type valueType = sourceValue.GetType();

                destinationDict[safeKey] = sourceValue;
            }
        }

        static IList CopyObjects(IEnumerable sourceObjects, IList destinationObjects, DeepCopy deepCopy, HashSet<object> visited)
        {
            destinationObjects.Clear(); // Clear to avoid duplicates
            foreach (var source in sourceObjects)
            {
                if (source == null)
                {
                    destinationObjects.Add(null);
                    continue;
                }

                var destination = ConverterHelper.InstantiateOne(source.GetType());
                destination = DeepCopyObject(source, destination, deepCopy, visited); // Pass visited
                destinationObjects.Add(destination);
            }
            return destinationObjects;
        }

        public static T CopyMembers<T>(object source, T destination, DeepCopy deepCopy, HashSet<object> visited)
        {
            if (source == null || destination == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Source or destination is null in CopyMembers.", null, GPALObjectType.None);
                return destination;
            }

            Type sourceType = source.GetType();
            Type destType = destination.GetType();

            // Handle specific types like IGPALGrid<string>
            if (source is IGPALGrid<string>)
            {
                return (T)sourceType.GetMethod("Clone")?.Invoke(source, null) ?? destination;
            }

            // Copy fields if specified
            if (deepCopy == DeepCopy.Fields || deepCopy == DeepCopy.FieldsAndProperties)
            {
                FieldInfo[] fields = sourceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (FieldInfo field in fields)
                {
                    if (IsSystemInternalType(field.FieldType) || IsCultureRelatedType(field.FieldType))
                    {
                        continue;
                    }

                    try
                    {
                        object sourceValue = field.GetValue(source);
                        if (sourceValue == null)
                        {
                            field.SetValue(destination, null);
                            continue;
                        }

                        Type fieldType = field.FieldType;

                        if (typeof(IEnumerable).IsAssignableFrom(fieldType) && !IsImmutableType(fieldType))
                        {
                            object destEnumerable = ConverterHelper.InstantiateOne(fieldType);
                            if (destEnumerable == null)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to instantiate enumerable type [{fieldType.Name}].", source, GPALObjectType.None);
                                continue;
                            }

                            if (typeof(IDictionary).IsAssignableFrom(fieldType))
                            {
                                CopyDictionary((IDictionary)sourceValue, (IDictionary)destEnumerable, deepCopy, visited);
                            }
                            else if (typeof(IList).IsAssignableFrom(fieldType))
                            {
                                CopyObjects((IEnumerable)sourceValue, (IList)destEnumerable, deepCopy, visited);
                            }
                            else
                            {
                                CopyGenericEnumerable((IEnumerable)sourceValue, destEnumerable, fieldType, deepCopy, visited);
                            }

                            field.SetValue(destination, destEnumerable);
                        }
                        else if (!fieldType.IsValueType && !IsImmutableType(fieldType) && !IsSystemInternalType(fieldType) && !IsCultureRelatedType(fieldType))
                        {
                            object destValue = ConverterHelper.InstantiateOne(fieldType);
                            destValue = DeepCopyObject(sourceValue, destValue, deepCopy, visited); // Pass visited
                            field.SetValue(destination, destValue);
                        }
                        else
                        {
                            field.SetValue(destination, sourceValue); // Shallow copy
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to copy field [{field.Name}] of type [{sourceType.Name}].", source, GPALObjectType.Other, ex);
                    }
                }
            }

            // Copy properties if specified
            if (deepCopy == DeepCopy.Properties || deepCopy == DeepCopy.FieldsAndProperties)
            {
                PropertyInfo[] properties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (PropertyInfo property in properties)
                {
                    if (property.GetIndexParameters().Length > 0 || // Skip indexers
                        property.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0 || // Skip compiler-generated properties
                        (property.DeclaringType != null && (typeof(ICollection).IsAssignableFrom(property.DeclaringType) ||
                                                           IsSystemInternalType(property.DeclaringType) ||
                                                           IsCultureRelatedType(property.DeclaringType))))
                    {
                        continue;
                    }

                    try
                    {
                        if (property.CanRead && property.CanWrite)
                        {
                            object sourceValue = property.GetValue(source);
                            if (sourceValue == null)
                            {
                                property.SetValue(destination, null);
                                continue;
                            }

                            Type propType = property.PropertyType;

                            if (typeof(IEnumerable).IsAssignableFrom(propType) && !IsImmutableType(propType))
                            {
                                object destEnumerable = ConverterHelper.InstantiateOne(propType);
                                if (destEnumerable == null)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to instantiate enumerable type [{propType.Name}].", source, GPALObjectType.None);
                                    continue;
                                }

                                if (typeof(IDictionary).IsAssignableFrom(propType))
                                {
                                    CopyDictionary((IDictionary)sourceValue, (IDictionary)destEnumerable, deepCopy, visited);
                                }
                                else if (typeof(IList).IsAssignableFrom(propType))
                                {
                                    CopyObjects((IEnumerable)sourceValue, (IList)destEnumerable, deepCopy, visited);
                                }
                                else
                                {
                                    CopyGenericEnumerable((IEnumerable)sourceValue, destEnumerable, propType, deepCopy, visited);
                                }

                                property.SetValue(destination, destEnumerable);
                            }
                            else if (!propType.IsValueType && !IsImmutableType(propType) && !IsSystemInternalType(propType) && !IsCultureRelatedType(propType))
                            {
                                object destValue = ConverterHelper.InstantiateOne(propType);
                                destValue = DeepCopyObject(sourceValue, destValue, deepCopy, visited); // Pass visited
                                property.SetValue(destination, destValue);
                            }
                            else
                            {
                                property.SetValue(destination, sourceValue); // Shallow copy
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to copy property [{property.Name}] of type [{sourceType.Name}].", source, GPALObjectType.Other, ex);
                    }
                }
            }

            return destination;
        }

        private static void CopyGenericEnumerable(IEnumerable sourceEnumerable, object destEnumerable, Type enumerableType, DeepCopy deepCopy, HashSet<object> visited)
        {
            MethodInfo addMethod = enumerableType.GetMethod("Add");
            if (addMethod == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No Add method found for type [{enumerableType.Name}].", null, GPALObjectType.None);
                return;
            }

            foreach (var item in sourceEnumerable)
            {
                if (item == null)
                {
                    addMethod.Invoke(destEnumerable, new object[] { null });
                    continue;
                }

                Type itemType = item.GetType();
                object destItem;

                if (!itemType.IsValueType && !IsImmutableType(itemType) && !IsSystemInternalType(itemType) && !IsCultureRelatedType(itemType))
                {
                    if (!visited.Add(item))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Circular reference detected for item of type [{itemType.Name}].", item, GPALObjectType.None);
                        continue;
                    }

                    destItem = ConverterHelper.InstantiateOne(itemType);
                    destItem = DeepCopyObject(item, destItem, deepCopy, visited); // Pass visited
                }
                else
                {
                    destItem = item; // Shallow copy
                }

                addMethod.Invoke(destEnumerable, new object[] { destItem });
            }
        }

        private static bool IsImmutableType(Type type)
        {
            return type == typeof(string) ||
                   type == typeof(DateTime) ||
                   type == typeof(TimeSpan) ||
                   type == typeof(decimal) ||
                   type == typeof(Guid);
        }

        private static bool IsSystemInternalType(Type type)
        {
            return type.Namespace != null &&
                   (type.Namespace.StartsWith("System.Reflection") ||
                    type.Namespace.StartsWith("System.Runtime") ||
                    type.Namespace.StartsWith("System.Threading") ||
                    type.Namespace.StartsWith("System.Security"));
        }

        private static bool IsCultureRelatedType(Type type)
        {
            return type == typeof(System.Globalization.CultureInfo) ||
                   type.Namespace == "System.Globalization";
        }

        public static int GetItemCount(dynamic propertyOrField, dynamic source)
        {
            int count = 0;

            if (source is IEnumerable<object> objectEnumerable)
            {
                return objectEnumerable.Count();
            }
            else if (source is Array array)
            {
                return array.Length;
            }
            else if (source is ICollection collection)
            {
                return collection.Count;
            }
            else if (null != propertyOrField)
            {
                dynamic enumeration = propertyOrField.GetValue(source);
                if (null != enumeration)
                    foreach (dynamic item in enumeration)
                        count++;
                return count;
            }
            else
            {
                try
                {
                    foreach (var item in source)
                        count++;
                } 
                catch (Exception)
                { }
                return count;
            }
        }

        static bool IsPropertyEnumerable(PropertyInfo property)
        {
            return typeof(IEnumerable).IsAssignableFrom(property.PropertyType);
        }

        static bool IsFieldEnumerable(FieldInfo field)
        {
            return typeof(IEnumerable).IsAssignableFrom(field.FieldType);
        }
    }
}
