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
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1, 1); // one SendAsync at a time

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
            _windowSessionsQueue = new ConcurrentQueue<WindowSession>();

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
            // A lost pipe means the browser closed. A WebSocket can drop on a bad CDP command or an aborted connection, so only it reconnects
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
                ws.Options.SetBuffer(65536, 65536); // 64 KB receive and send buffers
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

            // Target.* commands are browser level and take no sessionId, so one passed by any caller is dropped
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
                    BrowserHelper.KillAllRunningProcesses(Browser);
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

                        // Wait for the reply, up to timeoutSeconds
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
                            throw;
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
                                if (false == lastErrorMessage.Contains(msg))    // the caller clears lastErrorMessage
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
                                if (false == lastErrorMessage.Contains(msg))    // the caller clears lastErrorMessage
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

                        // Handle response data: the reply's result comes back whole, and callers read its fields by name
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
                        data = ((Newtonsoft.Json.Linq.JObject)message?.result).ToObject<object>();

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

        private readonly List<RegisteredScript> _registeredScripts = new List<RegisteredScript>();

        // The session Chrome auto-attached to each new tab, by target id, and the NewTab, OpenWindow or CreateTarget
        // calls waiting for one
        private readonly ConcurrentDictionary<string, string> _autoSessions = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _autoSessionWaiters = new ConcurrentDictionary<string, TaskCompletionSource<string>>();
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
            // Signal that the reader is running
            _readerReadyTcs?.SetResult(true);

            var stream = Browser.BrowserSettings.OutboundPipe; // FileStream (byte-oriented)
            if (stream == null || !stream.IsConnected || !stream.CanRead)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "OutboundPipe null, disconnected, or not readable", this, GPALObjectType.PuppeteerCommunicator);
                return;
            }

            var encoding = Encoding.UTF8;

            // Helper to process one complete JSON text message
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

                        if (method == "Target.targetCreated")
                        {
                            if (true == targetType?.Equals("page") && false == url?.StartsWith("chrome://") && false == url?.Equals("about:blank"))
                            {
                                string targetId = paramsData?.targetInfo?.targetId?.ToString();
                                string browserContextId = paramsData?.targetInfo?.browserContextId?.ToString();
                                if (!string.IsNullOrEmpty(targetId))
                                {
                                    // Off the reader, which receives the replies HandleNewTarget waits on, and
                                    // whether or not a command holds the send semaphore
                                    _ = Task.Run(() => HandleNewTarget(targetId, browserContextId, null, false));

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
                            // The browser closed first and waits for our close. Close() marks this reader stopped
                            // before it sends ours, so a stopped reader leaves the close to Close(); after ours, the
                            // browser's close ends the handshake and the socket is already closed
                            if (true == _isRunning && WebSocketState.CloseReceived == _webSocket.State)
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
                                    }
                                }
                            }

                            if (method.StartsWith("Cast.")) // Cast.sinksUpdated lists the cast devices
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

                            if (method == "Target.targetCreated")
                            {
                                if (true == targetType.Equals("page") && false == url?.StartsWith("chrome://") && false == url?.Equals("about:blank"))
                                {
                                    string targetId = paramsData.targetInfo?.targetId?.ToString();
                                    string browserContextId = paramsData.targetInfo?.browserContextId?.ToString();

                                    if (!string.IsNullOrEmpty(targetId))
                                    {
                                        // List the tab, and prepare its session when Chrome has already attached it.
                                        // Off the reader, which receives the replies HandleNewTarget waits on, and
                                        // whether or not a command holds the send semaphore
                                        _ = Task.Run(() => HandleNewTarget(targetId, browserContextId, null, false));

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
                catch (Exception ex)
                {
                    string innerMsg = "";

                    // Add the inner exception's message
                    if (ex.InnerException != null)
                    {
                        innerMsg = $" Inner: [{ex.InnerException.Message}]";
                    }

                    string exMsg = $"Error receiving WebSocket message: [{ex.Message}][{innerMsg}]";

                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEEPDEBUG,
                        exMsg,
                        this,
                        GPALObjectType.Puppeteer,
                        ex);

                    // Not rethrown: a throw here ends the reader thread and takes the process down
                }
            }
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
                // the connection is being torn down, so a failed close changes nothing
            }
        }

        private readonly SemaphoreSlim _evaluateSelectorSemaphore = new SemaphoreSlim(1, 1);

        internal string LastNavigationError { get; set; }

        /// <summary>
        /// True while commands can reach the browser: a pipe, or a websocket that is open. A browser that ended with
        /// its last window leaves the websocket aborted.
        /// </summary>
        internal bool ConnectionOpen { get => true == _usePipes || WebSocketState.Open == _webSocket?.State; }

        // Where the last move left the pointer, so the next one moves from there. Null until the first move.
        private PointF? pointerAt = null;

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
        // Implementation for "left-click-and-upload"
        // CAVEAT: DOM.setFileInputFiles replaces the input's files, so attached paths are tracked per session and input, re-sent in full on each call, and cleared on navigation
        private static readonly Dictionary<string, List<string>> _attachedFilesPerSession = new Dictionary<string, List<string>>();
    }
}
