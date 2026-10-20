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
    public class BrowserHelper
    {
        static bool alreadyUpdating = false;

        static List<string> lastErrorMessage = new List<string>();
        static bool supressedMessage = false;
        static string lastSessionToken;

        /// <summary>
        /// Create (or return the existing) Selenium WebDriver for the given BrowserSettings.<br/>
        /// Configures Chrome, Edge or Firefox options (profile, headless, download, stealth, debugging),
        /// starts the driver, navigates to the configured URL and waits for document ready.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings describing how to launch and configure the browser</param>
        /// <returns>The IWebDriver for the browser, or the existing driver if already set</returns>
        public static IWebDriver GetBrowserDriver(BrowserSettings browserSettings)
        {
            IWebDriver tmpDriver = null;
            ChromiumOptions options = null;
            bool waitForDebugger = false;
            string userAgent = GPAL.GPALSettings.UserAgent;

            if (null != browserSettings.BrowserDriver)
                return browserSettings.BrowserDriver;

            // Delete temp profiles left behind by killed runs. Runs on every launch, including ones with a supplied
            // profile.
            ChromeProfileManager.SweepAbandonedTempProfiles();

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Running workflow for Automation Engine [{browserSettings.AutomationEngine}]", null, GPALObjectType.None);

            if (null == browserSettings.DriverLocation)
                // driver is located with executable
                browserSettings.DriverLocation = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6);    // skip file:\\

            if (true == GPAL.GPALSettings.AutoUpdateWebDriver && false == alreadyUpdating)
            {
                alreadyUpdating = true;
                DriverHelper.UpdateDriver(browserSettings);
                // NOTE: no more port overlap, can have multiple running, each one exits when its own browser closes
                // its stdin, no need to kill any longer, we'll just get the next available port
                // BrowserHelper.KillProcess("GPALRestAPI");   // could be started by browser that has ottomagic installed
            }

            try
            {
                if (BrowserType.Chrome == browserSettings.BrowserType || BrowserType.Edge == browserSettings.BrowserType)
                {
                    if (BrowserType.Chrome == browserSettings.BrowserType)
                        options = new ChromeOptions();
                    else
                        options = new EdgeOptions();

                    // The driver answers alert, confirm and prompt itself. Nothing is injected and the page cannot
                    // see it.
                    if (null != browserSettings.DialogsAccepted)
                        options.UnhandledPromptBehavior = true == browserSettings.DialogsAccepted
                            ? UnhandledPromptBehavior.Accept
                            : UnhandledPromptBehavior.Dismiss;

                    if (true == browserSettings.UseExistingBrowser)
                    {
                        options.DebuggerAddress = "127.0.0.1:" + browserSettings.ExistingBrowserPort.ToString();
                        waitForDebugger = true;
                    }
                    else
                    {
                        if (null == browserSettings.DebugPort && true == browserSettings.UsePuppeteer && false == browserSettings.DebugPipe)  // make sure puppeteerURL is properly set - if debugport is set, it will update url, if not, set the debugport to default
                        {
                            // First free port from 0xdead (57005) up.
                            browserSettings.DebugPort = FindFreePort();
                            browserSettings.PuppeteerUrl = $"http://127.0.0.1:{browserSettings.DebugPort}";
                        }

                        // chrome 136+ requires a user data dir to do anything with cdp port, so define first cause it has to be in place to query useragent
                        if (false == string.IsNullOrEmpty(browserSettings.DownloadLocation))
                            options.AddUserProfilePreference("download.default_directory", browserSettings.DownloadLocation);

                        // NOTE: chrome 136 requires a user-data-dir for security reasons
                        // Profile settings
                        if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                        {
                            browserSettings.ProfileDataDirectory = GetBrowserProfileDirectory(browserSettings);

                            if (false == string.IsNullOrEmpty(browserSettings.ProfileName))
                            {
                                // the profile name might not be what we expect, which is a directory name.
                                // this will iterate thru all directories under user-data-dir, open the Preferences file and try to match token profile.name
                                // if it matches, then the directory containing that Preferences file is returned
                                // FindProfileDirectory returns a directory or throws
                                options.AddArguments($"--profile-directory={FindProfileDirectory(browserSettings.ProfileDataDirectory, browserSettings.ProfileName)}");
                            }
                        }
                        else if (true == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                        {
                            // Create temporary profile if none specified
                            browserSettings.ProfileDataDirectory = ChromeProfileManager.CreateTempUserProfile(
                                browserSettings.DownloadLocation ?? FileHelper.GetDefaultDownloadDirectory(browserSettings.Browser),
                                browserSettings.PromptForDownload,
                                browserSettings.OpenPDFExternally,
                                browserSettings.LoadImages,
                                browserSettings.UseOttoMagic
                            );
                            browserSettings.TempProfileCreated = true;
                        }

                        // Selenium writes download preferences into a supplied profile and Chrome keeps them, so save
                        // the current values for Browser.Close to restore. Temp profiles are deleted on close and
                        // skipped.
                        if (false == browserSettings.TempProfileCreated && false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                            browserSettings.PreviousDownloadPreferences = ChromeProfileManager.ApplyDownloadPreferences(
                                browserSettings.ProfileDataDirectory,
                                browserSettings.DownloadLocation,
                                browserSettings.PromptForDownload,
                                browserSettings.OpenPDFExternally);

                        // A profile can be open in only one browser. Launching onto an open one hands the command line
                        // to that browser and exits; the debug port never opens and the run hangs. Refuse here.
                        if (false == browserSettings.TempProfileCreated)
                            RefuseIfProfileIsOpen(browserSettings);

                        if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                            options.AddArgument($"--user-data-dir={browserSettings.ProfileDataDirectory.Replace("\\", "/")}");
                        /* prolly no longer a use case for this - edge has some annoying popups that block selenium unless logged in as guest
                                                else if (true == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory) && BrowserType.Edge == browserSettings.BrowserType)
                                                    options.AddArgument("--guest");     // in msedge, an annoyting 'personalize your experience' nag and options bar pops up blocking automation, this prevents that
                        */

                        // *******************************************************************
                        // NOTE: CAVEAT: suppose a time comes to run with some sort of extension, this isn't causing any harm, just annoying...
                        // not keen on disabling extensions, they make the bot look alive, but somehow we score low on recaptcha with ottomagic, no idea why, puppeteer is awesome
                        // *******************************************************************
                        options.AddArguments("--disable-extensions");    // no ottomagic - selenium and gpalrestapi/ottomagic DO NOT get along at all
                                                                         // *******************************************************************
                                                                         // *******************************************************************
                        // Chromium honors only the last --disable-features switch, so all features go in one. Edge
                        // shopping content scripts keep changing retail pages, which hangs DOM.getDocument on item
                        // pages.
                        string disableFeatures = "TranslateUI,Translate";

                        // Temp profiles only: turn off the 46 MB of on-device models a new profile downloads in its
                        // first 30 seconds.
                        if (true == browserSettings.TempProfileCreated)
                            disableFeatures += ",OptimizationGuideModelDownloading,OptimizationHintsFetching,OptimizationTargetPrediction,OptimizationHints";

                        if (BrowserType.Edge == browserSettings.BrowserType)
                        {
                            disableFeatures += ",msShoppingTrigger,msShopping,msEdgeShoppingUI,msEdgeShoppingList";

                            // Edge implicit sign-in, which --disable-sync does not cover
                            if (true == browserSettings.TempProfileCreated)
                                disableFeatures += ",msImplicitSignin,msEdgeImplicitSignin,msIdentityImplicitSignin";
                        }

                        options.AddArgument($"--disable-features={disableFeatures}");

                        // Edge signs a new profile in with the Windows account and syncs favourites, settings
                        // and extensions down onto it. Applied to temp profiles only; a supplied profile keeps
                        // whatever sync settings it has
                        if (true == browserSettings.TempProfileCreated)
                        {
                            options.AddArgument("--disable-sync");
                            options.AddArgument("--disable-signin-promo");
                        }

                        // Disable infobars and any chrome overlays
                        options.AddArgument("--disable-infobars");
                        options.AddArgument("--disable-session-crashed-bubble");
                        options.AddArgument("--hide-crash-restore-bubble");
                        options.AddArgument("--noerrdialogs");

                        if (true == browserSettings.UseHeadless)
                        {
                            // Set headless mode
                            options.AddArgument("--headless=new");
                            // Disable GPU
                            //options.AddArgument("--disable-gpu");
                            // Disable software rasterizer
                            //options.AddArgument("--disable-software-rasterizer");
                            // Disable sandbox
                            //options.AddArgument("--no-sandbox");

                            // Disable notifications
                            options.AddArgument("--disable-notifications");
                            // Disable popup blocking
                            options.AddArgument("--disable-popup-blocking");
                            // Disable setuid sandbox
                            //options.AddArgument("--disable-setuid-sandbox");

                            // Headless puts "Headless" in the user agent, so remove it. Reading the user agent from
                            // the browser costs a launch, so only with UserAgentFromBrowser; otherwise
                            // ResolveUserAgent supplies it.
                            string currUserAgent;

                            if (false == browserSettings.UserAgentFromBrowser)
                                currUserAgent = ResolveUserAgent(browserSettings);
                            else
                            {
                                if (BrowserType.Chrome == browserSettings.BrowserType)
                                    tmpDriver = new ChromeDriver(browserSettings.DriverLocation, (ChromeOptions)options);
                                else
                                    tmpDriver = new EdgeDriver(browserSettings.DriverLocation, (EdgeOptions)options);

                                IJavaScriptExecutor js = (IJavaScriptExecutor)tmpDriver;
                                currUserAgent = (String)js.ExecuteScript("return navigator.userAgent");
                                tmpDriver.Quit();
                            }

                            options.AddArgument($"--user-agent={currUserAgent.Replace("Headless", "")}");

                            options.AddUserProfilePreference("download.prompt_for_download", false);
                        }
                        else
                        {
                            if (true == browserSettings.PromptForDownload)
                                options.AddUserProfilePreference("download.prompt_for_download", true);
                            else
                                options.AddUserProfilePreference("download.prompt_for_download", false);
                        }

                        // default for chrome is false - so turn on this safeguard
                        // Purpose: When set to true, it allows Chrome to automatically handle cases where the specified download directory
                        // (set via "download.default_directory") is invalid, inaccessible, or doesn't exist. Chrome may attempt to upgrade
                        // or fall back to a valid directory, ensuring downloads proceed without errors.
                        options.AddUserProfilePreference("download.directory_upgrade", true);

                        options.AddUserProfilePreference("plugins.always_open_pdf_externally", browserSettings.OpenPDFExternally);
                        options.AddArguments($"--blink-settings=imagesEnabled={browserSettings.LoadImages.ToString().ToLower()}");
                        options.AddUserProfilePreference("disable-popup-blocking", $"{!browserSettings.BlockPopUps}");

                        // turn on dark mode
                        if (browserSettings.StealthType.HasFlag(StealthType.DarkMode))
                            options.AddArgument("--force-dark-mode");

                        // Set remote debugging pipe or portt
                        if (true == browserSettings.DebugPipe)
                            options.AddArgument("--remote-debugging-pipe");
                        else if (null != browserSettings.DebugPort)
                            options.AddArgument($"--remote-debugging-port={browserSettings.DebugPort.Value}");

                        //options.AddUserProfilePreference("safebrowsing.enabled", true); // CAVEAT: should this be configurable? disables warnings while downloading files... 'this may harm' seems we don't want that during automation

                        // obscure anti-bot detections
                        // Adding argument to disable the AutomationControlled flag 
                        // not supported, throws infobar message
                        options.AddArguments("--disable-blink-features=AutomationControlled");

                        // Exclude the collection of enable-automation switches
                        options.AddExcludedArgument("enable-automation");
                        if (options is ChromeOptions chromeOpts)
                            chromeOpts.AddAdditionalChromeOption("useAutomationExtension", false);
                        else if (options is EdgeOptions edgeOpts)
                            edgeOpts.AddAdditionalEdgeOption("useAutomationExtension", false);
                    }

                    if (browserSettings.StealthType.HasFlag(StealthType.PatchDriver))
                    {
                        if (BrowserType.Edge == browserSettings.BrowserType)
                            EdgePatcher.PatchEdgeDriver(browserSettings.Browser);
                        else
                            ChromePatcher.PatchChromeDriver(browserSettings.Browser);
                    }

                    // selenium will not accept a url as a commandline option, adds -- in front, there is no
                    // options.AddArgument(browserSettings.CurrentURL);    // doesn't always work, some enterprise browsers have a mandatory first page that bypasses commandline url

                    // Use this to query the logs to get the server response code for the goto
                    // NOTE: CAVEAT: this might make us more detectable in selenium??
                    // this is not a required feature, to return the navigation status code in SeleniumGoto
                    options.SetLoggingPreference(OpenQA.Selenium.LogType.Performance, LogLevel.All);

                    tmpDriver = GetWebDriver(browserSettings, options);

                    // chromedriver does not report the browser's pid, so the browser is found by the debug port it
                    // listens on: the one reached with WithExistingCDPDebugPort, or one launched with a debug port.
                    // Without a port it is the browser the driver started. Bringing the window to the front and
                    // hardware input find the window through it, teardown waits for it and ends its tree when it does
                    // not exit (which a driver that died can no longer do), and GPAL browsers on the same process are
                    // known to be the same browser.
                    if (true == browserSettings.UseExistingBrowser)
                        browserSettings.Process = PortOwnerProcess(browserSettings.ExistingBrowserPort.Value);
                    else if (null != browserSettings.DebugPort)
                        browserSettings.Process = PortOwnerProcess(browserSettings.DebugPort.Value);
                    else if (null != tmpDriver)
                        browserSettings.Process = DriverBrowserProcess(browserSettings.ServiceDriverPid, BrowserType.Edge == browserSettings.BrowserType ? "msedge.exe" : "chrome.exe");

                    //if (BrowserType.Chrome == browserSettings.BrowserType)
                    //    tmpDriver = new ChromeDriver(browserSettings.DriverLocation, (ChromeOptions)options);
                    //else
                    //    tmpDriver = new EdgeDriver(browserSettings.DriverLocation, (EdgeOptions)options);
                }
                else if (BrowserType.FireFox == browserSettings.BrowserType)
                {
                    // read more: https://github.com/mozilla/geckodriver/issues/430
                    FirefoxDriverService firefoxDriverService = FirefoxDriverService.CreateDefaultService(browserSettings.DriverLocation);
                    FirefoxOptions ffOptions = new FirefoxOptions();

                    if (null != browserSettings.DialogsAccepted)
                        ffOptions.UnhandledPromptBehavior = true == browserSettings.DialogsAccepted
                            ? UnhandledPromptBehavior.Accept
                            : UnhandledPromptBehavior.Dismiss;
                    if (true == browserSettings.UseExistingBrowser)
                    {
                        firefoxDriverService.HideCommandPromptWindow = true;
                        // a free port for geckodriver's own server. Selenium waits on the port it is given, so 0
                        // never answers
                        firefoxDriverService.Port = FindFreePort();
                        firefoxDriverService.Host = "127.0.0.1";
                        firefoxDriverService.ConnectToRunningBrowser = true;
                        // Marionette is Firefox's control port, like the debugger port for Chrome and Edge.
                        // ExistingBrowserPort, or 2828, the Firefox default.
                        firefoxDriverService.BrowserCommunicationPort = browserSettings.ExistingBrowserPort ?? 2828;

                        tmpDriver = new FirefoxDriver(firefoxDriverService, ffOptions);
                        browserSettings.ServiceDriverPid = firefoxDriverService.ProcessId;

                        // Firefox listens on Marionette itself, so the port finds its process.
                        browserSettings.Process = PortOwnerProcess(browserSettings.ExistingBrowserPort ?? 2828);

                        waitForDebugger = true;
                    }
                    else
                    {
                        // Set window size
                        ffOptions.AddArgument($"-width={browserSettings.WindowSize.Width}");
                        ffOptions.AddArgument($"-height={browserSettings.WindowSize.Height}");

                        if (true == browserSettings.UseHeadless)
                        {

                            // Set headless mode
                            ffOptions.AddArgument("-headless");

                            // Disable notifications
                            ffOptions.AddArgument("-disable-notifications");

                            // Set user agent - this makes firefox not start byt gives error
                            // if (null != userAgent)
                            //   ffOptions.AddArgument($"--user-agent={userAgent}");
                        }

                        if (null != browserSettings.DebugPort)
                        {
                            if (true == browserSettings.UsePuppeteer)
                                browserSettings.PuppeteerUrl = $"http://127.0.0.1:{browserSettings.DebugPort}";
                        }
                        else if (true == browserSettings.UsePuppeteer)  // make sure puppeteerURL is properly set - if debugport is set, it will update url, if not, set the debugport to default
                        {
                            // First free port from 0xdead (57005) up, skipping bound ports. Call once per browser;
                            // a second call reserves a port nothing uses.
                            if (null == browserSettings.DebugPort)
                                browserSettings.DebugPort = FindFreePort();

                            browserSettings.PuppeteerUrl = $"http://127.0.0.1:{browserSettings.DebugPort}";
                        }

                        // No --remote-debugging-port: geckodriver runs Firefox's remote agent itself and refuses it.

                        ffOptions.SetPreference("print.always_print_silent", true);
                        ffOptions.SetPreference("print.show_print_progress", false);


                        // Headless always disables pdfjs so pdfs download. Get() sets OpenPDFExternally false for
                        // headless, which without this check would turn pdfjs back on.
                        ffOptions.SetPreference("pdfjs.disabled", browserSettings.OpenPDFExternally || browserSettings.UseHeadless);

                        if (false == browserSettings.PromptForDownload)
                            ffOptions.SetPreference("browser.helperApps.neverAsk.saveToDisk", GPAL.GPALSettings.FirefoxDirectDownloadMimeTypes);
                        else
                            ffOptions.SetPreference("browser.helperApps.neverAsk.saveToDisk", "");

                        // browser.download.useDownloadDir false shows the save dialog for every file. Set it from
                        // PromptForDownload so Selenium Firefox does not depend on the profile. OttoMagic sets it too.
                        ffOptions.SetPreference("browser.download.useDownloadDir", false == browserSettings.PromptForDownload);

                        if (false == browserSettings.LoadImages)
                            ffOptions.SetPreference("permissions.default.image", 0);
                        else
                            ffOptions.SetPreference("permissions.default.image", 1);

                        // dom.popup_maximum: popups one event may open. BlockPopUps sets 0; otherwise 20, the Firefox
                        // default.
                        ffOptions.SetPreference("dom.popup_maximum", true == browserSettings.BlockPopUps ? 0 : 20);

                        ffOptions.SetPreference("safebrowsing.enabled", false); // TODO: should this be configurable? disables warnings while downloading files... 'this may harm' seems we don't want that during automation

                        if (false == string.IsNullOrEmpty(browserSettings.DownloadLocation))
                        {
                            ffOptions.SetPreference("browser.download.dir", browserSettings.DownloadLocation);
                            ffOptions.SetPreference("browser.download.folderList", 2); // 2: Use the custom download directory
                        }

                        ffOptions.SetPreference("dom.webdriver.enabled", false);

                        // A profile can hold OttoMagic, which is unsigned. Firefox disables an unsigned add-on when it
                        // starts with signatures required, and the OttoMagic engine then finds it off, so Selenium starts
                        // Firefox the way OttoMagic does.
                        ffOptions.SetPreference("xpinstall.signatures.required", false);

                        // -profile and the path go as two arguments. As one, Firefox sees an unknown flag and starts
                        // on another profile while geckodriver waits on the named one. Trim a trailing separator; it
                        // escapes the closing quote Windows adds.
                        if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                            // browserSettings.ProfileDataDirectory = GetBrowserProfileDirectory(browserSettings); // not sure this is applicable to firefox
                            ffOptions.AddArguments("-profile", (browserSettings.ProfileName ?? browserSettings.ProfileUserName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        else if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                            ffOptions.AddArguments("-profile", browserSettings.ProfileDataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                        // geckodriver writes its preferences and ours into a supplied profile's user.js, and Firefox
                        // saves them into prefs.js. Snapshot the profile for Browser.Close to restore.
                        if (false == browserSettings.TempProfileCreated && false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                            browserSettings.PreviousFirefoxPreferences = FirefoxProfileManager.TakeSnapshot(browserSettings.ProfileDataDirectory);

                        // Ensure that debugging is enabled in Firefox, F12, ..., settings, enable browser chrome and bugging toolboxes checkbox, enable remote debugging checkbox
                        //options.AddArguments("-foreground");
                        //options.AddArguments("-start-debugger-server 2828");
                        //tmpDriver = new FirefoxDriver(browserSettings.driverLocation, options);

                        // kludge on opening first window
                        //Uri safe = RemoteWebDriverExtended.FirefoxURL;
                        //RemoteWebDriverExtended.FirefoxURL = new Uri(browserSettings.currentURL);
                        //tmpDriver = RemoteWebDriverExtended.GetFireFoxDriver(browserSettings.driverLocation);
                        //RemoteWebDriverExtended.FirefoxURL = safe;
                    }

                    // A browser reached with WithExistingCDPDebugPort was connected to above; launching here as well would
                    // start a second Firefox and drop the connection.
                    if (false == browserSettings.UseExistingBrowser)
                    {
                        ffOptions.BinaryLocation = FindFirefoxBinaryLocation();

    //                    tmpDriver = new FirefoxDriver(browserSettings.DriverLocation, ffOptions);   // would ever start up a session with no options?
                                                                                                    // Explicit service for Firefox to get geckodriver PID
                        if (true == browserSettings.HiddenDesktop)
                        {
                            // Start geckodriver ourselves on the hidden desktop and give Selenium its port instead of a
                            // service. StartHiddenDriver sets ServiceDriverPid.
                            int hiddenPort = StartHiddenDriver(browserSettings);

                            tmpDriver = new OpenQA.Selenium.Remote.RemoteWebDriver(new Uri($"http://127.0.0.1:{hiddenPort}"), ffOptions);
                        }
                        else
                        {
                            FirefoxDriverService firefoxService = FirefoxDriverService.CreateDefaultService(browserSettings.DriverLocation);
                            firefoxService.HideCommandPromptWindow = true;  // hides the console window

                            // Firefox's Marionette on the debug port, so another program can connect to it
                            if (null != browserSettings.DebugPort)
                                firefoxService.BrowserCommunicationPort = browserSettings.DebugPort.Value;

                            tmpDriver = new FirefoxDriver(firefoxService, ffOptions);

                            // Capture geckodriver.exe PID — reuse the same property
                            browserSettings.ServiceDriverPid = firefoxService.ProcessId;
                        }

                        // Get the Firefox process from moz:processID. Geckodriver launches Firefox, so this is our only
                        // handle to it; without it teardown cannot wait for, warn about or kill Firefox, and it outlives
                        // geckodriver.
                        browserSettings.Process = SeleniumBrowserProcess(tmpDriver, "moz:processID");

                        // Firefox is up, so geckodriver's preferences are in user.js. Record them before geckodriver puts
                        // user.js back at the end of the session.
                        FirefoxProfileManager.NoteWrittenPreferences(browserSettings.PreviousFirefoxPreferences);
                    }

                    //tmpDriver = new FirefoxDriver(firefoxDriverService, ffOptions);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to start browser driver for [{browserSettings.BrowserType}].", browserSettings.Browser, GPALObjectType.Browser, ex);
                // unrecoverable error, the only time we rethrow
                // return null;
                throw;
            }
            if (null != tmpDriver)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Browser [{browserSettings.BrowserType}] launched{(true == browserSettings.UseHeadless ? " headless" : "")}.", browserSettings, GPALObjectType.Other);

                browserSettings.BrowserDriver = tmpDriver;

                // Edge preloads its New Tab Page into a hidden background window that appears asynchronously as an
                // extra WindowHandle and corrupts our sequential tab-index tracking. At this instant (before we
                // navigate) the driver's url is that preload's url, so capture it as a runtime signature - later we
                // positively match the phantom tab by this url, never by position (which races the async insert)
                // nor a hardcoded string.
                var edgeGhostBrowser = browserSettings.Browser as Browser;
                if (BrowserType.Edge == browserSettings.BrowserType && null != edgeGhostBrowser)
                    try { edgeGhostBrowser.GhostTabUrl = tmpDriver.Url; }
                    catch { /* non-fatal: without a signature we simply never skip a ghost */ }

                // The driver waits for a page load no longer than GPAL waits for document ready. Its own default is 300 s,
                // longer than the client's 60 s command timeout: a page that never finishes loading left geckodriver
                // busy for 5 minutes, and every command sent meanwhile timed out.
                tmpDriver.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(browserSettings.WaitForDocumentReadyTimeoutMs);

                //BrowserHelper.SeleniumGoToUrl(browserSettings.CurrentURL, browserSettings);

                browserSettings.CurrentURL = "about:blank";

                if (true == browserSettings.Maximize)
                    tmpDriver.Manage().Window.Maximize();
                else if (true == browserSettings.Minimize)
                    tmpDriver.Manage().Window.Minimize();
                else if (true == browserSettings.FullScreen)
                    tmpDriver.Manage().Window.FullScreen();
                else if (false == browserSettings.WindowSize.IsEmpty)
                {
                    // Set the desired window size
                    var windowSize = new Size(browserSettings.WindowSize.Width, browserSettings.WindowSize.Height);
                    tmpDriver.Manage().Window.Size = windowSize;

                    // Set the desired window position (top-left corner). Point is x then y, which is Left then Top
                    var windowPosition = new Point(browserSettings.WindowSize.Left, browserSettings.WindowSize.Top);
                    tmpDriver.Manage().Window.Position = windowPosition;
                }

                if (true == waitForDebugger)
                    Thread.Sleep(2000);

                if (BrowserType.Chrome == browserSettings.BrowserType)
                {
                    DriverHelper.GetChromeVersion(out browserSettings.Version);
                }
                else if (BrowserType.Edge == browserSettings.BrowserType)
                {
                    DriverHelper.GetEdgeVersion(out browserSettings.Version);
                }
                else if (BrowserType.FireFox == browserSettings.BrowserType)
                {
                    DriverHelper.GetFirefoxVersion(out browserSettings.Version);
                }

                CheckDocumentReady(((Browser)browserSettings.Browser), true);
            }

            return tmpDriver;
        }
        /// <summary>
        /// Tells Chrome or Edge (CDP Page.setDownloadBehavior, as <see cref="SetHeadlessDownload"/> uses) to save
        /// every download into <paramref name="directory"/> without a Save dialog, whatever the profile says.<br/>
        /// Without it a windowed browser relies on download.prompt_for_download, which a supplied profile can
        /// override.<br/>
        /// Chromium only; the caller checks. Firefox download preferences are set when the driver is built.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings for a Chrome or Edge browser</param>
        /// <param name="directory">Directory downloads should land in</param>
        public static void AllowDownloads(BrowserSettings browserSettings, string directory)
        {
            var parameters = new Dictionary<string, object>
            {
                { "behavior", "allow" },
                { "downloadPath", directory }
            };

            BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.setDownloadBehavior", parameters);
        }
        /// <summary>
        /// Returns the user agent without launching a browser.<br/>
        /// In order: .WithUseUserAgent, userAgent in gpal.yaml, then the per-browser template in gpal.yaml with the
        /// installed version. GPAL's own requests use that template, so both send the same user agent.<br/>
        /// Headless needs this because its own contains "Headless". Reading it from a real browser costs a launch,
        /// so only with <see cref="Browser.WithUserAgentFromBrowser"/>.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings to resolve the user agent for</param>
        /// <returns>The user agent, never null</returns>
        internal static string ResolveUserAgent(BrowserSettings browserSettings)
        {
            string userAgent = browserSettings.OverrideUserAgent;

            if (true == string.IsNullOrWhiteSpace(userAgent))
                userAgent = GPAL.GPALSettings.UserAgent;

            if (true == string.IsNullOrWhiteSpace(userAgent))
                userAgent = MagicHelper.GetUserAgentString(browserSettings.BrowserType);

            return userAgent;
        }
        /// <summary>
        /// Sends the browser's credentials once, before the first navigation.<br/>
        /// Basic, Proxy, Bearer and API key are set as session-wide extra headers on Chrome and Edge with Selenium
        /// or Puppeteer. Digest, Firefox and OttoMagic use <see cref="CredentialsInUrl"/>: one navigation with the
        /// credentials in the url, and the browser answers the challenge. Form does nothing here.<br/>
        /// Credentials are never published.
        /// </summary>
        /// <param name="browser">Browser holding the credentials</param>
        /// <param name="url">Where the workflow is about to go, whose origin the credentials are for</param>
        internal static void PresentCredentials(Browser browser, GPALUrl url)
        {
            Credentials credential = (Credentials)browser.BrowserSettings.Credentials;
            WebAuthType authType = credential.WebAuthType;
            string header = AuthorizationHeader(credential, authType);

            browser.BrowserSettings.CredentialsPresented = DateTime.UtcNow;

            // Form and None send nothing; the workflow handles a login form with selectors.
            if (WebAuthType.Form == authType || WebAuthType.None == authType)
                return;

            if (true == string.IsNullOrEmpty(header) || BrowserType.FireFox == browser.BrowserSettings.BrowserType || true == browser.UseOttoMagic)
                CredentialsInUrl(browser, url, credential);
            else
            {
                string name = AuthorizationHeaderName(authType);
                Dictionary<string, object> headers = new Dictionary<string, object> { { name, header } };

                if (true == browser.UsePuppeteer)
                    browser.PuppeteerCommunicator.SetExtraHttpHeaders(headers).GetAwaiter().GetResult();
                else
                    BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Network.setExtraHTTPHeaders",
                        new Dictionary<string, object> { { "headers", headers } });

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Credentials presented as [{authType}] for [{url?.Url}]", browser, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Builds the Authorization value for the auth types that can be settled before the first request is made.
        /// </summary>
        /// <param name="credential">Credentials to present</param>
        /// <param name="authType">How the site expects to be told</param>
        /// <returns>The header value, or null for an auth type no header can satisfy</returns>
        internal static string AuthorizationHeader(Credentials credential, WebAuthType authType)
        {
            string value = null;

            if (WebAuthType.Basic == authType || WebAuthType.Proxy == authType)
                value = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Password}"));
            else if (WebAuthType.Bearer == authType)
                value = "Bearer " + credential.AccessToken;
            else if (WebAuthType.XAuthToken == authType || WebAuthType.XApiKey == authType || WebAuthType.ApiKey == authType)
                value = credential.AccessToken;      // their own header, so no scheme word in front of it

            return value;
        }

        /// <summary>
        /// The header an auth type is presented in.
        /// </summary>
        /// <param name="authType">How the site expects to be told</param>
        /// <returns>The header name</returns>
        internal static string AuthorizationHeaderName(WebAuthType authType)
        {
            string name = "Authorization";

            if (WebAuthType.Proxy == authType)
                name = "Proxy-Authorization";
            else if (WebAuthType.XAuthToken == authType)
                name = "X-Auth-Token";
            else if (WebAuthType.XApiKey == authType)
                name = "X-API-Key";
            else if (WebAuthType.ApiKey == authType)
                name = "api-key";

            return name;
        }
        /// <summary>
        /// Navigates once to the url with the credentials in it, so the browser answers the challenge and caches it
        /// per origin. The workflow's navigation then uses the plain url.
        /// </summary>
        /// <param name="browser">Browser to navigate</param>
        /// <param name="url">The url the workflow is about to visit, whose origin the credentials are for</param>
        /// <param name="credential">Credentials to carry</param>
        static void CredentialsInUrl(Browser browser, GPALUrl url, Credentials credential)
        {
            UriBuilder builder = new UriBuilder(url?.Url)
            {
                UserName = Uri.EscapeDataString(credential.Username ?? string.Empty),
                Password = Uri.EscapeDataString(credential.Password ?? string.Empty)
            };
            GPALUrl credentialed = new GPALUrl(builder.Uri.ToString());

            // Call the engines directly; .GoTo would publish the url, which holds the password.
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.GoTo(credentialed);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.GoTo(credentialed).Execute();
            else
                SeleniumGoToUrl(credentialed, browser.BrowserSettings);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Credentials presented to [{builder.Host}], answering its challenge", browser, GPALObjectType.Browser);
        }

        /// <summary>
        /// The browser process for a Selenium session, from the pid in its capabilities. Firefox reports
        /// moz:processID; Chromium drivers report none.
        /// </summary>
        /// <param name="driver">Driver whose session was just created</param>
        /// <param name="capability">Capability the driver publishes the browser's pid under</param>
        /// <returns>The browser process, or null when the driver did not say or it has already gone</returns>
        private static Process SeleniumBrowserProcess(IWebDriver driver, string capability)
        {
            Process retVal = null;

            try
            {
                object pid = (driver as IHasCapabilities)?.Capabilities?.GetCapability(capability);

                if (null != pid)
                    retVal = Process.GetProcessById(Convert.ToInt32(pid));
            }
            catch (Exception ex)
            {
                // No process with this pid means the browser already exited; DEBUG only and return null.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No browser process behind the driver for [{capability}]", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
        /// <summary>
        /// Waits up to <paramref name="timeoutMs"/> for the browser process to exit.<br/>
        /// Chrome and Firefox write preferences on shutdown, so a profile restored before exit gets overwritten.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings holding the process to wait on</param>
        /// <param name="timeoutMs">How long to wait before giving up and restoring anyway</param>
        internal static void WaitForBrowserToExit(BrowserSettings browserSettings, int timeoutMs)
        {
            try
            {
                browserSettings.Process?.WaitForExit(timeoutMs);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not wait on the browser process before restoring [{browserSettings.ProfileDataDirectory}]", browserSettings.Browser, GPALObjectType.Browser, ex);
            }
        }

        /// <summary>
        /// Tells Chrome or Edge (CDP Page.setDownloadBehavior) to save the next download into the directory of
        /// <paramref name="destination"/>, where the download watcher is watching.<br/>
        /// Runs for every watched download, so a stale download.default_directory in a supplied profile cannot send
        /// the file elsewhere.<br/>
        /// Silent no-op on other browsers. Firefox's directory is set when the driver is built, and its watcher
        /// watches that.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings for the browser performing the download</param>
        /// <param name="destination">Full path (directory + filename) to allow the download to be written to</param>
        public static void SetHeadlessDownload(BrowserSettings browserSettings, string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            string filename = Path.GetFileName(destination);

            var enableDownloadCommandParameters = new Dictionary<string, object>
            {
                { "behavior", "allow" },
                { "downloadPath", directory },
                { "filename", filename }
            };

            if (BrowserType.Chrome == browserSettings.BrowserType || BrowserType.Edge == browserSettings.BrowserType)
                BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.setDownloadBehavior", enableDownloadCommandParameters);
        }

        /// <summary>
        /// Records network calls on Selenium by wrapping fetch and XMLHttpRequest in the page and pushing each call
        /// onto window.__gpalCalls. Puppeteer uses CDP instead.<br/>
        /// Documents, scripts and images are not recorded.
        /// </summary>
        internal const string CallRecorderScript = @"
            (function () {
                if (window.__gpalCalls) return;

                window.__gpalCalls = [];

                var record = function (url, method, headers, body, type) {
                    var call = { url: String(url), method: method || 'GET', type: type, headers: headers || {}, postData: body || null, status: 0 };
                    window.__gpalCalls.push(call);
                    return call;
                };

                var nativeFetch = window.fetch;

                if (nativeFetch)
                    window.fetch = function (input, init) {
                        var url = input && input.url ? input.url : input;
                        var headers = {};
                        var options = init || {};

                        // a Headers object, a plain object or an array of pairs, all of which fetch accepts
                        if (options.headers && options.headers.forEach)
                            options.headers.forEach(function (v, k) { headers[k] = v; });
                        else if (options.headers)
                            Object.keys(options.headers).forEach(function (k) { headers[k] = options.headers[k]; });

                        var call = record(url, options.method, headers, options.body, 'Fetch');

                        return nativeFetch.apply(this, arguments).then(function (response) {
                            call.status = response.status;
                            return response;
                        }, function (error) {
                            call.status = 0;
                            throw error;
                        });
                    };

                var nativeOpen = XMLHttpRequest.prototype.open;
                var nativeSend = XMLHttpRequest.prototype.send;
                var nativeSetHeader = XMLHttpRequest.prototype.setRequestHeader;

                XMLHttpRequest.prototype.open = function (method, url) {
                    this.__gpalCall = { method: method, url: url, headers: {} };
                    return nativeOpen.apply(this, arguments);
                };

                XMLHttpRequest.prototype.setRequestHeader = function (name, value) {
                    if (this.__gpalCall) this.__gpalCall.headers[name] = value;
                    return nativeSetHeader.apply(this, arguments);
                };

                XMLHttpRequest.prototype.send = function (body) {
                    var pending = this.__gpalCall;

                    if (pending) {
                        var call = record(pending.url, pending.method, pending.headers, body, 'XHR');
                        this.addEventListener('loadend', function () { call.status = this.status; });
                    }

                    return nativeSend.apply(this, arguments);
                };
            })();
        ";

        /// <summary>
        /// Registers a script to be evaluated automatically on every new document load, using the Selenium
        /// Chromium CDP command <c>Page.addScriptToEvaluateOnNewDocument</c>. Chrome/Edge only.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings for the browser to inject the script into</param>
        /// <param name="script">The JavaScript source code to run on each new document</param>
        /// <returns>The CDP-assigned script identifier (for later removal via <see cref="SeleniumRemoveScriptToEvaluateOnNewDocument"/>), or null if not supported for this BrowserType</returns>
        public static string SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings browserSettings, string script)
        {
            if (BrowserType.Chrome != browserSettings.BrowserType && BrowserType.Edge != browserSettings.BrowserType)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] does not support Page.addScriptToEvaluateOnNewDocument", browserSettings.Browser, GPALObjectType.Browser);
                return null;
            }

            var result = BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object>
            {
                { "source", script }
            }) as Dictionary<string, object>;

            string identifier = result?["identifier"]?.ToString();

            // chromedriver registered it on the current tab only; tabs opened later get it from TabScripts
            browserSettings.TabScripts ??= SeleniumTabScripts.Start(browserSettings);
            browserSettings.TabScripts?.Add(script, identifier);

            return identifier;
        }

        /// <summary>
        /// Removes a script previously registered via <see cref="SeleniumAddScriptToEvaluateOnNewDocument"/>, using
        /// the Selenium Chromium CDP command <c>Page.removeScriptToEvaluateOnNewDocument</c>. Chrome/Edge only.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings for the browser to remove the script from</param>
        /// <param name="identifier">The CDP script identifier returned by <see cref="SeleniumAddScriptToEvaluateOnNewDocument"/></param>
        public static void SeleniumRemoveScriptToEvaluateOnNewDocument(BrowserSettings browserSettings, string identifier)
        {
            if (BrowserType.Chrome != browserSettings.BrowserType && BrowserType.Edge != browserSettings.BrowserType)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] does not support Page.removeScriptToEvaluateOnNewDocument", browserSettings.Browser, GPALObjectType.Browser);
                return;
            }

            BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.removeScriptToEvaluateOnNewDocument", new Dictionary<string, object>
            {
                { "identifier", identifier }
            });

            browserSettings.TabScripts?.Remove(identifier);
        }
        /// <summary>
        /// Get the browserSettings.ProfileUserName profile directory under the default user directory
        /// </summary>
        /// <param name="browserSettings">BrowserSettings object</param>
        /// <param name="getDefaultDirectory">ignore browserSettings.ProfileUserName and return the default profile directory alone</param>
        /// <returns></returns>
        public static string GetBrowserProfileDirectory(BrowserSettings browserSettings, bool getDefaultDirectory = false)
        {
            // Chromium profiles live under LocalApplicationData, Firefox profiles under ApplicationData
            // (Roaming). There is no single base for all three
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string profilePath = string.Empty;

            switch (browserSettings.BrowserType)
            {
                case BrowserType.Chrome:
                    profilePath = Path.Combine(localAppData, "Google", "Chrome", "User Data");
                    break;
                case BrowserType.FireFox:
                    profilePath = Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles");
                    break;
                case BrowserType.Edge:
                    profilePath = Path.Combine(localAppData, "Microsoft", "Edge", "User Data");
                    break;
                default:
                    Exception ex = new Exception("Unsupported Browser Type");   // let's force this as an exception to post to the exception channel
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported browser type for loading user profile [{browserSettings.ProfileUserName}]: [{browserSettings.BrowserType.ToString()}]", ex);
                    break;
            }

            string newProfilePath = profilePath;
            // chrome 136 will not let the default directory be specified as the user-data-dir if running via selenium (no access to user cookies)
            // - copy to a new folder is a workaround, we will guard against defining them the same
            if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) && false == getDefaultDirectory)
            {
                newProfilePath = Path.Combine(profilePath, browserSettings.ProfileUserName);
                // Check if the profile directory exists
                // Throws rather than falling back to the user data root, which would silently run the default
                // profile instead of the one named
                if (!Directory.Exists(newProfilePath))
                    RefuseProfile($"[{browserSettings.BrowserType}] has no profile directory [{newProfilePath}] for user name [{browserSettings.ProfileUserName}]");
            }
            return newProfilePath;
        }

        /// <summary>
        /// the profile name might not be what we expect, which is a directory name.
        /// this will iterate thru all directories under user-data-dir, open the Preferences file and try to match token profile.name
        /// if it matches, then the directory containing that Preferences file is returned
        /// </summary>
        /// <param name="userDataDirectory"></param>
        /// <param name="profileName"></param>
        /// <returns>The directory holding profileName</returns>
        /// <exception cref="GPALException">The profile is not on this machine under either name</exception>
        public static string FindProfileDirectory(string userDataDirectory, string profileName)
        {
            // Checked before GetDirectories, which throws DirectoryNotFoundException without naming the
            // profile that was asked for
            if (false == Directory.Exists(userDataDirectory))
                RefuseProfile($"Looked for profile [{profileName}] in [{userDataDirectory}], which is not there");

            string[] profileDirectories = Directory.GetDirectories(userDataDirectory);
            List<string> matches = new List<string>();

            foreach (string profileDirectory in profileDirectories)
            {
                string preferencesFile = Path.Combine(profileDirectory, "Preferences");

                if (File.Exists(preferencesFile))
                {
                    string profileNameFromFile = GetProfileNameFromPreferencesFile(preferencesFile);

                    if (null != profileNameFromFile && profileNameFromFile.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                        matches.Add(new DirectoryInfo(profileDirectory).Name);
                }
            }

            if (0 < matches.Count)
            {
                // Chrome allows duplicate display names. The first match is used; the warning names the
                // alternatives and their sign-in state, which is usually what separates them
                if (1 < matches.Count)
                {
                    string choices = string.Join(", ", matches.Select(directory =>
                        $"[{directory}] ({SignedInSaid(Path.Combine(userDataDirectory, directory))})"));

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"[{matches.Count}] profiles are named [{profileName}]: {choices}. Using [{matches[0]}]. Name the directory instead of the display name to say which one you meant",
                        null, GPALObjectType.Browser);
                }

                return matches[0];
            }
            // Accepts a directory name as well as a display name. Chrome puts the directory name on
            // --profile-directory, so both forms are in circulation
            if (true == Directory.Exists(Path.Combine(userDataDirectory, profileName)))
                return profileName;

            // Chrome creates an empty profile rather than refusing an unknown --profile-directory, so an
            // unmatched name is thrown rather than passed through
            RefuseProfile($"No profile named [{profileName}] in [{userDataDirectory}], by display name or directory name");

            return null;
        }

        // Sign-in state from account_info in the profile Preferences. Reported in the duplicate-name warning
        private static string SignedInSaid(string profileDirectory)
        {
            string retVal = "cannot tell";

            try
            {
                string preferencesFile = Path.Combine(profileDirectory, "Preferences");

                if (true == File.Exists(preferencesFile))
                {
                    JObject preferences = JObject.Parse(File.ReadAllText(preferencesFile));
                    JArray accounts = preferences.SelectToken("account_info") as JArray;

                    retVal = null != accounts && 0 < accounts.Count ? "signed in" : "not signed in";
                }
            }
            catch (Exception)
            {
                // An unparseable Preferences leaves the state unknown; the directory name is still reported
            }

            return retVal;
        }

        // Publishes the ERROR and throws, matching RefuseIfProfileIsOpen
        private static void RefuseProfile(string message)
        {
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, message, null, GPALObjectType.Browser);

            throw new GPALException($"{GPAL.MyMethodName()}: {message}");
        }

        /// <summary>
        /// Reads a Chromium "Preferences" file and extracts the value of the "profile.name" JSON token.
        /// </summary>
        /// <param name="preferencesFilePath">Full path to the profile's "Preferences" file</param>
        /// <returns>The profile's display name, or null if not found or the file could not be parsed</returns>
        static string GetProfileNameFromPreferencesFile(string preferencesFilePath)
        {
            try
            {
                string preferencesContent = File.ReadAllText(preferencesFilePath);
                JObject preferencesJson = JObject.Parse(preferencesContent);

                // Extract the given_name from the JSON structure
                JToken givenNameToken = preferencesJson.SelectToken("profile.name");
                if (givenNameToken != null)
                {
                    return givenNameToken.ToString();
                }

                return null; // given_name not found
            }
            catch
            {
                return null; // ERROR occurred while parsing JSON or accessing values
            }
        }

        /// <summary>
        /// Looks up the installed Firefox executable path from the registry key
        /// configured in GPAL.GPALSettings.FirefoxBinaryPathRegistry.
        /// </summary>
        /// <returns>Full path to firefox.exe, or null if Firefox is not installed/registered</returns>
        public static string FindFirefoxBinaryLocation()
        {
            // Open the registry key corresponding to Firefox installation
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(GPAL.GPALSettings.FirefoxBinaryPathRegistry))
            {
                if (key != null)
                {
                    // Retrieve the version of Firefox installed
                    string firefoxPath = key.GetValue(null)?.ToString();    // get (Default) value
                    if (!string.IsNullOrEmpty(firefoxPath))
                        return firefoxPath;
                }
            }

            return null; // Firefox binary location not found
        }
        /// <summary>
        /// Searches all open browser window handles for one whose URL matches (or is matched by) <paramref name="URL"/>,
        /// switching the driver's focus to that window if found.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings whose driver's windows are searched</param>
        /// <param name="URL">URL (or partial URL) to look for among the open tabs</param>
        /// <returns>The window handle of the matching tab, or null if no tab matches</returns>
        public static string FindTab(BrowserSettings browserSettings, string URL)
        {
            var handles = browserSettings.BrowserDriver.WindowHandles;
            foreach (var handle in handles)
            {
                browserSettings.BrowserDriver.SwitchTo().Window(handle);
                if (URL.Contains(browserSettings.BrowserDriver.Url) || browserSettings.BrowserDriver.Url.Contains(URL))
                {
                    return handle;
                }
            }
            return null;
        }
        /// <summary>
        /// Navigates to the next page of results, either by clicking/following the configured "next page"
        /// selector (or its href, if any), or, when InfiniteScroll is enabled, by scrolling to the bottom
        /// of the page and waiting for new content to load.
        /// </summary>
        /// <param name="browser">Browser to navigate</param>
        /// <returns>True if the next page action (click or scroll) was performed, false if no next page button was found</returns>
        public static bool GotoNextPage(Browser browser)
        {

            List<GPALElement> matchedElems = null;
            ReadOnlyCollection<GPALElement> elems = null;
            bool clicked = false;
            byte VK_END = 0x23;
            int withAllThatMatchSave = browser.CurrentUOW.WithAllThatMatch;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Getting next page.", browser, GPALObjectType.Browser);
            browser.WithAllThatMatch(1);


            // CAVEAT: TODO: rework, scroll and scrape, scrolling might scroll elements off page or unrender them
            if (false == browser.CurrentUOW.InfiniteScroll)
            {
                // we cannot used cached results knowing we are going to a new page
                browser.CurrentUOW.NextPageButton.WebSelectorFoundResults = null;
                browser.CurrentUOW.NextPageButton.WebSelectorMatchedResults = null;
                foreach (var sel in browser.CurrentUOW.WithSelectorList)
                {
                    sel.WebSelectorFoundResults = null;
                    sel.WebSelectorMatchedResults = null;
                }

                elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, browser.CurrentUOW.NextPageButton, out bool matchedAll, out matchedElems, null, false);

                // can only be one match
                if (null != elems && 0 < elems.Count && true == matchedAll)
                {
                    try
                    {

                        if (false == string.IsNullOrEmpty(elems[0].Href))
                        {
                            string href = elems[0].Href;
                            UnitOfWork saveUOW = browser.CurrentUOW;    // keep this in
                            bool obeySave = browser.BrowserSettings.ObeyRobotsTxt;
                            browser.BrowserSettings.ObeyRobotsTxt = false;
                            browser.GoTo(href);    // checkdocumentready is in goto - do not perform robots check on navigation

                            // A site can show an interstitial, such as a device check, at its own url that is already
                            // document-ready. Poll until the url equals href, up to WaitForDocumentReadyTimeoutMs.
                            Stopwatch arriving = Stopwatch.StartNew();
                            string here = GetCurrentUrl(browser.BrowserSettings);

                            if (false == href.Equals(here, StringComparison.OrdinalIgnoreCase))
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting for [{href}], the page showing is [{here}]", browser, GPALObjectType.Browser);

                            while (false == href.Equals(here, StringComparison.OrdinalIgnoreCase)
                                && browser.BrowserSettings.WaitForDocumentReadyTimeoutMs > arriving.ElapsedMilliseconds)
                            {
                                System.Threading.Thread.Sleep(250);

                                here = GetCurrentUrl(browser.BrowserSettings);
                            }

                            if (false == href.Equals(here, StringComparison.OrdinalIgnoreCase))
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Gave up waiting for [{href}] after [{arriving.ElapsedMilliseconds}] ms, the page showing is [{here}]", browser, GPALObjectType.Browser);

                            browser.CurrentUOW = saveUOW;
                            browser.BrowserSettings.ObeyRobotsTxt = obeySave;
                            browser.BrowserSettings.CurrentURL = href;
                        }
                        else
                        {
                            // can be only one next page button elems[0] - on ebay, the dimensions are weird > 10k
                            elems[0].Click();
                            //CheckDocumentReady(browser);
                        }

                        clicked = true;
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Exception thrown", browser, GPALObjectType.Browser, ex);
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to find Next Page Button", browser, GPALObjectType.Browser);
                }
            }
            else // infinite scroll, go to page end and wait for load
            {
                if (false == browser.BrowserSettings.UseHeadless && (true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))
                    HardwareHelper.SendKey(VK_END);
                else
                    _ = browser.PageEnd;

                CheckDocumentReady(browser);

                clicked = true;
            }

            browser.WithAllThatMatch(withAllThatMatchSave);

            return clicked;
        }

        /// <summary>
        /// Finds the elements matching the unit of work's selectors, arranges them into a grid of rows and columns,
        /// and fills each editable element with the corresponding token from <paramref name="tokens"/> using
        /// <paramref name="writeMode"/> (Overwrite, Insert or Append).<br/>
        /// If more rows of tokens remain than were consumed, invokes CallAfterFillIn (if set) and repeats for the
        /// next page/iteration until all tokens have been consumed.
        /// </summary>
        /// <param name="browser">Browser whose CurrentUOW selectors are used to locate the input elements</param>
        /// <param name="tokens">Grid (rows of columns) of string values to fill into the matched elements</param>
        /// <param name="writeMode">How to write each token: Overwrite, Insert or Append</param>
        public static void FillInWithTokens(Browser browser, IGPALGrid<string> tokens, WriteMode writeMode)
        {
            int tokenIdx = 0;
            List<ReadOnlyCollection<GPALElement>> rowsOfColumns = new List<ReadOnlyCollection<GPALElement>>();
            List<ReadOnlyCollection<GPALElement>> rowsOfElements = new List<ReadOnlyCollection<GPALElement>>();
            // a column-wise list of the selector info to match with the column of matched/returned elements to know how to interact with that column of elemnts
            // of course selectors are always a 'column'
            List<SmallSelectorNode> interactionInfo = new List<SmallSelectorNode>();

            while (true)
            {

                // waitfor did not match if returns false
                int rowCnt = 0;
                int lastRowCnt = 0;
                int elementIdx = 0;

                interactionInfo.Clear();    // are we looping? of course, the CallAfterFillIn method workflow has returned for the next iteration.
                rowsOfElements.Clear();
                rowsOfColumns.Clear();
                browser.CurrentUOW.MatchedRowIndexes.Clear();
                browser.CurrentUOW.ColCount = 0;
                browser.CurrentUOW.RowCount = 0;    // Math.Max below only ever raises it, so a pass that finds fewer elements than the last would size the grid for rows it has no elements to fill
                browser.CurrentUOW.PartialMatch = false;    // only ever |= true below, so one pass that did not match everything would put every later pass down the partial-match path

                foreach (Selector sel in browser.CurrentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != sel.SelectorType)
                        continue;   // we don't look up data literals

                    // TODO: what to do on partial match?
                    List<GPALElement> matchedElems = null;
                    ReadOnlyCollection<GPALElement> tmpElems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, sel, out bool matchedAll, out matchedElems);
                    rowsOfElements.Add(tmpElems);
                    browser.CurrentUOW.ColCount++;

                    if (false == matchedAll)
                        browser.CurrentUOW.PartialMatch |= true;

                    if (0 < tmpElems?.Count && true == matchedAll)
                    {
                        // RowCount is the total count of elements found or just the WithAllThatMatch row count
                        browser.CurrentUOW.RowCount = Math.Max(browser.CurrentUOW.RowCount, (int.MaxValue == browser.CurrentUOW.WithAllThatMatch ? tmpElems.Count : browser.CurrentUOW.WithAllThatMatch));
                    }
                    else if (0 < matchedElems?.Count && false == matchedAll)
                    {
                        // partial match
                        int tmpIdx = 0;
                        // add the indices of the matched items
                        // when we build the grid, we will only get rows that matched
                        // NO 'AND' on selector MATCH
                        // only 'OR' operator, all those that match on different selectors
                        foreach (GPALElement webElement in matchedElems)
                            if (-1 != (tmpIdx = tmpElems.IndexOf(webElement)))
                                if (false == browser.CurrentUOW.MatchedRowIndexes.Contains(new KeyValuePair<int, string>(tmpIdx, sel.AttributeName)))
                                    browser.CurrentUOW.MatchedRowIndexes.Add(tmpIdx, sel.AttributeName);

                        // matched row indexes is the row count we will extract data from on partial matches, so however many indices we have, that is our MAX row count
                        // up to WithAllThatMatch row count if not int.MaxValue
                        browser.CurrentUOW.RowCount = (int.MaxValue == browser.CurrentUOW.WithAllThatMatch ? browser.CurrentUOW.MatchedRowIndexes.Count : browser.CurrentUOW.WithAllThatMatch);
                    }

                    // if we returned elements, add an entry for this selector in interactionInfo, we consume it below
                    if (0 < matchedElems?.Count || 0 < tmpElems?.Count)
                        interactionInfo.Add(new SmallSelectorNode() { InteractionType = sel.InteractionType, OffsetX = sel.OffsetX, OffsetY = sel.OffsetY, DeltaX = sel.DeltaX, DeltaY = sel.DeltaY });
                }

                // we have partial matches, so construct the grid only from matched rows
                if (true == browser.CurrentUOW.PartialMatch)
                {
                    List<GPALElement> newColumn = new List<GPALElement>();

                    browser.CurrentUOW.RowCount = browser.CurrentUOW.MatchedRowIndexes.Count;
                    // get a list of all the elements in one column
                    // iterate thru each column pulling out matched rows
                    // create the return grid as only matched elements
                    foreach (ReadOnlyCollection<GPALElement> webElements in rowsOfElements)
                    {
                        // create a newCOlumn from all elements via indexes where rows 'matched' on some selector value
                        // we have multiple selectors contributing to matched rows based upon that selectors 'match' criteria
                        foreach (KeyValuePair<int, string> idx in browser.CurrentUOW.MatchedRowIndexes)
                            newColumn.Add(webElements[idx.Key]);
                        rowsOfColumns.Add(new ReadOnlyCollection<GPALElement>(newColumn));
                        newColumn = new List<GPALElement>();
                    }
                }
                else
                {
                    foreach (ReadOnlyCollection<GPALElement> webElements in rowsOfElements)
                        rowsOfColumns.Add(webElements);
                }

                // if we are looping, we have a new 'page' and new elements to interact with, our old list is stale
                browser.CurrentUOW.ElementGrid.Clear();

                // we should have one column for each selector (that found elements)
                if (0 != browser.CurrentUOW.ColCount)
                {
                    for (int cnt = 0; cnt < browser.CurrentUOW.RowCount; cnt++)
                        browser.CurrentUOW.ElementGrid.AddRow(new List<UnitOfWork.ElementNode>(browser.CurrentUOW.ColCount));    // create a row of columns for the number of elements found

                    int columnIdx = 0; ;  // start with the first column in the row

                    // grab a row of columns 
                    // construct our grid by pivoting the data from columns, to rows
                    // iterate each column elements (rows) add an element to each row of the element grid
                    foreach (ReadOnlyCollection<GPALElement> column in rowsOfColumns)
                    {
                        rowCnt = lastRowCnt;    // if we are on subsequent pages, we have to add at the end of the list, up to WithAllThatMatch row count (or all rows)
                        foreach (GPALElement elem in column)
                        {
                            if (true == ElementHelper.IsElementEditable(elem))
                                browser.CurrentUOW.ElementGrid[rowCnt].Add(
                                    new UnitOfWork.ElementNode()
                                    {
                                        GPALElement = elem,
                                        InteractionType = interactionInfo[columnIdx].InteractionType,
                                        OffsetX = interactionInfo[columnIdx].OffsetX,
                                        OffsetY = interactionInfo[columnIdx].OffsetY
                                    });

                            // only add as many rows as was asked for
                            // all or only save up to withallthatmatch row count
                            if (int.MaxValue != browser.CurrentUOW.WithAllThatMatch && rowCnt == browser.CurrentUOW.WithAllThatMatch)   // only save the first one, we do not have the WithAll directive
                                break;

                            rowCnt++;
                        }
                    }
                }

                // TODO: CAVEAT: do we have concept of next page in apps? prolly? if we still have tokens and are done, shouldn't we get a next page or bail
                // BUG: IF WE consume all our tokens, why would we loop again (while true)

                // keep looping while we have tokens
                // but after dealing with the first round of tokens, with more to go
                if (tokenIdx < tokens.Count())
                {
                    // we now have our elements in rows and columns
                    // iterate over the tokens, in rows and columns to fill in the text
                    foreach (List<UnitOfWork.ElementNode> elementNode in browser.CurrentUOW.ElementGrid)    // get one row of elements
                    {
                        elementIdx = 0;
                        List<string> currentRow = tokens[tokenIdx];

                        foreach (string token in currentRow)                                            // iterate over tokens, filling in the corresponding input/textarea webelement
                        {
                            if (elementIdx < elementNode.Count && null != elementNode[elementIdx])
                            {
                                ElementHelper.ScrollIntoView(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].InteractionType);

                                if (true == browser.UseOttoMagic)
                                {
                                    if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                        ElementHelper.HardwareFillInFrom(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                    else
                                        switch (writeMode)
                                        {
                                            case WriteMode.Append:
                                                browser.BrowserSettings.MagicHelper.FillInAppend(elementNode[elementIdx].GPALElement.Css, token.ToString());
                                                break;

                                            case WriteMode.Insert:
                                                browser.BrowserSettings.MagicHelper.FillInInsert(elementNode[elementIdx].GPALElement.Css, token.ToString());
                                                break;

                                            case WriteMode.Overwrite:
                                                browser.BrowserSettings.MagicHelper.FillInOverwrite(elementNode[elementIdx].GPALElement.Css, token.ToString());
                                                break;
                                        }
                                }
                                else if (true == browser.UsePuppeteer)
                                {
                                    if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                        ElementHelper.HardwareFillInFrom(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                    else
                                        switch (writeMode)
                                        {
                                            case WriteMode.Append:
                                                browser.PuppeteerClient.FillInAppend(elementNode[elementIdx].GPALElement.Css).WithText(token.ToString()).Execute();
                                                break;

                                            case WriteMode.Insert:
                                                browser.PuppeteerClient.FillInInsert(elementNode[elementIdx].GPALElement.Css).WithText(token.ToString()).Execute();
                                                break;

                                            case WriteMode.Overwrite:
                                                browser.PuppeteerClient.FillInOverwrite(elementNode[elementIdx].GPALElement.Css).WithText(token.ToString()).Execute();
                                                break;
                                        }
                                }
                                else    // selenium
                                {
                                    if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                        ElementHelper.HardwareFillInFrom(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                    else if (InteractionType.JavaScript == elementNode[elementIdx].InteractionType)
                                        ElementHelper.JavaScriptFillInFrom(browser, elementNode[elementIdx].GPALElement, token.ToString(), writeMode);
                                    else
                                        ElementHelper.SeleniumFillInFrom(browser, elementNode[elementIdx].GPALElement, token.ToString(), writeMode);
                                }

                                elementIdx++;   // increment the column [element] count for each token. if we have a token, we are expecting elements for it.
                            }
                            else
                                // detect if we have too many tokens to consume and publish an information event [entirely possible this scenario is fine]
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unused token [{token}] from tokens [{string.Join(",", currentRow)}] in row [{tokenIdx + 1}]", browser, GPALObjectType.Browser);

                            try
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] text [{token.ToString()}] in element [{elementNode[elementIdx - 1].GPALElement.TagName}]", browser, GPALObjectType.Browser);
                            }
                            catch
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"NOT WRITTEN: [{writeMode}] text [{token.ToString()}] in element [NOT FOUND].", browser, GPALObjectType.Browser);
                            }
                        }

                        // detect if we do not have enough tokens for all the form fields [entirely possible this scenario is fine]
                        // we got here and finished our tokens but have not exhausted our columns [elements]
                        while (elementIdx < elementNode.Count && null != elementNode[elementIdx++])
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No token for control: <[{elementNode[elementIdx - 1].GPALElement.GetType()}]> : [{elementNode[elementIdx - 1].GPALElement.Text}]", browser, GPALObjectType.Browser);
                    }

                    UnitOfWork safeUOW = browser.CurrentUOW;
                    CallIfStatus handled = 0;

                    // CAVEAT: there is no concept of 'handled (1)' vs 'not handled (0)' but definitely can request to exit
                    // TODO: CAVEAT: is cast to dynamic best?
                    if (null != browser.CurrentUOW.CallAfterFillIn && true == rowsOfColumns.Any())
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallAfterFillIn [{browser.CurrentUOW.CallAfterFillIn.Method.Name}]", browser, GPALObjectType.Browser);
                        handled = browser.CurrentUOW.CallAfterFillIn(browser, tokens, tokenIdx);
                    }

                    // CAVEAT: kludge - the call after handler can and probably will set a new current unit of work, but we need our old current unit
                    // the call after handler UOW is no longer in scope, so restore our UOW
                    browser.CurrentUOW = safeUOW;

                    // CAVEAT: elements may go stale because of callafterfillin handler actions
                    // test if elements still valid by testing the first one
                    //try
                    //{
                    //    ((UnitOfWork.ElementNode)(browser.CurrentUOW.ElementGrid[0])[0]).GPALElement.GetAttribute("tag");
                    //} 
                    //catch (Exception ex)   // failed, invalidate elementgrid
                    //{
                    //    browser.CurrentUOW.ElementGrid.Clear();
                    //    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CallAfterFillIn handler [{browser.CurrentUOW.CallAfterFillIn.GetInvocationList()[0].Method.Name}] invalidated browser elements, clearing cache. Exception follows: [{ex.Message}]", browser, GPALObjectType.Browser, ex);
                    //    break;
                    //}

                    if (CallIfStatus.Terminate == handled)
                    {
                        string str = $"CallAfterFillIn handler [{browser.CurrentUOW.CallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException($"{GPAL.MyMethodName()}: " + str);
                    }

                    tokenIdx++;
                }
                else
                    break;

                lastRowCnt += rowCnt;
                // TODO: are we done or do we have to go to the next page?

                // we are out of tokens, break out
                // if we have more tokens, shouldn't we go to another page?
                // CAVEAT: BUG: we are just looping back thru the same selectors and consuming more tokens, so does that mean CallAfterFillIn will do something and this is the behavior we want?
                // or do we break because we have more tokens then elements? or should we loop?
                // why did i put this in a while true loop?
                if (tokenIdx == tokens.Count())
                    break;
            }
        }

        // this is failing after clicks on wells fargo because it's checking faster than the page can load, but i'm reticent to add too much of a delay in jswait
        // since this is not entirely critical, we are just going to not print out any messages about this failing, but we also updated execute javascript to return 'fail' instead of 'complete' on catching an exception
        // that will result in this waiting the full 30 seconds timeout. not sure if that is good or bad, we will see.

        // gap between url reads while waiting to see if a click navigated. Each read is a driver round trip, so this
        // is not a hot loop the way WaitForValueOrTime is
        private const int NavigationPollMs = 50;

        // chromium window handles are the CDP target id behind this prefix; firefox reports its pid in capabilities
        private const string CdpWindowPrefix = "CDwindow-";
        private const string MozProcessIdCapability = "moz:processID";
        /// <summary>
        /// Gets the document ready state from OttoMagic. On nav-error, raise OnFail with GPALFailure.Navigation; an
        /// error page runs no content script, so waiting will not help.<br/>
        /// OttoMagic answers NOTOK until a new page loads, so navigating elsewhere recovers.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <param name="sessionToken">Token identifying this wait, so repeat messages can be suppressed</param>
        /// <returns>The ready state, or what the browser said about the failed navigation</returns>
        static string ReadyStatusOrNavError(Browser browser, string sessionToken)
        {
            string status = browser.MagicHelper.GetReadyStatus(sessionToken)?.Trim('"');

            if (true == status?.ToLower().Contains("nav-error"))
            {
                // GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The page did not load, so there is nothing to work with. [{status}]", browser, GPALObjectType.Browser);
                browser.RaiseOnFail(GPALFailure.Navigation, status);
            }

            return status;
        }
        /// <summary>
        /// Waits for the page to be ready before/after an action, using whichever signal the browser is configured for:
        /// document.readyState (via OttoMagic, Puppeteer or Selenium javascript), or network-idle detection.<br/>
        /// unless <paramref name="overrideSetting"/> is true.
        /// </summary>
        /// <param name="browser">Browser to check</param>
        /// <param name="overrideSetting">When true, waits for document.ready even if BrowserSettings.WaitOnDocumentReady is false</param>
        public static void CheckDocumentReady(Browser browser, bool overrideSetting = false)
        {
            bool hasURL = null != browser?.BrowserSettings.CurrentURL && false == "about:blank".Equals(browser.BrowserSettings.CurrentURL);

            // A loading page rejects scripts, so suppress JavaScript failures while waiting. The finally restores
            // the previous value rather than false, for nested calls and throws.
            bool wasSuppressed = true == browser?.BrowserSettings.SuppressJavaScriptFailures;
            string sessionToken = Guid.NewGuid().ToString();

            if (null != browser)
                browser.BrowserSettings.SuppressJavaScriptFailures = true;

            try
            {
                if ((true == browser?.BrowserSettings.WaitOnDocumentReady && true == hasURL) || true == overrideSetting)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting up to [{browser.BrowserSettings.WaitForDocumentReadyTimeoutMs}] ms on document.ready");

                    // nav-error means the navigation failed; stop instead of polling to the timeout.
                    if (true == browser?.UseOttoMagic)
                        WaitForValueOrTime(browser, "complete, interactive, nav-error", () => ReadyStatusOrNavError(browser, sessionToken), browser.BrowserSettings.WaitForDocumentReadyTimeoutMs);
                    else if (true == browser?.UsePuppeteer)
                        WaitForValueOrTime(browser, "complete, interactive", () => ((PuppeteerClient)browser.PuppeteerClient).GetReadyStatus(sessionToken).Execute<object>(), browser.BrowserSettings.WaitForDocumentReadyTimeoutMs);
                    else    // handles selenium
                        WaitForValueOrTime(browser, "complete, interactive", () => JsCheckReadyState(browser, sessionToken), browser.BrowserSettings.WaitForDocumentReadyTimeoutMs);
                }
                else if (true == browser.BrowserSettings.WaitOnNetworkIdle && true == hasURL)
                {
                    WaitForValueOrTime(browser, "true", () => WaitForNetworkIdle(browser, browser.BrowserSettings.NetworkIdleMaxConnections, browser.BrowserSettings.NetworkIdlePruneMs, sessionToken).ToString(), browser.BrowserSettings.NetworkIdleTimeoutMs, NetworkIdlePollMs);
                    }
                }
            finally
            {
                if (null != browser)
                    browser.BrowserSettings.SuppressJavaScriptFailures = wasSuppressed;
            }
        }

        /// <summary>
        /// Sends a GPALRequest with fetch from inside the page, with the browser's cookies, TLS fingerprint, header
        /// order and anti-bot clearance. A relative path resolves against the current page.
        /// </summary>
        /// <summary>
        /// Runs the request once per row of the unit of work's token source, walking .WithPages pages within each
        /// row, and calls the request's CallAfterFetch handler with that row's pages as soon as they are in.<br/>
        /// With no token source this is one pass of the request exactly as it was written.
        /// </summary>
        /// <param name="browser">Browser whose page issues the requests</param>
        /// <param name="request">The request to issue</param>
        internal static void FetchWithTokens(Browser browser, GPALRequest request)
        {
            IGPALGrid<string> tokens = browser.CurrentUOW.FetchTokens;
            int required = request.TokenCount();
            string name = request.Name ?? request.Path;

            if (null == tokens && 0 < required)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Fetch of [{name}] uses [{required}] tokens but no token source was given. Use .WithTokensFrom before .Fetch.", browser, GPALObjectType.Browser);
            else
            {
                // no token source is still one pass, the request exactly as it was written
                int rowCount = tokens?.Rows ?? 1;

                for (int tokenIdx = 0; tokenIdx < rowCount; tokenIdx++)
                {
                    List<string> row = null == tokens ? null : tokens[tokenIdx];

                    // Skip a row with fewer values than the request has tokens, or an unreplaced token goes out in
                    // the body.
                    if (null != row && row.Count < required)
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Fetch of [{name}] needs [{required}] tokens but row [{tokenIdx + 1}] has [{row.Count}]. Skipping the row.", browser, GPALObjectType.Browser);
                    else
                        FetchRow(browser, request, row, tokenIdx);
                }
            }
        }
        /// <summary>
        /// Fetches every page for one row of tokens, appending each body to the unit of work so .SaveTo still hands
        /// back the whole run, then hands just this row's bodies to the request's CallAfterFetch handler.
        /// </summary>
        /// <param name="browser">Browser whose page issues the requests</param>
        /// <param name="request">The request to issue</param>
        /// <param name="row">Values for the request's numbered tokens, or null when there is no token source</param>
        /// <param name="tokenIdx">The current token row index</param>
        static void FetchRow(Browser browser, GPALRequest request, List<string> row, int tokenIdx)
        {
            int mark = browser.CurrentUOW.FetchResults.Count;
            string name = request.Name ?? request.Path;

            for (int page = 0; page < browser.CurrentUOW.PageCount; page++)
            {
                string where = null == row ? $"Page [{page + 1}]" : $"Row [{tokenIdx + 1}] page [{page + 1}]";

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Fetching [{name}]. {where}.", browser, GPALObjectType.Browser);

                string body = FetchInPage(browser, request, page, row);

                // A failed page does not stop the run. Its error goes into FetchResults for .SaveTo and
                // CallAfterFetch. Every page adds one entry, so position maps to row and page.
                if (null == body)
                {
                    string why = $"Fetch of [{name}] returned nothing on {where.ToLower()}.";

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, why, browser, GPALObjectType.Browser);
                    body = why;
                }
                else if (200 > browser.ServerResponseCode || 300 <= browser.ServerResponseCode)
                {
                    // On a non-2xx status keep the body, which usually explains the error, prefixed with the status
                    // message.
                    string why = $"Fetch of [{name}] returned HTTP [{browser.ServerResponseCode}] on {where.ToLower()}.";

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, why, browser, GPALObjectType.Browser);
                    body = why + Environment.NewLine + body;
                }

                browser.CurrentUOW.FetchResults.Add(body);
            }

            if (null != request.AfterFetch)
            {
                UnitOfWork safeUOW = browser.CurrentUOW;
                List<string> results = browser.CurrentUOW.FetchResults.GetRange(mark, browser.CurrentUOW.FetchResults.Count - mark);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallAfterFetch [{request.AfterFetch.Method.Name}]", browser, GPALObjectType.Browser);

                CallIfStatus handled = request.AfterFetch(browser, results, browser.CurrentUOW.FetchTokens, tokenIdx);

                // the handler can and probably will start its own unit of work, so restore ours
                browser.CurrentUOW = safeUOW;

                if (CallIfStatus.Terminate == handled)
                {
                    string str = $"CallAfterFetch handler [{request.AfterFetch.Method.Name}] requested program termination.";
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                    throw new GPALException($"{GPAL.MyMethodName()}: " + str);
                }
            }
        }
        /// <param name="browser">Browser whose page issues the request</param>
        /// <param name="request">The request to issue</param>
        /// <param name="page">Zero based page number, added to the request's page parameter</param>
        /// <param name="row">Values for the request's numbered tokens, or null when there is no token source</param>
        /// <returns>The response body, or null if the fetch could not be made</returns>
        internal static string FetchInPage(Browser browser, GPALRequest request, int page, List<string> row)
        {
            // Resolve the url, body and headers for this page and token row here; the engines only see a finished
            // url and plain values.
            string envelope = FetchEnvelope(browser, request.ResolveUrl(page, row), request.Method, request.ResolveBody(page, row), request.ContentType, request.HeaderPairs(page, row), false);

            return UnpackFetchResponse(browser, envelope);
        }
        /// <summary>
        /// Runs the fetch on the current engine and returns the page's envelope unchanged. The one place that knows
        /// how each engine is called.
        /// </summary>
        /// <param name="browser">Browser whose page issues the request</param>
        /// <param name="url">Url to request, relative to wherever the browser is or absolute</param>
        /// <param name="method">HTTP method, GET when null</param>
        /// <param name="body">Request body, or null for none</param>
        /// <param name="contentType">Content type of the body</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="asBytes">True to bring the body back base64 encoded, which is what a file needs</param>
        /// <returns>The envelope the page built, or null if the fetch could not be made</returns>
        static string FetchEnvelope(Browser browser, string url, string method, string body, string contentType, string[] headers, bool asBytes)
        {
            string envelope = null;

            if (true == browser.UseSelenium)
            {
                // Selenium throws when the page navigates away before the fetch answers ("Document was unloaded"),
                // where the other engines hand back a failure. Build the same failure envelope the page would.
                try
                {
                    envelope = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteAsyncScript(
                        FetchSetupScript(url, method, body, contentType, headers, asBytes) + @"
                        var done = arguments[arguments.length - 1];

                        fetch(target.toString(), options)
                            .then(gpalEnvelope)
                            .then(done)
                            .catch(function (error) { done(gpalFailure(error)); });
                    ") as string;
                }
                catch (JavaScriptException ex)
                {
                    envelope = Newtonsoft.Json.JsonConvert.SerializeObject(new { status = 0, body = (string)null, encoding = (string)null, lastModified = (string)null, error = $"{ex.Message} requesting {url}" });
                }
            }
            else if (true == browser.UsePuppeteer)
                envelope = browser.PuppeteerClient
                    .Fetch(url)
                    .WithVerb(method)
                    .WithBody(body)
                    .WithContentType(contentType)
                    .WithHeaders(headers)
                    .WithBytes(asBytes)
                    .Execute();
            else if (true == browser.UseOttoMagic)
                envelope = browser.MagicHelper.Fetch(url, method, body, contentType, headers, asBytes);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Fetch is not implemented for [{browser.BrowserSettings.AutomationEngine}] yet.", browser, GPALObjectType.Browser);

            return envelope;
        }
        /// <summary>
        /// Reads the user agent from the running browser, not the launch settings.<br/>
        /// Some sites bind the session to it: Sinatra's Rack::Protection::SessionHijacking rejects the cookie when a
        /// later request sends a different user agent.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <returns>The user agent, or null when the engine could not answer</returns>
        internal static string LiveUserAgent(Browser browser)
        {
            string retVal = null;

            if (true == browser.UseSelenium)
                retVal = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteScript("return navigator.userAgent") as string;
            else if (true == browser.UsePuppeteer)
                retVal = browser.PuppeteerClient.GetUserAgent().Execute();
            else if (true == browser.UseOttoMagic)
                retVal = Unquoted(browser.MagicHelper.GetUserAgent());

            return retVal;
        }

        /// <summary>
        /// Removes the JSON quotes from a string returned through native messaging. A site that hashes the header
        /// needs exactly what the browser sent.
        /// </summary>
        /// <param name="value">What the endpoint answered</param>
        /// <returns>The value as the browser has it</returns>
        static string Unquoted(string value)
        {
            string retVal = value;

            while (false == string.IsNullOrEmpty(retVal) && true == retVal.StartsWith("\"") && true == retVal.EndsWith("\"") && 1 < retVal.Length)
            {
                string unwrapped = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

                if (unwrapped == retVal)
                    break;

                retVal = unwrapped;
            }

            return retVal;
        }
        /// <summary>
        /// Builds the Accept-Language header the browser sends, from navigator.languages.<br/>
        /// A site can bind the session to it like the user agent. Browsers expose the list, not the header, so
        /// <see cref="AcceptLanguageFrom"/> applies each browser's weighting.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <returns>The header value, or null when the engine could not answer</returns>
        internal static string LiveAcceptLanguage(Browser browser)
        {
            string languages = null;
            string retVal = null;

            if (true == browser.UseSelenium)
                languages = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteScript("return navigator.languages.join(',')") as string;
            else if (true == browser.UsePuppeteer)
                languages = browser.PuppeteerClient.GetLanguages().Execute();
            else if (true == browser.UseOttoMagic)
                languages = Unquoted(browser.MagicHelper.GetLanguages());

            if (false == string.IsNullOrWhiteSpace(languages))
                retVal = AcceptLanguageFrom(languages.Split(','), browser.BrowserSettings.BrowserType);

            return retVal;
        }

        /// <summary>
        /// Builds Accept-Language from a language list as the browser does: the first has no q and each next drops
        /// 0.1, giving en-US,en;q=0.9. Firefox lowercases the tags: en-us,en;q=0.9.<br/>
        /// Both copied from real traffic. A site hashing this header rejects a wrongly cased tag like a missing one.
        /// </summary>
        /// <param name="languages">The languages, most preferred first</param>
        /// <param name="browserType">Which browser's spelling to apply</param>
        /// <returns>The header value</returns>
        static string AcceptLanguageFrom(string[] languages, Enums.BrowserType browserType)
        {
            List<string> weighted = new List<string>();

            for (int index = 0; index < languages.Length; index++)
            {
                string language = languages[index].Trim();

                if (true == string.IsNullOrEmpty(language))
                    continue;

                if (Enums.BrowserType.FireFox == browserType)
                    language = language.ToLowerInvariant();

                if (0 == index)
                    weighted.Add(language);
                else
                {
                    // One decimal place. Drop a language whose weight reaches 0; q=0 tells the server never to use it.
                    double quality = Math.Round(1.0 - (0.1 * index), 1);

                    if (0 < quality)
                        weighted.Add($"{language};q={quality.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }

            return string.Join(",", weighted);
        }
        /// <summary>
        /// Fetches a url from inside the page and returns its bytes, with the browser's cookies, TLS fingerprint and
        /// clearance. Nothing navigates, so no tab or download prompt opens and the page stays put.<br/>
        /// The status goes in ServerResponseCode like a navigation's, so an If-Modified-Since caller can check for
        /// 304.
        /// </summary>
        /// <param name="browser">Browser whose page issues the request</param>
        /// <param name="url">Url of the file to fetch</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="lastModified">Answers the response's Last-Modified header, null when the server sent none</param>
        /// <returns>The file's bytes, or null when the fetch could not be made or the response carried no body</returns>
        internal static byte[] FetchBytes(Browser browser, string url, string[] headers, out string lastModified)
        {
            byte[] bytes = null;
            // Only GPAL calls this and every caller has a fallback, so failures are DEBUG, not ERROR.
            JObject response = UnpackFetchEnvelope(browser, FetchEnvelope(browser, url, "GET", null, null, headers, true), false);
            string encoded = response?["body"]?.ToString();

            lastModified = response?["lastModified"]?.ToString();

            if (false == string.IsNullOrEmpty(encoded))
                bytes = Convert.FromBase64String(encoded);

            return bytes;
        }
        /// <summary>
        /// Stores the envelope's status in ServerResponseCode, as a navigation does, and returns the body.
        /// </summary>
        /// <param name="browser">Browser that issued the request, whose ServerResponseCode is set</param>
        /// <param name="envelope">What the engine returned, or null when the fetch could not be made</param>
        /// <returns>The response body, or null if the fetch could not be made</returns>
        static string UnpackFetchResponse(Browser browser, string envelope)
        {
            return UnpackFetchEnvelope(browser, envelope, true)?["body"]?.ToString();
        }
        /// <summary>
        /// Parses what the page sent back and records the status on the browser the same way a navigation records
        /// one. Every engine answers with the same envelope, so this is the only place that knows its shape.
        /// </summary>
        /// <param name="browser">Browser that issued the request, whose ServerResponseCode is set</param>
        /// <param name="envelope">What the engine returned, or null when the fetch could not be made</param>
        /// <param name="requested">True when the workflow asked for this fetch. GPAL's own fetches have a fallback
        /// behind them, so their failures are published as DEBUG rather than ERROR</param>
        /// <returns>The parsed envelope, or null when nothing came back or its shape was not understood</returns>
        static JObject UnpackFetchEnvelope(Browser browser, string envelope, bool requested)
        {
            JObject response = null;
            GPALEventType failure = true == requested ? GPALEventType.ERROR : GPALEventType.DEBUG;

            // Reset ServerResponseCode to 0 so a failed fetch does not leave the previous 200 or 304.
            browser.ServerResponseCode = 0;

            if (true == string.IsNullOrEmpty(envelope))
                GPAL.PublishSimpleEvent(failure, $"[{browser.BrowserSettings.AutomationEngine}] returned nothing for the fetch, so the page never answered", browser, GPALObjectType.Browser);
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Fetch envelope [{envelope}]", browser, GPALObjectType.Browser);

                // Check the envelope is a JSON object before parsing; if not, publish what arrived.
                if (false == envelope.TrimStart().StartsWith("{"))
                    GPAL.PublishSimpleEvent(failure, $"Fetch response was not understood. Received [{envelope}]", browser, GPALObjectType.Browser);
                else
                {
                    response = JObject.Parse(envelope);
                    browser.ServerResponseCode = response["status"]?.Value<int>() ?? 0;

                    // An error field means the page could not make the request; publish the reason so a refusal is
                    // distinguishable from a bug.
                    if (false == string.IsNullOrEmpty(response["error"]?.ToString()))
                        GPAL.PublishSimpleEvent(failure, $"The page could not make the request [{response["error"]}]", browser, GPALObjectType.Browser);
                }
            }

            return response;
        }
        /// <summary>
        /// Builds the JavaScript that turns the request into fetch's url and options. Each engine adds its own
        /// promise wait.<br/>
        /// Values are inserted as JSON literals so quotes or braces cannot break the script.
        /// </summary>
        /// <param name="url">Url to request, relative or absolute</param>
        /// <param name="method">HTTP method, GET when null</param>
        /// <param name="body">Request body, or null for none</param>
        /// <param name="contentType">Content type of the body</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="asBytes">True to bring the body back base64 encoded, which is what a file needs</param>
        /// <returns>Javascript leaving "target", "options" and "gpalEnvelope" in scope</returns>
        internal static string FetchSetupScript(string url, string method, string body, string contentType, string[] headers, bool asBytes)
        {
            return $@"
                var target = new URL({Newtonsoft.Json.JsonConvert.SerializeObject(url)}, location.href);
                var reqBody = {Newtonsoft.Json.JsonConvert.SerializeObject(body)};
                var contentType = {Newtonsoft.Json.JsonConvert.SerializeObject(contentType)};
                var headerPairs = {Newtonsoft.Json.JsonConvert.SerializeObject(headers ?? new string[0])};
                var asBytes = {(true == asBytes ? "true" : "false")};

                var options = {{ method: {Newtonsoft.Json.JsonConvert.SerializeObject(method ?? "GET")}, credentials: 'include', headers: {{}} }};
                for (var h = 0; h < headerPairs.length; h += 2)
                    options.headers[headerPairs[h]] = headerPairs[h + 1];

                if (reqBody) {{
                    options.body = reqBody;
                    if (!options.headers['Content-Type'])
                        options.headers['Content-Type'] = contentType;
                }}

                // One JSON envelope for every engine. With asBytes the body is read as an arraybuffer and base64
                // encoded, since text() mangles non-utf-8 bytes. Promises, not await, so it serves both Selenium's
                // callback and Puppeteer's awaited expression. String.fromCharCode runs over 32 KB slices; one call
                // with hundreds of thousands of arguments overflows the stack.
                function gpalEnvelope(response) {{
                    return (asBytes
                        ? response.arrayBuffer().then(function (buffer) {{
                            var bytes = new Uint8Array(buffer), binary = '', slice = 0x8000;
                            for (var i = 0; i < bytes.length; i += slice)
                                binary += String.fromCharCode.apply(null, bytes.subarray(i, i + slice));
                            return btoa(binary);
                        }})
                        : response.text()
                    ).then(function (payload) {{
                        return JSON.stringify({{
                            status: response.status,
                            body: payload,
                            encoding: asBytes ? 'base64' : null,
                            lastModified: response.headers.get('Last-Modified')
                        }});
                    }});
                }}

                // gpalFailure returns the envelope with status 0 and the error, so C# can tell a refusal from a bug.
                function gpalFailure(error) {{
                    return JSON.stringify({{
                        status: 0,
                        body: null,
                        encoding: null,
                        lastModified: null,
                        error: String((error && error.message) || error) + ' requesting ' + target.toString()
                    }});
                }}
            ";
        }
        /// <summary>
        /// Executes javascript in the page and returns the raw result object (no string conversion).<br/>
        /// Routed through OttoMagic's ExecuteJavaScript, Puppeteer's ExecuteJavaScript or Selenium's IJavaScriptExecutor
        /// depending on the browser's automation engine.
        /// </summary>
        /// <param name="executeMe">The javascript to execute</param>
        /// <param name="browser">Browser to execute the javascript in</param>
        /// <param name="args">Optional arguments passed to the script; OttoMagic does not pass them</param>
        /// <returns>The raw result of the javascript execution, or null on failure</returns>
        public static object ExecuteJavaScriptObj(string executeMe, IBrowser browser, params object[] args)
        {
            dynamic retVal = null;

            try
            {
                if (browser.UseOttoMagic)
                {
                    retVal = browser.MagicHelper.ExecuteJavaScript(executeMe);
                }
                else if (true == browser.UsePuppeteer)
                {
                    retVal = browser.PuppeteerClient.ExecuteJavaScript(executeMe).WithParameters(args).Execute<object>();
                    retVal = retVal.result.value;
                }
                else // since chrome 136, dynamic javascript execution, even by a browser extension, is not possible. has to be baked in code.
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)browser.BrowserDriver;
                    retVal = js.ExecuteScript(executeMe, args);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Use this browser's SuppressJavaScriptFailures; a non-Browser argument always publishes.
                bool suppressed = true == (browser as Browser)?.BrowserSettings.SuppressJavaScriptFailures;

                if (false == suppressed)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $@"Failed to execute javascript. Continuing.", browser, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
        // NOTE: if this fails we could fallback to puppeteer, but could we do anything with selenium if this doesn't work?
        /// <summary>
        /// Executes javascript in the page and returns the result as a string.<br/>
        /// Routed through Puppeteer's ExecuteJavaScript or Selenium's IJavaScriptExecutor depending on the browser's
        /// automation engine. Returns "fail" if execution throws.
        /// </summary>
        /// <returns>The string result of the javascript execution, or "fail" on failure</returns>
        /// <example>
        /// <code>
        /// string title = BrowserHelper.ExecuteJavaScript(browser, "return document.title");
        /// </code>
        /// </example>
        /// <summary>
        /// Returns a script result as a plain value on every engine, unwrapping Puppeteer's CDP RemoteObject and any
        /// JSON encoding.
        /// </summary>
        /// <param name="result">What the engine handed back.</param>
        /// <returns>The value the script returned.</returns>
        private static string UnwrapScriptResult(string result)
        {
            string retVal = result;


            // Unwrap only a RemoteObject: type and value, with no keys beyond type, value, description, className
            // and subtype. Any other object is the script's own return value.
            if (false == string.IsNullOrWhiteSpace(retVal) && true == retVal.TrimStart().StartsWith("{"))
            {
                try
                {
                    Newtonsoft.Json.Linq.JObject remote = Newtonsoft.Json.Linq.JObject.Parse(retVal);

                    if (null != remote["type"] && null != remote["value"]
                        && remote.Properties().All(each => "type" == each.Name || "value" == each.Name || "description" == each.Name || "className" == each.Name || "subtype" == each.Name))
                    {
                        Newtonsoft.Json.Linq.JToken value = remote["value"];

                        retVal = Newtonsoft.Json.Linq.JTokenType.String == value.Type
                            ? value.ToString()
                            : Newtonsoft.Json.JsonConvert.SerializeObject(value);
                    }
                }
                catch (Exception)
                {
                    // If unwrapping fails, return the result unchanged.
                }
            }

            // Decode JSON string layers until it is no longer quoted.
            while (false == string.IsNullOrEmpty(retVal) && retVal.StartsWith("\"") && retVal.EndsWith("\""))
            {
                try
                {
                    string peeled = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

                    if (peeled == retVal)
                        break;

                    retVal = peeled;
                }
                catch (Exception)
                {
                    break;   // not an encoded string after all, so it is the value already
                }
            }

            return retVal;
        }

        /// <summary>
        /// Runs a script and returns its result.
        /// <br/><br/>
        /// Selenium runs it as a function body and needs a return. OttoMagic and Puppeteer evaluate it like eval,
        /// where V8 rejects a return; when they report "Illegal return statement", the script is wrapped in a
        /// function and rerun. Either style works on all three.
        /// <br/><br/>
        /// The script text is never inspected.
        /// </summary>
        /// <param name="browser">Browser to run the script in</param>
        /// <param name="executeMe">The script</param>
        /// <param name="args">Arguments the script refers to, where the engine supports them</param>
        /// <returns>What the script produced</returns>
        public static string ExecuteJavaScript(IBrowser browser, string executeMe, params object[] args)
        {
            string retVal = RunScript(browser, executeMe, args);

            // V8 raises this message only for a return outside a function
            if (false == browser.UseSelenium && true == retVal?.Contains("Illegal return statement"))
                retVal = RunScript(browser, "(function(){" + Environment.NewLine + executeMe + Environment.NewLine + "})()", args);

            return retVal;
        }

        private static string RunScript(IBrowser browser, string executeMe, params object[] args)
        {
            dynamic retVal = null;

            try
            {
                if (browser.UseOttoMagic)
                {
                    retVal = browser.MagicHelper.ExecuteJavaScript(executeMe);
                }
                else if (true == browser.UsePuppeteer)
                {
                    if (true == args.Any())
                        retVal = browser.PuppeteerClient.ExecuteJavaScript(executeMe).WithParameters(args).Execute<object>();
                    else
                        retVal = browser.PuppeteerClient.ExecuteJavaScript(executeMe).Execute<object>();

                    if (Puppeteer.resultExtractors != null && Puppeteer.resultExtractors.TryGetValue(DevToolsMethods.RuntimeEvaluate, out var extractor))
                        retVal = extractor((Newtonsoft.Json.Linq.JObject)retVal.result);
                    else
                        retVal = retVal.result.ToObject();
                }
                else // since chrome 136, dynamic javascript execution, even by a browser extension, is not possible. has to be baked in code.
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)browser.BrowserDriver;
                    if (true == args?.Any())
                        retVal = js.ExecuteScript(executeMe, args);
                    else
                        retVal = js.ExecuteScript(executeMe);


                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Use this browser's SuppressJavaScriptFailures; a non-Browser argument always publishes.
                bool suppressed = true == (browser as Browser)?.BrowserSettings.SuppressJavaScriptFailures;

                if (false == suppressed)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $@"Failed to execute javascript. Continuing.", browser, GPALObjectType.Browser, ex);
                retVal = "fail";
            }

            return UnwrapScriptResult(retVal?.ToString());
        }

        /// <summary>
        /// Returns the current page URL, queried via OttoMagic, Puppeteer, or the Selenium WebDriver
        /// depending on the browser's automation engine.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings of the browser to query</param>
        /// <returns>The current page URL</returns>
        public static string GetCurrentUrl(BrowserSettings browserSettings)
        {
            string currentUrl = null;

            if (true == browserSettings.UseOttoMagic)
                currentUrl = browserSettings.MagicHelper.GetCurrentUrl();
            else if (true == browserSettings.UsePuppeteer)
                currentUrl = browserSettings.PuppeteerClient.GetCurrentUrl().Execute();
            else
                currentUrl = browserSettings.Browser.BrowserDriver.Url;

            // OttoMagic and Puppeteer return the raw JSON string, quotes included. Deserialize rather than trim so
            // escaped quotes in the url are unescaped.
            if (true == currentUrl?.StartsWith("\"") && true == currentUrl.EndsWith("\""))
                currentUrl = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(currentUrl);

            return currentUrl;
        }
        /// <summary>
        /// Use javascript to check the page document.ready state, only called from one place that does the timing checks
        /// While only used in GPAL for Selenium checking, it is defensively coded to handle all Automation Engines
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="sessionToken">Token used to suppress repeated log messages within the same wait session</param>
        /// <returns></returns>
        public static string JsCheckReadyState(dynamic browser, string sessionToken)
        {
            string readyState = "loading";

            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                supressedMessage = false;
                lastSessionToken = sessionToken;
            }

            try
            {
                if (browser.UseOttoMagic)
                {
                    readyState = browser.BrowserSettings.MagicHelper.GetReadyStatus();
                    if (null == readyState)
                        readyState = @"loading";
                    else
                        readyState = readyState.Trim('"');
                }
                else
                {
                    BrowserSettings settings = browser.BrowserSettings;

                    // Selenium on Chrome and Edge reads it on GPAL's own connection to the browser, so the check
                    // runs nothing through chromedriver that the page can catch
                    if (false == browser.UsePuppeteer && BrowserType.FireFox != settings.BrowserType)
                        settings.TabScripts ??= SeleniumTabScripts.Start(settings);

                    if (false == browser.UsePuppeteer && null != settings.TabScripts)
                        readyState = settings.TabScripts.ReadyState(CurrentTargetId(settings.BrowserDriver)) ?? "loading";
                    else
                    {
                        bool withReturn = false == browser.UsePuppeteer;

                        // use javascript to get our html document location, but it's off by 2 pixels compared to uiautomation. uiautomation is unreliable here
                        readyState = ExecuteJavaScript(browser, $"{(true == withReturn ? "return " : "")}document.readyState");
                    }
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"Error executing JavaScript",
                    null, GPALObjectType.None, ex);
            }

            string currentErrorMessage = $"Document.Ready status [{readyState}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }

            return readyState;
        }

        /// <summary>
        /// Starts the browser downloading <paramref name="url"/> by sending the tab to it, without waiting for a
        /// page, since a file never becomes one. Chrome and Edge take CDP Page.navigate, which returns at once.
        /// Firefox has no CDP, and its driver waits out the whole page-load limit on a download, so the limit is
        /// cut to <see cref="DownloadNavigationMs"/> for this one navigation and put back after.<br/>
        /// Whether the file arrived is for the download watcher to say.
        /// </summary>
        /// <param name="url">Absolute http or https url to download</param>
        /// <param name="browserSettings">BrowserSettings of the browser that downloads it</param>
        internal static void SeleniumStartDownload(string url, BrowserSettings browserSettings)
        {
            IWebDriver driver = browserSettings.BrowserDriver;

            if (BrowserType.FireFox != browserSettings.BrowserType)
            {
                object answer = ExecuteCdp(driver, "Page.navigate", new Dictionary<string, object> { { "url", url } });
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Sent the tab to [{url}] to download it, the browser answered [{Newtonsoft.Json.JsonConvert.SerializeObject(answer)}]", browserSettings.Browser, GPALObjectType.Browser);
            }
            else
            {
                try
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(DownloadNavigationMs);
                    driver.Navigate().GoToUrl(url);
                }
                catch (WebDriverTimeoutException)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No page came back for [{url}] within [{DownloadNavigationMs}] ms, as expected of a file", browserSettings.Browser, GPALObjectType.Browser);
                }
                finally
                {
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromMilliseconds(browserSettings.WaitForDocumentReadyTimeoutMs);
                }
            }
        }

        /// <summary>
        /// How long Firefox's driver waits on a navigation that starts a download. The download goes on after the
        /// driver stops waiting; this only bounds how long the driver holds the command.
        /// </summary>
        private const int DownloadNavigationMs = 3000;

        // written by grok
        /// <summary>
        /// Navigates the Selenium WebDriver to <paramref name="url"/> and inspects the browser's performance logs
        /// (Network.responseReceived events) to determine the HTTP status code of the navigation, storing it in
        /// browserSettings.ServerResponseCode (0 if no status could be determined, -1 on error).
        /// </summary>
        /// <param name="url">URL to navigate to</param>
        /// <param name="browserSettings">BrowserSettings of the browser to navigate, used to read performance logs and store the resulting status code</param>
        public static void SeleniumGoToUrl(GPALUrl url, BrowserSettings browserSettings)
        {
            // The browser never sends the #fragment, so the response's url has none; match without it.
            string target = url.ToString();
            int fragmentAt = target.IndexOf('#');
            string normalizedTarget = (0 <= fragmentAt ? target.Substring(0, fragmentAt) : target).TrimEnd('/').ToLowerInvariant();

            try
            {
                // 1. Perform navigation. The referrer goes with the navigation itself, which only CDP can send; the
                // driver's own navigate has no referrer. It is the one WithUseReferrer asked for, or the stealth
                // Google one, on every GoTo and Get until cleared. Firefox has no CDP, so it navigates without one.
                string referrer = BrowserType.FireFox == browserSettings.BrowserType ? null : browserSettings.Referrer;

                if (null == referrer)
                    browserSettings.BrowserDriver.Navigate().GoToUrl(url);
                else
                {
                    ExecuteCdp(browserSettings.BrowserDriver, "Page.navigate", new Dictionary<string, object> { { "url", url.Url }, { "referrer", referrer } });

                    // CDP returns once the new page commits, the driver's navigate once it has loaded. With both waits
                    // off the workflow asked for none, so it goes on from the commit.
                    if (true == browserSettings.WaitOnDocumentReady || true == browserSettings.WaitOnNetworkIdle)
                        CheckDocumentReady((Browser)browserSettings.Browser, true);
                }

                // Give Chrome a brief moment for network events (helps on fast 304s / cached pages)
                System.Threading.Thread.Sleep(400); // 400–800 ms usually enough; can be replaced with WebDriverWait if preferred

                // 2. Fetch performance logs (Chrome/Edge only — geckodriver does not support getLog)
                if (browserSettings.BrowserType != BrowserType.FireFox)
                {

                var performanceLogs = browserSettings.BrowserDriver.Manage().Logs.GetLog("performance");

                // The browser can land on another url than the one asked for, as when Chrome upgrades http to https,
                // so the response is looked for under the landed url too. It is read over CDP, since chromedriver's
                // own url read runs a script the page can catch.
                string landed = CurrentTargetInfo(browserSettings.BrowserDriver)?["url"]?.ToString() ?? "";
                int landedFragmentAt = landed.IndexOf('#');
                string normalizedLanded = (0 <= landedFragmentAt ? landed.Substring(0, landedFragmentAt) : landed).TrimEnd('/').ToLowerInvariant();

                int? bestStatus = null;
                string bestMatchedUrl = null;
                int candidatePriority = int.MaxValue;
                // the page responses the log did hold, named when none matched the target
                List<string> documentUrls = new List<string>();

                foreach (var entry in performanceLogs)
                {
                    string message = entry.Message;

                    if (message.IndexOf("Network.responseReceived", StringComparison.Ordinal) == -1)
                        continue;

                    if (message.IndexOf("\"type\":\"Document\"", StringComparison.Ordinal) != -1)
                        documentUrls.Add((string)Newtonsoft.Json.Linq.JObject.Parse(message).SelectToken("message.params.response.url"));

                    // Quick filter to reduce sub-resource noise
                    if (message.IndexOf("\"url\":\"", StringComparison.Ordinal) == -1 ||
                        (message.IndexOf(normalizedTarget, StringComparison.OrdinalIgnoreCase) == -1 &&
                         message.IndexOf(normalizedLanded, StringComparison.OrdinalIgnoreCase) == -1))
                        continue;

                    // Extract status
                    var statusMatch = System.Text.RegularExpressions.Regex.Match(
                        message,
                        @"""status(?:Code)?""\s*:\s*(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant
                    );

                    if (!statusMatch.Success)
                        continue;

                    int status;
                    if (!int.TryParse(statusMatch.Groups[1].Value, out status))
                        continue;

                    if (status < 100 || status > 599)
                        continue;

                    // Priority: lower number = better candidate
                    int priority;
                    if (status == 304)
                        priority = 0;           // jackpot: cache hit
                    else if (status >= 200 && status < 300)
                        priority = 10;          // success
                    else if (status >= 300 && status < 400)
                        priority = 20;          // redirect (often intermediate)
                    else if (status >= 400 && status < 500)
                        priority = 30;          // client error (useful to know)
                    else if (status >= 500)
                        priority = 40;          // server error
                    else
                        priority = 50;

                    bool shouldUpdate = false;

                    if (!bestStatus.HasValue)
                    {
                        shouldUpdate = true;
                    }
                    else if (priority < candidatePriority)
                    {
                        shouldUpdate = true;
                    }
                    else if (priority == candidatePriority && status == 304)
                    {
                        shouldUpdate = true;
                    }

                    if (shouldUpdate)
                    {
                        bestStatus = status;
                        candidatePriority = priority;

                        // Extract matched URL for debug (optional)
                        var urlMatch = System.Text.RegularExpressions.Regex.Match(message, @"""url"":""([^""]+)""");
                        if (urlMatch.Success)
                        {
                            bestMatchedUrl = urlMatch.Groups[1].Value;
                        }
                    }
                }

                if (bestStatus.HasValue)
                {
                    browserSettings.ServerResponseCode = bestStatus.Value;

                    // Optional: log what we decided (uncomment for debugging)
                    /*
                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEBUG,
                        $"Captured final status {bestStatus.Value} (priority {candidatePriority}) for ~{bestMatchedUrl ?? "unknown"}",
                        browserSettings,
                        GPALObjectType.Other
                    );
                    */
                }
                else
                {
                    // Rare: no plausible status found despite navigation
                    browserSettings.ServerResponseCode = 0;
                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEBUG,
                        $"No valid HTTP status captured in performance logs for [{url}], [{performanceLogs.Count}] entries held page responses [{string.Join(", ", documentUrls)}]",
                        browserSettings,
                        GPALObjectType.Other
                    );
                }

                } // end Chrome/Edge only block
                else
                {
                    try
                    {
                        var result = ((OpenQA.Selenium.IJavaScriptExecutor)browserSettings.BrowserDriver)
                            .ExecuteScript("return performance.getEntriesByType('navigation')[0]?.responseStatus ?? 0;");
                        browserSettings.ServerResponseCode = result != null && int.TryParse(result.ToString(), out int ffStatus) && ffStatus > 0 ? ffStatus : 0;
                    }
                    catch
                    {
                        browserSettings.ServerResponseCode = 0;
                    }
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                browserSettings.ServerResponseCode = -1;
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"Unexpected error navigating to [{url}]",
                    browserSettings,
                    GPALObjectType.Other,
                    ex
                );
            }
        }

        /// <summary>
        /// Kills all running processes whose name matches any of <paramref name="namesOfProcessesToKill"/>.
        /// </summary>
        /// <param name="namesOfProcessesToKill">One or more process names to kill (without .exe)</param>
        /// <returns>The result of killing the last process name in the list</returns>
        /// <example>
        /// <code>
        /// BrowserHelper.KillProcessesByName("chromedriver", "msedgedriver");
        /// </code>
        /// </example>
        public static KillProcessesResult KillProcessesByName(params string[] namesOfProcessesToKill)
        {
            KillProcessesResult result = null;

            foreach (var name in namesOfProcessesToKill)
                result = KillProcess(name);

            return result;
        }
        /// <summary>
        /// Returns the live user agent string actually presented by the browser's active automation engine
        /// (OttoMagic, Puppeteer, or Selenium), as opposed to <see cref="MagicHelper.GetUserAgentString"/>
        /// which returns a templated user agent built from the locally installed browser version.<br/>
        /// Used by <see cref="TestUrlForPDF"/> and <see cref="DownloadPdfFile"/> so that out-of-band HttpClient
        /// requests present the same User-Agent header as the browser itself.
        /// </summary>
        /// <param name="browser">Browser to query</param>
        /// <returns>The live user agent string, or a templated fallback if it could not be determined</returns>
        public static string GetUserAgent(IBrowser browser)
        {
            string userAgent = null;

            try
            {
                if (true == browser.UseOttoMagic)
                    userAgent = ((Browser)browser).MagicHelper.GetUserAgent();
                else if (true == browser.UsePuppeteer)
                    userAgent = browser.PuppeteerClient.GetUserAgent().Execute<string>();
                else
                    userAgent = ExecuteJavaScript(browser, "return navigator.userAgent");
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get live user agent.", browser, GPALObjectType.Browser, ex);
            }

            if (true == string.IsNullOrEmpty(userAgent))
                userAgent = MagicHelper.GetUserAgentString(browser.BrowserType);

            // OttoMagic and Puppeteer return the raw JSON string, quotes included, and a quote is not legal in a
            // header. Deserialize it.
            if (true == userAgent?.StartsWith("\"") && true == userAgent.EndsWith("\""))
                userAgent = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(userAgent);

            return userAgent;
        }
        /// <summary>
        /// checking the extension of the url, but also
        /// calling the URL to see if it returns a pdf response header
        /// GPAL calls this if we don't detect a file downloaded but expect one
        /// </summary>
        /// <param name="browser">Browser whose user agent the header request is made with</param>
        /// <param name="url">the url of the potential pdf file</param>
        /// <returns>true or false</returns>
        public static bool TestUrlForPDF(Browser browser, string url)
        {
            if (null == url)
                return false;

            bool retVal = false;

            // does the url contain .pdf?
            if (0 <= url.IndexOf(".pdf", 0, StringComparison.OrdinalIgnoreCase))
                retVal = true;
            else
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", GetUserAgent(browser));

                    try
                    {
                        // Use HttpMethod.Head to retrieve headers ONLY (minimal overhead)
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url))
                        {
                            HttpResponseMessage response = client.SendAsync(request).Result;

                            if (response.IsSuccessStatusCode)
                            {
                                // 1. Check Content-Type for application/pdf
                                string contentType = response.Content.Headers.ContentType?.MediaType;
                                if (contentType != null && contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                                {
                                    retVal = true;
                                }
                                // 2. Fallback: Check Content-Disposition for a .pdf filename
                                else if (response.Content.Headers.ContentDisposition != null)
                                {
                                    string fileName = response.Content.Headers.ContentDisposition.FileName;
                                    if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                    {
                                        retVal = true;
                                    }
                                }
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to verify URL [{url}] via HEAD request. Status code [{response.StatusCode}]");
                            }
                        }
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Exception during HEAD request for [{url}]", null, GPALObjectType.None, ex);
                    }
                }

            if (true == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GPAL detected PDF file opened in browser [{url}]");

            return retVal;
        }
        // How many times DownloadPdfFile fetches while a redirect or consent page is served instead of the PDF, and
        // the wait between fetches.
        internal const int PdfFetchAttempts = 3;
        internal const int PdfFetchWaitMs = 1500;

        /// <summary>
        /// True if <paramref name="content"/> starts with %PDF.<br/>
        /// The url proves nothing: a redirect or consent page is served as HTML at the same .pdf url.
        /// </summary>
        /// <param name="content">The bytes that came back</param>
        /// <returns>True if the bytes begin with the pdf signature</returns>
        internal static bool IsPdf(byte[] content)
        {
            return 4 < content?.Length
                && '%' == content[0] && 'P' == content[1] && 'D' == content[2] && 'F' == content[3];
        }
        /// <summary>
        /// Ensures a PDF file ends up at <paramref name="savePath"/>.<br/>
        /// First checks if the browser already downloaded it to the target or default download directory
        /// (and moves it into place if so); otherwise fetches <paramref name="pdfUrl"/> from inside the page,
        /// and falls back to downloading it directly via HttpClient.
        /// </summary>
        /// <param name="pdfUrl">URL of the PDF file</param>
        /// <param name="savePath">Full path (directory + filename) to save the PDF to</param>
        /// <param name="browser">Browser used to determine the default download directory and user agent, and to fetch from</param>
        /// <returns>True if the file was moved or downloaded successfully, false otherwise</returns>
        public static bool DownloadPdfFile(string pdfUrl, string savePath, IBrowser browser)
        {
            // we clicked first, it could've downloaded the file, so look 
            string filePath = $@"{FileHelper.EnsureDirectoryEndsWithBackslash(Path.GetDirectoryName(savePath))}{System.IO.Path.GetFileName(pdfUrl)}";
            if (true == File.Exists(filePath))
            {
                FileHelper.EnsureFolderFor(savePath);
                File.Move(filePath, savePath);
                return true;
            }
            else
            {
                filePath = $@"{FileHelper.GetDefaultDownloadDirectory(browser)}{Path.GetFileName(pdfUrl)}";
                if (true == File.Exists(filePath))
                {
                    FileHelper.EnsureFolderFor(savePath);
                    File.Move(filePath, savePath);
                    return true;
                }
            }

            // Fetch from inside the page first, on the browser's connection, so a url signed for this session works;
            // HttpClient below would be refused. A redirect page can be served at this url until the browser
            // reaches the PDF, so write only PDF bytes. Otherwise wait PdfFetchWaitMs, read the browser's current
            // url, and fetch that.
            for (int attempt = 0; attempt < PdfFetchAttempts; attempt++)
            {
                byte[] fetched = FetchBytes((Browser)browser, pdfUrl, null, out string _);

                if (true == IsPdf(fetched))
                {
                    FileHelper.EnsureFolderFor(savePath);
                    File.WriteAllBytes(savePath, fetched);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"PDF file [{pdfUrl}] fetched from the page to [{savePath}]");
                    return true;
                }

                if (attempt < PdfFetchAttempts - 1)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{pdfUrl}] answered with [{fetched?.Length ?? 0}] bytes that are not a pdf, waiting for the file itself", browser, GPALObjectType.Browser);
                    Thread.Sleep(PdfFetchWaitMs);
                    pdfUrl = GetCurrentUrl(((Browser)browser).BrowserSettings) ?? pdfUrl;
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{pdfUrl}] never answered with a pdf, the page in front of the file did. Nothing written to [{savePath}]", browser, GPALObjectType.Browser);
            }

            // In-page fetch never got a PDF: use HttpClient. In-page fetch obeys cross-origin rules, so a PDF on
            // another domain can be refused there.
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", GetUserAgent(browser));

                try
                {
                    // Download the PDF file content synchronously
                    byte[] pdfContent = client.GetByteArrayAsync(pdfUrl).Result;

                    if (false == IsPdf(pdfContent))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{pdfUrl}] answered with [{pdfContent?.Length ?? 0}] bytes that are not a pdf. Nothing written to [{savePath}]", browser, GPALObjectType.Browser);
                        return false;
                    }

                    // Save the PDF file to disk
                    FileHelper.EnsureFolderFor(savePath);
                    File.WriteAllBytes(savePath, pdfContent);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"PDF file [{pdfUrl}] downloaded successfully to [{savePath}]");
                    return true;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"PDF file NOT downloaded from [{pdfUrl}]", ex);
                }
            }
            return false;
        }

        /// <summary>
        /// Sends a CDP command through a Selenium driver and returns the result.<br/>
        /// ChromeDriver and EdgeDriver use ExecuteCdpCommand. On a hidden desktop the driver is a RemoteWebDriver
        /// with no ExecuteCdpCommand, so the command goes through the custom command GetWebDriver registers.
        /// </summary>
        /// <param name="driver">The Selenium driver of a Chrome or Edge browser</param>
        /// <param name="command">The CDP command, e.g. Page.setDownloadBehavior</param>
        /// <param name="parameters">The command's parameters</param>
        /// <returns>What the browser returned for the command</returns>
        internal static object ExecuteCdp(IWebDriver driver, string command, Dictionary<string, object> parameters)
        {
            object result = null;

            if (driver is ChromiumDriver chromium)
                result = chromium.ExecuteCdpCommand(command, parameters);
            else
                result = ((ICustomDriverCommandExecutor)driver).ExecuteCustomDriverCommand(HiddenCdpCommand, new Dictionary<string, object> { ["cmd"] = command, ["params"] = parameters });

            return result;
        }

        /// <summary>
        /// Custom command name for the driver's CDP endpoint, registered in GetWebDriver.
        /// </summary>
        private const string HiddenCdpCommand = "gpalExecuteCdp";

        /// <summary>
        /// Creates the Chrome or Edge WebDriver via an explicit DriverService (so the driver process PID can be
        /// captured into browserSettings.ServiceDriverPid for later cleanup).<br/>
        /// On failure, publishes an EXCEPTION event with a link to update the driver.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings describing the browser type and driver location</param>
        /// <param name="options">ChromeOptions or EdgeOptions to launch the driver with</param>
        /// <returns>The created IWebDriver, or null if creation failed</returns>
        internal static IWebDriver GetWebDriver(BrowserSettings browserSettings, dynamic options)
        {
            IWebDriver tmpDriver = null;

            try
            {
                if (true == browserSettings.HiddenDesktop)
                {
                    // StartHiddenDriver launches the driver on the hidden desktop and sets ServiceDriverPid. Selenium
                    // connects by port, not a DriverService.
                    int hiddenPort = StartHiddenDriver(browserSettings);

                    tmpDriver = new OpenQA.Selenium.Remote.RemoteWebDriver(new Uri($"http://127.0.0.1:{hiddenPort}"), options);

                    // Register the driver's CDP endpoint as a custom command for ExecuteCdp. The path uses goog for
                    // chromedriver, ms for msedgedriver.
                    string vendor = BrowserType.Edge == browserSettings.BrowserType ? "ms" : "goog";
                    ((ICustomDriverCommandExecutor)tmpDriver).RegisterCustomDriverCommand(HiddenCdpCommand, new HttpCommandInfo(HttpCommandInfo.PostCommand, $"/session/{{sessionId}}/{vendor}/cdp/execute"));
                }
                else if (BrowserType.Chrome == browserSettings.BrowserType)
                {
                    // Create the service explicitly so we can get the PID
                    ChromeDriverService chromeService = ChromeDriverService.CreateDefaultService(browserSettings.DriverLocation);
                    chromeService.HideCommandPromptWindow = true; // optional but recommended

                    ChromeOptions chromeOptions = (ChromeOptions)options;

                    tmpDriver = new ChromeDriver(chromeService, chromeOptions);

                    // *** Capture the chromedriver.exe PID ***
                    browserSettings.ServiceDriverPid = chromeService.ProcessId;
                }
                else // Edge
                {
                    EdgeDriverService edgeService = EdgeDriverService.CreateDefaultService(browserSettings.DriverLocation);
                    edgeService.HideCommandPromptWindow = true; // optional

                    EdgeOptions edgeOptions = (EdgeOptions)options;

                    tmpDriver = new EdgeDriver(edgeService, edgeOptions);

                    // *** Capture the msedgedriver.exe PID (reuse the same property) ***
                    browserSettings.ServiceDriverPid = edgeService.ProcessId;
                }
            } catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Please manually update the driver @ [{DriverHelper.ManualDownloadPage(browserSettings.BrowserType)}]..", browserSettings.Browser, GPALObjectType.Browser, ex);
            }
            return tmpDriver;
        }

        /// <summary>
        /// The browser a Chromium driver started: its child of that name without --type=, which renderer, GPU and
        /// utility processes carry. Null when there is none, and Close then ends only the driver.
        /// </summary>
        /// <param name="driverPid">The chromedriver or msedgedriver process</param>
        /// <param name="processName">chrome.exe or msedge.exe</param>
        /// <returns>The browser process, or null</returns>
        private static Process DriverBrowserProcess(int driverPid, string processName)
        {
            Process retVal = null;

            try
            {
                if (0 < driverPid)
                    using (var search = new System.Management.ManagementObjectSearcher(
                        $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ParentProcessId = {driverPid} AND Name = '{processName}'"))
                    {
                        foreach (System.Management.ManagementObject found in search.Get())
                            if (null == retVal && false == (found["CommandLine"]?.ToString() ?? string.Empty).Contains("--type="))
                                retVal = Process.GetProcessById(Convert.ToInt32(found["ProcessId"]));
                    }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No [{processName}] found under driver process [{driverPid}]", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
        #region <KillProcess>

        /// <summary>
        /// https://github.com/matthewbierman/RemoteWebDriverExtended/blob/master/RemoteWebDriverExtended/RemoteWebDriverExtended.cs
        /// </summary>

        private static class DriverInfos
        {
            public static DriverInfo Chrome = new DriverInfo(
                executableFileName: GPAL.GPALSettings.ChromeDriverFilename,
                processName: "chromedriver",
                browserName: "chrome"
                );

            public static DriverInfo FireFox = new DriverInfo(
                executableFileName: GPAL.GPALSettings.FirefoxDriverFilename,
                processName: "geckodriver",
                browserName: "firefox"
                );

            public static DriverInfo Edge = new DriverInfo(
                executableFileName: GPAL.GPALSettings.EdgeDriverFilename,
                processName: "MSEdgeDriver",
                browserName: "edge"
                );

        }

        /// <summary>
        /// Holds the driver executable filename, driver process name, and browser name used by KillProcess to clean up
        /// a particular browser's webdriver process.
        /// </summary>
        private class DriverInfo
        {
            /// <summary>
            /// Creates a DriverInfo describing the driver executable, process name and browser name.
            /// </summary>
            /// <param name="executableFileName">The driver's executable filename (e.g. chromedriver.exe)</param>
            /// <param name="processName">The driver's process name (without .exe), used to find/kill the process</param>
            /// <param name="browserName">The browser's name (e.g. "chrome", "edge", "firefox")</param>
            public DriverInfo(string executableFileName, string processName, string browserName)
            {
                ExecutableFileName = executableFileName;
                ProcessName = processName;
                BrowserName = browserName;
            }
            public string ExecutableFileName { get; private set; }
            public string ProcessName { get; private set; }
            public string BrowserName { get; private set; }
        }
        /// <summary>
        /// Result of attempting to kill one or more processes by name.
        /// </summary>
        public class KillProcessesResult
        {
            public string Name { get; set; }
            public int Found { get; set; } = 0;
            public int Killed { get; set; } = 0;
            public bool Success { get; set; } = false;
        }

        /// <summary>
        /// Kills all known webdriver processes (geckodriver, chromedriver, MSEdgeDriver) and resets
        /// the "already updating" driver-update flag.
        /// </summary>
        /// <returns>An array of KillProcessesResult, one per webdriver process name attempted</returns>
        public static KillProcessesResult[] KillAllRunningWebDrivers()
        {
            List<KillProcessesResult> result = new List<KillProcessesResult>();
            KillProcessesResult[] kpa = result.ToArray();
            alreadyUpdating = false;

            try
            {
                result.Add(KillProcess(DriverInfos.FireFox.ProcessName));
                result.Add(KillProcess(DriverInfos.Chrome.ProcessName));
                result.Add(KillProcess(DriverInfos.Edge.ProcessName));

                // zero out all browsers 
                //GPAL.Browsers.Clear();
            } catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to kill processes", ex);
            }

            return kpa;
        }

        /// <summary>Maximum parent processes CameFromProcess checks.</summary>
        private const int MaxParentHops = 8;

        internal const int GracefulExitMs = 10000;   // wait for a browser to exit after close
        internal const int ProfileReleaseWaitMs = 15000;   // max wait for a closing browser to free its profile
        internal const int ProfileReleaseCheckMs = 250;

        /// <summary>
        /// Asks the browser to close through its engine, so the profile records a normal exit.<br/>
        /// A killed browser marks the profile crashed, Chromium reopens the old session next launch, and the dying
        /// process can write its preferences over GPAL's.<br/>
        /// Does not wait; the caller waits and kills.
        /// </summary>
        /// <param name="aBrowser">The browser to close</param>
        /// <param name="endItAll">True when the session is ending, false when other windows carry on</param>
        private static void CloseBrowserGracefully(Browser aBrowser, bool endItAll)
        {
            try
            {
                if (true == aBrowser.UsePuppeteer)
                {
                    // Browser.close is CDP's own orderly shutdown. Not over a dead connection: a browser that ended
                    // with its last window leaves nothing to answer, and sending would first try to reconnect.
                    if (false == aBrowser.BrowserSettings.Process?.HasExited && true == aBrowser.BrowserSettings.PuppeteerCommunicator.ConnectionOpen)
                    {
                        aBrowser.BrowserSettings.PuppeteerCommunicator.SendCommand(DevToolsMethods.BrowserClose, new Dictionary<string, object>(), null, timeoutSeconds: 2).GetAwaiter().GetResult();

                        // Browser.close sent. Mark the communicator closed so nothing more is sent and it does not
                        // reconnect when the browser drops.
                        aBrowser.BrowserSettings.PuppeteerCommunicator._isRunning = false;
                        aBrowser.BrowserSettings.PuppeteerCommunicator.Closed = true;

                        aBrowser.PuppeteerCommunicator._readerReadyTcs.SetCanceled();

                        if (false == aBrowser.PuppeteerCommunicator._usePipes)
                            aBrowser.PuppeteerCommunicator.CloseOutputWebSocketAsync().GetAwaiter();
                    }
                }
                else if (true == aBrowser.UseSelenium && null != aBrowser.BrowserDriver)
                {
                    // Quit ends the session; Close closes only this window when other GPAL browsers share the
                    // driver. EdgeDriver can block on Quit for its whole command timeout, so cap it at
                    // GracefulExitMs. The caller kills what remains.
                    if (true == endItAll)
                    {
                        aBrowser.BrowserSettings.TabScripts?.Dispose();
                        aBrowser.BrowserSettings.TabScripts = null;
                    }

                    Task closing = Task.Run(() =>
                    {
                        if (true == endItAll)
                        {
                            // A browser reached with WithExistingCDPDebugPort was not started by this driver, and
                            // Quit ends only the driver's session. Closing every window is a normal quit, with the
                            // profile saved and a clean exit.
                            if (true == aBrowser.BrowserSettings.UseExistingBrowser)
                                foreach (string handle in aBrowser.BrowserDriver.WindowHandles)
                                {
                                    aBrowser.BrowserDriver.SwitchTo().Window(handle);
                                    aBrowser.BrowserDriver.Close();
                                }

                            aBrowser.BrowserDriver?.Quit();
                        }
                        else
                            aBrowser.BrowserDriver?.Close();     // NOTE: CAVEAT: closes the currently focused window
                    });

                    closing.Wait(GracefulExitMs);

                    // A browser this driver connected to may outlive its windows and the session, as Firefox did.
                    // Closing its main window is the same normal quit a person makes.
                    if (true == endItAll && true == aBrowser.BrowserSettings.UseExistingBrowser && false == aBrowser.BrowserSettings.Process?.HasExited)
                        aBrowser.BrowserSettings.Process?.CloseMainWindow();
                }
                else
                {
                    // OttoMagic. The extension cannot quit the browser but can close all its windows, including
                    // page-opened ones; closing the last is a normal quit with the profile saved and a clean exit.
                    // Works headless, where there is no main window to message. Call CloseBrowser only if the
                    // browser is alive; on a dead one it retries a dead port and slows the next run. If the process
                    // survives, send CloseMainWindow.
                    if (true == aBrowser.UseOttoMagic && true == aBrowser.IsAlive)
                        aBrowser.MagicHelper.CloseBrowser();

                    if (false == aBrowser.BrowserSettings.Process?.HasExited)
                        aBrowser.BrowserSettings.Process?.CloseMainWindow();
                }
            }
            catch
            {
            }
        }

        public static KillProcessesResult[] KillAllRunningProcesses(bool killWebDrivers, IBrowser browser)
        {
            List<KillProcessesResult> result = new List<KillProcessesResult>();
            KillProcessesResult[] kpa = result.ToArray();

            try
            {
                int browserCount = GPAL.Browsers.Count;

                if (0 == browserCount)
                {
                    // forms or converter or application, this is expected...
                    //GPAL.PublishSimpleEvent(GPALEventType.INFO, "No browsers to kill? How'd we get here?", browser, GPALObjectType.Browser);
                    return kpa;
                }

                // suppose we have two windows open, we will have two GPAL browsers in the list, both with the same driver
                // what happens if one workflow issues a .close() which ends the workflow instead of a .CloseWindow()
                // we can't perform any operation on the browserdriver as that will affect both windows
                // we could see the browser count > 1 and if killWebDrivers == false, assume it's not the end
                // then we can look at the browsersettings, get the CUrrentUrl and then CloseWindow(browserSettings.CurrentUrl)?
                foreach (Browser aBrowser in GPAL.Browsers)
                {
                    if (aBrowser == browser || null == browser) // null browser means unhandled exception, kill it all; browser not null = close(), killwebdrivers ends it all
                        try
                        {
                            bool endItAll = (aBrowser == browser && true == killWebDrivers) || null == browser;

                            // Ask before killing. A killed browser records a crash, and Chromium then reopens the
                            // previous tabs regardless of session.restore_on_startup. It also holds the profile long
                            // enough to write its Preferences over GPAL's.
                            CloseBrowserGracefully(aBrowser, endItAll);

                            if (null != aBrowser.BrowserSettings.Process)
                                try
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{aBrowser.BrowserSettings.BrowserType}] waiting on process [{aBrowser.BrowserSettings.Process.Id}] to exit", aBrowser, GPALObjectType.Browser);

                                    // Kill only if the browser is still running after GracefulExitMs.
                                    if (false == aBrowser.BrowserSettings.Process.WaitForExit(GracefulExitMs))
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{aBrowser.BrowserSettings.BrowserType}] did not shut down within [{GracefulExitMs / 1000}] seconds, killing it. Its profile will read as crashed.", aBrowser, GPALObjectType.Browser);

                                        // the whole tree: the browser's own helper processes hold the profile
                                        // open after the browser itself has gone, and the profile cannot be deleted
                                        KillProcessTree(aBrowser.BrowserSettings.Process.Id);
                                        result.Add(KillProcess(aBrowser.BrowserSettings.Process));
                                    }
                                }
                                catch
                                { }

                            // The webdriver process is ours and holds no profile, so kill it once its browser is
                            // closed.
                            if (true == aBrowser.UseSelenium && true == endItAll)
                            {
                                int pid = aBrowser.BrowserSettings.ServiceDriverPid;

                                GPAL.PublishSimpleEvent(GPALEventType.INFO, ">>> Disposing browser driver");

                                try
                                {
                                    if (0 < pid)
                                        KillProcessTree(pid);

                                    // fallback - kill any stray driver processes by name
                                    var kpaDrivers = KillAllRunningWebDrivers();

                                    foreach (KillProcessesResult killProcessesResult in kpaDrivers)
                                        result.Add(killProcessesResult);
                                }
                                catch { /* ignore */ }
                                finally
                                {
                                    aBrowser.BrowserSettings.ServiceDriverPid = 0;
                                }
                            }

                            Thread.Sleep(1_000);
                        }
                        catch
                        {

                        }
                        finally
                        {
                            if (true == aBrowser.BrowserSettings.TempProfileCreated)
                                ChromeProfileManager.RemoveTempUserProfile(aBrowser.BrowserSettings.ProfileDataDirectory);

                            // SweepAbandonedTempProfiles removes temp profiles left by earlier runs that never got
                            // here.
                            ChromeProfileManager.SweepAbandonedTempProfiles();
                        }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to kill processes", ex);
            }

            // not needed: the driver pid is captured and its process tree killed
            //try
            //{
            //    if (true == killWebDrivers)
            //    {
            //        var kpaDrivers = KillAllRunningWebDrivers();

            //        foreach (KillProcessesResult killProcessesResult in kpaDrivers)
            //            result.Add(killProcessesResult);

            //        foreach (IBrowser aBrowser in GPAL.Browsers)
            //            switch (aBrowser.BrowserType)
            //            {
            //                case BrowserType.Chrome:
            //                    KillProcess(DriverInfos.Chrome.BrowserName);
            //                    break;

            //                case BrowserType.Edge:
            //                    KillProcess(DriverInfos.Edge.BrowserName);
            //                    break;

            //                case BrowserType.FireFox:
            //                    KillProcess(DriverInfos.FireFox.BrowserName);
            //                    break;
            //            }
            //    }
            //}
            //catch { }

            return result.ToArray();
        }
        /// <summary>
        /// Kill the PID and all it's child processes
        /// NOTE: CAVEAT: this calls Taskkill because our version of .Net doesn't offer an elegant solution
        /// </summary>
        /// <summary>
        /// Kills the windowless browser Edge leaves after exit. A browser with an extension background page starts a
        /// --no-startup-window replacement when its last window closes, which keeps the user data directory open.
        /// The next launch hands off to it and exits, with no debug port and no extension.
        /// <br/><br/>
        /// Only direct children of our browser with --no-startup-window are killed. Chrome does not do this, so
        /// nothing is found.
        /// </summary>
        /// <param name="browserSettings">The browser that was just closed.</param>
        internal static void EndWindowlessSuccessor(BrowserSettings browserSettings)
        {
            int ourPid = browserSettings.Process?.Id ?? 0;

            if (0 < ourPid)
            {
                // The replacement starts as our browser exits, so wait for the exit first.
                WaitForBrowserToExit(browserSettings, Browser.ProfileRestoreWaitMs);

                // Catch: a failed WMI query should not fail an otherwise finished close.
                try
                {
                    using (var search = new System.Management.ManagementObjectSearcher(
                        $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ParentProcessId = {ourPid}"))
                        foreach (System.Management.ManagementObject found in search.Get())
                        {
                            string commandLine = found["CommandLine"]?.ToString() ?? string.Empty;

                            if (true == commandLine.Contains("--no-startup-window"))
                            {
                                int successor = Convert.ToInt32(found["ProcessId"]);

                                GPAL.PublishSimpleEvent(GPALEventType.INFO,
                                    $"Ending the windowless browser [{successor}] left behind by [{ourPid}], which would hold the profile open",
                                    browserSettings, GPALObjectType.Browser);

                                KillProcessTree(successor);
                            }
                        }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not look for a windowless browser left behind by [{ourPid}]",
                        browserSettings, GPALObjectType.Browser, ex);
                }
            }
        }

        internal static void KillProcessTree(int pid)
        {
            if (pid <= 0) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",  // /T = tree, /F = force
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (Process taskkillProcess = Process.Start(startInfo))
                {
                    taskkillProcess?.WaitForExit(5000); // optional timeout
                }
            }
            catch
            {
                // Ignore errors during cleanup (access denied, already dead, etc.)
            }
        }
        /// <summary>
        /// Kill the process (browser launched by MagicHelper or puppeteer
        /// </summary>
        /// <param name="process"></param>
        public static KillProcessesResult KillProcess(Process process)
        {
            if (null != process)
            {
                KillProcessesResult result = new KillProcessesResult();

                string Name = "";
                try
                {
                    Name = process.ProcessName;
                    result.Name = Name;
                }
                catch (Exception)
                {
                    // process has already exited, nothing to do
                    result.Success = true;
                    return result;
                }

                process.Kill();
                process.WaitForExit();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Killed process [{result.Name}]");
                return result;
            }
            return null;
        }

        /// <summary>
        /// Kills all running processes with the given name, waiting for each to exit.
        /// </summary>
        /// <param name="processName">Process name to kill (without .exe)</param>
        /// <returns>A KillProcessesResult describing how many matching processes were found and killed</returns>
        /// <example>
        /// <code>
        /// BrowserHelper.KillProcess("GPALRestAPI");
        /// </code>
        /// </example>
        public static KillProcessesResult KillProcess(string processName)
        {
            KillProcessesResult result = new KillProcessesResult { Name = processName };

            var processes = Process.GetProcessesByName(processName);

            result.Found = processes.Count();

            if (result.Found > 0)
            {
                foreach (var process in processes)
                {
                    do
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(); // Wait for the process to finish
                        }
                        catch
                        {

                        }
                    } while (false == process.HasExited);

                    result.Killed++;
                    result.Success = true;
                    result.Found++;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Killed process [{processName}]");
                }
            }
            else
            {
                result.Success = true;
            }

            return result;
        }
        #endregion <KillProcess>

        private static Stopwatch watch = new System.Diagnostics.Stopwatch();
        private static int lastX = 0, lastY = 0;

        // https://stackoverflow.com/questions/8438483/bring-browser-from-back-to-front-selenium-web-driver-java
        // NOTE: we prolly should restore the window, too, if it's minimized? or do a hardware top for all?
        // NOTE: maybe we only need to top if we have image selectors? otherwise, we don't need to be on top...
        /// <summary>
        /// Brings the browser window to the foreground (or moves the mouse off it) so hardware-driven
        /// interactions and image selectors target the correct window.<br/>
        /// Headless browsers, OttoMagic/Puppeteer without image selectors, and repeated calls within
        /// 1 second are skipped. Uses a hardware click on the window's top-left corner for Firefox/hardware
        /// mode, Selenium's SwitchTo().Window for Selenium, or WindowHelper.TopBrowser otherwise.
        /// </summary>
        /// <summary>
        /// True if baseUrl's status endpoint answers 200 within 1 second; a wedged listener counts as not
        /// running.<br/><br/>
        /// GPALRestAPI exits with its browser, so <see cref="Browser.IsAlive"/> uses this.
        /// </summary>
        /// <returns>True when a GPALRestAPI answered.</returns>
        /// <summary>File in the GPALRestAPI exe folder listing each running GPALRestAPI's process id and port.</summary>
        private const string PortsLogName = "ports-in-use.log";

        /// <summary>Registered name of the GPALRestAPI native messaging host.</summary>
        private const string NativeHostName = "gpal.rest.api.nativeapp";

        /// <summary>
        /// Registry keys where Chromium browsers look for the native messaging host, HKCU before HKLM. WOW6432Node
        /// keys are the 32 bit view.
        /// </summary>
        private static readonly string[] NativeHostKeys =
        {
            @"Software\\Google\\Chrome\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\Chromium\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\Microsoft\\Edge\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\WOW6432Node\\Google\\Chrome\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\WOW6432Node\\Chromium\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\WOW6432Node\\Microsoft\\Edge\\NativeMessagingHosts\\" + NativeHostName
        };

        /// <summary>
        /// Path of GPALRestAPI.exe, found as the browser finds it: registry key to host manifest, manifest to exe.
        /// Nothing needs to be running.
        /// </summary>
        /// <returns>Full path to GPALRestAPI.exe, or null when the host is not registered.</returns>
        public static string RestApiLocation()
        {
            string retVal = null;

            foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string keyPath in NativeHostKeys)
                    try
                    {
                        using (RegistryKey key = root.OpenSubKey(keyPath))
                        {
                            string manifestPath = key?.GetValue(null) as string;

                            if (true == string.IsNullOrWhiteSpace(manifestPath) || false == File.Exists(manifestPath))
                                continue;

                            // The browser launches the exe the manifest names, so use that.
                            dynamic manifest = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(manifestPath));
                            string exePath = manifest?.path;

                            if (false == string.IsNullOrWhiteSpace(exePath) && true == File.Exists(exePath))
                            {
                                retVal = exePath;
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not read the native host registration at [{keyPath}]", null, GPALObjectType.None, ex);
                    }

                if (false == string.IsNullOrWhiteSpace(retVal))
                    break;
            }

            return retVal;
        }

        /// <summary>
        /// Process id and port of every running GPALRestAPI, from ports-in-use.log, where each adds its line at
        /// start and removes it at exit.
        /// <br/><br/>
        /// OttoMagic only; Selenium and Puppeteer get an empty list, which is not an error.
        /// <br/><br/>
        /// <see cref="Browser.RestApiBaseUrl"/> gives one browser's port; this gives all, for example to check every
        /// browser started.
        /// </summary>
        /// <param name="hosts">Process id and port for each running GPALRestAPI, empty when none are.</param>
        /// <returns>How many were found.</returns>
        public static int LiveRestApiHosts(out List<(int processId, int port)> hosts)
        {
            hosts = new List<(int processId, int port)>();

            string exePath = RestApiLocation();
            string folder = true == string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
            string path = true == string.IsNullOrWhiteSpace(folder) ? null : Path.Combine(folder, PortsLogName);

            // A missing file means no OttoMagic browser is running; the last GPALRestAPI out deletes it.
            if (true == string.IsNullOrWhiteSpace(path) || false == File.Exists(path))
                return hosts.Count;

            try
            {
                // FileShare.ReadWrite so a GPALRestAPI can write while we read.
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream))
                    foreach (string line in reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] halves = line.Split(':');

                        if (2 == halves.Length
                            && true == int.TryParse(halves[0], out int pid)
                            && true == int.TryParse(halves[1], out int port)
                            && false == hosts.Any(each => each.processId == pid))
                            hosts.Add((pid, port));
                    }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not read the host list at [{path}]", null, GPALObjectType.None, ex);
            }

            return hosts.Count;
        }

        internal static bool RestApiAnswering(string baseUrl)
        {
            bool retVal = false;

            if (false == string.IsNullOrWhiteSpace(baseUrl))
                try
                {
                    System.Net.HttpWebRequest ask = (System.Net.HttpWebRequest)System.Net.WebRequest.Create($"{baseUrl}status");
                    ask.Timeout = 1000;
                    ask.ReadWriteTimeout = 1000;

                    using (System.Net.HttpWebResponse answer = (System.Net.HttpWebResponse)ask.GetResponse())
                        retVal = System.Net.HttpStatusCode.OK == answer.StatusCode;
                }
                catch (System.Net.WebException)
                {
                    // refused, timed out, or answered something other than a running server
                }

            return retVal;
        }

        public static void TopBrowser(Browser browser, bool moveDontClick, bool forceClick = false) // AFTER HARDWARE CLICK, we want to MOVE NOT CLICK mouse off browser into a safe area, so reusing this method
        {
            int x, y;

            // only top if we are interacting with the page using hardware or image mathcing on the current selector
            // NOTE: null-safe throughout - CurrentUOW/CurrentSelector are null when we are not on a real page, and
            // a (bool) cast on a null bool? (from the ?. chain) would throw.
            bool needsTopping = (browser.CurrentUOW?.CurrentSelector?.SelectorSettings?.SelectorPaths
                    ?.Any(path => path.SelectorPathType == SelectorPathType.Image) ?? false)
                    || InteractionType.Hardware == browser.CurrentUOW?.CurrentSelector?.InteractionType
                    || true == browser.BrowserSettings.UseHardware;

            // don't do anything, we are always in control and only need it for image matching potentially
            if (true == browser.BrowserSettings.UseHeadless || false == needsTopping)
                return;

            // NOTE: moving the mouse off the browser after a click exists only to keep stray :hover effects
            // (tooltips, dropdowns, sticky menus) from interfering with subsequent IMAGE selector matching -
            // CSS/DOM selectors don't care about hover state. If this unit of work has no image selectors,
            // skip the move entirely: teleporting the cursor off-viewport after every single click is itself
            // a mechanical, easily-fingerprinted pattern, so we only pay that cost when it's actually needed.
            if (true == moveDontClick || true == forceClick)   // selenium control - click the topmost left corner of the browser to top it, only thing that works 100%
            {
                if (true == watch.IsRunning)
                {
                    if (1000 > watch.ElapsedMilliseconds)   // don't do anything if we were called less than 1 second ago, avoid double clicking the browser (max window)
                        return;
                    watch.Stop();
                }
                watch = System.Diagnostics.Stopwatch.StartNew();

                HardwareHelper.GetCursorPos(out Point currCursorPos);

                if (true == browser.BrowserSettings.UseHardware || true == GPAL.UseHardware || Enums.BrowserType.FireFox == browser.BrowserSettings.BrowserType || true == moveDontClick || true == forceClick)
                {
                    try
                    {
                        Rectangle rect = ElementHelper.GetWindowRectangle(browser);
                        // true screen origin/size of the window (window.screenLeft/screenTop + outer size), so
                        // nothing has to guess at the height of a browser's chrome
                        Rectangle outerRect = ElementHelper.GetWindowOuterRectangle(browser);

                        if (true == moveDontClick)
                        {
                            // NOTE: park the mouse at a randomly chosen edge/corner of the window (above, below,
                            // left, or right) instead of always "above the window" - landing in the same relative
                            // spot after every click is itself a mechanical, easily-fingerprinted pattern.
                            var rnd = new Random();
                            switch (rnd.Next(4))
                            {
                                case 0: x = rect.Left + rnd.Next(-50, rect.Width + 50); y = outerRect.Top - 20; break;   // above the true window top
                                case 1: x = rect.Left + rnd.Next(-50, rect.Width + 50); y = rect.Bottom + 50; break;    // below
                                case 2: x = rect.Left - 60; y = rect.Top + rnd.Next(0, rect.Height); break;             // left
                                default: x = rect.Right + 60; y = rect.Top + rnd.Next(0, rect.Height); break;          // right
                            }
                            // Keep the parked cursor in the working area. Below a maximized window is the taskbar,
                            // where hovering opens previews that cover the page and break image matching.
                            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
                            x = Math.Min(Math.Max(workingArea.Left, x), workingArea.Right - 1);
                            y = Math.Min(Math.Max(workingArea.Top, y), workingArea.Bottom - 1);
                        }
                        else
                        {
                            // click a few pixels into the true top-left corner of the browser window to top it -
                            // just inside the corner so we land on the window frame/title, off the resize hotzone,
                            // and never on a tab or page content
                            x = outerRect.Left + 3;
                            y = outerRect.Top + 3;
                        }
                        lastX = x;
                        lastY = y;
                        //x = browser.BrowserDriver.Manage().Window.Position.X + 6;
                        //y = x = browser.BrowserDriver.Manage().Window.Position.Y + 6;
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get browser coords. Retrying.", browser, GPALObjectType.Browser, ex);
                        x = lastX;
                        y = lastY;
                    }

                    if (true == GPAL.SimulateMouse || true == browser.CurrentUOW.CurrentSelector?.SimulateMouse)
                        HardwareHelper.MoveMouse(x, y, 10, 10);    // if we are moving the mouse out of the way, just move it up from the current spot
                    else
                        HardwareHelper.MoveMouse(x, y);

                    if (false == moveDontClick)
                    {
                        HardwareHelper.HardwareClick(x, y, ClickType.LeftClick);     // firefox will not take software topping requests, only hardware clicks
                        Thread.Sleep(10);

                        if (true == GPAL.SimulateMouse || true == browser.CurrentUOW.CurrentSelector?.SimulateMouse)
                            HardwareHelper.MoveMouse(currCursorPos.X, currCursorPos.Y, 10, 10);
                        else
                            HardwareHelper.MoveMouse(currCursorPos.X, currCursorPos.Y);
                    }
                }
                else if (true == browser.UseSelenium)
                {
                    try
                    {
                        // Top the browser by re-focusing whatever tab Selenium currently has active - a no-op for
                        // tab selection but it still brings the window forward. Do NOT switch by our own
                        // CurrentTabIdx here: if that index is stale it yanks focus to the wrong tab right before
                        // a click. If even this is problematic, the catch falls back to the WindowHelper route.
                        browser.BrowserDriver.SwitchTo().Window(browser.BrowserDriver.CurrentWindowHandle);
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string tmpStr = "Faling back to windows topping.";
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to top browser using Selenium. {(false == GPAL.NoFallbackRecoveryActions ? tmpStr : String.Empty)})", browser, GPALObjectType.Browser, ex);
                        if (false == GPAL.NoFallbackRecoveryActions)
                            WindowHelper.TopBrowser(browser.BrowserSettings.Process, browser.BrowserSettings.ServiceDriverPid);
                    }
                }
            }
            else
            {   // we have an image selector or we use ottomagic or puppeteer so use windows to top the browser
                if (false == WindowHelper.TopBrowser(browser.BrowserSettings.Process, browser.BrowserSettings.ServiceDriverPid))
                    throw new GPALException("Browser has exited");
            }
        }

        // New utility method to find a free port
        /// <summary>
        /// Finds the first TCP port, starting from <paramref name="startPort"/> up to 65535, that nothing is
        /// listening on (a connection attempt fails with SocketException).
        /// </summary>
        /// <returns>The first free port number found</returns>
        /// <summary>
        /// Throws if a browser is already running on this run's profile.<br/>
        /// Chromium would hand the command line to it and exit, dropping the debug port while appearing to succeed.
        /// Reports the process id holding the profile.
        /// </summary>
        /// <exception cref="GPALException">Thrown when another browser already holds the profile</exception>
        /// <summary>
        /// True if <paramref name="ancestorId"/> is <paramref name="processId"/> or one of its ancestors. Matches a
        /// native messaging host's port announcement to the browser that started it when several start at once.
        /// <br/><br/>
        /// Windows records only the immediate parent, so walk up, at most <see cref="MaxParentHops"/>.
        /// </summary>
        /// <param name="processId">The host that announced a port.</param>
        /// <param name="ancestorId">The browser this launch started.</param>
        /// <returns>True when ancestorId is somewhere above processId.</returns>
        internal static bool CameFromProcess(int processId, int ancestorId)
        {
            bool retVal = false;
            int walking = processId;

            for (int hop = 0; hop < MaxParentHops && 0 != walking && false == retVal; hop++)
            {
                if (walking == ancestorId)
                    retVal = true;
                else
                    walking = ParentOf(walking);
            }

            return retVal;
        }

        /// <summary>
        /// Parent process id from WMI, or zero if the query fails. Works after the parent exits; the id is stored
        /// on the child.
        /// </summary>
        /// <param name="processId">The process whose parent is wanted.</param>
        /// <returns>The parent's id, or zero.</returns>
        private static int ParentOf(int processId)
        {
            int retVal = 0;

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}"))
                    foreach (System.Management.ManagementObject each in searcher.Get())
                        using (each)
                            retVal = Convert.ToInt32(each["ParentProcessId"]);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not ask who started process [{processId}]", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }

        /// <summary>
        /// Throws if ProfileDataDirectory is a profile directory (contains a Preferences file) rather than a user
        /// data directory.<br/>
        /// Given one, Chromium silently creates and runs a new empty Default profile inside it. Firefox is skipped.
        /// </summary>
        /// <param name="browserSettings">The settings naming the directory</param>
        /// <exception cref="GPALException">The directory given is a profile rather than a root</exception>
        internal static void RefuseIfProfileIsNotARoot(BrowserSettings browserSettings)
        {
            string given = browserSettings.ProfileDataDirectory;

            if (true == string.IsNullOrEmpty(given) || BrowserType.FireFox == browserSettings.BrowserType) return;

            // A user data root holds profile directories; a profile directory holds a Preferences file
            if (false == File.Exists(Path.Combine(given, "Preferences"))) return;

            string message = $"[{given}] is a browser profile, not a user data directory. Chromium would build a new empty profile inside it and run that instead. Give the directory that holds it and name the profile, or copy it out to a directory of its own";

            GPAL.PublishSimpleEvent(GPALEventType.ERROR, message, null, GPALObjectType.Browser);

            throw new GPALException($"{GPAL.MyMethodName()}: {message}");
        }

        internal static void RefuseIfProfileIsOpen(BrowserSettings browserSettings)
        {
            RefuseIfProfileIsNotARoot(browserSettings);

            string profile = browserSettings.ProfileDataDirectory;

            if (false == string.IsNullOrEmpty(profile))
            {
                string wanted = NormalizedDirectory(profile);
                bool isFirefox = BrowserType.FireFox == browserSettings.BrowserType;

                string processName = true == isFirefox ? "firefox.exe"
                                   : BrowserType.Edge == browserSettings.BrowserType ? "msedge.exe"
                                   : "chrome.exe";

                // chromium names the profile with --user-data-dir=, firefox with -profile and the path after it
                string profileArgument = true == isFirefox
                                       ? "-profile\\s+(\"[^\"]*\"|[^\\s]*)"
                                       : "--user-data-dir=(\"[^\"]*\"|[^\\s]*)";
                int holder = 0;

                // A browser started without --user-data-dir owns the default root. Chromium only: Firefox
                // names a profile rather than a root, and its default cannot be derived from the path
                string defaultRoot = true == isFirefox
                                   ? null
                                   : NormalizedDirectory(GetBrowserProfileDirectory(browserSettings, true));

                holder = ProfileHolder(profile, processName, wanted, profileArgument, defaultRoot);

                // A browser that just closed may still hold the profile, as when runs follow each other. Wait up to
                // ProfileReleaseWaitMs; still held after that means another browser has it.
                if (0 != holder)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Profile [{profile}] is still held by [{processName}] process [{holder}], waiting up to [{ProfileReleaseWaitMs / 1000}] seconds for it to be let go", null, GPALObjectType.Browser);

                    Stopwatch waited = Stopwatch.StartNew();

                    while (0 != holder && ProfileReleaseWaitMs > waited.ElapsedMilliseconds)
                    {
                        System.Threading.Thread.Sleep(ProfileReleaseCheckMs);

                        holder = ProfileHolder(profile, processName, wanted, profileArgument, defaultRoot);
                    }

                    if (0 == holder)
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Profile [{profile}] was let go after [{waited.ElapsedMilliseconds}] ms", null, GPALObjectType.Browser);
                }

                if (0 != holder)
                {
                    string message = $"Profile [{profile}] is already open in [{processName}] process [{holder}]. Close it, or give this run a profile of its own";

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, message, null, GPALObjectType.Browser);

                    throw new GPALException($"{GPAL.MyMethodName()}: {message}");
                }
            }
        }

        /// <summary>
        /// Process id of the browser using profile <paramref name="wanted"/>, or zero. Read from each browser's
        /// command line, the only place it appears.
        /// </summary>
        /// <param name="profile">The profile as the caller wrote it, for the message when WMI will not answer</param>
        /// <param name="processName">Executable to look through, chrome.exe, msedge.exe or firefox.exe</param>
        /// <param name="wanted">The profile directory, normalized</param>
        /// <param name="profileArgument">Pattern that pulls the profile out of a command line</param>
        /// <param name="defaultRoot">The user data directory Chromium uses without --user-data-dir; null for
        /// Firefox</param>
        /// <returns>The process id holding it, or zero</returns>
        private static int ProfileHolder(string profile, string processName, string wanted, string profileArgument, string defaultRoot)
        {
            int retVal = 0;

            try
            {
                using (var search = new System.Management.ManagementObjectSearcher(
                    $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{processName}'"))
                {
                    foreach (System.Management.ManagementObject found in search.Get())
                    {
                        string commandLine = found["CommandLine"]?.ToString();

                        if (true == string.IsNullOrEmpty(commandLine)) continue;

                        // Only the browser process holds the profile. Renderer, GPU, utility and crashpad
                        // processes carry --type= and no --user-data-dir, so without this every child of
                        // every browser reads as holding the default root
                        if (true == commandLine.Contains("--type=")) continue;

                        Match match = Regex.Match(commandLine, profileArgument);

                        // The directory on the command line, or defaultRoot.
                        string holds = true == match.Success
                                     ? NormalizedDirectory(match.Groups[1].Value.Trim('"'))
                                     : defaultRoot;

                        if (true == string.IsNullOrEmpty(holds)) continue;

                        if (true == wanted.Equals(holds, StringComparison.OrdinalIgnoreCase))
                        {
                            retVal = Convert.ToInt32(found["ProcessId"]);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Optional check. If WMI fails, warn and treat the profile as not open.
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not check whether [{profile}] is already open", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
        // Full path without trailing slash, so different spellings of one directory compare equal.
        private static string NormalizedDirectory(string path)
        {
            string retVal = path;

            try
            {
                retVal = Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch
            {
                retVal = path.TrimEnd('\\', '/');
            }

            return retVal;
        }

        // Headers the browser sets itself. In-page fetch cannot override them, so they are not copied; only headers
        // the site's code added matter.
        private static readonly string[] browserOwnedHeaders =
        {
            "referer", "user-agent", "host", "origin", "cookie", "content-length",
            "connection", "accept-encoding", "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
            "sec-ch-ua-arch", "sec-ch-ua-model", "sec-ch-ua-full-version-list", "sec-ch-device-memory",
            "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site"
        };

        /// <summary>
        /// True for a header the browser sets itself, which an in-page fetch cannot set.
        /// </summary>
        /// <param name="name">Header name</param>
        /// <returns>True when the browser owns it</returns>
        internal static bool BrowserOwnedHeader(string name)
        {
            return Array.Exists(browserOwnedHeaders, owned => owned.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // Every port FindFreePort has returned in this process. A browser's port looks free until it starts
        // listening, so ports are never removed and never handed out twice.
        private static readonly HashSet<int> portsHandedOut = new HashSet<int>();

        // Where the next search starts, so ports climb instead of rescanning from the base
        private static int lastPortHandedOut = 0;

        /// <summary>
        /// Starts this browser's driver on the hidden desktop and waits for it to answer, so the browser the
        /// driver goes on to launch is created there as well. Selenium's own DriverService starts the driver
        /// through Process.Start, which has nowhere to name a desktop, so on a hidden run the driver is started
        /// here instead and Selenium is pointed at the port it is listening on.
        /// </summary>
        /// <param name="browserSettings">Settings for this browser, which receive the driver's pid.</param>
        /// <returns>The port the driver answered on.</returns>
        internal static int StartHiddenDriver(BrowserSettings browserSettings)
        {
            int retVal = FindFreePort();
            string driverName;
            string portArgument;

            switch (browserSettings.BrowserType)
            {
                case BrowserType.FireFox:
                    driverName = "geckodriver.exe";
                    portArgument = $"--port {retVal}";

                    // Firefox's Marionette on the debug port, so another program can connect to it
                    if (null != browserSettings.DebugPort)
                        portArgument += $" --marionette-port {browserSettings.DebugPort.Value}";
                    break;
                case BrowserType.Edge:
                    driverName = "msedgedriver.exe";
                    portArgument = $"--port={retVal}";
                    break;
                default:
                    driverName = "chromedriver.exe";
                    portArgument = $"--port={retVal}";
                    break;
            }

            string driverPath = Path.Combine(browserSettings.DriverLocation ?? AppDomain.CurrentDomain.BaseDirectory, driverName);

            // Selenium Manager is not used on this path, so a hidden desktop run needs the driver installed. Throw
            // naming the missing driver and folder.
            if (false == File.Exists(driverPath))
                throw new GPALException($"No [{driverName}] in [{Path.GetDirectoryName(driverPath)}], which a hidden run needs because Selenium Manager cannot fetch one here");

            Native.STARTUPINFO startup = new Native.STARTUPINFO
            {
                cb = (uint)Marshal.SizeOf<Native.STARTUPINFO>(),
                lpDesktop = HiddenDesktop.StartupNameFor(browserSettings.HiddenDesktopName)
            };

            // Firefox's sandboxed GPU process fails to launch on a hidden desktop, and Firefox runs WebGL in it, so a
            // page gets no WebGL context. geckodriver starts Firefox with its own environment, so it carries
            // MOZ_DISABLE_GPU_SANDBOX, which lifts that sandbox for this Firefox only.
            IntPtr environment = BrowserType.FireFox == browserSettings.BrowserType ? MagicHelper.EnvironmentBlockWith("MOZ_DISABLE_GPU_SANDBOX", "1") : IntPtr.Zero;
            Native.PROCESS_INFORMATION started;
            int startError = 0;

            try
            {
                if (false == Native.CreateProcess(null, $@"""{driverPath}"" {portArgument}", IntPtr.Zero, IntPtr.Zero, false,
                                                  Native.CREATE_NO_WINDOW | (IntPtr.Zero == environment ? 0 : Native.CREATE_UNICODE_ENVIRONMENT), environment, null, ref startup, out started))
                    startError = Marshal.GetLastWin32Error();
            }
            finally
            {
                if (IntPtr.Zero != environment)
                    Marshal.FreeHGlobal(environment);
            }

            if (0 != startError)
                throw new System.ComponentModel.Win32Exception(startError, $"Could not start [{driverName}] on [{browserSettings.HiddenDesktopName}]");

            Native.CloseHandle(started.hProcess);
            Native.CloseHandle(started.hThread);

            // Save the driver pid from CreateProcess; teardown kills its process tree by it. Without it the browser
            // the driver launched outlives the run.
            browserSettings.ServiceDriverPid = (int)started.dwProcessId;

            if (false == WaitForPort(retVal, 30_000))
                throw new GPALException($"[{driverName}] never answered on port [{retVal}]");

            return retVal;
        }

        /// <summary>
        /// Waits for something to start listening on a port.
        /// </summary>
        /// <param name="port">The port to watch.</param>
        /// <param name="timeoutInMs">How long to give it.</param>
        /// <returns>True once the port answers.</returns>
        private static bool WaitForPort(int port, int timeoutInMs)
        {
            bool retVal = false;
            DateTime giveUpAt = DateTime.Now.AddMilliseconds(timeoutInMs);

            while (false == retVal && DateTime.Now < giveUpAt)
            {
                try
                {
                    using (TcpClient probe = new TcpClient())
                    {
                        probe.Connect("127.0.0.1", port);
                        retVal = true;
                    }
                }
                catch (SocketException)
                {
                    Thread.Sleep(200);
                }
            }

            return retVal;
        }

        /// <summary>
        /// The first port from <paramref name="startPort"/> up that nothing is listening on and that has not
        /// just been handed to another browser. Two workflows starting together get two different ports.
        /// </summary>
        /// <param name="startPort">Where to start looking.</param>
        /// <returns>A port to start a browser on.</returns>
        internal static int FindFreePort(int startPort = 0xdead)
        {
            int retVal = 0;

            lock (portsHandedOut)
            {
                // Continues from the last port handed out, so one not yet bound is not offered again
                int from = Math.Max(startPort, lastPortHandedOut + 1);

                for (int port = from; port <= 65535 && 0 == retVal; port++)
                {
                    if (true == portsHandedOut.Contains(port))
                        continue;

                    // Test by binding, not connecting. A connect test calls a port free when its browser has not
                    // started listening yet.
                    TcpListener probe = null;

                    try
                    {
                        probe = new TcpListener(System.Net.IPAddress.Loopback, port);
                        probe.Start();

                        // claimed while the lock is still held, so the next thread in never sees this one free
                        portsHandedOut.Add(port);
                        lastPortHandedOut = port;
                        retVal = port;
                    }
                    catch (SocketException)
                    {
                        // somebody holds it, so move along
                    }
                    finally
                    {
                        probe?.Stop();
                    }
                }
            }

            if (0 == retVal)
                throw new GPALException($"No free ports available between [{startPort}] and [65535].");

            return retVal;
        }

        /// <summary>
        /// Records the current document so <see cref="DocumentReplaced"/> can tell after a click whether a new page
        /// loaded, which shows before the url changes.<br/>
        /// Selenium keeps the html element; Puppeteer and OttoMagic keep a DocumentStamp.
        /// </summary>
        /// <returns>A token to hand to <see cref="DocumentReplaced"/>, null when this engine cannot mark one</returns>
        /// <summary>
        /// Returns performance.timeOrigin and location.href in one call, separated by a bar.<br/>
        /// A new timeOrigin means a new document, even at the same url, as when a form posts to itself; it does not
        /// change on a same-document navigation. One call saves a round trip per poll.
        /// </summary>
        /// <param name="browser">Browser to read</param>
        /// <returns>timeOrigin and url separated by a bar, or null when the page could not answer</returns>
        internal static string DocumentStamp(Browser browser)
        {
            // OttoMagic uses its own endpoint. ExecuteJavaScript there runs through userScripts.execute, which
            // requires the profile to allow user scripts
            string stamp = true == browser.UseOttoMagic
                ? browser.MagicHelper.GetDocumentStamp()
                : ExecuteJavaScriptObj("return performance.timeOrigin + '|' + location.href;", browser)?.ToString();

            return stamp;
        }

        // Split on the first bar; timeOrigin comes first and has none, the url may.
        private static string OriginFromStamp(string stamp)
        {
            int bar = stamp?.IndexOf('|') ?? -1;
            string origin = 0 <= bar ? stamp.Substring(0, bar) : null;

            return origin;
        }

        private static string UrlFromStamp(string stamp)
        {
            int bar = stamp?.IndexOf('|') ?? -1;
            string url = 0 <= bar ? stamp.Substring(bar + 1) : null;

            return url;
        }

        internal static object MarkDocument(Browser browser)
        {
            object mark = null;

            // Selenium marks the document with the html element and detects replacement by staleness, which
            // needs no script. The others take a stamp, which also carries the url the poll would otherwise
            // fetch separately
            if (true == browser.UseSelenium)
                mark = browser.BrowserDriver.FindElement(By.TagName("html"));
            else if (true == browser.UsePuppeteer || true == browser.UseOttoMagic)
                mark = DocumentStamp(browser);

            return mark;
        }
        /// <summary>
        /// True if the marked document was replaced. Selenium: reading the saved html element throws
        /// StaleElementReferenceException or NoSuchElementException. Puppeteer and OttoMagic: a new DocumentStamp
        /// has a different timeOrigin. With no mark, false, leaving only the url check.
        /// </summary>
        /// <param name="browser">Browser the mark came from</param>
        /// <param name="mark">Token from <see cref="MarkDocument"/></param>
        /// <returns>true when the page we marked is gone, false while it is still there or we cannot tell</returns>
        internal static bool DocumentReplaced(Browser browser, object mark)
        {
            bool replaced = false;

            if (true == browser.UseSelenium && mark is IWebElement html)
                try { _ = html.TagName; }
                catch (StaleElementReferenceException) { replaced = true; }
                catch (NoSuchElementException) { replaced = true; }
            else if (mark is string priorStamp)
            {
                string nowOrigin = OriginFromStamp(DocumentStamp(browser));
                string wasOrigin = OriginFromStamp(priorStamp);

                replaced = false == string.IsNullOrEmpty(nowOrigin)
                        && false == string.IsNullOrEmpty(wasOrigin)
                        && wasOrigin != nowOrigin;
            }

            return replaced;
        }
        /// <summary>
        /// After a click, polls until the document is replaced or the url changes, or
        /// <paramref name="timeOutAfterMs"/> passes.<br/>
        /// Replacement shows first and catches a slow load whose url still shows the old page; the url catches a
        /// same-document navigation. A missed navigation means the workflow reads the old page.<br/>
        /// Console and debug events are off while polling.
        /// </summary>
        /// <param name="browser">Browser to watch</param>
        /// <param name="priorUrl">Url the browser was on before the action that may have navigated</param>
        /// <param name="documentMark">Document held before the action, from <see cref="MarkDocument"/></param>
        /// <param name="timeOutAfterMs">How long to keep watching before deciding nothing navigated</param>
        /// <returns>true when the browser started going somewhere inside the grace period, false when it never did</returns>
        internal static bool WaitForNavigationOrTime(Browser browser, string priorUrl, object documentMark, int timeOutAfterMs)
        {
            bool navigated = false;
            bool documentGone = false;
            bool urlChanged = false;
            string lastUrl = priorUrl;
            DateTime timeout = DateTime.Now.AddMilliseconds(timeOutAfterMs);
            GPALEventType publishToConsole = GPAL.GPALSettings.ConsoleEvents;
            GPALEventType publishToDebug = GPAL.GPALSettings.DebugEvents;

            GPAL.GPALSettings.ConsoleEvents = GPALEventType.NONE;
            GPAL.GPALSettings.DebugEvents = GPALEventType.NONE;

            try
            {
                if (true == browser.IsAlive)
                    do
                    {
                        // One round trip for both signals. A stamp that cannot be read leaves the last known
                        // url in place and the loop repeats
                        if (documentMark is string priorStamp)
                        {
                            string nowStamp = DocumentStamp(browser);
                            string nowOrigin = OriginFromStamp(nowStamp);
                            string wasOrigin = OriginFromStamp(priorStamp);

                            // A missing origin on either side means undecided. Only two origins that both
                            // arrived and differ count as a replaced document
                            documentGone = false == string.IsNullOrEmpty(nowOrigin)
                                        && false == string.IsNullOrEmpty(wasOrigin)
                                        && wasOrigin != nowOrigin;

                            lastUrl = UrlFromStamp(nowStamp) ?? lastUrl;
                        }
                        else
                        {
                            documentGone = DocumentReplaced(browser, documentMark);
                            lastUrl = browser.GetSetCurrentUrl();
                        }

                        urlChanged = false == UrlHelper.AreEquivalent(priorUrl, lastUrl);

                        navigated = true == documentGone || true == urlChanged;

                        // only sleep if we are going round again, so a grace of 0 costs exactly one look
                        if (false == navigated && DateTime.Now < timeout)
                            Thread.Sleep(NavigationPollMs);
                    }
                    while (false == navigated && DateTime.Now < timeout);
            }
            finally
            {
                GPAL.GPALSettings.ConsoleEvents = publishToConsole;
                GPAL.GPALSettings.DebugEvents = publishToDebug;
            }

            // One DEBUG line per click, after events are back on: document replaced, url changed, and whether there
            // was a document mark.
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                $"Navigation watch [{(navigated ? "navigated" : "stayed")}] document [{(documentGone ? "replaced" : "same")}] url [{(urlChanged ? "changed" : "same")}] mark [{(documentMark is long mark ? mark.ToString() : null == documentMark ? "none" : "held")}] from [{priorUrl}] to [{lastUrl}]",
                browser, GPALObjectType.Browser);

            return navigated;
        }
        // TODO - pass in a unique GUID to each method (add a parm to them) so the called methods can suppress repeat messages knowing they are called with the same token (so the same session)
        // Wait between polls in WaitForValueOrTime: little delay for a ready page, a few polls per second for a slow
        // one.
        internal const int WaitForValuePollMs = 250;
        // Wait between network idle asks, every engine. A wait for idle already runs for the quiet period and more, so
        // asking twice a second adds at most half a second, and on Selenium each ask is a call into the page.
        internal const int NetworkIdlePollMs = 500;

        /// <summary>
        /// Method that will repeatedly call another method until it either times out or it gets the expected result string from the method
        /// the expected results can be a csv string if multiple values are acceptable
        /// this method is used to check document.ready status or to check for network idle conditions. so the expectes stringd are well known or user defined
        /// </summary>
        /// <param name="browser">Browser the waited-on value comes from</param>
        /// <param name="resultsExpected">string or csv string list of expected return results</param>
        /// <param name="action">method and parms to call repeatedly</param>
        /// <param name="timeOutAfterMs">time to wait in milliseconds for the expected result before timing out</param>
        /// <param name="pollMs">time to wait in milliseconds between calls</param>
        internal static void WaitForValueOrTime(Browser browser, string resultsExpected, Func<dynamic> action, int timeOutAfterMs, int pollMs = WaitForValuePollMs)
        {
            DateTime timeout = DateTime.Now.AddMilliseconds(timeOutAfterMs);
            string taskReturnValue;
            // Trim each expected value; callers put a space after each comma.
            string[] tokens = resultsExpected.Split(',').Select(token => token.Trim()).ToArray();

            if (true == browser.IsAlive)
                do
                {
                    dynamic result = action();

                    try
                    {
                        // Try to treat as Task
                        var task = (Task<string>)result; // will throw if not a Task
                        task.GetAwaiter().GetResult(); // block until complete

                        // If generic Task<T>, get the Result property
                        var taskType = task.GetType();
                        if (taskType.IsGenericType)
                        {
                            var taskResult = taskType.GetProperty("Result")?.GetValue(task);
                            taskReturnValue = taskResult?.ToString() ?? resultsExpected;
                        }
                        else
                        {
                            taskReturnValue = tokens[0]; // non-generic Task
                        }
                    }
                    catch
                    {
                        // Not a Task — treat as a string
                        taskReturnValue = result?.ToString() ?? resultsExpected;
                    }

                    foreach (string token in tokens)
                        if (true == taskReturnValue.ToLower().Contains(token))
                            return;

                    Thread.Sleep(pollMs);
                }
                while (DateTime.Now < timeout);
        }

        #region NetworkHelpers
        /// <summary>
        /// Answers at once whether the network has been idle: at most <paramref name="maxConnections"/> requests in
        /// flight for the whole of the last <paramref name="pruneMs"/>. The caller asks again until it gets true or
        /// its own timeout ends.<br/>
        /// Puppeteer and OttoMagic ask their engine, which publishes what it found. Selenium looks at the javascript
        /// network monitor (window.__gpalNetworkMonitor); a page without it (Firefox after a navigation) gets it here,
        /// and is watched from then on.
        /// </summary>
        /// <param name="browser">Browser to monitor</param>
        /// <param name="maxConnections">The most requests in flight that still count as idle</param>
        /// <param name="pruneMs">How far back, in milliseconds, the in-flight count must have stayed at or below maxConnections</param>
        /// <param name="sessionToken">Token used to suppress repeated log messages within the same wait session</param>
        /// <returns>True if the network is idle, false otherwise</returns>
        public static bool WaitForNetworkIdle(Browser browser, int maxConnections, int pruneMs, string sessionToken)
        {
            bool retVal = false;

            if (true == browser.UsePuppeteer)
                retVal = browser.PuppeteerClient.CheckNetworkIdle(maxConnections).WithSessionToken(sessionToken).WithPruneMs(pruneMs).Execute<bool>();
            else if (true == browser.BrowserSettings.UseOttoMagic)
                retVal = true == browser.OttoMagicClient.WithEndpoint(ApiEndpoint.CheckNetworkIdle).WithMaxConnections(maxConnections).WithPruneMs(pruneMs).Execute()?.Contains("idle");
            else
                retVal = SeleniumNetworkIdle(browser, maxConnections, pruneMs, sessionToken);

            return retVal;
        }

        // The target info of the tab chromedriver is on, asked on chromedriver's own CDP session, which runs nothing in
        // the page
        private static Dictionary<string, object> CurrentTargetInfo(IWebDriver driver)
        {
            var current = ExecuteCdp(driver, "Target.getTargetInfo", new Dictionary<string, object>()) as Dictionary<string, object>;

            return current?["targetInfo"] as Dictionary<string, object>;
        }

        // The target id of the tab chromedriver is on
        private static string CurrentTargetId(IWebDriver driver)
        {
            return CurrentTargetInfo(driver)?["targetId"]?.ToString();
        }

        // Selenium has no engine to ask, so this looks at the page's monitor and publishes what it found.
        private static bool SeleniumNetworkIdle(Browser browser, int maxConnections, int pruneMs, string sessionToken)
        {
            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                supressedMessage = false;
                lastSessionToken = sessionToken;
            }

            string currentErrorMessage = $"Waiting for network to idle to [{maxConnections}] connections for [{pruneMs}] ms";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, browser, GPALObjectType.Browser);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }

            // Chrome and Edge count the tab's requests on GPAL's own connection to the browser, so an ask runs nothing
            // in the page. Firefox has no such connection and asks the page's monitor.
            if (BrowserType.FireFox != browser.BrowserSettings.BrowserType)
                browser.BrowserSettings.TabScripts ??= SeleniumTabScripts.Start(browser.BrowserSettings);

            bool retVal;

            if (null != browser.BrowserSettings.TabScripts)
                retVal = browser.BrowserSettings.TabScripts.CheckNetworkIdle(CurrentTargetId(browser.BrowserDriver), maxConnections, pruneMs);
            else
                // Idle when the page has been watched for pruneMs, is at or below maxConnections now, and has not
                // dropped from above maxConnections within the last pruneMs.
                retVal = Convert.ToBoolean(BrowserHelper.ExecuteJavaScriptObj(Browser.NetworkMonitorScript + $@"
                    const monitor = window.__gpalNetworkMonitor;
                    const windowStart = Date.now() - {pruneMs};
                    return monitor.since <= windowStart && monitor.inflight <= {maxConnections} && !(monitor.droppedBelow[{maxConnections + 1}] > windowStart);",
                    browser));

            currentErrorMessage = $"Network IS{(retVal ? "" : " NOT")} Idle. Status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, browser, GPALObjectType.Browser);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }
            return retVal;
        }

        // requires selenium.webdriver update, we are using the framework version :(
        //public static string WaitForNetworkIdle(Browser browser)
        //{
        //    var task = WaitForNetworkIdleAsyncInternal(browser);
        //    return task.GetAwaiter().GetResult();
        //}
        //public static async Task<string> WaitForNetworkIdleAsyncInternal(Browser browser)
        //{ 
        //    int timeoutMs = browser.BrowserSettings.NetworkIdleTimeoutMs;
        //    int maxConnections = browser.BrowserSettings.NetworkIdleMaxConnections;
        //    int pruneMs = browser.BrowserSettings.NetworkIdlePruneMs;
        //    IWebDriver driver = browser.BrowserDriver;


        //    if (!(driver is IDevTools devToolsDriver))
        //    {
        //        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "This driver does not support check network idle.", browser, GPALObjectType.Browser);
        //        return "true";
        //    }

        //    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting up to [{timeoutMs}] ms for network to idle to [{maxConnections}] connections for [{pruneMs}] ms", browser, GPALObjectType.Browser);

        //    var network = driver.Manage().Network;
        //    var requests = new ConcurrentBag<string>();
        //    bool isIdle = false;

        //    var activeRequests = new ConcurrentDictionary<string, byte>();

        //    // Handlers
        //    EventHandler<NetworkRequestSentEventArgs> requestSentHandler = (sender, e) =>
        //    {
        //        activeRequests[e.RequestId] = 0;
        //    };

        //    EventHandler<NetworkResponseReceivedEventArgs> responseReceivedHandler = (sender, e) =>
        //    {
        //        activeRequests.TryRemove(e.RequestId, out _);
        //    };

        //    // Attach
        //    network.NetworkRequestSent += requestSentHandler;
        //    network.NetworkResponseReceived += responseReceivedHandler;

        //    try
        //    {
        //        await network.StartMonitoring();

        //        var overallTimeout = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        //        DateTime? idleSince = null;

        //        while (DateTime.UtcNow < overallTimeout)
        //        {
        //            int openConnections = activeRequests.Count;

        //            if (openConnections <= maxConnections)
        //            {
        //                idleSince ??= DateTime.UtcNow; // mark when idle started
        //                if ((DateTime.UtcNow - idleSince.Value).TotalMilliseconds >= pruneMs)
        //                {
        //                    isIdle = true;
        //                    break;
        //                }
        //            }
        //            else
        //            {
        //                idleSince = null; // reset idle timer if activity resumed
        //            }

        //            await Task.Delay(250); // small polling interval
        //        }

        //        if (!isIdle)
        //            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Network idle timeout reached.", browser, GPALObjectType.Browser);
        //    }
        //    finally
        //    {
        //        network.NetworkRequestSent -= requestSentHandler;
        //        network.NetworkResponseReceived -= responseReceivedHandler;
        //        await network.StopMonitoring();
        //    }

        //    return isIdle.ToString();
        //}
        /// <summary>
        /// Returns all active TCP listener endpoints on this machine.
        /// </summary>
        /// <returns>Array of IPEndPoint for every active TCP listener</returns>
        public static IPEndPoint[] GetAllTcpListeners()
        {
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            return ipGlobalProperties.GetActiveTcpListeners();
        }
        /// <summary>
        /// Checks whether anything is currently listening on the given TCP port.
        /// </summary>
        /// <param name="port">Port number to check</param>
        /// <returns>True if a TCP listener is active on the port, false otherwise</returns>
        /// <example>
        /// <code>
        /// if (BrowserHelper.IsPortListening(9222))
        ///     GPAL.PublishSimpleEvent(GPALEventType.INFO, "CDP debug port already in use.");
        /// </code>
        /// </example>
        public static bool IsPortListening(int port)
        {
            IPEndPoint[] tcpListeners = GetAllTcpListeners();
            return tcpListeners.Any(ep => ep.Port == port);
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;     // network byte order in the low 16 bits
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        // Process names of the browsers GPALRestAPI can be started by.
        private static readonly HashSet<string> browserProcessNames = new HashSet<string> { "chrome", "msedge", "firefox" };

        /// <summary>
        /// The browser behind a GPALRestAPI address on this machine. GPALRestAPI is the extension's native host, which
        /// the browser starts (Chrome and Edge through cmd.exe), so the browser is found by walking up from the
        /// process GPALRestAPI reports as its own. Its port is held by Windows' HTTP service, not by GPALRestAPI, so
        /// the port does not lead to it. An address on another machine has no process here.
        /// </summary>
        /// <param name="restApiBaseUrl">The GPALRestAPI base url</param>
        /// <returns>The browser process, or null when the address is not on this machine or no browser started it</returns>
        internal static Process LocalRestApiBrowser(string restApiBaseUrl)
        {
            Process retVal = null;
            Uri address = new Uri(restApiBaseUrl);

            if (true == address.IsLoopback)
            {
                int restApiPid = 0;

                try
                {
                    System.Net.HttpWebRequest ask = (System.Net.HttpWebRequest)System.Net.WebRequest.Create($"{restApiBaseUrl}process-id");
                    ask.Timeout = 1000;
                    ask.ReadWriteTimeout = 1000;

                    using (System.Net.HttpWebResponse answer = (System.Net.HttpWebResponse)ask.GetResponse())
                    using (StreamReader reader = new StreamReader(answer.GetResponseStream()))
                        int.TryParse(reader.ReadToEnd().Trim().Trim('"'), out restApiPid);
                }
                catch (System.Net.WebException)
                {
                    // not answering, so there is no process to start from
                }

                Process process = 0 < restApiPid ? Process.GetProcesses().FirstOrDefault(p => p.Id == restApiPid) : null;

                for (int level = 0; level < 4 && null != process && null == retVal; level++)
                {
                    int parentId = 0;

                    using (var search = new System.Management.ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {process.Id}"))
                        foreach (System.Management.ManagementObject found in search.Get())
                            parentId = Convert.ToInt32(found["ParentProcessId"]);

                    process = 0 < parentId ? Process.GetProcesses().FirstOrDefault(p => p.Id == parentId) : null;

                    if (null != process && true == browserProcessNames.Contains(process.ProcessName.ToLowerInvariant()))
                        retVal = process;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"GPALRestAPI [{restApiBaseUrl}] was started by browser process [{retVal?.Id.ToString() ?? "none found"}]", null, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// The process listening on a local TCP port, from the Windows TCP table. A browser's debug port is opened by
        /// the browser itself, so for a browser GPAL connected to rather than launched, this is its process.
        /// </summary>
        /// <param name="port">The port the process listens on</param>
        /// <returns>The listening process, or null when nothing listens on the port</returns>
        internal static Process PortOwnerProcess(int port)
        {
            Process retVal = null;
            int pid = 0;
            int size = 0;

            // The first call answers the size the table needs.
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);

            IntPtr table = Marshal.AllocHGlobal(size);

            try
            {
                if (0 == GetExtendedTcpTable(table, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0))
                {
                    int rows = Marshal.ReadInt32(table);
                    int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                    for (int i = 0; i < rows && 0 == pid; i++)
                    {
                        MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(table + 4 + i * rowSize, typeof(MIB_TCPROW_OWNER_PID));
                        int rowPort = (int)(((row.localPort & 0xFF) << 8) | ((row.localPort >> 8) & 0xFF));

                        if (port == rowPort)
                            pid = (int)row.owningPid;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }

            if (0 < pid)
                retVal = Process.GetProcesses().FirstOrDefault(p => p.Id == pid);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Port [{port}] is held by process [{pid}], {(null != retVal ? "found" : "not running")}", null, GPALObjectType.Browser);

            return retVal;
        }
        #endregion NetworkHelpers
    }

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

