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

using DocumentFormat.OpenXml.Office.CustomUI;
using GenerallyPositive.Application;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using static GenerallyPositive.Browser.Browser;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPALForm.GPALForm;

namespace GenerallyPositive
{
    public partial class GPAL
    {
        private static bool SettingsLoaded 
        { 
            get
            {
                return gpalSettings.SettingsLoaded;
            }

            set
            {
                gpalSettings.SettingsLoaded = value;
            }
        }
        // deep copy cannot copy to properties
        private static GPALSettings gpalSettings = new GPALSettings(true);    // this will be initialized if we can't load from GPAL.yaml
        private static string _settingsFilePath = "GPAL.yaml";
        public static GPALSettings GPALSettings
        {
            [GPALSettings(Description = "The global settings GPAL is running with")] get
            {
                return gpalSettings;
            }
            set
            {
                gpalSettings = value;
            }
        }
        public static bool UseHardware
        {
            [GPALSettings(Description = "Whether GPAL uses the real mouse and keyboard by default")] get => gpalSettings.UseHardware;
        }
        public static bool UseJavaSCript
        {
            [GPALSettings(Description = "Whether GPAL interacts through injected JavaScript by default")] get => gpalSettings.UseJavaScript;
        }
        public static string Version
        {
            [Data(Description = "The version of GPAL that is running")] get => GPALSettings.Version;
        }
        /// <summary>
        /// Where an OAuth redirect lands and the token is read back, e.g. http://localhost:3117/. Set by
        /// ResolveTokenUrl from the registered redirect URI.
        /// </summary>
        public static string OAuthTokenUrl
        {
            [Data(Description = "Where an OAuth redirect lands and the token is read back from, e.g. http://localhost:3117/")] get
            {
                return gpalSettings.OAuthTokenUrl;
            }
            set
            {
                gpalSettings.OAuthTokenUrl = value;
            }
        }
        internal static string ErrorPlaceholder
        {
            get
            {
                return GPALSettings.ErrorPlaceholder;
            }
        }
        internal static string DriverLocation
        {
            get
            {
                return GPALSettings.DriverLocation;
            }
        }
        internal static bool SimulateMouse
        {
            get
            {
                return GPALSettings.SimulateMouse;
            }
        }
        internal static string TempProfileDirectory
        {
            get
            {
                return GPALSettings.TempProfileDirectory;
            }
        }
        /// <summary>
        /// A copy of the open browsers list, since concurrent workflows change it. <see cref="Forget"/> removes a
        /// browser from the list.
        /// </summary>
        internal static List<Browser.Browser> Browsers
        {
            get
            {
                lock (BrowserList)
                    return BrowserList.ToList();
            }
        }
        /// <summary>
        /// Takes a closed browser off the list of open ones.
        /// </summary>
        /// <param name="browser">The browser that closed</param>
        internal static void Forget(Browser.Browser browser)
        {
            lock (BrowserList)
                BrowserList.Remove(browser);
        }
        internal static int MatchingPercentage
        {
            get
            {
                return GPALSettings.MatchingPercentage;
            }
        }
        internal static int TypingDelay
        {
            get
            {
                return GPALSettings.TypingDelay;
            }
        }
        internal static bool StopOnNotFound
        {
            get
            {
                return GPALSettings.StopOnNotFound;
            }
        }
        internal static bool NoFallbackRecoveryActions
        {
            get
            {
                return gpalSettings.NoFallbackRecoveryActions;
            }
        }
        [Helper(Description = "Load GPAL's settings from GPAL.yaml or the given file, creating it with the defaults if it is missing")]
        public static void LoadSettings(GPALFile settingsFile = null)
        {
            bool hold = GPAL.PublishToConsole;
            GPALEventType events = GPAL.GPALSettings.ConsoleEvents;

            try
            {
                if (settingsFile != null)
                {
                    _settingsFilePath = settingsFile.Filenames[0];
                    SettingsLoaded = false;
                }

                if (false == SettingsLoaded)
                {
                    SettingsLoaded = true;
                    string currentVersion = GenerallyPositive.GPALSettings.BuildVersion;
                    GPALFile resolvedFile = _settingsFilePath;

                    if (true == System.IO.File.Exists(resolvedFile.Filenames[0]))
                    {
                        // Clear Corrected so it only lists this load's corrections.
                        gpalSettings.Corrected.Clear();

                        // load setting which contains default events to publish
                        GPAL.Converter.WithInput(resolvedFile).SaveTo(ref gpalSettings);

                        hold = GPAL.PublishToConsole;
                        events = GPAL.GPALSettings.ConsoleEvents;

                        // CAVEAT: a browser native app reads stdio as its message channel, so any output there disconnects it; GPAL_IS_SILENT=1 turns console output off
                        if ("1" == Environment.GetEnvironmentVariable("GPAL_IS_SILENT"))
                            GPAL.PublishToConsole = false;

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{resolvedFile.Filenames[0]}] loaded, using settings.", gpalSettings, GPALObjectType.Other);

                        // A value GPAL could not use was replaced with its default. Save it so it is not corrected on
                        // every start.
                        if (0 < gpalSettings.Corrected.Count)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{resolvedFile.Filenames[0]}] held values GPAL cannot use, set to their defaults and saved: {string.Join(", ", gpalSettings.Corrected)}", gpalSettings, GPALObjectType.Other);

                        if (false == gpalSettings.Version.Equals(currentVersion))
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{resolvedFile.Filenames[0]}] updated to current GPAL version [{currentVersion}].", gpalSettings, GPALObjectType.Other);

                        if (0 < gpalSettings.Corrected.Count || false == gpalSettings.Version.Equals(currentVersion))
                        {
                            gpalSettings.Version = currentVersion;
                            gpalSettings.Corrected.Clear();
                            SaveSettings();
                        }
                    }
                    else    // if file doesn't exist, initialize default settings and save a settings file, the user can then tweak those settings for next run/load
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{resolvedFile.Filenames[0]}] does not exist. Using default settings and creating GPAL.yaml.", gpalSettings, GPALObjectType.Other);
                        SaveSettings();
                    }
                }

                AIProvidersConfig.TryAutoLoad();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{_settingsFilePath}] load error. Try deleting it and restarting.", gpalSettings, GPALObjectType.Other, ex);
            }
            finally
            {
                GPAL.PublishToConsole = hold;
                GPAL.GPALSettings.ConsoleEvents = events;
            }
        }
        [Helper(Description = "Save GPAL's current settings to GPAL.yaml or the given file")]
        public static void SaveSettings(GPALFile settingsFile = null)
        {
            if (settingsFile != null) _settingsFilePath = settingsFile.Filenames[0];
            GPAL.Converter.WithInput(GPALSettings).SaveTo((GPALFile)_settingsFilePath);
        }
        // GPAL is static and cannot implement IAllowGPALSettings, so its settings methods return the GPALSettings
        // object, which does. A chain starts at GPAL. and continues on GPALSettings.

        /// <summary>
        /// Global Browser CallIfFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfFound">The handler to call</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings AppCallIfFound(Application.Application.CallIfDelegate callIfFound)
        {
            gpalSettings.AppCallIfFoundList.Add(callIfFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global Browser CallIfNotFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfNotFound"></param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfNotFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings AppCallIfNotFound(Application.Application.CallIfDelegate callIfNotFound)
        {
            gpalSettings.AppCallIfNotFoundList.Add(callIfNotFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global Browser CallIfFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfFound"></param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings CallIfFound(Browser.Browser.CallIfDelegate callIfFound)
        {
            gpalSettings.CallIfFoundList.Add(callIfFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global Browser CallIfNotFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfNotFound"></param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfNotFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings CallIfNotFound(Browser.Browser.CallIfDelegate callIfNotFound)
        {
            gpalSettings.CallIfNotFoundList.Add(callIfNotFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets a handler for actionable failures, for any browser without its own.<br/>
        /// Without a handler GPAL reports and continues. The handler receives the browser, to tell workflows apart.
        /// </summary>
        /// <param name="callOnFail">Callback delegate</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Callback for failures a workflow can decide about, used by any browser without its own CallOnFail")]
        public static IAllowGPALSettings CallOnFail(Browser.Browser.CallOnFailDelegate callOnFail)
        {
            GPALSettings.CallOnFailHandlers.Add(callOnFail);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// When GPAL is unable to retrieve data from an element, this message will be placed in the grid.<br/>
        /// GPAL will always strive to give you as much data as possible until you get your selectors perfected.
        /// </summary>
        /// <param name="message">The message to substitute for no data.<br/>"##UNABLE TO GET##" [default]</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Substitue string when data cannot be retrieved for an element [##UNABLE TO GET## (default)]")]
        public static IAllowGPALSettings WithDefaultErrorPlaceholder(string message = "##UNABLE TO GET##")
        {
            GPALSettings.ErrorPlaceholder = message;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Set the default timeout time in seconds to wait for a download to start
        /// The default is 60 seconds
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Set the default timeout for waiting for a download to start.")]
        public static IAllowGPALSettings WithDownloadTimeoutInSec([Seconds] int seconds)
        {
            GPALSettings.DownloadTimeoutInSec = seconds;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// GPAL has fallback alternatives for actions should they fail, generally applies to selenium.
        /// The fallback involves javascript alternative to complete the action.
        /// Set this to true to NOT use try any fallback recovery, just fail
        /// this is set to false by default, if used with no parameter the default is true
        /// </summary>
        /// <param name="trueFalse">Use fallback recovery? [true]</param>
        /// <returns></returns>
        [GPALSettings("DO NOT use GPAL fallback actions on failure, just fail. Usually involves javascript recovery attempt. [false (default)")]
        public static IAllowGPALSettings WithNoFallbackActions(bool trueFalse = true)
        {
            GPALSettings.NoFallbackRecoveryActions = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global value for selector matches so it's not set UOW at a time
        /// </summary>
        /// <param name="matchCount">How many matching elements a selector acts on [int.MaxValue]</param>
        /// <returns></returns>
        [GPALSettings(Description = "Global value for selector matches so it's not set UOW at a time")]
        public static IAllowGPALSettings WithAllThatMatch(int matchCount = int.MaxValue)
        {
            GPALSettings.WithAllThatMatch = matchCount;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Automatically download and unzip the web driver that matches the version of the browser being run.
        /// This uses setting which will be in the GPAL.yaml configuration file which is loaded on startup (or loaded on-demand with a path).
        /// this is set to false by default, if used with no parameter the default is true.<br/>
        /// The driver is checked when the first browser launches, whatever its automation engine. Puppeteer and
        /// OttoMagic start no driver, but it is kept current for when a Selenium engine runs.
        /// </summary>
        /// <param name="trueFalse">Auto Update? [true]</param>
        /// <returns></returns>
        [GPALSettings(Description = "Automatically download and unzip the web driver that matches the version of the browser being run")]
        public static IAllowGPALSettings WithAutoUpdateWebDriver(bool trueFalse = true)
        {
            GPALSettings.AutoUpdateWebDriver = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Set the automation type to use for all browsers.
        /// A selector can override it (to choose javascript or hardware)<br/>
        /// The default for every browser made after this call; a browser's own WithAutomationEngine replaces it for that
        /// browser. PuppeteerPort until set.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithAutomationEngine(AutomationEngine.Selenium);
        ///
        ///     // Selenium, from the global setting; .WithAutomationEngine on the browser would replace it
        ///     IBrowser browser = GPAL.Browser.WithBrowserType(BrowserType.Chrome).ToGPALObject();
        /// </code>
        /// </example>
        /// <param name="automationEngine">The engine browsers start on</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "The automation engine for every browser that does not name its own [PuppeteerPort (default)]")]
        public static IAllowGPALSettings WithAutomationEngine(AutomationEngine automationEngine)
        {
            GPALSettings.AutomationEngine = automationEngine;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// The location of the browser drivers to be used for each GPAL.Browser instance.<br/>
        /// This can be overridden at the GPAL.Browser level, as well.<br/>
        /// Additionally, if not defined, the default location is the same directory as the GPAL executable.
        /// </summary>
        /// <param name="driverPath">Path to the drivers folder</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global location for browser drivers. [./ (default)(same dir as exe)]")]
        public static IAllowGPALSettings WithDriverLocation(string driverPath)
        {
            GPALSettings.DriverLocation = FileHelper.EnsureDirectoryEndsWithBackslash(driverPath);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets the folder for GPAL's temporary files, such as a GPALFile downloaded from a url.<br/>
        /// Default: a GPALfilesSafeToDelete folder beside the executable, created on first use; safe to delete at any
        /// time.
        /// </summary>
        /// <param name="tempPath">Folder for GPAL's temporary files</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Where GPAL writes files it created itself and nobody asked to keep. [GPALfilesSafeToDelete beside the exe (default)]")]
        public static IAllowGPALSettings WithTempDirectory([Directory] string tempPath)
        {
            GPALSettings.TempDirectory = FileHelper.EnsureDirectoryEndsWithBackslash(tempPath);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// The folder GPAL writes its own throwaway files to, created if it is not there yet. Either what
        /// .WithTempDirectory was given, or GPALfilesSafeToDelete beside the executable.
        /// </summary>
        /// <returns>Full path, ending in a backslash</returns>
        internal static string TempDirectory()
        {
            string tempPath = true == string.IsNullOrWhiteSpace(GPALSettings.TempDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GPALfilesSafeToDelete") + @"\"
                : GPALSettings.TempDirectory;

            if (false == Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            return tempPath;
        }
        /// <summary>
        /// Sets the ceiling for the inter-character typing delay used by SendString across every automation
        /// engine (Hardware, Selenium, Puppeteer/CDP, OttoMagic).<br/>
        /// Default is 0 (no pacing - the whole string is sent atomically).<br/>
        /// When greater than 0, SendString dispatches the text character-by-character, sleeping a random
        /// duration between Math.Min(25, delayInTicks) and delayInTicks milliseconds before each character -
        /// not a fixed pause. A constant, identical gap between every keystroke is itself a detectable
        /// signature, so this value is a ceiling on a randomized range rather than the pace itself.
        /// </summary>
        /// <remarks>
        /// Only SendString honors this. FillIn* sets the value directly; its delayMs paces only its per-character
        /// fallback.
        /// </remarks>
        /// <param name="delayInTicks">Ceiling for the randomized inter-character delay, in milliseconds</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithSimulateMouse(true);
        /// </code>
        /// </example>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Intercharacter typing delay ceiling [0ms (default), no pacing]")]
        public static IAllowGPALSettings WithTypingDelay([Milliseconds] int delayInTicks)
        {
            GPALSettings.WithTypingDelay(delayInTicks);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets the image matching percentage for image Selectors.<br/>
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithSimulateMouse(true);
        /// </code>
        /// </example>
        /// <param name="matchPercent">Image matching percentage [default is 80 (80%)]</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        [GPALSettings(Description = "Image matching similarity percentage. [80 (default)]")]
        public static IAllowGPALSettings WithImageMatchingPercentage([Percent] int matchPercent)
        {
            GPALSettings.WithImageMatchingPercentage(matchPercent);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets how many nodes a YAML payload can have before GPAL stops writing aliases for shared references.<br/>
        /// Without aliases a shared object is written in full each place and reads back as separate objects.
        /// </summary>
        /// <remarks>
        /// Past the limit a shared reference is written out in full each time and reads back as separate objects. A
        /// cyclic payload keeps its aliases at any size.
        /// </remarks>
        /// <example>
        /// <code>
        /// GPAL.WithYamlAliasLimit(20_000);   // a bigger graph whose shared references matter
        /// GPAL.WithYamlAliasLimit(0);        // never stop writing aliases
        /// </code>
        /// </example>
        /// <param name="nodeLimit">Nodes to allow before aliases are dropped [default is 5000]; 0 never drops them</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        [GPALSettings(Description = "Nodes a YAML payload may hold before aliases are dropped for shared references. A cyclic payload always keeps them. [5000 (default), 0 never drops]")]
        public static IAllowGPALSettings WithYamlAliasLimit(int nodeLimit)
        {
            GPALSettings.WithYamlAliasLimit(nodeLimit);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global setting to simulate a human moving the mouse to the next point for all commands.<br/>
        /// Default is false
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithSimulateMouse(true)
        /// </code>
        /// </example>
        /// <param name="trueFalse">True to move the mouse to the elements emulating a human<br/>False to jump the cursor to the element</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        [GPALSettings(Description = "Simulate moving the mouse for every action (implies .WithHardware). [false (default)]")]
        public static IAllowGPALSettings WithSimulateMouseMovement(bool trueFalse = true)
        {
            GPALSettings.SimulateMouse = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Global setting instructing GPAL to terminate if any Selector cannot find any elements.<br/>
        /// This is the GLOBAL setting affecting all Selectors.<br/>
        /// This setting is also available on individual selectors to only stop on critical Selectors.<br/>
        /// Default is false
        /// </summary>
        /// <param name="trueFalse">True to stop the workflow if an element is not found<br/>False to continue</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithStopOnNotFound(true)
        /// </code>
        /// </example>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Stop workflow if any selector is not found")]
        public static IAllowGPALSettings WithStopOnNotFound(bool trueFalse = true)
        {
            GPALSettings.StopOnNotFound = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }

        [GPALSettings(Description = "Set the default location to store temporary profiles if browser.WithProfileDataDirectory is not used. default [C:\\profiles]")]
        public static IAllowGPALSettings WithTempProfileParentDirectory([Directory] string tempProfileDirectory)
        {
            // Expand environment variables as GPALSettings does, so %TEMP%\gpal resolves the same.
            GPALSettings.TempProfileDirectory = Environment.ExpandEnvironmentVariables(tempProfileDirectory);
            return (IAllowGPALSettings) GPALSettings;
        }
        /// <summary>
        /// Prompt 'Press any key...' before terminating due to an uncaught exception.
        /// GPAL tries to handle all exceptions and continue running, publishing events with more information.
        /// </summary>
        /// <param name="trueFalse">true - prompt before exit<br/>false [default] - just exit</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Print GPAL published information or exception messages to the output window in VisualStudio. [true (default)]")]
        public static IAllowGPALSettings WithPromptBeforeUnexpectedExit(bool trueFalse = true)
        {
            GPALSettings.PromptBeforeUnexpectedExit = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global setting to wait for time in ticks for an element to show up.
        /// Applied to every element in every workflow.
        /// Override in a unit of work using this pattern
        ///     .WithSelector(select0or)
        ///     .WaitFor(waitForInTicks)
        ///     .LeftClick()
        /// NOTE: shortcut actions like LeftClick(selector) do not wait;
        ///     only this global setting makes them wait
        /// </summary>
        /// <param name="waitForInTicks"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Global setting to wait for time in ticks for an element to show up")]
        public static IAllowGPALSettings WithWaitFor([Milliseconds] int waitForInTicks)
        {
            GPALSettings.WaitForInMs = waitForInTicks;
            return (IAllowGPALSettings)GPALSettings;
        }


        // Fills in the browser version through DriverHelper when the browser has none yet.
        private static void CheckVersions()
        {
            if (string.Empty.Equals(tmpBrowser?.BrowserVersion))
            {
                BrowserType browserType = tmpBrowser.BrowserType;

                if (BrowserType.Chrome == browserType)
                {
                    DriverHelper.GetChromeVersion(out tmpBrowser.BrowserSettings.Version);
                }
                else if (BrowserType.Edge == browserType)
                {
                    DriverHelper.GetEdgeVersion(out tmpBrowser.BrowserSettings.Version);
                }
                else if (BrowserType.FireFox == browserType)
                {
                    DriverHelper.GetFirefoxVersion(out tmpBrowser.BrowserSettings.Version);
                }

                //ICapabilities capabilities = ((RemoteWebDriver)tmpBrowser.BrowserDriver).Capabilities;
                //tmpBrowser.BrowserVersion = capabilities.GetCapability("browserVersion")?.ToString();

                // Common WebDriver Capabilities:
                // "browserName"        - Name of the browser (e.g., "chrome", "firefox", "edge")
                // "browserVersion"     - Version of the browser (e.g., "120.0.6099.110")
                // "platformName"       - OS platform ("windows", "linux", "macOS")
                // "acceptInsecureCerts" - Whether SSL cert warnings are ignored (true/false)
                // "setWindowRect"      - Whether the WebDriver can resize the window (true/false)
                //
                // Chrome-Specific:
                // "goog:chromeOptions" - ChromeDriver options (e.g., args, binary path)
                // "goog:loggingPrefs"  - Logging preferences (e.g., performance, console logs)
                //
                // Edge-Specific:
                // "ms:edgeOptions"     - EdgeDriver options (e.g., args, headless mode)
                // "ms:edgeChromium"    - Whether Edge is running on Chromium (true/false)
                //
                // Firefox-Specific:
                // "moz:firefoxOptions" - FirefoxDriver options
                // "moz:headless"       - Whether running in headless mode (true/false)
                //
                // Remote/Grid Execution:
                // "webdriver.remote.sessionid" - Session ID for remote WebDriver
                // "timeouts"           - Dictionary of timeouts (implicit, pageLoad, script)
            }
        }
    }
}
