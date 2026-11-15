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
        /// Hides every element matched by the current unit of work's selectors by setting their CSS display to none.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void Hide(Browser browser)
        {
            List<GPALElement> matchedElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                foreach (GPALElement elem in elems)
                    Hide(browser, elem);
            }
        }
        /// <summary>
        /// Hides a single element by setting its CSS display to none, using OttoMagic, Puppeteer or JavaScript
        /// depending on the active engine.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to hide</param>
        public static void Hide(Browser browser, GPALElement element)
        {
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.HideElement(element.Css);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.HideElement(element.Css).Execute();
            else
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].style.display = 'none';", element.WebElement);

        }
        /// <summary>
        /// Directly sets a DOM attribute on every element matched by the current unit of work's selectors.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="attribute">Name of the attribute to set</param>
        /// <param name="value">Value to assign to the attribute</param>
        public static void SetAttribute(Browser browser, string attribute, string value)
        {
            List<GPALElement> matchedElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                foreach (GPALElement elem in elems)
                    SetAttribute(browser, elem, attribute, value);
            }
        }
        /// <summary>
        /// Directly sets a DOM attribute on a single element, using OttoMagic, Puppeteer or JavaScript depending
        /// on the active engine. Hardware automation has no equivalent operation, so it falls through to the
        /// JavaScript branch (whichever of Selenium/Puppeteer is configured underneath).
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to set the attribute on</param>
        /// <param name="attribute">Name of the attribute to set</param>
        /// <param name="value">Value to assign to the attribute</param>
        public static void SetAttribute(Browser browser, GPALElement element, string attribute, string value)
        {
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.SetAttribute(element.Css, attribute, value);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.SetAttribute(element.Css).WithAttribute(attribute).WithValue(value).Execute();
            else
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].setAttribute(arguments[1], arguments[2]);", element.WebElement, attribute, value);
        }

        /// <summary>
        /// Checks the image match's click point still hits its element; something, usually an ad after a scroll, can
        /// cover it.<br/>
        /// If covered, scroll the element to the middle of the window, away from sticky headers, ad bars and cookie
        /// banners, and move the point with it. False if still covered. One page script, so a clear point costs one
        /// call.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <param name="resolved">The element the image match resolved to</param>
        /// <param name="selector">The selector, for its name</param>
        /// <param name="aim">The viewport point to click, moved when the element was centred</param>
        /// <returns>True when the point is the element's, false when something still covers it</returns>
        private static bool PointIsClear(Browser browser, GPALElement resolved, Selector selector, ref Point aim)
        {
            bool retVal = true;

            string answer = RunPageScript(browser, $@"
                (function (css, x, y) {{
                    var el = document.querySelector(css);

                    if (!el)
                        return 'gone';

                    function hits(px, py) {{
                        var hit = document.elementFromPoint(px, py);
                        return !!hit && (hit === el || el.contains(hit));
                    }}

                    function named(px, py) {{
                        var hit = document.elementFromPoint(px, py);

                        if (!hit)
                            return 'nothing';

                        var classes = 'string' === typeof hit.className ? hit.className.trim().split(/\s+/).filter(Boolean).slice(0, 3).join('.') : '';

                        return hit.tagName.toLowerCase() + (hit.id ? '#' + hit.id : '') + (classes ? '.' + classes : '');
                    }}

                    if (hits(x, y))
                        return 'clear|0|0|';

                    var covering = named(x, y);
                    var before = el.getBoundingClientRect();

                    el.scrollIntoView({{ block: 'center', inline: 'nearest', behavior: 'instant' }});

                    var after = el.getBoundingClientRect();
                    var dx = Math.round(after.left - before.left), dy = Math.round(after.top - before.top);

                    return (hits(x + dx, y + dy) ? 'centred' : 'covered') + '|' + dx + '|' + dy + '|' + covering;
                }})({Newtonsoft.Json.JsonConvert.SerializeObject(resolved.Css)}, {aim.X}, {aim.Y})");

            // No answer or "gone": leave the point unchanged and return true.
            string[] parts = answer?.Split(new[] { '|' }, 4);

            if (4 == parts?.Length && ("centred" == parts[0] || "covered" == parts[0]))
            {
                aim = new Point(aim.X + Convert.ToInt32(parts[1]), aim.Y + Convert.ToInt32(parts[2]));

                if ("centred" == parts[0])
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{parts[3]}] covered selector [{selector.Name}] where its picture was, so the element was moved to the middle of the window and is clicked there", browser, GPALObjectType.Browser);
                else
                {
                    retVal = false;

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, true == GPAL.NoFallbackRecoveryActions
                        ? $"[{parts[3]}] covers selector [{selector.Name}] even with it in the middle of the window, and recovery actions are off, so the point is clicked and [{parts[3]}] may take the click"
                        : $"[{parts[3]}] covers selector [{selector.Name}] even with it in the middle of the window, so the element itself is clicked instead of its picture", browser, GPALObjectType.Browser);
                }
            }

            return retVal;
        }

        // https://www.javascripttutorial.net/dom/css/check-if-an-element-is-visible-in-the-viewport/
        /// <summary>
        /// Re-reads the element's position and size and stores them.<br/>
        /// Location is read when the element is found, so after a scroll a hardware click from it lands off the
        /// window, or is refused though the element is in view.<br/>
        /// getBoundingClientRect is viewport relative, but Location is kept in document coordinates for
        /// <see cref="HardwareMoveTo"/>, so OttoMagic and Puppeteer add the page scroll back; Selenium reads Location
        /// directly. If it cannot be read, the old position stays.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to re-read, updated in place</param>
        internal static void RefreshLocation(Browser browser, GPALElement element)
        {
            try
            {
                Rectangle live = new Rectangle(0, 0, 0, 0);

                if (true == browser.UsePuppeteer && 0 != element.ElementBackendNodeId)
                {
                    // Pass the backend node id, not ElementHandle (the objectId). This endpoint parses an integer,
                    // so an objectId fails and the stale position is kept.
                    live = browser.PuppeteerClient.GetBoundingClientRect(element.ElementBackendNodeId.ToString()).Execute<Rectangle>();

                    // Same as the OttoMagic branch below: add the page scroll for a top level element, not for one
                    // inside an iframe, whose stored position already includes it.
                    bool inPuppeteerFrame = browser.CurrentUOW?.ContextPath?.Any(context => ElementType.IFrame == context.elementType) ?? false;

                    if (0 < live.Width && 0 < live.Height && false == inPuppeteerFrame)
                        live.Offset(browser.PuppeteerClient.WindowPageOffsetX().Execute<int>(),
                                    browser.PuppeteerClient.WindowPageOffsetY().Execute<int>());
                }
                else if (true == browser.UseOttoMagic)
                {
                    live = browser.MagicHelper.GetBoundingClientRect(element.Css);

                    // getBoundingClientRect is relative to the element's own document. Add the page scroll for a
                    // top level element, since HardwareMoveTo subtracts it. Not inside an iframe: the iframe's
                    // stored position already includes it.
                    bool inFrame = browser.CurrentUOW?.ContextPath?.Any(context => ElementType.IFrame == context.elementType) ?? false;

                    if (0 < live.Width && 0 < live.Height && false == inFrame)
                        live.Offset(browser.MagicHelper.WindowPageOffsetX(), browser.MagicHelper.WindowPageOffsetY());
                }
                else if (null != element.WebElement)
                    live = new Rectangle(element.WebElement.Location, element.WebElement.Size);   // selenium reads these live, in document coordinates

                if (0 < live.Width && 0 < live.Height)
                {
                    element.Location = live.Location;
                    element.Size = live.Size;
                    element.BoundingRect = new ClientRectangle(element.Location, element.Size);
                }
                else
                    // The stored position is from when the element was found; after a scroll a hardware click there
                    // hits whatever is there now.
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not re-read where [{element.TagName}][{element.Css}] is, so it is still where it was when it was found", browser, GPALObjectType.Browser);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not re-read where [{element.TagName}] is, using the reading from when it was found", browser, GPALObjectType.Browser, ex);
            }
        }

        /// <summary>
        /// True if a click at this screen point reaches the element or a descendant. The rectangle does not show
        /// what covers it. False if the engine does not answer, as in <see cref="IsVisibleInViewport"/>.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">The element that should receive the click</param>
        /// <param name="screenPoint">The point being considered, in screen coordinates</param>
        /// <returns>True when the element, or something inside it, is what is at that point</returns>
        internal static bool PointHitsElement(Browser browser, GPALElement element, Point screenPoint)
        {
            bool retVal = false;

            try
            {
                // elementFromPoint takes viewport coordinates, so subtract the content area origin.
                Rectangle content = GetWindowRectangle(browser);
                int x = screenPoint.X - content.X;
                int y = screenPoint.Y - content.Y;

                if (true == browser.UsePuppeteer)
                    retVal = browser.BrowserSettings.PuppeteerCommunicator.ElementFromPoint(element, x, y).GetAwaiter().GetResult();
                else if (true == browser.UseOttoMagic)
                    retVal = browser.MagicHelper.ElementFromPoint(element.Css, x, y);
                else
                {
                    var result = BrowserHelper.ExecuteJavaScriptObj(
                        "var elem = arguments[0]; " +
                        "var hit = document.elementFromPoint(arguments[1], arguments[2]); " +
                        "return hit === elem || elem.contains(hit);", browser, element.WebElement, x, y);

                    retVal = null != result && true == Convert.ToBoolean(result);
                }

                //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Asked what is at viewport [{x}, {y}] (screen [{screenPoint.X}, {screenPoint.Y}], content origin [{content.X}, {content.Y}]) for [{element.TagName}][{element.Css}]: [{retVal}]", browser, GPALObjectType.Browser);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not ask what is at [{screenPoint.X}, {screenPoint.Y}] for [{element.TagName}]", browser, GPALObjectType.Browser, ex);
            }

            return retVal;
        }

        /// <summary>
        /// Checks whether <paramref name="element"/>'s bounding rectangle is fully within the current viewport.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to check</param>
        /// <returns>True if the element is fully visible in the viewport</returns>
        public static bool IsVisibleInViewport(Browser browser, GPALElement element)
        {
            bool retval = false;    // False if the check fails; callers use this to decide whether a coordinate click is safe.

            if (true == browser.UsePuppeteer)
                retval = browser.PuppeteerClient.IsVisibleInViewport(element).Execute<bool>();
            else if (true == browser.UseOttoMagic)
                retval = browser.MagicHelper.IsVisibleInViewport(element.Css);
            else
            {
                IWebDriver browserDriver = browser.BrowserDriver;

                var result = BrowserHelper.ExecuteJavaScriptObj(
                    "var elem = arguments[0]; " +
                    "const rect = elem.getBoundingClientRect(); " +
                    "return ( " +
                    "  rect.top >= 0 && " +
                    "  rect.left >= 0 && " +
                    "  rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) && " +
                    "  rect.right <= (window.innerWidth || document.documentElement.clientWidth) " +
                    ");", browser, element.WebElement);

                if (null != result)
                    retval = (bool)result;
            }

            return retval;
        }
        /// <summary>
        /// Checks whether <paramref name="element"/> is in the viewport, rechecking briefly before returning false.
        /// A revealed header slides in with a CSS transition, and one check mid-slide sees it off screen.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to check</param>
        /// <returns>True if the element was visible in the viewport within the settle window</returns>
        private static bool SettledIsVisibleInViewport(Browser browser, GPALElement element)
        {
            bool visible = IsVisibleInViewport(browser, element);
            int checksLeft = VisibilitySettleChecks;

            while (false == visible && 0 < checksLeft--)
            {
                Thread.Sleep(VisibilitySettleMs);
                visible = IsVisibleInViewport(browser, element);
            }

            return visible;
        }
        /// <summary>
        /// Whether <paramref name="elementSize"/> is too big to ever sit inside <paramref name="viewport"/>.
        /// </summary>
        /// <param name="elementSize">Size of the element</param>
        /// <param name="viewport">The browser's content area</param>
        /// <returns>True if the element cannot fit on screen in either direction</returns>
        private static bool IsBiggerThanViewport(Size elementSize, Rectangle viewport)
        {
            return elementSize.Width > viewport.Width || elementSize.Height > viewport.Height;
        }
        /// <summary>
        /// Using selenium, get all Css Attributes for a given IWebElement
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="webElement"></param>
        /// <returns></returns>
        public static Dictionary<string, object> GetCssAttributes(Browser browser, IWebElement webElement)
        {
            var script = @"
                            const el = arguments[0];
                            const styles = window.getComputedStyle(el);
                            var props = {};

                            // 2. Values that are ""noise"" - we never want them in the result
                            const EMPTY_VALUES = new Set([
                                '', 'none', 'normal', 'inherit', 'initial', 'unset',
                                'auto', '0px', '0', 'transparent'
                            ]);

                            // 3. The only properties we ever care about
                            const RELEVANT = new Set([
                                'display', 'visibility', 'opacity', 'pointer-events', 'overflow', 'clip-path',
                                'position', 'top', 'left', 'right', 'bottom', 'z-index',
                                'width', 'height', 'min-width', 'min-height', 'max-width', 'max-height',
                                'margin', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
                                'padding', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
                                'border', 'border-width', 'border-style', 'border-color', 'box-sizing',
                                'flex', 'flex-direction', 'flex-wrap', 'flex-grow', 'flex-shrink', 'flex-basis',
                                'justify-content', 'align-items', 'align-self', 'order',
                                'grid', 'grid-template', 'grid-area', 'grid-column', 'grid-row',
                                'transform', 'transform-origin', 'transition', 'animation',
                                'cursor', 'user-select', 'content', 'filter'
                            ]);

                            // Properties that say whether the element shows or takes clicks keep every value, so
                            // display none, opacity 0 and pointer-events none reach the visible and clickable checks
                            const STATE = new Set(['display', 'visibility', 'opacity', 'pointer-events']);

                            // 4. Iterate only the properties that exist on the style object
                            for (let i = 0; i < styles.length; i++) {
                                const prop = styles[i];                  // e.g. ""display""
                                if (!RELEVANT.has(prop)) { continue; }   // skip everything else

                                const value = styles.getPropertyValue(prop).trim(); // ""block"", ""0.5"", etc.

                                // Skip empty / default values
                                if (!value || (!STATE.has(prop) && EMPTY_VALUES.has(value))) { continue; }

                                props[prop] = value;
                            }

                            // Opacity does not pass down to children, so an ancestor with opacity 0 is reported as
                            // ancestor-opacity 0, which hides the element too; opacity keeps the element's own value.
                            // The walk goes through slots and out of shadow roots to their hosts
                            let opacityNode = el.assignedSlot || el.parentElement || el.getRootNode().host;
                            while (opacityNode && props['ancestor-opacity'] !== '0') {
                                if (window.getComputedStyle(opacityNode).getPropertyValue('opacity').trim() === '0') { props['ancestor-opacity'] = '0'; }
                                opacityNode = opacityNode.assignedSlot || opacityNode.parentElement || opacityNode.getRootNode().host;
                            }
                            return props;
                        ";

            var dict = BrowserHelper.ExecuteJavaScriptObj(script, browser, webElement) as Dictionary<string, object>;

            return dict?.ToDictionary(k => k.Key, v => v.Value ?? "");
        }
        /// <summary>
        ///  Using selenium, Returns **all** DOM attributes of the IWebElement (including data-*, aria-*, etc.).
        /// The dictionary contains the *raw* attribute values (string or null).
        /// </summary>
        public static Dictionary<string, object> GetDomAttributes(
            Browser browser,
            IWebElement webElement)
        {
            const string script = @"
                                    const cleanText = (str) => (str || '').replace(/\s+/g, ' ').trim();
                                    const el = arguments[0];
                                    const attrs = el.attributes;
                                    const result = {};
                                    for (let i = 0; i < attrs.length; i++) {
                                        const a = attrs[i];
                                        result[a.name] = a.value;   // value is always a string or empty
                                    }
                                    // Also add a few handy built-in properties that are not attributes
                                    result['id']        = el.id;
                                    result['className'] = el.className;
                                    result['tagName']   = el.tagName;
                                    result['innerText'] = cleanText(el.innerText);
                                    result['textContent'] = cleanText(el.textContent);
                                    return result;
                                ";

            var raw = BrowserHelper.ExecuteJavaScriptObj(script, browser, webElement)
                      as Dictionary<string, object>;

            // Normalize null -> empty string
            return raw?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? ""
            ) ?? new Dictionary<string, object>();
        }
        /// <summary>
        /// Returns key JavaScript DOM properties of the element (tagName, value, innerHTML, etc.).
        /// Only includes properties that exist on the element.
        /// </summary>
        public static Dictionary<string, object> GetDomProperties(
            Browser browser,
            IWebElement webElement)
        {
            const string script = @"
                                    const el = arguments[0];
                                    const props = {};

                                    if (""textContent"" in el) props.textContent = el.textContent;
                                    if (""innerHTML"" in el) props.innerHTML = el.innerHTML;
                                    if (""innerText"" in el) props.innerText = el.innerText;
                                    if (""outerHTML"" in el) props.outerHTML = el.outerHTML;
                                    if (""className"" in el) props.className = el.className;
                                    if (""disabled"" in el) props.disabled = el.disabled;

                                    return props;
                                ";

            var raw = BrowserHelper.ExecuteJavaScriptObj(script, browser, webElement)
                      as Dictionary<string, object>;

            // Normalize null -> ""
            return raw?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? ""
            ) ?? new Dictionary<string, object>();
        }
        /// <summary>
        /// Checks whether a Selenium element is both displayed and enabled.
        /// </summary>
        /// <param name="webElement">Element to check</param>
        /// <returns>True if the element is displayed and enabled</returns>
        internal static bool IsElementClickable(IWebElement webElement)
        {
            return webElement.Displayed && webElement.Enabled;
        }
        internal static bool IsElementClickable(GPALElement element)
        {
            return element.Displayed && element.Enabled;
        }
        /// <summary>
        /// Heuristically determines whether <paramref name="elem"/> can be typed into, based on its tag, input
        /// type, CSS cursor, contenteditable attribute, and class names.
        /// </summary>
        /// <param name="elem">Element to check</param>
        /// <returns>True if the element appears editable (defaults to true if the check throws)</returns>
        public static bool IsElementEditable(GPALElement elem)
        {
            try
            {
                // Get the tag name and type
                string tag = elem?.WebElement?.TagName.ToUpperInvariant() ?? elem?.TagName.ToUpperInvariant();
                string type = string.Empty;
                if (tag == "INPUT")
                {
                    type = elem.GetAttribute("type")?.ToLower();
                }

                // Check CSS cursor property
                string cursor = elem?.GetCssValue("cursor")?.ToLower();
                if (cursor == "text")
                    return true;

                // Check tag/type/contentEditable
                bool retval = (tag == "INPUT" &&
                               (type == "text" || type == "email" || type == "password" || type == "search"))
                              || tag == "TEXTAREA"
                              || (tag == "DIV" && elem.GetAttribute("contenteditable")?.ToLower() == "true");

                // Otherwise, an "Editable" class counts as editable
                if (!retval)
                {
                    string classAttr = elem?.GetAttribute("class") ?? "";
                    if (null != classAttr)
                        retval = classAttr.Split(' ').Contains("Editable");
                }

                return retval;
            }
            catch
            {
                // If the check throws, treat the element as editable, as the workflow asked to type into it
                return true;
            }
        }
    }
}
