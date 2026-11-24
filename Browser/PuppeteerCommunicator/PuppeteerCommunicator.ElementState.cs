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
        // Helper method to get attributes
        static Dictionary<string, string> GetAttributesAsDictionary(string jsonString)
        {
            try
            {
                using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(jsonString);

                // Navigate to the 'node' object
                if (!doc.RootElement.TryGetProperty("node", out System.Text.Json.JsonElement nodeElement))
                {
                    return null;
                }

                // Navigate to the 'attributes' array
                if (!nodeElement.TryGetProperty("attributes", out System.Text.Json.JsonElement attributesElement) ||
                    attributesElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return null;
                }

                var dict = new Dictionary<string, string>();
                for (int i = 0; i < attributesElement.GetArrayLength() - 1; i += 2)
                {
                    string key = attributesElement[i].GetString();
                    string value = attributesElement[i + 1].GetString();
                    dict[key] = value;
                }

                return dict;
            }
            catch (System.Text.Json.JsonException)
            {
                return null; // Invalid JSON
            }
        }

        // Helper: Get current value via JS evaluate
        /// <summary>
        /// Helper that reads the current "value" property of an element via JavaScript evaluation.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        /// <returns>Task that resolves to the element's current value as a string, or an empty string if unavailable.</returns>
        private async Task<string> GetElementValue(string objectId, string sessionId)
        {
            var result = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                functionDeclaration = "function() { return this.value; }",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            // Parse the returned value (assuming it's a string)
            return result?.result?.value?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Whether the element has focus, so that text sent to the focused element reaches it.
        /// </summary>
        /// <param name="objectId">Remote object id of the element.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        /// <returns>True when the element is the active element of its document, or of its shadow root.</returns>
        private async Task<bool> IsElementFocused(string objectId, string sessionId)
        {
            // In a shadow root the document's active element is the host, so ask the element's own root
            var result = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                functionDeclaration = "function() { return this === this.getRootNode().activeElement; }",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            return true == (bool?)result?.result?.value;
        }

        // Implementation for "get-attribute"
        /// <summary>
        /// Retrieves a single attribute of the element identified by the given backend node id, read on the element.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="attribute">The name of the attribute to retrieve.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the attribute's value, or null when the element has no such attribute or is gone.</returns>
        public async Task<dynamic> GetAttribute(int backendNodeId, string attribute, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            dynamic retVal = await CallOnElement(backendNodeId, "function(name) { return this.getAttribute(name); }", sessionId, attribute).ConfigureAwait(false);

            return retVal;
        }


        // Implementation for "get-bounding-client-rect"
        /// <summary>
        /// Computes the bounding client rectangle (position and size) of the element identified by the given backend node id,
        /// preferring direct JavaScript evaluation in the iframe's context for cross-origin iframes and falling back to
        /// DOM.getBoxModel on the main page.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="sessionId">CDP session id; the effective session, or the current iframe session, is used if applicable.</param>
        /// <returns>Task that resolves to the element's bounding rectangle, or <c>(-1, -1, -1, -1)</c> if it could not be determined.</returns>
        public async Task<Rectangle> GetBoundingClientRect(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // If we are inside an iframe (via InFrame()), prefer the iframe's context/session
            bool isInFrame = CurrentContextId.HasValue;
            string effectiveSessionId = isInFrame && !string.IsNullOrEmpty(CurrentFrameSessionId)
                ? CurrentFrameSessionId
                : sessionId;

            // Inside a frame (CurrentContextId set, including same-origin iframes with no session), use
            // getBoundingClientRect: it is relative to the frame's document, and the screen calculation adds the
            // iframe position itself. DOM.getBoxModel returns main-frame coordinates that already include the offset.
            if (CurrentContextId.HasValue)
            {
                try
                {
                    // 1. Convert backendNodeId to a JavaScript RemoteObject in the frame context
                    var resolveResult = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new
                    {
                        backendNodeId = backendNodeId,
                        executionContextId = CurrentContextId.Value
                    }, effectiveSessionId).ConfigureAwait(false);

                    string objectId = resolveResult?.@object?.objectId;

                    if (!string.IsNullOrEmpty(objectId))
                    {
                        // 2. Call getBoundingClientRect directly on that object
                        var rectResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
                        {
                            objectId = objectId,
                            functionDeclaration = "function() { const r = this.getBoundingClientRect(); return {x: r.left, y: r.top, width: r.width, height: r.height}; }",
                            contextId = CurrentContextId,
                            returnByValue = true
                        }, effectiveSessionId).ConfigureAwait(false);

                        if (rectResult?.result?.value != null)
                        {
                            var r = rectResult.result.value;
                            if (0 < (int)r.width && 0 < (int)r.height)
                                // relative to the viewport of the document the element sits in
                                return new Rectangle((int)r.x, (int)r.y, (int)r.width, (int)r.height);
                        }
                    }
                }
                catch { }
            }

            // === main page or fallback ===
            Point pageOffset = Point.Empty;

            try
            {
                pageOffset = new Point(await WindowPageOffsetX(effectiveSessionId).ConfigureAwait(false),
                                       await WindowPageOffsetY(effectiveSessionId).ConfigureAwait(false));
            }
            catch (Exception)
            {
                // If the scroll offset cannot be read, use 0.
            }

            try
            {
                // getBoxModel takes the backend id, which lasts as long as the element; a node id can go stale
                dynamic box = await SendCommand<dynamic>(DevToolsMethods.DOMGetBoxModel, new { backendNodeId }, effectiveSessionId).ConfigureAwait(false);

                BoxModelResponse boxModel = ((JObject)box)?.ToObject<BoxModelResponse>();

                if (boxModel?.Model != null)
                {
                    // DOM.getBoxModel returns document coordinates; subtract the scroll offset to get viewport
                    // coordinates like the branch above. The offset is read in its own try, so a failure leaves it
                    // at 0.
                    return new Rectangle(
                        (int)boxModel.Model.Content[0] - pageOffset.X,
                        (int)boxModel.Model.Content[1] - pageOffset.Y,
                        (int)boxModel.Model.Width,
                        (int)boxModel.Model.Height
                    );
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                    $"DOM.getBoxModel failed",
                    this, GPALObjectType.PuppeteerCommunicator, ex);
            }

            return new Rectangle(-1, -1, -1, -1);
        }

        // Implementation for "get-content-and-css"
        /// <summary>
        /// Retrieves the inner HTML content and computed CSS styles of the element with the given DOM id.
        /// </summary>
        /// <param name="elementId">The HTML "id" attribute value of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the raw response from the Runtime.evaluate command, containing the element's content and CSS as a JSON string.</returns>
        public async Task<dynamic> GetContentAndCss(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new {
                    expression = $"let el = document.getElementById('{elementId}'); JSON.stringify({{content: el.innerHTML, css: getComputedStyle(el)}})",
                    contextId = CurrentContextId,
                    returnByValue = false
                }, sessionId).ConfigureAwait(false);
        }
        /// <summary>
        /// Retrieves a filtered set of computed CSS properties (layout/visibility/positioning relevant properties)
        /// for the element identified by the given backend node id, read with getComputedStyle on the element.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to a dictionary of relevant, non-default CSS property names and values, or null on failure.</returns>
        public async Task<Dictionary<string, object>> GetCssAttributes(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            Dictionary<string, object> retVal = null;

            string[] relevantProps =
            {
                "display","visibility","opacity","pointer-events","overflow","clip-path",
                "position","top","left","right","bottom","z-index",
                "width","height","min-width","min-height","max-width","max-height",
                "margin","margin-top","margin-right","margin-bottom","margin-left",
                "padding","padding-top","padding-right","padding-bottom","padding-left",
                "border","border-width","border-style","border-color","box-sizing",
                "flex","flex-direction","flex-wrap","flex-grow","flex-shrink","flex-basis",
                "justify-content","align-items","align-self","order",
                "grid","grid-template","grid-area","grid-column","grid-row",
                "transform","transform-origin","transition","animation",
                "cursor","user-select","content","filter"
            };

            var emptyValues = new HashSet<string> { "", "none", "normal", "inherit", "initial", "unset", "auto", "0px", "0", "transparent" };

            // Properties that say whether the element shows or takes clicks keep every value, so display none,
            // opacity 0, ancestor-opacity 0 and pointer-events none reach the visible and clickable checks
            var stateProps = new HashSet<string> { "display", "visibility", "opacity", "ancestor-opacity", "pointer-events" };

            // Read on the live element, the way the page computes it, rather than through CSS.getComputedStyleForNode,
            // which takes a node id that goes stale whenever DOM tracking is switched or reset
            dynamic styles = await CallOnElement(backendNodeId, @"
                    function(names) {
                        const style = getComputedStyle(this);
                        const found = {};
                        for (const name of names) found[name] = style.getPropertyValue(name);

                        // Opacity does not pass down to children, so an ancestor with opacity 0 is reported as
                        // ancestor-opacity 0, which hides the element too; opacity keeps the element's own value.
                        // The walk goes through slots and out of shadow roots to their hosts
                        let opacityNode = this.assignedSlot || this.parentElement || this.getRootNode().host;
                        while (opacityNode && found['ancestor-opacity'] !== '0') {
                            if (getComputedStyle(opacityNode).getPropertyValue('opacity').trim() === '0') found['ancestor-opacity'] = '0';
                            opacityNode = opacityNode.assignedSlot || opacityNode.parentElement || opacityNode.getRootNode().host;
                        }
                        return found;
                    }", sessionId, (object)relevantProps).ConfigureAwait(false);   // one argument, the whole list

            if (styles is Newtonsoft.Json.Linq.JObject styleObject)
            {
                retVal = new Dictionary<string, object>();

                foreach (var style in styleObject.Properties())
                {
                    string value = style.Value?.ToString()?.Trim();

                    if (false == string.IsNullOrEmpty(value) && (true == stateProps.Contains(style.Name) || false == emptyValues.Contains(value)))
                        retVal[style.Name] = value;
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Retrieved [{retVal.Count}] filtered CSS attributes for backendNodeId [{backendNodeId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No CSS could be read for element [{backendNodeId}]; it is no longer on the page", this, GPALObjectType.PuppeteerCommunicator);

            return retVal;
        }
        //public async Task<Dictionary<string, object>> GetCssAttributes(int backendNodeId, string sessionId = null)
        //{
        //    sessionId ??= GetEffectiveSessionId();

        //    try
        //    {
        //        var nodeId = await GetNodeIdFromBackendNodeId(backendNodeId, sessionId);

        //        await SendCommand<object>(DevToolsMethods.DOMEnable, new { }, sessionId);
        //        await SendCommand<object>(DevToolsMethods.CSSEnable, new { }, sessionId);   // required; it makes the reader log many unhandled messages for added elements

        //        // Send CSS.getComputedStyleForNode
        //        var result = await SendCommand<dynamic>(DevToolsMethods.CSSGetComputedStyleForNode, new
        //        {
        //            nodeId
        //        }, sessionId);


        //        if (result?.computedStyle == null)
        //        {
        //            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CSS.getComputedStyleForNode returned null for nodeId [{nodeId}]", this, GPALObjectType.PuppeteerCommunicator);
        //            return null;
        //        }

        //        // Process computedStyle array
        //        var computedStyles = new Dictionary<string, object>();
        //        foreach (var style in result.computedStyle)
        //            computedStyles[style.name?.ToString()] = style.value?.ToString();

        //        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Retrieved {computedStyles.Count} CSS attributes for nodeId [{nodeId}]", this, GPALObjectType.PuppeteerCommunicator);
        //        return computedStyles;
        //    }
        //    catch (Exception ex)
        //    {
        //        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get CSS attributes for backendNodeId [{backendNodeId}]: {ex.Message}", this, GPALObjectType.PuppeteerCommunicator, ex);
        //        return null;
        //    }
        //    finally
        //    {
        //        await SendCommand<object>(DevToolsMethods.CSSDisable, new { }, sessionId);
        //        await SendCommand<object>(DevToolsMethods.DOMDisable, new { }, sessionId);
        //    }
        //}
        // Returns the same attributes EvaluateSelector reads when it finds the element
        /// <summary>
        /// Retrieves all DOM attributes (name/value pairs) of the element identified by the given backend node id,
        /// read from the element's attributes.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to a dictionary of attribute names and values, or null on failure.</returns>
        public async Task<Dictionary<string, object>> GetDomAttributes(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            Dictionary<string, object> retVal = null;

            // Read on the live element; DOM.getAttributes takes a node id, which goes stale
            dynamic attributes = await CallOnElement(backendNodeId, @"
                    function() {
                        const found = {};
                        for (const attribute of this.attributes) found[attribute.name] = attribute.value;
                        return found;
                    }", sessionId).ConfigureAwait(false);

            if (attributes is Newtonsoft.Json.Linq.JObject attributeObject)
            {
                retVal = attributeObject.Properties().ToDictionary(a => a.Name, a => (object)a.Value?.ToString());

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Retrieved [{retVal.Count}] DOM attributes for backendNodeId [{backendNodeId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No attributes could be read for element [{backendNodeId}]; it is no longer on the page", this, GPALObjectType.PuppeteerCommunicator);

            return retVal;
        }
        /// <summary>
        /// Retrieves DOM node info and JavaScript-side own properties of the element identified by the given backend node id,
        /// via DOM.describeNode and Runtime.getProperties.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to a dictionary combining node attributes and own JavaScript property values, or null on failure.</returns>
        public async Task<Dictionary<string, object>> GetDomProperties(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            try
            {
                // Resolve the node to a JS object so we can run the same curated script the template
                // (ElementHelper.GetDomProperties) and OttoMagic use - not an attribute/property dump.
                var resolve = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new { backendNodeId, pierce = true }, sessionId).ConfigureAwait(false);
                string objectId = resolve.@object.objectId;

                // Same key set and truthiness guards as ElementHelper.GetDomProperties
                var jsProps = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
                {
                    objectId,
                    functionDeclaration = @"
                            function() {
                                const props = {};
                                if ('textContent' in this) props.textContent = this.textContent;
                                if ('innerHTML' in this) props.innerHTML = this.innerHTML;
                                if ('innerText' in this) props.innerText = this.innerText;
                                if ('outerHTML' in this) props.outerHTML = this.outerHTML;
                                if ('className' in this) props.className = this.className;
                                if ('disabled' in this) props.disabled = this.disabled;
                                return props;
                            }",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                // Release objectId to prevent leak
                await SendCommand<dynamic>(DevToolsMethods.RuntimeReleaseObject,
                    new {
                        contextId = CurrentContextId,
                        objectId
                    }, sessionId).ConfigureAwait(false);

                var properties = new Dictionary<string, object>();
                if (jsProps?.result?.value != null)
                    properties = ((Newtonsoft.Json.Linq.JObject)jsProps.result.value).ToObject<Dictionary<string, object>>();

                // Normalize null -> "" (consistent with ElementHelper.GetDomProperties)
                properties = properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? "");

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Retrieved [{properties.Count}] DOM properties for backendNodeId [{backendNodeId}]", this, GPALObjectType.PuppeteerCommunicator);
                return properties;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to get DOM properties for backendNodeId [{backendNodeId}]", this, GPALObjectType.PuppeteerCommunicator, ex);
                return null;
            }
        }
        // pure javascript, message is too large from browser and cdp / websocket hangs
        /*
        public async Task<dynamic> GetCssAttributes(string selector, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            if (string.IsNullOrEmpty(selector))
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            var jsExpression = $@"
                                (function() {{
                                    try {{
                                        console.log('get css attrs');
                                        // Helper to find the first matching element in a context
                                        function findFirstElement(sel, context = document) {{
                                            // Try CSS selector
                                            try {{
                                        console.log('css');
                                                const element = context.querySelector(sel);
                                                if (element) {{
                                        console.log('found css');
                                                    return element;
                                                }}
                                            }} catch (e) {{
                                        console.log('error ' + e);
                                            }}

                                            // Try XPath
                                            try {{
                                        console.log('xpath');
                                                const result = document.evaluate(sel, context, null, 9, null);
                                                if (result.singleNodeValue) {{
                                        console.log('found xpath');
                                                    return result.singleNodeValue;
                                                }}
                                            }} catch (e) {{
                                        console.log('error ' + e);
                                            }}

                                            return null;
                                        }}

                                        console.log('find first');
                                        // Search in main document
                                        let element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}');

                                        // Search in shadow DOM if no element found
                                        if (!element) {{
                                        console.log('try shadowdom');
                                            const shadowHosts = document.querySelectorAll('*');
                                            var shadowCount = 0;
                                            for (const host of shadowHosts) {{
                                                if (host.shadowRoot) {{
                                        console.log('searching shadow root' + shadowCount++);
                                                    element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}', host.shadowRoot);
                                                    if (element) break;
                                                }}
                                            }}
                                        }}

                                        // Search in iframes if no element found
                                        if (!element) {{
                                        console.log('try iframes');
                                            const iframes = document.querySelectorAll('iframe');
                                            var iframeCount = 0;
                                            for (const iframe of iframes) {{
                                                try {{
                                                    if (iframe.contentDocument) {{
                                        console.log('searching iframe' + iframeCount++);
                                                        element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}', iframe.contentDocument);
                                                        if (element) break;
                                                    }}
                                                }} catch (e) {{
                                                    // Ignore iframe access errors (e.g., cross-origin)
                                                }}
                                            }}
                                        }}

                                        // If no element found, return empty object
                                        if (!element) {{
                                        console.log('no element found');
                                            return {{}};
                                        }}

                                        console.log('get computed style');
                                        // Get computed styles for the element
                                        const styles = window.getComputedStyle(element);
                                        const props = {{}};
                                        for (let i = 0; i < styles.length; i++) {{
                                            const prop = styles[i];
                                            const value = styles.getPropertyValue(prop);
                                            if (value && value !== 'none') {{
                                                props[prop] = value;
                                            }}
                                        }}
                                        console.log('return props ' + props);
                                        return props;
                                    }} catch (e) {{
                                        console.error('Error in GetCssAttributes:', e);
                                        return {{}};
                                    }}
                                }})()";

            var jsProps = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
            {
                expression = jsExpression,
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Got jsprops [{jsProps}]");

            return jsProps?.result?.value != null
                ? ((Newtonsoft.Json.Linq.JObject)jsProps.result.value).ToObject<Dictionary<string, object>>()
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        public async Task<dynamic> GetDomAttributes(string selector, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var jsExpression = $@"
                                (function() {{
                                    try {{
                                        console.log('get dom attrs');
                                        // Helper to find the first matching element in a context
                                        function findFirstElement(sel, context = document) {{
                                            // Try CSS selector
                                            try {{
                                                const element = context.querySelector(sel);
                                                if (element) return element;
                                            }} catch (e) {{
                                                // Invalid CSS selector; try XPath
                                            }}

                                            // Try XPath
                                            try {{
                                                const result = document.evaluate(sel, context, null, 9, null);
                                                if (result.singleNodeValue) return result.singleNodeValue;
                                            }} catch (e) {{
                                                // Invalid XPath; continue to shadow DOM/iframes
                                            }}

                                            return null;
                                        }}

                                        // Search in main document
                                        let element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}');

                                        // Search in shadow DOM if no element found
                                        if (!element) {{
                                            const shadowHosts = document.querySelectorAll('*');
                                            for (const host of shadowHosts) {{
                                                if (host.shadowRoot) {{
                                                    element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}', host.shadowRoot);
                                                    if (element) break;
                                                }}
                                            }}
                                        }}

                                        // Search in iframes if no element found
                                        if (!element) {{
                                            const iframes = document.querySelectorAll('iframe');
                                            for (const iframe of iframes) {{
                                                try {{
                                                    if (iframe.contentDocument) {{
                                                        element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}', iframe.contentDocument);
                                                        if (element) break;
                                                    }}
                                                }} catch (e) {{
                                                    // Ignore iframe access errors (e.g., cross-origin)
                                                }}
                                            }}
                                        }}

                                        // If no element found, return empty object
                                        if (!element) return {{}};

                                        // Get DOM attributes for the element
                                        const attributes = element.attributes;
                                        const attrs = {{}};
                                        for (let i = 0; i < attributes.length; i++) {{
                                            const name = attributes[i].name;
                                            const value = attributes[i].value;
                                            if (name && value) {{
                                                attrs[name] = value;
                                            }}
                                        }}
                                        return attrs;
                                    }} catch (e) {{
                                        console.error('Error in GetDomAttributes:', e);
                                        return {{}};
                                    }}
                                }})()";

            var jsProps = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
            {
                expression = jsExpression,
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Got jsprops [{jsProps}]");

            return jsProps?.result?.value != null
                ? ((Newtonsoft.Json.Linq.JObject)jsProps.result.value).ToObject<Dictionary<string, object>>()
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        public async Task<dynamic> GetDomProperties(string selector, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var jsExpression = $@"
                                (function() {{
                                    try {{
                                        //console.log('get dom props');
                                        // Helper to find the first matching element in a context
                                        function findFirstElement(sel, context = document) {{
                                            // Try CSS selector
                                            try {{
                                        //console.log('css');
                                                const element = context.querySelector(sel);
                                                if (element) {{
                                        //console.log('found css');
                                                    return element;
                                                }}
                                            }} catch (e) {{
                                        //console.log('error ' + e);
                                            }}

                                            // Try XPath
                                            try {{
                                        //console.log('xpath');
                                                const result = document.evaluate(sel, context, null, 9, null);
                                                if (result.singleNodeValue) {{
                                                    return result.singleNodeValue;
                                                }}
                                            }} catch (e) {{
                                        //console.log('error ' + e);
                                            }}

                                            return null;
                                        }}

                                        // Search in main document
                                        let element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}');

                                        // Search in shadow DOM if no element found
                                        if (!element) {{
                                            const shadowHosts = document.querySelectorAll('*');
                                            let hostCount = 0;
                                            for (const host of shadowHosts) {{
                                        //console.log('searching shadow host ' + hostCount);
                                                hostCount++;
                                                if (host.shadowRoot) {{
                                                    element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}', host.shadowRoot);
                                                    if (element) {{
                                                        break;
                                                    }}
                                                }}
                                            }}
                                        }}

                                        // Search in iframes if no element found
                                        if (!element) {{
                                            const iframes = document.querySelectorAll('iframe');
                                            let iframeCount = 0;
                                            for (const iframe of iframes) {{
                                                iframeCount++;
                                                try {{
                                                    if (iframe.contentDocument) {{
                                        //console.log('searching iframe ' + iframeCount);
                                                        element = findFirstElement('{selector.Replace("'", "\\'").Replace("\\", "\\\\")}', iframe.contentDocument);
                                                        if (element) {{
                                                            break;
                                                        }}
                                                    }}
                                                }} catch (e) {{
                                        //console.log('error ' + e);
                                                }}
                                            }}
                                        }}

                                        // If no element found, return empty object
                                        if (!element) {{
                                            return {{}};
                                        }}

                                        // Get DOM properties for the element
                                        //console.log('get dom props 2');
                                        const props = {{}};
                                        if (element.tagName) props.tagName = element.tagName;
                                        if (element.textContent) props.textContent = element.textContent;
                                        if (element.value != null && element.value !== '') props.value = element.value;
                                        if (element.innerHTML) props.innerHTML = element.innerHTML;
                                        if (element.outerHTML) props.outerHTML = element.outerHTML;
                                        if (element.className) props.className = element.className;
                                        if (element.id) props.id = element.id;
                                        //console.log('return props');
                                        return props;
                                    }} catch (e) {{
                                        return {{}};
                                    }}
                                }})()";

            var jsProps = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
            {
                expression = jsExpression,
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Got jsprops [{jsProps}]");

            return jsProps?.result?.value != null
                ? ((Newtonsoft.Json.Linq.JObject)jsProps.result.value).ToObject<Dictionary<string, object>>()
                : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        */


        // Implementation for "get-element-attribute-hash"
        /// <summary>
        /// Retrieves a JSON representation of all attributes of the element with the given DOM id.
        /// </summary>
        /// <param name="elementId">The HTML "id" attribute value of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the raw response from the Runtime.evaluate command, containing the element's attributes as a JSON string.</returns>
        public async Task<dynamic> GetElementAttributeHash(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new
                {
                    expression = "JSON.stringify(document.getElementById('" + elementId + "').attributes)",
                    contextId = CurrentContextId,
                    returnByValue = false
                }, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves the page's document source as raw HTML/XML text, by fetching the root document and its outer HTML
        /// via DOM.getDocument and DOM.getOuterHTML. The source is returned raw; normalization (unescaping and
        /// stripping Chrome's XML-viewer wrapper) is applied uniformly for all engines in Browser.GetPageSource.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the page source as a string, or an empty string if it could not be retrieved.</returns>
        /// <example>
        /// <code>
        /// string html = await communicator.GetPageSource();
        /// </code>
        /// </example>
        public async Task<string> GetPageSource(string sessionId = null)
        {
            if (sessionId == null)
            {
                sessionId = GetEffectiveSessionId();
            }

            // -- 1. Get root document ---------------------------------------------
            var docResult = await SendCommand(
                DevToolsMethods.DOMGetDocument,
                new Dictionary<string, object> { { "depth", 0 } },
                sessionId).ConfigureAwait(false);

            if (docResult == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "DOM.getDocument returned null",
                    this, GPALObjectType.PuppeteerCommunicator);
                return string.Empty;
            }

            // Normalize to JObject
            JObject rootDoc = docResult as JObject;
            if (rootDoc == null)
            {
                try
                {
                    string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(docResult);
                    rootDoc = JObject.Parse(jsonStr);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        "Failed to convert DOM.getDocument response to JObject: [" + ex.Message + "]",
                        this, GPALObjectType.PuppeteerCommunicator);
                    return string.Empty;
                }
            }

            // Find root (direct or wrapped in "result")
            JToken rootToken = rootDoc["root"] ?? rootDoc["result"]?["root"];

            if (rootToken == null || rootToken.Type != JTokenType.Object)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    "No valid 'root' object found in DOM.getDocument response",
                    this, GPALObjectType.PuppeteerCommunicator);
                return string.Empty;
            }

            JObject root = (JObject)rootToken;

            if (!long.TryParse(root["nodeId"]?.ToString(), out long rootNodeId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Invalid or missing nodeId in root",
                    this, GPALObjectType.PuppeteerCommunicator);
                return string.Empty;
            }

            // -- 2. Get outerHTML -------------------------------------------------
            var htmlResult = await SendCommand(
                DevToolsMethods.DOMGetOuterHTML,
                new Dictionary<string, object> { { "nodeId", rootNodeId } },
                sessionId).ConfigureAwait(false);

            if (htmlResult == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "DOM.getOuterHTML returned null",
                    this, GPALObjectType.PuppeteerCommunicator);
                return string.Empty;
            }

            // Normalize htmlResult to JObject
            JObject htmlDoc = htmlResult as JObject;
            if (htmlDoc == null)
            {
                try
                {
                    string jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(htmlResult);
                    htmlDoc = JObject.Parse(jsonStr);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        "Failed to convert DOM.getOuterHTML response to JObject: [" + ex.Message + "]",
                        this, GPALObjectType.PuppeteerCommunicator);
                    return string.Empty;
                }
            }

            // Extract the actual source string
            string fullSource = htmlDoc["outerHTML"]?.ToString();

            if (string.IsNullOrWhiteSpace(fullSource))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "outerHTML was empty or missing",
                    this, GPALObjectType.PuppeteerCommunicator);
                return string.Empty;
            }

            // Return the raw page source. Browser.GetPageSource normalizes it for every engine: it JS-unescapes it
            // and strips Chrome's XML-viewer wrapper when a file is displayed.
            return fullSource;
        }

        // Implementation for "hide-element"
        /// <summary>
        /// Hides the element identified by the given backend node id by setting its inline style to "display: none".
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element to hide.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the result of setting the element's "style" attribute.</returns>
        public async Task<dynamic> HideElement(int backendNodeId, string sessionId = null)
        {
            return await SetAttribute(backendNodeId, "style", "display: none", sessionId).ConfigureAwait(false);
        }

        // Implementation for "is-clickable"
        /// <summary>
        /// Determines whether the element with the given DOM id is clickable, using the browser's <c>checkVisibility()</c> API.
        /// </summary>
        /// <param name="elementId">The HTML "id" attribute value of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the element is clickable/visible, otherwise false.</returns>
        /// <example>
        /// <code>
        /// bool clickable = await communicator.IsClickable("submitButton");
        /// </code>
        /// </example>
        public async Task<bool> IsClickable(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            List<GPALElement> elems = await EvaluateSelector(elementId, sessionId).ConfigureAwait(false);
            List<dynamic> responses = new List<dynamic>();

            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = "document.getElementById('" + elementId + "').checkVisibility()" 
                }, sessionId).ConfigureAwait(false);
            return (bool)result.value;
        }

        // Implementation for "is-displayed"
        /// <summary>
        /// Determines whether the element with the given DOM id is displayed, by checking that its <c>offsetParent</c> is not null.
        /// </summary>
        /// <param name="elementId">The HTML "id" attribute value of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the element is displayed, otherwise false.</returns>
        /// <example>
        /// <code>
        /// bool visible = await communicator.IsDisplayed("statusBanner");
        /// </code>
        /// </example>
        public async Task<bool> IsDisplayed(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = "document.getElementById('" + elementId + "').offsetParent !== null" 
                }, sessionId).ConfigureAwait(false);
            return (bool)result.value;
        }

        // Implementation for "is-enabled"
        /// <summary>
        /// Determines whether the element with the given DOM id is enabled, by checking that its <c>disabled</c> property is false.
        /// </summary>
        /// <param name="elementId">The HTML "id" attribute value of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the element is enabled, otherwise false.</returns>
        /// <example>
        /// <code>
        /// bool enabled = await communicator.IsEnabled("submitButton");
        /// </code>
        /// </example>
        public async Task<bool> IsEnabled(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = "!document.getElementById('" + elementId + "').disabled" 
                }, sessionId).ConfigureAwait(false);
            return (bool)result.value;
        }

        /// <summary>
        /// Determines whether one or more elements are fully within the current viewport bounds,
        /// based on their bounding client rectangles compared against the window's inner dimensions.
        /// </summary>
        /// <param name="elemsOrBackendNodeId">Either a <see cref="List{GPALElement}"/> of elements to check, or a single backend node id.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the element(s) are within the viewport (or if their bounding rectangle could not be determined, fail-forward true), otherwise false.</returns>
        public async Task<bool> IsVisibleInViewport(dynamic elemsOrBackendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
            {
                expression = @"({ 
                            height: window.innerHeight || document.documentElement.clientHeight, 
                            width:  window.innerWidth  || document.documentElement.clientWidth 
                        })",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            int viewportHeight = 0;
            int viewportWidth = 0;
            bool isInViewport = true;
            if (result?.result?.value != null)
            {
                var dict = ((Newtonsoft.Json.Linq.JObject)result.result.value).ToObject<Dictionary<string, int>>();
                viewportHeight = dict["height"];
                viewportWidth = dict["width"];
            }

            if (elemsOrBackendNodeId is List<GPALElement>)
                foreach (GPALElement element in elemsOrBackendNodeId)
                {
                    Rectangle rect = await GetBoundingClientRect(element.ElementBackendNodeId, sessionId).ConfigureAwait(false);
                    // the list holds the one element GPAL checks; PuppeteerClient.IsVisibleInViewport wraps it
                    if (-1 == rect.X)   // CAVEAT: an element with no box model counts as in the viewport
                        isInViewport = true;
                    else
                        isInViewport &= rect.Top >= 0 &&
                                   rect.Left >= 0 &&
                                   rect.Bottom <= viewportHeight &&
                                   rect.Right <= viewportWidth;
                }
            else
            {
                Rectangle rect = await GetBoundingClientRect(elemsOrBackendNodeId, sessionId).ConfigureAwait(false);
                if (-1 == rect.X)   // CAVEAT: an element with no box model counts as in the viewport
                    isInViewport = true;
                else
                    isInViewport &= rect.Top >= 0 &&
                           rect.Left >= 0 &&
                           rect.Bottom <= viewportHeight &&
                           rect.Right <= viewportWidth;
            }
            return isInViewport;
        }

        // Implementation for "set-attribute"
        /// <summary>
        /// Sets the value of an HTML attribute on the element identified by <paramref name="backendNodeId"/>.
        /// Implementation for the "set-attribute" workflow action.
        /// </summary>
        /// <param name="backendNodeId">CDP backend node ID of the target element.</param>
        /// <param name="attribute">Name of the attribute to set.</param>
        /// <param name="value">Value to assign to the attribute.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result of the attribute-set command.</returns>
        public async Task<dynamic> SetAttribute(int backendNodeId, string attribute, string value, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Set attribute [{attribute}] to [{value}] on backendNodId [{backendNodeId}]", this, GPALObjectType.PuppeteerCommunicator);

            // Set on the live element; DOM.setAttributeValue takes a node id, which goes stale
            dynamic retVal = await CallOnElement(backendNodeId, @"
                    function(name, value) {
                        this.setAttribute(name, value);
                        return true;
                    }", sessionId, attribute, value).ConfigureAwait(false);

            return retVal;
        }
    }
}
