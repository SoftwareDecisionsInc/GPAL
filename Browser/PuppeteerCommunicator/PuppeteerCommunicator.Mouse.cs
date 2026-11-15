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
        /// <summary>
        /// Finds all elements matching the given CSS selector and performs a left click on each of them.
        /// </summary>
        /// <param name="selector">A CSS selector identifying the element(s) to click.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        public async Task LeftClickByCss(string selector, string sessionId = null)
        {
            List<GPALElement> elems = await EvaluateSelector(selector, sessionId).ConfigureAwait(false);
            List<dynamic> responses = new List<dynamic>();

            foreach (GPALElement elem in elems)
                await ClickElement(elem, sessionId, responses, ClickType.LeftClick).ConfigureAwait(false);
        }

        // Implementation for "hover"
        /// <summary>
        /// Moves the mouse pointer to a random point within the element identified by the given backend node id,
        /// dispatching an Input.dispatchMouseEvent "mouseMoved" event.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element to hover over.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        public async Task Hover(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var x = await GetElementRandomCenterX(backendNodeId, sessionId).ConfigureAwait(false);
            var y = await GetElementRandomCenterY(backendNodeId, sessionId).ConfigureAwait(false);
            await MovePointerTo(x, y, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves the pointer to a point. With WithSimulateMouseMovement, or WithSimulateMouse on the current selector,
        /// it moves in a few steps from where the last move left it; before the first move it starts a short way up
        /// and to the left of the point. Otherwise it is one move straight to the point.
        /// </summary>
        /// <param name="x">Viewport x coordinate, in CSS pixels.</param>
        /// <param name="y">Viewport y coordinate, in CSS pixels.</param>
        /// <param name="sessionId">CDP session id the moves are sent to.</param>
        /// <returns>The CDP response to the last move</returns>
        private async Task<dynamic> MovePointerTo(float x, float y, string sessionId)
        {
            dynamic response = null;
            var random = new Random();
            bool simulate = true == GPAL.SimulateMouse || true == Browser?.CurrentUOW?.CurrentSelector?.SimulateMouse;
            PointF from = pointerAt ?? new PointF(Math.Max(1f, x - random.Next(80, 160)), Math.Max(1f, y - random.Next(40, 100)));
            int steps = true == simulate ? random.Next(4, 8) : 1;

            for (int step = 1; step <= steps; step++)
            {
                float along = (float)step / steps;

                response = await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
                {
                    type = "mouseMoved",
                    button = "none",
                    x = from.X + (x - from.X) * along,
                    y = from.Y + (y - from.Y) * along,
                    modifiers = HeldModifiers
                }, sessionId).ConfigureAwait(false);

                if (true == simulate)
                    await Task.Delay(random.Next(8, 20)).ConfigureAwait(false);
            }

            pointerAt = new PointF(x, y);

            return response;
        }

        // Implementation for "left-click"
        /// <summary>
        /// Performs a click (or other configured click type) on the given element, using CDP input dispatch,
        /// with a JavaScript-based fallback for cases (such as headless mode) where the box model cannot be obtained.
        /// </summary>
        /// <param name="element">The element to click, including its backend node id and bounding information.</param>
        /// <param name="sessionId">CDP session id; used to resolve the effective session, including iframe sessions.</param>
        /// <param name="responses">A list to which intermediate command responses are appended.</param>
        /// <param name="clickType">The type of click to perform (e.g. left click, double click, right click).</param>
        /// <param name="modifiers">Bitmask of keyboard modifier keys (Alt, Ctrl, Meta, Shift) to hold during the click.</param>
        public async Task ClickElement(GPALElement element, string sessionId, List<dynamic> responses,
                                      ClickType clickType = ClickType.LeftClick,
                                      int modifiers = 0)
        {
            // a modifier held with PressModifierKey is down for this click too
            modifiers |= HeldModifiers;
            bool cdpSuccess = false;
            bool elementGone = false;
            bool elementNotRendered = false;
            bool forceJavscript = false;
            bool jsSuccess = false;
            bool isInFrame = CurrentContextId.HasValue;
            string effectiveSessionId = isInFrame && !string.IsNullOrEmpty(CurrentFrameSessionId)
                ? CurrentFrameSessionId
                : sessionId;

            try
            {
                // Headless can return no box model for an A tag, so force a layout with JS before reading it
                if (true == Browser.BrowserSettings.UseHeadless)
                {
                    await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "document.body.offsetHeight;",  // reads force layout
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, sessionId).ConfigureAwait(false);

                    await Task.Delay(80).ConfigureAwait(false);  // let the layout finish
                }

                var boxModel = await SendCommand<object>(DevToolsMethods.DOMGetBoxModel, new
                {
                    objectId = element.ElementHandle
                }, effectiveSessionId).ConfigureAwait(false);

                if (null != boxModel)
                {
                    var contentQuad = boxModel?.model?.content;
                    if (contentQuad == null || contentQuad.Count < 8)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{element.TagName}][{clickType}] failed: Unable to get coordinates (boxmodel)");
                    }
                    else
                    {
                        float x = (float)contentQuad[0];
                        float y = (float)contentQuad[1];
                        float width = (float)(contentQuad[2] - contentQuad[0]);
                        float height = (float)(contentQuad[7] - contentQuad[1]);

                        var random = new Random();
                        var margin = 0.05f;
                        var clickAreaWidth = width * (1f - 2 * margin);
                        var clickAreaHeight = height * (1f - 2 * margin);

                        x = x + width * margin + (float)random.NextDouble() * clickAreaWidth;
                        y = y + height * margin + (float)random.NextDouble() * clickAreaHeight;

                        if (0 < x && 0 < y)
                        {
                            // A link is clicked in the middle of its first line. A link that wraps onto two lines has
                            // a box whose middle can be the text beside it, so the click would miss the link.
                            if ("A".Equals(element.TagName, StringComparison.OrdinalIgnoreCase))
                            {
                                PointF firstLine = await FirstLineMiddle(element, effectiveSessionId,
                                    new PointF((float)contentQuad[0] + width / 2f, (float)contentQuad[1] + height / 2f)).ConfigureAwait(false);
                                x = firstLine.X;
                                y = firstLine.Y;
                            }

                            // Map ClickType -> CDP values
                            string button = clickType switch
                            {
                                ClickType.LeftClick => "left",
                                ClickType.MiddleClick => "middle",
                                ClickType.RightClick => "right",
                                ClickType.LeftDoubleClick => "left",
                                _ => "left"
                            };

                            int clickCount = clickType == ClickType.LeftDoubleClick ? 2 : 1;

                            // A mouse event goes to whatever is on top at the point, and an overlay there (a
                            // full-page ad, a cookie banner) takes the click without an error. Ask the page what is
                            // at the point first; if something else is, click with JavaScript as Selenium does when
                            // Chrome reports the click intercepted. Frame coordinates differ from the page's, so a
                            // frame's element is not asked about.
                            string coveredBy = true == isInFrame ? null : await ElementCoveringPoint(element, x, y, effectiveSessionId).ConfigureAwait(false);

                            if (false == string.IsNullOrEmpty(coveredBy))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CDP [{clickType}] on [{element.TagName}][{element.Css}] would land on [{coveredBy}], which covers it at [{x},{y}]. Falling back to JavaScript.", this, GPALObjectType.PuppeteerClient);
                                forceJavscript = true;
                            }
                            else
                            {
                                await MovePointerTo(x, y, effectiveSessionId).ConfigureAwait(false);

                                // PRESS
                                responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
                                {
                                    type = "mousePressed",
                                    button,
                                    buttons = PressedButtons(button),
                                    clickCount,
                                    x,
                                    y,
                                    modifiers
                                }, effectiveSessionId).ConfigureAwait(false));

                                await Task.Delay(random.Next(60, 140)).ConfigureAwait(false); // human-like

                                // RELEASE
                                responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
                                {
                                    type = "mouseReleased",
                                    button,
                                    clickCount,
                                    x,
                                    y,
                                    modifiers
                                }, effectiveSessionId).ConfigureAwait(false));
                                cdpSuccess = true;
                            }
                        }
                        else
                        {
                            // the box model is viewport relative, so a negative coordinate means the element sits
                            // outside the viewport and a dispatched mouse event there would land on nothing
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CDP [{clickType}] has no on-screen point for [{element.TagName}][{element.Css}] at [{x},{y}]. Falling back to JavaScript.", this, GPALObjectType.PuppeteerClient);
                            forceJavscript = true;
                        }
                    }
                }
                else if (true == await ElementGone(element, effectiveSessionId).ConfigureAwait(false))
                {
                    // No box model: the element is gone and its stored rect is stale, so do not click.
                    elementGone = true;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{element.TagName}][{element.Css}] is no longer on the page, so it was not clicked", this, GPALObjectType.PuppeteerClient);
                }
                else
                {
                    // No box model and a zero-size rect: not rendered.
                    elementNotRendered = 0 >= element.BoundingRect.Width || 0 >= element.BoundingRect.Height;

                    float x = (float)element.BoundingRect.X;
                    float y = (float)element.BoundingRect.Y;
                    float width = (float)element.BoundingRect.Width;
                    float height = (float)element.BoundingRect.Height;

                    var random = new Random();
                    var margin = 0.05f;
                    var clickAreaWidth = width * (1f - 2 * margin);
                    var clickAreaHeight = height * (1f - 2 * margin);

                    x = x + width * margin + (float)random.NextDouble() * clickAreaWidth;
                    y = y + height * margin + (float)random.NextDouble() * clickAreaHeight;

                    if (0 < x && 0 < y)
                    {
                        // Force center for <a> tags
                        if ("A".Equals(element.TagName, StringComparison.OrdinalIgnoreCase))
                        {
                            x = (float)element.BoundingRect.X + width / 2f;
                            y = (float)element.BoundingRect.Y + height / 2f;
                        }

                        // Map ClickType -> CDP values
                        string button = clickType switch
                        {
                            ClickType.LeftClick => "left",
                            ClickType.MiddleClick => "middle",
                            ClickType.RightClick => "right",
                            ClickType.LeftDoubleClick => "left",
                            _ => "left"
                        };

                        int clickCount = clickType == ClickType.LeftDoubleClick ? 2 : 1;

                        await MovePointerTo(x, y, effectiveSessionId).ConfigureAwait(false);

                        // PRESS
                        responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
                        {
                            type = "mousePressed",
                            button,
                            buttons = PressedButtons(button),
                            clickCount,
                            x,
                            y,
                            modifiers
                        }, effectiveSessionId).ConfigureAwait(false));

                        await Task.Delay(random.Next(60, 140)).ConfigureAwait(false); // human-like

                        // RELEASE
                        responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
                        {
                            type = "mouseReleased",
                            button,
                            clickCount,
                            x,
                            y,
                            modifiers
                        }, effectiveSessionId).ConfigureAwait(false));
                        cdpSuccess = true;
                    }
                    else if (true == elementNotRendered)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CDP [{clickType}] has no point to click for [{element.TagName}][{element.Css}]. Element is not rendered, so it has no position on the page. Falling back to JavaScript.", this, GPALObjectType.PuppeteerClient);
                        forceJavscript = true;
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CDP [{clickType}] failed. Falling back to JavaScript.", this, GPALObjectType.PuppeteerClient);
                        forceJavscript = true;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!GPAL.NoFallbackRecoveryActions)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"CDP [{clickType}] failed.{(false == GPAL.NoFallbackRecoveryActions ? " Falling back to JavaScript." : " Javascript fallback disallowed.")}", this, GPALObjectType.PuppeteerClient, ex);
            }

            // JS fallback when CDP fails
            // No JavaScript fallback for a gone element; its selector could match a different element on the new page.
            if (false == GPAL.NoFallbackRecoveryActions && false == elementGone && (false == cdpSuccess || true == forceJavscript))
            {
                bool byCss = false == string.IsNullOrEmpty(element.Css);
                string selector = true == byCss ? element.Css : element.Xpath;

                if (string.IsNullOrEmpty(selector))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Fallback JavaScript failed: No valid XPath or CSS selector for [{element.TagName}]", this, GPALObjectType.PuppeteerClient);

                var clickScript = $@"
                            (() => {{
                                try {{
                                    const sel = `{selector.Replace("'", "\\'")}`;

                                    // querySelector for CSS, document.evaluate for XPath; document.evaluate
                                    // throws on CSS.
                                    const el = {(true == byCss ? "document.querySelector(sel)" : "document.evaluate(sel, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue")};
                                    if (!el) return false;

                                    // getBoundingClientRect is already viewport relative, which is what clientX and
                                    // clientY are defined as. adding the scroll offset turns them into document
                                    // coordinates and any handler that hit tests the event reads the wrong point
                                    const rect = el.getBoundingClientRect();
                                    const cx = rect.left + rect.width  / 2;
                                    const cy = rect.top  + rect.height / 2;

                                    const opts = {{ bubbles: true, cancelable: true, clientX: cx, clientY: cy, view: window }};

                                    // === LEFT CLICK (or double) ===
                                    if ({(clickType == ClickType.LeftClick || clickType == ClickType.LeftDoubleClick).ToString().ToLower()}) {{
                                        const count = {(clickType == ClickType.LeftDoubleClick ? 2 : 1)};
                                        for (let i = 0; i < count; i++) {{
                                            el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 0, buttons: 1}}));
                                            el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 0, buttons: 0}}));
                                            el.dispatchEvent(new MouseEvent('click',     {{...opts, button: 0, detail: i+1}}));
                                        }}
                                        return true;
                                    }}

                                    // === MIDDLE CLICK (eBay pop-under) ===
                                    if ({(clickType == ClickType.MiddleClick).ToString().ToLower()}) {{
                                        el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 1, buttons: 4}}));
                                        el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 1, buttons: 0}}));
                                        el.dispatchEvent(new MouseEvent('click',     {{...opts, button: 1}}));
                                        return true;
                                    }}

                                    // === RIGHT CLICK (context menu) ===
                                    if ({(clickType == ClickType.RightClick).ToString().ToLower()}) {{
                                        el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 2, buttons: 2}}));
                                        el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 2, buttons: 0}}));
                                        el.dispatchEvent(new MouseEvent('contextmenu', {{...opts, button: 2}}));
                                        return true;
                                    }}

                                    return false;
                                }} catch(e) {{
                                    return false;
                                }}
                            }})();
                            ";

                if (false == string.IsNullOrEmpty(selector))
                {
                    dynamic jsResult = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = clickScript,
                        objectGroup = "node",
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, sessionId).ConfigureAwait(false);

                    responses.Add(jsResult);

                    // the script returns false when it cannot find the element or the click type has no branch
                    // value is a Newtonsoft JValue, never a bool, so it is read as text
                    if (true == "true".Equals(jsResult?.result?.value?.ToString(), StringComparison.OrdinalIgnoreCase))
                        jsSuccess = true;
                }
            }

            // Log which path sent the click: CDP, JavaScript, or neither. Neither is a WARNING.
            string clickPath = true == cdpSuccess ? "CDP" : true == jsSuccess ? "JavaScript" : "nothing";

            // A gone element already published its warning.
            if (false == elementGone)
                GPAL.PublishSimpleEvent(true == cdpSuccess || true == jsSuccess ? GPALEventType.INFO : GPALEventType.WARNING,
                    $"Puppeteer [{clickType}] [{element.TagName}][{Browser.CurrentUOW?.CurrentSelector?.Name ?? element.Css}] w/modifiers [{((PuppeteerClient)PuppeteerClient).GetModifiersString(modifiers)}] dispatched by [{clickPath}]",
                    this, GPALObjectType.PuppeteerCommunicator);
        }
        /// <summary>
        /// The middle of the element's first line on screen, from DOM.getContentQuads, which gives one quad per line
        /// an inline element takes. The box middle is used when there are no quads.
        /// </summary>
        /// <param name="element">The element to click</param>
        /// <param name="sessionId">CDP session the element was found in</param>
        /// <param name="boxMiddle">The middle of the element's box</param>
        /// <returns>The viewport point to click</returns>
        private async Task<PointF> FirstLineMiddle(GPALElement element, string sessionId, PointF boxMiddle)
        {
            PointF retVal = boxMiddle;

            dynamic answer = await SendCommand<dynamic>(DevToolsMethods.DOMGetContentQuads, new
            {
                objectId = element.ElementHandle
            }, sessionId).ConfigureAwait(false);

            // each quad is four corners, x then y for each; the first one with an area is the first line
            if (answer?.quads is Newtonsoft.Json.Linq.JArray quads)
            {
                foreach (Newtonsoft.Json.Linq.JToken quad in quads)
                {
                    float[] q = quad.ToObject<float[]>();

                    if (8 <= q.Length)
                    {
                        float left = Math.Min(Math.Min(q[0], q[2]), Math.Min(q[4], q[6]));
                        float right = Math.Max(Math.Max(q[0], q[2]), Math.Max(q[4], q[6]));
                        float top = Math.Min(Math.Min(q[1], q[3]), Math.Min(q[5], q[7]));
                        float bottom = Math.Max(Math.Max(q[1], q[3]), Math.Max(q[5], q[7]));

                        if (0 < right - left && 0 < bottom - top)
                        {
                            retVal = new PointF((left + right) / 2f, (top + bottom) / 2f);
                            break;
                        }
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// What element is on top at a viewport point, when it is not the element to click, something inside it, or
        /// something holding it. Asked in the element's own document or shadow root.
        /// </summary>
        /// <param name="element">The element about to be clicked</param>
        /// <param name="x">Viewport x of the click</param>
        /// <param name="y">Viewport y of the click</param>
        /// <param name="sessionId">CDP session the element was found in</param>
        /// <returns>The covering element as tag#id.class, or null when the point is the element's or the page could not say</returns>
        private async Task<string> ElementCoveringPoint(GPALElement element, float x, float y, string sessionId)
        {
            dynamic answer = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId = element.ElementHandle,
                functionDeclaration = @"
                    function(x, y) {
                        const root = this.getRootNode();
                        const hit = (root && root.elementFromPoint ? root : document).elementFromPoint(x, y);
                        if (!hit || hit === this || this.contains(hit) || hit.contains(this))
                            return '';
                        const id = hit.id ? '#' + hit.id : '';
                        const cls = (typeof hit.className === 'string' && hit.className.trim()) ? '.' + hit.className.trim().split(/\s+/).join('.') : '';
                        return hit.tagName.toLowerCase() + id + cls;
                    }",
                arguments = new object[] { new { value = x }, new { value = y } },
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string retVal = answer?.result?.value?.ToString();

            return true == string.IsNullOrEmpty(retVal) ? null : retVal;
        }

        /// <summary>
        /// Checks isConnected. Gone if the call returns nothing (page replaced) or false (node removed).
        /// </summary>
        /// <param name="element">The element to ask about</param>
        /// <param name="sessionId">CDP session the element was found in</param>
        /// <returns>true when the element is no longer in the page</returns>
        private async Task<bool> ElementGone(GPALElement element, string sessionId)
        {
            dynamic answer = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId = element.ElementHandle,
                functionDeclaration = "function() { return this.isConnected; }",
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string isConnected = answer?.result?.value?.ToString();
            bool gone = false == "true".Equals(isConnected, StringComparison.OrdinalIgnoreCase);

            return gone;
        }
        /// <summary>
        /// Drags <paramref name="element"/> by (<paramref name="deltaX"/>, <paramref name="deltaY"/>) pixels from the
        /// start point, by dispatching a mousePressed/mouseMoved.../mouseReleased sequence via CDP
        /// Input.dispatchMouseEvent. The start point is the offset from the element's top-left, or its center when
        /// both offsets are 0.
        /// </summary>
        /// <param name="element">The element to drag, including its bounding rect.</param>
        /// <param name="sessionId">CDP session id; used as-is for each dispatched command.</param>
        /// <param name="responses">A list to which intermediate command responses are appended.</param>
        /// <param name="deltaX">Horizontal drag distance in pixels from the start point.</param>
        /// <param name="deltaY">Vertical drag distance in pixels from the start point.</param>
        /// <param name="offsetX">Horizontal distance in pixels from the element's left edge to the drag start point.</param>
        /// <param name="offsetY">Vertical distance in pixels from the element's top edge to the drag start point.</param>
        public async Task DragAndDrop(GPALElement element, string sessionId, List<dynamic> responses, int deltaX, int deltaY, int offsetX = 0, int offsetY = 0)
        {
            // The offset is from the element's top-left, as OttoMatic records it and hardware and OttoMagic use it;
            // with none, the drag starts from the centre
            bool fromCentre = 0 == offsetX && 0 == offsetY;
            var startX = (float)element.BoundingRect.X + (true == fromCentre ? (float)element.BoundingRect.Width / 2 : offsetX);
            var startY = (float)element.BoundingRect.Y + (true == fromCentre ? (float)element.BoundingRect.Height / 2 : offsetY);
            var endX = startX + deltaX;
            var endY = startY + deltaY;

            responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
            {
                type = "mousePressed",
                button = "left",
                clickCount = 1,
                x = startX,
                y = startY,
                modifiers = HeldModifiers
            }, sessionId).ConfigureAwait(false));

            const int STEPS = 10;
            for (int i = 1; i <= STEPS; i++)
            {
                var x = startX + (endX - startX) * i / STEPS;
                var y = startY + (endY - startY) * i / STEPS;

                responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
                {
                    type = "mouseMoved",
                    button = "left",
                    buttons = 1,
                    x,
                    y,
                    modifiers = HeldModifiers
                }, sessionId).ConfigureAwait(false));

                await Task.Delay(15).ConfigureAwait(false);
            }

            responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
            {
                type = "mouseReleased",
                button = "left",
                clickCount = 1,
                x = endX,
                y = endY,
                modifiers = HeldModifiers
            }, sessionId).ConfigureAwait(false));
        }
        /// <summary>
        /// Performs a left mouse click on the element(s) matching the given selector.
        /// Scrolls each matching element into view before clicking.
        /// Implementation for the "left-click" workflow action.
        /// </summary>
        /// <param name="elementId">CSS selector (or element identifier) used to locate the target element(s).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous click operation.</returns>
        /// <example>
        /// <code>
        /// await LeftClick("#submit-button");
        /// </code>
        /// </example>
        public async Task LeftClick(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            List<GPALElement> elems = await EvaluateSelector(elementId, sessionId).ConfigureAwait(false);
            List<dynamic> responses = new List<dynamic>();

            foreach (GPALElement elem in elems)
            {
                await ScrollIntoView(elem, sessionId).ConfigureAwait(false);
                await ClickElement(elem, sessionId, responses, ClickType.LeftClick).ConfigureAwait(false);
            }
        }

        // Implementation for "left-double-click"
        /// <summary>
        /// Performs a left mouse double-click on the element(s) matching the given selector.
        /// Scrolls each matching element into view before double-clicking.
        /// Implementation for the "left-double-click" workflow action.
        /// </summary>
        /// <param name="elementId">CSS selector (or element identifier) used to locate the target element(s).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous double-click operation.</returns>
        public async Task LeftDoubleClick(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            List<GPALElement> elems = await EvaluateSelector(elementId, sessionId).ConfigureAwait(false);
            List<dynamic> responses = new List<dynamic>();

            foreach (GPALElement elem in elems)
            {
                await ScrollIntoView(elem, sessionId).ConfigureAwait(false);
                await ClickElement(elem, sessionId, responses, ClickType.LeftDoubleClick).ConfigureAwait(false);
            }
        }
        /// <summary>
        /// Moves the mouse pointer to a randomized point within each given element's bounding box
        /// (clicking dead-center for anchor/"A" elements), scrolling each element into view first.
        /// Used as a humanization step before interacting with elements.
        /// </summary>
        /// <param name="elems">Elements to move the mouse over.</param>
        /// <param name="sessionId">CDP session ID used for the mouse move commands.</param>
        /// <param name="responses">List collecting the raw CDP responses from each mouse move.</param>
        /// <returns>Task that resolves to true if all moves succeeded, or false if any element raised an exception.</returns>
        public async Task<bool> MoveTo(List<GPALElement> elems, string sessionId, List<dynamic> responses)
        {
            bool retVal = true;

            foreach (GPALElement element in elems)
            {
                try
                {
                    await ScrollIntoView(element, sessionId).ConfigureAwait(false);

                    // Randomize move within 90% of the element's border
                    var random = new Random();
                    var margin = 0.05f; // 5% margin on each side (90% clickable area)
                    var clickAreaWidth = (float)element.BoundingRect.Width * (1f - 2 * margin);
                    var clickAreaHeight = (float)element.BoundingRect.Height * (1f - 2 * margin);
                    var x = (float)element.BoundingRect.X + (float)element.BoundingRect.Width * margin + (float)random.NextDouble() * clickAreaWidth;
                    var y = (float)element.BoundingRect.Y + (float)element.BoundingRect.Height * margin + (float)random.NextDouble() * clickAreaHeight;

                    if (true == "A".Equals(element.TagName)) // a link is aimed at its dead center so a short one is not missed
                    {
                        x = (float)element.BoundingRect.X + (float)element.BoundingRect.Width / 2;
                        y = (float)element.BoundingRect.Y + (float)element.BoundingRect.Height / 2;
                    }

                    responses.Add(await MovePointerTo(x, y, sessionId).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error moving to element [{element.ElementHandle}][{element.Css}]", this, GPALObjectType.Puppeteer, ex);
                    retVal = false;
                }
            }
            return retVal;
        }
        // Implementation for "move-to"
        /// <summary>
        /// Moves the mouse pointer to a randomized point within the element identified by
        /// <paramref name="backendNodeId"/>. Implementation for the "move-to" workflow action.
        /// </summary>
        /// <param name="backendNodeId">CDP backend node ID of the target element.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous mouse move operation.</returns>
        public async Task MoveTo(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var x = await GetElementRandomCenterX(backendNodeId, sessionId).ConfigureAwait(false);
            var y = await GetElementRandomCenterY(backendNodeId, sessionId).ConfigureAwait(false);
            await MovePointerTo(x, y, sessionId).ConfigureAwait(false);
        }

        // Implementation for "click-point"
        /// <summary>
        /// Buttons bit mask for a CDP press: 1 left, 2 right, 4 middle. Without it the press reports no button held.
        /// </summary>
        /// <param name="button">left, middle or right</param>
        /// <returns>The bit for that button</returns>
        private static int PressedButtons(string button)
        {
            int retVal = "middle" == button ? 4 : "right" == button ? 2 : 1;

            return retVal;
        }

        /// <summary>
        /// Clicks a viewport coordinate rather than an element, for targets with no DOM handle at all - an image
        /// match being the case that needs it. Moves first so the renderer's hover state is correct, then presses
        /// and releases, mirroring what the element click does once it has resolved its box model.
        /// </summary>
        /// <param name="x">Viewport x coordinate, in CSS pixels.</param>
        /// <param name="y">Viewport y coordinate, in CSS pixels.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <param name="responses">List collecting the raw CDP responses from the press and release.</param>
        /// <param name="clickType">Which button, and whether this is a double click.</param>
        /// <param name="modifiers">Keyboard modifier flags applied to the click.</param>
        /// <returns>Task representing the asynchronous click operation.</returns>
        public async Task ClickElement(int x, int y, string sessionId, List<dynamic> responses,
                                       ClickType clickType = ClickType.LeftClick,
                                       int modifiers = 0)
        {
            sessionId ??= GetEffectiveSessionId();
            // a modifier held with PressModifierKey is down for this click too
            modifiers |= HeldModifiers;

            string button = clickType switch
            {
                ClickType.LeftClick => "left",
                ClickType.MiddleClick => "middle",
                ClickType.RightClick => "right",
                ClickType.LeftDoubleClick => "left",
                _ => "left"
            };

            int clickCount = ClickType.LeftDoubleClick == clickType ? 2 : 1;
            var random = new Random();

            // land the pointer first so :hover resolves before the press, same as a real mouse would
            await MovePointerTo(x, y, sessionId).ConfigureAwait(false);

            responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
            {
                type = "mousePressed",
                button,
                buttons = PressedButtons(button),
                clickCount,
                x,
                y,
                modifiers
            }, sessionId).ConfigureAwait(false));

            await Task.Delay(random.Next(60, 140)).ConfigureAwait(false); // human-like

            responses.Add(await SendCommand<object>(DevToolsMethods.InputDispatchMouseEvent, new
            {
                type = "mouseReleased",
                button,
                clickCount,
                x,
                y,
                modifiers
            }, sessionId).ConfigureAwait(false));
        }

        // Implementation for "move-to-point"
        /// <summary>
        /// Moves the mouse pointer to a viewport coordinate rather than to an element, for targets that have no
        /// DOM handle at all - an image match being the case that needs it. This is a real browser level input
        /// event, so unlike a dispatched mouseover it does update the renderer's CSS :hover state.
        /// </summary>
        /// <param name="x">Viewport x coordinate, in CSS pixels.</param>
        /// <param name="y">Viewport y coordinate, in CSS pixels.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous mouse move operation.</returns>
        public async Task MoveTo(int x, int y, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await MovePointerTo(x, y, sessionId).ConfigureAwait(false);
        }

        // Implementation for "middle-click"
        /// <summary>
        /// Performs a middle mouse click on the element(s) matching the given selector.
        /// Scrolls each matching element into view before clicking.
        /// Implementation for the "middle-click" workflow action.
        /// </summary>
        /// <param name="elementId">CSS selector (or element identifier) used to locate the target element(s).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous click operation.</returns>
        /// <example>
        /// <code>
        /// await MiddleClick("a.result-link");
        /// </code>
        /// </example>
        public async Task MiddleClick(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            List<GPALElement> elems = await EvaluateSelector(elementId, sessionId).ConfigureAwait(false);
            List<dynamic> responses = new List<dynamic>();

            foreach (GPALElement elem in elems)
            {
                await ScrollIntoView(elem, sessionId).ConfigureAwait(false);
                await ClickElement(elem, sessionId, responses, ClickType.MiddleClick).ConfigureAwait(false);
            }
        }

        // Implementation for "right-click"
        /// <summary>
        /// Performs a right mouse click on the element(s) matching the given selector.
        /// Scrolls each matching element into view before clicking.
        /// Implementation for the "right-click" workflow action.
        /// </summary>
        /// <param name="elementId">CSS selector (or element identifier) used to locate the target element(s).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous click operation.</returns>
        /// <example>
        /// <code>
        /// await RightClick("#context-menu-target");
        /// </code>
        /// </example>
        public async Task RightClick(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            List<GPALElement> elems = await EvaluateSelector(elementId, sessionId).ConfigureAwait(false);
            List<dynamic> responses = new List<dynamic>();

            foreach (GPALElement elem in elems)
            {
                await ScrollIntoView(elem, sessionId).ConfigureAwait(false);
                await ClickElement(elem, sessionId, responses, ClickType.RightClick).ConfigureAwait(false);
            }
        }

        // Helper for getting element X coordinate
        /// <summary>
        /// Computes a randomized X coordinate near the horizontal center of the element's bounding box,
        /// used to vary click positions for more human-like interaction.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node ID of the target element.</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the randomized X coordinate, in viewport pixels.</returns>
        private async Task<int> GetElementRandomCenterX(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var rect = await GetBoundingClientRect(backendNodeId, sessionId).ConfigureAwait(false);
            return (int)rect.X + rect.Width / new Random().Next(2, 3);
        }

        // Helper for getting element Y coordinate
        /// <summary>
        /// Computes a randomized Y coordinate near the element's bounding box, used to vary click positions
        /// for more human-like interaction.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node ID of the target element.</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the randomized Y coordinate, in viewport pixels.</returns>
        private async Task<int> GetElementRandomCenterY(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var rect = await GetBoundingClientRect(backendNodeId, sessionId).ConfigureAwait(false);
            return (int)rect.Y + rect.Width / new Random().Next(2, 3);
        }
        /// <summary>
        /// Sets the value of an input or select element to the given value and dispatches "input" and "change"
        /// events so that page scripts react to the change.
        /// </summary>
        /// <param name="ElementHandle">The CDP remote object ID of the element to update.</param>
        /// <param name="value">The value to assign to the element.</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to true if the element's value actually changed; false otherwise.</returns>
        public async Task<bool> SelectByValue(string ElementHandle, string value, string sessionId = null)
        {
            var result = await SendCommand<dynamic>(
                DevToolsMethods.RuntimeCallFunctionOn,
                new
                {
                    objectId = ElementHandle,
                    contextId = CurrentContextId,
                    functionDeclaration = @"
                        function(value) {
                            const oldValue = this.value;
                            this.value = value;
                            const changed = this.value !== oldValue;
                            this.dispatchEvent(new Event('input', { bubbles: true }));
                            this.dispatchEvent(new Event('change', { bubbles: true }));
                            return changed;
                        }",
                    arguments = new[] { new { value = value } }
                },
                sessionId ?? GetEffectiveSessionId()).ConfigureAwait(false);

            return result?.result?.value == true;
        }
        /// <summary>
        /// Selects an option in a <c>&lt;select&gt;</c> element by its zero-based index and dispatches "input"
        /// and "change" events so that page scripts react to the change.
        /// </summary>
        /// <param name="ElementHandle">The CDP remote object ID of the select element to update.</param>
        /// <param name="index">The zero-based index of the option to select.</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to true if the selected index actually changed; false if the index is out of range or unchanged.</returns>
        public async Task<bool> SelectByIndex(string ElementHandle, int index, string sessionId = null)
        {
            var result = await SendCommand<dynamic>(
                DevToolsMethods.RuntimeCallFunctionOn,
                new
                {
                    objectId = ElementHandle,
                    contextId = CurrentContextId,
                    functionDeclaration = @"
                        function(idx) {
                            if (idx < 0 || idx >= this.options.length) {
                                return false;
                            }
                            const oldIndex = this.selectedIndex;
                            this.selectedIndex = idx;
                            const changed = this.selectedIndex !== oldIndex;
                            this.dispatchEvent(new Event('input', { bubbles: true }));
                            this.dispatchEvent(new Event('change', { bubbles: true }));
                            return changed;
                        }",
                    arguments = new[] { new { value = index } }
                },
                sessionId ?? GetEffectiveSessionId()).ConfigureAwait(false);

            return result?.result?.value == true;
        }
    }
}
