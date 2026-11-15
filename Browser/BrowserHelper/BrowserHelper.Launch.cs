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
    public partial class BrowserHelper
    {
        /// <summary>
        /// Brings the browser's driver up to date when GPAL.WithAutoUpdateWebDriver is on, once until the drivers are
        /// next killed. Every engine's launch calls it, Puppeteer and OttoMagic included, which never start the driver
        /// themselves, so it is current for whichever engine runs next. With no driver location set, the driver is
        /// the one beside the program.
        /// </summary>
        /// <param name="browserSettings">The browser being launched, whose type picks the driver</param>
        internal static void UpdateDriverIfAsked(BrowserSettings browserSettings)
        {
            if (null == browserSettings.DriverLocation)
#if NET
                browserSettings.DriverLocation = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#else
                browserSettings.DriverLocation = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6);    // skip file:\\
#endif

            if (true == GPAL.GPALSettings.AutoUpdateWebDriver && false == alreadyUpdating)
            {
                alreadyUpdating = true;
                DriverHelper.UpdateDriver(browserSettings);
            }
        }
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

            UpdateDriverIfAsked(browserSettings);

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
                        if (null == browserSettings.DebugPort && true == browserSettings.UsePuppeteer && false == browserSettings.DebugPipe)  // no debug port given: take a free one and set PuppeteerUrl from it
                        {
                            // First free port from 0xdead (57005) up.
                            browserSettings.DebugPort = FindFreePort();
                            browserSettings.PuppeteerUrl = $"http://127.0.0.1:{browserSettings.DebugPort}";
                        }

                        // the download directory goes in the profile preferences
                        if (false == string.IsNullOrEmpty(browserSettings.DownloadLocation))
                            options.AddUserProfilePreference("download.default_directory", browserSettings.DownloadLocation);

                        // Chrome 136 and later require a user-data-dir, so one is always given: the profile's or a temporary one
                        // Profile settings
                        if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                        {
                            browserSettings.ProfileDataDirectory = GetBrowserProfileDirectory(browserSettings);

                            if (false == string.IsNullOrEmpty(browserSettings.ProfileName))
                            {
                                // the profile name can be a display name rather than a directory name. FindProfileDirectory
                                // matches it to profile.name in each Preferences file under user-data-dir and returns that
                                // directory, or throws
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

                        // Selenium runs with extensions off; Selenium and OttoMagic's GPALRestAPI cannot drive the same browser
                        options.AddArguments("--disable-extensions");
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

                        // download.directory_upgrade, off by default: when download.default_directory is missing or
                        // inaccessible, Chrome falls back to a valid folder instead of failing the download
                        options.AddUserProfilePreference("download.directory_upgrade", true);

                        options.AddUserProfilePreference("plugins.always_open_pdf_externally", browserSettings.OpenPDFExternally);
                        options.AddArguments($"--blink-settings=imagesEnabled={browserSettings.LoadImages.ToString().ToLower()}");
                        options.AddUserProfilePreference("disable-popup-blocking", $"{!browserSettings.BlockPopUps}");

                        // turn on dark mode
                        if (browserSettings.StealthType.HasFlag(StealthType.DarkMode))
                            options.AddArgument("--force-dark-mode");

                        // remote debugging pipe or port
                        if (true == browserSettings.DebugPipe)
                            options.AddArgument("--remote-debugging-pipe");
                        else if (null != browserSettings.DebugPort)
                            options.AddArgument($"--remote-debugging-port={browserSettings.DebugPort.Value}");

                        //options.AddUserProfilePreference("safebrowsing.enabled", true); // safebrowsing on shows the 'this may harm your computer' download warnings

                        // turn off the AutomationControlled blink feature, which sets navigator.webdriver. Chrome shows an
                        // unsupported-flag infobar for it
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

                    // no start url on the command line: Selenium puts -- in front of it, and some enterprise browsers force
                    // their own first page

                    // performance logging gives SeleniumGoToUrl the navigation's status code
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

                            // no --user-agent: Firefox fails to start with it
                            // if (null != userAgent)
                            //   ffOptions.AddArgument($"--user-agent={userAgent}");
                        }

                        if (null != browserSettings.DebugPort)
                        {
                            if (true == browserSettings.UsePuppeteer)
                                browserSettings.PuppeteerUrl = $"http://127.0.0.1:{browserSettings.DebugPort}";
                        }
                        else if (true == browserSettings.UsePuppeteer)  // no debug port given: take a free one and set PuppeteerUrl from it
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

                        ffOptions.SetPreference("safebrowsing.enabled", false); // turns off the 'this may harm your computer' download warnings

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
                            ffOptions.AddArguments("-profile", (browserSettings.ProfileName ?? browserSettings.ProfileUserName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        else if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                            ffOptions.AddArguments("-profile", browserSettings.ProfileDataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                        // geckodriver writes its preferences and ours into a supplied profile's user.js, and Firefox
                        // saves them into prefs.js. Snapshot the profile for Browser.Close to restore.
                        if (false == browserSettings.TempProfileCreated && false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                            browserSettings.PreviousFirefoxPreferences = FirefoxProfileManager.TakeSnapshot(browserSettings.ProfileDataDirectory);
                    }

                    // A browser reached with WithExistingCDPDebugPort was connected to above; launching here as well would
                    // start a second Firefox and drop the connection.
                    if (false == browserSettings.UseExistingBrowser)
                    {
                        ffOptions.BinaryLocation = FindFirefoxBinaryLocation();

                        // geckodriver is started through an explicit service so its pid is known
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

                            // geckodriver's pid, in the same property chromedriver uses
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
                // unrecoverable, so rethrow
                throw;
            }
            if (null != tmpDriver)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Browser [{browserSettings.BrowserType}] launched{(true == browserSettings.UseHeadless ? " headless" : "")}.", browserSettings, GPALObjectType.Other);

                browserSettings.BrowserDriver = tmpDriver;

                // Edge preloads its New Tab Page into a hidden window that shows up later as an extra WindowHandle.
                // Before the first navigation the driver's url is that preload's url, so it is saved, and the phantom
                // tab is matched by this url, not by its position or a fixed string.
                var edgeGhostBrowser = browserSettings.Browser as Browser;
                if (BrowserType.Edge == browserSettings.BrowserType && null != edgeGhostBrowser)
                    try { edgeGhostBrowser.GhostTabUrl = tmpDriver.Url; }
                    catch { /* without the url no tab is skipped as the ghost */ }

                // The driver waits for a page load no longer than GPAL waits for document ready. Its own default of 300 s
                // outlasts the client's 60 s command timeout, so a page that never finishes loading keeps geckodriver
                // busy and every command sent meanwhile times out.
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
                    chromeService.HideCommandPromptWindow = true; // no console window

                    ChromeOptions chromeOptions = (ChromeOptions)options;

                    tmpDriver = new ChromeDriver(chromeService, chromeOptions);

                    // the chromedriver.exe pid
                    browserSettings.ServiceDriverPid = chromeService.ProcessId;
                }
                else // Edge
                {
                    EdgeDriverService edgeService = EdgeDriverService.CreateDefaultService(browserSettings.DriverLocation);
                    edgeService.HideCommandPromptWindow = true; // no console window

                    EdgeOptions edgeOptions = (EdgeOptions)options;

                    tmpDriver = new EdgeDriver(edgeService, edgeOptions);

                    // the msedgedriver.exe pid, in the same property
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
                    using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) })
                    using (HttpResponseMessage answer = client.GetAsync($"{baseUrl}status").ConfigureAwait(false).GetAwaiter().GetResult())
                        retVal = System.Net.HttpStatusCode.OK == answer.StatusCode;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    // refused, timed out, or answered something other than a running server
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
                    using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) })
                    using (HttpResponseMessage answer = client.GetAsync($"{restApiBaseUrl}process-id").ConfigureAwait(false).GetAwaiter().GetResult())
                        if (true == answer.IsSuccessStatusCode)
                            int.TryParse(answer.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult().Trim().Trim('"'), out restApiPid);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
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
    }
}
