// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Spreadsheet;
using HtmlAgilityPack;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Windows.Forms;
using System.Xml;
using YamlDotNet.Core;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    partial class ConverterHelper
    {
        private static bool IsList(object obj)
        {
            return obj is IList && obj.GetType().IsGenericType;
        }
        private static bool IsArray(object obj)
        {
            return obj != null && obj.GetType().IsArray;
        }
        private static bool IsSupportedCollectionType(Type type)
        {
            return type.IsArray ||
            (type.IsGenericType && (
                type.GetGenericTypeDefinition() == typeof(List<>) ||
                type.GetGenericTypeDefinition() == typeof(IList<>) ||
                type.GetGenericTypeDefinition() == typeof(ICollection<>)
            ));
        }

        /// <summary>
        /// Determine (as best we can) if we are converting a struct
        /// NOTE: it would be much 'easier' for uses to always use classes over structs, but we should be able to deal with it
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal static bool IsCustomStructType(Type type)
        {
            try
            {
                if (type == null ||
                    !type.IsValueType ||
                    type.IsPrimitive ||
                    type.IsEnum)
                {
                    return false;
                }

                // Explicitly exclude known system structs (safety net)
                if (IsKnownSystemStruct(type))
                {
                    return false;
                }

                // Optional: also reject types from core runtime assemblies if namespace is empty or unexpected
                // (rare, but covers some internal structs)
                if (type.Assembly == typeof(object).Assembly) // System.Private.CoreLib
                {
                    return false;
                }

                if (true == IsRegisteredStruct(type))
                    return true;
            }
            catch
            { }

            return false;   // we do not know
        }

        internal static bool IsRegisteredStruct(Type type)
        {
            return type != null && ConverterSettings.RegisteredStructs.Contains(type);
        }

        private static bool IsKnownSystemStruct(Type type)
        {
            // Widely available pre-.NET 6 system structs
            if (type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) ||
                type == typeof(TimeSpan) ||
                type == typeof(TimeZoneInfo) ||
                type == typeof(Guid) ||
                type == typeof(BigInteger) ||
                type == typeof(Complex))
            {
                return true;
            }

            // Primary filter: reject anything in System.* or Microsoft.* namespaces
            string ns = type?.Namespace ?? string.Empty;
            if (ns.StartsWith("System") || ns.StartsWith("Microsoft"))
            {
                return false;
            }
            // Tuples
            if (ns == "System" && true == type?.Name.StartsWith("ValueTuple"))
                return true;

            // Nullable<T>
            if (true == type?.IsGenericType && type?.GetGenericTypeDefinition() == typeof(Nullable<>))
                return true;

            // System.Numerics (Vector<T>, Matrix, etc.)
            if (ns == "System.Numerics")
                return true;

            // Half (.NET 5+)
            if (type?.Name == "Half" && ns == "System")
                return true;

            return false;
        }

        internal static bool IsArrayType(Type type)
        {
            return (bool)type?.IsArray;
        }

        internal static bool IsClassType(Type type)
        {
            if (null == type)
                return false;

            return (bool)type?.IsClass && (bool)!type?.IsInterface;
        }
        internal static bool IsUriType(Type type)
        {
            return type == typeof(Uri);
        }
        internal static bool IsSimpleType(Type type)
        {
            if (null == type)
                return true;

            if (true == IsKeyValuePairType(type))
                return false; // KeyValuePair and DictionaryEntry are NOT simple - we need to recurse into them
            else
                return type.IsPrimitive ||
                       type.IsValueType ||          // CAVEAT: complex is a valuetype and should be checked
                       type == typeof(object) ||    // CAVEAT, this could be trouble - we use Dictionary<dynamic, dynamic> which are mostly strings, but we may do the simple type test after all others
                       type == typeof(string) ||
                       type == typeof(decimal) ||
                       type == typeof(Uri) ||
                       type == typeof(System.Net.IPAddress) ||
                       type == typeof(System.Version) ||
                       type == typeof(System.Numerics.BigInteger) ||
                       type == typeof(bool) ||
                       type == typeof(Boolean) ||
                       type == typeof(byte) ||
                       type == typeof(sbyte) ||
                       type == typeof(char) ||
                       type == typeof(short) ||
                       type == typeof(ushort) ||
                       type == typeof(int) ||
                       type == typeof(uint) ||
                       type == typeof(long) ||
                       type == typeof(ulong) ||
                       type == typeof(float) ||
                       type == typeof(double) ||
                       type == typeof(DateTime) ||
                       typeof(DateTimeOffset) == type ||
                       true == IsTextClassType(type)
                       ;
        }
        // whether each type has a converter to and from text, looked up once per type
        private static readonly ConcurrentDictionary<Type, bool> textConverterTypes = new ConcurrentDictionary<Type, bool>();
        /// <summary>
        /// Whether a type has a converter of its own to and from text, such as a System.Drawing.Point, "3, 4", a Font,
        /// "Arial, 9.75pt, style=Bold", or a CultureInfo, "de-DE". A converter that names components, as a Control's does,
        /// is not one.
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>True when the type is written as its converter's text and read back through it</returns>
        internal static bool HasTextConverter(Type type)
        {
            return null != type && textConverterTypes.GetOrAdd(type, convertedType =>
            {
                TypeConverter converter = TypeDescriptor.GetConverter(convertedType);
                return typeof(TypeConverter) != converter.GetType() && false == converter is ReferenceConverter
                    && true == converter.CanConvertFrom(typeof(string)) && true == converter.CanConvertTo(typeof(string));
            });
        }
        /// <summary>
        /// A class written as one value, the text of its converter, such as a Font or a CultureInfo, and read back
        /// through that converter, where its members, such as a CultureInfo's Parent, do not make it again.
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>True for a class that has a converter to and from text, other than text, a Uri, an IPAddress or a
        /// Version, which keep their own conversion</returns>
        internal static bool IsTextClassType(Type type)
        {
            return null != type && true == type.IsClass && typeof(string) != type && typeof(Uri) != type
                && typeof(System.Net.IPAddress) != type && typeof(System.Version) != type && true == HasTextConverter(type);
        }
        /// <summary>
        /// A class of the framework that is not a list, a dictionary, a pair, a tuple or a value GPAL writes, such as a
        /// FileInfo, a Type or an Exception: written as its text, where its members lead on without end, as a
        /// DirectoryInfo's Root does, or across the framework, as a Type's do.
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>True for a class of the System or Microsoft namespaces that GPAL writes as its text</returns>
        internal static bool IsFrameworkObjectType(Type type)
        {
            string space = type?.Namespace ?? string.Empty;
            bool frameworkType = "System" == space || true == space.StartsWith("System.", StringComparison.Ordinal)
                || "Microsoft" == space || true == space.StartsWith("Microsoft.", StringComparison.Ordinal);

            return null != type && true == frameworkType && true == type.IsClass && false == IsSimpleType(type)
                && false == IsEnumerableType(type) && false == IsKeyValuePairType(type) && false == IsTupleType(type);
        }
        /// <summary>
        /// A struct of the framework written as one value, the text of its converter, such as a System.Drawing.Point,
        /// "3, 4", or a Size, "7, 8", and read back through that converter, where its members, such as a Point's
        /// IsEmpty, do not make it again. A number, a date or another value that formats itself keeps its own conversion.
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>True for a struct of the framework that has a converter to and from text and does not format itself</returns>
        internal static bool IsTextStructType(Type type)
        {
            string space = type?.Namespace ?? string.Empty;
            return null != type && true == type.IsValueType && false == type.IsEnum && false == type.IsPrimitive && null == Nullable.GetUnderlyingType(type)
                && ("System" == space || true == space.StartsWith("System.", StringComparison.Ordinal))
                && false == typeof(IFormattable).IsAssignableFrom(type) && false == IsValueAlone(type)
                && false == IsKeyValuePairType(type) && false == IsTupleType(type) && true == HasTextConverter(type);
        }
        internal static bool IsValueType(Type type)
        {
            return type.IsValueType && !type.IsEnum && !type.IsClass && !type.IsInterface;
        }
        // Column names are positional headers for a flat/delimited row with no named keys - they don't
        // apply to YAML/JSON/XML, which already carry real key names.
        internal static bool IsDelimitedDataFormat(DataFormat format)
        {
            return format == DataFormat.CARET
                || format == DataFormat.COLON
                || format == DataFormat.CSV
                || format == DataFormat.CUSTOM_DELIMITER
                || format == DataFormat.DOT
                || format == DataFormat.HYPHEN
                || format == DataFormat.PIPE
                || format == DataFormat.PRN
                || format == DataFormat.SEMICOLON
                || format == DataFormat.SPACE
                || format == DataFormat.TAB;
        }
        internal static bool IsNullableType(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }
        private static bool IsKeyValuePairType(Type type)
        {
            // A null value has no type. Checked first, as casting the null that type?.IsGenericType gives to bool
            // throws, and under a debugger a throw per null value in the data costs seconds.
            return null != type && ((type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) || type == typeof(DictionaryEntry));
        }
        internal static bool IsDictionaryType(Type type)
        {
            if (null == type)
                return false;

            return typeof(IDictionary).IsAssignableFrom(type);
        }
        public static bool IsBigIntegerType(Type type)
        {
            return typeof(BigInteger) == type || typeof(BigInteger?) == type;
        }

        // Converts a value to BigInteger; handles BigInteger, string, and any numeric type (int, long, etc.)
        // Throws if conversion is not possible.
        private static BigInteger AsBigInteger(object value)
        {
            if (value is BigInteger bi) return bi;
            if (value is string s) return BigInteger.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return new BigInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
        public static bool IsByteType(Type type)
        {
            return typeof(Byte) == type || typeof(Byte?) == type;
        }
        public static bool IsBooleanType(Type type)
        {
            return typeof(Boolean) == type || typeof(Boolean?) == type;
        }
        public static bool IsDateTimeType(Type type)
        {
            return typeof(DateTime) == type || typeof(DateTime?) == type;
        }
        public static bool IsDateTimeOffsetType(Type type)
        {
            return typeof(DateTimeOffset) == type || typeof(DateTimeOffset?) == type;
        }
        public static bool IsIpAddressType(Type type)
        {
            return typeof(IPAddress) == type;
        }
        public static bool IsTimeSpanType(Type type)
        {
            return typeof(TimeSpan) == type || typeof(TimeSpan?) == type;
        }
        private static bool IsTupleType(Type type)
        {
            if (type == null || !type.IsGenericType) return false;
            var openType = type.GetGenericTypeDefinition();

            // ValueTuples are structs; Tuples are classes. 
            // Both are often unwanted when looking for "standard" structs.
            return openType.Namespace == "System" &&
                   (openType.Name.StartsWith("ValueTuple`") || openType.Name.StartsWith("Tuple`"));
        }

        public static bool IsGuidType(Type type)
        {
            return typeof(Guid) == type || typeof(Guid?) == type;
        }
        public static bool IsVersionType(Type type)
        {
            return typeof(System.Version) == type;
        }
        public static bool IsEnumerableType(Type type)
        {
            if (null == type)
                return false;

            return (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string));
        }
        /// <summary>
        /// True when the type has a public property or field of that name, whatever its case. Two members whose names
        /// differ only by case do not throw, as a lookup for one of them does.
        /// </summary>
        /// <param name="type">The type to look in</param>
        /// <param name="name">The member name</param>
        /// <returns>True when the type has the member</returns>
        internal static bool HasMember(Type type, string name)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>
        /// The key and value types of a dictionary type, from the IDictionary&lt;TKey, TValue&gt; it is, a subclass of one
        /// included; object and object for a Hashtable, which has none.
        /// </summary>
        /// <param name="dictionaryType">The dictionary type</param>
        /// <returns>The key type and the value type</returns>
        internal static Type[] DictionaryTypeArguments(Type dictionaryType)
        {
            Type genericDictionary = dictionaryType.GetInterfaces().Concat(new[] { dictionaryType })
                .FirstOrDefault(type => true == type.IsGenericType && typeof(IDictionary<,>) == type.GetGenericTypeDefinition());

            return null != genericDictionary ? genericDictionary.GetGenericArguments() : new[] { typeof(object), typeof(object) };
        }
        /// <summary>
        /// The generic collection a class derives from, such as List&lt;Item&gt; for Items : List&lt;Item&gt;: the
        /// nearest base that lists one item type, can be made empty, and has Clear and an Add, Enqueue, Push or AddLast
        /// of its item. An abstract base between them is passed over. Null for a generic type, a dictionary, or a class
        /// that derives from no such collection.
        /// </summary>
        /// <param name="type">The class</param>
        /// <returns>The generic collection it derives from, or null</returns>
        internal static Type GenericCollectionBase(Type type)
        {
            Type collectionBase = true == type.IsGenericType || true == IsDictionaryType(type) ? null : type.BaseType;
            while (null != collectionBase && false == IsFillableCollection(collectionBase))
                collectionBase = collectionBase.BaseType;

            return collectionBase;
        }
        /// <summary>
        /// Whether a type is a generic collection the converter can make empty and fill: one item type, not abstract, a
        /// constructor that takes nothing, Clear, and an Add, Enqueue, Push or AddLast of its item.
        /// </summary>
        /// <param name="type">The type</param>
        /// <returns>True when it can be made and filled</returns>
        private static bool IsFillableCollection(Type type)
        {
            return true == type.IsGenericType && 1 == type.GetGenericArguments().Length
                && true == typeof(IEnumerable<>).MakeGenericType(type.GetGenericArguments()[0]).IsAssignableFrom(type)
                && false == type.IsAbstract && null != type.GetConstructor(Type.EmptyTypes)
                && null != type.GetMethod("Clear", Type.EmptyTypes) && null != CollectionAddMethod(type);
        }
        /// <summary>
        /// The method a generic collection takes one item through: Add, Enqueue, Push, or AddLast for a LinkedList; null
        /// when it has none.
        /// </summary>
        /// <param name="collectionType">A generic collection of one item type</param>
        /// <returns>The method, or null</returns>
        internal static MethodInfo CollectionAddMethod(Type collectionType)
        {
            Type itemType = CollectionItemType(collectionType);

            return collectionType.GetMethod("Add", new[] { itemType })
                ?? collectionType.GetMethod("Enqueue", new[] { itemType })
                ?? collectionType.GetMethod("Push", new[] { itemType })
                ?? collectionType.GetMethod("AddLast", new[] { itemType });
        }
        /// <summary>
        /// The item type of a collection type: an array's element, a generic type's first type argument, or for a
        /// subclass that has none, such as Items : List&lt;Item&gt;, the T of the IEnumerable&lt;T&gt; it is; object
        /// when it names none. A generic type of several type arguments that is not a dictionary, such as
        /// Foo&lt;A, B&gt; : List&lt;B&gt;, takes the T of the one IEnumerable&lt;T&gt; it is, B; one that is the
        /// enumerable of pairs, as an IDictionary&lt;K, V&gt; is, takes its first type argument, as a dictionary does.
        /// </summary>
        /// <param name="collectionType">The collection type</param>
        /// <returns>The item type</returns>
        internal static Type CollectionItemType(Type collectionType)
        {
            Type itemType = typeof(object);
            // the IEnumerable<T> of other than pairs a generic type of several type arguments is, when it is one alone
            Type[] enumerablesOfSeveral = true == collectionType.IsGenericType && 1 < collectionType.GetGenericArguments().Length && false == IsDictionaryType(collectionType)
                ? collectionType.GetInterfaces().Where(each => true == each.IsGenericType && typeof(IEnumerable<>) == each.GetGenericTypeDefinition()
                    && false == IsKeyValuePairType(each.GetGenericArguments()[0])).ToArray()
                : new Type[0];

            if (true == collectionType.IsArray)
                itemType = collectionType.GetElementType();
            else if (1 == enumerablesOfSeveral.Length)
                itemType = enumerablesOfSeveral[0].GetGenericArguments()[0];
            else if (true == collectionType.IsGenericType)
                itemType = collectionType.GetGenericArguments()[0];
            else
            {
                // the first IEnumerable<T> it lists; a class that is the enumerable of two item types gets one of them
                Type enumerable = collectionType.GetInterfaces()
                    .FirstOrDefault(each => true == each.IsGenericType && typeof(IEnumerable<>) == each.GetGenericTypeDefinition());

                if (null != enumerable)
                    itemType = enumerable.GetGenericArguments()[0];
            }

            return itemType;
        }
        /// <summary>
        /// The item type of a read-only collection, such as a ReadOnlyCollection, which cannot be filled by adding to it.
        /// Null for any other collection, an array or a dictionary.
        /// </summary>
        /// <param name="collection">The collection being filled</param>
        /// <returns>The item type, or null when the collection is not read-only</returns>
        internal static Type ReadOnlyItemType(object collection)
        {
            Type type = collection.GetType();
            Type genericCollection = false == type.IsArray && false == IsDictionaryType(type)
                ? type.GetInterfaces().FirstOrDefault(each => true == each.IsGenericType && typeof(ICollection<>) == each.GetGenericTypeDefinition())
                : null;

            return null != genericCollection && true == (bool)genericCollection.GetProperty("IsReadOnly").GetValue(collection)
                ? genericCollection.GetGenericArguments()[0]
                : null;
        }
        /// <summary>
        /// The constructor a collection type that cannot be added to, such as ReadOnlyCollection&lt;Item&gt;, is made
        /// with from its items. Null for an array, a dictionary, a collection with an Add, Enqueue, Push or AddLast of
        /// its item, or one with no such constructor.
        /// </summary>
        /// <param name="collectionType">The collection type</param>
        /// <returns>The constructor, or null</returns>
        internal static ConstructorInfo ReadOnlyItemsConstructor(Type collectionType)
        {
            return false == collectionType.IsArray && false == IsDictionaryType(collectionType) && null == CollectionAddMethod(collectionType)
                ? ItemsConstructor(collectionType, CollectionItemType(collectionType))
                : null;
        }
        /// <summary>
        /// The constructor a read-only collection is made with from its items: one whose single parameter a List of
        /// them can be passed to, as ReadOnlyCollection&lt;Item&gt;(IList&lt;Item&gt;) does, else one whose single
        /// parameter is a collection made from such a List, as ReadOnlyObservableCollection&lt;Item&gt;
        /// (ObservableCollection&lt;Item&gt;) does. Null when it has neither. MakeFromItems calls it.
        /// </summary>
        /// <param name="type">The read-only collection type</param>
        /// <param name="itemType">Its item type</param>
        /// <returns>The constructor, or null</returns>
        internal static ConstructorInfo ItemsConstructor(Type type, Type itemType)
        {
            Type itemList = typeof(List<>).MakeGenericType(itemType);
            ConstructorInfo[] oneParameter = type.GetConstructors().Where(each => 1 == each.GetParameters().Length).ToArray();

            return oneParameter.FirstOrDefault(each => true == each.GetParameters()[0].ParameterType.IsAssignableFrom(itemList))
                ?? oneParameter.FirstOrDefault(each => type != each.GetParameters()[0].ParameterType
                    && false == each.GetParameters()[0].ParameterType.IsAbstract
                    && true == each.GetParameters()[0].ParameterType.GetConstructors()
                        .Any(inner => 1 == inner.GetParameters().Length && true == inner.GetParameters()[0].ParameterType.IsAssignableFrom(itemList)));
        }
        /// <summary>
        /// Makes a read-only collection from a List of its items through the constructor ItemsConstructor found: the
        /// List is passed as it is, or first made into the collection the constructor takes, such as an
        /// ObservableCollection.
        /// </summary>
        /// <param name="makeWith">The constructor ItemsConstructor found</param>
        /// <param name="items">A List of the items</param>
        /// <returns>The collection made</returns>
        internal static object MakeFromItems(ConstructorInfo makeWith, object items)
        {
            Type parameterType = makeWith.GetParameters()[0].ParameterType;
            object argument = true == parameterType.IsInstanceOfType(items) ? items : Activator.CreateInstance(parameterType, items);

            return makeWith.Invoke(new[] { argument });
        }

        public static bool IsEnumerableObject(dynamic dynamicObject)
        {
            // Compared as object: a dynamic struct has no == with null, and catching that throw cost seconds under a
            // debugger
            if (null == (object)dynamicObject)
                return false;

            return (typeof(IEnumerable).IsAssignableFrom(dynamicObject?.GetType()) || dynamicObject?.GetType().IsArray) && typeof(string) != dynamicObject?.GetType();
        }
        private static bool IsEnumType(Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsEnum;
        }
        internal static bool IsComplexType(Type type)
        {
            return type == typeof(System.Numerics.Complex);
        }
        private static bool IsCompatibleType(Type targetType, Type valueType)
        {
            if (valueType == null || targetType == null) return false;

            if (targetType.IsAssignableFrom(valueType)) return true;

            if (IsSimpleType(targetType) && IsSimpleType(valueType)) return true;

            if (IsNullableType(targetType))
            {
                var underlyingType = Nullable.GetUnderlyingType(targetType);
                return IsSimpleType(underlyingType) && IsSimpleType(valueType);
            }

            if (IsComplexType(targetType) && valueType == typeof(string)) return true;

            if (IsDictionaryType(targetType) && typeof(IDictionary).IsAssignableFrom(valueType)) return true;

            if (IsEnumerableType(targetType) && typeof(IEnumerable).IsAssignableFrom(valueType)) return true;

            if (IsClassType(targetType) && typeof(IDictionary).IsAssignableFrom(valueType)) return true;

            return false;
        }
        internal static DataFormat GetDataFormatFromExtension(string filename)
        {
            switch (Path.GetExtension(filename).ToLowerInvariant().Replace(".", ""))
            {
                case "csv":
                    return DataFormat.CSV;
                case "tsv":
                    return DataFormat.TAB;
                case "json":
                case "jsn":
                    return DataFormat.JSON;
                // lines of text with nothing to split on
                case "txt":
                case "log":
                    return DataFormat.LOG;
                //// post-v1: PDF
                //case "pdf":
                //    return DataFormat.PDF;
                case "prn":
                    return DataFormat.PRN;
                case "xls":
                case "xlsx":
                    return DataFormat.XLSX;
                case "xml":
                    return DataFormat.XML;
                case "yml":
                case "yaml":
                    return DataFormat.YAML;
                case "htm":
                case "html":
                    return DataFormat.HTML;
                default:
                    return DataFormat.NOTSET;
            }
        }

        // Check if dict has sequential GPALKEY# keys like an array
        internal static bool IsArrayLike(dynamic dictListOrObject)
        {
            Type parmType = dictListOrObject.GetType();

            if (IsDictionaryType(parmType) || IsEnumerableType(parmType))
            {
                if (dictListOrObject == null || dictListOrObject.Count == 0) return false;

                foreach (dynamic entry in dictListOrObject)
                {
                    try
                    {
                        string keyStr = entry.Key.ToString();
                        if (!keyStr.StartsWith("GPALKEY")) return false; // All keys must be surrogate
                        if (int.TryParse(keyStr.Substring(7), out int idx))
                            break;
                        else
                            return false;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            else
                return false;

            return true;
        }

        private static bool IsAnonymousType(Type type)
        {
            return Attribute.IsDefined(type, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) &&
                   type.IsGenericType &&
                   type.Name.Contains("Anonymous") &&
                   (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }
    }
}
