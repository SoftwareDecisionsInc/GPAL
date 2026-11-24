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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Threading;
//using DocumentFormat.OpenXml.Bibliography;
//using DocumentFormat.OpenXml.Drawing;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Keys = OpenQA.Selenium.Keys;
using MatchType = GenerallyPositive.Enums.MatchType;

namespace GenerallyPositive.Browser
{
    internal partial class ElementHelper
    {
        /// <summary>
        /// Offset from the top left of an image match to aim at when no offset is given: the center, moved up to 2
        /// pixels at random. Not used with .WithOffsetX/.WithOffsetY.
        /// </summary>
        /// <param name="size">Size of the matched image</param>
        /// <returns>Offset from the top left of the match</returns>
        private static Point ImageCenterOffset(Size size)
        {
            // never jitter so far that a tiny match is missed
            int jitterX = Math.Max(0, Math.Min(2, (size.Width / 2) - 1));
            int jitterY = Math.Max(0, Math.Min(2, (size.Height / 2) - 1));

            return new Point((size.Width / 2) + Jitter.Next(-jitterX, jitterX + 1),
                             (size.Height / 2) + Jitter.Next(-jitterY, jitterY + 1));
        }
        /// <summary>
        /// The css path of the first element an XPath selector matches, for an action that names its element by css,
        /// such as printing to PDF. Selenium and Puppeteer evaluate the XPath in the page and build the path there;
        /// OttoMagic finds the element and hands back the css path it carries.
        /// </summary>
        /// <param name="browser">Browser to look in</param>
        /// <param name="selector">The XPath selector</param>
        /// <returns>The css path, or null when nothing matches</returns>
        internal static string CssForXPath(Browser browser, Selector selector)
        {
            string retVal = null;
            string xpath = selector.SelectorPaths[0].SelectorPath;

            if (true == browser.UseOttoMagic)
            {
                ReadOnlyCollection<GPALElement> found = FindWebElementsByXPath(browser, selector.SelectorPaths[0], selector);

                retVal = 0 < (found?.Count ?? 0) ? found[0].Css : null;
            }
            else
            {
                // the XPath goes in as a JSON string, so its quotes cannot end the script's own
                string css = BrowserHelper.ExecuteJavaScript(browser, $@"
                    return (function (xpath) {{
                        var el = document.evaluate(xpath, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                        var path = [];

                        while (el && el.nodeType === Node.ELEMENT_NODE) {{
                            if (el.id) {{
                                path.unshift('#' + CSS.escape(el.id));
                                break;
                            }}

                            var part = el.nodeName.toLowerCase(), sibling = el, nth = 1;

                            while ((sibling = sibling.previousElementSibling) != null)
                                if (sibling.nodeName === el.nodeName)
                                    nth++;

                            path.unshift(part + ':nth-of-type(' + nth + ')');
                            el = el.parentElement;
                        }}

                        return path.join(' > ');
                    }})({Newtonsoft.Json.JsonConvert.SerializeObject(xpath)});");

                retVal = false == string.IsNullOrEmpty(css) ? css : null;
            }

            if (null == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"XPath selector [{selector.Name}] matched nothing on the page, so it was left out", browser, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// Css path of the element under an image selector's match, for actions that need css, such as printing to
        /// PDF. Uses <see cref="ElementUnderImage"/>. OttoMagic needs Allow user scripts on; without it this returns
        /// null and the image is skipped with a warning.
        /// </summary>
        /// <param name="browser">Browser to look in</param>
        /// <param name="selector">The image selector</param>
        /// <returns>The css path, or null when the picture is not there, has no element of its own, or cannot be asked</returns>
        internal static string CssUnderImage(Browser browser, Selector selector)
        {
            string retVal = null;
            ReadOnlyCollection<GPALElement> found = new ReadOnlyCollection<GPALElement>(selector.WebSelectorFoundResults);
            GPALElement first = 0 < (found?.Count ?? 0) ? found[0] : null;

            // headless and hidden desktop runs have already resolved the match to its element
            if (null != first && "GPALElement" == first.TagName)
            {
                ReadOnlyCollection<GPALElement> under = ElementUnderImage(browser, first, selector);

                first = 0 < (under?.Count ?? 0) ? under[0] : null;
            }

            if (false == string.IsNullOrEmpty(first?.Css))
                retVal = first.Css;
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Image selector [{selector.Name}] found no element of its own on the page, so it was left out", browser, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// The url for an image match: the nearest enclosing link's href, else the image's src. OttoMagic needs Allow
        /// user scripts on; without it this returns null and <see cref="RunPageScript"/> warns.
        /// </summary>
        /// <param name="match">The image match</param>
        /// <returns>The url, or null when there is none or it cannot be asked</returns>
        internal static string UrlUnderImage(GPALElement match)
        {
            string retVal = null;
            Browser browser = match.Browser;

            if (null == browser)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The url behind image match [{match.Selector?.Name}] cannot be read, since the match carries no browser to ask", null, GPALObjectType.None);
            else
            {
                Point aim = ToViewportPoint(browser, match, match.Selector);

                // the numbers go into the script rather than as arguments, since the engines pass arguments differently
                string url = RunPageScript(browser, $@"
                    (function (x, y) {{
                        var el = document.elementFromPoint(x, y);

                        if (!el)
                            return '';

                        var link = el.closest('a[href], area[href]');

                        return link ? link.href : (el.currentSrc || el.src || el.data || '');
                    }})({aim.X}, {aim.Y})");

                if (false == string.IsNullOrEmpty(url))
                    retVal = url;
            }

            return retVal;
        }

        /// <summary>
        /// The element under an image match. Calls elementFromPoint at the click point and walks up to the nearest
        /// actionable element: link, button, form field, label, clickable role, onclick or contenteditable. The point
        /// usually lands on an svg, path or span with no text, value or href. If nothing up the tree is actionable,
        /// the element at the point is used.<br/>
        /// No size limit: clicks, hovers and drags still go to the matched point in
        /// <see cref="GPALElement.ImageMatch"/>.<br/>
        /// Returns null for an iframe or shadow host, which a top document css selector cannot reach into.
        /// </summary>
        /// <param name="browser">Browser the match was made in</param>
        /// <param name="match">The image match</param>
        /// <param name="selector">The selector that matched, for its offsets</param>
        /// <returns>The element, or null when there is none and only the point is left</returns>
        private static ReadOnlyCollection<GPALElement> ElementUnderImage(Browser browser, GPALElement match, Selector selector)
        {
            ReadOnlyCollection<GPALElement> retVal = null;
            Point aim = ToViewportPoint(browser, match, selector);

            // the numbers go into the script rather than as arguments, since the engines pass arguments differently
            string css = RunPageScript(browser, $@"
                (function (x, y) {{
                    var el = document.elementFromPoint(x, y);

                    if (!el || 'IFRAME' === el.tagName || 'FRAME' === el.tagName || el.shadowRoot)
                        return '';

                    var actionable = el.closest('a[href], area[href], button, input, select, textarea, label, summary, ' +
                        '[role=button], [role=link], [role=menuitem], [role=menuitemcheckbox], [role=menuitemradio], ' +
                        '[role=tab], [role=checkbox], [role=radio], [role=switch], [role=option], [role=treeitem], ' +
                        '[onclick], [contenteditable]:not([contenteditable=false])');

                    if (actionable)
                        el = actionable;

                    var path = [];

                    while (el && el.nodeType === Node.ELEMENT_NODE) {{
                        if (el.id) {{
                            path.unshift('#' + CSS.escape(el.id));
                            break;
                        }}

                        var part = el.nodeName.toLowerCase(), sibling = el, nth = 1;

                        while ((sibling = sibling.previousElementSibling) != null)
                            if (sibling.nodeName === el.nodeName)
                                nth++;

                        path.unshift(part + ':nth-of-type(' + nth + ')');
                        el = el.parentElement;
                    }}

                    return path.join(' > ');
                }})({aim.X}, {aim.Y})");

            if (false == string.IsNullOrEmpty(css))
            {
                Selector cssSel = GPAL.Selector.WithCSS(css).ToGPALObject();

                retVal = FindWebElementsByCss(browser, cssSel.SelectorPaths[0], cssSel);

                if (0 < (retVal?.Count ?? 0))
                {
                    retVal[0].Css = css;
                    retVal[0].ImageMatch = match;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, 0 < retVal?.Count
                ? $"Image match for [{selector.Name}] is element [{css}], clicked at the matched point"
                : $"Image match for [{selector.Name}] has no element of its own under it, so only the point is used", browser, GPALObjectType.Browser);

            return retVal;
        }
    }
}
