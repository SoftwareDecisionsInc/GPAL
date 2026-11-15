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
    /// <summary>
    /// Browser object that contains the fluent methods to create your Browser workflow.<br/>
    /// Instatiated using GPAL.Browser
    /// </summary>
    /// <example>
    /// <code>
    ///     Selector inputSearchSelector = (Selector)GPAL.Selector
    ///         .WithName("SearchInput")
    ///         .WithCSS("#gh-ac")
    ///         .WithXPath("//*[@id=\"gh-ac\"]")
    ///         .MatchPlaceholder("Search for anything");
    ///
    ///     Browser myBrowser = (Browser)GPAL.Browser
    ///         .WithBrowserType(BrowserType.Chrome)
    ///         .WithDriverLocation(@"c:\drivers")
    ///         .GoTo("https://www.ebay.com/")
    ///         .WithWaitOnDocumentReady(true)
    ///         .WithSelector(inputSearchSelector)
    ///         .FillInFrom("Find me");
    /// </code>
    /// </example>
    public partial class Browser : IBrowser
    {
        /// <summary>
        /// Delegate callback for the CallIfFound/CallIfNotFound EventHandlers which will be invoked when a selector is found/not found<br/>
        /// CallIf handlers may be defined at two levels: At each selector or for a Unit of Work (UOW) [will be invoked for each Selector in the UOW]<br/>
        /// A UOW is defined as all Selectors between actions (first action is Goto)<br/>
        /// More than one callback handler can be defined and subsequent handlers will only be invoked based upon return code values
        /// </summary>
        /// <example>
        /// <code>
        /// public static int FoundAllCookies(Browser browser, List&lt;GPALElement&gt; foundElements, List&lt;GPALElement&gt; matchedElements, Selector selector, bool matchedAll)
        /// {
        ///     int retVal = 0;
        ///     foreach (GPALElement webElement in foundElements)
        ///     {
        ///         if (true == webElement.Text.ToLower().Contains("accept"))   // "Accept all cookies"
        ///         {
        ///             webElement.Click();
        ///             selector.Browser.RemoveCallIfHandlerEverywhere(FoundAllCookies);
        ///             selector.Remove();
        ///             retVal = 1;
        ///         }
        ///     }
        ///     return retVal;
        /// }
        /// </code>
        /// </example>
        /// <param name="browser">The current browser running</param>
        /// <param name="foundElements">All found elements</param>
        /// <param name="matchedElements">All matched elements.<br/>If a 'Match' criteria is set on a Selector, otherwise all found</param>
        /// <param name="selector">The Selector used</param>
        /// <param name="matchedAll">true - all elements matched in foundElements<br/>false - matchedElements out of foundElements</param>
        /// <returns>
        /// try the next handler in the chain based upon handler return value<br/>
        /// 0 = not handled, bubble to next handler<br/>
        /// 1 = handled and continue with the program<br/>
        /// 2 = try next selector
        /// -1 = terminate program<br/>
        /// </returns>
        public delegate CallIfStatus CallIfDelegate(IBrowser browser, List<IGPALElement> foundElements, List<IGPALElement> matchedElements, Selector selector, bool matchedAll);
        /// <summary>
        /// Delegate callback for the CallAfterFillIn EventHandler which will be invoked after each row of tokens is consumed.<br/>
        /// Invoked whenever FillInFrom/FilllInFromDatabase/FillInFrom are used
        /// </summary>
        /// <example>
        /// <code>
        /// public static int CallAFterFillIn(Browser myBrowser, GPALGrid&lt;string&gt; tokens, int tokenIdx) // text filled in search box, click 'Search' button
        /// {
        ///     Selector searchButton = (Selector)GPAL.Selector
        ///     .WithName("SearchButton")
        ///     .WithCSS("#gh-btn")
        ///     .WithXPath("//*[@id=\"gh-btn\"]")
        ///     .MatchText("Search");
        ///     
        ///     myBrowser
        ///         .WithSelector(searchButton)
        ///  //                .WithSelector(searchButton).WithHardware     // override selector default of Selenium
        ///  //                .WithSelector(searchButton).WithJavaScript   // override selector default of Selenium
        ///     .LeftClick();
        /// }
        /// </code>
        /// </example>
        /// <param name="browser">THe GPAL Browser object being filled in</param>
        /// <param name="tokens">The current GPALGrid&lt;string&gt; of tokens</param>
        /// <param name="tokenIdx">The current token row index</param>
        /// <returns>-1 if the handler requests the application to exit, otherwise the return value is ignored</returns>
        public delegate CallIfStatus CallAfterFillInDelegate(IBrowser browser, IGPALGrid<string> tokens, int tokenIdx);
        /// <summary>
        /// Delegate callback for the CallAfterFetch handler declared on a GPALRequest, invoked after each row of
        /// tokens has been fetched, with every page that row produced.<br/>
        /// Invoked whenever .Fetch is used, once even when there is no token source.
        /// </summary>
        /// <example>
        /// <code>
        /// public static CallIfStatus ProcessSearch(IBrowser myBrowser, List&lt;string&gt; results, IGPALGrid&lt;string&gt; tokens, int tokenIdx)
        /// {
        ///     foreach (string body in results)
        ///         Debug.WriteLine($"[{tokens[tokenIdx][0]}] returned [{body.Length}] characters.");
        ///
        ///     return CallIfStatus.Continue;
        /// }
        /// </code>
        /// </example>
        /// <param name="browser">The GPAL Browser object that issued the requests</param>
        /// <param name="results">The response bodies for this row, one per page, in the order requested</param>
        /// <param name="tokens">The current GPALGrid&lt;string&gt; of tokens, null when there is no token source</param>
        /// <param name="tokenIdx">The current token row index</param>
        /// <returns>-1 if the handler requests the application to exit, otherwise the return value is ignored</returns>
        public delegate CallIfStatus CallAfterFetchDelegate(IBrowser browser, List<string> results, IGPALGrid<string> tokens, int tokenIdx);

        /// <summary>
        /// Delegate for CallOnFail, called when an operation fails so the workflow can decide.<br/>
        /// Without a handler GPAL reports the failure and continues. Return NotHandled to continue, Handled if you
        /// dealt with it, or Terminate to end the run.
        /// </summary>
        /// <param name="browser">The failing browser; tells workflows apart under one global handler.</param>
        /// <param name="failure">What failed</param>
        /// <param name="detail">What the engine said about it, including the url where that applies</param>
        /// <returns>Terminate to end the run, otherwise whether the handler dealt with it</returns>
        public delegate CallIfStatus CallOnFailDelegate(IBrowser browser, GPALFailure failure, string detail);

        /// <summary>
        /// Return a GPAL.Browser object so that GPAL.Browser does not have to be cast.
        /// </summary>
        /// <returns>A GPAL.Browser object</returns>
        /// <example>
        /// <code>
        /// Browser browser = GPAL.Browser
        ///     .WithBrowserType(BrowserType.FireFox)
        ///     .WithWaitOnDocumentReady(true)
        ///     .WithLoadImages(true)
        ///     .WithOverwriteExistingFile(false)
        ///     .GoTo("https://www.ebay.com/")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        public IBrowser ToGPALObject()
        {
            return this;
        }

        BrowserSettings _browserSettings = null;
        /// <summary>
        /// Persistent unit of work, used for selectors/state that should survive across UnitOfWork() calls.
        /// </summary>
        internal UnitOfWork persistentUOW { get; set; } = null;
        /// <summary>
        /// Number of tabs/windows opened so far in this browser session.
        /// </summary>
        // When the browser was launched; null until then.
        internal DateTime? BrowserLaunched { get; set; } = null;

        /// <summary>
        /// True once the browser was started or reached: Selenium has its driver, the other engines a browser
        /// process, a GPALRestAPI attachment or an existing browser.
        /// </summary>
        private bool HasBrowser => true == UseSelenium
                             ? null != BrowserDriver
                             : null != BrowserSettings.Process || true == BrowserSettings.AttachedRestApi || true == BrowserSettings.UseExistingBrowser;
        // When Close ran. A closed browser is done; every action after it throws.
        internal DateTime? ClosedAt { get; private set; } = null;

        // Set while Close runs, so WorkflowSetup does not bring a window to the front that Close is about to close or
        // that may already be gone.
        private bool inClose = false;

        // Set when another GPAL browser on the same browser ended it, so this one's Close puts back what it changed
        // without asking a browser that is gone to close.
        private bool browserEnded = false;

        // Set once we have published that this engine cannot capture calls, so it is said once.
        private bool captureUnavailableReported = false;

        // Set once the hidden desktop engine choice is published, since WorkflowSetup runs on every action.
        private bool hiddenDesktopReported = false;

        // Set once we have checked, after launch, that the browser's windows opened on the hidden desktop.
        private bool hiddenDesktopChecked = false;

        // Set once we have published that hardware input is off on this hidden desktop; nearly every selector hits
        // it.
        private bool hiddenDesktopHardwareReported = false;

        // Set once we have published that hardware input cannot reach a browser driven through GPALRestAPI.
        private bool restApiHardwareReported = false;

        // Tabs GPAL opened, not tabs the browser has. ResolveGhostTabHandle finds Edge's phantom tab when the
        // browser has more. Counted only where GPAL opens a tab; no engine is queried.
        internal int tabsWeOpened { get; set; } = 0;
        /// <summary>
        /// Edge only: url captured at launch (before we navigate) that Edge's preloaded New Tab Page
        /// (a hidden background window that appears asynchronously as an extra WindowHandle) will carry.
        /// Used as a runtime signature to positively identify the phantom tab by url - not by position
        /// (which races the async insert) and not by a hardcoded string.
        /// </summary>
        internal string GhostTabUrl { get; set; } = null;
        /// <summary>
        /// Edge only: the resolved window handle of the phantom New Tab Page tab, once matched by GhostTabUrl.
        /// Tab-navigation always skips this handle so our sequential index tracking stays correct.
        /// </summary>
        internal string GhostTabHandle { get; set; } = null;
        /// <summary>
        /// Index of the currently active browser tab/window.
        /// </summary>
        public int CurrentTabIdx { get; internal set; } = 0;

        // Maps each tab handle to its window handle. Selenium does not say which window a tab is in, so GPAL records
        // it once, when it opens the tab. A tab GPAL did not open joins the current window when first seen, so
        // cycling still reaches it.
        private readonly Dictionary<string, string> tabWindows = new Dictionary<string, string>();

        internal bool _areRobotsAllowed = true;
        /// <summary>
        /// True if the current page's robots.txt/meta tags allow automation, as determined by
        /// <see cref="WithRespectRobotMetaTags"/>.
        /// </summary>
        public bool AreRobotsAllowed { get => _areRobotsAllowed; internal set => _areRobotsAllowed = value; }

        /// <summary>
        /// Browser object that contains the fluent methods to create your Browser workflow.<br/>
        /// Instatiated using GPAL.Browser
        /// </summary>
        /// <example>
        /// <code>
        ///     Selector inputSearchSelector = (Selector)GPAL.Selector
        ///         .WithName("SearchInput")
        ///         .WithCSS("#gh-ac")
        ///         .WithXPath("//*[@id=\"gh-ac\"]")
        ///         .MatchPlaceholder("Search for anything");
        ///
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        internal Browser()
        {
            // GPAL's own settings are each browser's defaults, which its own With... calls replace
            this.BrowserSettings = new BrowserSettings(this)
            {
                DriverLocation = GPAL.DriverLocation,
                DownloadTimeoutInSec = GPAL.GPALSettings.DownloadTimeoutInSec,
                AutomationEngine = GPAL.GPALSettings.AutomationEngine
            };

            CurrentUOW = new UnitOfWork() { Browser = this };
            persistentUOW = new UnitOfWork() { Browser = this };
            WorkflowManager = new WorkflowManager(this);
        }

        /// <summary>
        /// True once the automation engine can be talked to: Selenium has a driver, Puppeteer has a url, and
        /// OttoMagic has finished the GPALRestAPI port handshake.
        /// </summary>
        private bool EngineReachable
        {
            get
            {
                bool retVal;

                if (true == UseOttoMagic)
                    retVal = false == string.IsNullOrEmpty(BrowserSettings.RestApiBaseUrl);
                else if (true == UsePuppeteer)
                    retVal = false == string.IsNullOrEmpty(BrowserSettings.PuppeteerUrl);
                else // selenium
                    retVal = null != BrowserSettings.BrowserDriver;

                return retVal;
            }
        }

        // True once a navigation has finished. Not BrowserSettings.CurrentURL, which GoTo sets before navigating.
        private bool _navigated = false;
        private bool _started = false;
        /// <summary>
        /// True once the browser/automation engine has been started.
        /// </summary>
        internal bool Started
        {
            get => _started;
            set => _started = value;
        }
        /// <summary>
        /// HTTP status code of the most recent page navigation/response.
        /// </summary>
        public int ServerResponseCode
        {
            get => BrowserSettings.ServerResponseCode;
            set => BrowserSettings.ServerResponseCode = value;
        }

        /// <summary>
        /// The URL of the page currently loaded in the browser.
        /// </summary>
        public string CurrentUrl
        {
            get => BrowserSettings.CurrentURL;
            internal set => BrowserSettings.CurrentURL = value;
        }
        /// <summary>
        /// True if this Browser is using the Selenium automation engine.
        /// </summary>
        public bool UseSelenium
        {
            get => BrowserSettings.UseSelenium;
        }
        /// <summary>
        /// True if this Browser is using the Puppeteer automation engine.
        /// </summary>
        public bool UsePuppeteer
        {
            get => BrowserSettings.UsePuppeteer;
        }
        /// <summary>
        /// True if this Browser is using the OttoMagic automation engine.
        /// </summary>
        public bool UseOttoMagic
        {
            get => BrowserSettings.UseOttoMagic;
        }
        /// <summary>
        /// The OS process hosting the browser, if started and tracked by GPAL.
        /// </summary>
        public Process Process
        {
            get => BrowserSettings.Process;
        }
        /// <summary>
        /// The automation engine (OttoMagic, Puppeteer, or Selenium) used by this Browser.
        /// </summary>
        public AutomationEngine AutomationEngine
        {
            get => BrowserSettings.AutomationEngine;
            internal set => BrowserSettings.AutomationEngine = value;
        }
        /// <summary>
        /// True if the most recent download action successfully downloaded a file. Null if no download has been attempted.
        /// </summary>
        public bool? FileDownloaded
        {
            get
            {
                return BrowserSettings.FileDownloaded;
            }

            set
            {
                BrowserSettings.FileDownloaded = value;
            }
        }

        /// <summary>
        /// Return the current browser type.
        /// </summary>
        public BrowserType BrowserType
        {
            get
            {
                return BrowserSettings.BrowserType;
            }
        }
        /// <summary>
        /// The unit of work currently being built/executed by the fluent workflow.
        /// </summary>
        internal UnitOfWork CurrentUOW { get; set; } = null;
        /// <summary>
        /// Manages the queue of units of work for this browser and runs them via .Run.
        /// </summary>
        internal WorkflowManager WorkflowManager { get; set; }
        /// <summary>
        /// A name you provide to be used when publishing events.
        /// </summary>
        internal string Name
        {
            get
            {
                return BrowserSettings.BrowserName;
            }

            set
            {
                BrowserSettings.BrowserName = value;
            }
        }
        /// <summary>
        /// The browser driver controlling the current Browser.<br/>
        /// Only available after the first .GoTo<br/>
        /// <b>NOTE:</b> Only for extended Selenium control access. Be careful.
        /// </summary>
        public IWebDriver BrowserDriver
        {
            get
            {
                return BrowserSettings.BrowserDriver;
            }

            internal set
            {
                BrowserSettings.BrowserDriver = value;
            }
        }
        /// <summary>
        /// Base url of this browser's GPALRestAPI, e.g. http://localhost:3117/, from the port it reported at startup.
        /// Null unless the engine is OttoMagic.
        /// </summary>
        public string RestApiBaseUrl
        {
            get
            {
                return BrowserSettings.RestApiBaseUrl;
            }
        }
        /// <summary>
        /// The settings backing this Browser, including configuration, state, and engine handles.
        /// </summary>
        public BrowserSettings BrowserSettings
        {
            get
            {
                return _browserSettings;
            }

            internal set
            {
                _browserSettings = value;
            }
        }
        /// <summary>
        /// The folder downloaded files are saved to, as configured via <see cref="WithDownloadLocation"/>.
        /// </summary>
        internal string DownloadLocation
        {
            get
            {
                return BrowserSettings.DownloadLocation;
            }
        }
        /// <summary>
        /// Communicator used to send/receive messages to the Puppeteer engine for this Browser.
        /// </summary>
        public PuppeteerCommunicator PuppeteerCommunicator
        {
            get => BrowserSettings.PuppeteerCommunicator;
            internal set => BrowserSettings.PuppeteerCommunicator = value;
        }

        public bool IsAlive
        {
            get
            {
                // GPAL did not launch this browser, so ask its GPALRestAPI, which exits with the browser.
                if (true == BrowserSettings.AttachedRestApi)
                    return BrowserHelper.RestApiAnswering(BrowserSettings.RestApiBaseUrl);

                if (BrowserSettings.Process != null)
                    return !BrowserSettings.Process.HasExited;
                if (BrowserSettings.ServiceDriverPid != 0)
                {
                    try { System.Diagnostics.Process.GetProcessById(BrowserSettings.ServiceDriverPid); return true; }
                    catch (ArgumentException) { return false; }
                }
                return true;
            }
        }
        /// <summary>
        /// The Puppeteer client used to issue commands when this Browser is using the Puppeteer engine.
        /// </summary>
        public IPuppeteerClient PuppeteerClient
        {
            get => BrowserSettings.PuppeteerClient;
            internal set => BrowserSettings.PuppeteerClient = value;
        }
        /// <summary>
        /// The MagicHelper used to issue commands when this Browser is using the OttoMagic engine.
        /// </summary>
        public MagicHelper MagicHelper
        {
            get => (MagicHelper)BrowserSettings.MagicHelper;
        }

        /// <summary>
        /// REST client for this browser's GPALRestAPI. On a non-OttoMagic engine this publishes an error now rather
        /// than failing at the first call.
        /// </summary>
        public IRESTClient OttoMagicClient
        {
            get
            {
                if (false == BrowserSettings.UseOttoMagic)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"No OttoMagic REST API on [{BrowserSettings.AutomationEngine}]. Only the OttoMagic engines run one. Use PuppeteerClient on a Puppeteer engine, or the browser's own actions",
                        this, GPALObjectType.Browser);

                return MagicHelper.Client;
            }
        }

        /// <summary>
        /// The hidden desktop this browser runs on, or null if on your screen.
        /// </summary>
        public IHiddenDesktop HiddenDesktop => true == BrowserSettings.HiddenDesktop
            ? new GPALHiddenDesktop(BrowserSettings.HiddenDesktopName)
            : null;
        /// <summary>
        /// The version of the browser being automated.
        /// </summary>
        public string BrowserVersion
        {
            get => BrowserSettings.Version;
            internal set => BrowserSettings.Version = value;
        }

        // Result caches, so a Save* reuses what a preceding Get* produced. The sitemap is site-wide, keyed by
        // origin; the digest and hydration results are per page, keyed by the full URL. Each browser has its own;
        // ClearResultCache() empties them.
        private readonly Dictionary<string, List<string>> _sitemapCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LLMDigestResult> _digestCache = new Dictionary<string, LLMDigestResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NextJsHydrationResult> _hydrationCache = new Dictionary<string, NextJsHydrationResult>(StringComparer.OrdinalIgnoreCase);
    }
}
