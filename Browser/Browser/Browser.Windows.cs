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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// <summary>
        /// Restore the browser to window state, unminimize
        /// </summary>
        public IAllowAllBrowserAndAllSelector Restore
        {
            get
            {
                // read before it is cleared; a full screen window comes back through Normal
                bool wasFullScreen = BrowserSettings.FullScreen;

                BrowserSettings.FullScreen = false;
                BrowserSettings.Maximize = false;
                BrowserSettings.Minimize = false;

                //InAction(false, false);

                if (null != BrowserSettings.Process || 0 != BrowserSettings.ServiceDriverPid)
                    if (true == BrowserSettings.UseOttoMagic)
                    {
                        if (true == wasFullScreen)
                            MagicHelper.Normal();
                        else
                            MagicHelper.Restore();
                    }
                    else if (true == BrowserSettings.UsePuppeteer)
                    {
                        if (true == wasFullScreen)
                            PuppeteerClient.Normal().Execute();
                        else
                            PuppeteerClient.Restore().Execute();
                    }
                    else if (null != BrowserSettings.BrowserDriver)
                        BrowserSettings.BrowserDriver.Manage().Window.Size = BrowserSettings.SavedWindowSize;

                return this;
            }
        }

        /// <summary>
        /// Make the browser window fullscreen, saving the current window size so it can be restored later via <see cref="Restore"/>.
        /// </summary>
        public IAllowAllBrowserAndAllSelector FullScreen
        {
            get
            {
                BrowserSettings.Minimize = false;
                BrowserSettings.Maximize = false;
                BrowserSettings.FullScreen = true;

                //InAction(false, false);

                if (null != BrowserSettings.Process || 0 != BrowserSettings.ServiceDriverPid)
                    if (true == BrowserSettings.UseOttoMagic)
                    {
                        BrowserSettings.SavedWindowSize = new Size(MagicHelper.WindowOuterWidth(), MagicHelper.WindowOuterHeight());
                        MagicHelper.FullScreen();
                    }
                    else if (true == BrowserSettings.UsePuppeteer)
                    {
                        BrowserSettings.SavedWindowSize = new Size(PuppeteerClient.WindowOuterWidth().Execute<int>(), PuppeteerClient.WindowOuterHeight().Execute<int>());
                        PuppeteerClient.FullScreen().Execute();
                    }
                    else if (null != BrowserSettings.BrowserDriver)
                    {
                        BrowserSettings.SavedWindowSize = BrowserSettings.BrowserDriver.Manage().Window.Size;
                        BrowserSettings.BrowserDriver?.Manage().Window.FullScreen();
                    }

                return this;
            }
        }
        /// <summary>
        /// Maximize the browser to full-screen
        /// </summary>
        public IAllowAllBrowserAndAllSelector Maximize
        {
            get
            {
                BrowserSettings.Maximize = true;
                BrowserSettings.Minimize = false;
                BrowserSettings.FullScreen = false;

                //InAction(false, false);

                if (null != BrowserSettings.Process || 0 != BrowserSettings.ServiceDriverPid)
                    if (true == BrowserSettings.UseOttoMagic)
                    {
                        if (0 == BrowserSettings.WindowSize.Width && 0 == BrowserSettings.WindowSize.Height)
                            BrowserSettings.SavedWindowSize = new Size(MagicHelper.WindowOuterWidth(), MagicHelper.WindowOuterHeight());
                        MagicHelper.Maximize();
                    }
                    else if (true == BrowserSettings.UsePuppeteer)
                    {
                        if (0 == BrowserSettings.WindowSize.Width && 0 == BrowserSettings.WindowSize.Height)
                            BrowserSettings.SavedWindowSize = new Size(PuppeteerClient.WindowOuterWidth().Execute<int>(), PuppeteerClient.WindowOuterHeight().Execute<int>());
                        PuppeteerClient.Maximize().Execute();
                    }
                    else if (null != BrowserSettings.BrowserDriver)
                    {
                        // get current window size if one was not defined on startup
                        BrowserSettings.SavedWindowSize = BrowserSettings.BrowserDriver.Manage().Window.Size;
                        BrowserSettings.BrowserDriver?.Manage().Window.Maximize();
                    }

                return this;
            }
        }
        /// <summary>
        /// Minimize the browser to the taskbar
        /// </summary>
        public IAllowAllBrowserAndAllSelector Minimize
        {
            get
            {
                BrowserSettings.Minimize = true;
                BrowserSettings.Maximize = false;
                BrowserSettings.FullScreen = false;

                //InAction(false);

                if (null != BrowserSettings.Process || 0 != BrowserSettings.ServiceDriverPid)
                    if (true == BrowserSettings.UseOttoMagic)
                    {
                        BrowserSettings.SavedWindowSize = new Size(MagicHelper.WindowOuterWidth(), MagicHelper.WindowOuterHeight());
                        MagicHelper.Minimize();
                    }
                    else if (true == BrowserSettings.UsePuppeteer)
                    {
                        BrowserSettings.SavedWindowSize = new Size(PuppeteerClient.WindowOuterWidth().Execute<int>(), PuppeteerClient.WindowOuterHeight().Execute<int>());
                        PuppeteerClient.Minimize().Execute();
                    }
                    else if (null != BrowserSettings.BrowserDriver)
                    {
                        // get current window size if one was not defined on startup
                        BrowserSettings.SavedWindowSize = BrowserSettings.BrowserDriver.Manage().Window.Size;
                        BrowserSettings.BrowserDriver?.Manage().Window.Minimize();
                    }

                return this;
            }
        }

        /// <summary>
        /// Close the current in focus, active tab.
        /// </summary>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector CloseTab(dynamic URLorTabId = null)
        {
            // CloseTab picks the tab to land on itself; PreviousTab would bring the browser to the front mid-close
            if (true == UsePuppeteer)
            {
                if (URLorTabId is string s)
                    PuppeteerClient.CloseTab(s).Execute();
                else if (URLorTabId is GPALUrl url)
                    PuppeteerClient.CloseTab(url).Execute();
                else if (URLorTabId is int i)
                    PuppeteerClient.CloseTab(i).Execute();
                else
                    PuppeteerClient.CloseTab().Execute();
            }
            else if (true == UseOttoMagic)
            {
                string currentUrl = BrowserSettings.CurrentURL;

                int currentTabId = GetActiveTabId();
                TabTuple tabTuple = MagicHelper.CloseTab(URLorTabId);   // tabtuple is new active tab

                if (true == string.IsNullOrEmpty(URLorTabId))
                {
                    BrowserSettings.TabIdsToURL.Remove(currentTabId);
                    BrowserSettings.URLsToTabId.Remove(currentUrl);
                }
                else if (URLorTabId is int tabId)
                {
                    BrowserSettings.URLsToTabId.Remove(BrowserSettings.TabIdsToURL[tabId].URL);
                    BrowserSettings.TabIdsToURL.Remove(tabId);
                }
                else if (URLorTabId is string url)
                {
                    BrowserSettings.URLsToTabId.Remove(url);
                    BrowserSettings.TabIdsToURL.Remove(currentTabId);
                }

                UpdateActiveTab(tabTuple);
            }
            else
            {
                if (true == string.IsNullOrWhiteSpace(URLorTabId))
                {
                    ResolveGhostTabHandle();   // ensure Edge's phantom tab is known before we pick a tab to land on

                    // Pick the next tab BEFORE closing, while the handle list still has this tab.
                    string tabClosing = null;
                    string landOn = null;

                    try
                    {
                        var before = BrowserDriver.WindowHandles;
                        tabClosing = BrowserDriver.CurrentWindowHandle;

                        // the tab to the left of the one closing, skipping Edge's ghost, else anything that is not us
                        int closingIdx = before.IndexOf(tabClosing);
                        for (int idx = closingIdx - 1; 0 <= idx && null == landOn; idx--)
                            if (before[idx] != GhostTabHandle)
                                landOn = before[idx];

                        if (null == landOn)
                            landOn = before.FirstOrDefault(h => h != tabClosing && h != GhostTabHandle);
                    }
                    catch (Exception exRead)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CloseTab: could not read the tab list before closing, will fall back to whatever the driver leaves us on.", this, GPALObjectType.Browser, exRead);
                    }

                    // guarded: EdgeDriver can hang here for its 20 s command timeout and throw; the tab is closing
                    // either way, so the workflow goes on.
                    try { BrowserSettings.BrowserDriver.Close(); }  // close current tab
                    catch (Exception exClose) { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CloseTab: driver failed to close the current tab, continuing.", this, GPALObjectType.Browser, exClose); }

                    // closing the last tab leaves nothing to land on, which is a legitimate end of the workflow
                    if (false == string.IsNullOrEmpty(landOn))
                        try
                        {
                            BrowserDriver.SwitchTo().Window(landOn);
                            CurrentTabIdx = BrowserDriver.WindowHandles.IndexOf(landOn);   // re-seat against the list we actually have now

                        }
                        catch (Exception exSwitch)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CloseTab: could not switch to tab [{landOn}] after closing.", this, GPALObjectType.Browser, exSwitch);
                        }
                }
                else    // close a tab with a particular url (we don't use handles on selenium side)
                {
                    // Save the current tab to return to; FindTab switches through tabs to read their urls.
                    string staying = BrowserDriver.CurrentWindowHandle;
                    var handle = BrowserHelper.FindTab(BrowserSettings, URLorTabId);

                    // no tab is sitting on that url any more, which happens whenever the tab has navigated on, a
                    // download being the obvious case. Selenium answers a null handle with "'handle' must be a
                    // string" and takes the workflow down, so say what happened instead
                    if (true == string.IsNullOrEmpty(handle))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CloseTab: no tab is on [{URLorTabId}], nothing closed. Call .CloseTab() with no url to close the current tab.", this, GPALObjectType.Browser);
                    else
                    {
                        BrowserSettings.BrowserDriver.SwitchTo().Window(handle);

                        // guarded for the same reason as the no-url case above
                        try { BrowserSettings.BrowserDriver.Close(); }
                        catch (Exception exClose) { GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"CloseTab: driver failed to close tab [{URLorTabId}], continuing.", this, GPALObjectType.Browser, exClose); }

                        // go back to where the workflow was. Closing the tab we were on ourselves leaves nothing to
                        // go back to, so take the last one open, and closing the only tab leaves nothing at all
                        var after = BrowserDriver.WindowHandles;
                        string landOn = true == after.Contains(staying) ? staying : after.LastOrDefault(h => h != GhostTabHandle);

                        if (false == string.IsNullOrEmpty(landOn))
                        {
                            BrowserDriver.SwitchTo().Window(landOn);
                            CurrentTabIdx = after.IndexOf(landOn);

                        }
                    }
                }
            }

            // Any tabs left? Ask the browser; tabsWeOpened misses tabs a page opened or closed. Firefox reports 1 (an
            // about:blank tab); Chrome and Edge report 0 or have ended the session.
            int tabsLeft = 0;

            try
            {
                tabsLeft = RealTabCount();

                if (0 < tabsLeft)
                {
                    GetSetCurrentUrl();
                    SetTabGPALUrl();
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Browser stopped answering after closing the tab.", this, GPALObjectType.Browser, ex);
            }

            if (0 == tabsLeft)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Closed the last tab, the browser closed with it.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Closed the tab, [{tabsLeft}] left, now on [{UrlHelper.ForMessage(BrowserSettings.CurrentURL)}].", this, GPALObjectType.Browser);

            tabsWeOpened--;

            //CheckPersistentSelectors();

            return this;
        }
        /// <summary>
        /// Tabs in the current tab's window, in driver order. NextTab and PreviousTab cycle this, not the driver's
        /// all-window list.<br/>
        /// Excludes Edge's phantom tab. A tab with no recorded window joins this one.
        /// </summary>
        /// <param name="handles">The driver's tab list, read fresh</param>
        /// <param name="current">The handle the driver is on right now</param>
        /// <returns>The handles in this window, in list order</returns>
        private List<string> TabsInThisWindow(IList<string> handles, string current)
        {
            List<string> siblings = new List<string>();

            // The current tab picks the window; an unrecorded one is recorded as its own window.
            if (false == tabWindows.ContainsKey(current))
                tabWindows[current] = current;

            string thisWindow = tabWindows[current];

            foreach (string handle in handles)
            {
                if (false == tabWindows.ContainsKey(handle))
                    tabWindows[handle] = thisWindow;

                if (tabWindows[handle] == thisWindow && handle != GhostTabHandle)
                    siblings.Add(handle);
            }

            return siblings;
        }
        /// <summary>
        /// Records a new tab (NewTab or a click) in the window of the tab it opened from. OpenWindow records new
        /// windows.
        /// </summary>
        /// <param name="newHandle">The tab that just appeared</param>
        /// <param name="openedFrom">The tab it was opened from, whose window it joins</param>
        private void RecordTabWindow(string newHandle, string openedFrom)
        {
            if (true == string.IsNullOrEmpty(newHandle))
                return;

            if (false == string.IsNullOrEmpty(openedFrom) && true == tabWindows.ContainsKey(openedFrom))
                tabWindows[newHandle] = tabWindows[openedFrom];
            else
                tabWindows[newHandle] = string.IsNullOrEmpty(openedFrom) ? newHandle : openedFrom;
        }
        /// <summary>
        /// Edge only: lazily resolve the window handle of Edge's phantom New Tab Page preload window by matching
        /// the url signature captured at launch (<see cref="GhostTabUrl"/>). Runs the scan only when there are more
        /// open handles than tabs we opened ourselves, so it is a no-op once found or when no ghost is present.
        /// Momentarily switches to each unexplained handle to read its url, then restores focus.
        /// </summary>
        /// <returns>The ghost handle if known/identified, otherwise null.</returns>
        internal string ResolveGhostTabHandle()
        {
            // only Selenium switches by handle, and only Edge injects the preload ghost
            if (false == UseSelenium || BrowserType.Edge != BrowserSettings.BrowserType || true == string.IsNullOrEmpty(GhostTabUrl))
                return GhostTabHandle;

            // already found - cached for the life of the session
            if (false == string.IsNullOrEmpty(GhostTabHandle))
                return GhostTabHandle;

            var handles = BrowserDriver.WindowHandles;

            // nothing foreign present: no more open handles than tabs we opened ourselves
            if (handles.Count <= tabsWeOpened)
                return null;

            string current;
            try { current = BrowserDriver.CurrentWindowHandle; }
            catch { return null; }

            foreach (string handle in handles)
            {
                if (handle == current) continue;   // the tab we are on is never the ghost

                string handleUrl = null;
                try
                {
                    BrowserDriver.SwitchTo().Window(handle);
                    handleUrl = BrowserDriver.Url;
                }
                catch { /* can't read it, skip */ }
                finally { try { BrowserDriver.SwitchTo().Window(current); } catch { } }

                // positive identity match against the launch signature - a real tab will not match.
                // Match on the base url (scheme+host+path) only: Edge's launch url and the settled ghost url
                // share the same NTP path but differ in query (prerender=1 vs startpage=1, space encoding, an
                // extra invalidaterender token), so a full-url compare would never match.
                if (false == string.IsNullOrEmpty(handleUrl) && true == UrlHelper.SameBaseUrl(handleUrl, GhostTabUrl))
                {
                    GhostTabHandle = handle;
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Ghost tab found, will always skip [{handleUrl}]", this, GPALObjectType.Browser);
                    break;
                }
            }

            return GhostTabHandle;
        }
        /// <summary>
        /// Navigate to the next tab. If at the last tab nothing will happen.
        /// It moves among the tabs of the current window; a window opened with OpenWindow is reached with .NextWindow.
        /// </summary>
        /// <remarks>
        /// Under Puppeteer and Selenium the browser window comes to the front; under OttoMagic it does not. Run
        /// headless to avoid it.
        /// </remarks>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector NextTab
        {
            get
            {
                // each engine switches tabs itself; Selenium switches through the driver, so it knows which tab is current
                if (true == UseOttoMagic)
                {
                    TabTuple tabTuple = MagicHelper.NextTab();
                    UpdateActiveTab(tabTuple);
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerClient.NextTab().Execute();
                }
                else
                {
                    ResolveGhostTabHandle();   // Edge injects a phantom NTP handle; identify it so we never land on it

                    var handles = BrowserDriver.WindowHandles;
                    string current = BrowserDriver.CurrentWindowHandle;

                    // Cycle the current window's tabs. Get the current tab from the driver each time; a saved index
                    // breaks when tabs open or close.
                    List<string> siblings = TabsInThisWindow(handles, current);

                    int idx = siblings.IndexOf(current);
                    if (0 > idx) idx = 0;

                    idx = (idx + 1) % siblings.Count;

                    CurrentTabIdx = handles.IndexOf(siblings[idx]);   // reported to the workflow, never read back as our position

                    BrowserDriver.SwitchTo().Window(siblings[idx]);
                    if (BrowserDriver.CurrentWindowHandle != siblings[idx])
                        BrowserDriver.SwitchTo().Window(siblings[idx]);

                }

                Thread.Sleep(50);
                //BrowserHelper.CheckDocumentReady(this, true);   // check document.ready

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Switched to the next tab, [{UrlHelper.ForMessage(GetSetCurrentUrl())}].", this, GPALObjectType.Browser);
                SetTabGPALUrl();

                return this;
            }
        }
        /// <summary>
        /// Used to specify the next GoTo command should open in a new tab. The new tab is then the active tab.<br/>
        /// Use .NextTab and .PreviousTab to navigate tabs.
        /// Applies only once. Use .WithNewTab before each .GoTo to open in a new tab.
        /// </summary>
        /// <example>
        /// <code>
        ///     myBrowser
        ///         .WithNewTab("http://www.google.com");
        ///         
        /// // open a new tab in headless mode, then navigate in the new tab
        ///     myBrowser
        ///         .WithNewTab()
        ///         .Get("http://www.google.com");
        /// </code>
        /// </example>
        public IAllowBrowserActionOrAnySelector NewTab(GPALUrl url)
        {
            BrowserSettings.OpenInTab = true;
            BrowserSettings.Browser.GoTo(url);  // sets CurrentGPALUrl and records it as the new tab's
            return this;
        }
        /// <summary>
        /// Navigate to the previous tab. If at the first tab nothing will happen.
        /// </summary>
        /// <remarks>
        /// Under Puppeteer and Selenium the browser window comes to the front; under OttoMagic it does not. Run
        /// headless to avoid it.
        /// </remarks>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector PreviousTab
        {
            get
            {
                // each engine switches tabs itself; Selenium switches through the driver, so it knows which tab is current
                if (true == UseOttoMagic)
                {
                    UpdateActiveTab(MagicHelper.PreviousTab());
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerClient.PreviousTab().Execute();
                }
                else
                {
                    ResolveGhostTabHandle();   // Edge injects a phantom NTP handle; identify it so we never land on it

                    var handles = BrowserDriver.WindowHandles;
                    string current = BrowserDriver.CurrentWindowHandle;

                    // same as NextTab: the tabs of this window only, and the driver is asked where we are
                    List<string> siblings = TabsInThisWindow(handles, current);

                    int idx = siblings.IndexOf(current);
                    if (0 > idx) idx = 0;

                    // (i - 1 + n) % n - add the raw count before the modulo so a -1 wrap stays positive
                    idx = (idx - 1 + siblings.Count) % siblings.Count;

                    CurrentTabIdx = handles.IndexOf(siblings[idx]);   // reported to the workflow, never read back as our position

                    BrowserDriver.SwitchTo().Window(siblings[idx]);
                    if (BrowserDriver.CurrentWindowHandle != siblings[idx])
                        BrowserDriver.SwitchTo().Window(siblings[idx]);

                }

                //BrowserHelper.CheckDocumentReady(this, true);   // check idle

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Switched to the previous tab, [{UrlHelper.ForMessage(GetSetCurrentUrl())}].", this, GPALObjectType.Browser);
                SetTabGPALUrl();

                return this;
            }
        }

        /// <summary>
        /// Goto the tab with the specified URL
        /// If using OttoMagic, you can use the tabid from browser.BrowserSettings.TabIds[tabid] search for matching URL
        /// </summary>
        /// <remarks>
        /// Under Puppeteer and Selenium the browser window comes to the front; under OttoMagic it does not. Run
        /// headless to avoid it.
        /// </remarks>
        /// <param name="URLorTabTuple"></param>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector GoToTab(dynamic URLorTabTuple = null)
        {
            if (true == UseOttoMagic)
            {
                TabTuple gotoTabTuple = MagicHelper.GotoTab(URLorTabTuple);
                if (URLorTabTuple is TabTuple)
                    UpdateActiveTab(URLorTabTuple);
                else
                    UpdateActiveTab(gotoTabTuple);
            }
            else if (true == UsePuppeteer)
            {
                if (URLorTabTuple is GPALUrl url)
                    PuppeteerClient.GoToTab(url.Url).Execute();
                else if (URLorTabTuple is int tabId)
                    PuppeteerClient.GoToTab(tabId).Execute();
            }
            else
            {
                foreach (var handle in this.BrowserDriver.WindowHandles)
                {
                    string currentUrl = this.BrowserDriver.SwitchTo().Window(handle).Url;
                    if (currentUrl == URLorTabTuple)
                    {
                        this.BrowserDriver.SwitchTo().Window(handle);
                        BrowserSettings.CurrentURL = URLorTabTuple;

                        var handles = BrowserDriver.WindowHandles.ToList();
                        string currentHandle = BrowserDriver.CurrentWindowHandle;

                        CurrentTabIdx = handles.IndexOf(currentHandle);

                        SetTabGPALUrl();
                        return this;
                    }
                }
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to detect tab with URL [{URLorTabTuple}]", this, GPALObjectType.Browser);
            }

            SetTabGPALUrl();
            return this;
        }

        /// <summary>
        /// The browser window position and size in screen coordinates, chrome included.
        /// <br/><br/>
        /// Puppeteer reads it from the browser over CDP, which needs no page. Selenium and OttoMagic read the
        /// screen position and outer size from the page. A window that cannot be measured returns empty.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.GetWindowRectangle(out Rectangle window);
        /// </code>
        /// </example>
        /// <param name="window">Receives the window rectangle in screen coordinates.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetWindowRectangle(out Rectangle window)
        {
            window = Rectangle.Empty;

            if (true == InAction(false))
            {
                if (true == UsePuppeteer)
                    window = PuppeteerCommunicator.GetWindowRectangle().GetAwaiter().GetResult();
                else
                    ElementHelper.GetWindowRectangle(this, out window);
            }

            return this;
        }
        /// <summary>
        /// Do the actual work for InAction (And WaitFor cause it interact with the browser)
        /// </summary>
        /// <param name="checkWaitFor"></param>
        /// <param name="doMaintenance"></param>
        /// <param name="URL"></param>
        /// <returns></returns>
        /// <summary>
        /// The Selenium driver or browser process that has exited, by name, or null while both run. A driver GPAL
        /// did not start, and a browser whose process was not found at launch, are not looked at.
        /// </summary>
        /// <returns>The exited process's name, or null</returns>
        private string SeleniumProcessExited()
        {
            string retVal = null;
            int driverPid = BrowserSettings.ServiceDriverPid;

            if (0 < driverPid)
            {
                try
                {
                    using (Process driver = Process.GetProcessById(driverPid))
                        if (true == driver.HasExited)
                            retVal = "webdriver";
                }
                catch (ArgumentException)
                {
                    // no process with that id is running
                    retVal = "webdriver";
                }
            }

            if (null == retVal && true == BrowserSettings.Process?.HasExited)
                retVal = BrowserSettings.BrowserType.ToString();

            return retVal;
        }
        /// <summary>
        /// Get the active tabid
        /// </summary>
        /// <returns></returns>
        /// <summary>
        /// Restores state after a tab GPAL opened closes itself, which a browser does when the tab's first navigation
        /// is a download.<br/>
        /// Decrements tabsWeOpened, switches back to the original tab and resets the current url to it. Not a
        /// failure: the download continues. No page loaded, so nothing is added to the url list or waited on.
        /// </summary>
        /// <param name="URL">The url that downloaded rather than opening a page</param>
        /// <param name="priorUrl">Url the workflow was on before the navigation</param>
        /// <param name="priorGPALUrl">GPALUrl the workflow was on before the navigation</param>
        /// <param name="priorTab">The OttoMagic tab the workflow was on, null for the other engines</param>
        /// <param name="priorHandle">The Selenium handle of the tab the workflow was on, null for the other engines</param>
        private void BackOutOfSelfClosedTab(GPALUrl URL, string priorUrl, GPALUrl priorGPALUrl, TabTuple priorTab, string priorHandle)
        {
            tabsWeOpened--;

            // Switch back to the original tab with each engine's own call. That tab never navigated, so it is still
            // on its url.
            if (true == UseOttoMagic)
                GoToTab(priorTab);
            else if (true == UsePuppeteer)
                PuppeteerClient.GoToTab(priorGPALUrl).Execute();
            else
            {
                // back to the tab we left, by id. It never navigated, so it is still open unless the user closed
                // it; then the last tab in the list is used
                var handles = BrowserDriver.WindowHandles;
                string landOn = true == handles.Contains(priorHandle) ? priorHandle : handles[handles.Count - 1];

                BrowserDriver.SwitchTo().Window(landOn);
                CurrentTabIdx = handles.IndexOf(landOn);
            }

            BrowserSettings.CurrentURL = priorUrl;
            BrowserSettings.CurrentGPALUrl = priorGPALUrl;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{URL?.Url}] downloaded rather than opening a page, so its tab closed itself. Back on [{priorUrl}].", this, GPALObjectType.Browser);
        }

        /// <summary>
        /// The browser's own open tab count, unlike <see cref="tabsWeOpened"/>, which
        /// <see cref="ResolveGhostTabHandle"/> needs to find Edge's phantom tab.<br/>
        /// Use only as a before/after pair; tabs GPAL did not open appear in both and cancel out.
        /// </summary>
        /// <returns>The number of tabs open, zero if the engine could not say</returns>
        internal int RealTabCount()
        {
            int realCount;

            if (true == UseOttoMagic)
                realCount = MagicHelper.TabCount();
            else if (true == UsePuppeteer)
                realCount = PuppeteerCommunicator.TabCount().GetAwaiter().GetResult();
            else
                // WindowHandles works after the current tab has closed (CurrentWindowHandle does not).
                realCount = BrowserDriver.WindowHandles.Count;

            return realCount;
        }
        private int GetActiveTabId()
        {
            foreach(var key in BrowserSettings.TabIdsToURL.Keys.Where(k => BrowserSettings.TabIdsToURL[k].ActiveTab == true))
                return key;

            return 0;
        }
        /// <summary>
        /// For OttoMagic, track the tabid and url and active tab status
        /// </summary>
        /// <param name="activeTabTuple"></param>
        private void UpdateActiveTab(TabTuple activeTabTuple)
        {
            string priorUrl = null;
            if (null != activeTabTuple)
            {
                // First, update or add the current tabId with its tabInfo
                if (BrowserSettings.TabIdsToURL.ContainsKey(activeTabTuple.TabId))
                {
                    priorUrl = BrowserSettings.TabIdsToURL[activeTabTuple.TabId].URL;
                    BrowserSettings.TabIdsToURL[activeTabTuple.TabId].URL = activeTabTuple.Url ?? BrowserSettings.TabIdsToURL[activeTabTuple.TabId].URL;
                    BrowserSettings.TabIdsToURL[activeTabTuple.TabId].ActiveTab = true;
                }
                else
                {
                    priorUrl = activeTabTuple.Url;
                    BrowserSettings.TabIdsToURL.Add(activeTabTuple.TabId, new TabInfo { URL = activeTabTuple.Url, ActiveTab = true });
                }

                // Second, set ActiveTab = false for all other tabs
                foreach (var key in BrowserSettings.TabIdsToURL.Keys.Where(k => k != activeTabTuple.TabId))
                    BrowserSettings.TabIdsToURL[key].ActiveTab = false;

                if (null != activeTabTuple.Url)
                {
                    URLInfo uRLInfo = new URLInfo { TabId = activeTabTuple.TabId, ActiveTab = true };
                    // Next, update or add the current URL with its urlInfo
                    if (BrowserSettings.URLsToTabId.ContainsKey(priorUrl))
                    {
                        BrowserSettings.URLsToTabId[activeTabTuple.Url] = uRLInfo;
                        BrowserSettings.URLsToTabId[activeTabTuple.Url].ActiveTab = true;
                        if (false == priorUrl.Equals(activeTabTuple.Url))
                            BrowserSettings.URLsToTabId.Remove(priorUrl);
                    }
                    else
                        BrowserSettings.URLsToTabId.Add(activeTabTuple.Url, new URLInfo { TabId = activeTabTuple.TabId, ActiveTab = true });

                    // last, set ActiveTab = false for all other tabs
                    foreach (var key in BrowserSettings.URLsToTabId.Keys.Where(k => k != activeTabTuple.Url))
                        BrowserSettings.URLsToTabId[key].ActiveTab = false;

                    BrowserSettings.CurrentURL = activeTabTuple.Url;
                }
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "ActiveTabTuple from last operation is null", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// The engine's key for the tab now shown: the Selenium window handle, the Puppeteer target id or the
        /// OttoMagic tab id. Null when the engine has no tab to name.
        /// </summary>
        /// <returns>The tab's key, or null</returns>
        private string CurrentTabKey()
        {
            string retVal;

            if (true == UseOttoMagic)
            {
                int tabId = GetActiveTabId();
                retVal = 0 == tabId ? null : tabId.ToString();   // 0 is no tab marked active
            }
            else if (true == UsePuppeteer)
                retVal = PuppeteerCommunicator.GetCurrentTargetId();
            else
                retVal = BrowserDriver.CurrentWindowHandle;

            return retVal;
        }
        /// <summary>
        /// Gives the tab now shown the current GPALUrl, the one a GoTo, Get, OpenWindow or GoToWindow just went to.
        /// CloseWindow leaves no current GPALUrl, and then nothing is recorded.
        /// </summary>
        private void RecordTabGPALUrl()
        {
            string tabKey = CurrentTabKey();

            if (null != tabKey && null != BrowserSettings.CurrentGPALUrl)
                BrowserSettings.TabGPALUrls[tabKey] = BrowserSettings.CurrentGPALUrl;
        }
        /// <summary>
        /// After a switch to another tab or window, makes the GPALUrl of the tab now shown the current one, so a
        /// storage action with no For runs with that tab's definitions. A tab with none, as one a link opened, takes
        /// the current GPALUrl and keeps it.
        /// </summary>
        private void SetTabGPALUrl()
        {
            string tabKey = CurrentTabKey();

            if (null != tabKey && true == BrowserSettings.TabGPALUrls.TryGetValue(tabKey, out GPALUrl tabGPALUrl))
            {
                // setting it starts a new storage action, so it is set only when the tab's GPALUrl is another one
                if (false == ReferenceEquals(tabGPALUrl, BrowserSettings.CurrentGPALUrl))
                    BrowserSettings.CurrentGPALUrl = tabGPALUrl;
            }
            else
                RecordTabGPALUrl();
        }
        /// <summary>
        /// After an OttoMagic window switch, marks the tab the extension named as the active tab, so CurrentTabKey
        /// names the tab shown and not one in the window left. An extension that names no tab leaves no tab marked,
        /// and then no tab GPALUrl is read or recorded.
        /// </summary>
        /// <param name="window">The window the extension answered with</param>
        private void MarkWindowTab(WindowTuple window)
        {
            if (null != window?.TabId)
                UpdateActiveTab(new TabTuple(window.TabId.Value, window.Url));
            else
                foreach (var key in BrowserSettings.TabIdsToURL.Keys)
                    BrowserSettings.TabIdsToURL[key].ActiveTab = false;
        }
        /// <summary>
        /// Close/terminate the browser.<br/>
        /// This terminates the fluent browser workflow and nothing can be chained to this command.<br/>
        /// The browser is ended with its web driver, whether GPAL launched it or attached to it. A driver that does
        /// not end is ended when the program exits. Release leaves the browser running for another program, and
        /// CloseWindow closes a window and leaves the browser usable.
        /// </summary>
        public void Close()
        {
            // A closed browser is done, so a second Close does nothing. Close is called again from catch blocks
            // after a failure, which may already have closed it.
            if (null == ClosedAt)
            {
                inClose = true;

                // InAction launches a browser that is not running yet, so it runs only for one that was started or
                // reached. Closing one whose launch failed must not launch it again.
                if (true == HasBrowser)
                    InAction(false, false);

                tabsWeOpened = CurrentTabIdx = 0;
                BrowserLaunched = null;

                // The cleanup below fails if the browser has no page left (last tab closed, or a crash). Catch and log it
                // so Close still ends the browser; a Chromium browser with extensions would otherwise keep running
                // windowless and lock the profile.
                try
                {
                    // Scripts registered through the extension live in the profile, not the browser, and persist after
                    // it closes. The next browser on that profile would run them, so they are removed here
                    if (true == UseOttoMagic && true == _scriptsRegistered)
                        ClearInjectedScripts();

                    StopCasting();

                    // Restore the user's download directory.
                    if (true == BrowserSettings.DownloadBehaviorSet)
                    {
                        BrowserSettings.DownloadBehaviorSet = false;
                        PuppeteerCommunicator.RestoreDownloadBehavior().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{BrowserSettings.BrowserType}] could not be tidied up before closing, so it is closed as it is", this, GPALObjectType.Browser, ex);
                }

                // Close ends the browser, whether GPAL launched it or attached to it; Release is what leaves it
                // running. A GPALRestAPI browser whose process is not found here, such as one on another machine, is
                // ended by closing all its windows. Every other browser is ended through its process.
                if (true == BrowserSettings.AttachedRestApi && null == BrowserSettings.Process)
                {
                    if (false == browserEnded)
                        MagicHelper.CloseBrowser();
                }
                else
                {
                    // KillAllRunningProcesses also deletes the temp profile and asks the browser to quit over the
                    // Puppeteer connection, so mark the connection closed after it returns.
                    BrowserHelper.KillAllRunningProcesses(this);

                    if (true == UsePuppeteer)
                    {
                        BrowserSettings.PuppeteerCommunicator._isRunning = false;
                        BrowserSettings.PuppeteerCommunicator.Closed = true;
                    }

                    // A desktop made for this browser alone goes with it. A named desktop is shared and stays.
                    if (true == BrowserSettings.HiddenDesktop && true == BrowserSettings.HiddenDesktopOwned)
                        Desktops.Close(BrowserSettings.HiddenDesktopName);
                }

                // firefox rewrites its prefs.js on the way out the same as chrome does, so this waits for the same
                // reason: the process has to be gone first
                if (null != BrowserSettings.PreviousFirefoxPreferences)
                {
                    BrowserHelper.WaitForBrowserToExit(BrowserSettings, ProfileRestoreWaitMs);
                    FirefoxProfileManager.RestoreProfile(BrowserSettings.PreviousFirefoxPreferences);
                    BrowserSettings.PreviousFirefoxPreferences = null;
                }

                // chrome writes Preferences as it shuts down, so anything we put back while it is still running is
                // overwritten. the profile only goes back once the process is gone
                if (null != BrowserSettings.PreviousDownloadPreferences)
                {
                    BrowserHelper.WaitForBrowserToExit(BrowserSettings, ProfileRestoreWaitMs);
                    ChromeProfileManager.RestoreDownloadPreferences(BrowserSettings.ProfileDataDirectory, BrowserSettings.PreviousDownloadPreferences);
                    BrowserSettings.PreviousDownloadPreferences = null;
                }

                // A Chromium browser running an extension background page starts a windowless process when its last
                // window closes, which holds the profile. End it, or the next run on this profile fails.
                BrowserHelper.EndWindowlessSuccessor(BrowserSettings);

                // The browser this Close ended, known by its process or, for OttoMagic, by the GPALRestAPI address
                // every GPAL browser driving it talks to.
                int endedPid = BrowserSettings.Process?.Id ?? 0;
                string endedRestApi = BrowserSettings.RestApiBaseUrl;

                BrowserSettings.Process = null;

                GPAL.Forget(this);

                ClosedAt = DateTime.Now;

                // This Close ended the browser, so every other GPAL browser on it ended with it. Each runs its own
                // Close, which puts back what it changed (profile preferences, a temporary profile, a hidden desktop)
                // now that the browser is gone, and a later Close on one of them does nothing.
                if (0 < endedPid || null != endedRestApi)
                    foreach (Browser other in GPAL.Browsers)
                        if ((0 < endedPid && endedPid == (other.BrowserSettings.Process?.Id ?? 0))
                            || (null != endedRestApi && true == string.Equals(endedRestApi, other.BrowserSettings.RestApiBaseUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Another GPAL browser on the same [{other.BrowserSettings.BrowserType}] {(0 < endedPid ? $"process [{endedPid}]" : $"GPALRestAPI [{endedRestApi}]")} is closed with it", other, GPALObjectType.Browser);

                            other.browserEnded = true;
                            other.Close();
                        }
            }
        }
        /// <summary>
        /// Same as Close(), which ends the browser and its web driver; the argument is ignored.
        /// </summary>
        /// <param name="killWebDrivers">Not used</param>
        [Obsolete("Close() ends the browser and its web driver; call Close() instead.")]
        public void Close(bool killWebDrivers)
        {
            Close();
        }
        /// <summary>
        /// Leaves the browser running for another program to pick up, and ends this one's hold on it. Like
        /// <see cref="Close()"/>, the browser is then done for this program.<br/>
        /// Chrome and Edge (debug port) and OttoMagic (GPALRestAPI) take several connections at once, so nothing is
        /// let go. Firefox on Selenium takes one: its geckodriver is ended without quitting, which frees Marionette
        /// and leaves Firefox running.<br/>
        /// What GPAL changed in the profile stays until the browser closes; the next launch on it puts it back.<br/>
        /// On Chrome and Edge the web driver keeps running after this program exits, because ending it would end the
        /// browser it started.
        /// </summary>
        /// <example>
        /// <code>
        ///     // the first program hands its browser on
        ///     browser.Release();
        ///
        ///     // the second program picks it up on the same port
        ///     Browser next = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.FireFox)
        ///         .WithAutomationEngine(AutomationEngine.Selenium)
        ///         .WithExistingCDPDebugPort(6565)
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        public void Release()
        {
            if (null == ClosedAt)
            {
                if (true == UseSelenium && BrowserType.FireFox == BrowserSettings.BrowserType && 0 < BrowserSettings.ServiceDriverPid)
                {
                    System.Diagnostics.Process geckodriver = System.Diagnostics.Process.GetProcesses().FirstOrDefault(p => p.Id == BrowserSettings.ServiceDriverPid);

                    if (null != geckodriver)
                        BrowserHelper.KillProcess(geckodriver);

                    BrowserSettings.ServiceDriverPid = 0;
                }

                // The browser goes on to another program, so its driver is not ended at exit; on Chrome and Edge that
                // would end the browser it started too.
                if (0 < BrowserSettings.ServiceDriverPid)
                    BrowserHelper.ForgetDriver(BrowserSettings.ServiceDriverPid);

                if (true == BrowserSettings.HasPuppeteerCommunicator)
                {
                    BrowserSettings.PuppeteerCommunicator._isRunning = false;
                    BrowserSettings.PuppeteerCommunicator.Closed = true;
                }

                // Its connection would otherwise keep holding the next program's new tabs for this one's scripts
                BrowserSettings.TabScripts?.Dispose();
                BrowserSettings.TabScripts = null;

                GPAL.Forget(this);

                ClosedAt = DateTime.Now;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{BrowserSettings.BrowserType}] left running for another program to pick up", this, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Switches focus to the next browser tab/window in the window list, wrapping around to the first.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector NextWindow
        {
            get
            {
                InAction(false);
                if (true == UseOttoMagic)
                {
                    WindowTuple window = MagicHelper.NextWindow();
                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;
                    BrowserSettings.WindowIdsToURL[window.WindowId] = new WindowInfo { URL = window.Url, ActiveWindow = true };
                    BrowserSettings.CurrentURL = window.Url;
                    MarkWindowTab(window);
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerClient.NextWindow().Execute();
                }
                else // Selenium
                {
                    // the tracked windows
                    var trackedWindows = BrowserSettings.WindowIdsToURL.Keys.ToList();

                    if (trackedWindows.Count <= 1)
                        return this; // Nothing to cycle to

                    // Find the current active one
                    string currentHandle = trackedWindows.FirstOrDefault(h =>
                        BrowserSettings.WindowIdsToURL[h].ActiveWindow);

                    // none marked active: use Selenium's current handle
                    if (string.IsNullOrEmpty(currentHandle))
                        currentHandle = BrowserDriver.CurrentWindowHandle;

                    // Find its index in the list
                    int currentIndex = trackedWindows.IndexOf(currentHandle);
                    if (currentIndex == -1) currentIndex = 0;

                    // Calculate next index (cycle)
                    int nextIndex = (currentIndex + 1) % trackedWindows.Count;
                    string nextHandle = trackedWindows[nextIndex];

                    // Switch to it in Selenium
                    BrowserDriver.SwitchTo().Window(nextHandle);

                    // Update active flags
                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                    BrowserSettings.WindowIdsToURL[nextHandle].ActiveWindow = true;

                    // the window may have navigated since it was recorded
                    BrowserSettings.WindowIdsToURL[nextHandle].URL = BrowserDriver.Url;

                    BrowserSettings.CurrentURL = BrowserDriver.Url;
                }

                GetSetCurrentUrl();
                SetTabGPALUrl();
                return this;
            }
        }

        /// <summary>
        /// Opens a new browser tab/window navigated to <paramref name="URL"/> and switches focus to it.
        /// </summary>
        /// <param name="URL">URL to open in the new window/tab</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector OpenWindow(GPALUrl URL)
        {
            InAction(false);

            // the new window is the page a storage action with no For runs on, with this GPALUrl's definitions
            BrowserSettings.CurrentGPALUrl = URL;

            string urlString = MagicHelper.GetFullUrl(URL?.Url, this, out _areRobotsAllowed);

            if (false == AreRobotsAllowed && true == BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Visiting [{URL?.Url}] is disallowed by robots.txt and your request to honor it. Not going to URL. Workflow will fail.", this, GPALObjectType.Browser);
                URL.ForUrl("https://google.com");
            }

            if (UseOttoMagic)
            {
                WindowTuple window = MagicHelper.OpenWindow(urlString);

                // Deactivate all others
                foreach (var key in BrowserSettings.WindowIdsToURL.Keys.ToList())
                {
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;
                }

                // Add or update the new one
                BrowserSettings.WindowIdsToURL[window.WindowId] = new WindowInfo
                {
                    URL = window.Url,
                    ActiveWindow = true
                };

                BrowserSettings.CurrentURL = window.Url ?? urlString;
                MarkWindowTab(window);
            }
            else if (UsePuppeteer)
            {
                PuppeteerClient.OpenWindow(urlString).Execute();
                BrowserSettings.CurrentURL = urlString;
            }
            else // Selenium
            {
                string originalHandle = BrowserDriver.CurrentWindowHandle;

                // The window the browser opened with is listed too, so CloseWindow can find it by its url
                if (false == BrowserSettings.WindowIdsToURL.ContainsKey(originalHandle))
                    BrowserSettings.WindowIdsToURL[originalHandle] = new WindowInfo { URL = BrowserDriver.Url, ActiveWindow = false };

                // This creates a new top-level window and switches to it automatically
                BrowserDriver.SwitchTo().NewWindow(WindowType.Window);

                string newHandle = BrowserDriver.CurrentWindowHandle;

                tabWindows[newHandle] = newHandle;   // a window is its own window, and the first tab in it

                // Navigate to the desired URL
                BrowserHelper.SeleniumGoToUrl(urlString, BrowserSettings);

                // mark the new window active
                foreach (var key in BrowserSettings.WindowIdsToURL.Keys.ToList())
                {
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;
                }

                BrowserSettings.WindowIdsToURL[newHandle] = new WindowInfo
                {
                    URL = urlString,
                    ActiveWindow = true
                };

                BrowserSettings.CurrentURL = BrowserDriver.Url;
            }

            URL = GetSetCurrentUrl();

            // RespectRobotMetaTags is tested first, so the meta tags are read only when asked for
            if (true == BrowserSettings.RespectRobotMetaTags && false == UrlHelper.CheckRobotMetaTags(URL, this))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Visiting [{BrowserSettings.CurrentURL}] is disallowed by robot meta tags and your request to honor them..");
                URL.ForUrl("https://google.com");

                if (true == UseOttoMagic)
                {
                    TabTuple tabTuple = MagicHelper.GoTo(URL);
                    UpdateActiveTab(tabTuple);
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                    PuppeteerClient.GoTo(URL).Execute();
                }
                else
                {
                    BrowserDriver.SwitchTo().DefaultContent();
                    BrowserHelper.SeleniumGoToUrl(URL, BrowserSettings);
                }
            }

            // the new window keeps the GPALUrl the workflow gave, with its storage definitions, not one made from the
            // url read back
            RecordTabGPALUrl();

            return this;
        }

        /// <summary>
        /// Switches focus to the previous browser tab/window in the window list, wrapping around to the last.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector PreviousWindow
        {
            get
            {
                InAction(false);

                if (UseOttoMagic)
                {
                    WindowTuple window = MagicHelper.PreviousWindow();

                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                    BrowserSettings.WindowIdsToURL[window.WindowId] = new WindowInfo
                    {
                        URL = window.Url,
                        ActiveWindow = true
                    };

                    BrowserSettings.CurrentURL = window.Url;
                    MarkWindowTab(window);
                }
                else if (UsePuppeteer)
                {
                    PuppeteerClient.PreviousWindow().Execute();
                }
                else // Selenium
                {
                    // the tracked windows, in the order they were added
                    var trackedWindows = BrowserSettings.WindowIdsToURL.Keys.ToList();

                    if (trackedWindows.Count <= 1)
                        return this; // Nothing to cycle to

                    // Find the current active one
                    string currentHandle = trackedWindows.FirstOrDefault(h =>
                        BrowserSettings.WindowIdsToURL[h].ActiveWindow);

                    // none marked active: use Selenium's current handle
                    if (string.IsNullOrEmpty(currentHandle))
                        currentHandle = BrowserDriver.CurrentWindowHandle;

                    // Find its index
                    int currentIndex = trackedWindows.IndexOf(currentHandle);
                    if (currentIndex == -1) currentIndex = 0;

                    // Calculate previous index (wrap around to the last one if at the beginning)
                    int previousIndex = (currentIndex - 1 + trackedWindows.Count) % trackedWindows.Count;
                    string previousHandle = trackedWindows[previousIndex];

                    // Switch to it in Selenium
                    BrowserDriver.SwitchTo().Window(previousHandle);

                    // Update active flags
                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                    BrowserSettings.WindowIdsToURL[previousHandle].ActiveWindow = true;

                    // the window may have navigated since it was recorded
                    BrowserSettings.WindowIdsToURL[previousHandle].URL = BrowserDriver.Url;

                    BrowserSettings.CurrentURL = BrowserDriver.Url;
                }

                GetSetCurrentUrl();
                SetTabGPALUrl();
                return this;
            }
        }
        
        /// <summary>
        /// Switches focus to the browser tab/window matching the given URL or window/tab id.
        /// </summary>
        /// <param name="urlOrId">A <see cref="GPALUrl"/>, URL string, or window/tab id identifying the window to switch to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GoToWindow(dynamic urlOrId)
        {
            InAction(false);
            if (true == UseOttoMagic)
            {
                string windowId = null;
                WindowTuple window = null;

                // a GPALUrl goes by its url, so the extension makes the tab holding it active, as Puppeteer and
                // Selenium do; a window found by id would show whatever tab it was on
                if (urlOrId is string urlStr)
                    windowId = BrowserSettings.WindowIdsToURL.FirstOrDefault(x => x.Value.URL.Contains(urlStr) || urlStr.Contains(x.Value.URL)).Key;

                if (true == string.IsNullOrEmpty(windowId))
                    window = MagicHelper.GoToWindow(urlOrId);
                else
                    window = MagicHelper.GoToWindow(windowId);

                // the extension answers NOTOK, read as no window, when no tab holds the url
                if (null == window)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to detect window with URL/ID [{urlOrId}]", this, GPALObjectType.Browser);
                else
                {
                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                    if (false == BrowserSettings.WindowIdsToURL.ContainsKey(window.WindowId))
                        BrowserSettings.WindowIdsToURL[window.WindowId] = new WindowInfo { URL = window.Url, ActiveWindow = true };
                    else
                        BrowserSettings.WindowIdsToURL[window.WindowId].ActiveWindow = true;

                    BrowserSettings.CurrentURL = window.Url;
                    MarkWindowTab(window);

                    // a GPALUrl asked for is the GPALUrl of the tab it found
                    if (urlOrId is GPALUrl url)
                    {
                        BrowserSettings.CurrentGPALUrl = url;
                        RecordTabGPALUrl();
                    }
                }
            }
            else if (true == UsePuppeteer)
            {
                // Puppeteer finds the window by the url of one of its tabs, or by a tab index, makes that tab active
                // and answers its target id, or none when nothing matched. It answers no window id or url, so the
                // url is read back below
                Dictionary<string, string> found = PuppeteerClient.GoToWindow(urlOrId).Execute<Dictionary<string, string>>();

                if (null == found?["targetId"])
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to detect window with URL/ID [{urlOrId}]", this, GPALObjectType.Browser);
                else if (urlOrId is GPALUrl url)
                {
                    // a GPALUrl asked for is the GPALUrl of the tab it found
                    BrowserSettings.CurrentGPALUrl = url;
                    RecordTabGPALUrl();
                }
            }
            else if (true == UseSelenium)
            {
                // a GPALUrl is found by its url, as a url string is: the tab whose url holds it, or the handle given
                string wanted = urlOrId is GPALUrl wantedUrl ? wantedUrl.Url : urlOrId as string;
                string startHandle = null;   // none when the tab the driver was on has closed, as a popup that closes itself does
                string targetHandle = null;

                try
                {
                    startHandle = BrowserDriver.CurrentWindowHandle;
                }
                catch (NoSuchWindowException)
                {
                    // the search below switches from a closed tab as well
                }

                foreach (var handle in BrowserDriver.WindowHandles)
                {
                    BrowserDriver.SwitchTo().Window(handle);
                    if (null != wanted && (handle == wanted || BrowserDriver.Url.Contains(wanted)))
                    {
                        targetHandle = handle;
                        break;
                    }
                }

                if (targetHandle != null)
                {
                    BrowserDriver.SwitchTo().Window(targetHandle);
                    BrowserSettings.CurrentURL = BrowserDriver.Url;

                    // a GPALUrl asked for is the GPALUrl of the tab it found
                    if (urlOrId is GPALUrl url)
                    {
                        BrowserSettings.CurrentGPALUrl = url;
                        RecordTabGPALUrl();
                    }
                }
                else
                {
                    // the search went through every tab; back to the one it started on, if it is still open
                    if (null != startHandle)
                        BrowserDriver.SwitchTo().Window(startHandle);
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to detect window with URL/ID [{urlOrId}]", this, GPALObjectType.Browser);
                }
            }

            GetSetCurrentUrl();

            // the tab now shown takes its GPALUrl; a GPALUrl asked for was recorded for it above
            SetTabGPALUrl();

            return this;
        }

        /// <summary>
        /// Closes the current browser tab/window, or the one matching the given URL or window/tab id, and switches focus to the next available window.
        /// </summary>
        /// <param name="urlOrId">Optional <see cref="GPALUrl"/>, URL string, or window/tab id identifying the window to close. If null, closes the current window.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CloseWindow(dynamic urlOrId = null)
        {
            // No url closes the active window. A GPALUrl is matched by its url, and an int id as the OttoMagic window id
            // it is. Puppeteer matches a url only.
            if (urlOrId is GPALUrl givenGPALUrl)
                urlOrId = givenGPALUrl.Url;

            int? givenWindowId = urlOrId is int givenId ? givenId : (int?)null;
            if (null != givenWindowId)
                urlOrId = givenWindowId.ToString();

            if (urlOrId is string givenUrl && true == string.IsNullOrWhiteSpace(givenUrl))
                urlOrId = null;

            InAction(false);

            BrowserSettings.CurrentGPALUrl = null;

            // Puppeteer has no window to ask for a url once the last one is closed
            bool lastPuppeteerWindowClosed = false;

            if (true == UseOttoMagic)
            {
                // The extension closes the current window when sent nothing, and matches a url against every tab itself.
                // A window id given, or the id of a listed window found for the url, is sent as the id. A closed window
                // is taken off the list.
                string windowIdToClose;
                if (null != givenWindowId)
                    windowIdToClose = BrowserSettings.WindowIdsToURL.ContainsKey(urlOrId) ? (string)urlOrId : null;
                else
                    windowIdToClose = urlOrId is string urlStr ? FindListedWindow(urlStr) : null;

                WindowTuple newActiveWindow;
                if (null != givenWindowId)
                    newActiveWindow = MagicHelper.CloseWindow(givenWindowId.Value);
                else if (true == int.TryParse(windowIdToClose, out int windowId))
                    newActiveWindow = MagicHelper.CloseWindow(windowId);
                else
                    newActiveWindow = MagicHelper.CloseWindow(urlOrId);

                if (null == newActiveWindow)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"No window found to close for [{urlOrId}]", this, GPALObjectType.Browser);
                    return this;
                }

                if (null != windowIdToClose)
                    BrowserSettings.WindowIdsToURL.Remove(windowIdToClose);

                // The extension goes down with the last window, so nothing is left to ask for the url
                if (true == string.IsNullOrEmpty(newActiveWindow.WindowId))
                {
                    BrowserSettings.CurrentURL = null;
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "[CloseWindow()] closed the last window", this, GPALObjectType.Browser);
                    return this;
                }

                // Update active status for the new one
                foreach (var key in BrowserSettings.WindowIdsToURL.Keys.ToList())
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                BrowserSettings.WindowIdsToURL[newActiveWindow.WindowId] = new WindowInfo
                {
                    URL = newActiveWindow.Url,
                    ActiveWindow = true
                };

                BrowserSettings.CurrentURL = newActiveWindow.Url;
                MarkWindowTab(newActiveWindow);
            }
            else if (true == UsePuppeteer)
            {
                // Puppeteer handles its own tracking and current URL update
                if (urlOrId is string urlStr)
                    PuppeteerClient.CloseWindow(MagicHelper.GetFullUrl(urlStr, this, out bool _, true)).Execute();
                else
                    PuppeteerClient.CloseWindow().Execute();

                // The communicator says when the last window closed
                if (true == BrowserSettings.PuppeteerCommunicator._windowSessionsQueue.IsEmpty)
                {
                    lastPuppeteerWindowClosed = true;
                    BrowserSettings.CurrentURL = null;
                }
            }
            else // Selenium
            {
                // Edge's phantom tab is known before the windows are counted, and never counted
                ResolveGhostTabHandle();

                // The window the driver is on, which a close by url stays on unless it is the one closed
                string currentHandle = BrowserDriver.CurrentWindowHandle;
                string handleToClose = urlOrId is string urlStr ? FindListedWindow(urlStr) : currentHandle;

                if (string.IsNullOrEmpty(handleToClose))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"No window found to close for [{urlOrId}]", this, GPALObjectType.Browser);
                    return this;
                }

                bool lastWindow = 1 == BrowserDriver.WindowHandles.Count(h => h != GhostTabHandle);

                // Switch to the target window and close it
                BrowserDriver.SwitchTo().Window(handleToClose);
                BrowserDriver.Close(); // Closes the current window/tab

                // Remove from internal tracking
                BrowserSettings.WindowIdsToURL.Remove(handleToClose);

                // The driver session ends with the last window, so nothing is left to switch to
                if (true == lastWindow)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "[CloseWindow()] closed the last window", this, GPALObjectType.Browser);
                    return this;
                }

                // Selenium stays on the closed window: go back to the window the driver was on, or when that one closed,
                // to the first window left, a tracked one when there is one, never Edge's phantom tab
                ReadOnlyCollection<string> handles = BrowserDriver.WindowHandles;
                string newActiveHandle = handleToClose != currentHandle && handles.Contains(currentHandle)
                    ? currentHandle
                    : BrowserSettings.WindowIdsToURL.Keys.FirstOrDefault(k => handles.Contains(k))
                        ?? handles.FirstOrDefault(h => h != GhostTabHandle);
                BrowserDriver.SwitchTo().Window(newActiveHandle);

                // Update active flags
                foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                if (BrowserSettings.WindowIdsToURL.ContainsKey(newActiveHandle))
                {
                    BrowserSettings.WindowIdsToURL[newActiveHandle].ActiveWindow = true;
                    BrowserSettings.WindowIdsToURL[newActiveHandle].URL = BrowserDriver.Url; // refresh
                }
            }

            if (false == lastPuppeteerWindowClosed)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Closed the window, now on [{UrlHelper.ForMessage(GetSetCurrentUrl())}].", this, GPALObjectType.Browser);
                SetTabGPALUrl();
            }

            return this;
        }

        /// <summary>
        /// The listed window for an OttoMagic window id, a Selenium window handle or a url. A url is completed to a full
        /// one, then matched exactly, then by a window url that contains it, then by a window url it contains, so a
        /// window at https://example.com/ never wins over one at https://example.com/page.
        /// </summary>
        /// <param name="urlOrId">The window id, handle or url</param>
        /// <returns>The window's key in WindowIdsToURL, or null when none matches</returns>
        private string FindListedWindow(string urlOrId)
        {
            string found = urlOrId;

            if (false == BrowserSettings.WindowIdsToURL.ContainsKey(urlOrId))
            {
                string url = MagicHelper.GetFullUrl(urlOrId, this, out bool _, true);

                // a window the extension reported without a url has none to match
                var withUrls = BrowserSettings.WindowIdsToURL.Where(x => false == string.IsNullOrEmpty(x.Value.URL)).ToList();

                found = withUrls.FirstOrDefault(x => UrlHelper.AreEquivalent(x.Value.URL, url)).Key
                    ?? withUrls.FirstOrDefault(x => x.Value.URL.Contains(url)).Key
                    ?? withUrls.FirstOrDefault(x => url.Contains(x.Value.URL)).Key;
            }

            return found;
        }
    }
}
