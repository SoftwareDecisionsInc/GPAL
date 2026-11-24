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
        /// Maps a single delimiter character to its corresponding <see cref="DataFormat"/> (e.g. ',' to CSV, '\t' to TAB).
        /// Returns CUSTOM_DELIMITER for any character not recognized as a known delimiter.
        /// </summary>
        /// <param name="delimiter">The delimiter character to map</param>
        /// <returns>The DataFormat corresponding to the delimiter</returns>
        internal static DataFormat GetDataFormat(char delimiter)
        {
            switch (delimiter)
            {
                case ('^'):
                    return DataFormat.CARET;
                case (':'):
                    return DataFormat.COLON;
                case (','):
                    return DataFormat.CSV;
                case ('.'):
                    return DataFormat.DOT;
                case ('-'):
                    return DataFormat.HYPHEN;
                case ('|'):
                    return DataFormat.PIPE;
                case (' '):
                    return DataFormat.SPACE;
                case (';'):
                    return DataFormat.SEMICOLON;
                case ('\t'):
                    return DataFormat.TAB;
                default:
                    return DataFormat.CUSTOM_DELIMITER;
            }
        }

        internal static class DataFormatDetector
        {
            // Helper to detect the best DataFormat from input string.
            // Focuses on XML, JSON, YAML, CSV first, then others via heuristics.
            // Never throws - returns NOTSET on failure, publishes GPAL warnings.
            // Assumptions: Trims input, checks startsWith/patterns, tries light parsing.
            // For delimiters (CSV, TAB, etc.): Checks first few lines for consistent column count.
            // Add NuGets if needed: Newtonsoft.Json (JSON), YamlDotNet (YAML full parse - optional here).
            /// <summary>
            /// Inspects a raw input string and guesses its <see cref="DataFormat"/> (XML, JSON, YAML, HTML, PDF, a delimited
            /// format, DATABASE, CLASS, or DICTIONARY), falling back to STRING if nothing matches. Never throws; publishes a
            /// GPAL warning/debug event and returns NOTSET/STRING on ambiguous or empty input.
            /// </summary>
            /// <param name="input">The raw input string to inspect</param>
            /// <returns>The detected DataFormat, or STRING if no specific format could be determined</returns>
            public static DataFormat DetectFormat(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.WARNING,
                        "DetectFormat called with empty/null input. Returning NOTSET.",
                        typeof(DataFormatDetector),
                        GPALObjectType.Other);

                    return DataFormat.NOTSET;
                }

                string trimmed = input.Trim();

                // Early check: is this likely base64-encoded data?
                //if (IsLikelyBase64(trimmed))
                //{
                //    return DataFormat.BASE64;
                //}

                string firstLine = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                // a full HTML document, one that starts with <!DOCTYPE html or an <html> element, is HTML even when it
                // parses as XML; a fragment such as a bare <table> is read as XML
                bool isHtmlDocument = trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(trimmed, @"^<html[\s>]", RegexOptions.IgnoreCase);

                // XML: Starts with <, valid tags (light check)
                if (false == isHtmlDocument && trimmed.StartsWith("<") && IsLikelyXml(trimmed))
                {
                    return DataFormat.XML;
                }

                // JSON: Starts with { or [, try parse
                if ((trimmed.StartsWith("{") || trimmed.StartsWith("[")) && IsValidJson(trimmed))
                {
                    return DataFormat.JSON;
                }

                // YAML: Key: value patterns, no {/[ start, try basic check (full parse if YamlDotNet added)
                if (!trimmed.StartsWith("{") && !trimmed.StartsWith("[") && !trimmed.StartsWith("<") && IsLikelyYaml(trimmed))
                {
                    return DataFormat.YAML;
                }

                // HTML: Starts with <html> or <!DOCTYPE html> or has <body> etc.
                if (true == isHtmlDocument || Regex.IsMatch(trimmed, @"<body|</body|<head|</head", RegexOptions.IgnoreCase))
                {
                    return DataFormat.HTML;
                }

                //// post-v1: PDF
                //// PDF: Starts with %PDF- (binary-ish, but string might be base64 or partial)
                //if (trimmed.StartsWith("%PDF-"))
                //{
                //    return DataFormat.PDF;
                //}

                // Tabular/Delimited: TAB, CSV, SEMICOLON, PIPE, CARET, PRN (space)
                DataFormat delimFormat = DetectDelimiterFormat(trimmed);
                if (delimFormat != DataFormat.NOTSET)
                {
                    return delimFormat;
                }

                // Others: DATABASE (SQL dump?), GRID (maybe table-like), DICTIONARY (key=value?), CLASS (C# code?), STRING (fallback)
                // For now, fallback to STRING if none match
                if (Regex.IsMatch(firstLine, @"^\s*CREATE TABLE|SELECT|INSERT|UPDATE", RegexOptions.IgnoreCase))
                {
                    return DataFormat.DATABASE;
                }
                else if (Regex.IsMatch(trimmed, @"class\s+\w+\s*{|public\s+class", RegexOptions.IgnoreCase))
                {
                    return DataFormat.CLASS;
                }
                else if (IsKeyValueLike(trimmed, "="))
                {
                    return DataFormat.DICTIONARY;
                }

                GPAL.PublishSimpleEvent(
                    GPALEventType.DEBUG,
                    "No clear DataFormat detected for input starting with [" + firstLine.Substring(0, Math.Min(50, firstLine.Length)) + "]. Returning STRING.",
                    input,
                    GPALObjectType.Other);

                return DataFormat.STRING;
            }

            // Returns true if the string strongly resembles base64 encoding
            /// <summary>
            /// Returns true if the given string strongly resembles base64-encoded data (only base64 characters,
            /// plausible length/padding, and sufficient character variety).
            /// </summary>
            /// <param name="s">The string to inspect</param>
            /// <returns>True if the string looks like base64-encoded data</returns>
            private static bool IsLikelyBase64(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                if (s.Length < 12) return false;  // too short to be useful base64

                string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

                // must contain only base64 characters
                foreach (char c in s)
                {
                    if (alphabet.IndexOf(c) == -1)
                    {
                        return false;
                    }
                }

                // length should be multiple of 4 (or close, allowing for missing padding)
                int mod = s.Length % 4;
                if (mod != 0)
                {
                    // accept common unpadded or incorrectly padded cases
                    if (mod != 2 && mod != 0)
                    {
                        return false;
                    }
                }

                // rough entropy check: base64 is never all same character or very low variety
                // (this is optional but helps reject garbage)
                int unique = new HashSet<char>(s).Count;
                if (unique < 5 && s.Length > 20)
                {
                    return false;
                }

                return true;
            }
            /// <summary>
            /// Returns true if the given string can be parsed as XML (a light, full-document XmlReader pass).
            /// </summary>
            /// <param name="input">The string to inspect</param>
            /// <returns>True if the input parses successfully as XML</returns>
            private static bool IsLikelyXml(string input)
            {
                try
                {
                    using (var reader = XmlReader.Create(new StringReader(input)))
                    {
                        while (reader.Read()) { } // Light parse
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Returns true if the given string can be parsed as JSON.
            /// </summary>
            /// <param name="input">The string to inspect</param>
            /// <returns>True if the input parses successfully as JSON</returns>
            private static bool IsValidJson(string input)
            {
                try
                {
                    JsonConvert.DeserializeObject(input, new JsonSerializerSettings { MaxDepth = null }); // Or use System.Text.Json.JsonDocument.Parse(input)
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Returns true if the given string looks like YAML, based on a heuristic count of "key: value", indented,
            /// and "- " list-item lines. Two lines are YAML when each is one of those, one is a "key: value" or each is a
            /// "- " item, and YamlDotNet reads them as a mapping or a list with no key given twice.
            /// </summary>
            /// <param name="input">The string to inspect</param>
            /// <returns>True if the input has enough YAML-like lines to be treated as YAML</returns>
            private static bool IsLikelyYaml(string input)
            {
                // Basic heuristic: lines with key: value, indents, no XML/JSON starts
                // If YamlDotNet added: try YamlStream.Load(new StringReader(input))
                var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int yamlPatterns = lines.Count(line => Regex.IsMatch(line.Trim(), @"^[\w\-]+:\s*(.+|$)") || line.StartsWith("  ") || line.StartsWith("- "));
                bool retVal = 2 < yamlPatterns && 1 < lines.Length; // Threshold

                // a short document, two lines as a: 1 and b: 2 or - 1 and - 2 are: every line other than a comment or a ---
                // marker is a "key: value" (a space after the colon), a "- " item or indented, and one is a "key: value" or
                // every one is a "- " item
                if (false == retVal && 1 < lines.Length)
                {
                    string keyLine = @"^(- )?[\w\-]+:(\s.*)?$";
                    string[] content = lines.Where(line => false == string.IsNullOrWhiteSpace(line) && false == line.TrimStart().StartsWith("#") && "---" != line.Trim()).ToArray();

                    if (0 < content.Length
                        && true == content.All(line => Regex.IsMatch(line.Trim(), keyLine) || line.StartsWith("  ") || line.StartsWith("- "))
                        && (true == content.Any(line => Regex.IsMatch(line.Trim(), keyLine)) || true == content.All(line => line.StartsWith("- "))))
                    {
                        // a key given twice, as in two log lines starting INFO:, is text, not a mapping
                        try
                        {
                            object parsed = new DeserializerBuilder().WithMaximumRecursion(ConverterHelper.NestingLimit).WithDuplicateKeyChecking().Build().Deserialize<object>(input);
                            retVal = parsed is IDictionary || parsed is IList;
                        }
                        catch (YamlException) { }
                    }
                }

                return retVal;
            }

            /// <summary>
            /// Inspects up to the first 5 non-blank lines of the input for a delimiter that splits every line into the same
            /// number of columns, more than one; a quoted token, as in "Smith, John", keeps the delimiter inside it. A tab
            /// wins outright, on one line too. Otherwise, on two lines or more, the comma, semicolon, pipe or caret giving the
            /// most columns wins, and two giving the same number are too close to call, with a warning naming them, so the
            /// input is read as text, one value per line. A space counts only when nothing else does.
            /// </summary>
            /// <param name="input">The raw input string to inspect</param>
            /// <returns>The detected delimited DataFormat, or NOTSET if none could be determined</returns>
            private static DataFormat DetectDelimiterFormat(string input)
            {
                DataFormat format = DataFormat.NOTSET;
                string[] lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Where(line => false == string.IsNullOrWhiteSpace(line))
                                 .Take(5)
                                 .ToArray();
                Dictionary<char, DataFormat> delimiters = new Dictionary<char, DataFormat>
                {
                    { ',', DataFormat.CSV },
                    { ';', DataFormat.SEMICOLON },
                    { '|', DataFormat.PIPE },
                    { '^', DataFormat.CARET },
                };
                List<char> best = MostColumnsDelimiters(input, out int bestColumns);

                // a tab is rare inside text, and most text has spaces in it
                if (1 < ConsistentColumns(lines, '\t'))
                    format = DataFormat.TAB;
                else if (1 == best.Count)
                    format = delimiters[best[0]];
                else if (1 < best.Count)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "The input splits into [" + bestColumns + "] columns on each of [" + string.Join("] [", best) + "], so no delimiter is chosen for it and it is read as text, one value per line", input, GPALObjectType.Converter);
                else if (1 < lines.Length && 1 < ConsistentColumns(lines, ' '))
                    format = DataFormat.PRN;

                return format;
            }

            /// <summary>
            /// The comma, semicolon, pipe or caret that splits each of the first 5 non-blank lines of the input into the
            /// same number of columns, more than one, giving the most columns; two or more when they give the same number.
            /// One line can be a sentence, as Hello, world is, so it takes two lines to show a delimiter splits them alike,
            /// and one line gives none.
            /// </summary>
            /// <param name="input">The raw input string to inspect</param>
            /// <param name="bestColumns">The number of columns they give, else 1</param>
            /// <returns>The delimiters giving the most columns, or none</returns>
            internal static List<char> MostColumnsDelimiters(string input, out int bestColumns)
            {
                string[] lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Where(line => false == string.IsNullOrWhiteSpace(line))
                                 .Take(5)
                                 .ToArray();
                List<char> best = new List<char>();
                bestColumns = 1;
                foreach (char delimiter in 1 < lines.Length ? new[] { ',', ';', '|', '^' } : new char[0])
                {
                    int columns = ConsistentColumns(lines, delimiter);
                    if (bestColumns < columns)
                    {
                        best = new List<char> { delimiter };
                        bestColumns = columns;
                    }
                    else if (1 < columns && bestColumns == columns)
                        best.Add(delimiter);
                }

                return best;
            }

            /// <summary>
            /// Returns the number of columns every line splits into on the delimiter, a quoted token keeping the delimiter
            /// inside it, or 0 when the lines split into different numbers of columns or there are no lines.
            /// </summary>
            /// <param name="lines">The lines of input to check</param>
            /// <param name="delimiter">The delimiter character to test</param>
            /// <returns>The column count shared by every line, or 0</returns>
            private static int ConsistentColumns(string[] lines, char delimiter)
            {
                List<int> counts = lines.Select(line => ConverterHelper.SplitRespectingQuotes(line, delimiter, false).Count).Distinct().ToList();
                return 1 == counts.Count ? counts[0] : 0;
            }

            /// <summary>
            /// Returns true if more than two lines look like "key&lt;separator&gt;value" pairs (i.e. contain the separator
            /// exactly once).
            /// </summary>
            /// <param name="input">The input string to inspect</param>
            /// <param name="separator">The key/value separator to look for (e.g. "=")</param>
            /// <returns>True if the input looks like key/value pair data</returns>
            private static bool IsKeyValueLike(string input, string separator)
            {
                var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Count(line => line.Contains(separator) && line.Split(separator.ToCharArray()).Length == 2) > 2;
            }
        }
    }
}
