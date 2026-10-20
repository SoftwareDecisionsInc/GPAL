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

using DocumentFormat.OpenXml.Drawing;
using GenerallyPositive.Application;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Browser.Browser;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPAL;

/*
var browser =
    GPAL.Browser
    // optional from here
    .GlobalWaitOnDocumentReady(bool) // true - turn ON waiting for document.ready before returning from verb
    .WithBrowser(BrowserType enum) // default chrome
		.WithLoadImages(bool)   // default true
        .WithScrollIntoView(bool) // default false - follow each scraped element on screen
        .WithOpenPDFExternally(bool) // default true
        .WithDownloadFileTypes(string) // csv - defaults?
        .WithOpenFileTypes(string) // csv - defaults?
    // optional to here
    .GoTo(URL)

    // test document.ready()
    // do we ever deal with before/after/during events
    // on With, create Selector container for this UOW which ends in an action
    .WithSelector(searchSelector)
    .FillInFrom(string) /// WithHardware implies moving the mouse and clicks and enter text via keyboard emulation

    // test document.ready()
    // on With, create Selector container for this UOW which ends in an action
    .GlobalWaitOnDocumentReady(bool) // false - turn OFF waiting for document.ready before returning from verb
    .WithSelector(searchButtonSelector)
    .LeftClick() /// WithHardware implies moving the mouse and clicks

    // test document.ready()
    // on using, create Selector container for this UOW which ends in an action
    // TODO: how to say do not return sparesely populated arrays
    .WithSelector(colum1DataSelector)   // link .All at selector level
    .WithSelector(colum2DataSelector)   // description .All at selector level
    .WithSelector(colum3DataSelector)   // price .All at selector level
    .WithSelector(colum4DataSelector)   // image link .All at selector level
    .WithSelector(colum5DataSelector)   // repeat this one value in this column for all rows .Repeat at selector level
                                        // indicates above selectors are repeated and get all on the current page matching pattern
    .WithAll
    .WithNextPageButton(nextPageSelector)   // if not defined, only gets current page
    .WithPages(10)  // default is one page, but should be used in conjunction with above
    .GetGrid()  // change the return type to Tokens for manipulation/validation/saving

    // test document.ready()
    // tied to GPALGrid object
    .WithHeader(columnAString)
    .WithHeader(columnBString)
    .WithHeader(columnCString)
    .WithHeader(columnDString)
    .WithHeader(columnEString)
    .SaveToExcel(fileName)  // return type is GPAL.browser

    // the formal end of everything
    .Close()	// implies dispose, implement IDisposable on Browser

    // end of iterations
*/
namespace GenerallyPositive.Browser
{
    #region <BrowserInterfaces>
    public interface IBrowser :
        IAllowBrowserSettingsOrGoTo,
        IAllowWithAllThatMatch,
        IAllowWithNextPageButtonOrGetGrid,
        IAllowWithPagesAndGridActions,
        IAllowWithHeaderOrFileActions,
        IAllowBrowserActionOrAnySelector,
        IAllowCallTemplate,
        IAllowAfterAnySelector,
        IAllowAfterAnySelectorExceptWithAll,
        IAllowAfterWaitForAndSizeControl,
        IAllowAllBrowserAndAllSelector,
        IAllowBrowserSettingsOrSelectorAction,
        IAllowGetGridAndFillInFrom,
        IAllowWithPagesAndFetch,
        IAllowWithGridToSave,
        IAllowBrowserTypeOrGoto,
        IAllowWorkflowExecution,
        IAllowWorkflow,
        IAllowRun,
        IAllowWindowActions,
        //        IAllowOCRSettingsOrExecute,
        IAllowStorageAction,
        IAllowStorageOptions,
        IAllowStorageData,
        IAllowStorageOutData,
        IAllowToGPALObject<IBrowser>
    {
        // Public properties
        bool? FileDownloaded { [Data(Description = "True if the most recent download action successfully downloaded a file", Submenu = "Browser State")] get; set; }
        BrowserType BrowserType { [Data(Description = "Return the current browser type", Submenu = "Browser State")] get; }
        IWebDriver BrowserDriver { [Data(Description = "The browser driver controlling the current Browser", Submenu = "Browser State")] get; }
        /// <summary>
        /// Base url of this browser's GPALRestAPI, e.g. http://localhost:3117/, from the port it reported at startup.
        /// Null unless the engine is OttoMagic.
        /// </summary>
        string RestApiBaseUrl { [Data(Description = "Where this browser's GPALRestAPI is listening, e.g. http://localhost:3117/, taken from the port it announced while starting", Submenu = "Browser State")] get; }
        int CurrentTabIdx { [Data(Description = "Index of the currently active browser tab/window", Submenu = "Browser State")] get; }
        string CurrentUrl { [Data(Description = "The URL of the page currently loaded in the browser", Submenu = "Browser State")] get; }
        string BrowserVersion { [Data(Description = "The version of the browser being automated", Submenu = "Browser State")] get; }
        object JavaScriptResultObj { [Data(Description = "The result object returned by the most recent ExecuteJavaScriptObj call", Submenu = "Browser State")] get; }
        string JavaScriptResultStr { [Data(Description = "The result string returned by the most recent ExecuteJavaScriptStr call", Submenu = "Browser State")] get; }
        PuppeteerCommunicator PuppeteerCommunicator { [Data(Description = "Communicator used to send/receive messages to the Puppeteer engine for this Browser", Submenu = "Engines")] get; }
        IPuppeteerClient PuppeteerClient { [Data(Description = "The Puppeteer client used to issue commands when this Browser is using the Puppeteer engine", Submenu = "Engines")] get; }
        int ServerResponseCode { [Data(Description = "HTTP status code of the most recent page navigation/response", Submenu = "Browser State")] get; set; } // CAVEAT: public set: unsure why visual studio will not let me use internal set in Broser without set here, like everywhere else
        string SiteMapSource { [Data(Description = "The sitemap text the last GetSiteMapUrls read", Submenu = "Browser State")] get; }
        IReadOnlyList<string> ColumnNames { [Data(Description = "The column names the last GetGrid read off the page itself, from the header row of a table it was pointed at", Submenu = "Grids")] get; }
        bool UseOttoMagic { [Data(Description = "True if this Browser is using the OttoMagic automation engine", Submenu = "Browser State")] get; }
        bool UsePuppeteer { [Data(Description = "True if this Browser is using the Puppeteer automation engine", Submenu = "Browser State")] get; }
        bool UseSelenium { [Data(Description = "True if this Browser is using the Selenium automation engine", Submenu = "Browser State")] get; }
        AutomationEngine AutomationEngine { [Data(Description = "The automation engine (OttoMagic, Puppeteer, or Selenium) used by this Browser", Submenu = "Browser State")] get; }
        IRESTClient OttoMagicClient { [Data(Description = "The client for the OttoMagic REST API this browser is running", Submenu = "Engines")] get; }
        [Settings(Description = "The hidden desktop this browser is running on, or null when it is on the visible one.")]
        IHiddenDesktop HiddenDesktop { [Component(Description = "The hidden desktop this browser is running on, or null when it is on the desktop you are looking at")] get; }
        [Scroll(Description = "Helper method to check whether the current page is at the bottom")]
        bool IsEndOfPage();
        bool IsAlive { [Data(Description = "Whether the browser is still running", Submenu = "Browser State")] get; }
        // Public methods not covered by existing interfaces
        [Action(Description = "Remove the CallIf handler everywhere it has been set. Use this once you are done using the handler.", Submenu = "Page")]
        void RemoveCallIfHandlerEverywhere(CallIfDelegate func);
        MagicHelper MagicHelper { [Data(Description = "The MagicHelper used to issue commands when this Browser is using the OttoMagic engine", Submenu = "Engines")] get; }
    }
    /// <summary>
    /// A hidden Win32 desktop a browser runs on, reached from the browser's HiddenDesktop property, so you need not
    /// keep its name.
    /// </summary>
    public interface IHiddenDesktop
    {
        [Settings(Description = "What this desktop is called. Browsers naming the same one share it.")]
        string Name { [Data(Description = "What this desktop is called")] get; }
        [Settings(Description = "How many visible top level windows are on it right now.")]
        int WindowCount { [Data(Description = "Visible top level windows on it right now")] get; }
        [Browser(Description = "Put this desktop on the screen for the default time, then put yours back.")]
        string Peek();
        [Browser(Description = "Put this desktop on the screen for this many milliseconds, then put yours back.")]
        string Peek([Milliseconds] int forMs);
        [Browser(Description = "Give up GPAL's handle on this desktop. Closes nothing running there.")]
        void Close();
        [Browser(Description = "Close what is running on this desktop and then give up the handle on it.")]
        void CloseEverything();
    }

    /// <summary>
    /// All hidden desktops this process created, and the screen. Peek and Return change which desktop is on screen,
    /// so they live here, not on IHiddenDesktop.
    /// </summary>
    public interface IHiddenDesktops
    {
        [Settings(Description = "Every desktop made so far, in the order they were made.")]
        IReadOnlyList<string> Names { [Data(Description = "Every desktop made so far, in the order they were made")] get; }
        [Settings(Description = "One desktop by name, so a workflow that did not keep the browser can still reach it.")]
        IHiddenDesktop this[string desktopName] { [Data(Description = "One hidden desktop, by name")] get; }
        [Settings(Description = "Visible top level windows across every hidden desktop added up.")]
        int WindowCount { [Data(Description = "Windows on every hidden desktop added up")] get; }
        [Settings(Description = "True while the screen is on a hidden desktop and has not been handed back.")]
        bool IsShowing { [Data(Description = "True while the screen is on a hidden desktop")] get; }
        [Browser(Description = "Walk every hidden desktop made so far, the default time each.")]
        string Peek();
        [Browser(Description = "Walk every hidden desktop made so far, this many milliseconds each.")]
        string Peek([Milliseconds] int forMsEach);
        [Browser(Description = "Put one named desktop on the screen for the default time.")]
        string Peek(string desktopName);
        [Browser(Description = "Put the screen back on the desktop a peek took it from. Safe from any thread.")]
        bool Return();
        [Browser(Description = "Give up GPAL's handles on every hidden desktop. Closes nothing running on them.")]
        void CloseAll();
        [Browser(Description = "Close what is running on every hidden desktop and then give up the handles.")]
        void CloseEverything();
    }

    #region <BaseBrowserInterfaces>
    public interface IAllowGoToOrGet : IAllowGoTo, IAllowGet, IAllowToGPALObject<IBrowser>
    { }
    public interface IAllowBrowserTypeOrGoto : IAllowBrowserType, IAllowGoToOrGet, IAllowExistingBrowser, IAllowWithAutomationEngine // if we just want to use default browser
    { }
    public interface IAllowBrowserType { [Settings(Description = "Set which browser type to run: Chrome (default), Edge or Firefox.", Submenu = "Engine")]  IAllowBrowserSettingsOrGoTo WithBrowserType([BrowserType(Description = "Chrome, Edge or Firefox")] BrowserType browserType); }

    public interface IAllowWithAutomationEngine
    {
        [Settings(Description = "Set which engine to use to automate the browser: OttoMagic, Puppeteer (default) or Selenium.", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithAutomationEngine(AutomationEngine automationEngine);

    }
    public interface IAllowDebugPort
    {
        [Settings(Description = "Set debug port to use [57005 (0xdead) default]. Specify to reconnect to a browser session using .WithExistingCDPDebugPort.  If both port and pipe are set, port is preferred.", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithCDPDebugPort(int debugPort = 0xdead);
        [Settings(Description = "Use debug pipe for puppeteer commmunications. If both port and pipe are set, port is preferred.", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithCDPDebugPipe(bool trueFalse = false);
        [Settings(Description = "Drive a browser whose GPALRestAPI is already running, here or on another machine, instead of launching one", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithGPALRestAPIUrl([URL] string restApiUrl);
    }
    public interface IAllowExistingBrowser
    {
        [Settings(Description = "Drive a browser that is already running and listening on this remote debugging port, instead of starting one", Submenu = "Engine")]
        IAllowBrowserActionOrAnySelector WithExistingCDPDebugPort(int debugPort = 6565);
    }
    public interface IAllowBrowserSettings : IAllowWithAutomationEngine, IAllowSizeControl, IAllowExistingBrowser, IAllowPersistentCallBack, IAllowWithPersistentSelector, IAllowNewTabOrWindow, IAllowNetworkIdleSettings, IAllowDebugPort
    {
        [Settings(Description = "Load images in the page, this can affect layout and object coordinates, if that matters.", Submenu = "Stealth")]
        IAllowBrowserSettingsOrGoTo WithLoadImages(bool trueFalse = true);
        [Settings(Description = "Scroll each scraped element into view as it is read, so the page follows the workflow. For watching a run, not for correctness. Ignored in headless. [false default]", Submenu = "Selectors")]
        IAllowBrowserSettingsOrGoTo WithScrollIntoView(bool trueFalse = true);
        [Settings(Description = "Open PDF files externally, not in the browser. So, download the pdf.", Submenu = "Downloads")]
        IAllowBrowserSettingsOrGoTo WithOpenPDFExternally(bool trueFalse = false);
        [Settings(Description = "Prompt with a Save As dialog when downloading a file? Up to the browser; Edge, headless and hidden desktops never prompt. [false default]", Submenu = "Downloads")]
        IAllowBrowserSettingsOrGoTo WithPromptForDownload(bool trueFalse = true);
        [Settings(Description = "Location of the browser driver and where automatically updating the driver will occur. Default is executable directory.", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithDriverLocation([Directory(Description = "Location of browser driver for this instance. [./ (default)(same dir as exe)]")] string driverPath);
        [Settings(Description = "Control whether popups are blocked", Submenu = "Stealth")]
        IAllowBrowserSettingsOrGoTo WithBlockPopUps(bool trueFalse = true);
        [Settings(Description = "Use javascript injection to interact with an element", Submenu = "Stealth")]
        IAllowBrowserSettingsOrGoTo WithUseStealth(StealthType steathType);
        [Settings(Description = "Always overwrite a file if it exists", Submenu = "Downloads")]
        IAllowBrowserSettingsOrGoTo WithOverwriteExistingFile(bool trueFalse = true);
        [Settings(Description = "Wait for browser page to finish loading", Submenu = "Waiting")]
        IAllowBrowserSettingsOrGoTo WithWaitOnDocumentReady([Milliseconds] int timeoutInMs);
        [Settings(Description = "How long a click waits to see whether it started a navigation. [250 (default)] 0 skips the check", Submenu = "Waiting")]
        IAllowBrowserSettingsOrGoTo WithNavigationGrace([Milliseconds] int graceInMs);
        [Settings(Description = "Run the browser on a Win32 desktop object of its own, headful but invisible, so the workflow does not take the screen. Hardware engines cannot reach a desktop nobody is on and step down to the protocol engine of their own family.", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithHiddenDesktop(bool trueFalse = true);
        [Settings(Description = "Run the browser on the named Win32 desktop object. Browsers naming the same desktop share it, browsers naming different ones get one each.", Submenu = "Engine")]
        IAllowBrowserSettingsOrGoTo WithHiddenDesktop(string desktopName);
        [Settings(Description = "To change browser profiles, use this option to specify the data-dir path. If Directory and UserName are both defined, Directory will be used.", Submenu = "Profile")]
        IAllowBrowserSettingsOrGoTo WithProfileDataDirectory([Directory] string profilePath);
        [Settings(Description = "To change browser profiles, use this option to specify the username whose browser data-dir to use. If Directory and UserName are both defined, Directory will be used.", Submenu = "Profile")]
        IAllowBrowserSettingsOrGoTo WithProfileUserName(string profileUserName);
        [Settings(Description = "The name of the browser profile to use under the data-dir (sometime, might be same result as UserName)", Submenu = "Profile")]
        IAllowBrowserSettingsOrGoTo WithProfileName(string profileName);
        [Settings(Description = "Set the browser default download directory. Can be overriden with a fully qualified filename. [path/file/]", Submenu = "Downloads")]
        IAllowBrowserSettingsOrGoTo WithDownloadLocation(string downloadPat);
        [Settings(Description = "Use websockets to download for Right Click and Download", Submenu = "Downloads")]
        IAllowBrowserSettingsOrGoTo WithUseDirectDownload(bool trueFalse = false);
        [Settings(Description = "Set the time in seconds to wait for a download to finish before giving up.", Submenu = "Downloads")]
        IAllowBrowserSettingsOrGoTo WithDownloadTimeoutInSec(int seconds = 60);
        [Settings(Description = "Set the HTTP referrer to use when navigating every subsequent Get/Goto. Use empty string to clear.", Submenu = "Stealth")]
        IAllowBrowserSettingsOrGoTo WithUseReferrer(string referrer = "");
        [Settings(Description = "Set the user agent to use with any Goto or Get for the rest of the browser sessions", Submenu = "Stealth")]
        IAllowBrowserSettingsOrGoTo WithUseUserAgent(string userAgent = "");
        [Settings(Description = "Launch a browser purely to read its real user agent. Off by default, in which case the user agent comes from WithUseUserAgent, then gpal.yaml, then the per-browser template with the installed version filled in", Submenu = "Stealth")]
        IAllowBrowserSettingsOrGoTo WithUserAgentFromBrowser(bool userAgentFromBrowser = true);
        [Settings(Description = "Set whether or not to obey robots.txt (false - default).", Submenu = "Robots")]
        IAllowBrowserSettingsOrGoTo WithObeyRobotsTxt(bool trueFalse = false);
        [Settings(Description = "Set whether or not to respect robot meta tags noindex/nofollow/none (false - default).", Submenu = "Robots")]
        IAllowBrowserSettingsOrGoTo WithRespectRobotMetaTags(bool trueFalse = false);
    }
    public interface IAllowNewTabOrWindow
    {
        [Action(Description = "Open url in a new tab", Submenu = "Tabs and Windows")]
        IAllowBrowserActionOrAnySelector NewTab(GPALUrl url = null);
        [Action(Description = "Open url in a new window", Submenu = "Tabs and Windows")]
        IAllowBrowserActionOrAnySelector OpenWindow(GPALUrl url = null);
    }
    public interface IAllowPersistentCallBack
    {
        // return values
        // 0 = not handled
        // 1 = handled
        // -1 = error
        [Callback(Description = "GLOBAL based callback if any selector in any UOW is found")]
        IAllowBrowserSettingsOrGoTo PersistentCallIfFound(Browser.CallIfDelegate CallIfFound);
        [Callback(Description = "GLOBAL based callback if any selector in any UOW is NOT found")]
        IAllowBrowserSettingsOrGoTo PersistentCallIfNotFound(Browser.CallIfDelegate CallIfNotFound);
        [Callback(Description = "Callback when something fails that the workflow can decide about, a navigation that never loaded a page being the case today. Falls through to GPAL.CallOnFail when this browser has none")]
        IAllowBrowserSettingsOrGoTo CallOnFail(Browser.CallOnFailDelegate CallOnFail);
        [Settings(Description = "Who this browser is, for a site that asks. The credential's WebAuthType decides how it is presented, and it is presented once at the first navigation", Submenu = "Profile")]
        IAllowBrowserSettingsOrGoTo WithCredentials(ICredentials credentials);
    }
    public interface IAllowCallBack
    {
        // return values
        // 0 = not handled
        // 1 = handled
        // -1 = error
        [Callback(Description = "UOW based callback if any selector in the UOW is found")]
        IAllowAfterAnySelector CallIfFound(Browser.CallIfDelegate CallIfFound);
        [Callback(Description = "UOW based callback if any selector in the UOW is NOT found")]
        IAllowAfterAnySelector CallIfNotFound(Browser.CallIfDelegate CallIfNotFound);
        [Callback(Description = "UOW based callback when a row of input is consumed and entered.\nUseful for chaining to another workflow")]
        IAllowAfterAnySelector CallAfterFillIn(Browser.CallAfterFillInDelegate CallAfterFillIn);
    }
    public interface IAllowSizeControl : IAllowToGPALObject<IBrowser>
    {
        IAllowAllBrowserAndAllSelector Maximize { [Browser(Description = "Maximize the browser window", Submenu = "Windows")] get; }
        IAllowAllBrowserAndAllSelector Minimize { [Browser(Description = "Minimize the browser window", Submenu = "Windows")] get; }
        //        IAllowAllBrowserAndAllSelector Normal { [Browser(Description = "Restore the browser window after fullscreen.")] get; }
        IAllowAllBrowserAndAllSelector Restore { [Browser(Description = "Restore the browser window from maximize or full-screen.", Submenu = "Windows")] get; }
        IAllowAllBrowserAndAllSelector FullScreen { [Browser(Description = "Put the browser in full-screen mode.", Submenu = "Windows")] get; }
        [Browser(Description = "Set the desired browser window size.", Submenu = "Windows")]
        IAllowAllBrowserAndAllSelector WithWindowSize(System.Drawing.Rectangle windowSize);
    }
    public interface IAllowHideElement
    {
        [Action(Description = "Hide the found element", Submenu = "Element")]
        IAllowBrowserActionOrAnySelector Hide(Selector selector = null);
    }
    public interface IAllowSetAttributeValue
    {
        /// <summary>
        /// Sets a DOM attribute on the found element(s) with element.setAttribute, using the name from the preceding
        /// <see cref="IAllowWithAttribute.WithAttribute"/>. Hardware falls back to Selenium/JavaScript or Puppeteer.
        /// Fires no input or change events; follow with FireChangeEvent if the page must react.
        /// </summary>
        /// <param name="value">Value to assign to the attribute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        [Action(Description = "Directly set a DOM attribute (named by the preceding WithAttribute call) to the given value on the found element(s). Does not simulate typing and does not honor GPAL.TypingDelay.", Submenu = "Element")]
        IAllowBrowserActionOrAnySelector SetAttribute(string value);
    }
    public interface IAllowSetValueFromElement
    {
        /// <summary>
        /// Copies the value from the element matched by <paramref name="selector"/> into the element(s) matched
        /// by the preceding <see cref="IAllowWithSelector.WithSelector"/> call, i.e.
        /// <c>destElement.value = srcElement.value</c>. Uses OttoMagic, Puppeteer or JavaScript depending on the
        /// active engine, and fires "input"/"change" events on the destination so the page reacts to the new value.
        /// </summary>
        /// <param name="selector">Selector identifying the source element to read the value from</param>
        /// <returns>Fluent interface to write your workflow</returns>
        [Action(Description = "Set the value of the element(s) matched by the preceding WithSelector call from the value of the element matched by the given selector.", Submenu = "Element")]
        IAllowBrowserActionOrAnySelector SetValueFrom(Selector selector);
    }
    public interface IAllowWithAttribute
    {
        /// <summary>
        /// Names the DOM attribute to set on the found element(s). Must be followed by
        /// <see cref="IAllowSetAttributeValue.SetAttribute"/> to supply the value and complete the action.
        /// </summary>
        /// <param name="attribute">Name of the attribute to set (e.g. "disabled", "value", "data-foo")</param>
        /// <returns>Fluent interface; must be followed by SetAttribute(value) to complete the action</returns>
        [Data(Description = "Names the attribute to set; must be followed by .SetAttribute(value) to specify the value and complete the action.", Submenu = "Grids")]
        IAllowSetAttributeValue WithAttribute(string attribute);
    }
    public interface IAllowClicks
    {
        [Action(Description = "Left click the found element.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector LeftClick();
        [Action(Description = "Left click the found element.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector LeftClick(Selector selector);
        [Action(Description = "M iddle click the found element.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector MiddleClick();
        [Action(Description = "Left click the found select menu element.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector SelectClick(SelectClickType selectClickType);
        [Action(Description = "Stealth (Runtime.disable before) Left click the found element.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector StealthLeftClick(Selector selector);
        [Action(Description = "Left click the found element, pressing modifier key (none [default]) while clicking.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector LeftClick(ModifierKeys modifierKeys);
        [Action(Description = "Left click the found element, handling a download dialog after clicking.", Submenu = "Files")]
        IAllowBrowserActionOrAnySelector LeftClickAndDownload(GPALFile filenames);
        [Action(Description = "Left click the found element, handling an upload dialog after clicking.", Submenu = "Files")]
        IAllowBrowserActionOrAnySelector LeftClickAndUpload(GPALFile filenames);
        [Action(Description = "Left double click the found element, pressing modifier key (none [default]) while clicking.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector LeftDoubleClick();
        [Action(Description = "Right click the found element, pressing modifier key (none [default]) while clicking.", Submenu = "Clicks")]
        IAllowBrowserActionOrAnySelector RightClick(Selector selector = null);

        // returns the file name(s) saved to by adding to the ReturnedFilenames list
        // if not overwriting files, then a timestamp is appended to the filename and returned
        // if overwriting, the same filename is added to the list
        // since we can iterate over multiple elements, this could right click and download multiple links, so filenames contains ALL expected filenames to save to
        [Action(Description = "Save the element (if WithSelector is specified) or page to the GPALFile.", Submenu = "Files")]
        IAllowBrowserActionOrAnySelector RightClickAndDownload(GPALFile filenames);
    }
    public interface IAllowWaitForWindow
    {
        [Wait(Description = "After an action is called (ie, LeftClick), wait for a window to open with this name (title)")]
        IAllowAfterWaitForAndSizeControl WaitForWindow(string windowName);
        [Wait(Description = "After an action is called (ie, LeftClick), wait for a window to open which name (title) matches the regular expression")]
        IAllowAfterWaitForAndSizeControl WaitForWindowRegex([Regex] string windowNameRegex);
        [Wait(Description = "The time in seconds to wait for a window to appear before giving up")]
        IAllowAfterWaitForAndSizeControl WithWaitForWindowTimeout([Seconds] int waitTimeInSeconds);

    }
    public interface IAllowFillInFrom
    {
        [Data(Description = "Send the specified string using the selector interaction type (selenium/javascript/hardware)", Submenu = "Keyboard")]
        IAllowBrowserActionOrAnySelector SendString(string textToSend);
        [Data(Description = "Send a control character specified by the VKCode (GPAL has some defined)", Submenu = "Keyboard")]
        IAllowBrowserActionOrAnySelector SendKey(byte VKCode);
        [Data(Description = "Press the modifier key specified (Control/Shift/Alt). NOTE: Be careful with Shift and sending mixed case strings after pressing Shift. Mixed case strings will release the Shift key.", Submenu = "Keyboard")]
        IAllowBrowserActionOrAnySelector PressModifierKey(ModifierKeys modifierKeys);
        [Data(Description = "Release the modifier key specified (Control/Shift/Alt) as previously pressed.", Submenu = "Keyboard")]
        IAllowBrowserActionOrAnySelector ReleaseModifierKey(ModifierKeys modifierKeys);

        [Data(Description = "Fill in [overwrite] text from a string", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector FillInFrom(string textToUse);
        [Data(Description = "Append to the end text from a string", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector AppendFrom(string textToUse);
        [Data(Description = "Insert text (wherever the cursor is) from a string", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector InsertFrom(string textToUse);

        [Data(Description = "Fill in [overwrite] text from a file. CSV values per line takenized for each input selector", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector FillInFrom(GPALFile inputFile);
        [Data(Description = "Append to the end text from a file. CSV values per line takenized for each input selector", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector AppendFrom(GPALFile inputFile);
        [Data(Description = "Insert text (wherever the cursor is) from a file. CSV values per line takenized for each input selector", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector InsertFrom(GPALFile inputFile);

        [Data(Description = "Fill in [overwrite] text from a database query. Tokenized by each column per input per row", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector FillInFrom(GPALDatabase inpuDatabase);
        [Data(Description = "Append to the end text from a database query. Tokenized by each column per input per row", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector AppendFrom(GPALDatabase inpuDatabase);
        [Data(Description = "Insert text (wherever the cursor is) from a database query. Tokenized by each column per input per row", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector InsertFrom(GPALDatabase inpuDatabase);
        [Data(Description = "Fill in [overwrite] text from a data grid. Tokenized List<List<string>> each string per input per list entry", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector FillInFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Append to the end  text from a data grid. Tokenized List<List<string>> each string per input per list entry", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector AppendFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Insert text (wherever the cursor is) from a data grid. Tokenized List<List<string>> each string per input per list entry", Submenu = "Fill In")]
        IAllowBrowserActionOrAnySelector InsertFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Set the value of an input range element", Submenu = "Keyboard")]
        IAllowBrowserActionOrAnySelector SetRange(int rangeValue);
    };
    public interface IAllowTabActions
    {
        [Browser(Description = "Close the current tab [no URL], or the tab with URL", Submenu = "Tabs")]
        IAllowBrowserActionOrAnySelector CloseTab(dynamic URLorTabId = null);
        IAllowBrowserActionOrAnySelector NextTab { [Browser(Description = "Move to the next tab (to the right)", Submenu = "Tabs")] get; }
        IAllowBrowserActionOrAnySelector PreviousTab { [Browser(Description = "Move to the previous tab (to the left)", Submenu = "Tabs")] get; }
        [Browser(Description = "Go to tab with URL", Submenu = "Tabs")]
        IAllowBrowserActionOrAnySelector GoToTab(dynamic URLorTabTuple = null);
    }
    public interface IAllowInSelector
    {
        [Selector(Description = "Subsequent withselector searches are for elements nested under this element. Nesting is possible.")]
        IAllowAfterAnySelector InElement(Selector selector);
        [Selector(Description = "Subsequent withselector searches are for elements nested under this iframe. Nesting is possible.")]
        IAllowAfterAnySelector InFrame(Selector selector);

        /// <summary>
        /// NOTE: can only use CSS selectors in shadow dom
        /// </summary>
        /// <param name="shadowDomSelector"></param>
        /// <returns></returns>
        [Selector(Description = "Subsequent selector are for elements in a shadowdom specified by the shadowdom selector. Nesting is possible.")]
        IAllowAfterAnySelector InShadowDom(Selector shadowDomSelector);
        [Selector(Description = "Reset back to the top level dom document for finding elements")]
        IAllowAfterAnySelector InMainDom();
    }
    public interface IAllowScrolling
    {
        [Action(Description = "Scroll the specified web element n pixels horizontally", Submenu = "Element")]
        IAllowBrowserActionOrAnySelector ScrollElementHorizontal([Pixels] int scrollAmountInPixels);
        [Action(Description = "Scroll the specified web element n pixels vertically", Submenu = "Element")]
        IAllowBrowserActionOrAnySelector ScrollElementVertical([Pixels] int scrollAmountInPixels);
    }
    public interface IAllowWindowScrollBy
    {
        [Browser(Description = "Scroll the browser window horizontally by scrollAmount pixels", Submenu = "Scrolling")]
        IAllowBrowserActionOrAnySelector ScrollWindowHorizontal(int scrollAmount);
        [Browser(Description = "Scroll the browser window veritcally by scrollAmount pixels", Submenu = "Scrolling")]
        IAllowBrowserActionOrAnySelector ScrollWindowVertical(int scrollAmount);
    }
    public interface IAllowWindowActions
    {
        [Browser(Description = "Close the current window [no URL], or the window with URL", Submenu = "Windows")]
        IAllowBrowserActionOrAnySelector CloseWindow(dynamic URLorId = null);
        IAllowBrowserActionOrAnySelector NextWindow { [Browser(Description = "Move to the next window (to the right)", Submenu = "Windows")] get; }
        IAllowBrowserActionOrAnySelector PreviousWindow { [Browser(Description = "Move to the previous window (to the left)", Submenu = "Windows")] get; }
        [Browser(Description = "Go to window with URL", Submenu = "Windows")]
        IAllowBrowserActionOrAnySelector GoToWindow(dynamic URLorId = null);
    }
    public interface IAllowPageControl
    {
        IAllowBrowserActionOrAnySelector Back { [Browser(Description = "Move back one entry in the browser history", Submenu = "Navigation")] get; }
        IAllowBrowserActionOrAnySelector Forward { [Browser(Description = "Move forward one entry in the browser history", Submenu = "Navigation")] get; }
        IAllowBrowserActionOrAnySelector PageDown { [Browser(Description = "Scroll one page down", Submenu = "Scrolling")] get; }
        IAllowBrowserActionOrAnySelector PageEnd { [Browser(Description = "Scroll to page bottom", Submenu = "Scrolling")] get; }
        IAllowBrowserActionOrAnySelector PageTop { [Browser(Description = "Scroll to page top", Submenu = "Scrolling")] get; }
        IAllowBrowserActionOrAnySelector PageUp { [Browser(Description = "Scroll one page up", Submenu = "Scrolling")] get; }
        IAllowBrowserActionOrAnySelector Refresh { [Browser(Description = "Refresh the current page", Submenu = "Navigation")] get; }
    }
    public interface IAllowExecuteJavaScript
    {
        [Browser(Description = "Execute the supplied javascript in the browser and return an integer in browser.JavaScriptResultObj", Submenu = "Scripts")]
        IAllowBrowserActionOrAnySelector ExecuteJavaScriptObj([JavaScript] string javascript);
        [Browser(Description = "Execute the supplied javascript in the browser and return an integer in browser.JavaScriptResultStr", Submenu = "Scripts")]
        IAllowBrowserActionOrAnySelector ExecuteJavaScriptStr([JavaScript] string javascript);
    }
    public interface IAllowDialogHandling
    {
        [Browser(Description = "Answer the alert, confirm and prompt dialogs the page raises, instead of letting them open and block. True accepts (OK, and confirm returns true), false dismisses (Cancel, and confirm returns false). Set it before the browser launches.", Submenu = "Dialogs")]
        IAllowBrowserActionOrAnySelector WithDialogsAccepted(bool accept);
        [Browser(Description = "What a prompt hands back when dialogs are accepted. Not available on Selenium, where the driver answers the dialog itself.", Submenu = "Dialogs")]
        IAllowBrowserActionOrAnySelector WithDialogText(string text);
        [Browser(Description = "Stop answering dialogs, so they open and block again. Different from WithDialogsAccepted(false), which answers Cancel.", Submenu = "Dialogs")]
        IAllowBrowserActionOrAnySelector ClearDialogHandling();
    }
    public interface IAllowGetWindowRectangle
    {
        [Browser(Description = "Where this browser's window is and how big it is, in screen coordinates, chrome included. Answered by whichever engine is driving, so a workflow does not have to know which one that is.", Submenu = "Windows")]
        IAllowBrowserActionOrAnySelector GetWindowRectangle(out System.Drawing.Rectangle window);
    }
    public interface IAllowInjectScript
    {
        [Browser(Description = "Inject a script (raw JS string, or GPALFile - if multiple files are specified, all are loaded and injected as separate scripts) to run on every new document load. Callable before the first .GoTo()/.Get() (active from the first page load) or at any later point (applies to subsequent navigations). Persists until .ClearInjectedScripts() is called.", Submenu = "Scripts")]
        IAllowBrowserActionOrAnySelector InjectScript([JavaScript] string script);
        [Browser(Description = "Inject script(s) loaded from a GPALFile - if multiple filenames are specified (e.g. via wildcard), all are loaded and injected as separate scripts.", Submenu = "Scripts")]
        IAllowBrowserActionOrAnySelector InjectScript(GPALFile scriptFile);
        [Browser(Description = "Remove all scripts previously registered via InjectScript.", Submenu = "Scripts")]
        IAllowBrowserActionOrAnySelector ClearInjectedScripts();
    }
    public interface IAllowNetworkIdleSettings
    {
        [Settings(Description = "Wait for browser network activity to idles. Defauilt is 0 connections.", Submenu = "Waiting")]
        IAllowBrowserSettingsOrGoTo WithWaitOnIdleConnection(bool trueFalse = true);
        [Settings(Description = "Timeout to wait for network idle = true. Defauilt is 500 ms.", Submenu = "Waiting")]
        IAllowNetworkIdleSettings WithNetworkIdleTimeoutMs(int networkIdleTimeoutMs = 500);
        [Settings(Description = "Timeout to wait for network idle = true. Defauilt is 500 ms.", Submenu = "Waiting")]
        IAllowNetworkIdleSettings WithNetworkIdleMaxConnections(int maxConnections = 0);
        [Settings(Description = "Duration to maintain network stats before pruning old data. Default is 10 seconds (10_000 ms).", Submenu = "Waiting")]
        IAllowNetworkIdleSettings WithNetworkIdlePruneMs(int networkIdlePruneMs = 10_000);

    }
    #endregion <BaseInterfaces>

    #region <Atoms>
    public interface IAllowToGPALObject<TResult>
    {
        [Fluent(Description = "Ends a declaration or workflow, returning a GPAL object which can be assigned. Use instead of an explicit cast. [ Selector mySel = GPAL.Selector.WithCSS(css).ToGPALObject() ]")]
        TResult ToGPALObject();
    }
    public interface IAllowWithPersistentSelector { [Settings(Description = "Add a selector that is looked for on every action, not just the one it is added to", Submenu = "Selectors")] IAllowBrowserSettingsOrGoTo WithPersistentSelector(Selector persistentSelector); }
    public interface IAllowWithSelector : IAllowWithData { [Selector(Description = "Add a selector to this UOW")] IAllowAfterAnySelector WithSelector(Selector selector); }
    public interface IAllowWithData
    {
        [Selector(Description = "Add literal data to a grid when using .GetGrid")] IAllowAfterAnySelector WithSelector(string literalData);
        [Selector(Description = "Add literal data to a grid by invoking this function when using .GetGrid")] IAllowAfterAnySelector WithSelector(Func<string> dataFunction);
    }
    public interface IAllowSiteMapOperations
    {
        [Action("Get the current page source", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector GetPageSource(out string sitemapXML);
        [Action("Carry on without the browser: hand back a RESTClient holding this browser's origin, cookies, user agent, accept language and credential", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector ContinueAsRESTClient(out IRESTClient client);
        [Action("Get the raw sitemap XML", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector GetSiteMap(out string sitemapXML);
        [Settings(Description = "The sitemap the next GetSiteMapUrls or SaveSiteMapUrls reads, xml or html, gzipped or not, without going to it", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector WithSitemap(GPALUrl sitemapUrl);
        [Action("Get the sitemap URLs as a List<string>")]
        IAllowBrowserActionOrAnySelector GetSiteMapUrls(out List<string> sitemapXML);
        [Action("Save the sitemap URLs to a file, format chosen by the file extension", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector SaveSiteMapUrls(GPALFile sitemapUrlsFile);
        [Action("Clear cached sitemap / LLM digest / hydration results, forcing the next Get/Save to refetch", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector ClearResultCache();
        [Action("Get the current page source as an LLM digest")]
        IAllowBrowserActionOrAnySelector GetLLMDigest(out LLMDigestResult digestResult);
        [Action("Save to file, the current page source as an LLM digest")]
        IAllowBrowserActionOrAnySelector SaveLLMDigest(GPALFile llmDigestFile);
        [Settings(Description = "The rule set from LLMDigestRules.yaml that GetLLMDigest and SaveLLMDigest use until changed; null picks one from the page's url", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector WithDigestRules(string ruleSetName);
        [Action("Get the current page next.js hydrated data", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector GetHydratedData(out NextJsHydrationResult hydrationResult);
        [Action("Save to file, the current page next.js hydrated data", Submenu = "Page")]
        IAllowBrowserActionOrAnySelector SaveHydratedData(GPALFile hydrationFile);
    }
    public interface IAllowStorageAction
    {
        [Action("Run the get storage actions for storageType", Submenu = "Calls")]
        IAllowStorageOutData RunGet(WebsiteStorageType storageType);
        [Action("Run the set storage actions for storageType", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector RunSet(WebsiteStorageType storageType);
        [Action("Run the delete storage actions for storageType", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector RunDelete(WebsiteStorageType storageType);
    }
    public interface IAllowStorageOptions : IAllowStorageAction, IAllowStorageData
    {
        [Browser(Description = "The domain for the storage action", Submenu = "Storage")]
        IAllowStorageOptions WithStorageDomain(string domain);
        [Browser(Description = "The path for the storage action", Submenu = "Storage")]
        IAllowStorageOptions WithStoragePath(string path);
        [Browser(Description = "The key for the storage action", Submenu = "Storage")]
        IAllowStorageOptions WithStorageKey(string key);
        [Browser(Description = "The store name for the indexedDb action", Submenu = "Storage")]
        IAllowStorageOptions WithStorageStoreName(string storeName);
        [Browser(Description = "The user defined filter to help select wildcard storage actions", Submenu = "Storage")]
        IAllowStorageOptions WithUserDefinedFilter(string userDefined);

    }
    public interface IAllowStorageData { [Browser(Description = "The data for storage set operations", Submenu = "Storage")] IAllowStorageOptions WithStorageData(string data); }
    public interface IAllowStorageFor { [Browser(Description = "Run the next storage action against this url's storage definitions without going there, such as clearing a site's cookies before visiting it", Submenu = "Storage")] IAllowStorageOptions For([URL()] GPALUrl url); }
    public interface IAllowStorageOutData { [Browser(Description = "The data for storage get operations", Submenu = "Storage")] IAllowBrowserActionOrAnySelector GetStorageData(out string data); }
    public interface IAllowGoTo { [Browser(Description = "Go to the specified URL", Submenu = "Navigation")] IAllowBrowserActionOrAnySelector GoTo([URL()] GPALUrl URL); }
    public interface IAllowGet { [Browser(Description = "Navigate to the URL headless, launching a headless browser when none is open. Same navigation as GoTo, with the headless download options set first", Submenu = "Navigation")] IAllowBrowserActionOrAnySelector Get([URL()] GPALUrl URL); }
    public interface IAllowClose
    {
        [Browser(Description = "Close the current browser session. True [default] to kill web drivers running.", Submenu = "Navigation")] void Close(bool killWebDrivers = false);
        [Browser(Description = "Leave the browser running for another program to pick up, and end this one's hold on it.", Submenu = "Navigation")] void Release();
    }
    public interface IAllowFocus { [Action(Description = "Bring the current element (specified by .WithSelector) into focus.", Submenu = "Element")] IAllowBrowserActionOrAnySelector Focus(Selector selector = null); }
    public interface IAllowHover { [Action(Description = "Hover the mouse over the current element (specified by .WithSelector). [hardware emulation (see .WithSimulateMouse)]", Submenu = "Mouse")] IAllowBrowserActionOrAnySelector Hover(Selector selector = null); }
    public interface IAllowMoveTo { [Action(Description = "Move the cursor (focus) to the current element (specified by .WithSelector).", Submenu = "Mouse")] IAllowBrowserActionOrAnySelector MoveTo(Selector selector = null); }
    public interface IAllowWithNextPageButton { [Data(Description = "When retrieving multiple pages of results, this is the button to click to get more results.", Submenu = "Pages")] IAllowWithPagesAndGridActions WithNextPageButton(Selector selector); }
    public interface IAllowWithInfiniteScroll { IAllowWithPagesAndGridActions WithInfiniteScroll { [Data(Description = "When retrieving multiple pages of results, this imdocates scroll to page bottom to get more results.", Submenu = "Pages")] get; } }
    public interface IAllowWithPages { [Data(Description = "Number of result pages to retrieve when retrieving multiple pages of results.", Submenu = "Pages")] IAllowGetGridAndFillInFrom WithPages(int numberOfPages); }
    public interface IAllowWithTokensFrom
    {
        [Data(Description = "Values .Fetch drops into its request's numbered tokens, one row per set of requests. Column 0 fills {0}, column 1 fills {1}, and so on", Submenu = "Fill In")]
        IAllowWithPagesAndFetch WithTokensFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Values .Fetch drops into its request's numbered tokens, from a file. CSV values per line, one line per set of requests", Submenu = "Fill In")]
        IAllowWithPagesAndFetch WithTokensFrom(GPALFile inputFile);
        [Data(Description = "Values .Fetch drops into its request's numbered tokens, from a database query. One column per token, one row per set of requests", Submenu = "Fill In")]
        IAllowWithPagesAndFetch WithTokensFrom(GPALDatabase inputDatabase);
        [Data(Description = "Values .Fetch drops into its request's numbered tokens, as a single row of comma separated values", Submenu = "Fill In")]
        IAllowWithPagesAndFetch WithTokensFrom(string tokens);
    }
    public interface IAllowGetGrid { [Data(Description = "Retrieve data and add it into this GPALGrid. Pass a grid configured with .WithDedupeData(true) to skip duplicate rows. GPAL keeps one internally for a workflow if you pass null.", Submenu = "Grids")] IAllowWithHeaderOrFileActions GetGrid(ref IGPALGrid<string> returnGrid); }
    public interface IAllowGetElements { [Data(Description = "Find elements for all selectors in this UOW and return them as a unified List<GPALElement>. Results are also available per-selector via selector.WebSelectorFoundResults.", Submenu = "Grids")] IAllowBrowserActionOrAnySelector GetElements(out List<GPALElement> elements); }
    public interface IAllowWithHeader { [Data(Description = "Column header for exported data. If a selector is not given a new (.WithName), use this. Or use this instead of the name when used on a Selector.", Submenu = "Grids")] IAllowWithHeaderOrFileActions WithHeader(string header); }
    public interface IAllowWithAllThatMatch { [Data(Description = "How many items per page to retrieve OR total items to retrieve. Defaults to int.MaxValue, get all items that match. If less than returns on a page, retrieve only rowCount, rows of results per page. If greater than the number of results on a page, this is the total number of results to return.", Submenu = "Grids")] IAllowAfterAnySelectorExceptWithAll WithAllThatMatch(int rowCount = int.MaxValue); }
    public interface IAllowWithHardware { IAllowAfterAnySelector WithHardware { [Settings(Description = "Use hardwre emulation to interact with this element", Submenu = "Engine")] get; } }
    public interface IAllowWithJavaScript { IAllowAfterAnySelector WithJavaScript { [Settings(Description = "Use javascript injection to interact with this element", Submenu = "Engine")] get; } }
    public interface IAllowWaitFor
    {
        [Wait(Description = "IF after an action is called, this is a sleep timer. If used after adding selectors (.WithSelector) [before calling an action], this is the time to wait to find elements for .WithSelectors before continuing the workflow (can cause to invoke CallIfNotFound handler)")]
        IAllowAfterWaitFor WaitFor(WaitTime waitForTimeInMs);
        [Wait(Description = "Wait for the Selector before proceeding with the workflow. Use WaitFor(timeInMs) or use the default of three [3] seconds. Similar to .WithSelector.WaitFor.Action (can cause to invoke CallIfNotFound handler)")]
        IAllowAfterWaitFor WaitFor(Selector waitForSelector);
        [Wait(Description = "Wait for the element(s) defined using .WithSelector to enter the specified state.")]
        IAllowAfterWaitFor WaitFor(ElementState elementState);
    }
    public interface IAllowWithGridToSave { [Data(Description = "Data GPALGrid used to save results, could be created in another/sub-workflow", Submenu = "Grids")] IAllowWithHeaderOrFileActions WithGridToSave(IGPALGrid<string> filename); }
    public interface IAllowStartWorkflow { [Fluent(Description = "Start the workflow if no action is used to start the workflow. Perhaps your logic is in the CallIf handlers, so this will kick off finding the selector elements, thus calling Found/NotFound handlers")] IAllowBrowserActionOrAnySelector StartWorkflow(); }
    public interface IAllowDragAndDrop
    {
        [Fluent(Description = "Perform a drag-and-drop operation. OffsetX/Y is the grab point, DeltaX/DeltaY is the drop offset. Uses native drag events for the current automation engine (Selenium Actions, CDP mouse events, or OttoMagic in-page events); falls back to smoothed hardware mouse emulation for image-matched/remote-desktop elements")]
        IAllowBrowserActionOrAnySelector DragAndDrop(Selector selector = null);
        [Fluent(Description = "Perform a drag-and-drop operation while pressing a modifier key. OffsetX/Y is the grab point, DeltaX/DeltaY is the drop offset. Uses native drag events for the current automation engine (Selenium Actions, CDP mouse events, or OttoMagic in-page events); falls back to smoothed hardware mouse emulation for image-matched/remote-desktop elements")]
        IAllowBrowserActionOrAnySelector DragAndDrop(ModifierKeys modifierKeys);
    }
    public interface IAllowPrintDomElement
    {
        [Fluent(Description = "Print each element named by .WithSelector to a PDF of its own, or the page when no selector is set. Each file comes from GPALFile.Next")]
        IAllowBrowserActionOrAnySelector PrintToPDF(GPALFile gpalFile);
        [Fluent(Description = "Print orientation for PrintToPDF - use before calling PrintToPDF")]
        IAllowBrowserActionOrAnySelector WithPageOrientation(PageOrientation pageOrientation);
    }
    public interface IAllowCasting
    {
        [Fluent(Description = "Cast the current tab to the specified device")]
        IAllowBrowserActionOrAnySelector CastTab(string deviceNameOrId);
        [Fluent(Description = "Cast the desktop to the specified device")]
        IAllowBrowserActionOrAnySelector CastDesktop(string deviceNameOrId);
        [Fluent(Description = "Stop casting to device")]
        IAllowBrowserActionOrAnySelector StopCasting();
    }
    #endregion <Atoms>

    #region <DerivedInterfaces>
    // An emergent rule of thumb...
    // If the word "Or" appears more than once, invent an umbrella term.
    // And then derive stuff... from the derived stuff.
    // Because nobody wants to look at IAllowGoToOrGlobalWaitOnDocumentReadyOrClose.
    // .WithTokensFrom and .WithPages both feed .Fetch, so either can come first
    public interface IAllowWithPagesAndFetch
        : IAllowWithPages
        , IAllowFetch
    { }
    public interface IAllowGetGridAndFillInFrom
        : IAllowGetGrid
        , IAllowGetElements
        , IAllowFillInFrom
        , IAllowFetch
        , IAllowWithTokensFrom
        , IAllowStartWorkflow
    { }
    public interface IAllowAnySelector
        : IAllowWithSelector
        , IAllowInSelector
    { }
    public interface IAllowBrowserSettingsOrSelectorAction
        : IAllowBrowserSettings
        , IAllowSelectorAction
    { }
    /// <summary>
    /// Allow Browser Settings, Goto, WaitOnDocumentReady
    /// </summary>
    public interface IAllowBrowserSettingsOrGoTo
        : IAllowBrowserSettings
        , IAllowGoToOrGet
        , IAllowWithAllThatMatch
    { }
    /// <summary>
    /// Calls available after .WithCallFilter; CaptureCallTemplate needs a filter, so it is only here.
    /// </summary>
    public interface IAllowCallTemplate
        : IAllowBrowserActionOrAnySelector
    {
        [Action(Description = "Wait for the page to make a matching call and hand it back as a request ready to issue", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector CaptureCallTemplate(out GPALRequest template);
    }

    public interface IAllowBrowserActionOrAnySelector
        : IAllowBrowserAction
        , IAllowAnySelector
        , IAllowSelectorAction
        , IAllowWaitFor
        , IAllowWaitForWindow
        , IAllowNewTabOrWindow
        , IAllowToGPALObject<IBrowser>
        , IAllowWorkflow
        , IAllowStorageOptions
        , IAllowStorageFor
    { }
    public interface IAllowBrowserAction
        : IAllowGoToOrGet
        , IAllowClose
        , IAllowTabActions
        , IAllowSizeControl
        , IAllowWindowScrollBy
        , IAllowPageControl
        , IAllowExecuteJavaScript
        , IAllowInjectScript
        , IAllowGetWindowRectangle
        , IAllowDialogHandling
        , IAllowSiteMapOperations
        , IAllowCasting
        , IAllowFetch
        , IAllowWithTokensFrom
        , IAllowClicks
    { }
    public interface IAllowFetch
    {
        [Fluent(Description = "Issue an API request from inside the page, so it carries the session the browser already earned: its cookies, TLS fingerprint, header order and any anti-bot clearance. Honors .WithPages and .WithTokensFrom. The response bodies are handed back with .SaveTo(ref string)")]
        IAllowWithHeaderOrFileActions Fetch(GPALRequest request);
        [Action(Description = "Record what the page asks for, so an endpoint can be read off a site", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector CaptureCalls(bool capture = true);
        [Action(Description = "Record what the page asks for and hand back everything recorded so far", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector CaptureCalls(out List<GPALCall> calls);
        [Action(Description = "Forget everything recorded so far, so what comes next is what happens next", Submenu = "Calls")]
        IAllowBrowserActionOrAnySelector ClearCapturedCalls();
        [Settings(Description = "Only record calls whose url holds this", Submenu = "Calls")]
        IAllowCallTemplate WithCallFilter(string urlFragment);
    }
    public interface IAllowAllBrowserAndAllSelector
        : IAllowBrowserAction
        , IAllowAnySelector
        , IAllowAfterAnySelector
        , IAllowBrowserSettingsOrGoTo
    { }
    public interface IAllowSelectorAction
        : IAllowClicks
        , IAllowFillInFrom
        , IAllowFocus
        , IAllowHover
        , IAllowMoveTo
        , IAllowScrolling
        , IAllowWithGridToSave
        , IAllowDragAndDrop
        , IAllowPrintDomElement
        , IAllowHideElement
        , IAllowWithAttribute
        , IAllowSetAttributeValue
        , IAllowSetValueFromElement
    { }
    public interface IAllowAfterWaitFor
        : IAllowCallBack
        , IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
        , IAllowSelectorAction
        , IAllowAnySelector
        , IAllowClicks
        , IAllowWaitForWindow
        , IAllowFillInFrom
        , IAllowTabActions
        , IAllowBrowserAction
        , IAllowToGPALObject<IBrowser>
        , IAllowStartWorkflow
        , IAllowWithAllThatMatch
    { }
    public interface IAllowAfterImageSelector
        : IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
        , IAllowSelectorAction
        , IAllowWithHardware
        , IAllowWaitFor
        , IAllowCallBack
        , IAllowFillInFrom
    { }
    // NOTE: Add to BELOW LIST what is added here
    public interface IAllowAfterAnySelector
        : IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
        , IAllowGetElements
        , IAllowSelectorAction
        , IAllowAnySelector
        , IAllowWithAllThatMatch
        , IAllowWithHardware
        , IAllowWithJavaScript
        , IAllowWaitFor
        , IAllowCallBack
        , IAllowClicks
        , IAllowFillInFrom
        , IAllowStartWorkflow
        , IAllowToGPALObject<IBrowser>
    { }
    // NOTE: Add to this list what is added above
    public interface IAllowAfterAnySelectorExceptWithAll
        : IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
        , IAllowGetElements
        , IAllowSelectorAction
        , IAllowAnySelector
        , IAllowWithHardware
        , IAllowWithJavaScript
        , IAllowWaitFor
        , IAllowCallBack
        , IAllowClicks
        , IAllowFillInFrom
        , IAllowStartWorkflow
        , IAllowBrowserSettings
    { }
    public interface IAllowAfterWaitForAndSizeControl
        : IAllowAfterWaitFor
        , IAllowSizeControl
        , IAllowCallBack
    { }
    public interface IAllowWithNextPageButtonOrGetGrid
        : IAllowWithNextPageButton
        , IAllowGetGrid
    { }
    public interface IAllowWithPagesAndGridActions
        : IAllowWithPages
        , IAllowGetGrid
        , IAllowWithHardware
        , IAllowWithJavaScript
    { }
    public interface IAllowWithHeaderOrFileActions
        : IAllowWithHeader
        , IAllowFileActions
        , IAllowTabActions
        , IAllowClose
    //        , IAllowBrowserActionOrAnySelector
    { }
    #endregion <DerivedInterfaces>
    #region Workflow Interfaces
    public interface IAllowRun
    {
        [Workflow(Description = "Runs the workflows added to this browser, in the order they were added.")]
        IAllowBrowserActionOrAnySelector Run();
    }
    public interface IAllowWorkflowExecution : IAllowRun
    {
        [Workflow(Description = "Executes the workflow while the predicate returns true.")]
        IAllowBrowserActionOrAnySelector While(Func<Browser, bool> predicate);

        [Workflow(Description = "Executes the workflow until the specified selector finds at least one element.")]
        IAllowBrowserActionOrAnySelector Until(Selector selector);

        [Workflow(Description = "Executes the workflow until the predicate returns true.")]
        IAllowBrowserActionOrAnySelector Until(Func<Browser, bool> predicate);

        [Workflow(Description = "Sets the timeout in milliseconds for While or Until loops.")]
        IAllowWorkflowExecution WhileLoopTimeout([Milliseconds] int timeoutMs);

        [Workflow(Description = "Sets the maximum number of iterations for While or Until loops.")]
        IAllowWorkflowExecution WhileLoopMaxIterations(int maxIterations);
    }
    public interface IAllowScheduledWorkflow
    {
        [Workflow(Description = "Adds a workflow to be scheduled. It builds and owns whatever it drives.")]
        IAllowScheduledWorkflowExecution WithWorkflow(Action workflow);
        [Workflow(Description = "Adds several workflows to be scheduled, in order. Each builds and owns whatever it drives.")]
        IAllowScheduledWorkflowExecution WithWorkflows(IEnumerable<Action> workflows);
    }
    public interface IAllowScheduledWorkflowExecution : IAllowScheduledWorkflow
    {
        [Settings(Description = "How many workflows may be in flight at once. One, the default, runs them one after another.")]
        IAllowScheduledWorkflowExecution WithMaxAtOnce(int howMany);
        [Settings(Description = "The least time between two workflows starting. [250ms (default)] 0 starts them together.")]
        IAllowScheduledWorkflowExecution WithStaggerStart([Milliseconds] int delayInMs);
        [Settings(Description = "How long to wait for them all before giving up and reporting the unfinished ones as failed. [no limit (default)]")]
        IAllowScheduledWorkflowExecution WithTimeout([Milliseconds] int timeoutInMs);
        [Workflow(Description = "Runs every workflow added and waits for them all.")]
        IAllowScheduledWorkflowExecution Run();
        [Workflow(Description = "Runs every workflow added and hands back what happened to each.")]
        IAllowScheduledWorkflowExecution Run(out List<WorkflowRun> runs);
    }
    public interface IAllowWorkflow
    {
        [Workflow(Description = "Defines a workflow to be executed repeatedly based on While or Until conditions.")]
        IAllowWorkflowExecution WithWorkflow(Action<IBrowser> workflow);
    }
    #endregion Workflow Interfaces
    #endregion <BrowserInterfaces>
    #region <Element Assistant Interfaces>
    public interface IAllowElementSettingsAndActions
        : IAllowElementSetting
        , IAllowElementActions
    { }
    public interface IAllowElementActions
    {
        [Action(Description = "Left click the specified element, pressing modifier key (none [default]) while clicking.")]
        IAllowElementSettingsAndActions LeftClick(IGPALElement webElement);
        [Action(Description = "Left click the specified element.")]
        IAllowElementSettingsAndActions LeftClick();
        [Action(Description = "Left click the specified element, pressing modifier key (none [default]) while clicking.")]
        IAllowElementSettingsAndActions LeftClick(ModifierKeys modifierKeys);
        [Action(Description = "Left double click the specified element, pressing modifier key (none [default]) while clicking.")]
        IAllowElementSettingsAndActions LeftDoubleClick(IGPALElement webElement);
        [Action(Description = "M iddle click the found element.")]
        IAllowElementSettingsAndActions MiddleClick();
        [Action(Description = "Right click the specified element, pressing modifier key (none [default]) while clicking.")]
        IAllowElementSettingsAndActions RightClick(IGPALElement webElement);
        [Data(Description = "Fill in [overwrite] text from a string")]
        IAllowElementSettingsAndActions FillInFrom(string textToUse);
        [Data(Description = "Insert text (wherever the cursor is) from a string")]
        IAllowElementSettingsAndActions InsertFrom(string textToUse);
        [Data(Description = "Append to the end text from a string")]
        IAllowElementSettingsAndActions AppendFrom(string textToUse);
        [Data(Description = "Send a control character specified by the VKCode (GPAL has some defined)")]
        IAllowElementSettingsAndActions SendKey(byte VKCode);
        [Data(Description = "Send the specified string using the selector interaction type (selenium/javascript/hardware)")]
        IAllowElementSettingsAndActions SendString(string textToSend);
        [Data(Description = "Press the modifier key specified (Control/Shift/Alt). NOTE: Be careful with Shift and sending mixed case strings after pressing Shift. Mixed case strings will release the Shift key.")]
        IAllowElementSettingsAndActions PressModifierKey(ModifierKeys modifierKeys);
        [Data(Description = "Release the modifier key specified (Control/Shift/Alt) as previously pressed.")]
        IAllowElementSettingsAndActions ReleaseModifierKey(ModifierKeys modifierKeys);
        [Action(Description = "Scroll the specified element n pixels horizontally")]
        IAllowElementSettingsAndActions ScrollElementHorizontal(int scrollAmount);
        [Action(Description = "Scroll the specified element n pixels vertically")]
        IAllowElementSettingsAndActions ScrollElementVertical(int scrollAmount);
    }
    public interface IAllowElementSetting : IAllowToGPALObject<ElementAssistant>
    {
        [ElementAssitant(Description = "Declare the element to interact with using the ElementAssistant")]
        IAllowElementSettingsAndActions WithElement(IGPALElement GPALElement);
        IAllowElementSettingsAndActions WithJavaScript { [Settings(Description = "Use javascript injection to interact with this element")] get; }
        IAllowElementSettingsAndActions WithHardware { [Settings(Description = "Use hardwre emulation to interact with this element")] get; }
    }

    #endregion <\Element Interfaces>
    #region <File Interface>
    public interface IAllowFileActions
    {
        // One save/append syntax. Output format and delimiter come from the GPALFile: its extension
        // (.csv, .tsv/.txt, .xlsx, .json, .xml, .yaml, ...) or its explicit .WithDelimiter settings.
        [File(Description = "Save the retrieved grid. Output format and delimiter are determined by the GPALFile (its extension, or explicit file settings).")]
        IAllowBrowserActionOrAnySelector SaveTo(GPALFile file);
        [File(Description = "Append the retrieved grid. Output format and delimiter are determined by the GPALFile (its extension, or explicit file settings).")]
        IAllowBrowserActionOrAnySelector AppendTo(GPALFile file);
        [File(Description = "Hand back what .Fetch retrieved, as a JSON array holding one response body per page requested.")]
        IAllowBrowserActionOrAnySelector SaveTo(ref string data);
    }
    #endregion
    #region Graphics Interface
    public interface IAllowScreenshot
    {
        [GPAL(Description = "Capture a screenshot")]
        IAllowGraphicSettings CaptureScreen(object ApplicationOrBrowser = null);

    }
    // NOTE: TODO: might these be used in Convertor class?
    public interface IAllowGraphicSettings
    {
        [GPAL(Description = "Save to BASE64 string")]
        IAllowGraphicSettings ToBase64String(out string base64String);
        [Data(Description = "Returns the last captured screenshot as a Bitmap")]
        IAllowGraphicSettings ToBitmap(out Bitmap base64String);
        [File(Description = "Save to file (overwrite)")]
        IAllowGraphicSettings SaveTo(GPALFile File);

    }
    public interface IImageHelper : IAllowGraphicSettings, IAllowScreenshot, IAllowToGPALObject<IImageHelper>
    {

    }
    #endregion Graphics Interface
    #region MagicHelper
    public interface IMagicHelper
    {
        // CalculateHash, GetFullUrl, GetRandomStarWarsQuery and LaunchBrowser are static on MagicHelper, not in this
        // interface, since they need no browser.
        [Browser(Description = "Navigates browser history back", Submenu = "Navigation")]
        void Back();
        [Action(Description = "A screenshot of the visible part of the tab", Submenu = "Page")]
        string CaptureVisibleTab();
        string CheckNetworkIdle(int? maxConnections = null, int? pruneMs = null);
        [Browser(Description = "Closes a tab by URL or tab ID", Submenu = "Tabs")]
        TabTuple CloseTab(dynamic URLorTabId = null);
        bool DeleteStorage(WebsiteStorageType storageType, bool deleteAcrossOrigins = false, string domain = null, string path = null, string key = null, string storeName = null);
        [Browser(Description = "Navigates browser history forward", Submenu = "Navigation")]
        void Forward();
        [Browser(Description = "Sets browser to full screen", Submenu = "Windows")]
        void FullScreen();
        [Browser(Description = "Navigates to a URL", Submenu = "Navigation")]
        string Get(GPALUrl URL); // NOTE: CAVEAT: not sure this makes sense, goto will be the same action on the browser, how it is Launched makes it headless...
        [Data(Description = "Gets content and CSS for a elementId", Submenu = "Element")]
        string GetContentAndCss(string elementId);
        [Data(Description = "The computed styles of the element", Submenu = "Element")]
        Dictionary<string, object> GetCssAttributes(string elementId);
        [Data(Description = "The attributes of the element", Submenu = "Element")]
        Dictionary<string, object> GetDomAttributes(string elementId);
        [Data(Description = "The DOM properties of the element", Submenu = "Element")]
        Dictionary<string, object> GetDomProperties(string elementId);
        [Browser(Description = "Gets the current URL", Submenu = "Navigation")]
        string GetCurrentUrl();
        [Data(Description = "Gets the ready status NOTE: if no page is loaded, this will return null cause there is no content script", Submenu = "Page")]
        string GetReadyStatus();
        [Data(Description = "Gets the HTTP status of the active tab's page", Submenu = "Page")]
        int GetStatusCode();
        [Data(Description = "Gets the shadow root for an elementId", Submenu = "Find Elements")]
        GPALElement GetShadowRoot(string Css);
        string GetStorage(WebsiteStorageType storageType, string domain = null, string path = null, string key = null, string storeName = null);
        [Browser(Description = "Navigates to a URL", Submenu = "Navigation")]
        TabTuple GoTo(GPALUrl URL);
        [Action(Description = "Navigates to a tab by URL or tab ID", Submenu = "Tabs and Windows")]
        TabTuple GotoTab(dynamic URLorTabId);
        [Browser(Description = "Returns the browser window inner/outer height/widge { y: window.innerHeight, x: window.width, height: window.outerHeight, width: window.outerWidth }", Submenu = "Windows")]
        System.Drawing.Rectangle GetWindowRectangle();
        [Browser(Description = "Switches context to an element NOTE: InFrame and InElement are identical in implementation", Submenu = "Frames")]
        void SwitchToElement(string elementId);    // in element and in iframe are the same thing
        [Settings(Description = "Switches context to iframe NOTE: InFrame and InElement are identical in implementation")]
        void InFrame(string elementId);    // in element and in iframe are the same thing
        [Browser(Description = "Minimizes the browser window", Submenu = "Windows")]
        void Minimize();
        [Browser(Description = "Maximizes the browser window", Submenu = "Windows")]
        void Maximize();
        [Action(Description = "Scrolls an element into view by ID", Submenu = "Mouse")]
        void MoveTo(string elementiId);
        [Browser(Description = "Restores the browser window to normal", Submenu = "Windows")]
        void Normal();
        [Action(Description = "Opens a new tab with optional URL", Submenu = "Tabs and Windows")]
        TabTuple NewTab(GPALUrl URL = null);
        [Browser(Description = "Switches to the next tab", Submenu = "Tabs")]
        TabTuple NextTab();
        [Browser(Description = "How many tabs the browser actually has open, which is not the same as how many GPAL opened", Submenu = "Tabs")]
        int TabCount();
        [Action(Description = "Overrides the referrer URL", Submenu = "Referrer and User Agent")]
        void OverrideReferrer(string referrer);
        [Action(Description = "Overrides the browser's user agent string", Submenu = "Referrer and User Agent")]
        void SetUserAgent(string userAgent);
        [Scroll(Description = "Scrolls the page down")]
        void PageDown();
        [Scroll(Description = "Scrolls to the page end")]
        void PageEnd();
        [Scroll(Description = "Scrolls to the page top")]
        void PageTop();
        [Scroll(Description = "Scrolls the page up")]
        void PageUp();
        [Data(Description = "Press the specified modifier keys", Submenu = "Keyboard")]
        void PressModifierKey(ModifierKeys modifierKeys);
        [Browser(Description = "Switches to the previous tab", Submenu = "Tabs")]
        TabTuple PreviousTab();
        [Browser(Description = "Refreshes the current page", Submenu = "Navigation")]
        void Refresh();
        [Data(Description = "Press the specified modifier keys", Submenu = "Keyboard")]
        void ReleaseModifierKey(ModifierKeys modifierKeys);
        [Browser(Description = "Restores the browser window", Submenu = "Windows")]
        void Restore();
        [Browser(Description = "Scrolls the window horizontally by pixels", Submenu = "Scrolling")]
        void ScrollWindowHorizontal(int pixels);
        [Browser(Description = "Scrolls the window vertically by pixels", Submenu = "Scrolling")]
        void ScrollWindowVertical(int pixels);
        void SetRange(string elementId, int rangeValue);
        bool SetStorage(WebsiteStorageType storageType, string data, string domain = null, string path = null, string key = null, string storeName = null);
        [Action(Description = "Overrides referrer stealthily", Submenu = "Referrer and User Agent")]
        void SetGoogleReferrer();
        [Settings(Description = "Names the file the next download should land under")]
        void WithDownloadFile(string downloadPath);
        [Action(Description = "Focuses an element by ID", Submenu = "Mouse")]
        void Focus(string elementId);
        [Action(Description = "Fires a change event on an element by ID", Submenu = "Input")]
        void FireChangeEvent(string elementId);
        [Scroll(Description = "Scrolls an element into view by ID")]
        void ScrollIntoView(string elementId);
        void ScrollElement(string elementId, int hPixels, int vPixels);
        [Data(Description = "Gets the attribute hash of an element", Submenu = "Element")]
        string GetElementAttributeHash(string elementId);
        [Action(Description = "Get the html source of the current page", Submenu = "Page")]
        string GetPageSource();
        [Data(Description = "Gets the parent node of an element by ID", Submenu = "Find Elements")]
        GPALElement GetParentNode(string elementId);
        string GetAttribute(string elementId, string attribute);
        void SetAttribute(string elementId, string attribute, string value);
        void SetValueFromElement(string srcSelector, string destElementId);
        [Action(Description = "Performs a left click on an element by ID", Submenu = "Clicks")]
        void LeftClick(string elementId);
        [Action(Description = "Performs a left double click on an element by ID", Submenu = "Clicks")]
        void LeftDoubleClick(string elementId);
        [Action(Description = "Performs a left click on an element by ID", Submenu = "Clicks")]
        void MiddleClick(string elementId);
        [Action(Description = "Performs a right click on an element by ID", Submenu = "Clicks")]
        void RightClick(string elementId);
        void SelectClick(string elementId, dynamic indexOrValue);
        [Data(Description = "Checks if an element is visible in viewport by ID", Submenu = "Element")]
        bool IsVisibleInViewport(string elementId);
        [Scroll(Description = "Checks if the page is at the end")]
        bool IsEndOfPage();
        [Action(Description = "Hovers over an element by ID", Submenu = "Mouse")]
        void Hover(string elementId);
        void DragAndDrop(string elementId, int deltaX, int deltaY, int offsetX = 0, int offsetY = 0);
        [Data(Description = "Gets the window inner height", Submenu = "Window")]
        int WindowInnerHeight();
        [Data(Description = "Gets the window inner width", Submenu = "Window")]
        int WindowInnerWidth();
        [Data(Description = "Gets the window outer height", Submenu = "Window")]
        int WindowOuterHeight();
        [Data(Description = "Gets the window outer width", Submenu = "Window")]
        int WindowOuterWidth();
        string Fetch(string url, string method = null, string body = null, string contentType = null, string[] headers = null, bool asBytes = false);
        [Data(Description = "Gets the window page offset X", Submenu = "Window")]
        int WindowPageOffsetX();
        [Data(Description = "Gets the window page offset Y", Submenu = "Window")]
        int WindowPageOffsetY();
        [Data(Description = "Where the element is in the viewport, and its size", Submenu = "Element")]
        System.Drawing.Rectangle GetBoundingClientRect(string elementId);
        [Action(Description = "Hides an element by ID", Submenu = "Input")]
        void HideElement(string elementId);
        [Browser(Description = "Registers a script to run on every new document load", Submenu = "Scripts")]
        void InjectScript(string script);
        [Browser(Description = "Removes all scripts previously registered via InjectScript", Submenu = "Scripts")]
        void ClearInjectedScripts();
        [Data(Description = "Whether the element can be clicked", Submenu = "Element")]
        bool IsClickAble(string elementId);
        [Action(Description = "Clears the referrer header", Submenu = "Referrer and User Agent")]
        void ClearReferrer();
        [Data(Description = "Evaluates an XPath expression to locate a single element", Submenu = "Find Elements")]
        GPALElement Evaluate(string xpath);
        [Data(Description = "Evaluates an XPath expression to locate multiple elements", Submenu = "Find Elements")]
        List<GPALElement> EvaluateAll(string xpath);
        [Data(Description = "Evaluates an XPath expression to locate a single element", Submenu = "Find Elements")]
        GPALElement EvaluatePersistent(string xpath);
        [Data(Description = "Evaluates an XPath expression to locate multiple elements", Submenu = "Find Elements")]
        List<GPALElement> EvaluateAllPersistent(string xpath);
        [Data(Description = "Gets the browser's preferred languages, most preferred first", Submenu = "Page")]
        string GetLanguages();
        [Data(Description = "Gets the user agent", Submenu = "Page")]
        string GetUserAgent();
        [Data(Description = "Get select menu option items", Submenu = "Element")]
        List<GPALElement> GetOptions(string elementId);
        void FillInAppend(string elementId, string text);
        void FillInInsert(string elementId, string text);
        void FillInOverwrite(string elementId, string text);
        void LeftClickAndDownload(string elementId, string filenameAndPath);
        void DownloadUrl(string url, string filenameAndPath);
        void LeftClickAndUpload(string elementId, string filenameAndPath);
        void LeftClickAndUpload(string elementId, GPALFile filenamesAndPaths);
        [Data(Description = "Queries a single element using a CSS selector", Submenu = "Find Elements")]
        GPALElement QueryPersistentSelector(string css);
        [Data(Description = "Queries multiple elements using a CSS selector", Submenu = "Find Elements")]
        List<GPALElement> QueryPersistentSelectors(string css);
        [Data(Description = "Queries a single element using a CSS selector", Submenu = "Find Elements")]
        GPALElement QuerySelector(string css);
        [Data(Description = "Queries multiple elements using a CSS selector", Submenu = "Find Elements")]
        List<GPALElement> QuerySelectors(string css);
        void ScrollWindow(int hPixels, int vPixels);
        [Data(Description = "Sends a single key", Submenu = "Keyboard")]
        void SendKey(byte vkcode);
        void SendString(string text, int delayMs = 0);
        [Browser(Description = "Switches to the default content", Submenu = "Frames")]
        void SwitchToDefaultContent();
        // void SwitchToFrame(string elementId);    // there is no switch to frame, ottomagic runs in all frames
        [Browser(Description = "Switches to a shadow root by element ID", Submenu = "Frames")]
        void SwitchToShadowRoot(string elementId);
        [Data(Description = "Gets browser settings", Submenu = "Recorder")]
        string GetBrowserSettings();
        [Data(Description = "Gets GPAL settings", Submenu = "Recorder")]
        string GetGpalSettings();
        [Data(Description = "Gets general settings", Submenu = "Recorder")]
        string GetSettings();
        [Data(Description = "Gets the workflow", Submenu = "Recorder")]
        string GetWorkflow();
        // Process LaunchBrowser(BrowserSettings browserSettings, string URL = "https://google.com", List<string> additionalArguments = null);
        [Browser(Description = "Brings the browser window to the top", Submenu = "Windows")]
        bool TopBrowser(Process process);
        [Action(Description = "Open a new window on the url", Submenu = "Tabs and Windows")]
        public WindowTuple OpenWindow(GPALUrl url = null);
        [Browser(Description = "Switch to a window", Submenu = "Windows")]
        public WindowTuple GoToWindow(dynamic urlOrId);
        [Browser(Description = "Close a window", Submenu = "Windows")]
        public WindowTuple CloseWindow(dynamic urlOrId);
        [Browser(Description = "Move to the next window (to the right)", Submenu = "Windows")]
        public WindowTuple NextWindow();
        [Browser(Description = "Move to the previous window (to the left)", Submenu = "Windows")]
        public WindowTuple PreviousWindow();
        [Browser(Description = "The window the browser is on", Submenu = "Windows")]
        public WindowTuple GetCurrentWindow();
        [Data(Description = "Gets the window screen left position", Submenu = "Window")]
        int WindowScreenLeft();
        [Data(Description = "Gets the window screen top position", Submenu = "Window")]
        int WindowScreenTop();
    }
    #endregion MagicHelper
    #region Google Sheets
    public interface IAllowSpreadsheetSelection : IAllowToGPALObject<IGoogleSheets>
    {
        [Settings(Description = "The spreadsheet to work on, by name or id")]
        IAllowSheetSelection WithSpreadsheet(string spreadsheetIdOrTitle);
    }

    public interface IAllowSheetSelection : IAllowDataOperations, IAllowSheetOperations, IAllowToGPALObject<IGoogleSheets>
    {
        [Settings(Description = "The sheet to work on, by name or id")]
        IAllowInDataOperations WithSheet(object sheetNameOrId);
        [Action(Description = "The names of the sheets in the spreadsheet")]
        IAllowSheetSelection ListSheets(out IEnumerable<(string name, int sheetId)> sheets);
        [Action(Description = "Rename the spreadsheet")]
        IAllowSheetSelection SetSpreadsheetTitle(string title);
    }

    public interface IAllowInDataOperations : IAllowSheetOperations, IAllowWithTriggerFunction
    {
        [Action(Description = "Rename the sheet")]
        IAllowSheetSelection SetSheetName(string sheetName);
        [Settings(Description = "The range to read, such as A1:C10")]
        IAllowDataOperations WithReadRange(string range);
        [Settings(Description = "The rows to write")]
        IAllowDataOperations WithData(IGPALGrid<string> data);
        [Settings(Description = "What kind of formatting to apply")]
        IAllowFormatSettings WithFormatType(FormatType formatType);
    }

    public interface IAllowDataOperations : IAllowInsertSettings, IAllowSheetOperations
    {
        [Action(Description = "Add the data to the end of what the cell holds")]
        IAllowDataOperations AppendToCell(string cell);
        [Action(Description = "Add the data to the start of what the cell holds")]
        IAllowDataOperations PrependToCell(string cell);
        [Settings(Description = "The range to write to, such as A1:C10")]
        IAllowSheetOperations WithWriteRange(string range);
        [Action(Description = "The sum of the range")]
        IAllowDataOperations CalculateSum(out string result);
        [Action(Description = "How many cells in the range hold a value")]
        IAllowDataOperations CalculateCount(out string result);
    }

    public interface IAllowSheetOperations : IAllowGoogleSheetsSaveTo
    {
        [Action(Description = "Delete the sheet")]
        IAllowSheetSelection DeleteSheet(object sheetNameOrId);
        [Action(Description = "Write the data to the sheet")]
        IAllowDataOperations WriteToSheet(object sheetNameOrId);
    }

    public interface IAllowInsertSettings
    {
        [Settings(Description = "Where in the cell text to insert")]
        IAllowInsertSettings WithInsertPosition(int position);
        [Settings(Description = "Text to put between what is inserted and what was there")]
        IAllowInsertSettings WithInsertSeparator(string separator);
        [Action(Description = "Insert the data into the cell")]
        IAllowDataOperations InsertAtCell(string cell);
    }

    public interface IAllowFormatSettings
    {
        [Settings(Description = "The value for the chosen format")]
        IAllowFormatRange WithFormatValue(object formatValue);
        [Settings(Description = "How the text is aligned")]
        IAllowFormatRange WithAlignmentFormatValue(HorizontalAlignmentType alignment);
        [Settings(Description = "How the text is aligned")]
        IAllowFormatRange WithAlignmentFormatValue(VerticalAlignmentType alignment);
        [Settings(Description = "How numbers are shown")]
        IAllowFormatRange WithNumberFormatValue(NumberFormatType numberFormat);
        [Settings(Description = "How far the text is turned")]
        IAllowFormatRange WithTextRotationFormatValue(TextRotationType rotation);
        [Settings(Description = "The color to apply")]
        IAllowFormatRange WithColorFormatValue(Color color);
    }
    public interface IAllowFormatRange
    {
        [Action(Description = "Apply the format to the range")]
        IAllowInDataOperations FormatRange(string range);
    }
    public interface IAllowWithTriggerFunction
    {
        [Settings(Description = "The Apps Script function the trigger runs")]
        IAllowTriggerSettings WithTriggerFunction(string functionName);

    }
    public interface IAllowTriggerSettings : IAllowWithTriggerFunction, IAllowToGPALObject<IGoogleSheets>
    {
        [Settings(Description = "How often a time trigger runs")]
        IAllowTriggerSettings WithTriggerSchedule(TriggerScheduleType schedule);
        [Action(Description = "Create the trigger")]
        IAllowDataOperations CreateTrigger(GoogleTriggerType triggerType);
    }
    public interface IAllowScriptOperations : IAllowToGPALObject<IGoogleSheets>
    {
        [Settings(Description = "The Apps Script project to work on")]
        IAllowScriptOperations WithProjectId(string projectId);
        [Settings(Description = "The name of the script file in the project")]
        IAllowScriptOperations WithScriptName(string scriptName);
        [Settings(Description = "Who may call the web app")]
        IAllowScriptOperations WithScriptAccess(ScriptAccessType access);
        [Settings(Description = "Whose account the web app runs as")]
        IAllowScriptOperations WithExecuteAs(ExecuteAsType executeAs);
        [Settings(Description = "The description of the deployment")]
        IAllowScriptOperations WithDeploymentDescription(string description);
        //// post-v1: pinning a script version. DeployScriptAsWebApp overwrites this from the create response
        //IAllowScriptOperations WithVersionNumber(int versionNumber);
        [Action(Description = "Upload the script to the project")]
        IAllowDeployWebApp UploadScript([JavaScript] string scriptContent);
        [Action(Description = "Upload the script to the project")]
        IAllowDeployWebApp UploadScript(GPALFile scriptFile);
    }
    public interface IAllowDeployWebApp : IAllowDataOperations
    {
        [Action(Description = "Deploy the uploaded script as a web app and hand back its url")]
        IAllowDataOperations DeployScriptAsWebApp(out string webAppUrl);
    }
    public interface IAllowGoogleSheetsSaveTo
    {
        [Action(Description = "Save what was read")]
        IAllowSheetSelection SaveTo(out IGPALGrid<string> gPalGrid);
        [Action(Description = "Save what was read")]
        IAllowSheetSelection SaveTo(GPALFile gPALFile);
    }
    public interface IGoogleSheets : IAllowSpreadsheetSelection, IAllowSheetSelection, IAllowSheetOperations, IAllowDataOperations, IAllowInDataOperations, IAllowDeployWebApp
        , IAllowFormatSettings, IAllowInsertSettings, IAllowTriggerSettings, IAllowScriptOperations, IAllowFormatRange, IAllowToGPALObject<IGoogleSheets>
    {
        [Settings(Description = "The Google account to use Sheets as")]
        IAllowSpreadsheetSelection WithCredentials(ICredentials credentials);
        [Settings(Description = "Drive sheets through an Apps Script web app deployed by hand, instead of the Sheets API. Takes the deployment url and needs no credentials.")]
        IAllowSpreadsheetSelection WithWebAppUrl([URL] string webAppUrl);
    }
    #endregion Google Sheets
    #region PuppeteerClient / based entirely on restclient
    public interface IAllowPuppeteerEndpoint
    {
    }
    public interface IAllowPuppeteerCommunicator
    {
        [Settings(Description = "Sets the PuppeteerCommunicator used to send commands to the puppeteer/CDP server", Submenu = "Request")]
        IAllowPuppeteerEndpointDetails WithPuppeteerCommunicator(PuppeteerCommunicator communicator);
    }
    public interface IAllowPuppeteerEndpointDetails : IAllowPuppeteerExecution, IAllowPuppeteerParameters, IAllowToGPALObject<IPuppeteerClient>
    {
        [Settings(Description = "Sets a custom endpoint path to call directly, bypassing the built-in ApiEndpoint enum", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithEndpoint(string endpoint);
        [Settings(Description = "Sets the endpoint to call, resolved from the given ApiEndpoint value", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithEndpoint(ApiEndpoint endpoint);
        [Settings(Description = "Sets the name under which this execution's result will be stored, for later retrieval via the WithXFromResult methods", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithResultName(string name);
        [Settings(Description = "Save the result to this file", Submenu = "Request")]
        IAllowPuppeteerEndpointDetails WithSaveFile(GPALFile file);
        [Settings(Description = "Sets the display name for this client", Submenu = "Request")]
        IAllowPuppeteerEndpointDetails WithName(string name);
    }

    public interface IAllowPuppeteerParametersOrExecution : IAllowPuppeteerParameters, IAllowPuppeteerExecution, IAllowPuppeteerScrollElement, IAllowPuppeteerCheckNetworkIdleOrExecution
    {
        [Settings(Description = "Sets the url parameter for the request", Submenu = "Navigation")]
        IAllowPuppeteerParametersOrExecution WithUrl([URL] string url);
        [Data(Description = "Sets the url parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithUrlFromResult([Index] int resultIndex);
        [Data(Description = "Sets the url parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithUrlFromResult(string name);
        [Settings(Description = "Sets the tab id parameter for the request", Submenu = "Navigation")]
        IAllowPuppeteerParametersOrExecution WithTabId(int tabId);
        [Data(Description = "Sets the tab id parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithTabIdFromResult([Index] int resultIndex);
        [Data(Description = "Sets the tab id parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithTabIdFromResult(string name);
        [Settings(Description = "Sets the window id parameter for the request", Submenu = "Navigation")]
        IAllowPuppeteerParametersOrExecution WithWindowId(int windowId);
        [Data(Description = "Sets the window id parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithWindowIdFromResult([Index] int resultIndex);
        [Data(Description = "Sets the window id parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithWindowIdFromResult(string name);
        [Settings(Description = "Sets the key parameter for the request", Submenu = "Storage")]
        IAllowPuppeteerParametersOrExecution WithKey(string key);
        [Data(Description = "Sets the key parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithKeyFromResult([Index] int resultIndex);
        [Data(Description = "Sets the key parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithKeyFromResult(string name);
        [Settings(Description = "Sets the css selector parameter for the request", Submenu = "Element")]
        IAllowPuppeteerParametersOrExecution WithCss([Css] string css);
        [Data(Description = "Sets the css selector parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithCssFromResult([Index] int resultIndex);
        [Data(Description = "Sets the css selector parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithCssFromResult(string name);
        [Settings(Description = "Sets the gpalElements parameter, identifying the elements the request should act on", Submenu = "Element")]
        IAllowPuppeteerParametersOrExecution WithElements(List<GPALElement> gpalElement);
        [Data(Description = "Sets the elementId parameter from the result at the given index of a previous execution, then calls WithElementId", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithElementFromResult([Index] int resultIndex);
        [Data(Description = "Sets the elementId parameter from the named result of a previous execution, then calls WithElementId", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithElementFromResult(string name);
        [Settings(Description = "Sets the xpath parameter for the request", Submenu = "Element")]
        IAllowPuppeteerParametersOrExecution WithXPath([XPath] string xpath);
        [Data(Description = "Sets the xpath parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithXPathFromResult([Index] int resultIndex);
        [Data(Description = "Sets the xpath parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithXPathFromResult(string name);
        [Settings(Description = "Sets the pixels parameter for the request", Submenu = "Drag and Scroll")]
        IAllowPuppeteerParametersOrExecution WithPixels([Pixels] int pixels);
        [Data(Description = "Sets the pixels parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithPixelsFromResult([Index] int resultIndex);
        [Data(Description = "Sets the pixels parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithPixelsFromResult(string name);
        [Settings(Description = "Sets the referrer parameter for the request", Submenu = "Navigation")]
        IAllowPuppeteerParametersOrExecution WithReferrer(string referrer);
        [Data(Description = "Sets the referrer parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithReferrerFromResult([Index] int resultIndex);
        [Data(Description = "Sets the referrer parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithReferrerFromResult(string name);
        [Settings(Description = "Sets the text parameter for the request", Submenu = "Element")]
        IAllowPuppeteerExecution WithText(string text);
        [Data(Description = "Sets the text parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerExecution WithTextFromResult([Index] int resultIndex);
        [Data(Description = "Sets the text parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerExecution WithTextFromResult(string name);
        [Action(Description = "Where a file the browser downloads should end up")]
        IAllowPuppeteerParametersOrExecution DownloadTo(GPALFile file);
        [Action(Description = "Sets the upload path parameter(s) so a subsequent action uploads the given file(s)")]
        IAllowPuppeteerParametersOrExecution UploadFrom(GPALFile file);
        [Settings(Description = "Sets the elementId parameter for the request, resolving CSS selectors to an element handle if needed", Submenu = "Element")]
        IAllowPuppeteerParametersOrExecution WithElementId(string elementId);
        [Data(Description = "Sets the elementId parameter from the result at the given index of a previous execution, then calls WithElementId", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithElementIdFromResult([Index] int resultIndex);
        [Data(Description = "Sets the elementId parameter from the named result of a previous execution, then calls WithElementId", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithElementIdFromResult(string name);
        [Action(Description = "Sets the value of the destination element identified by destSelector from the source element identified by the preceding WithCss or WithElementId call")]
        IAllowPuppeteerExecution SetValueFrom([Css] string destSelector);
        [Data(Description = "Sets the horizontal pixels parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithHPixelsFromResult([Index] int resultIndex);
        [Data(Description = "Sets the horizontal pixels parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithHPixelsFromResult(string name);
        [Data(Description = "Sets the vertical pixels parameter from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithVPixelsFromResult([Index] int resultIndex);
        [Data(Description = "Sets the vertical pixels parameter from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithVPixelsFromResult(string name);
        [Settings(Description = "Sets the modifier keys parameter for the request", Submenu = "Element")]
        IAllowPuppeteerParametersOrExecution WithModifiers(ModifierKeys modifierKeys);
        [Settings(Description = "Sets the session token parameter for the request", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithSessionToken(string sessionToken);
        [Settings(Description = "Sets the CSS parameter", Submenu = "Request")]
        IAllowPuppeteerExecution WithDeviceName(string deviceNameOrId);
    }
    public interface IAllowPuppeteerExecution : IAllowPuppeteerActions
    {
        [Fluent(Description = "Synchronously executes the configured request and returns the result as a string")]
        string Execute();
        [Fluent(Description = "Synchronously executes the configured request and returns the result as a string")]
        T Execute<T>();
        // AndThen runs the request and returns the full IPuppeteerClient, as RESTClient.AndThen does, so the next
        // call can name its result.
        [Fluent(Description = "Executes the configured request, then returns this client so further fluent calls can configure the next request")]
        IPuppeteerClient AndThen();
        [Fluent(Description = "Executes the configured request, then returns this client so further fluent calls can configure the next request")]
        IPuppeteerClient AndThen<T>();
        [Action(Description = "Asynchronously executes the configured request and returns the result as a string")]
        Task<string> ExecuteAsync();
        [Action(Description = "Asynchronously executes the configured request and returns the result as a string")]
        Task<T> ExecuteAsync<T>();
        [Data(Description = "Everything stored under a name so far, in the order it was stored")]
        ResultCollection GetExecutionResults();
        [Data(Description = "Every result stored under name, in the order it was stored, so a loop that ran five times answers with five")]
        IReadOnlyList<object> GetExecutionResults(string name);
        [Action(Description = "Empties what WithResultName has stored, so GetExecutionResults answers about what has happened since rather than everything since the client was made")]
        IAllowPuppeteerExecution ClearResults();
        IAllowPuppeteerExecution WithHeader(string name, string value);
    }

    public interface IAllowPuppeteerParameters : IAllowPuppeteerExecution
    {
        [Settings(Description = "Sets the request parameters as an ordinal array", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithParameters(params object[] parameters);
        [Settings(Description = "Sets the request parameters from an object with named fields", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithParameters(object parameters);
        [Data(Description = "Sets the request parameters from the result at the given index of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithParametersFromResult([Index] int resultIndex);
        [Data(Description = "Sets the request parameters from the named result of a previous execution", Submenu = "From Result")]
        IAllowPuppeteerParametersOrExecution WithParametersFromResult(string name);
        [Settings(Description = "Sets the content encoding used for requests sent to the puppeteer communicator", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithEncoding(Enums.ContentEncoding encoding);
    }

    public interface IAllowPuppeteerWorkflow
    {
        [Workflow(Description = "Adds a workflow step that invokes the given action against this client")]
        IAllowPuppeteerWorkflow WithWorkflow(Action<PuppeteerClient> workflow);
        [Workflow(Description = "Empties the declared workflow steps so the next loop runs a new set.")]
        IAllowPuppeteerWorkflow ClearWorkflows();
        [Workflow(Description = "Sets the timeout in milliseconds for While or Until loops.")]
        IAllowPuppeteerWorkflow WhileLoopTimeout([Milliseconds] int timeoutMs);

        [Workflow(Description = "Sets the maximum number of iterations for While or Until loops.")]
        IAllowPuppeteerWorkflow WhileLoopMaxIterations(int maxIterations);
        [Workflow(Description = "Repeat the workflow while the condition holds")]
        IAllowPuppeteerParametersOrExecution While(Func<bool> condition);
        [Workflow(Description = "Configures execution to repeat until the given condition returns true, subject to WhileLoopTimeout/WhileLoopMaxIterations")]
        IAllowPuppeteerParametersOrExecution Until(Func<bool> condition);
    }

    public interface IAllowPuppeteerCheckNetworkIdleOrExecution : IAllowPuppeteerExecution
    {
        [Settings(Description = "Sets the prune interval, in milliseconds, used by CheckNetworkIdle", Submenu = "Network Idle")]
        IAllowPuppeteerCheckNetworkIdleOrExecution WithPruneMs([Milliseconds] int pruneMs);
        [Settings(Description = "Sets the maximum number of in-flight connections allowed before the network is considered busy, used by CheckNetworkIdle", Submenu = "Network Idle")]
        IAllowPuppeteerCheckNetworkIdleOrExecution WithMaxConnections(int maxConnections);
    }

    public interface IAllowPuppeteerScrollElement : IAllowPuppeteerExecution
    {
        [Settings(Description = "Sets the horizontal pixels parameter for the request", Submenu = "Drag and Scroll")]
        IAllowPuppeteerScrollElement WithHPixels([Pixels] int hPixels);
        [Settings(Description = "Sets the vertical pixels parameter for the request", Submenu = "Drag and Scroll")]
        IAllowPuppeteerScrollElement WithVPixels([Pixels] int vPixels);
    }

    public interface IAllowPuppeteerDragAndDrop : IAllowPuppeteerExecution
    {
        [Settings(Description = "Sets the horizontal drag delta parameter for a drag and drop", Submenu = "Drag and Scroll")]
        IAllowPuppeteerDragAndDrop WithDeltaX([Pixels] int deltaX);
        [Settings(Description = "Sets the vertical drag delta parameter for a drag and drop", Submenu = "Drag and Scroll")]
        IAllowPuppeteerDragAndDrop WithDeltaY([Pixels] int deltaY);
        [Settings(Description = "Sets the horizontal grab offset within the element for a drag and drop", Submenu = "Drag and Scroll")]
        IAllowPuppeteerDragAndDrop WithOffsetX([Pixels] int offsetX);
        [Settings(Description = "Sets the vertical grab offset within the element for a drag and drop", Submenu = "Drag and Scroll")]
        IAllowPuppeteerDragAndDrop WithOffsetY([Pixels] int offsetY);
    }

    public interface IAllowPuppeteerAttribute : IAllowPuppeteerExecution
    {
        [Data(Description = "Sets the attribute name parameter for SetAttribute/GetAttribute requests")]
        IAllowPuppeteerAttribute WithAttribute(string attribute);
        [Settings(Description = "Sets the attribute value parameter for SetAttribute requests", Submenu = "Element")]
        IAllowPuppeteerAttribute WithValue(string value);
    }

    public interface IAllowPuppeteerSelectOptions
    {
        [Settings(Description = "Set the value to choose from the Select menu", Submenu = "Element")]
        IAllowPuppeteerExecution WithSelectValue(string value);
        [Settings(Description = "Sets the index of the option to choose from a Select menu", Submenu = "Element")]
        IAllowPuppeteerExecution WithSelectIndex([Index] int index);
    }
    public interface IAllowPuppeteerInputText
    {
        [Settings(Description = "Sets the text parameter for the request")]
        IAllowPuppeteerExecution WithText(string text);
    }
    public interface IAllowPuppeteerStorageOptions : IAllowPuppeteerExecution
    {
        [Browser(Description = "Sets the storage domain parameter for storage operations")]
        IAllowPuppeteerStorageOptions WithStorageDomain(string domain);
        [Browser(Description = "Sets the storage path parameter for storage operations")]
        IAllowPuppeteerStorageOptions WithStoragePath(string path);
        [Browser(Description = "Sets the storage key parameter for storage operations")]
        IAllowPuppeteerStorageOptions WithStorageKey(string key);
        [Browser(Description = "Sets the storage store name parameter for IndexedDB storage operations")]
        IAllowPuppeteerStorageOptions WithStorageStoreName(string storeName);
        [Browser(Description = "Sets the storage data parameter for storage operations")]
        IAllowPuppeteerStorageOptions WithStorageData(string data);
        [Settings(Description = "Sets whether storage should be deleted across all origins, not just the current one", Submenu = "Storage")]
        IAllowPuppeteerStorageOptions WithDeleteAcrossOrigins(bool trueFalse);
    }

    public interface IAllowPuppeteerRangeValue
    {
        [Settings(Description = "Sets the range value parameter for a SetRange request", Submenu = "Element")]
        IAllowPuppeteerExecution WithRangeValue(int rangeValue);
    }

    public interface IAllowPuppeteerActions
    {
        [Browser(Description = "Configure this client to navigate the browser back one page in history", Submenu = "Navigation")]
        IAllowPuppeteerExecution Back();
        [Action(Description = "Configure this client to capture a screenshot of the visible tab in the given image format", Submenu = "Page")]
        IAllowPuppeteerExecution CaptureVisibleTab(ImageFormat imageFormat = ImageFormat.JPEG);
        [Fluent(Description = "Cast the desktop to the specified device name")]
        IAllowPuppeteerExecution CastDesktop(string deviceNameOrId);
        [Fluent(Description = "Cast the current tab to the specified device name")]
        IAllowPuppeteerExecution CastTab(string deviceNameOrId);
        [Browser(Description = "Configure this client to close the tab with the given URL, or the current tab if none is given", Submenu = "Tabs")]
        IAllowPuppeteerExecution CloseTab(GPALUrl url = null);
        [Browser(Description = "Configure this client to close the tab with the given tab id", Submenu = "Tabs")]
        IAllowPuppeteerExecution CloseTab(int tabId);
        [Browser(Description = "Configure this client to close the window containing the given URL, or the current window if none is given", Submenu = "Windows")]
        IAllowPuppeteerExecution CloseWindow(GPALUrl url = null);
        [Browser(Description = "Configure this client to close the window containing the tab with the given tab id", Submenu = "Windows")]
        IAllowPuppeteerExecution CloseWindow(int tabId);
        [Action(Description = "Configure this client to delete the given type of website storage", Submenu = "Storage")]
        IAllowPuppeteerStorageOptions DeleteStorage(WebsiteStorageType storageType);
        [Action(Description = "Configure this client to execute the given JavaScript in the page", Submenu = "Page")]
        IAllowPuppeteerParameters ExecuteJavaScript([JavaScript] string script); // Removed params version as noted
        [Fluent(Description = "Issue an API request from inside the page, so it carries the session the browser already earned: its cookies, TLS fingerprint, header order and any anti-bot clearance. A relative url resolves against wherever the browser currently is")]
        IAllowPuppeteerParametersOrExecution Fetch([URL] string url);
        [Settings(Description = "HTTP method for the request", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithVerb(string method);
        [Settings(Description = "Body to send with the request", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithBody(string body);
        [Settings(Description = "Content type of the body", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithContentType(string contentType);
        [Settings(Description = "Extra request headers, flattened to name, value, name, value", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithHeaders(string[] headers);
        [Settings(Description = "True to bring the response body back base64 encoded, which is what a file needs", Submenu = "Request")]
        IAllowPuppeteerParametersOrExecution WithBytes(bool asBytes);
        /// <summary>
        /// Sets the element's value directly (fast path) - does NOT honor <see cref="GPAL.TypingDelay"/>.
        /// <paramref name="delayMs"/> only paces the per-character fallback used if the fast-path
        /// assignment fails. Prefer <see cref="SendString"/> for fields that need real per-keystroke
        /// simulation. See <see cref="GPAL.WithTypingDelay"/> for the SendString vs. FillIn guidance.
        /// </summary>
        IAllowPuppeteerInputText FillInAppend(dynamic elementOrelementId, int delayMs = 0);
        /// <inheritdoc cref="FillInAppend(dynamic, int)"/>
        IAllowPuppeteerInputText FillInInsert(dynamic elementOrelementId, int delayMs = 0);
        /// <inheritdoc cref="FillInAppend(dynamic, int)"/>
        IAllowPuppeteerInputText FillInOverwrite(dynamic elementOrelementId, int delayMs = 0);
        [Browser(Description = "Configure this client to navigate the browser forward one page in history", Submenu = "Navigation")]
        IAllowPuppeteerExecution Forward();
        [Browser(Description = "Configure this client to make the browser window fullscreen", Submenu = "Windows")]
        IAllowPuppeteerExecution FullScreen();
        [Data(Description = "Configure this client to query the puppeteer server's ready status, optionally for a specific session token", Submenu = "Page")]
        IAllowPuppeteerExecution GetReadyStatus();
        [Action(Description = "Configure this client to check whether network activity has gone idle", Submenu = "Page")]
        IAllowPuppeteerParametersOrExecution CheckNetworkIdle(int maxConnections = 0);
        [Data(Description = "Configure this client to retrieve the shadow root of the element matching the given CSS selector", Submenu = "Find Elements")]
        IAllowPuppeteerExecution GetShadowRoot([Css] string css);
        [Browser(Description = "Configure this client to retrieve the current page URL", Submenu = "Navigation")]
        IAllowPuppeteerExecution GetCurrentUrl();
        [Browser(Description = "Configure this client to retrieve information about the current browser window", Submenu = "Windows")]
        IAllowPuppeteerExecution GetCurrentWindow();
        [Data(Description = "Configure this client to retrieve the given type of website storage", Submenu = "Page")]
        IAllowPuppeteerStorageOptions GetStorage(WebsiteStorageType storageType);
        [Data(Description = "Configure this client to retrieve the browser's preferred languages, most preferred first", Submenu = "Page")]
        IAllowPuppeteerExecution GetLanguages();
        [Data(Description = "Configure this client to retrieve the browser's user agent string", Submenu = "Page")]
        IAllowPuppeteerExecution GetUserAgent();
        [Browser(Description = "Configure this client to navigate the current tab to the given URL, resolving relative URLs and checking robots.txt restrictions", Submenu = "Navigation")]
        IAllowPuppeteerExecution GoTo(GPALUrl url);
        [Browser(Description = "Configure this client to switch to the tab whose URL matches the given URL", Submenu = "Tabs")]
        IAllowPuppeteerExecution GoToTab(GPALUrl url);
        [Browser(Description = "Configure this client to switch to the tab with the given tab id", Submenu = "Tabs")]
        IAllowPuppeteerExecution GoToTab(int tabId);
        [Browser(Description = "Configure this client to switch to the window containing the tab whose URL matches the given URL", Submenu = "Windows")]
        IAllowPuppeteerExecution GoToWindow(GPALUrl url);
        [Browser(Description = "Configure this client to switch to the window containing the tab with the given tab id", Submenu = "Windows")]
        IAllowPuppeteerExecution GoToWindow(int tabId);
        [Settings(Description = "Configure this client to switch context into the element matching the given selector", Submenu = "Element")]
        IAllowPuppeteerExecution InElement(string elementId);
        [Browser(Description = "Configure this client to inject the given JavaScript into the page", Submenu = "Scripts")]
        IAllowPuppeteerExecution InjectScript([JavaScript] string script);
        [Browser(Description = "Configure this client to remove all previously injected scripts (via InjectScript)", Submenu = "Scripts")]
        IAllowPuppeteerExecution ClearInjectedScripts();
        [Browser(Description = "Configure this client to switch context back to the main document", Submenu = "Frames")]
        IAllowPuppeteerExecution InMainDom();
        [Settings(Description = "Configure this client to switch context into the shadow root of the element matching the given selector", Submenu = "Element")]
        IAllowPuppeteerExecution InShadowDom(string elementId);
        [Browser(Description = "Configure this client to maximize the browser window", Submenu = "Windows")]
        IAllowPuppeteerExecution Maximize();
        [Browser(Description = "Configure this client to minimize the browser window", Submenu = "Windows")]
        IAllowPuppeteerExecution Minimize();
        [Action(Description = "Configure this client to move the mouse to the element matching the given selector", Submenu = "Mouse")]
        IAllowPuppeteerExecution MoveTo(string elementId);
        [Action(Description = "Configure this client to move the mouse to a viewport coordinate instead of to an element, for targets with no DOM handle such as an image match", Submenu = "Mouse")]
        IAllowPuppeteerExecution MoveTo(System.Drawing.Point point);
        [Action(Description = "Configure this client to open a new tab navigated to the given URL, resolving relative URLs and checking robots.txt restrictions", Submenu = "Tabs and Windows")]
        IAllowPuppeteerExecution NewTab(GPALUrl url = null);
        [Browser(Description = "Configure this client to switch to the next tab", Submenu = "Tabs")]
        IAllowPuppeteerExecution NextTab();
        [Browser(Description = "Configure this client to switch to the next browser window", Submenu = "Windows")]
        IAllowPuppeteerExecution NextWindow();
        [Browser(Description = "Configure this client to restore the browser window to its normal (non-maximized, non-minimized) state", Submenu = "Windows")]
        IAllowPuppeteerExecution Normal();
        [Action(Description = "Configure this client to open a new browser window navigated to the given URL, resolving relative URLs and checking robots.txt restrictions", Submenu = "Tabs and Windows")]
        IAllowPuppeteerExecution OpenWindow(GPALUrl url);
        [Action(Description = "Configure this client to override the HTTP referrer sent with subsequent requests", Submenu = "Referrer and User Agent")]
        IAllowPuppeteerExecution OverrideReferrer(string referrer);
        [Scroll(Description = "Configure this client to scroll the page down by the given number of page heights")]
        IAllowPuppeteerExecution PageDown(int pagesToScroll = 1);
        [Scroll(Description = "Configure this client to scroll to the bottom of the page")]
        IAllowPuppeteerExecution PageEnd();
        [Scroll(Description = "Configure this client to scroll to the top of the page")]
        IAllowPuppeteerExecution PageTop();
        [Scroll(Description = "Configure this client to scroll the page up by the given number of page heights")]
        IAllowPuppeteerExecution PageUp(int pagesToScroll = 1);
        [Data(Description = "Configure this client to press and hold the given modifier key(s)", Submenu = "Keyboard")]
        IAllowPuppeteerExecution PressModifierKey(ModifierKeys modifierKeys);
        [Browser(Description = "Configure this client to switch to the previous tab", Submenu = "Tabs")]
        IAllowPuppeteerExecution PreviousTab();
        [Browser(Description = "Configure this client to switch to the previous browser window", Submenu = "Windows")]
        IAllowPuppeteerExecution PreviousWindow();
        [Browser(Description = "Configure this client to refresh the current page", Submenu = "Navigation")]
        IAllowPuppeteerExecution Refresh();
        [Data(Description = "Configure this client to release the given modifier key(s)", Submenu = "Keyboard")]
        IAllowPuppeteerExecution ReleaseModifierKey(ModifierKeys modifierKeys);
        [Browser(Description = "Configure this client to restore the browser window to its normal size", Submenu = "Windows")]
        IAllowPuppeteerExecution Restore();
        [Action(Description = "Configure this client to left-click the given element and download the resulting target", Submenu = "Files")]
        IAllowPuppeteerParametersOrExecution LeftClickAndDownload(string elementId);
        [Action(Description = "Configure this client to left-click the given element to open a file picker, then upload the file(s) set via UploadFrom", Submenu = "Files")]
        IAllowPuppeteerParametersOrExecution LeftClickAndUpload(dynamic elementOrelementId);
        [Scroll(Description = "Configure this client to scroll the browser window horizontally by the given number of pixels")]
        IAllowPuppeteerExecution ScrollWindowByHorizontal([Pixels] int pixels);
        [Scroll(Description = "Configure this client to scroll the browser window vertically by the given number of pixels")]
        IAllowPuppeteerExecution ScrollWindowByVertical([Pixels] int pixels);
        /// <summary>
        /// Simulates typing: dispatches real key events character-by-character. Honors
        /// <see cref="GPAL.TypingDelay"/> when <paramref name="delayMs"/> is left at its default (0).
        /// When the resulting ceiling is greater than 0, each character is preceded by a random delay
        /// between Math.Min(25, delayMs) and delayMs milliseconds - not a fixed pace. Use this (rather
        /// than FillIn*) for fields that react to individual keystrokes or need to look human.
        /// See <see cref="GPAL.WithTypingDelay"/> for the SendString vs. FillIn guidance.
        /// </summary>
        IAllowPuppeteerExecution SendString(string text, int delayMs = 0);
        [Data(Description = "Configure this client to send a key press for the given virtual key code, translating it to the corresponding DOM key and code", Submenu = "Keyboard")]
        IAllowPuppeteerExecution SendKey(byte vkcode);
        [Action(Description = "Configure this client to write the given type of website storage; combine with WithStorageDomain/WithStorageKey/WithStorageData etc to specify what to write", Submenu = "Storage")]
        IAllowPuppeteerStorageOptions SetStorage(WebsiteStorageType storageType);
        [Action(Description = "Configure this client to override the browser's user agent string for subsequent requests", Submenu = "Referrer and User Agent")]
        IAllowPuppeteerExecution SetUserAgent(string userAgent);
        [Action(Description = "Configure this client to override the HTTP referrer with https://www.google.com, to mimic arriving from a search engine", Submenu = "Referrer and User Agent")]
        IAllowPuppeteerExecution SetGoogleReferrer();
        [Fluent(Description = "Configure this client to stop any active screen casting")]
        IAllowPuppeteerExecution StopCasting();
        [Browser(Description = "Configure this client to switch context back to the main document", Submenu = "Frames")]
        IAllowPuppeteerExecution SwitchToDefaultContent();
        [Browser(Description = "Configure this client to switch context into the element matching the given selector", Submenu = "Frames")]
        IAllowPuppeteerExecution SwitchToElement(string elementId);
        [Browser(Description = "Configure this client to switch context into the iframe matching the given selector", Submenu = "Frames")]
        IAllowPuppeteerExecution SwitchToFrame(string elementId);
        [Browser(Description = "Configure this client to switch context into the shadow root of the element matching the given selector", Submenu = "Frames")]
        IAllowPuppeteerExecution SwitchToShadowRoot(string elementId);
        [Action(Description = "Where files the browser downloads should end up", Submenu = "Files")]
        IAllowPuppeteerExecution DownloadTo([Directory] string downloadPath);
        [Action(Description = "Set the directory the browser sends downloads to, on its own", Submenu = "Files")]
        IAllowPuppeteerExecution SetDownloadFilename([Directory] string downloadPath);
        [Action(Description = "Configure this client to fire a DOM change event on the given element", Submenu = "Input")]
        IAllowPuppeteerExecution FireChangeEvent(string elementId);
        [Action(Description = "Configure this client to set keyboard focus on the given element", Submenu = "Mouse")]
        IAllowPuppeteerExecution Focus(string elementId);
        [Data(Description = "Configure this client to retrieve the bounding client rectangle of the given element", Submenu = "Element")]
        IAllowPuppeteerExecution GetBoundingClientRect(string backendNodeId);
        [Data(Description = "Configure this client to compute a hash of the given element's attributes, useful for detecting element changes", Submenu = "Element")]
        IAllowPuppeteerExecution GetElementAttributeHash(string elementId);
        [Action(Description = "Configure this client to retrieve the current page's HTML source", Submenu = "Page")]
        IAllowPuppeteerExecution GetPageSource();
        [Data(Description = "Configure this client to retrieve the parent node of the given element", Submenu = "Find Elements")]
        IAllowPuppeteerExecution GetParentNode(string elementId);
        [Data(Description = "Configure this client to retrieve the text content and computed CSS of the given element", Submenu = "Element")]
        IAllowPuppeteerExecution GetContentAndCss([Css] string selectorPath);
        [Data(Description = "Configure this client to retrieve the CSS attributes of the given element", Submenu = "Element")]
        IAllowPuppeteerExecution GetCssAttributes(string elementId);
        [Data(Description = "Configure this client to retrieve the DOM attributes of the given element", Submenu = "Element")]
        IAllowPuppeteerExecution GetDomAttributes(string nodeId);
        [Data(Description = "Configure this client to retrieve the DOM properties of the given element", Submenu = "Element")]
        IAllowPuppeteerExecution GetDomProperties(string backendNodeId);
        [Action(Description = "Configure this client to hide the given element", Submenu = "Input")]
        IAllowPuppeteerExecution HideElement(string elementId);
        [Data(Description = "Configure this client to check whether the given element is clickable", Submenu = "Element")]
        IAllowPuppeteerExecution IsClickable(string elementId);
        [Data(Description = "Configure this client to check whether the given element is displayed", Submenu = "Element")]
        IAllowPuppeteerExecution IsDisplayed(string elementId);
        [Data(Description = "Configure this client to check whether the given element is enabled", Submenu = "Element")]
        IAllowPuppeteerExecution IsEnabled(string elementId);
        [Scroll(Description = "Configure this client to check whether the page is scrolled to its end")]
        IAllowPuppeteerExecution IsEndOfPage();
        [Data(Description = "Configure this client to check whether the given element is visible in the viewport", Submenu = "Element")]
        IAllowPuppeteerExecution IsVisibleInViewport(IGPALElement element);
        [Data(Description = "Configure this client to check whether the element with the given backend node id is visible in the viewport", Submenu = "Element")]
        IAllowPuppeteerExecution IsVisibleInViewport(string elementId);
        [Action(Description = "Configure this client to left-click the given element", Submenu = "Clicks")]
        IAllowPuppeteerParametersOrExecution LeftClick(dynamic elementOrelementId);
        [Action(Description = "Configure this client to double left-click the given element", Submenu = "Clicks")]
        IAllowPuppeteerParametersOrExecution LeftDoubleClick(dynamic elementOrelementId);
        [Action(Description = "Configure this client to middle-click the given element", Submenu = "Clicks")]
        IAllowPuppeteerParametersOrExecution MiddleClick(dynamic elementOrelementId);
        [Action(Description = "Configure this client to right-click the given element", Submenu = "Clicks")]
        IAllowPuppeteerParametersOrExecution RightClick(dynamic elementOrelementId);
        [Action(Description = "Open a drop-down's options by clicking the element; follow with WithSelectValue or WithSelectIndex", Submenu = "Clicks")]
        IAllowPuppeteerSelectOptions SelectClick(string elementHandle);     // objectid
        [Scroll(Description = "Configure this client to scroll the given element into view")]
        IAllowPuppeteerExecution ScrollIntoView(dynamic elementOrelementId);
        [Data(Description = "Configure this client to retrieve the browser window's inner height", Submenu = "Window")]
        IAllowPuppeteerExecution WindowInnerHeight();
        [Data(Description = "Configure this client to retrieve the browser window's inner width", Submenu = "Window")]
        IAllowPuppeteerExecution WindowInnerWidth();
        [Data(Description = "Configure this client to retrieve the browser window's outer height", Submenu = "Window")]
        IAllowPuppeteerExecution WindowOuterHeight();
        [Data(Description = "Configure this client to retrieve the browser window's outer width", Submenu = "Window")]
        IAllowPuppeteerExecution WindowOuterWidth();
        [Data(Description = "Configure this client to retrieve the page's horizontal scroll offset", Submenu = "Window")]
        IAllowPuppeteerExecution WindowPageOffsetX();
        [Data(Description = "Configure this client to retrieve the page's vertical scroll offset", Submenu = "Window")]
        IAllowPuppeteerExecution WindowPageOffsetY();
        [Data(Description = "Configure this client to retrieve the browser window's screen X position", Submenu = "Window")]
        IAllowPuppeteerExecution WindowScreenLeft();
        [Data(Description = "Configure this client to retrieve the browser window's screen Y position", Submenu = "Window")]
        IAllowPuppeteerExecution WindowScreenTop();
        [Action(Description = "Configure this client to hover the mouse over the given element", Submenu = "Mouse")]
        IAllowPuppeteerExecution Hover(string elementId);
        [Data(Description = "Configure this client to evaluate the given XPath expression and return the first matching node", Submenu = "Find Elements")]
        IAllowPuppeteerExecution Evaluate([XPath] string xpath);
        [Data(Description = "Configure this client to evaluate the given XPath expression and return all matching nodes", Submenu = "Find Elements")]
        IAllowPuppeteerExecution EvaluateAll([XPath] string xpath);
        [Data(Description = "Configure this client to find the first element matching the given CSS selector", Submenu = "Find Elements")]
        IAllowPuppeteerExecution QuerySelector([Css] string css);
        [Data(Description = "Configure this client to find all elements matching the given CSS selector", Submenu = "Find Elements")]
        IAllowPuppeteerExecution QuerySelectors([Css] string css);
        IAllowPuppeteerExecution ScrollWindow(int hPixels, int vPixels);
        [Action(Description = "Configure this client to clear any overridden HTTP referrer", Submenu = "Referrer and User Agent")]
        IAllowPuppeteerExecution ClearReferrer();
        [Browser(Description = "Configure this client to bring the browser window to the foreground", Submenu = "Windows")]
        IAllowPuppeteerExecution TopBrowser();


        // Multi-parameter methods with fluent steps
        IAllowPuppeteerExecution ScrollElement(int hPixels, int vPixels);
        [Action(Description = "Configure this client to set an attribute on the given element; combine with WithAttribute and WithValue to specify the attribute and value", Submenu = "Input")]
        IAllowPuppeteerAttribute SetAttribute(string elementId);
        [Data(Description = "Configure this client to set the value of a range input element; combine with WithRangeValue to specify the value", Submenu = "Keyboard")]
        IAllowPuppeteerRangeValue SetRange(string elementId);
        [Scroll(Description = "Configure this client to scroll the given element; combine with WithHPixels/WithVPixels to specify the scroll amount")]
        IAllowPuppeteerScrollElement ScrollElement(string elementId);
        [Action(Description = "Configure this client to start a drag from the given element; combine with WithDestElementId or WithDeltaX/WithDeltaY to set the drop target", Submenu = "Mouse")]
        IAllowPuppeteerDragAndDrop DragAndDrop(string elementId);
        [Data(Description = "Configure this client to retrieve an attribute from the given element; combine with WithAttribute to specify which attribute", Submenu = "Element")]
        IAllowPuppeteerAttribute GetAttribute(string elementId); // Returns IAllowRESTAttribute, skips WithValue}
    }
    public interface IPuppeteerClient : IAllowPuppeteerEndpoint, IAllowPuppeteerEndpointDetails, IAllowPuppeteerScrollElement, IAllowPuppeteerDragAndDrop, IAllowPuppeteerExecution, IAllowPuppeteerParametersOrExecution
        , IAllowPuppeteerCheckNetworkIdleOrExecution, IAllowPuppeteerAttribute, IAllowPuppeteerParameters, IAllowPuppeteerInputText, IAllowPuppeteerWorkflow
        , IAllowPuppeteerCommunicator, IAllowPuppeteerSelectOptions, IAllowPuppeteerStorageOptions, IAllowPuppeteerRangeValue, IAllowToGPALObject<IPuppeteerClient>
    {
        string Name { [Data(Description = "The display name assigned to this client via WithName")] get; }
    }
    #endregion Puppeteer
    #region GPAL URL
    /// <summary>
    /// Starting interface - only allows setting the target URL
    /// </summary>
    public interface IAllowGPALUrlForUrl : IAllowToGPALObject<IGPALUrl>
    {
        /// <summary>
        /// Sets the target URL (blank or null becomes https://google.com)
        /// </summary>
        [Settings(Description = "Sets the target URL (blank or null becomes https://google.com)")]
        IAllowGPALUrlStorageType ForUrl([URL] string url);
    }

    /// <summary>
    /// Interface after URL is set - allows selecting storage type
    /// </summary>
    public interface IAllowGPALUrlStorageType : IAllowToGPALObject<IGPALUrl>
    {
        /// <summary>
        /// Start declaring the storage action with storage type first
        /// </summary>
        [Settings(Description = "Start declaring the storage action with storage type first")]
        IAllowGPALUrlStorageItemConfig WithStorageType(WebsiteStorageType type);
    }

    /// <summary>
    /// Interface after deleting a specific item - allows optional path/domain
    /// </summary>
    public interface IAllowGPALUrlStorageItemConfig : IAllowGPALUrlStorageType, IAllowToGPALObject<IGPALUrl>
    {
        /// <summary>
        /// Optional: domain override
        /// </summary>
        [Browser(Description = "Optional: domain override")]
        IAllowGPALUrlStorageItemConfig WithStorageDomain(string domain);

        /// <summary>
        /// For indexedDb, this is the 'table' the object store, they call it a store name
        /// </summary>
        [Browser(Description = "For indexedDb, this is the 'table' the object store, they call it a store name")]
        IAllowGPALUrlStorageItemConfig WithStorageStoreName(string storeName);

        /// <summary>
        /// Optional: path (e.g. cookie path, object store name)
        /// </summary>
        [Browser(Description = "Optional: path (e.g. cookie path, object store name)")]
        IAllowGPALUrlStorageItemConfig WithStoragePath(string path);

        /// <summary>
        /// Specific key for the current storage type
        /// </summary>
        [Browser(Description = "Specific key for the current storage type")]
        IAllowGPALUrlStorageItemConfig WithStorageKey(string key);

        /// <summary>
        /// User defined data, strictly used for filtering in GPAL. Can store data of value
        /// </summary>
        [Settings(Description = "User defined data, strictly used for filtering in GPAL")]
        IAllowGPALUrlStorageItemConfig WithUserDefined(string userDefined);
        /// <summary>
        /// OttoMagic Only: For delete operations without a domain, limit to origin?
        /// true = delete across origins
        /// false = delete only in the current origin (default)
        /// </summary>
        /// <param name="trueFalse"></param>
        /// <returns></returns>
        [Settings(Description = "Delete the storage on every origin, not only the current one (OttoMagic only)")]
        IAllowGPALUrlStorageItemConfig WithDeleteAcrossOrigins(bool trueFalse);
    }

    /// <summary>
    /// Allow setting the storage data parameter for 'set' operations
    /// </summary>
    public interface IAllowGPALUrlStorageData
    {
        /// <summary>
        /// Optional: domain override
        /// </summary>
        [Browser(Description = "Optional: domain override")]
        IAllowGPALUrlStorageItemConfig WithStorageData(string data);
    }

    /// <summary>
    /// Public interface exposed to consumers
    /// </summary>
    public interface IGPALUrl :
        IAllowGPALUrlForUrl,
        IAllowGPALUrlStorageType,
        IAllowGPALUrlStorageItemConfig,
        IAllowGPALUrlStorageData
    {
        /// <summary>
        /// The target URL (never null)
        /// </summary>
        string Url { [Data(Description = "The target URL (never null)")] get; }

        [Action(Description = "Adds a fully-configured StorageAction to the list")]
        List<StorageAction> Add(StorageAction storageAction);
        [Action(Description = "Adds multiple fully-configured StorageAction items")]
        List<StorageAction> Add(List<StorageAction> storageActions);

        /// <summary>
        /// List of storage actions to perform before navigation
        /// </summary>
        [Data(Description = "List of storage actions to perform before navigation")]
        IReadOnlyList<StorageAction> GetActions();
    }
    #endregion GPAL URL
}
