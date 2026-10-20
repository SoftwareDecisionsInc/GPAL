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

using DocumentFormat.OpenXml.Bibliography;
using MimeKit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace GenerallyPositive
{
    // written by grok
    /// <summary>
    /// Our generic handler to create any type using one method.
    /// Also this allows us to keep GPAL classes internal so users cannot instantiate them except via the GPAL factory
    /// new GPALFile() is not allowed, use GPALFile gpalFile = GPAL.File.ToGPALObject();
    /// </summary>
    public static class ActivatorHelper
    {
        /// <summary>Type-safe overload of <see cref="CreateInstance(Type, int)"/>.</summary>
        /// <typeparam name="T">The type to create.</typeparam>
        /// <param name="elementCount">Number of elements when T is an array or <see cref="List{T}"/>.</param>
        /// <returns>A new instance of <typeparamref name="T"/>.</returns>
        public static T CreateInstance<T>(int elementCount = 0)
        {
            return (T)CreateInstance(typeof(T), elementCount);
        }

        // Original method with object return type
        /*
        public static object CreateInstance(Type type, int elementCount = 0)
        {
            // ensure any interface passed in is converted to a real class with a public constructor
            type = ConverterHelper.GetConcreteType(type);
            Type factoryType = typeof(GPAL);

            // Handle array & list<T> types
            // Handle arrays and List<T> generically
            bool isArray = type.IsArray;
            bool isList = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

            if (isArray || isList)
            {
                Type elementType = isArray ? type.GetElementType() : type.GetGenericArguments()[0];
                if (elementType == null)
                {
                    string stackTrace = Environment.StackTrace;
                    Exception ex = new InvalidOperationException($"Cannot determine element type for type [{type.Name}].\n\nStack trace:\n{stackTrace}");
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{type.Name}]", null, Enums.GPALObjectType.None, ex);
                    return null;
                }

                object collectionInstance = isArray
                    ? Array.CreateInstance(elementType, elementCount)
                    : Activator.CreateInstance(type);

                IList list = isList ? (IList)collectionInstance : null;
                Array array = isArray ? (Array)collectionInstance : null;

                if (elementType.Name.StartsWith("GPAL", StringComparison.OrdinalIgnoreCase))
                {
                    string baseName = elementType.IsGenericType ? elementType.Name.Split('`')[0] : elementType.Name;
                    string propertyName = baseName.StartsWith("GPAL") ? baseName.Substring(4) : baseName;
                    var property = factoryType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);

                    if (property == null)
                    {
                        string stackTrace = Environment.StackTrace;
                        Exception ex = new InvalidOperationException($"Factory getter [GPAL.{propertyName}] not found for type [{elementType.Name}].\n\nStack trace:\n{stackTrace}");
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{elementType.Name}]", null, Enums.GPALObjectType.None, ex);
                        return null;
                    }

                    Type factoryReturnType = property.PropertyType;
                    if (!elementType.IsAssignableFrom(factoryReturnType) && !factoryReturnType.IsAssignableFrom(elementType))
                    {
                        string stackTrace = Environment.StackTrace;
                        Exception ex = new InvalidOperationException(
                            $"Factory getter [GPAL.{propertyName}] returns type [{factoryReturnType.Name}] which is not compatible with requested type [{elementType.Name}].\n\nStack trace:\n{stackTrace}"
                        );
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{elementType.Name}]", null, Enums.GPALObjectType.None, ex);
                        return null;
                    }

                    for (int i = 0; i < elementCount; i++)
                    {
                        object elementInstance = property.GetValue(null);
                        if (isArray)
                            array.SetValue(elementInstance, i);
                        else
                            list.Add(elementInstance);
                    }
                }

                return collectionInstance;
            }

            // Handle non-array, non-list types
            if (type.Name.StartsWith("GPAL", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = type.IsGenericType ? type.Name.Split('`')[0] : type.Name;
                string propertyName = baseName.StartsWith("GPAL") ? baseName.Substring(4) : baseName;
                var property = factoryType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
                if (property == null)
                {
                    string stackTrace = Environment.StackTrace;
                    Exception ex = new InvalidOperationException($"Factory getter [GPAL.{propertyName}] not found for type [{type.Name}].\n\nStack trace:\n{stackTrace}");
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{type.Name}]", null, Enums.GPALObjectType.None, ex);
                    return null;
                }

                Type factoryReturnType = property.PropertyType;
                if (!type.IsAssignableFrom(factoryReturnType) && !factoryReturnType.IsAssignableFrom(type))
                {
                    string stackTrace = Environment.StackTrace;
                    Exception ex = new InvalidOperationException(
                        $"Factory getter [GPAL.{propertyName}] returns type [{factoryReturnType.Name}] which is not compatible with requested type [{type.Name}].\n\nStack trace:\n{stackTrace}"
                    );
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{type.Name}]", null, Enums.GPALObjectType.None, ex);
                    return null;
                }

                return property.GetValue(null);
            }

            return Activator.CreateInstance(type);
        }
        */
        /// <summary>Creates an instance of <paramref name="type"/> via the GPAL factory, a parameterless constructor, or the constructor with the fewest parameters.</summary>
        /// <param name="type">The type to instantiate. Interfaces are resolved to their concrete type via <see cref="ConverterHelper.GetConcreteType"/>.</param>
        /// <param name="elementCount">Number of elements when <paramref name="type"/> is an array or <see cref="List{T}"/>.</param>
        /// <returns>The created instance, or null if instantiation fails.</returns>
        public static dynamic CreateInstance(Type type, int elementCount = 0)
        {
            // Ensure any interface passed in is converted to a real class with a public constructor
            type = ConverterHelper.GetConcreteType(type);
            Type factoryType = typeof(GPAL);

            bool isArray = type.IsArray;
            bool isList = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

            if (isArray || isList)
            {
                Type elementType = isArray ? type.GetElementType() : type.GetGenericArguments()[0];
                if (elementType == null)
                {
                    string stackTrace = Environment.StackTrace;
                    Exception ex = new InvalidOperationException($"Cannot determine element type for type [{type.Name}].\n\nStack trace:\n{stackTrace}");
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{type.Name}]", null, Enums.GPALObjectType.None, ex);
                    return null;
                }

                object collectionInstance = isArray
                    ? Array.CreateInstance(elementType, elementCount)
                    : Activator.CreateInstance(type);

                IList list = isList ? (IList)collectionInstance : null;
                Array array = isArray ? (Array)collectionInstance : null;

                if (elementType.Name.StartsWith("GPAL", StringComparison.OrdinalIgnoreCase))
                {
                    var property = GetGPALFactoryProperty(elementType);
                    if (property == null) return null;

                    for (int i = 0; i < elementCount; i++)
                    {
                        object elementInstance = property.GetValue(null);
                        if (isArray)
                            array.SetValue(elementInstance, i);
                        else
                            list.Add(elementInstance);
                    }
                }

                return collectionInstance;
            }

            // Handle single GPAL-based objects
            if (type.Name.StartsWith("GPAL", StringComparison.OrdinalIgnoreCase))
            {
                var property = GetGPALFactoryProperty(type);
                if (property == null) return null;

                return property.GetValue(null);
            }

            // Fallback to parameterless constructor
            // Try parameterless constructor first
            var paramlessCtor = type.GetConstructor(Type.EmptyTypes);
            if (paramlessCtor != null)
            {
                return paramlessCtor.Invoke(null);
            }

            // Find constructor with fewest parameters
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type); // Default value for value types (e.g., false for bool)
            }

            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (constructors.Length == 0)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"No constructors found for type [{type.FullName}]. Don't know what you want to do.");
                throw new InvalidOperationException($"No constructors found for type [{type.FullName}]");
            }

            var targetCtor = constructors.OrderBy(c => c.GetParameters().Length).First();
            var parameters = targetCtor.GetParameters();
            var args = new object[parameters.Length];

            // Provide default values for parameters
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (paramType == typeof(string))
                    args[i] = null;
                else if (paramType.IsValueType)
                    args[i] = Activator.CreateInstance(paramType);
                else if (paramType.IsGenericType && (paramType.GetGenericTypeDefinition() == typeof(Dictionary<,>) || paramType.GetGenericTypeDefinition() == typeof(List<>)))
                    args[i] = Activator.CreateInstance(paramType);
                else
                    args[i] = null;
            }

            return targetCtor.Invoke(args);
        }
        /// <summary>Resolves the static GPAL factory property that produces instances of <paramref name="targetType"/>.</summary>
        /// <param name="targetType">A GPAL-prefixed type whose factory property name is derived by stripping the "GPAL" prefix.</param>
        /// <returns>The matching <see cref="PropertyInfo"/>, or null if not found or type-incompatible.</returns>
        private static PropertyInfo GetGPALFactoryProperty(Type targetType)
        {
            if (targetType == null) return null;

            string baseName = targetType.IsGenericType
                ? targetType.Name.Split('`')[0]
                : targetType.Name;

            string propertyName = baseName.StartsWith("GPAL", StringComparison.OrdinalIgnoreCase)
                ? baseName.Substring(4)
                : baseName;

            Type factoryType = typeof(GPAL);
            var property = factoryType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (property == null)
            {
                string stackTrace = Environment.StackTrace;
                Exception ex = new InvalidOperationException(
                    $"Factory getter [GPAL.{propertyName}] not found for type [{targetType.Name}].\n\nStack trace:\n{stackTrace}"
                );
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{targetType.Name}]", null, Enums.GPALObjectType.None, ex);
                return null;
            }

            Type factoryReturnType = property.PropertyType;
            if (!targetType.IsAssignableFrom(factoryReturnType) && !factoryReturnType.IsAssignableFrom(targetType))
            {
                string stackTrace = Environment.StackTrace;
                Exception ex = new InvalidOperationException(
                    $"Factory getter [GPAL.{propertyName}] returns type [{factoryReturnType.Name}] which is not compatible with requested type [{targetType.Name}].\n\nStack trace:\n{stackTrace}"
                );
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to instantiate a [{targetType.Name}]", null, Enums.GPALObjectType.None, ex);
                return null;
            }

            return property;
        }
    }
}

