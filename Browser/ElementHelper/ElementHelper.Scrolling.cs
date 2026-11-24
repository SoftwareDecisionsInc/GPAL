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
        /// Scrolls <paramref name="element"/> into view if it is not already fully visible in the viewport,
        /// using OttoMagic, Puppeteer or injected JavaScript depending on the active engine, then confirms it
        /// really landed on-screen and pages the window to it when it did not.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to scroll into view</param>
        /// <param name="interactionType">Interaction the scroll is preparing for, or null to take the current selector's</param>
        /// <param name="forVisibility">True when the element must end up visible, as in a requested scroll; false
        /// when the scroll only prepares a coordinate click</param>
        internal static void ScrollIntoView(Browser browser, dynamic element, Enums.InteractionType? interactionType = null, bool forVisibility = false)
        {
            if ("GPALElement".Equals(((GPALElement)element).TagName))
                return; // image surrogate, it's already on-screen if matched

            // OttoMagic acts through javascript by css and does not need the element on screen. Skip the scroll
            // unless the interaction uses screen coordinates or forVisibility is set.
            if (true == browser.UseOttoMagic && false == forVisibility && false == UsesScreenCoordinates(browser, interactionType))
                return;

            // an element already wholly on screen is left where it is. scrolling it anyway costs the scroll, the
            // settle wait, the visibility check after it and a re-read of where it went, on every click
            if (true == IsVisibleInViewport(browser, (GPALElement)element))
                return;

            if (true == browser.UseOttoMagic)
                browser.MagicHelper.ScrollIntoView(((GPALElement)element).Css);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.ScrollIntoView((GPALElement)element).Execute();
            else if (null != element)
            {
                string debounceScript = @"
                    // Best JS replacement for scrollIntoViewIfNeeded - do not scroll to if it's already on-screen
                    function scrollIntoViewIfNeeded(el) {
                        return new Promise(resolve => {
                            const io = new IntersectionObserver(([entry]) => {
                                io.disconnect();
                                if (entry.intersectionRatio < 1) {
                                    el.scrollIntoView({ behavior: 'instant', block: 'center', inline: 'center' });
                                }
                                resolve();
                            }, { threshold: 1 });
                            io.observe(el);
                        });
                    }

                    scrollIntoViewIfNeeded(arguments[0]);
                ";

                BrowserHelper.ExecuteJavaScript(browser, debounceScript, typeof(GPALElement) == element.GetType() ? ((GPALElement)element).WebElement : element);
                //BrowserHelper.ExecuteJavaScript(browser, "arguments[0].scrollIntoView()", element.WebElement);
            }
            Thread.Sleep(250);  // let the scroll finish; asked sooner, the page reports the offset from before it

            // Scrolling the document does not move a fixed or sticky element, so it can still be off screen.
            // ScrollUntilVisible pages the window to it.
            ScrollUntilVisible(browser, (GPALElement)element);

            // The scroll made the stored position stale for a coordinate click, so re-read it. Not Puppeteer;
            // HardwareMoveTo re-reads it every time.
            if (false == browser.UsePuppeteer && true == UsesScreenCoordinates(browser, interactionType))
                RefreshLocation(browser, (GPALElement)element);
        }

        /// <summary>
        /// Scrolls each element matched by the current unit of work's selectors horizontally or vertically by
        /// the given pixel amount.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="amount">Pixels to scroll (negative scrolls left/up)</param>
        /// <param name="scrollTypes">Whether to scroll Horizontal or Vertical</param>
        public static void ScrollElement(Browser browser, int amount, ScrollTypes scrollTypes)
        {
            List<GPALElement> matchedElements;
            bool matchedAll = false;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                   int rowCount = 0;

                   foreach (GPALElement elem in matchedElements)
                   {
                        if (null != elem)
                        {
                            int hAmount = ScrollTypes.Horizontal == scrollTypes ? amount : 0;
                            int vAmount = ScrollTypes.Vertical == scrollTypes ? amount : 0;

                            // Bring the element on screen first, as a click does, or an element below the page
                            // bottom scrolls out of sight.
                            ScrollIntoView(browser, elem, null, true);

                            if (true == browser.UseOttoMagic)
                                browser.MagicHelper.ScrollElement(elem.Css, hAmount, vAmount);
                            else if (true == browser.UsePuppeteer)
                                browser.PuppeteerClient.ScrollElement(elem.Css).WithHPixels(hAmount).WithVPixels(vAmount).Execute();
                            else if (true == browser.UseSelenium)
                                BrowserHelper.ExecuteJavaScript(browser, $"arguments[0].scrollBy({{ left: {hAmount}, top: {vAmount}, behavior: 'instant' }})", elem.WebElement);

                            if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                                break;
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Checks whether the page is currently scrolled to its bottom.
        /// </summary>
        /// <param name="browser">Browser to check</param>
        /// <param name="element">Unused</param>
        /// <returns>True if the viewport's bottom edge is at or past the document's bottom</returns>
        public static bool IsEndOfPage(Browser browser, GPALElement element)
        {
            bool retval = true;

            if (true == browser.UsePuppeteer)
                retval = browser.PuppeteerClient.IsEndOfPage().Execute<bool>();
            else if (true == browser.UseOttoMagic)
                retval = browser.MagicHelper.IsEndOfPage();
            else
            {
                IWebDriver browserDriver = browser.BrowserDriver;
                // The same check Puppeteer and OttoMagic make. documentElement.scrollHeight, not body's, which is
                // short when html is the scroll container or body has margins, reporting end of page on page one.
                // Scroll positions can be fractional, so the bottom counts within 1px. A page whose html does not
                // scroll is measured on the first element that does.
                var result = BrowserHelper.ExecuteJavaScriptObj(@"
                    if ('hidden' !== getComputedStyle(document.documentElement).overflow)
                        return (window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 1;
                    const scrollable = Array.from(document.querySelectorAll('*')).find(
                        el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                    ) || document.documentElement;
                    return (scrollable.clientHeight + scrollable.scrollTop) >= scrollable.scrollHeight - 1;", browser, null);

                if (null != result)
                    retval = (bool)result;
            }

            return retval;
        }
        /// <summary>
        /// Brings <paramref name="element"/> on screen when <see cref="ScrollIntoView"/> could not: goes to the top,
        /// then pages down a screen at a time until it is in the viewport.<br/>
        /// Needed for fixed or sticky elements, which scrolling never moves; the site shows them at some position,
        /// usually the top. If none works, leaves the page at the top and warns.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element that has to be on-screen for a coordinate click to land</param>
        /// <returns>True if the element ended up visible in the viewport</returns>
        internal static bool ScrollUntilVisible(Browser browser, GPALElement element)
        {
            bool visible = SettledIsVisibleInViewport(browser, element);

            // IsVisibleInViewport wants the whole element inside the viewport, so an element bigger than the
            // window can never answer yes and paging the document looking for one only wastes the time
            if (false == visible && true == IsBiggerThanViewport(element.Size, GetWindowRectangle(browser)))
                visible = true;

            if (false == visible)
            {
                int pagesLeft = MaxScrollPages;

                _ = browser.PageTop;
                visible = SettledIsVisibleInViewport(browser, element);

                // Stop at MaxScrollPages as well as end of page, or a site whose scroll container cannot be
                // measured is paged end to end on every failed click.
                while (false == visible && 0 < pagesLeft-- && false == IsEndOfPage(browser, element))
                {
                    _ = browser.PageDown;
                    visible = SettledIsVisibleInViewport(browser, element);
                }

                if (false == visible)
                {
                    _ = browser.PageTop;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not scroll element [{element.TagName}][{element.Css}] into the viewport. Left the page at the top and continuing.", browser, GPALObjectType.Browser);
                }
            }

            return visible;
        }
    }
}
