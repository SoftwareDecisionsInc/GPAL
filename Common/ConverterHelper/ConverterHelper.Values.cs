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

using DocumentFormat.OpenXml.Drawing.Diagrams;
using DocumentFormat.OpenXml.Spreadsheet;
using HtmlAgilityPack;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections;
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
        // How many levels deep json, yaml, xml, html and classes are read and written, as an overflow of the stack ends
        // the process and cannot be caught. Deeper json and yaml are not read and deeper yaml is not written, with an error; html
        // and xml are cut at HtmlDepthLimit elements, with a warning; a class is written as its type name at ClassDepthLimit.
        // Measured on .NET 10 Release, the tightest route, xml written to html, overflows at about 160 levels, and json,
        // yaml and class input at about 200 to 250; 100 leaves the rest of the stack to the program calling GPAL
        internal const int NestingLimit = 100;
        // How many classes and structs, and the lists and dictionaries between them, deep a class or struct is written as
        // its members; one deeper is written as its type name. Each class takes far more of the stack than a level of read data: a .NET 10
        // Debug build overflows at about 55 classes deep, so 32 leaves the rest of the stack to the program calling GPAL
        internal const int ClassDepthLimit = 32;

        // A List converted to a dictionary synthesizes GPALKEY-prefixed sequential keys internally to
        // stand in for list position - that's an internal sentinel, not a value meant to reach the final
        // key. Strip it and hand the plain index to ConvertValue so the target key type decides its own
        // representation (a genuine int for an int/object key, an unpadded "0"/"1"/... for a string key).
        internal static object ResolveSimpleDictionaryKey(dynamic rawKey, Type keyType)
        {
            string rawKeyStr = rawKey as string;
            if (rawKeyStr != null && rawKeyStr.StartsWith("GPALKEY", StringComparison.Ordinal) && int.TryParse(rawKeyStr.Substring(7), out int gpalKeyIdx))
                return ConvertValue(gpalKeyIdx, keyType);

            // xml writes "_" before an element name that does not start with a letter, so a number key 1 is <_1>; a key
            // that is not text goes back without it
            Type plainKeyType = Nullable.GetUnderlyingType(keyType) ?? keyType;
            if (typeof(string) != plainKeyType && typeof(object) != plainKeyType && null != rawKeyStr && 1 < rawKeyStr.Length
                && '_' == rawKeyStr[0] && false == char.IsLetter(rawKeyStr[1]) && '_' != rawKeyStr[1])
                rawKey = rawKeyStr.Substring(1);

            if (rawKey != null && keyType.IsAssignableFrom(((object)rawKey).GetType()))
                return (object)rawKey;

            return ConvertValue(rawKey?.ToString(), keyType);
        }
       
        /// <summary>
        /// Data read from a format that holds only text, a csv, xlsx, xml or html file or a grid, with each text null made a
        /// null. Those formats write a null as the text null, so json and yaml written from them write a null there and not
        /// the text "null". The data is copied, not changed.
        /// </summary>
        /// <param name="value">The data, its dictionaries and lists walked to the values they hold</param>
        /// <returns>A copy with the text nulls as nulls</returns>
        internal static object TextNullsAsNulls(object value)
        {
            object retVal = value;
            if (value is string text && "null" == text)
                retVal = null;
            else if (value is IDictionary entries)
            {
                Dictionary<object, object> copied = new Dictionary<object, object>();
                foreach (DictionaryEntry entry in entries)
                    copied[entry.Key] = TextNullsAsNulls(entry.Value);
                retVal = copied;
            }
            else if (value is IList items)
                retVal = items.Cast<object>().Select(item => TextNullsAsNulls(item)).ToList();
            return retVal;
        }

        /// <summary>
        /// A value going into a place typed object, such as an object member or a Dictionary&lt;string, object&gt; value, as
        /// an object holds it: each decimal json and yaml read to keep its digits, such as 1.50, is the double it was read
        /// as before, so code that reads it as a double still does, and a list the converter holds under keys GPALKEY0000,
        /// GPALKEY0001 and so on, as a class's int[] is held, is a list again, in the records and lists it holds too. A record
        /// or list holding one is copied, not changed, so the data read stays as it was for the next save.
        /// </summary>
        /// <param name="value">The value, its dictionaries and lists walked to the values they hold</param>
        /// <returns>The value, or a copy as an object holds it</returns>
        internal static object ObjectSlotValue(object value)
        {
            // only json and yaml read a number as a decimal to keep its digits; the other inputs keep what they give
            bool fromJsonOrYaml = 0 < ConverterSettings.InDataFormat.Count
                && (DataFormat.JSON == ConverterSettings.InDataFormat[0] || DataFormat.YAML == ConverterSettings.InDataFormat[0]);
            HashSet<object> walked = new HashSet<object>(ReferenceEqualityComparer.Instance);
            // a list held under keys GPALKEY0000, GPALKEY0001 and so on
            bool IsNumberedList(object held) => held is IDictionary numbered && 0 < numbered.Count
                && true == numbered.Keys.Cast<object>().All(key => true == Regex.IsMatch(key?.ToString() ?? string.Empty, @"^GPALKEY[0-9]+$"));
            // true when the value holds such a decimal or list anywhere, each record or list looked at once
            bool NeedsChange(object held) => (true == fromJsonOrYaml && held is decimal) || true == IsNumberedList(held)
                || (held is IDictionary heldEntries && true == walked.Add(held) && true == heldEntries.Values.Cast<object>().Any(entry => true == NeedsChange(entry)))
                || (held is IList heldItems && false == held is string && true == walked.Add(held) && true == heldItems.Cast<object>().Any(item => true == NeedsChange(item)));
            object retVal = value;
            if (true == fromJsonOrYaml && value is decimal number)
                retVal = double.Parse(number.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            else if (true == IsNumberedList(value))
                // in the order of the numbers after GPALKEY, so GPALKEY10000 comes after GPALKEY9999
                retVal = ((IDictionary)value).Keys.Cast<object>().OrderBy(key => System.Numerics.BigInteger.Parse(key.ToString().Substring(7), CultureInfo.InvariantCulture))
                    .Select(key => ObjectSlotValue(((IDictionary)value)[key])).ToList();
            else if (value is IDictionary entries && true == NeedsChange(value))
            {
                Dictionary<object, object> copied = new Dictionary<object, object>();
                foreach (DictionaryEntry entry in entries)
                    copied[entry.Key] = ObjectSlotValue(entry.Value);
                retVal = copied;
            }
            else if (value is IList items && false == value is Array && true == NeedsChange(value))
                retVal = items.Cast<object>().Select(item => ObjectSlotValue(item)).ToList();
            return retVal;
        }

        /// <summary>
        /// A double as every format writes it: the shortest text that reads back as the same number, such as 0.1, 3 or
        /// 1.7976931348623157E+308, -0 for a negative zero, and NaN, Infinity or -Infinity. .NET Framework's R format
        /// misses a few numbers and drops the sign of zero, so its text is read back and G17 is taken when it differs.
        /// </summary>
        /// <param name="number">The double</param>
        /// <returns>The text</returns>
        internal static string NumberText(double number)
        {
            string retVal;
            if (true == double.IsNaN(number))
                retVal = "NaN";
            else if (true == double.IsPositiveInfinity(number))
                retVal = "Infinity";
            else if (true == double.IsNegativeInfinity(number))
                retVal = "-Infinity";
            else if (0 == number && 0 > BitConverter.DoubleToInt64Bits(number))
                retVal = "-0";
            else
            {
                retVal = number.ToString("R", CultureInfo.InvariantCulture);
                if (BitConverter.DoubleToInt64Bits(number) != BitConverter.DoubleToInt64Bits(double.Parse(retVal, NumberStyles.Float, CultureInfo.InvariantCulture)))
                    retVal = number.ToString("G17", CultureInfo.InvariantCulture);
            }
            return retVal;
        }

        /// <summary>
        /// A float as every format writes it: the shortest text that reads back as the same number, -0 for a negative
        /// zero, and NaN, Infinity or -Infinity, with G9 where .NET Framework's R format does not read back.
        /// </summary>
        /// <param name="number">The float</param>
        /// <returns>The text</returns>
        internal static string NumberText(float number)
        {
            string retVal;
            if (true == float.IsNaN(number))
                retVal = "NaN";
            else if (true == float.IsPositiveInfinity(number))
                retVal = "Infinity";
            else if (true == float.IsNegativeInfinity(number))
                retVal = "-Infinity";
            else if (0 == number && 0 > BitConverter.DoubleToInt64Bits(number))
                retVal = "-0";
            else
            {
                retVal = number.ToString("R", CultureInfo.InvariantCulture);
                if (number != float.Parse(retVal, NumberStyles.Float, CultureInfo.InvariantCulture))
                    retVal = number.ToString("G9", CultureInfo.InvariantCulture);
            }
            return retVal;
        }

        // Helper to format known complex types nicely
        private static string ValueToString(dynamic value, Type type = null)
        {
            if (null == type)
                type = value?.GetType();

            // KeyValuePair - return only the Value, read by reflection as dynamic cannot see it when the pair's types are
            // not public. A pair being read in arrives as a dictionary of its Key and Value, with the pair's type as the
            // type, and is written as that dictionary
            if (IsKeyValuePairType(type))
                return IsKeyValuePairType(((object)value)?.GetType()) ? ValueToString(((object)value).GetType().GetProperty("Value").GetValue((object)value)) : ValueToString(value);

            // Compared as object: a dynamic struct (a tuple, a Complex) has no == with null, and the throw that made,
            // caught here, cost seconds under a debugger
            if (false == IsCustomStructType(type) && (null == (object)value || value is char c && c == '\0'))
                return "null";

            // Already a string (e.g. round-tripped from YAML/JSON, where Complex/Tuple values
            // are stored in their string-formatted form) - nothing further to format.
            if (value is string alreadyString)
                return alreadyString;

            // XML may leave a scalar property's value in a List<object> or dict wrapper that
            // StripGpalKeys didn't fully collapse.  Extract the inner string safely so the
            // type-specific ToString calls below receive a real value, not a container object.
            if (value is IList scalarList && type != null && !IsEnumerableType(type) && !type.IsArray)
            {
                object first = scalarList.Count > 0 ? scalarList[0] : null;
                if (first == null) return "null";
                if (first is string fs) return fs;
                // Inner item is a dict — extract first string value (handles {value:"..."} etc.)
                if (first is IDictionary fd)
                {
                    foreach (DictionaryEntry de in fd)
                        if (de.Value is string dvs) return dvs;
                    return "null";
                }
                // Inner item is another list — recurse once more
                if (first is IList) return ValueToString(first, type);
                return first.ToString();
            }

            if (IsTupleType(type))
            {
                var fields = type.GetFields();
                var parts = new List<string>();
                foreach (var field in fields)
                {
                    var fieldValue = field.GetValue(value);
                    parts.Add(ValueToString(fieldValue)); // recursive — handles nested tuples, BigInteger, etc.
                }
                return $"({string.Join(", ", parts)})";
            }

            // Complex – clean format
            if (IsComplexType(type))
            {
                double im = value.Imaginary;
                double re = value.Real;
                // the imaginary part keeps its sign, a negative zero included
                string sign = 0 > BitConverter.DoubleToInt64Bits(im) ? "-" : "+";
                return $"{NumberText(re)}{sign}{NumberText(Math.Abs(im))}i";
            }

            // Enum (including flags) – use name if possible, fall back to numeric
            if (IsEnumType(type))
            {
                // IsEnumType is also true for a nullable enum, but Enum.GetName(typeof(Priority?), 2) throws. Use the
                // underlying enum type.
                Type enumType = Nullable.GetUnderlyingType(type) ?? type;
                object realValue = value;

                // If incoming value is a string, try to parse it to the target enum
                if (value is string strValue)
                {
                    // Use reflection to call generic Enum.TryParse<T>(string, bool, out T)
                    var tryParseMethod = typeof(Enum).GetMethod(nameof(Enum.TryParse), new[]
                    {
                        typeof(string),
                        typeof(bool),
                        enumType.MakeByRefType()   // out TEnum
                    });

                    if (tryParseMethod != null)
                    {
                        var parameters = new object[] { strValue, true, null };
                        bool success = (bool)tryParseMethod.Invoke(null, parameters);

                        if (success)
                        {
                            realValue = parameters[2];  // the out parameter
                        }
                        else
                        {
                            // String did not match > return as-is
                            return strValue;
                        }
                    }
                    else
                    {
                        dynamic enumObject = InstantiateOne(enumType);

                        return enumObject.ToString();
                        // Very old runtime – fallback to case-sensitive non-generic
                        //if (Enum.TryParse(strValue, out enumObject))
                        //{
                        //    realValue = enumObject;
                        //}
                        //else
                        //{
                        //    return strValue;
                        //}
                    }
                }

                long numeric = Convert.ToInt64(realValue);

                // A value of 0 is written by its name too, such as None or the first member; with no name it is written as 0 below.
                // Enum.ToObject because JSON returns an Int64 and GetName needs the underlying type (Int32 here).
                string name = Enum.GetName(enumType, Enum.ToObject(enumType, numeric));
                if (name != null)
                    return name;

                // Handle flags enum if decorated with FlagsAttribute
                if (enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
                {
                    var names = Enum.GetValues(enumType)
                        .Cast<object>()
                        .Where(v => Convert.ToInt64(v) != 0 && (numeric & Convert.ToInt64(v)) == Convert.ToInt64(v))
                        .Select(v => Enum.GetName(enumType, v))
                        .Where(n => n != null);

                    if (names.Any())
                        return string.Join(", ", names);
                }

                // Fallback: show the numeric value
                return numeric.ToString(CultureInfo.InvariantCulture);
            }

            // A DateTime or DateTimeOffset value - ISO 8601 round-trip form, such as 2026-11-07T21:52:48.7632579Z, which keeps
            // the fractional seconds, the Z or the offset, and reads the same on every culture
            if (value is DateTime || value is DateTimeOffset)
            {
                return value.ToString("o", CultureInfo.InvariantCulture);
            }

            // Guid – standard string
            if (IsGuidType(type))
            {
                return value.ToString();
            }

            // Uri - the text it was made from, as json writes it, so https://example.com stays without a / added
            if (IsUriType(type))
            {
                return value is Uri uri ? uri.OriginalString : value.ToString();
            }

            // IPAddress – standard string
            if (IsIpAddressType(type))
            {
                return value.ToString();
            }

            // TimeSpan – standard round-trippable format
            if (IsTimeSpanType(type))
            {
                return value.ToString("c"); // "01:02:03" or "-00:00:01"
            }

            // Version – standard string
            if (IsVersionType(type))
            {
                return value.ToString();
            }

            // BigInteger – invariant culture
            if (IsBigIntegerType(type))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            // A double or a float - the shortest text that reads back as the same number, on .NET Framework too
            if (value is double || value is float)
            {
                return value is double doubleValue ? NumberText(doubleValue) : NumberText((float)value);
            }

            if (typeof(WaitTime) == type)
            {
                WaitTime wt;
                if (true == TryConvertToWaitTime(value, out wt))
                    return wt.ToString();

                wt = CreateObjectFromDictionary(value, type, null, null);
                return wt.ToString();
            }

            // A type that does not format itself and has a converter to and from text, such as a System.Drawing Point, is
            // written as its converter writes it, "3, 4", as json writes it, so it reads back. Its ToString, {X=3,Y=4},
            // does not. A bool, a char or a string, written below as itself, needs no converter; HasTextConverter keeps
            // the answer for each type, so the converter is looked up only for a type it writes
            bool converterWrites = false == typeof(IFormattable).IsAssignableFrom(type) && typeof(bool) != type && typeof(char) != type
                && typeof(string) != type && true == HasTextConverter(type);
            TypeConverter textConverter = true == converterWrites ? TypeDescriptor.GetConverter(type) : null;

                // Simple built-in types (int, bool, char, decimal, etc.) – invariant culture
            // a bool value is written true or false, as JSON and YAML write it
            if (IsSimpleType(type))
            {
                return value is bool flag ? (true == flag ? "true" : "false") : true == converterWrites ? textConverter.ConvertToInvariantString((object)value) : Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            var toString = GetToString(type);
            // Fallback - how'd we get here? a struct? some custome data structure we don't know about
            return true == converterWrites ? textConverter.ConvertToInvariantString((object)value) : (string)toString.Invoke(value, null);
        }
        private static MethodInfo GetToString(Type t)
        {
            if (!ToStringCache.TryGetValue(t, out var method))
            {
                method = t.GetMethod(
                    "ToString",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);

                ToStringCache[t] = method;
            }

            return method;
        }
        /// <summary>
        /// Returns the array of arrays an array of two or more dimensions is read as, int[][] for int[,].
        /// </summary>
        /// <param name="arrayType">An array type of two or more dimensions, such as int[,]</param>
        /// <returns>The same element type as arrays nested one per dimension</returns>
        internal static Type RowsTypeOf(Type arrayType)
        {
            Type rowsType = arrayType.GetElementType();
            for (int dimension = 0; dimension < arrayType.GetArrayRank(); dimension++)
                rowsType = rowsType.MakeArrayType();

            return rowsType;
        }

        /// <summary>
        /// Lays rows of rows, as [[1, 2], [3, 4]], into an array of two or more dimensions, such as int[,].
        /// </summary>
        /// <remarks>
        /// Each value keeps its place: one that is null where the element type holds no null, or that does not convert,
        /// leaves the element type's default there, with a warning. Rows of different lengths take the length of the
        /// longest, with a warning; a place no row fills keeps the default too.
        /// </remarks>
        /// <param name="rows">The rows as read, lists or arrays nested one per dimension, or records holding them</param>
        /// <param name="arrayType">The array type to make, such as int[,]</param>
        /// <param name="propertyName">The member the rows are for, named in a warning; null when there is none</param>
        /// <returns>The array, empty when there are no rows</returns>
        internal static Array ArrayFromRows(object rows, Type arrayType, string propertyName)
        {
            int rank = arrayType.GetArrayRank();
            Type elementType = arrayType.GetElementType();
            bool elementIsValueAlone = true == IsValueAlone(elementType) || true == HasTextConverter(elementType);
            bool takesNull = false == elementType.IsValueType || null != Nullable.GetUnderlyingType(elementType);
            string fromCtx = null == propertyName ? string.Empty : $" from [{propertyName}]";

            // Rows given as a record are the rows it holds: keyed by place, {"GPALKEY0000": [...]}, as a class's data holds
            // them, or under the one element name xml gives them, {"row": [...]}. A record of one key is such a wrapper when it
            // holds a list or a record, or one value of a grid of values alone; in the last dimension, whose items are the
            // values, only when no member of the element type of its name takes what it holds, so a row that is a record of
            // one member stays that record
            bool MemberTakes(string memberName, object heldValue)
            {
                Type memberType = elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(property => true == string.Equals(property.Name, memberName, StringComparison.OrdinalIgnoreCase))?.PropertyType
                    ?? elementType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(field => true == string.Equals(field.Name, memberName, StringComparison.OrdinalIgnoreCase))?.FieldType;
                return null != memberType
                    && (typeof(object) == memberType
                        || (heldValue is IList ? true == IsEnumerableType(memberType) && typeof(string) != memberType
                            : heldValue is IDictionary ? false == IsValueAlone(memberType)
                            : true == IsValueAlone(memberType) || true == HasTextConverter(memberType)));
            }
            List<object> RowsAt(object level, int dimension)
            {
                IDictionary record = level as IDictionary;
                object heldAlone = null != record && 1 == record.Count ? record.Values.Cast<object>().First() : null;
                IEnumerable held = null == level
                    ? new List<object>()
                    : null != record && true == record.Keys.Cast<object>().All(key => Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                    ? record.Values
                    : null != record && 1 == record.Count
                        && (heldAlone is IList || heldAlone is IDictionary || true == elementIsValueAlone)
                        && (dimension + 1 < rank
                            || false == MemberTakes(record.Keys.Cast<object>().First()?.ToString() ?? string.Empty, heldAlone))
                    ? (heldAlone is IList heldRows ? heldRows : new List<object> { heldAlone })
                    : level is IList listed
                    ? listed
                    : new List<object> { level };
                List<object> levelRows = new List<object>();
                foreach (object row in held)
                    levelRows.Add(dimension + 1 < rank ? RowsAt(row, dimension + 1) : row);
                return levelRows;
            }
            List<object> grid = RowsAt(rows, 0);

            // -1 until a row of that dimension is seen
            int[] lengths = Enumerable.Repeat(-1, rank).ToArray();
            bool ragged = false;
            void Measure(IList level, int dimension)
            {
                if (-1 != lengths[dimension] && lengths[dimension] != level.Count)
                    ragged = true;
                lengths[dimension] = Math.Max(lengths[dimension], level.Count);
                if (dimension + 1 < rank)
                    foreach (IList row in level)
                        Measure(row, dimension + 1);
            }
            Measure(grid, 0);

            if (true == ragged)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Rows{fromCtx} differ in length, so the [{ShortTypeName(arrayType)}] is as long as the longest, the places short rows leave holding the [{ShortTypeName(elementType)}] default", null, GPALObjectType.Other);

            // A record is made into a dictionary, a class or a struct of members, and takes its place even when one of its
            // members does not convert, as a record in a list does; any other value is converted as a list item is
            object Cell(object item)
            {
                object cell = null;
                bool wasConverting = convertingListItem;
                valueNotConverted = null == item && false == takesNull;
                // text is converted, so the text null of xml or a delimited file is a null, as ConvertValue reads it
                if (null == item || (true == elementType.IsInstanceOfType(item) && false == item is string))
                    cell = item;
                else if (item is IDictionary record && typeof(string) != elementType
                    && (true == IsDictionaryType(elementType) || true == IsClassType(elementType) || true == IsMemberStruct(elementType) || true == IsCustomStructType(elementType)))
                {
                    cell = CreateObjectFromDictionary(record, elementType, record, null);
                    valueNotConverted = false;
                }
                else
                {
                    convertingListItem = true;
                    try
                    {
                        cell = ConvertValue(item, elementType, null, propertyName);
                    }
                    finally
                    {
                        convertingListItem = wasConverting;
                    }
                }
                if (true == valueNotConverted)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog(item)}]{fromCtx} is not a [{ShortTypeName(elementType)}], so its place holds the default", item, GPALObjectType.Other);
                    cell = null;
                }
                return cell;
            }

            Array retVal = Array.CreateInstance(elementType, lengths.Select(length => Math.Max(0, length)).ToArray());
            int[] at = new int[rank];
            void Place(IList level, int dimension)
            {
                for (int index = 0; index < level.Count; index++)
                {
                    at[dimension] = index;
                    if (dimension + 1 < rank)
                        Place((IList)level[index], dimension + 1);
                    else
                        retVal.SetValue(Cell(level[index]), at);
                }
            }
            Place(grid, 0);

            // the grid is made, whatever its values did
            valueNotConverted = false;

            return retVal;
        }

        // Set when ConvertValue gives a default in place of a value it could not convert, so an item of a collection
        // member that does not convert is not added; cleared by the caller before each item
        [ThreadStatic]
        private static bool valueNotConverted;
        // Set while an item of a list, array or other collection, or a dictionary key, is converted: a value that does not
        // convert is not added, so ConvertValue does not say it returns a default and the caller says what it does
        [ThreadStatic]
        private static bool convertingListItem;
        private static object ConvertValue(dynamic value, Type targetType, dynamic parentDictionary = null, string propertyName = null)
        {
            string fromCtx = propertyName != null ? $" from [{propertyName}]" : string.Empty;

            // Handle null / empty / "null"
            bool isNullInput = false;

            // passthrough, as an object holds the value: json's and yaml's numbers as doubles and lists as lists
            if (targetType == typeof(object))
                return ObjectSlotValue((object)value);

            // === Tuple handling (ValueTuple<> and Tuple<>) ===
            if (IsTupleType(value?.GetType()) && IsTupleType(targetType))
            {
                Type targetTupleType = Nullable.GetUnderlyingType(targetType) ?? targetType;

                // Extract elements from source tuple using ITuple (available in .NET 4.7+ / .NET Core)
                if (value is System.Runtime.CompilerServices.ITuple sourceTuple)
                {
                    int arity = sourceTuple.Length;
                    Type[] targetElementTypes = targetTupleType.GetGenericArguments();

                    // Arity must match (we don't support automatic padding/truncation)
                    if (arity == targetElementTypes.Length)
                    {
                        var convertedItems = new object[arity];
                        for (int i = 0; i < arity; i++)
                        {
                            object item = sourceTuple[i];
                            convertedItems[i] = ConvertValue(item, targetElementTypes[i]);
                        }

                        // Now create the target tuple via constructor
                        // All ValueTuple<> and Tuple<> types have a constructor taking each element
                        var ctor = targetTupleType.GetConstructor(targetElementTypes);
                        if (ctor != null)
                        {
                            return ctor.Invoke(convertedItems);
                        }
                    }
                }
            }

            try
            {
                bool isStruct = IsCustomStructType(value?.GetType());

                Type ut2 = Nullable.GetUnderlyingType(targetType) ?? targetType;
                bool isCharTarget = ut2 == typeof(char);
                // empty or blank text is text to a string target, and is kept; to any other target it has no value.
                // The text null is a null, except to a string target read from json, yaml or a class, which have a null
                // of their own, so there the text null is the text
                bool isStringTarget = typeof(string) == ut2;
                DataFormat sourceFormat = 0 < (ConverterSettings?.InDataFormat?.Count ?? 0) ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
                bool textNullIsText = true == isStringTarget && (DataFormat.JSON == sourceFormat || DataFormat.YAML == sourceFormat || DataFormat.CLASS == sourceFormat);
                isNullInput = false == isStruct
                ? null == (object)value ||  // compared as object: a dynamic struct, such as a ValueTuple or a struct of the program's own, has no == with null
                    (value is string str && !isCharTarget && false == isStringTarget && string.IsNullOrWhiteSpace(str)) ||
                    (value is string s2 && false == textNullIsText && s2.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                : false;
            }
            catch
            {
                isNullInput = true;
            }

            if (isNullInput)
            {
                if (IsNullableType(targetType) || targetType.IsClass)
                    return null;

                if (false == convertingListItem)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"Cannot convert [{DescribeForLog(value)}] to non-nullable [{ShortTypeName(targetType)}]{fromCtx}. Returning default.",
                        null, GPALObjectType.Other);
                valueNotConverted = true;
                return Activator.CreateInstance(targetType);
            }

            Type underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // === Collection handling ===
            if (IsSupportedCollectionType(underlyingTargetType))
            {
                // an array of two or more dimensions, such as int[,], is read from rows of rows, each value in its place
                bool multiDimensional = true == underlyingTargetType.IsArray && 1 < underlyingTargetType.GetArrayRank();
                Type elementType = underlyingTargetType.IsArray
                    ? underlyingTargetType.GetElementType()!
                    : underlyingTargetType.GetGenericArguments()[0];

                // a list as xml writes one, its items under one element name, gives those items
                IList wrappedItems = true == multiDimensional ? null : ListWrapperItems((object)value, elementType);
                IEnumerable<object> sourceItems = true == multiDimensional
                    ? Enumerable.Empty<object>()
                    : null != wrappedItems
                    ? wrappedItems.Cast<object>()
                    : (value is IEnumerable enumerable && false == value is string)
                    ? enumerable.Cast<object>()
                    : new[] { value };

                // an item read from text alone that does not convert, such as x for an int, is not added, with a warning.
                // When no item converts, the value does not convert, and a caller converting it as an item of its own says so
                var convertedItems = new List<object>();
                List<object> itemsNotConverted = new List<object>();
                bool wasConverting = convertingListItem;
                bool typeFromText = true == IsValueAlone(elementType) || true == HasTextConverter(elementType);
                foreach (var item in sourceItems)
                {
                    // a value alone, such as text or a null, is converted as an item read from text, whatever the item type
                    bool itemFromText = true == typeFromText || (false == item is IDictionary && false == item is IList);
                    object converted = null;
                    valueNotConverted = false;
                    convertingListItem = true == wasConverting || true == itemFromText;
                    try
                    {
                        converted = ConvertValue(item, elementType);
                    }
                    finally
                    {
                        convertingListItem = wasConverting;
                    }
                    if (true == itemFromText && true == valueNotConverted)
                        itemsNotConverted.Add(item);
                    else
                        convertedItems.Add(converted);
                }
                valueNotConverted = 0 < itemsNotConverted.Count && 0 == convertedItems.Count;
                if (false == valueNotConverted || false == wasConverting)
                    foreach (object item in itemsNotConverted)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog(item)}]{fromCtx} is not a [{ShortTypeName(elementType)}], so it is not added", item, GPALObjectType.Other);

                return true == multiDimensional
                    ? ArrayFromRows((object)value, underlyingTargetType, propertyName)
                    : CreateCollection(convertedItems, underlyingTargetType);
            }

            // === Scalar conversion ===

            // Special float/double: NaN, Infinity, -Infinity – accept .nan, .inf, etc.
            if (value is string floatStr)
            {
                string trimmed = floatStr.Trim().ToLowerInvariant();
                if (underlyingTargetType == typeof(float) || underlyingTargetType == typeof(double))
                {
                    if (trimmed == "nan" || trimmed == ".nan")
                        return underlyingTargetType == typeof(float) ? (object)float.NaN : double.NaN;
                    if (trimmed == "infinity" || trimmed == ".inf" || trimmed == "+infinity" || trimmed == "+.inf")
                        return underlyingTargetType == typeof(float) ? (object)float.PositiveInfinity : double.PositiveInfinity;
                    if (trimmed == "-infinity" || trimmed == "-.inf")
                        return underlyingTargetType == typeof(float) ? (object)float.NegativeInfinity : double.NegativeInfinity;
                    // a zero written with a minus, such as -0, is a negative zero, which .NET Framework reads as 0
                    if (true == trimmed.StartsWith("-", StringComparison.Ordinal)
                        && true == double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double signedZero) && 0 == signedZero)
                        return typeof(float) == underlyingTargetType ? (object)(-0.0f) : -0.0;
                }
                // a decimal written with an exponent, such as 1E-10, as a double is written
                if (typeof(decimal) == underlyingTargetType && false == decimal.TryParse(floatStr.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                    && true == decimal.TryParse(floatStr.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal withExponent))
                    return withExponent;
            }

            // A decimal, as json and yaml read a number that keeps its digits, given to a double or a float is read from its
            // text, as the decimal's own conversion can miss the nearest double by its last bit
            if (value is decimal decimalNumber && (typeof(double) == underlyingTargetType || typeof(float) == underlyingTargetType))
                value = decimalNumber.ToString(CultureInfo.InvariantCulture);

            // === Enum handling (any enum, including flags and nullable) ===
            if (IsEnumType(underlyingTargetType))
            {
                string strValue = value.ToString().Trim();

                // Dynamic generic TryParse<TEnum>(string, bool ignoreCase, out TEnum result)
                var tryParseMethod = typeof(Enum)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        m.Name == nameof(Enum.TryParse)
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == 3
                        && m.GetParameters()[0].ParameterType == typeof(string)
                        && m.GetParameters()[1].ParameterType == typeof(bool)
                        && m.GetParameters()[2].IsOut);

                if (tryParseMethod != null)
                {
                    var genericMethod = tryParseMethod.MakeGenericMethod(underlyingTargetType);
                    object[] parameters = new object[] { strValue, true, null }; // ignoreCase = true
                    bool success = (bool)genericMethod.Invoke(null, parameters);
                    if (success)
                    {
                        return parameters[2]; // the parsed enum value
                    }
                }

                // Fallback: try parsing as numeric value (common in serialized data)
                if (long.TryParse(strValue, out long numericValue))
                {
                    try
                    {
                        return Enum.ToObject(underlyingTargetType, numericValue);
                    }
                    catch
                    {
                        // Invalid numeric value for this enum (out of range)
                    }
                }

                // If all else fails — return default (0) with warning
                if (false == convertingListItem)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"Invalid enum value [{value}] for type [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default (0).",
                        null, GPALObjectType.Other);
                valueNotConverted = true;

                return Enum.ToObject(underlyingTargetType, 0);
            }

            // A number given to a type written as text, such as the version 1.2, which an xml file reads as the number 1.2,
            // is read from its text
            if ((true == IsVersionType(underlyingTargetType) || true == IsUriType(underlyingTargetType) || true == IsIpAddressType(underlyingTargetType)
                    || true == IsTimeSpanType(underlyingTargetType) || true == IsGuidType(underlyingTargetType) || true == IsComplexType(underlyingTargetType)
                    || true == IsDateTimeType(underlyingTargetType) || true == IsDateTimeOffsetType(underlyingTargetType))
                && (value is decimal || value is double || value is float || value is long || value is int || value is BigInteger))
                value = value is double doubleNumber ? NumberText(doubleNumber) : value is float floatNumber ? NumberText(floatNumber) : ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);

            // === Custom parsing for non-ChangeType types ===
            if (value is string strValue2)
            {
                string trimmed = strValue2.Trim();

                if (IsGuidType(underlyingTargetType))
                {
                    if (Guid.TryParse(trimmed, out Guid guid))
                        return guid;
                    // Strip leading _ added by SanitizeXmlElementName for digit-starting dictionary keys
                    if (trimmed.Length > 1 && trimmed[0] == '_' && Guid.TryParse(trimmed.Substring(1), out Guid strippedGuid))
                        return strippedGuid;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Guid format: [{strValue2}]{fromCtx}. Returning Guid.Empty.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return Guid.Empty;
                }

                if (IsIpAddressType(underlyingTargetType))
                {
                    if (IPAddress.TryParse(trimmed, out IPAddress ip))
                        return ip;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid IPAddress format: [{strValue2}]{fromCtx}. Returning IPAddress.Any.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return IPAddress.Any;
                }

                if (IsUriType(underlyingTargetType))
                {
                    if (Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out Uri uri))
                        return uri;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Uri format: [{strValue2}]{fromCtx}. Returning null.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return null;
                }

                if (IsTimeSpanType(underlyingTargetType))
                {
                    if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan ts))
                        return ts;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid TimeSpan format: [{strValue2}]{fromCtx}. Returning TimeSpan.Zero.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return TimeSpan.Zero;
                }

                // Parse with RoundtripKind so UTC reads back as UTC. Convert.ChangeType below would return local time
                // for a trailing Z. Unparsable values go on to the checks below.
                if (IsDateTimeType(underlyingTargetType)
                    && DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime roundTripped))
                    return roundTripped;

                // Parse with RoundtripKind so the written offset is kept, not shifted to local time.
                if (IsDateTimeOffsetType(underlyingTargetType))
                {
                    if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset dto))
                        return dto;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid DateTimeOffset format: [{strValue2}]{fromCtx}. Returning DateTimeOffset.MinValue.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return DateTimeOffset.MinValue;
                }

                if (IsBigIntegerType(underlyingTargetType))
                {
                    if (BigInteger.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger bi))
                        return bi;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger format: [{strValue2}]{fromCtx}. Returning 0.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return BigInteger.Zero;
                }

                if (IsComplexType(underlyingTargetType))
                {
                    dynamic parsedComplex = ParseComplex(trimmed);
                    if (null == parsedComplex)
                    {
                        if (false == convertingListItem)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Complex format: [{strValue2}]{fromCtx}. Returning 0+0i.", null, GPALObjectType.Other);
                        parsedComplex = new Complex(0, 0);
                        valueNotConverted = true;
                    }
                    return parsedComplex;
                }

                if (IsVersionType(underlyingTargetType))
                {
                    if (System.Version.TryParse(trimmed, out System.Version ver))
                        return ver;
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Version format: [{strValue2}]{fromCtx}. Returning 0.0.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return new System.Version(0, 0);
                }
            }

            // === Version is a special type
            if (true == IsVersionType(underlyingTargetType))
            {
                if (value.GetType() == typeof(System.Version))
                    return value;

                List<int> versionInfo = new List<int>();
                int count = 4;

                foreach (KeyValuePair<object, object> item in value)
                {
                    if (0 == count--)
                        break;
                    versionInfo.Add(Int32.Parse(item.Value.ToString()));
                }

                return new System.Version(versionInfo[0], versionInfo[1], versionInfo[2], versionInfo[3]);
            }

            if (typeof(WaitTime) == underlyingTargetType)
            {
                WaitTime wt;
                if (true == TryConvertToWaitTime(value, out wt))
                    return wt;
            }

            // a registered struct, or a struct of members given a record of them, such as a struct of the program's own in
            // an array or a list, is filled from the record
            if (true == IsCustomStructType(underlyingTargetType) || (true == IsMemberStruct(underlyingTargetType) && value is IDictionary))
            {
                try
                {
                    return value.GetType() == underlyingTargetType
                        ? value
                        : true == IsCustomStructType(underlyingTargetType)
                        ? ConvertToClass(value, underlyingTargetType, parentDictionary, null)
                        : CreateObjectFromDictionary(value, underlyingTargetType, value, null);
                }
                catch (Exception ex)
                {
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                            $"Failed to convert [{value}] [{ShortTypeName(value?.GetType())}] to [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default.",
                            null, GPALObjectType.Other, ex);
                    valueNotConverted = true;
                    return Activator.CreateInstance(underlyingTargetType);
                }
            }
            // BigInteger is listed in IsSimpleType but doesn't implement IConvertible — Convert.ChangeType fails.
            // Handle it explicitly before the generic simple-type path.
            if (IsBigIntegerType(underlyingTargetType))
            {
                try { return AsBigInteger(value); }
                catch
                {
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger value: [{value}]{fromCtx}. Returning 0.", null, GPALObjectType.Other);
                    valueNotConverted = true;
                    return BigInteger.Zero;
                }
            }

            // === Simple built-in types (primitives, decimal, DateTime) ===
            if (IsSimpleType(underlyingTargetType))
            {
                // BigInteger doesn't implement IConvertible — Convert.ChangeType(BigInteger, T) always throws.
                // Route through its string representation, which Convert.ChangeType handles for all numeric types.
                if (value is BigInteger bigIntSrc)
                {
                    try { return System.Convert.ChangeType(bigIntSrc.ToString(CultureInfo.InvariantCulture), underlyingTargetType, CultureInfo.InvariantCulture); }
                    catch (Exception ex)
                    {
                        if (false == convertingListItem)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to convert BigInteger [{value}] to [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default.", null, GPALObjectType.Other, ex);
                        valueNotConverted = true;
                        return Activator.CreateInstance(underlyingTargetType);
                    }
                }
                // a struct of the framework or a class written as text by its own converter, such as a Point written
                // "3, 4" or a Font written "Arial, 9.75pt, style=Bold", reads back through that converter. A value that
                // is already of the type, such as a Font, is kept
                TypeConverter textConverter = value is string && false == typeof(IConvertible).IsAssignableFrom(underlyingTargetType)
                    ? TypeDescriptor.GetConverter(underlyingTargetType)
                    : null;
                try
                {
                    return null != textConverter && true == textConverter.CanConvertFrom(typeof(string))
                        ? textConverter.ConvertFromInvariantString((string)value)
                        : false == value is IConvertible && true == underlyingTargetType.IsInstanceOfType((object)value)
                            ? value
                            : System.Convert.ChangeType(value, underlyingTargetType, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    // a class, such as a Font, that has no value without arguments is null
                    if (false == convertingListItem)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                            $"Failed to convert [{value}] [{ShortTypeName(value?.GetType())}] to [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning {(true == underlyingTargetType.IsValueType ? "default" : "null")}.",
                            null, GPALObjectType.Other, ex);
                    valueNotConverted = true;
                    return true == underlyingTargetType.IsValueType ? Activator.CreateInstance(underlyingTargetType) : null;
                }
            }

            // already the right type — no conversion needed
            // a value going into a place typed object as an object holds it: json's and yaml's numbers as doubles and lists as lists
            if (targetType.IsAssignableFrom(value.GetType()))
                return typeof(object) == targetType ? ObjectSlotValue((object)value) : value;

            // Cycle sentinel — "Recursion to [TypeName]" strings are placeholders written during
            // serialization when a cycle is detected. They cannot be reconstructed; return null/default.
            if (value is string cvCycleStr && cvCycleStr.StartsWith("Recursion to [") && cvCycleStr.EndsWith("]") && targetType != typeof(string))
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            // dict → POCO: delegate to the full reconstruction engine
            if (value is IDictionary dictSrc && IsClassType(targetType) && !IsDictionaryType(targetType))
            {
                try { return CreateObjectFromDictionary(dictSrc, targetType, dictSrc, null); }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to reconstruct [{ShortTypeName(targetType)}] from dict{fromCtx}", (object)value, GPALObjectType.None, ex);
                }
            }

            // List wrapping a POCO — peel layers until we reach a dict or give up.
            if (value is IList cvListVal && IsClassType(targetType) && !IsDictionaryType(targetType) && !IsSupportedCollectionType(targetType))
            {
                object cvInner = cvListVal;
                while (cvInner is IList cvInnerList && cvInnerList.Count == 1)
                    cvInner = cvInnerList[0];
                if (cvInner is IDictionary cvInnerDict)
                {
                    try { return CreateObjectFromDictionary(cvInnerDict, targetType, cvInnerDict, null); }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to reconstruct [{ShortTypeName(targetType)}] from unwrapped dict{fromCtx}", (object)value, GPALObjectType.None, ex);
                    }
                }
            }

            // === Final fallback ===
            // an item of a list or a dictionary value is reported by its caller as not added
            if (false == convertingListItem)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Unable to convert [{DescribeForLog(value)}] to [{ShortTypeName(targetType)}]{fromCtx}",
                    null, GPALObjectType.Other);
            valueNotConverted = true;
            return value;
        }

        private static string ShortTypeName(Type t)
        {
            if (t == null) return "null";
            if (IsNullableType(t))
                return ShortTypeName(Nullable.GetUnderlyingType(t)) + "?";
            if (!t.IsGenericType) return t.Name;
            if (IsTupleType(t))
                return "(" + string.Join(", ", t.GetGenericArguments().Select(a => ShortTypeName(a))) + ")";
            string baseName = t.Name.Substring(0, t.Name.IndexOf('`'));
            string args = string.Join(", ", t.GetGenericArguments().Select(a => ShortTypeName(a)));
            return $"{baseName}<{args}>";
        }

        private static string CompactValue(object v, int depth = 0)
        {
            if (v == null) return "null";
            if (v is string s) return s.Length <= 40 ? s : s.Substring(0, 40) + "...";
            if (depth >= 1)
            {
                if (v is IDictionary) return "{...}";
                if (v is IEnumerable) return "[...]";
            }
            if (v is IDictionary dict)
            {
                var pairs = new List<string>();
                foreach (dynamic kvp in dict) { pairs.Add($"{kvp.Key}={CompactValue(kvp.Value, depth + 1)}"); if (pairs.Count >= 3) break; }
                return "{" + string.Join(", ", pairs) + (dict.Count > 3 ? ", ..." : "") + "}";
            }
            if (v is IEnumerable seq)
            {
                var items = new List<string>();
                foreach (var item in seq) { items.Add(CompactValue(item, depth + 1)); if (items.Count >= 2) break; }
                return "[" + string.Join(", ", items) + (items.Count >= 2 ? ", ..." : "") + "]";
            }
            try
            {
                string str = v.ToString();
                return str == v.GetType().FullName ? ShortTypeName(v.GetType()) : str;
            }
            catch { return ShortTypeName(v.GetType()); }
        }

        private static string DescribeForLog(object value, int maxLen = 80)
        {
            if (value == null) return "null";
            if (value is string s)
                return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
            if (value is IDictionary dict)
            {
                var pairs = new List<string>();
                foreach (dynamic kvp in dict) { pairs.Add($"{kvp.Key}={CompactValue(kvp.Value)}"); if (pairs.Count >= 3) break; }
                return "{" + string.Join(", ", pairs) + (dict.Count > 3 ? ", ..." : "") + "}";
            }
            if (value is IEnumerable seq)
            {
                var items = new List<string>();
                foreach (var item in seq) { items.Add(CompactValue(item)); if (items.Count >= 3) break; }
                return "[" + string.Join(", ", items) + (items.Count >= 3 ? ", ..." : "") + "]";
            }
            try
            {
                string str = value.ToString();
                return str == value.GetType().FullName ? ShortTypeName(value.GetType()) : (str.Length <= maxLen ? str : str.Substring(0, maxLen) + "...");
            }
            catch { return ShortTypeName(value.GetType()); }
        }


        /// <summary>
        /// The text of an XML element as JSON and YAML write it: a number when the text is a number written plainly, such
        /// as 1, -2, 2.5 or 42.50, else the text itself, so 007, .5, -0 or 1e3 stays as it was written. A decimal keeps
        /// the digits as written, so 42.50 is written 42.50. A whole number too large for a long stays whole, as a YAML
        /// file's does.
        /// </summary>
        /// <param name="text">The element's text</param>
        /// <returns>A long, a BigInteger, a decimal, or the text</returns>
        internal static object XmlTextValue(string text)
        {
            object retVal = text;
            if (true == long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole) && whole.ToString(CultureInfo.InvariantCulture) == text)
                retVal = whole;
            else if (true == BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger bigWhole) && bigWhole.ToString(CultureInfo.InvariantCulture) == text)
                retVal = bigWhole;
            else if (true == decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)
                && number.ToString(CultureInfo.InvariantCulture) == text)
                retVal = number;
            return retVal;
        }

        public static object NormalizeForCleanJson(object input)
        {
            // NOTE
            if (input is Dictionary<object, dynamic> dict)
            {
                // Check if this dict looks like it came from XML (has @ or #text keys)
                bool hasXmlMarkers = dict.Keys.Cast<object>().Any(k => k.ToString().StartsWith("@GPAL") || k.ToString() == "#text");

                if (!hasXmlMarkers)
                {
                    // A dictionary key can be a complex object, and JSON keys must be strings. Write the dictionary as
                    // an array of {key, value} pairs so the key stays a JSON object. GPALKEY_PAIRS marks this shape for
                    // ConvertJSONToDictionary.
                    // a struct of the program's own is a complex key too, as its text names its type alone
                    bool hasComplexKey = dict.Keys.Cast<object>().Any(k => null != k && (false == IsSimpleType(k.GetType()) || true == IsMemberStruct(k.GetType())));
                    if (hasComplexKey)
                    {
                        var pairList = new List<object>();
                        foreach (var kvp in dict)
                        {
                            pairList.Add(new Dictionary<string, object>
                            {
                                ["Key"] = NormalizeForCleanJson(ConvertClassToDictionary(kvp.Key)),
                                ["Value"] = NormalizeForCleanJson(kvp.Value)
                            });
                        }
                        return pairList;
                    }

                    // Pure generic dict, all-simple keys: normalize children recursively, but keep as-is at this level. A key
                    // with a converter to and from text, such as a Point, is written as its converter writes it, "1, 2", so
                    // it reads back through the converter
                    var result = new Dictionary<string, object>();
                    foreach (var kvp in dict)
                    {
                        Type keyType = ((object)kvp.Key)?.GetType();
                        string key = (true == IsTextStructType(keyType) || true == IsTextClassType(keyType) ? ValueToString(kvp.Key) : kvp.Key.ToString()).Replace("@GPAL_", "");
                        result[key] = NormalizeForCleanJson(kvp.Value);
                    }
                    return result;
                }

                // XML-derived: clean it up
                var clean = new Dictionary<string, object>();

                string textValue = null;
                if (dict.TryGetValue("#text", out var txt))
                {
                    textValue = txt?.ToString();
                }

                // First: collect attributes (remove @ prefix)
                foreach (var kvp in dict)
                {
                    string key = kvp.Key.ToString();

                    if (key.StartsWith("@GPAL_"))
                    {
                        clean[key.Substring(6)] = kvp.Value;
                    }
                }

                // Then: child elements
                foreach (var kvp in dict)
                {
                    string key = kvp.Key.ToString();
                    if (key.StartsWith("@GPAL_") || key == "#text") continue;

                    object child = NormalizeForCleanJson(kvp.Value);

                    clean[key] = child;
                }

                // If this node has text and attributes (like <color_swatch image="...">Red</color_swatch>)
                // -> keep as object with attributes + value/text field
                if (textValue != null && clean.Count > 0)
                {
                    clean["value"] = textValue;
                    return clean;
                }

                // If only text and no attributes/children -> flatten to direct string/number
                if (textValue != null && clean.Count == 0)
                {
                    return XmlTextValue(textValue);
                }

                return clean;
            }
            else if (input is List<Dictionary<object, dynamic>> list)
            {
                return list.Select(NormalizeForCleanJson).ToList();
            }
            else if (input is List<object> objList)
            {
                return objList.Select(NormalizeForCleanJson).ToList();
            }

            return input; // primitive, leave as-is
        }

        /// <summary>
        /// Strips GPAL###_ prefixes and correctly handles repeated XML elements by turning
        /// sibling elements with the same name into proper lists — without extra nesting.
        /// </summary>
        /// <param name="input">The reader's dictionary, or part of it</param>
        /// <param name="fromXml">True for what the XML reader built, which wraps every element in a list of one. False for a
        /// JSON, YAML, html or class source, whose lists of one, empty lists and keys such as value, item or @id are its own
        /// and are kept; only the GPALKEY prefix is taken off its keys</param>
        public static object StripGpalKeys(object input, bool fromXml = true)
        {
            // Phase 1: Strip keys and correctly group repeated siblings
            // Phase 2: Recursively unwrap unnecessary single-item lists
            object retVal = true == fromXml ? UnwrapSingleItemLists(StripAndGroup(input)) : StripKeyPrefixes(input);
            return retVal;
        }

        // the GPALKEY prefix taken off every key, at every depth; nothing else changed
        private static object StripKeyPrefixes(object input)
        {
            object retVal = input;

            if (input is IDictionary dict)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dict)
                {
                    string key = entry.Key?.ToString() ?? string.Empty;
                    result[key.StartsWith("GPALKEY") ? key.Substring(7) : key] = StripKeyPrefixes(entry.Value);
                }
                retVal = result;
            }
            else if (input is IEnumerable enumerable && false == input is string)
                retVal = enumerable.Cast<object>().Select(StripKeyPrefixes).ToList();

            return retVal;
        }

        // xmlKeys true for what the XML reader built: @ is taken off a key, #text becomes value, and an object of one value
        // becomes that value. False for any other source, whose keys such as @id or value are its own and are kept
        private static object StripAndGroup(object input, bool xmlKeys = true)
        {
            if (input == null) return null;

            if (input is IDictionary dict)
            {
                var grouped = new Dictionary<string, List<object>>(StringComparer.Ordinal);

                foreach (DictionaryEntry entry in dict)
                {
                    string originalKey = entry.Key?.ToString() ?? string.Empty;
                    string cleanedKey = originalKey;

                    if (cleanedKey.StartsWith("@GPAL_"))
                        cleanedKey = cleanedKey.Substring(6); // -> @description
                    else if (true == xmlKeys && cleanedKey.StartsWith("@"))
                        cleanedKey = cleanedKey.Substring(1);

                    object value = StripAndGroup(entry.Value, xmlKeys);

                    // ConvertXmlNodeToDictionary always wraps each child node in a List<Dict> (even when there is
                    // only one child with that tag name). Unwrap those single-item dict-lists here so that sibling
                    // elements group correctly: e.g. three <item> siblings produce [pt1,pt2,pt3] not
                    // [[pt1],[pt2],[pt3]]. Only unwrap when the single item is a dict; scalar single-item lists
                    // (e.g. ["X"]) must stay wrapped so they round-trip as single-element collections.
                    if (value is List<object> singleWrap && singleWrap.Count == 1 && singleWrap[0] is IDictionary)
                        value = singleWrap[0];

                    // If this is text content with no key (common for mixed content elements like color_swatch)
                    if (true == xmlKeys && string.IsNullOrEmpty(cleanedKey) && value is string str && !string.IsNullOrWhiteSpace(str))
                        cleanedKey = "value";
                    // Flatten pure text nodes
                    else if (true == xmlKeys && "#text" == cleanedKey)
                        cleanedKey = "value";
                    else if (cleanedKey.StartsWith("GPALKEY"))
                        cleanedKey = cleanedKey.Substring(7);
                    // ConvertXmlNodeToDictionary tags each child "GPAL###_tagname" (a global, ever-increasing
                    // index) purely so same-named siblings don't collide as dictionary keys before grouping
                    // happens right here - the index has no meaning past this point and must never reach
                    // output. "GPALKEY" is excluded since it's letters, not digits, right after "GPAL", so
                    // this can't collide with that other internal marker scheme.
                    else if (System.Text.RegularExpressions.Regex.IsMatch(cleanedKey, @"^GPAL\d+_"))
                        cleanedKey = System.Text.RegularExpressions.Regex.Replace(cleanedKey, @"^GPAL\d+_", "");

                    if (!grouped.TryGetValue(cleanedKey, out var list))
                    {
                        list = new List<object>();
                        grouped[cleanedKey] = list;
                    }
                    list.Add(value);
                }

                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kvp in grouped)
                {
                    result[kvp.Key] = kvp.Value.Count == 1 ? kvp.Value[0] : (object)kvp.Value;
                }
                // If this is a leaf text node (only one entry and it's "value"), promote it to the parent
                if (true == xmlKeys && 1 == result.Count && result.ContainsKey("value"))
                {
                    return result["value"]; // flatten pure text nodes to scalar
                }
                return result;
            }

            if (input is IEnumerable enumerable && !(input is string))
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    list.Add(StripAndGroup(item, xmlKeys));
                }
                return list.Count == 0 ? null : list;
            }

            return input;
        }

        // xmlKeys true for what the XML reader built: an object of one value becomes that value and an {item: [...]} wrapper
        // becomes its list. False for any other source, whose value and item keys are its own and are kept
        private static object UnwrapSingleItemLists(object input, bool xmlKeys = true)
        {
            if (input == null) return null;

            // If it's a list with exactly one item -> unwrap it only if scalar (XML text node)
            // OR if inner is a list of records or was collapsed from an {item: [...]} XML wrapper.
            if (input is IList list && list.Count == 1)
            {
                object inner = UnwrapSingleItemLists(list[0], xmlKeys);
                if (inner == null || inner is string || inner is ValueType)
                    return inner;
                // Rule 3: if the inner item resolved to a list of records, or a dictionary resolved to a list
                // (e.g. from {item:[...]} collapse), return it directly so the outer single-item wrapper doesn't
                // re-box it. A list that holds one list of values, such as a file of one row, keeps its wrapper.
                if (inner is IList innerList && (list[0] is IDictionary || innerList.Cast<object>().Any(item => item is IDictionary)))
                    return inner;
                return new List<object> { inner };
            }

            // If it's a list with >1 items -> always recurse into every child so nested
            // single-item list wrappers (XML artifacts) are fully unwrapped at every depth.
            if (input is IList multiList && multiList.Count > 1)
            {
                var unwrapped = new List<object>();
                foreach (var item in multiList)
                {
                    unwrapped.Add(UnwrapSingleItemLists(item, xmlKeys));
                }
                return unwrapped;
            }

            // If it's a dictionary -> process values, then collapse {item: [list]} wrappers
            if (input is IDictionary dict)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dict)
                {
                    string key = entry.Key?.ToString() ?? "";
                    object value = UnwrapSingleItemLists(entry.Value, xmlKeys);
                    result[key] = value;
                }
                // Collapse pure text-node singleton
                if (true == xmlKeys && 1 == result.Count && result.ContainsKey("value"))
                    return result["value"];
                // Rule 2: collapse XML {item: [...]} wrapper dicts — "item" is the generic
                // element tag name GPAL uses for array children, not a real property name.
                // Scalar value means a single-element array was serialized; wrap it back in a list.
                if (true == xmlKeys && 1 == result.Count && result.ContainsKey("item"))
                {
                    var itemVal = result["item"];
                    return itemVal is IList ? itemVal : new List<object> { itemVal };
                }
                return result;
            }

            // Scalar -> return as-is
            return input;
        }

        public static bool TryConvertToWaitTime(object value, out WaitTime result)
        {
            result = WaitTime.Immediate;

            if (value == null)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.WARNING,
                    "WaitTime treating NULL as ZERO",
                    value,
                    GPALObjectType.Converter);

                result = 0;
                return true;
            }

            if (value is WaitTime wt)
            {
                result = wt;
                return true;
            }

            if (value is int i)
            {
                result = i;
                return true;
            }

            if (value is string s)
            {
                if (WaitTime.TryFromString(s, out result))
                    return true;
            }

            if (WaitTime.TryFromDictionary((IDictionary<object, object>)value, out result))
            {
                return true;
            }

            GPAL.PublishSimpleEvent(
                GPALEventType.WARNING,
                $"WaitTime conversion failed: unsupported type [{value.GetType().FullName}]",
                value,
                GPALObjectType.Converter);

            return false;
        }
    }
}
