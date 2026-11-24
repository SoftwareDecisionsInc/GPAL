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
        /// <summary>
        /// Queries the document for the first element matching the given CSS selector.
        /// </summary>
        /// <param name="selector">A CSS selector to match against the document.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the matching DOM node id, or 0 if no element was found.</returns>
        public async Task<int> QueryByCss(string selector, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            //await WaitForEvent("Page.loadEventFired", TimeSpan.FromSeconds(10));

            var doc = await SendCommand<object>(DevToolsMethods.DOMGetDocument, new { depth = 0 }, sessionId).ConfigureAwait(false);
            var result = await SendCommand<object>(DevToolsMethods.DOMQuerySelector, new { nodeId = (int)doc.root.nodeId, selector }, sessionId).ConfigureAwait(false);

            var nodeId = (int)result.nodeId;

            if (nodeId == 0)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No nodes found for Css: [{selector}]", this, GPALObjectType.PuppeteerCommunicator);

            return nodeId;
        }
        public class Node
        {
            /// <summary>
            /// The CDP remote object id of the resolved DOM node.
            /// </summary>
            public string ObjectId { get; set; }
            /// <summary>
            /// The CDP backend node id of the resolved DOM node.
            /// </summary>
            public int BackendNodeId { get; set; }
            /// <summary>
            /// The CDP DOM node id of the resolved DOM node.
            /// </summary>
            public int NodeId { get; set; }
        }

        /// <summary>
        /// Resolves an iframe element matching the given selector and switches the current evaluation context
        /// into that iframe by attaching to its frame target (for cross-origin frames) and creating an isolated
        /// JavaScript execution world. Subsequent DOM/JS operations will operate inside the iframe until
        /// the context is reset.
        /// </summary>
        /// <param name="frameSelector">A CSS or XPath selector identifying the iframe element.</param>
        public async Task SwitchToFrame(string frameSelector)
        {
            // Cleared on a frame switch. A remote object from the old context finds nothing, which looks like a bad
            // selector.
            CurrentRootObjectId = null;


            var elements = await EvaluateSelector(frameSelector, isRecursive: true).ConfigureAwait(false);
            if (elements.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Frame not found: " + frameSelector, this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            var iframeElement = elements[0];

            var describe = await SendCommand<dynamic>(DevToolsMethods.DOMDescribeNode, new
            {
                objectId = iframeElement.ElementHandle,
                pierce = true
            }, GetEffectiveSessionId()).ConfigureAwait(false);

            string frameId = describe?.node?.frameId?.ToString();

            if (string.IsNullOrEmpty(frameId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No frameId for frame [" + frameSelector + "]", this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            CurrentFrameId = frameId;

            // drop any session left over from a previously resolved iframe; a frame that is not a target of its own
            // gets no session below
            CurrentFrameSessionId = null;

            // only an out-of-process frame is a target of its own; any other frame is reached through the page session
            var targets = await SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
            bool frameIsTarget = null != targets?.targetInfos
                && ((IEnumerable<dynamic>)targets.targetInfos).Any(t => frameId == (string)t.targetId);

            if (true == frameIsTarget)
            {
                var attach = await SendCommand<dynamic>(DevToolsMethods.TargetAttachToTarget, new
                {
                    targetId = frameId,
                    flatten = true
                }, null).ConfigureAwait(false);

                string frameSessionId = attach?.sessionId?.ToString();
                if (!string.IsNullOrEmpty(frameSessionId))
                    CurrentFrameSessionId = frameSessionId;
            }

            // Always create isolated world (works for both same-origin and cross-origin)
            await Task.Delay(300).ConfigureAwait(false);

            var isolated = await SendCommand<dynamic>(DevToolsMethods.PageCreateIsolatedWorld, new
            {
                frameId = frameId,
                worldName = "gpal_isolated_" + Guid.NewGuid().ToString("N"),
                grantUniversalAccess = false
            // a cross-origin frame lives in its own target, so the world has to be created on that target's
            // session - the page session does not know the frame at all. same-origin falls back to the page.
            }, GetEffectiveSessionId()).ConfigureAwait(false);

            CurrentContextId = isolated?.executionContextId?.ToObject<long?>();

            if (CurrentContextId.HasValue)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                    "Resolved iframe [" + frameSelector + "] | FrameId: " + frameId + " | ContextId: " + CurrentContextId.Value,
                    this, GPALObjectType.PuppeteerCommunicator);
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create isolated world for " + frameSelector, this, GPALObjectType.PuppeteerCommunicator);
            }
        }

        /// <summary>
        /// Scopes later selectors to the element matching <paramref name="elementSelector"/>, stored in
        /// CurrentRootObjectId since CDP has no current element.
        /// <br/><br/>
        /// Not for iframes: use <see cref="SwitchToFrame"/>, since a frame needs its own execution context and a
        /// cross-origin iframe is not reachable as a node.
        /// </summary>
        /// <param name="elementSelector">A CSS or XPath selector identifying the element to search inside.</param>
        public async Task SwitchToElement(string elementSelector)
        {
            var found = await EvaluateSelector(elementSelector, isRecursive: true).ConfigureAwait(false);

            if (0 == found.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Element to search inside not found: " + elementSelector, this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            // Later selectors call querySelector on this object. SwitchToShadowDom stores the shadowRoot instead.
            CurrentRootObjectId = found[0].ElementHandle;

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                "Searching inside [" + elementSelector + "] | ObjectId: " + CurrentRootObjectId,
                this, GPALObjectType.PuppeteerCommunicator);
        }

        public async Task SwitchToShadowDom(string shadowHostSelector)
        {
            var hosts = await EvaluateSelector(shadowHostSelector, isRecursive: true).ConfigureAwait(false);
            if (hosts.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No shadow host found for selector [{shadowHostSelector}]", this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            var host = hosts[0];
            var s = GetEffectiveSessionId();

            var describe = await SendCommand<dynamic>(DevToolsMethods.DOMDescribeNode, new
            {
                objectId = host.ElementHandle,
                pierce = true,
                depth = 1
            }, s).ConfigureAwait(false);

            var shadowRoots = describe?.node?.shadowRoots as IEnumerable<dynamic>;

            if (shadowRoots?.Any() != true)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "No shadow root on host: " + shadowHostSelector, this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            var shadowRoot = shadowRoots.First();

            if (shadowRoot.backendNodeId != null)
            {
                var resolve = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new
                {
                    backendNodeId = (long)shadowRoot.backendNodeId
                }, s).ConfigureAwait(false);

                if (resolve?.@object?.objectId != null)
                {
                    CurrentRootObjectId = (string)resolve.@object.objectId;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                "Resolved shadow DOM [" + shadowHostSelector + "] | ObjectId: " + CurrentRootObjectId,
                this, GPALObjectType.PuppeteerCommunicator);
        }
        
        /// <summary>
        /// Resolves a CSS selector, XPath expression, CDP node id, or remote object id to a list of matching
        /// <see cref="GPALElement"/> instances, including their attributes, bounding rectangle, CSS path and XPath.
        /// Handles shadow DOM contexts (via <see cref="SwitchToShadowDom"/>) and isolated iframe contexts
        /// (via <see cref="SwitchToFrame"/>). If nothing is found and the call is not recursive, it performs
        /// a deep search by recursively evaluating the selector inside any iframe targets.
        /// </summary>
        /// <param name="selector">A CSS selector, XPath expression, numeric node id, or CDP remote object id.</param>
        /// <param name="sessionId">CDP session id; the effective session (or current session, if inside an isolated iframe context) is used if null.</param>
        /// <param name="isRecursive">True when this call is a recursive deep search inside an iframe; suppresses the outer iframe search and the evaluation semaphore.</param>
        /// <returns>Task that resolves to the list of matching elements, or an empty list if none were found or an error occurred.</returns>
        public async Task<List<GPALElement>> EvaluateSelector(string selector, string sessionId = null, bool isRecursive = false)
        {
            var elements = new List<GPALElement>();

            // A passed session (recursive iframe search) wins; otherwise the effective session. The isolated world's
            // context id only works on the session that created it: the frame's own for cross-origin, the page's for
            // same-origin.
            string effectiveSessionId = (!string.IsNullOrEmpty(sessionId) && sessionId != GetCurrentSessionId())
                ? sessionId
                : GetEffectiveSessionId();

            if (false == isRecursive)
                await _evaluateSelectorSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                var nodes = new List<Node>();

                bool inRootContext = !string.IsNullOrEmpty(CurrentRootObjectId);

                // When we have an isolated world (from ResolveIframe), use MAIN session for runtime commands
                // but keep effectiveSessionId for DOM commands when finding child iframes
                bool useIsolatedContext = CurrentContextId.HasValue;

                if (inRootContext)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                        "Evaluating inside the scope set by InShadowDom or InElement, for selector [" + selector + "]",
                        this, GPALObjectType.PuppeteerCommunicator);
                }

                try
                {
                    // NodeId / ObjectId handling
                    bool isNodeId = int.TryParse(selector, out int tmpNodeId);
                    bool isObjectId = System.Text.RegularExpressions.Regex.IsMatch(selector, @"^-?\d+\.\d+\.\d+$");

                    // DOM.requestNode below hands out node ids only after DOM.getDocument has been called; it sends the
                    // node and its path itself, so the document node alone is enough. Asked 20 deep, every search
                    // shipped the whole page's tree and threw it away, which made each element search slow on a big page.
                        await SendCommand<object>(DevToolsMethods.DOMGetDocument, new
                        {
                            pierce = true,
                            depth = 0
                        }, effectiveSessionId).ConfigureAwait(false);

                    // === SCOPED QUERY (when .InShadowDom() or .InElement() is active) ===
                    if (inRootContext && !isNodeId && !isObjectId)
                    {
                        try
                        {
                            var shadowQueryResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
                            {
                                objectId = CurrentRootObjectId,
                                functionDeclaration = @"
                                    function(selector) {
                                        let nodes = [];
                                        try {
                                            const cssNodes = this.querySelectorAll(selector);
                                            if (cssNodes?.length > 0) {
                                                nodes = Array.from(cssNodes);
                                            }
                                        } catch (e) {}

                                        if (nodes.length === 0) {
                                            try {
                                                const result = document.evaluate(selector, this, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
                                                for (let i = 0; i < result.snapshotLength; i++) {
                                                    const node = result.snapshotItem(i);
                                                    if (node?.getBoundingClientRect) {
                                                        node.getBoundingClientRect();
                                                        nodes.push(node);
                                                    }
                                                }
                                            } catch (e) {}
                                        }
                                        return { nodes: nodes };
                                    }",
                                arguments = new[] { new { value = selector } },
                                contextId = CurrentContextId,
                                returnByValue = false
                            }, effectiveSessionId).ConfigureAwait(false);

                            if (shadowQueryResult?.result?.objectId != null)
                            {
                                // Get wrapper { nodes: [...] }
                                var wrapper = await SendCommand<dynamic>(DevToolsMethods.RuntimeGetProperties,
                                    new { 
                                        objectId = (string)shadowQueryResult.result.objectId,
                                        contextId = CurrentContextId,
                                        ownProperties = true 
                                    }, effectiveSessionId).ConfigureAwait(false);

                                string nodesArrayId = null;

                                // Find "nodes" property without lambda
                                if (wrapper?.result != null)
                                {
                                    foreach (var p in wrapper.result)
                                    {
                                        if (p.name == "nodes" && p.value?.objectId != null)
                                        {
                                            nodesArrayId = (string)p.value.objectId;
                                            break;
                                        }
                                    }
                                }

                                if (!string.IsNullOrEmpty(nodesArrayId))
                                {
                                    // Get array items
                                    var arrayProps = await SendCommand<dynamic>(DevToolsMethods.RuntimeGetProperties,
                                        new { 
                                            objectId = nodesArrayId,
                                            contextId = CurrentContextId,
                                            ownProperties = true 
                                        }, effectiveSessionId).ConfigureAwait(false);

                                    nodes.Clear();

                                    if (arrayProps?.result != null)
                                    {
                                        foreach (var item in arrayProps.result)
                                        {
                                            var val = item.value;
                                            if (val != null
                                                && val.type == "object"
                                                && val.subtype == "node"
                                                && val.objectId != null)
                                            {
                                                nodes.Add(new Node { ObjectId = (string)val.objectId });
                                            }
                                        }
                                    }
                                }

                                // The scoped query ran, so its answer stands, none included. Falling through searched
                                // the whole document and returned matches from outside the scope.
                                goto ProcessFoundNodes;
                            }
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                                $"Shadow DOM query failed for [{selector}]. Falling back to main path.",
                                this, GPALObjectType.PuppeteerCommunicator, ex);
                        }
                    }

                    if (isNodeId || isObjectId)
                    {
                        var requestNodeResult = await SendCommand<object>(DevToolsMethods.DOMRequestNode, new { objectId = selector }, effectiveSessionId).ConfigureAwait(false);
                        if (requestNodeResult?.nodeId != null)
                        {
                            var nodeId = (int)requestNodeResult.nodeId;
                            var resolveNodeResult = await SendCommand<object>(DevToolsMethods.DOMResolveNode, new { nodeId }, effectiveSessionId).ConfigureAwait(false);
                            if (resolveNodeResult?.@object?.objectId != null)
                            {
                                var describeNode = await SendCommand<object>(DevToolsMethods.DOMDescribeNode, new { objectId = (string)resolveNodeResult.@object.objectId, pierce = true }, effectiveSessionId).ConfigureAwait(false);
                                nodes.Add(new Node
                                {
                                    ObjectId = (string)resolveNodeResult.@object.objectId,
                                    BackendNodeId = describeNode?.node?.backendNodeId ?? 0,
                                    NodeId = nodeId
                                });
                            }
                        }
                    }
                    else
                    {
                        // === UNSCOPED QUERY: the selector as CSS, then as XPath ===
                        string jsFunc = @"
                        function evaluateSelector(selector) {
                            let nodes = [];
                            let type = 'unknown';
                            let error = null;

                            // Try CSS first
                            try {
                                const cssNodes = document.querySelectorAll(selector);
                                if (cssNodes.length > 0) {
                                    nodes = Array.from(cssNodes);
                                    type = 'css';
                                }
                            } catch (e) {}

                            // If CSS failed, try XPath
                            if (nodes.length === 0) {
                                try {
                                    const result = document.evaluate(
                                        selector,
                                        document,
                                        null,
                                        XPathResult.ORDERED_NODE_SNAPSHOT_TYPE,
                                        null
                                    );

                                    // Force CDP to wrap each node by accessing a layout property
                                    for (let i = 0; i < result.snapshotLength; i++) {
                                        const node = result.snapshotItem(i);
                                        if (node && node.getBoundingClientRect) {
                                            // This forces CDP to create a RemoteObject with objectId
                                            node.getBoundingClientRect();
                                            nodes.push(node);
                                        }
                                    }

                                    if (nodes.length > 0) {
                                        type = 'xpath';
                                    } else {
                                        error = 'No elements found for selector [' + selector + ']';
                                    }
                                } catch (e) {
                                    error = 'Invalid XPath or error: ' + e.message;
                                }
                            }

                            return { nodes: nodes };
                        }";

                        string escapedSelector = selector;
                        var evalResult = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                        {
                            expression = $"({jsFunc})(\"{escapedSelector}\")",
                            contextId = CurrentContextId,
                            returnByValue = false
                        }, effectiveSessionId).ConfigureAwait(false);

                        if (evalResult?.result?.objectId != null)
                        {
                            var nodesProps = await SendCommand<object>(DevToolsMethods.RuntimeGetProperties, 
                                new { 
                                    objectId = (string)evalResult.result.objectId,
                                    contextId = CurrentContextId,
                                    ownProperties = true 
                                }, effectiveSessionId).ConfigureAwait(false);
                            dynamic errorProp = null;

                            if (nodesProps?.result != null)
                            {
                                foreach (var p in nodesProps.result)
                                {
                                    if (p.name == "error" && p.value != null && p.value.value != null)
                                    {
                                        errorProp = p;
                                        break;
                                    }
                                }
                            }

                            if (errorProp != null)
                            {
                                string errMsg = (string)errorProp.value.value ?? "Unknown selector error";
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{errMsg}]", this, GPALObjectType.PuppeteerCommunicator);
                                if (false == isRecursive)
                                    return new List<GPALElement>();
                            }

                            foreach (var prop in nodesProps.result)
                            {
                                if (prop.name == "nodes" && prop.value?.objectId != null)
                                {
                                    var nodeArray = await SendCommand<object>(DevToolsMethods.RuntimeGetProperties, 
                                        new { 
                                            objectId = (string)prop.value.objectId,
                                            contextId = CurrentContextId,
                                            ownProperties = true 
                                        }, effectiveSessionId).ConfigureAwait(false);
                                    foreach (var np in nodeArray.result)
                                    {
                                        if (np.value != null && np.value.type == "object" && np.value.subtype == "node")
                                            nodes.Add(new Node { ObjectId = (string)np.value.objectId });
                                    }
                                }
                            }
                        }
                    }

                ProcessFoundNodes:

                    // Read each node's ids, attributes and position, one node at a time
                    foreach (var node in nodes)
                    {
                        try
                        {
                            var attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                            var describeNode = await SendCommand<object>(DevToolsMethods.DOMDescribeNode, new { objectId = node.ObjectId, pierce = true }, effectiveSessionId).ConfigureAwait(false);
                            if (describeNode?.node != null)
                            {
                                node.BackendNodeId = describeNode.node.backendNodeId ?? 0;
                                var requestNodeResult = await SendCommand<object>(DevToolsMethods.DOMRequestNode, new
                                {
                                    objectId = node.ObjectId
                                }, effectiveSessionId).ConfigureAwait(false);

                                node.NodeId = requestNodeResult.nodeId;
                                attributes["tagName"] = describeNode.node.nodeName;
                            }

                            var jsProps = await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
                            {
                                functionDeclaration = @"
                            function() {
                                const rect = this.getBoundingClientRect();
                                const cleanText = (str) => (str || '').replace(/\s+/g, ' ').trim();

                                const attrs = {
                                    enabled: !this.disabled,
                                    // selected covers a checked checkbox or a selected option, as Selenium's
                                    // Selected does.
                                    selected: this.checked === true || this.selected === true,
                                    indeterminate: this.indeterminate === true,
                                    displayed: this.offsetParent !== null,
                                    href: this.href || '',
                                    src: this.src || '',

                                    value: cleanText(this.value),
                                    text: cleanText(this.text || this.innerText),
                                    placeholder: cleanText(this.placeholder),

                                    index: this.index || '',
                                    length: this.files ? this.files.length : this.length || '',
                                    type: this.type || '',
                                    id: this.id || '',
                                };
                                for (let {name,value} of Array.from(this.attributes||[])) attrs[name]=value;

                                // Force boolean properties last - multiple not getting set above, being dropped by cdp?
                                attrs.multiple = this.multiple === true;

                                function getCss(el){
                                    if(!(el instanceof Element)) return '';
                                    const parts=[];
                                    while(el && el.nodeType===1){
                                        let part=el.nodeName.toLowerCase();
                                        if(el.id){part+='#'+el.id; parts.unshift(part); break;}
                                        let sib=el, nth=1;
                                        while((sib=sib.previousElementSibling)!=null){if(sib.nodeName.toLowerCase()===part)nth++;}
                                        if(nth>1) part+=':nth-of-type('+nth+')';
                                        parts.unshift(part); el=el.parentElement;
                                    }
                                    return parts.join(' > ');
                                }
                                return {attributes:attrs,css:getCss(this),boundingRect:{x:rect.x,y:rect.y,width:rect.width,height:rect.height,top:rect.top,left:rect.left,bottom:rect.bottom,right:rect.right}};
                            }",
                                objectId = node.ObjectId,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, effectiveSessionId).ConfigureAwait(false);

                            if (jsProps?.result?.value != null)
                            {
                                var dict = ((Newtonsoft.Json.Linq.JObject)jsProps.result.value).ToObject<Dictionary<string, object>>();

                                if (dict.ContainsKey("attributes"))
                                {
                                    var jsAttrDict = ((Newtonsoft.Json.Linq.JObject)dict["attributes"]).ToObject<Dictionary<string, object>>();
                                    foreach (var kvp in jsAttrDict) if (!attributes.ContainsKey(kvp.Key)) attributes[kvp.Key] = kvp.Value;
                                }

                                if (dict.ContainsKey("boundingRect"))
                                {
                                    var brObj = ((Newtonsoft.Json.Linq.JObject)dict["boundingRect"]).ToObject<Dictionary<string, object>>();
                                    float ToFloat(object o)
                                    {
                                        if (o is double d) return (float)d;
                                        if (o is long l) return (float)l;
                                        if (o is int i) return (float)i;
                                        if (o is string s && float.TryParse(s, out var r)) return r;
                                        return 0f;
                                    }

                                    var rect = new ClientRectangle
                                    {
                                        X = ToFloat(brObj.ContainsKey("x") ? brObj["x"] : 0),
                                        Y = ToFloat(brObj.ContainsKey("y") ? brObj["y"] : 0),
                                        Width = ToFloat(brObj.ContainsKey("width") ? brObj["width"] : 0),
                                        Height = ToFloat(brObj.ContainsKey("height") ? brObj["height"] : 0),
                                        Top = ToFloat(brObj.ContainsKey("top") ? brObj["top"] : 0),
                                        Left = ToFloat(brObj.ContainsKey("left") ? brObj["left"] : 0),
                                        Bottom = ToFloat(brObj.ContainsKey("bottom") ? brObj["bottom"] : 0),
                                        Right = ToFloat(brObj.ContainsKey("right") ? brObj["right"] : 0)
                                    };

                                    attributes["boundingRect"] = rect;
                                    attributes["location"] = new System.Drawing.Point((int)rect.X, (int)rect.Y);
                                    attributes["size"] = new System.Drawing.Size((int)rect.Width, (int)rect.Height);
                                }

                                if (dict.ContainsKey("css")) attributes["css"] = dict["css"]?.ToString();
                            }

                            attributes["xpath"] = await GetXPathForElement(node.BackendNodeId).ConfigureAwait(false);

                            elements.Add(new GPALElement(attributes, Browser)
                            {
                                ElementHandle = node.ObjectId,
                                ElementBackendNodeId = node.BackendNodeId,
                                ElementNodeId = node.NodeId
                            });
                        }
                        catch
                        {
                            // Skip failed nodes
                        }
                    }

                    return elements;
                }
                catch
                {
                    return new List<GPALElement>();
                }
            }
            finally
            {
                if (false == isRecursive)
                    _evaluateSelectorSemaphore.Release();
            }
        }

        /// <summary>
        /// Generates a CSS selector string that uniquely identifies the element with the given backend node id.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="optimized">Whether to prefer a shorter, optimized selector (e.g. using the element's id) when possible.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the generated CSS selector, or an empty string if it could not be determined.</returns>
        public async Task<string> GetCssForElement(int backendNodeId, bool optimized = false, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string jsFunc = @"
                    function getCssSelector(node) {
//                        console.log('getCssSelector called with node:', node);

                        if (!node || node.nodeType !== Node.ELEMENT_NODE) {
                            console.error('Invalid node, returning empty string');
                            return '';
                        }

                        if (node.id) {
//                            console.log('Found ID:', node.id);
                            return '#' + node.id.replace(/([ #.;+*~'!^$[\]()=>|\/@])/g, '\\$1');
                        }

                        let selector = node.localName.toLowerCase();
//                        console.log('Tag name:', selector);

                        if (node.classList && node.classList.length > 0) {
                            const classes = Array.from(node.classList)
                                .filter(c => c && /^[a-zA-Z][\w-]*$/.test(c))
                                .map(c => c.replace(/([ #.;+*~'!^$[\]()=>|\/@])/g, '\\$1'));
                            if (classes.length > 0) {
                                selector += '.' + classes.join('.');
                            }
 //                           console.log('Selector with classes:', selector);
                        }

                        try {
                            const matches = document.querySelectorAll(selector);
 //                           console.log('Matches for selector:', matches.length, selector);
                            if (matches.length === 1 && matches[0] === node) {
                                return selector;
                            }
                        } catch (e) {
 //                           console.error('Error in querySelectorAll:', e.message);
                            return '';
                        }

 //                       console.log('Returning fallback selector:', selector);
                        return selector;
                }";
            return await GetXPathOrCssForElement(backendNodeId, jsFunc, optimized, sessionId).ConfigureAwait(false);
        }
        /// <summary>
        /// Generates an XPath expression that identifies the element with the given backend node id.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element.</param>
        /// <param name="optimized">Whether to prefer a shorter, optimized path (e.g. using the element's id) when possible.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the generated XPath expression, or an empty string if it could not be determined.</returns>
        public async Task<string> GetXPathForElement(int backendNodeId, bool optimized = true, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string jsFunc = @"
                function getXPathForElement(node, optimized) {
                    let Elements = { DOMPath: {} };

                    Elements.DOMPath.xPath = function(node, optimized) {
                        if (node.nodeType === Node.DOCUMENT_NODE) return '/';
                        const steps = [];
                        let contextNode = node;
                        while (contextNode) {
                            const step = Elements.DOMPath._xPathValue(contextNode, optimized);
                            if (!step) break;
                            steps.push(step);
                            if (step.optimized) break;
                            contextNode = contextNode.parentNode;
                        }
                        steps.reverse();
                        return (steps.length && steps[0].optimized ? '' : '/') + steps.map(step => step.value).join('/');
                    };

                    Elements.DOMPath._xPathValue = function(node, optimized) {
                        let ownValue;
                        const ownIndex = Elements.DOMPath._xPathIndex(node);
                        if (ownIndex === -1) return null;
                        switch (node.nodeType) {
                            case Node.ELEMENT_NODE:
                                if (optimized && node.getAttribute('id'))
                                    return new Elements.DOMPath.Step('//*[@id=\'' + node.getAttribute('id') + '\']', true);
                                ownValue = node.localName;
                                break;
                            case Node.ATTRIBUTE_NODE:
                                ownValue = '@' + node.nodeName;
                                break;
                            case Node.TEXT_NODE:
                            case Node.CDATA_SECTION_NODE:
                                ownValue = 'text()';
                                break;
                            case Node.PROCESSING_INSTRUCTION_NODE:
                                ownValue = 'processing-instruction()';
                                break;
                            case Node.COMMENT_NODE:
                                ownValue = 'comment()';
                                break;
                            case Node.DOCUMENT_NODE:
                                ownValue = '';
                                break;
                            default:
                                ownValue = '';
                                break;
                        }
                        if (ownIndex > 0)
                            ownValue += '[' + ownIndex + ']';
                        return new Elements.DOMPath.Step(ownValue, node.nodeType === Node.DOCUMENT_NODE);
                    };

                    Elements.DOMPath._xPathIndex = function(node) {
                        function areNodesSimilar(left, right) {
                            if (left === right) return true;
                            if (left.nodeType === Node.ELEMENT_NODE && right.nodeType === Node.ELEMENT_NODE)
                                return left.localName === right.localName;
                            if (left.nodeType === right.nodeType) return true;
                            const leftType = left.nodeType === Node.CDATA_SECTION_NODE ? Node.TEXT_NODE : left.nodeType;
                            const rightType = right.nodeType === Node.CDATA_SECTION_NODE ? Node.TEXT_NODE : right.nodeType;
                            return leftType === rightType;
                        }
                        const siblings = node.parentNode ? node.parentNode.children : null;
                        if (!siblings) return 0;
                        let hasSameNamedElements;
                        for (let i = 0; i < siblings.length; ++i) {
                            if (areNodesSimilar(node, siblings[i]) && siblings[i] !== node) {
                                hasSameNamedElements = true;
                                break;
                            }
                        }
                        if (!hasSameNamedElements) return 0;
                        let ownIndex = 1;
                        for (let i = 0; i < siblings.length; ++i) {
                            if (areNodesSimilar(node, siblings[i])) {
                                if (siblings[i] === node) return ownIndex;
                                ++ownIndex;
                            }
                        }
                        return -1;
                    };

                    Elements.DOMPath.Step = function(value, optimized) {
                        this.value = value;
                        this.optimized = optimized || false;
                    };

                    return Elements.DOMPath.xPath(node, optimized);
                }";
            return await GetXPathOrCssForElement(backendNodeId, jsFunc, optimized, sessionId).ConfigureAwait(false);
        }
        /// <summary>
        /// Shared helper used by <see cref="GetCssForElement"/> and <see cref="GetXPathForElement"/> that resolves
        /// the element with the given backend node id to a remote object and invokes the supplied JavaScript
        /// function on it to compute a selector or path string.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element to resolve.</param>
        /// <param name="jsFunc">A JavaScript function declaration that takes (node, optimized) and returns the selector/path string.</param>
        /// <param name="optimized">Whether the JavaScript function should prefer a shorter, optimized result.</param>
        /// <param name="sessionId">CDP session id to use for the resolve and evaluate commands.</param>
        /// <returns>Task that resolves to the computed selector/path string, or an empty string on failure.</returns>
        public async Task<string> GetXPathOrCssForElement(int backendNodeId, string jsFunc, bool optimized, string sessionId)
        {
            try
            {
                // Resolve to objectId using backendNodeId
                var resolve = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new { backendNodeId, pierce = true }, sessionId).ConfigureAwait(false);
                string objectId = resolve.@object.objectId;

                // Call function
                var result = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
                {
                    objectId,
                    functionDeclaration = $"function(optimized) {{ return ({jsFunc})(this, optimized); }}",
                    arguments = new object[] { new { value = optimized } },
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                // Release objectId to prevent leak
                //await SendCommand<object>(DevToolsMethods.RuntimeReleaseObject, new { objectId }, sessionId);

                return result?.result?.value?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Failed to get XPath for backendNodeId [{backendNodeId}]",
                    this, GPALObjectType.PuppeteerCommunicator, ex);
                return "";
            }
        }

        // Implementation for "get-parent-node"
        /// <summary>
        /// Retrieves the parent node of the element with the given DOM id.
        /// </summary>
        /// <param name="elementId">The HTML "id" attribute value of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the raw response from the Runtime.evaluate command, representing the parent node.</returns>
        public async Task<dynamic> GetParentNode(string elementId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = "document.getElementById('" + elementId + "').parentNode"
                }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "get-shadow-root"
        /// <summary>
        /// Locates an element by CSS selector and retrieves a description of its node, including any shadow root, via DOM.describeNode.
        /// </summary>
        /// <param name="css">The CSS selector used to locate the element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the raw response from the DOM.describeNode command.</returns>
        public async Task<dynamic> GetShadowRoot(string css, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var nodeId = await QueryByCss(css, sessionId).ConfigureAwait(false);
            return await SendCommand<object>(DevToolsMethods.DOMDescribeNode, new { nodeId, pierce = true }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "is-visible-in-viewport"
        /// <summary>
        /// Checks whether a click at this viewport point hits the element or something covering it. A child counts as
        /// a hit.
        /// </summary>
        /// <param name="element">The element that should receive the click</param>
        /// <param name="x">Viewport x</param>
        /// <param name="y">Viewport y</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null</param>
        /// <returns>True when the element, or something inside it, is what is at that point</returns>
        public async Task<bool> ElementFromPoint(GPALElement element, int x, int y, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Resolve by backend node id, not selector. A selector can match another element or none, and runs in
            // whatever context is current.
            var resolved = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new
            {
                backendNodeId = element.ElementBackendNodeId
            }, sessionId).ConfigureAwait(false);

            string objectId = resolved?.@object?.objectId;

            if (true == string.IsNullOrEmpty(objectId))
                return false;

            // Returns "" on a hit (children count), otherwise a description of the covering element.
            var result = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId = objectId,
                functionDeclaration = @"function (x, y) {
                    const hit = document.elementFromPoint(x, y);
                    if (hit === this || this.contains(hit)) { return ''; }
                    if (!hit) { return 'nothing'; }
                    return hit.tagName
                         + (hit.id ? '#' + hit.id : '')
                         + (hit.className ? '.' + String(hit.className).trim().split(/\s+/).join('.') : '');
                }",
                arguments = new object[] { new { value = x }, new { value = y } },
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string covering = result?.result?.value as string;

            if (false == string.IsNullOrEmpty(covering))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{covering}] is what is at viewport [{x}, {y}], not [{element.TagName}]", null, GPALObjectType.Browser);

            return true == string.IsNullOrEmpty(covering) && null != result?.result?.value;
        }
        /// <summary>
        /// Finds the first element matching the given CSS selector.
        /// Implementation for the "query-selector" workflow action.
        /// </summary>
        /// <param name="css">CSS selector to query for.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the matching element (CDP result), or null/empty if not found.</returns>
        public async Task<dynamic> QuerySelector(string css, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await QueryByCss(css, sessionId).ConfigureAwait(false);
        }

        // Implementation for "query-selectors"
        /// <summary>
        /// Finds all elements matching the given CSS selector, starting from the document root.
        /// Implementation for the "query-selectors" workflow action.
        /// </summary>
        /// <param name="css">CSS selector to query for.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result containing the matching node IDs.</returns>
        public async Task<dynamic> QuerySelectors(string css, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var doc = await SendCommand<object>(DevToolsMethods.DOMGetDocument, new { depth = 0 }, sessionId).ConfigureAwait(false);

            return await SendCommand<object>(DevToolsMethods.DOMQuerySelectorAll, new { nodeId = (int)doc.root.nodeId, selector = css }, sessionId).ConfigureAwait(false);
        }

        // Runs functionDeclaration on the live element with this backend id and answers what it returns, by value. A
        // backend id lasts as long as the element; a node id goes stale whenever DOM tracking is switched or reset.
        // Null when the element is no longer on the page.
        private async Task<dynamic> CallOnElement(int backendNodeId, string functionDeclaration, string sessionId, params object[] arguments)
        {
            dynamic retVal = null;

            var resolve = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new { backendNodeId, pierce = true }, sessionId).ConfigureAwait(false);
            string objectId = resolve?.@object?.objectId?.ToString();

            if (null != objectId)
            {
                var called = await SendCommand<dynamic>(DevToolsMethods.RuntimeCallFunctionOn, new
                {
                    objectId,
                    functionDeclaration,
                    arguments = arguments.Select(argument => new { value = argument }).ToArray(),
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);

                await SendCommand<dynamic>(DevToolsMethods.RuntimeReleaseObject, new { objectId }, sessionId).ConfigureAwait(false);

                retVal = called?.result?.value;
            }

            return retVal;
        }


        // Implementation for "switch-to-default-content"
        /// <summary>
        /// Switches the active context back to the top-level page document, clearing any current iframe/shadow root context.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public Task SwitchToDefaultContent(string sessionId = null)
        {
            // Only clears the stored frame, context and root ids; sends nothing. Raising the window is
            // TopBrowser's job.
            CurrentContextId = null;
            CurrentFrameId = null;
            CurrentFrameSessionId = null;
            CurrentRootObjectId = null;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Resolves a backendNodeId to a RemoteObject objectId in the correct session and context (main page or iframe)
        /// </summary>
        public async Task<string> GetObjectIdFromBackendNodeId(int backendNodeId, string sessionId = null, long? contextId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Use iframe session if we're currently in an iframe
            string effectiveSessionId = !string.IsNullOrEmpty(CurrentFrameSessionId)
                ? CurrentFrameSessionId
                : sessionId;

            // Use provided contextId or fall back to current iframe context
            long? effectiveContextId = contextId ?? CurrentContextId;

            try
            {
                // Step 1: Resolve backendNodeId > nodeId (in the correct session)
                var resolveNodeParams = new Dictionary<string, object>
                {
                    ["backendNodeId"] = backendNodeId
                };

                // Add contextId only if we have one (important for iframes)
                if (effectiveContextId.HasValue)
                {
                    resolveNodeParams["executionContextId"] = effectiveContextId.Value;
                }

                var resolveResult = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, resolveNodeParams, effectiveSessionId).ConfigureAwait(false);

                string objectId = resolveResult?.@object?.objectId?.ToString();

                if (!string.IsNullOrEmpty(objectId))
                    return objectId;

                // Fallback: Try without executionContextId
                var fallbackResult = await SendCommand<dynamic>(DevToolsMethods.DOMResolveNode, new
                {
                    backendNodeId = backendNodeId
                }, effectiveSessionId).ConfigureAwait(false);

                return fallbackResult?.@object?.objectId?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                    $"GetObjectIdFromBackendNodeId failed for backendNodeId [{backendNodeId}]",
                    this, GPALObjectType.PuppeteerCommunicator, ex);

                return string.Empty;
            }
        }
    }
}
