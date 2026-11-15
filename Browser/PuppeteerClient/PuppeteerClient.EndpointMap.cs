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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Builds the endpoint map, associating each REST endpoint path with the PuppeteerCommunicator method that handles it.
        /// </summary>
        private void init()
        {
            endpointMap = new Dictionary<string, Func<PuppeteerCommunicator, IDictionary<string, object>, string, Task<object>>>
            {
                [RESTHelper.Endpoints[ApiEndpoint.Back]] = async (c, p, s) =>
                {
                    return await c.Back(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.CaptureVisibleTab]] = async (c, p, s) =>
                {
                    var parameters = new Dictionary<string, object> { { "format", "jpeg" } };
                    if (p.ContainsKey("clip"))
                    {
                        dynamic format = p.ContainsKey("imageFormat") ? p["imageFormat"] : "JPEG";
                        var clip = p["clip"] as IDictionary<string, object>;
                        if (clip != null)
                        {
                            double x = clip.ContainsKey("x") ? Convert.ToDouble(clip["x"]) : 0;
                            double y = clip.ContainsKey("y") ? Convert.ToDouble(clip["y"]) : 0;
                            double width = clip.ContainsKey("width") ? Convert.ToDouble(clip["width"]) : Puppeteer.GetScreenWidthInt();
                            double height = clip.ContainsKey("height") ? Convert.ToDouble(clip["height"]) : Puppeteer.GetScreenHeightInt();
                            double scale = clip.ContainsKey("scale") ? Convert.ToDouble(clip["scale"]) : 1.0;

                            if (width > 0 && height > 0 && x >= 0 && y >= 0)
                            {
                                parameters = new Dictionary<string, object>
                                {
                                    { "format", format },
                                    {
                                        "clip", new Dictionary<string, object>
                                        {
                                            { "x", x },
                                            { "y", y },
                                            { "width", width },
                                            { "height", height },
                                            { "scale", scale }
                                        }
                                    }
                                };
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid clip parameters: x=[{x}], y=[{y}], width=[{width}], height=[{height}]. Using viewport.", this, GPALObjectType.PuppeteerClient);
                            }
                        }
                    }
                    JObject retData = await c.SendCommand(DevToolsMethods.PageCaptureScreenshot, parameters, s).ConfigureAwait(false);
                    // null when the capture timed out
                    string base64 = retData?["data"]?.ToString();

                    return base64;
                },
                [RESTHelper.Endpoints[ApiEndpoint.CastDesktop]] = async (c, p, s) =>
                {
                    string sinkName = p.ContainsKey("sinkName") ? p["sinkName"]?.ToString() : null;
                    await c.CastDesktop(sinkName, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.CastTab]] = async (c, p, s) =>
                {
                    string sinkName = p.ContainsKey("sinkName") ? p["sinkName"]?.ToString() : null;
                    await c.CastTab(sinkName, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.CheckNetworkIdle]] = async (c, p, s) =>
                {
                    // A caller that asks repeatedly passes one token, so its repeat messages are suppressed.
                    string sessionToken = p.ContainsKey("sessionToken") ? p["sessionToken"]?.ToString() : Guid.NewGuid().ToString();
                    return await c.CheckNetworkIdle(s, _maxConnections, _pruneMs, sessionToken).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.ClearReferrer]] = async (c, p, s) =>
                {
                    await c.ClearReferrer(s).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.CloseTab]] = async (c, p, s) =>
                {
                    string targetId = null;
                    if (p.ContainsKey("tabId"))
                    {
                        int tabId = int.Parse(p["tabId"]?.ToString());
                        var queueArray = c.CurrentSessions.ToArray();
                        if (tabId >= 0 && tabId < queueArray.Length)
                        {
                            targetId = queueArray[tabId].Key;
                        }
                    }
                    else
                    {
                        targetId = c.GetCurrentTargetId(); // Default to active
                    }

                    if (targetId != null)
                    {
                        await c.CloseTab(targetId, s).ConfigureAwait(false);
                        return true;
                    }
                    return false;
                },
                [RESTHelper.Endpoints[ApiEndpoint.CloseWindow]] = async (c, p, s) =>
                {
                    string targetId = null;
                    string windowId = p.ContainsKey("windowId") ? p["windowId"]?.ToString() : null;
                    string url = p.ContainsKey("url") ? p["url"]?.ToString() : null;

                    if (!string.IsNullOrEmpty(windowId))
                    {
                        targetId = windowId; // Direct browserContextId
                    }
                    else if (!string.IsNullOrEmpty(url))
                    {
                        targetId = await c.GetTargetWindowIdByUrl(url).ConfigureAwait(false);
                        if (targetId != null)
                        {
                            // Set the window as active to ensure correct context
                            int windowIndex = c.GetCurrentWindowIndex(targetId);
                            if (windowIndex >= 0)
                            {
                                c.SetActiveWindowIndex(windowIndex);
                            }
                        }
                    }
                    else
                    {
                        targetId = c.GetCurrentWindowTargetId(); // Current window's BrowserContextId
                    }

                    if (!string.IsNullOrEmpty(targetId))
                    {
                        await c.CloseWindow(targetId, s).ConfigureAwait(false);
                        return true;
                    }

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to find window for targetId [{targetId}] or URL [{url}]", c, GPALObjectType.Puppeteer);
                    return false;
                },
                [RESTHelper.Endpoints[ApiEndpoint.DeleteStorage]] = async (c, p, s) =>
                {
                    var storageType = p.ContainsKey("storageType") ? p["storageType"]?.ToString() : "";
                    var domain = p.ContainsKey("domain") ? p["domain"]?.ToString() : "";
                    var path = p.ContainsKey("path") ? p["path"]?.ToString() : "";
                    var key = p.ContainsKey("key") ? p["key"]?.ToString() : "";
                    var storeName = p.ContainsKey("storeName") ? p["storeName"]?.ToString() : "";

                    return await c.DeleteStorage(storageType, s, domain, storeName, path, key).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.DragAndDrop]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    var deltaX = p.ContainsKey("deltaX") ? Convert.ToInt32(p["deltaX"]) : 0;
                    var deltaY = p.ContainsKey("deltaY") ? Convert.ToInt32(p["deltaY"]) : 0;
                    var offsetX = p.ContainsKey("offsetX") ? Convert.ToInt32(p["offsetX"]) : 0;
                    var offsetY = p.ContainsKey("offsetY") ? Convert.ToInt32(p["offsetY"]) : 0;
                    List<dynamic> responses = new List<dynamic>();

                    foreach (GPALElement elem in elems)
                    {
                        await c.ScrollIntoView(elem, s).ConfigureAwait(false);
                        await c.DragAndDrop(elem, s, responses, deltaX, deltaY, offsetX, offsetY).ConfigureAwait(false);
                    }

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Evaluate]] = async (c, p, s) =>
                {
                    var xpath = p.ContainsKey("xpath") ? p["xpath"]?.ToString() : "";
                    xpath = xpath.Replace("'", "\\'");
                    List<GPALElement> elems = await _puppeteerCommunicator.EvaluateSelector(xpath, s).ConfigureAwait(false);

                    if (0 < elems.Count)
                        return elems[0];
                    else
                        return null;
                    // return await c.Evaluate(xpath, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.EvaluateAll]] = async (c, p, s) =>
                {
                    var xpath = p.ContainsKey("xpath") ? p["xpath"]?.ToString() : "";

                    xpath = xpath.Replace("'", "\\'");
                    return await _puppeteerCommunicator.EvaluateSelector(xpath, s).ConfigureAwait(false);
                    /*
                    var expression = $@"(function() {{
                                        const result = document.evaluate('{xpath}', document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
                                        const elementsArray = [];
                                        for (let i = 0; i < result.snapshotLength; i++) {{
                                            elementsArray.push(result.snapshotItem(i));
                                        }}
                                        console.log('Nodes matched:', result.snapshotLength);
                                        return elementsArray;
                                    }})()";

                    // Step 1: Evaluate to get the array of nodes
                    var evalResponse = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression,
                        returnByValue = false
                    }, s).ConfigureAwait(false);

                    var arrayObjectId = evalResponse["result"]?["objectId"]?.ToString();
                    if (string.IsNullOrEmpty(arrayObjectId))
                    {
                        throw new InvalidOperationException("EvaluateAll failed to return an array objectId.");
                    }

                    // Step 2: Fetch array elements' properties
                    var propsResponse = await c.SendCommand<object>(DevToolsMethods.RuntimeGetProperties, new
                    {
                        objectId = arrayObjectId,
                        ownProperties = true
                    }, s).ConfigureAwait(false);

                    // Step 3: Extract node references and serialize to JSON strings
                    var nodeReferences = new List<string>();
                    foreach (var prop in propsResponse["result"])
                    {
                        int dummy;
                        var name = prop["name"]?.ToString();
                        if (int.TryParse(name, out dummy)) // Numeric indices
                        {
                            var value = prop["value"];
                            if (value != null)
                            {
                                nodeReferences.Add(value.ToString()); // Serialize to JSON string
                            }
                        }
                    }

                    // Step 4: Return as a value array
                    var response = new JObject
                    {
                        ["result"] = new JObject
                        {
                            ["type"] = "object",
                            ["subtype"] = "array",
                            ["className"] = "Array",
                            ["value"] = new JArray(nodeReferences)
                        }
                    };

                    return response;
                    */
                },
                [RESTHelper.Endpoints[ApiEndpoint.Fetch]] = async (c, p, s) =>
                {
                    return await c.Fetch(
                        p.ContainsKey("url") ? p["url"]?.ToString() : null,
                        p.ContainsKey("method") ? p["method"]?.ToString() : null,
                        p.ContainsKey("body") ? p["body"]?.ToString() : null,
                        p.ContainsKey("contentType") ? p["contentType"]?.ToString() : null,
                        p.ContainsKey("headers") ? p["headers"] as string[] : null,
                        p.ContainsKey("bytes") && true == Convert.ToBoolean(p["bytes"]),
                        s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.ExecuteJavaScript]] = async (c, p, s) =>
                {
                    var expression = p.ContainsKey("expression") ? p["expression"]?.ToString() : p["script"]?.ToString();
                    return await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression,
                        returnByValue = true,
                        awaitPromise = true
                    }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.FillInAppend]] = async (c, p, s) =>
                {
                    List<dynamic> responses = new List<dynamic>();

                    List<GPALElement> elems = (List<GPALElement>)(p.ContainsKey("gpalElements") ? p["gpalElements"] : null);
                    var text = p.ContainsKey("text") ? p["text"]?.ToString() : "";
                    var typingDelay = p.ContainsKey("typingDelay") ? Convert.ToInt32(p["typingDelay"]) : 0;

                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;

                    if (null == elems && null != parm)
                        elems = await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);

                        // Focus instead of clicking. Input.insertText goes to the focused element, and a click needs a
                        // position the element may not be at.
                        await c.FocusElement(element.ElementHandle, s).ConfigureAwait(false);
                        responses.Add(await c.FillIn(element.ElementHandle, element.Value + text, typingDelay, s).ConfigureAwait(false));
                        /*
                        string selector = !string.IsNullOrEmpty(element.Css) ? element.Css : element.Xpath; ;
                        string expression = $@"
                                            (function() {{
                                                var el = null;
                                                try {{
                                                    el = document.querySelector('{selector.Replace("'", "\\'")}');
                                                }} catch(e) {{}}
                                                if (!el) {{
                                                    try {{
                                                        el = document.evaluate(
                                                            '{selector.Replace("'", "\\'")}', 
                                                            document, 
                                                            null, 
                                                            XPathResult.FIRST_ORDERED_NODE_TYPE, 
                                                            null
                                                        ).singleNodeValue;
                                                    }} catch(e) {{}}
                                                }}
                                                if (el) {{
                                                    el.value += '{text.Replace("'", "\\'")}';
                                                }}
                                            }})();
                                            ";

                        responses.Add(await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                        {
                            expression,
                            returnByValue = true
                        }, s));
                        */
                    }
                    return new { responses, elems };
                },
                [RESTHelper.Endpoints[ApiEndpoint.FillInInsert]] = async (c, p, s) =>
                {
                    List<dynamic> responses = new List<dynamic>();
                    List<GPALElement> elems = (List<GPALElement>)(p.ContainsKey("gpalElements") ? p["gpalElements"] : null);
                    var text = p.ContainsKey("text") ? p["text"]?.ToString() : "";
                    var typingDelay = p.ContainsKey("typingDelay") ? Convert.ToInt32(p["typingDelay"]) : 0;

                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;

                    if (null == elems && null != parm)
                        elems = await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);

                        // Focus instead of clicking. Input.insertText goes to the focused element, and a click needs a
                        // position the element may not be at.
                        await c.FocusElement(element.ElementHandle, s).ConfigureAwait(false);
                        responses.Add(await c.FillIn(element.ElementHandle, text + element.Value, typingDelay, s).ConfigureAwait(false));
                        /*
                        string selector = !string.IsNullOrEmpty(element.Css) ? element.Css : element.Xpath; ;
                        string expression = $@"
                                            (function() {{
                                                var el = null;
                                                try {{
                                                    el = document.querySelector('{selector.Replace("'", "\\'")}');
                                                }} catch(e) {{}}
                                                if (!el) {{
                                                    try {{
                                                        el = document.evaluate(
                                                            '{selector.Replace("'", "\\'")}', 
                                                            document, 
                                                            null, 
                                                            XPathResult.FIRST_ORDERED_NODE_TYPE, 
                                                            null
                                                        ).singleNodeValue;
                                                    }} catch(e) {{}}
                                                }}
                                                if (el) {{
                                                    el.value = '{text.Replace("'", "\\'")}' + el.value;
                                                }}
                                            }})();
                                            ";

                        await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                        {
                            expression,
                            returnByValue = true
                        }, s);
                        */
                    }
                    return new { responses, elems };
                },
                [RESTHelper.Endpoints[ApiEndpoint.FillInOverwrite]] = async (c, p, s) =>
                {
                    List<dynamic> responses = new List<dynamic>();
                    List<GPALElement> elems = (List<GPALElement>)(p.ContainsKey("gpalElements") ? p["gpalElements"] : null);
                    var text = p.ContainsKey("text") ? p["text"]?.ToString() : "";
                    var typingDelay = p.ContainsKey("typingDelay") ? Convert.ToInt32(p["typingDelay"]) : 0;

                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;

                    if (null == elems && null != parm)
                        elems = await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);

                        // Focus instead of clicking. Input.insertText goes to the focused element, and a click needs a
                        // position the element may not be at.
                        await c.FocusElement(element.ElementHandle, s).ConfigureAwait(false);
                        responses.Add(await c.FillIn(element.ElementHandle, text, typingDelay, s).ConfigureAwait(false));
                    }
                    return new { responses, elems };
                },
                [RESTHelper.Endpoints[ApiEndpoint.FireChangeEvent]] = async (c, p, s) =>
                {
                    dynamic selector = p.ContainsKey("elementId") ? p["elementId"] : null;
                    return await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = $"document.querySelector('{selector.Replace("'", "\\'")}').dispatchEvent(new Event('change'))",
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Focus]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    //var text = p.ContainsKey("text") ? p["text"]?.ToString() : "";
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement elem in elems)
                    {
                        var requestNodeResult = await c.SendCommand<object>(DevToolsMethods.DOMRequestNode, new { objectId = elem.ElementHandle }, s).ConfigureAwait(false);

                        if (requestNodeResult?.nodeId != null)
                        {
                            return await c.SendCommand<object>(DevToolsMethods.DOMFocus, new { nodeId = (int)requestNodeResult.nodeId }, s).ConfigureAwait(false);
                        }

                        //var doc = await c.SendCommand<object>(DevToolsMethods.DOMGetDocument, new { depth = 0 }, s);
                        //var node = await c.SendCommand<object>(DevToolsMethods.DOMQuerySelector, new { nodeId = (int)doc.root.nodeId, selector }, s);
                        //if (node?.nodeId != null)
                        //{
                        //    return await c.SendCommand<object>(DevToolsMethods.DOMFocus, new { nodeId = (int)node.nodeId }, s);
                        //}
                    }
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.Forward]] = async (c, p, s) =>
                {
                    return await c.Forward(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.FullScreen]] = async (c, p, s) =>
                {
                    var windowId = await c.GetCurrentWindow(s).ConfigureAwait(false);
                    return await c.SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
                    {
                        windowId = windowId,
                        bounds = new { windowState = "fullscreen" }
                    }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetAttribute]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    var attribute = p.ContainsKey("attribute") ? p["attribute"]?.ToString() : "";
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement elem in elems)
                        return elem.GetAttribute(attribute);

                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetBoundingClientRect]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    string id = parm?.ToString();

                    // a backend node id, or a CSS selector as for the other element endpoints
                    if (false == int.TryParse(id, out int nodeId))
                    {
                        List<GPALElement> found = await _puppeteerCommunicator.EvaluateSelector(id).ConfigureAwait(false);
                        nodeId = found?.FirstOrDefault()?.ElementBackendNodeId ?? 0;
                    }

                    return await c.GetBoundingClientRect(nodeId).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetContentAndCss]] = async (c, p, s) =>
                {
                    var selector = p.ContainsKey("elementId") ? p["elementId"]?.ToString() : "";
                    return await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = $"(() => {{ const el = document.querySelector('{selector.Replace("'", "\\'")}'); return {{ text: el.textContent, css: window.getComputedStyle(el).cssText }}; }})()",
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetCssAttributes]] = async (c, p, s) =>
                {
                    string backendNodeId = p.ContainsKey("elementId") ? p["elementId"]?.ToString() : null;

                    return await c.GetCssAttributes(Int32.Parse(backendNodeId), s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetCurrentUrl]] = async (c, p, s) =>
                {
                    return await c.GetCurrentUrl(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetCurrentWindow]] = async (c, p, s) =>
                {
                    return await c.GetCurrentWindow(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetDomAttributes]] = async (c, p, s) =>
                {
                    string backendNodeId = p.ContainsKey("elementId") ? p["elementId"]?.ToString() : null;

                    return await c.GetDomAttributes(Int32.Parse(backendNodeId), s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetDomProperties]] = async (c, p, s) =>
                {
                    string backendNodeId = p.ContainsKey("elementId") ? p["elementId"]?.ToString() : null;

                    return await c.GetDomProperties(Int32.Parse(backendNodeId), s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetElementAttributeHash]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    var text = p.ContainsKey("text") ? p["text"]?.ToString() : "";
                    var attribute = p.ContainsKey("attribute") ? p["attribute"]?.ToString() : "";
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement elem in elems)
                        return MagicHelper.CalculateHash(elem.Attributes);

                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetPageSource]] = async (c, p, s) =>
                {
                    return await c.GetPageSource(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetParentNode]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement elem in elems)
                    {
                        // elem.ElementHandle is the Runtime.objectId of the <option>

                        if (!string.IsNullOrEmpty(elem.ElementHandle))
                        {
                            // Call parentElement on the remote object
                            var parentResult = await c.SendCommand<object>(
                                DevToolsMethods.RuntimeCallFunctionOn,
                                new
                                {
                                    objectId = elem.ElementHandle,
                                    functionDeclaration = "function() { return this.parentElement; }",
                                    returnByValue = false,           // We want a RemoteObject, not a value
                                    awaitPromise = false
                                },
                                s).ConfigureAwait(false);

                            if (parentResult?.result?.objectId != null)
                            {
                                string parentObjectId = parentResult.result.objectId;

                                // EvaluateSelector turns the objectId into a GPALElement
                                var parentElems = await c.EvaluateSelector(parentObjectId).ConfigureAwait(false);

                                return parentElems[0]; // the parent element, e.g. the <select>
                            }
                        }
                    }
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetReadyStatus]] = async (c, p, s) =>
                {
                    dynamic sessionToken = p.ContainsKey("sessionToken") ? p["sessionToken"] : null;
                    return await c.GetReadyStatus(s, sessionToken).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetShadowRoot]] = async (c, p, s) =>
                {
                    // returns the shadow root of the first element found
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement element in elems)
                        return element.GetShadowRoot();
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetStorage]] = async (c, p, s) =>
                {
                    var storageType = p.ContainsKey("storageType") ? p["storageType"]?.ToString() : null;
                    var domain = p.ContainsKey("domain") ? p["domain"]?.ToString() : null;
                    var path = p.ContainsKey("path") ? p["path"]?.ToString() : null;
                    var key = p.ContainsKey("key") ? p["key"]?.ToString() : null;
                    var storeName = p.ContainsKey("storeName") ? p["storeName"]?.ToString() : null;

                    return await c.GetStorage(storageType, s, domain, storeName, path, key).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetLanguages]] = async (c, p, s) =>
                {
                    var languages = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "navigator.languages.join(',')",
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                    return (string)languages.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.GetUserAgent]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "navigator.userAgent",
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                    return (string)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.GoTo]] = async (c, p, s) =>
                {
                    /*
                     * public class PageNavigateResponse
                        {
                            public string frameId { get; set; }
                            public string loaderId { get; set; }
                            public string errorText { get; set; }
                        }
                    */
                    var url = p.ContainsKey("url") ? p["url"]?.ToString() : "about:blank";
                    return await c.GoTo(url, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.GoToTab]] = async (c, p, s) =>
                {
                    string tabId = p.ContainsKey("tabId") ? p["tabId"]?.ToString() : null;
                    string url = p.ContainsKey("url") ? p["url"]?.ToString() : null;
                    int? index = p.TryGetValue("index", out object value) && int.TryParse(value?.ToString(), out int parsed) ? (int?)parsed : null;

                    object tabIdOrUrlOrIndex = tabId ?? url ?? (object)index;
                    if (tabIdOrUrlOrIndex == null)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No valid input provided for GoToTab (tabId, url, or index required)", c, GPALObjectType.PuppeteerCommunicator);
                        return null;
                    }

                    string targetId = await c.GoToTab(tabIdOrUrlOrIndex, s).ConfigureAwait(false);
                    if (targetId == null)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to switch to tab for input [{tabIdOrUrlOrIndex}]", c, GPALObjectType.PuppeteerCommunicator);
                        return null;
                    }

                    return new { targetId };
                },
                [RESTHelper.Endpoints[ApiEndpoint.GoToWindow]] = async (c, p, s) =>
                {
                    var tabIdOrUrl = p.ContainsKey("tabId") ? (object)Convert.ToInt32(p["tabId"])
                                                            : (object)(p.ContainsKey("url") ? p["url"]?.ToString() : null);

                    if (tabIdOrUrl == null)
                        return null;

                    var targetId = await c.GoToWindow(tabIdOrUrl, s).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(targetId))
                    {
                        _currentTargetId = targetId;
                        return new { targetId };
                    }

                    return new { targetId };
                },
                [RESTHelper.Endpoints[ApiEndpoint.HideElement]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] :
                                  p.ContainsKey("css") ? p["css"] :
                                  p.ContainsKey("xpath") ? p["xpath"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] as List<GPALElement> :
                                             await c.EvaluateSelector(parm).ConfigureAwait(false);
                    if (elems == null || !elems.Any())
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No elements resolved for HideElement", c, GPALObjectType.Puppeteer);
                        return new { };
                    }

                    List<dynamic> responses = new List<dynamic>();
                    foreach (GPALElement elem in elems)
                    {
                        responses.Add(await c.HideElement(elem.ElementBackendNodeId, s).ConfigureAwait(false));
                    }

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Hover]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    List<dynamic> responses = new List<dynamic>();

                    await c.MoveTo(elems, s, responses).ConfigureAwait(false);

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.ClickPoint]] = async (c, p, s) =>
                {
                    var x = p.ContainsKey("x") ? Convert.ToInt32(p["x"]) : 0;
                    var y = p.ContainsKey("y") ? Convert.ToInt32(p["y"]) : 0;
                    var clickType = p.ContainsKey("clickType") ? (ClickType)Convert.ToInt32(p["clickType"]) : ClickType.LeftClick;
                    var modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;
                    List<dynamic> responses = new List<dynamic>();

                    await c.ClickElement(x, y, s, responses, clickType, modifiers).ConfigureAwait(false);
                    return responses;
                },
                [RESTHelper.Endpoints[ApiEndpoint.MoveToPoint]] = async (c, p, s) =>
                {
                    var x = p.ContainsKey("x") ? Convert.ToInt32(p["x"]) : 0;
                    var y = p.ContainsKey("y") ? Convert.ToInt32(p["y"]) : 0;

                    await c.MoveTo(x, y, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.InjectScript]] = async (c, p, s) =>
                {
                    var script = p.ContainsKey("script") ? p["script"]?.ToString() : "";
                    await c.InjectScript(script, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.ClearInjectedScripts]] = async (c, p, s) =>
                {
                    await c.ClearInjectedScripts(s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SwitchToDefaultContent]] = async (c, p, s) =>
                {
                    // Clears the communicator's frame state.
                    await c.SwitchToDefaultContent(s).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SwitchToShadowRoot]] = (c, p, s) =>
                {
                    // GPAL tracks the shadow root itself; nothing is sent to the browser
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.IsClickable]] = async (c, p, s) =>
                {
                    // GPAL passes one element; when a selector finds several, the result is true only if every element is clickable
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    List<bool> isClickables = new List<bool>();

                    foreach (GPALElement element in elems)
                    {
                        isClickables.Add(element.IsClickable());
                    }
                    return isClickables.All(isClickAble => true == isClickAble);
                },
                [RESTHelper.Endpoints[ApiEndpoint.IsDisplayed]] = async (c, p, s) =>
                {
                    // GPAL passes one element; when a selector finds several, the result is true only if every element is displayed
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    bool isDisplayed = true;
                    foreach (GPALElement element in elems)
                    {
                        isDisplayed &= element.Displayed;
                    }
                    return isDisplayed;
                },
                [RESTHelper.Endpoints[ApiEndpoint.IsEnabled]] = async (c, p, s) =>
                {
                    // GPAL passes one element; when a selector finds several, the result is true only if no element is disabled
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    bool isDisabled = false;
                    foreach (GPALElement element in elems)
                    {
                        isDisabled |= element.IsDisabled();
                    }
                    return !isDisabled;
                },
                [RESTHelper.Endpoints[ApiEndpoint.IsEndOfPage]] = async (c, p, s) =>
                {
                    return await c.IsEndOfPage(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.IsVisibleInViewport]] = async (c, p, s) =>
                {
                    // GPAL passes one element; when a selector finds several, the result is true only if every element is in the viewport
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = (List<GPALElement>)(p.ContainsKey("gpalElements") ? p["gpalElements"] : null);

                    string id = parm?.ToString();

                    // a backend node id, or a CSS selector as for the other element endpoints
                    if (null != elems)
                        return await c.IsVisibleInViewport(elems, s).ConfigureAwait(false);
                    else if (true == int.TryParse(id, out int nodeId))
                        return await c.IsVisibleInViewport(nodeId, s).ConfigureAwait(false);
                    else
                        return await c.IsVisibleInViewport(await _puppeteerCommunicator.EvaluateSelector(id).ConfigureAwait(false), s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.LeftClick]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;
                    List<dynamic> responses = new List<dynamic>();

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);
                        await c.ClickElement(element, s, responses, ClickType.LeftClick, modifiers).ConfigureAwait(false);
                    }

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.LeftDoubleClick]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;
                    List<dynamic> responses = new List<dynamic>();

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);
                        await c.ClickElement(element, s, responses, ClickType.LeftDoubleClick, modifiers).ConfigureAwait(false);
                    }

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.LeftClickAndDownload]] = async (c, p, s) =>
                {
                    string downloadPath = p.ContainsKey("downloadPath") ? p["downloadPath"]?.ToString() : "";
                    dynamic elementId = p.ContainsKey("elementId") ? p["elementId"] : null;
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;

                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(elementId).ConfigureAwait(false);
                    List<dynamic> responses = new List<dynamic>();

                    // CDP sets the download folder but not the file name, so a file watcher renames the file
                    await c.LeftClickAndDownload(elems, downloadPath, modifiers, s, responses).ConfigureAwait(false);

                    // Downloads stay allowed; denying them here would stop a download still in progress
                    //await c.SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "deny" }, s);

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Upload]] = async (c, p, s) =>
                {
                    GPALFile uploadPaths = p.ContainsKey("uploadPaths") ? (GPALFile)p["uploadPaths"] : null;
                    string uploadPath = p.ContainsKey("uploadPath") ? p["uploadPath"]?.ToString() : "";
                    dynamic elementId = p.ContainsKey("elementId") ? p["elementId"] : null;
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;

                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(elementId).ConfigureAwait(false);
                    List<dynamic> responses = new List<dynamic>();

                    if (0 < uploadPaths?.Count)
                        await c.LeftClickAndUpload(elems, uploadPaths, modifiers, s, responses).ConfigureAwait(false);
                    else
                        await c.LeftClickAndUpload(elems, uploadPath, modifiers, s, responses).ConfigureAwait(false);

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.LeftDoubleClick]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;

                    List<dynamic> responses = new List<dynamic>();

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);
                        await c.ClickElement(element, s, responses, ClickType.LeftDoubleClick, modifiers).ConfigureAwait(false);
                    }
                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.MiddleClick]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;
                    List<dynamic> responses = new List<dynamic>();

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);
                        await c.ClickElement(element, s, responses, ClickType.MiddleClick, modifiers).ConfigureAwait(false);
                    }

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Maximize]] = async (c, p, s) =>
                {
                    return await c.Maximize(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Minimize]] = async (c, p, s) =>
                {
                    return await c.Minimize(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.MoveTo]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    List<dynamic> responses = new List<dynamic>();

                    return await c.MoveTo(elems, s, responses).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.NewTab]] = async (c, p, s) =>
                {
                    var url = p.ContainsKey("url") ? p["url"]?.ToString() : "https://google.com";
                    var targetId = await c.NewTab(url, s).ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(targetId))
                        _currentTargetId = targetId;
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                            "NewTab: Failed to open a new tab",
                            this, GPALObjectType.Puppeteer);

                    return _currentTargetId;
                },
                [RESTHelper.Endpoints[ApiEndpoint.NextTab]] = async (c, p, s) =>
                {
                    var tmp = _currentTargetId;

                    _currentTargetId = await c.NextTab(_currentTargetId).ConfigureAwait(false);

                    return _currentTargetId;
                },
                [RESTHelper.Endpoints[ApiEndpoint.NextWindow]] = async (c, p, s) =>
                {
                    _currentTargetId = await c.NextWindow(_currentTargetId).ConfigureAwait(false);

                    return _currentTargetId;
                },
                [RESTHelper.Endpoints[ApiEndpoint.Normal]] = async (c, p, s) =>
                {
                    return await c.Normal(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.OpenWindow]] = async (c, p, s) =>
                {
                    var url = p.ContainsKey("url") ? p["url"]?.ToString() : "about:blank";

                    _currentTargetId = await c.OpenWindow(url, s).ConfigureAwait(false);

                    return _currentTargetId;
                },
                [RESTHelper.Endpoints[ApiEndpoint.OverrideReferrer]] = async (c, p, s) =>
                {
                    var referrer = p.ContainsKey("url") ? p["url"]?.ToString() : "";
                    await c.OverrideReferrer(referrer, s).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetUserAgent]] = async (c, p, s) =>
                {
                    var userAgent = p.ContainsKey("userAgent") ? p["userAgent"]?.ToString() : "";
                    return await c.SetUserAgent(userAgent, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.PageDown]] = async (c, p, s) =>
                {
                    int pagesToScroll = p.ContainsKey("pagesToScroll") ? Convert.ToInt32(p["pagesToScroll"]) : 1;
                    return await c.ScrollPageAsync("down", pagesToScroll, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.PageEnd]] = async (c, p, s) =>
                {
                    return await c.ScrollToPositionAsync("end", s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.PageTop]] = async (c, p, s) =>
                {
                    return await c.ScrollToPositionAsync("top", s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.PageUp]] = async (c, p, s) =>
                {
                    int pagesToScroll = p.ContainsKey("pagesToScroll") ? Convert.ToInt32(p["pagesToScroll"]) : 1;
                    return await c.ScrollPageAsync("up", pagesToScroll, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.PressModifierKey]] = async (c, p, s) =>
                {
                    var modifier = p.ContainsKey("modifierKeys") ? Convert.ToInt32(p["modifierKeys"]) : 0;
                    await c.PressModifierKey(modifier, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.PreviousTab]] = async (c, p, s) =>
                {
                    _currentTargetId = await c.PreviousTab(_currentTargetId).ConfigureAwait(false);

                    return _currentTargetId;
                },
                [RESTHelper.Endpoints[ApiEndpoint.PreviousWindow]] = async (c, p, s) =>
                {
                    _currentTargetId = await c.PreviousWindow(_currentTargetId).ConfigureAwait(false);

                    return _currentTargetId;
                },
                [RESTHelper.Endpoints[ApiEndpoint.QuerySelector]] = async (c, p, s) =>
                {
                    var selector = p.ContainsKey("css") ? p["css"]?.ToString() : "";
                    List<GPALElement> elems = await _puppeteerCommunicator.EvaluateSelector(selector, s).ConfigureAwait(false);

                    if (0 < elems.Count)
                        return elems[0];
                    else
                        return null;
                    //var doc = await c.SendCommand<object>(DevToolsMethods.DOMGetDocument, new { depth = 0 }, s).ConfigureAwait(false);
                    //return await c.SendCommand<object>(DevToolsMethods.DOMQuerySelector, new { nodeId = (int)doc.root.nodeId, selector }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.QuerySelectors]] = async (c, p, s) =>
                {
                    var selector = p.ContainsKey("css") ? p["css"]?.ToString() : "";
                    return await _puppeteerCommunicator.EvaluateSelector(selector, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Refresh]] = async (c, p, s) =>
                {
                    return await c.SendCommand(DevToolsMethods.PageReload, new { }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.ReleaseModifierKey]] = async (c, p, s) =>
                {
                    var modifier = p.ContainsKey("modifierKeys") ? Convert.ToInt32(p["modifierKeys"]) : 0;
                    await c.ReleaseModifierKey(modifier, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.Restore]] = async (c, p, s) =>
                {
                    return await c.Restore(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.RightClick]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<dynamic> responses = new List<dynamic>();
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                    foreach (GPALElement element in elems)
                    {
                        await c.ScrollIntoView(element, s).ConfigureAwait(false);
                        await c.ClickElement(element, s, responses, ClickType.RightClick).ConfigureAwait(false);
                    }
                    return elems;
                },
                [RESTHelper.Endpoints[ApiEndpoint.ScrollElement]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    bool isObjectId = System.Text.RegularExpressions.Regex.IsMatch(parm, @"^-?\d+\.\d+\.\d+$");
                    var hPixels = p.ContainsKey("hPixels") ? Convert.ToInt32(p["hPixels"]) : 0;
                    var vPixels = p.ContainsKey("vPixels") ? Convert.ToInt32(p["vPixels"]) : 0;
                    List<dynamic> responses = new List<dynamic>();

                    foreach (GPALElement elem in elems)
                    {
                        string jsFunc = $@"
                        (function(selector, hPixels, vPixels) {{
                            function evaluateSelector(selector) {{
                                let nodes = [];
                                let type = 'unknown';
                                try {{
                                    nodes = Array.from(document.querySelectorAll(selector));
                                    if (nodes.length > 0) return nodes;
                                }} catch {{}}

                                try {{
                                    const result = document.evaluate(selector, document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null);
                                    nodes = Array.from({{ length: result.snapshotLength }}, (_, i) => result.snapshotItem(i));
                                    if (nodes.length > 0) return nodes;
                                }} catch {{}}

                                return [];
                            }}

                            const elements = evaluateSelector(selector);
                            elements.forEach(el => {{
                                if (el.scrollBy) el.scrollBy(hPixels, vPixels);
                            }});
                            return elements.length;  // optional: return number of elements scrolled
                        }})('{(true == isObjectId ? (elem.Css ?? elem.Xpath) : parm).Replace("'", "\\'")}', {hPixels}, {vPixels})";

                        responses.Add(await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                        {
                            expression = jsFunc,
                            returnByValue = true
                        }, s).ConfigureAwait(false));
                    }
                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.ScrollIntoView]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    List<dynamic> responses = new List<dynamic>();

                    if (null != elems)
                        foreach (GPALElement elem in elems)
                            responses.Add(await c.ScrollIntoView(elem, s).ConfigureAwait(false));

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.ScrollWindow]] = async (c, p, s) =>
                {
                    var hPixels = p.ContainsKey("hPixels") ? Convert.ToInt32(p["hPixels"]) : 0;
                    var vPixels = p.ContainsKey("vPixels") ? Convert.ToInt32(p["vPixels"]) : 0;
                    return await c.ScrollWindowByPixelsAsync(hPixels, vPixels, s).ConfigureAwait(false);
                },
                // SelectClick - selects an option in a <select> element
                // elementId = CSS selector of the <select>
                // value     = string: option value or visible text (optional)
                // index     = int: 0-based index of option (optional)
                // Exactly one of value/index must be provided
                [RESTHelper.Endpoints[ApiEndpoint.SelectClick]] = async (c, p, s) =>
                {
                    // this is the select selector
                    dynamic elementHandle = p.ContainsKey("elementId") ? p["elementId"] : null; // objectid
                    string selectValue = p.ContainsKey("selectValue") ? p["selectValue"]?.ToString() : null;
                    int? selectIndex = p.ContainsKey("selectIndex") && int.TryParse(p["selectIndex"]?.ToString(), out int idx) ? idx : (int?)null;
                    bool result = false;

                    if (selectIndex.HasValue)
                    {
                        result = await c.SelectByIndex(elementHandle, selectIndex.Value, s).ConfigureAwait(false);
                    }
                    else // value is not null
                    {
                        string safeValue = selectValue.Replace("'", "\\'"); // escapes single quotes for the script
                        result = await c.SelectByValue(elementHandle, safeValue, s).ConfigureAwait(false);
                    }

                    return result;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SendKey]] = async (c, p, s) =>
                {
                    var key = p.ContainsKey("key") ? p["key"]?.ToString() : "Unidentified";
                    var code = p.ContainsKey("code") ? p["code"]?.ToString() : "Unidentified";
                    var vk = p.ContainsKey("vk") ? Convert.ToInt32(p["vk"]) : 0;

                    return await c.SendKey(key, code, vk).ConfigureAwait(false);
                },

                [RESTHelper.Endpoints[ApiEndpoint.SendString]] = async (c, p, s) =>
                {
                    var text = p.ContainsKey("text") ? p["text"]?.ToString() : "";
                    var typingDelay = p.ContainsKey("typingDelay") ? Convert.ToInt32(p["typingDelay"]) : 0;
                    await c.SendString(text, typingDelay, s).ConfigureAwait(false);
                    return true;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetAttribute]] = async (c, p, s) =>
                {
                    List<dynamic> responses = new List<dynamic>();
                    List<GPALElement> elems = null;

                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    if (null != parm)
                    {
                        var value = p.ContainsKey("value") ? p["value"]?.ToString() : string.Empty;
                        var attribute = p.ContainsKey("attribute") ? p["attribute"].ToString() : "";
                        elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                        // set on each resolved element by its node, so one inside a frame or a shadow root is the one changed
                        foreach (GPALElement elem in elems)
                        {
                            responses.Add(await c.SetAttribute(elem.ElementBackendNodeId, attribute, value, s).ConfigureAwait(false));
                            elem.Attributes[attribute] = value;
                        }
                    }
                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetDownloadFilename]] = async (c, p, s) =>
                {
                    var filename = p.ContainsKey("downloadPath") ? p["downloadPath"]?.ToString() : "";
                    return await c.SendCommand<object>(DevToolsMethods.PageSetDownloadBehavior, new
                    {
                        behavior = "allow",
                        downloadPath = filename
                    }, s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetRange]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    string rangeValue = (string)(p.ContainsKey("rangeValue") ? p["rangeValue"] : "0xdeadbeef");   // 0xdeadbeef marks a range value that was not given
                    List<dynamic> responses = new List<dynamic>();

                    if ("0xdeadbeef" != rangeValue)
                        foreach (GPALElement elem in elems)
                            responses.Add(await c.SetRange(elem.Css, rangeValue, s).ConfigureAwait(false));

                    return responses;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetValueFromElement]] = async (c, p, s) =>
                {
                    dynamic parm = p.ContainsKey("elementId") ? p["elementId"] : null;
                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);
                    string destSelector = p.ContainsKey("destSelector") ? p["destSelector"]?.ToString() : null;
                    List<dynamic> responses = new List<dynamic>();

                    if (false == string.IsNullOrEmpty(destSelector))
                        foreach (GPALElement elem in elems)
                            responses.Add(await c.SetValueFromElement(elem.Css, destSelector, s).ConfigureAwait(false));

                    return responses;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetStorage]] = async (c, p, s) =>
                {
                    var storageType = p.ContainsKey("storageType") ? p["storageType"]?.ToString() : "";
                    var domain = p.ContainsKey("domain") ? p["domain"]?.ToString() : "";
                    var storeName = p.ContainsKey("storeName") ? p["storeName"]?.ToString() : null;
                    var path = p.ContainsKey("path") ? p["path"]?.ToString() : "";
                    var key = p.ContainsKey("key") ? p["key"]?.ToString() : "";
                    var data = p.ContainsKey("data") ? p["data"]?.ToString() : "";

                    return await c.SetStorage(storageType, s, domain, key, data, storeName, path).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.SetGoogleReferrer]] = async (c, p, s) =>
                {
                    await c.SetGoogleReferrer(s).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.StopCasting]] = async (c, p, s) =>
                {
                    return await c.StopCasting(s).ConfigureAwait(false);
                },
                [RESTHelper.Endpoints[ApiEndpoint.SwitchToDefaultContent]] = async (c, p, s) =>
                {
                    // doesn't mean anything, useful for gpal tracking
                    await c.SwitchToDefaultContent(s).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SwitchToElement]] = async (c, p, s) =>
                {
                    dynamic selector = p.ContainsKey("elementId") ? p["elementId"] : null;
                    await c.SwitchToElement(selector).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SwitchToFrame]] = async (c, p, s) =>
                {
                    dynamic selector = p.ContainsKey("elementId") ? p["elementId"] : null;
                    await c.SwitchToFrame(selector).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.SwitchToShadowRoot]] = async (c, p, s) =>
                {
                    dynamic selector = p.ContainsKey("elementId") ? p["elementId"] : null;
                    await c.SwitchToShadowDom(selector).ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.TopBrowser]] = async (c, p, s) =>
                {
                    await c.TopBrowser().ConfigureAwait(false);
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowInnerHeight]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.innerHeight",
                        returnByValue = true
                    // always the top level window - inside an iframe this would report the frame's own viewport
                    }, c.GetCurrentSessionId()).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowInnerWidth]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.innerWidth",
                        returnByValue = true
                    // always the top level window - inside an iframe this would report the frame's own viewport
                    }, c.GetCurrentSessionId()).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowOuterHeight]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.outerHeight",
                        returnByValue = true
                    // always the top level window - the browser frame is not a property of any iframe
                    }, c.GetCurrentSessionId()).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowOuterWidth]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.outerWidth",
                        returnByValue = true
                    // always the top level window - the browser frame is not a property of any iframe
                    }, c.GetCurrentSessionId()).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowPageOffsetX]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.pageXOffset",
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowPageOffsetY]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.pageYOffset",
                        returnByValue = true
                    }, s).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowScreenLeft]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.screenLeft",
                        returnByValue = true
                    // always the top level window - this is where the browser sits on the desktop
                    }, c.GetCurrentSessionId()).ConfigureAwait(false);
                    return (int)result.result.value;
                },
                [RESTHelper.Endpoints[ApiEndpoint.WindowScreenTop]] = async (c, p, s) =>
                {
                    var result = await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "window.screenTop",
                        returnByValue = true
                    // always the top level window - this is where the browser sits on the desktop
                    }, c.GetCurrentSessionId()).ConfigureAwait(false);
                    return (int)result.result.value;
                }
            };
        }
    }
}
