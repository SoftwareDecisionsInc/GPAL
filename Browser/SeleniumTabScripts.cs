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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;
using System.Net.NetworkInformation;
using System.Net;
using System.Text.RegularExpressions;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Runs Selenium's registered scripts in tabs opened after they were registered, including tabs the page opens.
    /// chromedriver registers a script only on the tab that is current, so this keeps a CDP connection of its own to
    /// the browser. Chrome holds every new tab until the scripts are registered on it, then it is let go, so they run
    /// on its first page. Chrome and Edge only.
    /// <br/><br/>
    /// The same connection counts each tab's requests for network idle, from the browser's own Network events, and
    /// reads document.readyState for the document-ready wait, so neither runs anything through chromedriver.
    /// </summary>
    internal sealed class SeleniumTabScripts : IDisposable
    {
        // A registered script, the identifier chromedriver gave it on the tab that was current, and its identifier
        // on each tab this connection held
        private class Registered
        {
            public string Source;
            public string FirstTabIdentifier;
            public Dictionary<string, string> Identifiers = new Dictionary<string, string>();
        }

        private readonly BrowserSettings _browserSettings;
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JObject>> _replies = new ConcurrentDictionary<int, TaskCompletionSource<JObject>>();
        private readonly List<Registered> _scripts = new List<Registered>();
        private readonly HashSet<string> _sessions = new HashSet<string>();   // held tabs still open
        private bool _holding = false;   // new tabs are held, from the first registered script on
        private int _nextId = 0;

        // Network idle, as PuppeteerCommunicator keeps it. CheckNetworkIdle turns Network on for a tab at its first
        // ask and leaves it on; Handle keeps these up to date from then on.
        private readonly object _idleLock = new object();
        // By tab target id: the session this connection has on it
        private readonly Dictionary<string, string> _sessionOf = new Dictionary<string, string>();
        // By session: the requests sent and not yet finished or failed, each with its frame, url and when it went
        private readonly Dictionary<string, Dictionary<string, (string FrameId, string Url, DateTime Sent)>> _idleInflight = new Dictionary<string, Dictionary<string, (string FrameId, string Url, DateTime Sent)>>();
        // By session: for each count n, when the in-flight count last dropped below n
        private readonly Dictionary<string, Dictionary<int, DateTime>> _idleDroppedBelow = new Dictionary<string, Dictionary<int, DateTime>>();
        // By session: the tab's main frame, whose id is the tab's target id
        private readonly Dictionary<string, string> _idleMainFrame = new Dictionary<string, string>();
        // By session: when tracking started; the look-back cannot reach past it
        private readonly Dictionary<string, DateTime> _idleTrackedSince = new Dictionary<string, DateTime>();
        // By session: when a wait that is not idle last said what it is waiting on
        private readonly Dictionary<string, DateTime> _idleLastExplained = new Dictionary<string, DateTime>();

        private SeleniumTabScripts(BrowserSettings browserSettings)
        {
            _browserSettings = browserSettings;
        }

        /// <summary>
        /// Connects to the browser chromedriver is driving. New tabs are held only once a script is registered, by
        /// <see cref="Add"/>; network idle attaches to the tab it is asked about and needs no hold.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings of the Selenium browser</param>
        /// <returns>The connection, or null when the driver reports no debugger address</returns>
        internal static SeleniumTabScripts Start(BrowserSettings browserSettings)
        {
            SeleniumTabScripts retVal = null;

            // chromedriver reports the browser's debugging address under its vendor capability
            string key = BrowserType.Edge == browserSettings.BrowserType ? "ms:edgeOptions" : "goog:chromeOptions";
            var vendor = ((IHasCapabilities)browserSettings.BrowserDriver).Capabilities.GetCapability(key) as Dictionary<string, object>;
            object address = null;
            vendor?.TryGetValue("debuggerAddress", out address);

            if (null == address)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] gave no debugger address, so injected scripts reach only the tab that was current when they were registered", browserSettings.Browser, GPALObjectType.Browser);
            else
            {
                retVal = new SeleniumTabScripts(browserSettings);
                retVal.Connect(address.ToString()).GetAwaiter().GetResult();
            }

            return retVal;
        }

        private async Task Connect(string address)
        {
            string version;
            using (var http = new HttpClient())
                version = await http.GetStringAsync($"http://{address}/json/version").ConfigureAwait(false);

            await _socket.ConnectAsync(new Uri((string)JObject.Parse(version)["webSocketDebuggerUrl"]), _cts.Token).ConfigureAwait(false);

            _ = Task.Run(() => Read());
        }

        /// <summary>
        /// Keeps a script chromedriver registered on the current tab, and registers it on every held tab still open,
        /// where it runs from the next page on. The first script asks Chrome to hold every new tab from then on, so
        /// the scripts are on it before its first page.
        /// </summary>
        /// <param name="source">The script</param>
        /// <param name="firstTabIdentifier">The identifier chromedriver returned for it</param>
        internal void Add(string source, string firstTabIdentifier)
        {
            var registered = new Registered { Source = source, FirstTabIdentifier = firstTabIdentifier };
            List<string> open;
            bool first;

            lock (_scripts)
            {
                first = false == _holding;
                _holding = true;
                _scripts.Add(registered);
                open = _sessions.ToList();
            }

            if (true == first)
                Send("Target.setAutoAttach", new
                {
                    autoAttach = true,
                    waitForDebuggerOnStart = true,
                    flatten = true,
                    filter = new object[] { new { type = "page" } }
                }, null).GetAwaiter().GetResult();

            foreach (string sessionId in open)
                RegisterOn(registered, sessionId).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Removes a script from every held tab still open and stops putting it on new ones.
        /// </summary>
        /// <param name="firstTabIdentifier">The identifier chromedriver returned for it</param>
        internal void Remove(string firstTabIdentifier)
        {
            Registered registered;
            List<KeyValuePair<string, string>> open;

            lock (_scripts)
            {
                registered = _scripts.FirstOrDefault(r => r.FirstTabIdentifier == firstTabIdentifier);
                _scripts.Remove(registered);
                open = registered?.Identifiers.Where(pair => _sessions.Contains(pair.Key)).ToList() ?? new List<KeyValuePair<string, string>>();
            }

            foreach (var pair in open)
                Send("Page.removeScriptToEvaluateOnNewDocument", new { identifier = pair.Value }, pair.Key).GetAwaiter().GetResult();
        }

        private async Task RegisterOn(Registered registered, string sessionId)
        {
            await Send("Page.enable", new { }, sessionId).ConfigureAwait(false);
            JObject reply = await Send("Page.addScriptToEvaluateOnNewDocument", new { source = registered.Source }, sessionId).ConfigureAwait(false);

            lock (_scripts)
                registered.Identifiers[sessionId] = (string)reply?["result"]?["identifier"];
        }

        // A held tab gets every script, then is let go. Every command is on the wire before any answer is awaited,
        // the release right behind them, as PuppeteerCommunicator.OnAttachedToTarget does. Chrome answers nothing on
        // a held tab until it is let go, and a popup held longer, as brotector opens, crashes the tab that opened it.
        // The release is sent even when registering fails, or the tab would stay blank.
        private async Task PrepareAndRelease(string sessionId)
        {
            List<Registered> scripts;

            lock (_scripts)
            {
                scripts = _scripts.ToList();
                _sessions.Add(sessionId);
            }

            // Each Send takes the send lock in the order it was called, so Page.enable goes first, the scripts follow
            // in order and the release goes last
            Task<JObject> enabling = Send("Page.enable", new { }, sessionId);
            List<Task<JObject>> adding = scripts.Select(registered => Send("Page.addScriptToEvaluateOnNewDocument", new { source = registered.Source }, sessionId)).ToList();
            Task<JObject> releasing = Send("Runtime.runIfWaitingForDebugger", new { }, sessionId);

            await Task.WhenAll(adding.Prepend(enabling).Append(releasing)).ConfigureAwait(false);

            lock (_scripts)
            {
                for (int i = 0; i < scripts.Count; i++)
                    scripts[i].Identifiers[sessionId] = (string)adding[i].Result?["result"]?["identifier"];
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Registered [{scripts.Count}] scripts on new tab session [{sessionId}]", _browserSettings.Browser, GPALObjectType.Browser);
        }

        /// <summary>
        /// The session this connection has on a tab, attaching to it the first time it is asked for
        /// </summary>
        /// <param name="targetId">The tab's target id, from chromedriver's own session</param>
        /// <returns>The session, or null when the tab could not be attached to</returns>
        private string SessionFor(string targetId)
        {
            string sessionId = null;

            lock (_idleLock)
                if (null != targetId)
                    _sessionOf.TryGetValue(targetId, out sessionId);

            if (null == sessionId && null != targetId)
            {
                JObject attached = Send("Target.attachToTarget", new { targetId, flatten = true }, null).GetAwaiter().GetResult();
                sessionId = (string)attached?["result"]?["sessionId"];

                if (null != sessionId)
                    lock (_idleLock)
                        _sessionOf[targetId] = sessionId;
            }

            return sessionId;
        }

        /// <summary>
        /// The tab's document.readyState, read with Runtime.evaluate on this connection. The page's own scripts see
        /// nothing of it, where chromedriver's ExecuteScript runs through a wrapper a page can catch.
        /// </summary>
        /// <param name="targetId">The tab's target id, from chromedriver's own session</param>
        /// <returns>loading, interactive or complete; null while the page has no document to ask</returns>
        internal string ReadyState(string targetId)
        {
            string sessionId = SessionFor(targetId);
            JObject reply = null == sessionId ? null : Send("Runtime.evaluate", new { expression = "document.readyState", returnByValue = true }, sessionId).GetAwaiter().GetResult();

            return (string)reply?["result"]?["result"]?["value"];
        }

        /// <summary>
        /// Answers at once whether a tab's network has been idle: at most <paramref name="maxConnections"/> requests
        /// in flight for the whole of the last <paramref name="pruneMs"/>, counted from the browser's Network events.
        /// The first ask on a tab attaches to it, turns Network on and starts tracking, and answers false; later asks
        /// look back over what the tab did since.
        /// </summary>
        /// <param name="targetId">The tab's target id, from chromedriver's own session</param>
        /// <param name="maxConnections">The most requests in flight that still count as idle</param>
        /// <param name="pruneMs">How far back, in milliseconds, the in-flight count must have stayed at or below maxConnections</param>
        /// <returns>True if the tab's network is idle, false otherwise</returns>
        internal bool CheckNetworkIdle(string targetId, int maxConnections, int pruneMs)
        {
            bool retVal = false;
            string sessionId = SessionFor(targetId);
            bool tracking = false;

            lock (_idleLock)
                tracking = null != sessionId && _idleTrackedSince.ContainsKey(sessionId);

            if (null != sessionId && false == tracking)
            {
                Send("Network.enable", new { }, sessionId).GetAwaiter().GetResult();

                lock (_idleLock)
                {
                    _idleTrackedSince[sessionId] = DateTime.UtcNow;
                    _idleMainFrame[sessionId] = targetId;
                }
            }
            else if (null != sessionId)
            {
                // Idle when tracking has run for pruneMs, the tab's count is at or below maxConnections now, and it has
                // not dropped from above maxConnections within the last pruneMs.
                DateTime windowStart = DateTime.UtcNow.AddMilliseconds(-pruneMs);
                string waitingOn = null;

                lock (_idleLock)
                {
                    retVal = true == _idleTrackedSince.TryGetValue(sessionId, out DateTime trackedSince) && trackedSince <= windowStart
                        && (false == _idleInflight.TryGetValue(sessionId, out Dictionary<string, (string FrameId, string Url, DateTime Sent)> inflight) || inflight.Count <= maxConnections)
                        && (false == _idleDroppedBelow.TryGetValue(sessionId, out Dictionary<int, DateTime> droppedBelow)
                            || false == droppedBelow.TryGetValue(maxConnections + 1, out DateTime dropped)
                            || dropped <= windowStart);

                    // Not idle: every 5 s, the oldest open requests with how long each has been open, and when the
                    // count last fell to maxConnections
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

                        waitingOn = $"[{open?.Count ?? 0}] requests open on tab [{targetId}], last at [{maxConnections}] or fewer [{fell}]: {string.Join(" | ", oldest)}";
                    }
                }

                if (null != waitingOn)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, waitingOn, _browserSettings.Browser, GPALObjectType.Browser);
            }

            return retVal;
        }

        // Keeps the in-flight requests for CheckNetworkIdle, per tab, by the rules PuppeteerCommunicator.TrackNetworkIdle
        // follows: a request counts from requestWillBeSent until loadingFinished or loadingFailed, a frame starting a
        // new document drops what its old document left in flight, the main frame starting one drops everything the
        // tab had, a finish closes the request on whichever tab's list holds it, a frame's own document is not
        // counted, and blob: and data: urls are not counted.
        private void TrackNetworkIdle(string method, JObject paramsData, string sessionId)
        {
            string requestId = (string)paramsData?["requestId"];
            string url = (string)paramsData?["request"]?["url"] ?? "";
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

                        string frameId = (string)paramsData["frameId"] ?? "";
                        bool document = "Document" == (string)paramsData["type"];
                        _idleMainFrame.TryGetValue(sessionId, out string mainFrameId);

                        // a new document is the request whose id is its loader's
                        if (true == document && requestId == (string)paramsData["loaderId"])
                        {
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

                        if (false == (true == document && null != mainFrameId && frameId != mainFrameId))
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

        private async Task<JObject> Send(string method, object parameters, string sessionId)
        {
            int id = Interlocked.Increment(ref _nextId);
            var reply = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _replies[id] = reply;

            var message = new JObject { ["id"] = id, ["method"] = method, ["params"] = JObject.FromObject(parameters) };
            if (null != sessionId)
                message["sessionId"] = sessionId;

            byte[] bytes = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(message));

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            Task done = await Task.WhenAny(reply.Task, Task.Delay(10_000)).ConfigureAwait(false);
            _replies.TryRemove(id, out _);

            return reply.Task == done ? reply.Task.Result : null;
        }

        private async Task Read()
        {
            var buffer = new byte[65536];
            var message = new MemoryStream();

            // The connection ends with the browser, or with Dispose
            try
            {
                while (WebSocketState.Open == _socket.State && false == _cts.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token).ConfigureAwait(false);

                    if (WebSocketMessageType.Close == result.MessageType)
                        break;

                    message.Write(buffer, 0, result.Count);

                    if (true == result.EndOfMessage)
                    {
                        Handle(JObject.Parse(Encoding.UTF8.GetString(message.ToArray())));
                        message.SetLength(0);
                    }
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException)
            {
            }
        }

        private void Handle(JObject message)
        {
            string method = (string)message["method"];

            if (null != message["id"])
            {
                if (true == _replies.TryGetValue((int)message["id"], out var reply))
                    reply.TrySetResult(message);
            }
            else if ("Target.attachedToTarget" == method && true == (bool?)message["params"]?["waitingForDebugger"])
            {
                // Off the reader, which receives the replies PrepareAndRelease waits on
                string sessionId = (string)message["params"]["sessionId"];
                _ = Task.Run(() => PrepareAndRelease(sessionId));
            }
            else if ("Target.detachedFromTarget" == method)
            {
                string sessionId = (string)message["params"]?["sessionId"];

                lock (_scripts)
                    _sessions.Remove(sessionId);

                // a closed tab's requests never count against another
                lock (_idleLock)
                {
                    foreach (string targetId in _sessionOf.Where(pair => sessionId == pair.Value).Select(pair => pair.Key).ToList())
                        _sessionOf.Remove(targetId);

                    _idleInflight.Remove(sessionId);
                    _idleDroppedBelow.Remove(sessionId);
                    _idleMainFrame.Remove(sessionId);
                    _idleTrackedSince.Remove(sessionId);
                    _idleLastExplained.Remove(sessionId);
                }
            }
            else if ("Network.requestWillBeSent" == method || "Network.loadingFinished" == method || "Network.loadingFailed" == method)
                TrackNetworkIdle(method, message["params"] as JObject, (string)message["sessionId"]);
        }

        public void Dispose()
        {
            _cts.Cancel();
            _socket.Abort();
            _socket.Dispose();
        }
    }
}
