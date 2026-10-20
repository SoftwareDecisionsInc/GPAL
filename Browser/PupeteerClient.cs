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
    public class PuppeteerClient : IPuppeteerClient
    {
        /// <summary>
        /// Workflow steps queued via WithWorkflow, run in order against this client by Run/While/Until.
        /// </summary>
        private readonly List<Action<PuppeteerClient>> _workflows = new List<Action<PuppeteerClient>>();
        /// <summary>
        /// Saved (endpoint, parameters, next result name) state for each workflow step, restored before each step executes.
        /// </summary>
        private  List<(string Endpoint, object Parameters, string NextResultName)?> _workflowStates { get; set; } = new List<(string, object, string)?>();
        /// <summary>
        /// True when the endpoint was set directly via WithEndpoint(string) rather than from an ApiEndpoint enum value.
        /// </summary>
        private bool _customEndpoint { get; set; } = false;
        /// <summary>
        /// The ApiEndpoint enum value selected for the current/next execution.
        /// </summary>
        private ApiEndpoint _apiEndpoint;
        /// <summary>
        /// The resolved endpoint path/URL to call for the current/next execution.
        /// </summary>
        private string _endpoint { get; set; }
        /// <summary>
        /// The parameters object to send to the endpoint for the current/next execution.
        /// </summary>
        private object _parameters { get; set; }
        /// <summary>
        /// The content encoding (e.g. Json) used for requests sent to the puppeteer communicator.
        /// </summary>
        internal ContentEncoding _contentEncoding = ContentEncoding.Json;
        /// <summary>
        /// The result name under which the current execution's result is stored, set via WithResultName/WithName.
        /// </summary>
        private string _name { get; set; }
        /// <summary>
        /// The default result name used when no name is explicitly set via WithResultName.
        /// </summary>
        private string _nextResultName { get; set; }    // = "results"; .. do not save results unless asked to, this is a potential memory leak, it just keeps accumulating
        /// <summary>
        /// The file to save the response to, set via WithSaveFile, or null if the response should not be saved.
        /// </summary>
        private string _saveFile { get; set; }

        /// <summary>
        /// History of executed steps and their results, keyed by name, used by WithXFromResult/GetResultFromName/GetResultFromIndex.
        /// </summary>
        private readonly List<(string Name, object Result, int Iteration, string TargetId)> _executionResults = new List<(string Name, object Result, int Iteration, string TargetId)>();
        /// <summary>
        /// The PuppeteerCommunicator used to send commands to the puppeteer/CDP server.
        /// </summary>
        private PuppeteerCommunicator _puppeteerCommunicator { get; set; }
        /// <summary>
        /// The current iteration count for While/Until workflow loops.
        /// </summary>
        private int _currentIteration { get; set; }
        /// <summary>
        /// Extra HTTP headers to send with the request, set via WithHeader.
        /// </summary>
        internal Dictionary<string, string> _headers = new Dictionary<string, string>();
        /// <summary>
        /// Prune interval, in milliseconds, used by CheckNetworkIdle, set via WithPruneMs.
        /// </summary>
        private int _pruneMs = 3_000;
        /// <summary>
        /// Maximum number of in-flight connections allowed before the network is considered busy, used by CheckNetworkIdle.
        /// </summary>
        private int _maxConnections { get; set; }
        /// <summary>
        /// Horizontal pixel amount for scroll operations, set via WithHPixels.
        /// </summary>
        private int _hPixels { get; set; }
        /// <summary>
        /// Vertical pixel amount for scroll operations, set via WithVPixels.
        /// </summary>
        private int _vPixels { get; set; }
        /// <summary>
        /// The attribute name targeted by SetAttribute/GetAttribute, set via WithAttribute.
        /// </summary>
        private string _attribute { get; set; }
        /// <summary>
        /// The attribute value to set, used together with <see cref="_attribute"/>, set via WithValue.
        /// </summary>
        private string _value { get; set; }
        /// <summary>
        /// The text to send for SendString/FillIn operations, set via WithText.
        /// </summary>
        private string _text { get; set; }
        /// <summary>
        /// REST client used for validating endpoint configuration.
        /// </summary>
        private RESTClient restClient { get; set; }  // for validation
        /// <summary>
        /// The browser target/tab id that the current execution applies to.
        /// </summary>
        internal string _currentTargetId { get; set; }
        /// <summary>
        /// Maps each REST endpoint path to the async PuppeteerCommunicator method that implements it.
        /// </summary>
        private Dictionary<string, Func<PuppeteerCommunicator, IDictionary<string, object>, string, Task<object>>> endpointMap { get; set; }

        /// <summary>
        /// The predicate evaluated each iteration of a While/Until workflow loop.
        /// </summary>
        /// <summary>
        /// The kind of loop in effect: "While" or "Until".
        /// </summary>
        /// <summary>
        /// Maximum time, in milliseconds, a While/Until workflow loop may run before timing out.
        /// </summary>
        private int _whileLoopTimeoutMs { get; set; } // loop timeout
        /// <summary>
        /// Maximum number of iterations a While/Until workflow loop may run.
        /// </summary>
        private int _whileLoopMaxIterations { get; set; } // loop max iterations

        /// <summary>
        /// Creates a new PuppeteerClient with a default RESTClient and initialized endpoint map.<br/>
        /// One per browser, built by its PuppeteerCommunicator and reached through browser.PuppeteerClient.
        /// Holds the target it is bound to, so it cannot be shared between browsers.
        /// </summary>
        internal PuppeteerClient()
        {
            restClient = new RESTClient();
            init();
        }

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

                                // Now use your existing EvaluateSelector method (or whatever you use to turn objectId > element)
                                var parentElems = await c.EvaluateSelector(parentObjectId).ConfigureAwait(false);

                                return parentElems[0]; // This is now your parent element (e.g., the <select>)
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
                    // NOTE: CAVEAT: this may not make any sense
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
                    // this means nothing in terms of puppeteer, only for gpal tracking
                    return null;
                },
                [RESTHelper.Endpoints[ApiEndpoint.IsClickable]] = async (c, p, s) =>
                {
                    // NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
                    // someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the composite result
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
                    // NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
                    // someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the composite result
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
                    // NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
                    // someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the composite result
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
                    // NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
                    // someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the composite result
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

                    // we can set the download location, but not the filename
                    // se we still require a filewatcher to rename to the users filename
                    await c.LeftClickAndDownload(elems, downloadPath, modifiers, s, responses).ConfigureAwait(false);

                    // we turn it off too quickly before it can even download, and do not want a sleep, so leave it on, it's all good
                    //await c.SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "deny" }, s);

                    return (responses, elems);
                },
                [RESTHelper.Endpoints[ApiEndpoint.Upload]] = async (c, p, s) =>
                {
                    GPALFile uploadPaths = p.ContainsKey("uploadPaths") ? (GPALFile)p["uploadPaths"] : null;
                    string uploadPath = p.ContainsKey("uploadPath") ? p["uploadPath"]?.ToString() : "";
                    dynamic elementId = p.ContainsKey("elementId") ? p["elementId"] : null;
                    int modifiers = p.ContainsKey("modifiers") ? Convert.ToInt32(p["modifiers"]) : 0;   // ???

                    List<GPALElement> elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(elementId).ConfigureAwait(false);
                    List<dynamic> responses = new List<dynamic>();

                    // we can set the download location, but not the filename
                    // se we still require a filewatcher to rename to the users filename
                    if (0 < uploadPaths?.Count)
                        await c.LeftClickAndUpload(elems, uploadPaths, modifiers, s, responses).ConfigureAwait(false);
                    else
                        await c.LeftClickAndUpload(elems, uploadPath, modifiers, s, responses).ConfigureAwait(false);

                    // we turn it off too quickly before it can even download, and do not want a sleep, so leave it on, it's all good
                    //await c.SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "deny" }, s);

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
                    return await c.SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                    {
                        type = "keyDown",
                        modifiers = modifier
                    }, s).ConfigureAwait(false);
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
                    return await c.SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                    {
                        type = "keyUp",
                        modifiers = modifier
                    }, s).ConfigureAwait(false);
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
                        string safeValue = selectValue.Replace("'", "\\'"); // not sure about this...
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
                        var attribute = p.ContainsKey("attribute") ? p["attribute"] : "";
                        bool isObjectId = System.Text.RegularExpressions.Regex.IsMatch(parm, @"^-?\d+\.\d+\.\d+$");
                        elems = p.ContainsKey("gpalElements") ? p["gpalElements"] : await _puppeteerCommunicator.EvaluateSelector(parm).ConfigureAwait(false);

                        foreach (GPALElement elem in elems)
                        {
                            string jsFunc = $@"
                        (function(selector, attribute, value) {{
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
                                el.setAttribute(attribute, value);
                            }});
                            return elements.length;  // optional: return number of elements scrolled
                        }})('{(true == isObjectId ? (elem.Css ?? elem.Xpath) : parm).Replace("'", "\\'")}', {attribute}, {value})";

                            responses.Add(await c.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = jsFunc,
                                returnByValue = true
                            }, s).ConfigureAwait(false));
                            elem.Attributes[attribute.ToString()] = value;
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
                    string rangeValue = (string)(p.ContainsKey("rangeValue") ? p["rangeValue"] : "0xdeadbeef");   // -1 should not be possib except it could be set by the user, but it's not valuid
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

        /// <summary>
        /// Returns this client cast to its public IPuppeteerClient interface.
        /// </summary>
        /// <returns>This client as an IPuppeteerClient</returns>
        public IPuppeteerClient ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// The display name assigned to this client via WithName.
        /// </summary>
        public string Name
        {
            get => _name;
        }

        /// <summary>
        /// Sets the PuppeteerCommunicator used to send commands to the puppeteer/CDP server.
        /// </summary>
        /// <param name="communicator">The communicator to use for this client</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerEndpointDetails WithPuppeteerCommunicator(PuppeteerCommunicator communicator)
        {
            _puppeteerCommunicator = communicator;
            //_currentTargetId = _puppeteerCommunicator.GetInitialTargetId().Result;  // Fetch initial target ID
            return this;
        }
        /// <summary>
        /// Sets a custom endpoint path to call directly, bypassing the built-in ApiEndpoint enum.
        /// </summary>
        /// <param name="endpoint">The endpoint path to call</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithEndpoint(string endpoint)
        {
            _endpoint = endpoint;
            _customEndpoint = true;
            return this;
        }

        /// <summary>
        /// Sets the endpoint to call, resolved from the given ApiEndpoint value. Parameters set before this
        /// call survive it, so the endpoint and its parameters can be named in either order.
        /// </summary>
        /// <param name="endpoint">The API endpoint to call</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithEndpoint(ApiEndpoint endpoint)
        {
            _apiEndpoint = endpoint;
            _endpoint = RESTHelper.Endpoints.TryGetValue(endpoint, out var ep) ? ep : "";
            return this;
        }

        /// <summary>
        /// Sets the name under which this execution's result will be stored, for later retrieval via the WithXFromResult methods.
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithResultName(string name)
        {
            _nextResultName = name;
            return this;
        }

        public IAllowPuppeteerEndpointDetails WithSaveFile(GPALFile file)
        {
            _saveFile = file?.Filename;
            return this;
        }

        /// <summary>
        /// Sets the session token parameter for the request.
        /// </summary>
        /// <param name="sessionToken">The session token to send</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithSessionToken(string sessionToken)
        {
            SetParameter("sessionToken", sessionToken);
            return this;
        }

        /// <summary>
        /// Sets the display name for this client.
        /// </summary>
        /// <param name="name">The name to assign</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerEndpointDetails WithName(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets the url parameter for the request.
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithUrl(string url)
        {
            SetParameter("url", url);
            return this;
        }

        /// <summary>
        /// Sets the image format parameter for the request (e.g. for CaptureVisibleTab).
        /// </summary>
        /// <param name="imageFormat">The image format to request</param>
        /// <returns>Fluent interface to continue building the request</returns>
        internal IAllowPuppeteerParametersOrExecution WithImageFormat(ImageFormat imageFormat)
        {
            SetParameter("imageFormat", imageFormat.ToString());
            return this;
        }

        /// <summary>
        /// Sets the url parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithUrlFromResult(int resultIndex)
        {
            var url = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the url parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithUrlFromResult(string name)
        {
            var url = GetResultFromName(name)?.ToString() ?? "";
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the tab id parameter for the request.
        /// </summary>
        /// <param name="tabId">The tab id</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithTabId(int tabId)
        {
            SetParameter("tabId", tabId);
            return this;
        }

        /// <summary>
        /// Sets the tab id parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithTabIdFromResult(int resultIndex)
        {
            var tabId = GetResultFromIndex(resultIndex) is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the tab id parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithTabIdFromResult(string name)
        {
            var tabId = GetResultFromName(name) is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the window id parameter for the request.
        /// </summary>
        /// <param name="windowId">The window id</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithWindowId(int windowId)
        {
            SetParameter("windowId", windowId);
            return this;
        }

        /// <summary>
        /// Sets the window id parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithWindowIdFromResult(int resultIndex)
        {
            var windowId = GetResultFromIndex(resultIndex) is int i ? i : 0;
            return WithWindowId(windowId);
        }

        /// <summary>
        /// Sets the window id parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithWindowIdFromResult(string name)
        {
            var windowId = GetResultFromName(name) is int i ? i : 0;
            return WithWindowId(windowId);
        }

        /// <summary>
        /// Sets the storage domain parameter for storage operations.
        /// </summary>
        /// <param name="domain">The domain to scope the storage operation to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageDomain(string domain)
        {
            SetParameter("domain", domain);
            return this;
        }
        /// <summary>
        /// Sets the storage path parameter for storage operations.
        /// </summary>
        /// <param name="path">The cookie/storage path to scope the operation to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStoragePath(string path)
        {
            SetParameter("path", path);
            return this;
        }
        /// <summary>
        /// Sets the storage key parameter for storage operations.
        /// </summary>
        /// <param name="key">The storage key</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageKey(string key)
        {
            SetParameter("key", key);
            return this;
        }
        /// <summary>
        /// Sets the storage store name parameter for IndexedDB storage operations.
        /// </summary>
        /// <param name="storeName">The object store name</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageStoreName(string storeName)
        {
            SetParameter("storeName", storeName);
            return this;
        }
        /// <summary>
        /// Sets the storage data parameter for storage operations.
        /// </summary>
        /// <param name="data">The data to store</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageData(string data)
        {
            SetParameter("data", data);
            return this;
        }
        /// <summary>
        /// Sets whether storage should be deleted across all origins, not just the current one.
        /// </summary>
        /// <param name="trueOrFalse">True to delete storage across all origins</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithDeleteAcrossOrigins(bool trueOrFalse)
        {
            SetParameter("crossOrigin", trueOrFalse);
            return this;
        }
        /// <summary>
        /// Sets the key parameter for the request.
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithKey(string key)
        {
            SetParameter("key", key);
            return this;
        }

        /// <summary>
        /// Sets the key parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithKeyFromResult(int resultIndex)
        {
            var key = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithKey(key);
        }

        /// <summary>
        /// Sets the key parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithKeyFromResult(string name)
        {
            var key = GetResultFromName(name)?.ToString() ?? "";
            return WithKey(key);
        }

        /// <summary>
        /// Sets the css selector parameter for the request.
        /// </summary>
        /// <param name="css">CSS selector</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithCss(string css)
        {
            SetParameter("css", css);
            return this;
        }

        /// <summary>
        /// Sets the css selector parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithCssFromResult(int resultIndex)
        {
            var css = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithCss(css);
        }

        /// <summary>
        /// Sets the css selector parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithCssFromResult(string name)
        {
            var css = GetResultFromName(name)?.ToString() ?? "";
            return WithCss(css);
        }

        /// <summary>
        /// Sets the gpalElements parameter, identifying the elements the request should act on.
        /// </summary>
        /// <param name="gpalElements">The elements to act on</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElements(List<GPALElement> gpalElements)
        {
            SetParameter("gpalElements", gpalElements);
            return this;
        }

        /// <summary>
        /// Sets the elementId parameter from the result at the given index of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementFromResult(int resultIndex)
        {
            var elementId = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the elementId parameter from the named result of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementFromResult(string name)
        {
            var elementId = GetResultFromName(name)?.ToString() ?? "";
            return WithElementId(elementId);
        }
        /// <summary>
        /// Sets the iteration for an endpoint to retrieve from the results when using WithXXXFromResults
        /// </summary>
        /// <param name="iteration"></param>
        /// <returns></returns>
        public IAllowPuppeteerParametersOrExecution WithIteration(int iteration)
        {
            _currentIteration = iteration > 0 ? iteration : 1;
            return this;
        }
        /// <summary>
        /// Sets the xpath parameter for the request.
        /// </summary>
        /// <param name="xpath">XPath expression</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithXPath(string xpath)
        {
            SetParameter("xpath", xpath);
            return this;
        }

        /// <summary>
        /// Sets the xpath parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithXPathFromResult(int resultIndex)
        {
            var xpath = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets the xpath parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithXPathFromResult(string name)
        {
            var xpath = GetResultFromName(name)?.ToString() ?? "";
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets the pixels parameter for the request.
        /// </summary>
        /// <param name="pixels">Number of pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithPixels(int pixels)
        {
            SetParameter("pixels", pixels);
            return this;
        }

        /// <summary>
        /// Sets the pixels parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithPixelsFromResult(int resultIndex)
        {
            var pixels = GetResultFromIndex(resultIndex) is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the pixels parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithPixelsFromResult(string name)
        {
            var pixels = GetResultFromName(name) is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the referrer parameter for the request.
        /// </summary>
        /// <param name="referrer">The referrer URL</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithReferrer(string referrer)
        {
            // the name the endpoint rules check for, shared with the REST client
            SetParameter("url", referrer);
            return this;
        }

        /// <summary>
        /// Sets the referrer parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithReferrerFromResult(int resultIndex)
        {
            var referrer = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the referrer parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithReferrerFromResult(string name)
        {
            var referrer = GetResultFromName(name)?.ToString() ?? "";
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the text parameter for the request.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithText(string text)
        {
            _text = text;
            SetParameter("text", text);
            return this;
        }

        /// <summary>
        /// Sets the text parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithTextFromResult(int resultIndex)
        {
            var text = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithText(text);
        }

        /// <summary>
        /// Sets the text parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithTextFromResult(string name)
        {
            var text = GetResultFromName(name)?.ToString() ?? "";
            return WithText(text);
        }

        /// <summary>
        /// Sets where a browser download is saved (SaveTo writes a file GPAL already has). CDP sets only the folder
        /// of <paramref name="file"/>; the download watcher applies the file name.
        /// </summary>
        /// <param name="file">Where the downloaded file is to end up</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution DownloadTo(GPALFile file)
        {
            SetParameter("downloadPath", file.Filename);
            return this;
        }
        /// <summary>
        /// Resolves a CSS selector to a list of GPALElements via the puppeteer communicator.
        /// </summary>
        /// <param name="selector">CSS selector to evaluate</param>
        /// <returns>The matched elements</returns>
        private List<GPALElement> GetElements(string selector)
        {
            return _puppeteerCommunicator.EvaluateSelector(selector).Result;
        }
        /// <summary>
        /// Sets the elementId parameter for the request, resolving CSS selectors to an element handle if needed.
        /// </summary>
        /// <param name="selector">An element id/object id, or a CSS selector to resolve</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementId(string selector)
        {
            bool isObjectId = System.Text.RegularExpressions.Regex.IsMatch(selector, @"^-?\d+\.\d+\.\d+$");

            if (true/* == isObjectId*/)
            {
                SetParameter("elementId", selector);
            }
            //else // we have to switch to css when interfacing with gpal, so insert it as it was called
            //{
            //    List<GPALElement> queryResponse = GetElements(selector);
            //    SetParameter("elementId", queryResponse?[0]?.ElementHandle);
            //}
            return this;
        }

        /// <summary>
        /// Sets the elementId parameter from the result at the given index of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementIdFromResult(int resultIndex)
        {
            var elementId = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the elementId parameter from the named result of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementIdFromResult(string name)
        {
            var elementId = GetResultFromName(name)?.ToString() ?? "";
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the horizontal pixels parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithHPixelsFromResult(int resultIndex)
        {
            _hPixels = GetResultFromIndex(resultIndex) is int i ? i : 0;
            SetParameter("hPixels", _hPixels);
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithHPixelsFromResult(string name)
        {
            _hPixels = GetResultFromName(name) is int i ? i : 0;
            SetParameter("hPixels", _hPixels);
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithVPixelsFromResult(int resultIndex)
        {
            _vPixels = GetResultFromIndex(resultIndex) is int i ? i : 0;
            SetParameter("vPixels", _vPixels);
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithVPixelsFromResult(string name)
        {
            _vPixels = GetResultFromName(name) is int i ? i : 0;
            SetParameter("vPixels", _vPixels);
            return this;
        }

        /// <summary>
        /// Synchronously executes the configured request and returns the result as a string.
        /// </summary>
        /// <returns>The result of the executed request</returns>
        public string Execute()
        {
            return Task.Run(async () => await ExecuteAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }

        public T Execute<T>()
        {
            try
            {
                Task<T> task = ExecuteAsync<T>();

                // WinForms compatibilty
                // This keeps the UI thread blocked, but allows the internal awaits 
                // to complete successfully on a background thread.
                return Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult();
            }
            catch (GPALException)
            {
                // Rethrow: the communicator throws GPALException when the browser is gone, and default(T) would look
                // like an answer.
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to execute synchronously for type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, ex);
                return default(T);
            }
        }

        /// <summary>
        /// Executes the configured request, then returns this client so further fluent calls can configure the next request.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IPuppeteerClient AndThen()
        {
            Execute();
            return this;
        }

        public IPuppeteerClient AndThen<T>()
        {
            Execute<T>();
            return this;
        }

        /// <summary>
        /// Asynchronously executes the configured request and returns the result as a string.
        /// </summary>
        /// <returns>Task that resolves to the result of the executed request</returns>
        public async Task<string> ExecuteAsync()
        {
            var result = await ExecuteInternalAsync().ConfigureAwait(false);

            // Reset after the call, not when the endpoint is set, so parameters set before the endpoint are still sent.
            Reset();

            return result?.ToString() ?? "";
        }

        public async Task<T> ExecuteAsync<T>()
        {
            dynamic result = await ExecuteInternalAsync().ConfigureAwait(false); // Returns Task<dynamic>

            // Reset after the call, not when the endpoint is set, so parameters set before the endpoint are still sent.
            Reset();

            if (result == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "ExecuteInternalAsync returned null.", this, GPALObjectType.PuppeteerClient, null);
                return default(T);
            }

            if (typeof(T) == typeof(JObject))
            {
                if (result is JObject)
                {
                    return (T)(object)result;
                }
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Expected JObject but got [{result.GetType().Name}].", this, GPALObjectType.PuppeteerClient, null);
                return default(T);
            }

            // Try direct deserialization for non-JObject types (e.g., bool, string)
            try
            {
                if (result is T)
                {
                    return (T)result;
                }
                if (result is JToken)
                {
                    return JsonConvert.DeserializeObject<T>(result.ToString());
                }

                // A plain value from the communicator, such as a list for EvaluateAll or QuerySelectors, converted
                // through JSON, which turns a list into an array
                return JToken.FromObject(result).ToObject<T>();
            }
            catch //(Exception ex)
            {
                //GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize [{result}] to type [{typeof(T).Name}]. [{ex.Message}]", this, GPALObjectType.PuppeteerClient, ex);
                // Continue to try CDP structure
            }

            // Try CDP structure (result.value or result)
            if (result is JObject jObject)
            {
                JToken resultToken = jObject["result"];
                if (resultToken == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Result object is missing 'result' property.", this, GPALObjectType.PuppeteerClient, null);
                    return default(T);
                }

                JToken valueToken = resultToken["value"];
                if (valueToken != null)
                {
                    // Special handling for string
                    if (typeof(T) == typeof(string) && valueToken.Type == JTokenType.String)
                    {
                        return (T)(object)valueToken.ToString();
                    }

                    try
                    {
                        return JsonConvert.DeserializeObject<T>(valueToken.ToString());
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize result.value [{valueToken.ToString()}] to type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, ex);
                        return default(T);
                    }
                }

                try
                {
                    return JsonConvert.DeserializeObject<T>(resultToken.ToString());
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize result [{resultToken.ToString()}] to type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, ex);
                    return default(T);
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported result type [{result.GetType().Name}] for type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, null);
            return default(T);
        }

        /// <summary>
        /// Returns a collection of all named results recorded so far across this client's executions.
        /// </summary>
        /// <returns>Collection of execution results keyed by result name</returns>
        /// <summary>
        /// Removes all <c>WithWorkflow</c> steps. Steps otherwise stay on the client, so the same set can run in
        /// several While or Until loops.
        /// </summary>
        /// <example><code>client.ClearWorkflows().WithWorkflow(c =&gt; ...).While(() =&gt; ...);</code></example>
        /// <returns>Fluent interface to declare the next workflow</returns>
        public IAllowPuppeteerWorkflow ClearWorkflows()
        {
            // the two lists are index aligned, so one is never emptied without the other
            _workflows.Clear();
            _workflowStates.Clear();

            return this;
        }

        /// <summary>
        /// Removes results stored with <c>WithResultName</c>; <c>GetExecutionResults</c> then returns only later ones.
        /// </summary>
        /// <example><code>client.ClearResults();</code></example>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowPuppeteerExecution ClearResults()
        {
            _executionResults.Clear();

            return this;
        }
        /// <summary>
        /// All named results in stored order, without the tab's target id.
        /// </summary>
        /// <returns>The results, ready to be read by name or by position</returns>
        public ResultCollection GetExecutionResults()
        {
            return new ResultCollection(_executionResults.Select(entry => (entry.Name, entry.Result, entry.Iteration)).ToList(), _name);
        }

        /// <summary>
        /// Every result stored under <paramref name="name"/>, in order: five loop passes return five. With no name,
        /// returns all; index that by name for the last one.
        /// </summary>
        /// <example><code>int passes = client.GetExecutionResults("atBottom").Count;</code></example>
        /// <param name="name">The name given to WithResultName</param>
        /// <returns>What was stored under that name</returns>
        public IReadOnlyList<object> GetExecutionResults(string name)
        {
            return GetExecutionResults().GetAllResults(name);
        }

        /// <summary>
        /// Sets an extra HTTP header to send with the request.
        /// </summary>
        /// <param name="name">Header name</param>
        /// <param name="value">Header value</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithHeader(string name, string value)
        {
            _headers[name] = value;
            return this;
        }

        /// <summary>
        /// Sets the request parameters as an ordinal array.
        /// </summary>
        /// <param name="parameters">Ordinal parameter values</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParameters(params object[] parameters)
        {
            SetParameter("parameters", parameters); // ordinal array
            return this;
        }

        /// <summary>
        /// Sets the request parameters from an object with named fields.
        /// </summary>
        /// <param name="parameters">Object whose properties are the named parameters</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParameters(object parameters)
        {
            SetParameter("parameters", parameters); // object with named fields
            return this;
        }

        /// <summary>
        /// Sets the request parameters from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParametersFromResult(int resultIndex)
        {
            SetParameter("parameters", GetResultFromIndex(resultIndex));
            return this;
        }

        /// <summary>
        /// Sets the request parameters from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParametersFromResult(string name)
        {
            SetParameter("parameters", GetResultFromName(name));
            return this;
        }

        /// <summary>
        /// Sets the content encoding used for requests sent to the puppeteer communicator.
        /// </summary>
        /// <param name="encoding">The content encoding to use</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithEncoding(ContentEncoding encoding)
        {
            _contentEncoding = encoding;
            return this;
        }

        /// <summary>
        /// Adds a workflow step that invokes the given action against this client.
        /// </summary>
        /// <param name="workflow">Action to run as a workflow step</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerWorkflow WithWorkflow(Action<PuppeteerClient> workflow)
        {
            // Store the current endpoint, parameters and result name with the step; they are restored before each pass.
            _workflowStates.Add((_endpoint, _parameters, _nextResultName));
            _workflows.Add(workflow);

            return this;
        }

        /// <summary>
        /// Sets the maximum time a subsequent While/Until loop may run before timing out.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerWorkflow WhileLoopTimeout(int timeoutMs)
        {
            _whileLoopTimeoutMs = timeoutMs;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of iterations a subsequent While/Until loop may run.
        /// </summary>
        /// <param name="maxIterations">Maximum number of iterations</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerWorkflow WhileLoopMaxIterations(int maxIterations)
        {
            _whileLoopMaxIterations = maxIterations;
            return this;
        }

        /// <summary>
        /// Configures execution to repeat while the given condition returns true, subject to WhileLoopTimeout/WhileLoopMaxIterations.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        /// <summary>
        /// Runs the declared workflow steps over and over, restoring each step's endpoint and parameters before
        /// it runs. Bounded by <see cref="WhileLoopMaxIterations(int)"/> and <see cref="WhileLoopTimeout(int)"/>,
        /// defaulting to 1000 passes and no time limit.
        /// </summary>
        /// <param name="condition">Checked once before each pass</param>
        /// <param name="until">True to run for as long as the condition is false, false to run for as long as it is true</param>
        private void RunWorkflowLoop(Func<bool> condition, bool until)
        {
            if (0 == _workflows.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No workflow steps declared for the {(true == until ? "Until" : "While")} loop.",
                    this, GPALObjectType.PuppeteerClient);

                Reset();

                return;
            }

            // Read max iterations and timeout now; Reset clears both.
            int maxIterations = 0 < _whileLoopMaxIterations ? _whileLoopMaxIterations : 1000;
            int timeoutMs = 0 < _whileLoopTimeoutMs ? _whileLoopTimeoutMs : int.MaxValue;

            // While runs for as long as the condition holds, Until for as long as it does not
            Func<bool> anotherPass = true == until ? () => false == condition() : condition;

            int iteration = 0;
            DateTime started = DateTime.UtcNow;

            while (true == anotherPass()
                && maxIterations > iteration
                && timeoutMs > (DateTime.UtcNow - started).TotalMilliseconds)
            {
                _currentIteration = iteration;

                for (int step = 0; step < _workflows.Count; step++)
                {
                    (string Endpoint, object Parameters, string NextResultName)? declaredAs = _workflowStates[step];

                    if (true == declaredAs.HasValue)
                        (_endpoint, _parameters, _nextResultName) = declaredAs.Value;
                    else
                        Reset();

                    _workflows[step](this);
                }

                iteration++;
            }

            if (maxIterations <= iteration)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Loop stopped at its maximum of [{maxIterations}] passes.",
                    this, GPALObjectType.PuppeteerClient);

            Reset();

            _currentIteration = 1;
        }
        public IAllowPuppeteerParametersOrExecution While(Func<bool> condition)
        {
            RunWorkflowLoop(condition, false);

            return this;
        }

        /// <summary>
        /// Configures execution to repeat until the given condition returns true, subject to WhileLoopTimeout/WhileLoopMaxIterations.
        /// </summary>
        /// <param name="condition">Predicate evaluated each iteration</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution Until(Func<bool> condition)
        {
            RunWorkflowLoop(condition, true);

            return this;
        }

        /// <summary>
        /// Sets the prune interval, in milliseconds, used by CheckNetworkIdle.
        /// </summary>
        /// <param name="pruneMs">Prune interval in milliseconds</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerCheckNetworkIdleOrExecution WithPruneMs(int pruneMs)
        {
            _pruneMs = pruneMs;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of in-flight connections allowed before the network is considered busy, used by CheckNetworkIdle.
        /// </summary>
        /// <param name="maxConnections">Maximum number of in-flight connections</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerCheckNetworkIdleOrExecution WithMaxConnections(int maxConnections)
        {
            _maxConnections = maxConnections;
            return this;
        }

        /// <summary>
        /// Sets the modifier keys parameter for the request.
        /// </summary>
        /// <param name="modifierKeys">Modifier keys to hold during the action</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithModifiers(ModifierKeys modifierKeys)
        {
            SetParameter("modifiers", GetModifierBitmask(modifierKeys).ToString());
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels parameter for the request.
        /// </summary>
        /// <param name="hPixels">Number of horizontal pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerScrollElement WithHPixels(int hPixels)
        {
            SetParameter("hPixels", hPixels);
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels parameter for the request.
        /// </summary>
        /// <param name="vPixels">Number of vertical pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerScrollElement WithVPixels(int vPixels)
        {
            SetParameter("vPixels", vPixels);
            return this;
        }

        /// <summary>
        /// Sets the horizontal drag delta parameter for a drag and drop.
        /// </summary>
        /// <param name="deltaX">Horizontal drag distance, in pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithDeltaX(int deltaX)
        {
            SetParameter("deltaX", deltaX);
            return this;
        }

        /// <summary>
        /// Sets the vertical drag delta parameter for a drag and drop.
        /// </summary>
        /// <param name="deltaY">Vertical drag distance, in pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithDeltaY(int deltaY)
        {
            SetParameter("deltaY", deltaY);
            return this;
        }

        /// <summary>
        /// Sets the horizontal grab offset within the element for a drag and drop.
        /// </summary>
        /// <param name="offsetX">Horizontal grab offset, in pixels, within the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithOffsetX(int offsetX)
        {
            SetParameter("offsetX", offsetX);
            return this;
        }

        /// <summary>
        /// Sets the vertical grab offset within the element for a drag and drop.
        /// </summary>
        /// <param name="offsetY">Vertical grab offset, in pixels, within the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithOffsetY(int offsetY)
        {
            SetParameter("offsetY", offsetY);
            return this;
        }

        /// <summary>
        /// Sets the attribute name parameter for SetAttribute/GetAttribute requests.
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute WithAttribute(string attribute)
        {
            _attribute = attribute;
            SetParameter("attribute", attribute);
            return this;
        }

        /// <summary>
        /// Sets the attribute value parameter for SetAttribute requests.
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute WithValue(string value)
        {
            _value = value;
            SetParameter("value", value);
            return this;
        }

        /// <summary>
        /// Sets the destination element id parameter for a drag and drop.
        /// </summary>
        /// <param name="destElementId">Element id of the drop target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithDestElementId(string destElementId)
        {
            SetParameter("destElementId", destElementId);
            return this;
        }

        /// <summary>
        /// Sets the CSS parameter
        /// </summary>
        /// <param name="sinkName">The device name sent to the server as the sinkName parameter</param>
        /// <returns></returns>
        public IAllowPuppeteerExecution WithDeviceName(string sinkName)
        {
            SetParameter("sinkName", sinkName);
            return this;
        }

        /// <summary>
        /// Configure this client to navigate the browser back one page in history.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Back()
        {
            WithEndpoint(ApiEndpoint.Back);
            return this;
        }

        /// <summary>
        /// Configure this client to capture a screenshot of the visible tab in the given image format.
        /// </summary>
        /// <param name="imageFormat">The image format to capture</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CaptureVisibleTab(ImageFormat imageFormat = ImageFormat.JPEG)
        {
            WithImageFormat(imageFormat);
            WithEndpoint(ApiEndpoint.CaptureVisibleTab);
            return this;
        }
        /// <summary>
        /// Cast the desktop to the specified device name
        /// <example>client.CastDesktop("living room").Execute();</example>
        /// </summary>
        /// <param name="deviceNameOrId">The Cast Device Name or ID</param>
        /// <returns></returns>
        public IAllowPuppeteerExecution CastDesktop(string deviceNameOrId)
        {
            WithEndpoint(ApiEndpoint.CastDesktop).WithDeviceName(deviceNameOrId);
            return this;
        }
        /// <summary>
        /// Cast the current tab to the specified device name
        /// <example>client.CastTab("living room").Execute();</example>
        /// </summary>
        /// <param name="deviceNameOrId">The Cast Device Name or ID</param>
        /// <returns></returns>
        public IAllowPuppeteerExecution CastTab(string deviceNameOrId)
        {
            WithEndpoint(ApiEndpoint.CastTab).WithDeviceName(deviceNameOrId);
            return this;
        }

        /// <summary>
        /// Configure this client to close the tab with the given URL, or the current tab if none is given.
        /// </summary>
        /// <param name="url">The URL of the tab to close, or null for the current tab</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseTab(GPALUrl url = null)
        {
            WithEndpoint(ApiEndpoint.CloseTab);
            if (url != null) WithUrl(url.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to close the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id to close</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.CloseTab);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to close the window containing the given URL, or the current window if none is given.
        /// </summary>
        /// <param name="url">The URL of the tab whose window should be closed, or null for the current window</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseWindow(GPALUrl url = null)
        {
            WithEndpoint(ApiEndpoint.CloseWindow);
            if (url != null) WithUrl(url.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to close the window containing the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id whose window should be closed</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseWindow(int tabId)
        {
            WithEndpoint(ApiEndpoint.CloseWindow);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to delete the given type of website storage.
        /// </summary>
        /// <param name="storageType">The type of storage to delete</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions DeleteStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.DeleteStorage);
            SetParameter("storageType", storageType.ToString());

            return this;
        }

        /// <summary>
        /// Configure this client to execute the given JavaScript in the page.
        /// </summary>
        /// <param name="script">The JavaScript to execute</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParameters ExecuteJavaScript(string script)
        {
            WithEndpoint(ApiEndpoint.ExecuteJavaScript);
            SetParameter("expression", script);
            return this;
        }

        /// <summary>
        /// Sends an API request from inside the page, with the browser's cookies, TLS fingerprint, header order and
        /// anti-bot clearance.
        /// </summary>
        /// <param name="url">Url to request. A relative one resolves against wherever the browser currently is</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution Fetch(string url)
        {
            WithEndpoint(ApiEndpoint.Fetch);
            SetParameter("url", url);
            return this;
        }

        /// <summary>
        /// HTTP method for the request. GET when nothing was said.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithVerb(string method)
        {
            SetParameter("method", method);
            return this;
        }

        /// <summary>
        /// Body to send with the request.
        /// </summary>
        /// <param name="body">Request body</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithBody(string body)
        {
            SetParameter("body", body);
            return this;
        }

        /// <summary>
        /// Content type of the body.
        /// </summary>
        /// <param name="contentType">Content type</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithContentType(string contentType)
        {
            SetParameter("contentType", contentType);
            return this;
        }

        /// <summary>
        /// Extra request headers, flattened to name, value, name, value.
        /// </summary>
        /// <param name="headers">Header names and values in pairs</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithHeaders(string[] headers)
        {
            SetParameter("headers", headers);
            return this;
        }

        /// <summary>
        /// True to bring the response body back base64 encoded, which is what a file needs. Text is the default,
        /// and reading a file as text would put a replacement character wherever a byte is not valid utf-8.
        /// </summary>
        /// <param name="asBytes">True for a base64 encoded body, false for text</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithBytes(bool asBytes)
        {
            SetParameter("bytes", asBytes);
            return this;
        }

        /// <summary>
        /// Configure this client to append text to the given input element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the input</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds for the per-character key-event fallback. 0 uses the fallback's default pacing.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerInputText FillInAppend(dynamic elementOrelementId, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.FillInAppend);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to insert text at the start of the given input element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the input</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds for the per-character key-event fallback. 0 uses the fallback's default pacing.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerInputText FillInInsert(dynamic elementOrelementId, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.FillInInsert);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to overwrite the text of the given input element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the input</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds for the per-character key-event fallback. 0 uses the fallback's default pacing.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerInputText FillInOverwrite(dynamic elementOrelementId, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.FillInOverwrite);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to navigate the browser forward one page in history.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Forward()
        {
            WithEndpoint(ApiEndpoint.Forward);
            return this;
        }

        /// <summary>
        /// Configure this client to make the browser window fullscreen.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution FullScreen()
        {
            WithEndpoint(ApiEndpoint.FullScreen);
            return this;
        }

        /// <summary>
        /// Configure this client to query the puppeteer server's ready status, optionally for a specific session token.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetReadyStatus()
        {
            return GetReadyStatus(null);
        }

        /// <summary>
        /// GetReadyStatus() plus a token identifying the wait loop, so a repeated status logs once per wait. Internal.
        /// </summary>
        /// <param name="sessionToken">Identifies the wait this reading belongs to</param>
        /// <returns>Fluent interface to execute the request</returns>
        internal IAllowPuppeteerExecution GetReadyStatus(string sessionToken)
        {
            WithEndpoint(ApiEndpoint.GetReadyStatus);

            if (false == string.IsNullOrEmpty(sessionToken))
                WithSessionToken(sessionToken);

            return this;
        }

        /// <summary>
        /// Configure this client to check whether network activity has gone idle.
        /// </summary>
        /// <param name="maxConnections">Maximum number of in-flight connections still considered idle</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution CheckNetworkIdle(int maxConnections = 0)
        {
            WithEndpoint(ApiEndpoint.CheckNetworkIdle);
            WithMaxConnections(maxConnections);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the shadow root of the element matching the given CSS selector.
        /// </summary>
        /// <param name="css">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetShadowRoot(string css)
        {
            WithEndpoint(ApiEndpoint.GetShadowRoot);
            WithCss(css);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the current page URL.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetCurrentUrl()
        {
            WithEndpoint(ApiEndpoint.GetCurrentUrl);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve information about the current browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetCurrentWindow()
        {
            WithEndpoint(ApiEndpoint.GetCurrentWindow);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the given type of website storage.
        /// </summary>
        /// <param name="storageType">The type of storage to retrieve</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions GetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.GetStorage);
            SetParameter("storageType", storageType.ToString());

            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser's preferred languages, most preferred first.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetLanguages()
        {
            WithEndpoint(ApiEndpoint.GetLanguages);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser's user agent string.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetUserAgent()
        {
            WithEndpoint(ApiEndpoint.GetUserAgent);
            return this;
        }

        /// <summary>
        /// Configure this client to navigate the current tab to the given URL, resolving relative URLs and checking robots.txt restrictions.
        /// </summary>
        /// <param name="url">The URL to navigate to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoTo(GPALUrl url)
        {
            url.ForUrl(MagicHelper.GetFullUrl(url?.Url, _puppeteerCommunicator.Browser, out _puppeteerCommunicator.Browser._areRobotsAllowed));

            if (false == _puppeteerCommunicator.Browser.AreRobotsAllowed && true == _puppeteerCommunicator.Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url?.Url}] not allowed to robots and your honoring robots.txt. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerClient);
                url.ForUrl("https://google.com");
            }
            WithEndpoint(ApiEndpoint.GoTo);
            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the tab whose URL matches the given URL.
        /// </summary>
        /// <param name="url">The URL of the tab to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToTab(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToTab);
            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.GoToTab);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the window containing the tab whose URL matches the given URL.
        /// </summary>
        /// <param name="url">The URL of the tab whose window to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToWindow);
            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the window containing the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id whose window to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToWindow(int tabId)
        {
            WithEndpoint(ApiEndpoint.GoToWindow);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InElement(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context back to the main document.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InMainDom()
        {
            WithEndpoint(ApiEndpoint.SwitchToDefaultContent);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the shadow root of the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InShadowDom(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToShadowRoot);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to maximize the browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Maximize()
        {
            WithEndpoint(ApiEndpoint.Maximize);
            return this;
        }

        /// <summary>
        /// Configure this client to minimize the browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Minimize()
        {
            WithEndpoint(ApiEndpoint.Minimize);
            return this;
        }

        /// <summary>
        /// Configure this client to move the mouse to the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution MoveTo(string selector)
        {
            WithEndpoint(ApiEndpoint.MoveTo);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to open a new tab navigated to the given URL, resolving relative URLs and checking robots.txt restrictions.
        /// </summary>
        /// <param name="url">The URL to navigate to in the new tab, or null for a blank tab</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution NewTab(GPALUrl url = null)
        {
            WithEndpoint(ApiEndpoint.NewTab);

            url?.ForUrl(MagicHelper.GetFullUrl(url?.Url, _puppeteerCommunicator.Browser, out _puppeteerCommunicator.Browser._areRobotsAllowed));

            if (false == _puppeteerCommunicator.Browser.AreRobotsAllowed && true == _puppeteerCommunicator.Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url.Url}] not allowed to robots and your honoring robots.txt. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerClient);
                url.ForUrl("https://google.com");
            }

            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the next tab.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution NextTab()
        {
            WithEndpoint(ApiEndpoint.NextTab);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the next browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution NextWindow()
        {
            WithEndpoint(ApiEndpoint.NextWindow);
            return this;
        }

        /// <summary>
        /// Configure this client to restore the browser window to its normal (non-maximized, non-minimized) state.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Normal()
        {
            WithEndpoint(ApiEndpoint.Normal);
            return this;
        }

        /// <summary>
        /// Configure this client to open a new browser window navigated to the given URL, resolving relative URLs and checking robots.txt restrictions.
        /// </summary>
        /// <param name="url">The URL to navigate to in the new window</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution OpenWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.OpenWindow);

            url.ForUrl(MagicHelper.GetFullUrl(url?.Url, _puppeteerCommunicator.Browser, out _puppeteerCommunicator.Browser._areRobotsAllowed));

            if (false == _puppeteerCommunicator.Browser.AreRobotsAllowed && true == _puppeteerCommunicator.Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url?.Url}] not allowed to robots and your honoring robots.txt. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerClient);
                url.ForUrl("https://google.com");
            }

            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to give the next navigation this referrer, sent with that navigation only.
        /// </summary>
        /// <param name="referrer">The referrer URL to send</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution OverrideReferrer(string referrer)
        {
            WithEndpoint(ApiEndpoint.OverrideReferrer);
            WithReferrer(referrer);
            return this;
        }

        /// <summary>
        /// Configure this client to override the browser's user agent string for subsequent requests.
        /// </summary>
        /// <param name="userAgent">The user agent string to use</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetUserAgent(string userAgent)
        {
            WithEndpoint(ApiEndpoint.SetUserAgent);
            SetParameter("userAgent", userAgent);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the page down by the given number of page heights.
        /// </summary>
        /// <param name="pagesToScroll">Number of page heights to scroll down</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageDown(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageDown);
            SetParameter("pagesToScroll", pagesToScroll);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll to the bottom of the page.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageEnd()
        {
            WithEndpoint(ApiEndpoint.PageEnd);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll to the top of the page.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageTop()
        {
            WithEndpoint(ApiEndpoint.PageTop);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the page up by the given number of page heights.
        /// </summary>
        /// <param name="pagesToScroll">Number of page heights to scroll up</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageUp(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageUp);
            SetParameter("pagesToScroll", pagesToScroll);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the previous tab.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PreviousTab()
        {
            WithEndpoint(ApiEndpoint.PreviousTab);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the previous browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PreviousWindow()
        {
            WithEndpoint(ApiEndpoint.PreviousWindow);
            return this;
        }

        /// <summary>
        /// Configure this client to refresh the current page.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Refresh()
        {
            WithEndpoint(ApiEndpoint.Refresh);
            return this;
        }

        /// <summary>
        /// Configure this client to press and hold the given modifier key(s).
        /// </summary>
        /// <param name="modifierKeys">Modifier key(s) to press and hold</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PressModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.PressModifierKey);
            SetParameter("modifierKeys", GetModifierBitmask(modifierKeys));
            return this;
        }

        /// <summary>
        /// Configure this client to release the given modifier key(s).
        /// </summary>
        /// <param name="modifierKeys">Modifier key(s) to release</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.ReleaseModifierKey);
            SetParameter("modifierKeys", GetModifierBitmask(modifierKeys));
            return this;
        }

        // Helper method to convert ModifierKeys to CDP modifiers bitmask
        /// <summary>
        /// Converts GPAL ModifierKeys flags to the equivalent Chrome DevTools Protocol modifiers bitmask.
        /// </summary>
        /// <param name="modifierKeys">The modifier keys to convert</param>
        /// <returns>The CDP modifiers bitmask</returns>
        public int GetModifierBitmask(ModifierKeys modifierKeys)
        {
            int bitmask = 0;
            if (modifierKeys.HasFlag(ModifierKeys.Alt))
                bitmask |= 1; // CDP Alt
            if (modifierKeys.HasFlag(ModifierKeys.Control))
                bitmask |= 2; // CDP Ctrl
            if (modifierKeys.HasFlag(ModifierKeys.Windows)) // Maps to Meta/Command
                bitmask |= 4; // CDP Meta
            if (modifierKeys.HasFlag(ModifierKeys.Shift))
                bitmask |= 8; // CDP Shift
            return bitmask;
        }
        /// <summary>
        /// Converts a Chrome DevTools Protocol modifiers bitmask back to a human-readable, plus-separated list of modifier names.
        /// </summary>
        /// <param name="bitmask">The CDP modifiers bitmask</param>
        /// <returns>The modifier names joined with "+", or "None" if no bits are set</returns>
        public string GetModifiersString(int bitmask)
        {
            var modifiers = new List<string>();

            if ((bitmask & 1) != 0) // Check for Alt
                modifiers.Add("Alt");
            if ((bitmask & 2) != 0) // Check for Control
                modifiers.Add("Control");
            if ((bitmask & 4) != 0) // Check for Windows/Meta
                modifiers.Add("Windows");
            if ((bitmask & 8) != 0) // Check for Shift
                modifiers.Add("Shift");

            return modifiers.Count > 0 ? string.Join("+", modifiers) : "None";
        }
        /// <summary>
        /// Configure this client to restore the browser window to its normal size.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Restore()
        {
            WithEndpoint(ApiEndpoint.Restore);
            return this;
        }

        /// <summary>
        /// Configure this client to left-click the given element and download the resulting target.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftClickAndDownload(string selector)
        {
            WithEndpoint(ApiEndpoint.LeftClickAndDownload);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the browser window horizontally by the given number of pixels.
        /// </summary>
        /// <param name="pixels">Number of pixels to scroll horizontally</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollWindowByHorizontal(int pixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindowByHorizontal);
            SetParameter("hPixels", pixels);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the browser window vertically by the given number of pixels.
        /// </summary>
        /// <param name="pixels">Number of pixels to scroll vertically</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollWindowByVertical(int pixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindowByVertical);
            SetParameter("vPixels", pixels);
            return this;
        }

        /// <summary>
        /// Configure this client to type the given text into the focused element.
        /// </summary>
        /// <param name="text">The text to type</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. 0 sends the whole string atomically; greater than 0 dispatches character-by-character with this delay between characters.</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SendString(string text, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.SendString);
            WithText(text);
            SetParameter("typingDelay", delayMs);
            return this;
        }

        /// <summary>
        /// Configure this client to send a key press for the given virtual key code, translating it to the corresponding DOM key and code.
        /// </summary>
        /// <param name="vkcode">The virtual key code to send</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SendKey(byte vkcode)
        {
            WithEndpoint(ApiEndpoint.SendKey);

            // A key with no DOM name is sent by its virtual key code alone, as "Unidentified"
            if (true == VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(vkcode, out string key, out string code))
            {
                SetParameter("key", key);
                SetParameter("code", code);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Virtual key [0x{vkcode:X2}] has no DOM key name, so it is sent as [Unidentified]", this, GPALObjectType.PuppeteerClient);

            SetParameter("vk", (int)vkcode);
            return this;
        }


        /// <summary>
        /// Configure this client to give the next navigation https://www.google.com/ as its referrer, to mimic arriving from a search engine.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetGoogleReferrer()
        {
            WithEndpoint(ApiEndpoint.SetGoogleReferrer);
            return this;
        }

        /// <summary>
        /// Configure this client to stop any active screen casting.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution StopCasting()
        {
            WithEndpoint(ApiEndpoint.StopCasting);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context back to the main document.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToDefaultContent()
        {
            WithEndpoint(ApiEndpoint.SwitchToDefaultContent);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToElement(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the iframe matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the iframe element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToFrame(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToFrame);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the shadow root of the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToShadowRoot(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToShadowRoot);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to inject the given JavaScript into the page.
        /// </summary>
        /// <param name="script">The JavaScript to inject</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InjectScript(string script)
        {
            WithEndpoint(ApiEndpoint.InjectScript);
            SetParameter("script", script);
            return this;
        }

        /// <summary>
        /// Configure this client to remove all previously injected scripts (via <see cref="InjectScript(string)"/>).
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ClearInjectedScripts()
        {
            WithEndpoint(ApiEndpoint.ClearInjectedScripts);
            return this;
        }

        /// <summary>
        /// Where files the browser downloads should end up.<br/>
        /// Not SaveTo, which is GPAL writing a file it already has. This sets the directory CDP sends downloads to.
        /// </summary>
        /// <param name="downloadPath">Directory downloaded files are to end up in</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution DownloadTo(string downloadPath)
        {
            // Sets only the downloadPath parameter, not the endpoint, so LeftClickAndDownload(handle).DownloadTo(path)
            // still clicks.
            SetParameter("downloadPath", downloadPath);
            return this;
        }

        /// <summary>
        /// Sets the directory the browser sends downloads to, as a request of its own. DownloadTo sets the same
        /// directory on another request, such as LeftClickAndDownload.
        /// </summary>
        /// <param name="downloadPath">Directory downloaded files are to end up in</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetDownloadFilename(string downloadPath)
        {
            WithEndpoint(ApiEndpoint.SetDownloadFilename);
            SetParameter("downloadPath", downloadPath);
            return this;
        }

        /// <summary>
        /// Configure this client to fire a DOM change event on the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution FireChangeEvent(string selector)
        {
            WithEndpoint(ApiEndpoint.FireChangeEvent);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to set keyboard focus on the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Focus(string selector)
        {
            WithEndpoint(ApiEndpoint.Focus);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the bounding client rectangle of the given element.
        /// </summary>
        /// <param name="backendNodeId">Backend node id of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetBoundingClientRect(string backendNodeId)
        {
            WithEndpoint(ApiEndpoint.GetBoundingClientRect);
            WithElementId(backendNodeId);
            return this;
        }

        /// <summary>
        /// Configure this client to compute a hash of the given element's attributes, useful for detecting element changes.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetElementAttributeHash(string selector)
        {
            WithEndpoint(ApiEndpoint.GetElementAttributeHash);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the current page's HTML source.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetPageSource()
        {
            WithEndpoint(ApiEndpoint.GetPageSource);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the parent node of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetParentNode(string selector)
        {
            WithEndpoint(ApiEndpoint.GetParentNode);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the text content and computed CSS of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetContentAndCss(string selector)
        {
            WithEndpoint(ApiEndpoint.GetContentAndCss);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the CSS attributes of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetCssAttributes(string selector)
        {
            WithEndpoint(ApiEndpoint.GetCssAttributes);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the DOM attributes of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetDomAttributes(string selector)
        {
            WithEndpoint(ApiEndpoint.GetDomAttributes);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the DOM properties of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetDomProperties(string selector)
        {
            WithEndpoint(ApiEndpoint.GetDomProperties);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to hide the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution HideElement(string selector)
        {
            WithEndpoint(ApiEndpoint.HideElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is clickable.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsClickable(string selector)
        {
            WithEndpoint(ApiEndpoint.IsClickable);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is displayed.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsDisplayed(string selector)
        {
            WithEndpoint(ApiEndpoint.IsDisplayed);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is enabled.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsEnabled(string selector)
        {
            WithEndpoint(ApiEndpoint.IsEnabled);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the page is scrolled to its end.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsEndOfPage()
        {
            WithEndpoint(ApiEndpoint.IsEndOfPage);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is visible in the viewport.
        /// </summary>
        /// <param name="element">The element to check</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsVisibleInViewport(IGPALElement element)
        {
            WithEndpoint(ApiEndpoint.IsVisibleInViewport);
           SetParameter("gpalElements", new List<GPALElement>() { (GPALElement)element });
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the element with the given backend node id is visible in the viewport.
        /// </summary>
        /// <param name="backendNodeId">Backend node id of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsVisibleInViewport(string backendNodeId)
        {
            WithEndpoint(ApiEndpoint.IsVisibleInViewport);
            WithElementId(backendNodeId);
            return this;
        }

        /// <summary>
        /// Configure this client to left-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, element id, or a viewport Point for a target with no DOM handle</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.LeftClick);

            WithEndpoint(ApiEndpoint.LeftClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            return this;
        }

        /// <summary>
        /// Configure this client to middle-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution MiddleClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.MiddleClick);

            WithEndpoint(ApiEndpoint.MiddleClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            return this;
        }

        /// <summary>
        /// Configure this client to double left-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftDoubleClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.LeftDoubleClick);

            WithEndpoint(ApiEndpoint.LeftDoubleClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);
            
            return this;
        }

        /// <summary>
        /// Configure this client to right-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution RightClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.RightClick);

            WithEndpoint(ApiEndpoint.RightClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);
            
            return this;
        }

        /// <summary>
        /// Configure this client to open a select dropdown's options by clicking the element with the given handle; combine with WithSelectValue/WithSelectIndex to choose an option.
        /// </summary>
        /// <param name="elementHandle">Object id of the select element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerSelectOptions SelectClick(string elementHandle)   // objectid
        {
            WithEndpoint(ApiEndpoint.SelectClick);
            WithElementId(elementHandle);

            return this;
        }
        /// <summary>
        /// Set the value to choose from the Select menu
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public IAllowPuppeteerExecution WithSelectValue(string value)
        {
            SetParameter("selectValue", value);
            return this;
        }
        /// <summary>
        /// Sets the index of the option to choose from a Select menu.
        /// </summary>
        /// <param name="index">Zero-based index of the option to choose</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithSelectIndex(int index)
        {
            SetParameter("selectIndex", index);
            return this;
        }
        /// <summary>
        /// Configure this client to scroll the given element into view.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollIntoView(dynamic elementOrelementId)
        {
            WithEndpoint(ApiEndpoint.ScrollIntoView);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);
            
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's inner height.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowInnerHeight()
        {
            WithEndpoint(ApiEndpoint.WindowInnerHeight);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's inner width.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowInnerWidth()
        {
            WithEndpoint(ApiEndpoint.WindowInnerWidth);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's outer height.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowOuterHeight()
        {
            WithEndpoint(ApiEndpoint.WindowOuterHeight);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's outer width.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowOuterWidth()
        {
            WithEndpoint(ApiEndpoint.WindowOuterWidth);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the page's horizontal scroll offset.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowPageOffsetX()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetX);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the page's vertical scroll offset.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowPageOffsetY()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetY);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's screen X position.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowScreenLeft()
        {
            WithEndpoint(ApiEndpoint.WindowScreenLeft);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's screen Y position.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowScreenTop()
        {
            WithEndpoint(ApiEndpoint.WindowScreenTop);
            return this;
        }

        /// <summary>
        /// Configure this client to hover the mouse over the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Hover(string selector)
        {
            WithEndpoint(ApiEndpoint.Hover);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Points the request at the coordinate click endpoint. Shared by the click methods when they are handed
        /// a Point rather than an element, which happens for a target with no DOM handle such as an image match.
        /// </summary>
        /// <param name="point">Viewport coordinate, in CSS pixels</param>
        /// <param name="clickType">Which button, and whether this is a double click</param>
        /// <returns>Fluent interface to continue building the request</returns>
        private IAllowPuppeteerParametersOrExecution WithClickPoint(System.Drawing.Point point, ClickType clickType)
        {
            WithEndpoint(ApiEndpoint.ClickPoint);
            SetParameter("x", point.X);
            SetParameter("y", point.Y);
            SetParameter("clickType", (int)clickType);
            return this;
        }

        /// <summary>
        /// Configure this client to move the mouse to a viewport coordinate instead of to an element, for targets
        /// with no DOM handle such as an image match.
        /// </summary>
        /// <param name="point">Viewport coordinate, in CSS pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution MoveTo(System.Drawing.Point point)
        {
            WithEndpoint(ApiEndpoint.MoveToPoint);
            SetParameter("x", point.X);
            SetParameter("y", point.Y);
            return this;
        }

        /// <summary>
        /// Configure this client to start a drag from the given element; combine with WithDestElementId or WithDeltaX/WithDeltaY to set the drop target.
        /// </summary>
        /// <param name="selector">CSS selector of the element to drag</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop DragAndDrop(string selector)
        {
            WithEndpoint(ApiEndpoint.DragAndDrop);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to find the first element matching the given CSS selector.
        /// </summary>
        /// <param name="css">CSS selector</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution QuerySelector(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelector);
            WithCss(css);
            return this;
        }

        /// <summary>
        /// Configure this client to find all elements matching the given CSS selector.
        /// </summary>
        /// <param name="css">CSS selector</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution QuerySelectors(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelectors);
            WithCss(css);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the browser window by the given horizontal and vertical pixel amounts.
        /// </summary>
        /// <param name="hPixels">Horizontal pixels to scroll</param>
        /// <param name="vPixels">Vertical pixels to scroll</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollWindow(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindow);
            WithHPixels(hPixels);
            WithVPixels(vPixels);
            return this;
        }

        /// <summary>
        /// Configure this client to evaluate the given XPath expression and return the first matching node.
        /// </summary>
        /// <param name="xpath">XPath expression</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Evaluate(string xpath)
        {
            WithEndpoint(ApiEndpoint.Evaluate);
            WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Configure this client to evaluate the given XPath expression and return all matching nodes.
        /// </summary>
        /// <param name="xpath">XPath expression</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution EvaluateAll(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluateAll);
            WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Configure this client to drop a referrer set for the next navigation.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ClearReferrer()
        {
            WithEndpoint(ApiEndpoint.ClearReferrer);
            return this;
        }

        /// <summary>
        /// Configure this client to set an attribute on the given element; combine with WithAttribute and WithValue to specify the attribute and value.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute SetAttribute(string selector)
        {
            WithEndpoint(ApiEndpoint.SetAttribute);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to set the value of a range input element; combine with WithRangeValue to specify the value.
        /// </summary>
        /// <param name="selector">CSS selector of the range element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerRangeValue SetRange(string selector)
        {
            WithEndpoint(ApiEndpoint.SetRange);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Sets the range value parameter for a SetRange request.
        /// </summary>
        /// <param name="rangeValue">The value to set on the range input</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithRangeValue(int rangeValue)
        {
            SetParameter("rangeValue", rangeValue.ToString());
            return this;
        }
        /// <summary>
        /// Configure this client to scroll the given element; combine with WithHPixels/WithVPixels to specify the scroll amount.
        /// </summary>
        /// <param name="selector">CSS selector of the element to scroll</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerScrollElement ScrollElement(string selector)
        {
            WithEndpoint(ApiEndpoint.ScrollElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the previously specified element by the given horizontal and vertical pixel amounts.
        /// </summary>
        /// <param name="hPixels">Horizontal pixels to scroll</param>
        /// <param name="vPixels">Vertical pixels to scroll</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollElement(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollElement);
            _hPixels = hPixels;
            _vPixels = vPixels;
            return this;
        }

        /// <summary>
        /// Sets the value of the destination element identified by <paramref name="destSelector"/> from the
        /// source element identified by the preceding <see cref="WithCss(string)"/> or
        /// <see cref="WithElementId(string)"/> call.
        /// <example>client.WithElementId("#source").SetValueFrom("#destination").Execute();</example>
        /// </summary>
        /// <param name="destSelector">CSS selector identifying the destination element whose value is set</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetValueFrom(string destSelector)
        {
            WithEndpoint(ApiEndpoint.SetValueFromElement);
            SetParameter("destSelector", destSelector);
            return this;
        }

        /// <summary>
        /// Configure this client to bring the browser window to the foreground.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution TopBrowser()
        {
            WithEndpoint(ApiEndpoint.TopBrowser);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve an attribute from the given element; combine with WithAttribute to specify which attribute.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute GetAttribute(string selector)
        {
            WithEndpoint(ApiEndpoint.GetAttribute);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Returns the result of a previous execution at the given index in the execution history, or null if the index is out of range.
        /// </summary>
        /// <param name="index">Zero-based index into the execution history</param>
        /// <returns>The result at that index, or null</returns>
        private object GetResultFromIndex(int index)
        {
            if (index < 0 || index >= _executionResults.Count) return null;
            return _executionResults[index].Result;
        }

        /// <summary>
        /// Last result stored under name, or null. Matches RESTClient for WithXFromResult chains.
        /// </summary>
        /// <param name="name">The result name to look up</param>
        /// <returns>The matching result, or null if none is found</returns>
        private object GetResultFromName(string name)
        {
            return _executionResults.LastOrDefault(entry => entry.Name == name).Result;
        }

        /// <summary>
        /// Sets a single named parameter on the request, creating the parameters dictionary if needed.
        /// </summary>
        /// <param name="key">The parameter name</param>
        /// <param name="value">The parameter value</param>
        private void SetParameter(string key, object value)
        {
            if (_parameters == null) _parameters = new Dictionary<string, object>();
            ((Dictionary<string, object>)_parameters)[key] = value;
        }

        /// <summary>
        /// Configure this client to write the given type of website storage; combine with WithStorageDomain/WithStorageKey/WithStorageData etc to specify what to write.
        /// </summary>
        /// <param name="storageType">The type of storage to write</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions SetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.SetStorage);
            SetParameter("storageType", storageType.ToString());

            return this;
        }
        /// <summary>
        /// Resets per-request state (endpoint, parameters, timeouts, loop settings) so the client is ready to build a new request.
        /// </summary>
        private void Reset()
        {
            _endpoint = "";
            _parameters = null;
            _customEndpoint = false;
            _nextResultName = null;     // don't save results unless asked, potential memory leak
            _pruneMs = 3_000;
            _maxConnections = 0;
            _hPixels = 0;
            _vPixels = 0;
            _attribute = null;
            _value = null;
            _text = null;
            _currentIteration = 1;
            _whileLoopTimeoutMs = 0;
            _whileLoopMaxIterations = 0;
        }
        // In PuppeteerClient.cs, updated ExecuteInternalAsync
        /// <summary>
        /// Executes the configured endpoint, handling While/Until looping, saving the response to file, and recording the result for later retrieval.
        /// </summary>
        /// <returns>Task that resolves to the raw result of the executed endpoint</returns>
        private async Task<object> ExecuteInternalAsync()
        {
            if (true == _puppeteerCommunicator._windowSessionsQueue.IsEmpty)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No browser windows open. Cannot call endpoint [{_endpoint}]", null, GPALObjectType.None);
                return null;
            }

            if (string.IsNullOrEmpty(_endpoint))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{_name}] No endpoint specified", null, GPALObjectType.None);
                return null;
            }

            var parameters = _parameters as IDictionary<string, object> ??
                             (_parameters?.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(_parameters)) ?? new Dictionary<string, object>());

            if (!_customEndpoint && !restClient.ValidateParameters(_apiEndpoint, parameters))
            {
                return null;
            }

            object result = await ExecuteEndpoint(_endpoint, parameters).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_saveFile))
            {
                SaveResponseToFile(result?.ToString(), _saveFile);
            }

            if (!string.IsNullOrEmpty(_nextResultName))
            {
                _executionResults.Add((_nextResultName, result, _executionResults.Where(r => r.Name == _nextResultName && r.TargetId == _currentTargetId).Select(r => r.Iteration).DefaultIfEmpty(0).Max() + 1, _currentTargetId));
                _workflowStates.Add((_endpoint, _parameters, _nextResultName));

            }

            // Only a plain value can carry a status; an object value, such as GetContentAndCss returns, cannot be read as text
            string valueStr = ((result as JObject)?["result"]?["value"] as JValue)?.Value<string>();
            if (!string.IsNullOrEmpty(valueStr))
            {
                int statusCode = 0;
                try
                {
                    statusCode = (int) JObject.Parse(valueStr)["status"]?.Value<int>();
                }
                catch { }

                if (statusCode > 0)
                {
                    _puppeteerCommunicator.ServerResponseCode = statusCode;
                    if (statusCode < 200 || statusCode >= 300)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{_name}] Endpoint [{_endpoint}] returned HTTP [{statusCode}]", null, GPALObjectType.None);
                }
            }

            // Reset loop state 
            _whileLoopTimeoutMs = 0;
            _whileLoopMaxIterations = 0;

            return result;
        }

        /// <summary>
        /// Looks up and invokes the handler registered for the given endpoint, passing the current effective session id.
        /// </summary>
        /// <param name="endpoint">The endpoint path to invoke</param>
        /// <param name="parameters">Parameters to pass to the handler</param>
        /// <returns>Task that resolves to the handler's result, or null if no handler is registered</returns>
        private async Task<object> ExecuteEndpoint(string endpoint, IDictionary<string, object> parameters)
        {
            if (endpointMap.TryGetValue(endpoint, out var handler))
            {
                // always use the most recent target/session
                var sessionId = _puppeteerCommunicator.GetEffectiveSessionId();
                if (sessionId == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No active session for execution. Using null.", this, GPALObjectType.Puppeteer);
                }

                return await handler(_puppeteerCommunicator, parameters, sessionId).ConfigureAwait(false);
            }

            return null;
        }

        #region Helpers
        /// <summary>
        /// Saves the given response text to the specified file path.
        /// </summary>
        /// <param name="response">The response text to save</param>
        /// <param name="filePath">The file path to write to</param>
        private void SaveResponseToFile(string response, string filePath)
        {
            RESTClient.SaveResponseToFile(response, filePath);
        }

        /// <summary>
        /// Sets the upload path parameter(s) so a subsequent action uploads the given file(s).
        /// </summary>
        /// <param name="fileToUpload">The file(s) to upload</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution UploadFrom(GPALFile fileToUpload)
        {
            if (1 < fileToUpload.Count)
                SetParameter("uploadPaths", fileToUpload);
            else
                SetParameter("uploadPath", fileToUpload.Filename);
            return this;
        }

        /// <summary>
        /// Configure this client to left-click the given element to open a file picker, then upload the file(s) set via UploadFrom.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftClickAndUpload(dynamic elementOrelementId)
        {
            WithEndpoint(ApiEndpoint.Upload);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            return this;
        }
        #endregion Helpers
    }
}
