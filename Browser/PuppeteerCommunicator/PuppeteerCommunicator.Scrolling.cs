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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerCommunicator
    {
        // Implementation for "is-end-of-page"
        /// <summary>
        /// Determines whether the page (or its nearest scrollable element) is scrolled to its end,
        /// taking into account the document's overflow style.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the page/element is scrolled to the end, or true if detection failed (fail-forward).</returns>
        public async Task<bool> IsEndOfPage(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            try
            {
                // JavaScript code to check if at end of page
                var result = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"
                                (function() {
                                    try {
                                        // the page's full height is documentElement's; body can be shorter. Scroll
                                        // positions can be fractional, so the bottom counts within 1px
                                        const overflow = getComputedStyle(document.documentElement).overflow;
                                        if (overflow !== 'hidden') {
                                            return (window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 1;
                                        }
                                        const scrollable = Array.from(document.querySelectorAll('*')).find(
                                            el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                        ) || document.documentElement;
                                        return (scrollable.clientHeight + scrollable.scrollTop) >= scrollable.scrollHeight - 1;
                                    } catch {
                                        return false;
                                    }
                                })()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                // Parse result
                return (bool)result?["result"]?["value"];
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error detecting end of page, returning [true]", this, GPALObjectType.PuppeteerCommunicator, ex);
                return true;
            }
        }

        // Implementation for "page-down"
        // PageDown: Scrolls down by one or more pages
        /// <summary>
        /// Scrolls the page down by one or more page heights.
        /// Implementation for the "page-down" workflow action.
        /// </summary>
        /// <param name="pagesToScroll">Number of page-height increments to scroll down (default 1).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous scroll operation.</returns>
        public async Task PageDown(int pagesToScroll = 1, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            for (int i = 0; i < pagesToScroll; i++)
            {
                await ScrollPageAsync("down", pagesToScroll, sessionId).ConfigureAwait(false);
            }
        }

        // PageEnd: Scrolls to the bottom of the page
        /// <summary>
        /// Scrolls to the very bottom of the page.
        /// Implementation for the "page-end" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous scroll operation.</returns>
        public async Task PageEnd(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await ScrollToPositionAsync("end", sessionId).ConfigureAwait(false);
        }

        // PageTop: Scrolls to the top of the page
        /// <summary>
        /// Scrolls to the very top of the page.
        /// Implementation for the "page-top" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous scroll operation.</returns>
        public async Task PageTop(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await ScrollToPositionAsync("top", sessionId).ConfigureAwait(false);
        }

        // PageUp: Scrolls up by one or more pages
        /// <summary>
        /// Scrolls the page up by one or more page heights.
        /// Implementation for the "page-up" workflow action.
        /// </summary>
        /// <param name="pagesToScroll">Number of page-height increments to scroll up (default 1).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous scroll operation.</returns>
        public async Task PageUp(int pagesToScroll = 1, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            for (int i = 0; i < pagesToScroll; i++)
            {
                await ScrollPageAsync("up", pagesToScroll, sessionId).ConfigureAwait(false);
            }
        }


        // Implementation for "scroll-element"
        /// <summary>
        /// Scrolls the element with the given DOM id by the specified number of pixels horizontally and vertically,
        /// using <c>Element.scrollBy</c>.
        /// Implementation for the "scroll-element" workflow action.
        /// </summary>
        /// <param name="elementId">DOM id of the element to scroll (used directly in <c>document.getElementById</c>).</param>
        /// <param name="hPixels">Horizontal pixels to scroll by.</param>
        /// <param name="vPixels">Vertical pixels to scroll by.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous scroll operation.</returns>
        public async Task ScrollElement(string elementId, int hPixels, int vPixels, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = "document.getElementById('" + elementId + "').scrollBy(" + hPixels + ", " + vPixels + ")" 
                }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "scroll-into-view"
        /// <summary>
        /// Scrolls the given element into the viewport, first attempting the native CDP
        /// <c>DOM.scrollIntoViewIfNeeded</c> command and falling back to a JavaScript
        /// <c>Element.scrollIntoView</c> call if the native command does not return a result.
        /// In headless mode, also forces a layout reflow before and a brief delay after scrolling,
        /// and injects a one-time stylesheet disabling overscroll bounce so elements don't shift after scrolling.
        /// Implementation for the "scroll-into-view" workflow action.
        /// </summary>
        /// <param name="element">Element to scroll into view.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true if the scroll completed (or was attempted) without error, or false if an exception occurred.</returns>
        /// <example>
        /// <code>
        /// await ScrollIntoView(element, sessionId);
        /// </code>
        /// </example>
        public async Task<bool> ScrollIntoView(GPALElement element, string sessionId = null)
        {
            int backendNodeId = element.ElementBackendNodeId;

            sessionId ??= GetEffectiveSessionId();

            try
            {
                if ("GPALElement".Equals(element.TagName))
                    return true;     // it's already in view if it's a matched image

                // force layout engine to run before we interact
                if (true == Browser.BrowserSettings.UseHeadless)
                {
                    // force layout
                    await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                        new {
                            contextId = CurrentContextId,
                            expression = @"
                                document.body.style.zoom = '100%';  // minor reflow trigger
                                window.dispatchEvent(new Event('resize'));
                                document.documentElement.scrollTop += 1; document.documentElement.scrollTop -= 1;" 
                        }, sessionId).ConfigureAwait(false);
                }

                // keep the items from bouncing around if they are already in view
                await SendCommand(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"
                            if (!document.querySelector('style[data-gpal-overscroll]')) {
                                const style = document.createElement('style');
                                style.setAttribute('data-gpal-overscroll', 'true');
                                style.textContent = `
                                    html, body {
                                        overscroll-behavior: none !important;
                                        overscroll-behavior-x: none !important;
                                        overscroll-behavior-y: none !important;
                                    }
                                `;
                                (document.head || document.documentElement).appendChild(style);
                            }
                        ",
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                // Try native CDP scroll first (quick & usually sufficient when element is renderable)
                var scrollResult = await SendCommand<object>(
                    DevToolsMethods.DOMScrollIntoViewIfNeeded,
                    new { backendNodeId },
                    sessionId
                ).ConfigureAwait(false);

                if (true == Browser.BrowserSettings.UseHeadless)
                    await Task.Delay(250).ConfigureAwait(false); // Give layout/paint time to settle

                if (null == scrollResult)
                {
                    // Escape properly for JS string literal (handles quotes, backslashes)
                    var escapedSelector = element.Css.Replace(@"\", @"\\").Replace("'", @"\'");

                    var callResult = await SendCommand<dynamic>(
                        DevToolsMethods.RuntimeEvaluate,
                        new
                        {
                            expression = $@"
                            (function() {{
                                const el = document.querySelector(""{escapedSelector}"");
                                if (!el) {{
                                    console.warn('Element not found by selector: {escapedSelector}');
                                    return false;
                                }}

                                el.scrollIntoView({{
                                    behavior: 'instant',
                                    block: 'center',
                                    inline: 'center'
                                }});

                                // Force reflow/layout
                                void el.getBoundingClientRect();
                                void el.offsetHeight;

                                return new Promise(r => requestAnimationFrame(() => r(true)));
                            }})()",
                            awaitPromise = true,
                            contextId = CurrentContextId,
                            returnByValue = true
                        },
                        sessionId
                    ).ConfigureAwait(false);

                    // The JS scroll result is not checked and the box model is not read again; the scroll counts as done
                }

                return true;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"ScrollIntoView failed",
                    this, GPALObjectType.PuppeteerCommunicator, ex);
                return false;
            }
        }
        /*
        public async Task<dynamic> ScrollIntoView(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            try
            {
                // === STEP 0: Let Chromium handle ALL scroll containers ===
                await SendCommand<object>(
                    DevToolsMethods.DOMScrollIntoViewIfNeeded,
                    new { backendNodeId },
                    sessionId
                );

                await Task.Delay(75); // allow paint tree to settle

                // === STEP 1: Get fresh box model AFTER scrolling ===
                var boxResult = await SendCommand<object>(
                    DevToolsMethods.DOMGetBoxModel,
                    new { backendNodeId },
                    sessionId
                );

                if (boxResult?.model?.content == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"No box model for node [{backendNodeId}]",
                        this, GPALObjectType.PuppeteerCommunicator);
                    return new { success = false };
                }

                var content = boxResult.model.content;

                double elX = content[0];
                double elY = content[1];
                double elW = content[2] - content[0];
                double elH = content[7] - content[1];

                // === STEP 2: Get viewport ===
                var vpResult = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = "({ height: window.innerHeight, width: window.innerWidth })",
                    returnByValue = true
                }, sessionId);

                int vpH = vpResult?.result?.value?.height ?? 0;
                int vpW = vpResult?.result?.value?.width ?? 0;

                if (vpH == 0 || vpW == 0)
                    return new { success = false };

                // === STEP 3: Clamp to viewport ===
                double visLeft = Math.Max(elX, 0);
                double visTop = Math.Max(elY, 0);
                double visRight = Math.Min(elX + elW, vpW);
                double visBottom = Math.Min(elY + elH, vpH);

                if (visLeft >= visRight || visTop >= visBottom)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"Element [{backendNodeId}] not visible after CDP scroll",
                        this, GPALObjectType.PuppeteerCommunicator);
                    return new { success = false };
                }

                int clickX = (int)((visLeft + visRight) / 2);
                int clickY = (int)((visTop + visBottom) / 2);

                return new
                {
                    success = true,
                    point = new { x = clickX, y = clickY }
                };
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"ScrollIntoView failed for node [{backendNodeId}]",
                    this, GPALObjectType.PuppeteerCommunicator, ex);
                return new { success = false };
            }
        }
        */
        // Implementation for "scroll-window"
        /// <summary>
        /// Scrolls the whole window/viewport by the specified number of pixels horizontally and vertically,
        /// then waits briefly to allow headless rendering to settle.
        /// Implementation for the "scroll-window" workflow action.
        /// </summary>
        /// <param name="hPixels">Horizontal pixels to scroll by.</param>
        /// <param name="vPixels">Vertical pixels to scroll by.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous scroll operation.</returns>
        public async Task ScrollWindow(int hPixels, int vPixels, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await ScrollWindowByPixelsAsync(hPixels, vPixels, sessionId).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);  // lets headless rendering settle
        }
        /// <summary>
        /// Scrolls the page up or down by one or more "pages" (the viewport height), trying <c>window.scrollBy</c> first
        /// and falling back to scrolling the nearest scrollable element (and finally a Page Up/Down key press) for
        /// single-page applications where the document itself does not scroll.
        /// </summary>
        /// <param name="direction">The scroll direction, either "up" or "down".</param>
        /// <param name="pagesToScroll">The number of viewport-height pages to scroll.</param>
        /// <param name="s">The CDP session ID to use.</param>
        /// <returns>A Task that resolves to true if at least one page of scrolling succeeded; false otherwise.</returns>
        public async Task<bool> ScrollPageAsync(string direction, int pagesToScroll, string s)
        {
            double initialScrollY = 0;
            bool retVal = false;

            try
            {
                // Validate direction
                if (direction != "up" && direction != "down")
                {
                    return retVal;
                }

                // Step 1: Check overflow property
                var overflowResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"getComputedStyle(document.documentElement).overflow",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);
                string overflow = overflowResult?["result"]?["value"]?.ToString();

                // Step 2: Try window.scrollBy if overflow is not 'hidden' - which means we have scroll bars
                if (overflow != "hidden")
                {
                    for (int count = 0; count < pagesToScroll; count++)
                    {
                        if (true == (retVal = await scrollPage().ConfigureAwait(false)))
                            continue;
                        else
                            break;
                    }
                }
                if (false == retVal)
                    for (int count = 0; count < pagesToScroll; count++)
                    {

                        if (true == (retVal = await scrollSinglePageApp().ConfigureAwait(false)))
                            continue;
                        else
                            break;
                    }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to page [{direction}] for [{pagesToScroll}] pages.", this, GPALObjectType.PuppeteerClient, ex);
            }

            async Task<bool> scrollPage()
            {
                // Get initial scroll position
                var initialScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"window.scrollY",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);
                initialScrollY = Convert.ToDouble(initialScrollResult?["result"]?["value"]);

                // Try window.scrollBy. 87.5% of the view, as the browser's own Page Down steps, so each page overlaps
                // the last and nothing under a fixed header or on a page boundary is skipped
                string scrollDirection = direction == "down" ? "Math.round(window.innerHeight * 0.875)" : "-Math.round(window.innerHeight * 0.875)";
                await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = $"window.scrollBy({{ left: 0, top: {scrollDirection}, behavior: 'instant' }})",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);

                // Verify scroll
                var afterScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"window.scrollY",
                    contextId = CurrentContextId,
                    returnByValue = true

                }, s).ConfigureAwait(false);
                double afterScrollY = Convert.ToDouble(afterScrollResult?["result"]?["value"]);

                if (afterScrollY != initialScrollY)
                {
                    return true; // Scroll worked
                }
                return false;
            }

            async Task<bool> scrollSinglePageApp()
            {
                // Step 3: Try scrolling the scrollable element
                var elementScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = $@"(function() {{
                                        const isDown = '{direction}' === 'down';
                                        const scrollAmount = Math.round(window.innerHeight * 0.875) * (isDown ? 1 : -1);
                                        const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                            el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                        ) || document.body;
                                        const initialScrollTop = scrollable.scrollTop;
                                        scrollable.scrollBy({{ left: 0, top: scrollAmount, behavior: 'instant' }});
                                        return {{ initialScrollTop, scrollTop: scrollable.scrollTop, element: scrollable.className || 'body' }};
                                    }})()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);

                double initialScrollTop = Convert.ToDouble(elementScrollResult?["result"]?["value"]?["initialScrollTop"]);
                double elementScrollTop = Convert.ToDouble(elementScrollResult?["result"]?["value"]?["scrollTop"]);

                if (elementScrollTop != initialScrollTop)
                {
                    return true; // Scroll worked
                }

                return await scrollByKeyPress().ConfigureAwait(false);

                async Task<bool> scrollByKeyPress()
                {
                    // Step 4: Fallback to Page Up/Down key press
                    // Focus scrollable element
                    await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = @"(function() {
                                    const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                        el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                    ) || document.body;
                                    scrollable.focus();
                                    return scrollable.className || 'body';
                                })()",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, s).ConfigureAwait(false);

                    // Send keyDown
                    int keyCode = direction == "down" ? 34 : 33;
                    string keyName = direction == "down" ? "PageDown" : "PageUp";
                    await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                    {
                        type = "keyDown",
                        windowsVirtualKeyCode = keyCode,
                        key = keyName,
                        code = keyName
                    }, s).ConfigureAwait(false);

                    // Send keyUp
                    await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                    {
                        type = "keyUp",
                        windowsVirtualKeyCode = keyCode,
                        key = keyName,
                        code = keyName
                    }, s).ConfigureAwait(false);

                    // Step 5: Verify scroll
                    var keyScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = @"(function() {
                                    const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                        el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                    ) || document.body;
                                    return { scrollTop: scrollable.scrollTop, element: scrollable.className || 'body' };
                                })()",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, s).ConfigureAwait(false);

                    double keyScrollTop = Convert.ToDouble(keyScrollResult?["result"]?["value"]["scrollTop"]);
                    return keyScrollTop != initialScrollTop || keyScrollTop > 0; // Return true if scrolled
                }
            }

            return retVal;
        }
        /// <summary>
        /// Scrolls the page to the very top or very bottom, trying <c>window.scrollTo</c> first, then the nearest
        /// scrollable element's <c>scrollTo</c>, and finally a Home/End key press as a last resort.
        /// </summary>
        /// <param name="position">The target scroll position, either "top" or "end".</param>
        /// <param name="s">The CDP session ID to use.</param>
        /// <returns>A Task that resolves to true if the scroll position changed; false otherwise.</returns>
        public async Task<bool> ScrollToPositionAsync(string position, string s)
        {
            try
            {
                // Validate position
                if (position != "top" && position != "end")
                {
                    return false;
                }

                // Step 1: Check overflow property
                var overflowResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = $@"(function() {{
                                        return getComputedStyle(document.documentElement).overflow;
                                    }})()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);
                string overflow = overflowResult?["result"]?["value"]?.ToString();

                // Step 2: Try window.scrollTo if overflow is not 'hidden'
                double initialScrollY = 0;
                if (overflow != "hidden")
                {
                    // Get initial scroll position
                    var initialScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = @"window.scrollY",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                    initialScrollY = Convert.ToDouble(initialScrollResult?["result"]?["value"]);

                    // Try window.scrollTo. The page's full height is documentElement's; body is shorter by its margins
                    string scrollTarget = position == "end" ? "Math.max(document.documentElement.scrollHeight, document.body.scrollHeight)" : "0";
                    await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = $"window.scrollTo(0, {scrollTarget})",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, s).ConfigureAwait(false);

                    // Verify scroll
                    var afterScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        contextId = CurrentContextId,
                        expression = @"window.scrollY"
                    }, s).ConfigureAwait(false);
                    double afterScrollY = Convert.ToDouble(afterScrollResult?["result"]?["value"]);

                    if (afterScrollY != initialScrollY)
                    {
                        return true; // Scroll worked
                    }
                }

                // Step 3: Try scrolling the scrollable element
                var elementScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = $@"(function() {{
                                        const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                            el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                        ) || document.body;
                                        const initialScrollTop = scrollable.scrollTop;
                                        const scrollTarget = {(position == "end" ? "scrollable.scrollHeight" : "0")};
                                        scrollable.scrollTo({{left: 0, top: scrollTarget, behavior: 'instant'}});
                                        return {{ initialScrollTop, scrollTop: scrollable.scrollTop, element: scrollable.className || 'body' }};
                                    }})()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);

                double initialScrollTop = Convert.ToDouble(elementScrollResult?["result"]?["value"]?["initialScrollTop"]);
                double elementScrollTop = Convert.ToDouble(elementScrollResult?["result"]?["value"]?["scrollTop"]);

                if (elementScrollTop != initialScrollTop)
                {
                    return true; // Scroll worked
                }

                // Step 4: Fallback to Home/End key press
                // Focus scrollable element
                await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"(function() {
                                    const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                        el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                    ) || document.body;
                                    scrollable.focus();
                                    return scrollable.className || 'body';
                                })()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);

                // Send keyDown
                int keyCode = position == "end" ? 35 : 36;
                string keyName = position == "end" ? "End" : "Home";
                await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                {
                    type = "keyDown",
                    windowsVirtualKeyCode = keyCode,
                    key = keyName,
                    code = keyName
                }, s).ConfigureAwait(false);

                // Send keyUp
                await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                {
                    type = "keyUp",
                    windowsVirtualKeyCode = keyCode,
                    key = keyName,
                    code = keyName
                }, s).ConfigureAwait(false);

                // Step 5: Verify scroll
                var keyScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"(function() {
                                    const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                        el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                    ) || document.body;
                                    return { scrollTop: scrollable.scrollTop, element: scrollable.className || 'body' };
                                })()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, s).ConfigureAwait(false);

                double keyScrollTop = Convert.ToDouble(keyScrollResult?["result"]?["value"]["scrollTop"]);
                return keyScrollTop != initialScrollTop || keyScrollTop > 0; // Return true if scrolled
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Scrolls the page or the nearest scrollable element by the given relative pixel offsets, trying
        /// <c>window.scrollBy</c> first, then an element's <c>scrollBy</c>, and finally falling back to
        /// <c>Input.synthesizeScrollGesture</c> (a touch-based scroll gesture).
        /// </summary>
        /// <param name="hPixels">The horizontal scroll distance in pixels (positive scrolls right).</param>
        /// <param name="vPixels">The vertical scroll distance in pixels (positive scrolls down).</param>
        /// <param name="sessionId">The CDP session ID to use.</param>
        /// <returns>A Task that resolves to true if the scroll position changed; false otherwise.</returns>
        public async Task<bool> ScrollWindowByPixelsAsync(int hPixels, int vPixels, string sessionId)
        {
            try
            {
                // Check for main window scrollability
                var overflowResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = $@"(function() {{
                                        return getComputedStyle(document.documentElement).overflow;
                                    }})()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
                string overflow = overflowResult?.result?.value?.ToString();

                // Try window.scrollBy if overflow is not 'hidden'
                if (overflow != "hidden")
                {
                    // Get initial scroll position
                    var initialScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = @"[window.scrollX, window.scrollY]",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, sessionId).ConfigureAwait(false);
                    var initialScrollX = initialScrollResult?.result?.value?.First;
                    var initialScrollY = initialScrollResult?.result?.value?.Last;

                    // Try window.scrollBy
                    await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = $"window.scrollBy({hPixels}, {vPixels})",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, sessionId).ConfigureAwait(false);

                    // Verify scroll
                    var afterScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = @"[window.scrollX, window.scrollY]",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, sessionId).ConfigureAwait(false);
                    var afterScrollX = afterScrollResult?.result?.value?.First;
                    var afterScrollY = afterScrollResult?.result?.value?.Last;

                    if (afterScrollX != initialScrollX || afterScrollY != initialScrollY)
                    {
                        return true; // Scroll worked
                    }
                }

                // Try scrolling the scrollable element
                var elementScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = $@"(function() {{
                                const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                    (el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden') ||
                                    (el.scrollWidth > el.clientWidth && getComputedStyle(el).overflowX !== 'hidden')
                                ) || document.body;
                                const initialScrollLeft = scrollable.scrollLeft;
                                const initialScrollTop = scrollable.scrollTop;
                                scrollable.scrollBy({hPixels}, {vPixels});
                                return {{ initialScrollLeft, initialScrollTop, scrollLeft: scrollable.scrollLeft, scrollTop: scrollable.scrollTop, element: scrollable.className || 'body' }};
                            }})()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                double initialScrollLeft = Convert.ToDouble(elementScrollResult?.result?.value?.initialScrollLeft ?? 0);
                double initialScrollTop = Convert.ToDouble(elementScrollResult?.result?.value?.initialScrollTop ?? 0);
                double elementScrollLeft = Convert.ToDouble(elementScrollResult?.result?.value?.scrollLeft ?? 0);
                double elementScrollTop = Convert.ToDouble(elementScrollResult?.result?.value?.scrollTop ?? 0);

                if (elementScrollLeft != initialScrollLeft || elementScrollTop != initialScrollTop)
                {
                    return true; // Scroll worked
                }

                // Fallback to Input.synthesizeScrollGesture (touch-based scrolling)
                // This is a more robust alternative to keyboard keys for relative pixel scrolling
                await SendCommand<object>(DevToolsMethods.InputSynthesizeScrollGesture, new
                {
                    x = 100,  // Arbitrary start position
                    y = 100,  // Arbitrary start position
                    xDistance = -hPixels, // positive scrolls left
                    yDistance = -vPixels, // positive scrolls up
                    speed = 800 // Optional speed control
                }, sessionId).ConfigureAwait(false);

                // Verify scroll again after the gesture
                var finalScrollResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = @"(function() {
                            const scrollable = Array.from(document.querySelectorAll('*')).find(el => 
                                (el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden') ||
                                (el.scrollWidth > el.clientWidth && getComputedStyle(el).overflowX !== 'hidden')
                            ) || document.body;
                            return { scrollLeft: scrollable.scrollLeft, scrollTop: scrollable.scrollTop };
                        })()",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                double finalScrollLeft = Convert.ToDouble(finalScrollResult?.result?.value?.scrollLeft ?? 0);
                double finalScrollTop = Convert.ToDouble(finalScrollResult?.result?.value?.scrollTop ?? 0);

                // This verification is simpler; just check that a change occurred.
                return (finalScrollLeft != initialScrollLeft || finalScrollTop != initialScrollTop);
            }
            catch
            {
                return false;
            }
        }
    }
}
