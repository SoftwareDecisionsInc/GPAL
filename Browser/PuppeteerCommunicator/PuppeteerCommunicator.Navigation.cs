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
        /// Polls until the given element's layout has stabilized with non-zero dimensions, first using
        /// <c>DOM.getBoxModel</c> (re-resolving the element via <see cref="EvaluateSelector"/> if it has become detached),
        /// and falling back to a JavaScript <c>getBoundingClientRect</c> check if the box model is unavailable.
        /// </summary>
        /// <param name="element">The element whose layout readiness should be checked; may be updated in place if it needs to be re-resolved.</param>
        /// <param name="sessionId">The CDP session to query; if null, the effective session ID is resolved automatically.</param>
        /// <param name="maxWaitMs">The maximum total time, in milliseconds, to wait for the element to become laid out.</param>
        /// <param name="pollIntervalMs">The delay, in milliseconds, between polling attempts.</param>
        /// <returns>A task that resolves to true if the element's layout becomes ready (non-zero, visible dimensions) within the timeout; otherwise false.</returns>
        /// <example>
        /// <code>
        /// bool ready = await communicator.WaitForLayoutReady(element, sessionId, maxWaitMs: 5000);
        /// if (!ready)
        /// {
        ///     // handle layout timeout
        /// }
        /// </code>
        /// </example>
        public async Task<bool> WaitForLayoutReady(GPALElement element, string sessionId, int maxWaitMs = 3000, int pollIntervalMs = 150)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            sessionId ??= GetEffectiveSessionId();

            while (stopwatch.ElapsedMilliseconds < maxWaitMs)
            {
                // Option A: Try CDP box model (most accurate)
                var box = await SendCommand<dynamic>(
                    DevToolsMethods.DOMGetBoxModel,
                    new { backendNodeId = element.ElementBackendNodeId },
                    sessionId
                ).ConfigureAwait(false);

                if (box?.model?.content != null)
                {
                    var quad = box.model.content;
                    if (quad.Count >= 8 && quad[2] > quad[0] && quad[7] > quad[1]) // width & height > 0
                    {
                        return true;
                    }
                }
                else if (null == box)   // no box model: the element may have been replaced, so find it again by its css
                {
                    List<GPALElement> elems = await EvaluateSelector(element.Css, sessionId).ConfigureAwait(false);
                    if (0 < elems.Count)
                    {
                        element.ElementBackendNodeId = elems[0].ElementBackendNodeId;
                        element.ElementHandle = elems[0].ElementHandle;
                        element.ElementNodeId = elems[0].ElementNodeId;
                        element.Css = elems[0].Css;
                        element.Xpath = elems[0].Xpath;
                    }
                }

                // Option B: Fallback to JS dimensions check (works even if CDP box fails)
                var jsResult = await SendCommand<dynamic>(
                    DevToolsMethods.RuntimeEvaluate,
                    new
                    {
                        expression = $@"
                        (function() {{
                            const el = document.querySelector('{element.Css}') ||
                                       document.evaluate('{element.Xpath}', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                            if (!el) return false;
                            const r = el.getBoundingClientRect();
                            return r.width > 0.1 && r.height > 0.1 && r.top >= 0; // visible-ish
                        }})()",
                        contextId = CurrentContextId,
                        returnByValue = true
                    },
                    sessionId
                ).ConfigureAwait(false);

                // value is a Newtonsoft JValue, never a bool, so it is read as text
                if (true == "true".Equals(jsResult?.result?.value?.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                await Task.Delay(pollIntervalMs).ConfigureAwait(false);
            }

            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                $"Timeout waiting for layout on [{element.TagName}][{element.Css ?? element.Xpath}] after [{maxWaitMs}]ms",
                this, GPALObjectType.PuppeteerCommunicator);

            return false;
        }

        /// <summary>
        /// Navigates the current page to the given URL, clearing any tracked file attachments for the session.
        /// </summary>
        /// <param name="url">The URL to navigate to.</param>
        /// <param name="sessionId">CDP session id for the page to navigate.</param>
        /// <returns>Task that resolves to true if the navigation command succeeded, otherwise false.</returns>
        /// <example>
        /// <code>
        /// await communicator.NavigateToUrl("https://example.com", sessionId);
        /// </code>
        /// </example>
        public async Task<bool> NavigateToUrl(string url, string sessionId)
        {
            _attachedFilesPerSession.Clear();

            // A referrer for this navigation goes with the navigation, so it is that page's referrer only
            string referrer = _navigationReferrer;
            object navigateParams = new { url };

            if (null != referrer)
            {
                _navigationReferrer = null;
                navigateParams = new { url, referrer };
            }

            var result = await SendCommand<object>(DevToolsMethods.PageNavigate, navigateParams, sessionId).ConfigureAwait(false);

            // A failed navigation still returns a result, with errorText set. A result only means the command was
            // accepted.
            LastNavigationError = (result as Newtonsoft.Json.Linq.JObject)?["errorText"]?.ToString();

            if (null == result)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to Go to [{url}]", this, GPALObjectType.PuppeteerCommunicator);
            else if (false == string.IsNullOrEmpty(LastNavigationError))
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to Go to [{url}][{LastNavigationError}]", this, GPALObjectType.PuppeteerCommunicator);
            else
                //await WaitForEvent("Page.loadEventFired", TimeSpan.FromSeconds(30));
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Go to [{url}]", this, GPALObjectType.PuppeteerCommunicator);

            return null != result && true == string.IsNullOrEmpty(LastNavigationError);
        }

        // Implementation for "back"
        /// <summary>
        /// Navigates the page back to the previous entry in its navigation history, if one exists.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the navigation result, or null if there is no previous history entry.</returns>
        /// <example>
        /// <code>
        /// await communicator.Back();
        /// </code>
        /// </example>
        public async Task<dynamic> Back(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            dynamic history = await SendCommand<object>(DevToolsMethods.PageGetNavigationHistory, new { }, sessionId).ConfigureAwait(false);
            if (history?.currentIndex != null)
            {
                var entries = (IEnumerable<dynamic>)history.entries;
                var nextEntry = entries.ElementAtOrDefault((int)history.currentIndex - 1);
                if (nextEntry?.id != null)
                {
                    string originBefore = (await PageOriginAndState(sessionId).ConfigureAwait(false))?.Split('|')[0];
                    var result = await SendCommand(DevToolsMethods.PageNavigateToHistoryEntry, new { entryId = (int)nextEntry.id }, sessionId).ConfigureAwait(false);

                    await WaitForNewDocument(originBefore, sessionId).ConfigureAwait(false);
                    return result;
                }
            }
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No previous page in history", this, GPALObjectType.PuppeteerClient);
            return null;
        }

        // Implementation for "check-network-idle"
        /// <summary>
        /// Answers at once whether the network has been idle: at most <paramref name="maxConnections"/> requests in
        /// flight for the whole of the last <paramref name="pruneMs"/>. The caller asks again until it gets true or
        /// its own timeout ends.<br/>
        /// The first ask on a tab turns Network on and starts tracking, and answers false; Network stays on, so later
        /// asks look back over what the tab did since.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <param name="maxConnections">The most requests in flight that still count as idle.</param>
        /// <param name="pruneMs">How far back, in milliseconds, the in-flight count must have stayed at or below maxConnections.</param>
        /// <param name="sessionToken">An opaque token used to deduplicate repeated status messages across calls.</param>
        /// <returns>Task that resolves to true if the network is idle, otherwise false.</returns>
        public async Task<bool> CheckNetworkIdle(string sessionId = null, int maxConnections = 0, int pruneMs = 3000, string sessionToken = null)
        {
            bool retVal = false;
            bool startTracking = false;
            string currentErrorMessage = null;

            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                lastSessionToken = sessionToken;
            }

            currentErrorMessage = $"Waiting for network to idle to [{maxConnections}] connections for [{pruneMs}] ms";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, this, GPALObjectType.PuppeteerCommunicator);
            }

            sessionId ??= GetEffectiveSessionId();

            lock (_idleLock)
                startTracking = _idleSessions.Add(sessionId);

            if (true == startTracking)
            {
                // the tab's main frame has the tab's target id, so its new documents can be told from a frame's
                string mainFrameId;

                lock (_tabQueueLock)
                    mainFrameId = _windowSessionsQueue.SelectMany(window => window.TabQueue).FirstOrDefault(tab => sessionId == tab.Value).Key;

                await SendCommand(DevToolsMethods.NetworkEnable, new { }, sessionId).ConfigureAwait(false);

                lock (_idleLock)
                {
                    _idleTrackedSince[sessionId] = DateTime.UtcNow;
                    _idleMainFrame[sessionId] = mainFrameId;
                }
            }
            else
            {
                // Idle when tracking has run for pruneMs, the tab's count is at or below maxConnections now, and it has
                // not dropped from above maxConnections within the last pruneMs.
                DateTime windowStart = DateTime.UtcNow.AddMilliseconds(-pruneMs);

                lock (_idleLock)
                    retVal = true == _idleTrackedSince.TryGetValue(sessionId, out DateTime trackedSince) && trackedSince <= windowStart
                        && (false == _idleInflight.TryGetValue(sessionId, out Dictionary<string, (string FrameId, string Url, DateTime Sent)> inflight) || inflight.Count <= maxConnections)
                        && (false == _idleDroppedBelow.TryGetValue(sessionId, out Dictionary<int, DateTime> droppedBelow)
                            || false == droppedBelow.TryGetValue(maxConnections + 1, out DateTime dropped)
                            || dropped <= windowStart);

                // Not idle: every 5 s, what the tab is waiting on. The oldest open requests with how long each has
                // been open, and when the count last fell to maxConnections, so a page that never settles shows
                // whether requests are stuck open or new ones keep starting
                string waitingOn = null;

                lock (_idleLock)
                    if (false == retVal && (false == _idleLastExplained.TryGetValue(sessionId, out DateTime explained) || explained <= DateTime.UtcNow.AddSeconds(-5)))
                    {
                        DateTime now = DateTime.UtcNow;
                        DateTime lastFell = DateTime.MinValue;

                        _idleLastExplained[sessionId] = now;
                        _idleInflight.TryGetValue(sessionId, out Dictionary<string, (string FrameId, string Url, DateTime Sent)> open);

                        if (true == _idleDroppedBelow.TryGetValue(sessionId, out Dictionary<int, DateTime> drops))
                            drops.TryGetValue(maxConnections + 1, out lastFell);

                        string fell = DateTime.MinValue == lastFell ? "never" : $"{(int)(now - lastFell).TotalMilliseconds} ms ago";
                        IEnumerable<string> oldest = (open?.Values ?? Enumerable.Empty<(string FrameId, string Url, DateTime Sent)>())
                            .OrderBy(request => request.Sent)
                            .Take(5)
                            .Select(request => $"{(int)(now - request.Sent).TotalSeconds} s {(100 < request.Url.Length ? request.Url.Substring(0, 100) + "..." : request.Url)}");

                        waitingOn = $"[{open?.Count ?? 0}] requests open on session [{sessionId}], last at [{maxConnections}] or fewer [{fell}]: {string.Join(" | ", oldest)}";
                    }

                if (null != waitingOn)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, waitingOn, this, GPALObjectType.PuppeteerCommunicator);
            }

            currentErrorMessage = $"Network IS{(retVal ? "" : " NOT")} Idle. Status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, this, GPALObjectType.PuppeteerCommunicator);
            }
            return retVal;
        }

        // Keeps the in-flight requests for CheckNetworkIdle, per tab: a request counts from requestWillBeSent until
        // loadingFinished or loadingFailed. A redirect reuses the request id, so it stays one request. A request that
        // started before tracking did is never counted, so its finish is ignored.
        //
        // A frame starting a new document drops what its old document left in flight, and the main frame starting one
        // drops everything the tab had, its old frames included. Chrome does not always report those requests as
        // finished, after a navigation that moves the tab to a new process most of all, and a tab whose list never
        // empties never goes idle again.
        //
        // A finish closes the request on whichever tab's list holds it. A cross-site frame runs in a process and a
        // session of its own, so its document request starts on the page's session and finishes on the frame's.
        // Request ids are unique across the browser. A blob: or data: url never reaches the network, and Chrome
        // reports it sent but never finished, so it is not counted.
        private void TrackNetworkIdle(string method, dynamic paramsData, string sessionId)
        {
            string requestId = paramsData?.requestId?.ToString();
            string url = paramsData?.request?.url?.ToString() ?? "";
            bool local = url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);

            if (null != requestId && null != sessionId && false == local)
                lock (_idleLock)
                {
                    if ("Network.requestWillBeSent" == method)
                    {
                        if (false == _idleInflight.TryGetValue(sessionId, out Dictionary<string, (string FrameId, string Url, DateTime Sent)> inflight))
                            _idleInflight[sessionId] = inflight = new Dictionary<string, (string FrameId, string Url, DateTime Sent)>();

                        if (false == _idleDroppedBelow.TryGetValue(sessionId, out Dictionary<int, DateTime> droppedBelow))
                            _idleDroppedBelow[sessionId] = droppedBelow = new Dictionary<int, DateTime>();

                        string frameId = paramsData?.frameId?.ToString() ?? "";

                        // a new document is the request whose id is its loader's
                        if ("Document" == paramsData?.type?.ToString() && requestId == paramsData?.loaderId?.ToString())
                        {
                            _idleMainFrame.TryGetValue(sessionId, out string mainFrameId);

                            List<string> left = inflight
                                .Where(request => request.Key != requestId && (frameId == mainFrameId || frameId == request.Value.FrameId))
                                .Select(request => request.Key)
                                .ToList();

                            if (0 < left.Count)
                            {
                                droppedBelow[inflight.Count] = DateTime.UtcNow;

                                foreach (string request in left)
                                    inflight.Remove(request);
                            }
                        }

                        // A frame's own document is not counted. A cross-site frame loads in a process of its own, whose
                        // session GPAL does not attach to, so its finish never arrives. Requests reported on this tab's
                        // session, a same-site frame's included, still count.
                        _idleMainFrame.TryGetValue(sessionId, out string mainFrame);

                        bool frameDocument = "Document" == paramsData?.type?.ToString() && null != mainFrame && frameId != mainFrame;

                        if (false == frameDocument)
                            inflight[requestId] = (frameId, url, DateTime.UtcNow);
                    }
                    else
                        foreach (KeyValuePair<string, Dictionary<string, (string FrameId, string Url, DateTime Sent)>> tab in _idleInflight)
                            if (true == tab.Value.ContainsKey(requestId))
                            {
                                if (false == _idleDroppedBelow.TryGetValue(tab.Key, out Dictionary<int, DateTime> droppedBelow))
                                    _idleDroppedBelow[tab.Key] = droppedBelow = new Dictionary<int, DateTime>();

                                droppedBelow[tab.Value.Count] = DateTime.UtcNow;
                                tab.Value.Remove(requestId);
                            }
                }
        }

        // Back and Forward return once the page they went to is loaded, as GoTo does. A command sent sooner reaches
        // a page still loading, and Edge refuses a screenshot of one. A new performance.timeOrigin marks the new
        // document, since readyState alone still reads complete on the page being left.
        private async Task WaitForNewDocument(string originBefore, string sessionId)
        {
            int timeoutMs = Browser?.BrowserSettings.WaitForDocumentReadyTimeoutMs ?? 30_000;
            var waited = System.Diagnostics.Stopwatch.StartNew();
            bool loaded = null == originBefore;   // nothing to compare against, so nothing to wait for

            while (false == loaded && waited.ElapsedMilliseconds < timeoutMs)
            {
                await Task.Delay(100, _cts.Token).ConfigureAwait(false);

                string now = await PageOriginAndState(sessionId).ConfigureAwait(false);

                loaded = null != now
                    && false == now.StartsWith(originBefore + "|", StringComparison.Ordinal)
                    && (true == now.EndsWith("|complete", StringComparison.Ordinal) || true == now.EndsWith("|interactive", StringComparison.Ordinal));
            }
        }

        // performance.timeOrigin and document.readyState of the top page, separated by a bar; null while it cannot answer
        private async Task<string> PageOriginAndState(string sessionId)
        {
            JObject result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
            {
                expression = "performance.timeOrigin + '|' + document.readyState",
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string retVal = result?["result"]?["value"]?.ToString();

            return retVal;
        }

        // Implementation for "forward"
        /// <summary>
        /// Navigates forward one entry in the page's navigation history, if a forward entry exists.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the result of navigating to the next history entry, or null if there is no forward entry.</returns>
        public async Task<dynamic> Forward(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            dynamic history = await SendCommand<object>(DevToolsMethods.PageGetNavigationHistory, new { }, sessionId).ConfigureAwait(false);
            if (history?.currentIndex != null)
            {
                var entries = (IEnumerable<dynamic>)history.entries;
                var nextEntry = entries.ElementAtOrDefault((int)history.currentIndex + 1);
                if (nextEntry?.id != null)
                {
                    string originBefore = (await PageOriginAndState(sessionId).ConfigureAwait(false))?.Split('|')[0];
                    var result = await SendCommand(DevToolsMethods.PageNavigateToHistoryEntry, new { entryId = (int)nextEntry.id }, sessionId).ConfigureAwait(false);

                    await WaitForNewDocument(originBefore, sessionId).ConfigureAwait(false);
                    return result;
                }
            }
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No next page in history", this, GPALObjectType.PuppeteerClient);
            return null;
        }
        // Implementation for "get-current-url"
        /// <summary>
        /// Gets the URL of the currently active page/frame by evaluating <c>window.location.href</c>.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the current page URL.</returns>
        /// <example>
        /// <code>
        /// string url = await communicator.GetCurrentUrl();
        /// </code>
        /// </example>
        public async Task<string> GetCurrentUrl(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Evaluate 'window.location.toString()' to get the current URL
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.location.href",
                    contextId = CurrentContextId,
                    returnByValue = true 
                }, sessionId).ConfigureAwait(false);

            // Extract and log the URL
            return result?.result?.value;
        }

        // Implementation for "get-ready-status"
        /// <summary>
        /// Gets the current <c>document.readyState</c> of the page, logging the status (and suppressing repeated
        /// identical messages within the same session token).
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <param name="sessionToken">A token identifying the current logical session; when it changes, error/log state is reset.</param>
        /// <returns>Task that resolves to the document's ready state (e.g. "loading", "interactive", "complete").</returns>
        public async Task<string> GetReadyStatus(string sessionId, string sessionToken)
        {
            sessionId ??= GetEffectiveSessionId();
            string retVal = null;

            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                lastSessionToken = sessionToken;
            }

            JObject result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
            {
                expression = "document.readyState",
                contextId = CurrentContextId,
                returnByValue = false
            }, sessionId).ConfigureAwait(false);

            if (null != result?["result"]?["value"])
                retVal = result?["result"]?["value"]?.ToString();
            else
                retVal = @"""loading""";

            // each status read once, as DEBUG; CheckDocumentReady publishes the result of the wait
            string currentErrorMessage = $"Document.Ready status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, currentErrorMessage, null, GPALObjectType.None);
            }

            return retVal;
        }

        // Implementation for "goto"
        /// <summary>
        /// Navigates the current page/tab to the given URL.
        /// </summary>
        /// <param name="url">The URL to navigate to.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if navigation succeeded, otherwise false.</returns>
        /// <example>
        /// <code>
        /// await communicator.GoTo("https://example.com");
        /// </code>
        /// </example>
        public async Task<bool> GoTo(string url, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await NavigateToUrl(url, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Backend node id of the document root. It changes with each new document, so comparing before and after a
        /// click shows whether the page changed. 0 if the browser did not answer, meaning unknown.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>The root backend node id, zero if it could not be read</returns>
        internal async Task<long> DocumentMark(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var doc = await SendCommand<object>(DevToolsMethods.DOMGetDocument, new { depth = 0 }, sessionId).ConfigureAwait(false);
            long mark = null == doc || null == doc.root ? 0 : (long)doc.root.backendNodeId;

            return mark;
        }

        // Implementation for "refresh"
        /// <summary>
        /// Reloads the current page.
        /// Implementation for the "refresh" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous reload operation.</returns>
        public async Task Refresh(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await SendCommand<object>(DevToolsMethods.PageReload, new { }, sessionId).ConfigureAwait(false);
        }
    }
}
