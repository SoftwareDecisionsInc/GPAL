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
            var windows = _windowSessionsQueue.ToArray();
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
    }
}
