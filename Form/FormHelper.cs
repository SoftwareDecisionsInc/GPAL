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
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPALForm.GPALForm;
using HWND = System.IntPtr;

namespace GenerallyPositive.GPALForm
{
    internal class FormHelper
    {
        /// <summary>
        /// Writes text into a form control, the way the write mode asks for.
        /// </summary>
        /// <param name="controlEntry">The control to write to</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">Overwrite replaces what is there, Append adds to the end, Insert puts it at the caret</param>
        public static void FillInFrom(GPALControl controlEntry, string useText, WriteMode writeMode)
        {
            Control control = controlEntry.WindowsControl as Control;

            if (null == control)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Control [{controlEntry.Name}] is not on a form yet, so nothing was written to it",
                    null, GPALObjectType.GPALForm);
            else if (FormControlType.Input != controlEntry.ControlType
                  && FormControlType.TextArea != controlEntry.ControlType)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"A [{controlEntry.ControlType}] does not take text, so nothing was written to [{controlEntry.Name}]",
                    null, GPALObjectType.GPALForm);
            else
                GPALControl.OnUi(control, () => control.Text = TextFor(control, useText, writeMode));
        }

        /// <summary>
        /// Returns the control's new text. Append adds to the end, Insert at the caret, Overwrite replaces it.
        /// </summary>
        /// <param name="control">The control being written to, read for its current text and caret</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">Which of the three ways to write it</param>
        /// <returns>The text to set on the control</returns>
        private static string TextFor(Control control, string useText, WriteMode writeMode)
        {
            string current = control.Text ?? string.Empty;
            string retVal = useText;

            if (WriteMode.Append == writeMode)
                retVal = current + useText;
            else if (WriteMode.Insert == writeMode)
            {
                // Only a text box has a caret; otherwise insert at the end.
                int at = control is TextBoxBase box ? box.SelectionStart : current.Length;

                retVal = current.Insert(Math.Min(Math.Max(at, 0), current.Length), useText);
            }

            return retVal;
        }
        public static void FillInWithTokens(GPALForm myForm, List<IGPALGrid<string>> tokenList, WriteMode writeMode)
        {
            foreach (IGPALGrid<string> tokens in tokenList)
            {
                // iterate over every row of tokens, filling in the form's editable controls a row at a time
                for (int tokenIdx = 0; tokenIdx < tokens.Count(); tokenIdx++)
                {
                    int elementIdx = 0;
                    List<string> currentRow = tokens[tokenIdx];

                    foreach (string token in currentRow)                                            // iterate over tokens, filling in the corresponding input/textarea webelement
                    {
                        bool textFilled = false;

                        while (false == textFilled && elementIdx < myForm.ControlList.Count)
                        {
                            switch (myForm.ControlList[elementIdx].ControlType)
                            {
                                case FormControlType.TextArea:
                                case FormControlType.Input:
                                    myForm.Form.SwitchToTab(myForm.ControlList[elementIdx]);    // bring the control's tab into view so the change is visible as it happens
                                    FillInFrom(myForm.ControlList[elementIdx], token, writeMode);
                                    textFilled = true;
                                    break;

                                default:
                                    break;
                            }
                            elementIdx++;
                        }

                        // detect if we have too many tokens to consume and publish an information event [entirely possible this scenario is fine]
                        if (elementIdx == myForm.ControlList.Count && false == textFilled)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unused token: [{token}]", myForm, GPALObjectType.GPALForm);
                    }

                    // detect if we do not have enough tokens for all the form fields [entirely possible this scenario is fine]
                    while (elementIdx < myForm.ControlList.Count)
                    {
                        switch (myForm.ControlList[elementIdx].ControlType)
                        {
                            case FormControlType.TextArea:
                            case FormControlType.Input:
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No token for control: [{myForm.ControlList[elementIdx].Name}]", myForm, GPALObjectType.GPALForm);
                                break;

                            default:
                                break;
                        }
                        elementIdx++;
                    }

                    // CAVEAT: Handled/NotHandled are both ignored here, the handler can request to exit by returning CallIfStatus.Terminate
                    if (null != myForm.Form.CallAfterFillIn)
                    {
                        CallIfStatus handled = myForm.Form.CallAfterFillIn(myForm, tokens, tokenIdx);

                        if (CallIfStatus.Terminate == handled)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CallAfterFillIn handler [{myForm.Form.CallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.", myForm, GPALObjectType.GPALForm);
                            throw new GPALException($"{GPAL.MyMethodName()}: CallAfterFillIn handler [{myForm.Form.CallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.");
                        }
                    }
                }
            }
        }
        public static HWND WaitForWindow(string windowTitle, int waitTimeInSeconds = 10)
        {
            return WaitForWindowGeneric(windowTitle, false, waitTimeInSeconds);
        }

        public static HWND WaitForWindowRegex(string windowTitleRegex, int waitTimeInSeconds)
        {
            return WaitForWindowGeneric(windowTitleRegex, true, waitTimeInSeconds);
        }

        public static HWND WaitForWindowGeneric(string waitForTitle, bool useRegex, int waitTimeInSeconds)
        {
            bool found = false;
            HWND retHWND = IntPtr.Zero;

            for (int cnt = 0; cnt < waitTimeInSeconds && false == found; cnt++)
            {
                Thread.Sleep(1000); // best to sleep first and give a second more for the window to appear
                IDictionary<HWND, string> windows = GetOpenWindows();
                foreach (var entry in windows)
                {
                    if (true == useRegex)
                    {
                        MatchCollection mc = Regex.Matches(entry.Value, waitForTitle);
                        if (0 < mc.Count)
                        {
                            found = true;
                            retHWND = entry.Key;
                            break;
                        }
                    }
                    else
                    {
                        if (true == waitForTitle.Equals(entry.Value))
                        {
                            found = true;
                            retHWND = entry.Key;
                            break;
                        }
                    }
                }
            }
            return retHWND;
        }

        /// <summary>
        /// Waits for a common dialog (window class #32770) belonging to the given process, whatever its title.
        /// </summary>
        /// <param name="processId">The process whose dialog is being waited for</param>
        /// <param name="waitTimeInSeconds">How long to keep looking</param>
        /// <returns>The dialog's window, or zero when none appeared</returns>
        public static HWND WaitForCommonDialog(int processId, int waitTimeInSeconds = 10)
        {
            HWND retHWND = IntPtr.Zero;
            Stopwatch waited = Stopwatch.StartNew();

            // Dialogs already on screen before waiting started. They share class #32770, so when each appeared is what
            // tells them apart.
            var before = new HashSet<HWND>(CommonDialogsOf(0));

            while (IntPtr.Zero == retHWND && waited.ElapsedMilliseconds < waitTimeInSeconds * 1000)
            {
                List<HWND> appeared = new List<HWND>();

                // A dialog qualifies if it appeared while waiting, or was already there and looks like a file dialog.
                // The second case catches one that opened before the first check, which a warmed-up browser does on
                // every later download.
                foreach (HWND window in CommonDialogsOf(0))
                    if (false == before.Contains(window) || true == LooksLikeAFileDialog(window))
                        appeared.Add(window);

                // among the new ones, the one belonging to the process that was asked about
                foreach (HWND window in appeared)
                {
                    GetWindowThreadProcessId(window, out int owner);

                    if (IntPtr.Zero == retHWND && owner == processId)
                        retHWND = window;
                }

                // Otherwise take any new dialog. The owning process is only a preference; a driver-started or
                // relaunched browser owns its windows from a process we were never given.
                if (IntPtr.Zero == retHWND && 0 < appeared.Count)
                {
                    GetWindowThreadProcessId(appeared[0], out int owner);

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"A common dialog appeared belonging to process [{owner}] rather than [{processId}], and is taken as the one that was waited for.", null, GPALObjectType.None);

                    retHWND = appeared[0];
                }

                if (IntPtr.Zero == retHWND)
                    Thread.Sleep(250);
            }

            if (IntPtr.Zero == retHWND)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No common dialog for process [{processId}] in [{waitTimeInSeconds}]s. {VisibleWindows()}", null, GPALObjectType.None);

            return retHWND;
        }

        /// <summary>
        /// Returns a window's title, for messages only.
        /// </summary>
        /// <param name="window">The window to read</param>
        /// <returns>The title; empty when it has none</returns>
        public static string TitleOf(HWND window)
        {
            var title = new StringBuilder(256);

            GetWindowText(window, title, title.Capacity);

            return title.ToString();
        }

        /// <summary>
        /// Lists every visible top-level window by class, title and owning process, for when a dialog did not appear.
        /// </summary>
        /// <returns>One entry per window</returns>
        public static string VisibleWindows()
        {
            var seen = new List<string>();

            EnumWindows(delegate (HWND hWnd, int lParam)
            {
                if (true == IsWindowVisible(hWnd))
                {
                    var className = new StringBuilder(256);
                    var title = new StringBuilder(256);

                    GetClassName(hWnd, className, className.Capacity);
                    GetWindowText(hWnd, title, title.Capacity);
                    GetWindowThreadProcessId(hWnd, out int owner);

                    if (0 < className.Length)
                        seen.Add($"[{className}] '{title}' (process {owner})");
                }

                return true;

            }, 0);

            return 0 == seen.Count ? "No visible windows were found at all." : $"What is on screen: {string.Join(" | ", seen)}";
        }

        /// <summary>
        /// Every visible common dialog owned by the given process.
        /// </summary>
        /// <param name="processId">The process to look for, or zero for any</param>
        /// <returns>The windows found, in the order Windows enumerated them</returns>
        public static List<HWND> CommonDialogsOf(int processId)
        {
            var found = new List<HWND>();

            EnumWindows(delegate (HWND hWnd, int lParam)
            {
                if (true == IsWindowVisible(hWnd))
                {
                    var className = new StringBuilder(64);

                    GetClassName(hWnd, className, className.Capacity);

                    if (COMMON_DIALOG_CLASS == className.ToString())
                    {
                        GetWindowThreadProcessId(hWnd, out int owner);

                        if (0 == processId || owner == processId)
                            found.Add(hWnd);
                    }
                }

                return true;

            }, 0);

            return found;
        }

        /// <summary>
        /// True when a dialog is a file dialog: an accept button with control id 1 and a place to type a name.
        /// </summary>
        /// <param name="dialog">The dialog to check</param>
        /// <returns>True when it has both an accept button and somewhere to type</returns>
        public static bool LooksLikeAFileDialog(HWND dialog)
        {
            bool accept = false;
            bool typing = false;

            EnumChildWindows(dialog, delegate (HWND child, int lParam)
            {
                if (ACCEPT_CONTROL_ID == GetDlgCtrlID(child))
                    accept = true;

                var className = new StringBuilder(64);

                GetClassName(child, className, className.Capacity);

                string name = className.ToString();

                if ("Edit" == name || true == name.StartsWith("ComboBox"))
                    typing = true;

                return true;

            }, 0);

            return true == accept && true == typing;
        }

        // the class every Open, Save As and folder picker has carried since Windows 3
        private const string COMMON_DIALOG_CLASS = "#32770";

        // IDOK, the accept button on any dialog that has one
        private const int ACCEPT_CONTROL_ID = 1;

        [DllImport("USER32.DLL")]
        private static extern bool EnumChildWindows(HWND parent, EnumWindowsProc enumFunc, int lParam);

        [DllImport("USER32.DLL")]
        private static extern int GetDlgCtrlID(HWND hWnd);

        [DllImport("USER32.DLL")]
        private static extern int GetClassName(HWND hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowThreadProcessId(HWND hWnd, out int processId);

        // https://stackoverflow.com/questions/7268302/get-the-titles-of-all-open-windows
        /// <summary>Contains functionality to get all the open windows.</summary>
        /// <summary>Returns a dictionary that contains the handle and title of all the open windows.</summary>
        /// <returns>A dictionary that contains the handle and title of all the open windows.</returns>
        public static IDictionary<HWND, string> GetOpenWindows()
        {
            HWND shellWindow = GetShellWindow();
            Dictionary<HWND, string> windows = new Dictionary<HWND, string>();

            EnumWindows(delegate (HWND hWnd, int lParam)
            {
                if (hWnd == shellWindow) return true;
                if (!IsWindowVisible(hWnd)) return true;

                int length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                StringBuilder builder = new StringBuilder(length);
                GetWindowText(hWnd, builder, length + 1);

                windows[hWnd] = builder.ToString();
                return true;

            }, 0);

            return windows;
        }

        private delegate bool EnumWindowsProc(HWND hWnd, int lParam);

        [DllImport("USER32.DLL")]
        private static extern bool EnumWindows(EnumWindowsProc enumFunc, int lParam);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowText(HWND hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("USER32.DLL")]
        private static extern int GetWindowTextLength(HWND hWnd);

        [DllImport("USER32.DLL")]
        private static extern bool IsWindowVisible(HWND hWnd);

        [DllImport("USER32.DLL")]
        private static extern IntPtr GetShellWindow();
    }
}

