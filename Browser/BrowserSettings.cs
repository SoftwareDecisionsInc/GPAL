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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;
using System.Drawing;
using System.Xml;
using System.Diagnostics;
using System.IO.Pipes;
using System.IO;
using Newtonsoft.Json.Linq;

namespace GenerallyPositive.Browser
{
    internal class TabInfo
    {
        public string URL { get; set; }
        public bool ActiveTab { get; set; }

        public TabInfo() { }
    }
    internal class URLInfo
    {
        public int TabId { get; set; }
        public bool ActiveTab { get; set; }

        public URLInfo() { }
    }
    public class BrowserSettings
    {
        internal IBrowser Browser { get; set; }
        // Suppresses script-failure events for this browser only. Set while waiting on document ready, where a
        // loading page rejecting script is expected
        internal bool SuppressJavaScriptFailures { get; set; } = false;

        internal bool ObeyRobotsTxt { get; set; } = false;
        internal bool RespectRobotMetaTags { get; set; } = false;

        internal List<string> RobotsTxt { get; set; }
        internal Dictionary<int, TabInfo> TabIdsToURL { get; set; } = new Dictionary<int, TabInfo>();    // tabId > url, activeTab
        internal Dictionary<string, URLInfo> URLsToTabId { get; set; } = new Dictionary<string, URLInfo>();    // ulr > tabId, activeTab
        internal Dictionary<string, WindowInfo> WindowIdsToURL { get; set; } = new Dictionary<string, WindowInfo>();
        // name of this browser instance, set by GPAL factory
        internal string BrowserName { get; set;  }  = null;
        internal string DriverLocation { get; set;  } = null;
        internal int ServiceDriverPid { get; set; } = 0;
        // load images on a web page?
        internal bool LoadImages { get; set; } = true;
        // open pdf documents in external program?
        internal bool OpenPDFExternally { get; set; } = false;
        // open file save dialog; off, so downloads go straight to the download folder
        internal bool PromptForDownload { get; set; } = false;

        // How to answer alert, confirm and prompt dialogs. null leaves them open and blocking, as for a person;
        // set, the browser answers them unseen.
        internal bool? DialogsAccepted { get; set; } = null;

        // Text returned to a prompt when dialogs are accepted. null keeps the prompt's default.
        internal string DialogText { get; set; } = null;

        // Failure handlers for this browser. Empty falls through to the global ones.
        internal List<Browser.CallOnFailDelegate> CallOnFail { get; set; } = new List<Browser.CallOnFailDelegate>();

        // Credentials from .WithCredentials, sent once at the first navigation. Basic, proxy and bearer last for the
        // session.
        internal ICredentials Credentials { get; set; }
        internal DateTime? CredentialsPresented { get; set; }
        // when performing an action, do we wait on document.ready?
        internal bool WaitOnDocumentReady { get; set; } = true;
        internal bool WaitOnNetworkIdle { get; set; } = false;
        internal int NetworkIdleTimeoutMs { get; set; } = 30_000;
        internal int NetworkIdleMaxConnections { get; set; } = 0;
        internal int NetworkIdlePruneMs { get; set; } = 3_000;
        internal bool TempProfileCreated { get; set; } = false;     // so we know to delete it later
        internal bool DownloadBehaviorSet { get; set; } = false;    // we pointed the browser's downloads at our own directory, so it gets handed back when we close
        internal Dictionary<string, JToken> PreviousDownloadPreferences { get; set; } = null;   // the supplied profile's download settings as we found them, put back once the browser is gone

        internal FirefoxProfileSnapshot PreviousFirefoxPreferences { get; set; } = null;    // the same for a supplied firefox profile's user.js and prefs.js
        /// <summary>
        /// profile directory used by ottomagic for all operations
        /// user specificed or the default directory
        /// </summary>
        internal string ProfileDataDirectory { get; set; }
        internal string ProfileName { get; set; }
        internal string ProfileUserName { get; set; }
        // current browser type
        internal BrowserType BrowserType { get; set; } = BrowserType.Chrome;

        // whether to connect to existing browser to start controlling something on-screen
        internal bool UseExistingBrowser { get; set; } = false;
        internal int? ExistingBrowserPort { get; set; } = null;

        // for using/connecting to an already open browser
        internal int? DebugPort { get; set; }
        internal bool DebugPipe { get; set; }
        public PipeStream InboundPipe { get; set; } // Parent writes, Chrome reads
        public PipeStream OutboundPipe { get; set; } // Parent reads, Chrome writes
        public PipeStream ErrorPipe { get; set; } // Parent error, Chrome writes
        internal string PuppeteerUrl { get; set; }

        // Set by .CaptureCalls, which can be called before the browser exists. Read when the PuppeteerCommunicator
        // is created, so recording starts with the first request.
        internal bool CaptureCalls { get; set; } = false;

        // Set by .WithCallFilter. Only calls whose url contains it are recorded, so a long run does not record
        // every image.
        internal string CallFilter { get; set; }

        PuppeteerCommunicator _puppeteerCommunicator = null;

        // True when the PuppeteerCommunicator exists. Checks the field because the getter creates one, and creating
        // it before the browser is up connects to a debug port nothing is listening on.
        internal bool HasPuppeteerCommunicator => null != _puppeteerCommunicator;

        internal PuppeteerCommunicator PuppeteerCommunicator
        {
            get
            {
                if (null == _puppeteerCommunicator && true == UsePuppeteer)
                {
                    // A pipe connection has no url
                    if (null == PuppeteerUrl && false == DebugPipe)
                    {
                        // No PuppeteerUrl means not launched. The default port belongs to the run's first browser,
                        // so using it would silently drive that browser. Use a free port, so the connection fails
                        // and is reported.
                        int ours = BrowserHelper.FindFreePort();

                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"A Puppeteer connection was asked for before [{BrowserType}] had a debugging port, so it is pointed at [{ours}] where nothing is listening rather than at another browser", this, GPALObjectType.Browser);

                        PuppeteerUrl = $"http://localhost:{ours}";
                    }

                    // Pass CaptureCalls to the constructor; the communicator attaches to the page inside it, so
                    // setting it afterwards misses that page.
                    _puppeteerCommunicator = new PuppeteerCommunicator(PuppeteerUrl, Browser, DebugPipe, CaptureCalls);
                }
                else if (null == _puppeteerCommunicator && false == UsePuppeteer)
                {
                    // Not Puppeteer, but the getter still runs, for example in the debugger. Create one empty
                    // PuppeteerCommunicator and keep it.
                    _puppeteerCommunicator = new PuppeteerCommunicator();
                }

                return _puppeteerCommunicator;
            }
            set
            {
                _puppeteerCommunicator = value;
            }
        }
        internal IPuppeteerClient PuppeteerClient
        {
            get => PuppeteerCommunicator?.PuppeteerClient;
            set => PuppeteerCommunicator.PuppeteerClient = value;
        }
        internal bool FullScreen { get; set; } = false;
        internal bool Maximize { get; set; } = false;
        internal bool Minimize { get; set; } = false;
        public bool UseHardware { get => AutomationEngine.OttoMagicHW == AutomationEngine || AutomationEngine.PuppeteerPipeHW == AutomationEngine || AutomationEngine.PuppeteerPortHW == AutomationEngine || AutomationEngine.SeleniumHW == AutomationEngine; }
        public bool UseJavaScript { get => AutomationEngine.OttoMagic == AutomationEngine || AutomationEngine.SeleniumJS == AutomationEngine; }
        public bool UseOttoMagic { get => AutomationEngine.OttoMagic == AutomationEngine || AutomationEngine.OttoMagicHW == AutomationEngine; }
        public bool UsePuppeteer { get => AutomationEngine.PuppeteerPipe == AutomationEngine || AutomationEngine.PuppeteerPort == AutomationEngine || AutomationEngine.PuppeteerPortHW == AutomationEngine || AutomationEngine.PuppeteerPipeHW == AutomationEngine; }
        public bool UseSelenium { get => AutomationEngine.Selenium == AutomationEngine || AutomationEngine.SeleniumJS == AutomationEngine || AutomationEngine.SeleniumHW == AutomationEngine; }

        public AutomationEngine AutomationEngine { get; set; } = AutomationEngine.PuppeteerPort;

        // Run the browser on a hidden Win32 desktop. Defaults to true when the workflow itself started on a hidden
        // desktop.
        internal bool HiddenDesktop { get; set; } = null != GenerallyPositive.Browser.HiddenDesktop.ProcessDesktop;

        // set by the browser image search while it runs: true when the match is made against the page rather than
        // the screen, so CaptureScreen asks the engine for its own capture. false the rest of the time
        internal bool ImageSearchInPage { get; set; } = false;

        // Name of this browser's hidden desktop. Same name, shared desktop; different names, separate desktops.
        // WithHiddenDesktop sets the given name, or one unique to this browser. Until then it is the workflow's own
        // hidden desktop if it has one, otherwise HiddenDesktop.DefaultName.
        internal string HiddenDesktopName { get; set; } = GenerallyPositive.Browser.HiddenDesktop.ProcessDesktop ?? GenerallyPositive.Browser.HiddenDesktop.DefaultName;

        // True when this browser's desktop was made for it alone (WithHiddenDesktop with no name), so Close releases it.
        internal bool HiddenDesktopOwned { get; set; } = false;
        internal int WaitForDocumentReadyTimeoutMs { get; set; } = 30_000;

        // how long a click waits to see whether it started a navigation. Only covers the browser committing the
        // new url, not loading it - CheckDocumentReady owns the loading half once we know we are going somewhere
        internal int NavigationGraceMs { get; set; } = 250;

        internal int WithAllThatMatch { get; set; } = int.MaxValue;   // all rows, default

        internal string RestApiBaseUrl { get; set; }
        // True when the workflow supplied the REST API address instead of the browser reporting it. Someone else
        // started the browser, so we never launch, stop or kill it.
        internal bool AttachedRestApi { get; set; }

        internal IMagicHelper _magicHelper = null;
        /// <summary>
        /// This browser's own MagicHelper, on its OttoMagic port. Null until the browser is created. Never falls back
        /// to another browser's, which would silently drive the wrong browser.
        /// </summary>
        public IMagicHelper MagicHelper
        {
            get => _magicHelper;

            internal set => _magicHelper = value;
        }
        /// <summary>
        /// Disconnects a copied BrowserSettings from its source so it holds launch settings only. The copy includes
        /// fields, so the driver, process, Browser reference, REST API url and both engine clients came across and
        /// would drive the original. These are cleared and a new MagicHelper created.<br/>
        /// The OttoMagic port is left empty; the udp handshake sets it at launch.
        /// </summary>
        /// <param name="browser">The browser these settings now belong to</param>
        internal void DetachFromRunningBrowser(Browser browser)
        {
            Browser = browser;
            BrowserDriver = null;
            Process = null;
            RestApiBaseUrl = null;
            _magicHelper = new MagicHelper(browser);
            _puppeteerCommunicator = null;
        }
        internal StealthType StealthType { get; set; } = StealthType.None;
        internal bool StealthApplied { get; set; } = false;
        internal string CurrentURL { get; set; }
        private GPALUrl _currentGPALUrl = null;
        internal GPALUrl CurrentGPALUrl 
        { 
            get => _currentGPALUrl; 
            set
            {
                _currentGPALUrl = value;
                StorageActionToRun = new StorageAction();   // any time we set this, we reset the action to run, but this doesn't help us clear values from a prior call
            }
        }
        internal List<GPALUrl> GPALUrlList { get; set; }

        // set by For: the url whose storage definitions the next RunGet, RunSet or RunDelete uses, in place of
        // the page the browser is on. Used once and cleared, so the run after it is back on the current page
        internal GPALUrl StorageForGPALUrl { get; set; }
        // the referrer every GoTo and Get sends with its navigation, until WithUseReferrer clears it: the one
        // WithUseReferrer set, or https://www.google.com/ from StealthType.GoogleReferrer
        internal string Referrer { get; set; } = null;
        internal string OverrideUserAgent { get; set; } = null;

        // Launch a browser just to read its real user agent. Off by default, so headless does not launch an extra
        // browser.
        internal bool UserAgentFromBrowser { get; set; } = false;
        internal Selector CurrentSelector { get; set; } = null;
        internal IWebDriver BrowserDriver { get; set; } = null;

        // Selenium on Chrome and Edge: puts the registered scripts on tabs opened after they were registered
        internal SeleniumTabScripts TabScripts { get; set; } = null;
        internal int WaitForWindowTimeoutInSeconds { get; set; } = 30; // seconds
        internal bool BlockPopUps { get; set; } = true;
        internal int DownloadTimeoutInSec { get; set; } = 10;
        internal bool UseDirectDownload { get; set; } = false;
        internal string DownloadLocation { get; set; }
        internal bool DeleteFileBeforeDownload { get; set; } = false;
        internal bool OpenInTab { get; set; } = false;   // caveat: note: only good for one GoTo
        internal bool UseHeadless { get; set; } = false;
        internal bool? FileDownloaded { get; set; }
        internal bool ScrollIntoView { get; set; } = false;
        internal Rectangle WindowSize { get; set; }
        internal Size SavedWindowSize { get; set; }
        internal PageOrientation PageOrientation { get; set; }
        internal object JavaScriptResultObj { get; set; }
        internal string JavaScriptResultStr { get; set; }
        internal StorageAction StorageActionToRun { get; set; }
        internal bool StorageActionCalled { get; set; }

        internal string Version = string.Empty;
        internal int ServerResponseCode { get; set; }

        internal Bitmap ScreenShot;
        internal Process Process { get; set; } = null;  // set when using ottomatic/magic which launches the browser without a driver
        internal int WhileLoopTimeoutMs { get; set; } = 30000; // Default: 30 seconds
        internal int WhileLoopMaxIterations { get; set; } = 1000; // Default: 1000 iterations
        internal List<Action<IBrowser>> Workflows { get; set; }
        internal BrowserSettings(Browser browser)
        {
            WindowSize = new Rectangle();
            SavedWindowSize = new Size();
            Workflows = new List<Action<IBrowser>>();
            Browser = browser;
            PageOrientation = PageOrientation.Portrait;
            RobotsTxt = new List<string>();
            StorageActionToRun = new StorageAction();
            StorageActionCalled = false;
            GPALUrlList = new List<GPALUrl>();
        }
    }
}

