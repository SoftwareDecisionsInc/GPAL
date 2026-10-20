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
    class ConverterHelper
    {
        internal static ConverterSettings ConverterSettings { get; set; }
        internal static Dictionary<Type, MethodInfo> ToStringCache { get; set; } = new Dictionary<Type, MethodInfo>();

        public static List<dynamic> ConvertClassToList(dynamic myClass)
        {
            Type myClassType = myClass.GetType();
            PropertyInfo[] properties = myClassType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            FieldInfo[] fields = myClassType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            List<dynamic> list = new List<dynamic>();
            List<dynamic> myList;

            // first check the object itself for collections
            // we don't want to iterate properties of collection
            // just on classes
            if (IsDictionaryType(myClassType))
            {
                int idx = 0;
                foreach (KeyValuePair<dynamic, dynamic> item in myClass)
                {
                    Type elementType = item.GetType();

                    if (true == IsSimpleType(elementType))
                        list.Add(item.Value);
                    else
                    {
                        myList = ConvertClassToList(item.Value);
                        list.Add(myList);
                    }
                    idx++;
                }
                return list;
            }
            else if (IsEnumerableType(myClassType))
            {
                int idx = 0;
                foreach (var item in myClass)
                {
                    Type elementType = item.GetType();

                    if (true == IsSimpleType(elementType))
                        list.Add(item);
                    else
                    {
                        myList = ConvertClassToList(item);
                        list.Add(myList);
                    }
                    idx++;
                }
                return list;
            }
            else if (true == myClassType.IsArray) // must before checking for enumerable, arrays are enumerable but will not convert like enums - NOTE: ok, so did i find a way to not use this?
            {
                var arrayList = myClass as Array;
                int len = arrayList.Length;

                for (int idx = 0; idx < len; idx++)
                {
                    var elementValue = arrayList.GetValue(idx);
                    var elementType = elementValue.GetType();

                    if (true == IsSimpleType(elementType))
                        list.Add(elementValue);
                    else
                    {
                        myList = ConvertClassToList(elementValue);
                        list.Add(myList);
                    }
                }
                return list;
            }

            if (null != properties)
                foreach (PropertyInfo property in properties)
                {
                    // skip indexers, property but nothing to read
                    if (property.GetIndexParameters().Length > 0 || // Indexer property
                        property.DeclaringType != null && property.DeclaringType.Namespace.StartsWith("System.Collections"))
                    {
                        continue; // Skip without logging
                    }

                    Type propertyType = property.PropertyType;

                    if (property.CanRead)
                    {
                        dynamic value = null;

                        if (true == propertyType.IsArray) // must be before checking for enumerable, arrays are enumerable but will not convert like enums
                        {
                            var arrayList = property.GetValue(myClass) as Array;
                            int len = arrayList.Length;

                            for (int idx = 0; idx < len; idx++)
                            {
                                var elementValue = arrayList.GetValue(idx);
                                var elementType = elementValue.GetType();

                                if (true == IsSimpleType(elementType))
                                    list.Add(elementValue);
                                else if (true == IsEnumerableType(elementType))
                                {
                                    IEnumerable enumeration = (IEnumerable)elementValue;
                                    list.Add(enumeration);
                                }
                                else if (true == IsDictionaryType(elementType))
                                {
                                    myList = ConvertClassToList(elementValue);
                                    list.Add(myList);
                                }
                            }
                        }
                        else
                        {
                            try
                            {
                                value = property.GetValue(myClass);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to getvalue of property [{property.Name}][{propertyType}]. Continuing and hoping for the best.", myClass, GPALObjectType.Other, ex);
                            }

                            if (value != null)
                            {
                                if (IsBigIntegerType(propertyType) || IsVersionType(propertyType))
                                {
                                    list.Add(value.ToString());
                                }
                                else if (IsSimpleType(propertyType))
                                {
                                    list.Add(ConvertValue(value, propertyType));
                                }
                                else if (IsDictionaryType(propertyType))
                                {
                                    list.Add(ConvertClassToList(value));
                                }
                                else if (IsEnumerableType(propertyType))
                                {
                                    int idx = 0;
                                    foreach (var item in value)
                                    {
                                        var elementType = item.GetType();

                                        if (true == IsSimpleType(elementType))
                                            list.Add(item);
                                        else if (true == IsComplexType(elementType))
                                        {
                                            string sign = item.Imaginary >= 0 ? "+" : "";
                                            list.Add($"{item.Real.ToString(CultureInfo.InvariantCulture)}{sign}{item.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                                        }
                                        else
                                        {
                                            myList = ConvertClassToList(item);
                                            list.Add(myList);
                                        }
                                        idx++;
                                    }
                                }
                                else if (true == IsComplexType(propertyType))
                                {
                                    string sign = value.Imaginary >= 0 ? "+" : "";
                                    list.Add($"{value.Real.ToString(CultureInfo.InvariantCulture)}{sign}{value.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                                }
                                else
                                {
                                    list.Add(ConvertClassToList(value));
                                }
                            }
                        }
                    }
                }

            if (null != fields)
                foreach (FieldInfo field in fields)
                {
                    {
                        object value = field.GetValue(myClass);
                        if (value != null)
                        {
                            if (IsSimpleType(field.FieldType))
                            {
                                list.Add(value);
                            }
                            else if (IsDictionaryType(field.FieldType))
                            {
                                list.Add(ConvertClassToList(value));
                            }
                            else if (IsEnumerableType(field.FieldType))
                            {
                                foreach (object item in (IEnumerable<dynamic>)value)
                                {
                                    myList = ConvertClassToList(item);
                                    list.Add(myList);
                                }
                                list.Add(list);
                            }
                            else
                            {
                                list.Add(ConvertClassToList(value));
                            }
                        }
                    }
                }
            return list;
        }
        // For correct reference equality in HashSet<object>
        public class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
        public static Dictionary<object, dynamic> ConvertClassToDictionary(
            dynamic myClass,
            string name = "",
            HashSet<object> visited = null)  // New parameter: tracks visited objects
        {
            // Initialize visited set at root call
            bool isRootCall = visited == null;
            if (isRootCall)
            {
                visited = new HashSet<object>(ReferenceEqualityComparer.Instance); // Critical: reference equality
            }

            Dictionary<object, dynamic> dict = new Dictionary<object, dynamic>();

            if (false == IsCustomStructType(myClass?.GetType()) && false == IsTupleType(myClass?.GetType()) && myClass == null)
            {
                return dict;
            }

            // === CYCLE DETECTION: If this is a reference type we've already seen, return a marker ===
            if (myClass.GetType().IsClass || myClass.GetType().IsInterface) // Reference types only
            {
                if (visited.TryGetValue(myClass, out dynamic fuggetAboutIt))
                {
                    string referenceName = string.Empty;

                    try
                    {
                        referenceName = myClass.Value;
                    }
                    catch
                    {
                        try
                        {
                            referenceName = myClass.Name;
                        }
                        catch
                        {
                            referenceName = myClass.GetType().Name;
                        }
                    }
                    // We've seen this exact object before > return a reference marker
                    return new Dictionary<object, dynamic>
                    {
                        { name, $"Recursion to [{referenceName}]" }
                    };
                }

                // First time seeing this object > add to visited; remove on exit so only the
                // current call-stack path is tracked (shared references are not false-positive cycles)
                visited.Add(myClass);
            }

            bool addedToVisited = false == IsCustomStructType(myClass?.GetType()) && false == IsTupleType(myClass?.GetType()) && myClass != null && (myClass?.GetType().IsClass || myClass?.GetType().IsInterface);
            try
            {

                Type myClassType = myClass?.GetType();
                var properties = (from p in myClassType?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                  group p by p.Name into g
                                  select g.OrderByDescending(t => t.DeclaringType == myClassType).First());

                FieldInfo[] fields = myClassType?.GetFields(BindingFlags.Public | BindingFlags.Instance);
                List<dynamic> list = new List<dynamic>();
                IDictionary myDict;

                // Skip collection checks for user-defined classes...
                if (IsClassType(myClassType) &&
                    !myClassType.IsAbstract &&
                    !myClassType.IsInterface &&
                    !myClassType.IsArray &&
                    !IsSimpleType(myClassType) &&
                    !IsEnumerableType(myClassType) &&
                    !IsDictionaryType(myClassType) &&
                    !myClassType.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
                {
                    goto ProcessPropertiesAndFields;
                }

                // Handle dictionary types
                if (IsDictionaryType(myClassType))
                {
                    // A complex/class-typed key falls back to Object.ToString() (the type's full name for
                    // every instance), so every entry would collide on the same string past the first -
                    // Dictionary<object, dynamic> already accepts non-string keys, so keep the key object
                    // itself instead. A simple-typed key (string, int, enum, etc.) keeps its own type/value
                    // via ConvertValue rather than being coerced to a string, so e.g. an int key round-trips
                    // back as an int, not "2".
                    void AddEntry(dynamic entryKey, dynamic entryValue)
                    {
                        object outputKey = IsSimpleType(entryKey?.GetType()) ? (object)ConvertValue(entryKey, entryKey?.GetType()) : entryKey;
                        // A simple-typed value (string, int, etc.) has no properties/fields to flatten -
                        // recursing it through ConvertClassToDictionary would just return an empty dict,
                        // discarding the value entirely (same reasoning as the key handling above).
                        // A null value (a JSON null) stays null: it has no type to convert it to, and ConvertValue
                        // with no target type fails. Compared as object: a dynamic struct such as a ValueTuple has no
                        // == with null.
                        object outputValue = null == (object)entryValue
                            ? null
                            : IsSimpleType(entryValue.GetType())
                                ? (object)ConvertValue(entryValue, entryValue.GetType())
                                : ConvertClassToDictionary(entryValue, entryKey?.ToString(), visited);
                        dict.Add(outputKey, outputValue);
                    }

                    try
                    {
                        var dictionary = (IDictionary)myClass;
                        foreach (var key in dictionary.Keys)
                        {
                            AddEntry(key, dictionary[key]);
                        }
                    }
                    catch
                    {
                        dict.Clear();
                        foreach (var item in (IEnumerable)myClass)
                        {
                            var itemType = item.GetType();
                            var keyProp = itemType.GetProperty("Key");
                            var valueProp = itemType.GetProperty("Value");
                            if (keyProp != null && valueProp != null)
                            {
                                AddEntry(keyProp.GetValue(item), valueProp.GetValue(item));
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to convert class [{itemType}] Key [{keyProp?.Name}]/Value [{valueProp?.Name}] to dictionary.", myClass, GPALObjectType.Other);
                            }
                        }
                    }
                    return dict;
                }
                else if (IsEnumerableType(myClassType))
                {
                    int idx = 0;

                    foreach (dynamic item in (IEnumerable)myClass)
                    {
                        Type elementType = item?.GetType();
                        if (elementType == null) continue;

                        string keyName = $"GPALKEY{idx:D4}";

                        if (IsSimpleType(elementType))
                        {
                            dict.Add(keyName, ConvertValue(item, elementType));
                        }
                        else if (IsComplexType(elementType))
                        {
                            string sign = item.Imaginary >= 0 ? "+" : "";
                            dict.Add(keyName, $"{item.Real.ToString(CultureInfo.InvariantCulture)}{sign}{item.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                        }
                        else if (IsClassType(elementType) && !IsEnumerableType(elementType) && !IsDictionaryType(elementType) && !elementType.IsArray)
                        {
                            myDict = ConvertClassToDictionary(item, "", visited);
                            dict.Add(keyName, myDict);
                        }
                        else if (IsDictionaryType(elementType) || IsEnumerableType(elementType) || elementType.IsArray)
                        {
                            myDict = ConvertClassToDictionary(item, "", visited);
                            dict.Add(keyName, myDict);
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported element type [{elementType}] in enumerable [{myClassType}]", item, GPALObjectType.Other);
                        }
                        idx++;
                    }
                    return dict;
                }

            ProcessPropertiesAndFields:
                if (properties != null)
                {
                    foreach (PropertyInfo property in properties)
                    {
                        if (property.GetIndexParameters().Length > 0)
                        {
                            continue;
                        }

                        if (property.DeclaringType == myClassType &&
                            property.DeclaringType.Namespace != null &&
                            property.DeclaringType.Namespace.StartsWith("System.Collections"))
                        {
                            continue;
                        }

                        Type propertyType = property.PropertyType;
                        list = new List<dynamic>();

                        if (property.CanRead)
                        {
                            dynamic value = null;
                            try
                            {
                                value = property.GetValue(myClass);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to getvalue of property [{property.Name}][{propertyType}]. Continuing and hoping for the best.", myClass, GPALObjectType.Other, ex);
                            }

                            if ((object)value == null)
                                continue;

                            if (false == IsTupleType(value?.GetType()))
                            {
                                if (propertyType.IsArray)
                                {
                                    var arrayList = (Array)property.GetValue(myClass);
                                    if (null != arrayList)
                                        for (int idx = 0; idx < arrayList.Length; idx++)
                                        {
                                            var elementValue = arrayList.GetValue(idx);
                                            var elementType = elementValue?.GetType() ?? typeof(object);


                                            if (IsSimpleType(elementType))
                                            {
                                                list.Add(ConvertValue(elementValue, elementType));
                                            }
                                            else if (IsEnumerableType(elementType))
                                            {
                                                list.Add((IEnumerable)elementValue);
                                            }
                                            else if (IsDictionaryType(elementType))
                                            {
                                                list.Add(ConvertClassToDictionary(elementValue, $"{property.Name}{idx}", visited));
                                            }
                                            else if (IsCustomStructType(elementType) || IsClassType(elementType) || IsTupleType(elementType))
                                            {
                                                list.Add(ConvertClassToDictionary(elementValue, $"{property.Name}{idx}", visited));
                                            }
                                        }
                                    else
                                        list.Add(ValueToString(arrayList));

                                    if (list.Count > 0)
                                        dict.Add(property.Name, list);
                                }
                                else if (IsSimpleType(propertyType))
                                {
                                    dict.Add(property.Name, ConvertValue(value, propertyType));
                                }
                                else if (IsDictionaryType(propertyType))
                                {
                                    dict.Add(property.Name, ConvertClassToDictionary(value, $"{propertyType}", visited));
                                }
                                else if (IsEnumerableType(propertyType))
                                {
                                    int idx = 0;
                                    if (null != value)
                                        foreach (var item in value)
                                        {
                                            var elementType = item?.GetType() ?? typeof(object);
                                            if (true == IsCustomStructType(item?.GetType()))
                                            {
                                                list.Add(ConvertClassToDictionary(item, $"{property.Name}{idx}", visited));
                                            }
                                            //else if (item == null)
                                            //{
                                            //    list.Add(null);
                                            //}
                                            else if (IsSimpleType(elementType))
                                            {
                                                list.Add(ConvertValue(item, elementType));
                                            }
                                            else if (IsComplexType(elementType))
                                            {
                                                string sign = item.Imaginary >= 0 ? "+" : "";
                                                list.Add($"{item.Real.ToString(CultureInfo.InvariantCulture)}{sign}{item.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                                            }
                                            else if (true == IsGuidType(elementType))
                                            {
                                                list.Add(Guid.TryParse(item.ToString(), out Guid outGuid) ? outGuid : item);
                                            }
                                            else if (IsEnumerableType(elementType))
                                            {
                                                list.Add((IEnumerable)item);
                                            }
                                            else
                                            {
                                                list.Add(ConvertClassToDictionary(item, $"{property.Name}{idx}", visited));
                                            }
                                            idx++;
                                        }
                                    else
                                        list.Add(ValueToString(value));

                                    if (list.Count > 0)
                                        dict.Add(property.Name, list);
                                }
                                else if (IsComplexType(propertyType))
                                {
                                    string sign = value.Imaginary >= 0 ? "+" : "";
                                    dict.Add(property.Name, $"{value.Real.ToString(CultureInfo.InvariantCulture)}{sign}{value.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                                }
                                else if (IsClassType(propertyType) || IsCustomStructType(value?.GetType()))
                                {
                                    dict.Add(property.Name, ConvertClassToDictionary(value, property.Name, visited));
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unknown property type [{myClass.GetType()}][{propertyType}] detected. Adding [{value}] to dict.", myClass, GPALObjectType.Other);
                                    dict.Add(property.Name, value);
                                }
                            }
                            else // IsTupleType: serialize tuple fields (Item1/Item2/...) as a keyed dict
                            {
                                dict.Add(property.Name, ConvertClassToDictionary(value, property.Name, visited));
                            }
                        }
                    }
                }

                if (fields != null)
                {
                    foreach (FieldInfo field in fields)
                    {
                        object value = field.GetValue(myClass);
                        if (true == IsCustomStructType(value?.GetType()) || value != null)
                        {
                            if (IsSimpleType(field.FieldType))
                            {
                                dict.Add(field.Name, ConvertValue(value, field.FieldType));
                            }
                            else if (IsDictionaryType(field.FieldType))
                            {
                                dict.Add(field.Name, ConvertClassToDictionary(value, field.Name, visited));
                            }
                            else if (IsEnumerableType(field.FieldType))
                            {
                                int idx = 0;
                                list = new List<dynamic>();
                                foreach (object item in (IEnumerable)value)
                                {
                                    Type itemType = item?.GetType() ?? typeof(object);
                                    list.Add(IsSimpleType(itemType) ? ConvertValue(item, itemType) : ConvertClassToDictionary(item, $"{itemType}{idx}", visited));
                                    idx++;
                                }
                                dict.Add(field.Name, list);
                            }
                            else
                            {
                                dict.Add(field.Name, ConvertClassToDictionary(value, field.Name, visited));
                            }
                        }
                    }
                }

                return dict;

            } // end try
            finally
            {
                if (addedToVisited)
                    visited.Remove(myClass);
            }
        }
        public static dynamic ConvertToClass(dynamic dictList, Type targetType, dynamic parentDictionary, List<string> columnNames)
        {

            Type createType = /*ConverterSettings.OutputClassElementType ??*/ GetConcreteType(targetType);
            dynamic myDictList = dictList;
            int dictListLength = 0;

            dictListLength = ObjectCopier.GetItemCount(null, dictList);

            if ((IsEnumerableObject(myDictList) || IsArray(myDictList)) && !IsDictionaryType(myDictList.GetType()) && 1 == dictListLength)
            {
                dynamic dictList2 = dictList[0];
                if (true == IsDictionaryType(dictList2.GetType()))
                {
                    myDictList = dictList2;
                    // dictListLength was the outer single-element wrapper's count (always 1 here) - recompute
                    // it for the unwrapped inner dictionary, or the nestedDictCount comparison below always
                    // fails against the stale outer count and silently takes the wrong branch.
                    dictListLength = ObjectCopier.GetItemCount(null, myDictList);
                }
            }

            if (true == IsDictionaryType(myDictList.GetType()))
            {
                int nestedDictCount = 0;

                foreach (var item in myDictList)
                {
                    if (true == IsDictionaryType(item?.Value?.GetType()))
                    {
                        nestedDictCount++;
                    }
                }

                // A one-row GPALKEY dict ("0000": {...}) has the same shape as a single-key wrapper (XML root, JSON
                // "root"). A row key is always numeric, so a single numeric key is a one-row list (e.g. a client with
                // one MatchedData record), not a wrapper.
                bool singleRowLooksIndexed = false;
                if (1 == dictListLength && 1 == nestedDictCount)
                {
                    singleRowLooksIndexed = true;
                    foreach (var soloRowItem in myDictList)
                    {
                        string soloRowKeyStr = soloRowItem.Key?.ToString();
                        if (false == int.TryParse(soloRowKeyStr, out int soloRowKeyDummy))
                        {
                            singleRowLooksIndexed = false;
                            break;
                        }
                    }
                }
                // Only a dictionary or collection target can be filled here; otherwise result[0] indexes an empty list.
                // A plain class goes to the single-object path below, including one whose members are all mappings,
                // which this count would read as rows.
                if (nestedDictCount == dictListLength && (1 < dictListLength || singleRowLooksIndexed)
                    && (true == IsDictionaryType(createType) || true == IsEnumerableType(createType)))  // all nested dictionaries, so iterate over them
                {
                    Type listType = typeof(List<>).MakeGenericType(createType);
                    IList result = (IList)InstantiateOne(listType);
                    // all nested dictionaries represent a collection of rows
                    // createType may be a collection type itself (List<T>, Queue<T>, Stack<T>, IGPALGrid<T>, etc.)
                    // We must instantiate the actual container (createType), populate it with the rows, then add that container to result
                    if (true == IsDictionaryType(createType))
                    {
                        IDictionary tmpDict = (IDictionary)InstantiateOne(createType);
                        Type dictKeyType = createType.GetGenericArguments()[0];
                        foreach (var item in myDictList)
                        {
                            try
                            {
                                dynamic element = ConvertToClass(item.Value, ConverterSettings.OutputClassElementType, parentDictionary, columnNames);
                                object resolvedKey = IsSimpleType(dictKeyType)
                                    ? ResolveSimpleDictionaryKey(item.Key, dictKeyType)
                                    : (item.Key is IDictionary keyDict ? CreateObjectFromDictionary(keyDict, dictKeyType, parentDictionary, null) : item.Key);
                                tmpDict[resolvedKey] = element;
                            }
                            catch
                            {
                                tmpDict = ConvertToClass(item.Value, createType, parentDictionary, columnNames);
                            }
                        }
                        result.Add(tmpDict);
                    }
                    else if (true == IsEnumerableType(createType))
                    {
                        object container = InstantiateOne(createType, dictListLength);

                        int index = 0;

                        // ConverterSettings.OutputClassElementType is only correct for the outermost SaveTo target. At
                        // deeper levels (each List<MatchedData> in Dictionary<Client, List<MatchedData>>) it still
                        // holds the outer type, so the row type comes from createType.
                        Type rowElementType = createType.IsArray
                            ? createType.GetElementType()
                            : (createType.IsGenericType && createType.GetGenericArguments().Length > 0 ? createType.GetGenericArguments()[0] : ConverterSettings.OutputClassElementType);

                        foreach (var item in myDictList)
                        {
                            object element = ConvertToClass(item.Value, rowElementType, parentDictionary, columnNames);

                            // Use your existing robust AddToList to add the row to the container
                            // It handles Add, Enqueue, Push, array indexing, dictionary, etc.
                            AddToList(container, element, index);

                            index++;
                        }

                        // Now add the fully populated container as the single item to result
                        // result is always List<createType>, so .Add is safe
                        result.Add(container);
                    }

                    if (1 < result.Count)
                        return result;
                    else
                        return result[0];
                }
                else
                {
                    // Unwrap single-key container dicts (XML root element, JSON "root" wrap).
                    // If the sole key has no matching property/field on the target type, step into its value.
                    dynamic unwrapped = myDictList;
                    if (myDictList.Count == 1)
                    {
                        foreach (var kvp in myDictList)
                        {
                            if (kvp.Value is IDictionary)
                            {
                                string key = kvp.Key?.ToString() ?? string.Empty;
                                bool isRealProperty =
                                    createType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                                    || createType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                        .Any(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase));
                                if (!isRealProperty)
                                    unwrapped = kvp.Value;
                            }
                            break;
                        }
                    }
                    return CreateObjectFromDictionary(unwrapped, createType, parentDictionary, columnNames);
                }
            }
            else if (IsEnumerableObject(myDictList) || IsArray(myDictList))
            {
                Type listType = typeof(List<>).MakeGenericType(createType);
                IList result = (IList)InstantiateOne(listType);
                dynamic createdType = InstantiateOne(createType);
                int count = 0;

                // It's a list or an array, so we handle its elements
                foreach (var item in myDictList)
                {
                    object element = CreateObjectFromDictionary(item, ConverterSettings.OutputClassElementType, parentDictionary, columnNames);
                    AddToList(createdType, element, count++);
                }

                result.Add(createdType);

                if (1 < result.Count)
                    return result;
                else
                    return result[0];
            }
            else
            {
                // It's a single dictionary, so we create an object
                return CreateObjectFromDictionary(myDictList, createType, parentDictionary, columnNames);
            }
        }

        static dynamic lastParentDictionary = null;
        static List<string> lastErrorMessage = new List<string>();
        static bool supressedMessage = false;
        [System.ThreadStatic]
        private static HashSet<object> _dictVisited;
        /// <summary>
        /// Iterate over the dictionary and create an object of targetType
        /// filling in the fields and/or properties from the dictionary
        /// the dictionary key is the field/property name to set with the value
        /// handles all known c# types
        /// </summary>
        /// <param name="dictionary">The dictionary to read the values from</param>
        /// <param name="targetType">The type of object to create</param>
        /// <param name="parentDictionary">used to track and suppress repeat messages converting the same object</param>
        /// <param name="columnNames">property/field names to map to if not the same in the source (eg, column names in a flat file)</param>
        /// <returns>The new object of targetType</returns>
        internal static object CreateObjectFromDictionary(dynamic dictionary, Type targetType, dynamic parentDictionary, List<string> columnNames)
        {
            // Cycle detection: YAML aliases resolve to the same object reference; track input
            // dictionaries/lists on the current call path and return a default when revisited.
            bool isRoot = _dictVisited == null;
            if (isRoot) _dictVisited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            bool addedToVisited = false;
            if (dictionary != null && !(targetType?.IsValueType == true))
            {
                object dictRef = (object)dictionary;
                if (!_dictVisited.Add(dictRef))
                    return InstantiateOne(targetType); // circular alias — return default
                addedToVisited = true;
            }

            dynamic objOrList = null;
            string kvpName = string.Empty;
            string kvpPropertyOrFieldName = string.Empty;
            dynamic kvpValue = null;
            PropertyInfo kvpProperty = null;
            FieldInfo kvpField = null;
            Type kvpPropertyType = null;
            Type priorKvpPropertyType = null;   // hack to get around json/yaml collapsing 1 item arrays into a single object
            Type kvpFieldType = null;
            object itemobj = null;
            int noPropertyOrField = 0;
            int count = 0;
            bool isEnumerable = IsEnumerableType(targetType);

            // XML parsers store every child as a List; for non-enumerable, non-array, non-dict targets
            // unwrap a single-item list to its inner dict so the property loop below can process it.
            if (dictionary is IList cofdSingleList && cofdSingleList.Count == 1 && cofdSingleList[0] is IDictionary cofdInnerDict
                && !isEnumerable && !targetType.IsArray && !IsDictionaryType(targetType))
            {
                dictionary = cofdInnerDict;
            }

            // Returns true if the parent container has changed (including becoming null or from null)
            bool HasParentChanged(dynamic lastParent, dynamic currentParent)
            {
                // If current is null, always treat as a change
                // (null is not a valid trackable container)
                if (currentParent == null)
                    return true;

                // If last was null but current is not, it's a change (we're entering a new container)
                if (lastParent == null)
                    return true;

                // Both non-null: if types differ, it's a different kind of container -> change
                if (lastParent.GetType() != currentParent.GetType())
                    return true;

                // Same type and both non-null: compare by reference
                // Different instance means the container object itself changed
                return !ReferenceEquals(lastParent, currentParent);
            }

            if (true == HasParentChanged(lastParentDictionary, parentDictionary))
            {
                lastParentDictionary = parentDictionary;
                lastErrorMessage.Clear();
                supressedMessage = false;
            }

            if (true == isEnumerable || targetType.IsArray)
                count = ObjectCopier.GetItemCount(null, dictionary);

            if (IsComplexType(targetType))
                return ComplexFromDict(dictionary);

            // When the target is object (e.g. Dictionary<string, object> value slot), preserve the
            // input dict as-is. Trying to map its keys to System.Object properties produces spurious
            // "No property/field" warnings and discards the data.
            if (targetType == typeof(object) && dictionary is IDictionary)
                return dictionary;

            try
            {
                objOrList = ConverterHelper.InstantiateOne(targetType, count);
            }
            catch
            {
                Type listType = typeof(List<>).MakeGenericType(targetType);
                objOrList = ActivatorHelper.CreateInstance(listType);
            }

            if (IsDictionaryType(targetType))
            {
                if (dictionary is IDictionary dictInput)
                {
                    Type keyType = targetType.GetGenericArguments()[0];
                    Type valueType = targetType.GetGenericArguments()[1];
                    IDictionary dict = (IDictionary)objOrList; // Cast to IDictionary for indexing
                    foreach (dynamic kvp in dictInput)
                    {
                        object key = IsSimpleType(keyType)
                            ? ResolveSimpleDictionaryKey(kvp.Key, keyType)
                            : (kvp.Key is IDictionary keyDict ? CreateObjectFromDictionary(keyDict, keyType, dictInput, null) : kvp.Key);
                        object value = kvp.Value?.ToString().Trim('"') ?? string.Empty; // CSV parsing keeps " as literal data, so it is removed here

                        try
                        {
                            // Check if kvp.Value is already of the target valueType
                            if (kvp.Value != null && valueType.IsAssignableFrom(kvp.Value.GetType()))
                            {
                                value = kvp.Value; // Use directly if already of correct type
                            }
                            // Handle dictionary input for class type
                            else if (kvp.Value is IDictionary && IsClassType(valueType) && valueType != typeof(string))
                            {
                                value = ConvertToClass(kvp.Value, valueType, dictInput, columnNames); // Map dictionary to class
                            }
                            // Handle dictionary input for dictionary type
                            else if (IsDictionaryType(valueType) && kvp.Value is IDictionary)
                            {
                                value = CreateObjectFromDictionary(kvp.Value, valueType, dictInput, columnNames); // Recursive dictionary conversion
                            }
                            // Handle simple types
                            else if (IsSimpleType(valueType) || IsNullableType(valueType))
                            {
                                value = ConvertValue(kvp.Value, valueType);
                            }
                            // Handle list/array values (e.g., Dictionary<string, List<T>>)
                            else if (IsEnumerableType(valueType) && valueType != typeof(string))
                            {
                                Type elemType = valueType.GetGenericArguments().Length > 0
                                    ? valueType.GetGenericArguments()[0]
                                    : valueType.GetElementType();
                                if (elemType != null)
                                {
                                    dynamic resultList = InstantiateOne(valueType);
                                    int i2 = 0;
                                    IEnumerable innerEnum = kvp.Value is IEnumerable enumer && !(kvp.Value is string)
                                        ? enumer
                                        : new object[] { kvp.Value };
                                    foreach (var innerItem in innerEnum)
                                    {
                                        if (innerItem is IDictionary innerDict && IsClassType(elemType) && elemType != typeof(string))
                                            AddToList(resultList, CreateObjectFromDictionary(innerDict, elemType, dictInput, columnNames), i2++);
                                        else if (IsSimpleType(elemType) || IsNullableType(elemType))
                                            AddToList(resultList, ConvertValue(innerItem, elemType), i2++);
                                        else if (IsBigIntegerType(elemType))
                                        {
                                            if (innerItem is string biNullStr2 && IsNullableType(elemType) && biNullStr2.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                                AddToList(resultList, null, i2++);
                                            else
                                            {
                                                try { AddToList(resultList, AsBigInteger(innerItem), i2++); }
                                                catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger format [{innerItem}] for element type [{elemType}]", innerItem, GPALObjectType.Other); }
                                            }
                                        }
                                        else
                                            AddToList(resultList, innerItem, i2++);
                                    }
                                    value = resultList;
                                }
                            }

                            dict[key] = value; // Assign to dictionary
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add key[{key}]-value[{value}] pair to dictionary [{targetType}]", kvp, GPALObjectType.Other, ex);
                        }
                    }
                    return dict;
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Expected IDictionary input for [{targetType}]", dictionary, GPALObjectType.None);
                }
            }

            // New case for Queue<T>, Stack<T>, Hashtable, and other enumerables
            else if (true == isEnumerable)
            {
                Type elementType = targetType.IsArray ? targetType.GetElementType() : (targetType.GetGenericArguments().Length > 0 ? targetType.GetGenericArguments()[0] : targetType);
                if (dictionary is IDictionary dictInput)
                {
                    bool isIndexed = dictInput.Keys.Cast<dynamic>().All(key => int.TryParse(key?.ToString(), out int dummy));
                    if (isIndexed)
                    {
                        dynamic keys = dictInput.Keys;

                        foreach (var key in keys)
                        {
                            try
                            {
                                object value = dictInput[key];
                                if (value is IDictionary classDict && IsClassType(elementType) && elementType != typeof(string))
                                {
                                    object classInstance = CreateObjectFromDictionary(classDict, elementType, keys, columnNames);
                                    if (classInstance != null)
                                    {
                                        AddToList(objOrList, classInstance, count++);
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ConvertToClass returned null for [{elementType}] with dict [{classDict}]", classDict, GPALObjectType.Other);
                                    }
                                }
                                else if (IsSimpleType(elementType) && (value == null || IsSimpleType(value.GetType())))
                                {
                                    // Handle simple types directly
                                    object convertedValue = ConvertValue(value, elementType);
                                    AddToList(objOrList, convertedValue, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Expected IDictionary for TestClass or simple type, got [{value?.GetType()}] for element type [{elementType}]", key, GPALObjectType.Other);
                                }
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add value to enumerable [{targetType}]", key, GPALObjectType.Other, ex);
                            }
                        }
                    }
                    else
                    {
                        foreach (dynamic kvp in dictionary)
                        {
                            {
                                try
                                {
                                    dynamic value = kvp.Value;
                                    if (value == null)
                                    {
                                        if (elementType.IsValueType && !IsNullableType(elementType))
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                        else
                                        {
                                            AddToList(objOrList, null, count++);
                                        }
                                    }
                                    else if (elementType.IsAssignableFrom(value.GetType()))
                                    {
                                        AddToList(objOrList, value, count++);
                                    }
                                    else if (IsCustomStructType(elementType))
                                    {
                                        // Handle structs not covered by other checks
                                        var converter = TypeDescriptor.GetConverter(elementType);
                                        if (converter != null && converter.CanConvertFrom(value.GetType()))
                                        {
                                            value = converter.ConvertFrom(value);
                                            AddToList(objOrList, value, count++);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                AddToList(objOrList, value, count++);
                                            }
                                            catch
                                            {
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported struct type [{value.GetType()}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                            }
                                        }
                                    }
                                    else if (value is IDictionary nestedDict)
                                    {
                                        if (IsClassType(elementType) && elementType != typeof(string))
                                        {
                                            bool nestedIsIndexed = nestedDict.Keys.Cast<dynamic>().All(k => int.TryParse(k?.ToString(), out int dummy));
                                            if (!nestedIsIndexed)
                                            {
                                                value = ConvertToClass(nestedDict, elementType, dictionary, columnNames);
                                                AddToList(objOrList, value, count++);
                                            }
                                        }
                                        else if (IsDictionaryType(elementType) || IsCustomStructType(elementType) || IsTupleType(elementType))
                                        {
                                            value = CreateObjectFromDictionary(nestedDict, elementType, dictionary, columnNames);
                                            AddToList(objOrList, value, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Dictionary value [{value.GetType()}] incompatible with element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsEnumerableType(elementType) && value is IEnumerable enumValue)
                                    {
                                        value = CreateObjectFromDictionary(enumValue, elementType, dictionary, columnNames);
                                        AddToList(objOrList, value, count++);
                                    }
                                    else if (IsSimpleType(elementType) || IsNullableType(elementType))
                                    {
                                        if (IsSimpleType(value.GetType()) || IsNullableType(value.GetType()))
                                        {
                                            value = ConvertValue(value, elementType);
                                            AddToList(objOrList, value, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{value.GetType()}] incompatible with simple/nullable element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsComplexType(elementType))
                                    {
                                        if (value is string complexStr)
                                        {
                                            value = ParseComplex(complexStr);
                                            AddToList(objOrList, value, count++);
                                        }
                                        else if (value is Complex complexValue)
                                        {
                                            AddToList(objOrList, complexValue, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{value.GetType()}] incompatible with complex/nullable element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsBigIntegerType(elementType))
                                    {
                                        if (value is string biNullStr && IsNullableType(elementType) && biNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            AddToList(objOrList, null, count++);
                                        else
                                        {
                                            try { AddToList(objOrList, AsBigInteger(value), count++); }
                                            catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger format [{value}] for element type [{elementType}]", kvp, GPALObjectType.Other); }
                                        }
                                    }
                                    else if (IsVersionType(elementType))
                                    {
                                        if (value is string versionStr && System.Version.TryParse(versionStr, out System.Version version))
                                        {
                                            AddToList(objOrList, version, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Version format [{value}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsIpAddressType(elementType))
                                    {
                                        if (value is string ipStr && IPAddress.TryParse(ipStr, out IPAddress ipAddress))
                                        {
                                            AddToList(objOrList, ipAddress, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid IPAddress format [{value}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsUriType(elementType))
                                    {
                                        if (value is string uriStr && Uri.TryCreate(uriStr, UriKind.Absolute, out Uri uri))
                                        {
                                            AddToList(objOrList, uri, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Uri format [{value}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsGuidType(elementType))
                                    {
                                        if (value is string guidStr && IsNullableType(elementType) && guidStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            AddToList(objOrList, null, count++);
                                        else if (value is string guidStr2 && Guid.TryParse(guidStr2, out Guid guid))
                                            AddToList(objOrList, guid, count++);
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Guid format [{value}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                    }
                                    else if (true == IsTimeSpanType(elementType))
                                    {
                                        if (value is string timeSpanStr && IsNullableType(elementType) && timeSpanStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            AddToList(objOrList, null, count++);
                                        else if (value is string timeSpanStr2 && TimeSpan.TryParse(timeSpanStr2, out TimeSpan timeSpan))
                                            AddToList(objOrList, timeSpan, count++);
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid TimeSpan format [{value}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                    }
                                    else if (IsClassType(elementType) && elementType != typeof(string))
                                    {
                                        // Handle non-dictionary class types (e.g., custom structs or classes)
                                        var converter = TypeDescriptor.GetConverter(elementType);
                                        if (converter != null && converter.CanConvertFrom(value.GetType()))
                                        {
                                            value = converter.ConvertFrom(value);
                                            AddToList(objOrList, value, count++);
                                        }
                                        else
                                        {
                                            AddToList(objOrList, value, count++);
                                            //GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported class type [{value.GetType()}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported value type [{value.GetType()}] for element type [{elementType}]", kvp, GPALObjectType.Other);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add value to enumerable [{targetType}]", kvp, GPALObjectType.Other, ex);
                                }
                            }
                        }
                    }
                }
                else if (dictionary is IEnumerable && !IsDictionaryType(dictionary.GetType()))
                {
                    IEnumerable enumInput = (IEnumerable)dictionary;
                    foreach (dynamic item in enumInput)
                    {
                        try
                        {
                            dynamic value = item;
                            if (value == null)
                            {
                                if (elementType.IsValueType && !IsNullableType(elementType))
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", item, GPALObjectType.Other);
                                }
                                else
                                {
                                    AddToList(objOrList, null, count++);
                                }
                            }
                            else if (elementType.IsAssignableFrom(value.GetType()))
                            {
                                AddToList(objOrList, value, count++);
                            }
                            else if (elementType.IsArray)
                            {
                                var arrayElementType = elementType.GetElementType();
                                var listType = typeof(List<>).MakeGenericType(arrayElementType);
                                var tempList = (IList)InstantiateOne(listType);
                                if (value is IEnumerable arrayValues)
                                {
                                    foreach (var arrayItem in arrayValues)
                                    {
                                        var convertedItem = CreateObjectFromDictionary(arrayItem, arrayElementType, enumInput, columnNames);
                                        tempList.Add(convertedItem);
                                    }
                                }
                                value = tempList.Cast<object>().ToArray();
                                AddToList(objOrList, value, count++);
                            }
                            else if (IsEnumerableType(elementType) && value is IEnumerable enumValue)
                            {
                                value = CreateObjectFromDictionary(enumValue, elementType, enumInput, columnNames);
                                AddToList(objOrList, value, count++);
                            }
                            else if (IsComplexType(elementType))
                            {
                                if (value is string complexStr)
                                {
                                    value = ParseComplex(complexStr);
                                    AddToList(objOrList, value, count++);
                                }
                                else if (value is Complex complexValue)
                                {
                                    AddToList(objOrList, complexValue, count++);
                                }
                                else if (value is IDictionary || (value is IEnumerable && !(value is string)))
                                {
                                    AddToList(objOrList, ComplexFromDict(value), count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{value.GetType()}] incompatible with complex/nullable element type [{elementType}]", value, GPALObjectType.Other);
                                }
                            }
                            else if (IsBigIntegerType(elementType))
                            {
                                if (value is string biNullStr4 && IsNullableType(elementType) && biNullStr4.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                    AddToList(objOrList, null, count++);
                                else
                                {
                                    try { AddToList(objOrList, AsBigInteger(value), count++); }
                                    catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger format [{value}] for element type [{elementType}]", item, GPALObjectType.Other); }
                                }
                            }
                            else if (IsVersionType(elementType))
                            {
                                if (value is string versionStr && System.Version.TryParse(versionStr, out System.Version version))
                                {
                                    AddToList(objOrList, version, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Version format [{value}] for element type [{elementType}]", item, GPALObjectType.Other);
                                }
                            }
                            else if (true == IsIpAddressType(elementType))
                            {
                                if (value is string ipStr && IPAddress.TryParse(ipStr, out IPAddress ipAddress))
                                {
                                    AddToList(objOrList, ipAddress, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid IPAddress format [{value}] for element type [{elementType}]", item, GPALObjectType.Other);
                                }
                            }
                            else if (true == IsUriType(elementType))
                            {
                                if (value is string uriStr && Uri.TryCreate(uriStr, UriKind.Absolute, out Uri uri))
                                {
                                    AddToList(objOrList, uri, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Uri format [{value}] for element type [{elementType}]", item, GPALObjectType.Other);
                                }
                            }
                            else if (true == IsGuidType(elementType))
                            {
                                if (value is string guidStr && IsNullableType(elementType) && guidStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                    AddToList(objOrList, null, count++);
                                else if (value is string guidStr2 && Guid.TryParse(guidStr2, out Guid guid))
                                    AddToList(objOrList, guid, count++);
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Guid format [{value}] for element type [{elementType}]", item, GPALObjectType.Other);
                            }
                            else if (true == IsTimeSpanType(elementType))
                            {
                                if (value is string timeSpanStr && IsNullableType(elementType) && timeSpanStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                    AddToList(objOrList, null, count++);
                                else if (value is string timeSpanStr2 && TimeSpan.TryParse(timeSpanStr2, out TimeSpan timeSpan))
                                    AddToList(objOrList, timeSpan, count++);
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid TimeSpan format [{value}] for element type [{elementType}]", item, GPALObjectType.Other);
                            }
                            else if (true == IsBooleanType(elementType))
                            {
                                if (value is string boolStr)
                                {
                                    if (IsNullableType(elementType) && boolStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                    {
                                        AddToList(objOrList, null, count++);
                                    }
                                    else if (bool.TryParse(boolStr.Trim(), out bool parsedBool))
                                    {
                                        AddToList(objOrList, parsedBool, count++);
                                    }
                                    else    // if we are mapping from a file to a class, then the file might be trying to map out these values as boolean
                                    {
                                        // Common aliases that bool.TryParse doesn't accept by default
                                        string lower = boolStr.Trim().ToLowerInvariant();
                                        if (lower == "1" || lower == "yes" || lower == "on" || lower == "true")
                                        {
                                            AddToList(objOrList, true, count++);
                                        }
                                        else if (lower == "0" || lower == "no" || lower == "off" || lower == "false")
                                        {
                                            AddToList(objOrList, false, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                                $"Invalid boolean format [{value}] for element type [{elementType}]",
                                                item, GPALObjectType.Other);
                                        }
                                    }
                                }
                                else if (value is bool directBool)
                                {
                                    AddToList(objOrList, directBool, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                        $"Invalid type for boolean: expected string or bool, got [{value?.GetType()}]",
                                        item, GPALObjectType.Other);
                                }
                            }
                            else if (IsSimpleType(elementType) || IsNullableType(elementType))
                            {
                                if (IsSimpleType(value.GetType()) || IsNullableType(value.GetType()))
                                {
                                    value = ConvertValue(value, elementType);
                                    AddToList(objOrList, value, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{value.GetType()}] incompatible with simple/nullable element type [{elementType}]", item, GPALObjectType.Other);
                                }
                            }
                            else if (IsClassType(elementType) && elementType != typeof(string))
                            {
                                var converter = TypeDescriptor.GetConverter(elementType);
                                if (converter != null && converter.CanConvertFrom(value.GetType()))
                                {
                                    value = converter.ConvertFrom(value);
                                    AddToList(objOrList, value, count++);
                                }
                                else
                                {
                                    try
                                    {
                                        AddToList(objOrList, value, count++);
                                    }
                                    catch
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported class type [{value.GetType()}] for element type [{elementType}]", item, GPALObjectType.Other);
                                    }
                                }
                            }
                            else if (IsCustomStructType(elementType))
                            {
                                // Unwrap single-item XML list wrapper if needed
                                if (value is IList svl2 && svl2.Count == 1 && svl2[0] is IDictionary svd2)
                                    value = svd2;
                                if (value is IDictionary svStructDict2)
                                {
                                    value = CreateObjectFromDictionary(svStructDict2, elementType, enumInput, columnNames);
                                    AddToList(objOrList, value, count++);
                                }
                                else
                                {
                                    var converter = TypeDescriptor.GetConverter(elementType);
                                    if (converter != null && converter.CanConvertFrom(value.GetType()))
                                    {
                                        value = converter.ConvertFrom(value);
                                        AddToList(objOrList, value, count++);
                                    }
                                    else if (false == IsSimpleType(value.GetType()))
                                    {
                                        try
                                        {
                                            AddToList(objOrList, value, count++);
                                        }
                                        catch
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported struct type [{value.GetType()}] for element type [{elementType}]", item, GPALObjectType.Other);
                                        }
                                    }
                                }
                            }
                            else if (value is IDictionary nestedDict)
                            {
                                if (IsClassType(elementType) && elementType != typeof(string))
                                {
                                    value = ConvertToClass(nestedDict, elementType, enumInput, columnNames);
                                    AddToList(objOrList, value, count++);
                                }
                                else if (IsDictionaryType(elementType) || IsTupleType(elementType))
                                {
                                    value = CreateObjectFromDictionary(nestedDict, elementType, enumInput, columnNames);
                                    AddToList(objOrList, value, count++);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Dictionary value [{value.GetType()}] incompatible with element type [{elementType}]", item, GPALObjectType.Other);
                                }
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported item type [{value?.GetType()}] for element type [{elementType}]", item, GPALObjectType.Other);
                            }
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add item to enumerable [{targetType}]", item, GPALObjectType.Other, ex);
                        }
                    }
                }
            }
            else if (!(dictionary is IDictionary inputDict))    // if we can't cast to dict for next step, we have something weird...
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Expected IDictionary input for [{targetType}]", dictionary, GPALObjectType.None);
            }
            else
            {
                int columnIdx = 0;
                foreach (dynamic kvp in inputDict)
                {
                    noPropertyOrField = 0;
                    priorKvpPropertyType = kvpPropertyType;

                    if (IsKeyValuePairType(kvp.GetType()))
                    {
                        if (null != columnNames && columnIdx < columnNames.Count)
                            kvpName = columnNames[columnIdx++].Trim('"');
                        else
                        {
                            string rawKey = kvp.Key?.ToString().Trim('"') ?? string.Empty;
                            if (rawKey.StartsWith("GPALKEY", StringComparison.Ordinal))
                                rawKey = rawKey.Substring(7);
                            else if (System.Text.RegularExpressions.Regex.IsMatch(rawKey, @"^GPAL\d+_"))
                                rawKey = System.Text.RegularExpressions.Regex.Replace(rawKey, @"^GPAL\d+_", "");
                            kvpName = rawKey;
                        }

                        if (typeof(string) == kvp.Value?.GetType())
                            kvpValue = kvp.Value?.ToString().Trim('"');
                        else
                            kvpValue = kvp.Value;

                        kvpProperty = targetType.GetProperty(kvpName)
                            ?? targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                         .FirstOrDefault(p => p.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase));

                        kvpField = targetType.GetField(kvpName)
                            ?? targetType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                         .FirstOrDefault(f => f.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase));

                        if (kvpProperty != null)
                        {
                            kvpPropertyType = kvpProperty.PropertyType;
                            kvpPropertyOrFieldName = kvpProperty.Name;
                        }
                        if (kvpField != null)
                        {
                            kvpFieldType = kvpField.FieldType;
                            kvpPropertyOrFieldName = kvpField.Name;
                        }
                    }
                    else
                    {
                        kvpValue = kvp;
                    }

                    if (kvpProperty == null && kvpField == null)
                    {
                        kvpPropertyType = targetType;
                        noPropertyOrField = 2;
                    }

                    if (kvpProperty != null && kvpProperty.CanWrite && kvpProperty.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsDictionaryType(kvpPropertyType))
                        {
                            if (kvpValue is IList kvpSingleWrap && kvpSingleWrap.Count == 1 && kvpSingleWrap[0] is IDictionary kvpUnwrappedInner)
                                kvpValue = kvpUnwrappedInner;
                            if (kvpValue == null || (kvpValue is string kvpDictNullStr && kvpDictNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)))
                            {
                                // null (C# null or XML "null" string) — leave dict property at default null
                            }
                            else if (kvpValue is IDictionary valueDict)
                            {
                                IDictionary propDict = (IDictionary)ConverterHelper.InstantiateOne(kvpPropertyType);
                                Type keyType = kvpPropertyType.GetGenericArguments()[0];
                                Type valueType = kvpPropertyType.GetGenericArguments()[1];

                                foreach (dynamic valueKvp in valueDict)
                                {
                                    try
                                    {
                                        object key = IsSimpleType(keyType)
                                            ? ResolveSimpleDictionaryKey(valueKvp.Key, keyType)
                                            : (valueKvp.Key is IDictionary valueKeyDict ? CreateObjectFromDictionary(valueKeyDict, keyType, valueDict, null) : valueKvp.Key);
                                        object value;

                                        if (IsKeyValuePairType(valueKvp.Value?.GetType()))
                                        {
                                            var nestedKvp = valueKvp.Value;
                                            value = IsDictionaryType(valueType)
                                                ? CreateObjectFromDictionary(new Dictionary<object, object> { { nestedKvp.Key, nestedKvp.Value } }, valueType, valueDict, columnNames)
                                                : ConvertValue(nestedKvp.Value, valueType, null, kvpName);
                                        }
                                        else if (IsDictionaryType(valueKvp.Value?.GetType()))
                                        {
                                            // If target is object or already assignable, preserve the dict as-is.
                                            // CreateObjectFromDictionary(dict, object) would try to map keys to
                                            // System.Object properties and fail.
                                            if (valueType == typeof(object) || valueType.IsAssignableFrom(valueKvp.Value.GetType()))
                                                value = valueKvp.Value;
                                            else
                                                value = CreateObjectFromDictionary(valueKvp.Value, valueType, valueDict, columnNames);
                                        }
                                        else
                                        {
                                            value = ConvertValue(valueKvp.Value, valueType, null, kvpName);
                                        }

                                        if (value == null || valueType.IsAssignableFrom(value.GetType()))
                                            propDict[key] = value;
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to convert [{DescribeForLog(value)}] to [{valueType.Name}] for [{kvpName}]", valueKvp, GPALObjectType.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to process key-value pair for [{kvpName}]", valueKvp, GPALObjectType.None, ex);
                                    }
                                }
                                if (objOrList.GetType().IsValueType)
                                {
                                    string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                    objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, propDict });
                                }
                                else
                                    kvpProperty.SetValue(objOrList, propDict);
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value for property [{kvpName}] is not a dictionary", kvpValue, GPALObjectType.None);
                            }
                        }
                        else if (IsEnumerableType(kvpPropertyType) || kvpPropertyType.IsArray)
                        {
                            Type elementType = kvpPropertyType.IsArray ? kvpPropertyType.GetElementType() : kvpPropertyType.GetGenericArguments()[0];
                            itemobj = ConverterHelper.InstantiateOne(elementType);

                            count = 0;
                            if (kvpValue is string kvpNullCollStr && kvpNullCollStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                            {
                                // "null" string from XML means the collection itself is null — leave property at default.
                            }
                            else if (kvpValue is IDictionary collapsedDict && !IsDictionaryType(elementType) && (IsClassType(elementType) || IsCustomStructType(elementType) || IsTupleType(elementType)))
                            {
                                // A JS array-like object (e.g. DOMRectList) serializes as {"0": {...}, "1": {...}};
                                // treat it as a list keyed by index. The case below is a single-element array YAML/JSON
                                // collapsed into a mapping.
                                bool looksLikeIndexedArray = collapsedDict.Keys.Cast<object>().All(k => int.TryParse(k?.ToString(), out _));
                                object innerList = ConverterHelper.InstantiateOne(kvpPropertyType, looksLikeIndexedArray ? collapsedDict.Count : 1);
                                if (looksLikeIndexedArray)
                                {
                                    int indexedIdx = 0;
                                    foreach (var indexedKey in collapsedDict.Keys)
                                    {
                                        object indexedElement = collapsedDict[indexedKey] is IDictionary indexedElementDict
                                            ? CreateObjectFromDictionary(indexedElementDict, elementType, parentDictionary, columnNames)
                                            : ConvertValue(collapsedDict[indexedKey], elementType);
                                        AddToList(innerList, indexedElement, indexedIdx++);
                                    }
                                }
                                else
                                {
                                    object element = CreateObjectFromDictionary(collapsedDict, elementType, parentDictionary, columnNames);
                                    AddToList(innerList, element, 0);
                                }
                                if (ObjectCopier.GetItemCount(kvpProperty, innerList) > 0)
                                {
                                    if (IsEnumerableObject(objOrList) || objOrList.GetType().IsArray)
                                        AddToList(objOrList, innerList, count++);
                                    else if (objOrList.GetType().IsValueType)
                                    {
                                        string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                        objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, innerList });
                                    }
                                    else
                                        kvpProperty.SetValue(objOrList, innerList);
                                }
                            }
                            else if (IsEnumerableObject(kvpValue) || IsArray(kvpValue))
                            {
                                // yaml and json have a nasty habit of collapsing arrays with one elemnt to a singular type, messes us up when we expect arrays not a singular value
                                // The element type comes from the target property's type: the data read from YAML or
                                // JSON is a List<object> whatever the target holds. Only a value that is itself a Type
                                // can answer for its own elements. Worked out without throwing, as a throw caught per
                                // value cost seconds under a debugger.
                                Type innerElementType;
                                Type valueAsType = (object)kvpValue as Type;

                                if (null != valueAsType && (true == valueAsType.IsArray || 0 < valueAsType.GetGenericArguments().Length))
                                    innerElementType = true == valueAsType.IsArray ? valueAsType.GetElementType() : valueAsType.GetGenericArguments()[0];
                                else if (null == elementType)
                                    innerElementType = null;
                                // For tuple element types their first generic arg is NOT the collection element
                                // type (it's just Item1's type), so leave innerElementType null.
                                else if (true == IsTupleType(elementType))
                                    innerElementType = null;
                                else
                                    innerElementType = 0 < elementType.GetGenericArguments().Length ? elementType.GetGenericArguments()[0] : elementType;

                                object innerList = ConverterHelper.InstantiateOne(kvpPropertyType, ObjectCopier.GetItemCount(kvpProperty, kvpValue));
                                foreach (dynamic kvpValueItem in (IEnumerable)kvpValue)
                                {
                                    Type kvpValueItemType = kvpValueItem.GetType();
                                    object element = null;

                                    if (false == IsKeyValuePairType(kvpValueItemType) && false == IsCustomStructType(kvpValueItemType) && kvpValueItem == null)
                                    {
                                        if (elementType.IsValueType && !IsNullableType(elementType))
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                        else
                                        {
                                            AddToList(innerList, null, count++);
                                        }
                                    }
                                    else if (elementType.IsAssignableFrom(kvpValueItem.GetType()))
                                    {
                                        AddToList(innerList, kvpValueItem, count++);
                                    }
                                    else if (kvpValueItem is IDictionary nestedDict)
                                    {
                                        if ((IsCustomStructType(elementType)  || IsClassType(elementType)) && elementType != typeof(string))
                                        {
                                            element = ConvertToClass(nestedDict, elementType, parentDictionary, columnNames);
                                            AddToList(innerList, element, count++);
                                        }
                                        else if (IsDictionaryType(kvpValueItem.GetType()) || IsDictionaryType(elementType) || true == IsEnumerableType(elementType))
                                        {
                                            element = CreateObjectFromDictionary(nestedDict, elementType, parentDictionary, columnNames);
                                            AddToList(innerList, element, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Dictionary value [{kvpValueItem.GetType()}] incompatible with element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsKeyValuePairType(kvpValueItemType))
                                    {
                                        var nestedKvp = kvpValueItem;
                                        Type valueType = elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                                            ? elementType.GetGenericArguments()[1]
                                            : elementType;

                                        if (IsDictionaryType(elementType))
                                        {
                                            element = CreateObjectFromDictionary(new Dictionary<object, object> { { nestedKvp.Key, nestedKvp.Value } }, elementType, parentDictionary, columnNames);
                                        }
                                        else if (IsComplexType(valueType))
                                        {
                                            element = ParseComplex(nestedKvp.Value?.ToString());
                                        }
                                        else if (IsSimpleType(valueType))
                                        {
                                            element = ConvertValue(nestedKvp.Value?.ToString(), valueType, null, kvpName);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                element = CreateObjectFromDictionary(nestedKvp.Value, elementType, parentDictionary, columnNames);
                                            }
                                            catch
                                            {
                                                element = nestedKvp.Value;
                                            }
                                        }
                                        AddToList(innerList, element, count++);
                                    }
                                    else if (IsTupleType(elementType) && kvpValueItem is IList tupleBundledList)
                                    {
                                        // The whole tuple sequence arrived as a single List<object> item.
                                        // Unpack each inner dict as one tuple element.
                                        foreach (dynamic tupleSubItem in tupleBundledList)
                                        {
                                            if (tupleSubItem is IDictionary tupleSubDict)
                                            {
                                                element = CreateObjectFromDictionary(tupleSubDict, elementType, parentDictionary, columnNames);
                                                AddToList(innerList, element, count++);
                                            }
                                        }
                                    }
                                    else if (IsComplexType(elementType) || (true == IsComplexType(innerElementType) && typeof(string) == kvpValueItemType))
                                    {
                                        if (kvpValueItem is string complexStr)
                                        {
                                            element = ParseComplex(complexStr);
                                            AddToList(innerList, element, count++);
                                        }
                                        else if (kvpValueItem is Complex complexValue)
                                        {
                                            AddToList(innerList, complexValue, count++);
                                        }
                                        else if (true == IsDictionaryType(kvpValueItemType) || true == IsEnumerableType(kvpValueItemType))  // json and yaml will collapse arrays of 1 into an object
                                        {
                                            // yaml and json have a nasty habit of collapsing arrays with one elemnt to a singular type, messes us up when we expect arrays not a singular value
                                            element = CreateObjectFromDictionary(kvpValueItem, elementType, kvpValue, columnNames);
                                            AddToList(innerList, element, count++);

                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Complex format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (((IsDictionaryType(kvpValueItemType) || IsEnumerableType(elementType) || elementType.IsArray)) && typeof(string) != kvpValueItemType)
                                    {
                                        if (IsEnumerableType(elementType) && !(kvpValueItem is IEnumerable))
                                        {
                                            // JSON collapses a single-item List<T> to a bare scalar; wrap it
                                            Type innerScalarType = elementType.IsGenericType ? elementType.GetGenericArguments()[0] : typeof(object);
                                            dynamic singleItemList = InstantiateOne(elementType);
                                            AddToList(singleItemList, ConvertValue(kvpValueItem, innerScalarType, null, kvpName), 0);
                                            element = singleItemList;
                                        }
                                        else
                                        {
                                            element = CreateObjectFromDictionary(kvpValueItem, elementType, parentDictionary, columnNames);
                                        }
                                        AddToList(innerList, element, count++);
                                    }
                                    else if (IsBigIntegerType(elementType) || IsBigIntegerType(innerElementType))
                                    {
                                        Type biNullableCheck2 = IsBigIntegerType(innerElementType) ? innerElementType : elementType;
                                        if (kvpValueItem is string biNullStr3 && IsNullableType(biNullableCheck2) && biNullStr3.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            AddToList(innerList, null, count++);
                                        else
                                        {
                                            try { AddToList(innerList, AsBigInteger(kvpValueItem), count++); }
                                            catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other); }
                                        }
                                    }
                                    else if (IsVersionType(elementType) || IsVersionType(innerElementType))
                                    {
                                        if (kvpValueItem is string versionStr && System.Version.TryParse(versionStr, out System.Version version))
                                        {
                                            AddToList(innerList, version, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Version format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsIpAddressType(elementType) || true == IsIpAddressType(innerElementType))
                                    {
                                        if (kvpValueItem is string ipStr && IPAddress.TryParse(ipStr, out IPAddress ipAddress))
                                        {
                                            AddToList(innerList, ipAddress, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid IPAddress format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsUriType(elementType) || true == IsUriType(innerElementType) )
                                    {
                                        if (kvpValueItem is string uriStr && Uri.TryCreate(uriStr, UriKind.Absolute, out Uri uri))
                                        {
                                            AddToList(innerList, uri, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Uri format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsGuidType(elementType) || true == IsGuidType(innerElementType))
                                    {
                                        Type gNullableCheck = IsGuidType(innerElementType) ? innerElementType : elementType;
                                        if (kvpValueItem is string guidNullStr && IsNullableType(gNullableCheck) && guidNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            AddToList(innerList, null, count++);
                                        else if (kvpValueItem is string guidStr && Guid.TryParse(guidStr, out Guid guid))
                                            AddToList(innerList, guid, count++);
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Guid format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                    }
                                    else if (true == IsTimeSpanType(elementType) || true == IsTimeSpanType(innerElementType))
                                    {
                                        Type tsNullableCheck = IsTimeSpanType(innerElementType) ? innerElementType : elementType;
                                        if (kvpValueItem is string tsNullStr && IsNullableType(tsNullableCheck) && tsNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            AddToList(innerList, null, count++);
                                        else if (kvpValueItem is string timeSpanStr && TimeSpan.TryParse(timeSpanStr, out TimeSpan timeSpan))
                                            AddToList(innerList, timeSpan, count++);
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid TimeSpan format [{kvpValueItem}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                    }

                                    else if (IsCustomStructType(elementType) || IsCustomStructType(innerElementType))
                                    {
                                        Type csElemType = IsCustomStructType(innerElementType) ? innerElementType : elementType;
                                        // Unwrap single-item XML list wrapper if needed
                                        dynamic csItem = kvpValueItem;
                                        if (csItem is IList csl3 && csl3.Count == 1 && csl3[0] is IDictionary csd3)
                                            csItem = csd3;
                                        if (csItem is IDictionary csStructDict3)
                                        {
                                            element = CreateObjectFromDictionary(csStructDict3, csElemType, parentDictionary, columnNames);
                                            AddToList(innerList, element, count++);
                                        }
                                        else
                                        {
                                            var converter = TypeDescriptor.GetConverter(csElemType);
                                            if (converter != null && converter.CanConvertFrom(csItem.GetType()))
                                            {
                                                element = converter.ConvertFrom(csItem);
                                                AddToList(innerList, element, count++);
                                            }
                                            else if (false == IsSimpleType(csItem.GetType()))
                                            {
                                                try
                                                {
                                                    AddToList(innerList, csItem, count++);
                                                }
                                                catch
                                                {
                                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported struct type [{csItem.GetType()}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                                }
                                            }
                                        }
                                    }
                                    else if (IsSimpleType(elementType) || IsNullableType(elementType))
                                    {
                                        if (IsSimpleType(kvpValueItem.GetType()) || IsNullableType(kvpValueItem.GetType()))
                                        {
                                            element = ConvertValue(kvpValueItem, elementType, null, kvpName);
                                            AddToList(innerList, element, count++);
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{kvpValueItem.GetType()}] incompatible with simple/nullable element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsClassType(elementType) && elementType != typeof(string))
                                    {
                                        var converter = TypeDescriptor.GetConverter(elementType);
                                        if (converter != null && converter.CanConvertFrom(kvpValueItem.GetType()))
                                        {
                                            element = converter.ConvertFrom(kvpValueItem);
                                            AddToList(innerList, element, count++);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                AddToList(innerList, kvpValueItem, count++);
                                            }
                                            catch
                                            {
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported class type [{kvpValueItem.GetType()}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported item type [{kvpValueItem?.GetType()}] for element type [{elementType}]", kvpValueItem, GPALObjectType.Other);
                                    }
                                }

                                if (ObjectCopier.GetItemCount(kvpProperty, innerList) > 0)
                                {
                                    if (IsEnumerableObject(objOrList) || objOrList.GetType().IsArray)
                                        AddToList(objOrList, innerList, count++);
                                    else if (objOrList.GetType().IsValueType)
                                    {
                                        string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                        objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, innerList });
                                    }
                                    else
                                        kvpProperty.SetValue(objOrList, innerList);
                                }
                                else
                                {
                                    string currentErrorMessage = $"No supplied values for [{kvpProperty?.Name}]";
                                    if (false == lastErrorMessage.Contains(currentErrorMessage))
                                    {
                                        lastErrorMessage.Add(currentErrorMessage);
                                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, currentErrorMessage, objOrList, GPALObjectType.Other);
                                        supressedMessage = false;
                                    }
                                    else if (false == supressedMessage)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", objOrList, GPALObjectType.Other);
                                        supressedMessage = true;
                                    }
                                }
                            }
                            else if (IsComplexType(elementType))
                            {
                                if (kvpValue != null)
                                {
                                    itemobj = ParseComplex(kvpValue.ToString());
                                    AddToList(objOrList, itemobj, count++);
                                }
                            }
                            else
                            {
                                // Scalar kvpValue for a collection property — SetValue handles wrapping it in a
                                // single-element list, including collapsed JSON/YAML (e.g. "gamma" -> List<string>{"gamma"}).
                                // Do NOT call AddToList(objOrList, ...) here: objOrList is the containing object,
                                // not the collection — EnsureCollectionType would mis-read its generic args.
                                SetValue(ref objOrList, kvpProperty, kvpValue, kvpPropertyType, parentDictionary, columnNames);
                            }
                        }
                        else if (IsCustomStructType(kvpPropertyType))
                        {
                            // Unwrap single-element XML list wrapper if needed
                            IDictionary structKvpDict = kvpValue as IDictionary;
                            if (structKvpDict == null && kvpValue is IList svl && svl.Count == 1 && svl[0] is IDictionary svd)
                                structKvpDict = svd;
                            if (structKvpDict != null)
                            {
                                object structObj = CreateObjectFromDictionary(structKvpDict, kvpPropertyType, parentDictionary, columnNames);
                                if (objOrList.GetType().IsValueType)
                                {
                                    string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                    objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, structObj });
                                }
                                else
                                    kvpProperty.SetValue(objOrList, structObj);
                            }
                        }
                        else if (IsComplexType(kvpPropertyType) || IsNullableType(kvpPropertyType) || IsSimpleType(kvpPropertyType))
                        {
                            SetValue(ref objOrList, kvpProperty, kvpValue, kvpPropertyType, parentDictionary, columnNames);
                        }
                        else if (kvpProperty.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase))
                        {
                            SetValue(ref objOrList, kvpProperty, kvpValue, kvpPropertyType, parentDictionary, columnNames);
                        }
                        else if (kvpValue != null && (IsDictionaryType(kvpValue.GetType()) || IsEnumerableType(kvpValue.GetType())))
                        {
                            objOrList = CreateObjectFromDictionary(kvpValue, targetType, kvpValue, columnNames);
                        }
                        else
                        {
                            noPropertyOrField++;
                        }
                    }
                    else if (kvpProperty != null && !kvpProperty.CanWrite && kvpProperty.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (false == targetType.Name.StartsWith("GPAL"))
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{targetType.Name}][{kvpName}] 'CanWrite' [false]", objOrList, GPALObjectType.Other);
                    }
                    else if (kvpField != null && kvpField.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsEnumerableType(kvpFieldType) || kvpFieldType.IsArray)
                        {
                            Type elementType = kvpFieldType.IsArray ? kvpFieldType.GetElementType() : kvpFieldType.GetGenericArguments()[0];
                            itemobj = ConverterHelper.InstantiateOne(elementType);

                            count = 0;
                            if (IsEnumerableObject(kvpValue) || IsArray(kvpValue))
                            {
                                object innerList = ConverterHelper.InstantiateOne(kvpFieldType, ObjectCopier.GetItemCount(kvpProperty, kvpValue));
                                foreach (dynamic kvpValueItem in (IEnumerable)kvpValue)
                                {
                                    Type kvpValueItemType = kvpValueItem.GetType();
                                    object element = null;

                                    if (IsKeyValuePairType(kvpValueItemType))
                                    {
                                        var nestedKvp = kvpValueItem;
                                        Type valueType = elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                                            ? elementType.GetGenericArguments()[1]
                                            : elementType;

                                        if (IsDictionaryType(elementType))
                                        {
                                            element = CreateObjectFromDictionary(new Dictionary<object, object> { { nestedKvp.Key, nestedKvp.Value } }, elementType, kvpValue, columnNames);
                                        }
                                        else if (IsComplexType(valueType))
                                        {
                                            element = ParseComplex(nestedKvp.Value?.ToString());
                                        }
                                        else if (IsSimpleType(valueType))
                                        {
                                            element = ConvertValue(kvp.Value?.ToString().Trim('"'), valueType);
                                        }
                                        else
                                        {
                                            element = nestedKvp.Value; // Use Value directly
                                        }
                                        AddToList(innerList, element, count++);
                                    }
                                    else if (IsDictionaryType(kvpValueItemType) || IsEnumerableType(elementType) || elementType.IsArray)
                                    {
                                        element = CreateObjectFromDictionary(kvpValueItem, elementType, kvpValue, columnNames);
                                        AddToList(innerList, element, count++);
                                    }
                                    else if (IsComplexType(elementType))
                                    {
                                        element = ParseComplex(kvpValueItem.ToString());
                                        AddToList(innerList, element, count++);
                                    }
                                    else if (IsSimpleType(elementType))
                                    {
                                        element = ConvertValue(kvpValueItem.ToString(), elementType);
                                        AddToList(innerList, Convert.ChangeType(element, elementType), count++);
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Skipping unexpected item type [{kvpValueItemType.Name}] for [{elementType.Name}]", kvpValueItem, GPALObjectType.None);
                                    }
                                }

                                if (ObjectCopier.GetItemCount(kvpProperty, innerList) > 0)
                                {
                                    if (IsEnumerableObject(objOrList) || objOrList.GetType().IsArray)
                                        AddToList(objOrList, innerList, count++);
                                    else if (objOrList.GetType().IsValueType)
                                        objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, kvpField.Name, (object)innerList });
                                    else
                                        kvpField.SetValue(objOrList, innerList);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No values for [{kvpField.Name}]", objOrList, GPALObjectType.Other);
                                }
                            }
                            else if (IsComplexType(kvpFieldType))
                            {
                                itemobj = ParseComplex(kvpValue.ToString());
                                AddToList(objOrList, itemobj, count++);
                            }
                            else if (IsSimpleType(kvpFieldType))
                            {
                                itemobj = ConvertValue(kvpValue.ToString(), kvpFieldType);
                                SetValue(ref objOrList, kvpField, itemobj, kvpFieldType, parentDictionary, columnNames);
                            }
                            else
                            {
                                SetValue(ref objOrList, kvpField, kvpValue, kvpFieldType, parentDictionary, columnNames);
                            }
                        }
                        else if (IsCustomStructType(kvpFieldType) && kvpValue is IDictionary)
                        {
                            object structObj = CreateObjectFromDictionary(kvpValue, kvpFieldType, parentDictionary, columnNames);
                            kvpField.SetValue(objOrList, structObj);
                        }
                        else if (IsComplexType(kvpFieldType) || IsNullableType(kvpFieldType) || IsSimpleType(kvpFieldType))
                        {
                            SetValue(ref objOrList, kvpField, kvpValue, kvpFieldType, parentDictionary, columnNames);
                        }
                        else if (kvpField.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase))
                        {
                            SetValue(ref objOrList, kvpField, kvpValue, kvpFieldType, parentDictionary, columnNames);
                        }
                        else
                        {
                            noPropertyOrField++;
                        }
                    }

                    // NOTE: dunno why i coded this, maybe an earlier (resolved bug) - instantiateone will do it's best, but maybe won't find the properties? iunno...
                    //if (objOrList.GetType() != targetType)
                    //{
                    //    if (kvpProperty != null && kvpProperty.CanWrite)
                    //    {
                    //        if (objOrList != null && (IsEnumerableObject(kvpProperty) || kvpPropertyType.IsArray) && ObjectCopier.GetItemCount(kvpProperty, objOrList) > 0)
                    //            kvpProperty.SetValue(objOrList, objOrList);
                    //        else if (itemobj != null)
                    //            kvpProperty.SetValue(objOrList, itemobj);
                    //    }
                    //    else if (kvpField != null)
                    //    {
                    //        if (objOrList != null && (IsEnumerableObject(kvpField) || kvpFieldType.IsArray) && ObjectCopier.GetItemCount(kvpField, objOrList) > 0)
                    //            kvpField.SetValue(objOrList, objOrList);
                    //        else if (itemobj != null)
                    //            kvpField.SetValue(objOrList, itemobj);
                    //    }
                    //}
                    //else 
                    if (noPropertyOrField == 2)
                    {
                        // can be excessive as ERROR for iterating over large collections that don't map
                        // we will assume the user knows these won't be there but only need to emit that once
                        string currentErrorMessage = $"No property/field [{kvpName}] in [{targetType.Name}]";
                        if (false == lastErrorMessage.Contains(currentErrorMessage))
                        {
                            lastErrorMessage.Add(currentErrorMessage);
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, currentErrorMessage, objOrList, GPALObjectType.Other);
                            supressedMessage = false;
                        }
                        else if (false == supressedMessage)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", objOrList, GPALObjectType.Other);
                            supressedMessage = true;
                        }
                    }
                }
            }

            if (addedToVisited) _dictVisited.Remove((object)dictionary);
            if (isRoot) _dictVisited = null;
            return objOrList;
        }
        private static readonly Dictionary<Type, MethodInfo> _setStructMethodCache = new Dictionary<Type, MethodInfo>();
        private static MethodInfo GetSetStructMethod(Type structType)
        {
            if (!_setStructMethodCache.TryGetValue(structType, out var m))
            {
                m = typeof(ConverterHelper)
                    .GetMethod("SetStructBackingField", BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(structType);
                _setStructMethodCache[structType] = m;
            }
            return m;
        }
        // Takes struct BY VALUE so __makeref points to the actual unboxed local — the only reliable
        // way to mutate a struct field via reflection in .NET 4.8 without boxing loss.
        // Returns the modified struct as object; caller assigns it back to the ref dynamic.
        // Works for both auto-property backing fields (<Name>k__BackingField, NonPublic)
        // and plain public fields (ValueTuple.Item1/Item2/..., Public).
        private static object SetStructBackingField<T>(T instance, string fieldName, object value) where T : struct
        {
            FieldInfo field = typeof(T).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                field.SetValueDirect(__makeref(instance), value);
            return instance;
        }
        private static bool IsList(object obj)
        {
            return obj is IList && obj.GetType().IsGenericType;
        }
        private static bool IsArray(object obj)
        {
            return obj != null && obj.GetType().IsArray;
        }
        // for structs - value types
        static object SetValueDirect(FieldInfo field, object instance, object value)
        {
            field.SetValueDirect(__makeref(instance), value);

            return instance;
        }

        /// <summary>
        /// Set the instance property or field with the supplied value converting it to the correct type
        /// </summary>
        /// <param name="instance">object to set values on</param>
        /// <param name="propertyOrField">property or field to set</param>
        /// <param name="propertyValue">the value to set</param>
        /// <param name="propertyType">the target type to convert propertyValue to</param>
        /// <param name="parentDictionary">used to track and suppress repeat messages converting the same object</param>
        /// <param name="columnNames">property//field names to map to if not the same in the source (eg, column names in a flat file)</param>
        // could be property or field
        internal static void SetValue(ref dynamic instance, dynamic propertyOrField, dynamic propertyValue, Type propertyType, dynamic parentDictionary, List<string> columnNames)
        {
            // "Recursion to [...]" is a placeholder written for shared/cyclic object references
            // (see ConvertClassToDictionary). It cannot be converted back into the original
            // (non-string) type, so leave the property/field at its default value.
            if (propertyValue is string recursionMarker && propertyType != typeof(string)
                && recursionMarker.StartsWith("Recursion to [") && recursionMarker.EndsWith("]"))
                return;

            // XML parsers wrap single child elements in a List<object>. When the target property
            // is a non-enumerable class/struct, unwrap the single-element list to the inner dict
            // so the IDictionary path below can reconstruct it properly.
            if (propertyValue is IList svSingleList && svSingleList.Count == 1
                && svSingleList[0] is IDictionary svInnerDict
                && !IsEnumerableType(propertyType) && !propertyType.IsArray && !IsDictionaryType(propertyType))
            {
                propertyValue = svInnerDict;
            }

            dynamic entryValue = null;
            string propertyTypeName = propertyType.Name;

            // XML serializes arrays/lists as <item>...</item> children.  StripGpalKeys may leave
            // the value as a single-item list wrapping a single-key dict, or directly as a
            // single-key dict.  Unwrap both patterns so the enumerable-handling code below sees
            // the actual sequence.
            if ((IsEnumerableType(propertyType) || propertyType.IsArray) && !IsDictionaryType(propertyType))
            {
                // Pattern: List<object>{ {item: [v1, v2, ...]} }
                if (propertyValue is IList pvList && pvList.Count == 1 && pvList[0] is IDictionary pvInner && pvInner.Count == 1)
                {
                    foreach (DictionaryEntry xw in pvInner)
                    {
                        propertyValue = xw.Value is IList ? xw.Value : new List<object> { xw.Value };
                        break;
                    }
                }
                // Pattern: {item: [v1, v2, ...]}
                else if (propertyValue is IDictionary xmlWrapperDict && xmlWrapperDict.Count == 1)
                {
                    foreach (DictionaryEntry xw in xmlWrapperDict)
                    {
                        propertyValue = xw.Value is IList ? xw.Value : new List<object> { xw.Value };
                        break;
                    }
                }
            }

            if (typeof(IPAddress) == propertyType)
            {
                if (propertyValue is IDictionary ipDict)
                {
                    foreach (var v in ipDict.Values) { entryValue = v; break; }
                }
                else
                    entryValue = propertyValue?.ToString();
            }
            else if (IsTupleType(propertyType) && propertyValue is IDictionary)
            {
                // Tuple property supplied as a nested dict — reconstruct then set directly.
                // Bypasses ValueToString, which expects a real tuple value, not a dict.
                object tupleValue = CreateObjectFromDictionary(propertyValue, propertyType, parentDictionary, columnNames);
                if (IsValueType(instance.GetType()))
                {
                    if (propertyOrField is PropertyInfo tupleProp)
                    {
                        string bfName = $"<{tupleProp.Name}>k__BackingField";
                        instance = GetSetStructMethod(instance.GetType()).Invoke(null, new[] { instance, bfName, tupleValue });
                    }
                    else if (propertyOrField is FieldInfo tupleField)
                        instance = GetSetStructMethod(instance.GetType()).Invoke(null, new[] { instance, tupleField.Name, tupleValue });
                }
                else
                    propertyOrField.SetValue(instance, tupleValue);
                return;
            }
            else if (IsComplexType(propertyType) && propertyValue is IEnumerable && !(propertyValue is string))
            {
                // Complex supplied as a dict/list from XML — extract Real/Imaginary directly.
                // Bypasses ValueToString (expects a real Complex) and CreateObjectFromDictionary
                // (fails on XML list wrappers and string-typed values from XML text nodes).
                propertyOrField.SetValue(instance, ComplexFromDict(propertyValue));
                return;
            }
            else
                entryValue = ValueToString(propertyValue, propertyType);

            if (true == IsValueType(instance.GetType()))
            {
                // Cycle sentinel: {"AnyKey": "Recursion to [...]"} — produced by ConvertClassToDictionary when a
                // cycle is detected. Converting the whole dict to the target type would cause COFD to report
                // "No property/field [AnyKey]" on the target. Collapse it to null here.
                if (propertyValue is IDictionary svCycleD && svCycleD.Count == 1 && !IsDictionaryType(propertyType))
                {
                    foreach (DictionaryEntry svce in svCycleD)
                    {
                        if (svce.Value is string svceStr && svceStr.StartsWith("Recursion to ["))
                        {
                            propertyValue = null;
                            entryValue = null;
                        }
                        break;
                    }
                }

                if (propertyOrField is FieldInfo)
                {
                    // ValueTuple uses public fields (Item1/Item2/...); auto-properties use NonPublic backing fields.
                    // SetStructBackingField<T> searches both, so route all struct field sets through it.
                    object converted;
                    if (propertyValue is IDictionary && !IsSimpleType(propertyType))
                        converted = CreateObjectFromDictionary(propertyValue, propertyType, null, null);
                    else
                        converted = ConvertValue(entryValue, propertyType);
                    instance = GetSetStructMethod(instance.GetType()).Invoke(null, new[] { instance, ((FieldInfo)propertyOrField).Name, converted });
                }
                else
                {
                    // PropertyInfo.SetValue on a boxed struct (NET 4.8) silently discards the result —
                    // it sets on a copy, not the box. Go through the compiler-generated backing field,
                    // which FieldInfo.SetValue correctly writes in-place on the box.
                    object converted;
                    if (propertyValue is IDictionary && !IsSimpleType(propertyType))
                        converted = CreateObjectFromDictionary(propertyValue, propertyType, null, null);
                    else
                        converted = ConvertValue(entryValue, propertyType);
                    string backingName = $"<{propertyOrField.Name}>k__BackingField";
                    FieldInfo bf = instance.GetType().GetField(backingName, BindingFlags.NonPublic | BindingFlags.Instance);
                    if (bf != null)
                        // Generic helper unboxes to a real local so __makeref is valid; returns updated struct.
                        // Assigning back to ref dynamic propagates to objOrList in CreateObjectFromDictionary.
                        instance = GetSetStructMethod(instance.GetType()).Invoke(null, new[] { instance, backingName, converted });
                    else
                        propertyOrField.SetValue(instance, converted);
                }
            }
            else if (true == IsVersionType(propertyType))   // must be before simple check
            {
                propertyOrField.SetValue(instance, ConvertValue(propertyValue, propertyType));
            }
            else if (IsSimpleType(propertyType) || IsEnumType(propertyType))  // <-- added enum support for scalar properties
            {
                if (null != propertyOrField)
                {
                    if (typeof(object) == instance.GetType())
                        instance = $"{propertyOrField}{ConverterSettings.OutDelimiter}{propertyValue}";
                    else
                    {
                        try
                        {
                            object convertedValue = ConvertValue(entryValue, propertyType);
                            propertyOrField.SetValue(instance, convertedValue);
                        }
                        catch
                        {
                            // Fallback: just set raw value (useful for dynamic/expando objects)
                            propertyOrField.SetValue(instance, propertyValue);
                        }
                    }
                }
                else if (typeof(string) == instance?.GetType())
                    instance = propertyValue;
                else
                    instance = propertyValue;   // instance is prolly dynamic
            }
            else if (true == IsBigIntegerType(propertyType))
            {
                BigInteger bigInteger;
                if (true == BigInteger.TryParse(entryValue, out bigInteger))
                    propertyOrField.SetValue(instance, bigInteger);
            }
            else if (true == IsComplexType(propertyType))
            {
                dynamic complex = ParseComplex(entryValue);
                if (null != complex)
                    propertyOrField.SetValue(instance, complex);
            }
            else if (true == IsNullableType(propertyType))
            {
                Type underlyingType = Nullable.GetUnderlyingType(propertyType);
                dynamic underlyingValueObject = InstantiateOne(underlyingType);
                SetValue(ref instance, propertyOrField, entryValue, underlyingType, parentDictionary, columnNames);
            }
            else if (IsDictionaryType(propertyType))
            {
                object value = CreateObjectFromDictionary(propertyValue, propertyType, parentDictionary, columnNames);
                propertyOrField.SetValue(instance, value);
            }
            else if (true == IsEnumerableType(propertyType) || true == propertyType.IsArray)
            {
                dynamic enumeration = null;

                // can't add null to a list
                if (null != propertyValue)
                {
                    // "null" string from XML means the collection is null — leave at default.
                    if (propertyValue is string svNullEnumStr && svNullEnumStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                        enumeration = null;
                    // strings implement IEnumerable<char>; dicts implement IEnumerable<KVP> —
                    // neither should be iterated as a sequence of collection elements.
                    else if ((true == IsEnumerableObject(propertyValue) && !(propertyValue is string) && !IsDictionaryType(propertyValue.GetType())) || true == IsArray(propertyValue))
                        enumeration = (IEnumerable)propertyValue;
                    else
                    {
                        Type _elemType = propertyType.GetElementType()
                            ?? (propertyType.IsGenericType ? propertyType.GetGenericArguments()[0] : null);
                        bool _compatible = _elemType == null
                            || _elemType.IsAssignableFrom(propertyValue.GetType())
                            || IsSimpleType(_elemType)
                            || IsDictionaryType(propertyValue.GetType());

                        if (_compatible)
                        {
                            // If the scalar is a dict and the element type is a class/struct, convert it
                            // before adding — don't add the raw dictionary to List<SomeType>.
                            object _singleItem = (_elemType != null && propertyValue is IDictionary && !IsDictionaryType(_elemType))
                                ? CreateObjectFromDictionary(propertyValue, _elemType, parentDictionary, columnNames)
                                : (object)propertyValue;
                            enumeration = InstantiateOne(propertyType, 1);
                            // Use AddToList (reflection-based) rather than dynamic .Add() — DLR fails to resolve
                            // List<StructType>.Add(StructType) when _singleItem is typed as object (boxed struct).
                            if (true == IsEnumerableObject(enumeration) && false == IsArray(enumeration))
                                AddToList(enumeration, _singleItem, 0);
                            else if (true == IsArray(enumeration))
                                enumeration[0] = _singleItem;
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Scalar [{propertyValue?.GetType()}] is not compatible with collection element type [{_elemType}]. Leaving [{propertyType}] at default.", propertyValue, GPALObjectType.Other);
                            enumeration = InstantiateOne(propertyType, 0);
                        }
                    }

                    if (enumeration == null)
                        enumeration = InstantiateOne(propertyType, 0);

                    int count = 0;
                    foreach (dynamic item in enumeration)
                        count++;

                    dynamic list = InstantiateOne(propertyType, count);

                    count = 0;
                    foreach (dynamic item in enumeration)
                    {
                        if (false == IsCustomStructType(item?.GetType()) && null == item)
                        {
                            Type _elemType = propertyType.GetElementType() ?? (propertyType.IsGenericType ? propertyType.GetGenericArguments()[0] : null);
                            bool _canBeNull = _elemType == null || !_elemType.IsValueType || IsNullableType(_elemType);
                            if (_canBeNull)
                                AddToList(list, null, count++);
                            continue;
                        }

                        dynamic itemValue = item;
                        dynamic itemKey = null;
                        Type itemType = item.GetType();
                        Type objectElementType = propertyType.GetElementType();
                        if (null == objectElementType)
                            objectElementType = propertyType.GetGenericArguments()[0];

                        if (true == IsKeyValuePairType(itemType))
                        {
                            itemKey = item.Key;
                            itemValue = item.Value;
                            itemType = item.Value.GetType();
                        }

                        if (true == IsComplexType(objectElementType))
                        {
                            dynamic complex = ParseComplex(itemValue.ToString());
                            if (null != complex)
                                AddToList(list, complex, count++);
                        }
                        else if (true == IsSimpleType(objectElementType) || IsEnumType(objectElementType))  // <-- enum support for collection elements
                        {
                            dynamic tempVal = ConvertValue(itemValue.ToString(), objectElementType, null, propertyOrField?.Name);
                            AddToList(list, tempVal, count++);
                        }
                        else if (IsEnumerableType(objectElementType))
                        {
                            dynamic myList = InstantiateOne(objectElementType);
                            Type elementType = objectElementType.GenericTypeArguments[0];
                            int innerCount = 0;

                            // Unwrap XML item-wrapper: single-item-list wrapping a single-key dict,
                            // or a bare single-key dict — both are {item:[...]} artifacts from XML.
                            dynamic resolvedItemValue = itemValue;
                            if (resolvedItemValue is IList rilvSingle && rilvSingle.Count == 1 && rilvSingle[0] is IDictionary rilvInner && rilvInner.Count == 1)
                            {
                                foreach (DictionaryEntry rkv in rilvInner) { resolvedItemValue = rkv.Value is IList ? rkv.Value : new List<object> { rkv.Value }; break; }
                            }
                            else if (resolvedItemValue is IDictionary rilvDict && rilvDict.Count == 1)
                            {
                                foreach (DictionaryEntry rkv in rilvDict) { resolvedItemValue = rkv.Value is IList ? rkv.Value : new List<object> { rkv.Value }; break; }
                            }

                            // A scalar (e.g. JSON long) that is not IEnumerable must be wrapped before the
                            // inner iteration; casting a non-IEnumerable to IEnumerable<dynamic> throws.
                            if (resolvedItemValue != null && !(resolvedItemValue is IEnumerable))
                            {
                                resolvedItemValue = new List<object> { resolvedItemValue };
                            }

                            if (true == IsComplexType(elementType))
                            {
                                Complex complex;

                                foreach (object item2 in (IEnumerable<dynamic>)resolvedItemValue)
                                {
                                    dynamic iv2 = item2;
                                    if (iv2 is IList il2 && ((IList)il2).Count == 1) iv2 = ((IList)il2)[0];
                                    complex = ParseComplex(iv2?.ToString());
                                    if (null != complex)
                                        AddToList(myList, complex, innerCount++);
                                }
                            }
                            else if (IsSimpleType(elementType) || IsEnumType(elementType) || IsBigIntegerType(elementType))
                            {
                                foreach (object item2 in (IEnumerable<dynamic>)resolvedItemValue)
                                {
                                    dynamic iv2 = item2;
                                    if (iv2 is IList il2 && ((IList)il2).Count == 1) iv2 = ((IList)il2)[0];
                                    dynamic convItem = IsBigIntegerType(elementType)
                                        ? System.Numerics.BigInteger.Parse(iv2?.ToString())
                                        : iv2;
                                    AddToList(myList, convItem, innerCount++);
                                }
                            }
                            list[count++] = myList;
                        }
                        else if (itemValue is IDictionary itemDictValue && !IsDictionaryType(objectElementType))
                        {
                            // class/struct/tuple element — deserialize the dict into the target type
                            dynamic converted = CreateObjectFromDictionary(itemDictValue, objectElementType, parentDictionary, columnNames);
                            AddToList(list, converted, count++);
                        }
                        else
                        {
                            list[count++] = itemValue;  // fallback – use raw value
                        }
                    }
                    if (0 < count)
                        propertyOrField.SetValue(instance, list);
                }
            }
            else if (true == IsClassType(propertyType))
            {
                dynamic value = null;
                Dictionary<dynamic, dynamic> myDict = new Dictionary<dynamic, dynamic>();
                try
                {
                    if (null != propertyValue)
                        foreach (dynamic item in propertyValue)
                            if (true == IsDictionaryType(item.GetType()))
                            {
                                myDict = item;
                                value = ConvertToClass(new List<Dictionary<dynamic, dynamic>>() { myDict }, propertyType, parentDictionary, columnNames);
                            }
                            else
                                myDict[item.Key] = item.Value;

                    // No input for this member means null, so "parent: null" reads back as null.
                    if (null == value && null != propertyValue)
                        value = ConvertToClass(new List<Dictionary<dynamic, dynamic>>() { myDict }, propertyType, parentDictionary, columnNames);
                }
                catch (Exception)
                {
                    try
                    {
                        IDictionary catchDict = propertyValue as IDictionary;
                        if (catchDict == null && propertyValue is IList pvCatchList)
                        {
                            foreach (var pvItem in pvCatchList)
                                if (pvItem is IDictionary d) { catchDict = d; break; }
                        }
                        if (catchDict != null)
                            value = ConvertToClass(new List<Dictionary<dynamic, dynamic>>() { (Dictionary<dynamic, dynamic>)(object)catchDict }, propertyType, parentDictionary, columnNames);
                    }
                    catch { }
                }
                propertyOrField.SetValue(instance, value);
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unknown object type [{propertyType}]", propertyValue, GPALObjectType.Other);
            }
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

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                return false; // KeyValuePair is NOT simple — we need to recurse into it
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
                       type == typeof(DateTimeOffset)
                       ;
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
        // A List converted to a dictionary synthesizes GPALKEY-prefixed sequential keys internally to
        // stand in for list position - that's an internal sentinel, not a value meant to reach the final
        // key. Strip it and hand the plain index to ConvertValue so the target key type decides its own
        // representation (a genuine int for an int/object key, an unpadded "0"/"1"/... for a string key).
        internal static object ResolveSimpleDictionaryKey(dynamic rawKey, Type keyType)
        {
            string rawKeyStr = rawKey as string;
            if (rawKeyStr != null && rawKeyStr.StartsWith("GPALKEY", StringComparison.Ordinal) && int.TryParse(rawKeyStr.Substring(7), out int gpalKeyIdx))
                return ConvertValue(gpalKeyIdx, keyType);

            if (rawKey != null && keyType.IsAssignableFrom(((object)rawKey).GetType()))
                return (object)rawKey;

            return ConvertValue(rawKey?.ToString(), keyType);
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
        /// Walks an object graph once to count nodes and detect a cycle, stopping at <paramref name="limit"/> nodes or
        /// the first cycle.
        /// </summary>
        /// <remarks>
        /// Stops at limit nodes or the first cycle, so cost does not grow with the payload. Cycles are judged by
        /// reference identity, not Equals.
        /// </remarks>
        /// <param name="root">The graph to walk</param>
        /// <param name="limit">Node count to stop at. Zero or less walks the whole graph and only checks for a cycle</param>
        /// <param name="cyclic">Set to true when a node appears twice on one path</param>
        /// <returns>Number of nodes counted, never more than <paramref name="limit"/></returns>
        public static int CountGraphNodes(object root, int limit, out bool cyclic)
        {
            int count = 0;
            bool foundCycle = false;

            CountGraphNodesRec(root, limit, ref count, ref foundCycle, new HashSet<object>(ReferenceComparer.Instance));

            cyclic = foundCycle;

            return count;
        }

        /// <summary>
        /// Counts nodes in an object graph, stopping at <paramref name="limit"/>.
        /// </summary>
        /// <param name="root">The graph to walk</param>
        /// <param name="limit">Node count to stop at</param>
        /// <returns>Nodes counted</returns>
        public static int CountGraphNodes(object root, int limit)
        {
            return CountGraphNodes(root, limit, out bool _);
        }

        private static void CountGraphNodesRec(object node, int limit, ref int count, ref bool cyclic, HashSet<object> onPath)
        {
            if (true == cyclic || null == node || (0 < limit && count >= limit))
                return;

            Type nodeType = node.GetType();

            // Only reference types can form a cycle; structs are copied and strings may be interned.
            bool tracked = false == nodeType.IsValueType && false == (node is string);

            if (true == tracked && false == onPath.Add(node))
            {
                cyclic = true;
                return;
            }

            count++;

            if (0 >= limit || count < limit)
            {
                if (node is IDictionary dictionary)
                {
                    foreach (object value in dictionary.Values)
                    {
                        CountGraphNodesRec(value, limit, ref count, ref cyclic, onPath);

                        if (true == cyclic || (0 < limit && count >= limit))
                            break;
                    }
                }
                else if (node is IEnumerable enumerable)
                {
                    foreach (object value in enumerable)
                    {
                        CountGraphNodesRec(value, limit, ref count, ref cyclic, onPath);

                        if (true == cyclic || (0 < limit && count >= limit))
                            break;
                    }
                }
                else
                {
                    foreach (MemberInfo member in CyclableMembers(nodeType))
                    {
                        CountGraphNodesRec(MemberValue(member, node), limit, ref count, ref cyclic, onPath);

                        if (true == cyclic || (0 < limit && count >= limit))
                            break;
                    }
                }
            }

            // Remove the node from the path when leaving it. A node reached from separate branches is shared, not a
            // cycle.
            if (true == tracked)
                onPath.Remove(node);
        }

        /// <summary>
        /// Returns a type's readable properties and fields that can lead to a cycle. Cached per type.
        /// </summary>
        /// <param name="type">Type to inspect</param>
        /// <returns>The members to walk; empty for a type that cannot be part of a cycle</returns>
        private static MemberInfo[] CyclableMembers(Type type)
        {
            return graphMembers.GetOrAdd(type, describing =>
            {
                List<MemberInfo> worthWalking = new List<MemberInfo>();

                foreach (PropertyInfo property in describing.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    if (true == property.CanRead && 0 == property.GetIndexParameters().Length && true == CanTakePartInACycle(property.PropertyType))
                        worthWalking.Add(property);

                foreach (FieldInfo field in describing.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (true == CanTakePartInACycle(field.FieldType))
                        worthWalking.Add(field);

                return worthWalking.ToArray();
            });
        }

        /// <summary>
        /// Returns true if a value of this type can be part of a cycle, so large flat collections are not walked.
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>False for a type that cannot be part of a cycle</returns>
        private static bool CanTakePartInACycle(Type type)
        {
            return false == (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(DateTime)
                || type == typeof(DateTimeOffset) || type == typeof(decimal) || type == typeof(Guid)
                || type == typeof(TimeSpan) || type == typeof(Uri));
        }

        /// <summary>
        /// Reads one member's value; null if the getter throws.
        /// </summary>
        /// <param name="member">Property or field to read</param>
        /// <param name="node">Instance to read it from</param>
        /// <returns>The value, or null when it could not be read</returns>
        private static object MemberValue(MemberInfo member, object node)
        {
            object retVal = null;

            try
            {
                retVal = member is PropertyInfo property ? property.GetValue(node) : ((FieldInfo)member).GetValue(node);
            }
            catch (Exception)
            {
                // a getter that throws is the type's business, not the walk's
            }

            return retVal;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MemberInfo[]> graphMembers
            = new System.Collections.Concurrent.ConcurrentDictionary<Type, MemberInfo[]>();

        /// <summary>
        /// Reference-identity comparer for the set that tracks the current path.
        /// </summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object of)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(of);
            }
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
        // Method to convert a dictionary to an XmlElement recursively
        public static void ConvertDictionaryToXmlElement(
            dynamic obj,
            XmlElement parent,
            bool emitWrapper = true,
            bool isTopLevel = true,
            HashSet<object> visited = null)   // cycle detection for reference types
        {
            if (visited == null) visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

            XmlDocument doc = parent.OwnerDocument;

            // Top-level emit = false: clear document and let first element become parent
            XmlElement currentParent = parent;
            if (!emitWrapper && isTopLevel)
            {
                doc.RemoveAll();
                currentParent = null; // no parent yet
            }

            Type objType = obj?.GetType();
            bool isDictionary = obj is IDictionary;
            bool isEnumerable = IsEnumerableType(objType) && !(obj is string);
            bool isSimple = IsSimpleType(objType);
            bool isTuple = IsTupleType(objType);

            // Detect cycles in reference types before recursing; exclude string — it's immutable,
            // commonly interned, and can never form a real cycle
            if (objType != null && !objType.IsValueType && objType != typeof(string) && (object)obj != null)
            {
                if (!visited.Add((object)obj))
                {
                    if (currentParent != null)
                    {
                        var bt = objType.Name.IndexOf('`');
                        var cycleName = bt >= 0 ? objType.Name.Substring(0, bt) : objType.Name;
                        currentParent.InnerText = $"Recursion to [{cycleName}]";
                    }
                    return;
                }
            }

            // Handle dictionary attributes and #text
            if (isDictionary)
            {
                dynamic dict = obj;
                foreach (var kvp in dict)
                {
                    string key = kvp.Key.ToString();
                    if (key.StartsWith("@GPAL_"))
                    {
                        // Assign attributes to currentParent if it exists, otherwise will be assigned after first element
                        if (currentParent != null)
                            currentParent.SetAttribute(key.Substring(6), ValueToString(kvp.Value));
                    }
                }
                foreach (var kvp in dict)
                {
                    string key = kvp.Key.ToString();
                    if (key == "#text" && currentParent != null)
                    {
                        currentParent.InnerText = ValueToString(kvp.Value);
                    }
                }
            }

            // Main processing will always start with a dictionary
            if (isDictionary)
            {
                dynamic dict = obj;
                foreach (var kvp in dict)
                {
                    // A key that is itself an object cannot be an element name; it became the key's type name. Written
                    // as an item holding a Key and a Value, as JSON writes it.
                    object rawKey = kvp.Key;
                    if (null != rawKey && false == IsSimpleType(rawKey.GetType()))
                    {
                        XmlElement pair = doc.CreateElement("item");

                        if (currentParent == null)
                        {
                            currentParent = pair;
                            doc.AppendChild(pair);
                        }
                        else
                            currentParent.AppendChild(pair);

                        XmlElement keyElement = doc.CreateElement("Key");
                        pair.AppendChild(keyElement);
                        ConvertDictionaryToXmlElement(rawKey, keyElement, emitWrapper, false, visited);

                        XmlElement valueElement = doc.CreateElement("Value");
                        pair.AppendChild(valueElement);
                        ConvertDictionaryToXmlElement(kvp.Value, valueElement, emitWrapper, false, visited);

                        continue;
                    }

                    string key = kvp.Key.ToString();
                    if (key.StartsWith("@") || key == "#text") continue;
                    if (key.StartsWith("GPALKEY")) key = "row";
                    // ConvertXmlNodeToDictionary tags each child "GPAL###_tagname" purely to keep same-named
                    // siblings from colliding as dictionary keys before StripGpalKeys groups them - the index
                    // has no meaning past that point and must never reach output as part of an element name.
                    else if (System.Text.RegularExpressions.Regex.IsMatch(key, @"^GPAL\d+_"))
                        key = System.Text.RegularExpressions.Regex.Replace(key, @"^GPAL\d+_", "");

                    string elementName = SanitizeXmlElementName(key);
                    dynamic value = kvp.Value;

                    XmlElement child = doc.CreateElement(elementName);

                    // If top-level and emit=false, this is the first element
                    if (currentParent == null)
                    {
                        currentParent = child;
                        doc.AppendChild(child);  // first element becomes root
                    }
                    else
                    {
                        currentParent.AppendChild(child);
                    }

                    ConvertDictionaryToXmlElement(value, child, emitWrapper, false, visited);
                }
            }
            else if (isEnumerable)
            {
                foreach (dynamic item in obj)
                {
                    if (emitWrapper)
                    {
                        XmlElement child = doc.CreateElement("item");
                        currentParent.AppendChild(child);
                        ConvertDictionaryToXmlElement(item, child, emitWrapper, false, visited);
                    }
                    else
                    {
                        ConvertDictionaryToXmlElement(item, currentParent, emitWrapper, false, visited);
                    }
                }
            }
            else if (isTuple)
            {
                if (currentParent != null)
                {
                    var fields = objType.GetFields();

                    foreach (var field in fields)
                    {
                        dynamic fieldValue = field.GetValue(obj);
                        XmlElement child = doc.CreateElement(SanitizeXmlElementName(field.Name));
                        currentParent.AppendChild(child);
                        ConvertDictionaryToXmlElement(fieldValue, child, emitWrapper: true, isTopLevel: false, visited: visited);
                    }
                }
            }
            else if (isSimple && (objType == null || !objType.IsValueType || objType.IsPrimitive || objType.IsEnum
                || objType == typeof(DateTime) || objType == typeof(DateTimeOffset) || objType == typeof(decimal)
                || IsNullableType(objType) || IsGuidType(objType) || IsTimeSpanType(objType) || IsBigIntegerType(objType)
                || IsComplexType(objType)))
            {
                if (currentParent != null)
                    currentParent.InnerText = ValueToString(obj);
            }
            else if ((object)obj != null && currentParent != null)
            {
                // Complex struct or class — reflect public properties and fields into child elements
                foreach (var prop in objType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        dynamic val = prop.GetValue(obj);
                        XmlElement child = doc.CreateElement(SanitizeXmlElementName(prop.Name));
                        currentParent.AppendChild(child);
                        ConvertDictionaryToXmlElement(val, child, emitWrapper, false, visited);
                    }
                    catch { }
                }
                foreach (var field in objType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try
                    {
                        dynamic val = field.GetValue(obj);
                        XmlElement child = doc.CreateElement(SanitizeXmlElementName(field.Name));
                        currentParent.AppendChild(child);
                        ConvertDictionaryToXmlElement(val, child, emitWrapper, false, visited);
                    }
                    catch { }
                }
            }
        }
        public static void ConvertHtmlDictionaryToXml(dynamic obj, XmlElement parent)
        {
            if (obj == null)
                return;

            XmlDocument doc = parent.OwnerDocument;

            // Case 1: List of children (common for "children" array)
            if (obj is IEnumerable enumerable && !(obj is string) && !(obj is IDictionary))
            {
                foreach (dynamic child in enumerable)
                {
                    ConvertHtmlDictionaryToXml(child, parent);
                }
                return;
            }

            // Case 2: Plain text node
            if (obj is string text)
            {
                parent.AppendChild(doc.CreateTextNode(text));
                return;
            }

            // Case 3: HTML comment node — special case for your parser format
            if (true == IsDictionaryType(obj.GetType()))
            {
                object tagObj;
                if (obj.TryGetValue("tag", out tagObj) && "#comment".Equals(tagObj?.ToString()))
                {
                    string commentText = "";
                    object valueObj;
                    if (obj.TryGetValue("value", out valueObj))
                    {
                        commentText = SanitizeComment(ValueToString(valueObj));
                    }

                    // Create real XML comment
                    parent.AppendChild(doc.CreateComment(commentText));
                    return;
                }
            }

            // Case 4: Regular element node
            if (!(obj is IDictionary elementDict))
            {
                parent.AppendChild(doc.CreateTextNode(ValueToString(obj)));
                return;
            }

            string tagName = "div";
            dynamic attributes = null;
            dynamic children = null;
            string textContent = null;

            foreach (DictionaryEntry entry in elementDict)
            {
                string key = entry.Key.ToString().ToLower();
                if (key == "tag")
                    tagName = entry.Value?.ToString() ?? "div";
                else if (key == "attributes")
                    attributes = entry.Value;
                else if (key == "children")
                    children = entry.Value;
                else if (key == "text")
                    textContent = entry.Value?.ToString();
            }

            XmlElement element = doc.CreateElement(SanitizeXmlElementName(tagName));

            // Attributes
            if (attributes is IDictionary attrDict)
            {
                foreach (DictionaryEntry attr in attrDict)
                {
                    string attrName = attr.Key.ToString();
                    string attrValue = ValueToString(attr.Value);
                    element.SetAttribute(attrName, attrValue);
                }
            }

            // Text content
            if (!string.IsNullOrEmpty(textContent))
            {
                element.AppendChild(doc.CreateTextNode(textContent));
            }

            // Children
            if (children != null)
            {
                ConvertHtmlDictionaryToXml(children, element);
            }

            parent.AppendChild(element);
        }
       
        // Helper to format known complex types nicely
        private static string ValueToString(dynamic value, Type type = null)
        {
            if (null == type)
                type = value?.GetType();

            // KeyValuePair – return only the Value
            if (IsKeyValuePairType(type))
                return ValueToString(value.Value);

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
                string sign = im >= 0 ? "+" : "-";
                return $"{value.Real}{sign}{Math.Abs(im)}i";
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

                if (numeric == 0)
                    return "0";

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

            // DateTime
            if (IsDateTimeType(type))
            {
                return value.ToString("MM/dd/yyyy h:mm:ss tt");
            }

            // Guid – standard string
            if (IsGuidType(type))
            {
                return value.ToString();
            }

            // Uri – absolute string
            if (IsUriType(type))
            {
                return value.ToString();
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

            // Special float/double values – use standard .NET strings
            if (type == typeof(float) || type == typeof(double))
            {
                if (double.IsNaN((double)value))
                    return "NaN";
                if (double.IsPositiveInfinity((double)value))
                    return "Infinity";
                if (double.IsNegativeInfinity((double)value))
                    return "-Infinity";

                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            if (typeof(WaitTime) == type)
            {
                WaitTime wt;
                if (true == TryConvertToWaitTime(value, out wt))
                    return wt.ToString();

                wt = CreateObjectFromDictionary(value, type, null, null);
                return wt.ToString();
            }

                // Simple built-in types (int, bool, char, decimal, etc.) – invariant culture
            if (IsSimpleType(type))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            var toString = GetToString(type);
            // Fallback - how'd we get here? a struct? some custome data structure we don't know about
            return (string)toString.Invoke(value, null);
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

        private static string SanitizeComment(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            text = text.Replace("<!--", "").Replace("-->", "").Replace("--", "-");

            // If it ends with -, add a space
            if (text.EndsWith("-"))
                text += " ";

            // Optional: trim excessive whitespace
            return text.Trim();
        }
        private static string SanitizeXmlElementName(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "item";

            if (key.StartsWith("GPALKEY", StringComparison.Ordinal))
                key = key.Substring(7);
            else if (System.Text.RegularExpressions.Regex.IsMatch(key, @"^GPAL\d+_"))
                key = System.Text.RegularExpressions.Regex.Replace(key, @"^GPAL\d+_", "");

            // XML element names can't start with digits — prefix with _ to preserve the full value
            string sanitized = (key.Length > 0 && !char.IsLetter(key[0]) && key[0] != '_')
                ? "_" + key
                : key;

            // Replace colon (namespace separator) with underscore
            sanitized = sanitized.Replace(":", "_");

            // Replace any invalid characters with underscore
            sanitized = Regex.Replace(sanitized, "[^a-zA-Z0-9_\\-.]", "_");

            // If empty after sanitizing (e.g. original was "123" > "" > needs fallback)
            if (string.IsNullOrEmpty(sanitized))
                return "item";

            return sanitized;
        }
        internal static string ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings converterSettings, out IGPALGrid<string> outGrid)
        {
            StringBuilder sb = new StringBuilder();
            StringBuilder sb2 = new StringBuilder();
            IGPALGrid<string> outputGrid = GPAL.Grid.ToGPALObject();
            List<string> gridRow = new List<string>();
            List<Dictionary<object, dynamic>> rows = new List<Dictionary<object, dynamic>>();
            List<string> flattenLastMessage = new List<string>();
            bool flattenSupressedMessage = false;
            object cleanedInput = ConverterHelper.StripGpalKeys(converterSettings.InputDictionary[0].Values);

            // Handle both single root object or list of root objects
            IEnumerable<object> rootItems = cleanedInput as IEnumerable<object> ?? new[] { cleanedInput };

            // A GPALKEY-surrogate record-list can appear at any nesting depth, not just the root - e.g. a
            // dictionary of clients whose per-client value is itself a list of records. StripGpalKeys already
            // stripped the "GPALKEY" prefix everywhere, so a record-list now just looks like a dictionary
            // whose keys are all plain sequential numbers. Recurse into those instead of handing them to
            // Flatten, which would dot/index-prefix every record onto a single, ever-widening row.
            bool LooksLikeRecordList(dynamic value) =>
                value is IDictionary asDict && asDict.Count > 0 &&
                asDict.Keys.Cast<object>().All(k => int.TryParse(k?.ToString(), out _));

            void EmitRows(dynamic value)
            {
                if (LooksLikeRecordList(value))
                {
                    foreach (dynamic subValue in ((IDictionary)value).Values)
                        EmitRows(subValue);
                    return;
                }

                // Some GPALKEY-indexed slots are genuinely empty placeholders (Count == 0) rather than
                // real records - skip them instead of emitting a blank CSV line for each one.
                if (false == IsCustomStructType(value?.GetType()) && (value == null || (value is ICollection emptyCheck && emptyCheck.Count == 0)))
                    return;

                flattenLastMessage.Clear();
                flattenSupressedMessage = false;
                var row = new Dictionary<object, dynamic>();

                // UnwrapSingleItemLists collapses a single-token row (e.g. ["Selenium"]) down to
                // a bare scalar "Selenium". Flatten would key that under "" (empty string), which
                // doesn't line up with the "0","1","2"... headers produced by multi-token rows.
                // Key it under "0" instead so it lands in the first column.
                if (false == IsCustomStructType(value?.GetType()) && null != value && true == IsSimpleType(value.GetType()))
                    row["0"] = value.ToString();
                else
                    Flatten(value, ref row);

                rows.Add(row);
            }

            foreach (dynamic item in rootItems)
            {
                EmitRows(item);
            }

            List<string> headers = null;

            // User-supplied column names are positional display labels, not field names, so values are
            // looked up by position via canonicalFieldNames rather than by matching header text to a key.
            bool useUserSuppliedHeaders = 0 < ((IGPALFileInternal)converterSettings.OutputFile)?.FileSettings.ColumnList.Count() && 0 < ((IGPALFileInternal)converterSettings.OutputFile)?.FileSettings.ColumnList[0].Count();  // CAVEAT: hard coded value, but we are only now allowing one file
            // A null-valued field is omitted entirely from a row's dictionary, so different rows can carry
            // different subsets/orders of keys. This merges all rows' keys into one order-preserving list,
            // anchoring each newly-seen key immediately after its predecessor from the row it came from.
            // The order is held in a linked list with a lookup from each name to its node, so finding a name and
            // placing a new one right after its predecessor cost the same however many names there are. A list
            // scanned and shifted for every key grows with keys times names.
            List<string> canonicalFieldNames = null;
            if (rows.Any())
            {
                LinkedList<string> orderedNames = new LinkedList<string>();
                Dictionary<string, LinkedListNode<string>> nodeFor = new Dictionary<string, LinkedListNode<string>>();

                foreach (var row in rows)
                {
                    LinkedListNode<string> lastNode = null;
                    foreach (string key in row.Keys.Cast<string>())
                    {
                        if (true == nodeFor.TryGetValue(key, out LinkedListNode<string> existingNode))
                        {
                            lastNode = existingNode;
                        }
                        else
                        {
                            lastNode = null == lastNode ? orderedNames.AddFirst(key) : orderedNames.AddAfter(lastNode, key);
                            nodeFor[key] = lastNode;
                        }
                    }
                }

                canonicalFieldNames = orderedNames.ToList();
            }
            if (useUserSuppliedHeaders)
                headers = ((IGPALFileInternal)converterSettings.OutputFile).FileSettings.ColumnList[0].ToList();
            else
                headers = canonicalFieldNames;

            if (true == converterSettings.FirstLineIsColumnHeaders && null != headers)
            {
                foreach (var header in headers)
                {
                    sb2.Append(CreateCSVToken(header.ToString(), converterSettings));
                    gridRow.Add(header.ToString());
                }
                if (sb2.Length > 0) sb2.Length--; // Remove trailing comma
                sb.AppendLine(sb2.ToString());
                sb2.Clear();
                outputGrid.AddRow(gridRow);
            }

            int startIndex = 0;
            int headerCount = headers?.Count ?? 0;
            // Pivot the data
            for (int rowIndex = startIndex; rowIndex < rows.Count; rowIndex++)
            {
                var dict = rows[rowIndex];
                gridRow = new List<string>();

                for (int i = 0; i < headerCount; i++)
                {
                    string key = useUserSuppliedHeaders ? (i < canonicalFieldNames.Count ? canonicalFieldNames[i] : null) : headers[i];
                    string value = key != null && dict.ContainsKey(key) && dict[key] != null ? dict[key].ToString() : "";
                    sb2.Append(CreateCSVToken(value, converterSettings));
                    gridRow.Add(value);
                }
                char delim = true == converterSettings.OutDelimiter.HasValue ? converterSettings.OutDelimiter.Value : ',';

                if (sb2.Length > 0 && sb2[sb2.Length - 1] == delim) sb2.Length--; // Remove trailing comma
                sb.AppendLine(sb2.ToString());
                sb2.Clear();
                outputGrid.AddRow(gridRow);
            }
            // Recursive function to flatten nested dictionary
            void Flatten(dynamic dictionaryOrList, ref Dictionary<object, dynamic> outDict, string prefix = "")
            {
                StringBuilder sb3 = new StringBuilder();
                Dictionary<object, dynamic> outDict2 = new Dictionary<object, dynamic>();
                Type dictOrListType = dictionaryOrList?.GetType();

                if (true == IsDictionaryType(dictOrListType))
                {
                    foreach (dynamic kvp in dictionaryOrList)
                    {
                        Type kvpType = kvp.GetType();
                        dynamic kvpValue = kvp;
                        dynamic kvpKey = null;

                        if (true == IsKeyValuePairType(kvpType))
                        {
                            kvpValue = kvp.Value;
                            kvpKey = kvp.Key;
                        }

                        Type kvpValueType = kvpValue?.GetType();

                        if (true == IsComplexType(kvpValueType))
                        {
                            Complex complex = (Complex)kvpValue;
                            sb3.Append("{");
                            sb3.Append($"{CreateCSVToken(complex.Real.ToString(), converterSettings)}{CreateCSVToken(complex.Imaginary.ToString(), converterSettings)}");
//                            sb3.Append($"{complex.Real.ToString()}{complex.Imaginary.ToString()}");
                            sb3.Remove(sb3.Length - 1, 1);
                            sb3.Append("}");
                            outDict[prefix + kvpKey] = sb3.ToString();
                            sb3.Clear();
                        }
                        else if (true == IsSimpleType(kvpValueType))
                        {
                            if (outDict.TryGetValue(prefix + kvpKey, out dynamic val))
                                sb3.Append($"{val}");
                            else
                                sb3.Append(ValueToString(kvpValue));
//                            sb3.Append(CreateCSVToken(kvpValue.ToString(), converterSettings));
                            outDict[prefix + kvpKey] = sb3.ToString();
                            sb3.Clear();
                        }
                        else if (true == IsIpAddressType(kvpValueType))
                        {
                            IPAddress iPAddress = (IPAddress)kvpValue;
                            sb3.Append(iPAddress.ToString());
//                            sb3.Append(CreateCSVToken(iPAddress.ToString(), converterSettings));
                            outDict[prefix + kvpKey] = sb3.ToString();
                            sb3.Clear();
                        }
                        else if (IsBigIntegerType(kvpValueType) || IsVersionType(kvpValueType))
                        {
                            sb3.Append(kvpValue.ToString());
//                            sb3.Append(CreateCSVToken(kvpValue.ToString(), converterSettings));
                            outDict[prefix + kvpKey] = sb3.ToString();
                            sb3.Clear();
                        }
                        else if (true == IsDictionaryType(kvpValueType))
                        {
                            Flatten(kvpValue, ref outDict2, prefix + kvpKey + ".");
                            foreach (var item in outDict2)
                            {
                                outDict[item.Key] = item.Value;
                            }
                            outDict2.Clear();
                        }
                        else if ((true == IsEnumerableType(kvpValueType) || true == kvpValueType.IsArray)/* && false == IsStructType(kvpValueType)*/)
                        {
                            int index = 0;
                            foreach (var item in kvpValue)
                            {
                                Flatten(item, ref outDict2, $"{prefix}{kvpKey}{index++}.");
                                foreach (var item2 in outDict2)
                                {
                                    outDict[item2.Key] = item2.Value;
                                }
                                outDict2.Clear(); // clear but DO NOT merge
                            }
                        }
                        else if ((true == IsClassType(kvpValueType)/* || true == IsStructType(kvpValueType)*/) && kvpValueType != typeof(string))
                        {
                            foreach (var prop in kvpValueType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                            {
                                try
                                {
                                    object propValue = prop.GetValue(kvpValue);
                                    if (propValue != null)
                                    {
                                        Flatten(propValue, ref outDict2, $"{prefix}{kvpKey}.{prop.Name}.");
                                        foreach (var item in outDict2)
                                        {
                                            outDict[item.Key] = item.Value;
                                        }
                                        outDict2.Clear();
                                    }
                                    else if (true == IsSimpleType(prop.PropertyType))
                                    {
                                        // A null simple property (e.g. OrganizationName for an individual
                                        // provider record) must still claim its column slot with a blank
                                        // value - silently omitting the key here shifts every subsequent
                                        // property's position out of alignment with a fixed header list.
                                        outDict[$"{prefix}{kvpKey}.{prop.Name}."] = "";
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to flatten property [{prop.Name}] of [{kvpValueType.Name}]", kvp, GPALObjectType.Other, ex);
                                }
                            }
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Unable to handle type [{kvpValueType}]", kvp, GPALObjectType.Other);
                        }
                    }
                }
                else // Top-level input
                {
                    if (true == IsKeyValuePairType(dictOrListType))   // keyvalupair, value is children
                    {
                        if (true == IsDictionaryType(dictionaryOrList.Value.GetType()))
                        {
                            Flatten(dictionaryOrList.Value, ref outDict2, $"{prefix}");
                            foreach (var item2 in outDict2)
                            {
                                outDict[item2.Key] = item2.Value.Trim(true == converterSettings.InDelimiter.HasValue ? converterSettings.InDelimiter.Value : ',');
                            }
                            outDict2.Clear();
                        }
                        else
                            outDict[dictionaryOrList.Key] = dictionaryOrList.Value;
                    }
                    // we might be passing in 'null' for a nullable type, so we should output 'null' and return null
                    else if (false == IsCustomStructType(dictOrListType) && true == ValueToString(dictionaryOrList).Equals("null"))
                    {
                        sb3.Append("null");
                        outDict[prefix] = null;
                    }
                    else if (true == IsComplexType(dictOrListType))
                    {
                        Complex complex = (Complex)dictionaryOrList;
                        sb3.Append("{");
                        sb3.Append($"{complex.Real.ToString()}{complex.Imaginary.ToString()}");
                        //                        sb3.Append($"{CreateCSVToken(complex.Real.ToString(), converterSettings)}{CreateCSVToken(complex.Imaginary.ToString(), converterSettings)}");
                        sb3.Remove(sb3.Length - 1, 1);
                        sb3.Append("}");
                        outDict[prefix] = sb3.ToString();
                    }
                    else if (true == IsSimpleType(dictOrListType))
                    {
                        if (outDict.TryGetValue(prefix, out dynamic val))
                            sb3.Append($"{val}");
                        else
                            sb3.Append(dictionaryOrList.ToString());
                        //                        sb3.Append(CreateCSVToken(dictionaryOrList.ToString(), converterSettings));
                        outDict[prefix] = sb3.ToString();
                    }
                    else if (true == IsIpAddressType(dictOrListType))
                    {
                        IPAddress iPAddress = (IPAddress)dictionaryOrList;
                        sb3.Append(iPAddress.ToString());
                        //                        sb3.Append(CreateCSVToken(iPAddress.ToString(), converterSettings));
                        outDict[prefix] = sb3.ToString();
                    }
                    else if (IsBigIntegerType(dictOrListType) || IsVersionType(dictOrListType))
                    {
                        sb3.Append(dictionaryOrList.ToString());
                        //                        sb3.Append(CreateCSVToken(dictionaryOrList.ToString(), converterSettings));
                        outDict[prefix] = sb3.ToString();
                    }
                    else if (true == IsEnumerableType(dictOrListType) || true == IsArrayType(dictOrListType))
                    {
                        int index = 0;
                        foreach (var item in dictionaryOrList)
                        {
                            Flatten(item, ref outDict2, $"{prefix}{index++}");
                            foreach (var item2 in outDict2)
                            {
                                outDict[item2.Key] = item2.Value;
                            }
                            outDict2.Clear();
                        }
                    }
                    else
                    {
                        string flattenMsg = $"Unable to handle type [{dictOrListType}]";
                        if (false == flattenLastMessage.Contains(flattenMsg))
                        {
                            flattenLastMessage.Add(flattenMsg);
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, flattenMsg, dictionaryOrList, GPALObjectType.Other);
                            flattenSupressedMessage = false;
                        }
                        else if (false == flattenSupressedMessage)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", dictionaryOrList, GPALObjectType.Other);
                            flattenSupressedMessage = true;
                        }
                    }
                }
            }

            outGrid = outputGrid;
            return sb.ToString();
        }
        internal static string CreateCSVToken(string inputString, ConverterSettings converterSettings)
        {
            char delimiter = true == converterSettings.OutDelimiter.HasValue ? converterSettings.OutDelimiter.Value : ',';
            inputString = (inputString ?? "").TrimEnd(delimiter);    // avoid double delimiters. we might be adding these when we parse into the dictionary

            // A value holding the delimiter is quoted even when fields are not, or it would split into extra columns.
            // Its own quotes are doubled, so the quoted value reads back whole.
            bool holdsDelimiter = 0 <= inputString.IndexOf(delimiter);
            bool quoted = true == converterSettings.FieldsEnclosedInQuotes || true == holdsDelimiter;

            if (true == holdsDelimiter)
                inputString = inputString.Replace(@"""", @"""""");

            StringBuilder sb = new StringBuilder();
            if (true == quoted)
                sb.Append(@"""");

            sb.Append(inputString); // NOTE: CAVEAT: is this line required?  .Replace(@"""", @""""""));

            if (true == quoted)
                sb.Append(@"""");

            if (0 == sb.Length || delimiter != sb[sb.Length - 1])
                sb.Append(delimiter);

            return sb.ToString();
        }
        internal static Dictionary<object, dynamic> ConvertXMLToDictionary(XmlDocument xmlDoc)
        {
            xmlItemCount = 0;
            var rootDict = new Dictionary<object, dynamic>();

            // Use the actual root element name as the key
            string rootName = xmlDoc.DocumentElement.Name;  // "catalog"

            // Convert the document element (the real root) to its dictionary
            dynamic contentDict = ConvertXmlNodeToDictionary(xmlDoc.DocumentElement);

            // Put it under the original root name
            rootDict[rootName] = contentDict;

            return rootDict;
        }

        private static int xmlItemCount = 0;
        private static Dictionary<object, dynamic> ConvertXmlNodeToDictionary(XmlNode xmlNode)
        {
            var dict = new Dictionary<object, dynamic>();

            try
            {
                // Attributes — prefix with @ so we can distinguish them later
                if (xmlNode.Attributes != null)
                {
                    foreach (XmlAttribute attr in xmlNode.Attributes)
                    {
                        dict["@GPAL_" + attr.Name] = attr.Value;
                    }
                }

                // Group child elements by name (fixes the <row> problem)
                var childGroups = new Dictionary<string, List<Dictionary<object, dynamic>>>();

                string textContent = null;
                string rawWhitespaceText = null; // saved in case this turns out to be a leaf element

                foreach (XmlNode childNode in xmlNode.ChildNodes)
                {
                    if (childNode.NodeType == XmlNodeType.Element)
                    {
                        var childDict = ConvertXmlNodeToDictionary(childNode);
                        string name = $"GPAL{xmlItemCount++.ToString("D3")}_"+childNode.Name;

                        if (!childGroups.TryGetValue(name, out var list))
                        {
                            list = new List<Dictionary<object, dynamic>>();
                            childGroups[name] = list;
                        }
                        list.Add(childDict);
                    }
                    else if (childNode.NodeType == XmlNodeType.Text
                          || childNode.NodeType == XmlNodeType.Whitespace
                          || childNode.NodeType == XmlNodeType.SignificantWhitespace)
                    {
                        string raw = childNode.Value ?? string.Empty;
                        string trimmed = raw.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            textContent = trimmed;
                        else if (raw.Length > 0)
                            rawWhitespaceText = raw; // may be a meaningful space char; keep if leaf
                    }
                }
                // Preserve whitespace-only text only in leaf elements (no child elements).
                // In parent elements the whitespace is just pretty-print indentation.
                if (textContent == null && rawWhitespaceText != null && childGroups.Count == 0)
                    textContent = rawWhitespaceText;

                // Add child elements (single or list)
                foreach (var group in childGroups)
                {
                    //if (group.Value.Count == 1)
                    //    dict[group.Key] = group.Value[0];
                    //else
                        dict[group.Key] = group.Value;
                }

                // Add text content with marker
                if (textContent != null)
                {
                    dict["#text"] = textContent;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to convert XMLnode [{xmlNode}] to dictionary.", null, GPALObjectType.None, ex);
            }

            return dict;
        }
        internal static List<Dictionary<object, dynamic>> ConvertGridToDictionary(List<string> columnNames, IGPALGrid<string> rows, int startRow = 0, int rowCnt = -1)
        {
            List<Dictionary<object, dynamic>> returnList = new List<Dictionary<object, dynamic>>();
            var rowDictionary = new Dictionary<object, dynamic>();
            int idx = 0;
            int ourRowCnt = 0;

            if (0 == startRow && -1 == rowCnt)
                try
                {
                    foreach (List<string> row in rows)
                    {
                        rowDictionary = new Dictionary<object, dynamic>();
                        idx = 0;

                        foreach (string columnName in columnNames)
                            rowDictionary[columnName] = row[idx++];

                        returnList.Add(rowDictionary);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to convert grid to dictionary.", rows, GPALObjectType.Other, ex);
                }
            else
            {
                try
                {
                    for (int rowIdx = startRow; ourRowCnt < rowCnt; ourRowCnt++)
                    {
                        rowDictionary = new Dictionary<object, dynamic>();
                        List<string> row = rows[rowIdx++];
                        idx = 0;

                        foreach (string columnName in columnNames)
                            rowDictionary[columnName] = row[idx++];

                        returnList.Add(rowDictionary);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to convert grid to dictionary.", rows, GPALObjectType.Other, ex);
                }

            }

            return returnList;
        }
        /// <summary>
        /// Converts a delimited flat-file input into a dictionary representation.
        /// Preserves all token content exactly, including quotes, without interpreting CSV semantics.
        /// This method is designed for round-trip conversion or transformation into other formats
        /// (CSV, JSON, XML, YAML, HTML) while keeping original data fidelity.
        /// </summary>
        /// <param name="converterSettings">Settings for input parsing, including delimiter and header options.</param>
        /// <param name="inputFile">The input file stream or StringReader containing the flat file.</param>
        /// <param name="filename">Optional file name, used for logging purposes.</param>
        /// <returns>
        /// A dictionary keyed by GPALKEYlineNumber, each value is a dictionary of column name -> literal token string.
        /// </returns>
        internal static Dictionary<object, dynamic> ConvertDelimitedToDictionary(
            ConverterSettings converterSettings,
            dynamic inputFile,
            string filename = null)
        {
            var records = new Dictionary<object, dynamic>();
            List<string> headers = null;
            int lineNumber = 0;

            char delimiter =
                converterSettings?.InDelimiter ??
                GetDelimiterFromFormat(
                    GetDataFormatFromExtension(filename ?? @"csv")
                ) ?? ',';

            try
            {
                TextReader reader;

                if (inputFile is Stream stream)
                {
                    stream.Position = 0;
                    reader = new StreamReader(stream);
                }
                else if (inputFile is StreamReader sr)
                {
                    reader = sr;
                }
                else if (inputFile is StringReader str)
                {
                    reader = str;
                }
                else
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $@"Unsupported inputFile type [{inputFile?.GetType().FullName}]",
                        inputFile,
                        GPALObjectType.GPALFile);
                    return records;
                }

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        lineNumber++;
                        continue;
                    }

                    var tokens = SplitRespectingQuotes(line, delimiter);

                    // First line: header handling
                    if (lineNumber == 0)
                    {
                        if (converterSettings?.FirstLineIsColumnHeaders == true)
                        {
                            headers = tokens;
                            lineNumber++;
                            continue; // skip header row
                        }
                        // No headers — treat first line as data
                    }

                    // Row processing
                    if (headers != null)
                    {
                        // With headers > Dictionary<string, string>
                        var rowDict = new Dictionary<string, string>();
                        for (int i = 0; i < Math.Min(headers.Count, tokens.Count); i++)
                        {
                            rowDict[headers[i]] = tokens[i];
                        }
                        records[$"GPALKEY{lineNumber:D4}"] = rowDict;
                    }
                    else
                    {
                        // No headers > List<string> (raw values, order preserved)
                        records[$"GPALKEY{lineNumber:D4}"] = tokens;
                    }

                    lineNumber++;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $@"Exception while parsing delimited file",
                    inputFile,
                    GPALObjectType.GPALFile, ex);
            }

            if (records.Count == 0)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $@"No data found in input file [{filename}]",
                    inputFile,
                    GPALObjectType.GPALFile);
            }

            return records;
        }

        /// <summary>
        /// Reads all lines from a TextReader into a list.
        /// Explicit method to avoid inline loops and improve readability for future maintainers.
        /// </summary>
        /// <param name="reader">TextReader instance</param>
        /// <returns>List of lines read from reader</returns>
        private static IEnumerable<string> ReadAllLines(TextReader reader)
        {
            var lines = new List<string>();
            string line;

            // Read until end of stream
            while (null != (line = reader.ReadLine()))
            {
                lines.Add(line);
            }

            return lines;
        }
        internal static char? GetDelimiterFromFormat(DataFormat dataFormat)
        {
            switch (dataFormat)
            {
                case DataFormat.CARET:
                    return ('^');
                case DataFormat.COLON:
                    return (':');
                case DataFormat.CSV:
                    return (',');
                case DataFormat.DOT:
                    return ('.');
                case DataFormat.HYPHEN:
                    return ('-');
                case DataFormat.PIPE:
                    return ('|');
                case DataFormat.PRN:
                case DataFormat.SPACE:
                    return (' ');
                case DataFormat.SEMICOLON:
                    return (';');
                case DataFormat.TAB:
                    return ('\t');

                case DataFormat.HTML:
                case DataFormat.JSON:
                //// post-v1: PDF case DataFormat.PDF:
                case DataFormat.XLSX:
                case DataFormat.XML:
                case DataFormat.YAML:
                    return null;

                case DataFormat.CUSTOM_DELIMITER:
                    return ConverterSettings.InDelimiter.Value;

                default:
                    //GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to determine delimiter from DataFormat [{dataFormat}], using comma.", null, GPALObjectType.None);
                    return (',');
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

        // Extract id and return rest of dict if id exists
        private static bool TryGetIdAndRest(IDictionary dict, out string id, out Dictionary<object, dynamic> rest)
        {
            id = string.Empty;
            rest = new Dictionary<object, dynamic>();

            if (dict.Contains("id"))
            {
                dynamic idValue = dict["id"];
                if (idValue is string idStr)
                {
                    id = idStr;
                    foreach (DictionaryEntry kvp in dict)
                    {
                        if (!"id".Equals(kvp.Key.ToString()))
                        {
                            rest[kvp.Key] = kvp.Value;
                        }
                    }
                    return true;
                }
            }

            // Fallback: copy all
            foreach (DictionaryEntry kvp in dict)
            {
                rest[kvp.Key] = kvp.Value;
            }
            return false;
        }

        // Render full object 
        private static string RenderFullObjectToHtml(IDictionary dict, bool emitUl = false, bool isTopLevel = true, string listStyle = "style='margin: 8px; padding-left: 8px;'")
        {
            var sb = new StringBuilder();
            int dictCount = dict.Count;

            if (true == isTopLevel)
                sb.Append($"<ul id='renderfull' {listStyle}>\n");

            foreach (object keyObj in dict.Keys)
            {
                string key = keyObj.ToString().Replace("@GPAL_", "");

                key = System.Text.RegularExpressions.Regex.Replace(key, @"^GPAL\d+_", "");

                // GPALKEY#### is an array index, not a field name. Blank it so it is handled like the empty-key case
                // below.
                if (true == System.Text.RegularExpressions.Regex.IsMatch(key, @"^GPALKEY\d+$"))
                    key = string.Empty;

                if ("#comment".Equals(key)) continue;
                dynamic value = dict[keyObj];

                if (false == key.Equals("#text") && false == string.IsNullOrEmpty(key)) // do not emit our array index keys
                {
                    sb.Append("<li><strong>");
                    sb.Append(HttpUtility.HtmlEncode(key));
                    sb.Append("</strong>: ");
                    listStyle = "style = 'list-style: none; margin: 8px; padding-left: 8px;'";
                }
                Type valueType = value?.GetType();
                bool isCollection = IsDictionaryType(valueType) || IsEnumerableType(valueType);
                bool isTuple = IsTupleType(valueType);

                int collectionCount = 0;

                if (true == isCollection)
                    collectionCount = ObjectCopier.GetItemCount(null, value);

                if (true == emitUl && 1 < dictCount)
                    sb.Append($"<ul id=rf2 {listStyle}><li>\n");

                    sb.Append(RenderValue(value, isCollection || isTuple, isCollection || isTuple ? "style='margin: 8px; padding-left: 8px;'" : listStyle));

                if (true == emitUl && 1 < dictCount)
                    sb.Append("</ul></li>");

                if (false == key.Equals("#text"))
                    sb.Append("</li>\n");
            }

            if (true == isTopLevel/* || true == emitUl*/)
                sb.Append("</ul>\n");

            return sb.ToString();
        }

        private static int idCnt = 0;

        // Render any value recursively
        private static string RenderValue(dynamic value, bool renderUl = false, string ulStyle = "style='margin: 0px; padding-left: 0px;", HashSet<object> visited = null)
        {
            var valueSb = new StringBuilder();
            Type valueType = value?.GetType();
            string vtn = valueType?.Name;

            // Initialize visited set at root call
            bool isRootCall = visited == null;
            bool isNull = false;

            if (isRootCall)
            {
                visited = new HashSet<object>(ReferenceEqualityComparer.Instance); // Critical: reference equality
            }

            // Compared as object: a dynamic struct has no == with null (and is never null), and catching that throw
            // cost seconds under a debugger
            isNull = null == (object)value;

            Dictionary<object, dynamic> dict = new Dictionary<object, dynamic>();

            if (false == isNull)
            {

                // === CYCLE DETECTION: If this is a reference type we've already seen, return a marker ===
                if (valueType.IsClass || valueType.IsInterface) // Reference types only
                {
                    if (visited.TryGetValue(value, out dynamic fuggetAboutIt))
                    {
                        string referenceName = string.Empty;

                        try
                        {
                            referenceName = value.Value;
                        }
                        catch
                        {
                            try
                            {
                                referenceName = value.Name;
                            }
                            catch
                            {
                                referenceName = valueType.Name;
                            }
                        }
                        // We've seen this exact object before > return a reference marker
                        return $"Recursion to [{referenceName}]";
                    }

                    // First time seeing this object > add to visited
                    visited.Add(value);
                }
            }

            if (true == IsKeyValuePairType(valueType))
            {
                return $"<strong>{value.Key}</strong>: {RenderValue(value.Value, true, ulStyle, visited)}";
            }
            // Tuple (special case)
            else if (IsTupleType(valueType))
            {
                if (true == renderUl)
                    valueSb.Append($"<ul id=rvtuple{idCnt++} {ulStyle}'>\n");

                // Render tuple in a clean, readable way
                var fields = valueType.GetFields();
                var parts = new List<string>();
                foreach (dynamic field in fields)
                {
                    var fieldValue = field.GetValue(value);
                    valueSb.AppendLine($"<li><strong>{field.Name}</strong>: {RenderValue(fieldValue, true, ulStyle, visited)}</li>");
                }

                if (true == renderUl)
                    valueSb.Append("</ul>\n");
            }
            else if (false == IsCustomStructType(valueType) && value == null)
                return HttpUtility.HtmlEncode("null");
            // Nested Dictionary<object, dynamic> -> full recursive call
            else if (value is Dictionary<object, dynamic> nestedDict)
            {
                valueSb.Append(RenderFullObjectToHtml(nestedDict, renderUl, false));
            }
            // IEnumerable (list/array)
            else if (true == IsEnumerableObject(value) && !(value is string))
            {
                if (true == renderUl)
                    valueSb.Append($"<ul id=rvenum{idCnt++} {ulStyle}'>\n");

                foreach (var item in value)
                {
                    Type itemType = item?.GetType();
                    bool isCollection = IsDictionaryType(itemType) || IsEnumerableType(itemType);
                    string toRender = RenderValue(item, isCollection || renderUl, ulStyle, visited);

                    if (false == string.IsNullOrEmpty(toRender))
                    {
                        // if we are a collection, bullets will be added, do not add here
                        // however if it is a scalar, add a bullet here
                        if ((false == isCollection || true == renderUl) && false == toRender.StartsWith("<li>"))  // a bit of a kludge, at times we get <li><li>
                            valueSb.Append("<li>");

                        valueSb.Append(toRender);

                        if ((false == isCollection || true == renderUl) && false == toRender.StartsWith("<li>")) // a bit of a kludge, at times we get <li><li>
                            valueSb.Append("</li>\n");
                    }
                }

                if (true == renderUl)
                    valueSb.Append("</ul>\n");
            }
            // Primitive value
            // Complex struct (any value type that's not simple and not tuple)
            else if (true == IsSimpleType(valueType) && false == IsCustomStructType(valueType))
            {
                string toRender = ValueToString(value);
                if (false == string.IsNullOrEmpty(toRender))
                    valueSb.Append($"{HttpUtility.HtmlEncode(toRender)}");
            }
            else if (true == IsCustomStructType(valueType) || true == IsClassType(valueType))
            {
                if (true == renderUl)
                    valueSb.Append($"<ul id=rvstruct{idCnt++} {ulStyle}'>\n");

                var fields = valueType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var props = valueType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    dynamic fieldValue = field?.GetValue(value);
                    valueSb.Append($"<li><strong>{field?.Name}</strong>: ");
                    Type fieldValueType = fieldValue?.GetType();

                    if (true == IsEnumerableType(fieldValueType))
                        valueSb.Append(RenderValue(fieldValue, true, ulStyle, visited));
                    else
                        valueSb.Append(ValueToString(fieldValue));

                    valueSb.Append("</li>\n");
                }

                foreach (var prop in props)
                {
                    if (true == prop?.CanRead && prop?.GetIndexParameters().Length == 0)
                    {
                        dynamic propValue = prop?.GetValue(value);
                        valueSb.Append($"<li><strong>{prop?.Name}</strong>: ");
                        Type propValueType = propValue?.GetType();
                        bool isEnumerable = IsEnumerableType(propValueType);
                        bool isClass = IsClassType(propValueType);
                        bool isSimple = IsSimpleType(propValueType);

                        if (true == isEnumerable || (true ==  isClass && false == isSimple))
                            valueSb.Append(RenderValue(propValue, true, ulStyle, visited));
                        else
                            valueSb.Append(ValueToString(propValue));

                        valueSb.Append("</li>\n");
                    }
                }

                if (true == renderUl)
                    valueSb.Append("</ul>\n");
            }
            else
            {
                valueSb.Append(ValueToString(value));
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Render [{valueType}] using ValueToString", value, GPALObjectType.Other);
            }

                return valueSb.ToString();
        }

        // Main method to convert dictionary to HTML
        public static string ConvertDictionaryToHtml(Dictionary<object, dynamic> dictionary)
        {
            var sb = new StringBuilder();

            if (0 == dictionary.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Dictionary is empty. No html will be output.", dictionary, GPALObjectType.Converter);
                return string.Empty;
            }

            if (dictionary.ContainsKey("tag"))
            {
                var tagName = dictionary["tag"].ToString();
                if ("#comment".Equals(tagName))
                {
                    if (dictionary.ContainsKey("value"))
                    {
                        sb.Append($" {dictionary["value"].ToString()}\n");
                    }
                    return sb.ToString();
                }

                if (false == "#text".Equals(tagName))
                {
                    sb.Append($"<{tagName}");
                    if (dictionary.ContainsKey("attributes"))
                    {
                        dynamic attributes = dictionary["attributes"];
                        foreach (var attribute in attributes)
                        {
                            sb.Append($" {attribute.Key}=\"{attribute.Value}\"");
                        }
                    }

                    switch (tagName)
                    {
                        case "br":
                        case "hr":
                            sb.Append("/>\n");
                            break;
                        case "img":
                        case "input":
                        case "meta":
                        case "link":
                        case "html":
                        case "head":
                        case "body":
                            sb.Append(">\n");
                            break;
                        default:
                            sb.Append(">");
                            break;
                    }
                }

                if (dictionary.ContainsKey("text"))
                {
                    sb.Append(dictionary["text"].ToString());
                }

                if (dictionary.ContainsKey("children"))
                {
                    var children = (List<Dictionary<object, dynamic>>)dictionary["children"];
                    foreach (var child in children)
                    {
                        if (false == child.ContainsKey("#text"))
                        {
                            string text = ConvertDictionaryToHtml(child);
                            if (0 < text.Length)
                                sb.Append(text);
                        }
                        else
                            sb.Append(child["text"]);
                    }
                }

                switch (tagName)
                {
                    case "br":
                    case "hr":
                    case "meta":
                    case "img":
                    case "input":
                    case "link":
                    case "#text":
                        break;
                    default:
                        sb.Append($"</{tagName}>\n");
                        break;
                }
            }
            else
            {
                // Generic data rendering (no "tag")
                bool isArray = IsArrayLike(dictionary);
                if (isArray)
                {
                    sb.Append($"<ul id=list style='margin: 8px; padding-left: 8px;'>\n");

                    foreach (var kvp in dictionary)
                    {
                        dynamic item = kvp.Value;
                        string toRender = RenderValue(item, true, "style='margin: 8px; padding-left: 8px;");

                        if (false == string.IsNullOrEmpty(toRender))
                        {
                            sb.Append("<li>");
                            sb.Append(toRender);
                            sb.Append("</li>\n");
                        }
                    }

                    sb.Append("</ul>");
                }
                else
                {
                    sb.Append(RenderFullObjectToHtml(dictionary));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputDataOrFilename"></param>
        /// <param name="isChildren">true if we are processing child records, so we want to keep #text tags</param>
        /// <returns></returns>
        public static List<Dictionary<object, dynamic>> ConvertHtmlToDictionary(dynamic inputDataOrFilename, bool isChildren = false)
        {
            var dictionaries = new List<Dictionary<object, dynamic>>();
            HtmlNodeCollection nodes = null;

            if (inputDataOrFilename is HtmlNodeCollection)
                nodes = inputDataOrFilename;
            else
                nodes = GetHtmlNodes(inputDataOrFilename);

            if (null == nodes)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No html nodes found. Dictionary will be empty", inputDataOrFilename, GPALObjectType.Converter);
                return new List<Dictionary<object, dynamic>>();
            }

            foreach (var node in nodes)
            {
                // Skip only irrelevant root-level nodes like #document
                if (node.Name == "#document" && false == isChildren)
                    continue;

                // Handle #comment nodes explicitly
                if (true == "#comment".Equals(node.Name))
                {
                    if (false == isChildren)
                        continue;

                    var commentDict = new Dictionary<object, dynamic>
            {
                { "tag", "#comment" },
                { "value", node.InnerHtml }
            };
                    dictionaries.Add(commentDict);
                    continue;
                }

                // Handle #text nodes
                if (true == "#text".Equals(node.Name))
                {
                    if (false == isChildren)
                        continue;

                    var textDict = new Dictionary<object, dynamic>
            {
                { "tag", "#text" },
                { "value", node.InnerHtml }
            };
                    dictionaries.Add(textDict);
                    continue;
                }

                var dict = new Dictionary<object, dynamic>();

                // Add the tag name and attributes to the dictionary
                dict["tag"] = node.Name;
                if (node.Attributes.Count > 0)
                {
                    var attributes = new Dictionary<string, string>();
                    foreach (var attribute in node.Attributes)
                    {
                        attributes[attribute.Name] = attribute.Value;
                    }
                    dict["attributes"] = attributes;
                }

                // Handle the node's text content directly
                if (!string.IsNullOrEmpty(node.InnerText.Trim()) && !node.HasChildNodes)
                {
                    dict["text"] = node.InnerText.Trim();
                }

                // Recursively add child nodes to the dictionary
                if (node.HasChildNodes)
                {
                    var children = new List<Dictionary<object, dynamic>>();
                    foreach (var childNode in node.ChildNodes)
                    {
                        if (childNode.Name == "#text")
                        {
                            if (!string.IsNullOrEmpty(childNode.InnerHtml.Trim()))
                            {
                                // If the parent has only text children, store it directly in "text"
                                if (node.ChildNodes.All(n => n.Name == "#text"))
                                {
                                    dict["text"] = childNode.InnerHtml.Trim();
                                    break;
                                }
                                else
                                {
                                    children.Add(new Dictionary<object, dynamic>
                            {
                                { "tag", "#text" },
                                { "value", childNode.InnerHtml }
                            });
                                }
                            }
                        }
                        else
                        {
                            var childDictionaries = ConvertHtmlToDictionary(new HtmlNodeCollection(null) { childNode }, true);
                            if (childDictionaries.Count > 0)
                            {
                                foreach (var childDict in childDictionaries)
                                {
                                    children.Add(childDict);
                                }
                            }
                        }
                    }
                    if (children.Count > 0)
                        dict["children"] = children;
                }

                dictionaries.Add(dict);
            }
            return dictionaries;
        }
        public static HtmlNodeCollection GetHtmlNodes(dynamic htmlorFilename)
        {
            string htmlString;
            var doc = new HtmlAgilityPack.HtmlDocument();
            try
            {
                htmlString = File.ReadAllText(htmlorFilename);
            }
            catch
            {
                htmlString = htmlorFilename;
            }

            try
            {
                doc.LoadHtml(htmlString);
                return doc.DocumentNode.ChildNodes; // descandants causes duplicates, this works perfectly
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Expected html string or GPALFile, got [{htmlorFilename.GetType()}]", htmlorFilename, GPALObjectType.Other, ex);
                return null;
            }

        }
        public static dynamic CreateIEnumerableFromClass<T>(ConverterSettings converterSettings)
        {
            IEnumerable<T> resultList = null;
            bool isArrayOutput = converterSettings.OutputClassType.IsArray;
            T[] arrayOutput = null;
            int arrayCapacity = 0;
            int elementIndex = 0;

            // Determine capacity for array based on target size, not input
            if (isArrayOutput)
            {
                arrayCapacity = ((Array)Activator.CreateInstance(converterSettings.OutputClassType, 4)).Length; // Use the predefined length (4)
            }
            else
            {
                arrayCapacity = converterSettings.InputDictionary.Count();
            }

            // Instantiate the output collection
            if (isArrayOutput)
            {
                arrayOutput = (T[])Array.CreateInstance(converterSettings.OutputClassType.GetElementType(), arrayCapacity);
                // Populate with empty instances up to array length
                for (int i = 0; i < arrayCapacity; i++)
                {
                    arrayOutput[i] = (T)InstantiateOne(converterSettings.OutputClassElementType);
                }
            }
            else
            {
                switch (converterSettings.OutputClassType)
                {
                    default:
                        resultList = (IEnumerable<T>)ActivatorHelper.CreateInstance(converterSettings.OutputClassType);
                        break;
                }
            }

            // Process input dictionary, but limit to array capacity if applicable
            int inputIndex = 0;
            foreach (var dict in converterSettings.InputDictionary)
            {
                if (isArrayOutput && inputIndex >= arrayCapacity)
                {
                    break; // Stop if we've reached the array capacity
                }

                bool allKeysSame = false;
                try
                {
                    allKeysSame = CheckKeysPrefix(dict);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"CheckKeysPrefix exception", converterSettings, GPALObjectType.Other, ex);
                }

                int dictIdx = 0;
                int dictCount = ObjectCopier.GetItemCount(null, dict);

                foreach (var dict2 in dict)
                {
                    T element = (T)(object)InstantiateOne(converterSettings.OutputClassElementType);

                    dynamic retVal = null;
                    if (true == allKeysSame)
                        retVal = ConvertToClass(dict, converterSettings.OutputClassType, dict, null);
                    else
                        retVal = ConvertToClass(dict2.Value, converterSettings.OutputClassElementType, dict, null);

                    if (isArrayOutput)
                    {
                        int count = ObjectCopier.GetItemCount(null, retVal);

                        try
                        {
                            if (0 < count)
                                foreach (dynamic element2 in retVal)
                                {
                                    arrayOutput[elementIndex++] = (T)element2;
                                }
                            else
                                arrayOutput[elementIndex++] = (T)retVal;
                        }
                        catch (IndexOutOfRangeException)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{converterSettings.OutputClassType.Name}] does not have enough capacity for [{dictCount}] items. Returning [{elementIndex - 1}]", retVal, GPALObjectType.Other);
                            break;
                        }
                    }
                    else if (converterSettings.OutputClassType == retVal.GetType())
                        return retVal;
                    else
                    {
                        element = (T)retVal;

                        try
                        {

                            {
                                MethodInfo addMethod = ConverterSettings.OutputClassType.GetMethod("Add",
                                    BindingFlags.Public | BindingFlags.Instance, null, new[] { ConverterSettings.OutputClassElementType }, null);
                                MethodInfo addRowMethod = ConverterSettings.OutputClassType.GetMethod("AddRow",
                                    BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(List<>).MakeGenericType(ConverterSettings.OutputClassElementType) }, null);
                                MethodInfo enqueueMethod = ConverterSettings.OutputClassType.GetMethod("Enqueue",
                                    BindingFlags.Public | BindingFlags.Instance, null, new[] { ConverterSettings.OutputClassElementType }, null);
                                MethodInfo pushMethod = ConverterSettings.OutputClassType.GetMethod("Push",
                                    BindingFlags.Public | BindingFlags.Instance, null, new[] { ConverterSettings.OutputClassElementType }, null);

                                if (null != addRowMethod)
                                {
                                    converterSettings.OutputData = converterSettings.InputData[dictIdx];
                                    if (null != converterSettings.InDelimiter && null != converterSettings.OutDelimiter)
                                        converterSettings.OutputData = converterSettings.InputData[dictIdx].Replace(converterSettings.InDelimiter.Value, converterSettings.OutDelimiter.Value);
                                    string[] lines = converterSettings.OutputData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                                    foreach (object line in lines)
                                    {
                                        addRowMethod.Invoke(resultList, new object[] { new List<object>() { (dynamic)line } });
                                    }
                                }
                                else if (null != addMethod)
                                    addMethod.Invoke(resultList, new object[] { element });
                                else if (null != enqueueMethod)
                                    enqueueMethod.Invoke(resultList, new object[] { element });
                                else if (null != pushMethod)
                                    pushMethod.Invoke(resultList, new object[] { element });
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Can't find 'Add/Enqueue/Push' method for [{converterSettings.OutputClassType}]", converterSettings, GPALObjectType.Other);
                            }
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Can't 'Add' to [{converterSettings.OutputClassType}]", converterSettings, GPALObjectType.Other, ex);
                        }

                        if (false == allKeysSame)
                            break;
                    }
                    dictIdx++;
                }
                inputIndex++;
            }

            return isArrayOutput ? arrayOutput : resultList;
        }
        public static List<T> CreateListFromClass<T>(ConverterSettings converterSettings)
        {
            List<T> resultList = new List<T>();

            foreach (var data in converterSettings.InputDictionary)  // list<selectoset>
            {
                dynamic element = InstantiateOne(converterSettings.OutputClassElementType);
                element = ConvertToClass(new List<Dictionary<object, dynamic>>() { data }, converterSettings.OutputClassElementType, converterSettings.InputDictionary, null);
                //                LoadClassFromDictionary<T>(element, data);
                resultList.Add(element);
            }
            return resultList;
        }
        public static dynamic InstantiateOne(Type objectType, int arrayLength = 0)
        {
            if (objectType == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "ObjectType is null in InstantiateOne", null, GPALObjectType.Other);
                return null;
            }

            if (objectType.IsArray)
            {
                Type elementType = objectType.GetElementType();
                Array dynamicArray = Array.CreateInstance(elementType, arrayLength);
                for (int i = 0; i < arrayLength; i++)
                {
                    dynamicArray.SetValue(InstantiateOne(elementType), i);
                }
                return dynamicArray;
            }
            else if (objectType == typeof(string))
            {
                return string.Empty;
            }
            else if (objectType.IsValueType)
            {
                return ActivatorHelper.CreateInstance(objectType);
            }
            else if (IsAnonymousType(objectType))
            {
                return new System.Dynamic.ExpandoObject();
            }
            else if (ConverterHelper.IsDictionaryType(objectType))
            {
                try
                {
                    // First: Try to create the EXACT requested type
                    return ActivatorHelper.CreateInstance(objectType);
                }
                catch (Exception exInner)
                {
                    // If that fails (e.g., no parameterless ctor, abstract, interface),
                    // fall back to creating a concrete Dictionary<,>
                    try
                    {
                        Type[] genericArgs = objectType.IsGenericType
                            ? objectType.GetGenericArguments()
                            : new[] { typeof(object), typeof(object) };

                        Type keyType = genericArgs[0];
                        Type valueType = genericArgs.Length > 1 ? genericArgs[1] : typeof(object);

                        Type concreteDictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                        return ActivatorHelper.CreateInstance(concreteDictType);
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                            $"Failed to instantiate dictionary type [{objectType}] (fallback also failed: [{exInner.Message}])",
                            null, GPALObjectType.Other, ex);
                        return new System.Dynamic.ExpandoObject();
                    }
                }
            }
            else
            {
                try
                {
                    // Try parameterless constructor first
                    return ActivatorHelper.CreateInstance(objectType);
                }
                catch (MissingMethodException)
                {
                    // Try constructor with default parameters
                    var constructors = objectType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var ctor in constructors)
                    {
                        var parameters = ctor.GetParameters();
                        var args = new object[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var paramType = parameters[i].ParameterType;
                            if (paramType == typeof(string))
                                args[i] = string.Empty;
                            else if (paramType == typeof(int))
                                args[i] = 0;
                            else if (paramType == typeof(Dictionary<string, string>))
                                args[i] = new Dictionary<string, string>();
                            else if (paramType.IsValueType)
                                args[i] = ActivatorHelper.CreateInstance(paramType);
                            else
                                args[i] = null;
                        }
                        try
                        {
                            return ctor.Invoke(args);
                        }
                        catch
                        {
                            // Try next constructor
                        }
                    }
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No suitable constructor for type [{objectType}]", null, GPALObjectType.Other);
                    return null;
                }
            }
        }

        #region <Helpers>
        private static bool IsAnonymousType(Type type)
        {
            return Attribute.IsDefined(type, typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) &&
                   type.IsGenericType &&
                   type.Name.Contains("Anonymous") &&
                   (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }

        private static Complex ComplexFromDict(dynamic value)
        {
            // Unwrap single-item list (XML wraps every element in a list)
            dynamic dict = value;
            if (value is IEnumerable && !(value is IDictionary) && !(value is string))
                foreach (var item in (IEnumerable)value) { dict = item; break; }

            double real = 0, imaginary = 0;
            if (dict is IDictionary idict)
            {
                foreach (DictionaryEntry entry in idict)
                {
                    string rawEntryKey = entry.Key?.ToString() ?? string.Empty;
                    string key = rawEntryKey.StartsWith("GPALKEY", StringComparison.Ordinal)
                        ? rawEntryKey.Substring(7)
                        : (System.Text.RegularExpressions.Regex.IsMatch(rawEntryKey, @"^GPAL\d+_")
                            ? System.Text.RegularExpressions.Regex.Replace(rawEntryKey, @"^GPAL\d+_", "")
                            : rawEntryKey);

                    // Unwrap single-item list value (XML text nodes arrive as lists)
                    object val = entry.Value;
                    if (val is IEnumerable valEnum && !(val is string))
                        foreach (var v in valEnum) { val = v; break; }

                    string str = val?.ToString();
                    if (key.Equals("Real", StringComparison.OrdinalIgnoreCase))
                        double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out real);
                    else if (key.Equals("Imaginary", StringComparison.OrdinalIgnoreCase))
                        double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out imaginary);
                }
            }
            return new Complex(real, imaginary);
        }

        internal static dynamic ParseComplex(string complexString)
        {
            string s = complexString.Trim();

            string numPart = s.Substring(0, s.Length - 1); // remove 'i'

            int signIndex = Math.Max(numPart.LastIndexOf('+'), numPart.LastIndexOf('-'));
            if (signIndex == -1) throw new FormatException("No sign found");

            string realStr = numPart.Substring(0, signIndex);   // may be empty or "-"
            string imagStr = numPart.Substring(signIndex);     // "+4" or "-4"

            double real = string.IsNullOrEmpty(realStr) ? 0 : double.Parse(realStr, CultureInfo.InvariantCulture);
            double imag = double.Parse(imagStr, CultureInfo.InvariantCulture);

            return new Complex(real, imag);
        }
        // todo: handle dictionary, stack and hashset, add key parm
        public static bool AddToList(dynamic listQueueStackDictHashsetOrArray, dynamic value, int count)
        {
            bool retVal = true;
            value = EnsureCollectionType(listQueueStackDictHashsetOrArray, value);

            if ((true == IsEnumerableObject(listQueueStackDictHashsetOrArray) && false == IsArray(listQueueStackDictHashsetOrArray)) || true == IsList(listQueueStackDictHashsetOrArray))
            {
                // It's an IEnumerable<OutputClass>
                try
                {
                    MethodInfo addMethod = listQueueStackDictHashsetOrArray.GetType().GetMethod("Add",
                            BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo enqueueMethod = listQueueStackDictHashsetOrArray.GetType().GetMethod("Enqueue",
                            BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo pushMethod = listQueueStackDictHashsetOrArray.GetType().GetMethod("Push",
                            BindingFlags.Public | BindingFlags.Instance);

                    if (null != addMethod)
                    {
                        if (true == ConverterHelper.IsDictionaryType(listQueueStackDictHashsetOrArray.GetType()))
                            addMethod.Invoke(listQueueStackDictHashsetOrArray, new object[] { count, value });
                        else
                            addMethod.Invoke(listQueueStackDictHashsetOrArray, new object[] { value });
                    }
                    else if (null != enqueueMethod)
                        enqueueMethod.Invoke(listQueueStackDictHashsetOrArray, new object[] { value });
                    else if (null != pushMethod)
                        pushMethod.Invoke(listQueueStackDictHashsetOrArray, new object[] { value });
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Can't Add [{value}] to [{listQueueStackDictHashsetOrArray}]. May require a Convert.ChangeType", listQueueStackDictHashsetOrArray, GPALObjectType.Other, ex);
                    retVal = false;
                }

            }
            else if (true == IsArray(listQueueStackDictHashsetOrArray))
                listQueueStackDictHashsetOrArray[count] = EnsureCollectionType(listQueueStackDictHashsetOrArray, value);
            // json/yaml will collapse single item arrays/lists to a scalar which will cause a collection mismatch, thus ensurecollectiontypes


            return retVal;
        }
        /// <summary>
        /// json/yaml will collapse single item arrays/lists to a scalar which will cause a collection mismatch
        /// this method will look at the container and then item and ensure it is boxed to the c# collection type
        /// </summary>
        /// <param name="container"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static object EnsureCollectionType(object container, object value)
        {
            if (container == null || value == null) return value;
            Type containerType = container.GetType();

            // Inlined element type detection (e.g., List<Complex> -> Complex)
            Type targetElementType = containerType.IsArray ? containerType.GetElementType() : (containerType.IsGenericType ? containerType.GetGenericArguments()[0] : null);

            // If the value already matches the container (e.g., List matches List), return as is
            // If targetElementType is null, we can't determine how to wrap
            if (targetElementType == null || targetElementType.IsAssignableFrom(value.GetType()))
                return value;

            // Ensure the scalar matches the internal element type (converting if it's a string representation)
            object convertedItem = ConvertValue(value, targetElementType, container);

            // Now wrap the (potentially converted) item into the collection
            return WrapInCollection(targetElementType, convertedItem);
        }

        //private static object ConvertValue(object value, Type targetType)
        //{
        //    if (value == null || targetType.IsAssignableFrom(value.GetType())) return value;

        //    try
        //    {
        //        // Handle standard types (int, double, bool, etc.)
        //        if (typeof(IConvertible).IsAssignableFrom(targetType))
        //            return Convert.ChangeType(value, targetType);

        //        // Handle complex types from strings (Guid, IPAddress, etc.)
        //        var converter = System.ComponentModel.TypeDescriptor.GetConverter(targetType);
        //        if (converter != null && converter.CanConvertFrom(value.GetType()))
        //            return converter.ConvertFrom(value);
        //    }
        //    catch { /* Fallback to original value if conversion fails */ }

        //    return value;
        //}

        private static object WrapInCollection(Type typeToCreate, dynamic item)
        {
            // If the expected type is an array, create a 1-length array
            if (typeToCreate.IsArray)
            {
                Array newArray = Array.CreateInstance(typeToCreate.GetElementType(), 1);
                newArray.SetValue(item, 0);
                return newArray;
            }

            if (typeToCreate == item.GetType())
                return item;

            // If the expected type is a generic collection (List, Queue, Stack)
            object collection = Activator.CreateInstance(typeToCreate);

            // Find the correct method to add the item based on collection type
            MethodInfo addMethod = typeToCreate.GetMethod("Add")
                                   ?? typeToCreate.GetMethod("Push")
                                   ?? typeToCreate.GetMethod("Enqueue");

            addMethod?.Invoke(collection, new[] { item });
            return collection;
        }

        internal static string UppercaseFirst(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return char.ToUpper(input[0]) + input.Substring(1);
        }
        // written by grok
        static bool CheckKeysPrefix(Dictionary<dynamic, dynamic> dict)
        {
            if (!dict.Any()) return true; // Empty case

            string commonPrefix = null;

            foreach (var entry in dict.Values)
            {
                // Each entry.Value should be a dictionary-like object with one property
                // We need to get its keys (property names)

                // Using dynamic, we can enumerate keys via .GetEnumerator() or cast
                IEnumerable<string> innerKeys;

                try
                {
                    // Try to get the keys from the inner dynamic object
                    // This works if it's ExpandoObject or similar
                    var innerDict = (IDictionary<string, object>)entry;
                    innerKeys = innerDict.Keys;
                }
                catch
                {
                    // Fallback: reflect or handle other dynamic types
                    return false;
                }

                if (!innerKeys.Any())
                    return false; // Empty object?

                string currentKey = innerKeys.First(); // Assuming one key per object

                string prefix = GetPrefixUpToNumber(currentKey);

                if (string.IsNullOrEmpty(prefix))
                    return false;

                if (commonPrefix == null)
                {
                    commonPrefix = prefix;
                }
                else if (currentKey != prefix && !currentKey.StartsWith(commonPrefix))
                {
                    return false;
                }
            }

            return true;
        }

        static string GetPrefixUpToNumber(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            int i = 0;
            while (i < key.Length && !char.IsDigit(key[i]))
                i++;

            return i > 0 ? key.Substring(0, i) : string.Empty;
        }
        public static Type GetConcreteType(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));

            // If type is already a concrete class (not abstract, not an interface), return it
            if (!type.IsInterface && !type.IsAbstract)
            {
                return type;
            }

            // If type is an interface or abstract, find a concrete implementation
            return type.Assembly.GetTypes()
                .FirstOrDefault(t => !t.IsInterface && !t.IsAbstract && type.IsAssignableFrom(t))
                ?? throw new InvalidOperationException($"No concrete implementation found for {type.Name}.");
        }
        private static object ConvertValue(dynamic value, Type targetType, dynamic parentDictionary = null, string propertyName = null)
        {
            string fromCtx = propertyName != null ? $" from [{propertyName}]" : string.Empty;

            // Handle null / empty / "null"
            bool isNullInput = false;

            if (targetType == typeof(object))
                return value; // passthrough

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
                isNullInput = false == isStruct
                ? value == null ||  // NOTE: comparing some types to null throws an exception, latest one valuetuple
                    (value is string str && !isCharTarget && string.IsNullOrWhiteSpace(str)) ||
                    (value is string s2 && s2.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
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

                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Cannot convert [{DescribeForLog(value)}] to non-nullable [{ShortTypeName(targetType)}]{fromCtx}. Returning default.",
                    null, GPALObjectType.Other);
                return Activator.CreateInstance(targetType);
            }

            Type underlyingTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // === Collection handling ===
            if (IsSupportedCollectionType(underlyingTargetType))
            {
                Type elementType = underlyingTargetType.IsArray
                    ? underlyingTargetType.GetElementType()!
                    : underlyingTargetType.GetGenericArguments()[0];

                IEnumerable<object> sourceItems = (value is IEnumerable enumerable && !(value is string))
                    ? enumerable.Cast<object>()
                    : new[] { value };

                var convertedItems = new List<object>();
                int index = 0;
                foreach (var item in sourceItems)
                {
                    object converted = ConvertValue(item, elementType);
                    convertedItems.Add(converted);
                    index++;
                }

                return CreateCollection(convertedItems, underlyingTargetType);
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
                }
            }

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
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Invalid enum value [{value}] for type [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default (0).",
                    null, GPALObjectType.Other);

                return Enum.ToObject(underlyingTargetType, 0);
            }

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
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Guid format: [{strValue2}]{fromCtx}. Returning Guid.Empty.", null, GPALObjectType.Other);
                    return Guid.Empty;
                }

                if (IsIpAddressType(underlyingTargetType))
                {
                    if (IPAddress.TryParse(trimmed, out IPAddress ip))
                        return ip;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid IPAddress format: [{strValue2}]{fromCtx}. Returning IPAddress.Any.", null, GPALObjectType.Other);
                    return IPAddress.Any;
                }

                if (IsUriType(underlyingTargetType))
                {
                    if (Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out Uri uri))
                        return uri;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Uri format: [{strValue2}]{fromCtx}. Returning null.", null, GPALObjectType.Other);
                    return null;
                }

                if (IsTimeSpanType(underlyingTargetType))
                {
                    if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out TimeSpan ts))
                        return ts;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid TimeSpan format: [{strValue2}]{fromCtx}. Returning TimeSpan.Zero.", null, GPALObjectType.Other);
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
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid DateTimeOffset format: [{strValue2}]{fromCtx}. Returning DateTimeOffset.MinValue.", null, GPALObjectType.Other);
                    return DateTimeOffset.MinValue;
                }

                if (IsBigIntegerType(underlyingTargetType))
                {
                    if (BigInteger.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger bi))
                        return bi;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger format: [{strValue2}]{fromCtx}. Returning 0.", null, GPALObjectType.Other);
                    return BigInteger.Zero;
                }

                if (IsComplexType(underlyingTargetType))
                {
                    try
                    {
                        return ParseComplex(trimmed);
                    }
                    catch
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Complex format: [{strValue2}]{fromCtx}. Returning 0+0i.", null, GPALObjectType.Other);
                        return new Complex(0, 0);
                    }
                }

                if (IsVersionType(underlyingTargetType))
                {
                    if (System.Version.TryParse(trimmed, out System.Version ver))
                        return ver;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid Version format: [{strValue2}]{fromCtx}. Returning 0.0.", null, GPALObjectType.Other);
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

            if (true == IsCustomStructType(underlyingTargetType))
            {
                try
                {
                    if (value.GetType() == underlyingTargetType)
                        return value;
                    else
                        return ConvertToClass(value, underlyingTargetType, parentDictionary, null);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"Failed to convert [{value}] [{ShortTypeName(value?.GetType())}] to [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default.",
                        null, GPALObjectType.Other, ex);
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
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid BigInteger value: [{value}]{fromCtx}. Returning 0.", null, GPALObjectType.Other);
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
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to convert BigInteger [{value}] to [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default.", null, GPALObjectType.Other, ex);
                        return Activator.CreateInstance(underlyingTargetType);
                    }
                }
                try
                {
                    return System.Convert.ChangeType(value, underlyingTargetType, CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"Failed to convert [{value}] [{ShortTypeName(value?.GetType())}] to [{ShortTypeName(underlyingTargetType)}]{fromCtx}. Returning default.",
                        null, GPALObjectType.Other, ex);
                    return Activator.CreateInstance(underlyingTargetType);
                }
            }

            // already the right type — no conversion needed
            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

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
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to reconstruct [{ShortTypeName(targetType)}] from dict{fromCtx}", value, GPALObjectType.None, ex);
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
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to reconstruct [{ShortTypeName(targetType)}] from unwrapped dict{fromCtx}", value, GPALObjectType.None, ex);
                    }
                }
            }

            // === Final fallback ===
            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                $"Unable to convert [{DescribeForLog(value)}] to [{ShortTypeName(targetType)}]{fromCtx}",
                null, GPALObjectType.Other);
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

        private static object CreateCollection(List<object> convertedItems, Type targetCollectionType)
        {
            Type underlying = targetCollectionType;
            if (targetCollectionType.IsGenericType &&
                targetCollectionType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                underlying = Nullable.GetUnderlyingType(targetCollectionType)!;
            }

            if (underlying.IsArray)
            {
                Type elementType = underlying.GetElementType()!;
                Array array = Array.CreateInstance(elementType, convertedItems.Count);
                for (int i = 0; i < convertedItems.Count; i++)
                {
                    object converted = ConvertValue(convertedItems[i], elementType);
                    if (converted == null || elementType.IsAssignableFrom(converted.GetType()))
                        array.SetValue(converted, i);
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to convert [{DescribeForLog(converted)}] to [{elementType.Name}]", converted, GPALObjectType.None);
                }
                return array;
            }

            // Handle IList types: List<T>, IList<T>, etc.
            if (underlying.IsGenericType)
            {
                Type[] genericArgs = underlying.GetGenericArguments();
                Type elementType = genericArgs[0];

                Type listType = typeof(List<>).MakeGenericType(elementType);
                IList list = (IList)Activator.CreateInstance(listType)!;

                foreach (var rawItem in convertedItems)
                {
                    object convertedItem = ConvertValue(rawItem, elementType);
                    if (convertedItem == null || elementType.IsAssignableFrom(convertedItem.GetType()))
                        list.Add(convertedItem);
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to convert [{DescribeForLog(convertedItem)}] to [{elementType.Name}]", convertedItem, GPALObjectType.None);
                }

                // If the original target was exactly List<T>, return it
                if (underlying.IsGenericType &&
                    underlying.GetGenericTypeDefinition() == typeof(List<>))
                {
                    return list;
                }

                // Otherwise, try to construct the exact target type (e.g. ObservableCollection<T>, etc.)
                // Fallback to List<T> if constructor fails
                try
                {
                    var targetListType = targetCollectionType; // may still be underlying
                    var ctor = targetListType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(elementType) });
                    if (ctor != null)
                        return ctor.Invoke(new object[] { list });

                    // Another common pattern: constructor taking ICollection<T> or just List<T>
                    ctor = targetListType.GetConstructor(new[] { listType });
                    if (ctor != null)
                        return ctor.Invoke(new object[] { list });
                }
                catch { /* ignore - fall through */ }

                // Final fallback
                return list;
            }

            // Fallback for non-generic collections (rare)
            IList nonGenericList = new ArrayList();
            foreach (var item in convertedItems)
                nonGenericList.Add(item);
            return nonGenericList;
        }

        private static object ConvertDictionaryToStruct(
            IDictionary<string, object> dict,
            Type structType,
            HashSet<object> visited)
        {
            // Create default struct instance
            object instance = Activator.CreateInstance(structType);

            foreach (var prop in structType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite)
                    continue;

                if (dict.TryGetValue(prop.Name, out var rawValue))
                {
                    object converted = ConvertValue(
                        rawValue,
                        prop.PropertyType,
                        rawValue);

                    prop.SetValue(instance, converted);
                }
            }

            return instance;
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
                    bool hasComplexKey = dict.Keys.Cast<object>().Any(k => k != null && false == IsSimpleType(k.GetType()));
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

                    // Pure generic dict, all-simple keys — normalize children recursively, but keep as-is at this level
                    var result = new Dictionary<string, object>();
                    foreach (var kvp in dict)
                    {
                        string key = kvp.Key.ToString().Replace("@GPAL_", "");
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
                    if (double.TryParse(textValue, out double num))
                        return num;
                    return textValue;
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
        public static object StripGpalKeys(object input)
        {
            // Phase 1: Strip keys and correctly group repeated siblings
            object phase1 = StripAndGroup(input);

            // Phase 2: Recursively unwrap unnecessary single-item lists
            return UnwrapSingleItemLists(phase1);
        }

        private static object StripAndGroup(object input)
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
                    else if (cleanedKey.StartsWith("@"))
                        cleanedKey = cleanedKey.Substring(1);

                    object value = StripAndGroup(entry.Value);

                    // ConvertXmlNodeToDictionary always wraps each child node in a List<Dict> (even when there is
                    // only one child with that tag name). Unwrap those single-item dict-lists here so that sibling
                    // elements group correctly: e.g. three <item> siblings produce [pt1,pt2,pt3] not
                    // [[pt1],[pt2],[pt3]]. Only unwrap when the single item is a dict; scalar single-item lists
                    // (e.g. ["X"]) must stay wrapped so they round-trip as single-element collections.
                    if (value is List<object> singleWrap && singleWrap.Count == 1 && singleWrap[0] is IDictionary)
                        value = singleWrap[0];

                    // If this is text content with no key (common for mixed content elements like color_swatch)
                    if (string.IsNullOrEmpty(cleanedKey) && value is string str && !string.IsNullOrWhiteSpace(str))
                        cleanedKey = "value";
                    // Flatten pure text nodes
                    else if (cleanedKey == "#text")
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
                if (result.Count == 1 && result.ContainsKey("value"))
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
                    list.Add(StripAndGroup(item));
                }
                return list.Count == 0 ? null : list;
            }

            return input;
        }

        private static object UnwrapSingleItemLists(object input)
        {
            if (input == null) return null;

            // If it's a list with exactly one item -> unwrap it only if scalar (XML text node)
            // OR if inner is itself a list (collapsed from an {item: [...]} XML wrapper).
            if (input is IList list && list.Count == 1)
            {
                object inner = UnwrapSingleItemLists(list[0]);
                if (inner == null || inner is string || inner is ValueType)
                    return inner;
                // Rule 3: if the inner item resolved to a list (e.g. from {item:[...]} collapse),
                // return it directly so the outer single-item wrapper doesn't re-box it.
                if (inner is IList)
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
                    unwrapped.Add(UnwrapSingleItemLists(item));
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
                    object value = UnwrapSingleItemLists(entry.Value);
                    result[key] = value;
                }
                // Collapse pure text-node singleton
                if (result.Count == 1 && result.ContainsKey("value"))
                    return result["value"];
                // Rule 2: collapse XML {item: [...]} wrapper dicts — "item" is the generic
                // element tag name GPAL uses for array children, not a real property name.
                // Scalar value means a single-element array was serialized; wrap it back in a list.
                if (result.Count == 1 && result.ContainsKey("item"))
                {
                    var itemVal = result["item"];
                    return itemVal is IList ? itemVal : new List<object> { itemVal };
                }
                return result;
            }

            // Scalar -> return as-is
            return input;
        }
        internal static string GetXmlHeaderFromFile(string inputFilename)
        {
            if (!File.Exists(inputFilename))
                return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

            var lines = File.ReadAllLines(inputFilename);
            var headerLines = new List<string>();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();

                // Stop when we hit the first actual element (starts with < followed by letter)
                if (trimmed.StartsWith("<") && !trimmed.StartsWith("<?") && !trimmed.StartsWith("<!DOCTYPE") && !trimmed.StartsWith("<!--"))
                {
                    break;
                }

                // Capture declaration, PI, DOCTYPE, comments if you want
                if (trimmed.StartsWith("<?xml") ||
                    trimmed.StartsWith("<?xml-stylesheet") ||
                    trimmed.StartsWith("<!DOCTYPE") ||
                    trimmed.StartsWith("<!--"))
                {
                    headerLines.Add(line); // preserve original indentation/whitespace
                }
            }

            // Fallback if nothing found
            if (headerLines.Count == 0)
                return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

            return string.Join(Environment.NewLine, headerLines) + Environment.NewLine;
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
        internal static List<string> SplitRespectingQuotes(string line, char delimiter)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // Handle escaped quotes ("")
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    tokens.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else if (c == delimiter && inQuotes)
                {
                    // An embedded delimiter is only safe inside its original quotes. Once unquoted here,
                    // a literal delimiter would re-split this token on a later re-parse/output pass -
                    // replace it with a space instead of carrying it through.
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }

            tokens.Add(sb.ToString().Trim());
            return tokens;
        }
        internal static string ExtractRawXmlFromBrowserSource(string fullPageSource)
        {
            if (string.IsNullOrWhiteSpace(fullPageSource))
                return string.Empty;

            // Look for the <div id="webkit-xml-viewer-source-xml"> ... </div>
            int startIndex = fullPageSource.IndexOf("<div id=\"webkit-xml-viewer-source-xml\">");
            if (startIndex == -1)
            {
                // Not wrapped — maybe it's already raw XML
                if (fullPageSource.TrimStart().StartsWith("<?xml"))
                    return fullPageSource;

                GPAL.PublishSimpleEvent(
                    GPALEventType.DEBUG,
                    "No webkit-xml-viewer wrapper found and content doesn't start with <?xml. Returning empty.",
                    fullPageSource,
                    GPALObjectType.Other);
                return string.Empty;
            }

            startIndex += "<div id=\"webkit-xml-viewer-source-xml\">".Length;

            int endIndex = fullPageSource.IndexOf("</div>", startIndex);
            if (endIndex == -1) endIndex = fullPageSource.Length;

            string innerHtml = fullPageSource.Substring(startIndex, endIndex - startIndex).Trim();

            // The inner content is usually the raw XML as text nodes
            // But in your sample, it's already the <sitemapindex>...</sitemapindex>
            // Sometimes it's escaped or has extra spans — strip if needed

            // Quick clean-up if there are any HTML artifacts (rare for sitemaps)
            innerHtml = innerHtml.Replace("<![CDATA[", "").Replace("]]>", ""); // if CDATA wrapped

            return innerHtml;
        }
        #endregion <Helpers>
    }
    /// <summary>
    /// Custom objet factory for our classes which do not have public constructors
    /// </summary>
    public static class ObjectFactory
    {
        /// <summary>
        /// Creates an instance of the specified type using reflection, looking for non-public constructors if necessary.
        /// </summary>
        /// <typeparam name="T">The type to instantiate.</typeparam>
        /// <returns>An instance of T.</returns>
        public static T CreateInstance<T>() where T : class
        {
            Type type = typeof(T);
            var constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null
            );

            if (constructor != null)
            {
                return (T)constructor.Invoke(new object[0]);
            }
            else
            {
                throw new InvalidOperationException($"No suitable constructor found for type {type.Name}.");
            }
        }
    }
}
