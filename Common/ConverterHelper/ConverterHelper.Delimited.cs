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
        internal static string ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings converterSettings, out IGPALGrid<string> outGrid, DataFormat inputFormat = DataFormat.NOTSET)
        {
            StringBuilder sb = new StringBuilder();
            StringBuilder sb2 = new StringBuilder();
            IGPALGrid<string> outputGrid = GPAL.Grid.ToGPALObject();
            List<string> gridRow = new List<string>();
            List<Dictionary<object, dynamic>> rows = new List<Dictionary<object, dynamic>>();
            int singleValueRows = 0;   // rows that are one plain value, such as the items of ["x", "y"]
            int valueListRows = 0;     // rows that are a list of plain values, such as the rows of a List<List<string>>
            List<string> flattenLastMessage = new List<string>();
            bool flattenSupressedMessage = false;
            // how many classes held as objects, in a member typed object or a Dictionary<string, object>, are being written
            // as their properties, around the value being written; a class inside one of them is written as its type
            int objectClassesOpen = 0;

            // A YAML file's top-level mapping is one record, as a JSON or XML object is. The YAML reader keeps the
            // mapping's fields as they are, where the JSON reader puts the object under "root", so the mapping is
            // taken whole. A YAML list or single value is under GPALKEY keys and goes by its values, as before.
            Dictionary<object, dynamic> input = converterSettings.InputDictionary[0];

            // A key with no text form of its own: an array, a Tuple, whose text is (), or a class or struct of the
            // program's own with no converter. Another type of the framework, such as a Type, a ValueTuple, (1, 2), or a
            // FileInfo, keeps its own text
            bool KeyHasNoText(Type keyType)
            {
                string keySpace = keyType.Namespace ?? string.Empty;
                bool frameworkType = "System" == keySpace || true == keySpace.StartsWith("System.", StringComparison.Ordinal)
                    || "Microsoft" == keySpace || true == keySpace.StartsWith("Microsoft.", StringComparison.Ordinal);
                return true == keyType.IsArray || (true == IsTupleType(keyType) && false == keyType.IsValueType)
                    || (false == frameworkType && ((false == IsSimpleType(keyType) && false == IsTextClassType(keyType)) || (true == IsMemberStruct(keyType) && false == HasTextConverter(keyType))));
            }

            // The members of a key with no text form, as ConvertClassToDictionary gives them, with its own dictionaries
            // whose keys have no text form written as the entries below; an array's items, each item that is an array or
            // has no text form through its members too, and each item that is a dictionary as the entries below. A key met
            // again inside its own members, as a node of a graph that links back to itself is, is the text Recursion to
            // [type], as ConvertClassToDictionary writes a cycle. A class key is met again when it is the same object; a
            // struct key, which is a copy each time, when it is equal to one on the path, as a copy holding the same
            // dictionary is
            List<object> keyPath = new List<object>();
            object KeyRecord(object entryKey)
            {
                object record = $"Recursion to [{ShortTypeName(entryKey.GetType())}]";
                if (false == keyPath.Any(pathKey => true == (true == entryKey.GetType().IsValueType ? entryKey.GetType() == pathKey.GetType() && entryKey.Equals(pathKey) : ReferenceEquals(pathKey, entryKey))))
                {
                    keyPath.Add(entryKey);
                    record = entryKey is IList keyItems
                        ? (object)keyItems.Cast<object>().Select(keyItem => keyItem is IDictionary itemEntries ? EntriesByKeyMembers(ConvertClassToDictionary(itemEntries), false)
                            : null != keyItem && (keyItem is IList || true == KeyHasNoText(keyItem.GetType())) ? KeyRecord(keyItem)
                            : keyItem).ToList()
                        : EntriesByKeyMembers(ConvertClassToDictionary(entryKey), false);
                    keyPath.RemoveAt(keyPath.Count - 1);
                }
                return record;
            }

            // A dictionary in a C# object given as the input whose keys have no text form, such as the Dictionary<MyS, int>
            // D of a class, an item of a List<Dictionary<MyS, int>> or the dictionary given as the input when it is written
            // as one record, is written as a list of records of its key's members and its value, D0.Key.X, D0.Value, as a
            // list of records is written, so each key keeps its data. The list is held under GPALKEY keys, as a list is
            // held as a dictionary's value. The dictionary given as the input whose entries are rows under a Key column
            // keeps its keys; they are written through their members there. The input is copied along the path to such a
            // dictionary; everything else is the input as it is
            bool topAsEntries = DataFormat.CLASS == inputFormat && null != converterSettings.InputClassType && true == IsDictionaryType(converterSettings.InputClassType)
                && false == (true == IsValueAlone(DictionaryTypeArguments(converterSettings.InputClassType)[1]) || true == HasTextConverter(DictionaryTypeArguments(converterSettings.InputClassType)[1]))
                && (false == input.Values.Cast<object>().All(value => null == value || true == IsNullList(value) || (value is IDictionary record
                        && (false == record.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))
                            || record.Values.Cast<object>().All(entry => null == entry || entry is IDictionary))))
                    // a dictionary whose values are all null, which is written as one record
                    || false == input.Values.Cast<object>().Any(value => null != value && false == IsNullList(value)));
            object EntriesByKeyMembers(object node, bool top)
            {
                object copied = node;
                if (node is IDictionary members)
                {
                    if ((false == top || true == topAsEntries) && 0 < members.Count
                        && true == members.Keys.Cast<object>().All(memberKey => true == KeyHasNoText(memberKey.GetType())))
                    {
                        Dictionary<object, dynamic> entryRecords = new Dictionary<object, dynamic>();
                        foreach (object entryKey in members.Keys)
                            entryRecords[$"GPALKEY{entryRecords.Count:D4}"] = new Dictionary<object, dynamic>
                            {
                                ["Key"] = KeyRecord(entryKey),
                                ["Value"] = EntriesByKeyMembers(members[entryKey], false),
                            };
                        copied = entryRecords;
                    }
                    else
                    {
                        Dictionary<object, dynamic> copy = new Dictionary<object, dynamic>();
                        bool changed = false;
                        foreach (DictionaryEntry entry in members)
                        {
                            copy[entry.Key] = EntriesByKeyMembers(entry.Value, false);
                            changed = true == changed || false == ReferenceEquals(copy[entry.Key], entry.Value);
                        }
                        copied = true == changed ? copy : node;
                    }
                }
                return copied;
            }
            if (DataFormat.CLASS == inputFormat)
                input = (Dictionary<object, dynamic>)EntriesByKeyMembers(input, true);
            // An empty YAML document is held as an empty JSON file is, a null under root, and goes by its values as that does
            bool emptyYamlDocument = DataFormat.YAML == inputFormat && 1 == input.Count && true == input.ContainsKey("root") && null == input["root"];
            // An html page is one record too: the html reader gives its node as tag, attributes, text and children
            bool mappingRecord = (DataFormat.YAML == inputFormat || DataFormat.HTML == inputFormat)
                && false == emptyYamlDocument
                && false == input.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"));
            // An object given as the input, such as a class with a Title and a list of People, is one record too: its
            // dictionary holds its properties. A list given as the input goes by its entries. A dictionary given as the
            // input goes by its entries when every value is a record or a list of records, such as a
            // Dictionary<string, Item>; one with a plain value or a list of plain values among its values, such as a
            // Dictionary<string, string> or a Dictionary<string, List<string>>, is one record under its keys, as the
            // JSON object {"a": "1", "b": ["2", "3"]} is. A null value among records, such as the null Item of a
            // Dictionary<string, Item>, is a null record, so the dictionary still goes by its entries
            // A dictionary of values read from text whose keys are not text, such as a Dictionary<Point, int>, a
            // Dictionary<int, string> or a Dictionary<Item, int>, is a row for each entry, its key in the Key column and
            // its value in the Value column, so the keys are kept. A key with a text form is written as that text; a
            // key with none, such as a class, a struct of the program's own or an array, is written through its members,
            // Key.X or Key0, before the Value column, below
            bool keyedValues = DataFormat.CLASS == inputFormat && null != converterSettings.InputClassType && true == IsDictionaryType(converterSettings.InputClassType)
                && typeof(string) != DictionaryTypeArguments(converterSettings.InputClassType)[0] && typeof(object) != DictionaryTypeArguments(converterSettings.InputClassType)[0]
                && 0 < input.Count && (true == IsValueAlone(DictionaryTypeArguments(converterSettings.InputClassType)[1]) || true == HasTextConverter(DictionaryTypeArguments(converterSettings.InputClassType)[1]));
            bool classRecord = false == keyedValues && DataFormat.CLASS == inputFormat && null != converterSettings.InputClassType
                && ((false == IsDictionaryType(converterSettings.InputClassType) && false == typeof(IEnumerable).IsAssignableFrom(converterSettings.InputClassType))
                    || (true == IsDictionaryType(converterSettings.InputClassType) && 0 < input.Count
                        && false == (input.Values.Cast<object>().All(value => null == value
                            || (value is IDictionary fields && false == (0 < fields.Count && fields.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))))
                            || true == IsReaderRecordList(value) || true == IsNullList(value))
                            && input.Values.Cast<object>().Any(value => null != value && false == IsNullList(value)))))
                && false == input.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"));
            bool oneRecord = true == mappingRecord || true == classRecord;

            // Two keys of a dictionary in the input that write the same text, such as two keys of a struct with no text
            // form of its own in a dictionary member, cannot be told apart in a csv, tsv, xlsx or grid. One warning names
            // each such text, once for the whole input. A key of the dictionary given as the input that is written
            // through its members, KeyMembers below, is passed over; json, yaml and xml write such keys as records
            HashSet<string> sameKeyTexts = new HashSet<string>();
            void WarnSameKeyTexts(object node, bool topLevel)
            {
                if (node is IDictionary keyed)
                {
                    Dictionary<string, object> keyTexts = new Dictionary<string, object>();
                    foreach (object nodeKey in keyed.Keys)
                    {
                        string keyText = null != nodeKey && false == nodeKey is string && false == (true == topLevel && null != KeyMembers(nodeKey))
                            ? (string)ValueToString(nodeKey) : null;
                        if (null != keyText && true == keyTexts.ContainsKey(keyText))
                        {
                            if (false == Equals(keyTexts[keyText], nodeKey) && true == sameKeyTexts.Add(keyText))
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Two keys of [{ShortTypeName(nodeKey.GetType())}] are written as the same text [{keyText}], so they do not read back apart", (object)node, GPALObjectType.Other);
                        }
                        else if (null != keyText)
                            keyTexts[keyText] = nodeKey;
                    }
                    foreach (object member in keyed.Values)
                        WarnSameKeyTexts(member, false);
                }
                else if (node is IList items)
                    foreach (object item in items)
                        WarnSameKeyTexts(item, false);
            }
            // the XML reader's key conventions (@ attributes, #text, {item: [...]} wrappers) apply only to XML input; the keys of
            // any other source, such as a JSON key named value, item or @id, are kept as written
            bool xmlKeys = DataFormat.XML == inputFormat;
            object cleanedInput = UnwrapSingleItemLists(StripAndGroup(true == oneRecord ? (object)new List<object> { input } : input.Values, xmlKeys), xmlKeys);

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

            // a list of records: numbered entries or list items that are all objects, not single values. A null among
            // them is a null record, such as the second item of [{...}, null]
            bool IsRecordList(object value) =>
                (true == LooksLikeRecordList(value) && ((IDictionary)value).Values.Cast<object>().All(entry => null == entry || entry is IDictionary)
                    && ((IDictionary)value).Values.Cast<object>().Any(entry => entry is IDictionary))
                || (value is IList items && items.Cast<object>().All(entry => null == entry || entry is IDictionary) && items.Cast<object>().Any(entry => entry is IDictionary));

            // a list of records as the reader built it, list items or GPALKEY entries that are all objects. StripGpalKeys
            // turns a list of one record into that record, so only the reader's value still shows it was a list. A null
            // among them is a null record
            bool IsReaderRecordList(object value) =>
                (value is IList items && items.Cast<object>().All(entry => null == entry || entry is IDictionary) && items.Cast<object>().Any(entry => entry is IDictionary))
                || (value is IDictionary entries && 0 < entries.Count
                    && entries.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))
                    && entries.Values.Cast<object>().All(entry => null == entry || entry is IDictionary)
                    && entries.Values.Cast<object>().Any(entry => entry is IDictionary));

            // a list whose entries are each a list of records, there or in more lists, such as the one entry of
            // [[[{...}], [{...}]]]. Each record is a row, as those of a list of record lists at the top are
            bool IsListOfRecordLists(object value) =>
                value is IList lists && 0 < lists.Count
                && lists.Cast<object>().All(entry => entry is IList && (true == IsRecordList(entry) || true == IsListOfRecordLists(entry)));

            // a list as the reader built it that holds only nulls, such as [null]. Among records it is a null record for each
            bool IsNullList(object value) => ReaderEntries(value) is List<object> entries && 0 < entries.Count && entries.All(entry => null == entry);

            // the one member of an object, else null
            object OnlyMember(object value) => value is IDictionary members && 1 == members.Count ? members.Values.Cast<object>().First() : null;

            // The entries of a list as the reader built it, its items or the values of GPALKEY entries, else null
            List<object> ReaderEntries(object value) =>
                value is IList items ? items.Cast<object>().ToList()
                : value is IDictionary entries && 0 < entries.Count && entries.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY")) ? entries.Values.Cast<object>().ToList()
                : null;

            // the reader's entry at a position, when the reader has as many entries as the value written
            object ReaderAt(List<object> readerEntries, int count, int index) => null != readerEntries && readerEntries.Count == count ? readerEntries[index] : null;

            // the reader's value under a key, else null
            // the reader keeps a class dictionary's own keys, such as the ints of a Dictionary<int, object>, where the
            // flattened value is keyed by their text, so a key is matched by its text when it is not found as it is. The
            // text of each reader dictionary's keys is listed once, the first key of a text taking it
            Dictionary<IDictionary, Dictionary<string, object>> readerMembersByText = new Dictionary<IDictionary, Dictionary<string, object>>();
            object ReaderMember(object readerValue, object key)
            {
                object found = null;
                if (readerValue is IDictionary members && null != key)
                {
                    if (true == members.Contains(key))
                        found = members[key];
                    else
                    {
                        if (false == readerMembersByText.TryGetValue(members, out Dictionary<string, object> membersByText))
                        {
                            membersByText = new Dictionary<string, object>();
                            foreach (object member in members.Keys)
                                if (null != member.ToString() && false == membersByText.ContainsKey(member.ToString()))
                                    membersByText[member.ToString()] = members[member];
                            readerMembersByText[members] = membersByText;
                        }
                        membersByText.TryGetValue(key.ToString(), out found);
                    }
                }
                return found;
            }

            // an empty list as the reader built it. StripGpalKeys turns an empty list into null
            bool IsEmptyReaderList(object value) => value is IList items && 0 == items.Count;

            // An object whose one member holds records, there or inside more one-member objects, such as
            // {people: [...]} or {data: {people: [...]}}
            // declaredType is the C# type the value was made from, such as the class of a record given as the input, else null
            bool HoldsRecords(object value, object readerValue, Type declaredType) =>
                true == IsRecordList(OnlyMember(value))
                || (OnlyMember(value) is IDictionary
                    && (true == IsReaderRecordList(OnlyMember(readerValue)) || true == HoldsRecords(OnlyMember(value), OnlyMember(readerValue), DeclaredMemberType(declaredType, OnlyKey(value)))
                        || true == IsRecordGroup(OnlyMember(value), OnlyMember(readerValue), DeclaredMemberType(declaredType, OnlyKey(value)))));

            // the key of the one member of an object, else null
            object OnlyKey(object value) => value is IDictionary members && 1 == members.Count ? members.Keys.Cast<object>().First() : null;

            // The type a C# class or dictionary declares for a member, by its name, such as string for the Name of a class
            // with a string Name, or Item for any key of a Dictionary<string, Item>. Null when the type is not known, as
            // for input other than a C# object, or is object
            Type DeclaredMemberType(Type declaredType, object key)
            {
                Type memberType = null;
                if (null != declaredType && null != key && true == IsDictionaryType(declaredType))
                    memberType = DictionaryTypeArguments(declaredType)[1];
                // a member a class hides with new, such as a string Name over an object Name of its base, is the one the
                // class declares, as the class was read
                else if (null != declaredType && null != key && false == IsSimpleType(declaredType) && false == IsEnumerableType(declaredType))
                    memberType = declaredType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.Name == key.ToString())
                            .OrderByDescending(property => declaredType == property.DeclaringType).FirstOrDefault()?.PropertyType
                        ?? declaredType.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(field => field.Name == key.ToString())
                            .OrderByDescending(field => declaredType == field.DeclaringType).FirstOrDefault()?.FieldType;
                return typeof(object) == memberType ? null : memberType;
            }

            // the type a C# list or dictionary declares for its entries, such as Item for a List<Item>, else null
            Type DeclaredItemType(Type declaredType) =>
                null == declaredType || typeof(string) == declaredType ? null
                : true == IsDictionaryType(declaredType) ? DictionaryTypeArguments(declaredType)[1]
                : true == IsEnumerableType(declaredType) ? CollectionItemType(declaredType)
                : null;

            // An object of two or more members that are each a list of records, such as clients {A: [...], B: [...]}. Each
            // record is a row, as the records of a dictionary of clients are, where flattening them would put them all in
            // one row. An object of one member goes by HoldsRecords
            // A member that is a list of only nulls, such as x of {x: [null], y: [{...}]}, is null records of the group; an
            // empty list, such as x of {x: [], y: [{...}]}, is a member with no records, and a null, such as x of
            // {x: null, y: [{...}]}, is a null record of the group. A null member of a C# class that declares it as a
            // plain value, a single record or a dictionary, such as the null string Name of a class beside its List<Item>
            // Items, is a field of the record, so the record is one row. An XML element around its records, such as
            // <A><r>..</r><r>..</r></A>, is a list of records, and one around a single record is a list of one
            bool IsRecordGroup(object value, object readerValue, Type declaredType) =>
                value is IDictionary members && 1 < members.Count
                && members.Keys.Cast<object>().All(key => true == IsRecordList(members[key]) || true == IsReaderRecordList(ReaderMember(readerValue, key))
                    || (null == members[key] && (true == IsNullList(ReaderMember(readerValue, key)) || true == IsEmptyReaderList(ReaderMember(readerValue, key))
                        || (false == xmlKeys && null == ReaderMember(readerValue, key)
                            && false == (null != DeclaredMemberType(declaredType, key)
                                && (false == IsEnumerableType(DeclaredMemberType(declaredType, key)) || true == IsDictionaryType(DeclaredMemberType(declaredType, key)))))))
                    || null != XmlElementRecords(members[key]))
                && members.Keys.Cast<object>().Any(key => true == IsRecordList(members[key]) || true == IsReaderRecordList(ReaderMember(readerValue, key))
                    || true == IsRecordList(XmlElementRecords(members[key])));

            // the records an XML element holds as its one child, such as the r of <A><r>..</r><r>..</r></A>, a list of
            // records, or of <B><r>..</r></B>, one record. Null for any other value, and for any input other than XML
            object XmlElementRecords(object value) =>
                true == xmlKeys && value is IDictionary element && 1 == element.Count
                && (true == IsRecordList(OnlyMember(value)) || (OnlyMember(value) is IDictionary record && false == LooksLikeRecordList(record)))
                ? OnlyMember(value) : null;

            // an object the reader read as named fields, not a list, that has a plain value among its fields, such as
            // {"0": "a", "1": {"x": 1}}, or a list of plain values among its fields, such as {"0": [1, 2], "1": [3, 4]}.
            // One whose fields other than null are all objects or lists of objects, such as {"0": {...}, "1": {...}}
            // or {"0": {...}, "1": null}, holds records
            bool IsRecordOfValues(object value, object readerValue) =>
                null != readerValue && null == ReaderEntries(readerValue) && value is IDictionary fields
                && false == (fields.Values.Cast<object>().Any(field => null != field && false == field is NullValue)
                    && fields.Values.Cast<object>().Where(field => null != field && false == field is NullValue)
                        .All(field => true == IsRecordList(field) || (field is IDictionary && false == LooksLikeRecordList(field))));

            // The values of a row that is a list of plain values, such as a row of a List<List<string>> or a List<string[]>,
            // held as a list or as numbered entries, else null. An XML element whose one child is repeated, such as a
            // <tr> holding <td> cells, is such a row too: a repeated element's name is not kept, so the cells go by position
            List<object> ValueListRow(object value)
            {
                object listed = DataFormat.XML == inputFormat && value is IDictionary element && 1 == element.Count && element.Values.Cast<object>().First() is IList ? element.Values.Cast<object>().First() : value;
                IEnumerable entries = listed is IDictionary numbered && true == LooksLikeRecordList(listed) ? numbered.Values : listed as IList;
                List<object> values = entries?.Cast<object>().ToList();
                return null != values && 0 < values.Count && values.All(entry => false == entry is IDictionary && false == entry is IList) ? values : null;
            }

            // the key each row is written under, such as the key of a Dictionary<string, Item> entry or the name of a group
            // of record lists, else null. It goes in a Key column
            object currentRecordKey = null;
            List<object> recordKeys = new List<object>();

            // readerNull is true when the reader read the value as a null, where StripGpalKeys also makes an empty list null.
            // declaredType is the C# type the value was made from, else null
            void EmitRows(dynamic value, object readerValue = null, bool readerNull = false, Type declaredType = null)
            {
                // a record of plain values whose fields are named 0, 1, such as a csv row under a 0,1 header line, is one
                // record; the reader's value, when there is one, shows whether the numbered entries were a list
                // a list of records, such as one of the lists of [[{...}], [{...}, {...}]], is a row for each record. Numbered
                // entries that are all plain values are one row of values
                if ((LooksLikeRecordList(value) && false == IsRecordOfValues(value, readerValue) && null == ValueListRow(value)) || (value is IList && true == IsRecordList(value))
                    || true == IsListOfRecordLists(value))
                {
                    List<object> entries = ((IEnumerable)(value is IDictionary numbered ? numbered.Values : value)).Cast<object>().ToList();
                    List<object> entryKeys = value is IDictionary numberedKeys ? numberedKeys.Keys.Cast<object>().ToList() : null;
                    List<object> readerEntries = ReaderEntries(readerValue);
                    for (int entryIdx = 0; entryIdx < entries.Count; entryIdx++)
                    {
                        // a numbered object, such as {"0": {...}, "1": null}, has the reader's value under the same key
                        bool entryReaderNull = null != readerEntries
                            ? readerEntries.Count == entries.Count && null == readerEntries[entryIdx]
                            : null != entryKeys && readerValue is IDictionary readerFields && true == readerFields.Contains(entryKeys[entryIdx]) && null == readerFields[entryKeys[entryIdx]];
                        EmitRows(entries[entryIdx], ReaderAt(readerEntries, entries.Count, entryIdx), entryReaderNull, DeclaredItemType(declaredType));
                    }
                    return;
                }

                // A null the reader read, such as the second item of ["x", null] or the record "1" of {"0": {...}, "1": null},
                // is a row of its own, written as null
                if (null == (object)value && true == readerNull)
                {
                    rows.Add(null);
                    recordKeys.Add(currentRecordKey);
                    singleValueRows++;
                }
                // a list the reader read holding only nulls, such as [null], which StripGpalKeys made null, is a row for each
                // null, written as null; so are numbered entries of nulls under a key, such as a: [null, null] of a dictionary
                // of record lists. Numbered entries of nulls with no key are a row of values, such as [null, null] in a list of lists
                else if (true == IsNullList(readerValue)
                    && (null == (object)value || (null != currentRecordKey && value is IDictionary strippedNulls && strippedNulls.Values.Cast<object>().All(entry => null == entry))))
                {
                    int nullCount = ReaderEntries(readerValue).Count;
                    for (int nullIdx = 0; nullIdx < nullCount; nullIdx++)
                    {
                        rows.Add(null);
                        recordKeys.Add(currentRecordKey);
                        singleValueRows++;
                    }
                }
                // Some GPALKEY-indexed slots are genuinely empty placeholders (Count == 0) rather than
                // real records - skip them instead of emitting a blank CSV line for each one. A row of a list the reader
                // read as an empty object, such as {} in [{}, {"n": 1}], is a record and keeps its row.
                else if (false == IsCustomStructType(value?.GetType()) && (null == (object)value || (value is ICollection emptyCheck && 0 == emptyCheck.Count))
                    && false == (readerValue is IDictionary readerRecord && 0 == readerRecord.Count
                        && true == input.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))))
                    return;
                // each member of a group of record lists writes its records as rows, each under the member's name
                else if (true == IsRecordGroup(value, readerValue, declaredType))
                {
                    object groupKey = currentRecordKey;
                    foreach (object key in ((IDictionary)value).Keys.Cast<object>().ToList())
                    {
                        // a list of one record is that record now, and goes with the reader's one record
                        object member = ((IDictionary)value)[key];
                        object readerMember = ReaderMember(readerValue, key);
                        List<object> readerRecords = ReaderEntries(readerMember);
                        currentRecordKey = key;
                        // a member that is a null, not an empty list, is a null record under its key
                        bool memberNull = null == member && false == IsEmptyReaderList(readerMember) && false == IsNullList(readerMember);
                        // an XML element around its records writes the records it holds
                        if (false == IsRecordList(member) && null != XmlElementRecords(member))
                            member = XmlElementRecords(member);
                        bool oneRecordOfList = false == IsRecordList(member) && null != readerRecords && 1 == readerRecords.Count && readerRecords[0] is IDictionary;
                        EmitRows(member, true == oneRecordOfList ? readerRecords[0] : readerMember, memberNull,
                            true == oneRecordOfList ? DeclaredItemType(DeclaredMemberType(declaredType, key)) : DeclaredMemberType(declaredType, key));
                    }
                    currentRecordKey = groupKey;
                }
                // An object that holds records through its one member writes each record as a row, so the
                // records of {people: [...]} are rows, as those of a top-level list are
                else if (true == HoldsRecords(value, readerValue, declaredType))
                {
                    object member = OnlyMember(value);
                    object readerMember = OnlyMember(readerValue);
                    Type memberType = DeclaredMemberType(declaredType, OnlyKey(value));

                    if (true == IsRecordList(member))
                    {
                        List<object> records = ((IEnumerable)(member is IDictionary recordList ? recordList.Values : member)).Cast<object>().ToList();
                        List<object> readerRecords = ReaderEntries(readerMember);
                        // a null the reader read among the records, such as the second item of {people: [{...}, null]}, is a null record
                        for (int recordIdx = 0; recordIdx < records.Count; recordIdx++)
                            EmitRows(records[recordIdx], ReaderAt(readerRecords, records.Count, recordIdx),
                                null != readerRecords && readerRecords.Count == records.Count && null == readerRecords[recordIdx], DeclaredItemType(memberType));
                    }
                    else if (true == IsReaderRecordList(readerMember))
                        EmitRows(member, readerMember is IList readerList ? readerList[0] : ((IDictionary)readerMember).Values.Cast<object>().First(), false, DeclaredItemType(memberType));   // a list of one record, with the reader's record
                    else
                        EmitRows(member, readerMember, false, memberType);   // another one-member object around the records
                }
                else
                {
                    flattenLastMessage.Clear();
                    flattenSupressedMessage = false;
                    var row = new Dictionary<object, dynamic>();

                    // UnwrapSingleItemLists collapses a single-token row (e.g. ["Selenium"]) down to
                    // a bare scalar "Selenium". Flatten would key that under "" (empty string), which
                    // doesn't line up with the "0","1","2"... headers produced by multi-token rows.
                    // Key it under "0" instead so it lands in the first column.
                    // A tuple is a record of its items, Item1, Item2, as json writes it. A char is written as itself, a
                    // '\0' included, as json writes it
                    List<object> valueList = false == IsRecordOfValues(value, readerValue) ? ValueListRow(value) : null;
                    if (false == IsCustomStructType(value?.GetType()) && null != (object)value && true == IsSimpleType(value.GetType()) && false == IsTupleType(value.GetType()))
                    {
                        row["0"] = value is char ? value.ToString() : ValueToString(value);
                        singleValueRows++;
                    }
                    // a row of plain values goes by position, 0, 1, 2, which names nothing
                    else if (null != valueList)
                    {
                        Flatten(valueList, ref row, "", readerValue);
                        valueListRows++;
                    }
                    else
                        Flatten(value, ref row, "", readerValue);

                    rows.Add(row);
                    recordKeys.Add(currentRecordKey);
                }
            }

            // an object whose one member is an empty list, there or inside more one-member objects, such as {people: []}
            bool HoldsEmptyList(object value, object readerValue) =>
                value is IDictionary holder && 1 == holder.Count
                && ((null == OnlyMember(value) && true == IsEmptyReaderList(OnlyMember(readerValue))) || true == HoldsEmptyList(OnlyMember(value), OnlyMember(readerValue)));

            // each top-level item goes with the reader's own value for it, when the two lists line up. A file that is
            // one object holding only an empty list, such as {people: []}, holds no records and writes no rows; a record
            // in a list keeps its row
            List<object> items = rootItems.ToList();
            List<object> readerItems = true == oneRecord ? new List<object> { input } : input.Values.Cast<object>().ToList();
            bool oneObject = 1 == items.Count && false == input.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"));

            // A dictionary given as the input whose values are records or lists of records, such as a Dictionary<string, Item>
            // or a Dictionary<string, List<Item>>, writes each record under its entry's key, in a Key column. A null value
            // or a null in a list is a null record under its key
            bool keyedRecords = (true == keyedValues && items.Count == input.Count) || (DataFormat.CLASS == inputFormat && null != converterSettings.InputClassType && true == IsDictionaryType(converterSettings.InputClassType)
                && false == oneRecord && 0 < input.Count && items.Count == input.Count
                && false == input.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))
                && input.Values.Cast<object>().All(value => null == value || true == IsNullList(value) || (value is IDictionary record
                    && (false == record.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))
                        || record.Values.Cast<object>().All(entry => null == entry || entry is IDictionary)))));
            List<object> inputKeys = input.Keys.Cast<object>().ToList();

            // An empty json or yaml document, such as an empty file, a json null or a yaml file of only a comment, is held
            // as a null under the reader's root; it holds no records and writes no rows, as {} writes none
            bool emptyDocument = (DataFormat.JSON == inputFormat || DataFormat.YAML == inputFormat)
                && 1 == input.Count && true == input.ContainsKey("root") && null == input["root"];

            // The members of a key of the dictionary given as the input, when the key has no text form of its own, such
            // as a struct of the program's own, a class or an array: they are written in place of the Key column, as a
            // nested record's members are, Key.X, Key.Y, or an array's items, Key0, Key1, so two such keys keep their data
            // where their text, the type's name, is the same. Null for any other key, and for a key with no members,
            // which keeps its text in the Key column
            // each key's members are worked out once, by the key object, so what reading them publishes, such as a getter
            // that throws, is published once for the key however many rows it heads
            System.Runtime.CompilerServices.ConditionalWeakTable<object, object[]> keyMembersMade = new System.Runtime.CompilerServices.ConditionalWeakTable<object, object[]>();
            object KeyMembers(object entryKey)
            {
                object[] made = null;
                if (null != entryKey && false == keyMembersMade.TryGetValue(entryKey, out made))
                {
                    // an item of an array key with no text form, such as a struct of the program's own, is written through
                    // its members too, Key0.X
                    object members = true == KeyHasNoText(entryKey.GetType()) ? KeyRecord(entryKey) : null;
                    made = new[] { members is ICollection memberEntries && 0 < memberEntries.Count ? members : null };
                    keyMembersMade.Add(entryKey, made);
                }
                return made?[0];
            }
            // the C# type of each top-level item: the class given as the input when it is one record, else the type of the
            // entries of the list or dictionary given; null for input other than a C# object
            Type itemType = DataFormat.CLASS == inputFormat && null != converterSettings.InputClassType
                ? (true == oneRecord ? converterSettings.InputClassType : DeclaredItemType(converterSettings.InputClassType))
                : null;

            if (DataFormat.CLASS == inputFormat
                && (true == IsDelimitedDataFormat(converterSettings.OutDataFormat) || DataFormat.XLSX == converterSettings.OutDataFormat || DataFormat.GRID == converterSettings.OutDataFormat))
                WarnSameKeyTexts(input, true == keyedRecords);

            for (int itemIdx = 0; itemIdx < items.Count; itemIdx++)
                if (false == emptyDocument && false == (true == oneObject && true == HoldsEmptyList(items[itemIdx], 1 == readerItems.Count ? readerItems[0] : null)))
                {
                    currentRecordKey = true == keyedRecords ? inputKeys[itemIdx] : null;
                    if (true == keyedValues && true == keyedRecords)
                        EmitRows(new Dictionary<object, dynamic> { ["Value"] = items[itemIdx] });
                    else
                        EmitRows(items[itemIdx], items.Count == readerItems.Count ? readerItems[itemIdx] : null, items.Count == readerItems.Count && null == readerItems[itemIdx], itemType);
                }
            currentRecordKey = null;

            // Rows written under a key start with a Key column, named Key unless a record has a field of that name. A key
            // with no text form of its own is written through its members, Key.X or Key0, in place of the Key column, and
            // the name is then also passed over when a record has fields under it, Key.Name. A null record under a key
            // keeps its key, and is null in every column after its key's columns
            HashSet<int> keyedNullRows = new HashSet<int>();
            if (true == recordKeys.Any(rowKey => null != rowKey))
            {
                List<object> rowKeyMembers = recordKeys.Select(rowKey => KeyMembers(rowKey)).ToList();
                bool keysByMembers = true == rowKeyMembers.Any(members => null != members);
                string keyColumn = "Key";
                int keySuffix = 0;
                // a record's field under the name, Key.Name, or a column of an array key's items, Key0, is passed over too
                while (true == rows.Any(row => null != row && (true == row.ContainsKey(keyColumn)
                    || (true == keysByMembers && true == row.Keys.Any(field => true == Regex.IsMatch(field.ToString(), "^" + Regex.Escape(keyColumn) + @"(\.|\d)"))))))
                    keyColumn = $"Key_{++keySuffix}";

                for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
                    if (null != rows[rowIdx] || null != recordKeys[rowIdx])
                    {
                        Dictionary<object, dynamic> keyedRow = new Dictionary<object, dynamic>();
                        // the key's members with the GPALKEY prefix taken off, with the members as they are as the reader's
                        // value, so a list among them is written Key.M0, Key.M1, as a list member is
                        if (null != rowKeyMembers[rowIdx])
                            Flatten(new Dictionary<object, dynamic> { [keyColumn] = StripGpalKeys(rowKeyMembers[rowIdx], false) }, ref keyedRow, "",
                                new Dictionary<object, dynamic> { [keyColumn] = rowKeyMembers[rowIdx] });
                        else
                            keyedRow[keyColumn] = null != recordKeys[rowIdx] ? ValueToString(recordKeys[rowIdx]) : "";
                        if (null != rows[rowIdx])
                            foreach (KeyValuePair<object, dynamic> field in rows[rowIdx])
                                keyedRow[field.Key] = field.Value;
                        else
                            keyedNullRows.Add(rowIdx);
                        rows[rowIdx] = keyedRow;
                    }
            }

            List<string> headers = null;

            // User-supplied column names are positional display labels, not field names, so values are
            // looked up by position via canonicalFieldNames rather than by matching header text to a key.
            bool useUserSuppliedHeaders = 0 < ((IGPALFileInternal)converterSettings.OutputFile)?.FileSettings.ColumnList.Count() && 0 < ((IGPALFileInternal)converterSettings.OutputFile)?.FileSettings.ColumnList[0].Count();  // CAVEAT: hard coded value, but we are only now allowing one file
            // A null-valued field is omitted entirely from a row's dictionary, so different rows can carry
            // different subsets/orders of keys. This merges all rows' keys into one order-preserving list. A
            // newly-seen key that sits between two known keys of its row goes between them, right before the later
            // one, so [{"A":..,"B":..},{"A":..,"X":..,"B":..}] gives A,X,B. Any other new key goes after every name
            // before it, at the end, so [{"Name":..},{"Pet":..}] gives Name,Pet and [{"A":..,"B":..},{"X":..,"B":..}]
            // gives A,B,X.
            // The order is held in a linked list with a lookup from each name to its node, so finding a name and
            // placing a new one right after its predecessor cost the same however many names there are. A list
            // scanned and shifted for every key grows with keys times names.
            List<string> canonicalFieldNames = null;
            if (rows.Any())
            {
                LinkedList<string> orderedNames = new LinkedList<string>();
                Dictionary<string, LinkedListNode<string>> nodeFor = new Dictionary<string, LinkedListNode<string>>();

                foreach (var row in rows.Where(row => null != row))   // a null record names no fields
                {
                    LinkedListNode<string> lastNode = null;
                    List<string> rowKeys = row.Keys.Cast<string>().ToList();
                    // the place in this row of the next key after each one that was known before the row, else -1. A key
                    // of this row is new until it is reached, so this is listed once, from the end of the row
                    int[] nextKnownIdx = new int[rowKeys.Count];
                    int knownAfter = -1;
                    for (int keyIdx = rowKeys.Count - 1; 0 <= keyIdx; keyIdx--)
                    {
                        nextKnownIdx[keyIdx] = knownAfter;
                        if (true == nodeFor.ContainsKey(rowKeys[keyIdx]))
                            knownAfter = keyIdx;
                    }
                    for (int keyIdx = 0; keyIdx < rowKeys.Count; keyIdx++)
                    {
                        string key = rowKeys[keyIdx];
                        if (true == nodeFor.TryGetValue(key, out LinkedListNode<string> existingNode))
                        {
                            lastNode = existingNode;
                        }
                        else
                        {
                            // the next known key of this row, when it comes after the known key before this one
                            string nextKnownKey = null != lastNode && 0 <= nextKnownIdx[keyIdx] ? rowKeys[nextKnownIdx[keyIdx]] : null;
                            LinkedListNode<string> laterNode = lastNode?.Next;
                            while (null != nextKnownKey && null != laterNode && laterNode != nodeFor[nextKnownKey])
                                laterNode = laterNode.Next;
                            lastNode = null != laterNode && null != nextKnownKey ? orderedNames.AddBefore(laterNode, key) : orderedNames.AddLast(key);
                            nodeFor[key] = lastNode;
                        }
                    }
                }

                canonicalFieldNames = orderedNames.ToList();
            }
            if (useUserSuppliedHeaders)
            {
                headers = ((IGPALFileInternal)converterSettings.OutputFile).FileSettings.ColumnList[0].ToList();

                // fewer names than the rows have fields: the fields past the names are written under names made for them.
                // A json, yaml or xml output names its records' fields itself, keeping a nested record or list whole, and
                // warns of the names it makes, so these columns are not what it writes and are not named in a warning
                bool outputNamesFields = DataFormat.JSON == converterSettings.OutDataFormat || DataFormat.YAML == converterSettings.OutDataFormat
                    || DataFormat.XML == converterSettings.OutDataFormat;
                if (null != canonicalFieldNames && headers.Count < canonicalFieldNames.Count)
                {
                    int namedCount = headers.Count;
                    headers = NamesForWidth(headers, canonicalFieldNames.Count);
                    if (false == outputNamesFields)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{converterSettings.OutputFile.Filename}] has [{namedCount}] column names for [{canonicalFieldNames.Count}] columns, so the others are named [{string.Join(",", headers.Skip(namedCount))}]", converterSettings, GPALObjectType.Converter);
                }
            }
            else
                headers = canonicalFieldNames;

            // Records the reader read as lists of values, such as a csv or xlsx file with no header line or a JSON list of
            // lists, flatten to their positions, 0, 1, 2, which name nothing, so they get no header line unless the
            // output gives names. Rows that are each one plain value, such as the items of a JSON list ["x", "y"] or
            // of an XML list <item>x</item><item>y</item>, sit at position 0 and get no header line either, nor do rows
            // that are each a list of plain values, such as those of a List<List<string>>. A list that
            // holds records, such as the lists of [[{...}], [{...}]], is a list of records, whose names are the header
            bool recordsAreValueLists = false == oneRecord && 0 < input.Count
                && input.Values.Cast<object>().All(record => (record is IList values && values.Cast<object>().All(value => false == value is IDictionary && false == IsListOfRecordLists(value) && false == (value is IList && true == IsRecordList(value))))
                    || (record is IDictionary entries && 0 < entries.Count && entries.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY"))
                        && entries.Values.Cast<object>().All(value => false == value is IDictionary)));
            bool rowsAreSingleValues = 0 < rows.Count && rows.Count == singleValueRows + valueListRows;
            bool headersNameColumns = true == useUserSuppliedHeaders || (false == recordsAreValueLists && false == rowsAreSingleValues);
            // records with no fields name nothing, so they get no header line
            converterSettings.GridHasNamesRow = true == converterSettings.FirstLineIsColumnHeaders && null != headers && 0 < headers.Count && true == headersNameColumns;

            if (true == converterSettings.GridHasNamesRow)
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

                // a record with no fields, when no record has any, has no values and writes no line
                if (null != dict && 0 == dict.Count && 0 == headerCount)
                    continue;

                // a null record is null in every column, so a record whose values are null and empty, such as
                // {Name: null, Nick: ""}, is not read back as one; a record whose values are all null is written alike and
                // reads back as a null record. One under a key is null in every column other than its key's
                bool keyedNullRow = keyedNullRows.Contains(rowIndex);
                for (int i = 0; i < Math.Max(headerCount, null == dict ? 1 : true == keyedNullRow ? 2 : 0); i++)
                {
                    string key = useUserSuppliedHeaders ? (null != canonicalFieldNames && i < canonicalFieldNames.Count ? canonicalFieldNames[i] : null) : i < headerCount ? headers[i] : null;
                    string value = null == dict || (true == keyedNullRow && false == (null != key && true == dict.ContainsKey(key))) ? NullValue.Instance.ToString()
                        : null != key && true == dict.ContainsKey(key) && null != dict[key] ? dict[key].ToString() : "";
                    sb2.Append(CreateCSVToken(value, converterSettings));
                    gridRow.Add(value);
                }
                char delim = true == converterSettings.OutDelimiter.HasValue ? converterSettings.OutDelimiter.Value : ',';

                if (sb2.Length > 0 && sb2[sb2.Length - 1] == delim) sb2.Length--; // Remove trailing comma
                // a row of one empty value is written "", a quoted empty value, so it reads back as a row where a blank
                // line is no row
                if (0 == sb2.Length && 1 == gridRow.Count)
                    sb2.Append(@"""""");
                sb.AppendLine(sb2.ToString());
                sb2.Clear();
                outputGrid.AddRow(gridRow);
            }
            // Recursive function to flatten nested dictionary. readerValue is the same value as the reader built it,
            // which still shows a list of one, or an empty list, as a list
            void Flatten(dynamic dictionaryOrList, ref Dictionary<object, dynamic> outDict, string prefix = "", object readerValue = null)
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
                            // read by reflection: dynamic cannot see Key and Value when the pair's types are not public
                            kvpValue = PairMember((object)kvp, "Value");
                            kvpKey = PairMember((object)kvp, "Key");
                        }

                        object readerMember = ReaderMember(readerValue, kvpKey);

                        // StripGpalKeys turns a list of one into the one value it holds; it is written as a list, at
                        // index 0, as a list of two is
                        if (readerMember is IList readerListOfOne && 1 == readerListOfOne.Count && false == kvpValue is IList)
                            kvpValue = new List<object> { kvpValue };

                        // A list the reader keyed by GPALKEY, such as the Tags of a Dictionary<string, object> given as the
                        // input, is numbered 0000, 0001 once StripGpalKeys takes the prefix off; it is written as a list,
                        // Tags0, Tags1, as a List member is
                        if (kvpValue is IDictionary numberedList && readerMember is IDictionary readerList && 0 < readerList.Count
                            && readerList.Count == numberedList.Count && true == readerList.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY")))
                        {
                            kvpValue = numberedList.Values.Cast<object>().ToList();
                            readerMember = readerList.Values.Cast<object>().ToList();
                        }

                        Type kvpValueType = kvpValue?.GetType();

                        if (null == (object)kvpValue && true == IsEmptyReaderList(readerMember))
                        {
                            // an empty list, which StripGpalKeys made null, has no values to write
                        }
                        else if (true == IsComplexType(kvpValueType))
                        {
                            outDict[prefix + kvpKey] = ValueToString(kvpValue);   // 3+4i, as json writes it
                        }
                        else if (true == IsTupleType(kvpValueType))
                        {
                            // a tuple is a record of its items, Item1, Item2, as json writes it
                            Flatten(ConvertClassToDictionary(kvpValue), ref outDict2, prefix + kvpKey + ".");
                            foreach (var item in outDict2)
                            {
                                outDict[item.Key] = item.Value;
                            }
                            outDict2.Clear();
                        }
                        else if (true == IsSimpleType(kvpValueType))
                        {
                            if (outDict.TryGetValue(prefix + kvpKey, out dynamic val))
                                sb3.Append($"{val}");
                            else
                                sb3.Append(kvpValue is char ? kvpValue.ToString() : ValueToString(kvpValue));   // a char is itself, a '\0' included, as json writes it
//                            sb3.Append(CreateCSVToken(kvpValue.ToString(), converterSettings));
                            outDict[prefix + kvpKey] = null == (object)kvpValue && null == (object)val ? (object)NullValue.Instance : sb3.ToString();
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
                            Flatten(kvpValue, ref outDict2, prefix + kvpKey + ".", readerMember);
                            foreach (var item in outDict2)
                            {
                                outDict[item.Key] = item.Value;
                            }
                            outDict2.Clear();
                        }
                        else if ((true == IsEnumerableType(kvpValueType) || true == kvpValueType.IsArray)/* && false == IsStructType(kvpValueType)*/)
                        {
                            int index = 0;
                            List<object> readerEntries = ReaderEntries(readerMember);
                            int itemCount = kvpValue is ICollection itemCollection ? itemCollection.Count : -1;
                            foreach (var item in kvpValue)
                            {
                                object readerItem = ReaderAt(readerEntries, itemCount, index);
                                Flatten(item, ref outDict2, $"{prefix}{kvpKey}{index++}.", readerItem);
                                foreach (var item2 in outDict2)
                                {
                                    outDict[item2.Key] = item2.Value;
                                }
                                outDict2.Clear(); // clear but DO NOT merge
                            }
                        }
                        else if (true == IsClassType(kvpValueType) && 0 < objectClassesOpen)
                        {
                            // a class inside a class held in a member typed object is written as its type: its properties
                            // can lead on without end, as a DirectoryInfo's Root does, or back to the class holding it
                            outDict[prefix + kvpKey] = $"{kvpValueType}";
                        }
                        else if ((true == IsClassType(kvpValueType)/* || true == IsStructType(kvpValueType)*/) && kvpValueType != typeof(string))
                        {
                            objectClassesOpen++;
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
                                        // provider record) must still claim its column slot, written as null -
                                        // silently omitting the key here shifts every subsequent
                                        // property's position out of alignment with a fixed header list.
                                        outDict[$"{prefix}{kvpKey}.{prop.Name}"] = NullValue.Instance;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to flatten property [{prop.Name}] of [{kvpValueType.Name}]", (object)kvp, GPALObjectType.Other, ex);
                                }
                            }
                            objectClassesOpen--;
                        }
                        else
                        {
                            // its type is written in its column, where the value was left out
                            outDict[prefix + kvpKey] = $"{kvpValueType}";
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Unable to handle type [{kvpValueType}]", (object)kvp, GPALObjectType.Other);
                        }
                    }
                }
                else // Top-level input
                {
                    // a value in a list or a property is passed a name ending in a dot for the names below it, such as
                    // Tags0.; a value itself takes the name without the dot, Tags0
                    string valueKey = prefix.EndsWith(".") ? prefix.Substring(0, prefix.Length - 1) : prefix;

                    if (true == IsKeyValuePairType(dictOrListType))   // keyvalupair, value is children
                    {
                        // read by reflection: dynamic cannot see Key and Value when the pair's types are not public
                        object pairValue = PairMember((object)dictionaryOrList, "Value");

                        if (true == IsDictionaryType(pairValue?.GetType()))
                        {
                            Flatten(pairValue, ref outDict2, $"{prefix}");
                            foreach (var item2 in outDict2)
                            {
                                outDict[item2.Key] = item2.Value;
                            }
                            outDict2.Clear();
                        }
                        else
                            outDict[PairMember((object)dictionaryOrList, "Key")] = pairValue;
                    }
                    else if (null == (object)dictionaryOrList && true == IsEmptyReaderList(readerValue))
                    {
                        // an empty list, which StripGpalKeys made null, has no values to write
                    }
                    // a null, such as an item of [null, {...}], is written as null
                    else if (false == IsCustomStructType(dictOrListType) && null == (object)dictionaryOrList)
                    {
                        outDict[valueKey] = NullValue.Instance;
                    }
                    else if (true == IsComplexType(dictOrListType))
                    {
                        outDict[valueKey] = ValueToString(dictionaryOrList);   // 3+4i, as json writes it
                    }
                    else if (true == IsTupleType(dictOrListType))
                    {
                        // a tuple is a record of its items, Item1, Item2, as json writes it
                        Flatten(ConvertClassToDictionary(dictionaryOrList), ref outDict2, valueKey + ".");
                        foreach (var item2 in outDict2)
                        {
                            outDict[item2.Key] = item2.Value;
                        }
                        outDict2.Clear();
                    }
                    else if (true == IsSimpleType(dictOrListType))
                    {
                        // a date is written in ISO 8601 round-trip form and a bool as true or false, as ValueToString writes them
                        if (outDict.TryGetValue(valueKey, out dynamic val))
                            sb3.Append($"{val}");
                        else
                            sb3.Append(dictionaryOrList is char ? dictionaryOrList.ToString() : ValueToString(dictionaryOrList));   // a char is itself, a '\0' included, as json writes it
                        //                        sb3.Append(CreateCSVToken(dictionaryOrList.ToString(), converterSettings));
                        outDict[valueKey] = sb3.ToString();
                    }
                    else if (true == IsIpAddressType(dictOrListType))
                    {
                        IPAddress iPAddress = (IPAddress)dictionaryOrList;
                        sb3.Append(iPAddress.ToString());
                        //                        sb3.Append(CreateCSVToken(iPAddress.ToString(), converterSettings));
                        outDict[valueKey] = sb3.ToString();
                    }
                    else if (IsBigIntegerType(dictOrListType) || IsVersionType(dictOrListType))
                    {
                        sb3.Append(dictionaryOrList.ToString());
                        //                        sb3.Append(CreateCSVToken(dictionaryOrList.ToString(), converterSettings));
                        outDict[valueKey] = sb3.ToString();
                    }
                    else if (true == IsEnumerableType(dictOrListType) || true == IsArrayType(dictOrListType))
                    {
                        int index = 0;
                        List<object> readerEntries = ReaderEntries(readerValue);
                        int itemCount = dictionaryOrList is ICollection itemCollection ? itemCollection.Count : -1;
                        foreach (var item in dictionaryOrList)
                        {
                            object readerItem = ReaderAt(readerEntries, itemCount, index);
                            Flatten(item, ref outDict2, $"{prefix}{index++}", readerItem);
                            foreach (var item2 in outDict2)
                            {
                                outDict[item2.Key] = item2.Value;
                            }
                            outDict2.Clear();
                        }
                    }
                    else if (true == IsClassType(dictOrListType))
                    {
                        // a class held in a list in a member typed object, such as an Address of a List<Address>, is written
                        // as a class held in the member itself is, as its properties, Extra0.City, Extra0.Tags.0
                        Flatten(new Dictionary<object, dynamic> { [valueKey] = dictionaryOrList }, ref outDict2);
                        foreach (var item2 in outDict2)
                        {
                            outDict[item2.Key] = item2.Value;
                        }
                        outDict2.Clear();
                    }
                    else
                    {
                        // its type is written in its column, where the value was left out
                        outDict[valueKey] = $"{dictOrListType}";
                        string flattenMsg = $"Unable to handle type [{dictOrListType}]";
                        if (false == flattenLastMessage.Contains(flattenMsg))
                        {
                            flattenLastMessage.Add(flattenMsg);
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, flattenMsg, (object)dictionaryOrList, GPALObjectType.Other);
                            flattenSupressedMessage = false;
                        }
                        else if (false == flattenSupressedMessage)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Suppressing repeat messages", (object)dictionaryOrList, GPALObjectType.Other);
                            flattenSupressedMessage = true;
                        }
                    }
                }
            }

            outGrid = outputGrid;
            return sb.ToString();
        }
        /// <summary>
        /// A null value in a row the csv writer builds: written to the csv and to the grid as null, the converter's mark
        /// for one, where a field the row does not have is an empty cell.
        /// </summary>
        private sealed class NullValue
        {
            internal static readonly NullValue Instance = new NullValue();
            public override string ToString() => "null";
        }
        internal static string CreateCSVToken(string inputString, ConverterSettings converterSettings)
        {
            char delimiter = true == converterSettings.OutDelimiter.HasValue ? converterSettings.OutDelimiter.Value : ',';
            inputString ??= "";     // a value that ends in the delimiter keeps it; it is quoted below

            // A value holding the delimiter, a quote or a line break is quoted even when fields are not, or it would
            // split into extra columns or rows or open a quote when it is read back. Its own quotes are doubled, so it
            // reads back whole.
            bool holdsDelimiter = 0 <= inputString.IndexOf(delimiter) || 0 <= inputString.IndexOf('"') || 0 <= inputString.IndexOfAny(new[] { '\r', '\n' });
            // a value that starts or ends with a space, such as " world" or " ", is quoted, so a reader that trims the spaces
            // around a value keeps them and a one-column line of spaces is not read as a blank line
            bool edgeSpaces = 0 < inputString.Length && (true == char.IsWhiteSpace(inputString[0]) || true == char.IsWhiteSpace(inputString[inputString.Length - 1]));
            bool quoted = true == converterSettings.FieldsEnclosedInQuotes || true == holdsDelimiter || true == edgeSpaces;

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
        /// The names for a header line as wide as its widest row: the names given, then for each column past them a
        /// name made from its column number, Column3 for the third, with _1, _2 added when that name is already taken.
        /// </summary>
        /// <param name="names">The names given.</param>
        /// <param name="width">The number of columns the names are for.</param>
        /// <returns>A new list of the names given followed by the names made.</returns>
        internal static List<string> NamesForWidth(List<string> names, int width)
        {
            List<string> widened = new List<string>(names);

            for (int colIdx = names.Count; colIdx < width; colIdx++)
            {
                string madeName = $"Column{colIdx + 1}";
                int suffix = 0;

                while (widened.Contains(madeName))
                {
                    suffix++;
                    madeName = $"Column{colIdx + 1}_{suffix}";
                }
                widened.Add(madeName);
            }

            return widened;
        }

        /// <summary>
        /// The names of a header line, or the names given for a file, with each name repeated made its own, B, C, B read as B, C, B_1, with _1, _2 added
        /// as the name and number NamesForWidth makes, so each column keeps its values. A warning names each column renamed
        /// when the source is given.
        /// </summary>
        /// <param name="names">The names of the header line.</param>
        /// <param name="source">The file or sheet the names are read from, named in the warning; null for no warning.</param>
        /// <returns>A new list of the names, each one once.</returns>
        internal static List<string> UniqueNames(List<string> names, string source)
        {
            List<string> unique = new List<string>();

            foreach (string name in names)
            {
                string uniqueName = name;
                int suffix = 0;

                while (true == unique.Contains(uniqueName))
                {
                    suffix++;
                    uniqueName = $"{name}_{suffix}";
                }
                if (null != source && false == Equals(name, uniqueName))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The column names of [{source}] name [{name}] more than once, so the column at [{unique.Count + 1}] is read as [{uniqueName}]", (object)names, GPALObjectType.GPALFile);
                unique.Add(uniqueName);
            }

            return unique;
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
        /// <param name="trimTokens">True to trim the spaces around each value, as a file is read; false keeps them, as text given as input is read.</param>
        /// <returns>
        /// A dictionary keyed by GPALKEY and the row's place among the rows, from 0, each value is a dictionary of column name -> literal token string.
        /// </returns>
        internal static Dictionary<object, dynamic> ConvertDelimitedToDictionary(
            ConverterSettings converterSettings,
            dynamic inputFile,
            string filename = null,
            bool trimTokens = true)
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
                        (object)inputFile,
                        GPALObjectType.GPALFile);
                    return records;
                }

                // a file with no header line takes the column names given for it, as a header line would name them
                if (true != converterSettings?.FirstLineIsColumnHeaders && null != converterSettings?.ColumnNames
                    && 0 < converterSettings.ColumnNames.Count() && 0 < converterSettings.ColumnNames[0].Count)
                    headers = UniqueNames(converterSettings.ColumnNames[0], filename ?? "the input");

                string line;
                bool firstLineRead = false;     // blank lines before it are skipped, so the header is the first line with text
                Queue<string> heldLines = new Queue<string>();      // lines read past a quote that is never closed, read again as lines of their own
                Queue<string> heldLineEnds = new Queue<string>();   // the line break after each held line
                int carriedChar = -1;           // the character read after a \r that is not a \n, which starts the next line

                // Reads the next line as ReadLine does, a held line first, and gives the line break that ended it, \r\n, \n,
                // \r, or "" at the end of the input, so a line break inside a quoted value is kept as written. Null at the end
                string ReadLineAndEnd(out string lineEnd)
                {
                    string readLine = null;
                    lineEnd = "";
                    if (0 < heldLines.Count)
                    {
                        readLine = heldLines.Dequeue();
                        lineEnd = heldLineEnds.Dequeue();
                    }
                    else
                    {
                        StringBuilder lineText = new StringBuilder();
                        int nextChar = -1 != carriedChar ? carriedChar : reader.Read();
                        bool anyRead = -1 != nextChar;
                        carriedChar = -1;
                        while (-1 != nextChar && '\n' != nextChar && '\r' != nextChar)
                        {
                            lineText.Append((char)nextChar);
                            nextChar = reader.Read();
                        }
                        if ('\r' == nextChar)
                        {
                            int afterReturn = reader.Read();
                            lineEnd = '\n' == afterReturn ? "\r\n" : "\r";
                            carriedChar = '\n' == afterReturn ? -1 : afterReturn;
                        }
                        else if ('\n' == nextChar)
                            lineEnd = "\n";
                        readLine = true == anyRead ? lineText.ToString() : null;
                    }
                    return readLine;
                }

                string firstLineEnd;
                while (null != (line = ReadLineAndEnd(out firstLineEnd)))
                {
                    // a blank line is no row, in a file of one column as in a file of more; an empty value of a one-column
                    // file is written "", which is a row
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        lineNumber++;
                        continue;
                    }

                    // A quoted value can hold a line break, so a line that ends inside a quoted value goes on with the
                    // next line, joined by the line break read between them. A quote opens a quoted value only at the start
                    // of a value, as SplitRespectingQuotes reads it. Each line is scanned once, going on from where the
                    // line before it ended
                    List<string> segments = new List<string> { line };          // this line and the lines joined to it
                    List<string> segmentEnds = new List<string> { firstLineEnd };
                    bool insideQuotes = false;
                    bool valueStarted = false;
                    int openSegment = 0;        // the line, of segments, where the open quoted value started
                    bool scanning = true;
                    while (true == scanning)
                    {
                        string segment = segments[segments.Count - 1];
                        for (int charIdx = 0; charIdx < segment.Length; charIdx++)
                        {
                            char lineChar = segment[charIdx];
                            if (true == insideQuotes && '"' == lineChar && charIdx + 1 < segment.Length && '"' == segment[charIdx + 1])
                                charIdx++;     // a doubled quote is one quote
                            else if (true == insideQuotes && '"' == lineChar)
                                insideQuotes = false;
                            else if (false == insideQuotes && '"' == lineChar && false == valueStarted)
                            {
                                insideQuotes = true;
                                valueStarted = true;
                                openSegment = segments.Count - 1;
                            }
                            else if (false == insideQuotes && delimiter == lineChar)
                                valueStarted = false;
                            else if (false == char.IsWhiteSpace(lineChar))
                                valueStarted = true;
                        }

                        string nextLineEnd = "";
                        string nextLine = true == insideQuotes ? ReadLineAndEnd(out nextLineEnd) : null;
                        if (null != nextLine)
                        {
                            segments.Add(nextLine);
                            segmentEnds.Add(nextLineEnd);
                        }
                        else
                            scanning = false;
                    }

                    // The input ended inside a quoted value: the row goes up to the line where that value's quote opened,
                    // and the lines after it are read again as lines of their own, so no row is lost
                    int rowSegments = segments.Count;
                    if (true == insideQuotes)
                    {
                        rowSegments = openSegment + 1;
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Line [{lineNumber + 1 + openSegment}] of [{filename ?? "the input"}] opens a quoted value that is never closed, so the lines after it are read as lines of their own", (object)inputFile, GPALObjectType.GPALFile);
                        for (int segmentIdx = rowSegments; segmentIdx < segments.Count; segmentIdx++)
                        {
                            heldLines.Enqueue(segments[segmentIdx]);
                            heldLineEnds.Enqueue(segmentEnds[segmentIdx]);
                        }
                    }
                    StringBuilder rowText = new StringBuilder(segments[0]);
                    for (int segmentIdx = 1; segmentIdx < rowSegments; segmentIdx++)
                        rowText.Append(segmentEnds[segmentIdx - 1]).Append(segments[segmentIdx]);
                    line = rowText.ToString();

                    var tokens = SplitRespectingQuotes(line, delimiter, trimTokens);

                    // First line: header handling
                    if (false == firstLineRead)
                    {
                        firstLineRead = true;
                        if (converterSettings?.FirstLineIsColumnHeaders == true)
                        {
                            headers = UniqueNames(tokens, filename ?? "the input");
                            lineNumber += rowSegments;
                            continue; // skip header row
                        }
                        // No headers: the first line is data
                    }

                    // Row processing
                    if (headers != null)
                    {
                        // a value past the last name goes under a name made from its column number
                        if (tokens.Count > headers.Count)
                        {
                            int namedCount = headers.Count;
                            headers = NamesForWidth(headers, tokens.Count);
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Line [{lineNumber + 1}] of [{filename ?? "the input"}] has [{tokens.Count}] values and the header line names [{namedCount}], so the extra values are under [{string.Join(",", headers.Skip(namedCount))}]", (object)inputFile, GPALObjectType.GPALFile);
                        }

                        // With headers > Dictionary<string, string>
                        // a row shorter than the header line has empty values for the names past its end, as a
                        // spreadsheet reads it, so every name is kept
                        var rowDict = new Dictionary<string, string>();
                        for (int i = 0; i < headers.Count; i++)
                        {
                            rowDict[headers[i]] = i < tokens.Count ? tokens[i] : "";
                        }
                        records[$"GPALKEY{records.Count:D4}"] = rowDict;
                    }
                    else
                    {
                        // No headers > List<string> (raw values, order preserved)
                        records[$"GPALKEY{records.Count:D4}"] = tokens;
                    }

                    lineNumber += rowSegments;
                }

                // rows read before a wider row named more columns get empty values for the new names
                if (headers != null)
                {
                    foreach (var record in records.Values)
                    {
                        Dictionary<string, string> rowDict = record;
                        foreach (string header in headers)
                        {
                            if (false == rowDict.ContainsKey(header))
                            {
                                rowDict[header] = "";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $@"Exception while parsing delimited file",
                    (object)inputFile,
                    GPALObjectType.GPALFile, ex);
            }

            if (records.Count == 0)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $@"No data found in input file [{filename}]",
                    (object)inputFile,
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
        /// <summary>
        /// Splits a line on the delimiter. A quoted token keeps the delimiters inside it; the quotes are dropped and
        /// a doubled quote is one quote. A token holding the delimiter is quoted again when it is written out.<br/>
        /// A quote opens a quoted part only at the start of a token, after any spaces, so a quote inside a token, as in 12" ruler or
        /// He said "hi", stays in it. The next quote that is not doubled closes the quoted part, and the rest of the
        /// token is read as it stands, as Excel reads "Hi" she said. A quoted token keeps the spaces inside its quotes, so
        /// " " is a space, and drops the spaces outside them.
        /// </summary>
        /// <param name="line">One line of delimited text</param>
        /// <param name="delimiter">The character between the tokens</param>
        /// <param name="trim">True to trim the spaces around each token</param>
        /// <returns>The tokens of the line</returns>
        internal static List<string> SplitRespectingQuotes(string line, char delimiter, bool trim = true)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            bool quoted = false;        // the token has a quoted part
            int quotedEnd = 0;          // the length of the token where its quoted part closed
            string beforeQuote = "";    // the spaces before the opening quote

            // A quoted token is what its quotes hold, spaces included, so " " is a space, followed by any text after its
            // closing quote, as in "Hi" she said, which gives Hi she said. Spaces at either end of the token that are
            // outside its quotes are not part of it, so  "Hello, world"   gives Hello, world, "" x gives x and "a" b
            // gives a b
            string Token()
            {
                string text = sb.ToString();
                string afterQuotes = true == quoted ? text.Substring(quotedEnd).TrimEnd() : "";
                return true == quoted
                    ? text.Substring(0, quotedEnd) + (0 == quotedEnd ? afterQuotes.TrimStart() : afterQuotes)
                    : true == trim ? text.Trim() : text;
            }

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (true == inQuotes && c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');     // a doubled quote is one quote
                    i++;
                }
                else if (true == inQuotes && c == '"')
                {
                    inQuotes = false;
                    quotedEnd = sb.Length;
                }
                else if (false == inQuotes && false == quoted && '"' == c && 0 == sb.ToString().Trim().Length)
                {
                    inQuotes = true;
                    quoted = true;
                    beforeQuote = sb.ToString();
                    sb.Clear();     // the spaces before the opening quote
                }
                else if (c == delimiter && false == inQuotes)
                {
                    tokens.Add(Token());
                    sb.Clear();
                    quoted = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            // a quote that never closes opens no quoted part: the quote is dropped, and the rest of the token, the spaces
            // before the quote included, is read as it stands
            if (true == inQuotes)
            {
                quoted = false;
                sb.Insert(0, beforeQuote);
            }
            tokens.Add(Token());
            return tokens;
        }
    }
}
