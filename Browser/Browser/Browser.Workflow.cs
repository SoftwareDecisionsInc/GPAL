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

            // every file's rows, one grid per file, in one grid of requests
            IGPALGrid<string> fetchTokens = GPAL.Grid.ToGPALObject();

            foreach (IGPALGrid<string> grid in tokenList)
                foreach (List<string> row in grid)
                    fetchTokens.AddRow(row);

            CurrentUOW.FetchTokens = 0 == fetchTokens.Count() ? null : fetchTokens;
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
                for (int pages = 0; pages < CurrentUOW.PageCount; pages++)  // StartWorkflow runs the CallIf and CallAfter handlers with no main workflow
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
        /// <summary>
        /// Either wait the amount of time specified or wait UP TO the amount of time specified waiting for elements to be found for the Selector(s) in the current UOW.<br/>
        /// If an action has already been called, then this is interpretted as a sleep for the specified miliseconds.<br/>
        /// If only selectors have been defined in the current UOW, then this is a delay waiting for the selectors to be present before continuing with the workflow.<br/>
        /// Waiting for elements does not guarantee they will be present when the workflow continues. If they are not present, the CallIfNotFound handler will be invoked so you can deal with that.<br/>
        /// <b>NOTE:</b> WaitFor is not considered an action, it defines a time to wait for elements to be present before trying to perform actions.
        /// </summary>
        /// <param name="waitForTimeInMs">Time to wait in milliseconds; use a predefined WaitTime value or any int (ms)</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitFor WaitFor(WaitTime waitForTimeInMs)
        {
            // after an action there are no selectors to wait on, so this sleeps
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

            // a selector cannot be waited on with Never, so Immediate is used and Never put back after
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

                Close();

                throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
            }

            // settled before anything is launched, because the engine chosen here decides which of the branches
            // below runs
            if (true == BrowserSettings.HiddenDesktop && false == hiddenDesktopReported)
            {
                hiddenDesktopReported = true;

                // If the hidden desktop cannot be created, warn and run on the user's screen.
                if (false == Desktops.Ensure(BrowserSettings.HiddenDesktopName))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No hidden desktop could be made, so this browser opens on the desktop you are working on", this, GPALObjectType.Browser);

                    BrowserSettings.HiddenDesktop = false;
                }
            }

            // Every workflow, since an engine or WithPromptForDownload set after the first would reach the user's screen
            if (true == BrowserSettings.HiddenDesktop)
            {
                // SendInput only reaches the on-screen desktop. Switch to the non-hardware engine and warn with the
                // engine used.
                if (true == BrowserSettings.UseHardware)
                {
                    AutomationEngine steppedDownTo = HardwareStepDown(BrowserSettings.AutomationEngine);

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.AutomationEngine}] cannot drive a hidden desktop, so this browser runs on [{steppedDownTo}]", this, GPALObjectType.Browser);

                    BrowserSettings.AutomationEngine = steppedDownTo;
                }

                // A Save As dialog on a hidden desktop has no one to answer it, so downloads go straight to the
                // download folder, as they do headless, whatever WithPromptForDownload asked for
                BrowserSettings.PromptForDownload = false;
            }

            // Edge's download.prompt_for_download opens its own "what do you want to do" menu, never the Save As
            // dialog GPAL answers, so downloads go straight to the download folder and are saved under the name asked
            // for, as on a hidden desktop
            if (BrowserType.Edge == BrowserSettings.BrowserType && true == BrowserSettings.PromptForDownload)
            {
                BrowserSettings.PromptForDownload = false;

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Edge asks what to do with a download in its own menu, not a Save As dialog, so its downloads go straight to the download folder", this, GPALObjectType.Browser);
            }

            // URL is used only by the first launch
            if (true == UseSelenium)
            {
                // start the Selenium driver and its browser
                if (null == BrowserDriver)
                {
                    if (null == (BrowserDriver = LaunchWhereAsked(() => BrowserHelper.GetBrowserDriver(BrowserSettings))))
                        return false;    // GetBrowserDriver throws when the driver cannot start; false covers a null driver

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

                    // Chrome and Edge: turn the download prompt off at run time over CDP, so a supplied profile follows
                    // the workflow setting too. Firefox has no runtime equivalent; GetBrowserDriver sets it in the profile prefs.
                    if (false == BrowserSettings.PromptForDownload && BrowserType.FireFox != BrowserSettings.BrowserType)
                        BrowserHelper.AllowDownloads(BrowserSettings, BrowserSettings.DownloadLocation ?? FileHelper.GetDefaultDownloadDirectory(this));
                }
            }
            // Do not launch when attached through GPALRestAPI or an existing port. An attached browser has no
            // Process, which would otherwise launch a browser on every action.
            else if (null == BrowserSettings.Process && false == BrowserSettings.AttachedRestApi && false == BrowserSettings.UseExistingBrowser)   // use browser extension/puppeteer to control browser, not selenium
            {
                if (true == UseOttoMagic)
                    // OttoMagic needs the browser to open on a page its content script runs in
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
                _ = PuppeteerCommunicator;  // creates the communicator

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
                FileDownloaded = false; // every action clears it; a WithSelector after LeftClickAndDownload is not an action, so the value survives it

                // True lets CallIfFound and CallIfNotFound run; a WaitFor waits for the element and does not end the
                // workflow. GetBrowserDriver throws rather than returning null. inAction is cleared in finally, so after
                // an exception later actions still run WorkflowSetup.
                try
                {
                    retval = WorkflowSetup(checkWaitFor, doMaintenance, URL);
                }
                finally
                {
                    inAction = false;
                }
            }
            return retval;
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
            bool wantsHardware = InteractionType.Hardware == selector.RequestedInteractionType || true == BrowserSettings.UseHardware;

            if (true == wantsHardware)
            {
                // SendInput only reaches the on-screen desktop. WorkflowSetup handles hardware engines; this handles a
                // selector's WithHardware.
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
    }
}
