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

            // A list given its rows under wrapper keys, each a single key that is not a member of the item, such as the
            // {"root": {"row": [...]}} an xml file written to json holds, takes the rows the last wrapper holds; the wrappers
            // before it are opened here, as one wrapper around rows is below
            if (true == IsEnumerableType(createType) && false == IsDictionaryType(createType) && typeof(string) != createType)
            {
                Type wrappedItemType = true == createType.IsArray ? createType.GetElementType() : CollectionItemType(createType);
                // the keys of a dictionary item, an item typed object or a pair are its data, so no wrapper is opened for them
                bool itemHoldsKeys = null == wrappedItemType || typeof(object) == wrappedItemType || true == IsDictionaryType(wrappedItemType) || true == IsKeyValuePairType(wrappedItemType);
                bool IsWrapper(object candidate) => false == itemHoldsKeys && candidate is IDictionary wrapperCandidate && 1 == wrapperCandidate.Count
                    && false == Regex.IsMatch(wrapperCandidate.Keys.Cast<object>().First()?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")
                    && false == HasMember(wrappedItemType, wrapperCandidate.Keys.Cast<object>().First()?.ToString() ?? string.Empty);
                while (true == IsWrapper((object)myDictList) && true == IsWrapper(((IDictionary)myDictList).Values.Cast<object>().First()))
                {
                    myDictList = ((IDictionary)myDictList).Values.Cast<object>().First();
                    dictListLength = ObjectCopier.GetItemCount(null, myDictList);
                }
            }

            if (true == IsDictionaryType(createType) && 2 == DictionaryTypeArguments(createType).Length)
            {
                Type entryRecordType = DictionaryTypeArguments(createType)[1];
                bool entriesAreRecords = (true == IsClassType(entryRecordType) || true == IsMemberStruct(entryRecordType))
                    && typeof(string) != entryRecordType && typeof(object) != entryRecordType && false == IsEnumerableType(entryRecordType) && false == IsSimpleType(entryRecordType);

                // A dictionary of records given under wrapper keys, each a single key whose value holds the records, such
                // as the {"root": {"a": {...}, "b": {...}}} an xml file of the dictionary written to json holds, takes the
                // records the last wrapper holds: a value that holds records of the entry type is the entries, not one entry
                bool HoldsEntries(object candidate) => candidate is IDictionary held && 0 < held.Count
                    && true == held.Values.Cast<object>().All(heldEntry => heldEntry is IDictionary heldRecord && 0 < heldRecord.Count
                        && true == heldRecord.Keys.Cast<object>().Any(memberKey => true == HasMember(entryRecordType, memberKey?.ToString() ?? string.Empty)));
                object wrapped = myDictList;
                while (true == entriesAreRecords && wrapped is IDictionary entryWrapper && 1 == entryWrapper.Count
                    && false == HasMember(entryRecordType, entryWrapper.Keys.Cast<object>().First()?.ToString() ?? string.Empty)
                    && false == HoldsEntries(wrapped) && entryWrapper.Values.Cast<object>().First() is IDictionary)
                    wrapped = entryWrapper.Values.Cast<object>().First();
                // the wrappers are opened only when they end at the entries, so an entry whose record names no member stays
                if (false == ReferenceEquals(wrapped, (object)myDictList) && true == HoldsEntries(wrapped))
                {
                    myDictList = wrapped;
                    dictListLength = ObjectCopier.GetItemCount(null, myDictList);
                }

                // A dictionary of records written to a delimited or xlsx file or a grid as one row, its entries under dotted
                // names such as a.Name and b.Name, as json of the dictionary is written, gives its entries by the names
                // before the dots
                if (true == entriesAreRecords && 0 < ConverterSettings.InDataFormat.Count
                    && (true == IsDelimitedDataFormat(ConverterSettings.InDataFormat[0]) || DataFormat.XLSX == ConverterSettings.InDataFormat[0] || DataFormat.GRID == ConverterSettings.InDataFormat[0])
                    && myDictList is IDictionary oneRowSet && 1 == oneRowSet.Count
                    && true == Regex.IsMatch(oneRowSet.Keys.Cast<object>().First()?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")
                    && oneRowSet.Values.Cast<object>().First() is IDictionary dottedEntries && 0 < dottedEntries.Count
                    && true == dottedEntries.Keys.Cast<object>().All(dottedKey => 0 < (dottedKey?.ToString() ?? string.Empty).IndexOf('.')
                        && false == HasMember(entryRecordType, dottedKey.ToString().Substring(0, dottedKey.ToString().IndexOf('.')))))
                {
                    Dictionary<object, dynamic> entriesByName = new Dictionary<object, dynamic>();
                    foreach (DictionaryEntry dotted in dottedEntries)
                    {
                        string dottedName = dotted.Key.ToString();
                        string entryName = dottedName.Substring(0, dottedName.IndexOf('.'));
                        if (false == entriesByName.ContainsKey(entryName))
                            entriesByName[entryName] = new Dictionary<object, dynamic>();
                        entriesByName[entryName][dottedName.Substring(entryName.Length + 1)] = dotted.Value;
                    }
                    myDictList = entriesByName;
                    dictListLength = entriesByName.Count;
                }
            }

            // A list of pairs given to a dictionary, such as a List<KeyValuePair<string, Item>>, is keyed by each pair's Key
            // and holds its Value; a dictionary whose values are themselves a Key and a Value takes the rows by position.
            // Its keys are the data's, so one entry is not opened as a wrapper below
            bool keyedByPairs = false;
            if (true == IsDictionaryType(createType) && true == IsDictionaryType(myDictList.GetType()) && 0 < dictListLength
                && false == IsKeyValuePairType(DictionaryTypeArguments(createType)[1])
                && false == (HasMember(DictionaryTypeArguments(createType)[1], "Key") && HasMember(DictionaryTypeArguments(createType)[1], "Value"))
                && true == ((IDictionary)myDictList).Keys.Cast<object>().All(rowKey => Regex.IsMatch(rowKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                && true == ((IDictionary)myDictList).Values.Cast<object>().All(row => row is IDictionary pairRow && true == IsPairRecord(pairRow)))
            {
                Dictionary<object, dynamic> byPairKey = new Dictionary<object, dynamic>();
                foreach (IDictionary pairRow in ((IDictionary)myDictList).Values)
                {
                    object pairKey = null;
                    object pairValue = null;
                    foreach (DictionaryEntry pairPart in pairRow)
                    {
                        if (true == string.Equals(pairPart.Key?.ToString(), "Key", StringComparison.OrdinalIgnoreCase))
                            pairKey = pairPart.Value;
                        else
                            pairValue = pairPart.Value;
                    }
                    // a dictionary has no entry for a null key, and one entry for each key, the last pair's
                    if (null == pairKey)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"A pair with a null Key has no entry in [{createType.Name}], so its Value is not added", (object)pairRow, GPALObjectType.Other);
                    else
                    {
                        if (true == byPairKey.ContainsKey(pairKey))
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Key [{pairKey}] is given more than once, so [{createType.Name}] holds its last Value", (object)pairRow, GPALObjectType.Other);
                        byPairKey[pairKey] = pairValue;
                    }
                }
                myDictList = byPairKey;
                dictListLength = byPairKey.Count;
                keyedByPairs = true;
            }

            // Rows given to a class or struct with members, or to a collection or dictionary of one, where a row names no
            // member of it and holds lists of its records, as [{"Title": "T", "Items": [{...}, {...}]}] for a List<Item>,
            // are the records the rows hold, in order, keyed by position. A dictionary's rows are taken so only when
            // keyed by position, GPALKEY and a number, so keys of its own, such as 1 and 5, are kept
            Type rowRecordType = true == IsDictionaryType(createType) ? DictionaryTypeArguments(createType)[1]
                : true == createType.IsArray ? createType.GetElementType()
                : true == IsEnumerableType(createType) ? (true == createType.IsGenericType && 1 == createType.GetGenericArguments().Length ? createType.GetGenericArguments()[0] : CollectionItemType(createType))
                : createType;
            if (false == keyedByPairs && myDictList is IDictionary givenRows && 0 < givenRows.Count
                && true == givenRows.Keys.Cast<object>().All(rowKey => Regex.IsMatch(rowKey?.ToString() ?? string.Empty, true == IsDictionaryType(createType) ? @"^GPALKEY\d+$" : @"^(GPALKEY)?\d+$")))
            {
                Dictionary<object, dynamic> recordRows = new Dictionary<object, dynamic>();
                bool rowsHoldRecords = false;
                int recordIdx = 0;
                foreach (DictionaryEntry givenRow in givenRows)
                {
                    List<object> rowRecords = HeldRecords(givenRow.Value, rowRecordType, columnNames, (object)parentDictionary);
                    rowsHoldRecords = true == rowsHoldRecords || null != rowRecords;
                    foreach (object record in rowRecords ?? new List<object> { givenRow.Value })
                        recordRows[$"GPALKEY{recordIdx++:D4}"] = record;
                }
                if (true == rowsHoldRecords)
                {
                    myDictList = recordRows;
                    dictListLength = recordRows.Count;
                }
            }

            if (true == IsDictionaryType(myDictList.GetType()))
            {
                int nestedDictCount = 0;
                // a null under a row key, such as the second item of the json [{...}, null], is a null row of a list or array
                int nullRowCount = 0;

                foreach (var item in myDictList)
                {
                    if (true == IsDictionaryType(item?.Value?.GetType()))
                    {
                        nestedDictCount++;
                    }
                    else if (null == (object)item.Value && false == IsDictionaryType(createType)
                        && true == Regex.IsMatch(item.Key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                    {
                        nullRowCount++;
                    }
                }

                // A one-row GPALKEY dict ("0000": {...}) has the same shape as a single-key wrapper (XML root, JSON
                // "root"). A row key is always numeric, or GPALKEY and a number, so a single such key is a one-row list
                // (e.g. a client with one MatchedData record, or a json array of one), not a wrapper.
                bool singleRowLooksIndexed = false;
                if (1 == dictListLength && 1 == nestedDictCount)
                {
                    singleRowLooksIndexed = true;
                    foreach (var soloRowItem in myDictList)
                    {
                        string soloRowKeyStr = soloRowItem.Key?.ToString();
                        if (true == soloRowKeyStr?.StartsWith("GPALKEY", StringComparison.Ordinal))
                            soloRowKeyStr = soloRowKeyStr.Substring("GPALKEY".Length);
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
                // A collection of pairs given a dictionary takes its entries as pairs, below, not its values as rows
                Type collectionItemType = createType.IsArray ? createType.GetElementType()
                    : createType.IsGenericType && 1 == createType.GetGenericArguments().Length ? createType.GetGenericArguments()[0] : null;
                bool entriesArePairs = true == IsKeyValuePairType(collectionItemType) && null != PairRecords(myDictList, collectionItemType);

                if (0 < nestedDictCount && nestedDictCount + nullRowCount == dictListLength && (1 < dictListLength || singleRowLooksIndexed) && false == entriesArePairs
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
                        Type dictKeyType = DictionaryTypeArguments(createType)[0];
                        // a row that cannot be one value of the dictionary, as a record is not for a Hashtable, which has
                        // no value type, fills the dictionary itself; one dictionary holds one row, so the first fills it,
                        // with a warning when there are more
                        bool filledFromRow = false;
                        // A row of a delimited or xlsx file whose first column is the Key column GPAL writes for the entries of a
                        // dictionary of records, named Key, or Key_1, Key_2 when the record has a member of the name before,
                        // and not a member of the record, is that entry: the column's value is the entry's key and is not a
                        // member of the record. A row without it is keyed by position, as is a row whose key is empty, null
                        // or already taken, or that is not a key of the dictionary's key type, with a warning; a row keyed by
                        // position takes its place or, when that is a key, the first free place after it, so no row replaces
                        // another. The Key column is never a member of the record, the row keyed by position included
                        Type dictValueType = DictionaryTypeArguments(createType)[1];
                        // rows from json, yaml, xml or a grid that came from such a file keep its Key column, so they are
                        // keyed by it too
                        bool rowsMayBeKeyed = 0 < ConverterSettings.InDataFormat.Count
                            && true == IsSimpleType(dictKeyType) && (true == IsClassType(dictValueType) || true == IsMemberStruct(dictValueType))
                            && typeof(string) != dictValueType && typeof(object) != dictValueType && false == IsEnumerableType(dictValueType);
                        // the Key column of a row keyed by position, or null when the row has none
                        // a row of a delimited or xlsx file or a grid is at a numbered place; a json, yaml or xml dictionary's
                        // own keys, such as 5, are its keys, so only a row the reader numbered, GPALKEY and a number, is at a place
                        string rowPlacePattern = 0 == ConverterSettings.InDataFormat.Count || true == IsDelimitedDataFormat(ConverterSettings.InDataFormat[0])
                            || DataFormat.XLSX == ConverterSettings.InDataFormat[0] || DataFormat.GRID == ConverterSettings.InDataFormat[0] ? @"^(GPALKEY)?\d+$" : @"^GPALKEY\d+$";
                        object KeyColumnOf(object rowPlace, IDictionary keyedRow)
                        {
                            object keyColumn = null != keyedRow && 0 < keyedRow.Count && true == Regex.IsMatch(rowPlace?.ToString() ?? string.Empty, rowPlacePattern)
                                ? keyedRow.Keys.Cast<object>().First() : null;
                            return null != keyColumn && true == Regex.IsMatch(keyColumn.ToString(), @"^Key(_\d+)?$") && false == HasMember(dictValueType, keyColumn.ToString())
                                ? keyColumn : null;
                        }
                        // The keys the rows give in their Key column. A row keyed by its place takes none of them, so it
                        // does not replace a row that names that key, before or after it: it takes the first free place
                        // after its own
                        HashSet<object> namedKeys = new HashSet<object>();
                        // each row's Key value as a key, read once; a value that does not convert to the key type, such as
                        // 9999999999 for an int key, gives no key
                        Dictionary<object, object> rowKeys = new Dictionary<object, object>();
                        if (true == rowsMayBeKeyed)
                            foreach (var namingItem in myDictList)
                            {
                                IDictionary namingRow = namingItem.Value as IDictionary;
                                object namingColumn = KeyColumnOf((object)namingItem.Key, namingRow);
                                if (null != namingColumn && false == string.IsNullOrEmpty(namingRow[namingColumn]?.ToString()))
                                {
                                    valueNotConverted = false;
                                    bool wasConverting = convertingListItem;
                                    convertingListItem = true;
                                    object namedKey = null;
                                    try
                                    {
                                        namedKey = ResolveSimpleDictionaryKey(namingRow[namingColumn]?.ToString(), dictKeyType);
                                    }
                                    finally
                                    {
                                        convertingListItem = wasConverting;
                                    }
                                    if (false == valueNotConverted)
                                    {
                                        rowKeys[(object)namingItem.Key] = namedKey;
                                        namedKeys.Add(namedKey);
                                    }
                                }
                            }
                        // a row that fills nothing of the value type is not added
                        foreach (var item in myDictList)
                        {
                            IDictionary keyedRow = true == rowsMayBeKeyed ? item.Value as IDictionary : null;
                            object rowKeyColumn = KeyColumnOf((object)item.Key, keyedRow);
                            object rowKey = null != rowKeyColumn && true == rowKeys.ContainsKey((object)item.Key) ? rowKeys[(object)item.Key] : null;
                            bool keyColumnFallsBack = null != rowKeyColumn && (null == rowKey || true == tmpDict.Contains(rowKey));
                            // what is wrong with the Key value of a row that falls back to its place
                            string keyColumnFault = null != rowKeyColumn && null == rowKey && false == string.IsNullOrEmpty(keyedRow[rowKeyColumn]?.ToString())
                                ? $"is not a [{ShortTypeName(dictKeyType)}]" : "is empty or already a key";
                            // a row keyed by its place, among rows that may give keys, takes its place or the first free
                            // place after it
                            object placeKey = null;
                            bool placeTaken = false;
                            Match placeMatch = true == rowsMayBeKeyed && (null == rowKeyColumn || true == keyColumnFallsBack)
                                ? Regex.Match(((object)item.Key)?.ToString() ?? string.Empty, @"^(GPALKEY)?([0-9]+)$") : Match.Empty;
                            if (true == placeMatch.Success)
                            {
                                int place = int.Parse(placeMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                                placeKey = ResolveSimpleDictionaryKey($"GPALKEY{place}", dictKeyType);
                                object firstPlaceKey = placeKey;
                                // a key type that holds no place, such as a Guid, gives the same key for every place, and one
                                // of few values, such as a byte enum, comes back to the first, so the search ends there with
                                // the place taken
                                bool searching = true;
                                while (true == searching)
                                {
                                    placeTaken = null != placeKey && (true == tmpDict.Contains(placeKey) || true == namedKeys.Contains(placeKey));
                                    object nextPlaceKey = true == placeTaken ? ResolveSimpleDictionaryKey($"GPALKEY{++place}", dictKeyType) : placeKey;
                                    searching = true == placeTaken && false == Equals(nextPlaceKey, placeKey) && false == Equals(nextPlaceKey, firstPlaceKey);
                                    placeKey = true == searching ? nextPlaceKey : placeKey;
                                }
                            }
                            object ownPlace = true == placeMatch.Success ? ResolveSimpleDictionaryKey(item.Key, dictKeyType) : null;
                            // a row whose every place is a key, as every place of a Guid key is one key, is not added, so
                            // it replaces no row
                            if (true == placeTaken)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The row at place [{ownPlace}] has no free key, as its place [{placeKey}] and every other place a [{ShortTypeName(dictKeyType)}] key can hold are keys, so it is not added", (object)item.Value, GPALObjectType.Other);
                            else if (true == keyColumnFallsBack && true == Equals(ownPlace, placeKey))
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The [{rowKeyColumn}] value [{keyedRow[rowKeyColumn]}] of a row {keyColumnFault}, so the row is keyed by its place [{ownPlace}]", (object)keyedRow, GPALObjectType.Other);
                            else if (true == keyColumnFallsBack)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The [{rowKeyColumn}] value [{keyedRow[rowKeyColumn]}] of the row at place [{ownPlace}] {keyColumnFault}, and the place is a key too, so the row is keyed by [{placeKey}], the first free place after it", (object)keyedRow, GPALObjectType.Other);
                            else if (null != placeKey && false == Equals(ownPlace, placeKey))
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The place [{ownPlace}] of a row is already a key, so the row is keyed by [{placeKey}], the first free place after it", (object)item.Value, GPALObjectType.Other);
                            dynamic rowValue = item.Value;
                            if (null != rowKeyColumn)
                            {
                                Dictionary<object, dynamic> rowMembers = new Dictionary<object, dynamic>();
                                foreach (DictionaryEntry rowEntry in keyedRow)
                                    if (false == Equals(rowKeyColumn, rowEntry.Key))
                                        rowMembers[rowEntry.Key] = rowEntry.Value;
                                rowValue = rowMembers;
                            }

                            // a row for a dictionary of values read from text, such as a csv row of a Dictionary<string,
                            // CultureInfo>, is no one value, so it fills the dictionary itself, below. A row that fills
                            // nothing of the item of a collection value, such as a number for a List<Item>, is not added
                            if (false == placeTaken && (true == HasTextConverter(dictValueType) || false == FillsNothing(item.Value, dictValueType, columnNames, (object)parentDictionary))
                                && false == (true == IsEnumerableType(dictValueType) && false == IsDictionaryType(dictValueType) && typeof(string) != dictValueType
                                    && true == FillsNothing(item.Value, CollectionItemType(dictValueType), columnNames, (object)parentDictionary)))
                            {
                                try
                                {
                                    // a row written for a null record, null in every column after its Key, is a null value
                                    dynamic element = null != rowKeyColumn && (false == dictValueType.IsValueType || true == IsNullableType(dictValueType)) && true == IsNullRecordRow(rowValue)
                                        ? null
                                        : ConvertToClass(rowValue, ConverterSettings.OutputClassElementType, parentDictionary, columnNames);
                                    object resolvedKey = null != rowKeyColumn && false == keyColumnFallsBack
                                        ? rowKey
                                        : null != placeKey
                                        ? placeKey
                                        : IsSimpleType(dictKeyType)
                                        ? ResolveSimpleDictionaryKey(item.Key, dictKeyType)
                                        : (item.Key is IDictionary keyDict ? CreateObjectFromDictionary(keyDict, dictKeyType, parentDictionary, null) : item.Key);
                                    tmpDict[resolvedKey] = element;
                                }
                                catch
                                {
                                    if (false == filledFromRow)
                                        tmpDict = ConvertToClass(item.Value, createType, parentDictionary, columnNames);
                                    filledFromRow = true;
                                }
                            }
                        }
                        if (true == filledFromRow && 1 < dictListLength)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{createType.Name}] holds one row, so it is filled from the first of [{dictListLength}] rows; save to a list to take them all", (object)myDictList, GPALObjectType.Other);
                        result.Add(tmpDict);
                    }
                    else if (true == IsEnumerableType(createType))
                    {
                        object container = InstantiateOne(createType, dictListLength);

                        int index = 0;

                        // ConverterSettings.OutputClassElementType is only correct for the outermost SaveTo target. At
                        // deeper levels (each List<MatchedData> in Dictionary<Client, List<MatchedData>>) it still
                        // holds the outer type, so the row type comes from createType.
                        // A class that is not generic, such as an ArrayList or a class that is the enumerable of its
                        // own item, takes the item type it enumerates.
                        Type rowElementType = createType.IsArray
                            ? createType.GetElementType()
                            : CollectionItemType(createType);

                        // a null row is a null item, where the item can be null; so is a csv or xlsx row written for a null record
                        bool rowItemHoldsNull = false == rowElementType.IsValueType || true == IsNullableType(rowElementType);
                        foreach (var item in myDictList)
                        {
                            if (null == (object)item.Value || (true == rowItemHoldsNull && true == IsNullRecordRow(item.Value)))
                            {
                                if (true == rowItemHoldsNull && true == AddToList(container, null, index))
                                    index++;
                            }
                            // a row that fills nothing of the item is not taken
                            else if (false == FillsNothing(item.Value, rowElementType, columnNames, (object)parentDictionary))
                            {
                                valueNotConverted = false;
                                // a row of one value is converted as an item, which is not added when it does not convert
                                bool wasConverting = convertingListItem;
                                convertingListItem = true == wasConverting || true == IsValueAlone(rowElementType);
                                object element = null;
                                try
                                {
                                    // a row that is an array of two or more dimensions, as of a List<int[,]>, is rows of rows
                                    element = true == rowElementType.IsArray && 1 < rowElementType.GetArrayRank()
                                        ? ConvertValue(item.Value, rowElementType, parentDictionary)
                                        : ConvertToClass(item.Value, rowElementType, parentDictionary, columnNames);
                                }
                                finally
                                {
                                    convertingListItem = wasConverting;
                                }
                                if (true == IsValueAlone(rowElementType) && true == valueNotConverted)
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)item.Value)}] is not a [{ShortTypeName(rowElementType)}], so it is not added", (object)item.Value, GPALObjectType.Other);

                                // Use your existing robust AddToList to add the row to the container
                                // It handles Add, Enqueue, Push, array indexing, dictionary, etc.
                                // A row of one value that does not convert, such as x for an int, is not added, with a warning.
                                // A row none of whose values is added to a list item, such as x,y for an int[], adds no item;
                                // each value said why it is not added
                                bool noRowValueAdded = true == NoValueAdded(rowElementType, element, (object)item.Value);
                                if ((false == IsValueAlone(rowElementType) || false == valueNotConverted) && false == noRowValueAdded && true == AddToList(container, element, index))
                                    index++;
                            }
                        }

                        // an array was made as long as the rows, and holds only the rows taken
                        if (container is Array rowArray && index < rowArray.Length)
                        {
                            Array takenRows = Array.CreateInstance(rowArray.GetType().GetElementType(), index);
                            Array.Copy(rowArray, takenRows, index);
                            container = takenRows;
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
                    // If the sole key has no matching property/field on the target type, step into its value. A class
                    // that is not a collection keeps stepping in, so xml's root and the one element under it both open.
                    dynamic unwrapped = myDictList;
                    bool stepIn = true;
                    // A dictionary keeps the key of its one entry as data when the entry is a record of the dictionary's
                    // value type, a key of it naming one of its members, and, for a value type that is itself a dictionary
                    // or a collection, when a class or a dictionary given as the input holds it, as they have no wrapper
                    // key. The JSON reader's root key and the xml document element at the top are still opened.
                    Type entryValueType = true == IsDictionaryType(createType) ? DictionaryTypeArguments(createType)[1] : null;
                    DataFormat firstInputFormat = 0 < ConverterSettings.InDataFormat.Count ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
                    bool entriesAreData = null != entryValueType && true == IsEnumerableType(entryValueType)
                        && (DataFormat.CLASS == firstInputFormat || DataFormat.DICTIONARY == firstInputFormat);

                    while (true == stepIn && false == keyedByPairs && unwrapped is IDictionary wrapper && 1 == wrapper.Count)
                    {
                        stepIn = false;
                        foreach (var kvp in unwrapped)
                        {
                            if (kvp.Value is IDictionary)
                            {
                                string key = kvp.Key?.ToString() ?? string.Empty;
                                bool isRealProperty =
                                    createType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                                    || createType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                        .Any(f => string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase));
                                bool entryIsValue = true == entriesAreData
                                    || (null != entryValueType && false == IsValueAlone(entryValueType) && false == IsEnumerableType(entryValueType) && typeof(object) != entryValueType
                                        && false == (((DataFormat.JSON == firstInputFormat && "root" == key) || DataFormat.XML == firstInputFormat) && true == ReferenceEquals((object)unwrapped, (object)myDictList))
                                        && true == ((IDictionary)kvp.Value).Keys.Cast<object>().Any(entryKey => HasMember(entryValueType, entryKey?.ToString() ?? string.Empty)));
                                if (false == isRealProperty && false == entryIsValue)
                                {
                                    unwrapped = kvp.Value;
                                    // only a class with members has members to find further in; an object takes it here,
                                    // and a pair its entries, which may hold one key
                                    stepIn = false == IsEnumerableType(createType) && false == IsKeyValuePairType(createType)
                                        && (0 < createType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length
                                            || 0 < createType.GetFields(BindingFlags.Public | BindingFlags.Instance).Length);
                                }
                            }
                            break;
                        }
                    }

                    // One object given rows, a list inside the data or under a wrapper key, takes the first, with a warning
                    // when there are more, and the rest are not read. Rows are keyed by position, or are the list one key
                    // holds when the class has no member of that name.
                    List<object> oneObjectRows = null;
                    bool holdsOne = false == IsEnumerableType(createType) && typeof(object) != createType;

                    if (true == holdsOne && unwrapped is IDictionary rowSet && 1 < rowSet.Count
                        && rowSet.Keys.Cast<object>().All(rowKey => Regex.IsMatch(rowKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                        && rowSet.Values.Cast<object>().All(row => row is IDictionary))
                        oneObjectRows = rowSet.Keys.Cast<object>().OrderBy(rowKey => rowKey.ToString(), StringComparer.Ordinal).Select(rowKey => rowSet[rowKey]).ToList();
                    else if (true == holdsOne && unwrapped is IDictionary listOwner && 1 == listOwner.Count
                        && listOwner.Values.Cast<object>().First() is IList ownedRows && 0 < ownedRows.Count && ownedRows.Cast<object>().All(row => row is IDictionary)
                        && false == HasMember(createType, listOwner.Keys.Cast<object>().First()?.ToString() ?? string.Empty)
                        // the rows are of this class: a key of the first is one of its members; extra keys are passed over, as in any fill
                        && ((IDictionary)ownedRows[0]).Keys.Cast<object>().Any(rowKey => HasMember(createType, rowKey?.ToString() ?? string.Empty)))
                        oneObjectRows = ownedRows.Cast<object>().ToList();
                    // one pair given a dictionary's entries takes the first
                    else if (true == holdsOne && true == IsKeyValuePairType(createType))
                        oneObjectRows = PairRecords(unwrapped, createType);
                    // a record that names no member of the class and holds lists of its records gives those records
                    else if (true == holdsOne)
                        oneObjectRows = HeldRecords(unwrapped, createType, columnNames, (object)parentDictionary);

                    if (null != oneObjectRows)
                    {
                        unwrapped = oneObjectRows[0];

                        if (1 < oneObjectRows.Count)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{createType.Name}] holds one object, so it is filled from the first of [{oneObjectRows.Count}] rows; save to a list to take them all", oneObjectRows, GPALObjectType.Other);
                    }

                    // A collection given one key that holds a list, as xml's repeated elements of one name under its
                    // root, takes that list as its rows. A dictionary keys them by position, as rows read from json are,
                    // when it holds single objects; one holding lists or objects takes the key and its list as they are.
                    Type heldDictionaryValueType = true == IsDictionaryType(createType) ? DictionaryTypeArguments(createType)[1] : null;
                    bool takesHeldRows = true == IsEnumerableType(createType)
                        && (false == IsDictionaryType(createType)
                            || (null != heldDictionaryValueType && typeof(object) != heldDictionaryValueType && false == IsEnumerableType(heldDictionaryValueType)));
                    IList heldRows = null;
                    // a csv row is a list of values under a row key: its values are not rows of a class or struct
                    Type rowsOfType = heldDictionaryValueType ?? (createType.IsArray ? createType.GetElementType()
                        : createType.IsGenericType && 1 == createType.GetGenericArguments().Length ? createType.GetGenericArguments()[0] : null);
                    // a list of values alone under a name, such as {"Tags": ["a", "b"]}, is a member of one dictionary, not
                    // its rows; xml keeps an element's repeated children as such a list
                    bool valuesAreAMember = null != rowsOfType && true == IsDictionaryType(rowsOfType)
                        && false == (0 < ConverterSettings.InDataFormat.Count && DataFormat.XML == ConverterSettings.InDataFormat[0]);
                    if (true == takesHeldRows && unwrapped is IDictionary listHolder && 1 == listHolder.Count
                        && false == (true == valuesAreAMember && listHolder.Values.Cast<object>().First() is IList memberValues
                            && true == memberValues.Cast<object>().Any(memberValue => false == memberValue is IDictionary && false == memberValue is IList))
                        && false == (true == Regex.IsMatch(listHolder.Keys.Cast<object>().First()?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")
                            && null != rowsOfType && false == IsValueAlone(rowsOfType) && typeof(object) != rowsOfType && false == IsEnumerableType(rowsOfType)
                            && listHolder.Values.Cast<object>().First() is IList rowValues && false == rowValues.Cast<object>().Any(rowValue => rowValue is IDictionary))
                        // a list under one row key, as json's [[...]] gives, is that one row when the item is a collection
                        // itself, such as the Person[] of a List<Person[]>, not the rows
                        && false == (true == Regex.IsMatch(listHolder.Keys.Cast<object>().First()?.ToString() ?? string.Empty, @"^GPALKEY\d+$")
                            && null != rowsOfType && true == IsEnumerableType(rowsOfType) && false == IsDictionaryType(rowsOfType)
                            && listHolder.Values.Cast<object>().First() is IList))
                        foreach (object heldValue in listHolder.Values)
                            heldRows = heldValue as IList;

                    // One element of that name is not a list but the item itself: when every key is a member of the
                    // collection's item type, it is a list of one. A Nullable, such as a Point?, is one value, not a
                    // collection of its type argument. A class derived from a generic collection, such as Items :
                    // List<Item>, has the item type of that collection
                    Type heldItemType = createType.IsArray ? createType.GetElementType()
                        : true == IsEnumerableType(createType) && createType.IsGenericType && 1 == createType.GetGenericArguments().Length ? createType.GetGenericArguments()[0]
                        : null != GenericCollectionBase(createType) ? GenericCollectionBase(createType).GetGenericArguments()[0] : null;
                    if (null == heldRows && null != heldItemType && false == IsDictionaryType(createType)
                        && unwrapped is IDictionary oneItem && 0 < oneItem.Count
                        && ((false == ReferenceEquals((object)unwrapped, (object)myDictList)
                                && true == oneItem.Keys.Cast<object>().All(itemKey => HasMember(heldItemType, itemKey?.ToString() ?? string.Empty)))
                            // a record for an item of a class or struct with members is one object when a key names one of
                            // them, given alone or under a wrapper key; its other keys are passed over, as in any fill
                            || (false == IsValueAlone(heldItemType) && typeof(object) != heldItemType && false == IsEnumerableType(heldItemType)
                                && false == IsKeyValuePairType(heldItemType)
                                && false == oneItem.Keys.Cast<object>().All(itemKey => Regex.IsMatch(itemKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                                && true == oneItem.Keys.Cast<object>().Any(itemKey => HasMember(heldItemType, itemKey?.ToString() ?? string.Empty)))))
                        heldRows = new List<object> { unwrapped };
                    // A record of a class or struct with members, given to a collection of collections of it, such as a
                    // List<Person[]>, is one row holding the record, as it is one item of a List<Person>
                    else if (null == heldRows && null != heldItemType && true == IsEnumerableType(heldItemType) && false == IsDictionaryType(heldItemType)
                        && false == IsDictionaryType(createType) && unwrapped is IDictionary nestedRecord && 0 < nestedRecord.Count
                        && false == nestedRecord.Keys.Cast<object>().All(recordKey => Regex.IsMatch(recordKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                        && true == nestedRecord.Keys.Cast<object>().Any(recordKey => HasMember(CollectionItemType(heldItemType), recordKey?.ToString() ?? string.Empty)))
                        heldRows = new List<object> { new List<object> { unwrapped } };
                    // A list of dictionaries given one object, such as a JSON file's object or a class given as the input,
                    // takes it as its one dictionary; any key is a dictionary's member. Rows keyed by position are rows
                    // A list of objects, such as a List<object> or an ArrayList, takes it as its one item in the same way.
                    else if (null == heldRows && false == IsDictionaryType(createType)
                        && (true == IsDictionaryType(heldItemType) || (true == IsEnumerableType(createType) && typeof(object) == CollectionItemType(createType)))
                        && unwrapped is IDictionary oneRecord && 0 < oneRecord.Count
                        && false == oneRecord.Keys.Cast<object>().All(recordKey => Regex.IsMatch(recordKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")))
                        heldRows = new List<object> { unwrapped };
                    // One element of a name that holds a record of a pair or a struct, as xml gives a list of one, is a list
                    // of one: a pair's Key and Value, or a struct's members
                    else if (null == heldRows && true == IsEnumerableType(createType) && null != heldItemType && false == IsDictionaryType(createType)
                        && unwrapped is IDictionary itemHolder && 1 == itemHolder.Count
                        && (true == IsKeyValuePairType(heldItemType)
                            ? true == IsRecordOfPair(itemHolder.Values.Cast<object>().First(), heldItemType)
                            : true == heldItemType.IsValueType && false == IsValueAlone(heldItemType) && false == HasMember(heldItemType, itemHolder.Keys.Cast<object>().First()?.ToString() ?? string.Empty)
                                && itemHolder.Values.Cast<object>().First() is IDictionary structRecord && 0 < structRecord.Count
                                // a key of the record is one of the struct's members; extra keys, such as xml attributes, are passed over
                                && true == structRecord.Keys.Cast<object>().Any(recordKey => HasMember(heldItemType, recordKey?.ToString() ?? string.Empty))))
                        heldRows = new List<object> { itemHolder.Values.Cast<object>().First() };
                    // A collection of pairs given a dictionary takes each entry as a pair
                    else if (null == heldRows && true == IsEnumerableType(createType) && true == IsKeyValuePairType(heldItemType))
                        heldRows = PairRecords(unwrapped, heldItemType);

                    // A record that names no member of the item and holds lists of its records, such as {"Title": "T",
                    // "Items": [{...}, {...}]} for a List<Item>, gives every record it holds; a class of its own that
                    // enumerates its items, given a record that fills none of its members, holds the item it enumerates
                    if (null == heldRows && false == IsDictionaryType(createType))
                        heldRows = HeldRecords(unwrapped, heldItemType ?? (true == IsEnumerableType(createType) && false == FillsMembers(createType, (object)unwrapped) ? CollectionItemType(createType) : null),
                            columnNames, (object)parentDictionary);

                    // The rows go on keyed by position, as rows read from json or csv are, so one row is not taken for
                    // the item itself
                    Dictionary<object, dynamic> keyedRows = new Dictionary<object, dynamic>();
                    int rowIdx = 0;

                    if (null != heldRows)
                        foreach (object heldRow in heldRows)
                            keyedRows[$"GPALKEY{rowIdx++:D4}"] = heldRow;

                    // A value, such as a number, text or a date, takes the one value the data holds
                    object[] oneValue = null == heldRows ? HeldValue(myDictList, targetType) : null;

                    return null != heldRows
                        ? ConvertToClass(keyedRows, targetType, parentDictionary, columnNames)
                        : null != oneValue
                            ? ConvertValue(oneValue[0], targetType, parentDictionary)
                            : CreateObjectFromDictionary(unwrapped, createType, parentDictionary, columnNames);
                }
            }
            else if (IsEnumerableObject(myDictList) || IsArray(myDictList))
            {
                Type listType = typeof(List<>).MakeGenericType(createType);
                IList result = (IList)InstantiateOne(listType);
                // an array is made as long as the items it takes
                dynamic createdType = InstantiateOne(createType, dictListLength);
                int count = 0;

                // Each item is the collection's own item type. OutputClassElementType is right only for the outermost
                // target; a list inside it, such as each value of a Dictionary<string, List<T>>, holds T. A class that
                // is not generic takes the item type it enumerates.
                Type itemType = (targetType == ConverterSettings.OutputClassType || createType == ConverterSettings.OutputClassType) && null != ConverterSettings.OutputClassElementType
                    ? ConverterSettings.OutputClassElementType
                    : createType.IsArray ? createType.GetElementType()
                    : createType.IsGenericType && 1 == createType.GetGenericArguments().Length ? createType.GetGenericArguments()[0]
                    : ConverterSettings.OutputClassElementType ?? CollectionItemType(createType);

                // It's a list or an array, so we handle its elements; a value, such as a number in [[1,2],[3,4]], is
                // converted to the item type, and an item that fills nothing of the item type is not taken
                foreach (var item in myDictList)
                {
                    if (false == FillsNothing(item, itemType, columnNames, (object)parentDictionary))
                    {
                        // a value that does not convert is not added, with a warning. Text given to an item that is
                        // a collection, such as hello for an int[], is one value of it, not its characters
                        bool itemIsValue = (true == IsValueAlone(itemType) && false == item is IDictionary && false == item is IList)
                            || (item is string && true == IsEnumerableType(itemType) && false == IsDictionaryType(itemType) && typeof(char) != CollectionItemType(itemType));
                        valueNotConverted = false;
                        // an item that is an array of two or more dimensions, as of a List<int[,]>, is rows of rows
                        object element = true == itemIsValue
                            ? ConvertListItem(item, itemType)
                            : true == itemType.IsArray && 1 < itemType.GetArrayRank()
                            ? ConvertValue(item, itemType, parentDictionary)
                            : CreateObjectFromDictionary(item, itemType, parentDictionary, columnNames);
                        if ((false == itemIsValue || false == valueNotConverted) && true == AddToList(createdType, element, count))
                            count++;
                    }
                }

                // an array was made as long as the items, and holds only the items taken
                if ((object)createdType is Array itemArray && count < itemArray.Length)
                {
                    Array takenItems = Array.CreateInstance(itemArray.GetType().GetElementType(), count);
                    Array.Copy(itemArray, takenItems, count);
                    createdType = takenItems;
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

            // A class of its own that enumerates its items, such as a tree node or a page of rows, given a record whose
            // keys name its members, has those members filled as any class has. A collection from System, a dictionary,
            // an array and a class derived from a generic collection take their items as a collection does.
            bool fillsMembers = true == FillsMembers(targetType, (object)dictionary);

            // XML parsers store every child as a List; for non-enumerable, non-array, non-dict targets
            // unwrap a single-item list to its inner dict so the property loop below can process it.
            if (dictionary is IList cofdSingleList && cofdSingleList.Count == 1 && cofdSingleList[0] is IDictionary cofdInnerDict
                && !isEnumerable && !targetType.IsArray && !IsDictionaryType(targetType))
            {
                dictionary = cofdInnerDict;
            }

            // A row of a delimited or xlsx file holds the members of a member that is a record under dotted names, V.X and
            // V.Y for the X and Y of a member V, as the file was written. They are gathered into a record under V, which
            // fills V as a record read from json does. A name whose first part names no such member is kept as it is, and
            // column names given name a row's values by position, so the row is kept as it is then
            if ((null == columnNames || 0 == columnNames.Count) && 0 < ConverterSettings.InDataFormat.Count
                && (true == IsDelimitedDataFormat(ConverterSettings.InDataFormat[0]) || DataFormat.XLSX == ConverterSettings.InDataFormat[0])
                && dictionary is IDictionary dottedRow && false == isEnumerable && false == targetType.IsArray && false == IsDictionaryType(targetType))
            {
                // the member a dotted name is under, when that member is a record and the row has no value under its own name
                string RecordMemberOf(object key)
                {
                    string keyText = key?.ToString() ?? string.Empty;
                    string memberName = 0 < keyText.IndexOf('.') ? keyText.Substring(0, keyText.IndexOf('.')) : null;
                    Type memberType = null == memberName || true == HasMember(targetType, keyText) || true == dottedRow.Keys.Cast<object>().Any(key => string.Equals(key?.ToString(), memberName, StringComparison.OrdinalIgnoreCase)) ? null
                        : (Type)targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(property => string.Equals(property.Name, memberName, StringComparison.OrdinalIgnoreCase))?.PropertyType
                            ?? targetType.GetFields(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(field => string.Equals(field.Name, memberName, StringComparison.OrdinalIgnoreCase))?.FieldType;
                    bool recordMember = null != memberType
                        && ((true == IsClassType(memberType) && false == IsSimpleType(memberType) && false == IsEnumerableType(memberType))
                            || true == IsMemberStruct(memberType) || true == IsCustomStructType(memberType));
                    return true == recordMember ? memberName : null;
                }

                if (true == dottedRow.Keys.Cast<object>().Any(key => null != RecordMemberOf(key)))
                {
                    Dictionary<object, dynamic> gathered = new Dictionary<object, dynamic>();
                    foreach (DictionaryEntry entry in dottedRow)
                    {
                        string memberName = RecordMemberOf(entry.Key);
                        if (null == memberName)
                            gathered[entry.Key] = entry.Value;
                        else
                        {
                            if (false == gathered.TryGetValue(memberName, out dynamic memberRecord))
                            {
                                memberRecord = new Dictionary<object, dynamic>();
                                gathered[memberName] = memberRecord;
                            }
                            memberRecord[entry.Key.ToString().Substring(memberName.Length + 1)] = entry.Value;
                        }
                    }
                    dictionary = gathered;
                }
            }

            // A row of a delimited or xlsx file holds the items of a member that is a list or an array under numbered
            // names, Tags0 and Tags1 for the items of a member Tags, and Items0.Name and Items0.Age for the members of the
            // first record of a member Items, as the file was written. They are gathered into a list under Tags or Items,
            // which fills the member as a list read from json does. Empty cells at the end are the cells of a row with
            // fewer items than another, so they add no items. A name the class has a member of, a member that is not a
            // list, or a row that has a value under the member's own name keeps the names as they are, and column names
            // given name a row's values by position, so the row is kept as it is then
            if ((null == columnNames || 0 == columnNames.Count) && 0 < ConverterSettings.InDataFormat.Count
                && (true == IsDelimitedDataFormat(ConverterSettings.InDataFormat[0]) || DataFormat.XLSX == ConverterSettings.InDataFormat[0])
                && dictionary is IDictionary numberedRow && false == isEnumerable && false == targetType.IsArray && false == IsDictionaryType(targetType))
            {
                // the list member a numbered name is under, its item number and the name of the item's member after the dot
                Match ListItemOf(object key)
                {
                    string keyText = key?.ToString() ?? string.Empty;
                    Match numbered = Regex.Match(keyText, @"^([A-Za-z_][A-Za-z0-9_]*?)(0|[1-9]\d{0,8})(\.(.+))?$");
                    string memberName = true == numbered.Success ? numbered.Groups[1].Value : null;
                    Type memberType = null == memberName || true == HasMember(targetType, keyText) || true == HasMember(targetType, memberName + numbered.Groups[2].Value)
                        || true == numberedRow.Keys.Cast<object>().Any(rowKey => string.Equals(rowKey?.ToString(), memberName, StringComparison.OrdinalIgnoreCase)) ? null
                        : (Type)targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(property => string.Equals(property.Name, memberName, StringComparison.OrdinalIgnoreCase))?.PropertyType
                            ?? targetType.GetFields(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(field => string.Equals(field.Name, memberName, StringComparison.OrdinalIgnoreCase))?.FieldType;
                    bool listMember = null != memberType && typeof(string) != memberType && false == IsDictionaryType(memberType)
                        && (true == memberType.IsArray || true == IsEnumerableType(memberType));
                    return true == listMember ? numbered : null;
                }

                if (true == numberedRow.Keys.Cast<object>().Any(key => null != ListItemOf(key)))
                {
                    Dictionary<object, dynamic> gathered = new Dictionary<object, dynamic>();
                    Dictionary<string, SortedDictionary<int, dynamic>> listItems = new Dictionary<string, SortedDictionary<int, dynamic>>(StringComparer.OrdinalIgnoreCase);
                    foreach (DictionaryEntry entry in numberedRow)
                    {
                        Match numbered = ListItemOf(entry.Key);
                        if (null == numbered)
                            gathered[entry.Key] = entry.Value;
                        else
                        {
                            string memberName = numbered.Groups[1].Value;
                            int itemNumber = int.Parse(numbered.Groups[2].Value, CultureInfo.InvariantCulture);
                            if (false == listItems.TryGetValue(memberName, out SortedDictionary<int, dynamic> items))
                            {
                                items = new SortedDictionary<int, dynamic>();
                                listItems[memberName] = items;
                                gathered[memberName] = null;
                            }
                            // an item given both as a value, Items0, and as a record, Items0.Name, keeps the second name as it is
                            bool heldBefore = items.TryGetValue(itemNumber, out dynamic itemHeld);
                            if (false == numbered.Groups[4].Success && false == heldBefore)
                                items[itemNumber] = entry.Value;
                            else if (true == numbered.Groups[4].Success && false == heldBefore)
                                items[itemNumber] = new Dictionary<object, dynamic> { [numbered.Groups[4].Value] = entry.Value };
                            else if (true == numbered.Groups[4].Success && itemHeld is IDictionary itemRecord)
                                itemRecord[numbered.Groups[4].Value] = entry.Value;
                            else
                                gathered[entry.Key] = entry.Value;
                        }
                    }

                    foreach (KeyValuePair<string, SortedDictionary<int, dynamic>> member in listItems)
                    {
                        List<object> memberItems = member.Value.Values.Cast<object>().ToList();
                        // a cell is empty when it holds no text; a record is empty when all its cells are
                        while (0 < memberItems.Count && (memberItems[memberItems.Count - 1] is IDictionary lastRecord
                                ? lastRecord.Values.Cast<object>().All(cell => null == cell || (cell is string cellText && 0 == cellText.Length))
                                : null == memberItems[memberItems.Count - 1] || (memberItems[memberItems.Count - 1] is string lastText && 0 == lastText.Length)))
                            memberItems.RemoveAt(memberItems.Count - 1);
                        gathered[member.Key] = memberItems;
                    }
                    dictionary = gathered;
                }
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

            // a Nullable struct, such as a Point?, given a record is filled as the struct it holds
            if (true == IsStructRecord(targetType, (object)dictionary) && null != Nullable.GetUnderlyingType(targetType))
                targetType = Nullable.GetUnderlyingType(targetType);

            // A KeyValuePair's Key and Value cannot be set once it is made, so they are read into a holder with the same
            // two members, and the pair is made from the holder at the end
            Type pairType = null;
            if (true == IsKeyValuePairType(targetType))
            {
                pairType = targetType;
                targetType = typeof(PairHolder<,>).MakeGenericType(PairTypeArguments(pairType));
            }

            if (true == isEnumerable || targetType.IsArray)
                count = ObjectCopier.GetItemCount(null, dictionary);

            if (IsComplexType(targetType))
                return ComplexFromDict(dictionary);

            // When the target is object (e.g. Dictionary<string, object> value slot), preserve the
            // input dict as-is. Trying to map its keys to System.Object properties produces spurious
            // "No property/field" warnings and discards the data.
            if (targetType == typeof(object) && dictionary is IDictionary)
                return ObjectSlotValue((object)dictionary);

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
                    Type keyType = DictionaryTypeArguments(targetType)[0];
                    Type valueType = DictionaryTypeArguments(targetType)[1];
                    IDictionary dict = (IDictionary)objOrList; // Cast to IDictionary for indexing
                    // Rows of exactly two values with no header line, as a csv, tsv, xlsx file or grid of a dictionary of
                    // values is written with no header line, given to a dictionary whose keys and values are both read from
                    // text or held as one value, such as a Dictionary<int, string>, a Dictionary<Point, int> or a
                    // Dictionary<IPAddress, int>, are each a key and its value; a row whose key does not convert is left out
                    // with a warning, and the other rows are kept. Rows of another width are keyed by position. The reader
                    // gives a file of one such row as its two values alone; for a dictionary with text keys, such a row is
                    // the one record its values are written as, as a Dictionary<string, int> is written
                    bool pairSource = 0 < ConverterSettings.InDataFormat.Count && true != ConverterSettings.FirstLineIsColumnHeaders && typeof(object) != keyType
                        && (true == IsDelimitedDataFormat(ConverterSettings.InDataFormat[0]) || DataFormat.XLSX == ConverterSettings.InDataFormat[0] || DataFormat.GRID == ConverterSettings.InDataFormat[0])
                        && (true == IsValueAlone(keyType) || true == HasTextConverter(keyType) || (true == IsSimpleType(keyType) && false == IsMemberStruct(keyType)))
                        && typeof(object) != valueType
                        && (true == IsValueAlone(valueType) || true == HasTextConverter(valueType) || (true == IsSimpleType(valueType) && false == IsMemberStruct(valueType)))
                        && 0 < dictInput.Count
                        && dictInput.Keys.Cast<object>().All(rowKey => Regex.IsMatch(rowKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"));
                    bool pairRows = true == pairSource
                        && dictInput.Values.Cast<object>().All(row => row is IList pair && 2 == pair.Count && null != pair[0]
                            && pair.Cast<object>().All(pairValue => false == pairValue is IDictionary && false == pairValue is IList));
                    bool onePairRow = true == pairSource && false == pairRows && 2 == dictInput.Count && typeof(string) != keyType
                        && dictInput.Values.Cast<object>().All(pairValue => null != pairValue && false == pairValue is IDictionary && false == pairValue is IList);
                    // the rows' pairs in order, a key given twice included, so a row is not lost unseen
                    IEnumerable entries = dictInput;
                    if (true == pairRows)
                        entries = dictInput.Values.Cast<IList>().Select(pair => new KeyValuePair<object, object>(pair[0], pair[1])).ToList();
                    else if (true == onePairRow)
                        entries = new List<KeyValuePair<object, object>> { new KeyValuePair<object, object>(dictInput.Values.Cast<object>().First(), dictInput.Values.Cast<object>().Last()) };
                    // a list value as xml writes one, its items under one element name, such as {"row": [...]}, is those
                    // items; for a value typed object, read from xml, when it holds two or more, as one cannot be told from
                    // a record
                    Type listValueItemType = true == IsEnumerableType(valueType) && false == IsDictionaryType(valueType) && typeof(string) != valueType
                        ? CollectionItemType(valueType)
                        : null;
                    bool objectValuesFromXml = typeof(object) == valueType && 0 < ConverterSettings.InDataFormat.Count && DataFormat.XML == ConverterSettings.InDataFormat[0];
                    if (null != listValueItemType || true == objectValuesFromXml)
                        entries = entries.Cast<object>().Select(entry =>
                        {
                            dynamic given = entry;
                            object entryValue = given.Value;
                            IList items = ListWrapperItems(entryValue, listValueItemType);
                            return new KeyValuePair<object, object>((object)given.Key, (object)items ?? entryValue);
                        }).ToList();
                    foreach (dynamic kvp in entries)
                    {
                        // a key that does not convert, such as x for an int key, is not added, with a warning
                        // a key written as a record, as a key of a struct of the program's own or of a class is, is made from
                        // its members, which report their own values that do not convert; a record given to a key read from
                        // text, such as an int, a Guid or a string, is no key
                        bool recordKey = (object)kvp.Key is IDictionary && (false == IsSimpleType(keyType) || true == IsMemberStruct(keyType));
                        bool wasConverting = convertingListItem;
                        convertingListItem = true == wasConverting || (false == recordKey && true == IsSimpleType(keyType));
                        valueNotConverted = false;
                        object key = null;
                        bool keyThrew = false;
                        try
                        {
                            key = true == recordKey ? CreateObjectFromDictionary(kvp.Key, keyType, dictInput, null)
                                : true == IsSimpleType(keyType) ? ResolveSimpleDictionaryKey(kvp.Key, keyType)
                                : kvp.Key;
                        }
                        catch (Exception ex)
                        {
                            keyThrew = true;
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"The key [{DescribeForLog((object)kvp.Key)}] is not a [{ShortTypeName(keyType)}], so its entry is not added", (object)kvp, GPALObjectType.Other, ex);
                        }
                        finally
                        {
                            convertingListItem = wasConverting;
                        }
                        bool keyConverts = false == keyThrew && null != key
                            && (true == recordKey || false == (true == IsSimpleType(keyType) && true == valueNotConverted))
                            && (true == recordKey || false == (object)kvp.Key is IDictionary || typeof(object) == keyType);
                        if (false == keyConverts && false == keyThrew)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The key [{DescribeForLog((object)kvp.Key)}] is not a [{ShortTypeName(keyType)}], so its entry is not added", (object)kvp, GPALObjectType.Other);
                        object value = kvp.Value?.ToString() ?? string.Empty; // every reader already took off its quoting (csv, json, yaml, xml), so a quote here is data
                        // a value that fills nothing of the value type is not added
                        bool takeValue = true;

                        try
                        {
                            // a value under a key that does not convert is not added
                            if (false == keyConverts)
                            {
                                takeValue = false;
                            }
                            // a null, or the text null of a csv, tsv, xlsx, grid, xml or html source, is a null, for a value that
                            // can hold one, such as the null of {"a": null} for a Dictionary<string, Item>
                            else if ((null == (object)kvp.Value || true == IsNullText((object)kvp.Value)) && (false == valueType.IsValueType || true == IsNullableType(valueType)))
                            {
                                value = null;
                            }
                            // Check if kvp.Value is already of the target valueType
                            else if (null != kvp.Value && valueType.IsAssignableFrom(kvp.Value.GetType()))
                            {
                                // a value typed object, or a record or list of the reader's taken as it is, such as a List<object>,
                                // is as an object holds it: json's and yaml's numbers as doubles and lists as lists
                                value = typeof(object) == valueType || kvp.Value is IDictionary || kvp.Value is IList ? ObjectSlotValue((object)kvp.Value) : (object)kvp.Value;
                            }
                            // a value that is an array of two or more dimensions, as of a Dictionary<string, int[,]>, takes rows of rows
                            else if (true == valueType.IsArray && 1 < valueType.GetArrayRank())
                            {
                                value = ConvertValue(kvp.Value, valueType, dictInput, kvp.Key?.ToString());
                            }
                            else if (true == FillsNothing((object)kvp.Value, valueType, columnNames, dictInput))
                            {
                                takeValue = false;
                            }
                            // Handle dictionary input for class type
                            else if (kvp.Value is IDictionary && (IsClassType(valueType) || IsMemberStruct(valueType) || IsTupleType(valueType)) && valueType != typeof(string))
                            {
                                value = ConvertToClass(kvp.Value, valueType, dictInput, columnNames); // Map dictionary to class or struct
                            }
                            // A list of records for one object, such as {"a": [{...}, {...}]} into a Dictionary<string, Member>,
                            // is keyed by position, as rows read from json are, so the object takes the first, with a warning
                            // when there are more
                            else if (kvp.Value is IList recordList && 0 < recordList.Count && recordList.Cast<object>().All(record => record is IDictionary)
                                && (IsClassType(valueType) || IsMemberStruct(valueType) || IsTupleType(valueType)) && valueType != typeof(string) && false == IsEnumerableType(valueType))
                            {
                                Dictionary<object, dynamic> keyedRecords = new Dictionary<object, dynamic>();
                                int recordIdx = 0;
                                foreach (object record in recordList)
                                    keyedRecords[$"GPALKEY{recordIdx++:D4}"] = record;
                                value = ConvertToClass(keyedRecords, valueType, dictInput, columnNames);
                            }
                            // Handle dictionary input for dictionary type
                            else if (IsDictionaryType(valueType) && kvp.Value is IDictionary)
                            {
                                value = CreateObjectFromDictionary(kvp.Value, valueType, dictInput, columnNames); // Recursive dictionary conversion
                            }
                            // A record or a list given to one value, such as text or a number, gives its first value, with a
                            // warning when it holds more. One that holds no value, or whose first value does not convert,
                            // is not added.
                            else if ((kvp.Value is IDictionary || kvp.Value is IList) && true == IsValueAlone(valueType))
                            {
                                object[] firstValue = HeldValue((object)kvp.Value, valueType);

                                valueNotConverted = false;
                                if (null == firstValue)
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvp.Value)}] under [{key}] holds no value for a [{ShortTypeName(valueType)}], so it is not added", (object)kvp.Value, GPALObjectType.Other);
                                else
                                    value = ConvertListItem(firstValue[0], valueType, key?.ToString());
                                takeValue = null != firstValue && false == valueNotConverted;
                            }
                            // Handle simple types; a value that does not convert, such as x for an int, is not added
                            else if (IsSimpleType(valueType) || IsNullableType(valueType))
                            {
                                valueNotConverted = false;
                                value = ConvertListItem(kvp.Value, valueType, key?.ToString());
                                takeValue = false == valueNotConverted;
                            }
                            // Handle list/array values (e.g., Dictionary<string, List<T>>)
                            else if (IsEnumerableType(valueType) && valueType != typeof(string))
                            {
                                // the item type, also for a list subclass such as Items : List<Item>, which has no type
                                // arguments of its own
                                Type elemType = CollectionItemType(valueType);
                                if (elemType != null)
                                {
                                    int i2 = 0;
                                    IEnumerable innerEnum = kvp.Value is IEnumerable enumer && !(kvp.Value is string)
                                        ? enumer
                                        : new object[] { kvp.Value };
                                    // an array is made as long as the items, and holds only the items added
                                    dynamic resultList = InstantiateOne(valueType, innerEnum.Cast<object>().Count());
                                    foreach (var innerItem in innerEnum)
                                    {
                                        // a record that fills nothing of the item is not added
                                        if (true == FillsNothing(innerItem, elemType, columnNames, dictInput))
                                        {
                                        }
                                        else if (innerItem is IDictionary innerDict && (true == IsClassType(elemType) || true == IsMemberStruct(elemType) || true == IsTupleType(elemType)) && typeof(string) != elemType)
                                        {
                                            if (true == AddToList(resultList, CreateObjectFromDictionary(innerDict, elemType, dictInput, columnNames), i2))
                                                i2++;
                                        }
                                        // an item that does not convert, such as x for an int, is not added
                                        else if (IsSimpleType(elemType) || IsNullableType(elemType))
                                        {
                                            valueNotConverted = false;
                                            object element = ConvertListItem(innerItem, elemType, key?.ToString());
                                            if (false == valueNotConverted && true == AddToList(resultList, element, i2))
                                                i2++;
                                        }
                                        else if (IsBigIntegerType(elemType))
                                        {
                                            if (innerItem is string biNullStr2 && IsNullableType(elemType) && biNullStr2.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                            {
                                                if (true == AddToList(resultList, null, i2))
                                                    i2++;
                                            }
                                            else
                                            {
                                                try { if (true == AddToList(resultList, AsBigInteger(innerItem), i2)) i2++; }
                                                catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)innerItem)}] is not a [{ShortTypeName(elemType)}], so it is not added", (object)innerItem, GPALObjectType.Other); }
                                            }
                                        }
                                        // an item that is itself a list, as each list of a Dictionary<string, List<List<string>>>
                                        // value is, is converted to the item's list type, xml's items under one element name included
                                        else if (true == IsEnumerableType(elemType) && false == IsDictionaryType(elemType) && typeof(string) != elemType
                                            && (innerItem is IList || innerItem is IDictionary))
                                        {
                                            if (true == AddToList(resultList, ConvertValue(innerItem, elemType), i2))
                                                i2++;
                                        }
                                        else
                                        {
                                            if (true == AddToList(resultList, typeof(object) == elemType ? ObjectSlotValue((object)innerItem) : (object)innerItem, i2))
                                                i2++;
                                        }
                                    }
                                    if ((object)resultList is Array valueArray && i2 < valueArray.Length)
                                    {
                                        Array addedValues = Array.CreateInstance(valueArray.GetType().GetElementType(), i2);
                                        Array.Copy(valueArray, addedValues, i2);
                                        resultList = addedValues;
                                    }
                                    value = resultList;
                                    // a value none of whose items is added, such as text for a List<Item>, is left out, as a
                                    // record that fills nothing of a class value is; an empty list stays an empty list
                                    takeValue = 0 < i2 || 0 == innerEnum.Cast<object>().Count();
                                }
                            }

                            // a key a later row of pairs gives again is not added, so the first row's entry stays
                            bool keyGivenAgain = true == takeValue && (true == pairRows || true == onePairRow) && true == dict.Contains(key);
                            if (true == keyGivenAgain)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The key [{DescribeForLog(key)}] is given again, so its entry [{DescribeForLog(value)}] is not added", (object)kvp, GPALObjectType.Other);
                            else if (true == takeValue)
                                dict[key] = value; // Assign to dictionary
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add key[{key}]-value[{value}] pair to dictionary [{targetType}]", (object)kvp, GPALObjectType.Other, ex);
                        }
                    }
                    return dict;
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Expected IDictionary input for [{targetType}]", (object)dictionary, GPALObjectType.None);
                }
            }

            // New case for Queue<T>, Stack<T>, Hashtable, and other enumerables
            else if (true == isEnumerable && false == fillsMembers)
            {
                // the item type the collection enumerates; object for an ArrayList, so a text value is kept whole
                Type elementType = CollectionItemType(targetType);

                // count held the number of items an array was made for; the items are placed from its start
                count = 0;

                // the values of a list inside a list, such as the strings of a string[][], held under one name of their own, as
                // xml writes <item><row>a</row><row>b</row></item>: that one key, not a member of the item, holds the list.
                // So do the records or lists of a list of records or of lists, such as a List<Item> or List<List<string>>
                // value of a dictionary, under a key that is not a number, as rows are keyed
                bool itemsAreValues = true == IsValueAlone(elementType) || true == HasTextConverter(elementType);
                if (dictionary is IDictionary valueHolder && 1 == valueHolder.Count && typeof(object) != elementType
                    && false == IsKeyValuePairType(elementType)
                    && false == HasMember(elementType, valueHolder.Keys.Cast<object>().First()?.ToString() ?? string.Empty)
                    && valueHolder.Values.Cast<object>().First() is IList heldValues
                    && ((true == itemsAreValues && false == heldValues.Cast<object>().Any(heldValue => heldValue is IDictionary || heldValue is IList))
                        || (false == itemsAreValues && false == IsDictionaryType(elementType)
                            && false == Regex.IsMatch(valueHolder.Keys.Cast<object>().First()?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))))
                {
                    dictionary = heldValues;
                    // an array is made again as long as the values
                    objOrList = ConverterHelper.InstantiateOne(targetType, heldValues.Count);
                }

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
                                // a value that fills nothing of the item is not added
                                if (true == FillsNothing(value, elementType, columnNames, (object)keys))
                                {
                                }
                                else if (value is IDictionary classDict && IsClassType(elementType) && typeof(string) != elementType)
                                {
                                    object classInstance = CreateObjectFromDictionary(classDict, elementType, keys, columnNames);
                                    if (classInstance != null)
                                    {
                                        if (true == AddToList(objOrList, classInstance, count))
                                            count++;
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ConvertToClass returned null for [{elementType}] with dict [{classDict}]", classDict, GPALObjectType.Other);
                                    }
                                }
                                else if (IsSimpleType(elementType) && (value == null || IsSimpleType(value.GetType())))
                                {
                                    // Handle simple types directly; a value that does not convert is not added
                                    valueNotConverted = false;
                                    object convertedValue = ConvertListItem(value, elementType);
                                    if (false == valueNotConverted && true == AddToList(objOrList, convertedValue, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Expected IDictionary for TestClass or simple type, got [{value?.GetType()}] for element type [{elementType}]", (object)key, GPALObjectType.Other);
                                }
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add value to enumerable [{targetType}]", (object)key, GPALObjectType.Other, ex);
                            }
                        }
                    }
                    // A record that holds records or lists under names, such as an html element of a tag, its attributes
                    // and its children, is one item, not a set of values, for an item that is read from text or from a
                    // record of its members, such as a CultureInfo or a PointF, so its tag is not taken for an item; when
                    // none of its keys names a member it adds nothing, with a warning. Rows keyed by position are rows.
                    else if (true == IsStructOrTextType(elementType)
                        && false == dictInput.Keys.Cast<object>().All(key => Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                        && true == dictInput.Values.Cast<object>().Any(entryValue => entryValue is IDictionary || entryValue is IList)
                        && true == FillsNothing((object)dictInput, elementType, columnNames, (object)dictionary))
                    {
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
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", (object)kvp, GPALObjectType.Other);
                                        }
                                        else
                                        {
                                            if (true == AddToList(objOrList, null, count))
                                                count++;
                                        }
                                    }
                                    else if (elementType.IsAssignableFrom(value.GetType()))
                                    {
                                        if (true == AddToList(objOrList, typeof(object) == elementType ? ObjectSlotValue((object)value) : (object)value, count))
                                            count++;
                                    }
                                    // an item that is an array of two or more dimensions, as of a List<int[,]>, is rows of rows
                                    else if (true == elementType.IsArray && 1 < elementType.GetArrayRank())
                                    {
                                        if (true == AddToList(objOrList, ConvertValue(value, elementType, dictionary), count))
                                            count++;
                                    }
                                    // a value that fills nothing of the item is not added
                                    else if (true == FillsNothing((object)value, elementType, columnNames, (object)dictionary))
                                    {
                                    }
                                    // a row that is a list of records, as a json list of record lists gives, adds each record as
                                    // one dictionary
                                    else if (true == IsDictionaryType(elementType) && value is IList recordRow && 0 < recordRow.Count
                                        && true == recordRow.Cast<object>().All(record => record is IDictionary))
                                    {
                                        foreach (object record in recordRow)
                                        {
                                            if (true == AddToList(objOrList, CreateObjectFromDictionary(record, elementType, dictionary, columnNames), count))
                                                count++;
                                        }
                                    }
                                    else if (IsCustomStructType(elementType))
                                    {
                                        // Handle structs not covered by other checks
                                        var converter = TypeDescriptor.GetConverter(elementType);
                                        if (converter != null && converter.CanConvertFrom(value.GetType()))
                                        {
                                            value = converter.ConvertFrom(value);
                                            if (true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        else
                                        {
                                            try
                                            {
                                                if (true == AddToList(objOrList, value, count))
                                                    count++;
                                            }
                                            catch
                                            {
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported struct type [{value.GetType()}] for element type [{elementType}]", (object)kvp, GPALObjectType.Other);
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
                                                if (true == AddToList(objOrList, value, count))
                                                    count++;
                                            }
                                        }
                                        else if (IsDictionaryType(elementType) || IsCustomStructType(elementType) || IsTupleType(elementType) || true == IsStructRecord(elementType, value))
                                        {
                                            value = CreateObjectFromDictionary(nestedDict, elementType, dictionary, columnNames);
                                            if (true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Dictionary value [{value.GetType()}] incompatible with element type [{elementType}]", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    // a list of values, such as the [1, 2] of [[1, 2], [3]], or a value alone, such as the
                                    // text of an xml element, is not a dictionary, so no empty dictionary is made for it
                                    else if (true == IsDictionaryType(elementType))
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{elementType.Name}], so it is not added", (object)objOrList, GPALObjectType.Other);
                                    }
                                    // text is one value of a list item, such as a List<string>, not a list of its characters
                                    else if (IsEnumerableType(elementType) && value is IEnumerable enumValue
                                        && false == (value is string && false == IsDictionaryType(elementType) && typeof(char) != CollectionItemType(elementType)))
                                    {
                                        // a list none of whose entries is added, such as a list of records for an int[], adds
                                        // no item; each entry said why it is not added
                                        value = CreateObjectFromDictionary(enumValue, elementType, dictionary, columnNames);
                                        if (false == NoValueAdded(elementType, (object)value, (object)enumValue) && true == AddToList(objOrList, value, count))
                                            count++;
                                    }
                                    else if (IsSimpleType(elementType) || IsNullableType(elementType))
                                    {
                                        // a value that does not convert is not added, with a warning
                                        if (IsSimpleType(value.GetType()) || IsNullableType(value.GetType()))
                                        {
                                            valueNotConverted = false;
                                            value = ConvertListItem(value, elementType);
                                            if (false == valueNotConverted && true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        // a row of one value of a delimited or xlsx file, such as a line of a csv file of one
                                        // column with no header line, is that value, such as a Point written "3, 4" or a Font
                                        // written "Arial, 9.75pt"
                                        else if (value is IList oneValueRow && 1 == oneValueRow.Count && null != oneValueRow[0] && true == IsSimpleType(oneValueRow[0].GetType())
                                            && 0 < ConverterSettings.InDataFormat.Count
                                            && (true == IsDelimitedDataFormat(ConverterSettings.InDataFormat[0]) || DataFormat.XLSX == ConverterSettings.InDataFormat[0]))
                                        {
                                            valueNotConverted = false;
                                            value = ConvertListItem(oneValueRow[0], elementType);
                                            if (false == valueNotConverted && true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsComplexType(elementType))
                                    {
                                        if (value is string complexStr)
                                        {
                                            value = ParseComplex(complexStr);
                                            if (null == value)
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{complexStr}] is not a [Complex], so it is not added", (object)objOrList, GPALObjectType.Other);
                                            else if (true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        else if (value is Complex complexValue)
                                        {
                                            if (true == AddToList(objOrList, complexValue, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{value.GetType()}] incompatible with complex/nullable element type [{elementType}]", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsBigIntegerType(elementType))
                                    {
                                        if (value is string biNullStr && IsNullableType(elementType) && biNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (true == AddToList(objOrList, null, count))
                                                count++;
                                        }
                                        else
                                        {
                                            try { if (true == AddToList(objOrList, AsBigInteger(value), count)) count++; }
                                            catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other); }
                                        }
                                    }
                                    else if (IsVersionType(elementType))
                                    {
                                        if (value is string versionStr && System.Version.TryParse(versionStr, out System.Version version))
                                        {
                                            if (true == AddToList(objOrList, version, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsIpAddressType(elementType))
                                    {
                                        if (value is string ipStr && IPAddress.TryParse(ipStr, out IPAddress ipAddress))
                                        {
                                            if (true == AddToList(objOrList, ipAddress, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsUriType(elementType))
                                    {
                                        if (value is string uriStr && Uri.TryCreate(uriStr, UriKind.Absolute, out Uri uri))
                                        {
                                            if (true == AddToList(objOrList, uri, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsGuidType(elementType))
                                    {
                                        if (value is string guidStr && IsNullableType(elementType) && guidStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (true == AddToList(objOrList, null, count))
                                                count++;
                                        }
                                        else if (value is string guidStr2 && Guid.TryParse(guidStr2, out Guid guid))
                                        {
                                            if (true == AddToList(objOrList, guid, count))
                                                count++;
                                        }
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other);
                                    }
                                    else if (true == IsTimeSpanType(elementType))
                                    {
                                        if (value is string timeSpanStr && IsNullableType(elementType) && timeSpanStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (true == AddToList(objOrList, null, count))
                                                count++;
                                        }
                                        else if (value is string timeSpanStr2 && TimeSpan.TryParse(timeSpanStr2, out TimeSpan timeSpan))
                                        {
                                            if (true == AddToList(objOrList, timeSpan, count))
                                                count++;
                                        }
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvp, GPALObjectType.Other);
                                    }
                                    else if (IsClassType(elementType) && elementType != typeof(string))
                                    {
                                        // Handle non-dictionary class types (e.g., custom structs or classes)
                                        var converter = TypeDescriptor.GetConverter(elementType);
                                        if (converter != null && converter.CanConvertFrom(value.GetType()))
                                        {
                                            value = converter.ConvertFrom(value);
                                            if (true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        else
                                        {
                                            if (true == AddToList(objOrList, value, count))
                                                count++;
                                            //GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported class type [{value.GetType()}] for element type [{elementType}]", (object)kvp, GPALObjectType.Other);
                                        }
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported value type [{value.GetType()}] for element type [{elementType}]", (object)kvp, GPALObjectType.Other);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add value to enumerable [{targetType}]", (object)kvp, GPALObjectType.Other, ex);
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
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", (object)item, GPALObjectType.Other);
                                }
                                else
                                {
                                    if (true == AddToList(objOrList, null, count))
                                        count++;
                                }
                            }
                            else if (elementType.IsAssignableFrom(value.GetType()))
                            {
                                if (true == AddToList(objOrList, typeof(object) == elementType ? ObjectSlotValue((object)value) : (object)value, count))
                                    count++;
                            }
                            // a value that fills nothing of the item is not added
                            else if (true == FillsNothing((object)value, elementType, columnNames, enumInput))
                            {
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
                                if (true == AddToList(objOrList, value, count))
                                    count++;
                            }
                            else if (IsEnumerableType(elementType) && value is IEnumerable enumValue)
                            {
                                // a list none of whose entries is added, such as a list of records for an int[], adds no
                                // item; each entry said why it is not added
                                value = CreateObjectFromDictionary(enumValue, elementType, enumInput, columnNames);
                                if (false == NoValueAdded(elementType, (object)value, (object)enumValue) && true == AddToList(objOrList, value, count))
                                    count++;
                            }
                            else if (IsComplexType(elementType))
                            {
                                if (value is string complexStr)
                                {
                                    value = ParseComplex(complexStr);
                                    if (null == value)
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{complexStr}] is not a [Complex], so it is not added", (object)objOrList, GPALObjectType.Other);
                                    else if (true == AddToList(objOrList, value, count))
                                        count++;
                                }
                                else if (value is Complex complexValue)
                                {
                                    if (true == AddToList(objOrList, complexValue, count))
                                        count++;
                                }
                                else if (value is IDictionary || (value is IEnumerable && !(value is string)))
                                {
                                    if (true == AddToList(objOrList, ComplexFromDict(value), count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value type [{value.GetType()}] incompatible with complex/nullable element type [{elementType}]", (object)value, GPALObjectType.Other);
                                }
                            }
                            else if (IsBigIntegerType(elementType))
                            {
                                if (value is string biNullStr4 && IsNullableType(elementType) && biNullStr4.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (true == AddToList(objOrList, null, count))
                                        count++;
                                }
                                else
                                {
                                    try { if (true == AddToList(objOrList, AsBigInteger(value), count)) count++; }
                                    catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other); }
                                }
                            }
                            else if (IsVersionType(elementType))
                            {
                                if (value is string versionStr && System.Version.TryParse(versionStr, out System.Version version))
                                {
                                    if (true == AddToList(objOrList, version, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other);
                                }
                            }
                            else if (true == IsIpAddressType(elementType))
                            {
                                if (value is string ipStr && IPAddress.TryParse(ipStr, out IPAddress ipAddress))
                                {
                                    if (true == AddToList(objOrList, ipAddress, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other);
                                }
                            }
                            else if (true == IsUriType(elementType))
                            {
                                if (value is string uriStr && Uri.TryCreate(uriStr, UriKind.Absolute, out Uri uri))
                                {
                                    if (true == AddToList(objOrList, uri, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other);
                                }
                            }
                            else if (true == IsGuidType(elementType))
                            {
                                if (value is string guidStr && IsNullableType(elementType) && guidStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (true == AddToList(objOrList, null, count))
                                        count++;
                                }
                                else if (value is string guidStr2 && Guid.TryParse(guidStr2, out Guid guid))
                                {
                                    if (true == AddToList(objOrList, guid, count))
                                        count++;
                                }
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other);
                            }
                            else if (true == IsTimeSpanType(elementType))
                            {
                                if (value is string timeSpanStr && IsNullableType(elementType) && timeSpanStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (true == AddToList(objOrList, null, count))
                                        count++;
                                }
                                else if (value is string timeSpanStr2 && TimeSpan.TryParse(timeSpanStr2, out TimeSpan timeSpan))
                                {
                                    if (true == AddToList(objOrList, timeSpan, count))
                                        count++;
                                }
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other);
                            }
                            else if (true == IsBooleanType(elementType))
                            {
                                if (value is string boolStr)
                                {
                                    if (IsNullableType(elementType) && boolStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (true == AddToList(objOrList, null, count))
                                            count++;
                                    }
                                    else if (bool.TryParse(boolStr.Trim(), out bool parsedBool))
                                    {
                                        if (true == AddToList(objOrList, parsedBool, count))
                                            count++;
                                    }
                                    else    // if we are mapping from a file to a class, then the file might be trying to map out these values as boolean
                                    {
                                        // Common aliases that bool.TryParse doesn't accept by default
                                        string lower = boolStr.Trim().ToLowerInvariant();
                                        if (lower == "1" || lower == "yes" || lower == "on" || lower == "true")
                                        {
                                            if (true == AddToList(objOrList, true, count))
                                                count++;
                                        }
                                        else if (lower == "0" || lower == "no" || lower == "off" || lower == "false")
                                        {
                                            if (true == AddToList(objOrList, false, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                                $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added",
                                                (object)item, GPALObjectType.Other);
                                        }
                                    }
                                }
                                else if (value is bool directBool)
                                {
                                    if (true == AddToList(objOrList, directBool, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                        $"Invalid type for boolean: expected string or bool, got [{value?.GetType()}]",
                                        (object)item, GPALObjectType.Other);
                                }
                            }
                            else if (IsSimpleType(elementType) || IsNullableType(elementType))
                            {
                                // a value that does not convert is not added, with a warning
                                if (IsSimpleType(value.GetType()) || IsNullableType(value.GetType()))
                                {
                                    valueNotConverted = false;
                                    value = ConvertListItem(value, elementType);
                                    if (false == valueNotConverted && true == AddToList(objOrList, value, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)item, GPALObjectType.Other);
                                }
                            }
                            else if (IsClassType(elementType) && elementType != typeof(string))
                            {
                                var converter = TypeDescriptor.GetConverter(elementType);
                                if (converter != null && converter.CanConvertFrom(value.GetType()))
                                {
                                    value = converter.ConvertFrom(value);
                                    if (true == AddToList(objOrList, value, count))
                                        count++;
                                }
                                else
                                {
                                    try
                                    {
                                        if (true == AddToList(objOrList, value, count))
                                            count++;
                                    }
                                    catch
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported class type [{value.GetType()}] for element type [{elementType}]", (object)item, GPALObjectType.Other);
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
                                    if (true == AddToList(objOrList, value, count))
                                        count++;
                                }
                                else
                                {
                                    var converter = TypeDescriptor.GetConverter(elementType);
                                    if (converter != null && converter.CanConvertFrom(value.GetType()))
                                    {
                                        value = converter.ConvertFrom(value);
                                        if (true == AddToList(objOrList, value, count))
                                            count++;
                                    }
                                    else if (false == IsSimpleType(value.GetType()))
                                    {
                                        try
                                        {
                                            if (true == AddToList(objOrList, value, count))
                                                count++;
                                        }
                                        catch
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported struct type [{value.GetType()}] for element type [{elementType}]", (object)item, GPALObjectType.Other);
                                        }
                                    }
                                }
                            }
                            else if (value is IDictionary nestedDict)
                            {
                                if (IsClassType(elementType) && elementType != typeof(string))
                                {
                                    value = ConvertToClass(nestedDict, elementType, enumInput, columnNames);
                                    if (true == AddToList(objOrList, value, count))
                                        count++;
                                }
                                else if (IsDictionaryType(elementType) || IsTupleType(elementType))
                                {
                                    value = CreateObjectFromDictionary(nestedDict, elementType, enumInput, columnNames);
                                    if (true == AddToList(objOrList, value, count))
                                        count++;
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Dictionary value [{value.GetType()}] incompatible with element type [{elementType}]", (object)item, GPALObjectType.Other);
                                }
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported item type [{value?.GetType()}] for element type [{elementType}]", (object)item, GPALObjectType.Other);
                            }
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to add item to enumerable [{targetType}]", (object)item, GPALObjectType.Other, ex);
                        }
                    }
                }

                // an array was made as long as the items, and holds only the items added
                if ((object)objOrList is Array addedArray && count < addedArray.Length)
                {
                    Array addedItems = Array.CreateInstance(addedArray.GetType().GetElementType(), count);
                    Array.Copy(addedArray, addedItems, count);
                    objOrList = addedItems;
                }
            }
            else if (!(dictionary is IDictionary inputDict))    // if we can't cast to dict for next step, we have something weird...
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Expected IDictionary input for [{targetType}]", (object)dictionary, GPALObjectType.None);
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
                            kvpName = columnNames[columnIdx++];
                        else
                        {
                            string rawKey = kvp.Key?.ToString() ?? string.Empty;
                            if (rawKey.StartsWith("GPALKEY", StringComparison.Ordinal))
                                rawKey = rawKey.Substring(7);
                            else if (System.Text.RegularExpressions.Regex.IsMatch(rawKey, @"^GPAL\d+_"))
                                rawKey = System.Text.RegularExpressions.Regex.Replace(rawKey, @"^GPAL\d+_", "");
                            kvpName = rawKey;
                        }

                        if (typeof(string) == kvp.Value?.GetType())
                            kvpValue = kvp.Value?.ToString();
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
                        // a property typed object takes the value as it was read: a dictionary for a record, a list for a
                        // list, the value itself for a number or text. The text null from xml, html, a delimited file, xlsx
                        // or a grid is a null, as those formats write a null; from json or yaml it is text
                        if (typeof(object) == kvpPropertyType)
                        {
                            DataFormat readFrom = 0 < ConverterSettings.InDataFormat.Count ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
                            object objectValue = kvpValue is string objectNullText && true == objectNullText.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
                                && (DataFormat.XML == readFrom || DataFormat.HTML == readFrom || DataFormat.XLSX == readFrom || DataFormat.GRID == readFrom || true == IsDelimitedDataFormat(readFrom))
                                ? null
                                : CopyOfHeld(ObjectSlotValue((object)kvpValue));
                            if (true == objOrList.GetType().IsValueType)
                            {
                                string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, objectValue });
                            }
                            else
                                kvpProperty.SetValue(objOrList, objectValue);
                        }
                        // an array of two or more dimensions, such as int[,], takes rows of rows
                        else if (true == kvpPropertyType.IsArray && 1 < kvpPropertyType.GetArrayRank())
                        {
                            // an empty value, as xml writes an empty array, is an empty array
                            object multiDimensional = ConvertValue(kvpValue is string noRows && 0 == noRows.Length ? new List<object>() : kvpValue, kvpPropertyType, parentDictionary, kvpName);
                            if (true == objOrList.GetType().IsValueType)
                            {
                                string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, multiDimensional });
                            }
                            else
                                kvpProperty.SetValue(objOrList, multiDimensional);
                        }
                        else if (IsDictionaryType(kvpPropertyType))
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
                                Type keyType = DictionaryTypeArguments(kvpPropertyType)[0];
                                Type valueType = DictionaryTypeArguments(kvpPropertyType)[1];

                                foreach (dynamic valueKvp in valueDict)
                                {
                                    try
                                    {
                                        // a key that does not convert, such as x for an int key, is not added, with a warning
                                        // a key written as a record, as a key of a struct of the program's own or of a class
                                        // is, is made from its members, which report their own values that do not convert; a
                                        // record given to a key read from text is no key
                                        bool recordKey = (object)valueKvp.Key is IDictionary && (false == IsSimpleType(keyType) || true == IsMemberStruct(keyType));
                                        bool wasConverting = convertingListItem;
                                        convertingListItem = true == wasConverting || (false == recordKey && true == IsSimpleType(keyType));
                                        valueNotConverted = false;
                                        object key = null;
                                        bool keyThrew = false;
                                        try
                                        {
                                            key = true == recordKey ? CreateObjectFromDictionary(valueKvp.Key, keyType, valueDict, null)
                                                : true == IsSimpleType(keyType) ? ResolveSimpleDictionaryKey(valueKvp.Key, keyType)
                                                : valueKvp.Key;
                                        }
                                        catch (Exception ex)
                                        {
                                            keyThrew = true;
                                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"The key [{DescribeForLog((object)valueKvp.Key)}] of [{kvpName}] is not a [{ShortTypeName(keyType)}], so its entry is not added", (object)valueKvp, GPALObjectType.Other, ex);
                                        }
                                        finally
                                        {
                                            convertingListItem = wasConverting;
                                        }
                                        bool keyConverts = false == keyThrew && null != key
                                            && (true == recordKey || false == (true == IsSimpleType(keyType) && true == valueNotConverted))
                                            && (true == recordKey || false == (object)valueKvp.Key is IDictionary || typeof(object) == keyType);
                                        if (false == keyConverts && false == keyThrew)
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The key [{DescribeForLog((object)valueKvp.Key)}] of [{kvpName}] is not a [{ShortTypeName(keyType)}], so its entry is not added", (object)valueKvp, GPALObjectType.Other);
                                        bool valueConverts = true;
                                        object value = null;

                                        // a value under a key that does not convert is not converted
                                        if (true == keyConverts && IsKeyValuePairType(valueKvp.Value?.GetType()))
                                        {
                                            var nestedKvp = valueKvp.Value;
                                            valueNotConverted = false;
                                            value = IsDictionaryType(valueType)
                                                ? CreateObjectFromDictionary(new Dictionary<object, object> { { nestedKvp.Key, nestedKvp.Value } }, valueType, valueDict, columnNames)
                                                : ConvertListItem(nestedKvp.Value, valueType, kvpName);
                                            valueConverts = true == IsDictionaryType(valueType) || false == valueNotConverted;
                                        }
                                        // a value that is an array of two or more dimensions, as of a Dictionary<string, int[,]>, takes
                                        // rows of rows, also given as a record, as a class's data and xml give them
                                        else if (true == keyConverts && true == valueType.IsArray && 1 < valueType.GetArrayRank())
                                        {
                                            value = ConvertValue(valueKvp.Value, valueType, valueDict, kvpName);
                                        }
                                        else if (true == keyConverts && IsDictionaryType(valueKvp.Value?.GetType()))
                                        {
                                            // If target is object or already assignable, preserve the dict as-is.
                                            // CreateObjectFromDictionary(dict, object) would try to map keys to
                                            // System.Object properties and fail.
                                            // A list read from xml into a value typed object, its two or more items under one
                                            // element name, is those items, as json and yaml give it
                                            if (typeof(object) == valueType && 0 < ConverterSettings.InDataFormat.Count && DataFormat.XML == ConverterSettings.InDataFormat[0]
                                                && null != ListWrapperItems((object)valueKvp.Value, null))
                                                value = ListWrapperItems((object)valueKvp.Value, null);
                                            else if (valueType == typeof(object) || valueType.IsAssignableFrom(valueKvp.Value.GetType()))
                                                value = typeof(object) == valueType ? ObjectSlotValue((object)valueKvp.Value) : (object)valueKvp.Value;
                                            else
                                                value = CreateObjectFromDictionary(valueKvp.Value, valueType, valueDict, columnNames);
                                        }
                                        else if (true == keyConverts)
                                        {
                                            valueNotConverted = false;
                                            value = ConvertListItem(valueKvp.Value, valueType, kvpName);
                                            valueConverts = false == valueNotConverted;
                                        }

                                        // a key or value that does not convert is not added; its warning is published above
                                        if (true == keyConverts && true == valueConverts && (null == value || valueType.IsAssignableFrom(value.GetType())))
                                            propDict[key] = value;
                                        else if (true == keyConverts && true == valueConverts)
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to convert [{DescribeForLog(value)}] to [{valueType.Name}] for [{kvpName}]", (object)valueKvp, GPALObjectType.None);
                                    }
                                    catch (Exception ex)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to process key-value pair for [{kvpName}]", (object)valueKvp, GPALObjectType.None, ex);
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
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Value for property [{kvpName}] is not a dictionary", (object)kvpValue, GPALObjectType.None);
                            }
                        }
                        // a member that is a class of its own enumerating its items, such as a tree node, given a record
                        // of its members, is filled as a class member is
                        else if (true == FillsMembers(kvpPropertyType, (object)kvpValue))
                        {
                            object memberObject = CreateObjectFromDictionary(kvpValue, kvpPropertyType, parentDictionary, columnNames);
                            if (objOrList.GetType().IsValueType)
                            {
                                string bfName = $"<{kvpProperty.Name}>k__BackingField";
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, bfName, memberObject });
                            }
                            else
                                kvpProperty.SetValue(objOrList, memberObject);
                        }
                        else if (IsEnumerableType(kvpPropertyType) || kvpPropertyType.IsArray)
                        {
                            Type elementType = CollectionItemType(kvpPropertyType);

                            count = 0;

                            // A list the .NET serializers wrote holds its items under one element named for the item
                            // type, as <Members><Member>..</Member></Members> or <Tags><string>t1</string></Tags>. That one
                            // key is not a member of the item, and what it holds, one item or a list of them, is the list.
                            if (kvpValue is IDictionary itemWrapper && 1 == itemWrapper.Count && typeof(object) != elementType
                                && false == IsDictionaryType(elementType) && false == IsKeyValuePairType(elementType))
                            {
                                foreach (DictionaryEntry wrapperEntry in itemWrapper)
                                {
                                    string wrapperKey = wrapperEntry.Key?.ToString() ?? string.Empty;
                                    if (false == Regex.IsMatch(wrapperKey, @"^(GPALKEY)?\d+$") && false == HasMember(elementType, wrapperKey))
                                        kvpValue = wrapperEntry.Value is IList ? wrapperEntry.Value : new List<object> { wrapperEntry.Value };
                                }
                            }

                            // an empty value, as xml writes an empty list, is an empty list; xml writes each item, an empty text too, as an
                            // element of its own
                            if (kvpValue is string noItems && 0 == noItems.Length)
                                kvpValue = new List<object>();

                            // a record given for a list of pairs, such as {"a": 1, "b": 2}, is its pairs, as it is for a dictionary
                            List<object> memberPairs = true == IsKeyValuePairType(elementType) ? PairRecords((object)kvpValue, elementType) : null;
                            if (null != memberPairs)
                                kvpValue = memberPairs;

                            if (kvpValue is string kvpNullCollStr && kvpNullCollStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                            {
                                // "null" string from XML means the collection itself is null — leave property at default.
                            }
                            // a record of the members of a struct of the framework, such as the X and Y of a System.Drawing.Point,
                            // is one item, as a record of a class is
                            else if (kvpValue is IDictionary collapsedDict && !IsDictionaryType(elementType) && (IsClassType(elementType) || IsCustomStructType(elementType) || IsTupleType(elementType)
                                || true == IsStructRecord(elementType, (object)kvpValue)))
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
                                        // an element that does not convert is not added
                                        valueNotConverted = false;
                                        bool indexedRecord = collapsedDict[indexedKey] is IDictionary;
                                        object indexedElement = true == indexedRecord
                                            ? CreateObjectFromDictionary(collapsedDict[indexedKey], elementType, parentDictionary, columnNames)
                                            : ConvertListItem(collapsedDict[indexedKey], elementType, kvpName);
                                        if ((true == indexedRecord || false == valueNotConverted) && true == AddToList(innerList, indexedElement, indexedIdx))
                                            indexedIdx++;
                                    }

                                    // an array was made as long as the items, and holds only the items added
                                    if (innerList is Array indexedArray && indexedIdx < indexedArray.Length)
                                    {
                                        Array addedIndexed = Array.CreateInstance(indexedArray.GetType().GetElementType(), indexedIdx);
                                        Array.Copy(indexedArray, addedIndexed, indexedIdx);
                                        innerList = addedIndexed;
                                    }
                                }
                                // a record that fills nothing of the item is not added
                                else if (false == FillsNothing((object)collapsedDict, elementType, columnNames, (object)parentDictionary))
                                {
                                    object element = CreateObjectFromDictionary(collapsedDict, elementType, parentDictionary, columnNames);
                                    AddToList(innerList, element, 0);
                                }
                                if (0 < ObjectCopier.GetItemCount(null, innerList))
                                {
                                    if ((IsEnumerableObject(objOrList) && false == fillsMembers) || objOrList.GetType().IsArray)
                                    {
                                        if (true == AddToList(objOrList, innerList, count))
                                            count++;
                                    }
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

                                // A collection that cannot be added to, such as ReadOnlyCollection<Item>, takes its items
                                // in a List and is made from them when they are all read
                                ConstructorInfo makeWith = ReadOnlyItemsConstructor(kvpPropertyType);
                                // An immutable collection, such as ImmutableList<Item>, has no constructor to make it
                                // empty, and Add gives a new one; its items go in a List and it is made from its Empty
                                // through AddRange when they are all read
                                object immutableEmpty = kvpPropertyType.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                                    ?? kvpPropertyType.GetField("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                                MethodInfo addRange = null != immutableEmpty ? kvpPropertyType.GetMethod("AddRange", new[] { typeof(IEnumerable<>).MakeGenericType(elementType) }) : null;
                                if (null != addRange && kvpPropertyType != addRange.ReturnType)
                                    addRange = null;
                                object innerList = null != makeWith || null != addRange
                                    ? ConverterHelper.InstantiateOne(typeof(List<>).MakeGenericType(elementType))
                                    : ConverterHelper.InstantiateOne(kvpPropertyType, ObjectCopier.GetItemCount(kvpProperty, kvpValue));
                                foreach (dynamic kvpValueItem in (IEnumerable)kvpValue)
                                {
                                    // compared as object: a null item has no type, and a dynamic struct has no == with null
                                    Type kvpValueItemType = ((object)kvpValueItem)?.GetType();
                                    object element = null;

                                    if (null == (object)kvpValueItem)
                                    {
                                        if (elementType.IsValueType && !IsNullableType(elementType))
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                        else
                                        {
                                            if (true == AddToList(innerList, null, count))
                                                count++;
                                        }
                                    }
                                    // an item typed object is null from the text null of xml, html, a delimited file, xlsx or a grid,
                                    // as those formats write a null
                                    else if (typeof(object) == elementType && true == IsNullText((object)kvpValueItem))
                                    {
                                        if (true == AddToList(innerList, null, count))
                                            count++;
                                    }
                                    else if (elementType.IsAssignableFrom(kvpValueItem.GetType()))
                                    {
                                        if (true == AddToList(innerList, typeof(object) == elementType ? ObjectSlotValue((object)kvpValueItem) : (object)kvpValueItem, count))
                                            count++;
                                    }
                                    // an item that is an array of two or more dimensions, as of a List<int[,]>, takes rows of rows
                                    else if (true == elementType.IsArray && 1 < elementType.GetArrayRank())
                                    {
                                        if (true == AddToList(innerList, ConvertValue(kvpValueItem, elementType, parentDictionary, kvpName), count))
                                            count++;
                                    }
                                    else if (kvpValueItem is IDictionary nestedDict)
                                    {
                                        // a record that fills nothing of the item is not added
                                        if (true == FillsNothing(nestedDict, elementType, columnNames, (object)parentDictionary))
                                        {
                                        }
                                        else if ((true == IsCustomStructType(elementType) || true == IsClassType(elementType)) && typeof(string) != elementType)
                                        {
                                            // a list item, such as an int[], none of whose values the record gives adds no item;
                                            // each value said why it is not added
                                            element = ConvertToClass(nestedDict, elementType, parentDictionary, columnNames);
                                            if (false == NoValueAdded(elementType, element, (object)nestedDict) && true == AddToList(innerList, element, count))
                                                count++;
                                        }
                                        else if (IsDictionaryType(kvpValueItem.GetType()) || IsDictionaryType(elementType) || true == IsEnumerableType(elementType))
                                        {
                                            // one none of whose entries is added, such as a record for an int[], adds no item;
                                            // each entry said why it is not added
                                            element = CreateObjectFromDictionary(nestedDict, elementType, parentDictionary, columnNames);
                                            if (false == NoValueAdded(elementType, element, (object)nestedDict) && true == AddToList(innerList, element, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Dictionary value [{kvpValueItem.GetType()}] incompatible with element type [{elementType}]", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsKeyValuePairType(kvpValueItemType))
                                    {
                                        var nestedKvp = kvpValueItem;
                                        Type valueType = elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                                            ? elementType.GetGenericArguments()[1]
                                            : elementType;
                                        // a value that does not convert is not added
                                        bool takeElement = true;

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
                                            // a value that does not convert is not added, with a warning
                                            valueNotConverted = false;
                                            // the value as the converter writes it, the same on every culture; a character as itself, so a NUL stays one
                                            element = ConvertListItem(null == (object)nestedKvp.Value ? null : (nestedKvp.Value is char ? nestedKvp.Value.ToString() : ValueToString((object)nestedKvp.Value)), valueType, kvpName);
                                            takeElement = false == valueNotConverted;
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
                                        if (true == takeElement && true == AddToList(innerList, element, count))
                                            count++;
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
                                                if (true == AddToList(innerList, element, count))
                                                    count++;
                                            }
                                        }
                                    }
                                    else if (IsComplexType(elementType) || (true == IsComplexType(innerElementType) && typeof(string) == kvpValueItemType))
                                    {
                                        if (kvpValueItem is string complexStr)
                                        {
                                            element = ParseComplex(complexStr);
                                            if (null == element)
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{complexStr}] is not a [Complex], so it is not added", (object)objOrList, GPALObjectType.Other);
                                            else if (true == AddToList(innerList, element, count))
                                                count++;
                                        }
                                        else if (kvpValueItem is Complex complexValue)
                                        {
                                            if (true == AddToList(innerList, complexValue, count))
                                                count++;
                                        }
                                        else if (true == IsDictionaryType(kvpValueItemType) || true == IsEnumerableType(kvpValueItemType))  // json and yaml will collapse arrays of 1 into an object
                                        {
                                            // yaml and json have a nasty habit of collapsing arrays with one elemnt to a singular type, messes us up when we expect arrays not a singular value
                                            element = CreateObjectFromDictionary(kvpValueItem, elementType, kvpValue, columnNames);
                                            if (true == AddToList(innerList, element, count))
                                                count++;

                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (((IsDictionaryType(kvpValueItemType) || IsEnumerableType(elementType) || elementType.IsArray)) && typeof(string) != kvpValueItemType)
                                    {
                                        // a value that does not convert is not added, with a warning
                                        bool takeElement = true;
                                        if (IsEnumerableType(elementType) && !(kvpValueItem is IEnumerable))
                                        {
                                            // JSON collapses a single-item List<T> to a bare scalar; wrap it, in an array of one for an
                                            // array item such as the int[] of an int[][]
                                            Type innerScalarType = CollectionItemType(elementType);
                                            dynamic singleItemList = InstantiateOne(elementType, 1);
                                            valueNotConverted = false;
                                            object scalarItem = ConvertListItem(kvpValueItem, innerScalarType, kvpName);
                                            takeElement = false == valueNotConverted;
                                            AddToList(singleItemList, scalarItem, 0);
                                            element = singleItemList;
                                        }
                                        else
                                        {
                                            // a list none of whose entries is added, such as ["x"] for an int[], adds no item;
                                            // each entry said why it is not added
                                            element = CreateObjectFromDictionary(kvpValueItem, elementType, parentDictionary, columnNames);
                                            takeElement = false == NoValueAdded(elementType, element, (object)kvpValueItem);
                                        }
                                        if (true == takeElement && true == AddToList(innerList, element, count))
                                            count++;
                                    }
                                    else if (IsBigIntegerType(elementType) || IsBigIntegerType(innerElementType))
                                    {
                                        Type biNullableCheck2 = IsBigIntegerType(innerElementType) ? innerElementType : elementType;
                                        if (kvpValueItem is string biNullStr3 && IsNullableType(biNullableCheck2) && biNullStr3.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (true == AddToList(innerList, null, count))
                                                count++;
                                        }
                                        else
                                        {
                                            try { if (true == AddToList(innerList, AsBigInteger(kvpValueItem), count)) count++; }
                                            catch { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other); }
                                        }
                                    }
                                    else if (IsVersionType(elementType) || IsVersionType(innerElementType))
                                    {
                                        if (kvpValueItem is string versionStr && System.Version.TryParse(versionStr, out System.Version version))
                                        {
                                            if (true == AddToList(innerList, version, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsIpAddressType(elementType) || true == IsIpAddressType(innerElementType))
                                    {
                                        if (kvpValueItem is string ipStr && IPAddress.TryParse(ipStr, out IPAddress ipAddress))
                                        {
                                            if (true == AddToList(innerList, ipAddress, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsUriType(elementType) || true == IsUriType(innerElementType) )
                                    {
                                        if (kvpValueItem is string uriStr && Uri.TryCreate(uriStr, UriKind.Absolute, out Uri uri))
                                        {
                                            if (true == AddToList(innerList, uri, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (true == IsGuidType(elementType) || true == IsGuidType(innerElementType))
                                    {
                                        Type gNullableCheck = IsGuidType(innerElementType) ? innerElementType : elementType;
                                        if (kvpValueItem is string guidNullStr && IsNullableType(gNullableCheck) && guidNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (true == AddToList(innerList, null, count))
                                                count++;
                                        }
                                        else if (kvpValueItem is string guidStr && Guid.TryParse(guidStr, out Guid guid))
                                        {
                                            if (true == AddToList(innerList, guid, count))
                                                count++;
                                        }
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
                                    }
                                    else if (true == IsTimeSpanType(elementType) || true == IsTimeSpanType(innerElementType))
                                    {
                                        Type tsNullableCheck = IsTimeSpanType(innerElementType) ? innerElementType : elementType;
                                        if (kvpValueItem is string tsNullStr && IsNullableType(tsNullableCheck) && tsNullStr.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                                        {
                                            if (true == AddToList(innerList, null, count))
                                                count++;
                                        }
                                        else if (kvpValueItem is string timeSpanStr && TimeSpan.TryParse(timeSpanStr, out TimeSpan timeSpan))
                                        {
                                            if (true == AddToList(innerList, timeSpan, count))
                                                count++;
                                        }
                                        else
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
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
                                            if (true == AddToList(innerList, element, count))
                                                count++;
                                        }
                                        else
                                        {
                                            var converter = TypeDescriptor.GetConverter(csElemType);
                                            if (converter != null && converter.CanConvertFrom(csItem.GetType()))
                                            {
                                                element = converter.ConvertFrom(csItem);
                                                if (true == AddToList(innerList, element, count))
                                                    count++;
                                            }
                                            else if (false == IsSimpleType(csItem.GetType()))
                                            {
                                                try
                                                {
                                                    if (true == AddToList(innerList, csItem, count))
                                                        count++;
                                                }
                                                catch
                                                {
                                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported struct type [{csItem.GetType()}] for element type [{elementType}]", (object)kvpValueItem, GPALObjectType.Other);
                                                }
                                            }
                                        }
                                    }
                                    else if (IsSimpleType(elementType) || IsNullableType(elementType))
                                    {
                                        if (IsSimpleType(kvpValueItem.GetType()) || IsNullableType(kvpValueItem.GetType()))
                                        {
                                            // a value that does not convert is not added, with a warning
                                            valueNotConverted = false;
                                            element = ConvertListItem(kvpValueItem, elementType, kvpName);
                                            if (false == valueNotConverted && true == AddToList(innerList, element, count))
                                                count++;
                                        }
                                        else
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)kvpValueItem)}] from [{kvpName}] is not a [{ShortTypeName(elementType)}], so it is not added", (object)kvpValueItem, GPALObjectType.Other);
                                        }
                                    }
                                    else if (IsClassType(elementType) && elementType != typeof(string))
                                    {
                                        var converter = TypeDescriptor.GetConverter(elementType);
                                        if (converter != null && converter.CanConvertFrom(kvpValueItem.GetType()))
                                        {
                                            element = converter.ConvertFrom(kvpValueItem);
                                            if (true == AddToList(innerList, element, count))
                                                count++;
                                        }
                                        else
                                        {
                                            try
                                            {
                                                if (true == AddToList(innerList, kvpValueItem, count))
                                                    count++;
                                            }
                                            catch
                                            {
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported class type [{kvpValueItem.GetType()}] for element type [{elementType}]", (object)kvpValueItem, GPALObjectType.Other);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported item type [{kvpValueItem?.GetType()}] for element type [{elementType}]", (object)kvpValueItem, GPALObjectType.Other);
                                    }
                                }

                                // an array was made as long as the items, and holds only the items added
                                if (innerList is Array memberArray && count < memberArray.Length)
                                {
                                    Array addedMembers = Array.CreateInstance(memberArray.GetType().GetElementType(), count);
                                    Array.Copy(memberArray, addedMembers, count);
                                    innerList = addedMembers;
                                }

                                if (null != makeWith)
                                    innerList = MakeFromItems(makeWith, innerList);
                                else if (null != addRange)
                                    innerList = addRange.Invoke(immutableEmpty, new[] { innerList });

                                // A stack lists its top first, and pushing the items in order put the last on top. It is
                                // pushed again from the bottom up, so the first item is on top, as a stack saved to is
                                MethodInfo pushItem = null != innerList ? CollectionAddMethod(innerList.GetType()) : null;
                                if (null != pushItem && "Push" == pushItem.Name)
                                {
                                    List<object> pushed = ((IEnumerable)innerList).Cast<object>().ToList();
                                    innerList.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(innerList, null);
                                    foreach (object pushedItem in pushed)
                                        pushItem.Invoke(innerList, new[] { pushedItem });
                                }

                                // an empty list read gives the property an empty collection
                                if (0 < ObjectCopier.GetItemCount(null, innerList) || 0 == ObjectCopier.GetItemCount(null, (object)kvpValue))
                                {
                                    if ((IsEnumerableObject(objOrList) && false == fillsMembers) || objOrList.GetType().IsArray)
                                    {
                                        if (true == AddToList(objOrList, innerList, count))
                                            count++;
                                    }
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
                                    // a read-only collection with no constructor that takes its items, such as a class
                                    // derived from ReadOnlyCollection<Item> with only a parameterless one, cannot be given them
                                    bool cannotTakeItems = null == makeWith && null == addRange && null != innerList && null != ReadOnlyItemType(innerList);
                                    string currentErrorMessage = true == cannotTakeItems
                                        ? $"[{kvpPropertyType.Name}] is read-only and has no constructor that takes its items, so [{kvpProperty?.Name}] is left as it was"
                                        : $"No supplied values for [{kvpProperty?.Name}]";
                                    if (false == lastErrorMessage.Contains(currentErrorMessage))
                                    {
                                        lastErrorMessage.Add(currentErrorMessage);
                                        GPAL.PublishSimpleEvent(true == cannotTakeItems ? GPALEventType.WARNING : GPALEventType.ERROR, currentErrorMessage, (object)objOrList, GPALObjectType.Other);
                                        supressedMessage = false;
                                    }
                                    else if (false == supressedMessage)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.INFO, "Suppressing repeat messages", (object)objOrList, GPALObjectType.Other);
                                        supressedMessage = true;
                                    }
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
                        else if (IsCustomStructType(kvpPropertyType) || (IsMemberStruct(kvpPropertyType) && false == IsKeyValuePairType(kvpPropertyType)) || true == IsStructRecord(kvpPropertyType, kvpValue))
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
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{targetType.Name}][{kvpName}] 'CanWrite' [false]", (object)objOrList, GPALObjectType.Other);
                    }
                    else if (kvpField != null && kvpField.Name.Equals(kvpName, StringComparison.OrdinalIgnoreCase))
                    {
                        // A dictionary field takes the entries as a dictionary property does; xml gives them in a list of one
                        IDictionary fieldEntries = kvpValue is IList fieldWrap && 1 == fieldWrap.Count && fieldWrap[0] is IDictionary wrappedEntries
                            ? wrappedEntries
                            : kvpValue as IDictionary;

                        // a field typed object takes the value as it was read, as a property typed object does, the text null
                        // from xml, html, a delimited file, xlsx or a grid as a null
                        if (typeof(object) == kvpFieldType)
                        {
                            DataFormat readFrom = 0 < ConverterSettings.InDataFormat.Count ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
                            object objectValue = kvpValue is string objectNullText && true == objectNullText.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
                                && (DataFormat.XML == readFrom || DataFormat.HTML == readFrom || DataFormat.XLSX == readFrom || DataFormat.GRID == readFrom || true == IsDelimitedDataFormat(readFrom))
                                ? null
                                : CopyOfHeld(ObjectSlotValue((object)kvpValue));
                            if (true == objOrList.GetType().IsValueType)
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, kvpField.Name, objectValue });
                            else
                                kvpField.SetValue(objOrList, objectValue);
                        }
                        // an array of two or more dimensions, such as int[,], takes rows of rows
                        else if (true == kvpFieldType.IsArray && 1 < kvpFieldType.GetArrayRank())
                        {
                            // an empty value, as xml writes an empty array, is an empty array
                            object multiDimensional = ConvertValue(kvpValue is string noRows && 0 == noRows.Length ? new List<object>() : kvpValue, kvpFieldType, parentDictionary, kvpName);
                            if (true == objOrList.GetType().IsValueType)
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, kvpField.Name, multiDimensional });
                            else
                                kvpField.SetValue(objOrList, multiDimensional);
                        }
                        else if (true == IsDictionaryType(kvpFieldType) && null != fieldEntries)
                        {
                            object fieldDict = CreateObjectFromDictionary(fieldEntries, kvpFieldType, parentDictionary, columnNames);

                            if (objOrList.GetType().IsValueType)
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, kvpField.Name, fieldDict });
                            else
                                kvpField.SetValue(objOrList, fieldDict);
                        }
                        else if (IsEnumerableType(kvpFieldType) || kvpFieldType.IsArray)
                        {
                            Type elementType = CollectionItemType(kvpFieldType);

                            count = 0;
                            // an empty value, as xml writes an empty list, is an empty list; xml writes each item, an empty text too, as an
                            // element of its own
                            if (kvpValue is string noItems && 0 == noItems.Length)
                                kvpValue = new List<object>();

                            // a record given for a list of pairs, such as {"a": 1, "b": 2}, is its pairs, as it is for a dictionary;
                            // a record given for a list of records, objects or lists, as json and yaml give a list of one, is one
                            // item, as it is for a property, as is a record of a struct of the framework, such as the X and Y of a
                            // System.Drawing.Point. A record that fills nothing of the item leaves the field as it was
                            List<object> fieldPairs = true == IsKeyValuePairType(elementType) ? PairRecords((object)kvpValue, elementType) : null;
                            if (null != fieldPairs)
                                kvpValue = fieldPairs;
                            else if (kvpValue is IDictionary fieldRecord && 0 < fieldRecord.Count && false == IsDictionaryType(elementType) && false == IsKeyValuePairType(elementType)
                                && false == fieldRecord.Keys.Cast<object>().All(recordKey => Regex.IsMatch(recordKey?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                                && typeof(string) != elementType && (true == IsClassType(elementType) || true == IsCustomStructType(elementType) || true == IsTupleType(elementType) || true == IsStructRecord(elementType, (object)kvpValue)))
                                kvpValue = true == FillsNothing((object)kvpValue, elementType, columnNames, (object)parentDictionary) ? null : new List<object> { kvpValue };

                            if (IsEnumerableObject(kvpValue) || IsArray(kvpValue))
                            {
                                // A collection that cannot be added to, such as ReadOnlyCollection<Item>, takes its items
                                // in a List and is made from them when they are all read
                                ConstructorInfo makeWith = ReadOnlyItemsConstructor(kvpFieldType);
                                // An immutable collection, such as ImmutableList<Item>, has no constructor to make it
                                // empty, and Add gives a new one; its items go in a List and it is made from its Empty
                                // through AddRange when they are all read
                                object immutableEmpty = kvpFieldType.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                                    ?? kvpFieldType.GetField("Empty", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                                MethodInfo addRange = null != immutableEmpty ? kvpFieldType.GetMethod("AddRange", new[] { typeof(IEnumerable<>).MakeGenericType(elementType) }) : null;
                                if (null != addRange && kvpFieldType != addRange.ReturnType)
                                    addRange = null;
                                object innerList = null != makeWith || null != addRange
                                    ? ConverterHelper.InstantiateOne(typeof(List<>).MakeGenericType(elementType))
                                    : ConverterHelper.InstantiateOne(kvpFieldType, ObjectCopier.GetItemCount(kvpProperty, kvpValue));
                                foreach (dynamic kvpValueItem in (IEnumerable)kvpValue)
                                {
                                    // compared as object: a null item has no type, and a dynamic struct has no == with null
                                    Type kvpValueItemType = ((object)kvpValueItem)?.GetType();
                                    object element = null;

                                    if (null == (object)kvpValueItem)
                                    {
                                        if (true == elementType.IsValueType && false == IsNullableType(elementType))
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Null value not allowed for non-nullable type [{elementType}]", (object)kvpValueItem, GPALObjectType.Other);
                                        else if (true == AddToList(innerList, null, count))
                                            count++;
                                    }
                                    // an item typed object keeps what it was given, a record as a dictionary, a list as a list and a
                                    // value as itself; the text null of xml, html, a delimited file, xlsx or a grid is a null. An entry
                                    // of a record keyed by position gives its value
                                    else if (typeof(object) == elementType)
                                    {
                                        object objectItem = true == IsKeyValuePairType(kvpValueItemType) ? (object)kvpValueItem.Value : (object)kvpValueItem;
                                        if (true == AddToList(innerList, true == IsNullText(objectItem) ? null : ObjectSlotValue(objectItem), count))
                                            count++;
                                    }
                                    // an item that is an array of two or more dimensions, as of a List<int[,]>, takes rows of rows
                                    else if (true == elementType.IsArray && 1 < elementType.GetArrayRank())
                                    {
                                        if (true == AddToList(innerList, ConvertValue(kvpValueItem, elementType, kvpValue, kvpName), count))
                                            count++;
                                    }
                                    else if (IsKeyValuePairType(kvpValueItemType))
                                    {
                                        var nestedKvp = kvpValueItem;
                                        Type valueType = elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                                            ? elementType.GetGenericArguments()[1]
                                            : elementType;
                                        // a value that does not convert is not added
                                        bool takeElement = true;

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
                                            // a value that does not convert is not added, with a warning
                                            valueNotConverted = false;
                                            // the value as the converter writes it, the same on every culture; a character as itself, so a NUL stays one
                                            element = ConvertListItem(null == (object)nestedKvp.Value ? null : (nestedKvp.Value is char ? nestedKvp.Value.ToString() : ValueToString((object)nestedKvp.Value)), valueType);
                                            takeElement = false == valueNotConverted;
                                        }
                                        else
                                        {
                                            element = nestedKvp.Value; // Use Value directly
                                        }
                                        if (true == takeElement && true == AddToList(innerList, element, count))
                                            count++;
                                    }
                                    // a record that fills nothing of the item is not added
                                    else if (true == FillsNothing((object)kvpValueItem, elementType, columnNames, (object)kvpValue))
                                    {
                                    }
                                    else if (IsDictionaryType(kvpValueItemType) || IsEnumerableType(elementType) || elementType.IsArray)
                                    {
                                        // a list none of whose entries is added, such as ["x"] for an int[], adds no item; each
                                        // entry said why it is not added
                                        element = CreateObjectFromDictionary(kvpValueItem, elementType, kvpValue, columnNames);
                                        if (false == NoValueAdded(elementType, element, (object)kvpValueItem) && true == AddToList(innerList, element, count))
                                            count++;
                                    }
                                    else if (IsComplexType(elementType))
                                    {
                                        element = ParseComplex(kvpValueItem.ToString());
                                        if (null == element)
                                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{kvpValueItem}] is not a [Complex], so it is not added", (object)objOrList, GPALObjectType.Other);
                                        else if (true == AddToList(innerList, element, count))
                                            count++;
                                    }
                                    else if (IsSimpleType(elementType))
                                    {
                                        // a value that does not convert is not added, with a warning
                                        valueNotConverted = false;
                                        // the value as the converter writes it, the same on every culture; a character as itself, so a NUL stays one
                                        element = ConvertListItem(kvpValueItem is char ? kvpValueItem.ToString() : ValueToString((object)kvpValueItem), elementType);
                                        if (false == valueNotConverted && true == AddToList(innerList, Convert.ChangeType(element, elementType), count))
                                            count++;
                                    }
                                    else
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Skipping unexpected item type [{kvpValueItemType.Name}] for [{elementType.Name}]", (object)kvpValueItem, GPALObjectType.None);
                                    }
                                }

                                // an array was made as long as the items, and holds only the items added
                                if (innerList is Array fieldArray && count < fieldArray.Length)
                                {
                                    Array addedFieldItems = Array.CreateInstance(fieldArray.GetType().GetElementType(), count);
                                    Array.Copy(fieldArray, addedFieldItems, count);
                                    innerList = addedFieldItems;
                                }

                                if (null != makeWith)
                                    innerList = MakeFromItems(makeWith, innerList);
                                else if (null != addRange)
                                    innerList = addRange.Invoke(immutableEmpty, new[] { innerList });

                                // A stack lists its top first, and pushing the items in order put the last on top. It is
                                // pushed again from the bottom up, so the first item is on top, as a stack saved to is
                                MethodInfo pushItem = null != innerList ? CollectionAddMethod(innerList.GetType()) : null;
                                if (null != pushItem && "Push" == pushItem.Name)
                                {
                                    List<object> pushed = ((IEnumerable)innerList).Cast<object>().ToList();
                                    innerList.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(innerList, null);
                                    foreach (object pushedItem in pushed)
                                        pushItem.Invoke(innerList, new[] { pushedItem });
                                }

                                // an empty list read gives the field an empty collection
                                if (0 < ObjectCopier.GetItemCount(null, innerList) || 0 == ObjectCopier.GetItemCount(null, (object)kvpValue))
                                {
                                    if ((IsEnumerableObject(objOrList) && false == fillsMembers) || objOrList.GetType().IsArray)
                                    {
                                        if (true == AddToList(objOrList, innerList, count))
                                            count++;
                                    }
                                    else if (objOrList.GetType().IsValueType)
                                        objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, kvpField.Name, (object)innerList });
                                    else
                                        kvpField.SetValue(objOrList, innerList);
                                }
                                // a read-only collection with no constructor that takes its items cannot be given them
                                else if (null == makeWith && null == addRange && null != innerList && null != ReadOnlyItemType(innerList))
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{kvpFieldType.Name}] is read-only and has no constructor that takes its items, so [{kvpField.Name}] is left as it was", (object)objOrList, GPALObjectType.Other);
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No values for [{kvpField.Name}]", (object)objOrList, GPALObjectType.Other);
                                }
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
                        else if ((IsCustomStructType(kvpFieldType) || (IsMemberStruct(kvpFieldType) && false == IsKeyValuePairType(kvpFieldType)) || true == IsStructRecord(kvpFieldType, kvpValue)) && kvpValue is IDictionary)
                        {
                            object structObj = CreateObjectFromDictionary(kvpValue, kvpFieldType, parentDictionary, columnNames);

                            // a struct holding the field is a copy here, so the field is set on it and the struct kept
                            if (objOrList.GetType().IsValueType)
                                objOrList = GetSetStructMethod(objOrList.GetType()).Invoke(null, new object[] { (object)objOrList, kvpField.Name, structObj });
                            else
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
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, currentErrorMessage, (object)objOrList, GPALObjectType.Other);
                            supressedMessage = false;
                        }
                        else if (false == supressedMessage)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Suppressing repeat messages", (object)objOrList, GPALObjectType.Other);
                            supressedMessage = true;
                        }
                    }
                }
            }

            if (null != pairType)
                objOrList = Activator.CreateInstance(pairType, targetType.GetProperty("Key").GetValue(objOrList), targetType.GetProperty("Value").GetValue(objOrList));

            if (addedToVisited) _dictVisited.Remove((object)dictionary);
            if (isRoot) _dictVisited = null;
            return objOrList;
        }
        // Takes a KeyValuePair's Key and Value while it is converted, as the pair itself cannot be changed once made
        internal sealed class PairHolder<TKey, TValue>
        {
            public TKey Key { get; set; }
            public TValue Value { get; set; }
        }
        // A dictionary given where pairs are wanted is its pairs: each entry becomes a record of its Key and Value. The
        // dictionary may come from any source, such as a YAML mapping or a json object. Null for anything else: a
        // record of a Key and a Value alone, rows keyed GPALKEY, or rows that are each a record of the pair wanted.
        private static List<object> PairRecords(object data, Type pairType)
        {
            List<object> records = null;

            if (data is IDictionary entries && 0 < entries.Count && false == IsPairRecord(entries)
                && false == entries.Keys.Cast<object>().All(key => key is string rowKey && rowKey.StartsWith("GPALKEY", StringComparison.Ordinal))
                && false == entries.Values.Cast<object>().All(value => true == IsRecordOfPair(value, pairType)))
            {
                records = new List<object>();
                foreach (DictionaryEntry entry in entries)
                    records.Add(new Dictionary<object, dynamic> { ["Key"] = entry.Key, ["Value"] = entry.Value });
            }

            return records;
        }
        // A record of a Key and a Value alone, in any case, as a pair is written
        private static bool IsPairRecord(IDictionary record)
        {
            return 2 == record.Count
                && record.Keys.Cast<object>().Any(key => string.Equals(key?.ToString(), "Key", StringComparison.OrdinalIgnoreCase))
                && record.Keys.Cast<object>().Any(key => string.Equals(key?.ToString(), "Value", StringComparison.OrdinalIgnoreCase));
        }
        // The one value a type that is a value alone, such as a number, text, a date or an enum, is filled from: the value
        // under keys that are not its members, as a json file of one number is read under "root", a text file's line
        // under its row key and an xml element's text under "#text", or "value" once its keys are cleaned, beside its
        // attributes, and a pair record's under its Value. Rows or a list give the first, with a warning. A type written
        // as the text of its converter, such as a System.Drawing.Point or a Font, takes its one value the same way, but
        // not from a record of other names.
        // The value is the one item of the array returned; null when the type has members to fill or the data holds
        // no one value.
        private static object[] HeldValue(object data, Type type)
        {
            object[] value = null;
            Type valueType = Nullable.GetUnderlyingType(type) ?? type;
            object held = data;
            bool textType = true == IsTextStructType(valueType) || true == IsTextClassType(valueType);
            bool walking = true == IsValueAlone(valueType) || true == textType;
            int firstOf = 1;

            while (true == walking)
            {
                int count = 1;
                walking = false;

                if (held is IDictionary entries && 0 < entries.Count)
                {
                    List<object> keys = entries.Keys.Cast<object>().ToList();

                    object textKey = keys.FirstOrDefault(key => "#text" == key?.ToString() || "value" == key?.ToString());

                    if (1 < keys.Count && null != textKey)
                    {
                        held = entries[textKey];
                        walking = true;
                    }
                    // a record of a Key and a Value, as a pair is written, gives its Value
                    else if (true == IsPairRecord(entries))
                    {
                        held = entries[keys.First(key => string.Equals(key?.ToString(), "Value", StringComparison.OrdinalIgnoreCase))];
                        walking = true;
                    }
                    else if (1 == keys.Count && false == HasMember(valueType, keys[0]?.ToString() ?? string.Empty))
                    {
                        held = entries[keys[0]];
                        walking = true;
                    }
                    else if (true == keys.All(key => Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")))
                    {
                        held = entries[keys.OrderBy(key => key.ToString(), StringComparer.Ordinal).First()];
                        count = keys.Count;
                        walking = true;
                    }
                    // a record of several values under names that are not its members, such as {"a": 5, "b": 6}, gives
                    // the first
                    else if (false == textType && true == keys.All(key => false == HasMember(valueType, key?.ToString() ?? string.Empty)))
                    {
                        held = entries[keys[0]];
                        count = keys.Count;
                        walking = true;
                    }
                }
                else if (held is IList items && 0 < items.Count)
                {
                    held = items[0];
                    count = items.Count;
                    walking = true;
                }
                else if (false == held is IDictionary && false == held is IList)
                    value = new[] { held };

                // the outermost count is the one the caller gave, rows of a file rather than the values in a row
                if (1 < count && 1 == firstOf)
                    firstOf = count;
            }

            // warned once, and only when a value was taken
            if (null != value && 1 < firstOf)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{valueType.Name}] holds one value, so it is filled from the first of [{firstOf}]; save to a list to take them all", data, GPALObjectType.Other);

            return value;
        }
        // The records a record holds for a class or struct with members, when none of the record's keys names one of
        // its members and a value of the record is a list holding a record a key of which names one: each item of each
        // list that holds a record, in order, and each record under a key, as each is one item. A value under any other
        // key is left out with a warning. Null for data that is not such a record, and for an item type that is a value
        // alone, text, a collection, a pair or read from text by its own converter.
        private static List<object> HeldRecords(object data, Type itemType, List<string> columnNames, object parentDictionary)
        {
            List<object> records = null;
            bool recordItem = null != itemType && false == IsValueAlone(itemType) && typeof(object) != itemType && typeof(string) != itemType
                && false == IsEnumerableType(itemType) && false == IsKeyValuePairType(itemType) && false == HasTextConverter(itemType)
                && (true == IsClassType(itemType) || true == IsMemberStruct(itemType))
                && (0 < itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length || 0 < itemType.GetFields(BindingFlags.Public | BindingFlags.Instance).Length);

            if (true == recordItem && data is IDictionary holder && 0 < holder.Count
                && false == holder.Keys.Cast<object>().Any(key => HasMember(itemType, key?.ToString() ?? string.Empty))
                && true == holder.Values.Cast<object>().Any(value => value is IList heldList && true == heldList.Cast<object>().Any(item => item is IDictionary heldRecord
                    && true == heldRecord.Keys.Cast<object>().Any(key => HasMember(itemType, key?.ToString() ?? string.Empty)))))
            {
                records = new List<object>();
                foreach (DictionaryEntry entry in holder)
                {
                    if (entry.Value is IList heldList && true == heldList.Cast<object>().Any(item => item is IDictionary))
                        records.AddRange(heldList.Cast<object>());
                    else if (entry.Value is IDictionary)
                        records.Add(entry.Value);
                    else if (false == FillsNothing(entry.Value, itemType, columnNames, parentDictionary))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog(entry.Value)}] under [{entry.Key}] is not a [{itemType.Name}], so it is not added", entry.Value, GPALObjectType.Other);
                }
            }

            return records;
        }
        // True when a class of its own that enumerates its items, such as a tree node or a page of rows, is given a record
        // whose keys name its members, so its members are filled as any class's are. A collection from System, a
        // dictionary, an array, a generic type and a class derived from a generic collection take their items as a
        // collection does.
        private static bool FillsMembers(Type type, object data)
        {
            return true == IsEnumerableType(type) && false == type.IsArray && false == type.IsGenericType
                && false == IsDictionaryType(type) && null == GenericCollectionBase(type)
                && false == (type.Namespace ?? string.Empty).StartsWith("System", StringComparison.Ordinal)
                && data is IDictionary record
                && false == record.Keys.Cast<object>().All(key => Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                && true == record.Keys.Cast<object>().Any(key => HasMember(type, key?.ToString() ?? string.Empty));
        }
        // A type that is a value alone, with no members to fill: a number, a bool, a char, text, a date, a time span, a
        // Guid, a Uri, a Version, a BigInteger, a Complex or an enum
        internal static bool IsValueAlone(Type type)
        {
            Type valueType = null == type ? null : Nullable.GetUnderlyingType(type) ?? type;

            return null != valueType
                && (true == valueType.IsEnum || true == valueType.IsPrimitive
                    || typeof(string) == valueType || typeof(decimal) == valueType || typeof(DateTime) == valueType
                    || typeof(DateTimeOffset) == valueType || typeof(TimeSpan) == valueType || typeof(Guid) == valueType
                    || typeof(Uri) == valueType || typeof(System.Version) == valueType
                    || typeof(System.Numerics.BigInteger) == valueType || typeof(System.Numerics.Complex) == valueType);
        }
        // A struct of the program's own with members, such as a point of an X and a Y, or a KeyValuePair: written as a
        // record of its members and filled from one. A struct of the framework with members and no converter to and from
        // text, such as a PointF or a Vector2, is one too, as json writes it, where its own text, {X=1.5, Y=2.5}, does not
        // read back. A struct of the framework that is a value alone, such as a DateTime, or that has a converter, such as
        // a System.Drawing.Point, keeps its own conversion, and a tuple its own handling
        private static bool IsMemberStruct(Type type)
        {
            string space = type?.Namespace ?? string.Empty;
            bool frameworkType = "System" == space || true == space.StartsWith("System.", StringComparison.Ordinal)
                || "Microsoft" == space || true == space.StartsWith("Microsoft.", StringComparison.Ordinal);

            return null != type && true == type.IsValueType && false == type.IsEnum && null == Nullable.GetUnderlyingType(type) && false == IsTupleType(type)
                && (true == IsKeyValuePairType(type)
                    || ((false == frameworkType
                            || (false == IsValueAlone(type) && false == IsEnumerableType(type) && false == HasTextConverter(type)))
                        && (0 < type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length
                            || 0 < type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length)));
        }
        // A csv, other delimited, xlsx or grid row of two or more columns that is the text null in every one is the row
        // written for a null record, so it reads back as a null record. A row of one column that is null is a record
        // holding null, as a file of one column writes a null record and a record holding null alike
        internal static bool IsNullRecordRow(object row)
        {
            DataFormat sourceFormat = 0 < (ConverterSettings?.InDataFormat?.Count ?? 0) ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
            List<object> cells = row is IDictionary rowCells ? rowCells.Values.Cast<object>().ToList() : null;
            return (true == IsDelimitedDataFormat(sourceFormat) || DataFormat.XLSX == sourceFormat || DataFormat.GRID == sourceFormat)
                && null != cells && 1 < cells.Count
                && cells.All(cell => cell is string cellText && true == cellText.Trim().Equals("null", StringComparison.OrdinalIgnoreCase));
        }
        // The text null read from xml, html, a delimited file, xlsx or a grid, which write a null as that text; from json or
        // yaml it is text
        internal static bool IsNullText(object value)
        {
            DataFormat readFrom = 0 < ConverterSettings.InDataFormat.Count ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
            return value is string nullText && true == nullText.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
                && (DataFormat.XML == readFrom || DataFormat.HTML == readFrom || DataFormat.XLSX == readFrom || DataFormat.GRID == readFrom || true == IsDelimitedDataFormat(readFrom));
        }
        // Whether a row fills nothing of a class or struct of the program's own, so a collection of them takes nothing for
        // it, with a warning naming the type. Nothing is filled from a value alone, such as a line of text, that the type
        // has no converter from; from a list whose items each fill nothing, a csv row of values or an empty list among
        // them; or from a record none of whose keys, with GPAL's key prefix taken off, names a member of the type and
        // whose records and lists each fill nothing, so a record that wraps rows of the type fills something. An empty
        // record is an empty object, and column names given name a row's values by position, so a row fills something then.
        // A struct of the framework with members, such as a System.Drawing.Point, or a type read from text, such as a
        // CultureInfo, is filled from a record of its members or from the text its converter reads, so a record naming
        // none of them, or a value it has no converter from, fills nothing of it too.
        internal static bool FillsNothing(object row, Type type, List<string> columnNames, object parentDictionary)
        {
            string space = type?.Namespace ?? string.Empty;
            bool frameworkType = "System" == space || true == space.StartsWith("System.", StringComparison.Ordinal)
                || "Microsoft" == space || true == space.StartsWith("Microsoft.", StringComparison.Ordinal);
            bool frameworkStruct = null != type && true == type.IsValueType && false == type.IsEnum && null == Nullable.GetUnderlyingType(type)
                && false == IsValueAlone(type) && false == IsTupleType(type) && false == IsComplexType(type);
            bool ownType = null != type && false == IsKeyValuePairType(type)
                && false == IsEnumerableType(type) && typeof(object) != type && typeof(string) != type
                && ((false == frameworkType && (true == IsClassType(type) || true == IsMemberStruct(type))) || (true == frameworkType && true == frameworkStruct))
                && (0 < type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Length || 0 < type.GetFields(BindingFlags.Public | BindingFlags.Instance).Length);
            // a type of the framework that is read from text by its own converter, such as a CultureInfo, a Font or a
            // System.Drawing.Point, or from a record of its members, such as a PointF or a Vector2, is filled from
            // nothing by a record none of whose keys names one of its members, as a class of the program's own is
            ownType = true == ownType || true == IsStructOrTextType(type);

            // a class read from text by its own converter, such as a Font or a CultureInfo, has no member a record's
            // key fills, so only the records and lists a record holds can fill it
            bool textClass = true == IsTextClassType(type);

            // a key's name with GPAL's key prefix, GPALKEY or GPAL and a number and an underscore, taken off, as
            // CreateObjectFromDictionary takes it off
            string Bare(object key) => Regex.Replace(key?.ToString() ?? string.Empty, @"^(GPALKEY|GPAL\d+_)", string.Empty);
            // a row of a delimited or xlsx file, whose names may number the items of a list member
            DataFormat rowFormat = 0 < ConverterSettings.InDataFormat.Count ? ConverterSettings.InDataFormat[0] : DataFormat.NOTSET;
            bool rowFile = true == IsDelimitedDataFormat(rowFormat) || DataFormat.XLSX == rowFormat;
            // each record and list looked in once, so a YAML mapping that holds itself through an alias is not looked in
            // again; it fills nothing more the second time
            HashSet<object> lookedIn = new HashSet<object>(ReferenceEqualityComparer.Instance);

            bool Empty(object part) => (part is IDictionary || part is IList) && false == lookedIn.Add(part)
                ? true
                : part is IDictionary record
                ? (true == textClass || (0 < record.Count
                    && false == record.Keys.Cast<object>().Any(key => HasMember(type, Bare(key))
                        || true == HasMember(type, Bare(key).Split('.')[0])   // V.X of a csv row names the member V
                        // Tags0 and Items0.Name of a csv row name the list member Tags or Items
                        || (true == rowFile && type.GetMember(Regex.Replace(Bare(key).Split('.')[0], @"\d+$", string.Empty), MemberTypes.Property | MemberTypes.Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                            .Select(member => (member as PropertyInfo)?.PropertyType ?? ((FieldInfo)member).FieldType)
                            .Any(listType => typeof(string) != listType && false == IsDictionaryType(listType) && (true == listType.IsArray || true == IsEnumerableType(listType)))))))
                    && true == record.Values.Cast<object>().Where(value => value is IDictionary || value is IList).All(value => Empty(value))
                : part is IList items
                    ? true == items.Cast<object>().All(item => Empty(item))
                    : null != part && false == type.IsInstanceOfType(part) && false == TypeDescriptor.GetConverter(type).CanConvertFrom(part.GetType());

            bool fillsNothing = true == ownType && (null == columnNames || 0 == columnNames.Count) && true == Empty(row);

            // a record's names that name no member are each reported as CreateObjectFromDictionary reports them, once
            // for each parent the records are in, so a new conversion reports them again, then that the record is not
            // added; a value alone is reported with the value
            if (true == fillsNothing && row is IDictionary unmapped && false == textClass)
            {
                if (null == parentDictionary || false == ReferenceEquals((object)lastParentDictionary, parentDictionary))
                {
                    lastParentDictionary = parentDictionary;
                    lastErrorMessage.Clear();
                    supressedMessage = false;
                }

                List<string> messages = unmapped.Keys.Cast<object>().Select(key => $"No property/field [{Bare(key)}] in [{type.Name}]").Distinct().ToList();
                messages.Add($"No member of [{type.Name}] is named in the record, so it is not added");
                foreach (string message in messages)
                {
                    if (false == lastErrorMessage.Contains(message))
                    {
                        lastErrorMessage.Add(message);
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, message, row, GPALObjectType.Other);
                        supressedMessage = false;
                    }
                    else if (false == supressedMessage)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, "Suppressing repeat messages", row, GPALObjectType.Other);
                        supressedMessage = true;
                    }
                }
            }
            else if (true == fillsNothing)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog(row)}] fills no member of [{type.Name}], so it is not added", row, GPALObjectType.Other);

            return fillsNothing;
        }
        // A struct filled from a record of its members, such as a PointF, a Vector2 or a struct of the program's own, or a
        // type read from text by its own converter, such as a CultureInfo, a Font or a System.Drawing.Point, that is not
        // a value alone, a collection, a pair, a tuple or a Nullable
        private static bool IsStructOrTextType(Type type)
        {
            return null != type && null == Nullable.GetUnderlyingType(type) && false == IsValueAlone(type) && false == IsEnumerableType(type) && false == IsKeyValuePairType(type)
                && false == IsTupleType(type) && typeof(object) != type && (true == IsMemberStruct(type) || true == HasTextConverter(type));
        }
        // A struct of the framework given a record of its members, such as a System.Drawing.Point read from xml as an X
        // and a Y, is filled from the record, as is a Nullable of one, such as a Point?; given text, its own converter reads it
        private static bool IsStructRecord(Type type, object value)
        {
            Type structType = null == type ? null : Nullable.GetUnderlyingType(type) ?? type;
            return value is IDictionary && null != structType && true == structType.IsValueType && false == structType.IsEnum
                && false == IsValueAlone(structType) && false == IsTupleType(structType) && false == IsKeyValuePairType(structType) && false == IsComplexType(structType);
        }
        // A record of a Key and a Value alone that fills this pair; when the pair's Value is a pair, the record's Value
        // is a record of that pair, so a pair of a key and a pair is not taken for a row of pairs
        private static bool IsRecordOfPair(object data, Type pairType)
        {
            Type valueType = PairTypeArguments(pairType)[1];

            return data is IDictionary record && true == IsPairRecord(record)
                && (false == IsKeyValuePairType(valueType)
                    || true == IsRecordOfPair(record[record.Keys.Cast<object>().First(key => string.Equals(key?.ToString(), "Value", StringComparison.OrdinalIgnoreCase))], valueType));
        }
        // The Key and Value types of a KeyValuePair, or object and object for a DictionaryEntry
        private static Type[] PairTypeArguments(Type pairType)
        {
            return true == pairType.IsGenericType ? pairType.GetGenericArguments() : new[] { typeof(object), typeof(object) };
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
            // a property with a setter of its own, such as System.Drawing.Point.X, has no backing field of that name, so
            // its setter takes the value on a boxed copy, which is kept
            PropertyInfo setter = null == field
                ? typeof(T).GetProperty(Regex.Replace(fieldName, @"^<(.+)>k__BackingField$", "$1"), BindingFlags.Public | BindingFlags.Instance)
                : null;
            object boxed = instance;

            if (field != null)
                field.SetValueDirect(__makeref(instance), value);
            else if (null != setter && true == setter.CanWrite && 0 == setter.GetIndexParameters().Length)
            {
                setter.SetValue(boxed, value);
                instance = (T)boxed;
            }
            return instance;
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

            // A record that holds only the placeholder, such as {"Parent": "Recursion to [Root]"}, as json and xml write a
            // member that links back to an object holding it, is that link, which json and xml cannot hold: a class member
            // is null, where it was an empty object made from the record
            if (propertyValue is IDictionary markerRecord && 1 == markerRecord.Count && false == propertyType.IsValueType
                && false == IsDictionaryType(propertyType) && typeof(string) != propertyType
                && markerRecord.Values.Cast<object>().First() is string markerText
                && true == markerText.StartsWith("Recursion to [", StringComparison.Ordinal) && true == markerText.EndsWith("]", StringComparison.Ordinal))
                propertyValue = null;

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
            // a null stays a null: ValueToString writes it as the text null, which json, yaml and a class keep as text
            else if (null == (object)propertyValue)
                entryValue = null;
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
                    // Generic helper unboxes to a real local so __makeref is valid; returns updated struct.
                    // Assigning back to ref dynamic propagates to objOrList in CreateObjectFromDictionary. A property
                    // with a setter of its own and no backing field, such as System.Drawing.Point.X, is set through
                    // its setter there, as a set on the dynamic instance lands on a copy that is lost
                    instance = GetSetStructMethod(instance.GetType()).Invoke(null, new[] { instance, backingName, converted });
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
                if (null == complex)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{entryValue}] is not a [Complex], so [{propertyOrField.Name}] is left as it was", (object)instance, GPALObjectType.Other);
                else
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
            else if (true == IsKeyValuePairType(propertyType))
            {
                // a pair written as its Key and Value; one that is already a pair is set as it is
                object pair = propertyValue is IDictionary
                    ? CreateObjectFromDictionary(propertyValue, propertyType, parentDictionary, columnNames)
                    : (object)propertyValue;

                if (true == propertyType.IsInstanceOfType(pair))
                    propertyOrField.SetValue(instance, pair);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{propertyValue}] is not a Key and a Value, so [{propertyOrField.Name}] is left as it was", (object)propertyValue, GPALObjectType.Other);
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
                            // the one value is the one item the loop below reads and converts to the item type
                            enumeration = new List<object> { _singleItem };
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Scalar [{propertyValue?.GetType()}] is not compatible with collection element type [{_elemType}]. Leaving [{propertyType}] at default.", (object)propertyValue, GPALObjectType.Other);
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
                            {
                                if (true == AddToList(list, null, count))
                                    count++;
                            }
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

                        // an item typed object keeps what it was given, a record as a dictionary, a list as a list and a value
                        // as itself; the text null of xml, html, a delimited file, xlsx or a grid is a null
                        if (typeof(object) == objectElementType)
                        {
                            if (true == AddToList(list, true == IsNullText((object)itemValue) ? null : ObjectSlotValue((object)itemValue), count))
                                count++;
                        }
                        else if (true == IsComplexType(objectElementType))
                        {
                            dynamic complex = ParseComplex(itemValue.ToString());
                            if (null == complex)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{itemValue}] is not a [Complex], so it is not added", (object)instance, GPALObjectType.Other);
                            else if (true == AddToList(list, complex, count))
                                count++;
                        }
                        else if (true == IsSimpleType(objectElementType) || IsEnumType(objectElementType))  // <-- enum support for collection elements
                        {
                            // a value that does not convert is not added, with a warning
                            valueNotConverted = false;
                            // the value as the converter writes it, the same on every culture; a character as itself, so a NUL stays one
                            dynamic tempVal = ConvertListItem(itemValue is char ? itemValue.ToString() : ValueToString((object)itemValue), objectElementType, propertyOrField?.Name);
                            if (false == valueNotConverted && true == AddToList(list, tempVal, count))
                                count++;
                        }
                        else if (IsEnumerableType(objectElementType))
                        {
                            // the item type of the inner list, also of an array such as the int[] of an int[][]
                            Type elementType = CollectionItemType(objectElementType);
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

                            // an array is made as long as the items, and holds only the items added
                            dynamic myList = InstantiateOne(objectElementType, null == resolvedItemValue ? 0 : ((IEnumerable)resolvedItemValue).Cast<object>().Count());

                            if (true == IsComplexType(elementType))
                            {
                                dynamic complex;

                                foreach (object item2 in (IEnumerable<dynamic>)resolvedItemValue)
                                {
                                    dynamic iv2 = item2;
                                    if (iv2 is IList il2 && ((IList)il2).Count == 1) iv2 = ((IList)il2)[0];
                                    complex = ParseComplex(iv2?.ToString());
                                    if (null == complex)
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{iv2}] is not a [Complex], so it is not added", (object)instance, GPALObjectType.Other);
                                    else if (true == AddToList(myList, complex, innerCount))
                                        innerCount++;
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
                                    if (true == AddToList(myList, convItem, innerCount))
                                        innerCount++;
                                }
                            }
                            if ((object)myList is Array innerArray && innerCount < innerArray.Length)
                            {
                                Array innerItems = Array.CreateInstance(innerArray.GetType().GetElementType(), innerCount);
                                Array.Copy(innerArray, innerItems, innerCount);
                                myList = innerItems;
                            }
                            if (true == AddToList(list, myList, count))
                                count++;
                        }
                        else if (itemValue is IDictionary itemDictValue && !IsDictionaryType(objectElementType))
                        {
                            // class/struct/tuple element — deserialize the dict into the target type
                            dynamic converted = CreateObjectFromDictionary(itemDictValue, objectElementType, parentDictionary, columnNames);
                            if (true == AddToList(list, converted, count))
                                count++;
                        }
                        else
                        {
                            if (true == AddToList(list, itemValue, count))  // fallback: use raw value
                                count++;
                        }
                    }
                    // an array was made as long as the items, and holds only the items added
                    if ((object)list is Array setArray && count < setArray.Length)
                    {
                        Array setItems = Array.CreateInstance(setArray.GetType().GetElementType(), count);
                        Array.Copy(setArray, setItems, count);
                        list = setItems;
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
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unknown object type [{propertyType}]", (object)propertyValue, GPALObjectType.Other);
            }
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
                        // both failures are passed: making the type asked for, then the Dictionary made in its place
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                            $"Failed to instantiate dictionary type [{objectType}], and a Dictionary in its place failed too",
                            null, GPALObjectType.Other, new AggregateException(exInner, ex));
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
                // a type whose constructor throws for the default arguments it was given, as a Uri does for a null
                // text, has no instance made that way either
                catch (Exception createFailed) when (createFailed is MissingMethodException || createFailed is TargetInvocationException)
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

        // The objects of the source on the path of a copy from one object to another, the source itself first, set while
        // the copy is made; null when no such copy is being made
        [ThreadStatic]
        internal static HashSet<object> objectsBeingCopied;

        /// <summary>
        /// Returns a copy of an object a place typed object holds, in a copy from one object to another, so the copy does
        /// not share it with the source.
        /// </summary>
        /// <remarks>
        /// A class's records hold the object itself in a place typed object. One the converter makes again exactly from its
        /// records is made again as its own type: an array, a list or a dictionary whose items all are, or a class of the
        /// program's own, out of the System and Microsoft namespaces, with a constructor that takes nothing. Any other, such
        /// as a StringBuilder, a stream, a list of them or a Bitmap, and anything disposable, such as a form, is held as it
        /// is, shared with the source. One that
        /// links back to an object on the path is left null, and the copy's walk of its links sets it to that object's
        /// copy. One object held in two places is two copies. Outside such a copy the value is returned as it is.
        /// </remarks>
        /// <param name="held">The value the place holds</param>
        /// <returns>The copy, the value itself, or null for a link back</returns>
        internal static object CopyOfHeld(object held)
        {
            object retVal = held;
            Type heldType = held?.GetType();
            HashSet<object> itemsWalked = new HashSet<object>(ReferenceEqualityComparer.Instance);

            // A class made again exactly from its records: an array, a list or a dictionary whose items all are, all the way
            // down, or a class of the program's own; never anything disposable
            bool MadeAgain(Type classType, object value)
            {
                string space = classType.Namespace ?? string.Empty;
                bool frameworkType = "System" == space || true == space.StartsWith("System.", StringComparison.Ordinal)
                    || "Microsoft" == space || true == space.StartsWith("Microsoft.", StringComparison.Ordinal);
                bool collection = true == classType.IsArray
                    || (null != classType.GetConstructor(Type.EmptyTypes) && (value is IList || value is IDictionary));
                return true == classType.IsClass && typeof(string) != classType && false == typeof(IDisposable).IsAssignableFrom(classType)
                    && (true == collection || (false == frameworkType && null != classType.GetConstructor(Type.EmptyTypes)
                        && false == IsValueAlone(classType) && false == HasTextConverter(classType)))
                    && (false == value is IEnumerable || false == itemsWalked.Add(value)
                        || (value is IDictionary entries
                            ? true == entries.Keys.Cast<object>().All(item => true == ItemMadeAgain(item)) && true == entries.Values.Cast<object>().All(item => true == ItemMadeAgain(item))
                            : true == ((IEnumerable)value).Cast<object>().All(item => true == ItemMadeAgain(item))));
            }
            // an item is made again when it is null, text, a value, a value read as text, such as a Uri, or such a class
            bool ItemMadeAgain(object item) => null == item || item is string || true == item.GetType().IsValueType
                || true == IsValueAlone(item.GetType()) || true == HasTextConverter(item.GetType()) || true == MadeAgain(item.GetType(), item);

            bool copies = null != objectsBeingCopied && null != heldType && true == MadeAgain(heldType, held);

            if (true == copies && true == objectsBeingCopied.Contains(held))
                retVal = null;
            else if (true == copies)
            {
                objectsBeingCopied.Add(held);
                try
                {
                    // the objects on the path are links back in the records, so they are not written into them again
                    Dictionary<object, dynamic> heldRecords = ConvertClassToDictionary(held, string.Empty,
                        new HashSet<object>(objectsBeingCopied.Where(onPath => false == ReferenceEquals(onPath, held)), ReferenceEqualityComparer.Instance));
                    retVal = CreateObjectFromDictionary(heldRecords, heldType, heldRecords, null);
                }
                finally
                {
                    objectsBeingCopied.Remove(held);
                }
            }

            return retVal;
        }

        /// <summary>
        /// The items of a list as xml writes one, its items under one element name each, such as {"row": [...]} or {"row":
        /// "a"} for a list of one, which json and yaml written from xml keep. A single key that is a number, as rows are
        /// keyed, or a member of the item, as {"Name": "Al"} is one record of a List&lt;Item&gt;, is not such a list, nor is
        /// one record given to a list of dictionaries or of objects, which takes it as it is. A value typed object, given
        /// as a null item type, is such a list only when it holds two items or more, as one item under one name cannot be
        /// told from a record.
        /// </summary>
        /// <param name="value">A value given to a list</param>
        /// <param name="itemType">The list's item type, or null for a value typed object</param>
        /// <returns>The items, or null when the value is not a list written so</returns>
        internal static IList ListWrapperItems(object value, Type itemType)
        {
            IList retVal = null;
            if (value is IDictionary wrapper && 1 == wrapper.Count && (null == itemType || (typeof(object) != itemType && false == IsDictionaryType(itemType) && false == IsKeyValuePairType(itemType))))
            {
                string itemName = wrapper.Keys.Cast<object>().First()?.ToString() ?? string.Empty;
                object items = wrapper.Values.Cast<object>().First();
                if (false == Regex.IsMatch(itemName, @"^(GPALKEY)?\d+$") && (null == itemType || false == HasMember(itemType, itemName)))
                    retVal = items is IList itemList ? itemList : null == itemType ? null : new List<object> { items };
            }
            return retVal;
        }

        // A complex number written as GPAL writes one, such as 1+2i or -3.5-4i; text that is not one gives null
        internal static dynamic ParseComplex(string complexString)
        {
            dynamic retVal = null;
            string s = complexString?.Trim() ?? string.Empty;

            string numPart = 0 < s.Length ? s.Substring(0, s.Length - 1) : string.Empty; // remove 'i'

            // the sign before the imaginary part is the last + or - that is not the sign of an exponent, as in 1E-10
            int signIndex = -1;
            for (int charIdx = numPart.Length - 1; 0 < charIdx && -1 == signIndex; charIdx--)
                if (('+' == numPart[charIdx] || '-' == numPart[charIdx]) && 'e' != numPart[charIdx - 1] && 'E' != numPart[charIdx - 1])
                    signIndex = charIdx;
            if (-1 == signIndex && 0 < numPart.Length && ('+' == numPart[0] || '-' == numPart[0]))
                signIndex = 0;

            if (-1 != signIndex)
            {
                string realStr = numPart.Substring(0, signIndex);   // may be empty
                string imagStr = numPart.Substring(signIndex);     // "+4" or "-4"
                double real = 0;

                if ((true == string.IsNullOrEmpty(realStr) || true == double.TryParse(realStr, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out real))
                    && true == double.TryParse(imagStr, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double imag))
                    // a part written as a zero with a minus is a negative zero, which .NET Framework reads as 0
                    retVal = new Complex(0 == real && true == realStr.StartsWith("-", StringComparison.Ordinal) ? -0.0 : real,
                        0 == imag && true == imagStr.StartsWith("-", StringComparison.Ordinal) ? -0.0 : imag);
            }

            return retVal;
        }
        // Converts an item of a list, array or other collection, or a dictionary value, to the item type. A value alone,
        // such as text, a number or a null, a list of values alone given to a collection, such as ["bad"] for an
        // IPAddress[], or an item of a type read from text or held as one value, such as a number, a Point, an IPAddress or
        // an int[], that does not convert sets valueNotConverted, so the caller does not add it, and is reported here as
        // not added. A record or a list given to a class or struct of the program's own is reported by the conversion of
        // its members, and does not set valueNotConverted
        private static object ConvertListItem(dynamic value, Type itemType, string propertyName = null)
        {
            object item = null;
            bool wasConverting = convertingListItem;
            bool itemFromText = (false == (object)value is IDictionary && false == (object)value is IList)
                || ((object)value is IList valuesAlone && true == IsEnumerableType(itemType) && false == IsDictionaryType(itemType)
                    && true == valuesAlone.Cast<object>().All(entry => false == entry is IDictionary && false == entry is IList))
                || true == IsValueAlone(itemType) || true == HasTextConverter(itemType)
                || (true == IsSimpleType(itemType) && false == IsMemberStruct(itemType))
                || (true == IsEnumerableType(itemType) && typeof(string) != itemType && false == IsDictionaryType(itemType)
                    && (true == IsValueAlone(CollectionItemType(itemType)) || true == HasTextConverter(CollectionItemType(itemType))));
            valueNotConverted = false;
            convertingListItem = itemFromText;
            try
            {
                item = ConvertValue(value, itemType, null, propertyName);
            }
            finally
            {
                convertingListItem = wasConverting;
            }
            valueNotConverted = true == itemFromText && true == valueNotConverted;
            if (true == valueNotConverted)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}]{(null != propertyName ? $" from [{propertyName}]" : string.Empty)} is not a [{ShortTypeName(itemType)}], so it is not added", (object)value, GPALObjectType.Other);

            return item;
        }
        // Whether a list item of values read from text or held as one value, an array or a framework collection such as an
        // int[], a List<Point> or an IPAddress[], was made empty from a source that has entries, as when none of them converts, such as the int[] made
        // from ["x"] or from a record. Such an item is not added; each entry said why it is not added. A class of its own
        // that enumerates, such as a tree node that enumerates its children, is filled by its members and is not such an item
        private static bool NoValueAdded(Type itemType, object made, object source) =>
            true == IsEnumerableType(itemType) && false == IsDictionaryType(itemType)
            && (true == itemType.IsArray || "System" == itemType.Namespace || true == (itemType.Namespace ?? string.Empty).StartsWith("System.", StringComparison.Ordinal))
            && (true == IsValueAlone(CollectionItemType(itemType)) || true == HasTextConverter(CollectionItemType(itemType))
                || (true == IsSimpleType(CollectionItemType(itemType)) && false == IsMemberStruct(CollectionItemType(itemType))))
            && made is IEnumerable madeItems && false == madeItems.Cast<object>().Any()
            && source is IEnumerable sourceEntries && false == source is string && true == sourceEntries.Cast<object>().Any();
        // todo: handle dictionary, stack and hashset, add key parm
        public static bool AddToList(dynamic listQueueStackDictHashsetOrArray, dynamic value, int count)
        {
            bool retVal = true;
            value = EnsureCollectionType(listQueueStackDictHashsetOrArray, value);

            // a value that did not convert to the item type of a typed collection is not added
            Type containerType = ((object)listQueueStackDictHashsetOrArray)?.GetType();
            Type itemType = null == containerType ? null
                : containerType.IsArray ? containerType.GetElementType()
                : true == containerType.IsGenericType && true == IsEnumerableType(containerType) && false == IsDictionaryType(containerType) ? CollectionItemType(containerType)
                : null;

            // a null is not an item of a value type that cannot hold null, such as a Complex
            if (null != itemType && (null == (object)value
                    ? true == itemType.IsValueType && null == Nullable.GetUnderlyingType(itemType)
                    : false == itemType.IsInstanceOfType((object)value)))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{DescribeForLog((object)value)}] is not a [{itemType.Name}], so it is not added", (object)listQueueStackDictHashsetOrArray, GPALObjectType.Other);
                retVal = false;
            }
            else if ((true == IsEnumerableObject(listQueueStackDictHashsetOrArray) && false == IsArray(listQueueStackDictHashsetOrArray)) || true == IsList(listQueueStackDictHashsetOrArray))
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
                    else
                    {
                        // a LinkedList takes its items through AddLast; looked up by its item type, as AddLast also
                        // takes a node
                        MethodInfo addLastMethod = containerType.GetMethod("AddLast", new[] { CollectionItemType(containerType) });

                        if (null != addLastMethod)
                            addLastMethod.Invoke(listQueueStackDictHashsetOrArray, new object[] { value });
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{containerType.Name}] has no Add, Enqueue, Push or AddLast of its items, so [{DescribeForLog((object)value)}] is not added", (object)listQueueStackDictHashsetOrArray, GPALObjectType.Other);
                            retVal = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Can't Add [{value}] to [{listQueueStackDictHashsetOrArray}]. May require a Convert.ChangeType", (object)listQueueStackDictHashsetOrArray, GPALObjectType.Other, ex);
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

            // Inlined element type detection (e.g., List<Complex> -> Complex); a generic type that is not a collection,
            // such as a KeyValuePair, has none
            Type targetElementType = containerType.IsArray ? containerType.GetElementType() : (containerType.IsGenericType && true == IsEnumerableType(containerType) ? CollectionItemType(containerType) : null);

            // If the value already matches the container (e.g., List matches List), return as is
            // If targetElementType is null, we can't determine how to wrap
            if (targetElementType == null || targetElementType.IsAssignableFrom(value.GetType()))
                return value;

            // Ensure the scalar matches the internal element type (converting if it's a string representation); a value
            // that does not convert, such as x for an int[], is given back as it is, so AddToList does not add it
            // an element read from text alone, such as a number or an int[], is reported by AddToList as not added; one
            // with members is reported by the conversion of its members
            bool wasConverting = convertingListItem;
            valueNotConverted = false;
            convertingListItem = (false == (object)value is IDictionary && false == (object)value is IList)
                || true == IsValueAlone(targetElementType) || true == HasTextConverter(targetElementType)
                || (true == IsEnumerableType(targetElementType) && typeof(string) != targetElementType && false == IsDictionaryType(targetElementType)
                    && (true == IsValueAlone(CollectionItemType(targetElementType)) || true == HasTextConverter(CollectionItemType(targetElementType))));
            object convertedItem = null;
            try
            {
                convertedItem = ConvertValue(value, targetElementType, container);
            }
            finally
            {
                convertingListItem = wasConverting;
            }
            bool converted = false == valueNotConverted;

            // Now wrap the (potentially converted) item into the collection; an element that is not a collection, such
            // as a class, is not made empty for a value that does not convert to it, and the value is given back as it is.
            // A collection element is made only from a value it can hold: the element itself holding the value, as
            // ConvertValue makes [1] of 1 for an int[], or one of its items. Another value, such as a number for a
            // Dictionary or for a Person[], which converts to an empty element or one holding null, is given back as it
            // is, so AddToList does not add it and says so
            Type wrappedItemType = true == IsEnumerableType(targetElementType) && false == IsDictionaryType(targetElementType) ? CollectionItemType(targetElementType) : null;
            bool wraps = null != wrappedItemType
                && ((true == targetElementType.IsInstanceOfType(convertedItem) && convertedItem is IEnumerable convertedItems
                        && true == convertedItems.Cast<object>().Any() && false == convertedItems.Cast<object>().Contains(null))
                    || true == wrappedItemType.IsInstanceOfType(convertedItem));
            return false == converted ? value
                : true == wraps ? WrapInCollection(targetElementType, convertedItem)
                : true == IsEnumerableType(targetElementType) ? value
                : convertedItem;
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
                // an item that is already the array, as ConvertValue makes [1] of 1, is the array
                Array newArray = true == typeToCreate.IsInstanceOfType((object)item) ? (Array)item : Array.CreateInstance(typeToCreate.GetElementType(), 1);
                if (false == ReferenceEquals(newArray, (object)item))
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

            // A generic collection interface, such as IList<T>, IEnumerable<T>, ICollection<T> or IReadOnlyList<T>, is
            // made as a List<T>, and a dictionary interface as a Dictionary<TKey, TValue>; the types that implement them
            // are generic, so none is found by the search below
            Type[] typeArguments = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
            Type concreteCollection = 1 == typeArguments.Length ? typeof(List<>).MakeGenericType(typeArguments)
                : 2 == typeArguments.Length ? typeof(Dictionary<,>).MakeGenericType(typeArguments)
                : null;

            if (true == type.IsInterface && null != concreteCollection && true == type.IsAssignableFrom(concreteCollection))
                type = concreteCollection;

            // If type is an interface or abstract, find a concrete implementation
            return type == concreteCollection ? type : type.Assembly.GetTypes()
                .FirstOrDefault(t => !t.IsInterface && !t.IsAbstract && type.IsAssignableFrom(t))
                ?? throw new InvalidOperationException($"No concrete implementation found for {type.Name}.");
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
    }
}
