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
                        // an empty value gets no text node and is written <b />; an empty text node makes the indented
                        // writer put </b> on a line of its own
                        string text = ValueToString(kvp.Value);
                        if (0 < text.Length)
                            currentParent.InnerText = text;
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
                    // as an item holding a Key and a Value, as JSON writes it. So is a key with a converter to and from
                    // text, such as a Point, whose text, "1, 2", is no element name, and a struct of the program's own
                    object rawKey = kvp.Key;
                    if (null != rawKey && (false == IsSimpleType(rawKey.GetType()) || true == IsTextStructType(rawKey.GetType()) || true == IsTextClassType(rawKey.GetType())
                        || true == IsMemberStruct(rawKey.GetType())))
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
                || IsComplexType(objType) || IsTextStructType(objType)))
            {
                if (currentParent != null)
                {
                    // an empty value gets no text node and is written <b />; an empty text node makes the indented
                    // writer put </b> on a line of its own
                    string text = ValueToString(obj);
                    if (0 < text.Length)
                        currentParent.InnerText = text;
                }
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

            // Case 1: List of children (common for "children" array), or records keyed by place, GPALKEY0000 and so on, as
            // json and yaml read a list of them back
            IEnumerable enumerable = obj is IDictionary numberedRecords && 0 < numberedRecords.Count
                    && true == numberedRecords.Keys.Cast<object>().All(key => true == Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                ? numberedRecords.Values
                : obj is IEnumerable listed && !(obj is string) && !(obj is IDictionary)
                ? listed
                : null;
            if (null != enumerable)
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
                // a comment, or text beside elements such as the x of <p>x<br></p>, which the html reader keeps as
                // {tag: #text, value: x}; the text goes in as text, where it became an empty element named __text
                if (obj.TryGetValue("tag", out tagObj) && ("#comment".Equals(tagObj?.ToString()) || "#text".Equals(tagObj?.ToString())))
                {
                    string commentText = "";
                    object valueObj;
                    if (obj.TryGetValue("value", out valueObj))
                    {
                        commentText = ValueToString(valueObj);
                    }

                    // a real XML comment, or a text node
                    if ("#comment".Equals(tagObj?.ToString()))
                        parent.AppendChild(doc.CreateComment(SanitizeComment(commentText)));
                    else
                        parent.AppendChild(doc.CreateTextNode(System.Net.WebUtility.HtmlDecode(commentText)));   // the reader keeps the html as written, &amp; and all; the xml writer encodes it again
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
                // a null text, as json and yaml read the text null of a page, is written as the text null, as xml writes a null
                else if (key == "text")
                    textContent = null == entry.Value ? "null" : entry.Value.ToString();
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
                element.AppendChild(doc.CreateTextNode(System.Net.WebUtility.HtmlDecode(textContent)));   // decoded, as for text beside tags above
            }

            // Children
            if (children != null)
            {
                ConvertHtmlDictionaryToXml(children, element);
            }

            parent.AppendChild(element);
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

            // A name that does not start with a letter has a _ put before it, so 5 is written _5 and _5 is written __5; the
            // reader takes one _ off a name whose next character is not a letter, so both come back as they were, and a
            // name such as _tag is kept. A character a name cannot hold, such as a space or a colon, is written as xml
            // escapes it, _x0020_ for a space: 5 doubles is written _5_x0020_doubles
            string sanitized = XmlConvert.EncodeLocalName(0 < key.Length && false == char.IsLetter(key[0]) ? "_" + key : key);

            // If empty after sanitizing (e.g. original was "123" > "" > needs fallback)
            if (string.IsNullOrEmpty(sanitized))
                return "item";

            return sanitized;
        }
        // The tags of html elements, as the html reader gives them, in lower case, and those of them only markup uses, which
        // a page held in xml is known by. A member of a class, written as declared, such as Title or Body, is not a tag; a
        // name data holds too, such as a, b, p, table or body, is not one only markup uses
        private static readonly HashSet<string> htmlTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "html", "head", "body", "title", "meta", "link", "style", "script", "noscript", "base", "div", "span", "p", "br", "hr",
            "pre", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6", "a", "img", "b", "i", "u", "s", "em", "strong", "small", "sub",
            "sup", "code", "kbd", "samp", "var", "mark", "abbr", "cite", "q", "dfn", "time", "ins", "del", "wbr", "ul", "ol", "li",
            "dl", "dt", "dd", "table", "caption", "colgroup", "col", "thead", "tbody", "tfoot", "tr", "th", "td", "form", "label",
            "input", "button", "select", "option", "optgroup", "textarea", "fieldset", "legend", "nav", "section", "article",
            "header", "footer", "main", "aside", "figure", "figcaption", "picture", "source", "video", "audio", "track", "canvas",
            "iframe", "embed", "object", "param", "svg", "details", "summary", "dialog", "template", "address", "center", "font"
        };
        // A tag only markup uses, with the tags it sits in on a page; one that sits anywhere has none. A list item is markup
        // in a list, a row in a table and a cell in a row, so a member of data named li or tr is not
        private static readonly Dictionary<string, string[]> markupTags = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["div"] = new string[0], ["h1"] = new string[0], ["h2"] = new string[0], ["h3"] = new string[0], ["h4"] = new string[0],
            ["h5"] = new string[0], ["h6"] = new string[0], ["li"] = new[] { "ul", "ol" }, ["thead"] = new[] { "table" },
            ["tbody"] = new[] { "table" }, ["tr"] = new[] { "table", "thead", "tbody", "tfoot" }, ["td"] = new[] { "tr" }, ["th"] = new[] { "tr" }
        };

        /// <summary>
        /// Returns true when data holds the records of a page, as the html reader makes them and json and yaml read them
        /// back: one record, a list of them or records keyed by place, each of a tag that is an html tag, #text or #comment,
        /// of no key but tag, attributes, text and children, and value for #text and #comment, with children that are such
        /// records too, and one of them holding children or attributes, as a page's do.
        /// </summary>
        /// <param name="data">The data read</param>
        /// <returns>True for the records of a page, so xml is written as the page</returns>
        internal static bool HoldsPageRecords(object data)
        {
            HashSet<string> elementKeys = new HashSet<string>(StringComparer.Ordinal) { "tag", "attributes", "text", "children" };
            HashSet<string> nodeKeys = new HashSet<string>(StringComparer.Ordinal) { "tag", "value" };
            bool IsPageRecord(object candidate)
            {
                string tag = candidate is IDictionary tagged && true == tagged.Contains("tag") ? tagged["tag"]?.ToString() ?? string.Empty : null;
                bool isNode = "#text" == tag || "#comment" == tag;
                return candidate is IDictionary record && null != tag && (true == isNode || true == htmlTags.Contains(tag))
                    && true == record.Keys.Cast<object>().All(key => true == (true == isNode ? nodeKeys : elementKeys).Contains(key?.ToString() ?? string.Empty))
                    && (false == record.Contains("children") || (record["children"] is IDictionary oneChild && true == IsPageRecord(oneChild))
                        || (record["children"] is IList children && true == children.Cast<object>().All(child => true == IsPageRecord(child))));
            }
            IDictionary numbered = data as IDictionary;
            List<object> records = true == IsPageRecord(data)
                ? new List<object> { data }
                : null != numbered && 0 < numbered.Count && true == numbered.Keys.Cast<object>().All(key => true == Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$"))
                ? numbered.Values.Cast<object>().ToList()
                : data is IList listed && false == data is string
                ? listed.Cast<object>().ToList()
                : new List<object>();

            return 0 < records.Count && true == records.All(record => true == IsPageRecord(record))
                && true == records.Any(record => record is IDictionary element && (true == element.Contains("children") || true == element.Contains("attributes")));
        }

        internal static Dictionary<object, dynamic> ConvertXMLToDictionary(XmlDocument xmlDoc)
        {
            xmlItemCount = 0;
            var rootDict = new Dictionary<object, dynamic>();

            // A page held in xml is read as the html reader reads the page, so it is written back to html as the page and
            // not as data: a document element html, or elements under GPAL's own <root>, as an html file written to xml holds
            // them, that are all html tags and are markup: one holds an attribute, has text beside an element, or is a tag
            // only markup uses where markup puts it, such as a div, or an li in a ul
            List<XmlElement> inDocument = xmlDoc.DocumentElement.SelectNodes(".//*").OfType<XmlElement>().ToList();
            bool isPage = "html" == xmlDoc.DocumentElement.Name
                || ("root" == xmlDoc.DocumentElement.Name && 0 < inDocument.Count && true == inDocument.All(element => true == htmlTags.Contains(element.Name))
                    && true == inDocument.Any(element => 0 < element.Attributes.Count
                        || (true == markupTags.TryGetValue(element.Name, out string[] markupParents)
                            && (0 == markupParents.Length || true == markupParents.Contains(element.ParentNode?.Name)))
                        || (true == element.ChildNodes.OfType<XmlElement>().Any()
                            && true == element.ChildNodes.OfType<XmlText>().Any(text => 0 < text.Value.Trim().Length))));
            // set when an element is nested deeper than HtmlDepthLimit, as html is read, and is left out
            StrongBox<bool> notAllRead = new StrongBox<bool>(false);
            List<XmlElement> pageElements = "html" == xmlDoc.DocumentElement.Name
                ? new List<XmlElement> { xmlDoc.DocumentElement }
                : xmlDoc.DocumentElement.ChildNodes.OfType<XmlElement>().ToList();

            // one element is its record, two or more their records in order, as a list is held
            if (true == isPage && 1 == pageElements.Count)
                rootDict = HtmlRecordOfXml(pageElements[0], 0, notAllRead);
            else if (true == isPage)
                for (int pageIdx = 0; pageIdx < pageElements.Count; pageIdx++)
                    rootDict[$"GPALKEY{pageIdx:D4}"] = HtmlRecordOfXml(pageElements[pageIdx], 0, notAllRead);
            else
            {
                // Use the actual root element name as the key
                // the root's name is read as the other names are, its _ before a name not starting with a letter taken off
                string rootName = XmlConvert.DecodeName(xmlDoc.DocumentElement.Name);  // "catalog"
                if (1 < rootName.Length && '_' == rootName[0] && false == char.IsLetter(rootName[1]))
                    rootName = rootName.Substring(1);

                // Convert the document element (the real root) to its dictionary
                dynamic contentDict = ConvertXmlNodeToDictionary(xmlDoc.DocumentElement, 0, notAllRead);

                // A document element holding nothing, such as the <root /> an empty list or an empty object is written as,
                // is an empty document: no fields and no rows, as the JSON {} is, not one empty value
                if (1 == contentDict.Count && true == contentDict.ContainsKey("#text") && string.Empty == contentDict["#text"])
                    contentDict = new Dictionary<object, dynamic>();

                // Put it under the original root name
                rootDict[rootName] = contentDict;
            }

            if (true == notAllRead.Value)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Xml is nested deeper than [{HtmlDepthLimit}] elements, so the elements below that depth are not read");

            return rootDict;
        }

        /// <summary>
        /// Returns the record of an element of a page held in xml, as ConvertHtmlToDictionary makes it from the page.
        /// </summary>
        /// <remarks>
        /// The record holds the element's tag, its attributes, its text when text is all it holds, else its children: each
        /// element a record, text beside them a #text record of its value and a comment a #comment record. Text of spaces
        /// alone between elements is left out, as the html reader leaves it out.
        /// </remarks>
        /// <param name="element">The element</param>
        /// <param name="depth">How many elements deep the element is, 0 for the top one</param>
        /// <param name="notAllRead">set true when an element deeper than HtmlDepthLimit is left out</param>
        /// <returns>Its record</returns>
        private static Dictionary<object, dynamic> HtmlRecordOfXml(XmlElement element, int depth, StrongBox<bool> notAllRead)
        {
            Dictionary<object, dynamic> record = new Dictionary<object, dynamic> { ["tag"] = element.Name };
            if (0 < element.Attributes.Count)
                record["attributes"] = element.Attributes.Cast<XmlAttribute>().ToDictionary(attribute => attribute.Name, attribute => attribute.Value);

            // text as the html reader keeps it, as written in the page: the xml writer writes a page's text decoded, so &, <,
            // > and " are written as &amp;, &lt;, &gt; and &quot; again, as the html writer writes them. An attribute is
            // written as the reader keeps it, and read so
            string AsPageText(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

            List<XmlNode> nodes = element.ChildNodes.Cast<XmlNode>().ToList();
            bool textAlone = 0 < nodes.Count && true == nodes.All(node => node is XmlCharacterData && false == node is XmlComment);
            if (true == textAlone && 0 < element.InnerText.Trim().Length)
                record["text"] = AsPageText(element.InnerText.Trim());
            else if (false == textAlone)
            {
                List<Dictionary<object, dynamic>> children = new List<Dictionary<object, dynamic>>();
                foreach (XmlNode node in nodes)
                {
                    // an element deeper than html is read is left out, with everything under it
                    if (node is XmlElement && HtmlDepthLimit <= depth + 1)
                        notAllRead.Value = true;
                    else if (node is XmlElement childElement)
                        children.Add(HtmlRecordOfXml(childElement, depth + 1, notAllRead));
                    else if (node is XmlComment)
                        children.Add(new Dictionary<object, dynamic> { ["tag"] = "#comment", ["value"] = node.Value });
                    else if (0 < (node.Value ?? string.Empty).Trim().Length)
                        children.Add(new Dictionary<object, dynamic> { ["tag"] = "#text", ["value"] = AsPageText(node.Value) });
                }
                if (0 < children.Count)
                    record["children"] = children;
            }

            return record;
        }
        // The record of an xml element: depth is how many elements deep it is, 0 for the document element, and notAllRead
        // is set true when a child element deeper than HtmlDepthLimit is left out, with everything under it, as html is
        // read: each element is two levels of the dictionary, its record and the list it is held in, so xml read to that
        // depth is within NestingLimit
        private static Dictionary<object, dynamic> ConvertXmlNodeToDictionary(XmlNode xmlNode, int depth, StrongBox<bool> notAllRead)
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
                var textPieces = new List<string>(); // every text and CDATA piece, in order, such as the t and u of <b>t<c/>u</b>
                var writtenPieces = new List<string>(); // the same pieces as written, spaces at their ends kept
                bool childLeftOut = false; // a child element is deeper than HtmlDepthLimit and left out, so this is not text alone

                foreach (XmlNode childNode in xmlNode.ChildNodes)
                {
                    if (childNode.NodeType == XmlNodeType.Element && HtmlDepthLimit <= depth + 1)
                    {
                        notAllRead.Value = true;
                        childLeftOut = true;
                    }
                    else if (childNode.NodeType == XmlNodeType.Element)
                    {
                        var childDict = ConvertXmlNodeToDictionary(childNode, depth + 1, notAllRead);
                        // a name written with xml's escapes, such as edges_x0020_high, is read as the key it was, edges high,
                        // and the _ put before a name that does not start with a letter, such as _5 for 5, is taken off
                        string elementName = XmlConvert.DecodeName(childNode.Name);
                        if (1 < elementName.Length && '_' == elementName[0] && false == char.IsLetter(elementName[1]))
                            elementName = elementName.Substring(1);
                        string name = $"GPAL{xmlItemCount++.ToString("D3")}_" + elementName;

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
                        {
                            textPieces.Add(trimmed);
                            writtenPieces.Add(raw);
                        }
                        else if (raw.Length > 0)
                            rawWhitespaceText = raw; // may be a meaningful space char; keep if leaf
                    }
                    else if (XmlNodeType.CDATA == childNode.NodeType && 0 < childNode.Value.Length)
                    {
                        // CDATA text is kept exactly as written
                        textPieces.Add(childNode.Value);
                        writtenPieces.Add(childNode.Value);
                    }
                }
                // an element holding only text keeps it as written, so "  a b  " keeps its spaces, but for the line break
                // and indent around it of an element written on lines of its own, <b> then the text then </b>; the text
                // pieces of mixed content, such as <b>t<c/>u</b>, are joined with a space
                if (0 < textPieces.Count && 0 == childGroups.Count && false == childLeftOut)
                {
                    textContent = string.Concat(writtenPieces);
                    System.Text.RegularExpressions.Match laidOut = Regex.Match(textContent, @"^\r?\n[ \t]*(.*?)\r?\n[ \t]*$", RegexOptions.Singleline);
                    if (true == laidOut.Success)
                        textContent = laidOut.Groups[1].Value;
                }
                else if (0 < textPieces.Count)
                    textContent = string.Join(" ", textPieces);
                // Preserve whitespace-only text only in leaf elements (no child elements).
                // In parent elements the whitespace is just pretty-print indentation.
                // A line break and its indent are the layout of an empty value written indented, <b> then </b> on the next
                // line, so they read as an empty value; a space or a tab alone is kept
                if (textContent == null && rawWhitespaceText != null && childGroups.Count == 0 && false == childLeftOut)
                    textContent = rawWhitespaceText.Contains("\n") ? string.Empty : rawWhitespaceText;
                // an element holding nothing, such as <a/> or <b></b>, is an empty value
                else if (null == textContent && 0 == childGroups.Count && 0 == dict.Count && false == childLeftOut)
                    textContent = string.Empty;

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
    }
}
