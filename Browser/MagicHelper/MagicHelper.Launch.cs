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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using GenerallyPositive.Application;
using Microsoft.Win32.SafeHandles;
using OpenQA.Selenium;
using OpenQA.Selenium.DevTools;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class MagicHelper
    {
        /// <summary>
        /// Launches a browser with given settings
        /// <example>Process p = browser.MagicHelper.LaunchBrowser(new BrowserSettings(...), "https://example.com");</example>
        /// </summary>
        /// <returns>The browser process</returns>
        /// <summary>
        /// Builds a Unicode environment block for CreateProcess: this process's environment plus one variable. The
        /// caller frees it with Marshal.FreeHGlobal and passes CREATE_UNICODE_ENVIRONMENT.
        /// </summary>
        /// <param name="name">Variable to add or replace</param>
        /// <param name="value">Its value</param>
        /// <returns>The block, double null terminated</returns>
        internal static IntPtr EnvironmentBlockWith(string name, string value)
        {
            SortedDictionary<string, string> variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
                variables[(string)variable.Key] = (string)variable.Value;

            variables[name] = value;

            StringBuilder block = new StringBuilder();

            foreach (KeyValuePair<string, string> variable in variables)
                block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');

            block.Append('\0');

            return Marshal.StringToHGlobalUni(block.ToString());
        }

        /// <summary>
        /// Publishes each browser stdout or stderr line as DEEPDEBUG, keeping it out of normal output. A null line
        /// at end of stream is not published.
        /// </summary>
        /// <param name="browserSettings">Settings of the browser that wrote the line</param>
        /// <param name="data">The received line</param>
        static void PublishBrowserOutput(BrowserSettings browserSettings, DataReceivedEventArgs data)
        {
            if (null != data.Data)
                GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"[{browserSettings.BrowserType}] {data.Data}", browserSettings, GPALObjectType.Other);
        }
        /// <summary>
        /// firefox.exe from a command line starts a child and exits, and the child owns the window. Wait for the
        /// child and store it in browserSettings.Process; if none appears before the deadline, keep the launcher.
        /// </summary>
        /// <param name="browserSettings">The settings holding the process to swap</param>
        /// <param name="launcherPid">The process id the launch started</param>
        /// <param name="deadline">When to stop waiting, the same one the window wait uses</param>
        private static void AdoptFirefoxChild(BrowserSettings browserSettings, int launcherPid, DateTime deadline)
        {
            if (1 > launcherPid) return;

            while (DateTime.UtcNow < deadline)
            {
                int childPid = 0;

                try
                {
                    using (var searcher = new ManagementObjectSearcher(
                        $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {launcherPid} AND Name = 'firefox.exe'"))
                    {
                        foreach (ManagementObject found in searcher.Get())
                        {
                            childPid = Convert.ToInt32(found["ProcessId"]);
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // A WMI failure leaves the launcher process in place
                    return;
                }

                if (0 != childPid)
                {
                    try
                    {
                        browserSettings.Process = System.Diagnostics.Process.GetProcessById(childPid);

                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"FireFox launcher [{launcherPid}] handed over to browser process [{childPid}]", browserSettings, GPALObjectType.Browser);

                        return;
                    }
                    catch (Exception)
                    {
                        // Exited between being listed and being opened
                    }
                }

                Thread.Sleep(100);
            }
        }

        public Process LaunchBrowser(Browser browser, GPALUrl URL, List<string> additionalArguments = null)
        {
            BrowserSettings browserSettings = browser.BrowserSettings;
            bool created = false;
            int secondsToWait = 30;
            TimeSpan timeout = TimeSpan.FromSeconds(secondsToWait);

            if (null == browserSettings)
            {
                // No browser settings, as when OAuth credentials launch a browser to sign in: use a default browser
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "BrowserSettings parameter is null. Using chrome puppeteer port.", null, GPALObjectType.Other);
                browser = (Browser)GPAL.Browser.ToGPALObject();
                browserSettings = ((Browser)browser).BrowserSettings;
            }

            // Delete temp profiles left behind by killed runs. Runs on every launch, including ones with a supplied
            // profile.
            ChromeProfileManager.SweepAbandonedTempProfiles();

            if (null == URL || null == URL?.Url)
                URL = "about:blank";

            URL.ForUrl(GetFullUrl(URL?.Url, browser, out ((Browser)browser)._areRobotsAllowed, true));  // on launch we open to about:blank, don't do a robots.txt check on launch

            if (false == ((Browser)browser).AreRobotsAllowed && true == ((Browser)browser).BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Visiting [{URL?.Url}] is disallowed by robots.txt and your request to honor it. Not going to URL. Workflow will fail.", browser, GPALObjectType.Browser);
                URL.ForUrl("https://google.com");
            }

            bool headless = browserSettings.UseHeadless;
            var chromeArguments = additionalArguments ?? new List<string>();
            string browserPath;

            // Get browser executable path
            switch (browserSettings.BrowserType)
            {
                case BrowserType.Chrome:
                    browserPath = Puppeteer.GetChromePath();
                    break;
                case BrowserType.Edge:
                    browserPath = Puppeteer.GetEdgePath();
                    break;
                case BrowserType.FireFox:
                    browserPath = Puppeteer.GetFirefoxPath();
                    break;
                default:
                    Exception ex = new Exception("Unsupported Browser Type");
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported browser type [{browserSettings.BrowserType}]", browserSettings, GPALObjectType.Other, ex);
                    return null;
            }

            if (string.IsNullOrEmpty(browserPath) || false == File.Exists(browserPath))
            {
                string msg = $"[{browserSettings.BrowserType}] executable not found at [{browserPath}]";
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, browserSettings, GPALObjectType.Other);
                throw new NotFoundException(msg);
            }

            // Common arguments for Chrome/Edge
            if (browserSettings.BrowserType == BrowserType.Chrome || browserSettings.BrowserType == BrowserType.Edge)
            {
                string userAgent = null;

                // Profile settings
                if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                {
                    browserSettings.ProfileDataDirectory = BrowserHelper.GetBrowserProfileDirectory(browserSettings);
                    if (false == string.IsNullOrEmpty(browserSettings.ProfileName))
                    {
                        // FindProfileDirectory returns a directory or throws
                        // Quoted, as --user-data-dir below is. These arguments are joined into one command
                        // line and Chrome directory names contain spaces, so --profile-directory=Profile 4
                        // would split into a different profile plus a bare 4 that Chrome opens as a url
                        chromeArguments.Add($@"--profile-directory=""{BrowserHelper.FindProfileDirectory(browserSettings.ProfileDataDirectory, browserSettings.ProfileName)}""");
                    }
                }
                else if (true == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                {
                    browserSettings.ProfileDataDirectory = ChromeProfileManager.CreateTempUserProfile(
                        browserSettings.DownloadLocation ?? FileHelper.GetDefaultDownloadDirectory(browser),
                        browserSettings.PromptForDownload,
                        browserSettings.OpenPDFExternally,
                        browserSettings.LoadImages,
                        browserSettings.UseOttoMagic
                    );
                    browserSettings.TempProfileCreated = true;

                    // Chrome and Edge keep an extension only when it is installed through the browser, so a temp
                    // profile has no OttoMagic.
                    if (true == browserSettings.UseOttoMagic)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"OttoMagic needs a profile with it installed. [{browserSettings.BrowserType}] on a temporary profile has no OttoMagic. Give the workflow a profile, or use PuppeteerPort.", browser, GPALObjectType.Browser);
                }

                // Download prompt, download location and the PDF viewer are profile preferences the extension
                // cannot set, and there is no CDP here, so write them into the supplied profile. Browser.Close
                // restores them. Selenium and Puppeteer set these in BrowserHelper; Firefox in CreateUserJs below.
                if (false == browserSettings.TempProfileCreated && false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                    browserSettings.PreviousDownloadPreferences = ChromeProfileManager.ApplyDownloadPreferences(
                        browserSettings.ProfileDataDirectory,
                        browserSettings.DownloadLocation,
                        browserSettings.PromptForDownload,
                        browserSettings.OpenPDFExternally);

                // A profile can be open in only one browser. Launching onto an open one hands the command line to
                // it and exits, so the extension never loads and GPALRestAPI never reports a port. Refuse here, as
                // Selenium does.
                if (false == browserSettings.TempProfileCreated)
                    BrowserHelper.RefuseIfProfileIsOpen(browserSettings);

                // Set here, ahead of the arguments, so the browser starts with --remote-debugging-pipe
                if (AutomationEngine.PuppeteerPipe == browserSettings.AutomationEngine || AutomationEngine.PuppeteerPipeHW == browserSettings.AutomationEngine)
                    browserSettings.DebugPipe = true;

                // Debugging
                if (true == browserSettings.UsePuppeteer)
                {
                    if (true == browserSettings.DebugPipe)
                    {
                        // No --enable-unsafe-extension-debugging: GPAL sends no Extensions commands, and Chrome warns
                        // about the flag on screen. No debug port either: the pipe is the only way in
                        chromeArguments.Add("--remote-debugging-pipe");

                        // Driven over a pipe, Chrome and Edge report navigator.webdriver true, which bot protection
                        // turns away; over a port they report false. The AutomationControlled switch keeps it false.
                        // Chrome puts a warning bar on the page for that switch, which shortens the page, and
                        // --test-type, which ChromeDriver passes on every launch, keeps the bar off
                        chromeArguments.Add("--disable-blink-features=AutomationControlled");
                        chromeArguments.Add("--test-type");
                    }
                    else
                    {
                        // Pick a free port; with a fixed 0xdead two browsers launched together share one session.
                        if (null == browserSettings.DebugPort)
                            browserSettings.DebugPort = BrowserHelper.FindFreePort();

                        browserSettings.PuppeteerUrl = $"http://localhost:{browserSettings.DebugPort}";
                        chromeArguments.Add($"--remote-debugging-port={browserSettings.DebugPort.Value}");
                    }

                    // Back and Forward load the page again instead of restoring it from the back/forward cache. Chrome
                    // holds a command sent to a restored page, such as a screenshot, until it draws, which after Back
                    // was never. Playwright launches Chromium this way for the same reason.
                    chromeArguments.Add("--disable-back-forward-cache");

                }

                // Window size and position, unless the window opens full screen, maximized or minimized
                if (false == browserSettings.FullScreen &&
                    false == browserSettings.Maximize &&
                    false == browserSettings.Minimize)
                {
                    if (false == browserSettings.WindowSize.IsEmpty)
                    {
                        // explicit WindowSize provided - must set screen-info in headless=new
                        // otherwise CDP interactions fail with "Browser window not found"
                        chromeArguments.Add($"--window-size={browserSettings.WindowSize.Width},{browserSettings.WindowSize.Height}");

                        // WindowSize also holds the position, so several browsers can sit side by side. 0,0 is not
                        // sent.
                        if (0 != browserSettings.WindowSize.X || 0 != browserSettings.WindowSize.Y)
                            chromeArguments.Add($"--window-position={browserSettings.WindowSize.X},{browserSettings.WindowSize.Y}");
                    }
                    else if (browserSettings.UseHeadless)
                        chromeArguments.Add("--start-fullscreen");
                }
                else if (true == browserSettings.FullScreen)
                    chromeArguments.Add("--start-fullscreen");
                else if (true == browserSettings.Maximize)
                    chromeArguments.Add("--start-maximized");

                // Anti-bot detection
                //if (true == myBrowserSettings.UsePuppeteer || AutomationEngine.PuppeteerPortHW == myBrowserSettings.AutomationEngine)
                //{
                // chromeArguments.Add("--disable-blink-features=AutomationControlled");
                chromeArguments.Add("--excludeSwitches=enable-automation");
                chromeArguments.Add("--useAutomationExtension=false");
                //}

                // Core arguments to suppress popups and stabilize CDP
                chromeArguments.Add("--no-first-run");
                chromeArguments.Add("--no-default-browser-check");
                //chromeArguments.Add("--no-default-browser-check");
                //chromeArguments.Add("--disable-default-apps");
                //chromeArguments.Add("--disable-features=TranslateUI,FirstRun,ChromeWhatsNewUI,InterestCohort");
                //chromeArguments.Add("--disable-background-networking");
                //chromeArguments.Add("--disable-background-timer-throttling");
                //chromeArguments.Add("--disable-renderer-backgrounding");
                //chromeArguments.Add("--disable-backgrounding-occluded-windows");
                //chromeArguments.Add("--no-service-autorun");
                //chromeArguments.Add("--disable-client-side-phishing-detection");
                chromeArguments.Add("--disable-dev-shm-usage");

                // Edge signs a new profile into the Windows account and syncs favourites, settings and extensions,
                // each of which opens a page on install. Turn sync off for temp profiles only.
                if (true == browserSettings.TempProfileCreated)
                {
                    chromeArguments.Add("--disable-sync");
                    chromeArguments.Add("--disable-signin-promo");
                }

                // avoid chrome overlays on the rendered area
                chromeArguments.Add("--disable-infobars");
                chromeArguments.Add("--disable-session-crashed-bubble");
                chromeArguments.Add("--hide-crash-restore-bubble");
                chromeArguments.Add("--noerrdialogs");
                // Chromium honors only the last --disable-features switch, so build one. Edge shopping content
                // scripts keep changing retail pages, which hangs DOM.getDocument, so those features are disabled
                // below.
                string disableFeatures = "TranslateUI,Translate";

                // Temp profiles only: turn off the 46 MB of on-device models a new profile downloads in its first
                // 30 seconds.
                if (true == browserSettings.TempProfileCreated)
                    disableFeatures += ",OptimizationGuideModelDownloading,OptimizationHintsFetching,OptimizationTargetPrediction,OptimizationHints";

                if (BrowserType.Edge == browserSettings.BrowserType)
                {
                    disableFeatures += ",msShoppingTrigger,msShopping,msEdgeShoppingUI,msEdgeShoppingList";

                    // Edge implicit sign-in, which --disable-sync does not cover
                    if (true == browserSettings.TempProfileCreated)
                        disableFeatures += ",msImplicitSignin,msEdgeImplicitSignin,msIdentityImplicitSignin";
                }

                chromeArguments.Add($"--disable-features={disableFeatures}");
                chromeArguments.Add("--load-media-router-component-extension");
                chromeArguments.Add("--enable-media-router");
                chromeArguments.Add("--force-discovery");
    
                if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                    chromeArguments.Add($@"--user-data-dir=""{browserSettings.ProfileDataDirectory.Replace("\\", "/")}""");

                // Same switch the Selenium options path adds. On the command line so it works with any profile; the
                // extension and Puppeteer cannot set it after launch.
                chromeArguments.Add($"--blink-settings=imagesEnabled={browserSettings.LoadImages.ToString().ToLower()}");

                // Headless mode
                if (headless)
                {
                    chromeArguments.Add("--headless=new");
                    // headless Chromium's screen is 800x600, so maximized and full screen windows are that small; the
                    // primary screen's size gives them the size they have on the desktop
                    chromeArguments.Add($"--screen-info={{{System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width}x{System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height}}}");
                    chromeArguments.Add("--disable-notifications");
                    chromeArguments.Add("--disable-gpu");
                    if (AutomationEngine.PuppeteerPipe == browserSettings.AutomationEngine || AutomationEngine.PuppeteerPipeHW == browserSettings.AutomationEngine)
                        chromeArguments.Add("--no-sandbox"); // REQUIRED FOR PIPE IN HEADLESS + TEMP PROFILE

                    // Fetch user-agent
                    userAgent = GetUserAgent(browserSettings, chromeArguments);

                    userAgent = userAgent?.Trim('"');

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Headless [{browserSettings.BrowserType}] using UserAgent [{userAgent}]", browserSettings, GPALObjectType.Other);

                    chromeArguments.Add($"--user-agent=\"{userAgent}\"");
                }
                else
                {
                    if (false == browserSettings.BlockPopUps)
                        chromeArguments.Add("--disable-popup-blocking");
                }

                // Dark mode
                if (browserSettings.StealthType.HasFlag(StealthType.DarkMode))
                    chromeArguments.Add("--force-dark-mode");

                // Safe browsing stays off; it adds latency to each page load
                //chromeArguments.Add("--safebrowsing.enabled");

            }
            else if (browserSettings.BrowserType == BrowserType.FireFox)
            {
                // Firefox arguments
                var firefoxArguments = new List<string>();

                if (headless)
                {
                    firefoxArguments.Add("-headless");
                    firefoxArguments.Add($"-width={Puppeteer.GetScreenWidth()}");
                    firefoxArguments.Add($"-height={Puppeteer.GetScreenHeight()}");
                    firefoxArguments.Add("--disable-notifications");
                }
                // Headful Firefox gets no extra arguments; OttoMagic drives it through GPALRestAPI and needs no
                // debugging port. No --websocket-port: Firefox ignores the flag and opens the port number as a url.

                // With no profile given, create a temp profile. Firefox takes its preferences from the profile's
                // user.js, which CreateUserJs writes below.
                if (true == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                {
                    browserSettings.ProfileDataDirectory = FirefoxProfileManager.CreateTempProfileDirectory();

                    // Mark the profile temporary, as the Chromium path does, so Browser.Close deletes it.
                    browserSettings.TempProfileCreated = true;

                    if (true == browserSettings.UseOttoMagic)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"OttoMagic needs a profile with it installed. [{browserSettings.BrowserType}] on a temporary profile has no OttoMagic. Give the workflow a profile, or use Selenium.", browser, GPALObjectType.Browser);
                }

                // As with Chromium, one browser per profile. A Firefox already on it takes the launch and this run
                // waits for a browser that never starts.
                if (false == browserSettings.TempProfileCreated)
                    BrowserHelper.RefuseIfProfileIsOpen(browserSettings);

                // Write the preferences to every profile. Snapshot a supplied profile first, for Browser.Close to
                // restore; a temp profile is deleted.
                if (false == browserSettings.TempProfileCreated)
                    browserSettings.PreviousFirefoxPreferences = FirefoxProfileManager.TakeSnapshot(browserSettings.ProfileDataDirectory);

                FirefoxProfileManager.CreateUserJs(browserSettings.ProfileDataDirectory, browserSettings);
                FirefoxProfileManager.NoteWrittenPreferences(browserSettings.PreviousFirefoxPreferences);

                if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                    firefoxArguments.Add($"-profile \"{(browserSettings.ProfileName ?? browserSettings.ProfileUserName).TrimEnd('\\')}\"");
                else if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                    firefoxArguments.Add($"-profile \"{browserSettings.ProfileDataDirectory.TrimEnd('\\')}\"");

                chromeArguments = firefoxArguments; // Reuse chromeArguments for Process.Start
            }

            bool holdingHandshakeGate = false;   // set when this launch owns the OttoMagic port handshake
            UdpClient udpHandshake = null;       // out here so a launch that throws before the handshake still closes it

            // Launch browser
            try
            {
                // the launch url goes on Chrome's command line
                if (null != URL && false == string.IsNullOrEmpty(URL.Url))
                    chromeArguments.Add(URL?.Url);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Running workflow for Automation Engine [{browserSettings.AutomationEngine}]", null, GPALObjectType.None);

                // updates the web driver when asked; this engine does not use it, a Selenium run later does
                BrowserHelper.UpdateDriverIfAsked(browserSettings);

                if (true == browserSettings.UseOttoMagic)
                {
                    // Wait until any launch in progress has its port before opening the UDP socket.
                    // AbandonedMutexException means the holder died and this launch now owns the mutex.
                    try { holdingHandshakeGate = ottoHandshakeGate.WaitOne(OttoHandshakeGateTimeoutMs); }
                    catch (AbandonedMutexException) { holdingHandshakeGate = true; }

                    if (false == holdingHandshakeGate)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Waited [{OttoHandshakeGateTimeoutMs}] ms for another OttoMagic browser to finish its port handshake. Launching anyway, this browser may not get a port of its own.", browserSettings, GPALObjectType.Browser);

                    udpHandshake = new UdpClient(47623);
                    udpHandshake.Client.ReceiveTimeout = 30000;

                    KeepFromChildren(udpHandshake);
                }

                int firefoxLauncherPid = 0;

                if (true == browserSettings.DebugPipe)
                {
                    var sa = new Native.SECURITY_ATTRIBUTES
                    {
                        nLength = Marshal.SizeOf<Native.SECURITY_ATTRIBUTES>(),
                        bInheritHandle = 1,
                        lpSecurityDescriptor = IntPtr.Zero
                    };

                    IntPtr hProcess = IntPtr.Zero;
                    IntPtr reservedPtr = IntPtr.Zero;
                    AnonymousPipeServerStream stdinPipe = null, stdoutPipe = null, stderrPipe = null;

                    try
                    {
                        // Create anonymous pipes. bInheritHandle is set to true via HandleInheritability.Inheritable
                        stdinPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable, 1048576);
                        stdoutPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable, 1048576);
                        stderrPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable, 1048576);

                        // Build command line. On Windows, Chrome's pipe mode takes the handles it reads commands from
                        // and writes replies to through --remote-debugging-io-pipes, read handle first. The browser
                        // inherits them with the same values.
                        var chromeCommandLine = new StringBuilder();
                        chromeCommandLine.Append($@"""{browserPath}""");
                        chromeCommandLine.Append(" ");
                        chromeCommandLine.Append(string.Join(" ", chromeArguments));
                        chromeCommandLine.Append($" --remote-debugging-io-pipes={stdinPipe.GetClientHandleAsString()},{stdoutPipe.GetClientHandleAsString()}");

                        // Create process
                        var startupInfo = new Native.STARTUPINFO
                        {
                            cb = (uint)Marshal.SizeOf<Native.STARTUPINFO>(),
                            // Set the desktop explicitly, since this launch calls CreateProcess directly. null
                            // means the caller's desktop.
                            lpDesktop = true == browserSettings.HiddenDesktop ? HiddenDesktop.StartupNameFor(browserSettings.HiddenDesktopName) : null,
                            dwFlags = Native.STARTF_USESHOWWINDOW | Native.STARTF_USESTDHANDLES, // Use standard handles flag
                            wShowWindow = (ushort)(headless ? Native.SW_HIDE : Native.SW_SHOWNORMAL),
                            hStdInput = IntPtr.Zero,
                            hStdOutput = IntPtr.Zero,
                            hStdError = stderrPipe.ClientSafePipeHandle.DangerousGetHandle(), // Assign to standard error
                        };

                        // Call CreateProcess with bInheritHandles set to true
                        created = Native.CreateProcess(
                            null,
                            chromeCommandLine.ToString(),
                            IntPtr.Zero,
                            IntPtr.Zero,
                            true, // Inherit handles
                            0,
                            IntPtr.Zero,
                            null, // starts in GPAL's working directory; the profile folder goes in --user-data-dir
                            ref startupInfo,
                            out var processInfo2);

                        if (!created)
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "PuppeteerPipe CreateProcess failed.");

                        hProcess = processInfo2.hProcess;

                        // The browser holds its own copies of its pipe ends now. Closing GPAL's lets a read see the
                        // browser close its end.
                        stdinPipe.DisposeLocalCopyOfClientHandle();
                        stdoutPipe.DisposeLocalCopyOfClientHandle();

                        // Assign streams
                        browserSettings.InboundPipe = stdinPipe;
                        browserSettings.OutboundPipe = stdoutPipe;
                        browserSettings.ErrorPipe = stderrPipe;

                        // Capture stderr
                        string stderr = "";
                        var errorReader = new StreamReader(stderrPipe, Encoding.UTF8, true, 4096, false);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                char[] buffer = new char[4096];
                                int read;
                                while ((read = await errorReader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                                {
                                    stderr += new string(buffer, 0, read);
                                }
                                if (Native.GetExitCodeProcess(hProcess, out uint finalExitCode) && finalExitCode != 0)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Chrome exited with code: [{finalExitCode}], Stderr: [{stderr}]", browserSettings, GPALObjectType.Other);
                                }
                            }
                            catch (GPALException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error reading stderr", browserSettings, GPALObjectType.Other, ex);
                            }
                            finally
                            {
                                errorReader.Dispose();
                            }
                        });

                        try
                        {
                            // Wait/verify process
                            if (!Native.GetExitCodeProcess(hProcess, out uint exitCode))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get process exit code.");
                            if (exitCode != Native.STILL_ACTIVE)
                                throw new GPALException($"Chrome exited. Exit code: {exitCode}, Stderr: {stderr}, Check {Path.Combine(browserSettings.ProfileDataDirectory, "chrome_log.txt")}");

                            browserSettings.Process = Process.GetProcessById((int)processInfo2.dwProcessId);
                            browserSettings.Process.EnableRaisingEvents = true;
                        }
                        catch (ArgumentException ex)
                        {
                            if (Native.GetExitCodeProcess(hProcess, out uint exitCode) && exitCode != Native.STILL_ACTIVE)
                                throw new GPALException($"Chrome exited early. Exit code: {exitCode}, Stderr: {stderr}");
                            throw new GPALException($"Failed to get Chrome process by ID: {ex.Message}, Stderr: {stderr}");
                        }
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to launch Chrome with pipes", browserSettings, GPALObjectType.Other, ex);
                        throw;
                    }
                    finally
                    {
                        if (reservedPtr != IntPtr.Zero)
                            Marshal.FreeHGlobal(reservedPtr);
                        if (hProcess != IntPtr.Zero)
                            Native.CloseHandle(hProcess);
                        if (!created)
                        {
                            stdinPipe?.Dispose();
                            stdoutPipe?.Dispose();
                            stderrPipe?.Dispose();
                        }
                    }
                }
                else if (true == browserSettings.HiddenDesktop)
                {
                    // CreateProcess directly, because ProcessStartInfo cannot set lpDesktop, and without it the
                    // browser opens on the parent process's desktop. The cost is the browser's stdout and stderr,
                    // which elsewhere only reach DEEPDEBUG.
                    Native.STARTUPINFO hiddenStartup = new Native.STARTUPINFO
                    {
                        cb = (uint)Marshal.SizeOf<Native.STARTUPINFO>(),
                        lpDesktop = HiddenDesktop.StartupNameFor(browserSettings.HiddenDesktopName)
                    };

                    string hiddenCommandLine = $@"""{browserPath}"" {string.Join(" ", chromeArguments)}";

                    // Firefox's sandboxed GPU process fails to launch on a hidden desktop, and Firefox runs WebGL in
                    // it, so a page gets no WebGL context. MOZ_DISABLE_GPU_SANDBOX lifts that sandbox for this
                    // process only; a pref would be saved into the profile's prefs.js.
                    IntPtr environment = BrowserType.FireFox == browserSettings.BrowserType ? EnvironmentBlockWith("MOZ_DISABLE_GPU_SANDBOX", "1") : IntPtr.Zero;
                    Native.PROCESS_INFORMATION hiddenProcess;
                    int startError = 0;

                    try
                    {
                        if (false == Native.CreateProcess(null, hiddenCommandLine, IntPtr.Zero, IntPtr.Zero, false, IntPtr.Zero == environment ? 0 : Native.CREATE_UNICODE_ENVIRONMENT, environment, null, ref hiddenStartup, out hiddenProcess))
                            startError = Marshal.GetLastWin32Error();
                    }
                    finally
                    {
                        if (IntPtr.Zero != environment)
                            Marshal.FreeHGlobal(environment);
                    }

                    if (0 != startError)
                        throw new Win32Exception(startError, $"Could not start [{browserSettings.BrowserType}] on [{browserSettings.HiddenDesktopName}]");

                    Native.CloseHandle(hiddenProcess.hProcess);
                    Native.CloseHandle(hiddenProcess.hThread);

                    browserSettings.Process = Process.GetProcessById((int)hiddenProcess.dwProcessId);

                    // Capture the launcher PID immediately before it exits (Firefox only).
                    if (BrowserType.FireFox == browserSettings.BrowserType)
                        firefoxLauncherPid = browserSettings.Process?.Id ?? 0;
                }
                else
                {
                    browserSettings.Process = Process.Start(new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = string.Join(" ", chromeArguments),
                        UseShellExecute = false,
                        // Chrome and Edge write "DevTools listening on ws://..." and internal ERROR lines to
                        // stderr. Redirect both streams so they stay out of the workflow's console.
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });

                    // Drain both streams or the browser stalls when the pipe fills. DevTools uses the port, so the
                    // lines only go to DEEPDEBUG.
                    browserSettings.Process.OutputDataReceived += (sender, data) => PublishBrowserOutput(browserSettings, data);
                    browserSettings.Process.ErrorDataReceived += (sender, data) => PublishBrowserOutput(browserSettings, data);
                    browserSettings.Process.BeginOutputReadLine();
                    browserSettings.Process.BeginErrorReadLine();

                    // Capture the launcher PID immediately before it exits (Firefox only).
                    if (BrowserType.FireFox == browserSettings.BrowserType)
                        firefoxLauncherPid = browserSettings.Process?.Id ?? 0;

                    DateTime deadline = DateTime.UtcNow + timeout;

                    // Firefox starts through a launcher that exits once the browser is up, so switch to the child,
                    // which owns the window, before the window wait and resize. ParentProcessId survives the
                    // launcher exiting.
                    if (BrowserType.FireFox == browserSettings.BrowserType)
                        AdoptFirefoxChild(browserSettings, firefoxLauncherPid, deadline);

                    // we have no window to check to ensure the browser launched in headless
                    // so check the process flag
                    // for windowed, wait for the window to open
                    if (true == headless)
                    {
                        if (true == browserSettings.Process.HasExited)
                        {
                            string msg = $"[{browserSettings.BrowserType}] did not start. Exiting.";
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg);
                            throw new TimeoutException(msg);
                        }
                    }
                    else
                    {
                        while (browserSettings.Process.MainWindowHandle == IntPtr.Zero && !browserSettings.Process.HasExited)
                        {
                            if (DateTime.UtcNow > deadline)
                            {
                                string msg = $"[{browserSettings.BrowserType}] did not start in [{secondsToWait}] seconds. Exiting.";
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg);
                                throw new TimeoutException(msg);
                            }

                            Thread.Sleep(100); // prevents busy-wait CPU spike
                        }

                        // Move and size the window now that it exists. Works for every browser; --window-size and
                        // --window-position are Chromium only, and Firefox has no command line position. The
                        // Chromium arguments still open the window at the right size, so it does not jump.
                        if (false == browserSettings.WindowSize.IsEmpty &&
                            false == browserSettings.FullScreen &&
                            false == browserSettings.Maximize &&
                            false == browserSettings.Minimize)
                        {
                            browserSettings.Process.Refresh();
                            WindowHelper.ResizeWindow(browserSettings.Process, browserSettings.WindowSize);
                        }
                    }
                }

                // On a hidden desktop Chrome and Edge open at their --window-size, but Firefox has no such
                // argument and Process.MainWindowHandle sees only this process's desktop, so its window is found
                // on the hidden desktop, then moved and sized as it is on the visible one
                if (true == browserSettings.HiddenDesktop &&
                    false == headless &&
                    BrowserType.FireFox == browserSettings.BrowserType &&
                    false == browserSettings.WindowSize.IsEmpty &&
                    false == browserSettings.FullScreen &&
                    false == browserSettings.Maximize &&
                    false == browserSettings.Minimize)
                {
                    DateTime windowDeadline = DateTime.UtcNow + timeout;

                    AdoptFirefoxChild(browserSettings, firefoxLauncherPid, windowDeadline);

                    IntPtr firefoxWindow = HiddenDesktop.MainWindowOf(browserSettings.HiddenDesktopName, browserSettings.Process.Id);

                    while (IntPtr.Zero == firefoxWindow && DateTime.UtcNow < windowDeadline && false == browserSettings.Process.HasExited)
                    {
                        Thread.Sleep(100);
                        firefoxWindow = HiddenDesktop.MainWindowOf(browserSettings.HiddenDesktopName, browserSettings.Process.Id);
                    }

                    if (IntPtr.Zero != firefoxWindow)
                        WindowHelper.ResizeWindow(firefoxWindow, browserSettings.WindowSize);
                }

                if (true == browserSettings.UseOttoMagic)
                {
                    try
                    {
                        var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                        int restPort = 0;
                        int firstHeard = 0;
                        int launchedPid = browserSettings.Process?.Id ?? 0;

                        // Every browser with OttoMagic announces a port, so a message may be another browser's. It
                        // is ours when the announcing GPALRestAPI process descends from the browser we launched;
                        // others are skipped.
                        while (0 == restPort)
                        {
                            byte[] data;

                            // Receive timed out. If an untraceable announcement was heard, use the first and warn
                            // that it is a guess.
                            try { data = udpHandshake.Receive(ref remoteEp); }
                            catch (SocketException) when (0 != firstHeard)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No GPALRestAPI traced back to this browser, so port [{firstHeard}] is being used on the strength of it being the first one announced", browserSettings, GPALObjectType.Browser);

                                restPort = firstHeard;
                                break;
                            }

                            string[] said = Encoding.UTF8.GetString(data).Trim().Split(' ');

                            // GPALRestAPI sends "port processId". If it does not parse, throw here rather than fail
                            // later without a url.
                            if (2 > said.Length || false == int.TryParse(said[0], out int announced) || false == int.TryParse(said[1], out int hostPid))
                                throw new InvalidOperationException($"GPALRestAPI announced [{string.Join(" ", said)}], which is not a port and a process id");

                            if (0 == launchedPid || true == BrowserHelper.CameFromProcess(hostPid, launchedPid))
                                restPort = announced;
                            else
                            {
                                if (0 == firstHeard)
                                    firstHeard = announced;

                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Port [{announced}] was announced by another browser's GPALRestAPI [{hostPid}], so this launch is still waiting for its own", browserSettings, GPALObjectType.Browser);
                            }
                        }

                        browserSettings.RestApiBaseUrl = $"http://localhost:{restPort}/";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GPALRestAPI ready on port [{restPort}].", browserSettings, GPALObjectType.Browser);
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"GPALRestAPI never announced a port, so this browser cannot reach it", browserSettings, GPALObjectType.Browser, ex);
                        throw;
                    }
                    finally
                    {
                        udpHandshake?.Close();

                        // Port read and socket closed; release the mutex for the next launch.
                        if (true == holdingHandshakeGate)
                        {
                            ottoHandshakeGate.ReleaseMutex();
                            holdingHandshakeGate = false;
                        }
                    }

                    // Firefox launches via a wrapper that exits immediately; the real process is a child.
                    // Windows preserves ParentProcessId even after the parent exits, so we can query
                    // for the direct firefox.exe child of the launcher PID we captured at start.
                    if (firefoxLauncherPid > 0 &&
                        (browserSettings.Process == null || browserSettings.Process.HasExited))
                    {
                        try
                        {
                            using (var searcher = new ManagementObjectSearcher(
                                $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {firefoxLauncherPid} AND Name = 'firefox.exe'"))
                            {
                                foreach (ManagementObject obj in searcher.Get())
                                {
                                    int childPid = Convert.ToInt32(obj["ProcessId"]);
                                    try { browserSettings.Process = System.Diagnostics.Process.GetProcessById(childPid); break; }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }
                else if (true == browserSettings.UsePuppeteer)
                {
                    // If Puppeteer cannot connect, nothing else will close the browser. Kill it and delete its temp
                    // profile, then rethrow.
                    try
                    {
                        _ = browserSettings.PuppeteerCommunicator;    // initialize before we start using
                    }
                    catch (GPALException)
                    {
                        BrowserHelper.KillProcessTree(browserSettings.Process?.Id ?? 0);

                        if (true == browserSettings.TempProfileCreated)
                            ChromeProfileManager.RemoveTempUserProfile(browserSettings.ProfileDataDirectory);

                        throw;
                    }
                }

                // Handle maximize/minimize
                if (browserSettings.Maximize)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            // Use maximize window via extension - the browser we just launched, not whoever's helper was called
                            browser.MagicHelper.Client.Maximize().Execute();
                        else if (true == browserSettings.UsePuppeteer)
                            browserSettings.PuppeteerCommunicator.Maximize().GetAwaiter().GetResult();
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to maximize browser window", browserSettings, GPALObjectType.Other, ex);
                    }
                }
                else if (browserSettings.Minimize)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            browser.MagicHelper.Client.Minimize().Execute();
                        else if (true == browserSettings.UsePuppeteer)
                            browserSettings.PuppeteerCommunicator.Minimize().GetAwaiter().GetResult();
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to minimize browser window", browserSettings, GPALObjectType.Other, ex);
                    }
                }
                else if (browserSettings.FullScreen)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            browser.MagicHelper.Client.FullScreen().Execute();
                        else if (true == browserSettings.UsePuppeteer)
                            browserSettings.PuppeteerCommunicator.FullScreen().GetAwaiter().GetResult();
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to full screen browser window", browserSettings, GPALObjectType.Other, ex);
                    }
                }

                // Executable, process id and port together, to tell a started browser from an attached one.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Browser [{browserSettings.BrowserType}] is process [{browserSettings.Process?.Id}] from [{browserPath}] on port [{browserSettings.DebugPort}] at [{browserSettings.PuppeteerUrl}]", browserSettings, GPALObjectType.Other);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Browser [{browserSettings.BrowserType}] launched{(true == browserSettings.UseHeadless ? " headless" : "")}.", browserSettings, GPALObjectType.Other);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Publish the exception and rethrow. Do not end the application: one failed browser would stop a
                // scheduler's other workflows or close a form. Uncaught, the unhandled exception handler cleans up
                // and exits.
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to launch browser [{browserSettings.BrowserType}] for URL [{URL}]", browserSettings, GPALObjectType.Other, ex);
                throw;
            }
            finally
            {
                // Close the UDP socket and release the mutex even when the launch failed before reading a port, or
                // the next OttoMagic launch fails on port 47623.
                udpHandshake?.Close();

                if (true == holdingHandshakeGate)
                    ottoHandshakeGate.ReleaseMutex();
            }

             return browserSettings.Process;
        }

        /// <summary>
        /// Use the Star Wars API to generate random search terms for our google cookies we will create
        /// </summary>
        /// <returns></returns>
        public static string GetRandomStarWarsQuery()
        {
            IRESTClient restClient = GPAL.RESTClient.WithAPIBase("https://swapi.info/api/").ToGPALObject();

            var endpoints = new[]
            {
                ("people", 83, "name"),    // e.g., "Yoda"
                ("planets", 60, "name"),   // e.g., "Alderaan"
                ("films", 6, "title"),     // e.g., "A New Hope"
                ("species", 37, "name"),   // e.g., "Wookiee"
                ("vehicles", 39, "name"),  // e.g., "X-wing"
                ("starships", 36, "name")  // e.g., "Millennium Falcon"
            };

            string FetchQuery(string endpoint, int maxId, string field, int retries = 3)
            {
                for (int attempt = 0; attempt < retries; attempt++)
                {
                    int id = new Random().Next(1, maxId + 1);
                    var response = restClient.WithEndpoint($"{endpoint}/{id}").Execute<SwapiResponse>(false);
                    if (response != null)
                    {
                        return field == "name" ? response.name : response.title;
                    }
                    Thread.Sleep(100 * (int)Math.Pow(2, attempt)); // Backoff: 100ms, 200ms, 400ms
                }
                return null; // Let caller handle failure
            }

            // Select random primary endpoint
            var (endpoint, maxId, field) = endpoints[new Random().Next(endpoints.Length)];
            string query = FetchQuery(endpoint, maxId, field);

            // Fallback to another endpoint if null
            if (query == null)
            {
                var fallbackEndpoint = endpoints[new Random().Next(endpoints.Length)];
                query = FetchQuery(fallbackEndpoint.Item1, fallbackEndpoint.Item2, fallbackEndpoint.Item3);
                if (query == null) // Last resort, pick another
                {
                    fallbackEndpoint = endpoints[new Random().Next(endpoints.Length)];
                    query = FetchQuery(fallbackEndpoint.Item1, fallbackEndpoint.Item2, fallbackEndpoint.Item3) ?? "star wars"; // Absolute last fallback
                }
            }

            if (new Random().NextDouble() > 0.4) // 60% chance to add second term
            {
                var (otherEndpoint, otherMaxId, otherField) = endpoints[new Random().Next(endpoints.Length)];
                string otherQuery = FetchQuery(otherEndpoint, otherMaxId, otherField);
                if (otherQuery != null)
                {
                    query += $" {otherQuery}"; // e.g., "Yoda Millennium Falcon"
                }
                else
                {
                    var fallbackEndpoint = endpoints[new Random().Next(endpoints.Length)];
                    otherQuery = FetchQuery(fallbackEndpoint.Item1, fallbackEndpoint.Item2, fallbackEndpoint.Item3);
                    query += otherQuery != null ? $" {otherQuery}" : ""; // Skip if still null
                }
            }

            return query;
        }

        private class SwapiResponse
        {
            public string name { get; set; }
            public string title { get; set; }
        }
        /// <summary>
        /// Builds the Chrome, Edge or Firefox user agent from the installed version in the registry.<br/>
        /// Static because it reads only the registry and GPALSettings templates and must work with no browser;
        /// GPAL.MagicHelper returns MagicHelpers[0], which needs one.
        /// </summary>
        /// <param name="browserType"></param>
        /// <returns></returns>
        public static string GetUserAgentString(BrowserType browserType)
        {
            // construct headless user-agent
            string chromeVersion;
            string edgeVersion;
            string firefoxVersion;
            string retval = null;

            if (BrowserType.Chrome == browserType)
            {
                DriverHelper.GetChromeVersion(out chromeVersion);
                // a dotless or empty version means detection failed; slicing to IndexOf(-1) would throw
                if (null != chromeVersion && 0 < chromeVersion.IndexOf('.'))
                    chromeVersion = chromeVersion.AsSpan().Slice(0, chromeVersion.IndexOf('.')).ToString(); // major version
                retval = String.Format(GPAL.GPALSettings.ChromeUserAgentTemplate, chromeVersion);
            }
            else if (BrowserType.Edge == browserType)
            {
                DriverHelper.GetEdgeVersion(out edgeVersion);
                // a dotless or empty version means detection failed; slicing to IndexOf(-1) would throw
                if (null != edgeVersion && 0 < edgeVersion.IndexOf('.'))
                    edgeVersion = edgeVersion.AsSpan().Slice(0, edgeVersion.IndexOf('.')).ToString(); // major version
                retval = String.Format(GPAL.GPALSettings.EdgeUserAgentTemplate, edgeVersion);
            }
            else if (BrowserType.FireFox == browserType)
            {
                DriverHelper.GetFirefoxVersion(out firefoxVersion);
                // a dotless or empty version means detection failed; slicing to IndexOf(-1) would throw
                if (null != firefoxVersion && 0 < firefoxVersion.IndexOf('.'))
                    firefoxVersion = firefoxVersion.AsSpan().Slice(0, firefoxVersion.IndexOf('.')).ToString(); // major version
                retval = String.Format(GPAL.GPALSettings.FirefoxUserAgentTemplate, firefoxVersion);
            }

            return retval;
        }

        /// <summary>
        /// Clears the inherit flag on the handshake socket. Windows sockets are inheritable by default, and a
        /// browser started while it is open would keep port 47623 bound for its lifetime, failing the next launch
        /// with an error that does not say why.
        /// </summary>
        /// <param name="client">The handshake socket to keep out of the children.</param>
        private static void KeepFromChildren(UdpClient client)
        {
            if (false == Native.SetHandleInformation(client.Client.Handle, Native.HANDLE_FLAG_INHERIT, 0))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Could not take the inherit flag off the handshake socket, so the next OttoMagic launch may find [47623] still held",
                    null, GPALObjectType.Browser, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        internal string GetUserAgent(BrowserSettings browserSettings, List<string> chromeArguments)
        {
            string userAgent = null;
            string browserPath = null;

            // Starting a browser to read its user agent is slow, so only with UserAgentFromBrowser. Otherwise
            // BrowserHelper.ResolveUserAgent, the string GPAL's own requests send.
            if (false == browserSettings.UserAgentFromBrowser)
                return BrowserHelper.ResolveUserAgent(browserSettings);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Getting user agent for headless use.", chromeArguments, GPALObjectType.Other);

            // Get browser executable path
            switch (browserSettings.BrowserType)
            {
                case BrowserType.Chrome:
                    browserPath = Puppeteer.GetChromePath();
                    break;
                case BrowserType.Edge:
                    browserPath = Puppeteer.GetEdgePath();
                    break;
                case BrowserType.FireFox:
                    browserPath = Puppeteer.GetFirefoxPath();
                    break;
                default:
                    Exception ex = new Exception("Unsupported Browser Type");
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported browser type [{browserSettings.BrowserType}]", browser, GPALObjectType.Browser, ex);
                    return GetUserAgentString(browserSettings.BrowserType);
            }

            UdpClient probeHandshake = null;
            bool holdingProbeGate = false;

            try
            {
                List<string> ourArgs = new List<string>(chromeArguments);

                // same reason as the launch above: every browser gets its own port or they share a session
                if (null == browserSettings.DebugPort)
                    browserSettings.DebugPort = BrowserHelper.FindFreePort();

                if (BrowserType.FireFox != browserSettings.BrowserType)
                    ourArgs.Add($"--remote-debugging-port={browserSettings.DebugPort.Value}");
                else
                {
                    // Firefox is queried through GPALRestAPI, so open the handshake socket before starting it to
                    // receive its port. Otherwise the request goes to whichever browser is already running.
                    try { holdingProbeGate = ottoHandshakeGate.WaitOne(OttoHandshakeGateTimeoutMs); }
                    catch (AbandonedMutexException) { holdingProbeGate = true; }

                    probeHandshake = new UdpClient(47623);
                    probeHandshake.Client.ReceiveTimeout = 30000;

                    KeepFromChildren(probeHandshake);
                }

                browserSettings.Process = Process.Start(new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments = string.Join(" ", ourArgs),
                    UseShellExecute = false
                });

                //                Thread.Sleep(3000);

                // Firefox reads the user agent from the probe port; Chrome and Edge read it through Puppeteer
                if (BrowserType.FireFox == browserSettings.BrowserType)
                {
                    var probeEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    string probePortText = Encoding.UTF8.GetString(probeHandshake.Receive(ref probeEndPoint)).Trim();

                    // ask the browser we just launched, on the port it just announced
                    userAgent = true == int.TryParse(probePortText, out int probePort)
                        ? ((IRESTClient)GPAL.RESTClient.WithAPIBase($"http://localhost:{probePort}/").ToGPALObject()).GetUserAgent().Execute()
                        : Client.GetUserAgent().Execute();
                }
                else
                {
                    AutomationEngine aeSave = browserSettings.AutomationEngine;
                    browserSettings.AutomationEngine = AutomationEngine.PuppeteerPort;
                    _ = browserSettings.PuppeteerCommunicator;
                    userAgent = browserSettings.PuppeteerClient.GetUserAgent().Execute();
                    browserSettings.AutomationEngine = aeSave;
                }
                userAgent = userAgent.Replace("Headless", "");

                //if (true == browserSettings.UsePuppeteer)
                {
                    var parameters = new Dictionary<string, object>();
                    // gracefully close the browser, release resources, shutdown chrome
                    //browserSettings.PuppeteerCommunicator.SendCommand(DevToolsMethods.BrowserClose, parameters, null).GetAwaiter().GetResult();
                    browserSettings.PuppeteerCommunicator._readerReadyTcs?.SetCanceled();
                    if (false == browserSettings.PuppeteerCommunicator._usePipes)
                        browserSettings.PuppeteerCommunicator.CloseOutputWebSocketAsync().GetAwaiter();
                }

                // Left running, the user agent browser holds the profile lock and the real launch fails with "Failed to
                // create a ProcessSingleton for your profile directory"

                // kill the opened chrome and any children used to get the agent string
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {browserSettings.Process.Id} /F /T",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi).WaitForExit();
                }
                catch { }

                Thread.Sleep(1_000);

                if (null != browserSettings.Process && false == browserSettings.Process.HasExited)
                {
                    try
                    {
                        browserSettings.Process.Kill();
                        browserSettings.Process.Close();
                    }
                    catch { }
                }

                Thread.Sleep(1_000);

                // GPALRestAPI is not killed here: it shuts itself down when the extension's port closes, and killing
                // it by name would take down every other browser's rest api too
                browserSettings.PuppeteerClient = null;
                browserSettings.PuppeteerCommunicator = null;
                browserSettings.Process = null;

            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                userAgent = GetUserAgentString(browserSettings.BrowserType);
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to fetch user-agent using [{userAgent}].", browserSettings, GPALObjectType.Other, ex);
            }
            finally
            {
                probeHandshake?.Close();

                // The user agent browser is closed; close the socket and release the mutex for the real launch.
                if (true == holdingProbeGate)
                    ottoHandshakeGate.ReleaseMutex();
            }

            return false == string.IsNullOrEmpty(userAgent) ? userAgent : GetUserAgentString(browserSettings.BrowserType);
        }
    }
}
