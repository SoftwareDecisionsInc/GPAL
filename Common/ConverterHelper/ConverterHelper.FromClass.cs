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
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to getvalue of property [{property.Name}][{propertyType}]. Continuing and hoping for the best.", (object)myClass, GPALObjectType.Other, ex);
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
        // A value held in a member typed object is kept as it is held, unless it holds a class of the framework, such as a
        // FileInfo, holds a list or dictionary that holds itself, or nests NestingLimit lists, dictionaries and classes
        // deep, such as a property that returns a new object on each read; then a copy of it is kept, where the class of
        // the framework is its text and the list, dictionary or class that would repeat or go deeper is its type name.
        // visited holds the classes, lists and dictionaries around it
        private static object BoundedValue(object value, HashSet<object> visited)
        {
            object retVal = value;

            if (true == RunsOn(value, visited))
                retVal = BoundedCopy(value, visited);

            return retVal;
        }
        // The values a list, dictionary or class holds: a dictionary's values, a list's items and the value of a pair, a
        // class's properties and fields; a property that cannot be read is null here
        private static IEnumerable<object> HeldValues(object value)
        {
            if (value is IDictionary dictionary)
            {
                foreach (object item in dictionary.Values)
                    yield return item;
            }
            else if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    yield return true == IsKeyValuePairType(item?.GetType()) ? PairMember(item, "Value") : item;
            }
            else
            {
                foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    object propertyValue = null;
                    if (true == property.CanRead && 0 == property.GetIndexParameters().Length)
                    {
                        try
                        {
                            propertyValue = property.GetValue(value);
                        }
                        catch
                        {
                            // a property that cannot be read holds nothing to walk
                        }
                    }
                    yield return propertyValue;
                }
                foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    yield return field.GetValue(value);
            }
        }
        // The Key or Value of a pair, read by reflection: dynamic cannot see them when the pair's types are not public
        private static object PairMember(object pair, string memberName)
        {
            return pair.GetType().GetProperty(memberName).GetValue(pair);
        }
        // True when the value holds a class of the framework, a list or dictionary around it, or is NestingLimit lists,
        // dictionaries, classes and structs deep in them. Otherwise the value is kept as it is, a link back to a class
        // around it included, and the output's own writer handles that link
        private static bool RunsOn(object value, HashSet<object> visited)
        {
            bool runsOn = false;
            Type valueType = value?.GetType();

            if (null == valueType || value is string || ((true == valueType.IsValueType || true == IsSimpleType(valueType)) && false == IsMemberStruct(valueType)))
                runsOn = false;
            else if (true == IsFrameworkObjectType(valueType))
                runsOn = true;
            else if (true == visited.Contains(value))
                runsOn = value is IEnumerable;
            else if (NestingLimit <= visited.Count)
                runsOn = true;
            else if (value is IEnumerable || true == IsClassType(valueType) || true == valueType.IsValueType)
            {
                visited.Add(value);
                try
                {
                    foreach (object item in HeldValues(value))
                    {
                        runsOn = RunsOn(item, visited);
                        if (true == runsOn)
                            break;
                    }
                }
                finally
                {
                    visited.Remove(value);
                }
            }

            return runsOn;
        }
        // A copy of the value: a list is a List<object>, a dictionary a Dictionary<object, dynamic> and a class its
        // record, as ConvertClassToDictionary makes it; a class of the framework is its text, and a list or dictionary
        // around it, or one NestingLimit deep, is its type, as a class or struct ClassDepthLimit deep is
        private static object BoundedCopy(object value, HashSet<object> visited)
        {
            object retVal = value;
            Type valueType = value?.GetType();

            if (null == valueType || value is string || ((true == valueType.IsValueType || true == IsSimpleType(valueType)) && false == IsMemberStruct(valueType)))
                retVal = value;
            else if (true == IsFrameworkObjectType(valueType))
                retVal = value.ToString();
            else if ((value is IEnumerable && true == visited.Contains(value)) || NestingLimit <= visited.Count)
                retVal = $"{valueType}";
            else if (value is IDictionary dictionary)
            {
                Dictionary<object, dynamic> copy = new Dictionary<object, dynamic>();
                visited.Add(value);
                try
                {
                    foreach (DictionaryEntry entry in dictionary)
                        copy[entry.Key] = BoundedCopy(entry.Value, visited);
                }
                finally
                {
                    visited.Remove(value);
                }
                retVal = copy;
            }
            else if (value is IEnumerable enumerable)
            {
                List<object> copy = new List<object>();
                visited.Add(value);
                try
                {
                    foreach (object item in enumerable)
                        copy.Add(true == IsKeyValuePairType(item?.GetType())
                            ? new KeyValuePair<object, object>(PairMember(item, "Key"), BoundedCopy(PairMember(item, "Value"), visited))
                            : BoundedCopy(item, visited));
                }
                finally
                {
                    visited.Remove(value);
                }
                retVal = copy;
            }
            else if (ClassDepthLimit <= visited.Count)
                retVal = $"{valueType}";
            else
                retVal = ConvertClassToDictionary(value, "", visited);

            return retVal;
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

            // A string of the class is written as the class holds it, "" as "" and "null" as that text. ConvertValue reads
            // the text null as a null unless the first input is json, yaml or a class
            dynamic ClassValue(dynamic classValue, Type valueType) => classValue is string ? classValue : ConvertValue(BoundedValue((object)classValue, visited), valueType);
            // A class of the framework, such as a FileInfo or a Type, is written as its text, and a class, struct, list or
            // dictionary ClassDepthLimit of them deep, such as one a property builds anew on each read, as its type, so
            // the writing ends; another is written as its record
            dynamic ClassRecord(object classValue, string className) => true == IsFrameworkObjectType(classValue?.GetType()) ? (object)classValue.ToString()
                : null != classValue && ClassDepthLimit <= visited.Count ? (object)$"{classValue.GetType()}"
                : ConvertClassToDictionary(classValue, className, visited);

            // Compared as object: a dynamic struct such as a KeyValuePair has no == with null
            if (false == IsCustomStructType(myClass?.GetType()) && false == IsTupleType(myClass?.GetType()) && null == (object)myClass)
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

            }

            // Every class, struct, tuple, list and dictionary being written is in visited until it is written, so
            // visited.Count is how deep the writing is; a struct is its own boxed copy and never matches another. A pair
            // holds what it was given, so it cannot go on without end and is not counted. Only the current call-stack
            // path is tracked, so shared references are not false-positive cycles
            bool counted = false == IsKeyValuePairType(myClass.GetType());
            if (true == counted)
                visited.Add(myClass);
            try
            {

                Type myClassType = myClass?.GetType();
                var properties = (from p in myClassType?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                  group p by p.Name into g
                                  select g.OrderByDescending(t => t.DeclaringType == myClassType).First());

                FieldInfo[] fields = myClassType?.GetFields(BindingFlags.Public | BindingFlags.Instance);
                List<dynamic> list = new List<dynamic>();

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

                // A KeyValuePair or DictionaryEntry is a record of its Key and its Value. Its properties are declared
                // in System.Collections, which the property loop below skips, so they are read here and nothing is
                // added after.
                if (true == IsKeyValuePairType(myClassType))
                {
                    // read by reflection: dynamic cannot see Key and Value when the pair's types are not public
                    foreach (string memberName in new[] { "Key", "Value" })
                    {
                        dynamic memberValue = myClassType.GetProperty(memberName).GetValue((object)myClass);
                        object outputValue = null == (object)memberValue
                            ? null
                            : IsSimpleType(memberValue.GetType()) && false == IsMemberStruct(memberValue.GetType())
                                ? (object)ClassValue(memberValue, memberValue.GetType())
                                : ClassRecord(memberValue, memberName);
                        dict.Add(memberName, outputValue);
                    }
                }
                // Handle dictionary types
                else if (IsDictionaryType(myClassType))
                {
                    // A complex/class-typed key falls back to Object.ToString() (the type's full name for
                    // every instance), so every entry would collide on the same string past the first -
                    // Dictionary<object, dynamic> already accepts non-string keys, so keep the key object
                    // itself instead. A simple-typed key (string, int, enum, etc.) keeps its own type/value
                    // via ConvertValue rather than being coerced to a string, so e.g. an int key round-trips
                    // back as an int, not "2".
                    void AddEntry(dynamic entryKey, dynamic entryValue)
                    {
                        // a struct of the program's own, such as a MyS key, is kept as it is, as a class key is, so two of
                        // them stay two keys
                        object outputKey = IsSimpleType(entryKey?.GetType()) && false == IsMemberStruct(entryKey?.GetType()) ? (object)ClassValue(entryKey, entryKey?.GetType()) : entryKey;
                        // A simple-typed value (string, int, etc.) has no properties/fields to flatten -
                        // recursing it through ConvertClassToDictionary would just return an empty dict,
                        // discarding the value entirely (same reasoning as the key handling above).
                        // A null value (a JSON null) stays null: it has no type to convert it to, and ConvertValue
                        // with no target type fails. Compared as object: a dynamic struct such as a ValueTuple has no
                        // == with null.
                        object outputValue = null == (object)entryValue
                            ? null
                            : IsSimpleType(entryValue.GetType()) && false == IsMemberStruct(entryValue.GetType())
                                ? (object)ClassValue(entryValue, entryValue.GetType())
                                : ClassRecord(entryValue, entryKey?.ToString());
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
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to convert class [{itemType}] Key [{keyProp?.Name}]/Value [{valueProp?.Name}] to dictionary.", (object)myClass, GPALObjectType.Other);
                            }
                        }
                    }
                    return dict;
                }
                else if (IsEnumerableType(myClassType))
                {
                    int idx = 0;

                    // An array of two or more dimensions is a list of its rows, each row a list of the next dimension, so
                    // new int[,] { { 1, 2 }, { 3, 4 } } is [[1, 2], [3, 4]], where it was read as one-dimensional
                    List<object> Rows(Array grid, int dimension, int[] at)
                    {
                        List<object> rows = new List<object>();
                        for (int index = 0; index < grid.GetLength(dimension); index++)
                        {
                            at[dimension] = grid.GetLowerBound(dimension) + index;
                            rows.Add(dimension + 1 < grid.Rank ? Rows(grid, dimension + 1, at) : grid.GetValue(at));
                        }
                        return rows;
                    }

                    IEnumerable items = myClass is Array multiDimensional && 1 < multiDimensional.Rank
                        ? Rows(multiDimensional, 0, new int[multiDimensional.Rank])
                        : (IEnumerable)myClass;

                    foreach (dynamic item in items)
                    {
                        Type elementType = item?.GetType();

                        string keyName = $"GPALKEY{idx:D4}";

                        // a null item keeps its place as a null, which json and yaml write as null and the other formats as the text null
                        if (null == elementType)
                        {
                            dict.Add(keyName, null);
                        }
                        else if (IsSimpleType(elementType) && false == IsMemberStruct(elementType))
                        {
                            dict.Add(keyName, ClassValue(item, elementType));
                        }
                        else if (IsComplexType(elementType))
                        {
                            string sign = item.Imaginary >= 0 ? "+" : "";
                            dict.Add(keyName, $"{item.Real.ToString(CultureInfo.InvariantCulture)}{sign}{item.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                        }
                        else if ((IsClassType(elementType) || IsMemberStruct(elementType)) && !IsEnumerableType(elementType) && !IsDictionaryType(elementType) && !elementType.IsArray)
                        {
                            dict.Add(keyName, ClassRecord(item, ""));
                        }
                        else if (IsDictionaryType(elementType) || IsEnumerableType(elementType) || elementType.IsArray)
                        {
                            dict.Add(keyName, ClassRecord(item, ""));
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported element type [{elementType}] in enumerable [{myClassType}]", (object)item, GPALObjectType.Other);
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
                            bool valueRead = false;
                            try
                            {
                                value = property.GetValue(myClass);
                                valueRead = true;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to getvalue of property [{property.Name}][{propertyType}]. Continuing and hoping for the best.", (object)myClass, GPALObjectType.Other, ex);
                            }

                            // a property that could not be read is left out
                            if (false == valueRead)
                                continue;

                            // a null property is kept as a null, which json and yaml write as null and the other formats as the text null
                            if (null == (object)value)
                                dict.Add(property.Name, null);
                            else if (false == IsTupleType(value?.GetType()))
                            {
                                // an array of two or more dimensions is a list of its rows
                                if (value is Array multiDimensional && 1 < multiDimensional.Rank)
                                {
                                    dict.Add(property.Name, ClassRecord(value, property.Name));
                                }
                                else if (propertyType.IsArray)
                                {
                                    var arrayList = (Array)property.GetValue(myClass);
                                    if (null != arrayList)
                                        for (int idx = 0; idx < arrayList.Length; idx++)
                                        {
                                            var elementValue = arrayList.GetValue(idx);
                                            var elementType = elementValue?.GetType() ?? typeof(object);


                                            if (IsSimpleType(elementType) && false == IsMemberStruct(elementType))
                                            {
                                                list.Add(ClassValue(elementValue, elementType));
                                            }
                                            else if (elementValue is Array multiDimensionalItem && 1 < multiDimensionalItem.Rank)
                                            {
                                                list.Add(ClassRecord(elementValue, $"{property.Name}{idx}"));
                                            }
                                            else if (IsEnumerableType(elementType))
                                            {
                                                list.Add(BoundedValue(elementValue, visited));
                                            }
                                            else if (IsDictionaryType(elementType))
                                            {
                                                list.Add(ClassRecord(elementValue, $"{property.Name}{idx}"));
                                            }
                                            else if (IsMemberStruct(elementType) || IsCustomStructType(elementType) || IsClassType(elementType) || IsTupleType(elementType))
                                            {
                                                list.Add(ClassRecord(elementValue, $"{property.Name}{idx}"));
                                            }
                                        }
                                    else
                                        list.Add(ValueToString(arrayList));

                                    // an empty array is kept as an empty list
                                    dict.Add(property.Name, list);
                                }
                                // a pair, also one held in a property typed object, is a record of its Key and Value
                                else if (true == IsKeyValuePairType(((object)value).GetType()))
                                {
                                    dict.Add(property.Name, ClassRecord(value, property.Name));
                                }
                                else if (IsSimpleType(propertyType) && false == IsMemberStruct(Nullable.GetUnderlyingType(propertyType) ?? propertyType))
                                {
                                    dict.Add(property.Name, ClassValue(value, propertyType));
                                }
                                else if (IsDictionaryType(propertyType))
                                {
                                    dict.Add(property.Name, ClassRecord(value, $"{propertyType}"));
                                }
                                else if (IsEnumerableType(propertyType))
                                {
                                    int idx = 0;
                                    if (null != value)
                                        foreach (var item in value)
                                        {
                                            var elementType = item?.GetType() ?? typeof(object);
                                            if (true == IsCustomStructType(item?.GetType()) || true == IsMemberStruct(item?.GetType()))
                                            {
                                                list.Add(ClassRecord(item, $"{property.Name}{idx}"));
                                            }
                                            //else if (item == null)
                                            //{
                                            //    list.Add(null);
                                            //}
                                            else if (IsSimpleType(elementType))
                                            {
                                                list.Add(ClassValue(item, elementType));
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
                                            else if (IsEnumerableType(elementType) && false == (item is Array multiDimensionalItem && 1 < multiDimensionalItem.Rank))
                                            {
                                                list.Add(BoundedValue(item, visited));
                                            }
                                            else
                                            {
                                                list.Add(ClassRecord(item, $"{property.Name}{idx}"));
                                            }
                                            idx++;
                                        }
                                    else
                                        list.Add(ValueToString(value));

                                    // an empty list is kept as an empty list
                                    dict.Add(property.Name, list);
                                }
                                else if (IsComplexType(propertyType))
                                {
                                    string sign = value.Imaginary >= 0 ? "+" : "";
                                    dict.Add(property.Name, $"{value.Real.ToString(CultureInfo.InvariantCulture)}{sign}{value.Imaginary.ToString(CultureInfo.InvariantCulture)}i");
                                }
                                else if (IsClassType(propertyType) || IsMemberStruct(Nullable.GetUnderlyingType(propertyType) ?? propertyType) || IsCustomStructType(value?.GetType()))
                                {
                                    dict.Add(property.Name, ClassRecord(value, property.Name));
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unknown property type [{myClass.GetType()}][{propertyType}] detected. Adding [{value}] to dict.", (object)myClass, GPALObjectType.Other);
                                    dict.Add(property.Name, value);
                                }
                            }
                            else // IsTupleType: serialize tuple fields (Item1/Item2/...) as a keyed dict
                            {
                                dict.Add(property.Name, ClassRecord(value, property.Name));
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
                            // a pair, also one held in a field typed object, is a record of its Key and Value
                            if (true == IsKeyValuePairType(value?.GetType()))
                            {
                                dict.Add(field.Name, ClassRecord(value, field.Name));
                            }
                            else if (IsSimpleType(field.FieldType) && false == IsMemberStruct(Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType))
                            {
                                dict.Add(field.Name, ClassValue(value, field.FieldType));
                            }
                            else if (IsDictionaryType(field.FieldType))
                            {
                                dict.Add(field.Name, ClassRecord(value, field.Name));
                            }
                            // an array of two or more dimensions is a list of its rows
                            else if (value is Array multiDimensional && 1 < multiDimensional.Rank)
                            {
                                dict.Add(field.Name, ClassRecord(value, field.Name));
                            }
                            else if (IsEnumerableType(field.FieldType))
                            {
                                int idx = 0;
                                list = new List<dynamic>();
                                foreach (object item in (IEnumerable)value)
                                {
                                    Type itemType = item?.GetType() ?? typeof(object);
                                    list.Add(IsSimpleType(itemType) && false == IsMemberStruct(itemType) ? ClassValue(item, itemType) : ClassRecord(item, $"{itemType}{idx}"));
                                    idx++;
                                }
                                dict.Add(field.Name, list);
                            }
                            else
                            {
                                dict.Add(field.Name, ClassRecord(value, field.Name));
                            }
                        }
                        // a null field is kept as a null, as a null property is
                        else
                            dict.Add(field.Name, null);
                    }
                }

                return dict;

            } // end try
            finally
            {
                if (true == counted)
                    visited.Remove(myClass);
            }
        }
        public static dynamic CreateIEnumerableFromClass<T>(ConverterSettings converterSettings)
        {
            IEnumerable<T> resultList = null;
            bool isArrayOutput = converterSettings.OutputClassType.IsArray;
            // an array holds the items converted, as many as there are
            List<T> arrayItems = new List<T>();

            if (false == isArrayOutput)
                resultList = (IEnumerable<T>)ActivatorHelper.CreateInstance(converterSettings.OutputClassType);

            int inputIndex = 0;
            foreach (var dict in converterSettings.InputDictionary)
            {
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

                foreach (var dict2 in dict)
                {
                    // a row that fills nothing of the item is not taken
                    if (false == allKeysSame && true == FillsNothing(dict2.Value, typeof(T), null, dict))
                    {
                        dictIdx++;
                        continue;
                    }

                    T element = (T)(object)InstantiateOne(converterSettings.OutputClassElementType);

                    dynamic retVal = null;
                    bool rowNotConverted = false;
                    // a null row is a null item, such as the second item of the json [{...}, null], and so is a csv or xlsx row
                    // written for a null record
                    if (false == allKeysSame && (false == typeof(T).IsValueType || null != Nullable.GetUnderlyingType(typeof(T)))
                        && (null == (object)dict2.Value || true == IsNullRecordRow(dict2.Value)))
                        retVal = null;
                    else if (true == allKeysSame)
                        retVal = ConvertToClass(dict, converterSettings.OutputClassType, dict, null);
                    // an array of pairs given a dictionary takes each of its entries, as a List of pairs does; the
                    // dictionary goes under its own key, so one entry holding an object is not opened as the wrapper
                    else if (true == isArrayOutput && true == IsKeyValuePairType(typeof(T)) && null != PairRecords(dict2.Value, typeof(T)))
                        retVal = ConvertToClass(new Dictionary<object, dynamic> { [dict2.Key] = dict2.Value }, typeof(List<T>), dict, null);
                    // a line of text given to an item that is a collection, such as hello for an int[], is one value of
                    // it, as a csv row of one value is, not its characters
                    else if (dict2.Value is string rowText && true == IsEnumerableType(typeof(T)) && false == IsDictionaryType(typeof(T))
                        && typeof(string) != typeof(T) && typeof(char) != CollectionItemType(typeof(T)))
                        retVal = ConvertToClass(new List<object> { rowText }, converterSettings.OutputClassElementType, dict, null);
                    // a row of one value, such as a line of a csv file of one column, given to an item read from text, such
                    // as an int, is that value; one that does not convert gives no item, with a warning
                    else if (dict2.Value is IList oneValueRow && 1 == oneValueRow.Count && false == oneValueRow[0] is IDictionary && false == oneValueRow[0] is IList
                        && (true == IsValueAlone(typeof(T)) || true == HasTextConverter(typeof(T))))
                    {
                        retVal = ConvertListItem(oneValueRow[0], typeof(T));
                        rowNotConverted = valueNotConverted;
                    }
                    else
                        retVal = ConvertToClass(dict2.Value, converterSettings.OutputClassElementType, dict, null);

                    // a row of values none of which converts to the item's values, such as the line x or null for an
                    // int[], gives no item, with a warning for each value
                    if (true == rowNotConverted
                        || (false == allKeysSame && (dict2.Value is string || dict2.Value is IList) && true == IsEnumerableType(typeof(T)) && typeof(string) != typeof(T)
                            && (object)retVal is IEnumerable rowItems && false == rowItems.Cast<object>().Any()
                            && (dict2.Value is string || 0 < ((IList)dict2.Value).Count)))
                    {
                        dictIdx++;
                        continue;
                    }

                    if (isArrayOutput)
                    {
                        int count = null == (object)retVal ? 0 : ObjectCopier.GetItemCount(null, retVal);

                        // an item that is itself a collection, such as the int[] of an int[][], is one item
                        if (0 < count && typeof(T) != ((object)retVal).GetType())
                            foreach (dynamic element2 in retVal)
                                arrayItems.Add((T)element2);
                        else
                            arrayItems.Add((T)retVal);

                        // rows that all convert at once gave every item, so they are not converted again for each row
                        if (true == allKeysSame)
                            break;
                    }
                    else if (null != (object)retVal && converterSettings.OutputClassType == retVal.GetType())
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

                        // rows keyed by position each give an item, as they do an array; anything else gave its one item
                        if (false == allKeysSame && false == ((IEnumerable<object>)dict.Keys).All(rowKey => Regex.IsMatch(rowKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")))
                            break;
                    }
                    dictIdx++;
                }
                inputIndex++;
            }

            return isArrayOutput ? arrayItems.ToArray() : resultList;
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
    }
}
