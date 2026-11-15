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
        /// A copy of the captured calls, oldest first, so the reader thread can keep adding while the caller reads.
        /// <example>List&lt;GPALCall&gt; calls = browser.PuppeteerCommunicator.GetCapturedCalls();</example>
        /// </summary>
        /// <returns>The calls recorded so far, empty when nothing was captured</returns>
        public List<GPALCall> GetCapturedCalls()
        {
            return new List<GPALCall>(CapturedCalls);
        }

        /// <summary>
        /// Forgets everything recorded so far. Recording carries on.
        /// </summary>
        public void ClearCapturedCalls()
        {
            CapturedCalls.Clear();
        }

        /// <summary>
        /// Starts or stops recording network requests. Enables Network on every tracked tab, so requests from a tab
        /// opened by a click are recorded.
        /// <example>await browser.PuppeteerCommunicator.CaptureCalls(true);</example>
        /// </summary>
        /// <param name="capture">True to record, false to stop</param>
        /// <param name="sessionId">The session to tell, or null for every session known</param>
        public async Task CaptureCalls(bool capture, string sessionId = null)
        {
            CapturingCalls = capture;

            // Network is off at startup and enabled per session, so send Network.enable to every known tab. A repeat
            // on the same session is harmless.
            if (true == capture)
                foreach (string session in KnownSessions(sessionId))
                    await SendCommand<object>(DevToolsMethods.NetworkEnable, new { }, session).ConfigureAwait(false);
        }

        // Sets the HTTP status on the captured call with the same requestId.
        private void RecordStatus(dynamic paramsData)
        {
            try
            {
                string requestId = paramsData?.requestId?.ToString();

                if (false == string.IsNullOrEmpty(requestId) && true == _capturedById.TryGetValue(requestId, out GPALCall call))
                    call.Status = (int)(paramsData?.response?.status ?? 0);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not record a response status", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
        }

        // Records one Network.requestWillBeSent as a GPALCall: url, method, type, post data, initiator and headers.
        private void RecordCall(dynamic paramsData)
        {
            try
            {
                GPALCall call = new GPALCall
                {
                    Url = paramsData?.request?.url?.ToString(),
                    Method = paramsData?.request?.method?.ToString(),
                    ResourceType = paramsData?.type?.ToString(),
                    PostData = paramsData?.request?.postData?.ToString(),
                    Initiator = paramsData?.initiator?.type?.ToString()
                };

                // The page's own request headers, which a hand-built request usually lacks.
                if (paramsData?.request?.headers is Newtonsoft.Json.Linq.JObject headers)
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> header in headers)
                        call.Headers[header.Key] = header.Value?.ToString();

                // CallFilter only decides what is kept; the browser still reports everything. Skip urls that do not
                // contain it.
                string filter = Browser?.BrowserSettings?.CallFilter;

                if (false == string.IsNullOrEmpty(filter) && true != call.Url?.Contains(filter))
                    return;

                call.RequestId = paramsData?.requestId?.ToString();

                if (false == string.IsNullOrEmpty(call.RequestId))
                    _capturedById[call.RequestId] = call;

                CapturedCalls.Add(call);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not record a request", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
        }

        /// <summary>
        /// Sets headers sent with every request for the rest of the session, such as an Authorization header sent up
        /// front instead of on a challenge.
        /// </summary>
        /// <param name="headers">Header names and values to send with every request.</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task representing the asynchronous call.</returns>
        internal async Task SetExtraHttpHeaders(Dictionary<string, object> headers, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Network must be on first; it is off at startup. Without it setExtraHTTPHeaders succeeds but does nothing,
            // which looks like bad credentials.
            await SendCommand<object>(DevToolsMethods.NetworkEnable, new { }, sessionId).ConfigureAwait(false);
            await SendCommand<object>(DevToolsMethods.NetworkSetExtraHTTPHeaders, new { headers }, sessionId).ConfigureAwait(false);
        }
      
        // Implementation for "clear-referrer"
        /// <summary>
        /// Drops a referrer set for the next navigation, and the browser's referrer, so later GoTo and Get calls
        /// carry the browser's own.
        /// </summary>
        /// <param name="sessionId">CDP session id; unused, as the referrer belongs to the next navigation.</param>
        public Task ClearReferrer(string sessionId = null)
        {
            _navigationReferrer = null;

            if (null != Browser)
                Browser.BrowserSettings.Referrer = null;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets the browser's user agent string by evaluating <c>navigator.userAgent</c>.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the current user agent string.</returns>
        public async Task<string> GetUserAgent(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            JObject result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new
                {
                    expression = "navigator.userAgent",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return result?["result"]?["value"]?.ToString();
        }
        // Implementation for "override-referrer"
        /// <summary>
        /// Gives the next navigation the given URL as its referrer, sent with that navigation as the page's own
        /// request and its document.referrer. What the page loads, and later pages, carry their real referrers.
        /// Implementation for the "override-referrer" workflow action.
        /// </summary>
        /// <param name="url">URL value to use as the Referer header.</param>
        /// <param name="sessionId">CDP session ID; unused, as the referrer belongs to the next navigation.</param>
        /// <returns>Task that is already complete; the referrer is sent by the navigation.</returns>
        public Task OverrideReferrer(string url, string sessionId = null)
        {
            // an empty url asks for no referrer, the same as clear-referrer
            _navigationReferrer = true == string.IsNullOrEmpty(url) ? null : url;
            return Task.CompletedTask;
        }

        // Implementation for "set-useragent"
        /// <summary>
        /// Overrides the browser's user agent string (including <c>navigator.userAgent</c>) for subsequent requests.
        /// Implementation for the "set-useragent" workflow action.
        /// </summary>
        /// <param name="userAgent">The user agent string to use.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result of the override operation.</returns>
        public async Task<dynamic> SetUserAgent(string userAgent, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.NetworkSetUserAgentOverride, new { userAgent }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "stealth-override-referrer"
        /// <summary>
        /// Gives the next navigation https://www.google.com/ as its referrer, so that page looks like a click from
        /// a Google search. What the page loads, and later pages, carry their real referrers.
        /// Implementation for the "stealth-override-referrer" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; unused, as the referrer belongs to the next navigation.</param>
        /// <returns>Task that is already complete; the referrer is sent by the navigation.</returns>
        public Task SetGoogleReferrer(string sessionId = null)
        {
            _navigationReferrer = "https://www.google.com/";
            return Task.CompletedTask;
        }

        // Implementation for "fetch"
        /// <summary>
        /// Issues a GPALRequest from inside the page, so the browser's own socket carries it: same cookies, same
        /// TLS fingerprint, same header order, same anti-bot clearance. A relative path resolves against wherever
        /// the browser currently is, which is what a site proxying its own API needs.
        /// </summary>
        /// <param name="url">Url to request. A relative one resolves against wherever the browser currently is.</param>
        /// <param name="method">HTTP method, GET when null.</param>
        /// <param name="body">Request body, or null for none.</param>
        /// <param name="contentType">Content type of the body.</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value.</param>
        /// <param name="asBytes">True to bring the body back base64 encoded, which is what a file needs.</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the response envelope, or null if the fetch could not be made.</returns>
        public async Task<string> Fetch(string url, string method = null, string body = null, string contentType = null, string[] headers = null, bool asBytes = false, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new
                {
                    expression = $@"
                        (async () => {{
                            {BrowserHelper.FetchSetupScript(url, method, body, contentType, headers, asBytes)}

                            try {{
                                var response = await fetch(target.toString(), options);
                                return await gpalEnvelope(response);
                            }} catch (error) {{
                                return gpalFailure(error);
                            }}
                        }})()",
                    contextId = CurrentContextId,
                    returnByValue = true,
                    awaitPromise = true
                }, sessionId).ConfigureAwait(false);

            // Index the response as a JObject; dynamic member access does not return the string. Use ?. at each step:
            // a CDP error has no "result".
            Newtonsoft.Json.Linq.JObject evaluated = result as Newtonsoft.Json.Linq.JObject;
            Newtonsoft.Json.Linq.JToken value = evaluated?["result"]?["value"];

            if (null == value)
                // SerializeObject, not JToken.ToString(Formatting), whose overload binds against whichever
                // Newtonsoft the host app loaded and throws MissingMethodException when that is not ours
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The page did not answer the fetch. Runtime.evaluate returned [{(null == evaluated ? "nothing" : Newtonsoft.Json.JsonConvert.SerializeObject(evaluated))}]", null, GPALObjectType.PuppeteerCommunicator);

            return value?.ToString();
        }
    }
}
