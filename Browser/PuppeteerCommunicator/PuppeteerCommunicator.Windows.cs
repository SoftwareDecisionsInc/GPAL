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
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
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

        // Implementation for "fullscreen"
        /// <summary>
        /// Full screen the currnet browser window
        /// Headless has no full screen, so in headless mode this maximizes.
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
    }
}
