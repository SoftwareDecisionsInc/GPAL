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
using System.Runtime.CompilerServices;
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

            // an object inside a list or under a key is a list of its own, so its fields are never an li directly in an li
            if (true == isTopLevel)
                sb.Append($"<ul id='renderfull' {listStyle}>\n");
            else
                sb.Append($"<ul id=rf{idCnt++} {listStyle}>\n");

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

                // an xml element's text beside its attributes or child elements is its value, labeled value as json writes
                // it; text of any other source is an item with no label. Each is an item of the list, so no text is
                // directly inside a <ul>
                if ("#text".Equals(key) && 0 < (ConverterSettings?.InDataFormat?.Count ?? 0) && DataFormat.XML == ConverterSettings.InDataFormat[0])
                    key = "value";

                if (false == key.Equals("#text") && false == string.IsNullOrEmpty(key)) // do not emit our array index keys
                {
                    sb.Append("<li><strong>");
                    sb.Append(HttpUtility.HtmlEncode(key));
                    sb.Append("</strong>: ");
                    listStyle = "style = 'list-style: none; margin: 8px; padding-left: 8px;'";
                }
                else
                    sb.Append("<li>");      // a row of a list is an item with no label, so rows do not run together
                Type valueType = value?.GetType();
                bool isCollection = IsDictionaryType(valueType) || IsEnumerableType(valueType);
                bool isTuple = IsTupleType(valueType);
                bool isRecord = null != valueType && false == IsSimpleType(valueType) && (true == IsCustomStructType(valueType) || true == IsClassType(valueType));

                int collectionCount = 0;

                if (true == isCollection)
                    collectionCount = ObjectCopier.GetItemCount(null, value);

                if (true == emitUl && 1 < dictCount)
                    sb.Append($"<ul id=rf{idCnt++} {listStyle}><li>\n");

                sb.Append(RenderValue(value, isCollection || isTuple || isRecord, isCollection || isTuple || isRecord ? "style='margin: 8px; padding-left: 8px;'" : listStyle));

                if (true == emitUl && 1 < dictCount)
                    sb.Append("</li></ul>");

                sb.Append("</li>\n");
            }

            sb.Append("</ul>\n");

            return sb.ToString();
        }

        // Render any value recursively
        private static string RenderValue(dynamic value, bool renderUl = false, string ulStyle = "style='margin: 0px; padding-left: 0px;'", HashSet<object> visited = null)
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
                // read by reflection: dynamic cannot see Key and Value when the pair's types are not public
                return $"<strong>{PairMember((object)value, "Key")}</strong>: {RenderValue(PairMember((object)value, "Value"), true, ulStyle, visited)}";
            }
            // Tuple (special case)
            else if (IsTupleType(valueType))
            {
                if (true == renderUl)
                    valueSb.Append($"<ul id=rvtuple{idCnt++} {ulStyle}>\n");

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
            else if (true == isNull)
                return HttpUtility.HtmlEncode("null");
            // an xml element that holds only its text, such as <Name>Al</Name>, is that text
            else if (value is Dictionary<object, dynamic> textElement && 1 == textElement.Count && true == textElement.ContainsKey("#text"))
            {
                valueSb.Append(RenderValue(textElement["#text"], renderUl, ulStyle, visited));
            }
            // Nested Dictionary<object, dynamic> -> full recursive call
            else if (value is Dictionary<object, dynamic> nestedDict)
            {
                valueSb.Append(RenderFullObjectToHtml(nestedDict, renderUl, false));
            }
            // IEnumerable (list/array)
            else if (true == IsEnumerableObject(value) && !(value is string))
            {
                if (true == renderUl)
                    valueSb.Append($"<ul id=rvenum{idCnt++} {ulStyle}>\n");

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
                    valueSb.Append($"<ul id=rvstruct{idCnt++} {ulStyle}>\n");

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
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Render [{valueType}] using ValueToString", (object)value, GPALObjectType.Other);
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
                            sb.Append($" {attribute.Key}=\"{ValueToString(attribute.Value)}\"");
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
                // text beside elements, such as the x of <p>x<br></p>, which the html reader keeps as {tag: #text, value: x}
                else if (dictionary.ContainsKey("value"))
                {
                    sb.Append(ValueToString(dictionary["value"]));
                }

                // a text null, which json and yaml read as a null, is written as the text null
                if (dictionary.ContainsKey("text"))
                {
                    sb.Append(ValueToString(dictionary["text"]));
                }

                // the html reader's children, or the list json and yaml read them back into; one child alone, as a page read
                // from xml writes one, is that child's record
                if (dictionary.ContainsKey("children") && dictionary["children"] is IEnumerable heldChildren)
                {
                    IEnumerable childItems = heldChildren is IDictionary oneChild && true == oneChild.Contains("tag") ? new List<object> { oneChild } : heldChildren;
                    bool afterElement = false;
                    foreach (Dictionary<object, dynamic> child in childItems.OfType<Dictionary<object, dynamic>>())
                    {
                        // text right after an element follows its closing tag with no line break, which would show as a
                        // space, so <b>x</b>: stays as written
                        bool textChild = child.ContainsKey("tag") && "#text" == ((object)child["tag"])?.ToString();
                        if (true == textChild && true == afterElement && 0 < sb.Length && '\n' == sb[sb.Length - 1])
                            sb.Length--;
                        int lengthBefore = sb.Length;

                        if (false == child.ContainsKey("#text"))
                        {
                            string text = ConvertDictionaryToHtml(child);
                            if (0 < text.Length)
                                sb.Append(text);
                        }
                        else
                            sb.Append(child["text"]);

                        // a child that wrote nothing, such as an empty record, leaves what was written before it as it was
                        if (lengthBefore < sb.Length)
                            afterElement = false == textChild;
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
                        string toRender = RenderValue(item, true, "style='margin: 8px; padding-left: 8px;'");

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

        // How many elements deep html and xml are read: each element is two levels of the dictionary, its record and its
        // list of children, and the deepest element's attributes or text and comment records add one more, so html or xml
        // read to this depth is within NestingLimit when written to json or yaml
        internal const int HtmlDepthLimit = NestingLimit / 2 - 1;

        /// <summary>
        ///
        /// </summary>
        /// <param name="inputDataOrFilename"></param>
        /// <param name="isChildren">true if we are processing child records, so we want to keep #text tags</param>
        /// <param name="depth">how many elements deep these nodes are, 0 for the top</param>
        /// <param name="notAllRead">set true when elements deeper than HtmlDepthLimit are left out; the top call creates it</param>
        /// <returns></returns>
        public static List<Dictionary<object, dynamic>> ConvertHtmlToDictionary(dynamic inputDataOrFilename, bool isChildren = false, int depth = 0, StrongBox<bool> notAllRead = null)
        {
            if (null == notAllRead)
                notAllRead = new StrongBox<bool>(false);

            var dictionaries = new List<Dictionary<object, dynamic>>();
            HtmlNodeCollection nodes = null;

            if (inputDataOrFilename is HtmlNodeCollection)
                nodes = inputDataOrFilename;
            else
                nodes = GetHtmlNodes(inputDataOrFilename);

            if (null == nodes)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No html nodes found. Dictionary will be empty", (object)inputDataOrFilename, GPALObjectType.Converter);
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

                // Handle the node's text content directly. The children are checked first, as InnerText reads every
                // node under this one, and on html thousands of elements deep that overflows the stack
                if (!node.HasChildNodes && !string.IsNullOrEmpty(node.InnerText.Trim()))
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
                        else if (HtmlDepthLimit <= depth + 1)
                        {
                            // the child and everything under it is left out
                            notAllRead.Value = true;
                        }
                        else
                        {
                            var childDictionaries = ConvertHtmlToDictionary(new HtmlNodeCollection(null) { childNode }, true, depth + 1, notAllRead);
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

            if (0 == depth && true == notAllRead.Value)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Html is nested deeper than [{HtmlDepthLimit}] elements, so the elements below that depth are not read", (object)inputDataOrFilename, GPALObjectType.Converter);

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
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Expected html string or GPALFile, got [{htmlorFilename.GetType()}]", (object)htmlorFilename, GPALObjectType.Other, ex);
                return null;
            }

        }
    }
}
