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
        /// Returns the element a value can be written to: the found element, or the first element inside it that
        /// accepts a value.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <param name="element">The element the selector found</param>
        /// <returns>The element to write to; the given element when it accepts a value itself</returns>
        private static AutomationElement WritableFor(Application application, AutomationElement element)
        {
            AutomationElement retVal = element;

            if (false == element.TryGetCurrentPattern(RangeValuePattern.Pattern, out object unused)
                && false == element.TryGetCurrentPattern(ValuePattern.Pattern, out unused))
            {
                AutomationElement inside = element.FindFirst(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.IsRangeValuePatternAvailableProperty, true),
                    new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true)));

                if (null != inside)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] carries no value of its own, so the [{inside.Current.ControlType.ProgrammaticName}] inside it is what takes one.", application, GPALObjectType.Application);

                    retVal = inside;
                }
            }

            return retVal;
        }

        // Search the control's child windows. WinForms publishes a spinner as just two buttons, so its edit box is
        // found through Win32.
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

        private delegate bool EnumChildProc(IntPtr handle, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SendMessage(IntPtr handle, int message, int wParam, System.Text.StringBuilder lParam);

        private const int WM_KILLFOCUS = 0x0008;
        private const int WM_SETTEXT = 0x000C;
        private const int WM_GETTEXT = 0x000D;
        private const int WM_GETTEXTLENGTH = 0x000E;
        private const int EM_SETSEL = 0x00B1;
        private const int EM_REPLACESEL = 0x00C2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private const int SCI_GETLENGTH = 2006;
        private const int SCI_GETCODEPAGE = 2137;
        private const int SCI_SETSEL = 2160;
        private const int SCI_REPLACESEL = 2170;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        /// <summary>
        /// Inserts text at the start or the end of a Scintilla editor (Notepad++), through Scintilla's own messages.
        /// </summary>
        /// <remarks>
        /// Scintilla reads EM_REPLACESEL's text as 8-bit characters, and Windows hands it UTF-16, so it takes the first
        /// character only. SCI_REPLACESEL takes the text in the document's encoding, and Windows copies nothing across
        /// processes for a message it does not know, so the text is written into the editor's process first.
        /// </remarks>
        /// <param name="handle">The Scintilla window</param>
        /// <param name="atEnd">True to add after what is there, false to put it before</param>
        /// <param name="useText">The text to insert</param>
        private static void ScintillaInsert(IntPtr handle, bool atEnd, string useText)
        {
            IntPtr at = true == atEnd ? SendMessage(handle, SCI_GETLENGTH, IntPtr.Zero, IntPtr.Zero) : IntPtr.Zero;
            bool utf8 = 65001 == SendMessage(handle, SCI_GETCODEPAGE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            byte[] bytes = (true == utf8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(0)).GetBytes(useText + "\0");

            GetWindowThreadProcessId(handle, out uint processId);

            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE, false, processId);

            // Nothing written means nothing sent; the read-back afterwards finds the text missing and it is typed.
            if (IntPtr.Zero != process)
            {
                IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, new UIntPtr((uint)bytes.Length), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (IntPtr.Zero != remote)
                {
                    if (true == WriteProcessMemory(process, remote, bytes, new UIntPtr((uint)bytes.Length), out UIntPtr unused))
                    {
                        SendMessage(handle, SCI_SETSEL, at, at);
                        SendMessage(handle, SCI_REPLACESEL, IntPtr.Zero, remote);
                    }

                    VirtualFreeEx(process, remote, UIntPtr.Zero, MEM_RELEASE);
                }

                CloseHandle(process);
            }
        }

        /// <summary>
        /// The text a window is holding, read from the window rather than from anything it publishes.
        /// </summary>
        /// <param name="handle">The window to read</param>
        /// <returns>Its text, empty when it has none</returns>
        private static string NativeTextOf(IntPtr handle)
        {
            int length = SendMessage(handle, WM_GETTEXTLENGTH, 0, null);
            System.Text.StringBuilder text = new System.Text.StringBuilder(length + 1);

            SendMessage(handle, WM_GETTEXT, text.Capacity, text);

            return text.ToString();
        }

        /// <summary>
        /// Whether two texts are the same once their line ends are made alike; a rich edit keeps CR where an edit keeps
        /// CR LF.
        /// </summary>
        /// <param name="held">The text read back from the window</param>
        /// <param name="wanted">The text it should hold</param>
        /// <returns>True when they match line for line</returns>
        private static bool SameLines(string held, string wanted)
        {
            return string.Equals(held.Replace("\r\n", "\n").Replace('\r', '\n'), wanted.Replace("\r\n", "\n").Replace('\r', '\n'));
        }

        /// <summary>
        /// Writes text through the window holding it, for a control with no writable value, such as a multi-line edit
        /// (Notepad's text area). No input is used, so it works on a hidden desktop.
        /// </summary>
        /// <remarks>
        /// For edits whose ValuePattern is read only, such as Notepad's. A replace uses WM_SETTEXT. Append and Insert
        /// put the caret at the end or the start and insert there with EM_REPLACESEL, so the text already there is
        /// never written back.
        /// </remarks>
        /// <param name="application">The application being automated</param>
        /// <param name="element">The control to write into</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">Whether it replaces what is there, goes before it or goes after it</param>
        /// <returns>True when the window holds the text as asked</returns>
        private static bool SetNativeText(Application application, AutomationElement element, string useText, WriteMode writeMode)
        {
            bool retVal = false;
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            // WM_SETTEXT replaces the whole text, so adding to it would mean writing back what was there, and a window
            // can store part of a write and still return TRUE. EM_REPLACESEL inserts at the caret and leaves the rest
            // alone. Read back, with line ends made alike: a rich edit keeps a line end as CR alone.
            if (WriteMode.Overwrite != writeMode && IntPtr.Zero != handle)
            {
                string before = NativeTextOf(handle);
                System.Text.StringBuilder className = new System.Text.StringBuilder(64);

                GetClassName(handle, className, className.Capacity);

                if ("Scintilla" == className.ToString())
                    ScintillaInsert(handle, WriteMode.Append == writeMode, useText);
                else
                {
                    IntPtr at = new IntPtr(WriteMode.Append == writeMode ? before.Length : 0);   // a position past the end is the end

                    SendMessage(handle, EM_SETSEL, at, at);
                    SendMessage(handle, EM_REPLACESEL, new IntPtr(1), useText);
                }

                retVal = SameLines(NativeTextOf(handle), WriteMode.Append == writeMode ? before + useText : useText + before);

                if (true == retVal)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] publishes no value that can be written, so the text was inserted through the window holding it.", application, GPALObjectType.Application);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{element.Current.ControlType.ProgrammaticName}] did not take the text inserted through its window, so it is typed into instead.", application, GPALObjectType.Application);
            }
            else if (IntPtr.Zero != handle)
            {
                // Read the text back instead of trusting TRUE. A window that reads the message as bytes stores only the
                // first character of a wide string and still returns TRUE.
                bool answered = IntPtr.Zero != SendMessage(handle, WM_SETTEXT, IntPtr.Zero, useText);

                retVal = true == answered
                      && (true == string.IsNullOrEmpty(useText) || true == NativeTextOf(handle).Contains(useText));

                if (true == retVal)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] publishes no value that can be written, so the text was written to the window holding it.", application, GPALObjectType.Application);
                else if (true == answered)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{element.Current.ControlType.ProgrammaticName}] said it took the text written to its window but is not holding it, so it is typed into instead.", application, GPALObjectType.Application);
            }

            return retVal;
        }

        /// <summary>
        /// Returns the child window inside a control that holds its value, when UIAutomation shows no value in the
        /// tree.
        /// </summary>
        /// <remarks>
        /// A WinForms spinner lists only its buttons in UIAutomation; its edit is reached by window handle.
        /// </remarks>
        /// <param name="element">The control that offered no value of its own</param>
        /// <returns>The element for the window holding the value; null when no child window holds one</returns>
        private static AutomationElement NativeWritableFor(AutomationElement element)
        {
            AutomationElement retVal = null;
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            if (IntPtr.Zero != handle)
            {
                List<IntPtr> inside = new List<IntPtr>();

                EnumChildWindows(handle, (child, unused) => { inside.Add(child); return true; }, IntPtr.Zero);

                foreach (IntPtr child in inside)
                {
                    AutomationElement fromWindow = AutomationElement.FromHandle(child);

                    // Use ValuePattern only. These windows also offer RangeValuePattern with a wrong range that throws
                    // for any useful value.
                    if (true == fromWindow.TryGetCurrentPattern(ValuePattern.Pattern, out object unused))
                    {
                        retVal = fromWindow;
                        break;
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// Tells a control it lost focus so it reads what was written, as when a person types a number and clicks away.
        /// </summary>
        /// <remarks>
        /// A spinner reads its box only when it loses focus.
        /// </remarks>
        /// <param name="element">The element that was written into</param>
        private static void Commit(AutomationElement element)
        {
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            if (IntPtr.Zero != handle)
                SendMessage(handle, WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Writes a value into the window inside a control that publishes no value to UIAutomation, then tells the box
        /// focus has left so a spinner reads it.
        /// </summary>
        /// <param name="application">The application being automated</param>
        /// <param name="element">The control that offered no value of its own</param>
        /// <param name="value">The value to write</param>
        /// <returns>True when a child window took the value</returns>
        private static bool SetNativeValue(Application application, AutomationElement element, string value)
        {
            bool retVal = false;
            AutomationElement writable = NativeWritableFor(element);

            if (null != writable && true == writable.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
            {
                ((ValuePattern)valuePattern).SetValue(value);

                Commit(writable);

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] keeps its value in a window UIAutomation does not list, so [{value}] was written there and the control was told to read it.", application, GPALObjectType.Application);

                retVal = true;
            }

            return retVal;
        }

        public static void SetRange(Application application, double rangeValue)
        {
            List<GPALAutomationElement> matchedElements;
            bool matchedAll = false;

            foreach (Selector sel in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != sel.SelectorType)
                    continue;

                ElementHelper.FindElements(application, application.CurrentUOW, sel, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                    foreach (GPALAutomationElement elem in matchedElements)
                    {
                        if (null == elem.Ae) continue;

                        // Which part of a spinner holds the value depends on the framework, so try the named element
                        // first and the elements inside it second.
                        AutomationElement writable = WritableFor(application, elem.Ae);
                        bool taken = false;

                        // Try each way of setting the value until one works. A control need not honor a pattern it
                        // offers: a WinForms spinner's RangeValuePattern has the wrong range.
                        if (true == writable.TryGetCurrentPattern(RangeValuePattern.Pattern, out object rangeValuePattern))
                        {
                            try
                            {
                                ((RangeValuePattern)rangeValuePattern).SetValue(rangeValue);

                                taken = true;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"RangeValuePattern on [{writable.Current.ControlType.ProgrammaticName}] refused [{rangeValue}], so the next way of setting it is tried.", application, GPALObjectType.Application, ex);
                            }
                        }

                        if (false == taken && true == writable.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                        {
                            try
                            {
                                ((ValuePattern)valuePattern).SetValue(rangeValue.ToString());

                                // The box may belong to a spinner, and the value does not take effect until the control
                                // reads it.
                                Commit(writable);

                                taken = true;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ValuePattern on [{writable.Current.ControlType.ProgrammaticName}] refused [{rangeValue}], so the next way of setting it is tried.", application, GPALObjectType.Application, ex);
                            }
                        }

                        // If both fail, write to the child window. A WinForms spinner's edit box is a window Windows
                        // returns on request.
                        if (false == taken)
                            taken = SetNativeValue(application, elem.Ae, rangeValue.ToString());

                        // If that fails, type the value with hardware input, which reaches only the desktop on screen.
                        if (false == taken && false == HardwareReaches(application))
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{elem.Ae.Current.ControlType.ProgrammaticName}] offers no value to set through UIAutomation, and its value cannot be typed on a hidden desktop. Peek at the desktop to type it there.", application, GPALObjectType.Application);
                        else if (false == taken)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{elem.Ae.Current.ControlType.ProgrammaticName}] offers no value to set through UIAutomation, so it is typed into instead.", application, GPALObjectType.Application);

                            HardwareFillInFrom(application, elem, sel.OffsetX, sel.OffsetY, rangeValue.ToString(), WriteMode.Overwrite);
                        }
                    }
                }
            }
        }
    }
}
