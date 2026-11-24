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
        /// Navigate back one page
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Back
        {
            get
            {
                InAction(false, false);
                // each engine's own Back; a history.back() script would leave Selenium unaware of the navigation
                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.Back();
                else if (true == BrowserSettings.UsePuppeteer)
                    PuppeteerClient.Back().Execute();
                else
                    BrowserDriver.Navigate().Back();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Went back a page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Navigate forward one page
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Forward
        {
            get
            {
                InAction(false, false);
                // each engine's own Forward; a history.forward() script would leave Selenium unaware of the navigation
                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.Forward();
                else if (true == BrowserSettings.UsePuppeteer)
                    PuppeteerClient.Forward().Execute();
                else
                    BrowserDriver.Navigate().Forward();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Went forward a page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Refresh the current page
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Refresh
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                {
                    MagicHelper.Refresh();
                    Thread.Sleep(500);  // gives the reload time to start, so document ready reads the new page
                }
                else if (true == UsePuppeteer)
                    PuppeteerClient.Refresh().Execute();
                else
                    BrowserDriver.Navigate().Refresh();

                BrowserHelper.CheckDocumentReady(this);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Refreshed [{BrowserSettings.CurrentURL}].", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Navigate to the URL specified in headless mode.<br/>
        /// If no headless browser is open, one will be launched.<br/>
        /// If using an already open browser, navigate in the current tab.<br/>
        /// Use .WithNewTab before .Get to navigate on a new tab.
        /// </summary>
        /// <param name="URL">The page to open</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Get(GPALUrl URL)
        {
            try
            {
                BrowserSettings.UseHeadless = true;

                // configure headless options
                BrowserSettings.OpenPDFExternally = false;  // these two settings will download any pdf file to the specified "download.default_directory" (see WithDownloadDirectory and SetDownloadFilename)
                BrowserSettings.PromptForDownload = false;

                BrowserSettings.OpenInTab = false;
                GoTo(URL);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to GET from [{URL}]", this, GPALObjectType.Browser, ex);
            }

            return this;
        }
        /// <summary>
        /// Navigate to the URL specified.<br/>
        /// If no browser is open, one will be launched.<br/>
        /// If using an already open browser, navigate in the current tab.<br/>
        /// Use .WithNewTab before .GoTo to navigate on a new tab.
        /// </summary>
        /// <param name="URL"></param>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector GoTo(GPALUrl URL)
        {
            bool firstRunSave = firstRun;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Visiting [{URL?.Url}]", this, GPALObjectType.Browser);

            if (BrowserType.FireFox == BrowserSettings.BrowserType &&
                (AutomationEngine.PuppeteerPort == AutomationEngine || AutomationEngine.PuppeteerPortHW == AutomationEngine ||
                AutomationEngine.PuppeteerPipe == AutomationEngine || AutomationEngine.PuppeteerPipeHW == AutomationEngine))
            {
                AutomationEngine steppedDownTo = FirefoxStepDown(BrowserSettings.AutomationEngine);

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Firefox cannot be driven with [{BrowserSettings.AutomationEngine}], so this browser runs on [{steppedDownTo}]", this, GPALObjectType.Browser);

                AutomationEngine = steppedDownTo;
            }

            URL.ForUrl(MagicHelper.GetFullUrl(URL?.Url, this, out _areRobotsAllowed));

            if (false == AreRobotsAllowed && true == BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Visiting [{URL?.Url}] is disallowed by robots.txt and your request to honor it. Not going to URL. Workflow will fail.", this, GPALObjectType.Browser);
                URL.ForUrl("https://google.com");
            }

            // Save the url, GPALUrl and tab so BackOutOfSelfClosedTab can restore them if the navigation is a
            // download.
            string priorUrl = BrowserSettings.CurrentURL;
            GPALUrl priorGPALUrl = BrowserSettings.CurrentGPALUrl;
            TabTuple priorTab = true == UseOttoMagic ? new TabTuple(GetActiveTabId(), priorUrl) : null;
            string priorHandle = null;   // the tab we are leaving, by id, filled in only when we open a new one
            int tabsBefore = -1;

            BrowserSettings.CurrentURL = URL?.Url;
            BrowserSettings.CurrentGPALUrl = URL;

            // a new page starts a new unit of work, so an immediate .InShadowDom or .InFrame starts from the document
            CurrentUOW = new UnitOfWork() { Browser = this };

            // InAction gets no url: a first launch given the url fails on headless Puppeteer
            if (true == InAction(false, false, null))
            {
                try
                {
                    if (true == BrowserSettings.WaitOnNetworkIdle && true == UseSelenium)
                        ApplyNetworkMonitor();

                    CheckAndApplyStealth();
                    SetReferrer();
                    SetUserAgent();

                    // Present the credential once, before the first navigation; it holds for the session.
                    if (null != BrowserSettings.Credentials && null == BrowserSettings.CredentialsPresented)
                        BrowserHelper.PresentCredentials(this, URL);

                    BrowserHelper.CheckDocumentReady(this);

                    // Navigate the current tab for a browser GPAL has not launched yet, or when no new tab is asked for.
                    // A browser reached through the debug port or GPALRestAPI is already open, so it gets a new tab when
                    // one is asked for.
                    if ((null == BrowserLaunched && false == BrowserSettings.UseExistingBrowser && false == BrowserSettings.AttachedRestApi) || false == BrowserSettings.OpenInTab)
                    {

                        if (true == UseOttoMagic)
                        {
                            //if (false == firstRunSave)  // url passed on command line
                            {
                                //MagicHelper.InMainDom();
                                TabTuple tabTuple = MagicHelper.GoTo(URL);
                                UpdateActiveTab(tabTuple);
                            }
                            //else
                            //    UpdateActiveTab(MagicHelper.NextTab());     // get the active tab tuple, no other tabs, will return current
                        }
                        else if (true == UsePuppeteer)
                        {
                            PuppeteerClient.SwitchToDefaultContent().Execute();
                            //if (false == firstRunSave)  // url passed on command line
                            PuppeteerClient.GoTo(URL).Execute();
                        }
                        else
                        {
                            BrowserDriver.SwitchTo().DefaultContent();
                            BrowserHelper.SeleniumGoToUrl(URL, BrowserSettings);
                        }
                        if (null == BrowserLaunched)
                        {
                            BrowserLaunched = DateTime.Now;
                            tabsWeOpened++;
                        }
                    }
                    else
                    {
                        // what the browser has before we add ours, so we can tell afterwards whether ours survived
                        tabsBefore = RealTabCount();
                        priorHandle = true == UseSelenium ? BrowserDriver.CurrentWindowHandle : null;   // to get back by id, not by position
                        tabsWeOpened++;
                        if (true == UseOttoMagic)
                        {
                            TabTuple tabTuple = MagicHelper.NewTab(URL);

                            // Firefox answers about:blank for a tab that has not started its page yet. Recording that
                            // as the current url would make the ready check below skip the page, so the asked-for url
                            // is recorded instead.
                            if (true == string.IsNullOrEmpty(tabTuple?.Url) || true == UrlHelper.AreEquivalent(tabTuple.Url, "about:blank"))
                                tabTuple.Url = URL;
                            GoToTab(tabTuple);

                            // Firefox opens the tab on about:blank, which reports itself complete before the page it was
                            // opened for starts loading, so the ready check below would pass on a blank tab. Wait, within
                            // the document-ready time, for the tab to leave it.
                            if (false == UrlHelper.AreEquivalent(URL.Url, "about:blank"))
                            {
                                System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();

                                while (waited.ElapsedMilliseconds < BrowserSettings.WaitForDocumentReadyTimeoutMs
                                       && true == string.Equals((MagicHelper.GetCurrentUrl() ?? string.Empty).Trim('"'), "about:blank", StringComparison.OrdinalIgnoreCase))
                                    Thread.Sleep(BrowserHelper.WaitForValuePollMs);   // 250 ms, the document-ready check's own pace
                            }
                        }
                        else if (true == UsePuppeteer)
                        {
                            // NewTab already activates the new tab. NextTab here would wrap back to the original
                            // with two tabs, which would then be navigated and closed.
                            PuppeteerClient.NewTab().Execute();
                            PuppeteerClient.GoTo(URL).Execute();
                        }
                        else
                        {
                            BrowserDriver.SwitchTo().NewWindow(WindowType.Tab);
                            RecordTabWindow(BrowserDriver.CurrentWindowHandle, priorHandle);   // a new tab of the window we were in
                            BrowserHelper.SeleniumGoToUrl(URL, BrowserSettings);
                        }
                    }

                    // Check for a failed load: Selenium leaves ServerResponseCode at -1, Puppeteer returns errorText.
                    // OttoMagic raises CallOnFail from get-ready-status instead. The workflow continues, and
                    // CallIfNotFound runs unless a CallOnFail handler decides otherwise.
                    bool navigationFailed = (true == UseSelenium && -1 == BrowserSettings.ServerResponseCode)
                        || (true == UsePuppeteer && false == string.IsNullOrEmpty(PuppeteerCommunicator.LastNavigationError));

                    // Only a failure the engine reports. Selenium's -1 is any exception, a timeout on a slow page
                    // included, so it says nothing about what came back.
                    NavigationFailedAt = true == UsePuppeteer && false == string.IsNullOrEmpty(PuppeteerCommunicator.LastNavigationError)
                                       ? DateTime.Now
                                       : (DateTime?)null;

                    if (true == UseSelenium && -1 == BrowserSettings.ServerResponseCode)
                        RaiseOnFail(GPALFailure.Navigation, $"[{URL?.Url}] did not load.");
                    else if (true == UsePuppeteer && false == string.IsNullOrEmpty(PuppeteerCommunicator.LastNavigationError))
                        RaiseOnFail(GPALFailure.Navigation, $"[{URL?.Url}] did not load. [{PuppeteerCommunicator.LastNavigationError}]");

                    // A download url loads no page, so the browser silently closes the tab opened for it. If the tab
                    // count is unchanged from before the new tab, back out. Comparing counts cancels out tabs GPAL
                    // did not open.
                    if (-1 != tabsBefore && RealTabCount() == tabsBefore)
                        BackOutOfSelfClosedTab(URL, priorUrl, priorGPALUrl, priorTab, priorHandle);
                    else
                    {
                        BrowserHelper.CheckDocumentReady(this);

                        // RespectRobotMetaTags is tested first, so the meta tags are read only when asked for
                        if (true == BrowserSettings.RespectRobotMetaTags && false == UrlHelper.CheckRobotMetaTags(URL, this))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Visiting [{BrowserSettings.CurrentURL}] is disallowed by robot meta tags and your request to honor them..");
                            URL.ForUrl("https://google.com");

                            if (true == UseOttoMagic)
                            {
                                //MagicHelper.InMainDom();
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
                        else
                        {
                            BrowserSettings.CurrentURL = URL.Url;

                            // RaiseOnFail publishes nothing without a handler, so warn here or a failed load is
                            // logged as loaded.
                            if (true == navigationFailed)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.CurrentURL}] did not load. Continuing against whatever the browser is showing.");
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{BrowserSettings.CurrentURL}] loaded.");
                        }

                        // the tab the page loaded in, a new one included, keeps this GPALUrl
                        RecordTabGPALUrl();

                        // A page is loaded, so the dialog override can be applied to it, not only to later pages.
                        _navigated = true;
                    }
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to navigate to [{URL?.Url}]", this, GPALObjectType.Browser, ex);
                }

                //int retries = 3;

                //if (false == URL.Contains(GetSetCurrentUrl()) && 0 < retries--)
                //    Thread.Sleep(500);

                BrowserSettings.OpenInTab = false;  // .WithNewTab applies only once, so it must appear before each .GoTo
            }
            else // we had a driver initialization falure
                throw new GPALException("Driver initialization error. Please see mesages.");

            return this;
        }
        // When the last GoTo's navigation failed by the engine's own report (Puppeteer's error text); null when it did
        // not. A link that is a file fails this way on Puppeteer (net::ERR_ABORTED) as the browser turns it into a
        // download.
        internal DateTime? NavigationFailedAt { get; private set; }
        /// <summary>
        /// The lines of the current site's robots.txt, as parsed by <see cref="WithRespectRobotMetaTags"/>/<see cref="WithObeyRobotsTxt"/>.
        /// </summary>
        public List<string> RobotsTxt
        {
            get => BrowserSettings.RobotsTxt;
            internal set => BrowserSettings.RobotsTxt = value;
        }
        /// <summary>
        /// Refreshes <see cref="BrowserSettings.CurrentURL"/> from the active engine (OttoMagic, Puppeteer, or Selenium)
        /// and invalidates cached selector results if the URL has changed.
        /// </summary>
        /// <returns>The current page URL</returns>
        internal string GetSetCurrentUrl()
        {
            string currentUrl = null;

            try
            {
                if (true == BrowserSettings.UseOttoMagic)
                    currentUrl = MagicHelper.GetCurrentUrl();
                else if (true == UsePuppeteer)
                    currentUrl = BrowserSettings.Browser.PuppeteerClient.GetCurrentUrl().Execute<dynamic>();
                else
                    // read the url of the tab Selenium has active; CurrentTabIdx can be stale and name another tab
                    currentUrl = BrowserDriver.SwitchTo().Window(BrowserDriver.CurrentWindowHandle).Url;

                currentUrl = currentUrl?.Trim('"');

                if (false == UrlHelper.AreEquivalent(currentUrl, BrowserSettings.CurrentURL))
                {
                    BrowserSettings.CurrentURL = currentUrl;

                    // invalidate cached elements, we are on a new page
                    foreach (Selector selector in this.CurrentUOW.WithSelectorList)
                    {
                        selector.WebSelectorFoundResults = null;
                        selector.WebSelectorMatchedResults = null;
                    }
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to get URL, returning [{BrowserSettings.CurrentURL}]", this, GPALObjectType.Browser, ex);
            }
            return BrowserSettings.CurrentURL;
        }
    }
}
