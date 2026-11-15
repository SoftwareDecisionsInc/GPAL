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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Condition = System.Windows.Automation.Condition;
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    internal partial class ElementHelper
    {
        /// <summary>
        /// Fill in text on an element using the technique defined in the selector
        /// do this for the entire unit of work
        /// </summary>
        /// <param name="application">The Application whose selectors are filled in</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">How the text is written into the element</param>
        public static void FillInFrom(Application application, string useText, WriteMode writeMode)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        bool isPassword = elem.Ae.Current.IsPassword;

                        // Log empty, one- and two-character strings in full.
                        string abbreviatedText = 2 < (useText?.Length ?? 0)
                            ? useText[0] + "..." + useText[useText.Length - 1]
                            : useText;

                        if (false == isPassword)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] text [{abbreviatedText}] in element [{elem.Ae.Current.LocalizedControlType}][{selector.Name}]", application, GPALObjectType.Application);
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] XXXXXX in element [{elem.Ae.Current.LocalizedControlType}][{selector.Name}]", application, GPALObjectType.Application);

                        FillInFrom(application, elem.Ae, new UnitOfWork.ElementNode() { OffsetX = selector.OffsetX, OffsetY = selector.OffsetY }, useText, writeMode);
                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }

                UnitOfWork safeUOW = application.CurrentUOW;
                CallIfStatus handled = 0;

                // CAVEAT: Handled and NotHandled are treated the same; only Terminate is acted on
                IGPALGrid<string> tokens = GPAL.Grid.ToGPALObject();
                tokens.AddRow(new List<string> { useText });
                if (null != application.CurrentUOW.AppCallAfterFillIn)
                    handled = application.CurrentUOW.AppCallAfterFillIn(application, tokens, 1);

                // KLUDGE: the CallAfterFillIn handler can start a new current unit of work, so the one saved before the call is restored
                application.CurrentUOW = safeUOW;

                if (CallIfStatus.Terminate == handled)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CallAfterFillIn handler [{application.CurrentUOW.AppCallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.", application, GPALObjectType.Application);
                    throw new GPALException($"CallAfterFillIn handler [{application.CurrentUOW.AppCallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.");
                }
            }
        }
        /// <summary>
        /// Fills in the given AutomationElement's value using its ValuePattern (UI Automation), or falls back to
        /// hardware emulation if the element doesn't support ValuePattern or hardware interaction is requested.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="ae">The AutomationElement to fill in</param>
        /// <param name="elementNode">The element node describing interaction type and click offsets, used for hardware fallback</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">How to write the text into the control (overwrite/insert/append)</param>
        internal static void FillInFrom(Application application, AutomationElement ae, UnitOfWork.ElementNode elementNode, string useText, WriteMode writeMode)
        {
            bool taken = false;

            if ((Enums.InteractionType.Hardware == elementNode.InteractionType || true == application.applicationSettings.UseHardware) && true == HardwareReaches(application))
            {
                HardwareFillInFrom(application, new GPALAutomationElement(ae), elementNode.OffsetX, elementNode.OffsetY, useText, writeMode);

                taken = true;
            }

            if (false == taken)
            {
                // LegacyIAccessiblePattern is only accessible via the UIAComWrapperX nuget package
                /*
                if (true == ae.TryGetCurrentPattern(System.Windows.Automation.LegacyIAccessiblePattern.Pattern, out object legacyPattern))
                {
                    // Set focus for input functionality and begin.
                    try
                    {
                        ae.SetFocus();
                    }
                    catch (Exception ex)
                    {
                        GPAL.GPALEventArgs args = new GPAL.GPALEventArgs()
                        {
                            GPALObject = application,
                            GPALObjectType = GPALObjectType.Application,
                            CurrentUOW = application.CurrentUOW,
                            AutomationElement = ae,
                            Message = $"Unable to set focus on Control {ae.Current.ControlType.LocalizedControlType}.",
                            ExceptionRaised = ex,
                            ScreenShot = ImageHelper.CaptureScreen(),
                            DateTimeStamp = DateTime.Now
                        };

                        GPAL.ExceptionHandler?.Invoke(application, args);
                    }
                    if (WriteMode.Overwrite == writeMode)
                        ((LegacyIAccessiblePattern)legacyPattern).SetValue(useText);
                    else if (WriteMode.Append == writeMode)
                    {
                        string content = ((LegacyIAccessiblePattern)legacyPattern).Current.Value;
                        content += useText;
                        ((LegacyIAccessiblePattern)legacyPattern).SetValue(content);
                    }
                    else if (WriteMode.Insert == writeMode)
                    {
                        string content = ((LegacyIAccessiblePattern)legacyPattern).Current.Value;
                        useText += content;
                        ((LegacyIAccessiblePattern)legacyPattern).SetValue(content);
                    }
                }
                else
                */
                // A control can offer ValuePattern and throw on write (a multi-line edit does). Try the write and fall
                // through on failure.
                if (true == ae.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                {
                    // Set focus before writing.
                    try
                    {
                        ae.SetFocus();
                    }
                    catch (Exception)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to set focus on control [{ae.Current.ControlType.LocalizedControlType}], so the value is written to it without the focus.", application, GPALObjectType.Application);
                    }

                    try
                    {
                        string content = WriteMode.Overwrite == writeMode ? string.Empty : ((ValuePattern)valuePattern).Current.Value;

                        ((ValuePattern)valuePattern).SetValue(WriteMode.Append == writeMode ? content + useText
                                                            : WriteMode.Insert == writeMode ? useText + content
                                                            : useText);

                        taken = true;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ValuePattern on [{ae.Current.ControlType.ProgrammaticName}] would not take the text, so the next way of writing it is tried.", application, GPALObjectType.Application, ex);
                    }
                }

                // A control with no writable value is still a window, and a window holds its text.
                if (false == taken)
                    taken = SetNativeText(application, ae, useText, writeMode);

                if (false == taken)
                {
                    // Typing here is recovery, so NoFallbackRecoveryActions turns it off.
                    if (false == GPAL.NoFallbackRecoveryActions && true == HardwareReaches(application))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{ae.Current.ControlType.ProgrammaticName}] offers no way to write text into it, so it is typed into instead.", application, GPALObjectType.Application);

                        HardwareFillInFrom(application, new GPALAutomationElement(ae), elementNode.OffsetX, elementNode.OffsetY, useText, writeMode);
                    }
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.ProgrammaticName}] offers no way to write text into it, so it was left as it was.", application, GPALObjectType.Application);
                }
            }
        }
        /// <summary>
        /// Fills in text on the given element using hardware emulation: focuses/clicks the element, positions the
        /// caret/selection according to <paramref name="writeMode"/>, then sends the text as keystrokes.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to fill in</param>
        /// <param name="offsetX">X offset, relative to the element, to click before typing</param>
        /// <param name="offsetY">Y offset, relative to the element, to click before typing</param>
        /// <param name="text">The text to send</param>
        /// <param name="writeMode">How to write the text into the control (overwrite/insert/append)</param>
        public static void HardwareFillInFrom(Application application, GPALAutomationElement element, int offsetX, int offsetY, string text, WriteMode writeMode)
        {
            ApplicationHelper.TopProcess(application.Process);

            HardwareFocus(application, element, offsetX, offsetY);

            if (WriteMode.Overwrite == writeMode)
            {
                // CTRL-a selects the whole input, and the typed text replaces it
                HardwareHelper.SendChar('a', ModifierKeys.Control, true);
                Thread.Sleep(150);
            }
            else if (WriteMode.Append == writeMode)   // CTRL-End goto end of document
            {
                HardwareHelper.SendKey(GPAL.VK_END, ModifierKeys.Control);
                Thread.Sleep(150);
            }
            else if (WriteMode.Insert == writeMode)   // CTRL-Home goto start of document
            {
                HardwareHelper.SendKey(GPAL.VK_HOME, ModifierKeys.Control);
                Thread.Sleep(150);
            }

            HardwareHelper.SendString(text, GPAL.TypingDelay);

            SettleInput(application);
        }

        /// <summary>
        /// Waits until the application has processed the input just sent.
        /// </summary>
        /// <remarks>
        /// keybd_event returns before the application reads the key. Returns once the process is input idle, or after
        /// INPUT_SETTLE_MS.
        /// </remarks>
        /// <param name="application">The Application the input was sent to</param>
        internal static void SettleInput(Application application)
        {
            try
            {
                application?.Process?.WaitForInputIdle(INPUT_SETTLE_MS);
            }
            catch (Exception)
            {
                // A process with no message loop, or one that has exited, has nothing to wait on. Neither is a failure.
            }
        }

        // Maximum wait for an application to process input. Only reached by a process that never reports idle.
        private const int INPUT_SETTLE_MS = 2_000;

        /// <summary>
        /// Focuses each element matched by the unit of work's selectors, with UIAutomation SetFocus, or with a
        /// hardware click when hardware is asked for.
        /// </summary>
        /// <param name="application">The Application whose selectors are focused</param>
        public static void Focus(Application application)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        if ((Enums.InteractionType.Hardware == selector.InteractionType || true == application.applicationSettings.UseHardware) && true == HardwareReaches(application))
                        {
                            int offsetX, offsetY;
                            offsetX = selector.SelectorSettings.OffsetX;
                            offsetY = selector.SelectorSettings.OffsetY;

                            HardwareFocus(application, elem, offsetX, offsetY);   // hardwre focus will try to click in text inputs
                        }
                        else
                        {

                            try
                            {
                                if (null != elem.Ae)
                                    elem.Ae.SetFocus();
                                else
                                    HardwareFocus(application, elem, 0, 0);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Focus threw on selector [{selector.Name}], so the focus did not move.", application, GPALObjectType.Application, ex);
                            }
                        }

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Focuses the given element using hardware emulation: left-clicks it if it's a Text control, otherwise just moves the mouse to it.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to focus</param>
        /// <param name="OffsetX">X offset, relative to the element, to click/move to</param>
        /// <param name="OffsetY">Y offset, relative to the element, to click/move to</param>
        private static void HardwareFocus(Application application, GPALAutomationElement element, int OffsetX, int OffsetY)
        {
            if (null != element.Ae && ControlType.Text == element.Ae.Current.ControlType)
                HardwareClick(application, element, ClickType.LeftClick, ModifierKeys.NONE, OffsetX, OffsetY);
            else
                HardwareMoveTo(application, element, OffsetX, OffsetY);
        }
    }
}
