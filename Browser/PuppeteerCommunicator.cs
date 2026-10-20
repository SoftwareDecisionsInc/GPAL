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

// written with help from grok
namespace GenerallyPositive.Browser
{
    public class WindowSession
    {
        /// <summary>
        /// The CDP browser context id (window) this session belongs to.
        /// </summary>
        public string BrowserContextId { get; set; }
        /// <summary>
        /// Queue of (sessionId, targetId) pairs for tabs open within this window session.
        /// </summary>
        public ConcurrentQueue<KeyValuePair<string, string>> TabQueue { get; set; }
        /// <summary>
        /// Index of the currently active tab within <see cref="TabQueue"/>.
        /// </summary>
        public int ActiveTabIndex { get; set; }

        public WindowSession(string browserContextId, ConcurrentQueue<KeyValuePair<string, string>> tabQueue, int activeTabIndex = 0)
        {
            BrowserContextId = browserContextId;
            TabQueue = tabQueue ?? new ConcurrentQueue<KeyValuePair<string, string>>();
            ActiveTabIndex = activeTabIndex;
        }
    }

    public class PuppeteerCommunicator
    {
        private string _puppeteerUrl;
        private ClientWebSocket _webSocket;

        public string webSocketUrl { get; private set; }

        private int _activeWindowIndex = 0;

        /// <summary>
        ///  server response code from last operation
        /// </summary>
        public int ServerResponseCode { 
            get => Browser.ServerResponseCode; 
            set => Browser.ServerResponseCode = value; 
        }
        /// <summary>
        /// Maps CDP frame ids to their execution context ids.
        /// </summary>
        private Dictionary<string, long> FrameIdToContextId { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);
        /// <summary>
        /// Session id of the frame currently being interacted with (e.g. via InFrame/InShadowDom), or null for the main frame.
        /// </summary>
        private string CurrentFrameSessionId { get; set; }

        /// <summary>
        /// Execution context id of the frame/shadow root currently being interacted with, or null for the main frame.
        /// </summary>
        private long? CurrentContextId { get; set; }
        /// <summary>
        /// CDP frame id currently being interacted with, or null for the main frame.
        /// </summary>
        private string CurrentFrameId { get; set; }
        /// <summary>
        /// Object id of the shadow root currently being interacted with, or null if not inside a shadow root.
        /// </summary>
        // Root that scoped selectors resolve against: a shadow root from InShadowDom or an element from
        // InElement. Holds a remote object, which is valid only in the execution context it came from and is
        // cleared when that context changes
        private string CurrentRootObjectId { get; set; }

        /// <summary>
        /// Pending CDP command responses, keyed by command id, completed when the corresponding response message arrives.
        /// </summary>
        /// <summary>
        /// Sends Page.enable to the active tab so Page events arrive. Passes the effective session id; SendCommand
        /// with a null session sends at browser level.
        /// </summary>
        internal async Task EnablePageEvents()
        {
            await SendCommand<object>(DevToolsMethods.PageEnable, new { }, GetEffectiveSessionId()).ConfigureAwait(false);
        }

        private ConcurrentDictionary<long, TaskCompletionSource<dynamic>> _responseTasks { get;  } = new ConcurrentDictionary<long, TaskCompletionSource<dynamic>>();
        // what each command waiting on a reply asked for and when it was sent, so the pipe reader can name a stalled one
        private ConcurrentDictionary<long, (DevToolsMethods Method, DateTime Sent)> _commandSent { get; } = new ConcurrentDictionary<long, (DevToolsMethods Method, DateTime Sent)>();
        // Network idle tracking. CheckNetworkIdle turns Network on for a tab and leaves it on; the readers keep these up
        // to date from then on, so each ask answers at once from what happened before it.
        private readonly object _idleLock = new object();
        // By session: the requests sent and not yet finished or failed, each with the frame that sent it, its url and
        // when it went. Kept per tab, so another tab's requests, or a closed one's, never count against the tab asked
        // about.
        private Dictionary<string, Dictionary<string, (string FrameId, string Url, DateTime Sent)>> _idleInflight { get; } = new Dictionary<string, Dictionary<string, (string FrameId, string Url, DateTime Sent)>>();
        // By session: when a wait that is not idle last said what it is waiting on.
        private Dictionary<string, DateTime> _idleLastExplained { get; } = new Dictionary<string, DateTime>();
        // By session: for each count n, when the in-flight count last dropped below n.
        private Dictionary<string, Dictionary<int, DateTime>> _idleDroppedBelow { get; } = new Dictionary<string, Dictionary<int, DateTime>>();
        // By session: the tab's main frame, whose id is the tab's target id.
        private Dictionary<string, string> _idleMainFrame { get; } = new Dictionary<string, string>();
        // Sessions CheckNetworkIdle turned Network on for.
        private HashSet<string> _idleSessions { get; } = new HashSet<string>();
        // By session: when tracking started; the look-back cannot reach past it.
        private Dictionary<string, DateTime> _idleTrackedSince { get; } = new Dictionary<string, DateTime>();
        /// <summary>
        /// Queue of WindowSession objects, one per open browser window/context.
        /// </summary>
        internal ConcurrentQueue<WindowSession> _windowSessionsQueue { get; set; } = new ConcurrentQueue<WindowSession>();
        // Held while a method reads the window and tab queues and writes a rebuilt copy back. A new tab is reported
        // by NewTab and by the targetCreated event on another thread; without it one rebuild overwrites the other and
        // a closed tab comes back.
        private readonly object _tabQueueLock = new object();
        /// <summary>
        /// Queue of (sessionId, targetId) pairs for tabs in the currently active window.
        /// </summary>
        public ConcurrentQueue<KeyValuePair<string, string>> CurrentSessions
        {
            get
            {
                var tabQueue = GetActiveWindowTabQueue();
                return tabQueue ?? new ConcurrentQueue<KeyValuePair<string, string>>();
            }
        }
        /// <summary>
        /// The target id of the PuppeteerClient's currently selected tab/session.
        /// </summary>
        public string CurrentTargetID
        {
            get => ((PuppeteerClient)PuppeteerClient)._currentTargetId;
        }
        private int _commandId = 0xdead;
        /// <summary>
        /// True while the communicator's receive loop is running.
        /// </summary>
        internal bool _isRunning { get; set; }
        /// <summary>
        /// Set when the workflow closes the browser. SendCommand then refuses every command without reconnecting.
        /// </summary>
        internal bool Closed { get; set; }
        /// <summary>
        /// True if communicating with the browser via stdio pipes instead of a WebSocket.
        /// </summary>
        internal bool _usePipes { get; set; }
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1); // Added for SendAsync serialization

        // Held only while a command is written. A command sent without _sendSemaphore (a dialog answer, a held tab
        // let go) still waits here, since the pipe and the WebSocket take one write at a time.
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        /// <summary>
        /// The PuppeteerClient that issues commands through this communicator.
        /// </summary>
        internal IPuppeteerClient PuppeteerClient { get; set; }
        /// <summary>
        /// The Browser that owns this communicator.
        /// </summary>
        internal Browser Browser { get; set; }

        // The referrer the next navigation carries, set by override-referrer or google-referrer. NavigateToUrl sends
        // it with Page.navigate and clears it, so it is that page's own referrer and nothing else's.
        private string _navigationReferrer = null;

        // When true, records every request the page sends into CapturedCalls, in Chrome's order. Off by default to
        // save memory.
        internal bool CapturingCalls { get; set; } = false;
        internal List<GPALCall> CapturedCalls { get; } = new List<GPALCall>();

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

        // Maps requestId to its captured call, for RecordStatus.
        private readonly Dictionary<string, GPALCall> _capturedById = new Dictionary<string, GPALCall>();
        /// <summary>
        /// The background task that reads and dispatches incoming CDP messages.
        /// </summary>
        internal Task ReceiveTask { get; set; }
        /// <summary>
        /// Signaled once the receive loop is ready to process messages.
        /// </summary>
        internal TaskCompletionSource<bool> _readerReadyTcs { get; set; }

        // Instance field; a static one would let one browser suppress another's messages.
        List<string> lastErrorMessage = new List<string>();

        // CDP error messages for a node with no layout box: the element exists but is not rendered.
        static readonly string[] NotRenderedAnswers = { "Node does not have a layout object", "Could not compute box model" };
        // CDP error messages for an element that is no longer on the page: it was found on a page since replaced.
        static readonly string[] GoneAnswers = { "No node found for given backend id", "No node with given id found", "Could not find node with given id" };
        // CDP error messages for a session that is gone while its tab is still there, so a fresh session is attached
        // and the command sent again. Not "Not attached to an active page": attaching sends to that same page, gets the
        // same answer and attaches again, without end.
        static readonly string[] DeadSessionAnswers = { "Session with given id not found." };
        bool supressedMessage = false;
        string lastSessionToken;
        // Instance field; a static one would let closing one browser block every other browser.
        bool inExit = false;

        internal class CastDevice
        {
            public CastDevice(string name, string id)
            {
                this.name = name;
                this.id = id;
            }
            public string name;
            public string id;
        }

        internal List<CastDevice> CastDevices = new List<CastDevice>();
        /// <summary>
        /// Name of the cast sink (Chromecast device) currently targeted by CastTab/CastDesktop, or null if not casting.
        /// </summary>
        internal string SinkName { get; set; } = null;

        internal PuppeteerCommunicator(string puppeteerUrl, IBrowser browser, bool usePipes = false, bool capturingCalls = false)
        {
            // Set before InitializeCommunication, which reads it to decide whether to enable the Network domain.
            CapturingCalls = capturingCalls;

            Browser = (Browser)browser;
            // One client per communicator. The client holds its target id, so a shared one would send both browsers'
            // commands to the last one built.
            PuppeteerClient = new PuppeteerClient();
            _usePipes = usePipes;
            _windowSessionsQueue = new ConcurrentQueue<WindowSession>(); // Initialize with new type

            _puppeteerUrl = puppeteerUrl;

            // Longer than the wait inside GetWebSocketUrl, so a browser slow to open its debug port is not cut off
            // early.
            var initTask = InitializeCommunication(Browser);
            bool initFinished;

            try
            {
                initFinished = initTask.Wait(TimeSpan.FromSeconds(45));
            }
            catch (AggregateException ex)
            {
                // Rethrow the inner exception with its own stack, not the AggregateException.
                Exception reason = ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex;

                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(reason).Throw();

                throw;   // never reached, the compiler cannot know that
            }

            if (false == initFinished)
            {
                string msg = $"Puppeteer communication initialization timed out talking to [{puppeteerUrl}]. Workflow canot run without it, Try again.";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                // Throw GPALException so a workflow catching GPALException catches this too.
                throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
            }
            ReceiveTask = initTask.GetAwaiter().GetResult(); // Already finished, this just unwraps results/

            //InitializeCommunication().GetAwaiter().GetResult();

            // No base url: the client dispatches through endpointMap and makes no HTTP request.
            PuppeteerClient.WithPuppeteerCommunicator(this);

            Browser.Started = true;
        }

        public PuppeteerCommunicator()
        {
        }

        /// <summary>
        /// Recovers from a lost CDP connection by tearing down the existing WebSocket (if any) and
        /// re-running <see cref="InitializeCommunication"/> to re-establish the pipe or WebSocket transport.
        /// Has no effect when pipe transport is in use, since pipes are not individually reconnected.
        /// </summary>
        /// <returns>A task that completes once reconnection has been attempted.</returns>
        /// <summary>How many times the debug socket may be re-attempted before the browser is called gone.</summary>
        private const int MaxConnectAttempts = 3;

        /// <summary>
        /// How long Reconnect waits for a cancelled reader to stop before disposing the token source. Covers a reader
        /// stuck in a socket read.
        /// </summary>
        private const int ReaderStopWaitMs = 2_000;

        /// <summary>
        /// Failed connect attempts. A field because InitializeCommunication and Reconnect both retry and both count
        /// against MaxConnectAttempts. Reset when a socket opens.
        /// </summary>
        private int _connectAttempts;

        internal async Task Reconnect()
        {
            // if we lost pipes, then we closed, but ports can be closed due to bad cdp commands or other 'aborted connection' issues
            if (false == _usePipes)
            {
                if (_webSocket != null)
                {
                    try
                    {
                        _webSocket.Dispose();
                    }
                    catch { }
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot restart CDP pipes. Please restart workflow", this, GPALObjectType.PuppeteerCommunicator);

                _cts?.Cancel();

                // Cancel only asks the reader to stop; wait up to ReaderStopWaitMs before disposing. WhenAny does not
                // rethrow; a faulted reader already reported its error.
                if (null != ReceiveTask)
                    await Task.WhenAny(ReceiveTask, Task.Delay(ReaderStopWaitMs)).ConfigureAwait(false);

                _cts?.Dispose();

                await InitializeCommunication(Browser).ConfigureAwait(false);
            }
        }
        /// <summary>
        /// Establishes the underlying CDP transport (named pipes or a WebSocket) to the given <paramref name="Browser"/>,
        /// then performs initial DevTools Protocol setup: enables target discovery, polls <c>Target.getTargets</c> for
        /// the initial set of targets, attaches to the first usable "page" target, and registers the resulting
        /// session/target as the default window/tab in <c>_windowSessionsQueue</c>. Starts the background message
        /// reader task (<see cref="ReceivePipeMessages"/> or <see cref="ReceiveMessages"/>) for the chosen transport.
        /// On WebSocket connection failure or a disposed-socket error, attempts <see cref="Reconnect"/> and recursively
        /// retries initialization.
        /// </summary>
        /// <param name="Browser">The browser instance whose process/pipes or DevTools WebSocket endpoint should be used.</param>
        /// <returns>A task that resolves to the background message-reading task once initialization completes, or null if it failed early.</returns>
        private async Task<Task> InitializeCommunication(Browser Browser)
        {
            dynamic sessionId = null;
            dynamic session = null;
            dynamic targets = null;
            //bool _foundWhatsNew = false;
            Task ourTask = null;

            _readerReadyTcs = new TaskCompletionSource<bool>();
            _cts = new CancellationTokenSource();
            _isRunning = true;

            if (true == _usePipes) // Check if pipe transport is enabled
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Using Puppeteer Pipes", this, GPALObjectType.PuppeteerCommunicator);
                try
                {
                    // Ensure Chrome process is running with pipe transport
                    if (true == Browser?.Process?.HasExited)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Chrome process not running or exited for pipe transport", this, GPALObjectType.PuppeteerCommunicator);
                        return null;
                    }

                    ourTask = Task.Run(async () =>
                    {
                        try
                        {
                            await ReceivePipeMessages(_cts.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            _readerReadyTcs.TrySetCanceled();
                        }
                    }, _cts.Token);

                    // Wait for reader to be ready
                    //await _readerReadyTcs.Task; // - now safe
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Pipe initialization failed", this, GPALObjectType.PuppeteerCommunicator, ex);
                    return null;
                }
            }
            else // WebSocket transport
            {
                var ws = new ClientWebSocket();
                ws.Options.SetBuffer(65536, 65536); // 4MB
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30); // Ping every 30s
                _webSocket = ws;
                _webSocket.Options.KeepAliveInterval = TimeSpan.Zero;
                webSocketUrl = GetWebSocketUrl(_puppeteerUrl);

                if (string.IsNullOrEmpty(webSocketUrl))
                {
                    // No WebSocket url means the browser never opened its debug port. Fail here with that reason.
                    string msg = $"The browser at [{_puppeteerUrl}] never opened its debug port, so there is nothing to drive";

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                    throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Using Puppeteer Ports Connecting to WebSocket: [{webSocketUrl}]", this, GPALObjectType.PuppeteerCommunicator);
                try
                {
                    await _webSocket.ConnectAsync(new Uri(webSocketUrl), _cts.Token).ConfigureAwait(false);
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"WebSocket state after connect: [{_webSocket.State}]", this, GPALObjectType.PuppeteerCommunicator);

                    _connectAttempts = 0;

                    // Yield control for 10ms to let the connection handshake completely settle
                    await Task.Delay(10).ConfigureAwait(false);

                    // Use async/await inside Task.Run so .NET correctly tracks the background worker
                    ourTask = Task.Run(async () => await ReceiveMessages(_cts.Token).ConfigureAwait(false));
                }
                catch (Exception ex)
                {
                    // Connects fail for temporary reasons (heavy navigation, recreated target, load), but a dead
                    // browser fails the same way, so retries stop at MaxConnectAttempts.
                    _connectAttempts++;

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"WebSocket connection failed on attempt [{_connectAttempts}] of [{MaxConnectAttempts}]", this, GPALObjectType.PuppeteerCommunicator, ex);

                    if (MaxConnectAttempts <= _connectAttempts)
                    {
                        string msg = $"The browser at [{_puppeteerUrl}] refused its debug socket on [{_connectAttempts}] attempts, so there is nothing to drive";

                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                        throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
                    }

                    // Wait 1 second longer on each attempt, for a browser still starting.
                    await Task.Delay(_connectAttempts * 1_000).ConfigureAwait(false);

                    await Reconnect().ConfigureAwait(false);

                    return await InitializeCommunication(Browser).ConfigureAwait(false);
                }
            }

            try
            {
                // Enable target discovery
                await SendCommand<object>(DevToolsMethods.TargetSetDiscoverTargets, new { discover = true }, null).ConfigureAwait(false);

                // Get all targets so we can close "what's new"
                // this is handled via command line or profile setting?
                /*
                for (int cnt = 3; 0 < cnt && false == _foundWhatsNew; cnt--)
                {
                    targets = await SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
                    if (targets == null || targets.targetInfos == null)
                    {
                        // the socket is open and the browser answered, but there is no page to drive
                        string msg = $"The browser at [{_puppeteerUrl}] reported no targets, so there is no page to drive";

                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                        throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
                    }

                    // Close any tab with URL "chrome://whats-new/"
                    // NOTE: handled in temp profile
                    //foreach (dynamic target in targets.targetInfos)
                    //{
                    //    string targetType = target.type?.ToString();
                    //    string targetUrl = target.url?.ToString();
                    //    if (targetType == "page" && targetUrl == "chrome://whats-new/")
                    //    {
                    //        string targetId = target.targetId?.ToString();
                    //        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Closing tab with URL: [{targetUrl}] (targetId: [{targetId}])", this, GPALObjectType.PuppeteerCommunicator);
                    //        await SendCommand<object>(DevToolsMethods.TargetCloseTarget, new { targetId }, null);
                    //        _foundWhatsNew = true;
                    //        goto restartAfterClose;
                    //    }
                    //}
                    Thread.Sleep(1_000);
                }
                */

                // Enable Target domain to receive Target.created events
                await SendCommand<object>(DevToolsMethods.TargetSetDiscoverTargets, new { discover = true }, null).ConfigureAwait(false);

                // Call Target.getTargets up to 3 times, 1 second apart, until there is a page target. A just-started
                // browser may not have its first tab yet.
                dynamic pageTarget = null;

                for (int tries = 3; 0 < tries && null == pageTarget; tries--)
                {
                    targets = await SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);

                    if (null == targets || null == targets.targetInfos)
                    {
                        // the socket is open and the browser answered, but there is no page to drive
                        string msg = $"The browser at [{_puppeteerUrl}] reported no targets, so there is no page to drive";

                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                        throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
                    }

                    // Find a page target to attach to
                    pageTarget = ((IEnumerable<dynamic>)targets.targetInfos)
                        .FirstOrDefault(t => t.type == "page" && t.url != "");

                    if (null == pageTarget && 1 < tries)
                        await Task.Delay(1_000).ConfigureAwait(false);
                }

                if (pageTarget != null)
                {
                    var targetId = (string)pageTarget.targetId;
                    session = await SendCommand<object>(DevToolsMethods.TargetAttachToTarget, new
                    {
                        targetId,
                        flatten = true
                    }, null).ConfigureAwait(false);
                    sessionId = (string)session?.sessionId;

                    if (sessionId != null)
                    {
                        string browserContextId = pageTarget.browserContextId?.ToString() ?? "";
                        var tabQueue = new ConcurrentQueue<KeyValuePair<string, string>>();
                        tabQueue.Enqueue(new KeyValuePair<string, string>(targetId, sessionId));
                        lock (_tabQueueLock)
                        {
                            _windowSessionsQueue.Enqueue(new WindowSession(browserContextId, tabQueue, 0)); // Default context, activeTabIndex=0
                            _activeWindowIndex = 0;
                        }
                        ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Attached to targetId: [{targetId}], sessionId: [{sessionId}], URL: [{pageTarget.url}]", this, GPALObjectType.PuppeteerCommunicator);

                        // CaptureCalls can be set before the browser exists, so it comes in through the constructor.
                        // Enable Network here, now that there is a session, so recording starts before the first
                        // navigation.
                        if (true == CapturingCalls)
                            await CaptureCalls(true, sessionId).ConfigureAwait(false);
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to attach to targetId: [{targetId}]", this, GPALObjectType.PuppeteerCommunicator);
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No page target found after initialization", this, GPALObjectType.PuppeteerCommunicator);
                }
                //await SendCommand<object>(DevToolsMethods.PageEnable, new { }, sessionId);
                //await SendCommand<object>(DevToolsMethods.NetworkEnable, new { }, sessionId);

                // Chrome holds every new tab, whoever opens it, until GPAL has registered its scripts there and lets
                // it go, so they run on the tab's first page. See OnAttachedToTarget.
                await SendCommand<object>(DevToolsMethods.TargetSetAutoAttach, new
                {
                    autoAttach = true,
                    waitForDebuggerOnStart = true,
                    flatten = true,
                    filter = new object[] { new { type = "page" } }
                }, null).ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex) when (ex.ObjectName == "System.Net.WebSockets.ClientWebSocket")
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"WebSocket disposed", this, GPALObjectType.Puppeteer, ex);
                await Reconnect().ConfigureAwait(false);
                return await InitializeCommunication(Browser).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Initialization failed", this, GPALObjectType.Puppeteer, ex);
            }

            // Store this reader task so Reconnect waits on the current reader.
            ReceiveTask = ourTask;

            return ourTask;
        }

        /// <summary>
        /// Resolves the DevTools WebSocket endpoint URL (if not already known) and opens a new <see cref="ClientWebSocket"/>
        /// connection to it, storing the result in <c>_webSocket</c>.
        /// </summary>
        /// <returns>A task that completes once the WebSocket connection attempt has finished, or returns early if the endpoint URL could not be determined.</returns>
        private async Task InitializeWebSocket()
        {
            webSocketUrl ??= GetWebSocketUrl(_puppeteerUrl);
            if (string.IsNullOrEmpty(webSocketUrl))
            {
                string msg = $"The browser at [{_puppeteerUrl}] never opened its debug port, so there is nothing to reconnect to";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
            }

            _webSocket = new ClientWebSocket();
            await _webSocket.ConnectAsync(new Uri(webSocketUrl), _cts.Token).ConfigureAwait(false);
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        /// <summary>
        /// Seconds to wait for a CDP reply; 10 covers a busy page. Slow commands (navigation, large documents) use
        /// <see cref="LongCommandTimeoutSeconds"/>.
        /// </summary>
        internal const int DefaultCommandTimeoutSeconds = 10;
        internal const int LongCommandTimeoutSeconds = 60;

        // Commands that do slow work: page loads, reloads, screenshots. They get LongCommandTimeoutSeconds.
        private static readonly HashSet<DevToolsMethods> slowCommands = new HashSet<DevToolsMethods>
        {
            DevToolsMethods.PageNavigate,
            DevToolsMethods.PageNavigateToHistoryEntry,
            DevToolsMethods.PageReload,
            DevToolsMethods.PageCaptureScreenshot,
        };

        public async Task<dynamic> SendCommand<TParameters>(DevToolsMethods method, TParameters parameters, string sessionId, int retries = 3, bool doNotGetSemaphore = false, int timeoutSeconds = 0)
        {
            const int SW_MINIMIZE = 6;

            // 0 means no timeout given: long for slowCommands, default otherwise.
            if (0 >= timeoutSeconds)
                timeoutSeconds = slowCommands.Contains(method) ? LongCommandTimeoutSeconds : DefaultCommandTimeoutSeconds;
            const int SW_RESTORE = 9;
            const int SW_MAXIMIZE = 3;

            if (true == inExit)
                return null;

            if (true == Closed)
                throw new GPALException($"{GPAL.MyMethodName()}: The browser was closed, so there is nothing left to send [{method}] to");

            // For browser-level commands (Target.*), force null sessionId - we should have this properly managed in our code - but if someone else calls this from their code...
            if (method.ToString().StartsWith("Target") && null != sessionId)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Session ID [{sessionId}] cannot be used for 'Target' CDP commands. Using null.", this, GPALObjectType.PuppeteerCommunicator);
                sessionId = null;
            }

            // Handle window state commands directly
            if (method == DevToolsMethods.MinimizeWindow)
            {
                try
                {
                    if (false == Browser?.Process?.HasExited)
                    {
                        ShowWindow(Browser.Process.MainWindowHandle, SW_MINIMIZE);
                        return true;
                    }
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No valid browser process to minimize", this, GPALObjectType.PuppeteerCommunicator);
                    return false;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Failed to minimize window", this, GPALObjectType.PuppeteerCommunicator, ex);
                    return false;
                }
            }
            else if (method == DevToolsMethods.RestoreWindow)
            {
                try
                {
                    if (false == Browser?.Process?.HasExited)
                    {
                        ShowWindow(Browser.Process.MainWindowHandle, SW_RESTORE);
                        return true;
                    }
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No valid browser process to restore", this, GPALObjectType.PuppeteerCommunicator);
                    return false;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Failed to restore window", this, GPALObjectType.PuppeteerCommunicator, ex);
                    return false;
                }
            }
            else if (method == DevToolsMethods.MaximizeWindow)
            {
                try
                {
                    if (false == Browser?.Process?.HasExited)
                    {
                        ShowWindow(Browser.Process.MainWindowHandle, SW_MAXIMIZE);
                        return true;
                    }
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No valid browser process to maximize", this, GPALObjectType.PuppeteerCommunicator);
                    return false;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Failed to maximize window", this, GPALObjectType.PuppeteerCommunicator, ex);
                    return false;
                }
            }
            else if (method == DevToolsMethods.CheckNetworkIdle)
            {
                return CheckNetworkIdle();
            }

            if (true == Browser?.Process?.HasExited)
            {
                string msg = "The browser process has exited, so there is nothing left to send a command to";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);

                if (false == inExit)
                {
                    inExit = true;
                    BrowserHelper.KillAllRunningProcesses(true, Browser);
                }

                // Kill the browser processes, then throw.
                throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
            }
            else if (false == _usePipes && (_webSocket == null || _webSocket.State != WebSocketState.Open))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WebSocket in [{_webSocket?.State}] state, reconnecting", this, GPALObjectType.PuppeteerCommunicator);
                await Reconnect().ConfigureAwait(false);
            }

            dynamic data = null;

            try
            {
                if (false == doNotGetSemaphore)
                    await _sendSemaphore.WaitAsync(_cts.Token).ConfigureAwait(false);

                try
                {
                    long id = Interlocked.Increment(ref _commandId);
                    int retry = 2;
                    int rootNodeId = 1;

                    while (0 < retry)
                    {
                        var tcs = new TaskCompletionSource<dynamic>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _responseTasks[id] = tcs;
                        _commandSent[id] = (method, DateTime.UtcNow);

                        var command = DevToolsCommandBuilder.BuildCommand(method, parameters, id, sessionId, rootNodeId);
                        var bytes = Encoding.UTF8.GetBytes(command);

                        await _writeLock.WaitAsync(_cts.Token).ConfigureAwait(false);
                        try
                        {
                            if (_usePipes)
                            {
                                var stream = Browser.BrowserSettings.InboundPipe;

                                // CDP over a pipe takes each command as JSON ending in a nul byte, with no length
                                await stream.WriteAsync(bytes, 0, bytes.Length, _cts.Token).ConfigureAwait(false);
                                await stream.WriteAsync(new byte[] { 0 }, 0, 1, _cts.Token).ConfigureAwait(false);

                                // Flush to ensure immediate send
                                await stream.FlushAsync(_cts.Token).ConfigureAwait(false);
                            }
                            else
                            {
                                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            _writeLock.Release();
                        }

                        // --- Add timeout handling to prevent hanging ---
                        dynamic message = null;
                        try
                        {
                            // Browser.close often gets no answer: the browser is gone before it replies
                            message = await tcs.Task.TimeoutAfter(TimeSpan.FromSeconds(timeoutSeconds), method.ToString(), DevToolsMethods.BrowserClose == method).ConfigureAwait(false);
                        }
                        catch (TimeoutException)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Command [{method}] timed out, releasing semaphore.", this, GPALObjectType.PuppeteerCommunicator);
                            // Remove TCS to prevent blocking future commands
                            _responseTasks.TryRemove(id, out _);
                            throw; // optionally rethrow or handle gracefully
                        }
                        finally
                        {
                            // Ensure TCS is removed even on success or exception
                            _responseTasks.TryRemove(id, out _);
                            _commandSent.TryRemove(id, out _);
                        }

                        try
                        {
                            if (message is string)
                            {
                                if (true == DeadSessionAnswers.Any(answer => true == $"{message}".StartsWith(answer)))
                                {
                                    if (1 < retry--)
                                    {
                                        ClearCurrentSessionId();   // the stored one is dead, so drop it and let GetCurrentSessionId attach a fresh one
                                        CurrentFrameSessionId = null;
                                        sessionId = GetEffectiveSessionId();
                                        continue;
                                    }
                                }
                                else if (true == "Sink not found".Equals(message))
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "DIAL devices not supported. Use ID from chrome://media-router-internals/", this, GPALObjectType.PuppeteerCommunicator);
                                }

                                bool notRendered = true == NotRenderedAnswers.Any(answer => true == $"{message}".StartsWith(answer));
                                bool gone = true == GoneAnswers.Any(answer => true == $"{message}".StartsWith(answer));
                                string msg = true == notRendered ? "Element is not rendered, so it has no position on the page."
                                    : true == gone ? "Element is no longer on the page; the page it was found on has been replaced."
                                    : $"CDP error : [{message}].";
                                notRendered = true == notRendered || true == gone;
                                if (false == lastErrorMessage.Contains(msg))    // we are not in control of lastmessages.clear, whomever calls us is
                                {
                                    lastErrorMessage.Add(msg);
                                    GPAL.PublishSimpleEvent(true == notRendered ? GPALEventType.INFO : GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);
                                    supressedMessage = false;
                                }
                                else if (false == supressedMessage)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", msg, GPALObjectType.Other);
                                    supressedMessage = true;
                                }

                                throw new ApplicationException(msg);
                            }
                            else if (message?.error != null)
                            {
                                if (true == DeadSessionAnswers.Any(answer => true == $"{message?.error?.message}".StartsWith(answer)))
                                {
                                    if (1 < retry--)
                                    {
                                        ClearCurrentSessionId();   // the stored one is dead, so drop it and let GetCurrentSessionId attach a fresh one
                                        CurrentFrameSessionId = null;
                                        sessionId = GetEffectiveSessionId();
                                        continue;
                                    }
                                }

                                bool notRendered = true == NotRenderedAnswers.Any(answer => true == $"{message.error.message}".StartsWith(answer));
                                bool gone = true == GoneAnswers.Any(answer => true == $"{message.error.message}".StartsWith(answer));
                                string msg = true == notRendered ? "Element is not rendered, so it has no position on the page."
                                    : true == gone ? "Element is no longer on the page; the page it was found on has been replaced."
                                    : $"CDP error : [{message.error.message}].";
                                notRendered = true == notRendered || true == gone;
                                if (false == lastErrorMessage.Contains(msg))    // we are not in control of lastmessages.clear, whomever calls us is
                                {
                                    lastErrorMessage.Add(msg);
                                    GPAL.PublishSimpleEvent(true == notRendered ? GPALEventType.INFO : GPALEventType.ERROR, msg, this, GPALObjectType.PuppeteerCommunicator);
                                    supressedMessage = false;
                                }
                                else if (false == supressedMessage)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", msg, GPALObjectType.Other);
                                    supressedMessage = true;
                                }

                                throw new ApplicationException(msg);
                            }
                        }
                        catch //(Exception ex)
                        {
                            //var noErrorMessage = true;
                        }

                        if (null == resultExtractors)
                        {
                            var protocolJson = GetProtocolJSON();
                            resultExtractors = CdpResultExtractors.Build(protocolJson);
                        }
                        // Handle response data
                        if (resultExtractors != null && resultExtractors.TryGetValue(method, out var extractor))
                        {
                            if (null == message || message is string || message.result == null)
                            {
                                string msg = $"Command [{method}] returned null result.";
                                // an error reply was published above as what went wrong, and Browser.close is not expected to answer
                                bool reported = message is string || null != message?.error || DevToolsMethods.BrowserClose == method;
                                if (false == reported && false == lastErrorMessage.Contains(msg))
                                {
                                    lastErrorMessage.Add(msg);
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, msg, this, GPALObjectType.PuppeteerCommunicator);
                                    supressedMessage = false;
                                }
                                else if (false == reported && false == supressedMessage)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", msg, GPALObjectType.Other);
                                    supressedMessage = true;
                                }
                                return null;
                            }
                            data = extractor((Newtonsoft.Json.Linq.JObject)message?.result);
                        }
                        else
                        {
                            data = message.result?.ToObject<dynamic>() ?? new { };
                        }

                        break;
                        //if (data?.Length > 50_000_000) // Limit to 50MB
                        //{
                        //    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Message data too large", this, GPALObjectType.PuppeteerCommunicator);
                        //    return null;
                        //}
                    }
                }
                finally
                {
                    if (false == doNotGetSemaphore)
                        _sendSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Command [{method}][{parameters}] failed.", null, GPALObjectType.None, ex);
            }

            return data;
        }
        /// <summary>
        /// Registers a JavaScript snippet to be evaluated automatically on every new document load for the given session,
        /// using <c>Page.addScriptToEvaluateOnNewDocument</c>. When Puppeteer is not in use, the equivalent Selenium
        /// Chromium CDP command is issued instead via the underlying <see cref="OpenQA.Selenium.Chromium.ChromiumDriver"/>.
        /// </summary>
        /// <param name="scriptToInject">The JavaScript source code to run on each new document.</param>
        /// <param name="sessionId">The CDP session (or target ID) to inject the script into; if null, the effective session ID is resolved automatically.</param>
        /// <returns>A task that completes with the CDP-assigned script identifier (for later removal via <see cref="RemoveInjectedScript"/>), or throws if the CDP/Selenium command fails.</returns>
        public async Task<string> EnableFetchScriptInjection(string scriptToInject, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string identifier = null;
            try
            {
                if (true == Browser.UseSelenium)
                {
                    // Use Selenium CDP command for non-Puppeteer case
                    identifier = BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(Browser.BrowserSettings, scriptToInject);
                }
                else if (true == Browser.UsePuppeteer)
                {
                    // Chrome runs a registered script only in a session with the Page domain enabled. Enabling it
                    // again is harmless.
                    await SendCommand<object>(DevToolsMethods.PageEnable, new { }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId).ConfigureAwait(false);

                    // Use Puppeteer CDP command directly
                    var result = await SendCommand<object>(DevToolsMethods.PageAddScriptToEvaluateOnNewDocument, new
                    {
                        source = scriptToInject
                    }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId).ConfigureAwait(false);
                    identifier = result?.identifier?.ToString();

                    // Kept so every tab attached later gets it too, see PrepareSession
                    var registered = new RegisteredScript { Source = scriptToInject };
                    registered.Identifiers[sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId] = identifier;
                    lock (_registeredScripts)
                        _registeredScripts.Add(registered);
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Script injected via CDP for sessionId: [{sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to inject script via CDP", this, GPALObjectType.Puppeteer, ex);
                throw;
            }

            return identifier;
        }

        /// <summary>
        /// Removes a script previously registered via <see cref="EnableFetchScriptInjection"/>, using
        /// <c>Page.removeScriptToEvaluateOnNewDocument</c>.
        /// </summary>
        /// <param name="identifier">The CDP script identifier returned by <see cref="EnableFetchScriptInjection"/>.</param>
        /// <param name="sessionId">The CDP session (or target ID) to remove the script from; if null, the effective session ID is resolved automatically.</param>
        public async Task RemoveInjectedScript(string identifier, string sessionId = null)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return;
            }

            sessionId ??= GetEffectiveSessionId();
            try
            {
                if (true == Browser.UseSelenium)
                {
                    BrowserHelper.SeleniumRemoveScriptToEvaluateOnNewDocument(Browser.BrowserSettings, identifier);
                }
                else if (true == Browser.UsePuppeteer)
                {
                    await SendCommand<object>(DevToolsMethods.PageRemoveScriptToEvaluateOnNewDocument, new
                    {
                        identifier
                    }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId).ConfigureAwait(false);
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Injected script [{identifier}] removed via CDP for sessionId: [{sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to remove injected script via CDP", this, GPALObjectType.Puppeteer, ex);
                throw;
            }
        }

        /// <summary>
        /// A script registered to run on every new document, with the identifier Chrome gave it in each tab session.
        /// Chrome runs a registered script only in the session it was registered in, so <see cref="PrepareSession"/>
        /// registers every one of them on each tab GPAL attaches to.
        /// </summary>
        private class RegisteredScript
        {
            public string Source;
            public bool Injected;   // registered by InjectScript, so ClearInjectedScripts removes it
            public Dictionary<string, string> Identifiers = new Dictionary<string, string>();
        }

        private readonly List<RegisteredScript> _registeredScripts = new List<RegisteredScript>();

        /// <summary>
        /// Registers a script via <see cref="EnableFetchScriptInjection"/> and marks it as injected so it can later be
        /// removed by <see cref="ClearInjectedScripts"/>.
        /// </summary>
        public async Task<string> InjectScript(string scriptToInject, string sessionId = null)
        {
            var identifier = await EnableFetchScriptInjection(scriptToInject, sessionId).ConfigureAwait(false);

            lock (_registeredScripts)
            {
                var registered = _registeredScripts.LastOrDefault(r => r.Source == scriptToInject);
                if (null != registered)
                    registered.Injected = true;
            }

            return identifier;
        }

        /// <summary>
        /// Removes every script registered via <see cref="InjectScript"/> from every open tab it was registered on.
        /// </summary>
        public async Task ClearInjectedScripts(string sessionId = null)
        {
            List<RegisteredScript> injected;
            lock (_registeredScripts)
            {
                injected = _registeredScripts.Where(r => true == r.Injected).ToList();
                _registeredScripts.RemoveAll(r => true == r.Injected);
            }

            // Closed tabs took their scripts with them, so only the sessions still open are sent the removal
            var openSessions = new HashSet<string>(_windowSessionsQueue.ToArray()
                .SelectMany(w => w.TabQueue.ToArray())
                .Where(t => null != t.Value)
                .SelectMany(t => new[] { t.Key, t.Value }));

            foreach (var registered in injected)
            {
                foreach (var pair in registered.Identifiers)
                {
                    if (true == openSessions.Contains(pair.Key))
                        await RemoveInjectedScript(pair.Value, pair.Key).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Gets a newly attached tab session ready to run the scripts registered so far: enables the Page domain,
        /// without which Chrome does not run them, and registers each one the session does not have yet.
        /// <br/><br/>
        /// Every command is on the wire before any answer is awaited. Chrome answers none of them on a held tab until
        /// it is let go, so OnAttachedToTarget sends the release right behind them.
        /// </summary>
        /// <param name="sessionId">The CDP session of the newly attached tab.</param>
        /// <param name="doNotGetSendSemaphore">True when called for a held tab, whose opener may hold the semaphore.</param>
        internal async Task PrepareSession(string sessionId, bool doNotGetSendSemaphore = false)
        {
            List<RegisteredScript> missing;
            lock (_registeredScripts)
                missing = _registeredScripts.Where(r => false == r.Identifiers.ContainsKey(sessionId)).ToList();

            if (0 < missing.Count)
            {
                // Each call goes as far as the write lock before it returns, and the lock lets them through in the
                // order they came, so Page.enable goes first and the scripts follow in order.
                Task<dynamic> enabling = SendCommand<object>(DevToolsMethods.PageEnable, new { }, sessionId, 3, doNotGetSendSemaphore);
                List<Task<dynamic>> adding = missing.Select(registered => SendCommand<object>(DevToolsMethods.PageAddScriptToEvaluateOnNewDocument, new
                {
                    source = registered.Source
                }, sessionId, 3, doNotGetSendSemaphore)).ToList();

                await Task.WhenAll(adding.Prepend(enabling)).ConfigureAwait(false);

                for (int i = 0; i < missing.Count; i++)
                {
                    dynamic result = adding[i].Result;

                    lock (_registeredScripts)
                        missing[i].Identifiers[sessionId] = result?.identifier?.ToString();
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Registered [{missing.Count}] scripts on session [{sessionId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
        }

        // The session Chrome auto-attached to each new tab, by target id, and the NewTab, OpenWindow or CreateTarget
        // calls waiting for one
        private readonly ConcurrentDictionary<string, string> _autoSessions = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _autoSessionWaiters = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        /// <summary>
        /// Handles <c>Target.attachedToTarget</c>. Chrome holds a new tab before it runs anything, so the registered
        /// scripts are sent on its session first, then the tab is let go, and then the answers are awaited. It is let
        /// go even when registering fails, or it would stay blank. Bypasses the send semaphore, which the command that opened the tab may be holding.
        /// An attach that is not holding a tab (GPAL's own attaches, and tabs open before auto-attach was turned
        /// on) is left alone.
        /// </summary>
        /// <param name="paramsData">The event's params: sessionId, targetInfo and waitingForDebugger.</param>
        private async Task OnAttachedToTarget(dynamic paramsData)
        {
            string sessionId = paramsData?.sessionId?.ToString();
            string targetId = paramsData?.targetInfo?.targetId?.ToString();
            bool held = "true".Equals(paramsData?.waitingForDebugger?.ToString(), StringComparison.OrdinalIgnoreCase);

            if (true == held && false == string.IsNullOrEmpty(sessionId) && false == string.IsNullOrEmpty(targetId))
            {
                // The scripts go on the wire first and the release right behind them. Chrome answers nothing on a held
                // tab, so awaiting the scripts before the release waited out every command's timeout.
                Task preparing = PrepareSession(sessionId, true);

                try
                {
                    await SendCommand<object>(DevToolsMethods.RuntimeRunIfWaitingForDebugger, new { }, sessionId, 3, true).ConfigureAwait(false);
                }
                finally
                {
                    await preparing.ConfigureAwait(false);
                }

                _autoSessions[targetId] = sessionId;

                // A tab GPAL opened is waited on by its opener. A tab the page opened that is already listed gets
                // its session filled in; one listed later picks it up in AddTabToQueue.
                if (true == _autoSessionWaiters.TryRemove(targetId, out var waiter))
                    waiter.TrySetResult(sessionId);
                else if (-1 != GetCurrentWindowIndex(targetId))
                    await AddTabToQueue(targetId, sessionId, false, true).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits for the session Chrome auto-attached to a tab GPAL just created. <see cref="OnAttachedToTarget"/>
        /// lets the tab go once its scripts are registered, so it has them before its first page.
        /// </summary>
        /// <param name="targetId">The target id Target.createTarget returned.</param>
        /// <returns>The tab's session, or null if Chrome never attached it.</returns>
        private async Task<string> WaitForAutoSession(string targetId)
        {
            var waiter = _autoSessionWaiters.GetOrAdd(targetId, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
            string sessionId = null;

            // The event can arrive before the wait starts
            if (true == _autoSessions.TryGetValue(targetId, out var known))
            {
                _autoSessionWaiters.TryRemove(targetId, out _);
                sessionId = known;
            }
            else if (waiter.Task == await Task.WhenAny(waiter.Task, Task.Delay(DefaultCommandTimeoutSeconds * 1000)).ConfigureAwait(false))
                sessionId = waiter.Task.Result;
            else
                _autoSessionWaiters.TryRemove(targetId, out _);

            return sessionId;
        }
        // using fetchenable request - doesn't work due to nonce issues
        // the concept here is to inject javascript in a page response before the website loads it, but the above seems to work, iunno, i think this was for antibot scripts
        // but we are already stealthy except synthetic events, but then use hardware and chrome mode, not headless
        //public async Task EnableFetchScriptInjection(string scriptToInject, string sessionId = null)
        //{
        //    try
        //    {
        //        // Ensure Fetch domain is enabled with retries
        //        int maxRetries = 3;
        //        for (int attempt = 0; attempt < maxRetries; attempt++)
        //        {
        //            try
        //            {
        //                await SendCommand<object>(DevToolsMethods.FetchEnable, new
        //                {
        //                    patterns = new[]
        //                    {
        //            new { urlPattern = "*", resourceType = "Document", requestStage = "Response" }
        //        }
        //                }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId);
        //                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"FetchEnable successful on attempt {attempt + 1}", this, GPALObjectType.PuppeteerCommunicator);
        //                break;
        //            }
        //            catch (Exception ex)
        //            {
        //                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"FetchEnable failed on attempt {attempt + 1}: {ex.Message}", this, GPALObjectType.PuppeteerCommunicator);
        //                if (attempt == maxRetries - 1) throw;
        //                await Task.Delay(500); // Wait before retrying
        //            }
        //        }

        //        Action<object, dynamic> handler = async (sender, e) =>
        //        {
        //            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Fetch.requestPaused event data: {Newtonsoft.Json.JsonConvert.SerializeObject(e)}", this, GPALObjectType.PuppeteerCommunicator);

        //            var requestIdToken = e["requestId"];
        //            if (requestIdToken == null)
        //            {
        //                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "requestId is null in Fetch.requestPaused event. Full event data logged.", this, GPALObjectType.PuppeteerCommunicator);
        //                // Add more context for debugging
        //                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Event context: sessionId={e.sessionId}, targetId={e.targetInfo?.targetId}, url={e.targetInfo?.url}", this, GPALObjectType.PuppeteerCommunicator);
        //                return; // Exit if requestId is missing
        //            }

        //            var requestId = requestIdToken.ToString();
        //            var responseStatusCode = e["responseStatusCode"]?.ToObject<int>() ?? 200;
        //            var resourceType = e["resourceType"]?.ToString();

        //            if (string.IsNullOrEmpty(resourceType) || (resourceType == "Document" && responseStatusCode >= 200 && responseStatusCode < 300))
        //            {
        //                try
        //                {
        //                    var response = await SendCommand<object>(DevToolsMethods.FetchGetResponseBody, new { requestId }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId);
        //                    var body = response["body"]?.ToString();
        //                    var isBase64 = response["base64Encoded"]?.ToObject<bool>() ?? false;

        //                    if (string.IsNullOrEmpty(body))
        //                    {
        //                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Empty body received for requestId: {requestId}", this, GPALObjectType.PuppeteerCommunicator);
        //                        await SendCommand<object>(DevToolsMethods.FetchContinueRequest, new { requestId }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId);
        //                        return;
        //                    }

        //                    if (isBase64)
        //                    {
        //                        body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
        //                    }

        //                    // Extract the existing nonce from responseHeaders
        //                    string existingNonce = null;
        //                    var responseHeaders = e["responseHeaders"] as IEnumerable<dynamic>;
        //                    string cspHeader = null;
        //                    if (responseHeaders != null)
        //                    {
        //                        cspHeader = responseHeaders
        //                            .Where(h => h["name"].ToString().ToLower() == "content-security-policy")
        //                            .Select(h => h["value"].ToString())
        //                            .FirstOrDefault();
        //                        if (!string.IsNullOrEmpty(cspHeader))
        //                        {
        //                            var nonceMatch = System.Text.RegularExpressions.Regex.Match(cspHeader, @"nonce-([a-zA-Z0-9+/=]{16})");
        //                            if (nonceMatch.Success)
        //                            {
        //                                existingNonce = nonceMatch.Groups[1].Value;
        //                            }
        //                        }
        //                    }

        //                    // Use the existing nonce if found, otherwise generate a new one
        //                    var nonce = !string.IsNullOrEmpty(existingNonce) ? $"nonce-{existingNonce}" : $"nonce-{Guid.NewGuid().ToString("N").Substring(0, 16)}";
        //                    var modifiedBody = body.Replace(
        //                        "<head>",
        //                        $"<head><script {nonce}>{scriptToInject}</script>"
        //                    );

        //                    var responseHeadersList = new List<object>
        //        {
        //            new { name = "Content-Type", value = "text/html" }
        //        };

        //                    if (!string.IsNullOrEmpty(existingNonce) && cspHeader != null)
        //                    {
        //                        responseHeadersList.Add(new { name = "Content-Security-Policy", value = cspHeader });
        //                    }
        //                    else if (!string.IsNullOrEmpty(existingNonce))
        //                    {
        //                        responseHeadersList.Add(new { name = "Content-Security-Policy", value = $"script-src 'nonce-{existingNonce}' 'strict-dynamic' https: http:; object-src 'none';" });
        //                    }
        //                    else
        //                    {
        //                        responseHeadersList.Add(new { name = "Content-Security-Policy", value = $"script-src {nonce} 'strict-dynamic' https: http:; object-src 'none';" });
        //                    }

        //                    await SendCommand<object>(DevToolsMethods.FetchFulfillRequest, new
        //                    {
        //                        requestId,
        //                        responseCode = responseStatusCode,
        //                        responseHeaders = responseHeadersList,
        //                        body = Convert.ToBase64String(Encoding.UTF8.GetBytes(modifiedBody))
        //                    }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId);

        //                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Injected script into HTML response for requestId: {requestId} with nonce: {nonce}", this, GPALObjectType.PuppeteerCommunicator);
        //                }
        //                catch (Exception ex)
        //                {
        //                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to inject script: {ex.Message}", this, GPALObjectType.Puppeteer, ex);
        //                    await SendCommand<object>(DevToolsMethods.FetchContinueRequest, new { requestId }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId);
        //                }
        //            }
        //            else
        //            {
        //                await SendCommand<object>(DevToolsMethods.FetchContinueRequest, new { requestId }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId);
        //            }
        //        };

        //        string eventKey = $"Fetch.requestPaused_{sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId}";
        //        _eventHandlers[eventKey] = handler;

        //        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Subscribed to Fetch.requestPaused for sessionId: {sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId}", this, GPALObjectType.PuppeteerCommunicator);
        //    }
        //    catch (Exception ex)
        //    {
        //        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to enable Fetch script injection: {ex.Message}", this, GPALObjectType.Puppeteer, ex);
        //        throw;
        //    }
        //}
        /// <summary>
        /// Background reader loop for the pipe CDP transport. Continuously reads nul-terminated JSON messages
        /// from the browser's outbound pipe, deserializes each one, and either completes the pending
        /// <see cref="TaskCompletionSource{TResult}"/> for a matching command response (by message <c>id</c>) or
        /// processes CDP events (e.g. <c>Network.*</c> events are queued, and a new "page" target detected via
        /// <c>Target.targetCreated</c> triggers <see cref="HandleNewTarget"/>). Signals <c>_readerReadyTcs</c> once
        /// the reader has started, and exits on cancellation, pipe closure/EOF, or an unrecoverable read error.
        /// </summary>
        /// <param name="ct">Token used to cancel the read loop.</param>
        /// <returns>A task that completes when the read loop exits (normally, on cancellation, or due to an error).</returns>
        internal async Task ReceivePipeMessages(CancellationToken ct)
        {
            // Signal: "I'm alive and reading"
            _readerReadyTcs?.SetResult(true);

            var stream = Browser.BrowserSettings.OutboundPipe; // FileStream (byte-oriented)
            if (stream == null || !stream.IsConnected || !stream.CanRead)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "OutboundPipe null, disconnected, or not readable", this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            var encoding = Encoding.UTF8;

            // Helper to process one complete JSON text message (unchanged)
            void ProcessMessageString(string text)
            {
                try
                {
                    dynamic message = Newtonsoft.Json.JsonConvert.DeserializeObject(text);
                    if (message == null) return;

                    if (message.id != null)
                    {
                        if (_responseTasks.TryRemove((int)message.id, out TaskCompletionSource<dynamic> tcs))
                        {
                            if (message.error != null)
                            {
                                string errorMessage = (message.error.message != null ? message.error.message.ToString() :
                                                       message.error.ToString()) ?? "Unknown CDP error";
                                // DEBUG, as the port reader has it; the command's caller reports a failure that matters
                                if (!errorMessage.Contains("wasn't found"))
                                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, errorMessage, this, GPALObjectType.PuppeteerCommunicator);
                                tcs.TrySetResult(errorMessage);
                            }
                            else
                            {
                                tcs.TrySetResult(message);
                            }
                        }
                    }
                    else if (message.method != null)
                    {
                        string method = (string)message.method;

                        // Page.enable for dialogs also brings every lifecycle and frame event. Skip all but
                        // Page.javascriptDialogOpening; the dynamic reads below are slow.
                        if (true == method.StartsWith("Page.") && "Page.javascriptDialogOpening" != method)
                            return;

                        dynamic paramsData = message.@params;
                        string title = paramsData?.targetInfo?.title;
                        string targetType = paramsData?.targetInfo?.type?.ToString();
                        string sessionId = message.sessionId?.ToString() ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId;
                        string url = paramsData?.targetInfo?.url?.ToString() ?? "unknown";

                        if (method.StartsWith("Network."))
                        {
                            if (method == "Network.requestWillBeSent" ||
                                method == "Network.loadingFinished" ||
                                method == "Network.loadingFailed")
                            {
                                TrackNetworkIdle(method, paramsData, sessionId);
                            }

                            if (true == CapturingCalls && "Network.requestWillBeSent" == method)
                                RecordCall(paramsData);
                            else if (true == CapturingCalls && "Network.responseReceived" == method)
                                RecordStatus(paramsData);
                        }

                        // A dialog blocks the page and the workflow cannot reach it, so the reader answers it here with
                        // DialogsAccepted and DialogText. The page's alert, confirm and prompt are not modified.
                        if ("Page.javascriptDialogOpening" == method && null != Browser.BrowserSettings.DialogsAccepted)
                        {
                            bool accept = true == Browser.BrowserSettings.DialogsAccepted;
                            string typed = Browser.BrowserSettings.DialogText;

                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answering a [{paramsData?.type}] with accept [{accept}] text [{typed}]", this, GPALObjectType.PuppeteerCommunicator);

                            // Not awaited: this reader receives the reply, so awaiting would deadlock. Bypasses the
                            // send semaphore, which the click that opened the dialog is holding.
                            _ = SendCommand<object>(DevToolsMethods.PageHandleJavaScriptDialog,
                                null == typed ? (object)new { accept } : new { accept, promptText = typed },
                                sessionId, 3, true);
                        }

                        // A closed tab leaves this tab list, whoever closed it. Another GPAL browser attached to the same
                        // browser closes tabs this one also listed, and a listed tab that is gone is switched to and fails.
                        if (method == "Target.targetDestroyed")
                        {
                            string closedId = paramsData?.targetId?.ToString();

                            if (false == string.IsNullOrEmpty(closedId) && -1 != GetCurrentWindowIndex(closedId))
                                RemoveTabFromQueue(closedId);

                            if (false == string.IsNullOrEmpty(closedId))
                                _autoSessions.TryRemove(closedId, out _);
                        }

                        // Off the reader, which receives the replies OnAttachedToTarget waits on
                        if (method == "Target.attachedToTarget")
                            _ = Task.Run(() => OnAttachedToTarget(paramsData));

                        if (method == "Target.targetCreated" && _sendSemaphore.CurrentCount == 1)
                        {
                            if (true == targetType?.Equals("page") && false == url?.StartsWith("chrome://") && false == url?.Equals("about:blank"))
                            {
                                string targetId = paramsData?.targetInfo?.targetId?.ToString();
                                string browserContextId = paramsData?.targetInfo?.browserContextId?.ToString();
                                if (!string.IsNullOrEmpty(targetId))
                                {
                                    // TODO: this needs to be pushed onto a queue, then the next time send 
                                    // Fire-and-forget is fine here
                                    _ = HandleNewTarget(targetId, browserContextId, null, true);
                                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Detected new uncontrolled tab: TargetId [{targetId}] in Window [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
                                }
                            }
                        }

                        GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Received (pipe): [{text}]", this, GPALObjectType.PuppeteerCommunicator);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error processing pipe message", this, GPALObjectType.PuppeteerCommunicator, ex);
                }
            }

            // CDP over a pipe ends each message with a nul byte and sends no length. The bytes before a nul are one
            // message.
            var pending = new List<byte>();
            var chunk = new byte[65536];

            // One read at a time. A read still waiting after 5 s is kept and waited on again; starting another would
            // leave the first to take the next data with nothing reading its result.
            Task<int> readTask = null;

            try
            {
                while (_isRunning && !Browser.BrowserSettings.Process.HasExited && !ct.IsCancellationRequested)
                {
                    readTask ??= stream.ReadAsync(chunk, 0, chunk.Length, ct);

                    if (await Task.WhenAny(readTask, Task.Delay(5000, ct)).ConfigureAwait(false) != readTask)
                    {
                        // Quiet with nothing asked is the workflow doing something else. Only a command sent 5 s ago or
                        // more has waited the whole quiet spell; Browser.close is left out, as it often never answers
                        List<string> stalled = _commandSent.Values
                            .Where(command => DevToolsMethods.BrowserClose != command.Method && 5 <= (DateTime.UtcNow - command.Sent).TotalSeconds)
                            .Select(command => command.Method.ToString())
                            .ToList();

                        if (0 < stalled.Count)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{Browser.BrowserSettings.BrowserType}] has sent nothing on the pipe for 5 s with [{string.Join(", ", stalled)}] waiting on it", this, GPALObjectType.PuppeteerCommunicator);
                        continue;
                    }

                    int bytesRead = readTask.Result;
                    readTask = null;

                    if (0 == bytesRead)
                        break;   // EOF

                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (0 != chunk[i])
                        {
                            pending.Add(chunk[i]);
                            continue;
                        }

                        // Skip an empty message.
                        if (0 < pending.Count)
                        {
                            ProcessMessageString(encoding.GetString(pending.ToArray()));
                            pending.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"ReceivePipeMessages failed", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
        }
        /// <summary>
        /// Background reader loop for the WebSocket CDP transport. Continuously receives complete WebSocket messages,
        /// deserializes each as a CDP JSON message, and either completes the pending <see cref="TaskCompletionSource{TResult}"/>
        /// for a matching command response (by message <c>id</c>) or processes CDP events. Tracks <c>Network.*</c> events
        /// (queuing relevant ones and capturing the HTTP status code from <c>Network.responseReceived</c> for "Document"
        /// requests), records <c>Cast.sinksUpdated</c> sink information into <c>CastDevices</c>, and handles newly created
        /// "page" targets via <see cref="HandleNewTarget"/>. Closes and disposes the WebSocket on cancellation or when the
        /// socket reports a closed/aborted state, and logs (without throwing) any errors encountered while receiving.
        /// </summary>
        /// <param name="ct">Token used to cancel the receive loop.</param>
        /// <returns>A task that completes when the receive loop exits (the WebSocket closes, is cancelled, or <c>_isRunning</c> becomes false).</returns>
        private async Task ReceiveMessages(CancellationToken ct)
        {
            var buffer = new byte[102400];

            while (_isRunning && _webSocket.State == WebSocketState.Open)
            {
                // Check if the cancellation token was canceled before proceeding
                // prolly connecting to a new page after closing a tab or window?
                if (ct.IsCancellationRequested)
                {
                    // Handle cancellation gracefully
                    try
                    {
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Cancellation requested", CancellationToken.None).ConfigureAwait(false);
                        _webSocket.Dispose();
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Error during WebSocket cleanup on cancellation", this, GPALObjectType.PuppeteerCommunicator, ex);
                    }
                    break;
                }

                try
                {
                    using var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    try
                    {
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "WebSocket closed", CancellationToken.None).ConfigureAwait(false);
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Graceful cancellation
                    }
                    catch (WebSocketException ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WebSocket error", this, GPALObjectType.PuppeteerCommunicator, ex);
                    }
                    finally
                    {
                        if (_webSocket.State == WebSocketState.Closed || _webSocket.State == WebSocketState.Aborted)
                        {
                            try { await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cleanup", CancellationToken.None).ConfigureAwait(false); } catch { }
                            _webSocket.Dispose();
                            _webSocket = null;
                        }
                    }

                    if (null != ms)
                    {
                        var json = Encoding.UTF8.GetString(ms.ToArray());
                        var message = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(json);

                        if (message?.id != null)
                        {
                            if (_responseTasks.TryRemove((int)message.id, out TaskCompletionSource<dynamic> tcs))
                            {
                                if (message.error != null)
                                {
                                    string errorMessage = (message.error.message != null ? message.error.message.ToString() :
                                                           message.error.ToString()) ?? "Unknown CDP error";
                                    if (false == errorMessage.Contains("wasn't found"))  // trying to remove a token we removed already
                                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, errorMessage, this, GPALObjectType.PuppeteerCommunicator);
                                    tcs.SetResult(errorMessage);
                                }
                                else
                                    tcs.SetResult(message);
                            }
                        }
                        else if (message.method != null)
                        {
                            string method = (string)message.method;

                            // Page.enable for dialogs also brings every lifecycle and frame event. Skip all but
                            // Page.javascriptDialogOpening; the dynamic reads below are slow. Use continue: return
                            // would end the receive loop.
                            if (true == method.StartsWith("Page.") && "Page.javascriptDialogOpening" != method)
                                continue;

                            dynamic paramsData = message.@params;
                            string title = paramsData.targetInfo?.title;
                            string targetType = paramsData.targetInfo?.type?.ToString();
                            string sessionId = message.sessionId?.ToString() ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId;
                            string url = paramsData.targetInfo?.url?.ToString() ?? "unknown";


                            // Filter network events when suppressed
                            if (method.StartsWith("Network."))
                            {
                                if (method == "Network.requestWillBeSent" ||
                                    method == "Network.loadingFinished" ||
                                    method == "Network.loadingFailed")
                                {
                                    TrackNetworkIdle(method, paramsData, sessionId);
                                }

                                if (true == CapturingCalls && "Network.requestWillBeSent" == method)
                                    RecordCall(paramsData);
                                else if (true == CapturingCalls && "Network.responseReceived" == method)
                                    RecordStatus(paramsData);

                                if (method == "Network.responseReceived")
                                {
                                    // paramsData is message.@params
                                    string type = paramsData.type?.ToString();

                                    // We only care about "Document" (the actual HTML page)
                                    if (type == "Document")
                                    {
                                        this.ServerResponseCode = (int)paramsData.response.status;
                                        string responseUrl = paramsData.response.url?.ToString();

                                        Browser.ServerResponseCode = this.ServerResponseCode;
                                        //GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                                        //    $"HTTP Status Captured: [{this.ServerResponseCode}] for [{responseUrl}]",
                                        //    this, GPALObjectType.PuppeteerCommunicator);

                                        // You might want to store this in a field like: this.LastStatusCode = statusCode;
                                    }
                                }
                            }

                            if (method.StartsWith("Cast.")) // prolly just Cast.sinksUpdated
                            {
                                try
                                {
                                    JObject payload = paramsData;
                                    JToken sink = payload["sinks"];
                                    try
                                    {
                                        if (null != sink)
                                            CastDevices.Add(new CastDevice(sink[0]["name"]?.ToString(), sink[0]["id"]?.ToString()));
                                    } catch { } // index issue

                                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                                        $"[{method}] [{Newtonsoft.Json.JsonConvert.SerializeObject(paramsData)}]",
                                        this, GPALObjectType.PuppeteerCommunicator);

                                    // TODO: Store sinks globally so CastTo() can read them later and decipher friendly name
                                    // LastKnownSinks = paramsData.sinks;
                                }
                                catch (Exception ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                                        $"Error processing [{method}]",
                                        this, GPALObjectType.PuppeteerCommunicator, ex);
                                }
                            }

                            // A dialog blocks the page and the workflow cannot reach it, so the reader answers it here
                            // with DialogsAccepted and DialogText. The page's alert, confirm and prompt are not
                            // modified.
                            if ("Page.javascriptDialogOpening" == method && null != Browser.BrowserSettings.DialogsAccepted)
                            {
                                bool accept = true == Browser.BrowserSettings.DialogsAccepted;
                                string typed = Browser.BrowserSettings.DialogText;

                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answering a [{paramsData?.type}] with accept [{accept}] text [{typed}]", this, GPALObjectType.PuppeteerCommunicator);

                                // Not awaited: this reader receives the reply, so awaiting would deadlock. Bypasses
                                // the send semaphore, which the click that opened the dialog is holding.
                                _ = SendCommand<object>(DevToolsMethods.PageHandleJavaScriptDialog,
                                    null == typed ? (object)new { accept } : new { accept, promptText = typed },
                                    sessionId, 3, true);
                            }

                            // A closed tab leaves this tab list, whoever closed it. Another GPAL browser attached to the same
                            // browser closes tabs this one also listed, and a listed tab that is gone is switched to and fails.
                            if (method == "Target.targetDestroyed")
                            {
                                string closedId = paramsData?.targetId?.ToString();

                                if (false == string.IsNullOrEmpty(closedId) && -1 != GetCurrentWindowIndex(closedId))
                                    RemoveTabFromQueue(closedId);

                                if (false == string.IsNullOrEmpty(closedId))
                                    _autoSessions.TryRemove(closedId, out _);
                            }

                            // Off the reader, which receives the replies OnAttachedToTarget waits on
                            if (method == "Target.attachedToTarget")
                                _ = Task.Run(() => OnAttachedToTarget(paramsData));

                            if (method == "Target.targetCreated" && _sendSemaphore.CurrentCount == 1)
                            {
                                if (true == targetType.Equals("page") && false == url?.StartsWith("chrome://") && false == url?.Equals("about:blank"))
                                {
                                    string targetId = paramsData.targetInfo?.targetId?.ToString();
                                    string browserContextId = paramsData.targetInfo?.browserContextId?.ToString();

                                    if (!string.IsNullOrEmpty(targetId))
                                    {
                                        // Handle like NewTab: attach, enable domains, add to queue
                                        // TODO: this needs to be pushed onto a queue, then the next time send 
                                        await HandleNewTarget(targetId, browserContextId, null, true).ConfigureAwait(false);
                                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Detected new uncontrolled tab: TargetId [{targetId}] in Window [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
                                    }
                                }
                            }
                            //else if (method == "Target.targetInfoChanged" && _sendSemaphore.CurrentCount == 1)
                            //{
                            //    string targetId = paramsData.targetInfo?.targetId?.ToString();
                            //    string browserContextId = paramsData.targetInfo?.browserContextId?.ToString();
                            //    targetType = paramsData.targetInfo?.type?.ToString();
                            //    url = paramsData.targetInfo?.url?.ToString() ?? "unknown";

                            //    if (targetType == "page" && targetId != null && true == Browser.Started)
                            //    {
                            //        if (url.StartsWith("chrome://") || url == "about:blank")
                            //        {
                            //            ; GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Skipping tab info change: TargetId [{targetId}], URL [{url}]", this, GPALObjectType.PuppeteerCommunicator);
                            //        }
                            //        else
                            //        {
                            //            // Re-attach to ensure valid sessionId
                            //            var session = await SendCommand<object>(DevToolsMethods.TargetAttachToTarget, new { targetId, flatten = true }, null, 3, true);
                            //            string newSessionId = session?.sessionId?.ToString();
                            //            if (newSessionId != null)
                            //            {
                            //                await HandleNewTarget(targetId, browserContextId, newSessionId);
                            //                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Processed tab info change: TargetId [{targetId}] in Window [{browserContextId}], Session [{newSessionId}]", this, GPALObjectType.PuppeteerCommunicator);
                            //            }
                            //            else
                            //            {
                            //                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to re-attach on Target.targetInfoChanged: TargetId [{targetId}]", this, GPALObjectType.PuppeteerCommunicator);
                            //            }
                            //        }
                            //    }
                            //}      
                            GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Received: [{Newtonsoft.Json.JsonConvert.SerializeObject(message)}]", this, GPALObjectType.PuppeteerCommunicator);
                        }
                    }
                }
                catch (Exception ex)  // or catch (WebSocketException ex) if you want to be more specific
                {
                    string innerMsg = "";

                    // Option 1 – Simple & most common (recommended for logging)
                    if (ex.InnerException != null)
                    {
                        innerMsg = $" Inner: [{ex.InnerException.Message}]";
                        // Optional: also add type if helpful
                        // innerMsg += $" ({ex.InnerException.GetType().Name})";
                    }

                    string exMsg = $"Error receiving WebSocket message: [{ex.Message}][{innerMsg}]";

                    // Optional – even better: walk the full inner chain (very deep nesting is rare in WebSocket scenarios)
                    // string fullInnerChain = GetFullInnerExceptionMessage(ex);

                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEEPDEBUG,
                        exMsg,
                        this,
                        GPALObjectType.Puppeteer,
                        ex);   // - still pass original ex (it already contains .InnerException)

                    // throw new ApplicationException(exMsg, ex);  // - Important: pass original ex as InnerException! // can't do this kills everything
                }
            }
        }

        /// <summary>
        /// Handles a newly observed CDP target (e.g. a tab opened outside of GPAL's control, detected via
        /// <c>Target.targetCreated</c>) by adding it to the appropriate window's tab queue via <see cref="AddTabToQueue"/>.
        /// </summary>
        /// <param name="targetId">The CDP target ID of the newly created target.</param>
        /// <param name="browserContextId">The browser context (window) ID that the target belongs to, used only for logging.</param>
        /// <param name="newSessionId">The CDP session ID already attached to the target, if known; if null, the tab is added as not yet active/attached.</param>
        /// <param name="doNotGetSendSemaphore">If true, skips acquiring the send semaphore when adding the tab to the queue (used when already holding it).</param>
        /// <param name="isActive">Whether to mark the tab active; if null, the tab is active only when <paramref name="newSessionId"/> is not null.</param>
        /// <returns>A task that completes once the new target has been registered in the appropriate tab queue.</returns>
        private async Task HandleNewTarget(string targetId, string browserContextId, string newSessionId = null, bool doNotGetSendSemaphore = false, bool? isActive = null)
        {
            // A tab GPAL opened (NewTab) becomes active; a tab the browser opened does not. Without isActive, a tab
            // with a session id is treated as active.
            bool activeTab = isActive ?? !(null == newSessionId);

            if (null == newSessionId && true == _autoSessions.TryGetValue(targetId, out var autoSessionId))
                newSessionId = autoSessionId;

            // Add to the window's tab queue
            await AddTabToQueue(targetId, newSessionId, activeTab, doNotGetSendSemaphore).ConfigureAwait(false);

            if (null != newSessionId)
                await PrepareSession(newSessionId).ConfigureAwait(false);

            // Set as active tab (mimic NewTab behavior)
            //SetActiveTabIndex(CurrentSessions.Count - 1);

            // A new tab's session starts with Network off. If capture is on, enable it there too.
            if (true == CapturingCalls)
                await CaptureCalls(true, newSessionId).ConfigureAwait(false);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Handled new target: ID [{targetId}] in Window [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
        }

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
                else if (null == box)   // no box model? maybe it got detached? try to find the new element?
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
        /// Navigates to a new target by creating a new tab (or, optionally, a new browser window/context), attaching
        /// a CDP session to it, and registering it as the active tab via <see cref="AddTabToQueue"/>. Honors
        /// <c>robots.txt</c> rules by redirecting to a fallback URL if the requested URL is disallowed and the
        /// browser is configured to obey it.
        /// </summary>
        /// <param name="url">The URL to navigate the new target to (normalized/completed via <see cref="MagicHelper.GetFullUrl"/>).</param>
        /// <param name="newWindow">If true, the new target is created in a brand new browser context (window) rather than the current one.</param>
        /// <param name="sessionId">The CDP session used to issue the creation commands; if null, the effective session ID is resolved automatically.</param>
        /// <returns>A task that resolves to the CDP target ID of the newly created tab.</returns>
        /// <example>
        /// <code>
        /// string targetId = await communicator.CreateTarget("https://example.com", newWindow: true);
        /// </code>
        /// </example>
        public async Task<string> CreateTarget(string url, bool newWindow = false, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            url = MagicHelper.GetFullUrl(url, Browser, out Browser._areRobotsAllowed);

            if (false == Browser.AreRobotsAllowed && true == Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Visiting [{url}] is disallowed by robots.txt and your request to honor it. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerCommunicator);
                url = "https://google.com";
            }

            // The target opens blank and navigates once attached, so the registered scripts run on its first page too
            object parameters = new { url = "about:blank" };
            if (newWindow)
            {
                var context = await SendCommand<object>(DevToolsMethods.TargetCreateBrowserContext, new { }, sessionId).ConfigureAwait(false);
                string browserContextId = context.browserContextId.ToString();
                parameters = new { url = "about:blank", browserContextId };
            }

            var result = await SendCommand<object>(DevToolsMethods.TargetCreateTarget, parameters, null).ConfigureAwait(false); // null for browser-level
            string targetId = result.targetId.ToString();
            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId }, null).ConfigureAwait(false);
            string newSessionId = await WaitForAutoSession(targetId).ConfigureAwait(false);
            await AddTabToQueue(targetId, newSessionId).ConfigureAwait(false); // Add to queue, set active
            await PrepareSession(newSessionId).ConfigureAwait(false);

            await SendCommand<object>(DevToolsMethods.PageNavigate, new { url }, newSessionId).ConfigureAwait(false);

            return targetId;
        }
        /// <summary>
        /// Closes the outbound side of the active WebSocket connection with a normal closure status, swallowing any
        /// exceptions since this is typically called during teardown.
        /// </summary>
        /// <returns>A task that completes once the close attempt has finished (regardless of success).</returns>
        internal async Task CloseOutputWebSocketAsync()
        {
            // Close the WebSocket connection with a normal closure status and an empty description
            try
            {
                if (null != _webSocket)
                    await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client closing", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // we are terminating anyways...
            }
        }
        /// <summary>
        /// Queries the browser via <c>Target.getTargets</c> and returns the target ID of the first available "page" target,
        /// regardless of the currently active session.
        /// </summary>
        /// <param name="sessionId">Unused; reserved for future use.</param>
        /// <returns>A task that resolves to the target ID of the first "page" target found, or null if none exists.</returns>
        internal async Task<string> GetInitialTargetId(string sessionId)
        {
            dynamic targets = await SendCommand<dynamic>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
            var pageTarget = ((IEnumerable<dynamic>)targets.targetInfos).FirstOrDefault(t => (string)t.type == "page");  // Cast to IEnumerable<dynamic> and use typed lambda
            return pageTarget != null ? (string)pageTarget.targetId : null;
        }
        /// <summary>
        /// Counts the "page" targets the browser reports. Can differ from GPAL's tabs: a tab whose first navigation
        /// is a download closes itself without an event.
        /// </summary>
        /// <returns>A task that resolves to the number of page targets open.</returns>
        internal async Task<int> TabCount()
        {
            dynamic targets = await SendCommand<dynamic>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
            return ((IEnumerable<dynamic>)targets.targetInfos).Count(t => (string)t.type == "page");
        }
        /// <summary>
        /// Searches every tracked window and tab for one whose underlying CDP target URL contains <paramref name="url"/>,
        /// querying <c>Target.getTargetInfo</c> for each tab in turn, and returns the browser context ID of the
        /// containing window.
        /// </summary>
        /// <param name="url">The URL (or substring of it) to search for among open tabs; normalized via <see cref="MagicHelper.GetFullUrl"/>.</param>
        /// <param name="sessionId">Unused; reserved for future use.</param>
        /// <returns>A task that resolves to the browser context ID (window ID) of the window containing a matching tab, or null if no match is found.</returns>
        /// <example>
        /// <code>
        /// string windowId = await communicator.GetTargetWindowIdByUrl("https://example.com/dashboard");
        /// </code>
        /// </example>
        public async Task<string> GetTargetWindowIdByUrl(string url, string sessionId = null)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "URL is empty or null in GetTargetWindowIdByUrl", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            url = MagicHelper.GetFullUrl(url, Browser, out bool _); // Normalize URL (e.g., add "https://" if needed)
            var windowArray = _windowSessionsQueue.ToArray();
            foreach (var window in windowArray)
            {
                var tabArray = window.TabQueue.ToArray();
                foreach (var tab in tabArray)
                {
                    var targetInfo = await SendCommand<object>(DevToolsMethods.TargetGetTargetInfo, new { targetId = tab.Key }, null).ConfigureAwait(false);
                    string targetUrl = targetInfo?.targetInfo?.url?.ToString();
                    if (targetUrl != null && true == TabUrlMatches(targetUrl, url))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Found window with browserContextId [{window.BrowserContextId}] for URL [{url}]", this, GPALObjectType.PuppeteerCommunicator);
                        return window.BrowserContextId; // Return the window's BrowserContextId
                    }
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No window found with a tab matching URL [{url}]", this, GPALObjectType.PuppeteerCommunicator);
            return null;
        }
        /// <summary>
        /// Searches for an open tab whose CDP target URL contains <paramref name="url"/>, checking the tabs of the
        /// currently active window first and then all other windows, querying <c>Target.getTargetInfo</c> for each tab
        /// (with both the raw and URL-decoded forms of the URL compared for the active window's tabs).
        /// </summary>
        /// <param name="url">The URL (or substring of it) to search for; normalized via <see cref="MagicHelper.GetFullUrl"/>. If empty/whitespace, the current target ID is returned instead.</param>
        /// <param name="sessionId">The CDP session used to resolve the effective session if needed; if null, the effective session ID is resolved automatically.</param>
        /// <returns>A task that resolves to the CDP target ID of the matching tab, the current target ID if <paramref name="url"/> was empty, or null if no match is found.</returns>
        /// <example>
        /// <code>
        /// string tabId = await communicator.GetTargetTabIdByUrl("https://example.com/page2");
        /// </code>
        /// </example>
        // A tab is the one asked for when its url contains the one asked for, with the scheme, www. and a trailing
        // slash set aside. A site may add a query or a path to the address it was asked for, as YouTube adds
        // ?themeRefresh=1, so an exact match misses the tab.
        static bool TabUrlMatches(string targetUrl, string url)
        {
            string target = UrlKey(Uri.UnescapeDataString(targetUrl));
            string asked = UrlKey(Uri.UnescapeDataString(url)).TrimEnd('/');

            return 0 < asked.Length && true == target.Contains(asked);
        }

        // The url lower-cased, without its scheme and www.
        static string UrlKey(string url)
        {
            string key = url.ToLowerInvariant();
            int scheme = key.IndexOf("://", StringComparison.Ordinal);

            if (0 <= scheme)
                key = key.Substring(scheme + 3);

            if (true == key.StartsWith("www.", StringComparison.Ordinal))
                key = key.Substring(4);

            return key;
        }

        public async Task<string> GetTargetTabIdByUrl(string url, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            if (string.IsNullOrWhiteSpace(url))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "URL is empty or null in GetTargetTabIdByUrl", this, GPALObjectType.PuppeteerCommunicator);
                return GetCurrentTargetId();
            }

            url = MagicHelper.GetFullUrl(url, Browser, out bool _);
            var windowArray = _windowSessionsQueue.ToArray();

            if (_activeWindowIndex >= 0 && _activeWindowIndex < windowArray.Length)
            {
                var tabArray = windowArray[_activeWindowIndex].TabQueue.ToArray();
                foreach (var tab in tabArray)
                {
                    var targetInfo = await SendCommand<object>(DevToolsMethods.TargetGetTargetInfo, new { targetId = tab.Key }, null).ConfigureAwait(false);
                    string targetUrl = targetInfo?.targetInfo?.url?.ToString();
                    if (targetUrl != null)
                    {
                        if (true == TabUrlMatches(targetUrl, url))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                                $"Found tab with targetId [{tab.Key}] for URL [{url}] in active window",
                                this, GPALObjectType.PuppeteerCommunicator);

                            return tab.Key;
                        }
                    }
                }
            }

            for (int i = 0; i < windowArray.Length; i++)
            {
                if (i == _activeWindowIndex) continue;
                var tabArray = windowArray[i].TabQueue.ToArray();
                foreach (var tab in tabArray)
                {
                    var targetInfo = await SendCommand<object>(DevToolsMethods.TargetGetTargetInfo, new { targetId = tab.Key }, null).ConfigureAwait(false);
                    string targetUrl = targetInfo?.targetInfo?.url?.ToString();
                    if (targetUrl != null && true == TabUrlMatches(targetUrl, url))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Found tab with targetId [{tab.Key}] for URL [{url}] in window [{i}]", this, GPALObjectType.PuppeteerCommunicator);
                        return tab.Key;
                    }
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No tab found with URL [{url}]", this, GPALObjectType.PuppeteerCommunicator);

            // What the browser itself has open, against what GPAL tracked: shows whether the tab is gone or GPAL lost it
            dynamic targets = await SendCommand<dynamic>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
            var open = new List<string>();

            if (null != targets?.targetInfos)
                foreach (var target in targets.targetInfos)
                    if ("page" == target?.type?.ToString())
                        open.Add($"[{target.targetId}] [{target.url}]");

            var tracked = windowArray.SelectMany(window => window.TabQueue.ToArray()).Select(tab => $"[{tab.Key}]");

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Browser tabs [{open.Count}]: {string.Join(" ", open)}; GPAL tracks {string.Join(" ", tracked)}", this, GPALObjectType.PuppeteerCommunicator);

            return null;
        }
        /// <summary>
        /// Finds the browser window/target id that comes after the given window id, based on the set of
        /// distinct browser/page targets ordered by target id.
        /// </summary>
        /// <param name="id">The target id of the current window.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the target id of the next window, or null if not found or it is the last window.</returns>
        internal async Task<string> GetNextWindowId(string id, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var targets = await SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
            var windowList = ((IEnumerable<dynamic>)targets.targetInfos)
                .Where(t => t.type == "browser" || t.type == "page")
                .GroupBy(t => t.browserContextId ?? t.targetId)
                .Select(g => g.First())
                .OrderBy(t => t.targetId)
                .ToList();
            for (int i = 0; i < windowList.Count; i++)
            {
                if (windowList[i].targetId.ToString() == id)
                {
                    return i < windowList.Count - 1 ? windowList[i + 1].targetId.ToString() : null;
                }
            }
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No next window found for ID: [{id}]", this, GPALObjectType.PuppeteerCommunicator);
            return null;
        }
        /// <summary>
        /// Finds the browser window/target id that comes before the given window id, based on the set of
        /// distinct browser/page targets ordered by target id.
        /// </summary>
        /// <param name="id">The target id of the current window.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the target id of the previous window, or null if not found or it is the first window.</returns>
        internal async Task<string> GetPreviousWindowId(string id, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var targets = await SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);
            var windowList = ((IEnumerable<dynamic>)targets.targetInfos)
                .Where(t => t.type == "browser" || t.type == "page")
                .GroupBy(t => t.browserContextId ?? t.targetId)
                .Select(g => g.First())
                .OrderBy(t => t.targetId)
                .ToList();
            for (int i = 0; i < windowList.Count; i++)
            {
                if (windowList[i].targetId.ToString() == id)
                {
                    return i > 0 ? windowList[i - 1].targetId.ToString() : null;
                }
            }
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No previous window found for ID: [{id}]", this, GPALObjectType.PuppeteerCommunicator);
            return null;
        }
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

        private readonly SemaphoreSlim _evaluateSelectorSemaphore = new SemaphoreSlim(1, 1);

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

            // drop any session left over from a previously resolved iframe - the attach below is allowed to fail
            // silently for same-origin frames, and without this a stale cross-origin session would be reused
            CurrentFrameSessionId = null;

            // Try to attach to frame target (works for cross-origin OOPIFs)
            try
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
            catch
            {
                // Normal for same-origin iframes - ignore
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
                        // === YOUR ORIGINAL JS EVALUATION PATH (100% unchanged) ===
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

                    // Sequential per-node extraction (your original logic - completely untouched)
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

        // generate selectors from backendnodeid
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
            // Your full JS template as a string
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
            return await GetXPathOrCssForElement(backendNodeId, jsFunc, optimized, sessionId);
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

        /// <summary>
        /// Evaluates an arbitrary JavaScript expression in the current page context and returns its value.
        /// </summary>
        /// <param name="expression">The JavaScript expression to evaluate.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP Runtime.evaluate result, including the returned value.</returns>
        public async Task<dynamic> ExecuteJavaScript(string expression, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new {
                    expression,
                    contextId = CurrentContextId,
                    returnByValue = true,
                    awaitPromise = true
                }
                , sessionId).ConfigureAwait(false);
        }

        internal string LastNavigationError { get; set; }

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

        // The passed session, the effective session and every tab's session in every window, deduplicated.
        private List<string> KnownSessions(string sessionId)
        {
            List<string> retVal = new List<string>();

            foreach (string session in new[] { sessionId, GetEffectiveSessionId() })
                if (false == string.IsNullOrEmpty(session) && false == retVal.Contains(session))
                    retVal.Add(session);

            foreach (WindowSession window in _windowSessionsQueue.ToArray())
                foreach (KeyValuePair<string, string> tab in window.TabQueue.ToArray())
                    if (false == string.IsNullOrEmpty(tab.Value) && false == retVal.Contains(tab.Value))
                        retVal.Add(tab.Value);

            return retVal;
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

        /// <summary>
        /// Starts the browser downloading a url by sending the tab to it, without waiting for a page, since a file
        /// never becomes one. Chrome answers net::ERR_ABORTED when the navigation turns into a download, which here
        /// is the expected answer, not a failure. Whether the file arrived is for the download watcher to say.
        /// </summary>
        /// <param name="url">Absolute http or https url to download</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that completes once the browser has answered the navigation</returns>
        public async Task StartDownload(string url, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var result = await SendCommand<object>(DevToolsMethods.PageNavigate, new { url }, sessionId).ConfigureAwait(false);
            string errorText = (result as Newtonsoft.Json.Linq.JObject)?["errorText"]?.ToString();

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Sent the tab to [{url}] to download it, the browser answered [{(true == string.IsNullOrEmpty(errorText) ? "a page" : errorText)}]", this, GPALObjectType.PuppeteerCommunicator);
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

        // Implementation for "capture-visible-tab"
        /// <summary>
        /// Captures a screenshot of the currently visible tab as a PNG image.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP screenshot result, including the base64-encoded image data.</returns>
        public async Task<dynamic> CaptureVisibleTab(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.PageCaptureScreenshot, new { format = "png" }, sessionId).ConfigureAwait(false);
        }

        // Implementation for CastDesktop
        /// <summary>
        /// Searches for a Cast device with the given sink name and, if found, starts mirroring the entire
        /// desktop to it. Retries discovery up to 10 times, waiting between attempts for the device to appear.
        /// </summary>
        /// <param name="sinkName">The display name of the Cast sink/device to mirror to.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.CastDesktop("Living Room TV", sessionId);
        /// </code>
        /// </example>
        public async Task CastDesktop(string sinkName, string sessionId)
        {
            CastDevice sink = null;
            int retries = 10;
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.CastDisable, null, sessionId).ConfigureAwait(false);

            while (null == sink && 0 < retries--)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{sinkName}].", this, GPALObjectType.PuppeteerCommunicator);
                await SendCommand<object>(DevToolsMethods.CastEnable, new { }, sessionId).ConfigureAwait(false);

                await Task.Delay(3_000).ConfigureAwait(false);

                sink = CastDevices.Find(device => device.name == sinkName);
            }

            await Task.Delay(3_000).ConfigureAwait(false);

            if (null != sink)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{sinkName}] found.", this, GPALObjectType.PuppeteerCommunicator);
                SinkName = sinkName;
                object param = new { sinkName = sink.name };

                // cast or javascript, not both
                await SendCommand<object>(DevToolsMethods.CastStartDesktopMirroring, param, sessionId).ConfigureAwait(false);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{sinkName}] NOT found.", this, GPALObjectType.PuppeteerCommunicator);
        }

        // Implementation for CastTab
        /// <summary>
        /// Searches for a Cast device with the given sink name and, if found, starts mirroring the current
        /// tab to it. Retries discovery up to 10 times, waiting between attempts for the device to appear.
        /// </summary>
        /// <param name="sinkName">The display name of the Cast sink/device to mirror to.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.CastTab("Living Room TV", sessionId);
        /// </code>
        /// </example>
        public async Task CastTab(string sinkName, string sessionId)
        {
            CastDevice sink = null;
            int retries = 10;
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.CastDisable, null, sessionId).ConfigureAwait(false);

            while (null == sink && 0 < retries--)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{sinkName}].", this, GPALObjectType.PuppeteerCommunicator);
                await SendCommand<object>(DevToolsMethods.CastEnable, new { }, sessionId).ConfigureAwait(false);

                await Task.Delay(3_000).ConfigureAwait(false);

                sink = CastDevices.Find(device => device.name == sinkName);
            }

            await Task.Delay(3_000).ConfigureAwait(false);

            if (null != sink)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{sinkName}] found.", this, GPALObjectType.PuppeteerCommunicator);
                SinkName = sinkName;
                object param = new { sinkName = sink.name };

                // cast or javascript, not both
                await SendCommand<object>(DevToolsMethods.CastStartTabMirroring, param, sessionId).ConfigureAwait(false);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{sinkName}] NOT found.", this, GPALObjectType.PuppeteerCommunicator);
        }

        // Implementation for "stop-casting"
        /// <summary>
        /// Attempts to stop an active Cast session for the current sink. Note: there may be no reliable
        /// programmatic way to cleanly stop casting other than closing the tab or window.
        /// </summary>
        /// <param name="sessionId">CDP session id to send the stop-casting command on.</param>
        /// <returns>Task that resolves to true once the stop attempt has completed (regardless of whether casting actually stopped).</returns>
        /// <example>
        /// <code>
        /// await communicator.StopCasting(sessionId);
        /// </code>
        /// </example>
        public async Task<bool> StopCasting(string sessionId)
        {
            // NOTE: there may be no programmatic way to cleanly stop casting except to close the tab or window. This doesn't seem to work.
            try
            {
                await SendCommand<object>(DevToolsMethods.CastStopCasting, new { sinkName = SinkName }, sessionId).ConfigureAwait(false);
            }
            catch { }

            Thread.Sleep(500);

            return true;
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
                supressedMessage = false;
                lastSessionToken = sessionToken;
            }

            currentErrorMessage = $"Waiting for network to idle to [{maxConnections}] connections for [{pruneMs}] ms";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, this, GPALObjectType.PuppeteerCommunicator);
                supressedMessage = false;
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
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
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
        /// Says when no window is left. Chrome ends with its last window; Edge and a headless browser keep running
        /// until Browser.close.
        /// </summary>
        /// <param name="reason">The operation that closed the window, named in the event.</param>
        private void CheckForLastBrowserWindowOpen(string reason)
        {
            if (_windowSessionsQueue.IsEmpty)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{reason}] closed the last window", this, GPALObjectType.PuppeteerCommunicator);
        }

        /// <summary>
        /// True while commands can reach the browser: a pipe, or a websocket that is open. A browser that ended with
        /// its last window leaves the websocket aborted.
        /// </summary>
        internal bool ConnectionOpen { get => true == _usePipes || WebSocketState.Open == _webSocket?.State; }
        /// <summary>
        /// Closes a browser tab identified by tab id, URL, or (if neither is given) the current tab.
        /// </summary>
        /// <param name="url">Optional URL used to look up the target tab id if <paramref name="tabId"/> is not provided.</param>
        /// <param name="tabId">Optional target id of the tab to close.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the tab (and possibly its now-empty window) was closed successfully.</returns>
        /// <example>
        /// <code>
        /// await communicator.CloseTab(url: "https://example.com");
        /// </code>
        /// </example>
        public async Task<bool> CloseTab(string url = null, string tabId = null, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string targetId = tabId ?? (url != null ? await GetTargetTabIdByUrl(url).ConfigureAwait(false) : GetCurrentTargetId());
            return await CloseTab(targetId, sessionId).ConfigureAwait(false);
        }
        /// <summary>
        /// Closes the browser tab with the given target id, removes it from the internal tab queue, and
        /// closes its parent window if that was the last remaining tab.
        /// </summary>
        /// <param name="targetId">The CDP target id of the tab to close.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the tab was closed (and the parent window closed, if it became empty), otherwise false.</returns>
        public async Task<bool> CloseTab(string targetId, string sessionId = null)
        {
            bool retVal = false;
            if (string.IsNullOrEmpty(targetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No targetId provided for CloseTab", this, GPALObjectType.PuppeteerCommunicator);
            }
            else
            {

                await SendCommand<object>(DevToolsMethods.TargetCloseTarget, new { targetId }, null).ConfigureAwait(false);

                // Target.targetDestroyed may have taken the tab off the list already
                if (-1 != GetCurrentWindowIndex(targetId) && false == RemoveTabFromQueue(targetId))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to remove tab [{targetId}] from queue", this, GPALObjectType.PuppeteerCommunicator);
                }

                var windowArray = _windowSessionsQueue.ToArray();
                if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No active window after closing tab [{targetId}]", this, GPALObjectType.PuppeteerCommunicator);
                    retVal = true;
                }
                else
                {
                    var tabQueue = windowArray[_activeWindowIndex].TabQueue;
                    if (tabQueue != null && tabQueue.IsEmpty)
                    {
                        string browserContextId = windowArray[_activeWindowIndex].BrowserContextId;
                        await CloseWindow(browserContextId, sessionId).ConfigureAwait(false);
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Closed window for targetId [{targetId}] (context [{browserContextId}]) as it has no tabs", this, GPALObjectType.PuppeteerCommunicator);
                        retVal = true;
                    }
                }
            }
            CheckForLastBrowserWindowOpen("CloseTab()");
            return retVal;
        }
        /// <summary>
        /// Closes all tabs belonging to the browser window with the given browser context id, then removes
        /// the window from the internal window queue.
        /// </summary>
        /// <param name="browserContextId">The browser context id identifying the window to close.</param>
        /// <param name="sessionId">CDP session id; currently unused but reserved for future use.</param>
        /// <returns>Task that resolves to true if the window and all its tabs were closed successfully, otherwise false.</returns>
        /// <example>
        /// <code>
        /// await communicator.CloseWindow(browserContextId);
        /// </code>
        /// </example>
        public async Task<bool> CloseWindow(string browserContextId, string sessionId = null)
        {
            bool retVal = false;
            try
            {
                var windowArray = _windowSessionsQueue.ToArray();
                var window = windowArray.FirstOrDefault(w => w.BrowserContextId == browserContextId);
                if (window == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Window with browserContextId [{browserContextId}] not found", this, GPALObjectType.PuppeteerCommunicator);
                }
                else
                {
                    // iterate thru all open tabs, close them which will close the window, also cleanup our internal housekeeping
                    var tabArray = window.TabQueue.ToArray();
                    foreach (var tab in tabArray)
                    {
                        await SendCommand<object>(DevToolsMethods.TargetCloseTarget, new { targetId = tab.Key }, null).ConfigureAwait(false);
                        RemoveTabFromQueue(tab.Key);
                    }

                    if (RemoveWindowFromQueue(browserContextId))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Closed window with browserContextId [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
                        retVal = true;
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to remove window with browserContextId [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to close window with browserContextId [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator, ex);
            }

            CheckForLastBrowserWindowOpen("CloseWindow()");
            return retVal;
        }

        // Implementation for "delete-storage"
        /// <summary>
        /// written by chatgpt
        /// Deletes browser storage items according to hierarchical wildcard rules.
        /// Returns number of items deleted.
        /// </summary>
        /// <remarks>
        /// A null domain means the current page's origin, not every domain.
        /// </remarks>
        /// <param name="storageType">Storage type</param>
        /// <param name="sessionId">CDP session id</param>
        /// <param name="domain">Domain/origin; null acts as wildcard</param>
        /// <param name="storeName">Optional object store (IndexedDB)</param>
        /// <param name="path">Optional path (cookies)</param>
        /// <param name="key">Optional key; null acts as wildcard</param>
        /// <returns>Number of items successfully deleted</returns>
        public async Task<bool> DeleteStorage(
            string storageType,
            string sessionId,
            string domain = null,
            string storeName = null,
            string path = null,
            string key = null)
        {
            sessionId ??= GetEffectiveSessionId();
            int deletedCount = 0;
            bool retVal = false;

            bool IsMissing(string s) => string.IsNullOrEmpty(s);

            try
            {
                string origin = false == string.IsNullOrEmpty(domain) ? UrlHelper.GetOrigin(domain) : UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(Browser.BrowserSettings));

                if (IsMissing(domain))
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"DOMAIN IS MISSING. Deleting all [{storageType}] for origin [{origin}].", this, GPALObjectType.PuppeteerCommunicator);

                switch (storageType)
                {
                    case "cookie":
                        {
                            var cookiesJson = await GetStorage("cookie", sessionId, domain, storeName, path, key).ConfigureAwait(false);
                            var cookies = JsonConvert.DeserializeObject<List<dynamic>>(cookiesJson) ?? new List<dynamic>();
                            foreach (dynamic c in cookies)
                            {
                                var delParams = new Dictionary<string, object>
                                {
                                    { "name", (string)c.Key },
                                    { "domain", (string)c.Domain },
                                    //{ "url", $"https://{(string)c.Domain.ToString().TrimStart('.')}{(string)c.Path}" },
                                    { "path", (string)c.Path },
                                };

                                // Copy the cookie's partitionKey only when it is a JSON object. An unpartitioned
                                // cookie has a JSON null token, which is not a C# null.
                                JToken cookiePartition = ((JObject)c)["PartitionKey"];

                                if (null != cookiePartition && JTokenType.Object == cookiePartition.Type)
                                {
                                    var pk = new Dictionary<string, object>
                                    {
                                        { "topLevelSite", (string)cookiePartition["topLevelSite"] },
                                        { "hasCrossSiteAncestor", (bool?)cookiePartition["hasCrossSiteAncestor"] }
                                    };

                                    delParams.Add("partitionKey", pk);
                                }
                                // Network.deleteCookies returns a reply object, not a bool. Non-null means success.
                                dynamic deleteReply = await SendCommand(DevToolsMethods.NetworkDeleteCookies, delParams, sessionId).ConfigureAwait(false);

                                retVal = null != deleteReply;

                                if (true == retVal)
                                    deletedCount++;
                            }
                            break;
                        }
                    case "localStorage":
                    case "sessionStorage":
                        {
                            if (IsMissing(domain))
                            {
                                string js = $@"(function() {{
                                            try {{
                                                {storageType}.clear();
                                                return true;
                                            }} catch (e) {{
                                                return false;
                                            }}
                                        }})();";

                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                {
                                    expression = js,
                                    awaitPromise = false,
                                    contextId = CurrentContextId,
                                    returnByValue = true
                                }, sessionId).ConfigureAwait(false).GetAwaiter().GetResult()?.ToString().Contains("true");

                                if (true == retVal)
                                    deletedCount++;

                                break;
                            }

                            if (IsMissing(key))
                            {
                                var keysJson = await GetStorage(storageType, sessionId, domain).ConfigureAwait(false);

                                // GetStorage returns a key/value object, not a list of names. Take its keys.
                                List<string> keys = new List<string>();

                                if (false == string.IsNullOrWhiteSpace(keysJson))
                                {
                                    JToken storeRead = JToken.Parse(keysJson);

                                    if (JTokenType.Object == storeRead.Type)
                                        keys = ((JObject)storeRead).Properties().Select(each => each.Name).ToList();
                                    else if (JTokenType.Array == storeRead.Type)
                                        keys = storeRead.Select(each => each.ToString()).ToList();
                                }
                                foreach (var k in keys)
                                {
                                    retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                    {
                                        expression = $"{storageType}.removeItem('{EscapeJsString(k)}');",
                                        awaitPromise = false,
                                        contextId = CurrentContextId,
                                        returnByValue = true
                                    }, sessionId).ConfigureAwait(false).GetAwaiter().GetResult()?.ToString().Contains("true");
                                    if (true == retVal)
                                        deletedCount++;
                                }
                            }
                            else
                            {
                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                {
                                    expression = $"{storageType}.removeItem('{EscapeJsString(key)}');",
                                    awaitPromise = false,
                                    contextId = CurrentContextId,
                                    returnByValue = true
                                }, sessionId).ConfigureAwait(false).GetAwaiter().GetResult()?.ToString().Contains("true");
                                if (true == retVal)
                                    deletedCount++;
                            }
                            break;
                        }
                    case "indexedDb":
                        {
                            if (IsMissing(domain))
                            {
                                retVal = SendCommand<dynamic>(DevToolsMethods.StorageClearDataForOrigin, new
                                {
                                    origin = origin,
                                    storageTypes = "indexeddb"
                                }, sessionId).GetAwaiter().GetResult() != null;

                                if (retVal)
                                    deletedCount++;

                                break;
                            }

                            if (IsMissing(storeName))
                            {
                                // enumerate all DBs and delete
                                var dbsJson = await GetStorage("indexeddb", sessionId, domain).ConfigureAwait(false);
                                var dbs = JsonConvert.DeserializeObject<List<string>>(dbsJson) ?? new List<string>();
                                foreach (var db in dbs)
                                {
                                    string js = $@"(function(){{ indexedDB.deleteDatabase('{EscapeJsString(db)}'); }})();";
                                    retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                        new { 
                                            expression = js, 
                                            awaitPromise = true,
                                            contextId = CurrentContextId,
                                            returnByValue = true 
                                        }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                    if (true == retVal)
                                        deletedCount++;
                                }
                            }
                            else if (IsMissing(key))
                            {
                                string js = $@"(function(){{ indexedDB.deleteDatabase('{EscapeJsString(storeName)}'); }})();";
                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                    new { 
                                        expression = js, 
                                        awaitPromise = true,
                                        contextId = CurrentContextId,
                                        returnByValue = true 
                                    }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                if (true == retVal)
                                    deletedCount++;
                            }
                            else
                            {
                                string js = $@"(function() {{
                                                return new Promise((resolve, reject) => {{
                                                    const dbName     = '{EscapeJsString(path)}';      // path = dbName (per your note)
                                                    const store      = '{EscapeJsString(storeName)}'; // storeName = store (per your note)
                                                    const recordKey  = '{EscapeJsString(key)}';

                                                    const openReq = indexedDB.open(dbName);

                                                    openReq.onerror = () => reject(openReq.error);

                                                    openReq.onsuccess = () => {{
                                                        const db = openReq.result;

                                                        const tx = db.transaction(store, 'readwrite');
                                                        const objectStore = tx.objectStore(store);

                                                        objectStore.delete(recordKey);

                                                        tx.oncomplete = () => {{
                                                            db.close();
                                                            resolve(true);   // so your .Contains(""true"") check still works
                                                        }};

                                                        tx.onerror = () => {{
                                                            db.close();
                                                            reject(tx.error);
                                                        }};
                                                    }};
                                                }});
                                            }})();
                                        ";

                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate,
                                    new
                                    {
                                        expression = js,
                                        awaitPromise = true,
                                        contextId = CurrentContextId,
                                        returnByValue = true
                                    }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");

                                if (retVal)
                                    deletedCount++;
                            }
                            break;
                        }

                    case "cache":
                        {
                            if (IsMissing(domain))
                            {
                                string js = @"(async function() {
                                                try {
                                                    const keys = await caches.keys();
                                                    await Promise.all(keys.map(k => caches.delete(k)));
                                                    return true;
                                                } catch (e) {
                                                    return false;
                                                }
                                            })();";

                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                                {
                                    expression = js,
                                    awaitPromise = true,
                                    contextId = CurrentContextId,
                                    returnByValue = true
                                }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");

                                if (true == retVal)
                                    deletedCount++;

                                break;
                            }

                            if (IsMissing(key))
                            {
                                // storeName is the cache name. Without it, GetStorage returns cache names and whole
                                // caches are deleted; with it, the keys in that cache are deleted one by one.
                                var cachesJson = await GetStorage("cache", sessionId, domain, storeName).ConfigureAwait(false);
                                var cacheEntries = JsonConvert.DeserializeObject<List<string>>(cachesJson) ?? new List<string>();

                                foreach (var each in cacheEntries)
                                {
                                    string cacheDelete = IsMissing(storeName)
                                        ? $"caches.delete('{EscapeJsString(each)}');"
                                        : $"caches.open('{EscapeJsString(storeName)}').then(cache => cache.delete('{EscapeJsString(each)}',{{ignoreVary: true}}));";

                                    retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                        new { 
                                            expression = cacheDelete, 
                                            awaitPromise = true,
                                            contextId = CurrentContextId,
                                            returnByValue = true 
                                        }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                    if (true == retVal)
                                        deletedCount++;
                                }
                            }
                            else
                            {
                                retVal = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                                    new { 
                                        expression = $"caches.open('{EscapeJsString(storeName)}').then(cache => cache.delete('{EscapeJsString(key)}', {{ignoreVary: true}}));", 
                                        awaitPromise = true,
                                        contextId = CurrentContextId,
                                        returnByValue = true 
                                    }, sessionId).GetAwaiter().GetResult()?.ToString().Contains("true");
                                if (true == retVal)
                                    deletedCount++;
                            }
                            break;
                        }

                    default:
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Unsupported storage type: [{storageType}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"Failed for [{storageType}] d[{domain}] sn[{storeName}] p[{path}] k[{key}]",
                    this,
                    GPALObjectType.PuppeteerCommunicator,
                    ex);
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Deleted [{deletedCount}] [{storageType}](s)", this, GPALObjectType.PuppeteerCommunicator);

            return 0 < deletedCount;
        }
        // Small helper to prevent JS injection / quoting issues
        /// <summary>
        /// Escapes a string for safe inclusion inside a single-quoted JavaScript string literal, escaping
        /// backslashes, single quotes, carriage returns and newlines.
        /// </summary>
        /// <param name="value">The raw string value to escape.</param>
        /// <returns>The escaped string, or an empty string if <paramref name="value"/> is null or empty.</returns>
        private static string EscapeJsString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n");
        }

        // Implementation for "evaluate"
        /// <summary>
        /// Evaluates an XPath expression against the document and returns the first matching node.
        /// </summary>
        /// <param name="xpath">The XPath expression to evaluate.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP Runtime.evaluate result for the first matching node, or null if none match.</returns>
        /// <example>
        /// <code>
        /// await communicator.Evaluate("//button[@id='submit']");
        /// </code>
        /// </example>
        public async Task<dynamic> Evaluate(string xpath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new {
                    contextId = CurrentContextId,
                    expression = $"document.evaluate('{xpath}', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue"
                }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "evaluate-all"
        /// <summary>
        /// Evaluates an XPath expression against the document and returns all matching nodes as an array.
        /// </summary>
        /// <param name="xpath">The XPath expression to evaluate.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP Runtime.evaluate result containing an array of all matching nodes.</returns>
        /// <example>
        /// <code>
        /// await communicator.EvaluateAll("//a[@class='item']");
        /// </code>
        /// </example>
        public async Task<dynamic> EvaluateAll(string xpath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = $"Array.from(document.evaluate('{xpath}', document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null))" 
                }, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Maps a character to the corresponding CDP/DOM "code" value (e.g. <c>KeyA</c>, <c>Digit1</c>,
        /// <c>Comma</c>) used for synthesizing keyboard events.
        /// </summary>
        /// <param name="c">The character to map to a key code.</param>
        /// <returns>The CDP key code string for the character, or the character itself if no specific mapping exists.</returns>
        private string GetKeyCode(char c)
        {
            char upper = char.ToUpperInvariant(c);

            if (upper >= 'A' && upper <= 'Z')
            {
                return "Key" + upper;
            }

            if (char.IsDigit(c))
            {
                return "Digit" + c;
            }

            switch (c)
            {
                case '`':
                case '~':
                    return "Backquote";

                case '-':
                case '_':
                    return "Minus";

                case '=':
                case '+':
                    return "Equal";

                case '[':
                case '{':
                    return "BracketLeft";

                case ']':
                case '}':
                    return "BracketRight";

                case '\\':
                case '|':
                    return "Backslash";

                case ';':
                case ':':
                    return "Semicolon";

                case '\'':
                case '"':
                    return "Quote";

                case ',':
                case '<':
                    return "Comma";

                case '.':
                case '>':
                    return "Period";

                case '/':
                case '?':
                    return "Slash";

                case ' ':
                    return "Space";

                case '\t':
                    return "Tab";

                case '\r':
                case '\n':
                    return "Enter";

                // Add more special keys here as needed, e.g.:
                // case '\b': return "Backspace";
                // case '\x1B': return "Escape";

                default:
                    return c.ToString();  // fallback
            }
        }

        // Implementation for "fillin" helper
        /// <summary>
        /// Fills a form field with the given text, trying multiple strategies in order of speed/reliability:
        /// bulk text insertion, direct JS value assignment with synthetic events, and finally per-character key events.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text to type into the element.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to true if the text was successfully filled in, otherwise false.</returns>
        /// <example>
        /// <code>
        /// await communicator.FillIn(inputObjectId, "hello@example.com");
        /// </code>
        /// </example>
        public async Task<dynamic> FillIn(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Input.insertText and key events go to whatever has focus. An element that cannot take focus, such as one
            // that is not rendered, would send its text into the element that still has it, so for that element only
            // the direct fill is tried
            bool focused = await IsElementFocused(objectId, sessionId).ConfigureAwait(false);

            // Input.insertText (faster bulk insert)
            bool success = true == focused && await TryInsertText(objectId, text, sessionId).ConfigureAwait(false);

            // JS direct set + synthetic events
            if (false == success)
                success = await TryJsDirectFill(objectId, text, sessionId).ConfigureAwait(false);

            // Per-char key events (most reliable for validation), once the element has focus
            if (false == success)
            {
                await FocusElement(objectId, sessionId).ConfigureAwait(false);

                if (true == await IsElementFocused(objectId, sessionId).ConfigureAwait(false))
                    success = await TryTypeWithKeyEvents(objectId, text, delayMs, sessionId).ConfigureAwait(false);
            }

            if (false == success)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to fill element [{objectId}] with [{text}] using all methods.");

            return success;
        }

        /// <summary>
        /// Attempts to fill an element by dispatching synthetic keyDown/keyUp events for each character,
        /// then verifies the resulting value and blurs the element on success.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="text">The text to type, character by character.</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. If 0, a small fixed default is used since this path is inherently per-character.</param>
        /// <param name="sessionId">CDP session id to dispatch the key events on.</param>
        /// <returns>Task that resolves to true if the element's value matches the typed text afterwards, otherwise false.</returns>
        private async Task<bool> TryTypeWithKeyEvents(string objectId, string text, int delayMs, string sessionId)
        {
            try
            {
                await DispatchTextCharByChar(text, 0 < delayMs ? delayMs : 50, sessionId).ConfigureAwait(false);

                // After typing, verify
                string actualValue = await GetElementValue(objectId, sessionId).ConfigureAwait(false);
                if (actualValue == text)
                {
                    // Final blur to trigger any validation/button enable
                    await BlurElement(objectId, sessionId).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to type text, trying next method.", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
            return false;
        }

        /// <summary>
        /// Attempts to fill an element using a single Input.insertText call (fast bulk insert),
        /// then verifies the resulting value and blurs the element on success.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="text">The text to insert.</param>
        /// <param name="sessionId">CDP session id to dispatch the insert on.</param>
        /// <returns>Task that resolves to true if the element's value matches the inserted text afterwards, otherwise false.</returns>
        private async Task<bool> TryInsertText(string objectId, string text, string sessionId)
        {
            try
            {
                // Bulk insert
                await SendCommand(DevToolsMethods.InputInsertText, new { text }, sessionId).ConfigureAwait(false);

                // Verify
                string actualValue = await GetElementValue(objectId, sessionId).ConfigureAwait(false);
                if (actualValue == text)
                {
                    // Input.insertText fires input but no key events, so a page listening for keyup (to enable a
                    // button, say) never hears the typing. Release the last character's key, as a person's last
                    // keystroke would; a keyUp alone inserts nothing, so the value stays as it is.
                    if (0 < text.Length)
                    {
                        char last = text[text.Length - 1];
                        int vkCode = char.IsLetterOrDigit(last) ? (int)char.ToUpperInvariant(last) : (int)last;

                        await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new
                        {
                            type = "keyUp",
                            key = last.ToString(),
                            windowsVirtualKeyCode = vkCode,
                            nativeVirtualKeyCode = vkCode
                        }, sessionId).ConfigureAwait(false);
                    }

                    await BlurElement(objectId, sessionId).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to type text, trying next method.", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
            return false;
        }

        /// <summary>
        /// Attempts to fill an element by directly setting its "value" property via JavaScript
        /// (bypassing framework value setters where possible) and dispatching "input"/"change" events,
        /// then verifies the resulting value and blurs the element on success.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="text">The text to assign to the element's value.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        /// <returns>Task that resolves to true if the element's value matches the assigned text afterwards, otherwise false.</returns>
        private async Task<bool> TryJsDirectFill(string objectId, string text, string sessionId)
        {
            try
            {
                // Your original/enhanced JS function (set .value + dispatch events)
                var js = @"
                        function(value) {
                            this.value = value;

                            // Trigger everything frameworks listen to
                            const desc = Object.getOwnPropertyDescriptor(this, 'value') || 
                                          Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value') ||
                                          Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');

                            // Bypass any framework value setters that block direct assignment
                            if (desc && desc.set) {
                                desc.set.call(this, value);
                            }

                            this.dispatchEvent(new Event('input', { bubbles: true }));
                            this.dispatchEvent(new Event('change', { bubbles: true }));
                        }";

                await SendCommand(DevToolsMethods.RuntimeCallFunctionOn, new
                {
                    objectId,
                    functionDeclaration = js,
                    arguments = new[] { new { value = text } },
                    contextId = CurrentContextId,
                    awaitPromise = true
                }, sessionId).ConfigureAwait(false);

                // Verify
                string actualValue = await GetElementValue(objectId, sessionId).ConfigureAwait(false);
                if (actualValue == text)
                {
                    await BlurElement(objectId, sessionId).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to type text, trying next method.", this, GPALObjectType.PuppeteerCommunicator, ex);
            }
            return false;
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

        // Helper: Focus
        /// <summary>
        /// Helper that calls <c>focus()</c> on the target element via JavaScript.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        internal async Task FocusElement(string objectId, string sessionId)
        {
            await SendCommand(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                contextId = CurrentContextId,
                functionDeclaration = "function() { this.focus(); }"
            }, sessionId).ConfigureAwait(false);
        }

        // Helper: Blur
        /// <summary>
        /// Helper that calls <c>blur()</c> on the target element via JavaScript.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id to evaluate the JavaScript on.</param>
        private async Task BlurElement(string objectId, string sessionId)
        {
            await SendCommand(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                contextId = CurrentContextId,
                functionDeclaration = "function() { this.blur(); }"
            }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fill-in-append"
        /// <summary>
        /// Appends text to the end of an element's current value, then fills it in using <see cref="FillIn"/>.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text to append after the element's existing value.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.FillInAppend(inputObjectId, "-suffix");
        /// </code>
        /// </example>
        public async Task FillInAppend(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var currentResp = await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                functionDeclaration = "function() { return this.value || ''; }",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string current = currentResp?.result?.value?.ToString() ?? "";
            await FillIn(objectId, current + text, delayMs, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fill-in-insert"
        /// <summary>
        /// Inserts text before an element's current value (prepending it), then fills it in using <see cref="FillIn"/>.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text to insert before the element's existing value.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.FillInInsert(inputObjectId, "prefix-");
        /// </code>
        /// </example>
        public async Task FillInInsert(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var currentResp = await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                functionDeclaration = "function() { return this.value || ''; }",
                contextId = CurrentContextId,
                returnByValue = true
            }, sessionId).ConfigureAwait(false);

            string current = currentResp?.result?.value?.ToString() ?? "";
            await FillIn(objectId, text + current, delayMs, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fill-in-overwrite"
        /// <summary>
        /// Replaces an element's current value entirely with the given text using <see cref="FillIn"/>.
        /// </summary>
        /// <param name="objectId">Remote object id of the input/textarea element.</param>
        /// <param name="text">The text that will become the element's new value.</param>
        /// <param name="delayMs">Delay in milliseconds between characters when the per-character key event path is used; 0 uses a 50 ms default.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.FillInOverwrite(inputObjectId, "new value");
        /// </code>
        /// </example>
        public async Task FillInOverwrite(string objectId, string text, int delayMs = 0, string sessionId = null)
        {
            await FillIn(objectId, text, delayMs, sessionId).ConfigureAwait(false);
        }

        // Implementation for "fire-change-event"
        /// <summary>
        /// Dispatches a "change" event on the target element via JavaScript.
        /// </summary>
        /// <param name="objectId">Remote object id of the target element.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the raw response from the Runtime.callFunctionOn command.</returns>
        public async Task<dynamic> FireChangeEvent(string objectId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeCallFunctionOn, new
            {
                objectId,
                contextId = CurrentContextId,
                functionDeclaration = "function() {{ this.dispatchEvent(new Event('change')); }}"
            }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "focus"
        /// <summary>
        /// Sets browser focus on the element identified by the given backend node id, using DOM.focus.
        /// </summary>
        /// <param name="backendNodeId">The CDP backend node id of the element to focus.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        public async Task Focus(int backendNodeId, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            // DOM.focus takes the backend id, which lasts as long as the element; a node id can go stale
            await SendCommand<object>(DevToolsMethods.DOMFocus, new { backendNodeId }, sessionId).ConfigureAwait(false);
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

        // Implementation for "fullscreen"
        /// <summary>
        /// Full screen the currnet browser window
        /// NOTE: in headless mode this is a maximize, there is no way to full screen in headless mode
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        public async Task<bool> FullScreen(string sessionId = null)
        {
            if (true == Browser.BrowserSettings.UseHeadless)
                return await Maximize().ConfigureAwait(false);
            else
            {
                sessionId ??= GetEffectiveSessionId();

                var windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);

                await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
                {
                    windowId,
                    bounds = new { windowState = "fullscreen" }  // - this is the CDP way for browser fullscreen
                }, sessionId).ConfigureAwait(false);
            }

            return true;
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

            // Read on the live element, the way the page computes it, rather than through CSS.getComputedStyleForNode,
            // which takes a node id that goes stale whenever DOM tracking is switched or reset
            dynamic styles = await CallOnElement(backendNodeId, @"
                    function(names) {
                        const style = getComputedStyle(this);
                        const found = {};
                        for (const name of names) found[name] = style.getPropertyValue(name);
                        return found;
                    }", sessionId, (object)relevantProps).ConfigureAwait(false);   // one argument, the whole list

            if (styles is Newtonsoft.Json.Linq.JObject styleObject)
            {
                retVal = new Dictionary<string, object>();

                foreach (var style in styleObject.Properties())
                {
                    string value = style.Value?.ToString()?.Trim();

                    if (false == string.IsNullOrEmpty(value) && false == emptyValues.Contains(value))
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
        //        await SendCommand<object>(DevToolsMethods.CSSEnable, new { }, sessionId);   // NOTE: this command generates a lot of 'unhandled' receivemessage messages of elements added - this must be done tho

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
        // this appears to be identical to the attributes we get from evaluateselector javascript
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

        // Implementation for "get-current-window"
        /// <summary>
        /// Gets the browser window id associated with the current target via Browser.getWindowForTarget.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the window id of the current target.</returns>
        public async Task<int> GetCurrentWindow(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var result = await SendCommand<dynamic>(
                DevToolsMethods.BrowserGetWindowForTarget,
                new { }, // no params needed
                sessionId
            ).ConfigureAwait(false);

            return (int)result.windowId;
        }

        /// <summary>
        /// The browser window position and size in screen coordinates, from Browser.getWindowBounds. Includes
        /// the window chrome, needs no JavaScript, and works while a page is still loading.
        /// </summary>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the window rectangle in screen coordinates.</returns>
        public async Task<System.Drawing.Rectangle> GetWindowRectangle(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            int windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);

            dynamic result = await SendCommand<dynamic>(
                DevToolsMethods.BrowserGetWindowBounds,
                new { windowId },
                sessionId
            ).ConfigureAwait(false);

            dynamic bounds = result?.bounds;

            // A minimized window reports its restored bounds
            return new System.Drawing.Rectangle(
                (int)(bounds?.left ?? 0),
                (int)(bounds?.top ?? 0),
                (int)(bounds?.width ?? 0),
                (int)(bounds?.height ?? 0));
        }

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

            // ── 1. Get root document ─────────────────────────────────────────────
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

            // ── 2. Get outerHTML ─────────────────────────────────────────────────
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

            // Return the raw page source. Normalization (JS-unescaping and stripping
            // Chrome's XML-viewer wrapper when a file is being displayed) is applied
            // uniformly for every engine at the choke point in Browser.GetPageSource,
            // so this path stays as "ignorant" as the OttoMagic and Selenium paths.
            return fullSource;
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
                supressedMessage = false;
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
                retVal = @"""loading""";  //

            string currentErrorMessage = $"Document.Ready status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, null, GPALObjectType.None);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }

            return retVal;
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

        // Implementation for "get-storage"
        /// <summary>
        /// written by chatgpt
        /// Retrieves browser storage items as raw JSON text.
        /// Supports hierarchical wildcards: domain -> storeName -> path -> key.
        /// </summary>
        /// <param name="storageType">Storage type: "cookie", "localStorage", "sessionStorage", "indexeddb", "cache"</param>
        /// <param name="sessionId">CDP session id; current session used if null</param>
        /// <param name="domain">Domain/origin; null acts as wildcard</param>
        /// <param name="storeName">Optional object store name (IndexedDB)</param>
        /// <param name="path">Optional path (cookies)</param>
        /// <param name="key">Optional key; null acts as wildcard</param>
        /// <returns>Raw JSON string representing requested items or identifiers</returns>
        public async Task<string> GetStorage(
            string storageType,
            string sessionId,
            string domain = null,
            string storeName = null,
            string path = null,
            string key = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string resultJson = "null";

            try
            {
                switch (storageType)
                {
                    case "cookie":
                        {
                            // get all cookies via CDP
                            dynamic cookiesResp = await SendCommand<dynamic>(DevToolsMethods.StorageGetCookies,
                                null,
                                sessionId).ConfigureAwait(false);

                            if (cookiesResp?.cookies != null)
                            {
                                var filtered = new List<dynamic>();
                                path ??= "/";

                                foreach (dynamic c in cookiesResp.cookies)
                                {
                                    if ((false == UrlHelper.CookieDomainMatches(((dynamic)c).domain?.ToString(), domain)) ||
                                        (!string.IsNullOrEmpty(path) && ((dynamic)c).path != path) ||
                                        (!string.IsNullOrEmpty(key) && ((dynamic)c).name != key))
                                        continue;

                                    // NOTE: this gets all cookies, not just a list of names given a wildcard (missing parm)
                                    // add partitionKey conditionally
                                    if (((dynamic)c).partitionKey != null)
                                    {
                                        filtered.Add(new
                                        {
                                            Key = ((dynamic)c).name,
                                            Domain = ((dynamic)c).domain,
                                            Path = ((dynamic)c).path,
                                            Value = ((dynamic)c).value,
                                            Expires = ((dynamic)c).expires,

                                            Secure = ((dynamic)c).secure,
                                            HttpOnly = ((dynamic)c).httpOnly,
                                            SameSite = ((dynamic)c).sameSite,
                                            Session = ((dynamic)c).session,
                                            Priority = ((dynamic)c).priority,
                                            SameParty = ((dynamic)c).sameParty,
                                            SourceScheme = ((dynamic)c).sourceScheme,
                                            SourcePort = ((dynamic)c).sourcePort,

                                            PartitionKey = ((dynamic)c).partitionKey
                                        });
                                    }
                                    else
                                    {
                                        filtered.Add(new
                                        {
                                            Key = ((dynamic)c).name,
                                            Domain = ((dynamic)c).domain,
                                            Path = ((dynamic)c).path,
                                            Value = ((dynamic)c).value,
                                            Expires = ((dynamic)c).expires,

                                            Secure = ((dynamic)c).secure,
                                            HttpOnly = ((dynamic)c).httpOnly,
                                            SameSite = ((dynamic)c).sameSite,
                                            Session = ((dynamic)c).session,
                                            Priority = ((dynamic)c).priority,
                                            SameParty = ((dynamic)c).sameParty,
                                            SourceScheme = ((dynamic)c).sourceScheme,
                                            SourcePort = ((dynamic)c).sourcePort
                                        });
                                    }
                                }

                                resultJson = JsonConvert.SerializeObject(filtered);
                            }
                            break;
                        }
                    case "localStorage":
                    case "sessionStorage":
                        {
                            string js = string.IsNullOrEmpty(key)
                                ? $@"(function(){{
                               let obj={{}};
                               for(let i=0;i<{storageType}.length;i++){{ let k={storageType}.key(i); obj[k]={storageType}.getItem(k); }}
                               return JSON.stringify(obj);
                             }})()"
                                : $"{storageType}.getItem('{EscapeJsString(key)}');";

                            var evalResult = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, sessionId).ConfigureAwait(false);

                            resultJson = evalResult?.result?.value?.ToString() ?? "null";
                            break;
                        }
                    case "indexedDb":
                        {
                            string js;

                            if (string.IsNullOrEmpty(path))
                            {
                                // No database name > list all IndexedDB databases
                                js = @"
                                    (async () => {
                                        let dbs = await indexedDB.databases();
                                        return JSON.stringify(dbs.map(db => ({ name: db.name, version: db.version })));
                                    })();";
                            }
                            else if (string.IsNullOrEmpty(storeName))
                            {
                                // Path (database name) given + no object store name > list all object stores in that DB
                                js = $@"
                                    (async () => {{
                                        let req = indexedDB.open('{EscapeJsString(path)}');
                                        return new Promise((resolve) => {{
                                            req.onsuccess = e => {{
                                                let db = e.target.result;
                                                let stores = Array.from(db.objectStoreNames);
                                                db.close();
                                                resolve(JSON.stringify(stores));
                                            }};
                                            req.onerror = () => resolve(null);
                                        }});
                                    }})();";
                            }
                            else if (string.IsNullOrEmpty(key))
                            {
                                // Path + storeName given + no key > list all keys in that specific object store
                                js = $@"
                                    (async () => {{
                                        let req = indexedDB.open('{EscapeJsString(path)}');
                                        return new Promise((resolve) => {{
                                            req.onsuccess = e => {{
                                                let db = e.target.result;
                                                let tx = db.transaction('{EscapeJsString(storeName)}', 'readonly');
                                                let store = tx.objectStore('{EscapeJsString(storeName)}');
                                                let keys = [];
                                                let cursorReq = store.openKeyCursor();
                                                cursorReq.onsuccess = function(event) {{
                                                    let cursor = event.target.result;
                                                    if (cursor) {{
                                                        keys.push(cursor.key);
                                                        cursor.continue();
                                                    }} else {{
                                                        db.close();
                                                        resolve(JSON.stringify(keys));
                                                    }}
                                                }};
                                                cursorReq.onerror = () => {{ db.close(); resolve(null); }};
                                            }};
                                            req.onerror = () => resolve(null);
                                        }});
                                    }})();";
                            }
                            else
                            {
                                // All three (path + storeName + key) > get the value for that key
                                js = $@"
                                    (async () => {{
                                        let req = indexedDB.open('{EscapeJsString(path)}');
                                        return new Promise((resolve) => {{
                                            req.onsuccess = e => {{
                                                let db = e.target.result;
                                                let tx = db.transaction('{EscapeJsString(storeName)}', 'readonly');
                                                let store = tx.objectStore('{EscapeJsString(storeName)}');
                                                let getReq = store.get('{EscapeJsString(key)}');
                                                getReq.onsuccess = () => {{
                                                    db.close();
                                                    resolve(JSON.stringify(getReq.result));
                                                }};
                                                getReq.onerror = () => {{ db.close(); resolve(null); }};
                                            }};
                                            req.onerror = () => resolve(null);
                                        }});
                                    }})();";
                            }

                            var evalDb = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, sessionId).ConfigureAwait(false);

                            resultJson = evalDb?.result?.value?.ToString() ?? "null";
                            break;
                        }

                    case "cache":
                        {
                            string js;

                            if (string.IsNullOrEmpty(storeName))
                            {
                                // no key = enumerate alll cache names
                                // key = return all with the key name
                                js = string.IsNullOrEmpty(key)
                                    ? @"(async ()=>{let c=await caches.keys(); return JSON.stringify(c);})()"
                                    : $@"(async () => {{
                                        const response = await caches.match('{EscapeJsString(key)}');
                                        if (response) {{
                                            const data = await response.json();
                                            return JSON.stringify(data);
                                        }}
                                        return null;
                                    }})();";
                            }
                            else if (string.IsNullOrEmpty(key))
                            {
                                // Path (cache name) is defined + no key > list all keys (as URLs) inside that specific cache
                                js = $@"(async () => {{
                                    const cache = await caches.open('{EscapeJsString(storeName)}');
                                    const keys = await cache.keys();
                                    const keyUrls = keys.map(k => k.url);           // keys are Request objects; we return clean URLs
                                    return JSON.stringify(keyUrls);
                                }})();";
                            }
                            else
                            {
                                // Both path and key are defined > open the specific cache and match the key inside it
                                js = $@"(async () => {{
                                    const cache = await caches.open('{EscapeJsString(storeName)}');
                                    const response = await cache.match('{EscapeJsString(key)}');
                                        if (response) {{{{
                                            const contentType = response.headers.get('content-type') || '';
                                            let data;
                                            if (contentType.includes('application/json') || contentType.includes('text/json')) {{{{
                                                data = await response.json();
                                            }}}} else {{{{
                                                data = await response.text();
                                            }}}}
                                            return JSON.stringify(data);
                                        }}}}
                                        return null;
                                    }})();";

                            }
                            var evalCache = await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            }, sessionId).ConfigureAwait(false);

                                resultJson = evalCache?.result?.value?.ToString() ?? "null";
                            break;
                        }

                    case "notSet":
                        GPAL.PublishSimpleEvent(
                            GPALEventType.WARNING,
                            $"Nothing to do because StorageType is [{storageType}] d[{domain}] sn[{storeName}] p[{path}] k[{key}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;

                    default:
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Unsupported storage type: [{storageType}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"GetStorage failed for [{storageType}] d[{domain}] sn[{storeName}] p[{path}] k[{key}]",
                    this,
                    GPALObjectType.PuppeteerCommunicator,
                    ex);
            }

            return resultJson;
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

        // Implementation for "goto-tab"
        /// <summary>
        /// Switches the active tab to the one identified by the given index, target id, or URL.
        /// </summary>
        /// <param name="tabIdOrUrlOrIndex">An <see cref="int"/> tab index within the active window, or a <see cref="string"/> targetId or URL identifying the tab.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the targetId of the activated tab, or null if no matching tab was found.</returns>
        public async Task<string> GoToTab(object tabIdOrUrlOrIndex, string sessionId = null)
        {
            string targetId = null;
            var windowArray = _windowSessionsQueue.ToArray();

            if (tabIdOrUrlOrIndex is int index)
            {
                if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No active window for GoToTab", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }

                var tabQueue = windowArray[_activeWindowIndex].TabQueue.ToArray();
                if (index < 0 || index >= tabQueue.Length)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid tab index [{index}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }

                // Get targetId from index
                targetId = tabQueue[index].Key;
                if (string.IsNullOrEmpty(targetId))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No targetId at index [{index}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }

                // Target.activateTarget switches the tab. CDP cannot do that without focusing it, which brings the
                // window to the front.
                await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId }, null).ConfigureAwait(false);
                SetActiveTabIndex(index);
                ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;

                return targetId;
            }
            else if (tabIdOrUrlOrIndex is string str)
            {
                targetId = await GetTargetTabIdByUrl(str, sessionId).ConfigureAwait(false);
                if (string.IsNullOrEmpty(targetId))
                {
                    // Check if it's a direct targetId
                    foreach (var window in windowArray)
                    {
                        var tabArray = window.TabQueue.ToArray();
                        if (tabArray.Any(kvp => kvp.Key == str))
                        {
                            targetId = str;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(targetId))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No target found for tabId or URL [{str}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }

                int windowIndex = GetCurrentWindowIndex(targetId);
                if (windowIndex < 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Tab with targetId [{targetId}] not found in any window", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }

                SetActiveWindowIndex(windowIndex);
                var tabQueue = windowArray[windowIndex].TabQueue.ToArray();
                int tabIndex = Array.FindIndex(tabQueue, kvp => kvp.Key == targetId);
                if (tabIndex < 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Tab with targetId [{targetId}] not found in window [{windowArray[windowIndex].BrowserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }

                await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId }, null).ConfigureAwait(false);
                SetActiveTabIndex(tabIndex);
                ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Switched to tab with targetId [{targetId}], windowIndex [{windowIndex}], tabIndex [{tabIndex}]", this, GPALObjectType.PuppeteerCommunicator);
                return targetId;
            }

            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid input for GoToTab [{tabIdOrUrlOrIndex}]", this, GPALObjectType.PuppeteerCommunicator);
            return null;
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
                    if (-1 == rect.X)   // NOTE: CAVEAT: if we cannot compute the box model, the element is prolly not interactable/false positive, so fail-forward
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
                if (-1 == rect.X)   // NOTE: CAVEAT: if we cannot compute the box model, the element is prolly not interactable/false positive, so fail-forward
                    isInViewport = true;
                else
                    isInViewport &= rect.Top >= 0 &&
                           rect.Left >= 0 &&
                           rect.Bottom <= viewportHeight &&
                           rect.Right <= viewportWidth;
            }
            return isInViewport;
        }

        // Where the last move left the pointer, so the next one moves from there. Null until the first move.
        private PointF? pointerAt = null;

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
                    y = from.Y + (y - from.Y) * along
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
                // NOTE: for headless which is not finding a box model on an A tag
                // Get fresh box model
                // Cheap reflow trigger via JS (very reliable)
                if (true == Browser.BrowserSettings.UseHeadless)
                {
                    await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                    {
                        expression = "document.body.offsetHeight;",  // reads force layout
                        contextId = CurrentContextId,
                        returnByValue = true
                    }, sessionId).ConfigureAwait(false);

                    await Task.Delay(80).ConfigureAwait(false);  // extra breathing room
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
                            // Force center for <a> tags (your existing behavior)
                            if ("A".Equals(element.TagName, StringComparison.OrdinalIgnoreCase))
                            {
                                x = (float)contentQuad[0] + width / 2f;
                                y = (float)contentQuad[1] + height / 2f;
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
                        // Force center for <a> tags (your existing behavior)
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
                modifiers = 0
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
                    modifiers = 0
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
                modifiers = 0
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

        /// <summary>
        /// The browser's own process, as the browser reports it (SystemInfo.getProcessInfo, type "browser"). A browser
        /// GPAL connected to rather than launched has no Process otherwise, and bringing its window to the front
        /// needs one.
        /// </summary>
        /// <returns>The browser process, or null when the browser did not report one or it is no longer running</returns>
        internal async Task<System.Diagnostics.Process> GetBrowserProcess()
        {
            System.Diagnostics.Process retVal = null;
            dynamic info = await SendCommand<object>(DevToolsMethods.SystemInfoGetProcessInfo, new { }, null).ConfigureAwait(false);
            int pid = 0;

            if (null != info?.processInfo)
                foreach (dynamic process in info.processInfo)
                    if ("browser" == process.type?.ToString())
                        pid = Convert.ToInt32(process.id);

            if (0 < pid)
                retVal = System.Diagnostics.Process.GetProcesses().FirstOrDefault(p => p.Id == pid);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The browser reports its process as [{pid}], {(null != retVal ? "found" : "not running")}", this, GPALObjectType.PuppeteerCommunicator);

            return retVal;
        }

        /// <summary>
        /// Saves downloads to <paramref name="downloadPath"/> with no Save dialog. Browser-wide, so later tabs use it
        /// too.<br/>
        /// Done over CDP because the dialog setting is a launch-time profile preference that
        /// <see cref="MagicHelper.LaunchBrowser"/> writes only into profiles it creates.
        /// </summary>
        /// <param name="downloadPath">Directory downloads should land in</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null</param>
        /// <returns>Task representing the asynchronous call</returns>
        public async Task SetDownloadBehavior(string downloadPath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "allow", downloadPath = downloadPath }, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets download behavior back to "default", so a user's profile stops saving into the workflow's folder. CDP
        /// cannot read the previous directory.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null</param>
        /// <returns>Task representing the asynchronous call</returns>
        public async Task RestoreDownloadBehavior(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "default" }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "left-click-download"
        // NOTE: HEADLESS ONLY
        /// <summary>
        /// Performs a left click on each element in <paramref name="elems"/> to trigger a file download (headless mode).
        /// Sets the browser-level download behavior to the directory containing <paramref name="downloadPath"/> before clicking.
        /// The actual filename cannot be controlled here, so a download watcher is required to detect the resulting file.
        /// Implementation for the "left-click-download" workflow action. Headless only.
        /// </summary>
        /// <param name="elems">Elements to click (typically a download link or button).</param>
        /// <param name="downloadPath">Target file path; only its directory is used to configure the download location.</param>
        /// <param name="modifiers">Keyboard modifier flags applied to the click (e.g. Ctrl/Shift).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <param name="responses">List collecting the raw CDP responses from each click.</param>
        /// <returns>Task representing the asynchronous click-and-download operation.</returns>
        public async Task LeftClickAndDownload(List<GPALElement> elems, string downloadPath, int modifiers, string sessionId, List<dynamic> responses)
        {
            sessionId ??= GetEffectiveSessionId();

            string saveDirectory = Path.GetDirectoryName(downloadPath);

            // NOTE: we can set the download directory, but not the filename, so we still require the downloadwatcher
            await SetDownloadBehavior(saveDirectory, sessionId).ConfigureAwait(false);

            foreach (GPALElement elem in elems)
            {
                await ScrollIntoView(elem, sessionId).ConfigureAwait(false);
                await ClickElement(elem, sessionId, responses, ClickType.LeftClick, modifiers).ConfigureAwait(false);
            }
        }
        // Implementation for "left-click-and-upload"
        // NOTE: HEADLESS ONLY
        // NOTE: CAV#EAT: in order to call leftclickandupload repeatedly, we must track the files ourselves and clear the list...
        private static readonly Dictionary<string, List<string>> _attachedFilesPerSession = new Dictionary<string, List<string>>();

        /// <summary>
        /// Attaches one or more files to file input element(s) using CDP.
        /// Supports single file path (string) or multiple paths (List&lt;string&gt; / IEnumerable&lt;string&gt;).
        /// Accumulates files across calls by tracking attached paths per session/input and re-attaching the full list each time.
        /// Uses DOM.setFileInputFiles to set files directly (headless-friendly, no dialog).
        /// Dispatches change event after attachment to trigger page/widget processing.
        /// </summary>
        /// <param name="elems">List of GPALElement instances representing the file input(s). Usually one, but supports multiple.</param>
        /// <param name="uploadFiles">
        /// File path(s) to attach.
        /// - string: single file path (e.g. @"C:\temp\report.pdf")
        /// - IEnumerable&lt;string&gt; or List&lt;string&gt;: multiple file paths (e.g. new[] { @"C:\file1.pdf", @"C:\file2.jpg" })
        /// </param>
        /// <param name="modifiers">Keyboard modifiers (not used in current implementation)</param>
        /// <param name="sessionId">CDP session ID (optional; falls back to current)</param>
        /// <param name="responses">Optional list to collect status/info (e.g. for logging or fluent chaining)</param>
        /// <returns>Task (async operation)</returns>
        /// <example>
        /// Single file:
        /// <code>
        /// await LeftClickAndUpload(elems, @"C:\temp\doc.pdf", 0, sessionId, responses);
        /// </code>
        ///
        /// Multiple files (additive across calls):
        /// <code>
        /// var files = new List&lt;string&gt; { @"C:\doc1.pdf" };
        /// await LeftClickAndUpload(elems, files, 0, sessionId, responses);
        ///
        /// files.Add(@"C:\photo.jpg");
        /// await LeftClickAndUpload(elems, files, 0, sessionId, responses);  // now attaches both
        /// </code>
        ///
        /// Mixed:
        /// <code>
        /// await LeftClickAndUpload(elems, @"C:\invoice.docx", 0, sessionId, responses);  // single
        /// await LeftClickAndUpload(elems, new[] { @"C:\old1.pdf", @"C:\old2.pdf" }, 0, sessionId, responses);  // full list
        /// </code>
        /// </example>
        public async Task LeftClickAndUpload(
            List<GPALElement> elems,
            object uploadFiles,
            int modifiers,
            string sessionId,
            List<dynamic> responses)
        {
            sessionId ??= GetEffectiveSessionId();

            // Normalize uploadFiles to List<string>
            var newPaths = new List<string>();

            if (uploadFiles is string singlePath)
            {
                if ("" != singlePath?.Trim())
                {
                    newPaths.Add(Path.GetFullPath(singlePath));
                }
            }
            else if (uploadFiles is GPALFile pathEnumerable)
            {
                foreach (var p in pathEnumerable.Filenames)
                {
                    if ("" != p?.Trim())
                    {
                        newPaths.Add(Path.GetFullPath(p));
                    }
                }
            }
            else
            {
                // Invalid type – silent skip (fluent style)
                return;
            }

            if (0 == newPaths.Count)
            {
                return; // nothing to attach
            }

            foreach (GPALElement fileInput in elems)
            {
                string nodeKey = $"{sessionId}:{fileInput.ElementBackendNodeId}";

                // Get browser-reported file count via attribute
                string fileCountStr = fileInput.GetAttribute("length");
                int browserFileCount = 0;

                if ("" != fileCountStr)
                {
                    Int32.TryParse(fileCountStr, out browserFileCount);
                }

                // Get or create tracked list
                if (false == _attachedFilesPerSession.TryGetValue(nodeKey, out var currentFiles))
                {
                    currentFiles = new List<string>();
                    _attachedFilesPerSession[nodeKey] = currentFiles;
                }

                // Staleness check: browser count != tracked count > reset
                if (browserFileCount != currentFiles.Count)
                {
                    currentFiles.Clear();
                    _attachedFilesPerSession[nodeKey] = currentFiles;
                }

                // Add new paths
                currentFiles.AddRange(newPaths);

                var setFilesParams = new
                {
                    nodeId = fileInput.ElementNodeId,
                    files = currentFiles.ToArray()
                };

                await SendCommand<object>(DevToolsMethods.DOMSetFileInputFiles, setFilesParams, sessionId).ConfigureAwait(false);
                await FireChangeEvent(fileInput.ElementHandle, sessionId).ConfigureAwait(false);

                responses?.Add(new
                {
                    status = "upload_attached",
                    pathsAdded = newPaths,
                    totalFilesTracked = currentFiles.Count,
                    browserReportedCount = browserFileCount
                });
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


        // Implementation for "maximize"
        /// <summary>
        /// Maximizes the browser window so it fills the primary screen.
        /// Implementation for the "maximize" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true once the window bounds have been updated.</returns>
        public async Task<bool> Maximize(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);

            // Chromium refuses to maximize or minimize a full-screen window, or maximize a minimized one, so the
            // window goes back to normal first
            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId,
                bounds = new { windowState = "normal" }
            }, sessionId).ConfigureAwait(false);

            // window state, not bounds - chromium reads a bounds set as a normal window at that size
            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId,
                bounds = new { windowState = "maximized" }
            }, sessionId).ConfigureAwait(false);

            return true;
        }

        // Implementation for "minimize"
        /// <summary>
        /// Minimizes the browser window.
        /// Implementation for the "minimize" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true once the window state has been set to minimized.</returns>
        public async Task<bool> Minimize(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);

            // Chromium refuses to maximize or minimize a full-screen window, or maximize a minimized one, so the
            // window goes back to normal first
            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId,
                bounds = new { windowState = "normal" }
            }, sessionId).ConfigureAwait(false);

            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId = windowId,
                bounds = new { windowState = "minimized" }
            }, sessionId).ConfigureAwait(false);

            return true;
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

                    if (true == "A".Equals(element.TagName)) // click in dead center, don't miss it, altho width could be a bit more tolerant
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

        // Implementation for "new-tab"
        /// <summary>
        /// Opens a new background tab in the currently active window and navigates it to the given URL.
        /// Resolves the URL via the magic helper, honors robots.txt restrictions when configured,
        /// and registers the new target/tab in the active window's tab queue.
        /// Implementation for the "new-tab" workflow action.
        /// </summary>
        /// <param name="url">URL to open in the new tab; defaults to a blank GPALUrl if null.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the new tab's target ID, or null if no active window was found.</returns>
        /// <example>
        /// <code>
        /// string newTargetId = await NewTab(new GPALUrl("https://example.com"));
        /// </code>
        /// </example>
        public async Task<string> NewTab(GPALUrl url = null, string sessionId = null)
        {
            if (null == url)
                url = new GPALUrl();

            sessionId ??= GetEffectiveSessionId();
            url.ForUrl(MagicHelper.GetFullUrl(url.Url, Browser, out Browser._areRobotsAllowed));

            if (false == Browser.AreRobotsAllowed && true == Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Visiting [{url.Url}] is disallowed by robots.txt and your request to honor it. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerCommunicator);
                url.ForUrl("https://google.com");
            }

            var browserContextId = GetActiveWindowId();
            if (string.IsNullOrEmpty(browserContextId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    "NewTab: No active window found to attach tab",
                    this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            // Create new target in the current active window (browserContextId).
            // Not a background tab: NewTab switches to it, as on Selenium and OttoMagic.
            // It opens blank and navigates once attached, so the registered scripts run on its first page too.
            var result = await SendCommand<object>(DevToolsMethods.TargetCreateTarget,
                new { url = "about:blank", browserContextId }, null).ConfigureAwait(false);

            string targetId = result.targetId.ToString();

            // Activate the new tab, then attach, so later commands go to it.
            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId }, null).ConfigureAwait(false);

            string tabSessionId = await WaitForAutoSession(targetId).ConfigureAwait(false);

            if (true == string.IsNullOrEmpty(tabSessionId))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Opened tab [{targetId}] but could not attach to it, so commands still go to the tab before it", this, GPALObjectType.PuppeteerCommunicator);

            // Handle attachment and queuing
            await HandleNewTarget(targetId, browserContextId, tabSessionId, false, true).ConfigureAwait(false);

            await SendCommand<object>(DevToolsMethods.PageNavigate, new { url = url.Url }, tabSessionId).ConfigureAwait(false);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                $"Opened new tab TargetId [{targetId}] in Window [{browserContextId}]",
                this, GPALObjectType.PuppeteerCommunicator);

            return targetId;
        }

        // helper for next/previoustab
        // Implementation for getCurrentTabIndex
        /// <summary>
        /// Determines the index of the given target ID within the active window's tab queue,
        /// falling back to the window's currently recorded active tab index if the target is not found.
        /// Helper used by NextTab and PreviousTab.
        /// </summary>
        /// <param name="currentTargetId">Target ID of the tab whose index is being looked up.</param>
        /// <returns>Zero-based index of the tab in the active window's tab queue, or 0 if there is no active window or tab queue.</returns>
        internal int GetCurrentTabIndex(string currentTargetId)
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No active window for NextTab", this, GPALObjectType.PuppeteerCommunicator);
                return 0;
            }

            var tabQueue = windowArray[_activeWindowIndex].TabQueue;
            var queueArray = tabQueue.ToArray();
            if (queueArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No tabs for NextTab", this, GPALObjectType.PuppeteerCommunicator);
                return 0;
            }

            var currentIndex = Array.FindIndex(queueArray, kvp => kvp.Key == currentTargetId);
            if (currentIndex == -1)
                currentIndex = windowArray[_activeWindowIndex].ActiveTabIndex;

            return currentIndex;
        }
        // Implementation for "next-tab"
        /// <summary>
        /// Switches the active tab in the current window to the next tab in the tab queue (wrapping around).
        /// Before switching, polls CDP for all targets to discover and register any tabs not yet in the
        /// queue. Implementation for the "next-tab" workflow action.
        /// </summary>
        /// <param name="currentTargetId">Target ID of the currently active tab, used to determine the starting position.</param>
        /// <returns>Task that resolves to the target ID of the newly activated next tab, or null if there is no active window or no tabs.</returns>
        public async Task<string> NextTab(string currentTargetId)
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No active window for NextTab", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var session = windowArray[_activeWindowIndex];
            var tabQueue = session.TabQueue;
            var browserContextId = session.BrowserContextId;  // Use stored BrowserContextId

            // Poll all targets via CDP to discover unknown pages
            var targetsResponse = await SendCommand<dynamic>(
                DevToolsMethods.TargetGetTargets,
                new { },
                null  // Browser-level command, no sessionId
            ).ConfigureAwait(false);

            if (targetsResponse?.targetInfos == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Failed to get targets list in NextTab sync", this, GPALObjectType.PuppeteerCommunicator);
                // Proceed anyway with existing queue
            }
            else
            {
                var knownTargetIds = new HashSet<string>(tabQueue.Select(kv => kv.Key));

                foreach (var targetInfo in targetsResponse.targetInfos)
                {
                    string targetId = targetInfo.targetId?.ToString();
                    string type = targetInfo.type?.ToString();
                    string contextId = targetInfo.browserContextId?.ToString();

                    if (string.IsNullOrEmpty(targetId) || type != "page" || contextId != browserContextId)
                        continue;

                    if (!knownTargetIds.Contains(targetId))
                    {
                        // Add with doNotGetSendSemaphore=true to avoid reentrancy/locking issues
                        await AddTabToQueue(targetId, null, false, true).ConfigureAwait(false);
                        knownTargetIds.Add(targetId);
                    }
                }
            }

            var queueArray = tabQueue.ToArray();
            if (queueArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No tabs for NextTab", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            // Find the current tab by target id, as PreviousTab does. ActiveTabIndex goes wrong when tabs change, so it
            // is only the fallback when the id is not in the queue.
            var currentIndex = Array.FindIndex(queueArray, kvp => kvp.Key == currentTargetId);
            if (currentIndex == -1)
            {
                currentIndex = session.ActiveTabIndex;
            }

            var nextIndex = (currentIndex + 1) % queueArray.Length;
            var nextTargetId = await GoToTab(nextIndex).ConfigureAwait(false);

            session.ActiveTabIndex = nextIndex;

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                $"Switched to index [{nextIndex}], ID [{nextTargetId}], Active window tab queue size [{queueArray.Length}]",
                this, GPALObjectType.PuppeteerCommunicator);

            return nextTargetId;
        }
        /*
        public async Task<string> NextTab(string currentTargetId)
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No active window for NextTab", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var tabQueue = windowArray[_activeWindowIndex].TabQueue;
            var queueArray = tabQueue.ToArray();
            if (queueArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No tabs for NextTab", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var currentIndex = windowArray[_activeWindowIndex].ActiveTabIndex;

            var nextIndex = (currentIndex + 1) % queueArray.Length;
            var nextTargetId = await GoToTab(nextIndex);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Switched to index [{nextIndex}], ID [{nextTargetId}], Active window tab queue size [{queueArray.Length}]", this, GPALObjectType.PuppeteerCommunicator);

            return nextTargetId;
        }
        */
        // Implementation for "next-window"
        /// <summary>
        /// Switches the active browser window/context to the next window in the window queue (wrapping around),
        /// activating that window's currently active tab and re-attaching the CDP session if needed.
        /// Implementation for the "next-window" workflow action.
        /// </summary>
        /// <param name="currentTargetId">Target ID of the currently active tab, used to determine which window is currently active.</param>
        /// <returns>Task that resolves to the target ID of the active tab in the next window, or null if there are no windows or no tabs in the next window.</returns>
        public async Task<string> NextWindow(string currentTargetId)
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No windows for NextWindow", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var currentWindowIndex = -1;
            for (int i = 0; i < windowArray.Length; i++)
            {
                var tabArray = windowArray[i].TabQueue.ToArray();
                if (Array.Exists(tabArray, kvp => kvp.Key == currentTargetId))
                {
                    currentWindowIndex = i;
                    break;
                }
            }
            if (currentWindowIndex == -1)
            {
                currentWindowIndex = _activeWindowIndex;
            }

            var nextWindowIndex = (currentWindowIndex + 1) % windowArray.Length;
            SetActiveWindowIndex(nextWindowIndex);

            var nextWindowContextId = windowArray[nextWindowIndex].BrowserContextId;
            var nextTabQueue = windowArray[nextWindowIndex].TabQueue;
            var nextTabArray = nextTabQueue.ToArray();
            if (nextTabArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No tabs in next window for NextWindow", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var nextTargetId = nextTabArray[windowArray[nextWindowIndex].ActiveTabIndex].Key;
            var nextSessionId = nextTabArray[windowArray[nextWindowIndex].ActiveTabIndex].Value;

            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId = nextTargetId }, null).ConfigureAwait(false);

            // A tab found by the target sync has no session yet, so it is attached here. The session is filled in
            // on the tab where it already sits in the queue, and gets the injected scripts.
            if (null == nextSessionId)
            {
                var newSession = await SendCommand<object>(DevToolsMethods.TargetAttachToTarget, new { targetId = nextTargetId, flatten = true }, null).ConfigureAwait(false);
                if (newSession?.sessionId != null)
                {
                    nextSessionId = (string)newSession.sessionId;
                    await AddTabToQueue(nextTargetId, nextSessionId).ConfigureAwait(false);
                    await PrepareSession(nextSessionId).ConfigureAwait(false);
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to re-attach NextWindow tab ID [{nextTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"NextWindow: Switched to window index [{nextWindowIndex}], Context [{nextWindowContextId}], Tab ID [{nextTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            return nextTargetId;
        }
        // Implementation for "normal"
        /// <summary>
        /// Restores the browser window to its normal (non-maximized, non-minimized) state.
        /// Implementation for the "normal" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true once the window state has been set to normal.</returns>
        public async Task<bool> Normal(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);
            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId = windowId,
                bounds = new { windowState = "normal" }
            }, sessionId).ConfigureAwait(false);

            return true;
        }

        // Implementation for "open-window"
        /// <summary>
        /// Opens a brand new browser window (its own browser context) navigated to the given URL,
        /// activates and attaches to it via CDP, and registers it as the new active window with its own tab queue.
        /// Implementation for the "open-window" workflow action.
        /// </summary>
        /// <param name="url">URL to load in the new window; defaults to "https://google.com".</param>
        /// <param name="sessionId">CDP session ID used for the create/attach commands; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the target ID of the newly created window's tab.</returns>
        public async Task<string> OpenWindow(string url = "https://google.com", string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            // Create the new target in its own browser context
            var context = await SendCommand<object>(DevToolsMethods.TargetCreateBrowserContext, new { }, sessionId).ConfigureAwait(false);
            string browserContextId = context.browserContextId.ToString();

            // It opens blank and navigates once attached, so the registered scripts run on its first page too
            var result = await SendCommand<object>(DevToolsMethods.TargetCreateTarget,
                new { url = "about:blank", browserContextId }, null).ConfigureAwait(false);

            string targetId = result.targetId.ToString();

            // Activate and attach
            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId }, null).ConfigureAwait(false);
            string newSessionId = await WaitForAutoSession(targetId).ConfigureAwait(false);

            // Build tab queue for this window
            var tabQueue = new ConcurrentQueue<KeyValuePair<string, string>>();
            tabQueue.Enqueue(new KeyValuePair<string, string>(targetId, newSessionId));

            // Add new window with its tab queue into _windowSessionsQueue
            lock (_tabQueueLock)
                _windowSessionsQueue.Enqueue(new WindowSession(browserContextId, tabQueue, 0));

            // Update active window index to the newest one
            SetActiveWindowIndex(_windowSessionsQueue.Count - 1);

            ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;

            await PrepareSession(newSessionId).ConfigureAwait(false);
            await SendCommand<object>(DevToolsMethods.PageNavigate, new { url }, newSessionId).ConfigureAwait(false);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                $"Opened new window ContextId [{browserContextId}], TargetId [{targetId}], SessionId [{newSessionId}]",
                this, GPALObjectType.PuppeteerCommunicator);

            return targetId;
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

        // Implementation for "press-modifier-key"
        /// <summary>
        /// Dispatches a key-down event for the given modifier key(s) without a corresponding key-up,
        /// leaving the modifier(s) "held" for subsequent input events.
        /// Implementation for the "press-modifier-key" workflow action.
        /// </summary>
        /// <param name="modifierKeys">Bitmask of CDP modifier flags to press (e.g. Alt, Ctrl, Meta, Shift).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous key-down operation.</returns>
        public async Task PressModifierKey(int modifierKeys, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new { type = "keyDown", modifiers = modifierKeys }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "previous-tab"
        /// <summary>
        /// Switches the active tab in the current window to the previous tab in the tab queue (wrapping around).
        /// Before switching, polls CDP for all targets to discover and register any tabs not yet in the
        /// queue, updates the window's active tab index, and re-attaches the CDP session if needed.
        /// Implementation for the "previous-tab" workflow action.
        /// </summary>
        /// <param name="currentTargetId">Target ID of the currently active tab, used to determine the starting position.</param>
        /// <returns>Task that resolves to the target ID of the newly activated previous tab, or null if there is no active window or no tabs.</returns>
        public async Task<string> PreviousTab(string currentTargetId)
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No active window for PreviousTab", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var session = windowArray[_activeWindowIndex];
            var tabQueue = session.TabQueue;
            var browserContextId = session.BrowserContextId;  // Use stored BrowserContextId

            // Poll all targets via CDP to discover unknown pages
            var targetsResponse = await SendCommand<dynamic>(
                DevToolsMethods.TargetGetTargets,
                new { },
                null  // Browser-level command, no sessionId
            ).ConfigureAwait(false);

            if (targetsResponse?.targetInfos == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Failed to get targets list in NextTab sync", this, GPALObjectType.PuppeteerCommunicator);
                // Proceed anyway with existing queue
            }
            else
            {
                var knownTargetIds = new HashSet<string>(tabQueue.Select(kv => kv.Key));

                foreach (var targetInfo in targetsResponse.targetInfos)
                {
                    string targetId = targetInfo.targetId?.ToString();
                    string type = targetInfo.type?.ToString();
                    string contextId = targetInfo.browserContextId?.ToString();

                    if (string.IsNullOrEmpty(targetId) || type != "page" || contextId != browserContextId)
                        continue;

                    if (!knownTargetIds.Contains(targetId))
                    {
                        // Add with doNotGetSendSemaphore=true to avoid reentrancy/locking issues
                        await AddTabToQueue(targetId, null, false, true).ConfigureAwait(false);
                        knownTargetIds.Add(targetId);
                    }
                }
            }

            var queueArray = tabQueue.ToArray();
            if (queueArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No tabs for PreviousTab", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var currentIndex = Array.FindIndex(queueArray, kvp => kvp.Key == currentTargetId);
            if (currentIndex == -1)
            {
                currentIndex = windowArray[_activeWindowIndex].ActiveTabIndex;
            }

            var prevIndex = (currentIndex - 1 + queueArray.Length) % queueArray.Length;
            var prevTargetId = queueArray[prevIndex].Key;
            var prevSessionId = queueArray[prevIndex].Value;

            // Update ActiveTabIndex in WindowSession. The tab queue written back is the current one, not the copy
            // read above, so a tab added or removed meanwhile is not undone.
            lock (_tabQueueLock)
            {
                var windowList = _windowSessionsQueue.ToList();
                windowList[_activeWindowIndex] = new WindowSession(
                    windowList[_activeWindowIndex].BrowserContextId,
                    windowList[_activeWindowIndex].TabQueue,
                    prevIndex
                );
                _windowSessionsQueue = new ConcurrentQueue<WindowSession>();
                foreach (var w in windowList)
                {
                    _windowSessionsQueue.Enqueue(w);
                }
            }

            ((PuppeteerClient)PuppeteerClient)._currentTargetId = prevTargetId;

            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId = prevTargetId }, null).ConfigureAwait(false);

            // A tab found by the target sync has no session yet, so it is attached here. The session is filled in
            // on the tab where it already sits in the queue, and gets the injected scripts.
            if (null == prevSessionId)
            {
                var newSession = await SendCommand<object>(DevToolsMethods.TargetAttachToTarget, new { targetId = prevTargetId, flatten = true }, null).ConfigureAwait(false);
                if (newSession?.sessionId != null)
                {
                    prevSessionId = (string)newSession.sessionId;
                    await AddTabToQueue(prevTargetId, prevSessionId).ConfigureAwait(false);
                    await PrepareSession(prevSessionId).ConfigureAwait(false);
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to re-attach PreviousTab ID [{prevTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"PreviousTab: Switched to index [{prevIndex}], ID [{prevTargetId}], Active window tab queue size [{queueArray.Length}]", this, GPALObjectType.PuppeteerCommunicator);
            return prevTargetId;
        }

        // Implementation for "previous-window"
        /// <summary>
        /// Switches the active browser window/context to the previous window in the window queue (wrapping around),
        /// activating that window's currently active tab and re-attaching the CDP session if needed.
        /// Implementation for the "previous-window" workflow action.
        /// </summary>
        /// <param name="currentTargetId">Target ID of the currently active tab, used to determine which window is currently active.</param>
        /// <returns>Task that resolves to the target ID of the active tab in the previous window, or null if there are no windows or no tabs in the previous window.</returns>
        public async Task<string> PreviousWindow(string currentTargetId)
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No windows for PreviousWindow", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var currentWindowIndex = -1;
            for (int i = 0; i < windowArray.Length; i++)
            {
                var tabArray = windowArray[i].TabQueue.ToArray();
                if (Array.Exists(tabArray, kvp => kvp.Key == currentTargetId))
                {
                    currentWindowIndex = i;
                    break;
                }
            }
            if (currentWindowIndex == -1)
            {
                currentWindowIndex = _activeWindowIndex;
            }

            var prevWindowIndex = (currentWindowIndex - 1 + windowArray.Length) % windowArray.Length;
            SetActiveWindowIndex(prevWindowIndex);

            var prevWindowContextId = windowArray[prevWindowIndex].BrowserContextId;
            var prevTabQueue = windowArray[prevWindowIndex].TabQueue;
            var prevTabArray = prevTabQueue.ToArray();
            if (prevTabArray.Length == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No tabs in previous window for PreviousWindow (context [{prevWindowContextId}])", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            var prevTargetId = prevTabArray[windowArray[prevWindowIndex].ActiveTabIndex].Key;
            var prevSessionId = prevTabArray[windowArray[prevWindowIndex].ActiveTabIndex].Value;

            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId = prevTargetId }, null).ConfigureAwait(false);

            // A tab found by the target sync has no session yet, so it is attached here. The session is filled in
            // on the tab where it already sits in the queue, and gets the injected scripts.
            if (null == prevSessionId)
            {
                var newSession = await SendCommand<object>(DevToolsMethods.TargetAttachToTarget, new { targetId = prevTargetId, flatten = true }, null).ConfigureAwait(false);
                if (newSession?.sessionId != null)
                {
                    prevSessionId = (string)newSession.sessionId;
                    await AddTabToQueue(prevTargetId, prevSessionId).ConfigureAwait(false);
                    await PrepareSession(prevSessionId).ConfigureAwait(false);
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to re-attach PreviousWindow tab ID [{prevTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
                    return null;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"PreviousWindow: Switched to window index [{prevWindowIndex}], Context [{prevWindowContextId}], Tab ID [{prevTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            return prevTargetId;
        }        // Implementation for "query-selector"
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

        // Implementation for "release-modifier-key"
        /// <summary>
        /// Dispatches a key-up event for the given modifier key(s), releasing modifier(s) previously
        /// held via <see cref="PressModifierKey"/>.
        /// Implementation for the "release-modifier-key" workflow action.
        /// </summary>
        /// <param name="modifierKeys">Bitmask of CDP modifier flags to release (e.g. Alt, Ctrl, Meta, Shift).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous key-up operation.</returns>
        public async Task ReleaseModifierKey(int modifierKeys, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, new { type = "keyUp", modifiers = modifierKeys }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "restore"
        /// <summary>
        /// Restores the browser window to its normal (non-maximized, non-minimized) window state.
        /// Implementation for the "restore" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true once the window state has been set to normal.</returns>
        public async Task<bool> Restore(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);
            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId = windowId,
                bounds = new { windowState = "normal" }
            }, sessionId).ConfigureAwait(false);

            return true;
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

                    //var success = callResult?.result?.value as bool? ?? false;

                    //if (!success)
                    //{
                    //    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    //        $"JS scroll fallback failedfor element [{element.Css}]",
                    //        this, GPALObjectType.PuppeteerCommunicator);
                    //    // still return true if you want fail-forward, or false if stricter
                    //}

                    // Assume recovery succeeded after JS — we don't re-check box model
                    // we don't care about the return value, what else are we going to do, we have nothing to do
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
            await Task.Delay(150).ConfigureAwait(false);  // NOTE: for headless
        }

        // Implementation for "send-key"
        /// <summary>
        /// Dispatches a full key press sequence (keydown, optional char, keyup) for the given key via CDP,
        /// using "\r" as the char payload for the Enter key so forms submit correctly, and the literal
        /// character for single-character keys.
        /// Implementation for the "send-key" workflow action.
        /// </summary>
        /// <param name="key">Key value (e.g. "Enter", "a", "ArrowDown") as used by the CDP key event.</param>
        /// <param name="code">Physical key code (e.g. "Enter", "KeyA", "ArrowDown") as used by the CDP key event.</param>
        /// <param name="vkCode">Windows virtual key code corresponding to the key.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to true if all key events were dispatched successfully, or false if an exception occurred.</returns>
        public async Task<dynamic> SendKey(string key, string code, int vkCode, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            try
            {
                // For forms to submit on Enter, Chrome requires the text property to be "\r".
                // Otherwise, printable characters use their literal string value.
                string textPayload = (code == "Enter") ? "\r" : (key.Length == 1 ? key : null);

                // 1. Dispatch Key Down
                var keyDownParams = new
                {
                    type = "rawKeyDown",
                    key = key,
                    code = code,
                    windowsVirtualKeyCode = vkCode,
                    nativeVirtualKeyCode = vkCode
                };
                await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, keyDownParams, sessionId).ConfigureAwait(false);

                // 2. Dispatch Char (Only if the key sequence produces an explicit input character)
                if (textPayload != null)
                {
                    var charParams = new
                    {
                        type = "char",
                        text = textPayload,
                        unmodifiedText = textPayload,
                        key = key,
                        code = code,
                        windowsVirtualKeyCode = vkCode,
                        nativeVirtualKeyCode = vkCode
                    };
                    await SendCommand<object>(DevToolsMethods.InputDispatchKeyEvent, charParams, sessionId).ConfigureAwait(false);
                }

                // 3. Dispatch Key Up
                var keyUpParams = new
                {
                    type = "keyUp",
                    key = key,
                    code = code,
                    windowsVirtualKeyCode = vkCode,
                    nativeVirtualKeyCode = vkCode
                };
                await SendCommand<dynamic>(DevToolsMethods.InputDispatchKeyEvent, keyUpParams, sessionId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"SendKey execution failed for Code [{code}]", this, GPALObjectType.PuppeteerCommunicator, ex);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Maps a key name (e.g. "Enter", "Tab", "ArrowUp", "F1") to its corresponding Windows virtual key code.
        /// Returns 0 for letters, digits, symbols, and any unrecognized key name.
        /// </summary>
        /// <param name="key">Key name to map (case-insensitive).</param>
        /// <returns>The Windows virtual key code for the key, or 0 if unknown or not a special key.</returns>
        private int GetVirtualKeyCode(string key)
        {
            if (string.IsNullOrEmpty(key))
                return 0;

            string k = key.ToLowerInvariant();

            switch (k)
            {
                case "enter":
                case "return":
                    return 0x0D;

                case "tab":
                    return 0x09;

                case "backspace":
                    return 0x08;

                case "delete":
                case "del":
                    return 0x2E;

                case "escape":
                case "esc":
                    return 0x1B;

                case "space":
                    return 0x20;

                case "arrowup":
                case "up":
                    return 0x26;

                case "arrowdown":
                case "down":
                    return 0x28;

                case "arrowleft":
                case "left":
                    return 0x25;

                case "arrowright":
                case "right":
                    return 0x27;

                case "home":
                    return 0x24;

                case "end":
                    return 0x23;

                case "pageup":
                    return 0x21;

                case "pagedown":
                    return 0x22;

                case "insert":
                    return 0x2D;

                case "f1": return 0x70;
                case "f2": return 0x71;
                case "f3": return 0x72;
                case "f4": return 0x73;
                case "f5": return 0x74;
                case "f6": return 0x75;
                case "f7": return 0x76;
                case "f8": return 0x77;
                case "f9": return 0x78;
                case "f10": return 0x79;
                case "f11": return 0x7A;
                case "f12": return 0x7B;

                default:
                    return 0;   // letters, numbers, symbols etc.
            }
        }

        // Implementation for "send-string"
        // NOTE: same as fillin
        /// <summary>
        /// Sends the given text at the current focus/cursor position. With <paramref name="delayMs"/> equal to 0
        /// (the default), the whole string is inserted atomically via CDP's <c>Input.insertText</c>. When
        /// <paramref name="delayMs"/> is greater than 0, the text is dispatched character-by-character via
        /// <see cref="DispatchTextCharByChar"/>, sleeping <paramref name="delayMs"/> milliseconds between characters.
        /// Implementation for the "send-string" workflow action (equivalent to "fill-in").
        /// </summary>
        /// <param name="text">Text to insert.</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. 0 sends the whole string atomically.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous text insertion operation.</returns>
        public async Task SendString(string text, int delayMs = 0, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            if (0 == delayMs)
                await SendCommand<object>(DevToolsMethods.InputInsertText, new { text }, sessionId).ConfigureAwait(false);
            else
                await DispatchTextCharByChar(text, delayMs, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispatches the given text one character at a time via CDP's <c>Input.dispatchKeyEvent</c>
        /// (keyDown followed by keyUp for each character), sleeping a random duration between
        /// Math.Min(25, <paramref name="delayMs"/>) and <paramref name="delayMs"/> milliseconds between
        /// characters. Shared by <see cref="SendString"/> and <see cref="TryTypeWithKeyEvents"/>.
        /// </summary>
        /// <param name="text">Text to type, character by character.</param>
        /// <param name="delayMs">Ceiling in milliseconds for the randomized inter-character delay.</param>
        /// <param name="sessionId">CDP session id to dispatch the key events on.</param>
        private async Task DispatchTextCharByChar(string text, int delayMs, string sessionId)
        {
            bool first = true;
            foreach (char c in text)
            {
                if (false == first && 0 < delayMs)
                    await Task.Delay(HardwareHelper.GetTypingDelay(delayMs)).ConfigureAwait(false);

                string key = c.ToString();
                int vkCode = (int)c; // Basic; improve with real VK map for specials/shifts

                await SendCommand(DevToolsMethods.InputDispatchKeyEvent, new
                {
                    type = "keyDown",
                    text = key,                // for printable chars
                    unmodifiedtext = key,
                    key = key,
                    code = GetKeyCode(c),      // e.g. "KeyA" for 'a'/'A'
                    windowsVirtualKeyCode = vkCode,
                    nativeVirtualKeyCode = vkCode
                }, sessionId).ConfigureAwait(false);

                await SendCommand(DevToolsMethods.InputDispatchKeyEvent, new
                {
                    type = "keyUp",
                    key = key,
                    code = GetKeyCode(c),
                    windowsVirtualKeyCode = vkCode
                }, sessionId).ConfigureAwait(false);

                first = false;
            }
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

        // Implementation for "set-download-filename"
        /// <summary>
        /// Configures the page-level download behavior so that future downloads are saved to the given path.
        /// Implementation for the "set-download-filename" workflow action.
        /// </summary>
        /// <param name="downloadPath">Directory path where downloads should be saved.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous configuration operation.</returns>
        public async Task SetDownloadFilename(string downloadPath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await SendCommand<object>(DevToolsMethods.PageSetDownloadBehavior, new { behavior = "allow", downloadPath }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "scroll-element"
        /// <summary>
        /// Sets the value of the element matching the given CSS selector and dispatches "input" and "change"
        /// events so the page reacts to the new value (e.g. for range/slider inputs).
        /// Implementation for the "set-range" workflow action.
        /// </summary>
        /// <param name="elementId">CSS selector identifying the target element.</param>
        /// <param name="rangeValue">New value to assign (inserted directly into the generated JavaScript expression).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result of the JavaScript evaluation.</returns>
        public async Task<dynamic> SetRange(string elementId, string rangeValue, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new
                {
                    contextId = CurrentContextId,
                    expression = $@"var s = document.querySelector('{elementId}'); s.value = {rangeValue}; s.dispatchEvent(new Event('input', {{bubbles: true}})); s.dispatchEvent(new Event('change', {{bubbles: true}}));"
                }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "top-browser"
        /// <summary>
        /// Brings the current page/tab to the front of the operating system's window stack.
        /// Implementation for the "top-browser" workflow action.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous bring-to-front operation.</returns>
        public async Task TopBrowser(string sessionId = null)
        {
            // Page.bringToFront is top-level only, so this must never go to an iframe session - once we are inside
            // a cross-origin frame the effective session is that frame's and CDP rejects the command outright.
            // sessionId is deliberately ignored: any session that is not the top-level page target is wrong here
            await SendCommand<object>(DevToolsMethods.PageBringToFront, new { }, GetCurrentSessionId()).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a value in browser storage. Required parameters must be supplied.
        /// Returns "success" if write completed, null if failed.
        /// </summary>
        /// <param name="storageType">Storage type: "cookie", "localStorage", "sessionStorage", "indexeddb", "cache"</param>
        /// <param name="sessionId">CDP session id; current session used if null</param>
        /// <param name="domain">Domain/origin (required)</param>
        /// <param name="key">Key or cache name (required)</param>
        /// <param name="data">Value to store (required)</param>
        /// <param name="storeName">Optional object store (IndexedDB, required for IndexedDB)</param>
        /// <param name="path">Optional path (cookies, default "/")</param>
        /// <returns>"success" or null if failed</returns>
        public async Task<bool> SetStorage(
            string storageType,
            string sessionId,
            string domain,
            string key,
            string data,
            string storeName = null,
            string path = null)
        {
            bool retVal = false;
            sessionId ??= GetEffectiveSessionId();

            try
            {
                switch (storageType)
                {
                    case "cookie":
                        {
                            IDictionary<string, object> cookieToSet;

                            path ??= "/";
                            domain ??= ""; // or handle wildcard logic

                            // 1. Get current cookies via CDP
                            dynamic cookiesResp = await GetStorage(storageType, sessionId, domain, storeName, path, key).ConfigureAwait(false);

                            // 2. Find the existing cookie (exact match on name + domain + path)
                            dynamic existing = null;

                            if (cookiesResp != null)
                            {
                                var cookies = JsonConvert.DeserializeObject<List<dynamic>>(cookiesResp) ?? new List<dynamic>();
                                foreach (dynamic c in cookies)
                                {
                                    string cDomain = ((dynamic)c).Domain ?? "";
                                    string cPath = ((dynamic)c).Path ?? "/";
                                    string cName = ((dynamic)c).Key ?? "";

                                    if (cName == key &&
                                        UrlHelper.CookieDomainMatches(cDomain, domain) &&
                                        cPath == path)
                                    {
                                        existing = c;

                                        cookieToSet = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;

                                        cookieToSet["name"] = key;
                                        cookieToSet["value"] = data;
                                        cookieToSet["domain"] = ((dynamic)existing)?.Domain?.ToString();
                                        cookieToSet["path"] = path;

                                        cookieToSet["expires"] = ((dynamic)existing)?.Expires != null
                                                                 ? (double)existing.Expires
                                                                 : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (180L * 24 * 60 * 60); // ~6 months

                                        cookieToSet["secure"] = ((dynamic)existing)?.Secure ?? true;
                                        cookieToSet["httpOnly"] = ((dynamic)existing)?.HttpOnly ?? true;
                                        cookieToSet["sameSite"] = ((dynamic)existing)?.SameSite?.ToString() ?? "None";

                                        cookieToSet["session"] = ((dynamic)existing)?.Session ?? false;
                                        cookieToSet["priority"] = ((dynamic)existing)?.Priority?.ToString() ?? "Medium";

                                        cookieToSet["sourceScheme"] = ((dynamic)existing)?.SourceScheme?.ToString() ?? "Secure";
                                        cookieToSet["sourcePort"] = ((dynamic)existing)?.SourcePort ?? 443;

                                        // Only add partitionKey if it actually exists and is not null
                                        if (((dynamic)existing)?.PartitionKey != null)
                                        {
                                            cookieToSet["partitionKey"] = existing.PartitionKey;   // keep the original object
                                        }

                                        // 4. Set the cookie
                                        await SendCommand<dynamic>(DevToolsMethods.StorageSetCookies,
                                            new { cookies = new[] { cookieToSet } },
                                            sessionId).ConfigureAwait(false);
                                    }
                                }
                            }
                            else
                            {
                                // 3. Build the cookie to set (merge existing + supplied values)
                                cookieToSet = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;

                                cookieToSet["name"] = key;
                                cookieToSet["value"] = data;
                                cookieToSet["domain"] = ((dynamic)existing)?.Domain?.ToString();
                                cookieToSet["path"] = path;

                                cookieToSet["expires"] = ((dynamic)existing)?.Expires != null
                                                         ? (double)existing.Expires
                                                         : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (180L * 24 * 60 * 60); // ~6 months

                                cookieToSet["secure"] = ((dynamic)existing)?.Secure ?? true;
                                cookieToSet["httpOnly"] = ((dynamic)existing)?.HttpOnly ?? true;
                                cookieToSet["sameSite"] = ((dynamic)existing)?.SameSite?.ToString() ?? "None";

                                cookieToSet["session"] = ((dynamic)existing)?.Session ?? false;
                                cookieToSet["priority"] = ((dynamic)existing)?.Priority?.ToString() ?? "Medium";

                                cookieToSet["sourceScheme"] = ((dynamic)existing)?.SourceScheme?.ToString() ?? "Secure";
                                cookieToSet["sourcePort"] = ((dynamic)existing)?.SourcePort ?? 443;

                                // Only add partitionKey if it actually exists and is not null
                                if (((dynamic)existing)?.PartitionKey != null)
                                {
                                    cookieToSet["partitionKey"] = existing.PartitionKey;   // keep the original object
                                }

                                // 4. Set the cookie
                                await SendCommand<dynamic>(DevToolsMethods.StorageSetCookies,
                                    new { cookies = new[] { cookieToSet } },
                                    sessionId).ConfigureAwait(false);
                            }

                            // Optional: return the cookie we just set (or the old list)
                            //resultJson = JsonConvert.SerializeObject(new[] { cookieToSet });
                            retVal = true;
                            break;
                        }

                    case "localStorage":
                    case "sessionStorage":
                        string jsStorage = $@"{storageType}.setItem('{EscapeJsString(key)}','{EscapeJsString(data)}');";
                        await SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, 
                            new { 
                                expression = jsStorage, 
                                awaitPromise = false,
                                contextId = CurrentContextId,
                                returnByValue = true 
                            }, sessionId).ConfigureAwait(false);
                        retVal = true;
                        break;

                    case "indexedDb":
                        // Wrap the script in a Promise for awaitPromise. Runtime.evaluate passes no callback like
                        // Selenium's executeAsyncScript.
                        string js = $@"
                                    new Promise((callback) => {{

                                    (function() {{
                                        const dbName = '{EscapeJsString(path)}';
                                        const storeName = '{EscapeJsString(storeName)}';
                                        const inputKey = '{EscapeJsString(key)}';
                                        let rawValue = '{EscapeJsString(data)}';

                                        function resolve(val) {{
                                            callback(val);
                                        }}

                                        function fail(msg, err, db) {{
                                            console.error(msg, err?.name || err || '');
                                            try {{ db?.close(); }} catch {{ }}
                                            resolve(false);
                                        }}

                                        function parseValue(v) {{
                                            try {{
                                                return JSON.parse(v);
                                            }} catch {{
                                                return v;
                                            }}
                                        }}

                                        function isValidKey(k) {{
                                            if (k === undefined) return false;
                                            if (typeof k === 'string' || typeof k === 'number') return true;
                                            if (k instanceof Date) return true;
                                            if (Array.isArray(k)) return true;
                                            return false;
                                        }}

                                        function ensureInlineKey(value, keyPath, key, autoIncrement) {{
                                            if (keyPath === null) return {{ value, usedKey: key }};

                                            if (typeof value !== 'object' || value === null) {{
                                                if (keyPath && key !== undefined) {{
                                                    const wrapped = {{}};

                                                    if (typeof keyPath === 'string') {{
                                                        wrapped[keyPath] = key;
                                                    }} else if (Array.isArray(keyPath)) {{
                                                        if (!Array.isArray(key)) {{
                                                            throw new Error('Compound key requires array key');
                                                        }}
                                                        for (let i = 0; i < keyPath.length; i++) {{
                                                            wrapped[keyPath[i]] = key[i];
                                                        }}
                                                    }}

                                                    wrapped.value = value;
                                                    return {{ value: wrapped, usedKey: undefined }};
                                                }}

                                                if (autoIncrement) {{
                                                    return {{ value, usedKey: undefined }};
                                                }}

                                                throw new Error('Inline keyPath requires object value');
                                            }}

                                            if (typeof keyPath === 'string') {{
                                                if (!(keyPath in value)) {{
                                                    if (key !== undefined) {{
                                                        value[keyPath] = key;
                                                    }} else if (!autoIncrement) {{
                                                        throw new Error('Missing required inline keyPath property');
                                                    }}
                                                }}
                                            }} else if (Array.isArray(keyPath)) {{
                                                for (let i = 0; i < keyPath.length; i++) {{
                                                    const kp = keyPath[i];
                                                    if (!(kp in value)) {{
                                                        if (Array.isArray(key) && key[i] !== undefined) {{
                                                            value[kp] = key[i];
                                                        }} else if (!autoIncrement) {{
                                                            throw new Error('Missing compound keyPath property: ' + kp);
                                                        }}
                                                    }}
                                                }}
                                            }}

                                            return {{ value, usedKey: undefined }};
                                        }}

                                        const openReq = indexedDB.open(dbName);

                                        openReq.onerror = () => {{
                                            fail('Failed to open database', openReq.error);
                                        }};

                                        openReq.onsuccess = () => {{
                                            const db = openReq.result;

                                            let tx;
                                            try {{
                                                tx = db.transaction(storeName, 'readwrite');
                                            }} catch (e) {{
                                                fail('Transaction creation failed', e, db);
                                                return;
                                            }}

                                            const store = tx.objectStore(storeName);

                                            let value = parseValue(rawValue);
                                            let key = inputKey;

                                            if (key === '') key = undefined;

                                            if (key !== undefined && !isValidKey(key)) {{
                                                fail('Invalid key type', key, db);
                                                return;
                                            }}

                                            let finalValue, finalKey;

                                            try {{
                                                const result = ensureInlineKey(
                                                    value,
                                                    store.keyPath,
                                                    key,
                                                    store.autoIncrement
                                                );

                                                finalValue = result.value;
                                                finalKey = result.usedKey;
                                            }} catch (e) {{
                                                fail('Key handling failed', e, db);
                                                return;
                                            }}

                                            let putReq;

                                            try {{
                                                if (store.keyPath === null) {{
                                                    if (finalKey === undefined && !store.autoIncrement) {{
                                                        fail('Missing key for out-of-line store', null, db);
                                                        return;
                                                    }}
                                                    putReq = store.put(finalValue, finalKey);
                                                }} else {{
                                                    putReq = store.put(finalValue);
                                                }}
                                            }} catch (e) {{
                                                fail('Put setup error', e, db);
                                                return;
                                            }}

                                            // wait for transaction complete (not just put)
                                            tx.oncomplete = () => {{
                                                try {{ db.close(); }} catch {{ }}
                                                resolve(true);
                                            }};

                                            tx.onerror = (event) => {{
                                                fail('Transaction error', event.target.error, db);
                                            }};

                                            tx.onabort = (event) => {{
                                                fail('Transaction aborted', event.target.error, db);
                                            }};
                                        }};
                                    }})();

                                    }});
                                    ";

                        string indexedDbSaid = SendCommand<dynamic>(DevToolsMethods.RuntimeEvaluate, new
                        {
                            expression = js,
                            awaitPromise = true,
                            contextId = CurrentContextId,
                            returnByValue = true
                        }, sessionId).GetAwaiter().GetResult()?.ToString();

                        retVal = true == indexedDbSaid?.Contains("true");

                        // On failure, log the script's reply; it says why.
                        if (false == retVal)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                $"IndexedDB [{path}] store [{storeName}] did not take key [{key}], the page answered [{indexedDbSaid}]",
                                null, GPALObjectType.None);

                        break;

                    case "cache":
                        string cacheName = EscapeJsString(storeName);
                        string requestUrl = EscapeJsString(key);   // Key = the cache key (URL)

                        js = $@"
                                (async () => {{
                                    try {{
                                        const cache = await caches.open('{cacheName}');
                
                                        const request = new Request('{requestUrl}', {{
                                            method: 'GET'
                                        }});

                                        const response = new Response('{EscapeJsString(data)}', {{
                                            status: 200,
                                            statusText: 'OK',
                                            headers: {{
                                                'Content-Type': 'text/plain;charset=utf-8',
                                                'Cache-Control': 'max-age=31536000'   // or make this configurable
                                            }}
                                        }});

                                        await cache.put(request, response);
                                        return {{ success: true, cacheName: '{cacheName}', key: '{requestUrl}' }};
                                    }} catch (err) {{
                                        console.error('Cache set failed:', err);
                                        return {{ success: false, error: err.message }};
                                    }}
                                }})();
                            ";

                        var evalResult = await SendCommand<dynamic>(
                            DevToolsMethods.RuntimeEvaluate,
                            new
                            {
                                expression = js,
                                awaitPromise = true,
                                contextId = CurrentContextId,
                                returnByValue = true
                            },
                            sessionId).ConfigureAwait(false);

                        // Check result
                        retVal = evalResult?.result?.value?.success == true;
                        break;

                    default:
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Unsupported storage type: [{storageType}]",
                            this,
                            GPALObjectType.PuppeteerCommunicator);
                        break;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"SetStorage failed for [{storageType}] domain=[{domain}] storeName=[{storeName}] path=[{path}] key=[{key}]",
                    this,
                    GPALObjectType.PuppeteerCommunicator,
                    ex);
            }

            return retVal;
        }

        // Implementation for "set-value-from-element"
        /// <summary>
        /// Copies the value from a source element into a destination element by setting
        /// <c>destElement.value = srcElement.value</c> via JavaScript evaluation, then dispatches
        /// "input" and "change" events on the destination so the page reacts to the new value.
        /// Implementation for the "set-value-from-element" workflow action.
        /// </summary>
        /// <param name="srcCss">CSS selector identifying the source element whose value is read.</param>
        /// <param name="destCss">CSS selector identifying the destination element whose value is set.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task that resolves to the raw CDP result of the JavaScript evaluation.</returns>
        public async Task<dynamic> SetValueFromElement(string srcCss, string destCss, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new
                {
                    contextId = CurrentContextId,
                    expression = $@"var s = document.querySelector('{srcCss}'); var d = document.querySelector('{destCss}'); d.value = s.value; d.dispatchEvent(new Event('input', {{bubbles: true}})); d.dispatchEvent(new Event('change', {{bubbles: true}}));"
                }, sessionId).ConfigureAwait(false);
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
        /// Switches the active tab/window to the one identified by a tab index or page URL, activating the corresponding
        /// CDP target and updating the active window/tab indices.
        /// </summary>
        /// <param name="tabIdOrUrl">Either an <see cref="int"/> index into the current tab queue, or a <see cref="string"/> URL to search for among open tabs.</param>
        /// <param name="sessionId">Session ID used when looking up the target by URL, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the targetId of the activated tab, or null if no matching target was found.</returns>
        public async Task<string> GoToWindow(object tabIdOrUrl, string sessionId = null)
        {
            string targetId = null;

            if (tabIdOrUrl is int tabId)
            {
                var queueArray = CurrentSessions.ToArray();
                if (tabId >= 0 && tabId < queueArray.Length)
                {
                    targetId = queueArray[tabId].Key;
                    // Update active tab index
                    SetActiveTabIndex(tabId);
                }
            }
            else if (tabIdOrUrl is string url && !string.IsNullOrWhiteSpace(url))
            {
                targetId = await GetTargetTabIdByUrl(url, sessionId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(targetId))
                {
                    // Find and set active window and tab
                    int windowIndex = GetCurrentWindowIndex(targetId);
                    if (windowIndex >= 0)
                    {
                        SetActiveWindowIndex(windowIndex);
                        var windowArray = _windowSessionsQueue.ToArray();
                        var tabQueue = windowArray[windowIndex].TabQueue.ToArray();
                        int tabIndex = Array.FindIndex(tabQueue, kvp => kvp.Key == targetId);
                        if (tabIndex >= 0)
                        {
                            SetActiveTabIndex(tabIndex);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(targetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No target found for tabIdOrUrl [{tabIdOrUrl}]", this, GPALObjectType.PuppeteerCommunicator);
                return null;
            }

            await SendCommand<object>(DevToolsMethods.TargetActivateTarget, new { targetId }, null).ConfigureAwait(false);
            ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;
            return targetId;
        }
        // Implementation for "window-inner-height"
        /// <summary>
        /// Gets the height of the browser window's viewport (<c>window.innerHeight</c>), in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the viewport height in pixels.</returns>
        public async Task<int> WindowInnerHeight(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.innerHeight",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
        }

        // Implementation for "window-inner-width"
        /// <summary>
        /// Gets the width of the browser window's viewport (<c>window.innerWidth</c>), in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the viewport width in pixels.</returns>
        public async Task<int> WindowInnerWidth(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.innerWidth",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
        }

        // Implementation for "window-outer-height"
        /// <summary>
        /// Gets the outer height of the browser window (<c>window.outerHeight</c>), including browser chrome, in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the window's outer height in pixels.</returns>
        public async Task<int> WindowOuterHeight(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.outerHeight",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
        }

        // Implementation for "window-outer-width"
        /// <summary>
        /// Gets the outer width of the browser window (<c>window.outerWidth</c>), including browser chrome, in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the window's outer width in pixels.</returns>
        public async Task<int> WindowOuterWidth(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.outerWidth",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
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

        // Implementation for "window-page-xoffset"
        /// <summary>
        /// Gets the horizontal scroll offset of the page (<c>window.pageXOffset</c>), in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the current horizontal scroll offset in pixels.</returns>
        public async Task<int> WindowPageOffsetX(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.pageXOffset",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
        }

        // Implementation for "window-page-yoffset"
        /// <summary>
        /// Gets the vertical scroll offset of the page (<c>window.pageYOffset</c>), in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the current vertical scroll offset in pixels.</returns>
        public async Task<int> WindowPageOffsetY(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.pageYOffset",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
        }

        // Implementation for "window-screen-left"
        /// <summary>
        /// Gets the horizontal position of the browser window relative to the screen (<c>window.screenLeft</c>), in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the window's left position on screen, in pixels.</returns>
        public async Task<int> WindowScreenLeft(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.screenLeft",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
        }

        // Implementation for "window-screen-top"
        /// <summary>
        /// Gets the vertical position of the browser window relative to the screen (<c>window.screenTop</c>), in pixels.
        /// </summary>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to the window's top position on screen, in pixels.</returns>
        public async Task<int> WindowScreenTop(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            var result = await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new { 
                    expression = "window.screenTop",
                    contextId = CurrentContextId,
                    returnByValue = true
                }, sessionId).ConfigureAwait(false);
            return (int)result.value;
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
        // get the json protocol to dynamically build a list of extraction variable for sendcommand to return useful data
        /// <summary>
        /// Fetches the full Chrome DevTools Protocol JSON definition from the browser's <c>/json/protocol</c> endpoint,
        /// used to dynamically build a list of extraction variables for <c>SendCommand</c> to return useful data.
        /// </summary>
        /// <returns>The CDP protocol definition as a formatted JSON string.</returns>
        internal string GetProtocolJSON()
        {
            // A pipe connection has no url. The browser also opens a port of its own choosing (--remote-debugging-port=0)
            // and writes it on the first line of DevToolsActivePort in the profile folder.
            string protocolUrl = _puppeteerUrl ?? $"http://localhost:{System.IO.File.ReadLines(System.IO.Path.Combine(Browser.BrowserSettings.ProfileDataDirectory, "DevToolsActivePort")).First()}";

            using var http = new System.Net.Http.HttpClient();
            var json = http.GetStringAsync($"{protocolUrl}/json/protocol").Result;
            return Newtonsoft.Json.Linq.JObject.Parse(json).ToString();
        }
        /// <summary>
        /// Gets the browser context ID (window ID) of the currently active window.
        /// </summary>
        /// <returns>The browser context ID of the active window, or null if there is no active window.</returns>
        internal string GetActiveWindowId()
        {
            var windows = _windowSessionsQueue.ToArray();
            if (_activeWindowIndex < 0 || _activeWindowIndex >= windows.Length)
                return null;
            return windows[_activeWindowIndex].BrowserContextId;
        }
        // Get active window's tab queue (embedded in windows queue)
        /// <summary>
        /// Gets the tab queue belonging to the currently active window.
        /// </summary>
        /// <returns>The active window's tab queue (target ID to session ID pairs), or null if there is no active window.</returns>
        private ConcurrentQueue<KeyValuePair<string, string>> GetActiveWindowTabQueue()
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length) return null;
            return windowArray[_activeWindowIndex].TabQueue;
        }

        /// <summary>
        /// Gets the session ID that should be used for the current operation, preferring the current iframe session
        /// (if inside an iframe) and otherwise falling back to the active tab's session.
        /// </summary>
        /// <returns>The effective session ID to use for CDP commands.</returns>
        public string GetEffectiveSessionId()
        {
            return !string.IsNullOrEmpty(CurrentFrameSessionId)
                ? CurrentFrameSessionId
                : GetCurrentSessionId();
        }
        
        // Get current sessionId (active window -> active tab)
        /// <summary>
        /// Gets the CDP session ID for the active tab of the active window, attaching to the target
        /// (via <c>Target.attachToTarget</c>) and caching the resulting session ID if one is not yet known.
        /// </summary>
        /// <returns>The session ID for the active tab, or null if there is no active window or active tab.</returns>
        /// <summary>
        /// Forgets the session id held for the active tab so the next <see cref="GetCurrentSessionId"/> attaches a
        /// fresh one.<br/>
        /// A session id dies when its target is replaced (cross-origin navigation, crashed renderer, swapped tab).
        /// GetCurrentSessionId attaches only when the id is null, so clearing it forces a reattach.
        /// </summary>
        internal void ClearCurrentSessionId()
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (0 == windowArray.Length || _activeWindowIndex >= windowArray.Length)
                return;

            var window = windowArray[_activeWindowIndex];
            var tabArray = window.TabQueue.ToArray();
            if (0 == tabArray.Length || window.ActiveTabIndex >= tabArray.Length)
                return;

            string targetId = tabArray[window.ActiveTabIndex].Key;
            var newQueue = new ConcurrentQueue<KeyValuePair<string, string>>();

            foreach (var kvp in tabArray)
                newQueue.Enqueue(kvp.Key == targetId ? new KeyValuePair<string, string>(kvp.Key, null) : kvp);

            window.TabQueue = newQueue;
        }
        public string GetCurrentSessionId()
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length) return null;

            var window = windowArray[_activeWindowIndex];
            var tabQueue = window.TabQueue;
            var tabArray = tabQueue.ToArray();
            var activeTabIndex = window.ActiveTabIndex;
            if (tabArray.Length == 0 || activeTabIndex >= tabArray.Length) return null;

            var targetId = tabArray[activeTabIndex].Key;
            var sessionId = tabArray[activeTabIndex].Value;

            if (sessionId == null)
            {
                var attachResult = SendCommand<dynamic>(DevToolsMethods.TargetAttachToTarget, new
                {
                    targetId,
                    flatten = true
                }, null).GetAwaiter().GetResult();

                string newSessionId = attachResult.sessionId;

                // rebuild queue with updated value
                var newQueue = new ConcurrentQueue<KeyValuePair<string, string>>();
                foreach (var kvp in tabArray)
                    newQueue.Enqueue(kvp.Key == targetId
                        ? new KeyValuePair<string, string>(kvp.Key, newSessionId)
                        : kvp);

                window.TabQueue = newQueue;
                sessionId = newSessionId;

                PrepareSession(sessionId).GetAwaiter().GetResult();
            }

            return sessionId;
        }

        // Get current targetId (active window -> active tab)
        /// <summary>
        /// Gets the CDP target ID of the active tab in the active window.
        /// </summary>
        /// <returns>The target ID of the active tab, or null if there is no active window or active tab.</returns>
        public string GetCurrentTargetId()
        {
            var windowArray = _windowSessionsQueue.ToArray();
            if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length) return null;
            var tabQueue = windowArray[_activeWindowIndex].TabQueue;
            var tabArray = tabQueue.ToArray();
            var activeTabIndex = windowArray[_activeWindowIndex].ActiveTabIndex;
            if (tabArray.Length == 0 || activeTabIndex >= tabArray.Length) return null;
            return tabArray[activeTabIndex].Key;
        }
        // Get current window targetId (active window)
        /// <summary>
        /// Gets the browser context ID (window ID) of the active window.
        /// </summary>
        /// <returns>The browser context ID of the active window.</returns>
        public string GetCurrentWindowTargetId()
        {
            var windowArray = _windowSessionsQueue.ToArray();

            return windowArray[_activeWindowIndex].BrowserContextId;
        }
        // Add tab to active window's embedded tab queue, set as active tab
        /// <summary>
        /// Adds a tab (CDP target) to the active window's tab queue, creating a new window entry if no window
        /// is currently active, and optionally marks the new tab as the active tab.
        /// </summary>
        /// <param name="targetId">The CDP target ID of the tab/page to add.</param>
        /// <param name="sessionId">The CDP session ID associated with the target, if already known.</param>
        /// <param name="isActive">If true, the newly added tab becomes the active tab. Defaults to true.</param>
        /// <param name="doNotGetSendSemaphore">If true, skips acquiring the send semaphore when querying targets (used to avoid deadlocks during initialization). Defaults to false.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        internal async Task AddTabToQueue(string targetId, string sessionId, bool isActive = true, bool doNotGetSendSemaphore = false)
        {
            dynamic target = null;

            // A tab Chrome auto-attached already has a session with the scripts on it
            if (null == sessionId && true == _autoSessions.TryGetValue(targetId, out var autoSessionId))
                sessionId = autoSessionId;

            // The CDP lookup is awaited outside the lock; the lock is taken once it has answered.
            if (0 == _windowSessionsQueue.Count || _activeWindowIndex >= _windowSessionsQueue.Count)
            {
                // TODO: this needs to be pushed onto a queue, then the next time send
                var targets = await SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null, 3, doNotGetSendSemaphore).ConfigureAwait(false);
                target = ((IEnumerable<dynamic>)targets.targetInfos)
                    .FirstOrDefault(t => t.targetId == targetId && t.type == "page");
            }

            lock (_tabQueueLock)
            {
                var windowArray = _windowSessionsQueue.ToArray();
                if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
                {
                    if (target == null)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Target ID [{targetId}] not found in CDP", this, GPALObjectType.PuppeteerCommunicator);
                        return;
                    }

                    string browserContextId = target.browserContextId?.ToString() ?? "";

                    var newTabQueue = new ConcurrentQueue<KeyValuePair<string, string>>();
                    newTabQueue.Enqueue(new KeyValuePair<string, string>(targetId, sessionId));
                    _windowSessionsQueue.Enqueue(new WindowSession(browserContextId, newTabQueue, 0));
                    _activeWindowIndex = 0;
                    ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Created window with browserContextId [{browserContextId}], added tab ID [{targetId}], Active window [0], tab [0]", this, GPALObjectType.PuppeteerCommunicator);
                    return;
                }

                var tabQueue = windowArray[_activeWindowIndex].TabQueue;
                var tabList = tabQueue.ToList();
                int existing = tabList.FindIndex(kvp => kvp.Key == targetId);

                // A tab already listed is not added again; a session id it lacked is filled in.
                if (-1 != existing)
                {
                    if (null != sessionId && null == tabList[existing].Value)
                    {
                        tabList[existing] = new KeyValuePair<string, string>(targetId, sessionId);
                        tabQueue = new ConcurrentQueue<KeyValuePair<string, string>>(tabList);
                    }
                }
                else
                    tabQueue.Enqueue(new KeyValuePair<string, string>(targetId, sessionId));

                var newTabIdx = -1 != existing ? existing : tabQueue.Count - 1;

                // Update ActiveTabIndex in the WindowSession
                var windowList = _windowSessionsQueue.ToList();
                windowList[_activeWindowIndex] = new WindowSession(
                    windowList[_activeWindowIndex].BrowserContextId,
                    tabQueue,
                    true == isActive ? newTabIdx : windowList[_activeWindowIndex].ActiveTabIndex   // we do not know if the new tab is active, just that it is being added
                );
                _windowSessionsQueue = new ConcurrentQueue<WindowSession>();
                foreach (var w in windowList)
                {
                    _windowSessionsQueue.Enqueue(w);
                }

                if (true == isActive)
                    ((PuppeteerClient)PuppeteerClient)._currentTargetId = targetId;

                // Log the added and active tab indexes separately; they differ unless the tab was added as active.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"{(-1 != existing ? "Tab already in" : "Added tab to")} active window at index [{newTabIdx}], ID [{targetId}], Tab queue size [{tabQueue.Count}], active tab is now index [{windowList[_activeWindowIndex].ActiveTabIndex}]", this, GPALObjectType.PuppeteerCommunicator);
            }
        }        // Set active tab index in active window's embedded tab queue
        /// <summary>
        /// Sets the active tab index within the active window's tab queue, wrapping the index into range,
        /// and updates the current target ID accordingly.
        /// </summary>
        /// <param name="index">The desired tab index, wrapped (modulo) into the valid range of the tab queue.</param>
        internal void SetActiveTabIndex(int index)
        {
            lock (_tabQueueLock)
            {
                var windowArray = _windowSessionsQueue.ToArray();
                if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No active window for SetActiveTabIndex", this, GPALObjectType.PuppeteerCommunicator);
                    return;
                }
                var tabQueue = windowArray[_activeWindowIndex].TabQueue;
                var tabArray = tabQueue.ToArray();
                index = index % tabArray.Length;
                if (index < 0) index += tabArray.Length;

                // Update ActiveTabIndex in the WindowSession
                var windowList = _windowSessionsQueue.ToList();
                windowList[_activeWindowIndex] = new WindowSession(
                    windowList[_activeWindowIndex].BrowserContextId,
                    windowList[_activeWindowIndex].TabQueue,
                    index
                );
                _windowSessionsQueue = new ConcurrentQueue<WindowSession>();
                foreach (var w in windowList)
                {
                    _windowSessionsQueue.Enqueue(w);
                }

                ((PuppeteerClient)PuppeteerClient)._currentTargetId = tabArray[index].Key;
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Set active tab index [{index}] in active window, ID [{tabArray[index].Key}]", this, GPALObjectType.PuppeteerCommunicator);
            }
        }
        // Remove tab from active window's embedded tab queue by targetId
        /// <summary>
        /// Removes a tab from the active window's tab queue by target ID, adjusting the active tab index
        /// and current target ID as needed.
        /// </summary>
        /// <param name="targetId">The CDP target ID of the tab to remove.</param>
        /// <returns>True if the tab was found and removed; false if there is no active window or the tab was not found.</returns>
        internal bool RemoveTabFromQueue(string targetId)
        {
            lock (_tabQueueLock)
            {
                var windowArray = _windowSessionsQueue.ToArray();
                if (windowArray.Length == 0 || _activeWindowIndex >= windowArray.Length)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No active window for RemoveTabFromQueue [{targetId}]", this, GPALObjectType.PuppeteerCommunicator);
                    return false;
                }

                var tabQueue = windowArray[_activeWindowIndex].TabQueue;
                var queueList = tabQueue.ToList();
                var indexToRemove = queueList.FindIndex(kvp => kvp.Key == targetId);
                // Already gone: the browser's own close event and GPAL's close both remove a closed tab
                if (indexToRemove == -1)
                    return false;

                queueList.RemoveAt(indexToRemove);
                var newTabQueue = new ConcurrentQueue<KeyValuePair<string, string>>();
                foreach (var kvp in queueList)
                {
                    newTabQueue.Enqueue(kvp);
                }

                // Update ActiveTabIndex in WindowSession
                var newActiveTabIndex = windowArray[_activeWindowIndex].ActiveTabIndex;
                if (newTabQueue.IsEmpty)
                {
                    newActiveTabIndex = 0;
                }
                else if (newActiveTabIndex > indexToRemove)
                {
                    newActiveTabIndex--;
                }
                else if (newActiveTabIndex == indexToRemove)
                {
                    newActiveTabIndex = Math.Max(0, newActiveTabIndex - 1);
                }

                var windowList = _windowSessionsQueue.ToList();
                windowList[_activeWindowIndex] = new WindowSession(
                    windowList[_activeWindowIndex].BrowserContextId,
                    newTabQueue,
                    newActiveTabIndex
                );
                _windowSessionsQueue = new ConcurrentQueue<WindowSession>();
                foreach (var w in windowList)
                {
                    _windowSessionsQueue.Enqueue(w);
                }

                ((PuppeteerClient)PuppeteerClient)._currentTargetId = newTabQueue.IsEmpty ? null : newTabQueue.ToArray()[newActiveTabIndex].Key;
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Removed tab ID [{targetId}] from active window, Active tab index now [{newActiveTabIndex}], Tab queue size [{newTabQueue.Count}]", this, GPALObjectType.PuppeteerCommunicator);
                return true;
            }
        }

        /// <summary>
        /// Finds the index of the window whose tab queue contains the given target ID.
        /// </summary>
        /// <param name="targetId">The CDP target ID to search for.</param>
        /// <returns>The zero-based index of the window containing the target, or -1 if not found.</returns>
        public int GetCurrentWindowIndex(string targetId)
        {
            var windows = _windowSessionsQueue.ToArray(); // assuming you track sessions per window
            for (int i = 0; i < windows.Length; i++)
            {
                foreach (var tab in windows[i].TabQueue)
                    if (tab.Key == targetId)
                        return i;
            }
            return -1;
        }

        /// <summary>
        /// Sets the active window index, wrapping the index into range, and updates the current target ID
        /// to the active tab of the newly active window.
        /// </summary>
        /// <param name="index">The desired window index, wrapped (modulo) into the valid range of the window queue.</param>
        internal void SetActiveWindowIndex(int index)
        {
            lock (_tabQueueLock)
            {
                var windowArray = _windowSessionsQueue.ToArray();
                if (windowArray.Length == 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No windows for SetActiveWindowIndex", this, GPALObjectType.PuppeteerCommunicator);
                    return;
                }
                index = index % windowArray.Length;
                if (index < 0) index += windowArray.Length;
                _activeWindowIndex = index;

                ((PuppeteerClient)PuppeteerClient)._currentTargetId = GetCurrentTargetId();
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Set active window index [{_activeWindowIndex}], Active tab index [{windowArray[_activeWindowIndex].ActiveTabIndex}], Target ID [{((PuppeteerClient)PuppeteerClient)._currentTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
        }
        // Remove window from queue by browserContextId or targetId
        /// <summary>
        /// Removes a window (browser context) from the window queue, identified either by its browser context ID
        /// or by the target ID of one of its tabs, and adjusts the active window index and current target ID as needed.
        /// </summary>
        /// <param name="browserContextId">The browser context ID of the window to remove, or null to look up by <paramref name="targetId"/>.</param>
        /// <param name="targetId">The target ID of a tab belonging to the window to remove, used if <paramref name="browserContextId"/> is null.</param>
        /// <returns>True if a matching window was found and removed; false otherwise.</returns>
        internal bool RemoveWindowFromQueue(string browserContextId = null, string targetId = null)
        {
            lock (_tabQueueLock)
            {
                var windowArray = _windowSessionsQueue.ToArray();
                var indexToRemove = -1;

                if (browserContextId != null)
                {
                    indexToRemove = Array.FindIndex(windowArray, w => w.BrowserContextId == browserContextId);
                }
                else if (targetId != null)
                {
                    for (int i = 0; i < windowArray.Length; i++)
                    {
                        var tabArray = windowArray[i].TabQueue.ToArray();
                        if (Array.Exists(tabArray, kvp => kvp.Key == targetId))
                        {
                            indexToRemove = i;
                            browserContextId = windowArray[i].BrowserContextId;
                            break;
                        }
                    }
                }

                if (indexToRemove == -1)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Window/Context ID [{browserContextId ?? targetId}] not found", this, GPALObjectType.PuppeteerCommunicator);
                    return false;
                }

                var queueList = _windowSessionsQueue.ToList();
                queueList.RemoveAt(indexToRemove);

                _windowSessionsQueue = new ConcurrentQueue<WindowSession>();
                foreach (var w in queueList)
                {
                    _windowSessionsQueue.Enqueue(w);
                }

                if (queueList.Count == 0)
                {
                    _activeWindowIndex = 0;
                    ((PuppeteerClient)PuppeteerClient)._currentTargetId = null;
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No windows remain after removing window with browserContextId [{browserContextId}]", this, GPALObjectType.PuppeteerCommunicator);
                }
                else
                {
                    if (_activeWindowIndex > indexToRemove)
                    {
                        _activeWindowIndex--;
                    }
                    else if (_activeWindowIndex == indexToRemove)
                    {
                        _activeWindowIndex = Math.Max(0, _activeWindowIndex - 1);
                    }
                    ((PuppeteerClient)PuppeteerClient)._currentTargetId = GetCurrentTargetId();
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Removed window with browserContextId [{browserContextId}], Active window index now [{_activeWindowIndex}], Active tab index [{windowArray[_activeWindowIndex].ActiveTabIndex}], New active target [{((PuppeteerClient)PuppeteerClient)._currentTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
                }

                return true;
            }
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
                    xDistance = -hPixels, // Note: xDistance is positive for scrolling left
                    yDistance = -vPixels, // Note: yDistance is positive for scrolling up
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
        #region HELPERS
        /// <summary>
        /// Resizes and repositions the browser window to the given bounds via <c>Browser.setWindowBounds</c>.
        /// </summary>
        /// <param name="windowSize">The desired window position and size (X, Y, Width, Height).</param>
        /// <param name="sessionId">Target session ID, or null to use the effective session.</param>
        /// <returns>A Task that resolves to true once the window bounds have been set.</returns>
        internal async Task<bool> SetWindowSize(Rectangle windowSize, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            var windowId = await GetCurrentWindow(sessionId).ConfigureAwait(false);

            await SendCommand(DevToolsMethods.BrowserSetWindowBounds, new
            {
                windowId,
                bounds = new
                {
                    left = windowSize.X,
                    top = windowSize.Y,
                    width = windowSize.Width,
                    height = windowSize.Height
                }
            }, sessionId).ConfigureAwait(false);

            return true;
        }        
        #endregion HELPERS
    }
    internal static class DevToolsCommandBuilder
    {
        // Complete list of CDP DOM methods that require a nodeId parameter
        // Based on Chrome DevTools Protocol (version 1.3, stable as of Chrome 126+)
        internal static readonly HashSet<string> MethodsRequiringNodeId = new HashSet<string>
                            {
                                // DOM domain methods that require nodeId
                                "DOM.copyTo",
                                "DOM.describeNode",
                                "DOM.focus",
                                "DOM.getAttributes",
                                "DOM.getBoxModel",
                                "DOM.getContentQuads",
                                "DOM.getFlattenedDocument",
                                "DOM.getNodeForLocation",
                                "DOM.getOuterHTML",
                                "DOM.getRelayoutBoundary",
                                "DOM.moveTo",
                                "DOM.querySelector",
                                "DOM.querySelectorAll",
                                "DOM.removeAttribute",
                                "DOM.removeNode",
                                "DOM.requestNode",
                                "DOM.setAttributeValue",
                                "DOM.setAttributesAsText",
                                "DOM.setNodeName",
                                "DOM.setNodeValue",
                                "DOM.setOuterHTML",
                                "DOM.collectClassNames",
                                "DOM.getNodeStackTraces",
                                "DOM.scrollIntoViewIfNeeded",
                                // Accessibility domain methods that require nodeId
                                "Accessibility.getPartialAXTree",
                                "Accessibility.getFullAXTree",
                                // CSS domain methods that require nodeId
                                "CSS.getComputedStyleForNode",
                                "CSS.getInlineStylesForNode",
                                "CSS.getMatchedStylesForNode",
                                "CSS.getBackgroundColors",
                                // Add any experimental or newer methods if needed (none currently require nodeId in stable CDP)
                            };

        public static string BuildCommand<TParameters>(
                DevToolsMethods method,
                TParameters parameters,
                long commandId,
                string sessionId = null,
                int docRootId = 1)
        {
            string methodString = GetMethodString(method);
            object finalParameters = parameters;

            // Check if the method requires a nodeId and parameters are provided
            if (MethodsRequiringNodeId.Contains(methodString) && parameters != null)
            {
                // Convert parameters to dictionary for manipulation
                IDictionary<string, object> paramDict;
                if (parameters is IDictionary<string, object> dict)
                {
                    paramDict = dict;
                }
                else
                {
                    // Handle anonymous objects
                    paramDict = parameters.GetType()
                        .GetProperties()
                        .ToDictionary(p => p.Name, p => p.GetValue(parameters));
                }

                finalParameters = paramDict;
            }

            string paramsString = "";
            if (finalParameters != null)
            {
                IDictionary<string, object> paramDict;
                if (finalParameters is IDictionary<string, object> dict)
                {
                    paramDict = dict;
                }
                else
                {
                    paramDict = finalParameters.GetType()
                        .GetProperties()
                        .ToDictionary(p => p.Name, p => p.GetValue(finalParameters));
                }

                var filteredDict = paramDict.Where(kvp => kvp.Value != null)
                                           .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                if (filteredDict.Count > 0)
                {
                    paramsString = $", \"params\": {JsonConvert.SerializeObject(filteredDict)}";
                }
            }

            string sessionString = sessionId != null ? $"\"sessionId\": \"{sessionId}\"," : "";
            string command = $"{{\"id\": {commandId}, {sessionString} \"method\": \"{methodString}\"{paramsString}}}";

            // NOTE: this can be quite excessive
            GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Generated command: [{command}]");

            return command;
        }

        /// <summary>
        /// Gets the wire-format CDP method string (e.g. "DOM.querySelector") for the given <see cref="DevToolsMethods"/> value,
        /// using its <see cref="CommandAttribute"/> if present, or falling back to replacing underscores with dots.
        /// </summary>
        /// <param name="method">The DevTools method enum value.</param>
        /// <returns>The CDP wire-format method string.</returns>
        internal static string GetMethodString(DevToolsMethods method)
        {
            var attr = method.GetAttributeOfType<CommandAttribute>();
            return attr?.CommandString ?? method.ToString().Replace('_', '.');
        }
    }

    public static class EnumExtensions
    {
        public static TAttribute GetAttributeOfType<TAttribute>(this Enum enumVal) where TAttribute : Attribute
        {
            var type = enumVal.GetType();
            var memInfo = type.GetMember(enumVal.ToString());
            var attributes = memInfo[0].GetCustomAttributes(typeof(TAttribute), false);
            return (attributes.Length > 0) ? (TAttribute)attributes[0] : null;
        }
    }
    public static class TaskExtensions
    {
        public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout, string what = null, bool quiet = false)
        {
            Task completedTask = null;
            using (var cts = new CancellationTokenSource())
            {
                var delayTask = Task.Delay(timeout, cts.Token);
                try
                {
                    completedTask = await Task.WhenAny(task, delayTask).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var ex2 = ex;
                }

                if (completedTask == delayTask)
                {
                    if (false == quiet)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{what ?? "Operation"}] timed out after [{timeout.TotalSeconds}]s", null, GPALObjectType.Puppeteer);
                    return default;
                }

                // Cancel the delay task immediately so it stops consuming system resources
                cts.Cancel();

                if (task.IsFaulted)
                {
                    if (false == task.Exception?.InnerException?.Message.Contains("wasn't found"))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Task faulted: [{task.Exception?.InnerException?.Message ?? "Unknown error"}]", null, GPALObjectType.Puppeteer);
                    return default;
                }

                if (task.IsCanceled)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Task canceled", null, GPALObjectType.Puppeteer);
                    return default;
                }

                // using task.Result avoids spinning up another await context state machine.
                return task.Result;
            }
        }
    }

    public class BoxModelResponse
    {
        /// <summary>
        /// Gets or sets the box model details (content, padding, border, margin boxes and dimensions) for the queried node.
        /// </summary>
        public Model Model { get; set; }
    }

    public class Model
    {
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the content box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Content { get; set; } // Use double[] for the array of numbers
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the padding box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Padding { get; set; }
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the border box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Border { get; set; }
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the margin box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Margin { get; set; }
        /// <summary>
        /// Gets or sets the overall width of the node's box model, in pixels.
        /// </summary>
        public int Width { get; set; }
        /// <summary>
        /// Gets or sets the overall height of the node's box model, in pixels.
        /// </summary>
        public int Height { get; set; }
    }
}

