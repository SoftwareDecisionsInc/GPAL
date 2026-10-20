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
    public class Browser
        : IBrowser
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

        #region <Working Vars>
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

        #endregion <Working Vars>
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
            this.BrowserSettings = new BrowserSettings(this)
            {
                DriverLocation = GPAL.DriverLocation,
                DownloadTimeoutInSec = GPAL.GPALSettings.DownloadTimeoutInSec
            };

            CurrentUOW = new UnitOfWork() { Browser = this };
            persistentUOW = new UnitOfWork() { Browser = this };
            WorkflowManager = new WorkflowManager(this); // Pass current Browser instance
        }

        #region <Browser Settings>
        /// <summary>
        /// Specifies the location where the browser driver executable is loaded from.<br/>
        /// Default location is the directory with the GPAL executable.
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        /// </code>
        /// </example>
        /// <param name="directory">Directory containing the driver</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithDriverLocation([Directory] string directory)
        {
            BrowserSettings.DriverLocation = FileHelper.EnsureDirectoryEndsWithBackslash(Environment.ExpandEnvironmentVariables(directory));
            return this;
        }
        /// <summary>
        /// For use with .RightClickAndDownload, use websockets to directly download what the webelement references
        /// </summary>
        /// <param name="trueFalse">False [default - open context, choose save as] </param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithUseDirectDownload(bool trueFalse)
        {
            BrowserSettings.UseDirectDownload = trueFalse;
            return this;
        }
        /// <summary>
        /// Sets the maximum time to wait for a download to complete.
        /// </summary>
        /// <param name="seconds">Timeout in seconds</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithDownloadTimeoutInSec([Seconds] int seconds)
        {
            BrowserSettings.DownloadTimeoutInSec = seconds;
            return this;
        }
        /// <summary>
        /// Specifies the download location for this browser sessions, overrides the default
        /// </summary>
        /// <param name="directory">The full path to the download folder.</param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithDownloadLocation([Directory] string directory)
        {
            BrowserSettings.DownloadLocation = FileHelper.EnsureDirectoryEndsWithBackslash(Environment.ExpandEnvironmentVariables(directory));
            return this;
        }
        /// <summary>
        /// Specifies whether to block popups in the browser.
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithBlockPopUps(false)
        ///         .GoTo("https://www.ebay.com/")
        /// </code>
        /// </example>
        /// <param name="blockPopUp">True [default] to block popups.<br/>False allows popups.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithBlockPopUps(bool blockPopUp)
        {
            BrowserSettings.BlockPopUps = blockPopUp;
            return this;
        }
        /// <summary>
        /// Runs the browser on its own hidden Win32 desktop: a real, non-headless window that is not on your screen,
        /// so the machine stays usable.<br/>
        /// <b>NOTE:</b> With no name, each browser gets its own desktop, released when the browser closes; use
        /// <see cref="WithHiddenDesktop(string)"/> to share one.<br/>
        /// <b>NOTE:</b> Not a Windows virtual desktop (Win+Tab), which isolates nothing.<br/>
        /// <b>NOTE:</b> SendInput only reaches the on-screen desktop, so a hardware engine is switched to its
        /// non-hardware version, and the browser reports the engine used.<br/>
        /// <b>NOTE:</b> A workflow started on a hidden desktop (by a scheduler, for example) runs its browsers there;
        /// False then means that desktop, not your screen.
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithHiddenDesktop()
        ///         .GoTo("https://www.ebay.com/")
        /// </code>
        /// </example>
        /// <param name="hiddenDesktop">True [default] for a desktop of its own.<br/>False for the one the workflow is on.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithHiddenDesktop(bool hiddenDesktop)
        {
            BrowserSettings.HiddenDesktop = hiddenDesktop;
            BrowserSettings.HiddenDesktopOwned = hiddenDesktop;

            // No name given: make a unique one so this browser gets its own desktop.
            if (true == hiddenDesktop)
                BrowserSettings.HiddenDesktopName = Desktops.NextName();
            // A workflow already on a hidden desktop launches onto it, so false means that desktop.
            else if (null != Desktops.ProcessDesktop)
            {
                BrowserSettings.HiddenDesktop = true;
                BrowserSettings.HiddenDesktopName = Desktops.ProcessDesktop;
            }

            return this;
        }
        /// <summary>
        /// Run the browser on the named Win32 desktop object. Browsers naming the same desktop share it, and
        /// browsers naming different ones get one each, so three browsers on one desktop and three browsers on
        /// three desktops are the same call with different names.<br/>
        /// <b>NOTE:</b> everything <see cref="WithHiddenDesktop(bool)"/> says applies here too, hardware engines
        /// included.
        /// </summary>
        /// <example>
        /// <code>
        ///     // a desktop each, so they can be looked at one at a time
        ///     browser.WithHiddenDesktop($"GPAL.Hidden.{row}");
        /// </code>
        /// </example>
        /// <param name="desktopName">What to call the desktop this browser runs on.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithHiddenDesktop(string desktopName)
        {
            BrowserSettings.HiddenDesktop = true;
            BrowserSettings.HiddenDesktopOwned = string.IsNullOrWhiteSpace(desktopName);

            // Calling this turns on the hidden desktop. A null or blank name gets a unique desktop, as
            // WithHiddenDesktop(true) does.
            BrowserSettings.HiddenDesktopName = true == string.IsNullOrWhiteSpace(desktopName)
                                              ? Desktops.NextName()
                                              : desktopName;

            return this;
        }
        /// <summary>
        /// Runs launch on the hidden desktop when one is set, otherwise on the current thread.
        /// </summary>
        /// <typeparam name="T">What the launch returns, a driver or a process.</typeparam>
        /// <param name="launch">The launch to run.</param>
        /// <returns>Whatever the launch returned.</returns>
        private T LaunchWhereAsked<T>(Func<T> launch)
        {
            T retVal;

            if (true == BrowserSettings.HiddenDesktop)
                retVal = Desktops.Launch(BrowserSettings.HiddenDesktopName, launch);
            else
                retVal = launch();

            return retVal;
        }
        /// <summary>
        /// Maps an engine Firefox cannot use to one it can. Firefox has no CDP: Puppeteer modes become Selenium,
        /// hardware modes SeleniumHW.
        /// </summary>
        /// <param name="engine">Engine the workflow asked for.</param>
        /// <returns>The nearest engine Firefox supports, or the engine unchanged when it already supports it.</returns>
        private static AutomationEngine FirefoxStepDown(AutomationEngine engine)
        {
            AutomationEngine retVal = engine;

            switch (engine)
            {
                case AutomationEngine.PuppeteerPort:
                case AutomationEngine.PuppeteerPipe:
                    retVal = AutomationEngine.Selenium;
                    break;
                case AutomationEngine.PuppeteerPortHW:
                case AutomationEngine.PuppeteerPipeHW:
                    retVal = AutomationEngine.SeleniumHW;
                    break;
            }

            return retVal;
        }

        /// <summary>
        /// Non-hardware version of a hardware engine (OttoMagicHW to OttoMagic, and so on); other engines unchanged.
        /// </summary>
        /// <param name="engine">The engine the workflow asked for.</param>
        /// <returns>An engine that does not need SendInput.</returns>
        private static AutomationEngine HardwareStepDown(AutomationEngine engine)
        {
            AutomationEngine retVal = engine;

            switch (engine)
            {
                case AutomationEngine.OttoMagicHW:
                    retVal = AutomationEngine.OttoMagic;
                    break;
                case AutomationEngine.PuppeteerPortHW:
                    retVal = AutomationEngine.PuppeteerPort;
                    break;
                case AutomationEngine.PuppeteerPipeHW:
                    retVal = AutomationEngine.PuppeteerPipe;
                    break;
                case AutomationEngine.SeleniumHW:
                    retVal = AutomationEngine.Selenium;
                    break;
            }

            return retVal;
        }
        /// <summary>
        /// Specify which browser to run the workflow in.<br/>
        /// <b>NOTE:</b> Chrome and Edge act mostly the same, Firefox has some behavioral differences.<br/>
        /// For all browsers, GPAL handles any differences for you.<br/>
        /// Of particular note is topping the browser window with Firefox requires a hardware click.
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        /// </code>
        /// </example>
        /// <param name="browserType">Enumeration BrowserType of Chrome [default], Edge or Firefox.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        [BrowserType(Description = "Chrome, Edge or Firefox")]
        public IAllowBrowserSettingsOrGoTo WithBrowserType(BrowserType browserType)
        {
            BrowserSettings.BrowserType = browserType;

            return this;
        }
        /// <summary>
        /// Overwrite the destination file locally when downloading.<br/>
        /// If set to false, filenames will have a date timestamp appended to make each filename unique.<br/>
        /// Used with .LeftClickAndDownload(filename) and .RightClickAndDownload(filename)
        /// </summary>
        /// <example>
        /// <code>
        ///     myBrowser
        ///         .WithOverwriteExistingFile(true)
        ///         .RightClickAndDownload(fileList);   // NOTE: filename used on save is added to the end of filelist
        /// </code>
        /// </example>
        /// <param name="overwriteFile">True to overwrite destination file.<br/>False [default] to rename file on save.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserSettingsOrGoTo WithOverwriteExistingFile(bool overwriteFile)
        {
            BrowserSettings.DeleteFileBeforeDownload = overwriteFile;
            return this;
        }
        /// <summary>
        /// Determine whether to show images on a webpage.<br/>
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithLoadImages(false)
        ///         .GoTo("https://www.ebay.com/")
        /// </code>
        /// </example>
        /// <param name="loadImages">True to load/render images.<br/>False [default] to NOT load/render images.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithLoadImages(bool loadImages)
        {
            BrowserSettings.LoadImages = loadImages;
            return this;
        }
        /// <summary>
        /// Scrolls each element into view as it is scraped, so you can watch the run. Scraping does not need it.<br/>
        /// Ignored headless; skipped for GPALElement matches, which have no page position.
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithScrollIntoView()
        ///         .GoTo("https://www.ebay.com/")
        /// </code>
        /// </example>
        /// <param name="trueFalse">True [default] to scroll each element into view as it is scraped.<br/>False to leave the page where it is.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithScrollIntoView(bool trueFalse = true)
        {
            BrowserSettings.ScrollIntoView = trueFalse;
            return this;
        }
        /// <summary>
        /// Set to true to display the SAVE AS dialog box when downloading a file.<br/>
        /// Off by default: downloads go straight to the download folder and LeftClickAndDownload(filename) saves them
        /// under the name asked for, on every browser and engine.<br/>
        /// When set, every download prompts with the SAVE AS dialog and LeftClickAndDownload(filename) fills in the filename.<br/>
        /// <b>NOTE:</b> Whether the dialog opens is up to the browser. Edge has no Save As prompt to show, only its own
        /// download menu, so on Edge, and on a hidden desktop, downloads go straight to the download folder. Chrome on a
        /// signed-in profile can take the setting from the account and save directly; LeftClickAndDownload then waits for
        /// a dialog that never opens before it finds the file in the download folder.
        /// </summary>
        /// <example>
        /// <code>
        ///     myBrowser
        ///         .WithPromptForDownload(true)
        ///         .LeftClickAndDownload(fileList);   // NOTE: filename used on save is added to the end of filelist
        /// </code>
        /// </example>
        /// <param name="promptForDownload">True to prompt for a download save location.<br/>False [default] to save downloads to the download folder without asking.</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithPromptForDownload(bool promptForDownload)
        {
            BrowserSettings.PromptForDownload = promptForDownload;
            return this;
        }
        /// <summary>
        /// Specifies whether the browser will render PDFs or launch an external program.<br/>
        /// </summary>
        /// <param name="openPDFExternally">True [default] to open PDFs external to the browser.<br/>False to render in the browser.</param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithOpenPDFExternally(bool openPDFExternally)
        {
            BrowserSettings.OpenPDFExternally = openPDFExternally;
            return this;
        }

        //public IAllowBrowserSettingsOrGoTo WithDownloadFileTypes(string csvList)
        //{
        //    string[] fileTypes = csvList.Split(',');
        //    foreach (string fileType in fileTypes)
        //        BrowserSettings.DownloadFileTypeList.Add(fileType);
        //    return this;
        //}

        /// <summary>
        /// Specifies a GPALGrid&lt;string&gt; that will be subsequently saved to disk.<br/>
        /// Headers can be specified inline using .WithHeader or, if not specified, will use the Selector name as the column header.<br/>
        /// If using the Selector name as the header, the names and order of the headers are from the current UOW and the order the Selectors are defined.<br/>
        /// <b>NOTE:</b> Scraping data occurs when .GetGrid(optionalOutGrid) is called. An internal grid is then created in the currentUOW.<br/>
        /// This internal grid will be used in any subsequent .SaveTo[CSV|TabbedText|Text] method calls.<br/>
        /// In this case, you do not need to specify an input grid using WithGridToSave.
        /// </summary>
        /// <example>
        /// <code>
        /// using GenerallyPositive.Common; // acess to GPALGrid
        /// 
        /// public static GPALGrid&lt;string&gt; retGrid;
        /// 
        /// ... code to fill in the grid ...
        /// 
        ///     myBrowser
        ///         .WithGridToSave(retGrid)
        ///         .WithHeader("Description")
        ///         .WithHeader("Seller")
        ///         .WithHeader("Feedback")
        ///         .SaveTo(@"C:\ebay.retrieved.columns.txt");   // .txt => tab-delimited
        /// </code>
        /// </example>
        /// <param name="inputGrid">The GPALGrid&lt;string&gt; of data to save</param>
        /// <returns></returns>
        public IAllowWithHeaderOrFileActions WithGridToSave(IGPALGrid<string> inputGrid)
        {
            // Start a new unit of work for the grid, as WithSelector does, so the save does not reuse the previous
            // read's headers and selector names.
            if (true == CurrentUOW.ActionCalled)
            {
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot,
                    Browser = this
                };
            }

            if (null != inputGrid && 0 < inputGrid.Count())
            {
                // TODO: CAVEAT: is this best? we can define any type Grid but then saving it to dynamic?
                CurrentUOW.RetGrid = inputGrid;
                CurrentUOW.ColCount = inputGrid.Columns;
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Input IGPALGrid<T> is empty.", this, GPALObjectType.Browser);

            return this;
        }

        //public IAllowBrowserSettingsOrGoTo WithOpenFileTypes(string csvList)
        //{
        //    string[] fileTypes = csvList.Split(',');
        //    foreach (string fileType in fileTypes)
        //        BrowserSettings.OpenFileTypeList.Add(fileType);
        //    return this;
        //}

        /// <summary>
        /// Specifies the port to use to connect to an existing session from another workflow started with .WithDebugPort<br/>
        /// MUST be the same port as used when calling .WithDebugPort()
        /// </summary>
        /// <example>
        /// <code>
        ///     // open a browser using debug port 6565
        ///     Browser browser1 = (Browser)GPAL.Browser
        ///         .WithBrowserType(browserType)
        ///         .WithDriverLocation(gPALInput.Text)
        ///         .WithDebugPort(6565)
        ///         .WithWaitOnDocumentReady(true)
        ///         .GoTo("https://studio.youtube.com");
        ///        
        ///     // create an entirely new session connecting to the browser1 session
        ///     Browser browser2 = (Browser)GPAL.Browser
        ///         .WithExistingCDPDebugPort(6565)
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(detailsTab).LeftClick();
        ///         
        ///     browser1.Close(true);   // no need to close 2, it's the same
        /// </code>
        /// </example>
        /// <param name="port">The TCP port number to connect with to control the browser</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithExistingCDPDebugPort([Port] int port)
        {
            BrowserSettings.UseExistingBrowser = true;
            BrowserSettings.ExistingBrowserPort = port;

            // Use the existing browser's port as DebugPort. Unset, Puppeteer takes the next free port and starts a
            // second browser.
            BrowserSettings.DebugPort = port;

            return this;
        }
        /// <summary>
        /// Connects to a browser already running GPALRestAPI instead of launching one. Pass the base url shown in the
        /// GPALRestAPI window, e.g. http://localhost:3117/ locally or http://vm2:3117/ on another machine with LAN
        /// access on.<br/><br/>
        /// GPAL does not start it. On this machine GPAL finds the browser, so it brings it to the front and hardware
        /// input reaches it; on another machine it does neither. <see cref="Close"/> closes only GPAL's window, and
        /// Close(true) closes the browser.
        /// </summary>
        /// <example>
        /// <code>
        ///     IBrowser browser = GPAL.Browser
        ///         .WithAutomationEngine(AutomationEngine.OttoMagic)
        ///         .WithGPALRestAPIUrl("http://vm2:3117/")
        ///         .GoTo("https://www.example.com")
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        /// <param name="restApiUrl">The base url GPALRestAPI is serving on</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithGPALRestAPIUrl([URL] string restApiUrl)
        {
            if (true == string.IsNullOrWhiteSpace(restApiUrl))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No address given to WithGPALRestAPIUrl, so a browser will be launched as usual", this, GPALObjectType.Browser);
                return this;
            }

            // Only OttoMagic talks to GPALRestAPI.
            if (false == UseOttoMagic)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WithGPALRestAPIUrl drives a browser over GPALRestAPI, which is the OttoMagic engine. The engine is currently [{BrowserSettings.AutomationEngine}]", this, GPALObjectType.Browser);

            BrowserSettings.RestApiBaseUrl = restApiUrl.EndsWith("/") ? restApiUrl : restApiUrl + "/";
            BrowserSettings.AttachedRestApi = true;

            // On this machine the browser can be found, so its window comes to the front and hardware input reaches it.
            BrowserSettings.Process = BrowserHelper.LocalRestApiBrowser(BrowserSettings.RestApiBaseUrl);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Driving the browser already running on [{BrowserSettings.RestApiBaseUrl}].", this, GPALObjectType.Browser);

            return this;
        }

        public IAllowBrowserSettingsOrGoTo WithCDPDebugPort(int port = 0xdead) // will actually take default from interface and is not required, but for clarity
        {
            if (port < 1024 || port > 65535)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid debug port [{port}]. Must be between 1024 and 65535. Using current port [{BrowserSettings.DebugPort}].", this, GPALObjectType.Browser);
                return this;
            }

            // if the port is already in use on the computer, keep bumping it up one until we find an open port
            while (true == BrowserHelper.IsPortListening(port))
                port++;

            BrowserSettings.DebugPort = port;

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Puppeteer using port [{port}]", this, GPALObjectType.Browser);

            // NOTE: CAVEAT: existing port generally should be the same as debug port - do we want to set this?
            //  BUT, it is possible we are automating multiple browsers, so these have to be separate for each browser, but if it's not yet set...
            //      so, if we are open in debugger port, we could then connect to another browser on another existing port and copy data over.
            // automating multiple browsers is problematic, tabs is the way to go, if using puppeteer, we could in theory handle multiple browsers.
            if (null == BrowserSettings.ExistingBrowserPort)
                BrowserSettings.ExistingBrowserPort = port;

            AutomationEngine = AutomationEngine.PuppeteerPort;
            BrowserSettings.DebugPipe = false;

            return this;
        }
        /// <summary>
        /// Selects which automation engine drives the browser: Selenium, OttoMagic, or one of the Puppeteer
        /// connection modes. Firefox only supports Selenium or OttoMagic.
        /// </summary>
        /// <param name="automationEngine">Automation engine to use</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithAutomationEngine(AutomationEngine automationEngine)
        {
            if (BrowserType.FireFox == BrowserSettings.BrowserType && 
                (AutomationEngine.PuppeteerPort == automationEngine || AutomationEngine.PuppeteerPortHW == automationEngine || 
                AutomationEngine.PuppeteerPipe == automationEngine || AutomationEngine.PuppeteerPipeHW == automationEngine))
            {
                AutomationEngine steppedDownTo = FirefoxStepDown(automationEngine);

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Firefox cannot be driven with [{automationEngine}], so this browser runs on [{steppedDownTo}]", this, GPALObjectType.Browser);

                automationEngine = steppedDownTo;
            }

            AutomationEngine = automationEngine;
            return this;
        }
        /// <summary>
        /// Use CDP protocol over pipes, should this impply CDP because selenium doesn't support pipe, so this is all us?
        /// NOTE: Mutually exclusive with Debug Pipe, DebugPipe will take precedence, if specific with DebugPort
        /// NOTE: Firefox does not support CDP pipes
        /// </summary>
        /// <param name="trueFalse"></param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithCDPDebugPipe(bool trueFalse = false) // will actually take default from interface and is not required, but for clarity
        {
            AutomationEngine = AutomationEngine.PuppeteerPipe;
            BrowserSettings.DebugPipe = true;
            BrowserSettings.DebugPort = null;

            return this;
        }
        /// <summary>
        /// Specifies GPAL will wait for a true in the browser DocumentReady flag before trying to locate elements.<br/>
        /// Mutally excludes WithWaitOnNetworkIdle - setting this true will set WithWaitOnNetworkIdle to false
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="timeoutInMs">Longest time in milliseconds to wait for the document to be ready</param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowBrowserSettingsOrGoTo WithWaitOnDocumentReady([Milliseconds] int timeoutInMs)
        {
            BrowserSettings.WaitOnDocumentReady = true;
            BrowserSettings.WaitOnNetworkIdle = false;
            BrowserSettings.WaitForDocumentReadyTimeoutMs = timeoutInMs;

            // A Selenium driver already running gets the new page-load limit too
            if (null != BrowserSettings.BrowserDriver)
                BrowserSettings.BrowserDriver.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(timeoutInMs);

            return this;
        }
        /// <summary>
        /// Milliseconds a click watches for a navigation to start, since a slow site can show the old url briefly
        /// after the click returns. Navigating clicks then wait for the load (WithWaitOnDocumentReady).<br/>
        /// Non-navigating clicks always wait the full time: longer is slower but avoids acting on a stale page.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser
        ///         .WithNavigationGrace(250)
        ///         .WithWaitOnDocumentReady(30_000)
        ///         .GoTo("https://www.ebay.com/");
        /// </code>
        /// </example>
        /// <param name="graceInMs">Milliseconds to watch for a navigation. 0 skips the check and clicks return immediately</param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowBrowserSettingsOrGoTo WithNavigationGrace([Milliseconds] int graceInMs)
        {
            BrowserSettings.NavigationGraceMs = graceInMs;

            return this;
        }
        /// <summary>
        /// Specifies GPAL will wait for network to be idle on current page before trying to locate elements.<br/>
        /// Mutally excludes WithWaitOnDocumentReady - setting this true will set WithWaitOnDocumentReady to false
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithNetworkIdleTimeoutMs(500)  // already the default value
        ///         .WithNetworkIdlePruneMs(10_000) // already the default value
        ///         .WithNetworkIdleMaxConnections(0)   // already the default value
        ///         .WithWaitOnNetworkIdle(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="trueFalse">True to wait for the network to go idle</param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowBrowserSettingsOrGoTo WithWaitOnIdleConnection(bool trueFalse = true)
        {
            BrowserSettings.WaitOnNetworkIdle = trueFalse;
            BrowserSettings.WaitOnDocumentReady = trueFalse ? false : BrowserSettings.WaitOnDocumentReady;

            return this;
        }
        /// <summary>
        /// Specifies max time in milliseconds to wait for the network idle before returning false
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithNetworkIdleTimeoutMs(500)  // already the default value
        ///         .WithNetworkIdlePruneMs(10_000) // already the default value
        ///         .WithNetworkIdleMaxConnections(0)   // already the default value
        ///         .WithWaitOnIdleConnection(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="networkIdleTimeoutMs"></param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowNetworkIdleSettings WithNetworkIdleTimeoutMs(int networkIdleTimeoutMs = 500)
        {
            BrowserSettings.NetworkIdleTimeoutMs = networkIdleTimeoutMs;
            return this;
        }
        /// <summary>
        /// Specifies the maximum duration to keep network stats before pruning old data
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithNetworkIdleTimeoutMs(500)  // already the default value
        ///         .WithNetworkIdlePruneMs(10_000) // already the default value
        ///         .WithNetworkIdleMaxConnections(0)   // already the default value
        ///         .WithWaitOnIdleConnection(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="maxConnections">Number of open connections the network must drop to before it counts as idle</param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowNetworkIdleSettings WithNetworkIdleMaxConnections(int maxConnections = 0)
        {
            BrowserSettings.NetworkIdleMaxConnections = maxConnections;
            return this;
        }
        /// <summary>
        /// Specifies the maximum duration to keep network stats before pruning old data
        /// The time to wait for new connections to count as open. 
        /// eg, If prune is 3 seconds, if there are 9 connections and another one opens after 3 seconds, it is considered idle
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithNetworkIdleTimeoutMs(500)  // already the default value
        ///         .WithNetworkIdlePruneMs(10_000) // already the default value
        ///         .WithNetworkIdleMaxConnections(0)   // already the default value
        ///         .WithWaitOnIdleConnection(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="networkIdlePruneMs"></param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowNetworkIdleSettings WithNetworkIdlePruneMs(int networkIdlePruneMs = 3_000)
        {
            BrowserSettings.NetworkIdlePruneMs = networkIdlePruneMs;
            return this;
        }
        /// <summary>
        /// Specify the profile directory to use for this browser automation session. Null or empty means no profile
        /// directory, so the browser runs on a temporary profile.
        /// NOTE: if both Profile User Name and Profile Directory are defined, the specific directory will be used
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithProfileDataDirectory(@"c:\profileDataDirectory")
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="profileDataDirectory">Path to the browser profile to use</param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithProfileDataDirectory([Directory] string profileDataDirectory)
        {
            if (true == string.IsNullOrEmpty(profileDataDirectory))
                BrowserSettings.ProfileDataDirectory = null;
            else
            {
                string tmpProfileDirectory = FileHelper.EnsureDirectoryEndsWithBackslash(Environment.ExpandEnvironmentVariables(profileDataDirectory));

                BrowserSettings.ProfileDataDirectory = tmpProfileDirectory;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Using profile data directory [{tmpProfileDirectory}].", this, GPALObjectType.Browser);
            }

            return this;
        }
        /// <summary>
        /// Specify the operating system login username to find the profile directory to use for this browser automation session
        /// NOTE: if both Profile User Name and Profile Directory are defined, the specific directory will be used
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithProfileUserName(@"Ottomatic")
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="profileUserName">Name of the profile to use, inside the profile data directory</param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithProfileUserName(string profileUserName)
        {
            BrowserSettings.ProfileUserName = profileUserName;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Using profile user name [{profileUserName}].", this, GPALObjectType.Browser);

            return this;
        }
        /// <summary>
        /// Specify the profile name to use under the data directory for this browser automation session
        /// NOTE: use this if a data directory contains multiple profile directories and you do not want to use the default profile
        /// </summary>
        /// <example>
        /// <code>
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithProfileName(profileName)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="profileName">Name of the browser profile to use</param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithProfileName(string profileName)
        {
            BrowserSettings.ProfileName = profileName;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Using profile name [{profileName}].", this, GPALObjectType.Browser);

            return this;
        }
        /// <summary>
        /// Use stealth operations to block bot detection
        /// use Runtime.disable before clicking login button
        /// Add persistent script the runs before all others to inject anti-CDP detection script
        /// </summary>
        /// <param name="steathType">The kind of stealth to use</param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowBrowserSettingsOrGoTo WithUseStealth(StealthType steathType)
        {
            BrowserSettings.StealthType = steathType;
            return this;
        }
        /// <summary>
        /// Set the refererrer to use with any Goto or Get for the rest of the browser sessions.
        /// It goes with each GoTo's own navigation only, as that page's referrer; what the page loads, and pages
        /// reached by clicking, carry their real referrers.
        /// Will only clear if you clear it explicitly with an empty string or no parameter.
        /// </summary>
        /// <param name="referrer"></param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithUseReferrer(string referrer = "")
        {
            BrowserSettings.Referrer = referrer;
            return this;
        }
        /// <summary>
        /// Set the user agent to use with any Goto or Get for the rest of the browser sessions.
        /// Will only clear if you clear it explicitly with an empty string or no parameter.
        /// </summary>
        /// <param name="userAgent"></param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithUseUserAgent(string userAgent = "")
        {
            BrowserSettings.OverrideUserAgent = userAgent;
            return this;
        }
        /// <summary>
        /// When true, launches a browser to read its real user agent and removes "Headless" from it, at the cost of
        /// an extra launch per headless start.<br/>
        /// Off by default: the user agent comes from <see cref="WithUseUserAgent"/>, then userAgent in gpal.yaml,
        /// then the per-browser template in gpal.yaml with the installed version filled in.
        /// </summary>
        /// <param name="userAgentFromBrowser">True to launch a browser to read its user agent</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserSettingsOrGoTo WithUserAgentFromBrowser(bool userAgentFromBrowser = true)
        {
            BrowserSettings.UserAgentFromBrowser = userAgentFromBrowser;
            return this;
        }
        /// <summary>
        /// Add a selector to the current UOW. Selectors make up a Unit of Work (UOW) to perform actions upon.<br/>
        /// A UOW is: All selectors defined between actions like GoTo, LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the CallIf handlers to create workflows to deal with found/not found selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
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
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowAfterAnySelector WithSelector(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;  // in case it's being reused
            selector.DeleteMe = false;                  // Remove() latches this on and nothing else clears it, so a reused selector would be swept on its first maintenance pass
            selector.RemovedMethods.Clear();            // a handler that took itself out of play in an earlier workflow is back in play for this one
            if (true == CurrentUOW.ActionCalled)
            {
                // NOTE: CAVEAT: should this a list to have nested iframes?
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot
                };

                CurrentUOW.Browser = this;
                selector.Browser = this;
            }

            ResolveInteractionType(selector);

            // if we are reusing, we can't use cached results in a new uow
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;

            //CurrentUOW.SelectorType = SelectorType.WithSelector;
            // clone so if this selector is reused, each UOW list will be unique and we won't change the selector by changing the browser it is associated with
            // NOTE: cloning disassociates with actual program selector, so we can't set any queryable properties on Selector like 'ElementsFoundAndMatchedCount'
            // BrowserSettings.MySelector = new Selector(this, selector);    // MySelector temp hold for other browser syntax
            BrowserSettings.CurrentSelector = selector;

            CurrentUOW.WithSelectorList.Add(BrowserSettings.CurrentSelector);
            return this;
        }
        /// <summary>
        /// Add literal data to a grid when using .GetGrid
        /// </summary>
        /// <param name="literalData"></param>
        /// <returns></returns>
        public IAllowAfterAnySelector WithSelector(string literalData)
        {
            if (true == CurrentUOW.ActionCalled)
            {
                // NOTE: CAVEAT: should this a list to have nested iframes?
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;
                //                var shadowRootElement = CurrentUOW.ShadowRootGPALElement;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot
                };

                persistentUOW = new UnitOfWork();

                CurrentUOW.Browser = this;
                persistentUOW.Browser = this;
            }

            Selector tmpSel = GPAL.Selector.WithText(literalData).ToGPALObject();
            tmpSel.selectorSettings.SelectorType = SelectorType.Data;
            tmpSel.selectorSettings.Name = "Literal" + CurrentUOW.WithSelectorList.Select(s => SelectorType.Data == s.SelectorSettings.SelectorType).Count() + 1;
            CurrentUOW.WithSelectorList.Add(tmpSel);
            return this;
        }
        /// <summary>
        /// Add literal data to a grid by invoking this function when using .GetGrid
        /// </summary>
        /// <param name="dataFunction"></param>
        /// <returns></returns>
        public IAllowAfterAnySelector WithSelector(Func<string> dataFunction)
        {
            Selector tmpSel = GPAL.Selector.ToGPALObject();
            tmpSel.selectorSettings.SelectorType = SelectorType.DataFunc;
            tmpSel.selectorSettings.Name = dataFunction.Method.Name;
            tmpSel.selectorSettings.DataFunction = dataFunction;
            CurrentUOW.WithSelectorList.Add(tmpSel);
            return this;
        }

        #region Storage
        /// <summary>
        /// Runs <paramref name="body"/> once per .InElement container, setting that entry's current index.
        /// Without .InElement, runs <paramref name="body"/> once.
        /// </summary>
        /// <param name="body">What to do inside one container</param>
        /// <returns>How many containers it walked, zero when there were none</returns>
        private int WalkInElementIfAny(Action body)
        {
            UnitOfWork.WebElementWithType inElement = CurrentUOW.ContextPath?
                .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

            if (null == inElement)
            {
                body();

                return 0;
            }

            // .WithAllThatMatch already limited the containers in RefreshContainers; no limit here.
            for (int container = 0; container < inElement.elements.Count; container++)
            {
                inElement.current = container;

                body();
            }

            inElement.current = 0;

            return inElement.elements.Count;
        }

        /// <summary>
        /// Finds the .InElement containers again on the current page; after a next page the old ones find nothing.
        /// The .InElement entry is lifted off the context path during the search so it does not search inside the
        /// old container.
        /// </summary>
        /// <param name="inElement">The .InElement entry on the context path</param>
        private void RefreshContainers(UnitOfWork.WebElementWithType inElement)
        {
            int at = CurrentUOW.ContextPath.IndexOf(inElement);

            CurrentUOW.ContextPath.RemoveAt(at);

            // After a next page an interstitial may show first. Retry every 250 ms until containers appear or
            // .WaitFor runs out.
            List<GPALElement> containers = null;
            Stopwatch waited = Stopwatch.StartNew();
            long waitFor = WaitTime.Never < CurrentUOW.WaitForInMs ? (long)CurrentUOW.WaitForInMs : 0;

            do
            {
                ElementHelper.FindWebElements(this, CurrentUOW, inElement.selector, out _, out containers, null, false);

                if (0 < containers?.Count) break;

                if (waitFor > waited.ElapsedMilliseconds)
                    System.Threading.Thread.Sleep(250);
            }
            while (waitFor > waited.ElapsedMilliseconds);

            CurrentUOW.ContextPath.Insert(at, inElement);

            if (0 < containers?.Count)
            {
                // With .InElement, .WithAllThatMatch limits containers: ten cards, not ten prices per card.
                // Precedence: unit of work, browser, global.
                int wanted = int.MaxValue;

                if (int.MaxValue != CurrentUOW.WithAllThatMatch && 0 < CurrentUOW.WithAllThatMatch)
                    wanted = CurrentUOW.WithAllThatMatch;
                else if (int.MaxValue != BrowserSettings.WithAllThatMatch && 0 < BrowserSettings.WithAllThatMatch)
                    wanted = BrowserSettings.WithAllThatMatch;
                else if (0 < GPAL.GPALSettings.WithAllThatMatch)
                    wanted = GPAL.GPALSettings.WithAllThatMatch;

                if (int.MaxValue != wanted && wanted < containers.Count)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{wanted}] containers kept of [{containers.Count}] found for selector [{inElement.selector.Name}] with [WithAllThatMatch({wanted})]", this, GPALObjectType.Browser);

                    containers = containers.Take(wanted).ToList();
                }

                inElement.elements = containers;
                inElement.gPalElement = containers[0];
            }
            else
            {
                inElement.elements = null;
                inElement.gPalElement = null;

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"InElement selector returned no results [{inElement.selector.Name}]", this, GPALObjectType.Browser);
            }

            inElement.current = 0;
        }
        /// <summary>
        /// Builds the grid from .InElement containers, one row per container, one cell per selector. A missing value
        /// gets a placeholder so rows stay aligned.<br/>
        /// Returns false without .InElement.
        /// </summary>
        /// <param name="tmpColumns">Receives one column per selector</param>
        /// <param name="selectorMeta">Receives the name and attribute of each column</param>
        /// <param name="pages">Page being read, for the line this writes</param>
        /// <returns>True when containers built the grid</returns>
        private bool RowsFromContainers(List<ReadOnlyCollection<GPALElement>> tmpColumns,
            List<(string Name, string AttributeName)> selectorMeta, int pages)
        {
            bool retVal = false;

            UnitOfWork.WebElementWithType inElement = CurrentUOW.ContextPath?
                .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

            if (null != inElement)
            {
                RefreshContainers(inElement);

                // With .InElement, rows come only from containers. No fallback to the column search, which causes
                // the misalignment .InElement prevents.
                retVal = true;
            }

            if (0 < inElement?.elements?.Count)
            {
                List<Selector> selectors = CurrentUOW.WithSelectorList;
                List<List<GPALElement>> building = selectors.Select(sel => new List<GPALElement>()).ToList();

                foreach (Selector sel in selectors)
                {
                    sel.WebSelectorFoundResults = null;
                    sel.WebSelectorMatchedResults = null;

                    selectorMeta.Add((sel.Name, sel.AttributeName));
                }

                int rows = WalkInElementIfAny(() =>
                {
                    for (int column = 0; column < selectors.Count; column++)
                    {
                        Selector sel = selectors[column];
                        GPALElement cell = null;

                        if (SelectorType.Selector == sel.selectorSettings.SelectorType)
                        {
                            ElementHelper.FindWebElements(this, CurrentUOW, sel, out _, out List<GPALElement> matchedElems);

                            // Use the first match in the container; extras do not spill into the next row.
                            if (0 < matchedElems?.Count)
                            {
                                cell = matchedElems[0];
                                cell.AttributeName = sel.AttributeName;
                            }
                        }
                        else if (SelectorType.Data == sel.selectorSettings.SelectorType)
                        {
                            cell = GPAL.Element;
                            cell.Text = sel.SelectorPath;
                        }
                        else if (SelectorType.DataFunc == sel.selectorSettings.SelectorType)
                        {
                            cell = GPAL.Element;
                            cell.Text = sel.selectorSettings.DataFunction();
                        }

                        // Nothing in this container: placeholder cell.
                        if (null == cell)
                            cell = new GPALElement(new Point(0, 0), new Size(1, 1), $"{GPAL.ErrorPlaceholder} : {sel.Name}", BrowserDriver, "GPALElement")
                            {
                                AttributeName = sel.AttributeName,
                                Browser = this,
                                Selector = sel
                            };

                        building[column].Add(cell);
                    }
                });

                foreach (List<GPALElement> column in building)
                    tmpColumns.Add(new ReadOnlyCollection<GPALElement>(column));

                CurrentUOW.RowCount = rows;
                CurrentUOW.ColCount = selectors.Count;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Grid page [{pages + 1}] read [{rows}] containers of [{selectors.Count}] columns", this, GPALObjectType.Browser);
            }
            else if (true == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No containers for [{inElement.selector.Name}] on page [{pages + 1}], so no rows from it. The page was not read another way", this, GPALObjectType.Browser);

            return retVal;
        }
        /// <summary>
        /// Returns a RESTClient with this browser's session: origin, cookies, user agent, Accept-Language and any
        /// .WithCredentials credential.<br/>
        /// It works after the browser closes. It does not share the browser's TLS, so a site that fingerprints TLS
        /// needs .Fetch.
        /// </summary>
        /// <param name="client">Receives the client, ready to be given an endpoint</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ContinueAsRESTClient(out IRESTClient client)
        {
            // API base is the browser's current origin.
            string origin = UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(BrowserSettings));
            RESTClient retVal = (RESTClient)GPAL.RESTClient.WithAPIBase(origin);

            // No page loaded means no origin or cookies; warn now rather than fail at the first request.
            if (true == string.IsNullOrEmpty(origin))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No page loaded, so there is no session to carry. Navigate first, then continue as a RESTClient", this, GPALObjectType.Browser);

            // Read this origin's cookies on a new GPALUrl, leaving the workflow's own storage actions alone.
            GPALUrl cookieUrl = new GPALUrl(origin);

            cookieUrl.Add(new StorageAction { StorageType = WebsiteStorageType.cookie, Action = WebsiteStorageAction.get });

            UrlHelper.TryPerformAction(cookieUrl, BrowserSettings,
                new StorageAction { StorageType = WebsiteStorageType.cookie, Action = WebsiteStorageAction.get },
                out string cookieJson);

            string cookieHeader = CookieHeaderFrom(cookieJson, origin, out int cookiesRead);

            if (false == string.IsNullOrEmpty(cookieHeader))
                retVal.WithHeader("Cookie", cookieHeader);

            // The user agent and Accept-Language actually sent, not the configured ones; some sites bind the session
            // to the user agent.
            string sentUserAgent = BrowserHelper.LiveUserAgent(this);
            string sentAcceptLanguage = BrowserHelper.LiveAcceptLanguage(this);

            retVal.WithHeader("User-Agent", sentUserAgent ?? BrowserHelper.ResolveUserAgent(BrowserSettings));

            if (false == string.IsNullOrEmpty(sentAcceptLanguage))
                retVal.WithHeader("Accept-Language", sentAcceptLanguage);

            // Copy the WithCredentials credential as an Authorization header. Basic auth is never a cookie, so
            // cookies alone get a 401.
            ICredentials browserCredential = BrowserSettings.Credentials;

            if (null != browserCredential)
            {
                Credentials credential = (Credentials)browserCredential;
                string authValue = BrowserHelper.AuthorizationHeader(credential, credential.WebAuthType);

                if (false == string.IsNullOrEmpty(authValue))
                    retVal.WithHeader(BrowserHelper.AuthorizationHeaderName(credential.WebAuthType), authValue);

            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Continuing as a RESTClient from [{BrowserSettings.BrowserType}], carrying its [{(null == sentUserAgent ? "configured" : "live")}] user agent, [{(string.IsNullOrEmpty(sentAcceptLanguage) ? "no accept language" : sentAcceptLanguage)}], [{cookieHeader?.Split(';').Length ?? 0}] of [{cookiesRead}] cookies for [{origin}] and [{(null == browserCredential ? "no credential" : ((Credentials)browserCredential).WebAuthType.ToString())}]", this, GPALObjectType.Browser);

            client = retVal;

            return this;
        }

        /// <summary>
        /// Builds a Cookie header from a cookie get's json, keeping only the given origin's cookies.
        /// </summary>
        /// <param name="cookieJson">What the cookie get returned</param>
        /// <param name="origin">The origin the header is being built for</param>
        /// <param name="cookiesRead">How many cookies the engine answered with, before narrowing</param>
        /// <returns>name=value pairs joined with "; ", or null when there were none</returns>
        private static string CookieHeaderFrom(string cookieJson, string origin, out int cookiesRead)
        {
            string retVal = null;

            cookiesRead = 0;

            try
            {
                if (false == string.IsNullOrEmpty(cookieJson))
                {
                    string host = true == Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri) ? originUri.Host : null;
                    Newtonsoft.Json.Linq.JArray cookies = Newtonsoft.Json.Linq.JArray.Parse(cookieJson);
                    List<string> pairs = new List<string>();

                    cookiesRead = cookies.Count;

                    foreach (Newtonsoft.Json.Linq.JToken cookie in cookies)
                    {
                        // puppeteer calls the name Key, the other two call it name
                        string name = (string)(cookie["name"] ?? cookie["Name"] ?? cookie["Key"]);
                        string value = (string)(cookie["value"] ?? cookie["Value"]);
                        string domain = (string)(cookie["domain"] ?? cookie["Domain"]);

                        if (false == string.IsNullOrEmpty(name) && true == UrlHelper.CookieAppliesToHost(domain, host))
                            pairs.Add($"{name}={value}");
                    }

                    if (0 < pairs.Count)
                        retVal = string.Join("; ", pairs);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not read the cookies into a header", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }


        /// <summary>
        /// Reads data from browser storage (cookies, local/session storage, IndexedDB) using the domain/path/key
        /// configured by the preceding .WithStorage* calls.
        /// </summary>
        /// <param name="storageType">Type of storage to read from</param>
        /// <returns>Fluent interface to retrieve the result with .GetStorageData</returns>
        public IAllowStorageOutData RunGet(WebsiteStorageType storageType)
        {
            BrowserSettings.StorageActionCalled = true;
            BrowserSettings.StorageActionToRun.Action = WebsiteStorageAction.get;
            BrowserSettings.StorageActionToRun.StorageType = storageType;

            if (true == StorageRunsNow())
                UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, BrowserSettings.StorageActionToRun, out BrowserSettings.StorageActionToRun._data);

            return this;
        }
        /// <summary>
        /// Runs the next storage action (RunGet, RunSet or RunDelete) against <paramref name="url"/>'s storage
        /// definitions instead of the current page, without navigating. Applies to one action.<br/>
        /// Use it to clear a site's cookies before visiting.<br/>
        /// <b>NOTE:</b> Before the first GoTo or Get, a set or delete is held and runs when the browser starts,
        /// ahead of the first page.<br/>
        /// <b>NOTE:</b> Selenium on Firefox reaches only the current site's storage; there the action warns and finds
        /// nothing, so go to the site first.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPALUrl gucci = "https://www.gucci.com/us/en/";
        ///     gucci.WithStorageType(WebsiteStorageType.cookie).WithStorageDomain("gucci.com");
        ///
        ///     browser
        ///         .For(gucci)
        ///             .WithStorageKey("_abck")
        ///             .RunDelete(WebsiteStorageType.cookie)
        ///         .GoTo(gucci);
        /// </code>
        /// </example>
        /// <param name="url">The url whose storage definitions the next storage action uses.</param>
        /// <returns>Fluent interface to scope and run the storage action</returns>
        public IAllowStorageOptions For(GPALUrl url)
        {
            BrowserSettings.StorageForGPALUrl = url;

            // a new storage action, so nothing scoped for an earlier one carries over to this url
            BrowserSettings.StorageActionToRun = new StorageAction();
            BrowserSettings.StorageActionCalled = false;

            return this;
        }
        // Storage sets and deletes asked for before the browser started, each with the url it runs against (null
        // for the page the first GoTo or Get opens). Run once the engine answers, ahead of the first navigation.
        private readonly List<KeyValuePair<GPALUrl, StorageAction>> _pendingStorageActions = new List<KeyValuePair<GPALUrl, StorageAction>>();

        /// <summary>
        /// Whether a storage action runs now. A storage action never launches a browser: only the GoTo or Get that
        /// comes later knows whether it opens in a window or headless. Before the browser starts, a set or delete
        /// is held and run when it does; a get has nothing to read yet and warns. A browser that was closed, or
        /// whose Selenium driver died, throws in InAction.
        /// </summary>
        /// <returns>True when the action runs now</returns>
        private bool StorageRunsNow()
        {
            bool retVal = null != ClosedAt || true == HasBrowser;
            StorageAction asked = BrowserSettings.StorageActionToRun;

            if (true == retVal)
                InAction(false, false);
            else
            {
                if (WebsiteStorageAction.get == asked.Action)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No browser is running yet, so there is no [{asked.StorageType}] storage to read. Read it after the first GoTo or Get.", this, GPALObjectType.Browser);
                else
                {
                    _pendingStorageActions.Add(new KeyValuePair<GPALUrl, StorageAction>(BrowserSettings.StorageForGPALUrl, asked));

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Holding [{asked.Action}] on [{asked.StorageType}] storage for [{BrowserSettings.StorageForGPALUrl?.Url ?? "the first page"}] until the browser starts", this, GPALObjectType.Browser);
                }

                // For applies to one action, and this was it
                BrowserSettings.StorageForGPALUrl = null;
            }

            return retVal;
        }

        /// <summary>
        /// Runs every storage set and delete held while the browser was still down. Called from WorkflowSetup once
        /// the engine answers and before the first navigation, so cookies deleted ahead of .GoTo() are gone when
        /// the first page loads.
        /// </summary>
        internal void ApplyPendingStorageActions()
        {
            if (0 < _pendingStorageActions.Count && true == EngineReachable)
            {
                List<KeyValuePair<GPALUrl, StorageAction>> held = new List<KeyValuePair<GPALUrl, StorageAction>>(_pendingStorageActions);

                _pendingStorageActions.Clear();

                foreach (KeyValuePair<GPALUrl, StorageAction> action in held)
                {
                    BrowserSettings.StorageForGPALUrl = action.Key;
                    UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, action.Value, out _);
                }
            }
        }
        /// <summary>
        /// The url a storage action runs against: the one For named, used once and cleared, or else the page the
        /// browser is on.
        /// </summary>
        /// <returns>The url whose storage definitions the action uses</returns>
        private GPALUrl StorageUrl()
        {
            GPALUrl retVal = BrowserSettings.StorageForGPALUrl ?? BrowserSettings.CurrentGPALUrl;

            // WebDriver on Firefox sees only the current site's storage; warn so the empty result is explained.
            if (null != BrowserSettings.StorageForGPALUrl && true == UseSelenium && BrowserType.FireFox == BrowserSettings.BrowserType
                && false == string.Equals(UrlHelper.GetOrigin(BrowserSettings.StorageForGPALUrl.Url), UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(BrowserSettings)), StringComparison.OrdinalIgnoreCase))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium on Firefox reaches only the storage of the site it is on, so nothing for [{BrowserSettings.StorageForGPALUrl.Url}] can be reached from [{BrowserHelper.GetCurrentUrl(BrowserSettings)}]. Run this after going there.", this, GPALObjectType.Browser);

            BrowserSettings.StorageForGPALUrl = null;

            return retVal;
        }
        /// <summary>
        /// Writes data to browser storage (cookies, local/session storage, IndexedDB) using the domain/path/key/data
        /// configured by the preceding .WithStorage* calls.
        /// </summary>
        /// <param name="storageType">Type of storage to write to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RunSet(WebsiteStorageType storageType)
        {
            BrowserSettings.StorageActionCalled = true;
            BrowserSettings.StorageActionToRun.Action = WebsiteStorageAction.set;
            BrowserSettings.StorageActionToRun.StorageType = storageType;

            if (true == StorageRunsNow())
                UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, BrowserSettings.StorageActionToRun, out _);

            return this;
        }
        /// <summary>
        /// Deletes data from browser storage (cookies, local/session storage, IndexedDB) using the domain/path/key
        /// configured by the preceding .WithStorage* calls.
        /// </summary>
        /// <param name="storageType">Type of storage to delete from</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RunDelete(WebsiteStorageType storageType)
        {
            BrowserSettings.StorageActionCalled = true;
            BrowserSettings.StorageActionToRun.Action = WebsiteStorageAction.delete;
            BrowserSettings.StorageActionToRun.StorageType = storageType;

            if (true == StorageRunsNow())
                UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, BrowserSettings.StorageActionToRun, out _);

            return this;
        }

        /// <summary>
        /// Specifies the domain to scope the next storage Run* action to. Starts a new StorageAction if the
        /// previous one was already run.
        /// </summary>
        /// <param name="domain">Domain to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageDomain(string domain)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Domain = domain;

            return this;
        }

        /// <summary>
        /// Specifies the path to scope the next storage Run* action to. Starts a new StorageAction if the
        /// previous one was already run.
        /// </summary>
        /// <param name="path">Path to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStoragePath(string path)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Path = path;

            return this;
        }

        /// <summary>
        /// Specifies the key to scope the next storage Run* action to. Starts a new StorageAction if the
        /// previous one was already run.
        /// </summary>
        /// <param name="key">Key to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageKey(string key)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Key = key;

            return this;
        }

        /// <summary>
        /// Specifies the IndexedDB store name to scope the next storage Run* action to. Starts a new
        /// StorageAction if the previous one was already run.
        /// </summary>
        /// <param name="storeName">IndexedDB store name to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageStoreName(string storeName)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.StoreName = storeName;

            return this;
        }

        /// <summary>
        /// Specifies the data value to write for the next storage .RunSet action. Starts a new StorageAction
        /// if the previous one was already run.
        /// </summary>
        /// <param name="data">Data value to write to storage</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageData(string data)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Data = data;

            return this;
        }

        /// <summary>
        /// Specifies a user-defined filter string passed through to the storage action implementation. Starts
        /// a new StorageAction if the previous one was already run.
        /// </summary>
        /// <param name="userDefined">User-defined filter value</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithUserDefinedFilter(string userDefined)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.UserDefined = userDefined;

            return this;
        }

        /// <summary>
        /// Retrieves the data read by the most recent .RunGet storage action.
        /// </summary>
        /// <param name="data">Receives the data retrieved from storage</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetStorageData(out string data)
        {
            data = BrowserSettings.StorageActionToRun.Data;

            return this;
        }
        #endregion Storage

        /// <summary>
        /// InElement indicates the root element for following withselector element searches which would use a relative selector
        /// Example: on ebay, we could use a generic selector to get all descriptions, links, prices and images, but that assumes 1:1 correlation of the globally found items
        ///          or, we can find all the result spans, then search for the description, link, price and image relative to that containing div/span/element
        /// </summary>
        /// <param name="selector"></param>
        /// <returns>Fluent interface to chain more methods</returns>
        public IAllowAfterAnySelector InElement(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;

            // Fresh UOW after any action (your standard reset)
            if (CurrentUOW.ActionCalled)
            {
                var previousShadowRoot = CurrentUOW.ShadowRoot;
                var previousContextPath = CurrentUOW.ContextPath;

                CurrentUOW = new UnitOfWork
                {
                    Browser = this,
                    ShadowRoot = previousShadowRoot,
                    ContextPath = previousContextPath  // preserve chain
                };

                persistentUOW = new UnitOfWork { Browser = this };
            }

            BrowserSettings.CurrentSelector = selector;
            CurrentUOW.InSelectorList.Add(selector);

            // --- Switch to clean context first ---
            //if (true == UseOttoMagic)
            //    MagicHelper?.SwitchToDefaultContent();
            //else if (true == UsePuppeteer)
            //    PuppeteerClient.SwitchToDefaultContent().Execute();
            //else
            //    BrowserDriver.SwitchTo().DefaultContent();

            // Walk existing context
            IEnumerable<UnitOfWork.WebElementWithType> stepsToProcess;

            stepsToProcess = CurrentUOW.ContextPath;

            GPALElement inElement = null;

            foreach (UnitOfWork.WebElementWithType contextType in stepsToProcess)
            {
                if (ElementType.IFrame == contextType.elementType)
                {
                    if (UsePuppeteer)
                        PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                    else
                        BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                    // a search context belongs to one document - anything scoped in the frame we just left is
                    // stale, and a shadow root from the parent throws "no such shadow root" if reused here
                    inElement = null;
                }
                else if (ElementType.ShadowRoot == contextType.elementType)
                {
                    if (UseOttoMagic)
                        MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                    else if (UsePuppeteer)
                        PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                    // scope to THIS step's shadow root rather than the last one added to the unit of work, so a
                    // path with more than one shadow root walks through each in turn instead of collapsing them
                    inElement = contextType.gPalElement;
                }
                else if (ElementType.Element == contextType.elementType)
                {
                    inElement = contextType.gPalElement;   // this becomes the new root
                }
            }

            // Store the selector only. Containers are found when an action runs: .WithAllThatMatch comes later, and
            // a next page replaces them.
            var elementEntry = new UnitOfWork.WebElementWithType(selector, null, ElementType.Element);

            CurrentUOW.ContextPath ??= new List<UnitOfWork.WebElementWithType>();
            CurrentUOW.ContextPath.Add(elementEntry);

            return this;
        }

        /// <summary>
        /// Specify that the following .WithSelector(s) are contained in an iFrame specified by the Selector.<br/>
        /// Use .InMainDom() to get back to working with the main document.
        /// </summary>
        /// <example>
        /// <code>
        ///     static Selector captchaIframe = (Selector)GPAL.Selector
        ///         .WithCSS(@"#s0-69-captcha-ui > iframe");
        ///
        ///     static Selector captchaCheck = (Selector)GPAL.Selector
        ///         .WithXPath(@"//*[@id=""checkbox""]")
        ///         .WithHardware;
        ///
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.FireFox)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithWaitOnDocumentReady(true)
        ///         .GoTo(@"https://www.ebay.com/splashui/captcha")
        ///         .InFrame(captchaIframe)
        ///         .WithSelector(captchaCheck)
        ///         .LeftClick();
        /// </code>
        /// </example>
        /// <param name="selector">iFrame element Selector</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector InFrame(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;

            // Fresh UOW after any action (standard fluent reset)
            if (CurrentUOW.ActionCalled)
            {
                var previousShadowRoot = CurrentUOW.ShadowRoot;
                var previousContextPath = CurrentUOW.ContextPath;

                CurrentUOW = new UnitOfWork
                {
                    Browser = this,
                    ShadowRoot = previousShadowRoot,
                    ContextPath = previousContextPath
                };

                persistentUOW = new UnitOfWork { Browser = this };
            }

            BrowserSettings.CurrentSelector = selector;
            CurrentUOW.InSelectorList.Add(selector);

            if (true == UseOttoMagic)
                MagicHelper.SwitchToDefaultContent();
            else if (true == UsePuppeteer)
                PuppeteerClient.SwitchToDefaultContent().Execute();
            else
                this.BrowserDriver.SwitchTo().DefaultContent();

            IEnumerable<UnitOfWork.WebElementWithType> stepsToProcess;
            GPALElement inElement = null;

            // puppeteer or selenium have to be iframe aware: process the full original path
            stepsToProcess = CurrentUOW.ContextPath;

            // Now walk the (possibly filtered) steps
            foreach (UnitOfWork.WebElementWithType contextType in stepsToProcess)
            {
                if (ElementType.IFrame == contextType.elementType)
                {
                    if (true == UseOttoMagic)
                        MagicHelper.SwitchToElement(contextType.gPalElement.Css);
                    else if(true == UsePuppeteer)
                        PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                    else
                        BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                    // a search context belongs to one document - anything scoped in the frame we just left is
                    // stale, and a shadow root from the parent throws "no such shadow root" if reused here
                    inElement = null;
                }
                else if (ElementType.ShadowRoot == contextType.elementType)
                {
                    if (true == UseOttoMagic)
                        MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                    else if (true == UsePuppeteer)
                        PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                    // scope to THIS step's shadow root rather than the last one added to the unit of work, so a
                    // path with more than one shadow root walks through each in turn instead of collapsing them
                    inElement = contextType.gPalElement;
                }
            }
            
            // Find the iframe
            ElementHelper.FindWebElements(
                this, CurrentUOW, selector, out _, out var tempElems, CurrentUOW.ShadowRoot);

            if (tempElems?.Count > 0)
            {
                var iframeElement = new UnitOfWork.WebElementWithType(
                    selector, tempElems[0], ElementType.IFrame);

                CurrentUOW.ContextPath ??= new List<UnitOfWork.WebElementWithType>();
                CurrentUOW.ContextPath.Add(iframeElement);
            }

            return this;
        }

        /// <summary>
        /// Specify that the following .WithSelectors are contained in an a shadow dom specified by the Selector.<br/>
        /// Use .InMainDom() to get back to working with the main document.
        /// NOTE: can only use CSS selectors in shadow dom
        /// </summary>
        /// <example>
        /// <code>
        ///     static Selector shadowRoot = (Selector)GPAL.Selector
        ///         .WithXPath(@"/html/body/article/p[3]")
        ///         .WithHardware
        ///         .WithSimulateMouse(true);
        ///         
        ///     static Selector shadowSpan = (Selector)GPAL.Selector
        ///         .WithCSS(@"span")
        ///         .WithHardware
        ///         .WithSimulateMouse(true);
        ///         
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithWaitOnDocumentReady(true)
        ///         .GoTo(@"https://mdn.github.io/web-components-examples/word-count-web-component/")
        ///         .InShadowDom(shadowRoot)
        ///         .WithSelector(shadowSpan)
        ///         .Hover()
        ///         .WaitFor(5000);
        /// </code>
        /// </example>
        /// <param name="shadowDomSelector">Shadow dom element Selector</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector InShadowDom(Selector shadowDomSelector)
        {
            shadowDomSelector.ElementsFoundAndMatchedCount = 0;
            shadowDomSelector.WebSelectorFoundResults = null;
            shadowDomSelector.WebSelectorMatchedResults = null;

            if (CurrentUOW.ActionCalled)
            {
                var previousShadowRoot = CurrentUOW.ShadowRoot;
                var previousContextPath = CurrentUOW.ContextPath;

                CurrentUOW = new UnitOfWork
                {
                    Browser = this,
                    ShadowRoot = previousShadowRoot,
                    ContextPath = previousContextPath
                };

                persistentUOW = new UnitOfWork { Browser = this };
            }

            BrowserSettings.CurrentSelector = shadowDomSelector;
            CurrentUOW.InSelectorList.Add(shadowDomSelector);

            if (true == UseOttoMagic)
                MagicHelper.SwitchToDefaultContent();
            else if (true == UsePuppeteer)
                PuppeteerClient.SwitchToDefaultContent().Execute();
            else
                this.BrowserDriver.SwitchTo().DefaultContent();

            GPALElement inElement = null;

            // Now walk the (possibly filtered) steps
            foreach (UnitOfWork.WebElementWithType contextType in CurrentUOW.ContextPath)
            {
                if (ElementType.IFrame == contextType.elementType)
                {
                    if (true == UseOttoMagic)
                        MagicHelper.InFrame(contextType.gPalElement.Css);
                    else if (true == UsePuppeteer)
                        PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                    else
                        BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                    // a search context belongs to one document - anything scoped in the frame we just left is
                    // stale, and a shadow root from the parent throws "no such shadow root" if reused here
                    inElement = null;
                }
                else if (ElementType.ShadowRoot == contextType.elementType)
                {
                    if (true == UseOttoMagic)
                        MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                    else if (true == UsePuppeteer)
                        PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                    // scope to THIS step's shadow root rather than the last one added to the unit of work, so a
                    // path with more than one shadow root walks through each in turn instead of collapsing them
                    inElement = contextType.gPalElement;
                }
            }

            ElementHelper.FindWebElements(
                this, CurrentUOW, shadowDomSelector, out _, out var tempElems, inElement);

            if (tempElems?.Count > 0)
            {
                var shadowElement = new UnitOfWork.WebElementWithType(
                    shadowDomSelector, tempElems[0], ElementType.ShadowRoot);

                CurrentUOW.ShadowRoot = tempElems[0];
                CurrentUOW.ShadowRoot.IsShadowRoot = true;

                CurrentUOW.ContextPath ??= new List<UnitOfWork.WebElementWithType>();
                CurrentUOW.ContextPath.Add(shadowElement);

                // Existing shadow root plumbing (unchanged)
                if (true == UseOttoMagic)
                {
                    // CurrentUOW.ShadowRoot.ShadowRoot = MagicHelper.GetShadowRoot(shadowDomSelector.SelectorPaths[0].SelectorPath); // this doesn't make sense, nothing useful can be returned
                    // tempElems[0].ShadowRoot = CurrentUOW.ShadowRoot;
                }
                else if (true == UsePuppeteer)
                {
                    //CurrentUOW.ShadowRoot.ShadowRoot = PuppeteerClient.GetShadowRoot(tempElems[0].Css).Execute<GPALElement>();
                    //tempElems[0].ShadowRoot = CurrentUOW.ShadowRoot;
                }
                else if (true == UseSelenium)
                {
                    ISearchContext shadowRoot = null;

                    // Use WebDriver's GetShadowRoot, which throws only when there is no open root.
                    // ExecuteJavaScriptObj returns null for both a closed root and a failed script, and a wrong null
                    // marks an open root closed, so searches return the host instead of its children.
                    try
                    {
                        shadowRoot = ((IWebElement)tempElems[0].WebElement).GetShadowRoot();
                    }
                    catch
                    {
                        // closed, handled below
                    }

                    if (null != shadowRoot)
                        CurrentUOW.ShadowRoot.WebElement = shadowRoot;  // dynamic - the search dispatches to it at runtime
                    else
                    {
                        CurrentUOW.ShadowRoot.IsClosedShadowRoot = true;
                        // a closed shadow root is never handed to webdriver - arguments[0].shadowRoot is null and
                        // GetShadowRoot throws. leave the host element in place so the root still has a real
                        // position to click relative to, and so searches inside it stay scoped to the host rather
                        // than silently falling back to the whole document and matching something unrelated.
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Closed shadow root for selector [{shadowDomSelector.Name}]. Will return host element for searches inside.", this, GPALObjectType.Browser);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Specify that GPAL is no longer working in an iFram or shadow dom and reset back to the main document.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector InMainDom()
        {
            CurrentUOW.ShadowRoot = null;
            CurrentUOW.ContextPath?.Clear();

            if (true == UseOttoMagic)
                MagicHelper.SwitchToDefaultContent();
            else if (true == UsePuppeteer)
                PuppeteerClient.SwitchToDefaultContent().Execute();
            else if (true == UseSelenium)
                this.BrowserDriver.SwitchTo().DefaultContent();

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Returned to main document", CurrentUOW);
            return this;
        }

        /// <summary>
        /// Specifies the Next Page button used to retrieve multiple pages of data.<br/>
        /// Use with .GetPages(numOfPages) to specify how to get those next pages.<br/>
        /// .GetGrid or any .SaveTo[CSV|Excel|File] will treat the current UOW as output Selectors from which to retrieve data.<br/>
        /// Retrieving data will retrieve only the current page if nothing is specified.<br/>
        /// Also useful with .WithAllThatMatch to retrieve repeating results (like on eBay)<br/>
        /// See also .WithInfiniteScroll
        /// </summary>
        /// <param name="nextPageButtonselector">Next Page Button selector</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithPagesAndGridActions WithNextPageButton(Selector nextPageButtonselector)
        {
            CurrentUOW.NextPageButton = nextPageButtonselector;
            BrowserSettings.CurrentSelector = nextPageButtonselector;  // for use by .WithHardware
            return this;
        }
        /// <summary>
        /// Specified the number of pages to retrieve when using GetGrid or any .SaveTo[CSV|Excel|File]<br/>
        /// Must also specify the Next Page button [.WithNextPageButton(selector)] or Infinite scroll [.WithInfiniteScroll] to tell GPAL how to retrieve next pages.<br/>
        /// </summary>
        /// <param name="pageCount">Number of pages to retrieve.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowGetGridAndFillInFrom WithPages(int pageCount)
        {
            CurrentUOW.PageCount = pageCount;
            return this;
        }
        /// <summary>
        /// Supply the values .Fetch drops into the numbered tokens of its request, one row per set of requests.<br/>
        /// A column of the row fills the token of the same number, so the first column fills {0}, the second {1},
        /// the same way FillInFrom hands a row of tokens to a row of inputs.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithPagesAndFetch WithTokensFrom(IGPALGrid<string> inputGrid)
        {
            CurrentUOW.FetchTokens = inputGrid;
            return this;
        }
        /// <inheritdoc cref="WithTokensFrom(IGPALGrid{string})"/>
        /// <param name="inputFile">The GPAL File definition. CSV values per line, one line per set of requests</param>
        public IAllowWithPagesAndFetch WithTokensFrom(GPALFile inputFile)
        {
            List<IGPALGrid<string>> tokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);

            CurrentUOW.FetchTokens = 0 == tokenList?.Count ? null : tokenList[0];
            return this;
        }
        /// <inheritdoc cref="WithTokensFrom(IGPALGrid{string})"/>
        /// <param name="inputDatabase">The GPAL Database definition. One column per token, one row per set of requests</param>
        public IAllowWithPagesAndFetch WithTokensFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);

            CurrentUOW.FetchTokens = CurrentUOW.InputDatabase?.Tokens;
            return this;
        }
        /// <inheritdoc cref="WithTokensFrom(IGPALGrid{string})"/>
        /// <param name="tokens">A single row of tokens, written as comma separated values</param>
        public IAllowWithPagesAndFetch WithTokensFrom(string tokens)
        {
            IGPALGrid<string> grid = GPAL.GridForType<string>();

            grid.AddRow(new List<string>(tokens?.Split(',') ?? new string[0]));
            CurrentUOW.FetchTokens = grid;
            return this;
        }
        /// <summary>
        /// Add a handler to call after a row of tokens is consumed and after all inputs are filled in with data.<br/>
        /// Only one CallAfterFillIn handler exists.<br/>
        /// Use FillInFrom[Database|File|GPALGrid] to loop thru data in your main workflow and then perform next workflow steps in the CallAfter handler.
        /// </summary>
        /// <param name="callAfterFillIn">Your callback</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallAfterFillIn(Browser.CallAfterFillInDelegate callAfterFillIn)
        {
            CurrentUOW.CallAfterFillIn = callAfterFillIn;

            return this;
        }
        /// <summary>
        /// Add a Unit of Work level CallIfFound handler to be called if any defined selectors find/match elements.<br/>
        /// This method will be called for every found selector in the current UOW.<br/>
        /// CallIf handlers can remove themselves by calling myBrowser.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="callIfFoundDelegate">Your callback</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallIfFound(Browser.CallIfDelegate callIfFoundDelegate)
        {
            CurrentUOW.CallIfFound.Add(callIfFoundDelegate);

            return this;
        }
        /// <summary>
        /// Add a Unit of Work level CallIfNotFound handler to be called if any defined selectors DO NOT find/match elements.<br/>
        /// This method will be called for every NOT FOUND selector in the current UOW.<br/>
        /// CallIf handlers can remove themselve by calling myBrowser.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="callIfNotFound">Your callback</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallIfNotFound(Browser.CallIfDelegate callIfNotFound)
        {
            CurrentUOW.CallIfNotFound.Add(callIfNotFound);

            return this;
        }
        /// <summary>
        /// Waits for the browser's file dialog, Save or Open, found by window class #32770 and the browser's process
        /// rather than by title.
        /// </summary>
        /// <param name="waitTimeInSeconds">How long to wait for it to appear</param>
        /// <returns>The dialog's window, or zero when none appeared</returns>
        private HWND WaitForCommonDialog(int waitTimeInSeconds)
        {
            int owner = BrowserSettings.Process?.Id ?? 0;

            return FormHelper.WaitForCommonDialog(owner, waitTimeInSeconds);
        }

        /// <summary>
        /// Define the time in seconds to WaitForWindow/Regex
        /// </summary>
        /// <param name="waitTimeInSeconds">Wait time in seconds</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WithWaitForWindowTimeout([Seconds] int waitTimeInSeconds)
        {
            BrowserSettings.WaitForWindowTimeoutInSeconds = waitTimeInSeconds;
            return this;
        }
        /// <summary>
        /// Add a persistent CallIfFound handler to be called if any defined persistent selectors find/match elements.<br/>
        /// Persistent selectors are always looked for before every action cross-cutting your workflow.<br/>
        /// Persistent handlers can remove themselves in the CallIfHandler by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="persistentCallIfFound">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserSettingsOrGoTo PersistentCallIfFound(CallIfDelegate persistentCallIfFound)
        {
            persistentUOW.CallIfFound.Add(persistentCallIfFound);

            return this;
        }
        /// <summary>
        /// Add a persistent CallIfNotFound handler to be called if any defined persistent selectors DO NOT find/match elements.<br/>
        /// Persistent selectors are always looked for before every action cross-cutting your workflow.
        /// Persistent handlers can remove themselves in the CallIfHandler by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="persistentCallIfNotFound">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserSettingsOrGoTo PersistentCallIfNotFound(CallIfDelegate persistentCallIfNotFound)
        {
            persistentUOW.CallIfNotFound.Add(persistentCallIfNotFound);

            return this;
        }
        /// <summary>
        /// Credential for sites that require a login.<br/>
        /// By <see cref="WebAuthType"/>: Basic, Proxy and Bearer are set before the first request; Digest answers the
        /// challenge; Form is only stored, for the workflow to fill the login page with selectors.<br/>
        /// Applied at the first navigation for the session. Never published to the event stream.
        /// </summary>
        /// <param name="credentials">Credentials built with GPAL.CredentialsFor</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserSettingsOrGoTo WithCredentials(ICredentials credentials)
        {
            BrowserSettings.Credentials = credentials;

            return this;
        }
        /// <summary>
        /// Adds a handler called when an operation on this browser fails; today only a navigation that did not
        /// load.<br/>
        /// Without one, GPAL reports it and continues, so selectors find nothing and CallIfNotFound runs. A handler
        /// can retry, go elsewhere, or end the run.<br/>
        /// With no browser handler, <see cref="GPAL.CallOnFail"/> is called; workflows sharing it tell themselves
        /// apart by the browser parameter.
        /// </summary>
        /// <param name="callOnFail">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserSettingsOrGoTo CallOnFail(CallOnFailDelegate callOnFail)
        {
            BrowserSettings.CallOnFail.Add(callOnFail);

            return this;
        }
        /// <summary>
        /// Calls this browser's CallOnFail handlers, or the global ones, and returns the result.<br/>
        /// Only Terminate changes what GPAL does; it ends the run as from CallAfterFetch.
        /// </summary>
        /// <param name="failure">What failed</param>
        /// <param name="detail">What the engine said about it</param>
        /// <returns>What the handlers decided, NotHandled when there are none</returns>
        internal CallIfStatus RaiseOnFail(GPALFailure failure, string detail)
        {
            CallIfStatus handled = CallIfStatus.NotHandled;
            List<CallOnFailDelegate> handlers = 0 < BrowserSettings.CallOnFail.Count ? BrowserSettings.CallOnFail : GPAL.GPALSettings.CallOnFailHandlers;

            foreach (CallOnFailDelegate handler in handlers)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallOnFail [{handler.Method.Name}] for [{failure}]", this, GPALObjectType.Browser);

                handled = handler(this, failure, detail);

                if (CallIfStatus.Terminate == handled)
                {
                    string str = $"CallOnFail handler [{handler.Method.Name}] requested program termination.";
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, str, this, GPALObjectType.Browser);
                    throw new GPALException($"{GPAL.MyMethodName()}: " + str);
                }

                if (CallIfStatus.Handled == handled)
                    break;
            }

            return handled;
        }
        /// <summary>
        /// Add a persistent selector to the persistent UOW. Persistent selectors are always looked for whenever you perform an action on a UOW, like LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the PersistentCallIf handlers to create workflows to deal with the persistent selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// Only one persistent Unit of Work exists. All selectors are evaluated in the order they are defined.<br/>
        /// Example: For browser, a persistent selector could look for a popup panel like 'Accept all cookies' that displays on all pages and may block processing..
        /// NOTE: persisten selectors are only looked for in content/frame 0 as they are designed to deal with page popups eminating from base content. Use workflows to deal with elements in frames
        /// </summary>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowBrowserSettingsOrGoTo WithPersistentSelector(Selector selector)
        {
            // when would we ever add another persistent uow?
            if (true == CurrentUOW.ActionCalled)
            {
                // Preserve context for new UnitOfWork
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot,
                    Browser = this
                };

                persistentUOW = new UnitOfWork() { Browser = this };
            }

            selector.Browser = this;
            selector.DeleteMe = false;         // Remove() latches this on and nothing else clears it, so a reused persistent selector would be swept on its first maintenance pass
            selector.RemovedMethods.Clear();   // a handler that took itself out of play in an earlier workflow is back in play for this one

            ResolveInteractionType(selector);

            // if we are reusing, we can't use cached results in a new uow
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;
            
            persistentUOW.WithSelectorList.Add(selector);   // CAVEAT: there is no way to know when this is defined that we are headless, so this selector could be hardware emulaiton which won't work
            return this;
        }


        /// <summary>
        /// Specify a column header for output. Headers are output in order defined.
        /// </summary>
        /// <param name="header">Header text</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithHeaderOrFileActions WithHeader(string header)
        {
            if (true == CurrentUOW.GetGridCalled)
            {
                CurrentUOW.GetGridCalled = false;
                CurrentUOW.HeaderList.Clear();
            }
            CurrentUOW.HeaderList.Add(header);
            return this;
        }
        /// <summary>
        /// Indicates the Selectors refer to/match repeating, multiple elements on the page.<br/>
        /// Use after all your <b>.WithSelector</b> statements. Used two ways.<br/>
        /// For scraping: This is used to retrieve multiple rows of data from the webpage, up to rowCount.<br/>
        /// For input tokens/strings: This will repeat the same input into multiple elements, up to rowCount of lines.<br/>
        /// NOTE: if called before any
        /// </summary>
        /// <param name="rowCount">Set total number of entries to retrieve (if this is longer than a page size).<br/>If less than the page size, retrieve the 'top rowCount' rows on each page for the number of pages specified using .WithPages(pageCount)<br/>int.MaxValue [default] work with all rows.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelectorExceptWithAll WithAllThatMatch(int rowCount = int.MaxValue)
        {
            // if we haven't started yet, this is called on the browser to set the per browser value
            // if called after an action, it's the same thing, it's for the whole browser
            if (0 == CurrentUOW.WithSelectorList.Count || true == CurrentUOW.ActionCalled)
                BrowserSettings.WithAllThatMatch = rowCount;
            else
                CurrentUOW.WithAllThatMatch = rowCount;
            return this;
        }
        /// <summary>
        /// Set the default window size for when the browser opens.
        /// As well, exiting fullscreen will return to this size.
        /// IF NOT SET: 'Normal' to restore the window will restore the window to the Size before enterinng fullscreen
        /// </summary>
        /// <param name="windowSize">Cannot be less than 100 in either dimension, nor larger than screen size</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllBrowserAndAllSelector WithWindowSize(Rectangle windowSize)
        {
            int screenWidth = Puppeteer.GetScreenWidthInt();
            int screenHeight = Puppeteer.GetScreenHeightInt();

            if (100 > windowSize.Width || 100 > windowSize.Height)
            {
                int widthToUse = Math.Min(windowSize.Width, 100);
                int heightToUse = Math.Min(windowSize.Height, 100);

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Requested browser sizes [{windowSize.Width}x{windowSize.Height}] cannot be less than 100. Using [{widthToUse}x{heightToUse}].", this, GPALObjectType.Browser);

                windowSize.Width = widthToUse;
                windowSize.Height = heightToUse;
            }
            else if (screenWidth < windowSize.Width || screenHeight < windowSize.Height)
            {
                int widthToUse = Math.Min(windowSize.Width, screenWidth);
                int heightToUse = Math.Min(windowSize.Height, screenHeight);

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Requested browser size [{windowSize.Width}x{windowSize.Height}] exceeds screen size [{screenWidth}x{screenHeight}]. Using [{widthToUse}x{heightToUse}].", this, GPALObjectType.Browser);

                windowSize.Width = widthToUse;
                windowSize.Height = heightToUse;

            }

            BrowserSettings.WindowSize = windowSize;

            return this;
        }
        /// <summary>
        /// Specifies to use hardware emulation when interacting with the Selector just defined with .WithSelector. Use this to override what is defined on the Selector.<br/>
        /// Selenium is the default method to interact with elements, but sometimes hardware emulation is the only way that works.<br/>
        /// You can also specify to execute javascript on the page to interact with elements.
        /// </summary>
        public IAllowAfterAnySelector WithHardware
        {
            get
            {
                // set this in the selector by calling it fluently, then resolve it again: on a hidden desktop or
                // headless, hardware is replaced with the engine's own input, as it is for a selector added with it
                _ = BrowserSettings.CurrentSelector.WithHardware;
                ResolveInteractionType(BrowserSettings.CurrentSelector);
                return this;
            }
        }
        /// <summary>
        /// Specifies to execute javascript on the page to interact with the Selector just defined with .WithSelector. Use this to override what is defined on the Selector.<br/>
        /// This is different than Selenium interaction.
        /// Selenium is the default method to interact with elements, but sometimes that doesn't work as expected.<br/>
        /// You can also specify to use hardware emulation to interact with elements.
        /// </summary>
        public IAllowAfterAnySelector WithJavaScript
        {
            get
            {
                // set this in the selector by calling it fluently, then resolve it again, as for a selector added with it
                _ = BrowserSettings.CurrentSelector.WithJavaScript;
                ResolveInteractionType(BrowserSettings.CurrentSelector);
                return this;
            }
        }
        /// <summary>
        /// Indicates the current page does not have a Next Page button, but an infinite scroll.<br/>
        /// GPAL pagination then consists of moving to the bottom and waiting for the DocumentReady before paging again.
        /// </summary>
        public IAllowWithPagesAndGridActions WithInfiniteScroll // TODO: add aprameter ScrolThenScrape, ScrapeThenScroll to indicate how the pave behaves (1 page in mem or all pages)
        {
            get
            {
                CurrentUOW.InfiniteScroll = true;
                return this;
            }
        }
        #region ACTION GETTERS
        /// <summary>
        /// Navigate back one page
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Back
        {
            get
            {
                InAction(false, false);
                // selenium doesn't know what happened
                // BrowserHelper.ExecuteJavaScript(this, "return history.back();");
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
                // selenium doesn't know what happened
                // BrowserHelper.ExecuteJavaScript(this, "return history.forward();");
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
                    Thread.Sleep(500);  // for some reason we check document ready before it even refreshes
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
        /// Scrolls the current tab down one page.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageDown
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageDown();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageDown().Execute();
                else
                    ScrollPage("down");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled down one page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scrolls the current tab to the bottom.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageEnd
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageEnd();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageEnd().Execute();
                else
                    ScrollToPosition("end");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled to the end of the page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scrolls the current tab to the top.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageTop
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageTop(); // Assuming you have a PageUp() method in MagicHelper
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageTop().Execute();
                else
                    ScrollToPosition("top");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled to the top of the page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Scrolls the current tab up one page.<br/>
        /// Ends the unit of work, since scrolling can change the page: read first, add selectors again after. The
        /// window need not be on top; every engine scrolls through its protocol.
        /// </summary>
        public IAllowBrowserActionOrAnySelector PageUp
        {
            get
            {
                InAction(false, false);

                if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.PageUp();
                else if (true == UsePuppeteer)
                    PuppeteerClient.PageUp().Execute();
                else
                    ScrollPage("up");

                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Scrolled up one page.", this, GPALObjectType.Browser);

                return this;
            }
        }
        /// <summary>
        /// Restore the browser to window state, unminimize
        /// </summary>
        public IAllowAllBrowserAndAllSelector Restore
        {
            get
            {
                BrowserSettings.FullScreen = false;
                BrowserSettings.Maximize = false;
                BrowserSettings.Minimize = false;

                //InAction(false, false);

                if (null != BrowserSettings.Process || 0 != BrowserSettings.ServiceDriverPid)
                    if (true == BrowserSettings.UseOttoMagic)
                    {
                        if (true == BrowserSettings.FullScreen)
                            MagicHelper.Normal();
                        else
                            MagicHelper.Restore();
                    }
                    else if (true == BrowserSettings.UsePuppeteer)
                    {
                        if (true == BrowserSettings.FullScreen)
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
                        MagicHelper.FullScreen();
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
        #endregion ACTION GETTERS
        #endregion <Browser Settings>
        #region <Browser Actions>
        /// <summary>
        /// Set whether to deny navigating to webpage if denied by robots.txt
        /// </summary>
        /// <param name="trueFalse">true or false</param>
        /// <returns></returns>
        public IAllowBrowserSettingsOrGoTo WithObeyRobotsTxt(bool trueFalse = false)
        {
            BrowserSettings.ObeyRobotsTxt = trueFalse;
            return this;
        }
        /// <summary>
        /// Set whether to respect robots-related meta tags (e.g. noindex/nofollow) on the page.
        /// </summary>
        /// <param name="trueFalse">True to respect robot meta tags, false [default] to ignore them</param>
        /// <returns>Fluent interface to specify more Browser settings or GoTo</returns>
        public IAllowBrowserSettingsOrGoTo WithRespectRobotMetaTags(bool trueFalse = false)
        {
            BrowserSettings.RespectRobotMetaTags = trueFalse;
            return this;
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

            // keep this in, new page, new uow
            CurrentUOW = new UnitOfWork() { Browser = this }; // so an immediate .inshadowdom or .inframe starts new and doesn't carry forward

            // DO NOT PASS IN URL for first run startup, it doesn't like it in headless mode puppeteer
            if (true == InAction(false, false, null))   // actions have ended but we do have to signal how to denote a new UOW, too
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

                    // do we have an open browser? no. One reached through the debug port or GPALRestAPI is already
                    // open, so a new tab is opened in it when one is asked for.
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
                            //_ = NextTab;
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

                        // use shortcuiting, if it's not set, why check?
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

                        BrowserSettings.GPALUrlList.Add(URL);

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
        /// <summary>
        /// Makes every GoTo and Get look like a click from a Google search: https://www.google.com/ becomes the
        /// browser's referrer, which <see cref="SetReferrer"/> sends with each GoTo's own navigation until
        /// WithUseReferrer clears or replaces it. Pages reached by clicking, iframes and what the page loads carry
        /// their real referrers, as they would after a real click. A referrer WithUseReferrer already set is kept.
        /// </summary>
        private void ApplyGoogleReferrer()
        {
            bool applied = false == (true == UseSelenium && BrowserType.FireFox == BrowserSettings.BrowserType);

            if (true == applied && null == BrowserSettings.Referrer)
                BrowserSettings.Referrer = "https://www.google.com/";

            if (true == applied)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Stealth Google Referrer applied.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot set a header, so the Google referrer was not applied", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// Tracks in-flight XHR/fetch requests on window.__gpalNetworkMonitor: inflight is the requests not yet
        /// finished, since is when the page started being watched, and droppedBelow[n] is when the in-flight count
        /// last dropped below n. One look at it answers whether the count stayed at or below a limit over a past
        /// stretch of time. Does nothing when the page already has it.
        /// </summary>
        internal const string NetworkMonitorScript = @"
                    (function() {
                        if (!window.__gpalNetworkMonitor) {
                            const monitor = window.__gpalNetworkMonitor = { inflight: 0, since: Date.now(), droppedBelow: {} };
                            const started = () => { monitor.inflight++; };
                            const finished = () => { monitor.droppedBelow[monitor.inflight] = Date.now(); monitor.inflight--; };

                            const origOpen = XMLHttpRequest.prototype.open;
                            XMLHttpRequest.prototype.open = function() {
                                this.addEventListener('loadstart', started);
                                this.addEventListener('loadend', finished);
                                origOpen.apply(this, arguments);
                            };

                            const origFetch = window.fetch;
                            window.fetch = function(...args) {
                                started();
                                return origFetch(...args).finally(finished);
                            };
                        }
                    })();
                ";
        /// <summary>
        /// Injects <see cref="NetworkMonitorScript"/>, used to detect network idle.
        /// </summary>
        private void ApplyNetworkMonitor()
        {
            string networkMonitorScript = NetworkMonitorScript;

            if (UsePuppeteer)
            {
                PuppeteerCommunicator.EnableFetchScriptInjection(networkMonitorScript, null)
                    .GetAwaiter().GetResult();
            }
            else if (BrowserType.FireFox == BrowserSettings.BrowserType)
            {
                // Firefox has no CDP preload script support — inject post-load via ExecuteScript instead.
                // The guard (function(){if(!window.__gpalNetworkMonitor)...}) makes re-injection safe on each navigation.
                ((OpenQA.Selenium.IJavaScriptExecutor)BrowserDriver).ExecuteScript(networkMonitorScript);
            }
            else
            {
                // Chrome and Edge count requests on GPAL's own connection to the browser, so the page gets no monitor
                BrowserSettings.TabScripts ??= SeleniumTabScripts.Start(BrowserSettings);
            }

            if (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Network idle counted from the browser's network events.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Network idle monitor script injected.", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// Injects scripts to hide signs of CDP-based automation: pins the Error stack to defeat stack-based CDP
        /// detection, and on Selenium overrides navigator.webdriver to undefined.
        /// </summary>
        private void ApplyCDPProtection()
        {
            // hide CDP port detection by adding a script that will always run when a page loads (before the website loads any detection scripts)
            // https://www.reddit.com/r/webscraping/comments/1evht3i/help_in_bypassing_cdp_detection/
            // https://github.com/rebrowser/rebrowser-patches
            // https://github.com/kaliiiiiiiiii/brotector/blob/master/brotector.js#L377 // string detections
            string antiAntiBotScript = @"
                        (function() {
                            var originalError = Error;
                            Object.defineProperty(Error.prototype, 'stack', {
                                configurable: false,
                                enumerable: true,
                                writable: true,
                                value: (function() {
                                    try {
                                        throw new originalError();
                                    } catch (e) {
                                        return e.stack;
                                    }
                                })()
                            });

                            // Pins each new error's stack as a plain value, so a page cannot swap in a getter that
                            // fires when CDP serializes the error. The rest of the error stays writable.
                            window.Error = new Proxy(originalError, {
                                construct(target, args, newTarget) {
                                    var instance = Reflect.construct(target, args, newTarget);
                                    Object.defineProperty(instance, 'stack', { value: instance.stack, writable: true, enumerable: false, configurable: false });
                                    return instance;
                                }
                            });
                        })();
                    ";

            if (true == UsePuppeteer)
            {
                PuppeteerCommunicator.EnableFetchScriptInjection(antiAntiBotScript, null).GetAwaiter().GetResult();
            }
            else if (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType)
            {
                BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, antiAntiBotScript);
            }

            // Selenium only: chromedriver launches with --enable-automation, which sets navigator.webdriver. GPAL
            // launches Puppeteer's browser without it, so it already reports false there.
            string antiAntiBotScript2 = "Object.defineProperty(navigator, 'webdriver', { get: () => undefined })";
            if (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType)
            {
                BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, antiAntiBotScript2);
            }

            // Both injections above need Puppeteer, or Selenium on Chrome or Edge.
            bool applied = true == UsePuppeteer || (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType);

            if (true == applied)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Stealth CDP protection applied.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot run a script on every new document, so CDP protection was not applied", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// Applies the configured StealthType protections (CDP, network monitor, Google referrer, etc.) once
        /// per browser session, based on <see cref="BrowserSettings.StealthApplied"/>.
        /// </summary>
        private void CheckAndApplyStealth()
        {
            // hide CDP port detection by adding a script that will always run when a page loads (before the website loads any detection scripts)
            // https://www.reddit.com/r/webscraping/comments/1evht3i/help_in_bypassing_cdp_detection/
            // https://github.com/rebrowser/rebrowser-patches
            // https://github.com/kaliiiiiiiiii/brotector/blob/master/brotector.js#L377 // string detections

            if (false == BrowserSettings.StealthApplied)
            {
                if (false == UseOttoMagic && BrowserType.FireFox != BrowserType)
                {
                    if (BrowserSettings.StealthType.HasFlag(StealthType.CDP))
                        ApplyCDPProtection();
                }

                if (BrowserSettings.StealthType.HasFlag(StealthType.GoogleReferrer))
                {
                    // set referer to google to make us look like we are coming in from search results click
                    try 
                    { 
                        ApplyGoogleReferrer(); 
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex) 
                    { 
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Google Referrer stealth skipped", this, GPALObjectType.Browser, ex); 
                    }
                }
            }
            BrowserSettings.StealthApplied = true;
        }

        /// <summary>
        /// Close the current in focus, active tab.
        /// </summary>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector CloseTab(dynamic URLorTabId = null)
        {
            // special use case closing a tab; can't call previoustab because we aren't in a good window yet and it will try to top
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

                    // guarded: a flaky/off-screen EdgeDriver can hang the full 20s command timeout here and throw,
                    // which must not crash the workflow - the tab is being torn down regardless.
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
                    GetSetCurrentUrl();
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
        /// NOTE: New window is just another tab. So, if a window is opened, should be able to still use .NextTab
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
                // due to clicks opening new tabs and other reasons, we can't track open tab count unless we have our own browser extension (can't do it in javascript, only extension)

                // browser will be topped below by selenium when we switch tabs
                // BrowserHelper.TopBrowser(this);

                // next tab - control page-down
                // when we move away from selenium, we may use this
                // HardwareHelper.SendKey(GPAL.VK_NEXT, ModifierKeys.Control);   // page down

                // using selenium, it must know which tab we are on to function properly, it doesn't know we switched tabs
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

                return this;
            }
        }
        /// <summary>
        /// Used to specify the next GoTo command should open in a new tab. The new tab is then the active tab.<br/>
        /// Use .NextTab and .PreviousTab to navigate tabs.
        /// NOTE: Applies only once. Use .WithNewTab before each .GoTo to open in a new tab.
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
            BrowserSettings.Browser.GoTo(url);  // will set currentgpalurl and add url to list
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
                // due to clicks opening new tabs and other reasons, we can't track open tab count unless we have our own browser extension (can't do it in javascript, only extension)

                // browser will be topped below by selenium when we switch tabs
                // BrowserHelper.TopBrowser(this);

                // previous tab using ctrl-page up
                // when we move away from selenium, we may use this
                // HardwareHelper.SendKey(GPAL.VK_PRIOR, ModifierKeys.Control);   // page up

                // using selenium, it must know which tab we are on to function properly, it doesn't know we switched tabs above
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

                return this;
            }
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for the first element found for each selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClick()
        {
            return LeftClick(ModifierKeys.NONE);
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for each element found for selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClick(ModifierKeys modifierKeys)
        {
            string priorUrl = BrowserSettings.CurrentURL;
            object priorDocument = BrowserHelper.MarkDocument(this);   // the page we are clicking on, to tell later if we left it

            // snapshot open handles before the click so we can tell if the click opened a new tab (target=_blank)
            var handlesBefore = (true == UseSelenium) ? new HashSet<string>(BrowserDriver.WindowHandles) : null;

            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.LeftClick(this, modifierKeys);

            if (true == UseSelenium)
            {
                ResolveGhostTabHandle();   // make sure Edge's phantom tab is identified before we count new tabs

                // in case this opens a new tab
                var handles = BrowserDriver.WindowHandles.ToList();
                string currentHandle = BrowserDriver.CurrentWindowHandle;

                CurrentTabIdx = handles.IndexOf(currentHandle);

                // reliable our-tabs counter: add only genuinely new tabs this click opened, never Edge's ghost
                int opened = 0;

                foreach (string handle in handles.Where(h => false == handlesBefore.Contains(h) && h != GhostTabHandle))
                {
                    RecordTabWindow(handle, currentHandle);   // a click opens into the window it was clicked in
                    opened++;
                }

                if (0 < opened) tabsWeOpened += opened;
            }

            // The click returns before the url changes; wait up to NavigationGraceMs for a navigation.
            if (true == BrowserHelper.WaitForNavigationOrTime(this, priorUrl, priorDocument, BrowserSettings.NavigationGraceMs))
            {
                BrowserHelper.CheckDocumentReady(this);

                // read the url again now the document is committed, not while it was still tearing down
                string currentUrl = GetSetCurrentUrl();

                if (true == UseOttoMagic)
                {
                    int tabId = GetActiveTabId();
                    TabTuple tabTuple = new TabTuple() { TabId = tabId, Url = currentUrl };
                    UpdateActiveTab(tabTuple);
                }
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else
                    BrowserDriver.SwitchTo().DefaultContent();
            }

            return this;
        }
        /// <summary>
        /// Left click the element(s) found for Selector parameter, creates a UOW of 1 element<br/>
        /// One left click w/ no modiofiers pressed for each element found for the selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClick(Selector selector)
        {
            if (null != selector)
                WithSelector(selector);

            return LeftClick(ModifierKeys.NONE);
        }
        /// <summary>
        /// Clicks the option element(s) found for the current selector(s) using the given select-click behavior
        /// (e.g. RandomSelect, SequentialSelect).
        /// </summary>
        /// <param name="selectClickType">Select-menu click behavior</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SelectClick(SelectClickType selectClickType)
        {
            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                string priorUrl = BrowserSettings.CurrentURL;
                object priorDocument = BrowserHelper.MarkDocument(this);   // the page we are clicking on, to tell later if we left it
                ElementHelper.GenericClick(this, selectClickType);

                if (true == BrowserHelper.WaitForNavigationOrTime(this, priorUrl, priorDocument, BrowserSettings.NavigationGraceMs))
                    BrowserHelper.CheckDocumentReady(this);
            }

            GetSetCurrentUrl();

            return this;
        }
        /// <summary>
        /// Stealth (Runtime.disable before) Left click the element(s) found for Selector parameter, creates a UOW of 1 element<br/>
        /// One left click w/ no modifiers pressed for each element found for the selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector StealthLeftClick(Selector selector)
        {
            if (null != selector)
                WithSelector(selector);

            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                // OttoMagic's normal click is an in-page script event, so ask the extension for a trusted click.
                // Puppeteer's click is already real input and Runtime is never enabled there, so it is left alone.
                if (true == UseOttoMagic)
                    ElementHelper.LeftClick(this, trusted: true);
                else if (true == UsePuppeteer)
                    ElementHelper.LeftClick(this);
                else if (BrowserType.FireFox != BrowserSettings.BrowserType)
                {
                    BrowserHelper.ExecuteCdp(BrowserDriver, "Runtime.disable", new Dictionary<string, object> { });
                    ElementHelper.LeftClick(this);
                    BrowserHelper.ExecuteCdp(BrowserDriver, "Runtime.enable", new Dictionary<string, object> { });
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot disable the runtime around a click, so this is an ordinary left click", this, GPALObjectType.Browser);
                    ElementHelper.LeftClick(this);
                }
            }

            GetSetCurrentUrl();

            return this;
        }
        /// <summary>
        /// Left double click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for each element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftDoubleClick()
        {
            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.GenericClick(this, ClickType.LeftDoubleClick);
                BrowserHelper.CheckDocumentReady(this);
            }
            // NOTE: should we expect this to possibly change the url? how many websites do you interact that take double clicks? only a custom interface, i think.
            return this;
        }
        /// <summary>
        /// Midle click the element found for Selector(s) defined in the UOW<br/>
        /// One middle click for each element found for each selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector MiddleClick()
        {
            string priorUrl = BrowserSettings.CurrentURL;

            // snapshot open handles before the click so we can tell which tab(s) the middle-click opened
            var handlesBefore = (true == UseSelenium) ? new HashSet<string>(BrowserDriver.WindowHandles) : null;

            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.MiddleClick(this);

            string currentUrl = GetSetCurrentUrl();

            if (true == UseSelenium)
            {
                ResolveGhostTabHandle();   // make sure Edge's phantom tab is identified before we count new tabs

                // in case this opens a new tab
                var handles = BrowserDriver.WindowHandles.ToList();
                string currentHandle = BrowserDriver.CurrentWindowHandle;

                CurrentTabIdx = handles.IndexOf(currentHandle);

                // reliable our-tabs counter: add only genuinely new tabs this middle-click opened, never Edge's ghost
                int opened = 0;

                foreach (string handle in handles.Where(h => false == handlesBefore.Contains(h) && h != GhostTabHandle))
                {
                    RecordTabWindow(handle, currentHandle);   // a middle click opens into the window it was clicked in
                    opened++;
                }

                if (0 < opened) tabsWeOpened += opened;
            }

            if (false == UrlHelper.AreEquivalent(priorUrl, currentUrl))
            {
                if (true == UseOttoMagic)
                {
                    int tabId = GetActiveTabId();
                    TabTuple tabTuple = new TabTuple() { TabId = tabId, Url = currentUrl };
                    UpdateActiveTab(tabTuple);
                }
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else
                    BrowserDriver.SwitchTo().DefaultContent();
            }

            return this;
        }
        /// <summary>
        /// Right click the element found for Selector(s) defined in the UOW<br/>
        /// One right click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RightClick(Selector selector = null)
        {
            if (null != selector)
            {
                // if we are using GET, we are headless, there is no hardware interaction, force selenium on any selector, overriding definitions
                if (true == BrowserSettings.UseHeadless)
                    selector.InteractionType = InteractionType.Selenium;
                WithSelector(selector);
            }
            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.GenericClick(this, ClickType.RightClick);
                BrowserHelper.CheckDocumentReady(this);
            }
            // NOTE: should we expect this to possibly change the url? how many websites do you interact that take double clicks? only a custom interface, i think.
            return this;
        }

        /// NOTE: for programming use  by ElementAssistant we have a second method and use some static flags to set info for leftclickanddownload
        bool useEAvars = false;
        GPALElement EAelement = null;
        Selector EAselector = null;
        /// <summary>
        /// Entry point used by ElementAssistant.LeftClickAndDownload to download from a specific element/selector
        /// rather than the current unit of work's selector list.
        /// </summary>
        /// <param name="gPalFile">GPALFile to download to</param>
        /// <param name="webElement">Element to click to start the download</param>
        /// <param name="selector">Selector associated with the element</param>
        internal void LeftClickAndDownload(GPALFile gPalFile, GPALElement webElement, Selector selector)
        {
            useEAvars = true;
            EAelement = webElement;
            EAselector = selector;

            LeftClickAndDownload(gPalFile);

        }
        // Seconds to wait for a Save As dialog that may not open.
        internal const int UnexpectedSaveAsWaitSeconds = 2;
        internal const int ProfileRestoreWaitMs = 5000;      // how long to give the browser to shut down before the profile goes back

        /// <summary>
        /// Left clicks the element(s) found for the current selector(s) and downloads the resulting file to
        /// <paramref name="gPalFileToDownloadTo"/>.
        /// </summary>
        /// <param name="gPalFileToDownloadTo">GPALFile to download to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector LeftClickAndDownload(GPALFile gPalFileToDownloadTo)
        {
            string fullFilePathToSave;
            HWND windowOpened = IntPtr.Zero;
            List<string> returnFilename = new List<string>();
            bool noSaveAsDialog = false;
            FileDownloaded = false;
            int rowCount = 0;

            if (true == useEAvars)
            {
                DownloadWorkflow(EAelement, EAselector);
                return this;
            }

            int DownloadWorkflow(GPALElement webElement, Selector selector)
            {
                GPALFile fileToSave = gPalFileToDownloadTo.Next;
                fullFilePathToSave = FileHelper.CleanUpFileDestination(fileToSave.Filename, out string newFilename, BrowserSettings.DeleteFileBeforeDownload);
                returnFilename.Add(fullFilePathToSave);  // return the filenames generated

                string currentUrl = null, currentUrl2 = null;

                currentUrl = BrowserHelper.GetCurrentUrl(BrowserSettings);

                FileDownloaded = null;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloading to [{fullFilePathToSave}]", this, GPALObjectType.Browser);

                if (true == BrowserSettings.UseDirectDownload)
                {
                    // Download from inside the page first, with the browser's cookies and clearance, then in a tab.
                    // Not HttpClient: without cookies a file behind a login returns 403.
                    string directUrl = FileHelper.GetDownloadUrl(webElement);

                    if (true == string.IsNullOrWhiteSpace(directUrl))
                    {
                        // No download url on the element, usually a script-driven button; it has to be clicked.
                        FileDownloaded = false;
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{webElement?.TagName}] carries no url to download, so WithUseDirectDownload has nothing to ask for. Leave it off and the click is used instead", this, GPALObjectType.Browser);
                    }
                    else
                    {
                        FileDownloaded = FileHelper.FetchOrDownloadInTab(directUrl, fullFilePathToSave, this);

                        if (false == FileDownloaded)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{directUrl}] did not download to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                    }
                }
                // No prompt (or headless) means no Save As dialog; watch the download directory. OpenPDFExternally is
                // a profile preference neither CDP nor the extension can change, so it is not checked.
                else if (true == BrowserSettings.UseHeadless || false == BrowserSettings.PromptForDownload)    // headless mode or no prompt for filename, monitor default download dir
                {
                    // so assume the file will download
                    string watchedDirectory = FileHelper.SetupDownloadWatcher(fullFilePathToSave, BrowserSettings);

                    // BOTH OF THESE do not check for completion, just sets download path and clicks
                    if (true == BrowserSettings.UsePuppeteer)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][Left] clicked with modifiers [NONE].");
                        PuppeteerClient.LeftClickAndDownload(webElement.ElementHandle).DownloadTo(fileToSave).Execute();
                    }
                    else if (true == BrowserSettings.UseOttoMagic)
                    {
                        // These branches click through their own client, not ElementHelper.Click, so they publish the
                        // click event themselves; otherwise a missing file cannot be told from a missed click.
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][Left] clicked with modifiers [NONE].");
                        MagicHelper.LeftClickAndDownload(webElement.Css, fileToSave);
                    }
                    else   // selenium
                    {
                        BrowserHelper.SetHeadlessDownload(BrowserSettings, fullFilePathToSave);
                        ElementHelper.Click(this, selector, webElement, ClickType.LeftClick, ModifierKeys.NONE);
                    }

                    Thread.Sleep(250);
                    // NOTE: new scenario, some nag modal pops up before the download starts
                    // define those as persistent selectors and check persistent selectors here after we click to automatically dismiss them
                    string urlAfterClick = BrowserHelper.GetCurrentUrl(BrowserSettings);

                    // Persistent selectors apply only if the click stayed on the page.
                    if (true == UrlHelper.AreEquivalent(currentUrl, urlAfterClick))
                        CheckPersistentSelectors();

                    if (false == UrlHelper.AreEquivalent(currentUrl, urlAfterClick)
                        && true == fileToSave.Filename.EndsWith(".pdf")
                        && true == BrowserHelper.TestUrlForPDF(this, urlAfterClick))
                    {
                        //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{urlAfterClick}] is the pdf itself, fetching it rather than waiting on a Save As dialog.", this, GPALObjectType.Browser);
                        FileDownloaded = BrowserHelper.DownloadPdfFile(urlAfterClick, fullFilePathToSave, this);
                    }

                    // In case PromptForDownload is not honored or missing or somehow not working
                    /*
                    if (false == BrowserSettings.UseHeadless)
                    {
                        HWND unexpectedDialog = WaitForCommonDialog(UnexpectedSaveAsWaitSeconds);

                        if (IntPtr.Zero != unexpectedDialog)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] asked where to save the file even though prompting was turned off. Answering the dialog with [{newFilename}].", this, GPALObjectType.Browser);

                            HardwareHelper.SendString(newFilename);
                            HardwareHelper.SendKey(GPAL.VK_RETURN);
                        }
                    }
                    */

                    // The timeout counts time without progress. It restarts whenever the file size changes, so a
                    // moving download is never cut off and a stalled or unstarted one ends.
                    Stopwatch sinceProgress = Stopwatch.StartNew();
                    long lastSeen = long.MinValue;

                    // A false FileDownloaded is not final: the watcher fires many times, and an early event can be
                    // false before a later one finds the file. The loop ends on download or on the allowance.
                    while (null == FileDownloaded || false == FileDownloaded)
                    {
                        // NOTE: it is entirely possible, since we do not destroy the filewatcher, that the file could still come in and be handled, i've not seen it, but it's possible
                        long progress = FileHelper.DownloadProgress(watchedDirectory, fullFilePathToSave);

                        if (progress != lastSeen)
                        {
                            lastSeen = progress;
                            sinceProgress.Restart();
                        }
                        else if (sinceProgress.Elapsed.TotalSeconds > FileHelper.AllowanceFor(watchedDirectory, BrowserSettings))
                            break;

                        Thread.Sleep(100); // Prevents 100% CPU usage
                    }

                    currentUrl2 = BrowserHelper.GetCurrentUrl(BrowserSettings);

                    // but maybe it's a pdf opening in the browser itself, so check to see if that happened and if the url is a pdf file
                    // of course it is possible the url won't contain .pdf and served up via a header? we might have to chack that
                    if (false == (bool)(FileDownloaded ?? false) && false == BrowserSettings.OpenPDFExternally && true == fileToSave.Filename.EndsWith(".pdf"))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Testing [{currentUrl2}] for PDF file", this, GPALObjectType.Browser);
                        if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2) && true == BrowserHelper.TestUrlForPDF(this, currentUrl2))
                        {
                            FileDownloaded = BrowserHelper.DownloadPdfFile(currentUrl2, fullFilePathToSave, this);
                            noSaveAsDialog = !(bool)FileDownloaded;
                        }
                    }

                    // we're headless, no fallback, if it didn't download it's not going someplace other than where we specified
                    if (false == (bool)(FileDownloaded ?? false))
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File not downloaded to [{fullFilePathToSave}].", this, GPALObjectType.Browser);
                        FileHelper.DiscardAbandonedDownload(BrowserSettings);

                }
                else if (false == BrowserSettings.UseHeadless)    // if we are not headless, we can handle the dialog or direct download
                {
                    ElementHelper.Click(this, selector, webElement, ClickType.LeftClick, ModifierKeys.NONE);

                    string urlAfterClick = BrowserHelper.GetCurrentUrl(BrowserSettings);

                    // NOTE: new scenario, some nag modal pops up before the download starts
                    // define those as persistent selectors and check persistent selectors here after we click to automatically dismiss them
                    if (true == UrlHelper.AreEquivalent(currentUrl, urlAfterClick))
                        CheckPersistentSelectors();

                    // The click navigated to the pdf, so no Save As will open. Download it from its url and skip the
                    // dialog wait. OpenPDFExternally is not checked: had the browser honored it, it would not have
                    // navigated.
                    if (false == UrlHelper.AreEquivalent(currentUrl, urlAfterClick)
                        && true == fileToSave.Filename.EndsWith(".pdf")
                        && true == BrowserHelper.TestUrlForPDF(this, urlAfterClick))
                    {
                        //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{urlAfterClick}] is the pdf itself, fetching it rather than waiting on a Save As dialog.", this, GPALObjectType.Browser);
                        FileDownloaded = BrowserHelper.DownloadPdfFile(urlAfterClick, fullFilePathToSave, this);
                    }

                    if (false == (bool)(FileDownloaded ?? false) && true == BrowserSettings.PromptForDownload)
                        windowOpened = WaitForCommonDialog(BrowserSettings.WaitForWindowTimeoutInSeconds);

                    // The pdf is already downloaded; skip the Save As recovery below, which would report a false
                    // failure and use an element gone with the navigation.
                    if (true == (bool)(FileDownloaded ?? false))
                        noSaveAsDialog = false;
                    // save as window didn't open in the allotted timeout, or we set to no prompt and a direct download
                    else if (IntPtr.Zero != windowOpened)
                    {
                        HardwareHelper.SendString(newFilename);
                        HardwareHelper.SendKey(GPAL.VK_RETURN);

                        // is the file save dialog still open? send ENTER again
                        windowOpened = WaitForCommonDialog(1);

                        if (IntPtr.Zero != windowOpened)
                            HardwareHelper.SendKey(GPAL.VK_RETURN);

                        string actualDownloadedFilename = null;

                        actualDownloadedFilename = FileHelper.WaitForDownloadToFinish(newFilename, BrowserSettings.DownloadTimeoutInSec);

                        FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename);

                        if (false == FileDownloaded)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File not downloaded to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                            FileHelper.DiscardAbandonedDownload(BrowserSettings);

                    }
                    else
                    {
                        if (true == BrowserSettings.PromptForDownload)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to handle Save As... dialog [{fullFilePathToSave}][{selector.Name}]", BrowserSettings, GPALObjectType.Other);

                        noSaveAsDialog = true;

                        // NOTE: maybe it's a pdf opening in the browser itself, so check to see if that happened and if the url is a pdf file
                        currentUrl2 = BrowserHelper.GetCurrentUrl(BrowserSettings);

                        // OpenPDFExternally is not checked here either; no Save As appeared, so the browser ignored it.
                        if (false == (bool)(FileDownloaded ?? false) && true == fileToSave.Filename.EndsWith(".pdf"))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Testing [{currentUrl2}] for PDF file", this, GPALObjectType.Browser);
                            if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2) && true == BrowserHelper.TestUrlForPDF(this, currentUrl2))
                            {
                                FileDownloaded = BrowserHelper.DownloadPdfFile(currentUrl2, fullFilePathToSave, this);
                                noSaveAsDialog = !(bool)FileDownloaded;
                            }
                        }
                    }
                }

                // we didn't get a save as dialog within BrowserSettings.WaitForWindowTimeoutInSeconds which defaults to 30 seconds
                // so why? well, the website might have downloaded directly using the current browser default download directory
                // so we will attempt to see if a file was downloaded matching the file extension of the GPALFile the user specified
                if (true == noSaveAsDialog)
                {
                    string downloadDirectory = FileHelper.GetDefaultDownloadDirectory(this);
                    string downloadUrl = FileHelper.GetDownloadUrl(webElement);
                    string filename = Path.GetFileName(downloadUrl) ?? fullFilePathToSave;

                    string wildcardFilename = "*" + Path.GetExtension(filename);
                    string systemDownloadedTo = Path.Combine(downloadDirectory, wildcardFilename);
                    string actualDownloadedFilename = null;

                    actualDownloadedFilename = FileHelper.WaitForDownloadToFinish(systemDownloadedTo, BrowserSettings.DownloadTimeoutInSec);

                    FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename);

                    if (true == FileDownloaded)
                    {
                        if (false == fullFilePathToSave.Equals(actualDownloadedFilename))
                        {
                            FileHelper.EnsureFolderFor(fullFilePathToSave);
                            File.Move(actualDownloadedFilename, fullFilePathToSave);
                            if (false == actualDownloadedFilename.Equals(fullFilePathToSave) && true == File.Exists(actualDownloadedFilename))
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloaded [{actualDownloadedFilename}] unable to move to [{fullFilePathToSave}]", null, GPALObjectType.None);
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{actualDownloadedFilename}] moved to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                        }
                    }
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{filename}] not found in [{downloadDirectory}]", this, GPALObjectType.Browser);
                }

                if (null != FileHelper.watcher)    // we might not have disposed of watcher
                {
                    try
                    {
                        FileHelper.watcher.Dispose();
                        FileHelper.watcher = null;
                    }
                    catch { }
                }

                if (false == BrowserSettings.UseHeadless && BrowserType.Edge == BrowserSettings.BrowserType)    // dismiss the 'downloads' popup which can steal keys, clicks or get in the way
                {
                    // KLUDGE: must be a hardware top to dismiss 'downloads' by clicking the browser chrome
                    BrowserHelper.TopBrowser(this, false, true);
                }

                if (int.MaxValue != CurrentUOW.WithAllThatMatch && ++rowCount == CurrentUOW.WithAllThatMatch)
                    return -1;
                else 
                    return 0;
            }

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    ReadOnlyCollection<GPALElement> webelements = ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                    if (null != webelements)
                    {
                        // Save the elements' page url, to return to if a download click navigates away.
                        string pageOfTheElements = BrowserHelper.GetCurrentUrl(BrowserSettings);
                        int matchCount = matchedElements.Count;

                        for (int elementIdx = 0; elementIdx < matchCount; elementIdx++)
                        {
                            string urlNow = BrowserHelper.GetCurrentUrl(BrowserSettings);

                            // The click navigated away: go back and find the elements again. The old references throw
                            // an unknown reference error.
                            if (false == UrlHelper.AreEquivalent(pageOfTheElements, urlNow))
                            {
                                GoTo(pageOfTheElements);
                                ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool _, out matchedElements);

                                if (matchedElements.Count != matchCount)
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{selector.Name}] matched [{matchedElements.Count}] on the way back rather than [{matchCount}], so the remaining downloads may not be the ones first found.", this, GPALObjectType.Browser);
                            }

                            if (elementIdx >= matchedElements.Count)
                                break;

                            // int.MaxValue tells us we have met our .WithAllThatMatch elements to process
                            if (-1 == DownloadWorkflow(matchedElements[elementIdx], selector))
                                break;
                        }
                    }
                }

                foreach (string filepath in returnFilename)
                {
                    gPalFileToDownloadTo.ReturnFilenames.Add(filepath); // return the filenames we generated
//                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File saved to [{filepath}]");
                }
            }

            GetSetCurrentUrl();

            return this;
        }
        /// <summary>
        /// Entry point used by ElementAssistant.LeftClickAndUpload to upload from a specific element/selector
        /// rather than the current unit of work's selector list.
        /// </summary>
        /// <param name="gPalFile">GPALFile to upload from</param>
        /// <param name="webElement">Element to click to start the upload</param>
        /// <param name="selector">Selector associated with the element</param>
        internal void LeftClickAndUpload(GPALFile gPalFile, GPALElement webElement, Selector selector)
        {
            useEAvars = true;
            EAelement = webElement;
            EAselector = selector;

            LeftClickAndUpload(gPalFile);

        }
        /// <summary>
        /// Left click the input[type=file] or custom control button
        /// Headful - enter filename in open file dialog
        /// Headless - selenium and puppeteer can use CDP top populate files, OttoMagic requires the native app to stream the file to the browser...
        /// </summary>
        /// <param name="gPalFileToUpload"></param>
        /// <returns></returns>
        public IAllowBrowserActionOrAnySelector LeftClickAndUpload(GPALFile gPalFileToUpload)
        {
            HWND windowOpened = IntPtr.Zero;

            if (true == useEAvars)
            {
                UploadWorkflow(EAelement, EAselector, false);   // NOTE: CAVEAT: not sure this makes sense, once again, trying to interpret the concept of gpalfile having more than one file
                return this;
            }

            // NOTE: if you are providing a generic selector, say it finds 3 upload buttons, if you only supply 2 filenames in gg
            int UploadWorkflow(GPALElement webElement, Selector selector, bool uploadAllFiles)
            {
                string fileToUpload = null;

                // Check .multiple before the loop; the first SendKeys makes the element stale. Read the property
                // with JavaScript: GetAttribute capitalises the name and misses the driver's special case for this
                // valueless boolean attribute.
                bool sendAllAtOnce = true == UseSelenium
                                     && 1 < gPalFileToUpload.Count
                                     && true == Convert.ToBoolean(BrowserHelper.ExecuteJavaScript(this, "return arguments[0].multiple;", webElement.WebElement) ?? false);

                for (int fileCount = 0; fileCount < gPalFileToUpload.Count; fileCount++)
                {
                    fileToUpload = gPalFileToUpload.Filenames[fileCount];

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Uploading [{fileToUpload}]", this, GPALObjectType.Browser);

                    // while we can deal with the input dialog, why bother? this is cleaner
                    // if (true == BrowserSettings.UseHeadless || true == BrowserSettings.UseOttoMagic)    // headless mode or no prompt for filename, alternate methods to add file to input

                    // if the user wants to use hardware input, we can do that, otherwise direct is best - but hardware works for selenium headful mode
                    bool hardwareAsked = true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware || InteractionType.Hardware == selector.InteractionType;

                    // headless opens no file dialog for hardware to type into, so the file is set on the input directly
                    if (false == hardwareAsked || true == BrowserSettings.UseHeadless)
                    {
                        if (true == hardwareAsked)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Headless has no file dialog, so [{fileToUpload}] is set on the input directly", this, GPALObjectType.Browser);

                        if (true == string.IsNullOrEmpty(fileToUpload))
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, "For some reason we have an empty filename", gPalFileToUpload, GPALObjectType.Other);

                        // BOTH OF THESE do not check for completion, just sets download path and clicks
                        if (true == BrowserSettings.UsePuppeteer)
                        {
                            if (true == uploadAllFiles)
                            {
                                PuppeteerClient.LeftClickAndUpload(webElement.ElementHandle).UploadFrom(gPalFileToUpload).Execute();
                                break;
                            }
                            PuppeteerClient.LeftClickAndUpload(webElement.ElementHandle).UploadFrom(fileToUpload).Execute();
                        }
                        else if (true == BrowserSettings.UseOttoMagic)
                            MagicHelper.LeftClickAndUpload(webElement.Css, fileToUpload);
                        else   // selenium
                        {
                            // chromedriver adds a file per SendKeys; geckodriver throws on the second. Both accept all
                            // paths newline-separated in one SendKeys.
                            string filesToSend = true == sendAllAtOnce ? string.Join("\n", gPalFileToUpload.Filenames) : fileToUpload;

                            try
                            {
                                ((IWebElement)webElement.iWebElement).SendKeys(filesToSend);   // NOTE: special case in selenium sendkeys on input[type=files] adds a file
                            }
                            catch    // stale element
                            {
                                ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool _, out List<GPALElement> matchedElements);
                                webElement = 0 < matchedElements.Count ? matchedElements[0] : null;
                                webElement?.SendKeys(filesToSend.Replace("\\", "/"));
                            }

                            if (true == sendAllAtOnce)
                                break;
                        }

                        Thread.Sleep(250);
                    }
                    else    // hardware asked for, with a window to type into
                    {
                        ElementHelper.Click(this, selector, webElement, ClickType.LeftClick, ModifierKeys.NONE);    // click the input[type=file] button

                        windowOpened = WaitForCommonDialog(BrowserSettings.WaitForWindowTimeoutInSeconds);

                        // save as window didn't open in the allotted timeout, or we set to no prompt and a direct download
                        if (IntPtr.Zero != windowOpened)
                        {
                            int filesToSend = 1;

                            if (true == webElement.GetAttribute("multiple")?.ToLower().Equals("true"))
                                filesToSend = gPalFileToUpload.Count;

                            if (true == uploadAllFiles)
                                for (int idx = 0; idx < gPalFileToUpload.Count; idx++)
                                {
                                    fileToUpload = gPalFileToUpload.Filenames[idx];
                                    HardwareHelper.SendString($@"""{fileToUpload}"" ");
                                }
                            else
                                HardwareHelper.SendString($@"""{fileToUpload}"" ");

                            HardwareHelper.SendKey(GPAL.VK_RETURN);

                            // is the file dialog still open? send ENTER again
                            windowOpened = WaitForCommonDialog(1);

                            if (IntPtr.Zero != windowOpened)
                                HardwareHelper.SendKey(GPAL.VK_RETURN);

                            if (true == uploadAllFiles)
                                break;  // we send all the filenames, so long as they are not too long
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to handle Open File dialog [{fileToUpload}][{selector.Name}] not upoloaded.", BrowserSettings, GPALObjectType.Other);
                            return -1;
                        }
                    }

                    //if (false == BrowserSettings.UseHeadless && BrowserType.Edge == BrowserSettings.BrowserType)    // dismiss the 'downloads' popup which can steal keys, clicks or get in the way
                    //{
                    //    // KLUDGE: must be a hardware top to dismiss 'downloads' by clicking the browser chrome
                    //    bool tmpTop = GPAL.SeleniumTopsBrowser;
                    //    GPAL.WithSeleniumToppingBrowser(false);
                    //    BrowserHelper.TopBrowser(this);
                    //    GPAL.WithSeleniumToppingBrowser(tmpTop);
                    //}

                    // NOTE: CAVEAT: not sure how to use WithAllThatMatch, seems cumbersome?
                    if (int.MaxValue != CurrentUOW.WithAllThatMatch && fileCount == CurrentUOW.WithAllThatMatch && false == uploadAllFiles)
                        return -1;
                    else if (false == uploadAllFiles)
                        return 0;
                }

                return -1;
            }

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    ReadOnlyCollection<GPALElement> webelements = ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                    if (null != webelements)
                    {
                        foreach (GPALElement webElement in matchedElements)
                        {
                            // int.MaxValue tells us we have met our .WithAllThatMatch elements to process 
                            if (-1 == UploadWorkflow(webElement, selector, 1 == matchedElements.Count))
                                break;
                        }
                    }
                }
            }

            GetSetCurrentUrl();

            return this;
        }

        /// <summary>
        /// Right click the element(s) found for Selector(s) defined in the UOW, select 'Save as...', wait for the SAVE AS dialog, enter the filename and save the file.<br/>
        /// <b>The saved filename is then added to the end of the filename list.</b><br/>
        /// Useful if files are not overwritten.<br/>
        /// One right click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a right click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <param name="filenames">GPALFile the downloaded file is saved to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RightClickAndDownload(GPALFile filenames)
        {
            string fullFilePathToSave;
            HWND windowOpened = IntPtr.Zero;
            List<string> returnFilename = new List<string>();
            FileDownloaded = false;

            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    ReadOnlyCollection<GPALElement> webelements = ElementHelper.FindWebElements(this, CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                    if (null != webelements)
                    {
                        foreach (GPALElement webElement in matchedElements)
                        {
                            int rowCount = 0;

                            fullFilePathToSave = FileHelper.CleanUpFileDestination(filenames.Next, out string newFilename, BrowserSettings.DeleteFileBeforeDownload);
                            returnFilename.Add(fullFilePathToSave);  // return the filenames generated

                            // this will iterate over all selectors in the CurrentUOW and right click each and attempt to download from each
                            // CAVEAT: this is untested with multiple selector elements

                            // CAVEAT: KLUDGE: Selenium cannot interact with context menu, so switch to javascript click, then back, altho it's prolly pointless to restore it at this point
                            Enums.InteractionType interactionType = selector.InteractionType;
                            if (Enums.InteractionType.Selenium == selector.InteractionType && "GPALElement" != webElement.TagName)
                                selector.InteractionType = InteractionType.JavaScript;
                            else if ("GPALElement" != webElement.TagName && false == BrowserSettings.UseHeadless)
                                selector.InteractionType = InteractionType.Hardware;

                            if (false == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))    // if we are not headless, we will handle the dialog, otherwise set using selenium and click
                            {
                                // NOTE: CAVEAT: if DownloadToFile always works, why would we care about using the save as dialog?
                                bool noSaveAsDialog = false;
                                string currentUrl = null, currentUrl2 = null;

                                if (true == BrowserSettings.UseOttoMagic)
                                    currentUrl = MagicHelper.GetCurrentUrl();
                                else if (true == BrowserSettings.UsePuppeteer)
                                    currentUrl = PuppeteerClient.GetCurrentUrl().Execute();
                                else
                                    currentUrl = BrowserDriver.Url;

                                ElementHelper.Click(this, selector, webElement, ClickType.RightClick, ModifierKeys.NONE);

                                // MS EDGE and context menu is weird under driver control, it seems to need time to settle in because it resets to the top of the context menu
                                // so we sleep 5 seconds
                                // we also give it a home key to gobble up cause it's just unpredictable
                                Thread.Sleep(5000);

                                // arrow down menu, then enter to select entry
                                // 2 or 3 arrows down. if it goes too far, it copies the url to the clipboard, so if the save as... doesn't open, we will try with 3 arrow down
                                HardwareHelper.SendKey(GPAL.VK_HOME);   // home key press is first element
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);

                                if (BrowserType.FireFox == BrowserSettings.BrowserType || BrowserType.Edge == BrowserSettings.BrowserType)
                                    HardwareHelper.SendKey(GPAL.VK_DOWN);
                                Thread.Sleep(150);
                                HardwareHelper.SendKey(GPAL.VK_RETURN);

                                // CAVEAT: KLUDGE - see above, restore to selenium
                                selector.InteractionType = interactionType;

                                windowOpened = WaitForCommonDialog(BrowserSettings.WaitForWindowTimeoutInSeconds);

                                // Firefox titles: "Enter name of file to save to" for a link, "Save As" for the
                                // page. The dialog is found by class, so any language works; on an English system a
                                // "Save As" title means the wrong menu item was clicked, so warn.
                                if (IntPtr.Zero != windowOpened
                                    && BrowserType.FireFox == BrowserSettings.BrowserType
                                    && "Save As" == FormHelper.TitleOf(windowOpened))
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"GPAL thinks a misclick happened and the wrong item is saved. Please verify [{fullFilePathToSave}].", this, GPALObjectType.Browser);

                                if (IntPtr.Zero != windowOpened)
                                {
                                    HardwareHelper.SendString(fullFilePathToSave);
                                    HardwareHelper.SendKey(GPAL.VK_RETURN);

                                    // is the file save dialog still open? send ENTER again
                                    windowOpened = WaitForCommonDialog(5);

                                    if (IntPtr.Zero != windowOpened)
                                        HardwareHelper.SendKey(GPAL.VK_RETURN);

                                    // wait for file to download
                                    string actualDownloadedFilename2 = null;

                                    actualDownloadedFilename2 = FileHelper.WaitForDownloadToFinish(fullFilePathToSave, BrowserSettings.DownloadTimeoutInSec);

                                    FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename2);

                                    if (BrowserType.Edge == BrowserSettings.BrowserType)    // dismiss the 'downloads' popup which can steal keys, clicks or get in the way
                                    {
                                        // KLUDGE: must be a hardware top to dismiss 'downloads' by clicking the browser chrome
                                        BrowserHelper.TopBrowser(this, false, true);
                                    }
                                }
                                else
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to handle Save As... dialog [{fullFilePathToSave}][{selector.Name}]", BrowserSettings, GPALObjectType.Other);
                                    noSaveAsDialog = true;

                                    // but maybe it's a pdf opening in the browser itself, so check to see if that happened and if the url is a pdf file
                                    // of course it is possible the url won't contain .pdf and served up via a header? we might have to chack that

                                    if (true == BrowserSettings.UseOttoMagic)
                                        currentUrl2 = MagicHelper.GetCurrentUrl();
                                    else if (true == BrowserSettings.UsePuppeteer)
                                        currentUrl2 = PuppeteerClient.GetCurrentUrl().Execute();
                                    else
                                        currentUrl2 = BrowserDriver.Url;

                                    // but maybe it's a pdf opening in the browser itself, so check to see if that happened and if the url is a pdf file
                                    // of course it is possible the url won't contain .pdf and served up via a header? we might have to chack that
                                    if (false == BrowserSettings.OpenPDFExternally && true == fullFilePathToSave.EndsWith(".pdf"))
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Testing [{currentUrl2}] for PDF file", this, GPALObjectType.Browser);
                                        if (false == UrlHelper.AreEquivalent(currentUrl, currentUrl2) && true == BrowserHelper.TestUrlForPDF(this, currentUrl2))
                                        {
                                            FileDownloaded = BrowserHelper.DownloadPdfFile(currentUrl2, fullFilePathToSave, this);
                                            noSaveAsDialog = !(bool)FileDownloaded;
                                        }
                                    }
                                }

                                // we didn't get a save as dialog within BrowserSettings.WaitForWindowTimeoutInSeconds which defaults to 30 seconds
                                // so why? well, the website might have downloaded directly using the current browser default download directory
                                // so we will attempt to see if a file was downloaded matching the file extension of the GPALFile the user specified
                                if (true == noSaveAsDialog)
                                {
                                    string downloadDirectory = FileHelper.GetDefaultDownloadDirectory(this);
                                    string downloadUrl = FileHelper.GetDownloadUrl(webElement);
                                    string filename = Path.GetFileName(downloadUrl) ?? fullFilePathToSave;

                                    string wildcardFilename = "*" + Path.GetExtension(filename);
                                    string systemDownloadedTo = Path.Combine(downloadDirectory, wildcardFilename);
                                    string actualDownloadedFilename2 = null;

                                    actualDownloadedFilename2 = FileHelper.WaitForDownloadToFinish(systemDownloadedTo, BrowserSettings.DownloadTimeoutInSec);

                                    FileDownloaded = true != string.IsNullOrEmpty(actualDownloadedFilename2);

                                    if (true == FileDownloaded)
                                    {
                                        if (false == fullFilePathToSave.Equals(actualDownloadedFilename2))
                                        {
                                            FileHelper.EnsureFolderFor(fullFilePathToSave);
                                            File.Move(actualDownloadedFilename2, fullFilePathToSave);
                                            if (false == actualDownloadedFilename2.Equals(fullFilePathToSave) && true == File.Exists(actualDownloadedFilename2))
                                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloaded file [{actualDownloadedFilename2}] unable to move to [{fullFilePathToSave}]", null, GPALObjectType.None);
                                            else
                                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{actualDownloadedFilename2}] moved to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Replaces right click and Save As: download over the browser's connection, from the
                                // page first, then in a tab. FileDownloaded reflects the result.
                                string rightClickUrl = FileHelper.GetDownloadUrl(webElement);

                                if (true == string.IsNullOrWhiteSpace(rightClickUrl))
                                {
                                    FileDownloaded = false;
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{webElement?.TagName}] carries no url to download, so there is nothing to save", this, GPALObjectType.Browser);
                                }
                                else
                                {
                                    FileDownloaded = FileHelper.FetchOrDownloadInTab(rightClickUrl, fullFilePathToSave, this);

                                    if (false == FileDownloaded)
                                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{rightClickUrl}] did not download to [{fullFilePathToSave}]", this, GPALObjectType.Browser);
                                }
                            }

                            // The interaction was switched for the right click only. Put it back on every path, or the
                            // selector's next action on this or another browser runs with it.
                            selector.InteractionType = interactionType;
                            /* selenium navigate the context menu, untested
                            else
                            {
                                SetDownloadFilename(fullFilePath);

                                // Create an Actions object
                                Actions actions = new Actions(browserSettings.BrowserDriver);
                                actions.SendKeys(Keys.Home);

                                // Simulate pressing the down arrow key multiple times
                                actions.SendKeys(Keys.ArrowDown);
                                actions.SendKeys(Keys.ArrowDown);
                                actions.SendKeys(Keys.ArrowDown);

                                // If using Firefox, simulate pressing the down arrow key one more time to get to FF command
                                if (BrowserType.FireFox == BrowserSettings.BrowserType)
                                    actions.SendKeys(Keys.ArrowDown);

                                // Simulate pressing the return key
                                actions.SendKeys(Keys.Enter);

                                // Simulate typing the filename
                                if (BrowserType.FireFox == browserSettings.BrowserType || BrowserType.Edge == browserSettings.BrowserType)
                                    actions.SendKeys(Keys.ArrowDown);

                                // Simulate pressing the return key
                                actions.SendKeys(Keys.Enter).Build().Perform();
                            }
                            */
                            
                            // how many of the found elements are we downloading from?
                            if (int.MaxValue != CurrentUOW.WithAllThatMatch && ++rowCount == CurrentUOW.WithAllThatMatch)
                                break;
                        }
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to detect Save As open window.", this, GPALObjectType.Browser);
                    }
                }

                foreach (string filepath in returnFilename)
                {
                    filenames.ReturnFilenames.Add(filepath); // return the filenames we generated

                    // Log "saved" only if the file exists; otherwise warn.
                    if (true == File.Exists(filepath))
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File saved to [{filepath}]", this, GPALObjectType.Browser);
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Nothing was saved to [{filepath}]", this, GPALObjectType.Browser);
                }
            }

            return this;
        }
        /// <summary>
        /// Append data from the database to the end of any input Selectors defined in the UOW<br/>
        /// One token (one column in the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Insert the text from the database at the beginning of the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// FillIn data from the database overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Append the data from the input file to the end of any input Selectors defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (IGPALGrid<string> tokens in ((IGPALFileInternal)inputFile).TokenList)
                    BrowserHelper.FillInWithTokens(this, tokens, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// FillIn data from the file overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (IGPALGrid<string> tokens in ((IGPALFileInternal)CurrentUOW.InputFile).TokenList)
                    BrowserHelper.FillInWithTokens(this, tokens, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Insert the data from the file at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (IGPALGrid<string> tokens in ((IGPALFileInternal)inputFile).TokenList)
                    BrowserHelper.FillInWithTokens(this, tokens, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Append the data from the input grid to the end of any input Selectors defined in the UOW<br/>
        /// One token (one string from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, inputGrid, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// FillIn data from the grid overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, inputGrid, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Insert the data from the grid at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, inputGrid, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Append the given text to the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="text">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(string text)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.FillInFrom(this, text, WriteMode.Append);

            return this;
        }
        /// <summary>
        /// Overwrite all text in the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="text">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(string text)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.FillInFrom(this, text, WriteMode.Overwrite);

            return this;
        }
        /// <summary>
        /// Insert text at the beginning of the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// Example: If three inputs are defined and three 'InsertFrom' are defined, then the text is assigned to the same ordinal input element found via the Selector(s)
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the corresponding Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(string textToUse)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Focus the current element found via the Selector. Only the first element per Selector found will be focused.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, for text inputs, focus is a click in the control. Otherwise, hardware emulation hovers the mouse over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then focus will interate over {num} elements that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Focus(Selector selector)
        {
            if (null != selector)
                WithSelector(selector);
            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.Focus(this);
            return this;
        }
        /// <summary>
        /// Hides the element(s) found for <paramref name="selector"/> (or the current selector(s) if none is
        /// given) by setting their CSS display to none.
        /// </summary>
        /// <param name="selector">Optional selector to add to the current unit of work before hiding</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Hide(Selector selector = null)
        {
            if (null != selector)
                WithSelector(selector);

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.Hide(this);

            return this;
        }
        /// <summary>
        /// Name of the attribute named by the most recent <see cref="WithAttribute"/> call, pending its <see cref="SetAttribute(string)"/>.
        /// </summary>
        private string _pendingSetAttributeName;

        /// <summary>
        /// Names the DOM attribute to set on the current selector(s). Must be followed by
        /// <see cref="SetAttribute(string)"/> to supply the value and complete the action. See
        /// <see cref="IAllowSetAttributeValue.SetAttribute"/> for the distinction between this and SendString/FillIn*.
        /// </summary>
        /// <param name="attribute">Name of the attribute to set (e.g. "disabled", "value", "data-foo")</param>
        /// <returns>Fluent interface; must be followed by SetAttribute(value) to complete the action</returns>
        public IAllowSetAttributeValue WithAttribute(string attribute)
        {
            _pendingSetAttributeName = attribute;
            return this;
        }
        /// <summary>
        /// Assigns <paramref name="value"/> to the attribute named by the preceding <see cref="WithAttribute"/> call,
        /// completing the SetAttribute action.
        /// </summary>
        /// <param name="value">Value to assign to the attribute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SetAttribute(string value)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.SetAttribute(this, _pendingSetAttributeName, value);

            return this;
        }

        /// <summary>
        /// Sets the value of the element(s) matched by the preceding <see cref="WithSelector"/> call from the
        /// value of the element matched by <paramref name="selector"/>, i.e. <c>destElement.value =
        /// srcElement.value</c>. Uses OttoMagic, Puppeteer or JavaScript depending on the active engine, and
        /// fires "input"/"change" events on the destination so the page reacts to the new value.
        /// </summary>
        /// <param name="selector">Selector identifying the source element to read the value from</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SetValueFrom(Selector selector)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.SetValueFromElement(this, selector);

            return this;
        }

        /// <summary>
        /// Fluent alias for <see cref="MoveTo(Selector)"/>. Moving the mouse to an element is a real input-layer
        /// event on every engine, so the browser naturally updates CSS <c>:hover</c> state and fires
        /// mouseenter/mouseover/mouseleave - this is what reveals hover-triggered menus and tooltips. There is
        /// no separate "hover" operation; <c>Hover</c> exists only because it's the more discoverable name for
        /// this intent.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Hover(Selector selector = null)
        {
            return MoveTo(selector);
        }
        /// <summary>
        /// Moves the mouse to the current element found via the Selector. Only the first element per Selector found will be 'moved to'.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, the mouse is moved to hover over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then MoveTo will move the mouse over {num} elements that matches the Selector(s) in the current UOW.<br/>
        /// How and where on the page an element is moved to differs depending upon the technique: Hardware, OttoMagic, Puppeteer or Selenium all
        /// perform a real cursor move (triggering CSS :hover and mouseover/mouseenter/mouseleave). As a last resort, when no real cursor is
        /// available (e.g. plain Selenium in headless mode), a synthetic "mouseover" event is dispatched via JavaScript - this only fires JS
        /// mouseover listeners and does NOT trigger CSS :hover.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector MoveTo(Selector selector = null)
        {
            if (null != selector)
                WithSelector(selector);

            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.MoveTo(this);
            return this;
        }
        /// <summary>
        /// Start a workflow to invoke CallIf handlers (if no other action is being used in the workflow)<br/>
        /// A LeftClick, Hover, GetGrid, SaveTo. FillIn and other actions will all start the workflow.<br/>
        /// Sometimes your Unit of Work (UOW) consists of finding one repeating element, then chaining to a CallIfFound handler.
        /// StartWorkflow will kick off looking for your UOW selectors.<br/>
        /// <b>NOTE:</b> Only call this if you have not called another action. Any prior action will start the workflow and this will be ignored if used.<br/>
        /// But if you have not called an action, you can use .StartWorkflow to kick off the workflow which will invoke your CallIf handlers.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector StartWorkflow()
        {
            ReadOnlyCollection<GPALElement> tmpElems;

            if (true == CurrentUOW.ActionCalled)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Action has already been called. This workflow has already run. Continuing.", this, GPALObjectType.Browser);
                return this;
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Workflow starting, you should be careful if you follow this with actions. CallIf handlers will be invoked again.", this, GPALObjectType.Browser);

            if (true == WorkflowSetup(WaitTime.Never < CurrentUOW.WaitForInMs, true))
                for (int pages = 0; pages < CurrentUOW.PageCount; pages++)  // is start workflow is used, then we are using callif/callafter handlers with no main workflow
                {
                    foreach (Selector sel in CurrentUOW.WithSelectorList)
                        if (SelectorType.Selector == sel.SelectorType)
                            tmpElems = ElementHelper.FindWebElements(this, CurrentUOW, sel, out bool matchedAll, out List<GPALElement> matchedElems);

                    if (pages < CurrentUOW.PageCount - 1)
                    {
                        if (false == BrowserSettings.AttachedRestApi || null != BrowserSettings.Process)
                BrowserHelper.TopBrowser(this, false); // ensure the browser window is on top, can use selenium or hardware (mouse click) topping. firefox will only top with hardware
                        if (false == BrowserHelper.GotoNextPage(this))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No next page, breaking loop. Retrieved [{pages + 1}] out of [{CurrentUOW.PageCount}] pages.", this, GPALObjectType.Browser);
                            break;
                        }
                    }
                }

            return this;
        }
        public IAllowBrowserActionOrAnySelector GetElements(out List<GPALElement> elements)
        {
            elements = new List<GPALElement>();

            if (true == CurrentUOW.ActionCalled)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Action has already been called. Not retrieving elements.", this, GPALObjectType.Browser);
                return this;
            }

            if (true == WorkflowSetup(WaitTime.Never < CurrentUOW.WaitForInMs, true))
                for (int pages = 0; pages < CurrentUOW.PageCount; pages++)
                {
                    foreach (Selector sel in CurrentUOW.WithSelectorList)
                        if (SelectorType.Selector == sel.SelectorType)
                        {
                            ElementHelper.FindWebElements(this, CurrentUOW, sel, out bool matchedAll, out List<GPALElement> matchedElems);
                            if (sel.WebSelectorFoundResults != null)
                                elements.AddRange(sel.WebSelectorFoundResults);
                        }

                    if (pages < CurrentUOW.PageCount - 1)
                    {
                        BrowserHelper.TopBrowser(this, false);
                        if (false == BrowserHelper.GotoNextPage(this))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No next page, breaking loop. Retrieved [{pages + 1}] out of [{CurrentUOW.PageCount}] pages.", this, GPALObjectType.Browser);
                            break;
                        }
                    }
                }

            return this;
        }
        /// <summary>
        /// Retrieve the current UOW selectors data into a &lt;string&gt; of columns and rows to use with GPAL or however you want.<br/>
        /// using GenerallyPositive.Common;
        /// </summary>
        /// <example>
        /// <code>
        ///     ...
        ///     .WithSelector(descriptionColumn)
        ///     .WithSelector(priceColumn)
        ///     .WithSelector(linkColumn)
        ///     .WithSelector(imageColumn)
        ///     .WithAllThatMatch()
        ///     .WithNextPageButton(nextPageButton)
        ///     .WithPages(3)   // get three pages of results
        ///     .GetGrid(out retGrid);   // get grid for us
        ///     
        ///     myBrowser
        ///         .WithGridToSave(retGrid)
        ///         .WithHeader("Description")
        ///         .WithHeader("Price")
        ///         .WithHeader("Link")
        ///         .WithHeader("Image URL")
        ///         .SaveTo(@"c:\ebay.retrieved.columns.txt");   // save to file (.txt => tab-delimited)
        /// </code>
        /// </example>
        /// <param name="returnGrid">Output GPALGrid</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithHeaderOrFileActions GetGrid(ref IGPALGrid<string> returnGrid)
        {
            List<ReadOnlyCollection<GPALElement>> columns = new List<ReadOnlyCollection<GPALElement>>();
            List<ReadOnlyCollection<GPALElement>> tmpColumns = new List<ReadOnlyCollection<GPALElement>>();
            var selectorMeta = new List<(string Name, string AttributeName)>();
            List<GPALElement> matchedElems = new List<GPALElement>();
            dynamic foundElements = null;

            // GetGrid is an action, so the unit of work ends. GetGridCalled tells WithHeader that following headers
            // belong to this grid; SaveTo and WithHeader still read the grid from this unit of work.
            CurrentUOW.ActionCalled = true;
            CurrentUOW.GetGridCalled = true;
            CurrentUOW.RetGrid.Clear();

            // One selector that finds a <table> or <tr> rows is read as a table: a row per <tr>, a column per cell,
            // the <th> row as names. Anything else uses the column-per-selector code below.
            if (TryGetGridFromTable(ref returnGrid))
                return this;

            // Content-based row dedup, opt-in via .WithDedupeData(true) on the grid the caller passed in.
            // seenRows holds a key per row's extracted values. We seed it from the rows already in the caller's
            // grid so accumulation across GetGrid calls stays duplicate-free, not just within this one call.
            bool useDedupe = (returnGrid as GPALGrid<string>)?.UseDedupe ?? false;
            var seenRows = new HashSet<string>();
            if (true == useDedupe && null != returnGrid)
                foreach (List<string> existingRow in returnGrid)
                    seenRows.Add(RowDedupeKey(existingRow));
            bool capReached = false;

            for (int pages = 0; pages < CurrentUOW.PageCount && !capReached; pages++)
            {
                // With .InElement the selectors are relative, so do not wait on them page-wide; RowsFromContainers
                // waits for the containers.
                bool waitOnSelectors = null == CurrentUOW.ContextPath?
                    .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

                // check for persistent selectors on each new page
                if (!InAction(true == waitOnSelectors && WaitTime.Never < CurrentUOW.WaitForInMs))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "WaitFor did not find the elements for the grid. Not retrieving anything.", this, GPALObjectType.Browser);
                    return this;
                }

                tmpColumns.Clear();
                columns.Clear();
                CurrentUOW.MatchedRowIndexes.Clear();
                selectorMeta.Clear();

                BrowserHelper.CheckDocumentReady(this);

                // iterate over selectors
                // With .InElement each row is a container. RowsFromContainers fills the same columns, selectorMeta
                // and RowCount, so the rest is unchanged.
                if (true == RowsFromContainers(tmpColumns, selectorMeta, pages))
                {
                    columns = tmpColumns;
                }
                else
                foreach (Selector sel in CurrentUOW.WithSelectorList)
                {
                    // cache is not used, but maybe the user might wanna look at tham for debugging?
                    sel.WebSelectorFoundResults = null;
                    sel.WebSelectorMatchedResults = null;

                    selectorMeta.Add((sel.Name, sel.AttributeName));

                    // we have selectors for elements and data/datafunction literals
                    if (SelectorType.Selector == sel.selectorSettings.SelectorType)
                    {
                        bool matchedAll = false;

                        foundElements = ElementHelper.FindWebElements(this, CurrentUOW, sel, out matchedAll, out matchedElems);

                        // the only instance where matchedElems does not have what we want is if custom match doesn't return any...
                        // if nothing matches, then create a psuedo "NOT FOUND" element for the grid
                        if (matchedElems?.Count > 0)
                        {
                            foreach (GPALElement element in matchedElems)
                                element.AttributeName = sel.AttributeName;

                            tmpColumns.Add(new ReadOnlyCollection<GPALElement>(matchedElems));
                        }
                        //else if (foundElements?.Count > 0)    // silly check
                        //    tmpColumns.Add(new ReadOnlyCollection<GPALElement>(foundElements));
                        else
                        {
                            var placeholder = new List<GPALElement>();
                            int rows = CurrentUOW.RowCount > 0 ? CurrentUOW.RowCount : 0;   // set from previous iteration, how many found for that selector?
                            if (0 < rows)
                            {
                                for (int i = 0; i < rows; i++)
                                    placeholder.Add(new GPALElement(new Point(0, 0), new Size(1, 1), $"{GPAL.ErrorPlaceholder} : {sel.Name}", BrowserDriver, "GPALElement") { AttributeName = sel.AttributeName, Browser = this, Selector = sel });
                                tmpColumns.Add(new ReadOnlyCollection<GPALElement>(placeholder));
                            }
                        }

                        if (matchedElems?.Count > 0)
                        {
                            CurrentUOW.ColCount++;

                            // set rowcount (found elements) for this iteration
                            if (matchedAll)     // found = matched
                                CurrentUOW.RowCount = Math.Max(CurrentUOW.RowCount, matchedElems.Count);
                            else if (matchedElems?.Count > 0)   // matched match criteria
                            {
                                CurrentUOW.PartialMatch = true;
                                int tmpIdx = 0;
                                foreach (var elem in matchedElems)
                                    if ((tmpIdx = matchedElems.IndexOf(elem)) != -1 && !CurrentUOW.MatchedRowIndexes.ContainsKey(tmpIdx))
                                        CurrentUOW.MatchedRowIndexes.Add(tmpIdx, sel.AttributeName);
                                CurrentUOW.RowCount = CurrentUOW.MatchedRowIndexes.Count;
                            }
                            else    // we found no elements but need placeholders if the workflow specifies WithAllThatMatch (int.MaxValue means all, no end, limited by pages hopefully)
                                CurrentUOW.RowCount = int.MaxValue != CurrentUOW.WithAllThatMatch ? CurrentUOW.WithAllThatMatch : 0;
                        }
                    }
                    else if (SelectorType.Data == sel.selectorSettings.SelectorType)    // use literal data
                    {
                        CurrentUOW.ColCount++;
                        GPALElement tmpElement = GPAL.Element;
                        tmpElement.Text = sel.SelectorPath;
                        tmpColumns.Add(new ReadOnlyCollection<GPALElement>(new List<GPALElement>(1) { tmpElement }));
                    }
                    else if (SelectorType.DataFunc == sel.selectorSettings.SelectorType)    // use literal data provided by function
                    {
                        CurrentUOW.ColCount++;
                        GPALElement tmpElement = GPAL.Element;
                        tmpElement.Text = sel.selectorSettings.DataFunction();
                        tmpColumns.Add(new ReadOnlyCollection<GPALElement>(new List<GPALElement>(1) { tmpElement }));
                    }

                    // One log line per selector, including selectors that found nothing.
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Grid for [{sel.Name}] page [{pages + 1}] returned [{CurrentUOW.RowCount}] rows of [{tmpColumns.Count()}] columns", this, GPALObjectType.Browser);
                }

                if (CurrentUOW.PartialMatch)
                {
                    columns.Clear();
                    foreach (var colElems in tmpColumns)
                    {
                        var newCol = new List<GPALElement>();
                        // The matched indexes apply to every column, and columns differ in length (a literal column
                        // has one element). Skip indexes past the end; the padding loop fills short columns to
                        // targetRows.
                        foreach (var idx in CurrentUOW.MatchedRowIndexes.Keys)
                            if (idx < colElems.Count)
                                newCol.Add(colElems[idx]);
                        columns.Add(new ReadOnlyCollection<GPALElement>(newCol));
                    }
                }
                else if (0 == columns.Count)
                    columns = tmpColumns;

                // how many elements (rows of data) did we find on this page
                int pageRows = 0 < columns.Count ? columns.Max(c => c.Count) : 0;
                int targetRows = CurrentUOW.RowCount > 0 ? CurrentUOW.RowCount : pageRows;

                // apply WithAllThatMatch per-page cap (only when n fits within one page)
                // total-cap and dedup are handled row-by-row during population below
                if (int.MaxValue != CurrentUOW.WithAllThatMatch && CurrentUOW.WithAllThatMatch <= pageRows)
                    targetRows = Math.Min(targetRows, CurrentUOW.WithAllThatMatch);

                // how many colums did we get? should be the number of selectors
                // now create our columns to iterate, whatever had the most, that is what we will fill all rows to
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i].ToList();
                    while (col.Count < targetRows)  // one of the selectors has more rows than this one, so create a placeholder
                    {
                        // for scrollintoview, we need something...
                        var firstNonEmpty = columns.FirstOrDefault(c => c.Count > 0)?.FirstOrDefault();
                        var meta = selectorMeta[i];
                        col.Add(new GPALElement(new Point(0, 0), new Size(1, 1), $"{GPAL.ErrorPlaceholder} : {meta.Name}", BrowserDriver, "GPALElement") { AttributeName = meta.AttributeName, Browser = this, Css = firstNonEmpty?.Css, ElementBackendNodeId = (int)(firstNonEmpty?.ElementBackendNodeId) });
                    }
                    columns[i] = new ReadOnlyCollection<GPALElement>(col);
                }
                CurrentUOW.RowCount = targetRows;

                // Populate RetGrid row-major. Dedup (when opted in) is content-based: we key each row by its
                // extracted values, so a row is dropped if its data duplicates one already accumulated (seenRows
                // is seeded from the caller's grid) or one seen earlier this call. This composes with the
                // grid-accumulation contract and needs no per-element attribute hashing.
                // WithAllThatMatch total-cap stops accumulation once we have enough unique rows
                for (int r = 0; r < CurrentUOW.RowCount && !capReached; r++)
                {
                    var row = new List<string>();
                    for (int c = 0; c < columns.Count; c++)
                    {
                        var elem = columns[c][r];

                        // whatever item we are scraping, bring it into view
                        // for UX
                        if (true == BrowserSettings.ScrollIntoView)
                            if (false == BrowserSettings.UseHeadless && false == "GPALElement".Equals(elem.TagName))
                                if (true == UseOttoMagic)
                                {
                                    if (false == string.IsNullOrEmpty(elem.Css))
                                        MagicHelper.ScrollIntoView(elem.Css);
                                }
                                else if (true == UsePuppeteer)
                                    PuppeteerClient.ScrollIntoView(elem).Execute();
                                else
                                    ElementHelper.ScrollIntoView(this, elem);

                        if (!string.IsNullOrEmpty(elem.AttributeName))
                            row.Add(elem.GetAttribute(elem.AttributeName));
                        else if (null != elem.TagName)
                        {
                            switch (elem.TagName?.ToLower())
                            {
                                case "a": row.Add(elem.GetAttribute("href")); break;
                                case "img": row.Add(elem.GetAttribute("src")); break;
                                default: row.Add(elem.Text); break;
                            }
                        }
                        else // literal data
                            row.Add(elem.Text);
                    }

                    // DEDUPE: skip this row if its data matches one already in the grid or seen this call
                    if (true == useDedupe && false == seenRows.Add(RowDedupeKey(row)))
                        continue;

                    CurrentUOW.RetGrid.AddRow(row);
                    if (int.MaxValue != CurrentUOW.WithAllThatMatch && CurrentUOW.WithAllThatMatch > pageRows
                        && CurrentUOW.RetGrid.Count() >= CurrentUOW.WithAllThatMatch)
                        capReached = true;
                }

                if (!capReached && pages < CurrentUOW.PageCount - 1)
                {
                    BrowserHelper.TopBrowser(this, false);
                    if (!BrowserHelper.GotoNextPage(this))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"No next page, breaking loop. Retrieved [{pages + 1}] out of [{CurrentUOW.PageCount}] pages.", this, GPALObjectType.Browser);
                        break;
                    }
                }
            }

            // Hand back the results without taking ownership of a grid the caller passed in:
            //  - null in  -> the caller just wants a grid, so give them GPAL's internal one.
            //  - grid in   -> the caller owns it (and may have configured it, e.g. WithDedupeData); add our rows
            //                 into THEIR grid and keep their reference so repeated GetGrid calls can accumulate.
            if (null == returnGrid)
                returnGrid = (IGPALGrid<string>)CurrentUOW.RetGrid;
            else
                returnGrid.Add((IGPALGrid<string>)CurrentUOW.RetGrid);

            return this;
        }

        /// <summary>
        /// Builds a dedup key for a grid row from its cell values. Uses the unit-separator character (U+001F),
        /// which does not occur in scraped text, so two rows collide only when every column value is identical.
        /// </summary>
        private static string RowDedupeKey(List<string> row)
        {
            return string.Join("\u001F", row);
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        // Table-native GetGrid (engine-agnostic). Wedged into GetGrid above; does not touch the
        // column-per-selector zip. A single selector that resolves to a <table> (or its <tr> rows)
        // is walked structurally with the same ElementHelper.FindWebElements dispatch used
        // everywhere else, honoring colspan/rowspan, deriving headers from <th>, filling gaps with
        // GPAL.ErrorPlaceholder, and populating CurrentUOW.RetGrid so SaveTo*/pagination just work.
        // NOTE: OttoMagic/Puppeteer scope a child query by the parent element's Css, so the table
        // and its rows must carry a usable Css for those engines (Selenium scopes by IWebElement).
        // NOTE: deeply nested tables are a known limitation (descendant tr/td/th matches inner cells).
        // ─────────────────────────────────────────────────────────────────────────────────────
        private bool TryGetGridFromTable(ref IGPALGrid<string> returnGrid)
        {
            // Only a single element-selector request can be table-native. Multiple selectors are the
            // column-per-selector zip, and literal Data/DataFunc columns are never tables.
            if (1 != CurrentUOW.WithSelectorList.Count
                || SelectorType.Selector != CurrentUOW.WithSelectorList[0].selectorSettings.SelectorType)
                return false;

            Selector sel = CurrentUOW.WithSelectorList[0];

            // Probe once to learn the target type. If it is not a <table> or a set of <tr> rows,
            // this is not table-native; return false and let the untouched column-zip path run.
            bool matchedAll; List<GPALElement> matchedElems;
            ReadOnlyCollection<GPALElement> seeds = ElementHelper.FindWebElements(this, CurrentUOW, sel, out matchedAll, out matchedElems);
            string tag = (null != seeds && seeds.Count > 0) ? seeds[0].TagName?.ToLowerInvariant() : null;
            bool isTable = "table" == tag;
            bool isRows = "tr" == tag;
            if (!isTable && !isRows)
                return false;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: [{sel.Name}] resolved to <{tag}>; extracting the table structurally.", this, GPALObjectType.Browser);

            var seenRows = new HashSet<string>();
            List<string> headers = null;
            bool capReached = false;
            int cap = (int.MaxValue == CurrentUOW.WithAllThatMatch)
                        ? int.MaxValue : CurrentUOW.WithAllThatMatch;

            for (int page = 0; page < CurrentUOW.PageCount && !capReached; page++)
            {
                if (page > 0)
                {
                    seeds = ElementHelper.FindWebElements(this, CurrentUOW, sel, out matchedAll, out matchedElems);
                    if (null == seeds || 0 == seeds.Count)
                        break;
                }
                BrowserHelper.CheckDocumentReady(this);

                // Gather the body row elements for this page (and headers, once).
                var bodyRows = new List<GPALElement>();
                if (isTable)
                {
                    foreach (GPALElement table in seeds)
                    {
                        var headerRows = FindWithin(table, "thead tr");
                        var tableBody = FindWithin(table, "tbody tr");
                        if (0 == tableBody.Count && 0 == headerRows.Count)
                        {
                            // no thead/tbody: treat a leading all-<th> row as the header
                            var all = FindWithin(table, "tr");
                            if (all.Count > 0 && RowIsAllHeader(all[0]))
                            {
                                headerRows.Add(all[0]);
                                tableBody.AddRange(all.Skip(1));
                            }
                            else
                                tableBody.AddRange(all);
                        }
                        else if (0 == headerRows.Count && 0 < tableBody.Count && true == RowIsAllHeader(tableBody[0]))
                        {
                            // No thead and an all-<th> first row. The browser wraps loose rows in a tbody, so the
                            // header is the first tbody row.
                            headerRows.Add(tableBody[0]);
                            tableBody.RemoveAt(0);
                        }
                        if (null == headers && headerRows.Count > 0)
                        {
                            var hmatrix = BuildTableMatrix(headerRows);
                            if (hmatrix.Count > 0)
                                headers = hmatrix[0];   // first header row, span-aligned to the data
                        }
                        bodyRows.AddRange(tableBody);
                    }
                }
                else // the seeds themselves are the rows
                {
                    bodyRows.AddRange(seeds);
                }

                // Build a rectangular, span-aware matrix of cell text for these rows.
                List<List<string>> matrix = BuildTableMatrix(bodyRows);

                foreach (List<string> row in matrix)
                {
                    string key = string.Join("", row);
                    if (!seenRows.Add(key))
                        continue;   // duplicate row from a prior page/scroll pass
                    CurrentUOW.RetGrid.AddRow(row);
                    if (CurrentUOW.RetGrid.Count() >= cap)
                    {
                        capReached = true;
                        break;
                    }
                }

                if (!capReached && page < CurrentUOW.PageCount - 1)
                {
                    BrowserHelper.TopBrowser(this, false);
                    if (!BrowserHelper.GotoNextPage(this))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: no next page after page [{page + 1}].", this, GPALObjectType.Browser);
                        break;
                    }
                }
            }

            // Store the table's column names in ControlHeaderList, as the Application side does for controls.
            // WithHeader clears HeaderList, and ResolveHeaderColumns prefers these names over generated ones.
            if (null != headers && headers.Count > 0)
            {
                CurrentUOW.ControlHeaderList = headers;

                if (null == CurrentUOW.HeaderList || 0 == CurrentUOW.HeaderList.Count)
                    CurrentUOW.HeaderList = headers;
            }

            // Expose the table's column names on ColumnNames.
            ColumnNames = null == headers ? new List<string>() : new List<string>(headers);

            returnGrid = (IGPALGrid<string>)CurrentUOW.RetGrid;
            return true;
        }

        // Engine-agnostic scoped query: elements matching css *within* a parent element, using the
        // same ElementHelper.FindWebElements dispatch (Selenium/Puppeteer/OttoMagic) as the rest of GPAL.
        private List<GPALElement> FindWithin(GPALElement parent, string css)
        {
            Selector scoped = GPAL.Selector.WithCSS(css).ToGPALObject();
            bool matchedAll; List<GPALElement> matchedElems;
            ReadOnlyCollection<GPALElement> found = ElementHelper.FindWebElements(this, CurrentUOW, scoped, out matchedAll, out matchedElems, webElement: parent);

            // OttoMagic and Puppeteer search inside the parent by switching into it, and they find the next parent
            // inside wherever the last switch left them. Left in this row, the next row is not found, every row reads
            // the cells of this one, and the selectors after the grid search only this row. Go back to the
            // document, then into the unit of work's frames and shadow roots, as a search with a context does.
            if (true == UseOttoMagic || true == UsePuppeteer)
            {
                if (true == UseOttoMagic)
                    MagicHelper.SwitchToDefaultContent();
                else
                    PuppeteerClient.SwitchToDefaultContent().Execute();

                foreach (UnitOfWork.WebElementWithType contextType in CurrentUOW.ContextPath ?? new List<UnitOfWork.WebElementWithType>())
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToElement(contextType.gPalElement.Css);
                        else
                            PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                    }
                    else if (ElementType.ShadowRoot == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                        else
                            PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();
                    }
                }
            }

            return (null != found) ? found.ToList() : new List<GPALElement>();
        }

        // True when a <tr>'s cells are all <th> (a header row when there is no <thead>).
        private bool RowIsAllHeader(GPALElement tr)
        {
            var cells = FindWithin(tr, "td, th");
            if (0 == cells.Count)
                return false;
            foreach (GPALElement c in cells)
                if (!"th".Equals(c.TagName, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        // Turn <tr> elements into a rectangular grid of cell strings, honoring colspan and rowspan.
        // Spanned cells repeat their text across covered positions; structural gaps and short rows
        // are filled with GPAL.ErrorPlaceholder so every row has the same column count.
        private List<List<string>> BuildTableMatrix(List<GPALElement> rowElems)
        {
            var matrix = new List<List<string>>();
            // pending rowspans: column index -> (remaining future rows, text)
            var pending = new Dictionary<int, KeyValuePair<int, string>>();

            foreach (GPALElement tr in rowElems)
            {
                var cells = FindWithin(tr, "td, th");
                var row = new List<string>();
                int col = 0, ci = 0;

                while (ci < cells.Count || pending.Keys.Any(k => k >= col))
                {
                    KeyValuePair<int, string> carry;
                    if (pending.TryGetValue(col, out carry))
                    {
                        row.Add(carry.Value);
                        int left = carry.Key - 1;
                        if (left > 0) pending[col] = new KeyValuePair<int, string>(left, carry.Value);
                        else pending.Remove(col);
                        col++;
                        continue;
                    }

                    if (ci < cells.Count)
                    {
                        GPALElement cell = cells[ci++];
                        string text = CellText(cell);
                        int cspan = ParseSpan(cell, "colspan");
                        int rspan = ParseSpan(cell, "rowspan");
                        for (int c = 0; c < cspan; c++)
                        {
                            row.Add(text);
                            if (rspan > 1) pending[col] = new KeyValuePair<int, string>(rspan - 1, text);
                            col++;
                        }
                    }
                    else
                    {
                        // a rowspan carry sits further right but there is no cell here: keep alignment
                        row.Add(GPAL.ErrorPlaceholder);
                        col++;
                    }
                }

                matrix.Add(row);
            }

            // pad every row to the widest so the grid is rectangular
            int width = 0;
            foreach (var r in matrix) if (r.Count > width) width = r.Count;
            foreach (var r in matrix) while (r.Count < width) r.Add(GPAL.ErrorPlaceholder);

            return matrix;
        }

        private int ParseSpan(GPALElement cell, string attr)
        {
            int n;

            // An OttoMagic element carries no colspan or rowspan, so OttoMagic reads it from the page
            string v = true == UseOttoMagic ? MagicHelper.GetAttribute(cell.Css, attr) : cell.GetAttribute(attr);
            return (int.TryParse(v, out n) && n > 0) ? n : 1;
        }

        // Cell text read the same way the column-zip path reads cells, so both paths agree.
        private string CellText(GPALElement cell)
        {
            if (!string.IsNullOrEmpty(cell.AttributeName))
                return cell.GetAttribute(cell.AttributeName);
            switch (cell.TagName?.ToLowerInvariant())
            {
                case "a": return cell.GetAttribute("href");
                case "img": return cell.GetAttribute("src");
                default: return cell.Text ?? string.Empty;
            }
        }

        /// <summary>
        /// Limits <see cref="CaptureCalls(bool)"/> to calls whose url contains <paramref name="urlFragment"/>.<br/>
        /// Set it before capturing; earlier calls are not filtered.
        /// </summary>
        /// <param name="urlFragment">Any part of the url to keep, for example "/availability-api/"</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowCallTemplate WithCallFilter(string urlFragment)
        {
            BrowserSettings.CallFilter = urlFragment;

            return this;
        }

        /// <summary>
        /// Waits for a call matching WithCallFilter and returns it as a GPALRequest to send.<br/>
        /// Use it for values you cannot know ahead: a per-session token, a signed parameter, a header the page
        /// adds.<br/>
        /// The last match wins. Returns null if nothing matched in time.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .WithCallFilter("/availability-api/multiple-price-from")
        ///     .CaptureCallTemplate(out GPALRequest priceFrom);
        /// </code>
        /// </example>
        /// <param name="template">Receives the request the page made, or null when none arrived</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CaptureCallTemplate(out GPALRequest template)
        {
            template = null;

            // This engine cannot capture; say so now, not after 30 seconds.
            if (false == CanCaptureCalls)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No template: capturing calls is not available on [{BrowserSettings.AutomationEngine}]", this, GPALObjectType.Browser);

                return this;
            }

            CaptureCalls(true);

            GPALCall found = null;
            Stopwatch waited = Stopwatch.StartNew();

            while (null == found && CallTemplateWaitMilliseconds > waited.ElapsedMilliseconds)
            {
                CaptureCalls(out List<GPALCall> calls);

                // Last matching call. Calls captured before the filter was set are still listed, so filter again.
                foreach (GPALCall call in calls)
                    if (true == string.IsNullOrEmpty(BrowserSettings.CallFilter) || true == call.Url?.Contains(BrowserSettings.CallFilter))
                        found = call;

                if (null == found)
                    System.Threading.Thread.Sleep(250);
            }

            waited.Stop();

            if (null == found)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No call matching [{BrowserSettings.CallFilter}] in [{waited.ElapsedMilliseconds}]ms, so there is no template", this, GPALObjectType.Browser);

                return this;
            }

            template = (GPALRequest)GPAL.Request
                .WithPath(found.Url)
                .WithHttpMethod("POST".Equals(found.Method, StringComparison.OrdinalIgnoreCase) ? Enums.HttpVerb.Post : Enums.HttpVerb.Get)
                .WithBody(found.PostData)
                .WithName(BrowserSettings.CallFilter ?? found.Url);

            // Skip headers the browser sets itself; fetch refuses them.
            foreach (KeyValuePair<string, string> header in found.Headers)
                if (false == BrowserHelper.BrowserOwnedHeader(header.Key))
                    template.WithHeader($"{header.Key}: {header.Value}");

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Template taken from the page's own [{found.Method}] to [{found.Url}] after [{waited.ElapsedMilliseconds}]ms, with [{template.Headers.Count}] of its headers", this, GPALObjectType.Browser);

            return this;
        }

        // how long to wait for the page to make the call a template is wanted from
        private const int CallTemplateWaitMilliseconds = 30_000;

        // Puppeteer captures through CDP, OttoMagic through the extension's webRequest, Selenium through a recorder
        // script injected by the driver.
        private bool CanCaptureCalls => true == BrowserSettings.UsePuppeteer
                                     || true == BrowserSettings.UseOttoMagic
                                     || null != BrowserSettings.BrowserDriver;

        /// <summary>
        /// Records every request (method, url, resource type, body) until turned off, to find a site's API
        /// endpoints.<br/>
        /// Get them with <see cref="CaptureCalls(out List{GPALCall})"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .CaptureCalls()
        ///     .GoTo("https://www.example.com/rooms")
        ///     .SaveTo(out List&lt;GPALCall&gt; calls);
        /// </code>
        /// </example>
        /// <param name="capture">True to record, false to stop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CaptureCalls(bool capture = true)
        {
            // Selenium with no driver cannot capture. Warn once per browser; CaptureCallTemplate calls this in a
            // loop.
            if (false == CanCaptureCalls)
            {
                if (false == captureUnavailableReported)
                {
                    captureUnavailableReported = true;

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Capturing calls is not available on [{BrowserSettings.AutomationEngine}]. It needs a browser that reports its requests, and this one has none started", this, GPALObjectType.Browser);
                }

                return this;
            }

            if (true == BrowserSettings.UseOttoMagic)
            {
                // webRequest records outside the page, so recording survives navigation and includes requests no
                // page script made.
                BrowserSettings.CaptureCalls = capture;

                MagicHelper.CaptureCalls(capture, BrowserSettings.CallFilter);
            }
            else if (false == BrowserSettings.UsePuppeteer && null != BrowserSettings.BrowserDriver)
            {
                // Selenium has no CDP event stream, so inject a recorder script. On Chrome and Edge it is also
                // registered for every new document. It is always injected into the current page, so calling this
                // after a navigation works.
                BrowserSettings.CaptureCalls = capture;

                if (true == capture)
                {
                    if (BrowserType.Chrome == BrowserSettings.BrowserType || BrowserType.Edge == BrowserSettings.BrowserType)
                        BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, BrowserHelper.CallRecorderScript);

                    BrowserHelper.ExecuteJavaScript(this, BrowserHelper.CallRecorderScript);
                }
            }
            else
            {
                // Store the setting first. This is usually called before the first GoTo, and reading
                // PuppeteerCommunicator would create one early.
                BrowserSettings.CaptureCalls = capture;

                // A Puppeteer communicator exists only once the browser runs, so enable capture now. Check
                // HasPuppeteerCommunicator; reading PuppeteerCommunicator creates one.
                if (true == BrowserSettings.HasPuppeteerCommunicator)
                    BrowserSettings.PuppeteerCommunicator.CaptureCalls(capture).GetAwaiter().GetResult();
            }

            return this;
        }

        /// <summary>
        /// Records what the page asks for and hands back everything recorded so far, so an endpoint can be read
        /// off a site rather than guessed at.<br/>
        /// Say it before going anywhere and say it again whenever you want what has happened since. Each call
        /// hands back its own list, so a workflow reading one is never reading what the browser is still writing.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .GoTo("https://www.example.com/rooms")
        ///     .CaptureCalls(out List&lt;GPALCall&gt; calls);
        /// </code>
        /// </example>
        /// <param name="calls">Receives the calls, empty when nothing was captured</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CaptureCalls(out List<GPALCall> calls)
        {
            CaptureCalls(true);

            calls = true == BrowserSettings.UsePuppeteer
                ? true == BrowserSettings.HasPuppeteerCommunicator
                    ? new List<GPALCall>(BrowserSettings.PuppeteerCommunicator.CapturedCalls)
                    : new List<GPALCall>()
                : true == BrowserSettings.UseOttoMagic
                    ? ExtensionRecordedCalls()
                    : RecordedCalls();

            // DEBUG, not INFO: CaptureCallTemplate calls this in a loop. It logs the result at INFO.
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Handing back [{calls.Count}] captured calls.", this, GPALObjectType.Browser);

            return this;
        }

        /// <summary>
        /// Clears the calls recorded so far. Recording continues; lists already returned are unchanged.
        /// </summary>
        /// <example>
        /// <code>
        /// browser.ClearCapturedCalls();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearCapturedCalls()
        {
            if (true == BrowserSettings.UseOttoMagic)
                // Resend the capture settings with clear = true; the extension empties its list and keeps recording.
                MagicHelper.CaptureCalls(BrowserSettings.CaptureCalls, BrowserSettings.CallFilter, true);
            else if (true == BrowserSettings.UsePuppeteer)
            {
                if (true == BrowserSettings.HasPuppeteerCommunicator)
                    BrowserSettings.PuppeteerCommunicator.ClearCapturedCalls();
            }
            else if (null != BrowserSettings.BrowserDriver)
                // Selenium stores the calls in the page (window.__gpalCalls); empty that array.
                BrowserHelper.ExecuteJavaScript(this, "window.__gpalCalls = []; return true;");

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Forgot the calls captured so far.", this, GPALObjectType.Browser);

            return this;
        }

        // Convert webRequest resource types to CDP names (main_frame to Document, xmlhttprequest to XHR), so
        // ResourceType matches across engines.
        private static string CdpResourceType(string webRequestType)
        {
            string retVal = webRequestType;

            switch (webRequestType)
            {
                case "main_frame":
                case "sub_frame": retVal = "Document"; break;
                case "xmlhttprequest": retVal = "XHR"; break;
                case "script": retVal = "Script"; break;
                case "image":
                case "imageset": retVal = "Image"; break;
                case "stylesheet": retVal = "Stylesheet"; break;
                case "font": retVal = "Font"; break;
                case "media": retVal = "Media"; break;
                case "websocket": retVal = "WebSocket"; break;
                case "ping": retVal = "Ping"; break;
                case "csp_report": retVal = "CSPViolationReport"; break;

                // webRequest reports XHR and fetch both as xmlhttprequest; mapping both to XHR loses nothing an API
                // search needs. "other" maps to "Other".
                case "other": retVal = "Other"; break;
            }

            return retVal;
        }

        // Reads the extension's recorded calls as GPALCall, like CDP. webRequest sees every request, so unlike the
        // Selenium recorder this includes documents, scripts and images.
        private List<GPALCall> ExtensionRecordedCalls()
        {
            List<GPALCall> retVal = new List<GPALCall>();

            string json = MagicHelper.GetCapturedCalls();

            if (true == string.IsNullOrWhiteSpace(json)) return retVal;

            // GetCapturedCalls hands back the recorder's snapshot as the json text of one object
            json = json.Trim();

            Newtonsoft.Json.Linq.JObject snapshot;

            // Unparseable json logs an error with the start of the text, so it is not mistaken for a page with no
            // calls.
            try
            {
                snapshot = Newtonsoft.Json.Linq.JObject.Parse(json);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The captured calls could not be read. [{json.Length}] characters starting [{json.Substring(0, Math.Min(120, json.Length))}]", this, GPALObjectType.Browser, ex);

                return retVal;
            }

            // The recorder keeps the newest calls and counts dropped ones. Log the count, so too many calls can be
            // told from none.
            int dropped = (int?)snapshot["dropped"] ?? 0;

            if (0 < dropped)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The recorder dropped [{dropped}] older calls to stay within its limit", this, GPALObjectType.Browser);

            foreach (Newtonsoft.Json.Linq.JObject recorded in snapshot["calls"] ?? new Newtonsoft.Json.Linq.JArray())
            {
                GPALCall call = new GPALCall
                {
                    Url = recorded["url"]?.ToString(),
                    Method = recorded["method"]?.ToString(),
                    ResourceType = CdpResourceType(recorded["type"]?.ToString()),
                    PostData = recorded["postData"]?.ToString(),
                    Initiator = recorded["initiator"]?.ToString(),
                    Status = (int?)recorded["status"] ?? 0
                };

                if (recorded["headers"] is Newtonsoft.Json.Linq.JObject headers)
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> header in headers)
                        call.Headers[header.Key] = header.Value?.ToString();

                retVal.Add(call);
            }

            return retVal;
        }

        // Reads the Selenium recorder's calls from the page as GPALCall, like CDP. It sees only fetch and
        // XMLHttpRequest, not documents, scripts or images.
        private List<GPALCall> RecordedCalls()
        {
            List<GPALCall> retVal = new List<GPALCall>();

            if (null == BrowserSettings.BrowserDriver) return retVal;

            string json = BrowserHelper.ExecuteJavaScript(this, "return JSON.stringify(window.__gpalCalls || []);");

            if (true == string.IsNullOrWhiteSpace(json)) return retVal;

            foreach (Newtonsoft.Json.Linq.JObject recorded in Newtonsoft.Json.Linq.JArray.Parse(json))
            {
                GPALCall call = new GPALCall
                {
                    Url = recorded["url"]?.ToString(),
                    Method = recorded["method"]?.ToString(),
                    ResourceType = recorded["type"]?.ToString(),
                    PostData = recorded["postData"]?.ToString(),
                    Initiator = "script",
                    Status = (int?)recorded["status"] ?? 0
                };

                if (recorded["headers"] is Newtonsoft.Json.Linq.JObject headers)
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> header in headers)
                        call.Headers[header.Key] = header.Value?.ToString();

                retVal.Add(call);
            }

            return retVal;
        }

        /// <summary>
        /// Sends an API request from inside the page, with the browser's cookies, TLS fingerprint, header order and
        /// anti-bot clearance. The body is kept for .SaveTo.<br/>
        /// .WithPages sends one request per page, the page number replacing .WithPageToken. .WithTokensFrom runs all
        /// pages once per row, filling numbered tokens from the row's columns. A CallAfterFetch handler is called
        /// with each row's pages.
        /// </summary>
        /// <example>
        /// <code>
        /// string json = null;
        ///
        /// browser
        ///     .GoTo("https://www.harrods.com/")
        ///     .WithTokensFrom(searchTerms)
        ///     .WithPages(20)
        ///     .Fetch(productSearch)
        ///     .SaveTo(ref json);
        /// </code>
        /// </example>
        /// <param name="request">The request to issue, built with GPAL.Request</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithHeaderOrFileActions Fetch(GPALRequest request)
        {
            CurrentUOW.FetchResults.Clear();

            if (null == request)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No request given to fetch.", this, GPALObjectType.Browser);
            else
                BrowserHelper.FetchWithTokens(this, request);

            InAction(false);
            return this;
        }
        /// <summary>
        /// Hand back what .Fetch retrieved, as a JSON array holding one response body per page requested, so a
        /// single page and twenty pages come out in the same shape.
        /// </summary>
        /// <param name="data">Receives the JSON array of response bodies</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveTo(ref string data)
        {
            List<string> values = new List<string>();

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Handing back [{CurrentUOW.FetchResults.Count}] fetch responses.", this, GPALObjectType.Browser);

            // Always build a valid JSON array: a JSON body as is, anything else (an html block page, a body with a
            // leading http warning) as a JSON string.
            foreach (string body in CurrentUOW.FetchResults)
                values.Add(true == StringFormatDetector.IsJson(body.Trim()) ? body : Newtonsoft.Json.JsonConvert.SerializeObject(body));

            data = "[" + string.Join(",", values) + "]";
            return this;
        }
        /// <summary>
        /// Save the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension, e.g. .csv/.tsv/.txt/.xlsx/.json, or its explicit file settings).<br/>
        /// The single save syntax; the output format and delimiter come from the GPALFile.
        /// </summary>
        public IAllowBrowserActionOrAnySelector SaveTo(GPALFile file)
        {
            //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of page data to [{file?.Filename}].", this, GPALObjectType.Browser);

            var converter = GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file));
            // Naming headers via WithHeader is clear intent to write them; declare it so the output emits a
            // header row unless the output file explicitly turns it off (WithFirstLineIsColumnNames(false)).
            if (null != CurrentUOW.HeaderList && 0 < CurrentUOW.HeaderList.Count)
                converter.WithFirstLineHasColumnNames(true);
            converter.SaveTo(file);
            return this;
        }

        /// <summary>
        /// Append the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension or explicit file settings). Headers are not repeated on append.
        /// </summary>
        public IAllowBrowserActionOrAnySelector AppendTo(GPALFile file)
        {
            //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Appending [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of page data to [{file?.Filename}].", this, GPALObjectType.Browser);

            GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file)).AppendTo(file);
            return this;
        }

        // Resolve the output column names for a grid save, handed to the Converter as column metadata
        // (WithColumnNames), NOT injected as a data row. Precedence, highest first, with a short higher list
        // topped up from the next level down:
        //   1. CurrentUOW.HeaderList  - the workflow's .WithHeader columns (override)
        //   2. the output GPALFile's ColumnList - the file's own columns (wildcard/.Next replication of the
        //      first entry happens at expansion time, so entry 0 is representative here)
        //   3. selector names - the ultimate global, always defined
        //   4. the control's own column names, e.g. a UIA TablePattern's headers
        //   5. generated column1..columnN - so the list is never narrower than the widest row, because a
        //      column with no name must never mean a column with no data
        // The result is then made unique: the writer keys cells by column name, so a repeated name would
        // collapse those columns into one holding the last value.
        // Whether the names are written is governed by the output file's FirstLineIsColumnNames.
        private List<string> ResolveHeaderColumns(GPALFile file)
        {
            List<string> selectorNames = CurrentUOW.WithSelectorList?.Where(s => null != s.Name).Select(s => s.Name).ToList()
                ?? new List<string>();

            List<string> fileColumns = new List<string>();
            IGPALGrid<string> columnList = ((IGPALFileInternal)file).FileSettings.ColumnList;
            if (null != columnList && 0 < columnList.Count() && null != columnList[0])
                fileColumns = new List<string>(columnList[0]);

            List<string> resolved = (null != CurrentUOW.HeaderList && 0 < CurrentUOW.HeaderList.Count)
                ? new List<string>(CurrentUOW.HeaderList)
                : new List<string>();
            for (int i = resolved.Count; i < fileColumns.Count; i++)
                resolved.Add(fileColumns[i]);
            for (int i = resolved.Count; i < selectorNames.Count; i++)
                resolved.Add(selectorNames[i]);

            // Next, the control's or table's own names from ControlHeaderList, kept apart because WithHeader clears
            // HeaderList.
            List<string> controlHeaders = CurrentUOW.ControlHeaderList ?? new List<string>();

            for (int i = resolved.Count; i < controlHeaders.Count; i++)
                resolved.Add(controlHeaders[i]);

            // The converter writes only named columns, so add column1..columnN names to cover the widest row. No
            // data is dropped.
            int widest = 0;

            if (null != CurrentUOW.RetGrid)
                foreach (List<string> row in CurrentUOW.RetGrid)
                    if (null != row && widest < row.Count)
                        widest = row.Count;

            for (int i = resolved.Count; i < widest; i++)
                resolved.Add($"column{i + 1}");

            return UniqueColumns(resolved);
        }

        /// <summary>
        /// Makes every column name non-blank and unique; the writer keys cells by name, so duplicates merge.
        /// </summary>
        /// <remarks>
        /// A blank name becomes columnN and a repeat gets a number, so Size, Size becomes Size, Size2.
        /// </remarks>
        /// <param name="columns">The names as resolved, which may hold blanks and repeats</param>
        /// <returns>The same names, each present and each used once</returns>
        private static List<string> UniqueColumns(List<string> columns)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            for (int i = 0; i < columns.Count; i++)
            {
                string name = true == string.IsNullOrWhiteSpace(columns[i]) ? $"column{i + 1}" : columns[i].Trim();
                string candidate = name;
                int repeat = 1;

                while (false == used.Add(candidate))
                {
                    repeat++;
                    candidate = $"{name}{repeat}";
                }

                result.Add(candidate);
            }

            return result;
        }
        /// <summary>
        /// Either wait the amount of time specified or wait UP TO the amount of time specified waiting for elements to be found for the Selector(s) in the current UOW.<br/>
        /// If an action has already been called, then this is interpretted as a sleep for the specified miliseconds.<br/>
        /// If only selectors have been defined in the current UOW, then this is a delay waiting for the selectors to be present before continuing with the workflow.<br/>
        /// Waiting for elements does not guarantee they will be present when the workflow continues. If they are not present, the CallIfNotFound handler will be invoked so you can deal with that.<br/>
        /// <b>NOTE:</b> WaitFor is not considered an action, if defines a time to wait for elements to be present before trying to perform actions.
        /// </summary>
        /// <param name="waitForTimeInMs">Time to wait in milliseconds; use a predefined WaitTime value or any int (ms)</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitFor WaitFor(WaitTime waitForTimeInMs)
        {
            // we are done waiting on any selectors since we performed an action
            // so, we just wait/sleep
            if (true == CurrentUOW.ActionCalled)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Sleeping for [{waitForTimeInMs}] ms", this, GPALObjectType.Browser);
                Thread.Sleep(waitForTimeInMs);
            }
            else
                CurrentUOW.WaitForInMs = waitForTimeInMs;

            return this;
        }
        /// <summary>
        /// Wait for the specified elements(s) defined by withForSelector to be present and defined on-page
        /// </summary>
        /// <param name="waitForSelector"></param>
        /// <returns></returns>
        public IAllowAfterWaitFor WaitFor(Selector waitForSelector)
        {
            bool weSetWaitFor = false;
            WaitTime safe = WaitTime.Never;

            // never (can't wait on a selector 'never') so we will set a time and restore
            if (WaitTime.Never == CurrentUOW.WaitForInMs)
            {
                safe = CurrentUOW.WaitForInMs;
                CurrentUOW.WaitForInMs = WaitTime.Immediate;
                weSetWaitFor = true;
            }

            WithSelector(waitForSelector);  // this will create a new UOW with waittimeinms = never (or GPAL.WaitFor global setting)
            WorkflowSetup(true, true);  // this will set CurrentUOW.ActionCalled = true, so this is it's own action

            if (true == weSetWaitFor)
                CurrentUOW.WaitForInMs = safe;

            return this;
        }
        /// <summary>
        /// Waitfor an element to eneter the specified state.
        /// This is an action.
        /// </summary>
        /// <param name="elementState"></param>
        /// <returns></returns>
        public IAllowAfterWaitFor WaitFor(ElementState elementState)
        {
            if (ElementState.NotSet != elementState)
            {
                CurrentUOW.WaitForElementState = elementState;

                if (true == CurrentUOW.ActionCalled)    // do this now, we are running the workflow
                    ElementHelper.WaitFor(this, CurrentUOW.WaitForInMs, out bool matchedAll);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "ElementState NOTSET is not a valid wait condition. Please select a valid ElementState.", null, GPALObjectType.None);

            return this;
        }
        /// <summary>
        /// Wait for a Windows window with title to appear on the desktop.<br/>
        /// Used in conjunction with .WithWaitForWindowTimeout to set the wait duration in seconds.<br/>
        /// </summary>
        /// <param name="waitForTitle">Window title to wait for</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitForWindow(string waitForTitle)
        {
            FormHelper.WaitForWindow(waitForTitle, BrowserSettings.WaitForWindowTimeoutInSeconds);
            return this;
        }
        /// <summary>
        /// Wait for a Windows window with title that matches the regex pattern to appear on the desktop.<br/>
        /// Used in conjunction with .WithWaitForWindowTimeout to set the wait duration in seconds.<br/>
        /// </summary>
        /// <param name="waitForTitleRegex">Window title regex to wait for</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitForWindowRegex([Regex] string waitForTitleRegex)
        {
            FormHelper.WaitForWindowRegex(waitForTitleRegex, BrowserSettings.WaitForWindowTimeoutInSeconds);
            return this;
        }
        /// <summary>
        /// Sets the page orientation (Portrait/Landscape) used by <see cref="PrintToPDF"/>.
        /// </summary>
        /// <param name="pageOrientation">Desired page orientation</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithPageOrientation(PageOrientation pageOrientation)
        {
            BrowserSettings.PageOrientation = pageOrientation;
            return this;
        }
        // GPAL.Browser.GoTo("ebay.com").WithSelector(selectorToPrint).PrintToPDF("filename.pdf"); // print element and contents
        // GPAL.Browser.GoTo("ebay.com").WithSelector(first).WithSelector(second).PrintToPDF("filename.pdf"); // one pdf each
        // GPAL.Browser.GoTo("ebay.com").PrintToPDF("filename.pdf"); // print whole page
        /// <summary>
        /// Prints each selector in the unit of work to its own PDF, or the whole page with no selector.<br/>
        /// An action: honors .WaitFor and ends the unit of work.<br/>
        /// File names come from <see cref="GPALFile.Next"/>: your names in order, then names built from the last.
        /// Written files are in ReturnFilenames.
        /// </summary>
        /// <param name="gpalFile">GPALFile to save the PDFs to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector PrintToPDF(GPALFile gpalFile)
        {
            List<string> selectorPaths = new List<string>();
            bool named = false;

            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                    if (SelectorType.Selector == selector.SelectorType   // we don't look up data literals
                        && 0 < (selector.SelectorPaths?.Count ?? 0)
                        && false == string.IsNullOrEmpty(selector.SelectorPaths[0].SelectorPath))
                    {
                        named = true;

                        // Printing needs a css path. For an image selector, use the css of the element under the
                        // image; for XPath, the css of the matched element.
                        string css = SelectorPathType.Image == selector.SelectorPaths[0].SelectorPathType
                            ? ElementHelper.CssUnderImage(this, selector)
                            : SelectorPathType.Xpath == selector.SelectorPaths[0].SelectorPathType
                                ? ElementHelper.CssForXPath(this, selector)
                                : selector.SelectorPaths[0].SelectorPath;

                        if (null != css)
                            selectorPaths.Add(css);
                    }

                // No selector: print the whole page. A selector whose element was not found prints nothing.
                if (false == named)
                    selectorPaths.Add("html");

                foreach (string selectorPath in selectorPaths)
                    PrintOneToPDF(selectorPath, gpalFile.Next);
            }

            // No GetSetCurrentUrl: a separate browser renders the pdf, so this one stays put.

            return this;
        }
        /// <summary>
        /// Prints one element, named by its css path, to one PDF file.
        /// </summary>
        /// <param name="selectorPath">Css path of the element to print, "html" for the whole page</param>
        /// <param name="outputFile">The file to write, already named by .Next</param>
        private void PrintOneToPDF(string selectorPath, GPALFile outputFile)
        {
            string fullHtml = null;

            if (true == BrowserSettings.UseOttoMagic)
                fullHtml = MagicHelper.GetContentAndCss(selectorPath);
            else
            {
                // ExecuteJavaScript, not ExecuteJavaScriptObj. These scripts use a top level return, which Puppeteer
                // rejects; ExecuteJavaScript retries inside a function, while ExecuteJavaScriptObj returns null.
                string elementContent = BrowserHelper.ExecuteJavaScript(BrowserSettings.Browser,
                    $@"var div = document.querySelector('{selectorPath}'); return div.outerHTML;");

                // Inject the div content & CSS used
                string cssContent = BrowserHelper.ExecuteJavaScript(BrowserSettings.Browser, $@"
                function getAllCss() {{
                        var css = '';
                        var stylesheets = document.styleSheets;
                        for (var i = 0; i < stylesheets.length; i++) {{
                            try {{
                                var rules = stylesheets[i].cssRules;
                                for (var j = 0; j < rules.length; j++) {{
                                    css += rules[j].cssText + ' ';
                                }}
                            }} catch (e) {{ /* Ignore cross-origin stylesheet errors */ }}
                        }}
                        return css;
                    }}

                    function getElementCss(element) {{
                        var css = '';
                        var style = window.getComputedStyle(element);
                        for (var i = 0; i < style.length; i++) {{
                            css += style[i] + ': ' + style.getPropertyValue(style[i]) + '; ';
                        }}
                        return css;
                    }}

                    var allCss = getAllCss();
                    var div = document.querySelector('{selectorPath}');
                    var elementCss = getElementCss(div);

                    return '<style>' + allCss + ' ' + elementCss + '</style>';
                ");
                fullHtml = $@"<html><head>{cssContent}</head><body>{elementContent}</body></html>";
            }

            // The page cannot read cross-domain (CDN) stylesheet rules, so add a <link> for each stylesheet for the
            // printing browser to load, and a <base> of the page url for relative urls. On OttoMagic this needs Allow
            // user scripts; on failure nothing is added. One expression, since a top level return is a syntax error
            // on OttoMagic and Puppeteer; only Selenium gets "return ".
            string linksScript = @"(function () {
                var head = '<base href=""' + location.href.replace(/""/g, '&quot;') + '"">';
                var links = document.querySelectorAll('link[rel~=""stylesheet""][href]');
                for (var i = 0; i < links.length; i++)
                    head += '<link rel=""stylesheet"" href=""' + links[i].href.replace(/""/g, '&quot;') + '"">';
                return head;
            })()";
            string pageLinks = BrowserHelper.ExecuteJavaScript(this, true == UseSelenium ? "return " + linksScript : linksScript);

            if (true == pageLinks?.StartsWith("<base"))
            {
                int head = fullHtml?.IndexOf("<head>", StringComparison.OrdinalIgnoreCase) ?? -1;

                fullHtml = 0 <= head
                    ? fullHtml.Insert(head + "<head>".Length, pageLinks)
                    : pageLinks + fullHtml;
            }

#if DONOTUSECDP
        #region NEW get the browser to print to pdf without CDP
            // Step 1: the page settings go in the html. chrome's command line has --print-to-pdf and
            // --no-pdf-header-footer and nothing for orientation, margins or backgrounds, so @page and
            // print-color-adjust carry them. a style before <html> is moved into the head by the parser
            string pageCss = "<style>@page { size: "
                + (PageOrientation.Landscape == BrowserSettings.PageOrientation ? "landscape" : "portrait")
                + "; margin: 0; } html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }</style>";
            string outputHtmlPath = $"{outputFile.Filename}.html";

            // Step 2: Save the HTML to a file
            FileHelper.EnsureFolderFor(outputHtmlPath);
            File.WriteAllText(outputHtmlPath, pageCss + fullHtml);

            // Step 3: a headless chromium of its own prints the file and exits. it gets a new temp profile because
            // a launch on the workflow's profile is handed to the browser already running there as a new tab,
            // which never prints. firefox has no print to pdf on its command line, so a firefox workflow prints
            // with chrome when it is installed, otherwise edge
            string printerPath = null;
            if (BrowserType.Chrome == BrowserSettings.BrowserType)
                printerPath = Puppeteer.GetChromePath();
            else if (BrowserType.Edge == BrowserSettings.BrowserType)
                printerPath = Puppeteer.GetEdgePath();
            else
            {
                printerPath = Puppeteer.GetChromePath();
                if (string.IsNullOrEmpty(printerPath) || false == File.Exists(printerPath))
                    printerPath = Puppeteer.GetEdgePath();
            }

            if (string.IsNullOrEmpty(printerPath) || false == File.Exists(printerPath))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"PrintToPDF needs Chrome or Edge installed, neither was found to print [{outputFile.Filename}]",
                    BrowserSettings.Browser, GPALObjectType.Browser);
            }
            else
            {
                string printerProfile = ChromeProfileManager.NewTempProfilePath();
                string fileUrl = $"file:///{Path.GetFullPath(outputHtmlPath).Replace("\\", "/")}";
                string printerArgs = "--headless --disable-gpu --no-first-run --no-default-browser-check"
                    + $" --user-data-dir=\"{printerProfile}\""
                    + " --no-pdf-header-footer"
                    + $" --print-to-pdf=\"{Path.GetFullPath(outputFile.Filename)}\""
                    + $" \"{fileUrl}\"";

                System.Diagnostics.Process pdfPrinter = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(printerPath, printerArgs)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                // Wait for natural exit instead of blind sleep + kill
                bool exitedCleanly = pdfPrinter.WaitForExit(30000);   // generous timeout, e.g. 30s

                if (!exitedCleanly)
                {
                    try
                    {
                        if (!pdfPrinter.HasExited)
                            pdfPrinter.Kill();
                    }
                    catch { }

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        "PrintToPDF process did not exit cleanly (timeout)",
                        BrowserSettings.Browser, GPALObjectType.Browser);
                }
                else if (pdfPrinter.ExitCode != 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"PrintToPDF exited with code [{pdfPrinter.ExitCode}]",
                        BrowserSettings.Browser, GPALObjectType.Browser);
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Printed [{selectorPath}] to [{outputFile.Filename}]", BrowserSettings.Browser, GPALObjectType.Browser);

                ChromeProfileManager.RemoveTempUserProfile(printerProfile);
            }

            // Step 4: Clean up temporary files
            try
            {
                File.Delete(outputHtmlPath);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log warning if file deletion fails (e.g., file in use)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to delete temporary files", BrowserSettings.Browser, GPALObjectType.Browser, ex);
            }
        #endregion NEW get the browser to print to pdf without CDP
#else
    #region OLD use CDP to page.PrintToPDF a window
    BrowserHelper.ExecuteJavaScript(@"
        var newWindow = window.open('', '_blank');
        newWindow.document.open();
        newWindow.document.write('<html><head>' + arguments[0] + '</head><body>' + arguments[1] + '</body></html>');
        newWindow.document.close();
        newWindow.focus();
    ", browserSettings.Browser, new { cssContent, elementContent });

    // Wait for the new window to load
    System.Threading.Thread.Sleep(2000);

    // Switch to the new window
    var windows = browserSettings.BrowserDriver.WindowHandles;
    browserSettings.BrowserDriver.SwitchTo().Window(windows[windows.Count - 1]);

    // Define print settings
    var printSettings = new Dictionary<string, object>
    {
        { "printBackground", true },
        { "landscape", false }
    };

    // Execute the print command
    dynamic result = null;

    if (BrowserType.Chrome == browserSettings.BrowserType)
        result = BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.printToPDF", printSettings);
    else if (BrowserType.Edge == browserSettings.BrowserType)
        result = BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.printToPDF", printSettings);
    else
        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] does not support Page.printToPDF", browserSettings.Browser, GPALObjectType.Browser);

    var resultDict = result as IDictionary<string, object>;

    if (resultDict != null && resultDict.ContainsKey("data"))
    {
        // Decode the PDF data
        var pdfData = Convert.FromBase64String(resultDict["data"].ToString());
        // Save the PDF to a file
        FileHelper.EnsureFolderFor(outputFile.Filename);
        System.IO.File.WriteAllBytes(outputFile.Filename, pdfData);
    }

    browserSettings.BrowserDriver.Close();
    windows = browserSettings.BrowserDriver.WindowHandles;
    browserSettings.BrowserDriver.SwitchTo().Window(windows[windows.Count - 1]);
    #endregion OLD use CDP to page.PrintToPDF a window
#endif
        }
        /// <summary>
        /// Type in the literal string provided.<br/>
        /// <b>NOTE:</b> If you press SHIFT then SendString a mixed-case string, SHIFT will be released.<br/>
        /// To send ALL CAPS, press SHIFT then send an all lowercase string.
        /// </summary>
        /// <param name="textToSend">Text to type</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SendString(string textToSend)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.SendString(this, textToSend);
            }

            return this;
        }
        /// <summary>
        /// Press the specified modifer keys (can be ORd)<br/>
        /// <b>NOTE:</b> If you SendString a mixed case string, the SHIFT key will be released if it is pressed.
        /// </summary>
        /// <example>
        /// <code>
        /// // send CTRL-HOME
        ///     ...
        ///     myBrowser
        ///         .PressModifierKey(ModifierKeys.Control)
        ///         .SendKey(GPAL.VK_HOME);
        ///         .WaitFor(150)
        ///         .ReleaseModifierKey(ModifierKeys.Control)
        /// </code>
        /// </example>
        /// <param name="modifierKeys">Enumeration ModifierKeys, can be ORd togther</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector PressModifierKey(ModifierKeys modifierKeys)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Pressed [{GetModifierKeys(modifierKeys)}]", this, GPALObjectType.Browser);

            if (ModifierKeys.NONE != modifierKeys)
                if (false == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))
                {
                    HardwareHelper.PressModifierKey(modifierKeys);
                }
                else if (true == UseOttoMagic)
                {
                    MagicHelper.PressModifierKey(modifierKeys);
                }
                else if (true == UsePuppeteer || (true == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware)))
                {
                    PuppeteerClient.PressModifierKey(modifierKeys);
                }
                else
                {
                    Actions actions = new Actions(BrowserDriver);
                    bool anyKeySet = false;

                    if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    {
                        actions.KeyDown(Keys.Alt);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Control))
                    {
                        actions.KeyDown(Keys.Control);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    {
                        actions.KeyDown(Keys.Shift);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    {
                        actions.KeyDown(Keys.Meta);
                        anyKeySet = true;
                    }

                    ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                    if (ModifierKeys.NONE != unsupported)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"PressModifierKey: [{GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", this, GPALObjectType.Browser);

                    if (true == anyKeySet)
                        actions.Perform();
                }

            return this;
        }
        /// <summary>
        /// Release the specified modifer keys (can be ORd)<br/>
        /// <b>NOTE:</b> If you SendString a mixed case string, the SHIFT key will be released if it is pressed.
        /// </summary>
        /// <example>
        /// <code>
        /// // send CTRL-HOME
        ///     ...
        ///     myBrowser
        ///         .PressModifierKey(ModifierKeys.Control)
        ///         .SendKey(GPAL.VK_HOME);
        ///         .WaitFor(150)
        ///         .ReleaseModifierKey(ModifierKeys.Control)
        /// </code>
        /// </example>
        /// <param name="modifierKeys">Enumeration ModifierKeys, can be ORd togther</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Released [{GetModifierKeys(modifierKeys)}]", this, GPALObjectType.Browser);

            if (ModifierKeys.NONE != modifierKeys)
                if (false == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))
                {
                    HardwareHelper.ReleaseModifierKeys(modifierKeys);
                }
                else if (true == UseOttoMagic)
                {
                    MagicHelper.ReleaseModifierKey(modifierKeys);
                }
                else if (true == UsePuppeteer || (true == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware)))
                {
                    PuppeteerClient.ReleaseModifierKey(modifierKeys);
                }
                else
                {
                    Actions actions = new Actions(BrowserDriver);
                    bool anyKeySet = false;

                    if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    {
                        actions.KeyUp(Keys.Alt);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Control))
                    {
                        actions.KeyUp(Keys.Control);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    {
                        actions.KeyUp(Keys.Shift);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    {
                        actions.KeyUp(Keys.Meta);
                        anyKeySet = true;
                    }

                    ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                    if (ModifierKeys.NONE != unsupported)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ReleaseModifierKey: [{GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", this, GPALObjectType.Browser);

                    if (true == anyKeySet)
                        actions.Perform();
                }

            return this;
        }
        /// <summary>
        /// Type special characters like Enter, Page Up, Page Down, Tab, etc.<br/>
        /// Use GPAL.VK constants for ease of access to commmon keys.<br/>
        /// For all Virtual Keycodes, visit:<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        /// </summary>
        /// <example>
        /// <code>
        /// // send CTRL-HOME
        ///     ...
        ///     myBrowser
        ///         .PressModifierKey(ModifierKeys.Control)
        ///         .SendKey(GPAL.VK_HOME);
        ///         .WaitFor(150)
        ///         .ReleaseModifierKey(ModifierKeys.Control)
        /// </code>
        /// </example>
        /// <param name="VKCode">Virtual Keycode</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SendKey(byte VKCode)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.SendKey(this, VKCode);
            }

            return this;
        }
        /// <summary>
        /// Scroll the browser window horizontally the number of pixels.<br/>
        /// Negative numbers scroll left. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls right.<br/>Negative integer scrolls left.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollWindowHorizontal([Pixels] int scrollAmountInPixels)
        {
            InAction(false, false);

            if (true == UseOttoMagic)
                MagicHelper.ScrollWindowHorizontal(scrollAmountInPixels);
            else if (true == UsePuppeteer)
                PuppeteerClient.ScrollWindowByHorizontal(scrollAmountInPixels).Execute();
            else
                BrowserHelper.ExecuteJavaScript(this, $"window.scrollBy({{left: {scrollAmountInPixels}, top: 0, behavior: 'instant'}})");

            return this;
        }
        /// <summary>
        /// Scroll the browser window horizontally the number of pixels.<br/>
        /// Negative numbers scroll up. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls down.<br/>Negative integer scrolls up.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollWindowVertical([Pixels] int scrollAmountInPixels)
        {
            InAction(false, false);

            if (true == UseOttoMagic)
                MagicHelper.ScrollWindowVertical(scrollAmountInPixels);
            else if (true == UsePuppeteer)
                PuppeteerClient.ScrollWindowByVertical(scrollAmountInPixels).Execute();
            else
                BrowserHelper.ExecuteJavaScript(this, $"window.scrollBy({{left: 0, top: {scrollAmountInPixels}, behavior: 'instant'}})");

            return this;
        }
        /// <summary>
        /// Scroll the element horizontally by number of pixels.<br/>
        /// Negative numbers scroll left. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls right.<br/>Negative integer scolls left.</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        /// <remarks>
        /// Scrolls every selector in the unit of work, then ends it.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollElementHorizontal([Pixels] int scrollAmountInPixels)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.ScrollElement(this, scrollAmountInPixels, ScrollTypes.Horizontal);

            return this;
        }
        /// <summary>
        /// Scroll the element vertically by number of pixels.<br/>
        /// Negative numbers scroll up. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmountInPixels">Positive integer scrolls down.<br/>Negative integer scolls down.</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        /// <remarks>
        /// Scrolls every selector in the unit of work, then ends it.
        /// </remarks>
        public IAllowBrowserActionOrAnySelector ScrollElementVertical([Pixels] int scrollAmountInPixels)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.ScrollElement(this, scrollAmountInPixels, ScrollTypes.Vertical);

            return this;
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

                        return this;
                    }
                }
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to detect tab with URL [{URLorTabTuple}]", this, GPALObjectType.Browser);
            }

            return this;
        }

        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for the first element found for each selector.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector DragAndDrop(ModifierKeys modifierKeys)
        {
            // waitfor did not match if returns false, so we have no element to click
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.DragAndDrop(this, modifierKeys);
            }
            return this;
        }

        /// <summary>
        /// Drags the element(s) found for <paramref name="selector"/> (or the current selector(s) if none is
        /// given) by the selector's DeltaX/DeltaY.
        /// </summary>
        /// <param name="selector">Optional selector to add to the current unit of work before dragging</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector DragAndDrop(Selector selector = null)
        {
            if (null != selector)
                WithSelector(selector);

            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.DragAndDrop(this);
            }
            return this;
        }

        /// <summary>
        /// Executes JavaScript in the browser and stores the raw result object in JavaScriptResultObj.
        /// </summary>
        /// <param name="javascript">JavaScript to execute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ExecuteJavaScriptObj([JavaScript] string javascript)
        {
            JavaScriptResultObj = BrowserHelper.ExecuteJavaScriptObj(javascript, this);
            return this;
        }

        /// <summary>
        /// Executes JavaScript in the browser and stores the result as a string in JavaScriptResultStr.
        /// </summary>
        /// <param name="javascript">JavaScript to execute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ExecuteJavaScriptStr([JavaScript] string javascript)
        {
            JavaScriptResultStr = BrowserHelper.ExecuteJavaScript(this, javascript);
            return this;
        }

        private readonly List<string> _injectedScriptIdentifiers = new List<string>();

        // Whether a script is registered with the driving engine, and so needs removing on close
        private bool _scriptsRegistered = false;
        private readonly List<string> _pendingInjectedScripts = new List<string>();

        // True once the dialog override is registered; later answer changes are written into the page.
        private bool _dialogRegistered = false;

        // The last answers written and the page url, to avoid rewriting the same answers to the same page.
        private string _dialogPushed = null;

        // Incremented per registration; the highest number wins whatever the script order.
        private int _dialogSeq = 0;

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

        /// <summary>
        /// Hands one script to whichever engine is driving this browser. The engine must already be reachable -
        /// <see cref="InjectScript(string)"/> holds the script until it is.
        /// </summary>
        /// <param name="script">JavaScript source code to run on every new document</param>
        private void RegisterInjectedScript(string script)
        {
            _scriptsRegistered = true;

            if (true == UseOttoMagic)
            {
                MagicHelper.InjectScript(script);
            }
            else if (true == UsePuppeteer)
            {
                PuppeteerCommunicator.InjectScript(script, null).GetAwaiter().GetResult();
            }
            else // selenium
            {
                var identifier = BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, script);
                if (false == string.IsNullOrEmpty(identifier))
                    _injectedScriptIdentifiers.Add(identifier);
            }
        }

        /// <summary>
        /// Registers every script that <see cref="InjectScript(string)"/> held while the browser was still down.
        /// Called from WorkflowSetup once the engine answers and before the first navigation, so a script queued
        /// ahead of .GoTo() still runs on the first document.
        /// </summary>
        internal void ApplyPendingInjectedScripts()
        {
            if (0 < _pendingInjectedScripts.Count && true == EngineReachable)
            {
                foreach (var script in _pendingInjectedScripts)
                    RegisterInjectedScript(script);

                _pendingInjectedScripts.Clear();
            }
        }

        /// <summary>
        /// Answers alert, confirm and prompt automatically so they do not block. True is OK (confirm returns true),
        /// false is Cancel. Without this, dialogs block as usual.
        /// <br/><br/>
        /// Selenium answers through a driver capability and Puppeteer through CDP, leaving the page's functions
        /// untouched and undetectable. On OttoMagic an open dialog blocks the extension's page thread, so GPAL
        /// replaces alert, confirm and prompt before the page runs; a page can detect that, so only OttoMagic does
        /// it.
        /// </summary>
        /// <param name="accept">True to accept, false to dismiss.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithDialogsAccepted(bool accept)
        {
            // Selenium reads this capability only at driver start, so warn if the browser is already running. Not
            // when prompt text is set: Selenium then answers in the page, where the answer can change.
            if (true == UseSelenium && null != BrowserSettings.BrowserDriver && false == InPageDialogs)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium takes this when the browser starts, and this one is already running, so dialogs are not answered. Call WithDialogsAccepted before the first GoTo", this, GPALObjectType.Browser);

            BrowserSettings.DialogsAccepted = accept;

            ArmDialogHandling();

            return this;
        }

        /// <summary>
        /// Text a prompt returns when dialogs are accepted. Unset, the prompt's default stands.
        /// </summary>
        /// <param name="text">The text to answer a prompt with.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithDialogText(string text)
        {
            BrowserSettings.DialogText = text;

            // Setting prompt text also turns on accepting dialogs, if WithDialogsAccepted was not called.
            if (null == BrowserSettings.DialogsAccepted)
                BrowserSettings.DialogsAccepted = true;

            ArmDialogHandling();

            return this;
        }

        /// <summary>
        /// Stops answering dialogs, so they block again. Unlike <see cref="WithDialogsAccepted(bool)"/>(false), which
        /// answers Cancel.
        /// <br/><br/>
        /// Puppeteer stops at once. On OttoMagic the replacements stay (the script cannot be removed alone) but pass
        /// calls to the originals; <see cref="ClearInjectedScripts"/> and navigate to remove them. Selenium cannot
        /// stop; the driver keeps the capability until it closes.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearDialogHandling()
        {
            BrowserSettings.DialogsAccepted = null;
            BrowserSettings.DialogText = null;

            ArmDialogHandling();

            return this;
        }

        /// <summary>
        /// Sets up dialog answering on the current engine. Also called from WorkflowSetup, since the setting usually
        /// comes before the browser.
        /// </summary>
        internal void ArmDialogHandling()
        {
            // Return if the engine is unreachable, or dialogs are off and the override was never registered. A
            // registered override must be told to stop.
            if (false == EngineReachable || (null == BrowserSettings.DialogsAccepted && false == _dialogRegistered))
                return;

            if (true == InPageDialogs)
            {
                if (false == _dialogRegistered && null != BrowserSettings.DialogsAccepted)
                {
                    string script = DialogScript();

                    _dialogRegistered = true;

                    // The registered script covers new documents; also run it on an already loaded page.
                    InjectScript(script);

                    if (true == _navigated)
                        BrowserHelper.ExecuteJavaScript(this, script);

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answering dialogs in the page, accept [{BrowserSettings.DialogsAccepted}]", this, GPALObjectType.Browser);
                }

                // Each page's script sets the answers it was registered with, so they go stale when the workflow
                // changes them or the page changes. Rewrite when the answers or url changed since the last write.
                string written = DialogAnswers() + "@" + BrowserSettings.CurrentURL;

                if (true == _navigated && written != _dialogPushed)
                {
                    _dialogPushed = written;
                    PushDialogAnswers();
                }

                // OttoMagic only, once, after the first navigation: check the override is on the page. The
                // extension can silently refuse registration (usually Allow user scripts is off), and then the
                // first dialog blocks the workflow, so warn. Selenium registers over CDP, which cannot refuse.
                if (true == UseOttoMagic && true == _navigated && false == _dialogChecked)
                {
                    _dialogChecked = true;

                    // An expression, not a statement. OttoMagic evaluates the last expression, and a top-level
                    // return is a syntax error there
                    string seen = BrowserHelper.ExecuteJavaScript(this, "window.__gpalDialogSeq");

                    if (true == string.IsNullOrEmpty(seen) || "null" == seen)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The dialog override is not on the page, so dialogs will stop this workflow. The usual cause is user scripts being off; turn them on with the Turn On User Scripts button in OttoMagic's settings", this, GPALObjectType.Browser);
                }
            }
            else if (true == UsePuppeteer && null != BrowserSettings.DialogsAccepted && false == _dialogRegistered)
            {
                // Page.javascriptDialogOpening does not arrive until the domain is on, and the launch paths do
                // not turn it on
                _dialogRegistered = true;

                PuppeteerCommunicator.EnablePageEvents().GetAwaiter().GetResult();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answering dialogs over CDP, accept [{BrowserSettings.DialogsAccepted}]", this, GPALObjectType.Browser);
            }

            // selenium answering yes or no is armed at launch, because UnhandledPromptBehavior is a capability
            // on the driver options and nothing after that can change it
        }

        /// <summary>
        /// True when the in-page override answers dialogs instead of the browser.
        /// <br/><br/>
        /// Always for OttoMagic, since a dialog blocks its extension's page thread. For Selenium, only with
        /// WithDialogText, since its capability cannot type prompt text. Never for Puppeteer, which sends prompt
        /// text over CDP.
        /// </summary>
        private bool InPageDialogs => true == UseOttoMagic
            || (true == UseSelenium && null != BrowserSettings.DialogText);

        // True once a navigation has finished. Not BrowserSettings.CurrentURL, which GoTo sets before navigating.
        private bool _navigated = false;

        // whether the override was found in place on the page after the first navigation
        private bool _dialogChecked = false;

        /// <summary>
        /// Writes the current answers into the open page, which ran the registered script with the old answers when
        /// it loaded.
        /// </summary>
        private void PushDialogAnswers()
        {
            // Write only if the override is already on the page (window.__gpalDialogSeq); never installs it.
            BrowserHelper.ExecuteJavaScript(this, "if (window.__gpalDialogSeq) { " + DialogAnswers() + " }");
        }

        /// <summary>
        /// JavaScript setting window.__gpalDialogAccept and window.__gpalDialogAnswer. Written to the page, and
        /// compared as a string to detect changes.
        /// </summary>
        /// <returns>JavaScript assigning the current answers.</returns>
        private string DialogAnswers()
        {
            string accept = null == BrowserSettings.DialogsAccepted
                ? "null"
                : true == BrowserSettings.DialogsAccepted ? "true" : "false";
            string typed = null == BrowserSettings.DialogText ? "null" : JsonConvert.SerializeObject(BrowserSettings.DialogText);

            return "window.__gpalDialogAccept = " + accept + "; window.__gpalDialogAnswer = " + typed + ";";
        }

        /// <summary>
        /// The script that replaces alert, confirm and prompt before the page runs, so no dialog opens. The highest
        /// registration number wins, so the newest answers apply whatever the script order.
        /// </summary>
        /// <returns>JavaScript source to register on every new document.</returns>
        private string DialogScript()
        {
            return
                "(function () {" +
                "  var seq = " + (++_dialogSeq) + ";" +
                "  if (window.__gpalDialogSeq >= seq) return;" +
                "  window.__gpalDialogSeq = seq;" +
                "  " + DialogAnswers() +
                "  var asText = Function.prototype.toString;" +
                "  function stand(name, fn) {" +
                "    Object.defineProperty(fn, 'name', { value: name, configurable: true });" +
                // Define the replacement where the original is. Chrome has these as own properties of window,
                // so one on Window.prototype would be shadowed and never called. It also looks like the original
                // to the page.
                "    var onto = Object.getOwnPropertyDescriptor(window, name) ? window : Window.prototype;" +
                "    Object.defineProperty(onto, name," +
                "      { value: fn, writable: true, enumerable: true, configurable: true });" +
                "  }" +
                // Keep the originals, for the replacements to call when dialog handling is off.
                "  var real = { alert: window.alert, confirm: window.confirm, prompt: window.prompt };" +
                "  function off() { return null === window.__gpalDialogAccept; }" +
                "  stand('alert', function (message) {" +
                "    if (off()) return real.alert.call(window, message);" +
                "  });" +
                "  stand('confirm', function (message) {" +
                "    if (off()) return real.confirm.call(window, message);" +
                "    return true === window.__gpalDialogAccept;" +
                "  });" +
                "  stand('prompt', function (message, preset) {" +
                "    if (off()) return real.prompt.call(window, message, preset);" +
                "    if (true !== window.__gpalDialogAccept) return null;" +
                "    return null !== window.__gpalDialogAnswer ? window.__gpalDialogAnswer : (preset || '');" +
                "  });" +
                // toString on the replacements returns "[native code]" like the originals. Harder to detect, not
                // undetectable.
                "  Function.prototype.toString = function () {" +
                "    if (this === window.alert || this === window.confirm || this === window.prompt" +
                "        || this === Function.prototype.toString)" +
                "      return 'function ' + this.name + '() { [native code] }';" +
                "    return asText.call(this);" +
                "  };" +
                "})();";
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
        /// Registers a script (raw JS string) to run on every new document load. Callable before the first
        /// .GoTo()/.Get() (active from the first page load) or at any later point (applies to subsequent
        /// navigations). Persists until <see cref="ClearInjectedScripts"/> is called.
        /// </summary>
        /// <param name="script">JavaScript source code to run on every new document</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InjectScript([JavaScript] string script)
        {
            // no browser to inject into until the first .GoTo()/.Get() launches one, so the script is held
            // and registered the moment the engine answers, ahead of the first navigation
            if (false == EngineReachable)
                _pendingInjectedScripts.Add(script);
            else
                RegisterInjectedScript(script);

            return this;
        }

        /// <summary>
        /// Registers script(s) loaded from <paramref name="scriptFile"/> to run on every new document load - if
        /// multiple filenames are specified (e.g. via wildcard), all are loaded and injected as separate scripts.
        /// Persists until <see cref="ClearInjectedScripts"/> is called.
        /// </summary>
        /// <param name="scriptFile">GPALFile referencing one or more JavaScript files</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InjectScript(GPALFile scriptFile)
        {
            foreach (var filename in scriptFile.Filenames)
                InjectScript(File.ReadAllText(filename));
            return this;
        }

        /// <summary>
        /// Removes all scripts previously registered via <see cref="InjectScript(string)"/> or
        /// <see cref="InjectScript(GPALFile)"/>.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearInjectedScripts()
        {
            // scripts still waiting on a browser go first, so one queued and then cleared never reaches the page
            _pendingInjectedScripts.Clear();

            if (true == EngineReachable)
            {
                if (true == UseOttoMagic)
                {
                    MagicHelper.ClearInjectedScripts();
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerCommunicator.ClearInjectedScripts().GetAwaiter().GetResult();
                }
                else // selenium
                {
                    foreach (var identifier in _injectedScriptIdentifiers)
                        BrowserHelper.SeleniumRemoveScriptToEvaluateOnNewDocument(BrowserSettings, identifier);
                    _injectedScriptIdentifiers.Clear();
                }
            }
            return this;
        }

        /// <summary>
        /// Sets the value of a range/slider input element matched by the current selector(s) by scrolling it
        /// into view and setting its value to <paramref name="rangeValue"/>.
        /// </summary>
        /// <param name="rangeValue">Value to set the range input to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SetRange(int rangeValue)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    ElementHelper.FindWebElements(this, CurrentUOW, selector, out _, out List<GPALElement> elems);
                    foreach (GPALElement elem in elems)
                    {
                        ElementHelper.ScrollIntoView(this, elem);

                        if (true == UseOttoMagic)
                            MagicHelper.SetRange(elem.Css, rangeValue);
                        else if (true == UsePuppeteer)
                            PuppeteerClient.SetRange(elem.Css).WithRangeValue(rangeValue).Execute();
                        else if (true == UseSelenium)
                        {
                            // Set the value and dispatch the change events so the UI updates
                            string script = @"
                            arguments[0].value = arguments[1];
                            arguments[0].dispatchEvent(new Event('input', { bubbles: true }));
                            arguments[0].dispatchEvent(new Event('change', { bubbles: true }));
                        ";

                            BrowserHelper.ExecuteJavaScript(this, script, elem.WebElement, rangeValue);
                        }
                    }
                }

            return this;
        }
        #endregion <Browser Actions>
        #region <Getters>
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
        #endregion <Getters>
        #region <Helpers>
        #region <Private>
        /// <summary>
        /// Convenience method to page up or down
        /// Takes into account single page apps and scrolling containers as well as reglar html
        /// </summary>
        /// <param name="direction">"up" or "down"</param>
        /// <returns></returns>
        bool ScrollPage(string direction)
        {
            try
            {
                // Validate direction
                if (direction != "up" && direction != "down")
                {
                    return false;
                }

                // JavaScript code with direction parameter
                string script = @"
                    (function(direction) {
                        function scrollPage(direction) {
                        try {
                            const isDown = direction === 'down';
                            // 87.5% of the view, as the browser's own Page Down steps, so each page overlaps the
                            // last and nothing under a fixed header or on a page boundary is skipped
                            const scrollAmount = Math.round(window.innerHeight * 0.875) * (isDown ? 1 : -1);
                            const keyName = isDown ? 'PageDown' : 'PageUp';
                            const keyCode = isDown ? 34 : 33;

                            // Check overflow
                            const overflow = getComputedStyle(document.documentElement).overflow;

                            // Try window.scrollBy if overflow is not 'hidden'
                            const initialScrollY = window.scrollY;
                            if (overflow !== 'hidden') {
                            window.scrollBy({ left: 0, top: scrollAmount, behavior: 'instant' });
                            if (window.scrollY !== initialScrollY) {
                                return {
                                status: 'success',
                                method: 'window.scrollBy',
                                scrollPosition: window.scrollY,
                                elementClass: 'window'
                                };
                            }
                            }

                            // Try scrolling the scrollable element
                            const scrollable = Array.from(document.querySelectorAll('*')).find(
                            el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                            ) || document.body;
                            const initialScrollTop = scrollable.scrollTop;
                            scrollable.scrollBy({ left: 0, top: scrollAmount, behavior: 'instant' });
                            const elementClass = scrollable.className || 'body';

                            if (scrollable.scrollTop !== initialScrollTop) {
                            return {
                                status: 'success',
                                method: 'element.scrollBy',
                                scrollPosition: scrollable.scrollTop,
                                elementClass
                            };
                            }

                            // Fallback to Page Up/Down key press
                            scrollable.focus();
                            const event = new KeyboardEvent('keydown', {
                            key: keyName,
                            code: keyName,
                            keyCode,
                            which: keyCode,
                            bubbles: true,
                            cancelable: true
                            });
                            scrollable.dispatchEvent(event);

                            if (scrollable.scrollTop !== initialScrollTop) {
                            return {
                                status: 'success',
                                method: keyName,
                                scrollPosition: scrollable.scrollTop,
                                elementClass
                            };
                            }

                            // No scroll method worked
                            return {
                            status: 'failed',
                            message: 'No scroll method worked',
                            scrollPosition: scrollable.scrollTop,
                            elementClass
                            };
                        } catch (error) {
                            return {
                            status: 'error',
                            message: error.message,
                            scrollPosition: 0,
                            elementClass: 'none'
                            };
                        }
                        }
                        return scrollPage(direction);
                    })('" + direction + "')";

                // Execute the script
                var result = BrowserHelper.ExecuteJavaScript(this, script);

                // Parse result (adjust based on BrowserHelper's return type)
                dynamic response = result; // Assumes result is a dynamic object or JSON
                return response.status == "success";
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Convenience method to scroll to page top or end
        /// Takes into account single page apps and scrolling containers as well as reglar html
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        private bool ScrollToPosition(string position)
        {
            try
            {
                // Validate position
                if (position != "top" && position != "end")
                {
                    return false;
                }

                // JavaScript code with position parameter
                string script = @"
                    (function(position) {
                        function scrollToPosition(position) {
                            try {
                                const isEnd = position === 'end';
                                // the page's full height is documentElement's; body is shorter by its margins
                                const scrollTarget = isEnd ? Math.max(document.documentElement.scrollHeight, document.body.scrollHeight) : 0;
                                const keyName = isEnd ? 'End' : 'Home';
                                const keyCode = isEnd ? 35 : 36;

                                // Check overflow
                                const overflow = getComputedStyle(document.documentElement).overflow;

                                // Try window.scrollTo if overflow is not 'hidden'
                                const initialScrollY = window.scrollY;
                                if (overflow !== 'hidden') {
                                    window.scrollTo({ left: 0, top: scrollTarget, behavior: 'instant' });
                                    if (window.scrollY !== initialScrollY) {
                                        return {
                                            status: 'success',
                                            method: 'window.scrollTo',
                                            scrollPosition: window.scrollY,
                                            elementClass: 'window'
                                        };
                                    }
                                }

                                // Try scrolling the scrollable element
                                const scrollable = Array.from(document.querySelectorAll('*')).find(
                                    el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                                ) || document.body;
                                const initialScrollTop = scrollable.scrollTop;
                                const elementScrollTarget = isEnd ? scrollable.scrollHeight : 0;
                                scrollable.scrollTo({ left: 0, top: elementScrollTarget, behavior: 'instant' });
                                const elementClass = scrollable.className || 'body';

                                if (scrollable.scrollTop !== initialScrollTop) {
                                    return {
                                        status: 'success',
                                        method: 'element.scrollTo',
                                        scrollPosition: scrollable.scrollTop,
                                        elementClass
                                    };
                                }

                                // Fallback to Home/End key press
                                scrollable.focus();
                                const event = new KeyboardEvent('keydown', {
                                    key: keyName,
                                    code: keyName,
                                    keyCode,
                                    which: keyCode,
                                    bubbles: true,
                                    cancelable: true
                                });
                                scrollable.dispatchEvent(event);

                                if (scrollable.scrollTop !== initialScrollTop) {
                                    return {
                                        status: 'success',
                                        method: keyName,
                                        scrollPosition: scrollable.scrollTop,
                                        elementClass
                                    };
                                }

                                // No scroll method worked
                                return {
                                    status: 'failed',
                                    message: 'No scroll method worked',
                                    scrollPosition: scrollable.scrollTop,
                                    elementClass
                                };
                            } catch (error) {
                                return {
                                    status: 'error',
                                    message: error.message,
                                    scrollPosition: 0,
                                    elementClass: 'none'
                                };
                            }
                        }
                        return scrollToPosition(position);
                    })('" + position + "')";

                // Execute the script
                var result = BrowserHelper.ExecuteJavaScript(this, script);

                // Parse result (adjust based on BrowserHelper's return type)
                dynamic response = result; // Assumes result is a dynamic object or JSON
                return response.status == "success";
            }
            catch
            {
                return false;
            }
        }
        /// <summary>
        /// Check to see if any elements marked themselves for removal
        /// Very important if we are iterating a list and it changes mid-iteration, c# will complain
        /// </summary>
        /// <param name="CurrentUOW"></param>
        private static void SelectorListMaintenanceSweep(UnitOfWork CurrentUOW)
        {
            for (int idx = 0; idx < CurrentUOW.WithSelectorList.Count; idx++)
                if (true == CurrentUOW.WithSelectorList[idx].DeleteMe)
                    CurrentUOW.WithSelectorList.RemoveAt(idx);

            for (int idx = 0; idx < CurrentUOW.InSelectorList.Count; idx++)
                if (true == CurrentUOW.InSelectorList[idx].DeleteMe)
                    CurrentUOW.InSelectorList.RemoveAt(idx);
        }
        /// <summary>
        /// Check for persistent elements which can show up at any time and interrupt the main workflow
        /// These are checked before any action is performed.
        /// </summary>
        private void CheckPersistentSelectors()
        {
            UnitOfWork safeUOW = CurrentUOW;

            // all we have to do is walk the list, try to find the element and the callif callbacks will do the work
            if (0 < (persistentUOW.WithSelectorList?.Count ?? 0))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Checking [{persistentUOW.WithSelectorList?.Count}] persistent selector(s)");

                foreach (Selector selector in persistentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    // we cannot use cached results knowing we are a new uow/action
                    selector.WebSelectorFoundResults = null;
                    selector.WebSelectorMatchedResults = null;
                    CurrentUOW.CurrentSelector = selector;


                    ElementHelper.FindWebElements(this, persistentUOW, selector, out _, out _, null, true, true);
                }
            }
            CurrentUOW = safeUOW;
        }
        /// <summary>
        /// Check to see if we are waiting for a selector to show up or just moving directly into the workflow
        /// </summary>
        /// <returns></returns>
        private bool CheckWaitFor()
        {
            bool matchFound = true;
            List<GPALElement> foundElements = null;

            // An earlier action in this unit of work already found these selectors, and a later selector would have
            // started a new unit of work, so skip the wait. The action still finds its own elements.
            if (null != CurrentUOW.WaitForSatisfied)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Selectors for this unit of work already found at [{CurrentUOW.WaitForSatisfied:HH:mm:ss.fff}], skipping redundant wait.", this, GPALObjectType.Browser);
                return true;
            }

            // -1 means wait forever
            // -2 never
            if (WaitTime.Never < CurrentUOW.WaitForInMs)
            {
                // we are waiting on selector(s)
                foundElements = ElementHelper.WaitFor(this, CurrentUOW.WaitForInMs, out matchFound);

                if (0 < foundElements?.Count)
                    CurrentUOW.WaitForSatisfied = DateTime.Now;
            }

            return (0 < foundElements?.Count);
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

        internal bool WorkflowSetup(bool checkWaitFor, bool doMaintenance, GPALUrl URL = null)
        {
            // Close ended this browser. Its process, driver and connection are gone, so nothing it is asked to do can
            // run.
            if (null != ClosedAt)
                throw new GPALException($"This browser was closed at [{ClosedAt:HH:mm:ss}] and a closed browser is done. Create a new one with GPAL.Browser.");

            // A Selenium driver or browser that has exited answers nothing, and every command after it waits out its
            // timeout. That is GPAL's own infrastructure gone, so the browser is closed, freeing its profile, and the
            // workflow ends, as on Puppeteer.
            string exited = true == UseSelenium && false == inClose && null != BrowserDriver ? SeleniumProcessExited() : null;

            if (null != exited)
            {
                string msg = $"The [{exited}] process has exited, so there is nothing left to send a command to";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.Browser);

                Close(true);

                throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
            }

            // settled before anything is launched, because the engine chosen here decides which of the branches
            // below runs
            if (true == BrowserSettings.HiddenDesktop && false == hiddenDesktopReported)
            {
                hiddenDesktopReported = true;

                // SendInput only reaches the on-screen desktop. Switch to the non-hardware engine and warn with the
                // engine used.
                if (true == BrowserSettings.UseHardware)
                {
                    AutomationEngine steppedDownTo = HardwareStepDown(BrowserSettings.AutomationEngine);

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.AutomationEngine}] cannot drive a hidden desktop, so this browser runs on [{steppedDownTo}]", this, GPALObjectType.Browser);

                    BrowserSettings.AutomationEngine = steppedDownTo;
                }

                // If the hidden desktop cannot be created, warn and run on the user's screen.
                if (false == Desktops.Ensure(BrowserSettings.HiddenDesktopName))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No hidden desktop could be made, so this browser opens on the desktop you are working on", this, GPALObjectType.Browser);

                    BrowserSettings.HiddenDesktop = false;
                }
                else
                {
                    // A Save As dialog on a hidden desktop has no one to answer it, so downloads go straight to the
                    // download folder, as they do headless, whatever WithPromptForDownload asked for
                    BrowserSettings.PromptForDownload = false;
                }
            }

            // Edge's download.prompt_for_download opens its own "what do you want to do" menu, never the Save As
            // dialog GPAL answers, so downloads go straight to the download folder and are saved under the name asked
            // for, as on a hidden desktop
            if (BrowserType.Edge == BrowserSettings.BrowserType && true == BrowserSettings.PromptForDownload)
            {
                BrowserSettings.PromptForDownload = false;

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Edge asks what to do with a download in its own menu, not a Save As dialog, so its downloads go straight to the download folder", this, GPALObjectType.Browser);
            }

            // also checked in WithProfileDataDirectory as vital for that operation, but if that is not specified then this is the best location to set this
            // NOTE: CAVEAT: it's not clear this is even required here, but we may find a use for it
            //if (null == BrowserSettings.ProfileDataDirectory)
            //    BrowserSettings.ProfileDataDirectory = FileHelper.EnsureDirectoryEndsWithBackslash(BrowserHelper.GetBrowserProfileDirectory(BrowserSettings, true));

            // first time init is the only time URL is used
            // but due to window tracking, we will always go to https://google.com first then an explicit goto
            if (true == UseSelenium)
            {
                // instatiate a selenium driver and launch chrome headless
                if (null == BrowserDriver)
                {
                    if (null == (BrowserDriver = LaunchWhereAsked(() => BrowserHelper.GetBrowserDriver(BrowserSettings))))
                        return false;    // NOTE: CAVEAT: will never return false, getbrowserdiver throws an exception and end the workflow if it can't start the browser

                    // A driver that connects to a running browser starts on whichever tab it attaches to, which on Edge
                    // can be its hidden new-tab page. Move to the tab the browser is showing.
                    if (true == BrowserSettings.UseExistingBrowser)
                    {
                        string showing = null;

                        foreach (string handle in BrowserDriver.WindowHandles)
                            if (null == showing)
                            {
                                BrowserDriver.SwitchTo().Window(handle);

                                if ("visible" == ((IJavaScriptExecutor)BrowserDriver).ExecuteScript("return document.visibilityState;") as string)
                                    showing = handle;
                            }

                        if (null != showing)
                            BrowserDriver.SwitchTo().Window(showing);

                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Connected on the tab the browser is showing [{(null != showing ? BrowserDriver.Url : "none visible")}]", this, GPALObjectType.Browser);
                    }

                    // selenium allows runtime setting of prompt for download and for chromium browsers, we can turn it off runtime via CDP
                    // if we are running under a temp profile (none specified) we write out preferences to handle prompting to save
                    // but if a profile is loaded and it doesn't have it set in the profile, we have to toggle it based upon the workflow setting
                    //  firefox has no runtime equivalent, so it is set in the profile prefs when the driver is built(BrowserHelper GetBrowserDriver).
                    if (false == BrowserSettings.PromptForDownload && BrowserType.FireFox != BrowserSettings.BrowserType)
                        BrowserHelper.AllowDownloads(BrowserSettings, BrowserSettings.DownloadLocation ?? FileHelper.GetDefaultDownloadDirectory(this));
                }
            }
            // Do not launch when attached through GPALRestAPI or an existing port. An attached browser has no
            // Process, which would otherwise launch a browser on every action.
            else if (null == BrowserSettings.Process && false == BrowserSettings.AttachedRestApi && false == BrowserSettings.UseExistingBrowser)   // use browser extension/puppeteer to control browser, not selenium
            {
                if (true == UseOttoMagic)
                    // we have to ensure we always open to a page that loads a content page.
                    BrowserSettings.Process = LaunchWhereAsked(() => MagicHelper.LaunchBrowser(this, URL));
                else
                    BrowserSettings.Process = LaunchWhereAsked(() => Puppeteer.LaunchBrowser(this, URL));
            }

            // Once, after the first launch, mark the hidden desktop as checked. Skipped headless, which opens no
            // window.
            if (true == BrowserSettings.HiddenDesktop && false == BrowserSettings.UseHeadless && false == hiddenDesktopChecked)
            {
                hiddenDesktopChecked = true;
            }

            // No bring-to-front on a hidden desktop; it is not on screen, and the hardware fallback cannot reach it.
            // None for a browser reached through GPALRestAPI on another machine either: GPAL has no process for it.
            if (false == BrowserSettings.HiddenDesktop && false == inClose && (false == BrowserSettings.AttachedRestApi || null != BrowserSettings.Process))
                BrowserHelper.TopBrowser(this, false); // ensure the browser window is on top, can use selenium or hardware (mouse click) topping. firefox will only top with hardware

            CurrentUOW.ActionCalled = true;

            if (true == doMaintenance)
            {
                SelectorListMaintenanceSweep(CurrentUOW); // purge any nodes marked 'deleteme' before iterating list
                SelectorListMaintenanceSweep(persistentUOW); // purge any nodes marked 'deleteme' before iterating list
                CheckPersistentSelectors(); // check if there are persistent selectors to deal with
            }

            if (true == checkWaitFor)
                CheckWaitFor();

            if (true == UsePuppeteer && null == BrowserSettings.PuppeteerUrl)
            {
                BrowserSettings.PuppeteerUrl = $"http://127.0.0.1:{BrowserSettings.DebugPort}";
                _ = PuppeteerCommunicator;  // init

                // A browser reached with WithExistingCDPDebugPort was started by someone else, so it has no Process.
                // Bringing its window to the front, which hardware input needs, finds the window through it.
                if (true == BrowserSettings.UseExistingBrowser && null == BrowserSettings.Process)
                    BrowserSettings.Process = PuppeteerCommunicator.GetBrowserProcess().GetAwaiter().GetResult();

                // Selenium writes download.prompt_for_download as a profile preference on every launch, but the
                // puppeteer path only gets it when LaunchBrowser built the profile itself. Setting it here covers
                // every profile, and being browser scoped it holds for tabs opened later
                if (false == BrowserSettings.PromptForDownload)
                {
                    PuppeteerCommunicator.SetDownloadBehavior(BrowserSettings.DownloadLocation ?? FileHelper.GetDefaultDownloadDirectory(this)).GetAwaiter().GetResult();
                    BrowserSettings.DownloadBehaviorSet = true;
                }
            }

            // every engine is reachable and GoTo has not navigated yet, so held scripts registered here still
            // run on the first document
            ApplyPendingInjectedScripts();
            ApplyPendingStorageActions();
            ArmDialogHandling();

            return true;
        }
        bool firstRun { get; set; } = true;
        bool inAction = false;
        /// <summary>
        /// Perform Action maintenance before performing an action
        /// Such as:
        ///     first run instantiating a browser, performing .WaitFor selector operations, persistent selector searches and 
        ///     callifhandler self-removal maintenance when using these in a one-time use callif haandler
        ///         selector.Remove();                                      // remove the selector from the persistent selector list
        ///         browser.RemoveCallIfHandlerEverywhere(CloseLoginPopup); // CloseLoginPopup is the callif handler method reference
        /// </summary>
        /// <param name="checkWaitFor"></param>
        /// <param name="doMaintenance"></param>
        /// <param name="URL"></param>
        /// <returns></returns>
        internal bool InAction(bool checkWaitFor, bool doMaintenance = true, GPALUrl URL = null)
        {
            bool retval = true;
            if (false == inAction)
            {
                inAction = true;
                FileDownloaded = false; // any action will clear out this flag, but we can leftclickanddownload and then .WIthSelector(noDataSelector) and preserve it.

                // we have to return true otherwise callIfFound/NotFound are never invoked - waitfor just waits for the element to show up, but doesn't abend the workflow
                // getBrowserDriver throws rather than returning null, so this should always return true
                // Cleared in finally, so after an exception later actions still run WorkflowSetup.
                try
                {
                    retval = WorkflowSetup(checkWaitFor, doMaintenance, URL);
                }
                finally
                {
                    inAction = false;
                }
            }
            return retval;    // NOTE: CAVEAT: useless
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
                // back to the exact tab we left, by id. It never navigated, so it is still open unless the user
                // closed it themselves, in which case the last tab in the list is as good an answer as any
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
        // When the last GoTo's navigation failed by the engine's own report (Puppeteer's error text); null when it did
        // not. A link that is a file fails this way on Puppeteer (net::ERR_ABORTED) as the browser turns it into a
        // download.
        internal DateTime? NavigationFailedAt { get; private set; }

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
                    // NOTE: add search gpalurllist and setting gpalcurrenturl
                }
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "ActiveTabTuple from last operation is null", this, GPALObjectType.Browser);
        }
        // NOTE: not implemented. the concept was TabIds for ottomagic, for precise tab switching where the
        // server rewrites urls
        /// <summary>
        /// Attempts to interpret <paramref name="number"/> as a 32-bit unsigned integer and decode it into a
        /// dotted-quad IPv4 address.
        /// </summary>
        /// <param name="number">String to attempt to decode as an IP address</param>
        /// <param name="ipAddress">Receives the decoded dotted-quad IP address, or null if decoding failed</param>
        /// <returns>True if <paramref name="number"/> was successfully decoded</returns>
        internal bool TryDecodeNumberToIpAddress(string number, out string ipAddress)
        {
            bool retVal = false;
            uint numberUInt;

            ipAddress = null;

            if (true == UInt32.TryParse(number, out numberUInt))
            {

                // Convert number to IPv4 bytes (network byte order)
                byte[] bytes = BitConverter.GetBytes(numberUInt);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes); // Ensure big-endian for IP

                // Create IP address from bytes
                try
                {
                    var ip = new System.Net.IPAddress(bytes);
                    ipAddress = ip.ToString(); // e.g., "192.168.1.1"

                    // Validate: Ensure it's a valid IPv4 address (4 octets, 0-255 range)
                    var octets = ipAddress.Split('.');
                    if (octets.Length != 4) return false;
                    if (octets.All(o => byte.TryParse(o, out byte value) && value <= 255))
                    {
                        try
                        {
                            var host = System.Net.Dns.GetHostEntry(ip);
                            //                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{number}] resolves to [{ip}][{host.HostName}].", this, GPALObjectType.Browser);
                            retVal = true;
                        }
                        catch (GPALException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{number}][{ip}] doesn't resolve to a hostname.", this, GPALObjectType.Browser, ex);
                        }
                    }

                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{number}] doesn't resolve to an IP address", this, GPALObjectType.Browser, ex);
                }
            }

            return retVal;
        }
        #endregion <Private>
        /// <summary>
        /// Close/terminate the browser.<br/>
        /// This terminates the fluent browser workflow and nothing can be chained to this command.<br/>
        /// <b>NOTE:</b> If you are ending your application, pass in true to kill all running web drivers.
        /// </summary>
        /// <param name="killWebDrivers">True to kill all web drivers<br/>False [default] to only close the browser.</param>
        public void Close(bool killWebDrivers = false)
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

                // An attached browser may still be in use, so Close() closes only our window and leaves it running,
                // on every engine. Close(true) ends it, as for one GPAL launched: through GPALRestAPI by closing all
                // its windows, through the debug port by ending its process below.
                if (true == BrowserSettings.AttachedRestApi && true == killWebDrivers)
                {
                    if (false == browserEnded)
                        MagicHelper.CloseBrowser();
                }
                else if (true == BrowserSettings.AttachedRestApi || (true == BrowserSettings.UseExistingBrowser && false == killWebDrivers))
                    CloseOurWindowOnly();
                else if (1 == GPAL.Browsers.Count || true == killWebDrivers)
                {
                    // KillAllRunningProcesses also deletes the temp profile and asks the browser to quit over the
                    // Puppeteer connection, so mark the connection closed after it returns.
                    BrowserHelper.KillAllRunningProcesses(killWebDrivers, this);

                    if (true == UsePuppeteer)
                    {
                        BrowserSettings.PuppeteerCommunicator._isRunning = false;
                        BrowserSettings.PuppeteerCommunicator.Closed = true;
                    }

                    // A desktop made for this browser alone goes with it. A named desktop is shared and stays.
                    if (true == BrowserSettings.HiddenDesktop && true == BrowserSettings.HiddenDesktopOwned)
                        Desktops.Close(BrowserSettings.HiddenDesktopName);
                }
                else
                    CloseOurWindowOnly();

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
                // window closes, which holds the profile. End it, or the next run on this profile fails. A browser
                // GPAL connected to keeps running after Close(), so it is left alone then.
                if (false == BrowserSettings.UseExistingBrowser || true == killWebDrivers)
                    BrowserHelper.EndWindowlessSuccessor(BrowserSettings);

                // The browser Close(true) ended, known by its process or, for OttoMagic, by the GPALRestAPI address
                // every GPAL browser driving it talks to.
                int endedPid = true == killWebDrivers ? BrowserSettings.Process?.Id ?? 0 : 0;
                string endedRestApi = true == killWebDrivers ? BrowserSettings.RestApiBaseUrl : null;

                BrowserSettings.Process = null;

                GPAL.Forget(this);

                ClosedAt = DateTime.Now;

                // Close(true) ended the browser, so every other GPAL browser on it ended with it. Each runs its own
                // Close, which puts back what it changed (profile preferences, a temporary profile, a hidden desktop)
                // now that the browser is gone, and a later Close on one of them does nothing.
                if (0 < endedPid || null != endedRestApi)
                    foreach (Browser other in GPAL.Browsers)
                        if ((0 < endedPid && endedPid == (other.BrowserSettings.Process?.Id ?? 0))
                            || (null != endedRestApi && true == string.Equals(endedRestApi, other.BrowserSettings.RestApiBaseUrl, StringComparison.OrdinalIgnoreCase)))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Another GPAL browser on the same [{other.BrowserSettings.BrowserType}] {(0 < endedPid ? $"process [{endedPid}]" : $"GPALRestAPI [{endedRestApi}]")} is closed with it", other, GPALObjectType.Browser);

                            other.browserEnded = true;
                            other.Close(true);
                        }
            }
        }
        /// <summary>
        /// Leaves the browser running for another program to pick up, and ends this one's hold on it. Like
        /// <see cref="Close"/>, the browser is then done for this program.<br/>
        /// Chrome and Edge (debug port) and OttoMagic (GPALRestAPI) take several connections at once, so nothing is
        /// let go. Firefox on Selenium takes one: its geckodriver is ended without quitting, which frees Marionette
        /// and leaves Firefox running.<br/>
        /// What GPAL changed in the profile stays until the browser closes; the next launch on it puts it back.
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
        /// Closes this workflow's window and leaves the browser running.<br/>
        /// For a shared browser: another GPAL browser object is using it, or GPAL attached to it.
        /// </summary>
        private void CloseOurWindowOnly()
        {
            if (true == UseSelenium)
                BrowserDriver.Close();
            else if (true == UseOttoMagic && true == IsAlive)
                MagicHelper.CloseWindow(BrowserSettings.CurrentURL);
            else if (true == UsePuppeteer)
                PuppeteerClient.CloseWindow().Execute();
        }

        /// <summary>
        /// Sets the InteractionType of <paramref name="selector"/> for the current engine, from RequestedInteractionType
        /// rather than the last resolved value, so a selector reused on another engine resolves fresh.<br/>
        /// If unsupported, uses the engine default and warns.
        /// </summary>
        /// <param name="selector">Selector being attached to this browser</param>
        internal void ResolveInteractionType(Selector selector)
        {
            InteractionType engineDefault = true == UseOttoMagic ? InteractionType.OttoMagic
                                          : true == UsePuppeteer ? InteractionType.Puppeteer
                                          : InteractionType.Selenium;
            InteractionType resolved = engineDefault;
            bool wantsHardware = InteractionType.Hardware == selector.RequestedInteractionType || true == BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware;

            if (true == wantsHardware)
            {
                // SendInput only reaches the on-screen desktop. WorkflowSetup handles hardware engines; this handles
                // GPAL.WithUseHardware and a selector's WithHardware.
                if (true == BrowserSettings.HiddenDesktop)
                {
                    if (false == hiddenDesktopHardwareReported)
                    {
                        hiddenDesktopHardwareReported = true;

                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Hardware input cannot reach [{BrowserSettings.HiddenDesktopName}] while it is hidden, so [{engineDefault}] is used instead. Peek at the desktop to use hardware there.", this, GPALObjectType.Browser);
                    }
                }
                // A browser reached through GPALRestAPI on another machine has no window here to bring to the front.
                else if (true == BrowserSettings.AttachedRestApi && null == BrowserSettings.Process)
                {
                    if (false == restApiHardwareReported)
                    {
                        restApiHardwareReported = true;

                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Hardware input cannot reach the browser on [{BrowserSettings.RestApiBaseUrl}], so [{engineDefault}] is used instead.", this, GPALObjectType.Browser);
                    }
                }
                else if (false == BrowserSettings.UseHeadless)
                    resolved = InteractionType.Hardware;
                else if (InteractionType.Hardware == selector.RequestedInteractionType)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] asked for hardware interaction, which a headless browser has no mouse or keyboard to perform. Using [{engineDefault}].", this, GPALObjectType.Browser);
            }
            else if (InteractionType.JavaScript == selector.RequestedInteractionType || true == BrowserSettings.UseJavaScript)
                // ottomagic already drives the page through injected javascript, so its own interaction is the answer
                resolved = true == UseOttoMagic ? InteractionType.OttoMagic : InteractionType.JavaScript;
            else if (InteractionType.Selenium == selector.RequestedInteractionType && false == UseSelenium)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] asked for selenium interaction, which the [{AutomationEngine}] engine has no driver for. Using [{engineDefault}].", this, GPALObjectType.Browser);

            selector.InteractionType = resolved;
        }
        /// <summary>
        /// Helper method for GPAL applications to remove a handler from all UOWs. Envisioned to be used in a persistent handler, but can be used anyway imagined. This is not fluent.
        /// </summary>
        /// <example>
        /// <code>
        /// public static int FoundAllCookies(Browser browser, List&lt;GPALElement&gt; foundElements, List&lt;GPALElement&gt; matchedElements, Selector selector)
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
        /// <param name="func">The CallIf handler to remove</param>
        public void RemoveCallIfHandlerEverywhere(CallIfDelegate func)
        {
            // soft delete. the handler stays in the list and gets skipped when the chain runs, which keeps a
            // reused selector's handlers intact for the next workflow and keeps us from mutating a list
            // CallIfHandlers is in the middle of iterating - this is nearly always called from inside a handler
            foreach (Selector selector in CurrentUOW.WithSelectorList)
                if (false == selector.RemovedMethods.Contains(func))
                    selector.RemovedMethods.Add(func);

            foreach (Selector selector in CurrentUOW.InSelectorList)
                if (false == selector.RemovedMethods.Contains(func))
                    selector.RemovedMethods.Add(func);

            foreach (Selector selector in persistentUOW.WithSelectorList)
                if (false == selector.RemovedMethods.Contains(func))
                    selector.RemovedMethods.Add(func);
        }
        /// <summary>
        /// Helper method to check whether the current page is at the bottom
        /// </summary>
        /// <example>
        /// <code>
        ///     while (false == browser.IsEndOfPage())
        ///         _ = browser.PageDown
        /// </code>
        /// </example>
        /// <returns>true or false</returns>
        public bool IsEndOfPage()
        {
            // The check the image search and scroll-into-view use. Selenium had its own here, against
            // body.scrollHeight with no tolerance, so on Firefox, which scrolls to fractions, PageEnd never read as the end.
            bool retVal = ElementHelper.IsEndOfPage(this, null);

            return retVal;
        }
        #endregion <Helpers>
        #region Window Handling
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
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerClient.NextWindow().Execute();
                }
                else // Selenium
                {
                    // Get all tracked windows from your dictionary
                    var trackedWindows = BrowserSettings.WindowIdsToURL.Keys.ToList();

                    if (trackedWindows.Count <= 1)
                        return this; // Nothing to cycle to

                    // Find the current active one
                    string currentHandle = trackedWindows.FirstOrDefault(h =>
                        BrowserSettings.WindowIdsToURL[h].ActiveWindow);

                    // If none marked active (edge case), fall back to Selenium's current handle
                    if (string.IsNullOrEmpty(currentHandle))
                        currentHandle = BrowserDriver.CurrentWindowHandle;

                    // Find its index in the list
                    int currentIndex = trackedWindows.IndexOf(currentHandle);
                    if (currentIndex == -1) currentIndex = 0; // safety

                    // Calculate next index (cycle)
                    int nextIndex = (currentIndex + 1) % trackedWindows.Count;
                    string nextHandle = trackedWindows[nextIndex];

                    // Switch to it in Selenium
                    BrowserDriver.SwitchTo().Window(nextHandle);

                    // Update active flags
                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                    BrowserSettings.WindowIdsToURL[nextHandle].ActiveWindow = true;

                    // Optional: refresh URL in case page navigated independently
                    BrowserSettings.WindowIdsToURL[nextHandle].URL = BrowserDriver.Url;

                    BrowserSettings.CurrentURL = BrowserDriver.Url;
                }

                GetSetCurrentUrl();
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
            }
            else if (UsePuppeteer)
            {
                PuppeteerClient.OpenWindow(urlString).Execute();
                // Assume Puppeteer updates CurrentURL internally, or fetch it later
                BrowserSettings.CurrentURL = urlString; // fallback if not updated elsewhere
            }
            else // Selenium
            {
                string originalHandle = BrowserDriver.CurrentWindowHandle;

                // This creates a new top-level window and switches to it automatically
                BrowserDriver.SwitchTo().NewWindow(WindowType.Window);

                // Now we're in the new window
                string newHandle = BrowserDriver.CurrentWindowHandle;

                tabWindows[newHandle] = newHandle;   // a window is its own window, and the first tab in it

                // Navigate to the desired URL
                BrowserHelper.SeleniumGoToUrl(urlString, BrowserSettings);

                // Update your internal tracking
                foreach (var key in BrowserSettings.WindowIdsToURL.Keys.ToList())
                {
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;
                }

                BrowserSettings.WindowIdsToURL[newHandle] = new WindowInfo
                {
                    URL = urlString,
                    ActiveWindow = true
                };

                BrowserSettings.CurrentURL = BrowserDriver.Url; // should be the url now
            }

            URL = GetSetCurrentUrl();

            // NOTE: use evaluation shortcuiting, if it's not set, don't even check, it has a performance cost
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

            BrowserSettings.GPALUrlList.Add(URL);

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
                }
                else if (UsePuppeteer)
                {
                    PuppeteerClient.PreviousWindow().Execute();
                }
                else // Selenium
                {
                    // Get all tracked windows from dictionary (preserves insertion order)
                    var trackedWindows = BrowserSettings.WindowIdsToURL.Keys.ToList();

                    if (trackedWindows.Count <= 1)
                        return this; // Nothing to cycle to

                    // Find the current active one
                    string currentHandle = trackedWindows.FirstOrDefault(h =>
                        BrowserSettings.WindowIdsToURL[h].ActiveWindow);

                    // Fallback to Selenium's current handle if none marked active (safety)
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

                    // Optional: refresh stored URL in case the page changed
                    BrowserSettings.WindowIdsToURL[previousHandle].URL = BrowserDriver.Url;

                    BrowserSettings.CurrentURL = BrowserDriver.Url;
                }

                GetSetCurrentUrl();
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

                if (urlOrId is GPALUrl url)
                {
                    windowId = BrowserSettings.WindowIdsToURL.FirstOrDefault(x => x.Value.URL.Contains(url.Url) || url.Url.Contains(x.Value.URL)).Key;
                    BrowserSettings.CurrentGPALUrl = url;
                }
                else if (urlOrId is string urlStr)
                    windowId = BrowserSettings.WindowIdsToURL.FirstOrDefault(x => x.Value.URL.Contains(urlStr) || urlStr.Contains(x.Value.URL)).Key;

                if (true == string.IsNullOrEmpty(windowId))
                    window = MagicHelper.GoToWindow(urlOrId);
                else
                    window = MagicHelper.GoToWindow(windowId);

                foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                if (false == BrowserSettings.WindowIdsToURL.ContainsKey(window.WindowId))
                    BrowserSettings.WindowIdsToURL[window.WindowId] = new WindowInfo { URL = window.Url, ActiveWindow = true };
                else
                    BrowserSettings.WindowIdsToURL[window.WindowId].ActiveWindow = true;

                BrowserSettings.CurrentURL = window.Url;
            }
            else if (true == UsePuppeteer)
            {
                string windowId = null;
                WindowTuple window = null;

                if (urlOrId is GPALUrl url)
                {
                    windowId = BrowserSettings.WindowIdsToURL.FirstOrDefault(x => x.Value.URL.Contains(url.Url) || url.Url.Contains(x.Value.URL)).Key;
                    BrowserSettings.CurrentGPALUrl = url;
                }
                else if (urlOrId is string urlStr)
                    windowId = BrowserSettings.WindowIdsToURL.FirstOrDefault(x => x.Value.URL.Contains(urlStr) || urlStr.Contains(x.Value.URL)).Key;

                if (true == string.IsNullOrEmpty(windowId))
                    window = PuppeteerClient.GoToWindow(urlOrId).Execute<WindowTuple>();
                else if (true == Int32.TryParse(windowId, out int windowIdInt))
                    window = PuppeteerClient.GoToWindow(windowIdInt).Execute<WindowTuple>();

                foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                if (false == BrowserSettings.WindowIdsToURL.ContainsKey(window.WindowId))
                    BrowserSettings.WindowIdsToURL[window.WindowId] = new WindowInfo { URL = window.Url, ActiveWindow = true };
                else
                    BrowserSettings.WindowIdsToURL[window.WindowId].ActiveWindow = true;

                BrowserSettings.CurrentURL = window.Url;
            }
            else if (true == UseSelenium)
            {
                string theUrl = null;
                string targetHandle = null;

                if (urlOrId is GPALUrl url)
                    theUrl = url.Url;

                foreach (var handle in BrowserDriver.WindowHandles)
                {
                    BrowserDriver.SwitchTo().Window(handle);
                    if (urlOrId is string str && (handle == str || BrowserDriver.Url.Contains(str)))
                    {
                        targetHandle = handle;
                        break;
                    }
                }

                if (targetHandle != null)
                {
                    BrowserDriver.SwitchTo().Window(targetHandle);
                    BrowserSettings.CurrentURL = BrowserDriver.Url;
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to detect window with URL/ID [{urlOrId}]", this, GPALObjectType.Browser);
                }
            }

            GetSetCurrentUrl();
            return this;
        }

        /// <summary>
        /// Closes the current browser tab/window, or the one matching the given URL or window/tab id, and switches focus to the next available window.
        /// </summary>
        /// <param name="urlOrId">Optional <see cref="GPALUrl"/>, URL string, or window/tab id identifying the window to close. If null, closes the current window.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CloseWindow(dynamic urlOrId = null)
        {
            // NOTE: CAVEAT: we aren't currently using the id we planned for with ottomagic, but this will cause problems if we do
            urlOrId = MagicHelper.GetFullUrl(urlOrId, this, out bool _);

            InAction(false);

            BrowserSettings.CurrentGPALUrl = null;

            if (true == UseOttoMagic)
            {
                string windowIdToClose = null;

                if (urlOrId is GPALUrl url)
                {
                    if (false == string.IsNullOrEmpty(url.Url))
                        // Find by partial URL match
                        windowIdToClose = BrowserSettings.WindowIdsToURL
                            .FirstOrDefault(x => x.Value.URL.Contains(url.Url) || url.Url.Contains(x.Value.URL))  // NOTE: CAVEAT: is doing contains both ways safe?
                            .Key;
                }
                else if (urlOrId is string urlStr)
                {
                    if (false == string.IsNullOrEmpty(urlStr))
                        // Find by partial URL match
                        windowIdToClose = BrowserSettings.WindowIdsToURL
                            .FirstOrDefault(x => x.Value.URL.Contains(urlStr) || urlStr.Contains(x.Value.URL))  // NOTE: CAVEAT: is doing contains both ways safe?
                            .Key;
                }
                else
                {
                    // Default: close the currently active window
                    windowIdToClose = BrowserSettings.WindowIdsToURL
                        .FirstOrDefault(x => true == x.Value.ActiveWindow)
                        .Key;
                }

                if (string.IsNullOrEmpty(windowIdToClose))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"No window found to close (URL containing [{urlOrId}])", this, GPALObjectType.Browser);
                    return this;
                }

                // Let MagicHelper close it and return the new active window
                WindowTuple newActiveWindow = MagicHelper.CloseWindow(windowIdToClose);

                // Remove from tracking
                BrowserSettings.WindowIdsToURL.Remove(windowIdToClose);

                // Update active status for the new one (if any windows remain)
                if (newActiveWindow != null && !string.IsNullOrEmpty(newActiveWindow.WindowId))
                {
                    foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                        BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                    BrowserSettings.WindowIdsToURL[newActiveWindow.WindowId] = new WindowInfo
                    {
                        URL = newActiveWindow.Url,
                        ActiveWindow = true
                    };

                    BrowserSettings.CurrentURL = newActiveWindow.Url;
                }
                else if (BrowserSettings.WindowIdsToURL.Count == 0)
                {
                    BrowserSettings.CurrentURL = null;
                }
            }
            else if (true == UsePuppeteer)
            {
                // Puppeteer handles its own tracking and current URL update
                if (urlOrId is GPALUrl url)
                    PuppeteerClient.CloseWindow(url).Execute();
                else if (urlOrId is string urlStr)
                    PuppeteerClient.CloseWindow(urlStr).Execute();
                else if (urlOrId is int id)
                    PuppeteerClient.CloseWindow(id).Execute();
                else
                    PuppeteerClient.CloseWindow().Execute();
            }
            else // Selenium
            {
                string handleToClose = null;

                if (urlOrId is string urlStr)
                {
                    // Search by partial URL match
                    handleToClose = BrowserSettings.WindowIdsToURL
                        .FirstOrDefault(x => x.Value.URL.Contains(urlStr) || urlStr.Contains(x.Value.URL))
                        .Key;
                }
                else
                {
                    // Default: close the currently active window
                    handleToClose = BrowserSettings.WindowIdsToURL
                        .FirstOrDefault(x => x.Value.ActiveWindow)
                        .Key;

                    // Fallback to Selenium's current handle if not marked
                    if (string.IsNullOrEmpty(handleToClose))
                        handleToClose = BrowserDriver.CurrentWindowHandle;
                }

                if (string.IsNullOrEmpty(handleToClose) ||
                    !BrowserSettings.WindowIdsToURL.ContainsKey(handleToClose))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"No window found to close (URL containing [{urlOrId}])", this, GPALObjectType.Browser);
                    return this;
                }

                // Switch to the target window and close it
                BrowserDriver.SwitchTo().Window(handleToClose);
                BrowserDriver.Close(); // Closes the current window/tab

                // Remove from internal tracking
                BrowserSettings.WindowIdsToURL.Remove(handleToClose);

                // If no windows left
                if (BrowserDriver.WindowHandles.Count == 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No windows remain after closing.", this, GPALObjectType.Browser);
                    return this;
                }

                // Determine which window Selenium switched to automatically
                string newActiveHandle = BrowserDriver.CurrentWindowHandle;

                // Update active flags
                foreach (var key in BrowserSettings.WindowIdsToURL.Keys)
                    BrowserSettings.WindowIdsToURL[key].ActiveWindow = false;

                if (BrowserSettings.WindowIdsToURL.ContainsKey(newActiveHandle))
                {
                    BrowserSettings.WindowIdsToURL[newActiveHandle].ActiveWindow = true;
                    BrowserSettings.WindowIdsToURL[newActiveHandle].URL = BrowserDriver.Url; // refresh
                }
                else
                {
                    // Fallback: pick first remaining and mark active (shouldn't happen normally)
                    var firstRemaining = BrowserSettings.WindowIdsToURL.Keys.FirstOrDefault();
                    if (firstRemaining != null)
                    {
                        BrowserSettings.WindowIdsToURL[firstRemaining].ActiveWindow = true;
                        BrowserDriver.SwitchTo().Window(firstRemaining);
                    }
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Closed the window, now on [{UrlHelper.ForMessage(GetSetCurrentUrl())}].", this, GPALObjectType.Browser);

            return this;
        }

        #endregion Window Handling
        #region WithWorkFlow, Run, While, Until, Generated by Grok
        /// <summary>
        /// Runs the workflows added via <see cref="WithWorkflow"/> against this browser, in the order they were
        /// added. To give each workflow a browser of its own and run several at once, see <see cref="GPAL.Workflow"/>.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Run()
        {
            WorkflowManager.Run();
            return this;
        }
        /// <summary>
        /// Adds a workflow to run against this browser, used with <see cref="Run"/>, <see cref="While"/> and
        /// <see cref="Until(Selector)"/>. Workflows run in the order they were added, one after another, and the
        /// browser handed to each is this one.
        /// </summary>
        /// <remarks>
        /// Runs the workflows one at a time. For parallel workflows, each with its own browser, use
        /// <see cref="GPAL.Workflow"/>.
        /// </remarks>
        /// <example>
        /// Page through search results, gathering each page, until the Next button is no longer there:
        /// <code>
        /// browser
        ///     .GoTo("https://www.example.com/search?q=laptop")
        ///     .WithWorkflow(b =&gt;
        ///     {
        ///         b.WithSelector(GPAL.CssSelector(".result-title"))
        ///          .WithAllThatMatch()
        ///          .GetElements(out List&lt;GPALElement&gt; found);
        ///
        ///         results.AddRange(found);
        ///
        ///         b.WithSelector(GPAL.CssSelector("a.next")).LeftClick();
        ///     })
        ///     .Until(b =&gt; 0 == b.CurrentUOW.WebSelectorFoundResults.Count);
        /// </code>
        /// </example>
        /// <param name="workflow">The workflow to run. The browser passed in is this browser.</param>
        /// <returns>Fluent interface to add another workflow, or to run them</returns>
        public IAllowWorkflowExecution WithWorkflow(Action<IBrowser> workflow)
        {
            return WorkflowManager.WithWorkflow(workflow);
        }

        /// <summary>
        /// Sets the maximum amount of time a <see cref="While"/>/<see cref="Until"/> loop is allowed to run before it is aborted.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds. Must be greater than zero, otherwise the existing default is kept.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWorkflowExecution WhileLoopTimeout([Milliseconds] int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid timeout ([{timeoutMs}]ms) specified. Using default [{BrowserSettings.WhileLoopTimeoutMs}]ms.", this, GPALObjectType.Browser);
            }
            else
            {
                BrowserSettings.WhileLoopTimeoutMs = timeoutMs;
            }
            return this;
        }

        /// <summary>
        /// Sets the maximum number of iterations a <see cref="While"/>/<see cref="Until"/> loop is allowed to run before it is aborted.
        /// </summary>
        /// <param name="maxIterations">Maximum number of iterations. Must be greater than zero, otherwise the existing default is kept.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWorkflowExecution WhileLoopMaxIterations(int maxIterations)
        {
            if (maxIterations <= 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid max iterations ([{maxIterations}]) specified. Using default [{BrowserSettings.WhileLoopMaxIterations}].", this, GPALObjectType.Browser);
            }
            else
            {
                BrowserSettings.WhileLoopMaxIterations = maxIterations;
            }
            return this;
        }

        /// <summary>
        /// Repeats the workflow added via <see cref="WithWorkflow"/> for as long as the given predicate returns true,
        /// subject to <see cref="WhileLoopTimeout"/> and <see cref="WhileLoopMaxIterations"/>.
        /// </summary>
        /// <param name="predicate">A function evaluated against the <see cref="Browser"/> before each iteration; the loop continues while it returns true</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector While(Func<Browser, bool> predicate)
        {
            return WorkflowManager.While(predicate);
        }

        /// <summary>
        /// Repeats the workflow added via <see cref="WithWorkflow"/> until the given selector matches an element on the page,
        /// subject to <see cref="WhileLoopTimeout"/> and <see cref="WhileLoopMaxIterations"/>.
        /// </summary>
        /// <param name="selector">The <see cref="Selector"/> to wait for before stopping the loop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Until(Selector selector)
        {
            return WorkflowManager.Until(selector);
        }

        /// <summary>
        /// Repeats the workflow added via <see cref="WithWorkflow"/> until the given predicate returns true,
        /// subject to <see cref="WhileLoopTimeout"/> and <see cref="WhileLoopMaxIterations"/>.
        /// </summary>
        /// <param name="predicate">A function evaluated against the <see cref="Browser"/> after each iteration; the loop stops once it returns true</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Until(Func<Browser, bool> predicate)
        {
            return WorkflowManager.Until(predicate);
        }
        #endregion WithWorkFlow, Run, While, Until, Generated by Grok
        #region OCR
        #endregion OCR
        #region Getters/Setters
        /// <summary>
        /// The lines of the current site's robots.txt, as parsed by <see cref="WithRespectRobotMetaTags"/>/<see cref="WithObeyRobotsTxt"/>.
        /// </summary>
        public List<string> RobotsTxt
        {
            get => BrowserSettings.RobotsTxt;
            internal set => BrowserSettings.RobotsTxt = value;
        }
        /// <summary>
        /// The result object returned by the most recent <see cref="ExecuteJavaScriptObj"/> call.
        /// </summary>
        public object JavaScriptResultObj
        {
            get => BrowserSettings.JavaScriptResultObj;
            internal set => BrowserSettings.JavaScriptResultObj = value;
        }
        /// <summary>
        /// The result string returned by the most recent <see cref="ExecuteJavaScriptStr"/> call.
        /// </summary>
        public string JavaScriptResultStr
        {
            get => BrowserSettings.JavaScriptResultStr;
            internal set => BrowserSettings.JavaScriptResultStr = value;
        }
        /// <summary>
        /// The version of the browser being automated.
        /// </summary>
        public string BrowserVersion
        {
            get => BrowserSettings.Version;
            internal set => BrowserSettings.Version = value;
        }

        #endregion
        #region Helpers
        /// <summary>
        /// Converts a <see cref="ModifierKeys"/> flags value into a comma-separated, human-readable string of the set flags.
        /// </summary>
        /// <param name="modifierKeys">The modifier key flags to convert</param>
        /// <returns>"None" if no flags are set, otherwise a comma-separated list of the set flag names</returns>
        internal static string GetModifierKeys(ModifierKeys modifierKeys)
        {
            if (modifierKeys == ModifierKeys.NONE)
                return "None";

            var setFlags = Enum.GetValues(typeof(ModifierKeys))
                .Cast<ModifierKeys>()
                .Where(flag => flag != ModifierKeys.NONE && modifierKeys.HasFlag(flag))
                .Select(flag => flag.ToString());

            return string.Join(", ", setFlags);
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
                    // read the url of whatever tab Selenium currently has active - do NOT switch by our own
                    // CurrentTabIdx, which if stale would switch to the wrong tab and read the wrong url
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
        /// <summary>
        /// Applies the configured <see cref="BrowserSettings.Referrer"/> to the navigation GoTo is about to make, as
        /// that page's referrer only. Selenium and Puppeteer send it with the navigation; OttoMagic sets the header
        /// for that one request. If the referrer is set to an empty string, clears it instead; nothing outlives a
        /// navigation, so there is nothing in the browser to take back.
        /// </summary>
        internal void SetReferrer()
        {
            string message = $"[{BrowserSettings.Referrer}] referrer applied.";

            if (null != BrowserSettings.Referrer)
            {
                bool applied = true;

                // clear it, then ignore it.
                if (true == string.Empty.Equals(BrowserSettings.Referrer))
                {
                    BrowserSettings.Referrer = null;
                    message = $"Cleared referrer.";
                }
                else if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.OverrideReferrer(BrowserSettings.Referrer);
                else if (true == UsePuppeteer)
                    PuppeteerClient.OverrideReferrer(BrowserSettings.Referrer).Execute();
                else if (BrowserType.FireFox == BrowserSettings.BrowserType)
                    applied = false;
                // Selenium: SeleniumGoToUrl sends it with the navigation

                if (true == applied)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message, this, GPALObjectType.Browser);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot set a header, so the referrer was not applied", this, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Applies the configured <see cref="BrowserSettings.OverrideUserAgent"/> as the browser's user agent for subsequent requests.
        /// If the user agent is set to an empty string, clears the override instead.
        /// </summary>
        internal void SetUserAgent()
        {
            string message = $"[{BrowserSettings.OverrideUserAgent}] user agent applied.";
            var parms = new Dictionary<string, object> { { "userAgent", BrowserSettings.OverrideUserAgent } };

            if (null != BrowserSettings.OverrideUserAgent)
            {
                bool applied = true;

                if (true == UsePuppeteer)
                    PuppeteerClient.SetUserAgent(BrowserSettings.OverrideUserAgent).Execute();
                else if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.SetUserAgent(BrowserSettings.OverrideUserAgent);
                else if (BrowserType.FireFox != BrowserSettings.BrowserType)
                    BrowserHelper.ExecuteCdp(BrowserDriver, "Network.setUserAgentOverride", parms);
                else
                    applied = false;

                // clear it, then ignore it.
                if (true == string.Empty.Equals(BrowserSettings.OverrideUserAgent))
                {
                    BrowserSettings.OverrideUserAgent = null;
                    message = $"Cleared user agent override.";
                }

                if (true == applied)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message, this, GPALObjectType.Browser);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot set a user agent, so the override was not applied", this, GPALObjectType.Browser);
            }
        }
        #endregion Helpers
        #region Sitep Operations
        /// <summary>
        /// Extracts Next.js hydration data (e.g. __NEXT_DATA__) embedded in the current page's HTML.
        /// </summary>
        /// <param name="hydrationResult">Receives the extracted <see cref="NextJsHydrationResult"/></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetHydratedData(out NextJsHydrationResult hydrationResult)
        {
            hydrationResult = CachedHydration();
            return this;
        }

        /// <summary>
        /// Returns the hydration result for the current page, extracting it only on a cache miss so
        /// GetHydratedData followed by SaveHydratedData does not fetch and parse the page twice.
        /// </summary>
        private NextJsHydrationResult CachedHydration()
        {
            string key = GetSetCurrentUrl();

            if (false == string.IsNullOrEmpty(key) && true == _hydrationCache.TryGetValue(key, out NextJsHydrationResult cached))
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Hydrated data cache hit for [{UrlHelper.GetOrigin(CurrentUrl)}]", this, GPALObjectType.Browser);
                return cached;
            }

            // Read the hydration data from the document, without parsing HTML.
            NextJsHydrationResult result = NextJsHydrationExtractor.ExtractFromScripts(this, GetHydrationScripts());

            if (false == string.IsNullOrEmpty(key))
                _hydrationCache[key] = result;

            return result;
        }
        /// <summary>
        /// Returns the current page source for hydration extraction: the raw engine source with only the
        /// transport JSON-string escaping peeled off (so OttoMagic's wrapper is removed) and control characters
        /// stripped. Unlike <see cref="GetPageSource(out string)"/> it does NOT run the lenient unescape or
        /// HtmlDecode, because those collapse the <c>\"</c> inside embedded JSON such as <c>__NEXT_DATA__</c> and
        /// leave it unparseable. The peel stops as soon as real structural quotes appear, so the page's own
        /// escaped JSON is preserved intact and valid.
        /// </summary>
        /// <summary>
        /// The page title, __NEXT_DATA__ payload and React Flight chunks as one json string.<br/>
        /// No HTML is sent, so there is no escaping to undo. OttoMagic uses its own endpoint, so it does not need
        /// Allow user scripts.
        /// </summary>
        /// <returns>Json holding title, nextData and flightChunks, or empty when the page could not answer</returns>
        private string GetHydrationScripts()
        {
            string reported;

            if (true == UseOttoMagic)
                reported = MagicHelper.GetHydrationScripts();
            else if (true == UsePuppeteer)
                // Runtime.evaluate takes an expression, so the function is self-invoking
                reported = BrowserHelper.ExecuteJavaScriptObj(NextJsHydrationExtractor.HydrationScript, this)?.ToString();
            else
                // ExecuteScript takes a statement list and needs a return
                reported = BrowserHelper.ExecuteJavaScriptObj("return " + NextJsHydrationExtractor.HydrationScript + ";", this)?.ToString();

            return reported ?? string.Empty;
        }
        /// <summary>
        /// Extracts Next.js hydration data (e.g. __NEXT_DATA__) from the current page and saves it to a file.
        /// </summary>
        /// <param name="hydrationFile">The <see cref="GPALFile"/> to save the extracted hydration data to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveHydratedData(GPALFile hydrationFile)
        {
            NextJsHydrationExtractor.SaveToFile(CachedHydration(), hydrationFile.Filename);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved hydrated data [{hydrationFile.Filename}]", this, GPALObjectType.Browser);

            return this;
        }
        // The rule set WithDigestRules named; null picks one from the page's url
        private string _digestRules = null;

        /// <summary>
        /// Names the rule set from LLMDigestRules.yaml that <see cref="GetLLMDigest"/> and <see cref="SaveLLMDigest"/>
        /// use from here on, instead of the one picked from the page's url. Null goes back to picking it.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.WithDigestRules("generic")
        ///            .GetLLMDigest(out LLMDigestResult digest)
        ///            .SaveLLMDigest(digestFile);
        /// </code>
        /// </example>
        /// <param name="ruleSetName">The rule set's name, or null to pick one from the page's url</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithDigestRules(string ruleSetName)
        {
            _digestRules = ruleSetName;
            return this;
        }

        /// <summary>
        /// Converts the current page's HTML into an LLM-optimized markdown digest, with the rule set
        /// <see cref="WithDigestRules"/> named, or one picked from the page's url (falls back to "generic").
        /// </summary>
        /// <param name="digestResult">Receives the generated <see cref="LLMDigestResult"/></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetLLMDigest(out LLMDigestResult digestResult)
        {
            digestResult = CachedDigest(_digestRules);
            return this;
        }

        /// <summary>
        /// Returns the LLM digest for the current page (and rule set), building it only on a cache miss so
        /// GetLLMDigest followed by SaveLLMDigest does not fetch and convert the page twice.
        /// </summary>
        private LLMDigestResult CachedDigest(string ruleSetName)
        {
            string key = GetSetCurrentUrl() + "|" + (ruleSetName ?? string.Empty);

            if (true == _digestCache.TryGetValue(key, out LLMDigestResult cached))
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"LLM Digest cache hit for [{UrlHelper.GetOrigin(CurrentUrl)}]", this, GPALObjectType.Browser);
                return cached;
            }

            GetPageSource(out string pageSource);
            LLMDigestResult digestResult = HtmlToOptimizedMarkdown.CreateDigest(this, pageSource, ruleSetName);
            _digestCache[key] = digestResult;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"LLM Digest created for [{UrlHelper.GetOrigin(CurrentUrl)}]", this, GPALObjectType.Browser);

            return digestResult;
        }
        /// <summary>
        /// Converts the current page's HTML into an LLM-optimized markdown digest and saves it to a file, with the
        /// rule set <see cref="WithDigestRules"/> named, or one picked from the page's url (falls back to "generic").
        /// </summary>
        /// <param name="llmDigestFile">The <see cref="GPALFile"/> to save the digest to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveLLMDigest(GPALFile llmDigestFile)
        {
            HtmlToOptimizedMarkdown.SaveToFile(CachedDigest(_digestRules), llmDigestFile.Filename);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved LLM Digest [{llmDigestFile.Filename}]", this, GPALObjectType.Browser);

            return this;
        }
        /// <summary>
        /// Gets the current page's HTML source, cleaned of escape sequences and control characters.
        /// </summary>
        /// <param name="pageSource">Receives the cleaned page source HTML</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetPageSource(out string pageSource)
        {
            pageSource = string.Empty;

            if (true == UseOttoMagic)
            {
                pageSource = MagicHelper.GetPageSource();
            }
            else if (true == UsePuppeteer)
            {
                pageSource = PuppeteerClient.GetPageSource().Execute();
            }
            else
            {
                try
                {
                    pageSource = BrowserDriver.PageSource;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        "Failed to read page source in Selenium [" + ex.Message + "]",
                        this,
                        GPALObjectType.Browser);
                }
            }

            pageSource = NormalizePageSource(pageSource);

            Uri currUrl = new Uri(CurrentUrl, UriKind.Absolute);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Retrieved page source for [{UrlHelper.NormalizePath(currUrl.AbsolutePath)}]", this, GPALObjectType.Browser);

            return this;
        }
        /// <summary>
        /// Converts any engine's raw page source to the page's real source: cleaned HTML for a normal page. For a
        /// file such as XML, Chrome's XML viewer wraps it, so this returns the original text the viewer keeps in a
        /// hidden div.
        /// </summary>
        /// <param name="raw">The raw page source as returned by the active engine (OttoMagic, Puppeteer, or Selenium)</param>
        /// <returns>The normalized page source, or an empty string if <paramref name="raw"/> is null/whitespace</returns>
        private static string NormalizePageSource(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            // JS/JSON-unescape first so the XML-viewer marker (real quotes) is detectable
            // regardless of how the engine transported the source (e.g. OttoMagic returns it escaped).
            string working = SafeJsUnescape(raw.Trim().Trim('\"').Trim('\''));

            if (working.IndexOf("webkit-xml-viewer-source-xml", StringComparison.Ordinal) != -1)
            {
                string fileSource = ConverterHelper.ExtractRawXmlFromBrowserSource(working);
                if (!string.IsNullOrWhiteSpace(fileSource))
                {
                    // Strip control characters only; do NOT HTML-decode, that would corrupt escaped
                    // entities (e.g. &amp;) that are a genuine part of the recovered file source.
                    return Regex.Replace(fileSource.Trim(), @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
                }
            }

            // Ordinary page: return the honest page source with the standard cleanup.
            return SafeCleanPageSource(raw);
        }
        /// <summary>
        /// Trims, HTML-decodes, JS-unescapes, and strips control characters from raw page source.
        /// </summary>
        /// <param name="input">The raw page source to clean</param>
        /// <returns>The cleaned page source, or an empty string if <paramref name="input"/> is null/whitespace</returns>
        private static string SafeCleanPageSource(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string working = input.Trim().Trim('\"').Trim('\'');

            // Peel the transport's JSON-string escaping FIRST. OttoMagic sends outerHTML as a JSON string
            // (leading quote, \" for attribute quotes, \n for newlines); HtmlDecode must run AFTER this,
            // because decoding entities injects raw quotes that make the still-escaped payload un-peelable.
            working = SafeJsUnescape(working);

            working = System.Web.HttpUtility.HtmlDecode(working);

            // Remove control characters only
            working = Regex.Replace(working, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

            return working.Trim();
        }

        /// <summary>
        /// Unescapes common JS/JSON string escape sequences (quotes, backslashes, unicode) while preserving newline/carriage-return escapes.
        /// </summary>
        /// <param name="input">The escaped string to unescape</param>
        /// <returns>The unescaped string, or <paramref name="input"/> unchanged if null/empty</returns>
        private static string SafeJsUnescape(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Step 1: OttoMagic's native messaging hands the source over as one JSON string (leading quote, \"
            // for attribute quotes, \n for newlines), so that one layer is decoded with a real parser. Selenium
            // and Puppeteer return clean HTML, which is not a JSON string, so the decode leaves it as it is.
            string result = TryJsonStringDecode(input) ?? input;

            // Step 2: ALWAYS run the lenient character-level unescape on the result, so every engine converges
            // on identical output. Without this, OttoMagic (whose peel succeeds) would keep the single-level \"
            // left in the page's own embedded strings (e.g. __NEXT_DATA__ JSON), while Selenium/Puppeteer
            // (whose peel is a no-op) collapse those \" here - producing different source for the same page.

            // Basic escapes - but PROTECT newlines and carriage returns so they stay escaped for JSON
            result = result.Replace(@"\""", "\"")
                           .Replace(@"\'", "'")
                           .Replace(@"\\", "\\")     // keep single backslash
                           .Replace(@"\/", "/");

            // Safe unicode only
            result = Regex.Replace(result, @"\\u([0-9a-fA-F]{4})", m =>
            {
                try
                {
                    int code = int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber);
                    if (code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
                        return m.Value;        // keep original if invalid
                    return char.ConvertFromUtf32(code);
                }
                catch
                {
                    return m.Value;
                }
            });

            // Remove unknown escapes (but protect " \ ' \ / \b \f \t)
            result = Regex.Replace(result, @"\\([^""'\\/bfnrtu])", "$1");

            return result;
        }

        /// <summary>
        /// Treats the argument as the body of a JSON string literal and unescapes exactly one layer using a
        /// real JSON parser (correctly handling \\, \", \/, \uXXXX and friends). Returns null when the text is
        /// not a well-formed single JSON string, for example clean HTML holding a raw quote or newline.
        /// </summary>
        /// <param name="s">The candidate JSON-string body (without the surrounding quotes)</param>
        /// <returns>The one-layer-unescaped string, or null if it is not a valid JSON string token</returns>
        private static string TryJsonStringDecode(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;

            try
            {
                return JsonConvert.DeserializeObject("\"" + s + "\"") as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the raw XML/source of the current page, intended for use when the current page is itself a sitemap.
        /// </summary>
        /// <param name="sitemapXML">Receives the page source</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetSiteMap(out string sitemapXML)
        {
            GetPageSource(out sitemapXML);
            return this;
        }

        Dictionary<string, List<string>> siteMaps = new Dictionary<string, List<string>>();

        // Result caches so a Save* reuses what a preceding Get* already produced (and so we do not repeatedly
        // walk the same site). The sitemap is site-wide, keyed by origin; the digest/hydration results are
        // page-level, keyed by the full URL. Instance-scoped, so each browser/engine still does the work once;
        // call ClearResultCache() to force a refresh.
        private readonly Dictionary<string, List<string>> _sitemapCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LLMDigestResult> _digestCache = new Dictionary<string, LLMDigestResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NextJsHydrationResult> _hydrationCache = new Dictionary<string, NextJsHydrationResult>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The sitemap text read by the last <see cref="GetSiteMapUrls"/>, to tell an index from a urlset or an html
        /// page without reloading.<br/>
        /// Empty when served from cache; only urls are cached, since sitemaps can be megabytes.
        /// </summary>
        public string SiteMapSource { get; private set; } = string.Empty;

        /// <summary>
        /// Column names the last GetGrid read from a table's header row.<br/>
        /// Empty for column-per-selector reads or a table without headers; a saved file then uses selector names,
        /// then generated ones.
        /// </summary>
        public IReadOnlyList<string> ColumnNames { get; private set; } = new List<string>();

        // Why a sitemap walk found no urls, keyed like the sitemap cache. SaveSiteMapUrls writes it into the file.
        private readonly Dictionary<string, SitemapNote> _sitemapNotes = new Dictionary<string, SitemapNote>(StringComparer.OrdinalIgnoreCase);

        // Why the last walk found no urls; null when it found some.
        private SitemapNote _lastWalkNote = null;

        // Result code and message written when no urls were found.
        private class SitemapNote
        {
            internal string Result { get; set; }
            internal string Message { get; set; }
        }

        /// <summary>
        /// Clears the cached sitemap / LLM digest / hydration results for this browser, forcing the next
        /// Get*/Save* to re-walk or re-fetch instead of reusing an earlier result.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearResultCache()
        {
            _sitemapCache.Clear();
            _sitemapNotes.Clear();
            _digestCache.Clear();
            _hydrationCache.Clear();
            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Cleared sitemap/digest/hydration result cache.", this, GPALObjectType.Browser);
            return this;
        }

        // The sitemap WithSitemap named for the next GetSiteMapUrls or SaveSiteMapUrls; null reads from the current page
        private string _namedSitemap = null;

        /// <summary>
        /// Names the sitemap the next <see cref="GetSiteMapUrls"/> or <see cref="SaveSiteMapUrls"/> reads, xml or
        /// html. It is read without going to it, so a gzipped sitemap, which the browser would only download, is
        /// fetched and inflated. Used for walking from one sitemap to the next; to start from a page, GoTo it and
        /// leave this out.<br/><br/>
        /// A sitemap the browser has to go to (one on another origin, or an html sitemap built by script) leaves the
        /// browser on it. GoTo the page again when the walk is done.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.WithSitemap("https://www.walmart.com/sitemap_store_main_other.xml.gz")
        ///            .GetSiteMapUrls(out List&lt;string&gt; urls);
        /// </code>
        /// </example>
        /// <param name="sitemapUrl">The sitemap to read</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithSitemap(GPALUrl sitemapUrl)
        {
            _namedSitemap = sitemapUrl?.Url;
            return this;
        }

        /// <summary>
        /// Returns the urls one sitemap lists, child sitemaps and pages together. The sitemap is the one
        /// <see cref="WithSitemap"/> named; without it, the page the browser is on when that page is a sitemap,
        /// otherwise the sitemaps the site's robots.txt lists, otherwise its /sitemap.xml. The browser is back on
        /// the page it started on afterwards.
        /// </summary>
        /// <param name="sitemapUrls">Receives the urls the sitemap lists</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetSiteMapUrls(out List<string> sitemapUrls)
        {
            sitemapUrls = CachedSitemapUrls(_namedSitemap);

            // a name applies to one read
            _namedSitemap = null;

            return this;
        }

        /// <summary>
        /// Returns the sitemap URLs for the current origin, walking the site only on a cache miss so repeated
        /// calls (e.g. GetSiteMapUrls followed by SaveSiteMapUrls) do not re-crawl.
        /// </summary>
        private List<string> CachedSitemapUrls(string sitemapUrl = null)
        {
            // Cache by sitemap url (or origin when none is given), since a site has many sitemaps.
            string key = false == string.IsNullOrWhiteSpace(sitemapUrl) ? sitemapUrl : UrlHelper.GetOrigin(CurrentUrl);

            if (false == string.IsNullOrEmpty(key) && true == _sitemapCache.TryGetValue(key, out List<string> cached))
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Sitemap cache hit for [{key}] ([{cached.Count}] URLs).", this, GPALObjectType.Browser);

                // Restore the saved reason for an empty cached result too.
                _sitemapNotes.TryGetValue(key, out _lastWalkNote);

                // nothing was read, so there is no text to answer with
                SiteMapSource = string.Empty;

                return cached;
            }

            List<string> walked = WalkSitemapUrls(sitemapUrl);

            if (false == string.IsNullOrEmpty(key))
            {
                _sitemapCache[key] = walked;

                if (null == _lastWalkNote)
                    _sitemapNotes.Remove(key);
                else
                    _sitemapNotes[key] = _lastWalkNote;
            }

            return walked;
        }

        /// <summary>
        /// Reads a sitemap with a fetch from inside the page, falling back to navigating.<br/>
        /// A fetch is one request instead of a load, render and source read, and carries the browser's cookies,
        /// clearance and TLS.<br/>
        /// Navigation is used when the fetch cannot be made (a sitemap on another origin) or the result is not a
        /// sitemap (an html sitemap built by script).
        /// </summary>
        /// <param name="url">The sitemap to read</param>
        /// <returns>The sitemap text, or what the browser rendered when the fetch could not deliver</returns>
        private string ReadSitemap(string url)
        {
            string retVal = string.Empty;

            // Already on this sitemap: read the current page source. OttoMagic cannot fetch from an xml document,
            // and navigating would reload it.
            bool alreadyThere = true == UrlHelper.AreEquivalent(CurrentUrl, url);

            // Fetch only same-origin sitemaps; the browser blocks others, which would only add a failure before
            // navigating. Compare origins exactly, not with UrlHelper.AreEquivalent: www.harrods.com and
            // harrods.com differ (Harrods lists 422 sitemaps on harrods.com from www.harrods.com).
            // OttoMagic also cannot fetch while showing a rendered document such as robots.txt or a sitemap, where
            // no content script runs ("Failed to fetch"). Selenium and Puppeteer fetch in the document itself and
            // lack this limit.
            string onNow = (CurrentUrl ?? string.Empty).Split('?', '#')[0].TrimEnd('/').ToLower();
            bool onADocument = true == UseOttoMagic
                            && (true == onNow.EndsWith(".xml") || true == onNow.EndsWith(".txt") || true == onNow.EndsWith(".gz"));

            bool sameOrigin = false == alreadyThere
                           && false == onADocument
                           && true == string.Equals(UrlHelper.GetOrigin(CurrentUrl), UrlHelper.GetOrigin(url), StringComparison.OrdinalIgnoreCase);

            byte[] fetched = true == sameOrigin
                           ? BrowserHelper.FetchBytes(this, url, null, out string _)
                           : null;

            // The fetch sets ServerResponseCode like a navigation. Use the body only on 2xx; anything else is a
            // challenge or error page.
            bool answered = true == sameOrigin && 200 <= ServerResponseCode && 300 > ServerResponseCode;

            if (true == answered && null != fetched && 0 < fetched.Length)
                retVal = System.Text.Encoding.UTF8.GetString(fetched);

            // Check for <urlset or <sitemapindex first, so a large sitemap is not parsed twice. The heuristic is
            // for html sitemaps.
            bool looksLikeASitemap = false == string.IsNullOrWhiteSpace(retVal)
                                  && (true == retVal.Contains("<urlset")
                                      || true == retVal.Contains("<sitemapindex")
                                      || true == SitemapExtractor.IsValidSitemapContent(retVal));

            // Use the fetched text only if it looks like a sitemap; otherwise (error, challenge page, script-built
            // html sitemap) navigate and read the page source.
            if (true == alreadyThere)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The browser is already on [{url}], so its page is the sitemap", this, GPALObjectType.Browser);

                GetPageSource(out retVal);
            }
            else if (false == looksLikeASitemap)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, true == sameOrigin
                                                           ? $"The page did not hand over [{url}], HTTP [{ServerResponseCode}], so the browser goes there to read it"
                                                           : true == onADocument
                                                             ? $"The browser is on [{CurrentUrl}], a document rather than a page, which the extension cannot fetch from, so it goes to [{url}] instead"
                                                             : $"[{url}] is not on the origin the page is on, which a page may not fetch, so the browser goes there to read it",
                                        this, GPALObjectType.Browser);

                GoTo(url);
                GetPageSource(out retVal);
            }

            return retVal;
        }

        private List<string> WalkSitemapUrls(string sitemapUrl = null)
        {
            // Save the current url to return to. A named sitemap is read without visiting, and navigating to a .gz
            // aborts.
            string startedOn = CurrentUrl;

            bool named = false == string.IsNullOrWhiteSpace(sitemapUrl);

            // what the walk is about, for its messages: the named sitemap, or the page the browser is on
            string currentUrl = true == named ? sitemapUrl : startedOn;
            SitemapExtractor sme = new SitemapExtractor(this);
            IGPALGrid<string> retGrid = new GPALGrid<string>();
            List<string> potentialSitemapUrls = new List<string>();
            string pageSource = string.Empty;

            // a sitemap was read, even if it listed nothing
            bool readASitemap = false;

            _lastWalkNote = null;

            if (true == named)
                potentialSitemapUrls.Add(sitemapUrl);
            else
            {
                // The page the browser is on comes first. Only when it is not a sitemap is robots.txt asked, and
                // sitemap.xml after that.
                pageSource = ReadSitemap(startedOn);

                if (true == SitemapExtractor.IsValidSitemapContent(pageSource))
                {
                    retGrid.AddRow(sme.ExtractSitemapUrls(pageSource, Int32.MaxValue, out bool pageIsSitemap));
                    readASitemap = pageIsSitemap;
                }
                else
                    potentialSitemapUrls = sme.GetSitemapUrl(startedOn, out bool _);
            }

            foreach (string potentialSitemapUrl in potentialSitemapUrls)
            {
                // A gzipped sitemap is a binary download to the browser, so it is fetched and inflated instead
                pageSource = true == SitemapExtractor.IsGzipSitemapUrl(potentialSitemapUrl)
                           ? SitemapExtractor.FetchAndInflateGzipSitemap(potentialSitemapUrl, this)
                           : ReadSitemap(potentialSitemapUrl);

                // A bot-check interstitial (e.g. PerimeterX "Robot or human?") answered instead of the sitemap, so the
                // walk stops rather than reading the challenge page as one.
                if (true == SitemapExtractor.IsBotCheckPage(pageSource))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Bot-check interstitial detected at [{potentialSitemapUrl}], skipping sitemap walk.", this, GPALObjectType.Browser);

                    _lastWalkNote = new SitemapNote
                    {
                        Result = "bot-check-page",
                        Message = $"A bot-check interstitial answered for [{potentialSitemapUrl}] instead of a sitemap, so the walk stopped there and no urls were read. Run this one on a signed-in profile, or read the sitemap the site publishes by name."
                    };

                    break;
                }

                retGrid.AddRow(sme.ExtractSitemapUrls(pageSource, Int32.MaxValue, out bool wasSitemap));   // NOTE: CAVEAT: hardcoded value - we want them all
                readASitemap |= wasSitemap;
            }

            // A read from the page goes back to the page when it had to go to a sitemap. A named sitemap stays where
            // it was read, so a walk from one sitemap to the next goes back once, when it is done, not after every
            // sitemap.
            if (false == named)
            {
                string onNow = true == UseOttoMagic ? MagicHelper.GetCurrentUrl()
                             : true == BrowserSettings.UsePuppeteer ? PuppeteerClient.GetCurrentUrl().Execute()
                             : BrowserDriver.Url;

                if (false == UrlHelper.AreEquivalent(startedOn, onNow.Trim('\"')))
                    GoTo(startedOn);
            }

            List<string> sitemapUrls = retGrid.ToList();

            // Save the text in SiteMapSource, so the caller can tell the sitemap type without reloading.
            SiteMapSource = pageSource ?? string.Empty;

            // A sitemap that lists nothing is an answer, not a failure: said as INFO, and the reason kept for
            // SaveSiteMapUrls. Skipped if the bot-check set a reason.
            if (0 == sitemapUrls.Count && null == _lastWalkNote && true == readASitemap)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{currentUrl}] is a sitemap that lists no urls", this, GPALObjectType.Browser);

                _lastWalkNote = new SitemapNote
                {
                    Result = "empty-sitemap",
                    Message = $"[{currentUrl}] is a sitemap but lists no urls."
                };
            }

            // No urls and no sitemap read: warn with the url and sitemaps tried, and save the reason for
            // SaveSiteMapUrls. Skipped if the bot-check set a reason.
            if (0 == sitemapUrls.Count && null == _lastWalkNote)
            {
                string tried = true == named
                             ? sitemapUrl
                             : 0 == potentialSitemapUrls.Count
                               ? "nothing: the page is not a sitemap and robots.txt does not allow reading the site"
                               : $"the page, then {string.Join(", ", potentialSitemapUrls)}";

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No sitemap URLs found for [{currentUrl}], tried [{tried}]", this, GPALObjectType.Browser);

                _lastWalkNote = new SitemapNote
                {
                    Result = "no-sitemap-found",
                    Message = $"No sitemap was found for [{currentUrl}], so no urls were walked. Tried [{tried}]."
                };
            }

            return sitemapUrls;
        }
        /// <summary>
        /// Discovers and walks the site's sitemap(s), then saves the extracted URLs to a file. The output format
        /// (csv, txt, json, yaml, xml, ...) is chosen from the file extension by the converter, so one call turns
        /// a crawl into whatever downstream format you need.
        /// </summary>
        /// <param name="sitemapUrlsFile">The <see cref="GPALFile"/> to save the sitemap URLs to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveSiteMapUrls(GPALFile sitemapUrlsFile)
        {
            List<string> sitemapUrls = CachedSitemapUrls(_namedSitemap);

            // a name applies to one read
            _namedSitemap = null;

            // one URL per row, single "url" column, so the converter can emit any format from the file extension
            IGPALGrid<string> grid = new GPALGrid<string>();

            if (0 == sitemapUrls.Count)
            {
                // No urls: write a result entry with the reason instead of an empty file, in the format the file
                // extension selects.
                SitemapNote note = _lastWalkNote ?? new SitemapNote
                {
                    Result = "no-sitemap-found",
                    Message = $"No sitemap was found for [{UrlHelper.GetOrigin(CurrentUrl)}], so there are no urls to write."
                };

                grid.AddRow(new List<string> { note.Result, note.Message });

                GPAL.Converter
                    .WithInput(grid)
                    .WithColumnName("result")
                    .WithColumnName("message")
                    .SaveTo(sitemapUrlsFile);

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No sitemap URLs to save, so [{sitemapUrlsFile.Filename}] says [{note.Result}] instead", this, GPALObjectType.Browser);
            }
            else
            {
                foreach (string url in sitemapUrls)
                    grid.AddRow(new List<string> { url });

                GPAL.Converter
                    .WithInput(grid)
                    .WithColumnName("url")
                    .SaveTo(sitemapUrlsFile);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved [{sitemapUrls.Count}] sitemap URLs to [{sitemapUrlsFile.Filename}]", this, GPALObjectType.Browser);
            }

            return this;
        }
        #endregion Sitemap Operations
        #region Cast Operations
        /// <summary>
        /// Name of the cast sink (e.g. Chromecast device) currently being cast to, if any.
        /// </summary>
        private string SinkName { get; set; }

        /// <summary>
        /// Casts (mirrors) the current browser tab to the named cast sink (e.g. a Chromecast device).<br/>
        /// Supported via Puppeteer and Selenium; not available via OttoMagic.
        /// </summary>
        /// <param name="sinkName">Name of the cast sink to cast to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CastTab(string sinkName)
        {
            SinkName = sinkName;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Casting Tab to [{sinkName}]");

            if (true == UsePuppeteer)
                PuppeteerClient.CastTab(sinkName).Execute();
            else if (true == UseSelenium)
                CastTo_Selenium(sinkName, CastMode.Tab);
            else if (true == UseOttoMagic)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Casting is not available via OttoMagic");

            return this;
        }

        /// <summary>
        /// Casts (mirrors) the entire desktop to the named cast sink (e.g. a Chromecast device).<br/>
        /// Supported via Puppeteer and Selenium; not available via OttoMagic.
        /// </summary>
        /// <param name="sinkName">Name of the cast sink to cast to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CastDesktop(string sinkName)
        {
            SinkName = sinkName;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Casting Desktop to [{sinkName}]");

            if (true == UsePuppeteer)
                PuppeteerClient.CastDesktop(sinkName).Execute();
            else if (true == UseSelenium)
                CastTo_Selenium(sinkName, CastMode.Desktop);
            else if (true == UseOttoMagic)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Casting is not available via OttoMagic");

            return this;
        }

        /// <summary>
        /// Stops any active cast (tab or desktop mirroring) started via <see cref="CastTab"/> or <see cref="CastDesktop"/>.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector StopCasting()
        {
            if (false == string.IsNullOrEmpty(SinkName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Stopping cast to [{SinkName}]");

                if (true == UsePuppeteer)
                {
                    if (null != PuppeteerCommunicator.SinkName)
                    {
                        PuppeteerClient.StopCasting().Execute();
                        PuppeteerCommunicator.SinkName = null;
                    }
                }
                else if (true == UseSelenium)
                {

                    var param = new Dictionary<string, object> { ["sinkName"] = SinkName };

                    if (null != SinkName)
                        try
                        {
                            // 1. Try official stop command
                            BrowserHelper.ExecuteCdp(BrowserDriver, "Cast.stopCasting", param);
                            SinkName = null;
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Cast.stopCasting failed, which it does sometimes", this, GPALObjectType.Browser, ex);
                        }
                }
            }

            return this;
        }

        /// <summary>
        /// Starts tab or desktop mirroring to the named cast sink via Chrome DevTools Protocol Cast commands, retrying until the sink is found.
        /// </summary>
        /// <param name="sinkName">Name of the cast sink to cast to</param>
        /// <param name="mode">Whether to mirror the current tab or the entire desktop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        private IAllowBrowserActionOrAnySelector CastTo_Selenium(string sinkName, CastMode mode)
        {
            int retries = 10;

            if (string.IsNullOrEmpty(sinkName))
                return this;

            BrowserHelper.ExecuteCdp(BrowserDriver, "Cast.enable", 
                new Dictionary<string, object>());

            Thread.Sleep(3_000);
            var param = new Dictionary<string, object> { ["sinkName"] = sinkName };

            string command = (mode == CastMode.Tab)
                ? "Cast.startTabMirroring"
                : "Cast.startDesktopMirroring";

            while (0 < retries)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{sinkName}].");
                try
                {
                    BrowserHelper.ExecuteCdp(BrowserDriver, command, param);

                    GPAL.PublishSimpleEvent(GPALEventType.INFO,
                        $"[{sinkName}] found.", this, GPALObjectType.Browser);
                    break;
                }
                catch
                {
                    retries--;
                }
                Thread.Sleep(3_000);
            }
            return this;
        }
        #endregion Cast Operations
    }
}
