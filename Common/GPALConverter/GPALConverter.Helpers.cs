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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using static GenerallyPositive.Enums;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectGraphTraversalStrategies;
using YamlDotNet.Serialization.ObjectFactories;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using System.Reflection;
using Newtonsoft.Json;
using System.Collections;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace GenerallyPositive
{
    public partial class GPALConverter
    {
        /// <summary>
        /// Writes the YAML in OutputData to the file. Added to a file that has content, the file holds one list: the rows
        /// already in it and the rows added, under values:, as a list is written. A list, or a list under values:, gives
        /// its items as rows; a mapping, such as one object, or a value gives itself as one row. Each value keeps the way
        /// it was written, quoted or plain, and each anchored value gets a name no other value has, as two documents or
        /// a name given twice may share one, so every alias names what it named before. An empty document, or one that
        /// holds only a null, gives no row.
        /// A file that cannot be read or added to is left as it was, with an exception event.
        /// </summary>
        /// <param name="filename">The YAML file</param>
        /// <param name="addingToContent">True when the conversion adds to a file that has content</param>
        private void WriteYamlFile(string filename, bool addingToContent)
        {
            bool written = true;

            if (true == addingToContent)
            {
                try
                {
                    YamlStream inFile = new YamlStream();
                    YamlStream added = new YamlStream();
                    inFile.Load(new StringReader(File.ReadAllText(filename)));
                    added.Load(new StringReader(ConverterSettings.OutputData));

                    // every node once, a node an alias names again included, found without following a cycle twice
                    List<YamlNode> NodesOf(YamlStream stream)
                    {
                        List<YamlNode> found = new List<YamlNode>();
                        HashSet<object> seen = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);
                        Stack<YamlNode> toVisit = new Stack<YamlNode>(stream.Documents.Select(document => document.RootNode));
                        while (0 < toVisit.Count)
                        {
                            YamlNode node = toVisit.Pop();
                            if (true == seen.Add(node))
                            {
                                found.Add(node);
                                if (node is YamlSequenceNode sequence)
                                    foreach (YamlNode child in sequence.Children)
                                        toVisit.Push(child);
                                else if (node is YamlMappingNode mapping)
                                    foreach (KeyValuePair<YamlNode, YamlNode> entry in mapping.Children)
                                    {
                                        toVisit.Push(entry.Key);
                                        toVisit.Push(entry.Value);
                                    }
                            }
                        }
                        return found;
                    }

                    // the value that holds a name keeps it; another value under that name gets a name of its own
                    Dictionary<string, YamlNode> anchorOwners = new Dictionary<string, YamlNode>();
                    foreach (YamlNode node in NodesOf(inFile).Concat(NodesOf(added)).Where(node => false == node.Anchor.IsEmpty))
                    {
                        string ownName = node.Anchor.Value;
                        int suffix = 2;
                        while (true == anchorOwners.ContainsKey(ownName) && false == ReferenceEquals(anchorOwners[ownName], node))
                            ownName = $"{node.Anchor.Value}{suffix++}";
                        anchorOwners[ownName] = node;
                        node.Anchor = new AnchorName(ownName);
                    }

                    // a list's items are its rows, a list under values: too; an empty document, or one holding only a
                    // plain null (~, null, Null or NULL), gives none, and anything else is one row
                    YamlSequenceNode rows = new YamlSequenceNode(inFile.Documents.Concat(added.Documents)
                        .Where(document => false == (document.RootNode is YamlScalarNode nullRoot && ScalarStyle.Plain == nullRoot.Style && true == nullRoot.Tag.IsEmpty
                            && true == Regex.IsMatch(nullRoot.Value ?? string.Empty, "^(~|null|Null|NULL)?$")))
                        .SelectMany(document => document.RootNode is YamlMappingNode wrapper && 1 == wrapper.Children.Count
                            && wrapper.Children.First().Key is YamlScalarNode wrapperKey && "values" == wrapperKey.Value
                            && wrapper.Children.First().Value is YamlSequenceNode wrappedRows
                                ? wrappedRows.Children
                                : document.RootNode is YamlSequenceNode rowList ? rowList.Children : (IEnumerable<YamlNode>)new[] { document.RootNode })
                        .ToList());
                    rows.Style = SequenceStyle.Block;
                    ConverterSettings.OutputData = new SerializerBuilder().Build().Serialize(new YamlMappingNode(new YamlScalarNode("values"), rows));
                }
                catch (Exception ex)
                {
                    written = false;
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Nothing is added to [{filename}]", this, GPALObjectType.Converter, ex);
                }
            }

            if (true == written)
                File.WriteAllText(filename, ConverterSettings.OutputData);
        }
        /// <summary>
        /// Recursively flattens a dictionary or list (e.g. parsed JSON/YAML) into a list of "path=value" strings,
        /// using dot-separated keys and "[index]" segments to represent nesting.
        /// </summary>
        /// <param name="dictOrList">The dictionary, list, or scalar value to flatten</param>
        /// <param name="prefix">The path prefix to prepend to keys at this level (used during recursion)</param>
        /// <returns>A flat list of "path=value" strings representing the hierarchical input</returns>
        private List<string> BuildHierarchicalStrings(dynamic dictOrList, string prefix = "")
        {
            var result = new List<string>();
            if (dictOrList == null) return result;

            // Handle dictionaries (like reporting, uses)
            if (ConverterHelper.IsDictionaryType(dictOrList.GetType()))
            {
                foreach (var kvp in dictOrList)
                {
                    string key = kvp.Key.ToString();
                    dynamic value = kvp.Value;
                    string newPrefix = string.IsNullOrEmpty(prefix) ? "" : $"{prefix}.{key}";

                    // a null value has no type: it is a leaf, below
                    if (null != (object)value && ConverterHelper.IsDictionaryType(value.GetType()))
                    {
                        result.AddRange(BuildHierarchicalStrings(value));
                    }
                    else if (null != (object)value && ConverterHelper.IsEnumerableType(value.GetType()) && typeof(string) != value.GetType())
                    {
                        int index = 0;
                        foreach (var item in value)
                        {
                            string indexedPrefix = $"{newPrefix}[{index}]";
                            // a null item of a list, such as the second of a YAML values: list of a and null, is a null
                            if (null == (object)item)
                                result.Add(null);
                            else
                                result.AddRange(BuildHierarchicalStrings(item));
                            index++;
                        }
                    }
                    else
                    {
                        // Leaf node: add the key-value pair with the full path; a value alone is added as it is, a null as a null,
                        // such as the second item of the json ["a", null] read into a List<string>, and so is the text null
                        // of a csv, tsv, xlsx, grid, xml or html source, which writes a null as that text
                        if (false == string.IsNullOrEmpty(newPrefix))
                            result.Add($"{newPrefix}={value?.ToString() ?? ""}");
                        else
                            result.Add(true == ConverterHelper.IsNullText((object)value) ? null : value?.ToString());
                    }
                }
            }
            // Handle lists (like stored_procs)
            else if (ConverterHelper.IsEnumerableType(dictOrList.GetType()) && dictOrList.GetType() != typeof(string))
            {
                int index = 0;
                foreach (var item in dictOrList)
                {
                    // an item of a list with no path, such as a row of a YAML values: list of lists, is a value alone,
                    // as an item of a list in a dictionary is
                    string indexedPrefix = string.IsNullOrEmpty(prefix) ? "" : $"{prefix}[{index}]";
                    // a null item is a null, such as the second of the YAML list of lists [[a, null]]
                    if (null == (object)item)
                        result.Add(null);
                    else
                        result.AddRange(BuildHierarchicalStrings(item, indexedPrefix));
                    index++;
                }
            }
            else
            {
                // Leaf node: add the key-value pair with the full path; a value with no path, such as one value of a
                // csv row, is added alone, and the text null of a csv, tsv, xlsx, grid, xml or html source, which writes a
                // null as that text, is a null
                if (false == string.IsNullOrEmpty(prefix))
                    result.Add($"{prefix}={dictOrList?.ToString() ?? ""}");
                else
                    result.Add(true == ConverterHelper.IsNullText((object)dictOrList) ? null : $"{dictOrList?.ToString() ?? ""}");
            }

            return result;
        }
        /// <summary>
        /// Recursively walks a source object (which may be a scalar, collection, or dictionary) and collects all
        /// values that are (or can be converted to) the given target type.
        /// </summary>
        /// <param name="sourceItem">The source value, collection, or dictionary to walk</param>
        /// <param name="targetType">The type that returned items must be, or be convertible to</param>
        /// <returns>A flat sequence of values assignable to (or converted to) targetType</returns>
        private static IEnumerable<object> ConvertToCompatibleItems(object sourceItem, Type targetType)
        {
            var results = new List<object>();

            // a null item is a null item of a target that can hold null, such as the second Item of [{...}, null]
            if (sourceItem == null)
            {
                if (false == targetType.IsValueType || null != Nullable.GetUnderlyingType(targetType))
                    results.Add(null);
                return results;
            }

            // Direct compatibility check
            if (targetType.IsAssignableFrom(sourceItem.GetType()))
            {
                results.Add(sourceItem);
                return results;
            }

            // Handle collections
            if (sourceItem is IEnumerable enumerable && !(sourceItem is string))
            {
                foreach (var nestedItem in enumerable)
                {
                    results.AddRange(ConvertToCompatibleItems(nestedItem, targetType));
                }
                return results;
            }

            // Handle dictionaries
            if (sourceItem is IDictionary dict)
            {
                foreach (var value in dict.Values)
                {
                    if (value != null && targetType.IsAssignableFrom(value.GetType()))
                    {
                        results.Add(value);
                    }
                    else if (value != null)
                    {
                        results.AddRange(ConvertToCompatibleItems(value, targetType));
                    }
                }
                return results;
            }

            // Try type conversion
            var convertedItems = new List<object>();
            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(sourceItem.GetType()))
            {
                try
                {
                    var converted = converter.ConvertFrom(sourceItem);
                    convertedItems.Add(converted);
                }
                catch
                {
                    // Skip items that can't be converted
                }
            }
            else if (targetType == typeof(string))
            {
                try
                {
                    convertedItems.Add(sourceItem.ToString());
                }
                catch
                {
                    // Skip items that can't be converted
                }
            }

            results.AddRange(convertedItems);
            return results;
        }
    }
}
