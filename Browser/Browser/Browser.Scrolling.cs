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
        /// Scrolls the current tab down one page.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageDown
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageDown();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageDown().Execute();
                else
                    ScrollPage("down");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled down one page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scrolls the current tab to the bottom.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageEnd
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageEnd();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageEnd().Execute();
                else
                    ScrollToPosition("end");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled to the end of the page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scrolls the current tab to the top.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageTop
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageTop();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageTop().Execute();
                else
                    ScrollToPosition("top");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled to the top of the page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scrolls the current tab up one page.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageUp
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageUp();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageUp().Execute();
                else
                    ScrollPage("up");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled up one page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scroll the browser window horizontally the number of pixels.<br/>
        /// Negative numbers scroll left. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls right.<br/>Negative integer scrolls left.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollWindowHorizontal([Pixels] int scrollAmountInPixels)
        {
            InAction(false, false);

            if (true == UseOttoMagic)
                MagicHelper.ScrollWindowHorizontal(scrollAmountInPixels);
            else if (true == UsePuppeteer)
                PuppeteerClient.ScrollWindowByHorizontal(scrollAmountInPixels).Execute();
            else
                BrowserHelper.ExecuteJavaScript(this, $"window.scrollBy({{left: {scrollAmountInPixels}, top: 0, behavior: 'instant'}})");

            return this;
        }
        /// <summary>
        /// Scroll the browser window horizontally the number of pixels.<br/>
        /// Negative numbers scroll up. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls down.<br/>Negative integer scrolls up.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollWindowVertical([Pixels] int scrollAmountInPixels)
        {
            InAction(false, false);

            if (true == UseOttoMagic)
                MagicHelper.ScrollWindowVertical(scrollAmountInPixels);
            else if (true == UsePuppeteer)
                PuppeteerClient.ScrollWindowByVertical(scrollAmountInPixels).Execute();
            else
                BrowserHelper.ExecuteJavaScript(this, $"window.scrollBy({{left: 0, top: {scrollAmountInPixels}, behavior: 'instant'}})");

            return this;
        }
        /// <summary>
        /// Scroll the element horizontally by number of pixels.<br/>
        /// Negative numbers scroll left. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls right.<br/>Negative integer scolls left.</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        /// <remarks>
        /// Scrolls every selector in the unit of work, then ends it.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollElementHorizontal([Pixels] int scrollAmountInPixels)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.ScrollElement(this, scrollAmountInPixels, ScrollTypes.Horizontal);

            return this;
        }
        /// <summary>
        /// Scroll the element vertically by number of pixels.<br/>
        /// Negative numbers scroll up. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls down.<br/>Negative integer scolls down.</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        /// <remarks>
        /// Scrolls every selector in the unit of work, then ends it.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollElementVertical([Pixels] int scrollAmountInPixels)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.ScrollElement(this, scrollAmountInPixels, ScrollTypes.Vertical);

            return this;
        }
        /// <summary>
        /// Convenience method to page up or down
        /// Takes into account single page apps and scrolling containers as well as reglar html
        /// </summary>
        /// <param name="direction">"up" or "down"</param>
        /// <returns></returns>
        bool ScrollPage(string direction)
        {
            try
            {
                // Validate direction
                if (direction != "up" && direction != "down")
                {
                    return false;
                }

                // JavaScript code with direction parameter
                string script = @"
                    (function(direction) {
                        function scrollPage(direction) {
                        try {
                            const isDown = direction === 'down';
                            // 87.5% of the view, as the browser's own Page Down steps, so each page overlaps the
                            // last and nothing under a fixed header or on a page boundary is skipped
                            const scrollAmount = Math.round(window.innerHeight * 0.875) * (isDown ? 1 : -1);
                            const keyName = isDown ? 'PageDown' : 'PageUp';
                            const keyCode = isDown ? 34 : 33;

                            // Check overflow
                            const overflow = getComputedStyle(document.documentElement).overflow;

                            // Try window.scrollBy if overflow is not 'hidden'
                            const initialScrollY = window.scrollY;
                            if (overflow !== 'hidden') {
                            window.scrollBy({ left: 0, top: scrollAmount, behavior: 'instant' });
                            if (window.scrollY !== initialScrollY) {
                                return {
                                status: 'success',
                                method: 'window.scrollBy',
                                scrollPosition: window.scrollY,
                                elementClass: 'window'
                                };
                            }
                            }

                            // Try scrolling the scrollable element
                            const scrollable = Array.from(document.querySelectorAll('*')).find(
                            el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                            ) || document.body;
                            const initialScrollTop = scrollable.scrollTop;
                            scrollable.scrollBy({ left: 0, top: scrollAmount, behavior: 'instant' });
                            const elementClass = scrollable.className || 'body';

                            if (scrollable.scrollTop !== initialScrollTop) {
                            return {
                                status: 'success',
                                method: 'element.scrollBy',
                                scrollPosition: scrollable.scrollTop,
                                elementClass
                            };
                            }

                            // Fallback to Page Up/Down key press
                            scrollable.focus();
                            const event = new KeyboardEvent('keydown', {
                            key: keyName,
                            code: keyName,
                            keyCode,
                            which: keyCode,
                            bubbles: true,
                            cancelable: true
                            });
                            scrollable.dispatchEvent(event);

                            if (scrollable.scrollTop !== initialScrollTop) {
                            return {
                                status: 'success',
                                method: keyName,
                                scrollPosition: scrollable.scrollTop,
                                elementClass
                            };
                            }

                            // No scroll method worked
                            return {
                            status: 'failed',
                            message: 'No scroll method worked',
                            scrollPosition: scrollable.scrollTop,
                            elementClass
                            };
                        } catch (error) {
                            return {
                            status: 'error',
                            message: error.message,
                            scrollPosition: 0,
                            elementClass: 'none'
                            };
                        }
                        }
                        return scrollPage(direction);
                    })('" + direction + "')";

                // Execute the script
                var result = BrowserHelper.ExecuteJavaScript(this, script);

                // the script returns an object with a status field
                dynamic response = result;
                return response.status == "success";
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Convenience method to scroll to page top or end
        /// Takes into account single page apps and scrolling containers as well as reglar html
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        private bool ScrollToPosition(string position)
        {
            try
            {
                // Validate position
                if (position != "top" && position != "end")
                {
                    return false;
                }

                // JavaScript code with position parameter
                string script = @"
                    (function(position) {
                        function scrollToPosition(position) {
                            try {
                                const isEnd = position === 'end';
                                // the page's full height is documentElement's; body is shorter by its margins
                                const scrollTarget = isEnd ? Math.max(document.documentElement.scrollHeight, document.body.scrollHeight) : 0;
                                const keyName = isEnd ? 'End' : 'Home';
                                const keyCode = isEnd ? 35 : 36;

                                // Check overflow
                                const overflow = getComputedStyle(document.documentElement).overflow;

                                // Try window.scrollTo if overflow is not 'hidden'
                                const initialScrollY = window.scrollY;
                                if (overflow !== 'hidden') {
                                    window.scrollTo({ left: 0, top: scrollTarget, behavior: 'instant' });
                                    if (window.scrollY !== initialScrollY) {
                                        return {
                                            status: 'success',
                                            method: 'window.scrollTo',
                                            scrollPosition: window.scrollY,
                                            elementClass: 'window'
                                        };
                                    }
                                }

                                // Try scrolling the scrollable element
                                const scrollable = Array.from(document.querySelectorAll('*')).find(
                                    el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                ) || document.body;
                                const initialScrollTop = scrollable.scrollTop;
                                const elementScrollTarget = isEnd ? scrollable.scrollHeight : 0;
                                scrollable.scrollTo({ left: 0, top: elementScrollTarget, behavior: 'instant' });
                                const elementClass = scrollable.className || 'body';

                                if (scrollable.scrollTop !== initialScrollTop) {
                                    return {
                                        status: 'success',
                                        method: 'element.scrollTo',
                                        scrollPosition: scrollable.scrollTop,
                                        elementClass
                                    };
                                }

                                // Fallback to Home/End key press
                                scrollable.focus();
                                const event = new KeyboardEvent('keydown', {
                                    key: keyName,
                                    code: keyName,
                                    keyCode,
                                    which: keyCode,
                                    bubbles: true,
                                    cancelable: true
                                });
                                scrollable.dispatchEvent(event);

                                if (scrollable.scrollTop !== initialScrollTop) {
                                    return {
                                        status: 'success',
                                        method: keyName,
                                        scrollPosition: scrollable.scrollTop,
                                        elementClass
                                    };
                                }

                                // No scroll method worked
                                return {
                                    status: 'failed',
                                    message: 'No scroll method worked',
                                    scrollPosition: scrollable.scrollTop,
                                    elementClass
                                };
                            } catch (error) {
                                return {
                                    status: 'error',
                                    message: error.message,
                                    scrollPosition: 0,
                                    elementClass: 'none'
                                };
                            }
                        }
                        return scrollToPosition(position);
                    })('" + position + "')";

                // Execute the script
                var result = BrowserHelper.ExecuteJavaScript(this, script);

                // the script returns an object with a status field
                dynamic response = result;
                return response.status == "success";
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Helper method to check whether the current page is at the bottom
        /// </summary>
        /// <example>
        /// <code>
        ///     while (false == browser.IsEndOfPage())
        ///         _ = browser.PageDown
        /// </code>
        /// </example>
        /// <returns>true or false</returns>
        public bool IsEndOfPage()
        {
            // The same check the image search and scroll-into-view use, with a tolerance for Firefox, which scrolls
            // to fractions of a pixel.
            bool retVal = ElementHelper.IsEndOfPage(this, null);

            return retVal;
        }
    }
}
