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

//extern alias mUIA;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using static GenerallyPositive.Enums;
// The HiddenDesktop property hides the static HiddenDesktop class, so the class is used through this alias. Browser
// does the same.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    public partial class Application
    {
        /// <summary>
        /// Check to see whether the Selectors for the current UOW are finding elements.
        /// Only called if .WaitFor fluent command is used
        /// </summary>
        /// <returns>True if elements found for all Selector(s) or true if no check is requested</returns>
        private bool CheckWaitFor()
        {
            bool matchFound = true;
            List<GPALAutomationElement> foundElements = null;

            // An earlier action on this unit of work already waited for these selectors; skip the wait. A selector
            // added after an action starts a new unit of work.
            if (null != CurrentUOW.WaitForSatisfied)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Selectors for this unit of work already found at [{CurrentUOW.WaitForSatisfied:HH:mm:ss.fff}], skipping redundant wait.", this, GPALObjectType.Application);
                return true;
            }

            if (WaitTime.Never < CurrentUOW.WaitForInMs)
            {
                // we are waiting on selector(s)
                foundElements = ElementHelper.WaitFor(this, CurrentUOW.WaitForInMs, out matchFound);

                if (0 < foundElements?.Count)
                    CurrentUOW.WaitForSatisfied = DateTime.Now;
            }

            return matchFound;
        }
        /// <summary>
        /// Every action calls this first to perform to top the application, perform a maintenance sweep,<br/>
        /// check persistent selectors (and act upon them), check WaitFor status, and set the 'actionCalled' flag in the UOW
        /// </summary>
        /// <param name="checkWaitFor">True to check WaitFor</param>
        /// <returns>Always true - the gated action is always attempted</returns>
        private bool InAction(bool checkWaitFor)
        {
            // the program's window on top; a program on a hidden desktop has that desktop to itself, and the hardware
            // click TopProcess falls back to cannot reach it
            if (true == ElementHelper.HardwareReaches(this))
                ApplicationHelper.TopProcess(this.Process);

            CurrentUOW.ActionCalled = true;

            SelectorListMaintenanceSweep(CurrentUOW); // purge any nodes marked 'deleteme' before iterating list
            SelectorListMaintenanceSweep(persistentUOW); // purge any nodes marked 'deleteme' before iterating list
            CheckPersistentSelectors(); // check if there are persistent selectors to deal with

            if (true == checkWaitFor)
                CheckWaitFor();

            // Always run the action, as the browser side does. WaitFor only pauses; it is not a match test. A missing
            // element is reported by the action instead of being skipped silently.
            return true;
        }
        /// <summary>
        /// Remove any nodes self-marked for deletion before attempting to utilize them (could be marked via a persistent handler)
        /// </summary>
        /// <param name="CurrentUOW">The UOW to sweeo for deletions</param>
        private static void SelectorListMaintenanceSweep(dynamic CurrentUOW)
        {
            for (int idx = 0; idx < CurrentUOW.WithSelectorList.Count; idx++)
                if (true == CurrentUOW.WithSelectorList[idx].DeleteMe)
                    CurrentUOW.WithSelectorList.RemoveAt(idx);

            for (int idx = 0; idx < CurrentUOW.InSelectorList.Count; idx++)
                if (true == CurrentUOW.InSelectorList[idx].DeleteMe)
                    CurrentUOW.InSelectorList.RemoveAt(idx);
        }
        /// <summary>
        /// Check whether any persistent selectors are defined and process them before the current UOW<br/>
        /// This is checked before every action (click, hover, input, etc) is performed on a UOW
        /// </summary>
        private void CheckPersistentSelectors()
        {
            // Look up each persistent selector; its CallIf handlers do the work.
            if (0 < (persistentUOW.WithSelectorList?.Count ?? 0))
                foreach (Selector selector in persistentUOW.WithSelectorList)
                    ElementHelper.FindElements(this, persistentUOW, selector, out bool matchedAll, out List<GPALAutomationElement> elems);
        }
        /// <summary>
        /// Either wait the amount of time specified or wait UP TO the amount of time specified waiting for elements to be found for the Selector(s) in the current UOW.<br/>
        /// If an action has already been called, then this is interpretted as a sleep for the specified miliseconds.<br/>
        /// If only selectors have been defined in the current UOW, then this is a delay waiting for the selectors to be present before continuing with the workflow.<br/>
        /// Waiting for elements does not guarantee they will be present when the workflow continues. If they are not present, the CallIfNotFound handler will be invoked so you can deal with that.<br/>
        /// <b>NOTE:</b> WaitFor is not an action, it defines a time to wait for elements to be present before trying to perform actions.
        /// </summary>
        /// <param name="timeoutInTicks"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitFor([Milliseconds] int timeoutInTicks)
        {
            // After an action there are no selectors to wait for, so this is a sleep.
            if (true == CurrentUOW.ActionCalled)
                Thread.Sleep(timeoutInTicks);
            else
                CurrentUOW.WaitForInMs = timeoutInTicks;

            return this;
        }
        /// <summary>
        /// Wait for the controls of the selectors on this unit of work to enter the specified state, as on browsers.<br/>
        /// Before an action, the next action waits for its controls to match and then to reach the state, for the unit
        /// of work's WaitFor(timeInMs); with no wait time set there is no wait. After an action, the wait runs now,
        /// for three seconds when no wait time was set.
        /// </summary>
        /// <param name="elementState">The state to wait for</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitFor(ElementState elementState)
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
        /// Default is 10 seconds.
        /// </summary>
        /// <param name="windowTitle"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitForWindow(string windowTitle)
        {
            if (true == InAction(true))
                SettleOnWindow(true == ApplicationSettings.HiddenDesktop
                    ? Desktops.WaitForWindowOn(ApplicationSettings.HiddenDesktopName, title => windowTitle.Equals(title), ApplicationSettings.WaitForWindowTimeout)
                    : FormHelper.WaitForWindow(windowTitle, ApplicationSettings.WaitForWindowTimeout));
            return this;
        }
        /// <summary>
        /// Wait for a Windows window with title that matches the regex pattern to appear on the desktop.<br/>
        /// Used in conjunction with .WithWaitForWindowTimeout to set the wait duration in seconds.<br/>
        /// Default is 10 seconds.
        /// </summary>
        /// <param name="windowTitleRegex">Regular expression the window title must match</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitForWindowRegex([Regex] string windowTitleRegex)
        {
            if (true == InAction(true))
                SettleOnWindow(true == ApplicationSettings.HiddenDesktop
                    ? Desktops.WaitForWindowOn(ApplicationSettings.HiddenDesktopName, title => 0 < System.Text.RegularExpressions.Regex.Matches(title, windowTitleRegex).Count, ApplicationSettings.WaitForWindowTimeout)
                    : FormHelper.WaitForWindowRegex(windowTitleRegex, ApplicationSettings.WaitForWindowTimeout));
            return this;
        }

        /// <summary>
        /// Makes the window found by the wait the one the following selectors search, instead of the window Open found.<br/>
        /// This is how a launcher's window is reached: explorer.exe passes its folder to the running shell and exits,
        /// so the window belongs to a process Open never held.
        /// </summary>
        /// <param name="windowHandle">The window found; zero when the wait timed out</param>
        private void SettleOnWindow(IntPtr windowHandle)
        {
            ApplicationSettings.LastWindowHandle = windowHandle;

            if (IntPtr.Zero != windowHandle)
            {
                AutomationElement window = AutomationElement.FromHandle(windowHandle);

                if (null != window)
                    ApplicationSettings.RootAutomationElement = window;
            }
        }
        /// <summary>
        /// Helper method for GPAL applications to remove a handler from all UOWs. Envisioned to be used in a persistent handler, but can be used anyway imagined. This is not fluent.
        /// </summary>
        /// <param name="func">The CallIf handler to remove</param>
        public void RemoveCallIfHandlerEverywhere(Application.CallIfDelegate func)
        {
            CurrentUOW.AppCallIfFound.Remove(func);
            CurrentUOW.AppCallIfNotFound.Remove(func);

            foreach (Selector selector in CurrentUOW.WithSelectorList)
            {
                selector.SelectorSettings.AppCallIfFound.Remove(func);
                selector.SelectorSettings.AppCallIfNotFound.Remove(func);
            }

            foreach (Selector selector in CurrentUOW.InSelectorList)
            {
                selector.SelectorSettings.AppCallIfFound.Remove(func);
                selector.SelectorSettings.AppCallIfNotFound.Remove(func);
            }

            persistentUOW.AppCallIfFound.Remove(func);
            persistentUOW.AppCallIfNotFound.Remove(func);

            foreach (Selector selector in persistentUOW.WithSelectorList)
            {
                selector.SelectorSettings.AppCallIfFound.Remove(func);
                selector.SelectorSettings.AppCallIfNotFound.Remove(func);
            }
        }
        /// <summary>
        /// Add a persistent CallIfFound handler to be called if any defined persistent selectors find/match elements.<br/>
        /// Persistent selectors are always looked for before every action cross-cutting your workflow.<br/>
        /// Persistent handlers can remove themselves in the CallIfHandler by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="persistentCallIfFound"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowPersistentCallBack PersistentCallIfFound(CallIfDelegate persistentCallIfFound)
        {
            persistentUOW.AppCallIfFound.Add(persistentCallIfFound);

            return this;
        }
        /// <summary>
        /// Add a persistent CallIfNotFound handler to be called if any defined persistent selectors DO NOT find/match elements.<br/>
        /// Persistent selectors are always looked for before every action cross-cutting your workflow.
        /// Persistent handlers can remove themselves in the CallIfHandler by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="persistentCallIfNotFound"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowPersistentCallBack PersistentCallIfNotFound(CallIfDelegate persistentCallIfNotFound)
        {
            persistentUOW.AppCallIfNotFound.Add(persistentCallIfNotFound);

            return this;
        }
        /// <summary>
        /// Add a Unit of Work level CallIfFound handler to be called if any defined selectors find/match elements.<br/>
        /// This method will be called for every found selector in the current UOW.<br/>
        /// CallIf handlers can remove themselves by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="callIfFoundDelegate">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallIfFound(Application.CallIfDelegate callIfFoundDelegate)
        {
            CurrentUOW.AppCallIfFound.Add(callIfFoundDelegate);
            return this;
        }
        /// <summary>
        /// Add a Unit of Work level CallIfNotFound handler to be called if any defined selectors DO NOT find/match elements.<br/>
        /// This method will be called for every NOT FOUND selector in the current UOW.<br/>
        /// CallIf handlers can remove themselve by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="callIfNotFoundDelegate">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallIfNotFound(Application.CallIfDelegate callIfNotFoundDelegate)
        {
            CurrentUOW.AppCallIfNotFound.Add(callIfNotFoundDelegate);
            return this;
        }
        /// <summary>
        /// Add a handler to call after a row of tokens is consumed and after all inputs are filled in with data.<br/>
        /// Only one CallAfterFillIn handler is possible, no chaining.<br/>
        /// Use FillInFrom[Database|File|GPALGrid] to loop thru data in your main workflow and then perform next workflow steps in the CallAfterFillIn handler.
        /// </summary>
        /// <param name="callAfterFillIn">Your CallAfterFillIn method</param>
        /// <returns>-1 to terminate the program, otherwise the value is ignored.</returns>
        public IAllowAfterAnySelector CallAfterFillIn(CallAfterFillInDelegate callAfterFillIn)
        {
            CurrentUOW.AppCallAfterFillIn = callAfterFillIn;
            return this;
        }
    }
}
