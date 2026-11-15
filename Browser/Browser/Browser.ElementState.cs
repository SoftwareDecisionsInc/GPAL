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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// <summary>
        /// Hides the element(s) found for <paramref name="selector"/> (or the current selector(s) if none is
        /// given) by setting their CSS display to none.
        /// </summary>
        /// <param name="selector">Optional selector to add to the current unit of work before hiding</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Hide(Selector selector = null)
        {
            if (null != selector)
                WithSelector(selector);

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.Hide(this);

            return this;
        }
        /// <summary>
        /// Name of the attribute named by the most recent <see cref="WithAttribute"/> call, pending its <see cref="SetAttribute(string)"/>.
        /// </summary>
        private string _pendingSetAttributeName;

        /// <summary>
        /// Names the DOM attribute to set on the current selector(s). Must be followed by
        /// <see cref="SetAttribute(string)"/> to supply the value and complete the action. See
        /// <see cref="IAllowSetAttributeValue.SetAttribute"/> for the distinction between this and SendString/FillIn*.
        /// </summary>
        /// <param name="attribute">Name of the attribute to set (e.g. "disabled", "value", "data-foo")</param>
        /// <returns>Fluent interface; must be followed by SetAttribute(value) to complete the action</returns>
        public IAllowSetAttributeValue WithAttribute(string attribute)
        {
            _pendingSetAttributeName = attribute;
            return this;
        }
        /// <summary>
        /// Assigns <paramref name="value"/> to the attribute named by the preceding <see cref="WithAttribute"/> call,
        /// completing the SetAttribute action.
        /// </summary>
        /// <param name="value">Value to assign to the attribute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SetAttribute(string value)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.SetAttribute(this, _pendingSetAttributeName, value);

            return this;
        }
        /// <summary>
        /// Gets the current page's HTML source, cleaned of escape sequences and control characters.
        /// </summary>
        /// <param name="pageSource">Receives the cleaned page source HTML</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetPageSource(out string pageSource)
        {
            pageSource = string.Empty;

            if (true == UseOttoMagic)
            {
                pageSource = MagicHelper.GetPageSource();
            }
            else if (true == UsePuppeteer)
            {
                pageSource = PuppeteerClient.GetPageSource().Execute();
            }
            else
            {
                try
                {
                    pageSource = BrowserDriver.PageSource;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        "Failed to read page source in Selenium [" + ex.Message + "]",
                        this,
                        GPALObjectType.Browser);
                }
            }

            pageSource = NormalizePageSource(pageSource);

            Uri currUrl = new Uri(CurrentUrl, UriKind.Absolute);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Retrieved page source for [{UrlHelper.NormalizePath(currUrl.AbsolutePath)}]", this, GPALObjectType.Browser);

            return this;
        }
        /// <summary>
        /// Converts any engine's raw page source to the page's real source: cleaned HTML for a normal page. For a
        /// file such as XML, Chrome's XML viewer wraps it, so this returns the original text the viewer keeps in a
        /// hidden div.
        /// </summary>
        /// <param name="raw">The raw page source as returned by the active engine (OttoMagic, Puppeteer, or Selenium)</param>
        /// <returns>The normalized page source, or an empty string if <paramref name="raw"/> is null/whitespace</returns>
        private static string NormalizePageSource(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // JS/JSON-unescape first so the XML-viewer marker (real quotes) is detectable
            // regardless of how the engine transported the source (e.g. OttoMagic returns it escaped).
            string working = SafeJsUnescape(raw.Trim().Trim('\"').Trim('\''));

            if (working.IndexOf("webkit-xml-viewer-source-xml", StringComparison.Ordinal) != -1)
            {
                string fileSource = ConverterHelper.ExtractRawXmlFromBrowserSource(working);
                if (!string.IsNullOrWhiteSpace(fileSource))
                {
                    // Strip control characters only; do NOT HTML-decode, that would corrupt escaped
                    // entities (e.g. &amp;) that are a genuine part of the recovered file source.
                    return Regex.Replace(fileSource.Trim(), @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
                }
            }

            // Ordinary page: return the page source with the standard cleanup.
            return SafeCleanPageSource(raw);
        }
        /// <summary>
        /// Trims, HTML-decodes, JS-unescapes, and strips control characters from raw page source.
        /// </summary>
        /// <param name="input">The raw page source to clean</param>
        /// <returns>The cleaned page source, or an empty string if <paramref name="input"/> is null/whitespace</returns>
        private static string SafeCleanPageSource(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string working = input.Trim().Trim('\"').Trim('\'');

            // Peel the transport's JSON-string escaping FIRST. OttoMagic sends outerHTML as a JSON string
            // (leading quote, \" for attribute quotes, \n for newlines); HtmlDecode must run AFTER this,
            // because decoding entities injects raw quotes that make the still-escaped payload un-peelable.
            working = SafeJsUnescape(working);

            working = System.Web.HttpUtility.HtmlDecode(working);

            // Remove control characters only
            working = Regex.Replace(working, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

            return working.Trim();
        }

        /// <summary>
        /// Unescapes common JS/JSON string escape sequences (quotes, backslashes, unicode) while preserving newline/carriage-return escapes.
        /// </summary>
        /// <param name="input">The escaped string to unescape</param>
        /// <returns>The unescaped string, or <paramref name="input"/> unchanged if null/empty</returns>
        private static string SafeJsUnescape(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Step 1: OttoMagic's native messaging hands the source over as one JSON string (leading quote, \"
            // for attribute quotes, \n for newlines), so that one layer is decoded with a real parser. Selenium
            // and Puppeteer return clean HTML, which is not a JSON string, so the decode leaves it as it is.
            string result = TryJsonStringDecode(input) ?? input;

            // Step 2: ALWAYS run the lenient character-level unescape on the result, so every engine converges
            // on identical output. Without this, OttoMagic (whose peel succeeds) would keep the single-level \"
            // left in the page's own embedded strings (e.g. __NEXT_DATA__ JSON), while Selenium/Puppeteer
            // (whose peel is a no-op) collapse those \" here - producing different source for the same page.

            // Basic escapes - but PROTECT newlines and carriage returns so they stay escaped for JSON
            result = result.Replace(@"\""", "\"")
                           .Replace(@"\'", "'")
                           .Replace(@"\\", "\\")     // keep single backslash
                           .Replace(@"\/", "/");

            // Safe unicode only
            result = Regex.Replace(result, @"\\u([0-9a-fA-F]{4})", m =>
            {
                try
                {
                    int code = int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                    if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
                        return m.Value;        // keep original if invalid
                    return char.ConvertFromUtf32(code);
                }
                catch
                {
                    return m.Value;
                }
            });

            // Remove unknown escapes (but protect " \ ' \ / \b \f \t)
            result = Regex.Replace(result, @"\\([^""'\\/bfnrtu])", "$1");

            return result;
        }

        /// <summary>
        /// Treats the argument as the body of a JSON string literal and unescapes exactly one layer using a
        /// real JSON parser (correctly handling \\, \", \/, \uXXXX and friends). Returns null when the text is
        /// not a well-formed single JSON string, for example clean HTML holding a raw quote or newline.
        /// </summary>
        /// <param name="s">The candidate JSON-string body (without the surrounding quotes)</param>
        /// <returns>The one-layer-unescaped string, or null if it is not a valid JSON string token</returns>
        private static string TryJsonStringDecode(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;

            try
            {
                return JsonConvert.DeserializeObject("\"" + s + "\"") as string;
            }
            catch
            {
                return null;
            }
        }
    }
}
