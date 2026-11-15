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
    public partial class Browser
    {
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
        ///         .RightClickAndDownload(fileList);   // the filename used on save is added to the end of fileList
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
        ///         .LeftClickAndDownload(fileList);   // the filename used on save is added to the end of fileList
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
        ///     browser1.Close();   // no need to close 2, it's the same
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
        /// input reaches it; on another machine it does neither. <see cref="Close()"/> closes the browser, and
        /// <see cref="Release"/> leaves it running.
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

        public IAllowBrowserSettingsOrGoTo WithCDPDebugPort(int port = 0xdead) // callers get the interface's default; this one matches it
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

            // With no existing browser port set, the debug port is used for it too
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
        /// If both a profile user name (or profile name) and a profile data directory are set, the user name is used and the data directory is ignored.
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
        /// If both a profile user name (or profile name) and a profile data directory are set, the user name is used and the data directory is ignored.
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
        /// Specify the profile name to use for this browser automation session.
        /// Chrome and Edge look the name up in the browser's default User Data folder, by directory name or display name, and
        /// any profile data directory set is replaced by that folder. Firefox passes the name to -profile.
        /// Use this when the default User Data folder holds several profiles and the default profile is not the one wanted.
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
        public IAllowWithPagesAndGridActions WithInfiniteScroll
        {
            get
            {
                CurrentUOW.InfiniteScroll = true;
                return this;
            }
        }
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
    }
}
