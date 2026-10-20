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
using System.Text;
using System.Threading.Tasks;
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
    /// Use GPAL.Converter.WithInput(class).SaveTo(ref outClass) to access functionality, altho there is no granular control over copying fields or properties
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

            Type sourceType = source.GetType();

            // Handle dictionaries
            if (typeof(IDictionary).IsAssignableFrom(sourceType))
            {
                if (!typeof(IDictionary).IsAssignableFrom(destination.GetType()))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Destination [{destination.GetType()}] is not a compatible [{sourceType}] dictionary type.", source, GPALObjectType.None);
                    return destination;
                }
                CopyDictionary((IDictionary)source, (IDictionary)destination, deepCopy);
            }
            // Handle enumerables (lists, arrays, etc.)
            else if (typeof(IEnumerable).IsAssignableFrom(sourceType) && !IsImmutableType(sourceType))
            {
                if (!typeof(IList).IsAssignableFrom(destination.GetType()))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Destination [{destination.GetType()}] is not a compatible list type.", source, GPALObjectType.None);
                    return destination;
                }
                CopyObjects((IEnumerable)source, (IList)destination, deepCopy);
            }
            // Handle single objects
            else
            {
                if (DeepCopy.Fields == deepCopy || DeepCopy.FieldsAndProperties == deepCopy)
                    destination = CopyFields(source, destination, deepCopy, visited);
                if (DeepCopy.Properties == deepCopy || DeepCopy.FieldsAndProperties == deepCopy)
                    destination = CopyProperties(source, destination, deepCopy, visited);
            }

            return destination;
        }
        static void CopyDictionary(IDictionary sourceDict, IDictionary destinationDict, DeepCopy deepCopy)
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
                    // Log warning and use ToString as fallback to avoid invalid culture identifiers
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported dynamic key type [{key.GetType().Name}]. Using ToString as fallback.", key, GPALObjectType.None);
                    safeKey = key.ToString();
                }

                var sourceValue = sourceDict[key];
                if (sourceValue == null)
                {
                    destinationDict[safeKey] = null;
                    continue;
                }

                Type valueType = sourceValue.GetType();
                object destinationValue;

                // Skip copying if the type is internal or culture-related
                if (IsSystemInternalType(valueType) || IsCultureRelatedType(valueType))
                {
                    destinationValue = sourceValue; // Shallow copy to avoid issues
                }
                else
                {
                    destinationValue = ConverterHelper.InstantiateOne(valueType);

                    // Recursively copy fields and/or properties only for non-immutable types
                    if (!IsImmutableType(valueType))
                    {
                        if (DeepCopy.Fields == deepCopy || DeepCopy.FieldsAndProperties == deepCopy)
                            destinationValue = CopyFields(sourceValue, destinationValue, deepCopy);
                        if (DeepCopy.Properties == deepCopy || DeepCopy.FieldsAndProperties == deepCopy)
                            destinationValue = CopyProperties(sourceValue, destinationValue, deepCopy);
                    }
                    else
                    {
                        destinationValue = sourceValue; // Shallow copy for immutable types
                    }
                }

                destinationDict[safeKey] = destinationValue;
            }
        }
        static IList CopyObjects(IEnumerable sourceObjects, IList destinationObjects, DeepCopy deepCopy)
        {
            foreach (var source in sourceObjects)
            {
                var destination = ConverterHelper.InstantiateOne(source.GetType());
                if (DeepCopy.Fields == deepCopy || DeepCopy.FieldsAndProperties == deepCopy)
                    destination = CopyFields(source, destination);
                if (DeepCopy.Properties == deepCopy || DeepCopy.FieldsAndProperties == deepCopy)
                    destination = CopyProperties(source, destination);
                destinationObjects.Add(destination);
            }
            return destinationObjects;
        }
        public static T CopyProperties<T>(object source, T destination, DeepCopy deepCopy = DeepCopy.FieldsAndProperties, HashSet<object> visited = null)
        {
            visited = visited ?? new HashSet<object>(new ReferenceEqualityComparer());
            if (!visited.Add(source))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Circular reference detected in CopyProperties @ [{source}].", source, GPALObjectType.None);
                return destination;
            }

            Type sourceType = source.GetType();
            Type destType = destination.GetType();
            PropertyInfo[] properties = sourceType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Handle dictionary types
            if (typeof(IDictionary).IsAssignableFrom(sourceType))
            {
                if (!typeof(IDictionary).IsAssignableFrom(destType))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Destination [{destType}] is not a compatible [{sourceType}] dictionary type.", source, GPALObjectType.None);
                    return destination;
                }
                CopyDictionary((IDictionary)source, (IDictionary)destination, deepCopy);
            }
            // Handle specific types like IGPALGrid<string>
            else if (source is IGPALGrid<string>)
            {
                destination = (T)sourceType.GetMethod("Clone")?.Invoke(source, null) ?? destination;
            }
            // Handle other types
            else
            {
                foreach (PropertyInfo property in properties)
                {
                    // Skip indexers, collection interface properties, and system-internal/culture-related properties
                    if (property.GetIndexParameters().Length > 0 || // Skip indexers
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

                            // Handle enumerable properties (lists, arrays, sets, etc.)
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
                                    CopyDictionary((IDictionary)sourceValue, (IDictionary)destEnumerable, deepCopy);
                                }
                                else if (typeof(IList).IsAssignableFrom(propType))
                                {
                                    CopyObjects((IEnumerable)sourceValue, (IList)destEnumerable, deepCopy);
                                }
                                else
                                {
                                    // Handle other enumerables (e.g., HashSet<T>, Queue<T>)
                                    CopyGenericEnumerable((IEnumerable)sourceValue, destEnumerable, propType, deepCopy, visited);
                                }

                                property.SetValue(destination, destEnumerable);
                            }
                            // Handle complex objects (non-value types, non-immutable, non-system)
                            else if (!propType.IsValueType && !IsImmutableType(propType) && !IsSystemInternalType(propType) && !IsCultureRelatedType(propType))
                            {
                                object destValue = ConverterHelper.InstantiateOne(propType);
                                var nestedVisited = new HashSet<object>(new ReferenceEqualityComparer());
                                destValue = DeepCopyObject(sourceValue, destValue, deepCopy, nestedVisited);
                                property.SetValue(destination, destValue);
                            }
                            // Handle value types, strings, immutable types, and system types
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

        public static T CopyFields<T>(object source, T destination, DeepCopy deepCopy = DeepCopy.FieldsAndProperties, HashSet<object> visited = null)
        {
            visited = visited ?? new HashSet<object>(new ReferenceEqualityComparer());
            if (!visited.Add(source))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Circular reference detected in CopyFields @ [{source}].", source, GPALObjectType.None);
                return destination;
            }

            Type sourceType = source.GetType();
            Type destType = destination.GetType();
            FieldInfo[] fields = sourceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            foreach (FieldInfo field in fields)
            {
                // Skip system-internal or culture-related fields
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

                    // Handle enumerable fields (lists, arrays, dictionaries, etc.)
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
                            CopyDictionary((IDictionary)sourceValue, (IDictionary)destEnumerable, deepCopy);
                        }
                        else if (typeof(IList).IsAssignableFrom(fieldType))
                        {
                            CopyObjects((IEnumerable)sourceValue, (IList)destEnumerable, deepCopy);
                        }
                        else
                        {
                            // Handle other enumerables (e.g., HashSet<T>, Queue<T>)
                            CopyGenericEnumerable((IEnumerable)sourceValue, destEnumerable, fieldType, deepCopy, visited);
                        }

                        field.SetValue(destination, destEnumerable);
                    }
                    // Handle complex objects (non-value types, non-immutable, non-system)
                    else if (!fieldType.IsValueType && !IsImmutableType(fieldType) && !IsSystemInternalType(fieldType) && !IsCultureRelatedType(fieldType))
                    {
                        object destValue = ConverterHelper.InstantiateOne(fieldType);
                        var nestedVisited = new HashSet<object>(new ReferenceEqualityComparer());
                        destValue = DeepCopyObject(sourceValue, destValue, deepCopy, nestedVisited);
                        field.SetValue(destination, destValue);
                    }
                    // Handle value types, strings, immutable types, and system types
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
                object destItem = null;

                if (!itemType.IsValueType && !IsImmutableType(itemType) && !IsSystemInternalType(itemType) && !IsCultureRelatedType(itemType))
                {
                    if (!visited.Add(item))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Circular reference detected for item of type [{itemType.Name}].", item, GPALObjectType.None);
                        continue;
                    }

                    destItem = ConverterHelper.InstantiateOne(itemType);
                    var nestedVisited = new HashSet<object>(new ReferenceEqualityComparer());
                    destItem = DeepCopyObject(item, destItem, deepCopy, nestedVisited);
                }
                else
                {
                    destItem = item; // Shallow copy for value types, strings, immutable types, and system types
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
                return 0;
        }
        static bool IsPropertyEnumerable(PropertyInfo property)
        {
            // Check if the property type implements the IEnumerable interface
            return typeof(IEnumerable).IsAssignableFrom(property.PropertyType);
        }
        static bool IsFieldEnumerable(FieldInfo field)
        {
            return typeof(IEnumerable).IsAssignableFrom(field.FieldType);
        }
    }

}

