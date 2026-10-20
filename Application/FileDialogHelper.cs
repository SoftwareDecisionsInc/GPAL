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
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using static GenerallyPositive.Enums;
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    /// <summary>
    /// Drives the Windows common file dialogs: Open, Save As and the folder picker.
    /// </summary>
    /// <remarks>
    /// Recognised by contents, not title or class: an accept button with AutomationId 1 plus a file name field or
    /// folder tree. Answered through ValuePattern and InvokePattern, so it needs no focus. The FolderBrowserDialog tree
    /// picker is walked one folder at a time.
    /// </remarks>
    internal static class FileDialogHelper
    {
        // The file name field and accept button ids. The field usually has 1148, on itself or its combo box; some Save
        // dialogs use 1152.
        private const string FILE_NAME_ID = "1148";
        private const string FILE_NAME_ALTERNATE_ID = "1152";
        private const string FILE_NAME_HOST_ID = "FileNameControlHost";
        private const string ACCEPT_ID = "1";

        // how many of a window's buttons the "what is this dialog" message lists before it stops
        private const int BUTTONS_LISTED = 8;

        // the Win32 class every dialog button is, whatever UIAutomation decides to call the control
        private const string BUTTON_CLASS = "Button";

        // IDCANCEL, the id a common dialog's Cancel button carries the way accept carries 1
        private const string CANCEL_ID = "2";

        /// <summary>
        /// Whether a window still exists. Asks Windows, not UIAutomation; an element whose window is gone cannot be
        /// queried and raises a debugging assistant.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        private const int BETWEEN_LOOKS = 250;

        // How long to wait for a dialog to close after it is accepted. A dialog that took the value closes at once, so
        // ten seconds would look like a hang.
        private const int SETTLING_MS = 5000;

        // how often to ask whether the dialog has closed, which is one cheap call to Windows
        private const int BETWEEN_CLOSING_LOOKS = 50;


        /// <summary>
        /// Waits for a common file dialog to appear and answers it with the given file or folder.
        /// </summary>
        /// <param name="application">The Application that raised the dialog</param>
        /// <param name="file">The file or folder to answer with. More than one file is quoted and separated,
        /// which is what an Open dialog set to allow several expects</param>
        /// <param name="waitSeconds">How long to wait for the dialog to appear</param>
        /// <returns>True when the dialog was answered and closed</returns>
        internal static bool Answer(Application application, GPALFile file, int waitSeconds)
        {
            bool retVal = false;

            AutomationElement dialog = WaitForDialog(application, waitSeconds);

            if (null == dialog)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No file dialog appeared within [{waitSeconds}]s, so there was nothing to answer. {WindowsSeen(application)}", application, GPALObjectType.Application);
            else if (null == file || 0 == file.Count)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "A file dialog appeared but no file was named to answer it with.", application, GPALObjectType.Application);
            else
            {
                AutomationElement field = FileNameField(dialog);

                if (null != field)
                    retVal = AnswerByField(application, dialog, field, file);
                else
                    retVal = AnswerByTree(application, dialog, file);
            }

            // An unanswered dialog is modal and takes every later click and keystroke. Cancel it so the failure stays
            // just a failure.
            if (false == retVal && null != dialog)
                Cancel(application, dialog);

            return retVal;
        }

        /// <summary>
        /// Returns the common file dialog once one appears, searching the application's window first, then the desktop
        /// (Notepad++ puts its Open and Save As inside its own window).<br/>
        /// The dialog is identified by its contents, not class or title: an accept button with AutomationId 1, plus a
        /// file name field or a folder tree.
        /// </summary>
        /// <param name="application">The Application that raised it</param>
        /// <param name="waitSeconds">How long to keep looking</param>
        /// <returns>The dialog, or null when none appeared</returns>
        private static AutomationElement WaitForDialog(Application application, int waitSeconds)
        {
            AutomationElement retVal = null;
            Stopwatch waited = Stopwatch.StartNew();

            while (null == retVal && waited.ElapsedMilliseconds < waitSeconds * 1000)
            {
                retVal = FirstWindowWhere(application, LooksLikeADialog);

                if (null == retVal)
                    Thread.Sleep(BETWEEN_LOOKS);
            }

            return retVal;
        }

        /// <summary>
        /// Returns the first window the caller recognizes, searching the application's window first, then the desktop.
        /// </summary>
        /// <remarks>
        /// Never sweeps the whole desktop's descendants.
        /// </remarks>
        /// <param name="application">The Application being driven</param>
        /// <param name="matches">What makes a window the one being looked for</param>
        /// <returns>The window, or null when none matched</returns>
        private static AutomationElement FirstWindowWhere(Application application, Func<AutomationElement, bool> matches)
        {
            AutomationElement retVal = null;
            var isWindow = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);

            if (null != application?.RootAutomationElement)
            {
                retVal = FirstWindowUnder(application.RootAutomationElement, TreeScope.Children, isWindow, matches);

                if (null == retVal)
                    retVal = FirstWindowUnder(application.RootAutomationElement, TreeScope.Descendants, isWindow, matches);
            }

            if (null == retVal)
                foreach (AutomationElement top in TopLevelWindows(application))
                    if (true == matches(top))
                    {
                        retVal = top;
                        break;
                    }

            return retVal;
        }

        /// <summary>
        /// Returns the top-level windows a dialog could be, on the program's own desktop.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <returns>The windows to check, in the order the desktop lists them</returns>
        private static List<AutomationElement> TopLevelWindows(Application application)
        {
            List<AutomationElement> retVal = new List<AutomationElement>();

            if (false == application.applicationSettings.HiddenDesktop)
            {
                var isWindow = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);

                foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, isWindow))
                    retVal.Add(window);
            }
            else
                foreach (IntPtr handle in Desktops.WindowsOn(application.applicationSettings.HiddenDesktopName))
                {
                    AutomationElement onDesktop = AutomationElement.FromHandle(handle);

                    if (null != onDesktop)
                        retVal.Add(onDesktop);
                }

            return retVal;
        }

        /// <summary>
        /// The first window under the given element that the caller recognises.
        /// </summary>
        /// <param name="under">The element to look under</param>
        /// <param name="scope">How deep to look</param>
        /// <param name="isWindow">The window condition, built once by the caller</param>
        /// <param name="matches">What makes a window the one being looked for</param>
        /// <returns>The window, null when nothing under there matched</returns>
        private static AutomationElement FirstWindowUnder(AutomationElement under, TreeScope scope, Condition isWindow, Func<AutomationElement, bool> matches)
        {
            AutomationElement retVal = null;

            try
            {
                foreach (AutomationElement window in under.FindAll(scope, isWindow))
                    if (null == retVal && true == matches(window))
                        retVal = window;
            }
            catch (Exception)
            {
                // Skip a window that closed while the tree was being read.
            }

            return retVal;
        }

        /// <summary>
        /// The condition for an accept button: AutomationId 1, on a button.
        /// </summary>
        /// <remarks>
        /// Button by control type or by Win32 class, because Notepad++ publishes its Open button as a Pane. The id
        /// matters too: the file list also carries AutomationId 1.
        /// </remarks>
        /// <returns>The condition, built new each call because a Condition cannot be reused across calls</returns>
        private static Condition AcceptCondition()
        {
            return ButtonCondition(ACCEPT_ID);
        }

        /// <summary>The condition a dialog button carrying the given AutomationId answers to.</summary>
        /// <param name="id">The AutomationId the button carries</param>
        /// <returns>The condition, built fresh because a Condition is not reusable across calls</returns>
        private static Condition ButtonCondition(string id)
        {
            return new AndCondition(
                new PropertyCondition(AutomationElement.AutomationIdProperty, id),
                new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.ClassNameProperty, BUTTON_CLASS)));
        }

        /// <summary>
        /// Returns the dialog's accept button from the control view, then the raw view; a dialog can leave its buttons
        /// out of the control view.
        /// </summary>
        /// <remarks>
        /// The raw view search covers direct children only; the file list's raw view is too large to walk.
        /// </remarks>
        /// <param name="window">The window to search</param>
        /// <returns>The accept button, or null when neither view has one</returns>
        private static AutomationElement AcceptButton(AutomationElement window)
        {
            return ButtonWithId(window, ACCEPT_ID);
        }

        /// <summary>
        /// A dialog button by its AutomationId, looked for in the control view and then in the raw view.
        /// </summary>
        /// <param name="window">The window to look in</param>
        /// <param name="id">The AutomationId the button carries</param>
        /// <returns>The button, or null when neither view has one</returns>
        private static AutomationElement ButtonWithId(AutomationElement window, string id)
        {
            AutomationElement retVal = window.FindFirst(TreeScope.Descendants, ButtonCondition(id));

            if (null == retVal)
            {
                System.Collections.Generic.List<AutomationElement> raw = ElementHelper.RawMatches(window, ButtonCondition(id), false);

                retVal = 0 < raw.Count ? raw[0] : null;
            }

            return retVal;
        }

        /// <summary>
        /// True when a window is a common file dialog: an accept button with AutomationId 1 next to a file name field
        /// or folder tree.
        /// </summary>
        /// <param name="window">The window to check</param>
        /// <returns>True when it is a dialog this can answer</returns>
        private static bool LooksLikeADialog(AutomationElement window)
        {
            bool retVal = false;

            AutomationElement accept = AcceptButton(window);

            if (null != accept)
                retVal = null != FileNameField(window)
                      || null != window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree));

            return retVal;
        }

        /// <summary>
        /// Lists every window checked and why it was not the dialog, from the same three places the search uses, so the
        /// message shows whether the dialog was missing or not recognized.
        /// </summary>
        /// <param name="application">The Application that raised it</param>
        /// <returns>Each window's class, name, and whether it has an accept button, a field and a tree</returns>
        private static string WindowsSeen(Application application)
        {
            var seen = new List<string>();

            if (null != application?.RootAutomationElement)
            {
                Describe(seen, "under the application", application.RootAutomationElement, TreeScope.Children);
                Describe(seen, "deeper in the application", application.RootAutomationElement, TreeScope.Descendants);
            }

            foreach (AutomationElement top in TopLevelWindows(application))
                Describe(seen, true == application.applicationSettings.HiddenDesktop
                    ? $"on [{application.applicationSettings.HiddenDesktopName}]"
                    : "on the desktop", top, TreeScope.Element);

            return 0 == seen.Count ? "No windows were found in any of the places a dialog is looked for." : $"Windows looked at: {string.Join(" | ", seen)}";
        }

        /// <summary>
        /// Adds a line per window under the given element, saying what each one holds.
        /// </summary>
        /// <param name="seen">The lines so far</param>
        /// <param name="where">Which of the places being searched this is</param>
        /// <param name="under">The element to look under</param>
        /// <param name="scope">How deep to look</param>
        private static void Describe(List<string> seen, string where, AutomationElement under, TreeScope scope)
        {
            try
            {
                // Check the window itself when the scope is Element; a top-level window on a hidden desktop does not
                // always report itself as a Window.
                AutomationElementCollection windows = TreeScope.Element == scope
                    ? under.FindAll(scope, Condition.TrueCondition)
                    : under.FindAll(scope, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

                foreach (AutomationElement window in windows)
                {
                    bool accept = null != AcceptButton(window);

                    int edits = window.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)).Count;

                    bool named = null != window.FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.AutomationIdProperty, FILE_NAME_ID)));

                    bool tree = null != window.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree));

                    seen.Add($"{where}: [{window.Current.ClassName}] '{window.Current.Name}' accept button {(true == accept ? "yes" : $"NO, its buttons are {ButtonsIn(window)}")}, field {FILE_NAME_ID} {(true == named ? "yes" : "NO")}, edits [{edits}], tree {(true == tree ? "yes" : "no")}");
                }
            }
            catch (Exception)
            {
                // Skip a window that closed while the list was being read.
            }
        }

        /// <summary>
        /// Lists a window's buttons by AutomationId and name, so a failed message shows what id the accept button
        /// actually has.
        /// </summary>
        /// <param name="window">The window whose buttons to list</param>
        /// <returns>The buttons as id and name pairs</returns>
        private static string ButtonsIn(AutomationElement window)
        {
            var named = new System.Collections.Generic.List<string>();

            AutomationElementCollection buttons = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            for (int button = 0; button < buttons.Count && named.Count < BUTTONS_LISTED; button++)
                named.Add($"[{buttons[button].Current.AutomationId}] '{buttons[button].Current.Name}'");

            if (BUTTONS_LISTED <= named.Count && BUTTONS_LISTED < buttons.Count)
                named.Add($"and [{buttons.Count - BUTTONS_LISTED}] more");

            return 0 == named.Count ? "none in the control view" : string.Join(" ", named);
        }

        /// <summary>
        /// Returns the file name field, which every dialog except the old folder tree has.
        /// </summary>
        /// <remarks>
        /// Tried by AutomationId 1148, by the combo box hosting it, by 1152, then by position beside the accept button.
        /// </remarks>
        /// <param name="dialog">The dialog to search</param>
        /// <returns>The field; null when the dialog has none</returns>
        private static AutomationElement FileNameField(AutomationElement dialog)
        {
            var isEdit = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);

            // by its id, directly
            AutomationElement retVal = dialog.FindFirst(TreeScope.Descendants, new AndCondition(isEdit,
                new PropertyCondition(AutomationElement.AutomationIdProperty, FILE_NAME_ID)));

            // by its id, on the combo box that hosts it
            if (null == retVal)
            {
                AutomationElement host = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
                    new OrCondition(
                        new PropertyCondition(AutomationElement.AutomationIdProperty, FILE_NAME_ID),
                        new PropertyCondition(AutomationElement.AutomationIdProperty, FILE_NAME_HOST_ID))));

                if (null != host)
                    retVal = host.FindFirst(TreeScope.Descendants, isEdit);
            }

            // The alternate file name field id (FILE_NAME_ALTERNATE_ID).
            if (null == retVal)
                retVal = dialog.FindFirst(TreeScope.Descendants, new AndCondition(isEdit,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, FILE_NAME_ALTERNATE_ID)));

            // and by where it sits, which no version or language changes
            if (null == retVal)
                retVal = FieldBesideTheButton(dialog, isEdit);

            return retVal;
        }

        /// <summary>
        /// The writable field on the same line as the accept button, which is the file name field whatever it
        /// calls itself.
        /// </summary>
        /// <param name="dialog">The dialog to look in</param>
        /// <param name="isEdit">The Edit condition, built once by the caller</param>
        /// <returns>The field, null when nothing writable lines up with the button</returns>
        private static AutomationElement FieldBesideTheButton(AutomationElement dialog, Condition isEdit)
        {
            AutomationElement retVal = null;

            AutomationElement accept = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.AutomationIdProperty, ACCEPT_ID)));

            if (null != accept)
            {
                System.Windows.Rect button = accept.Current.BoundingRectangle;
                double nearest = double.MaxValue;

                foreach (AutomationElement edit in dialog.FindAll(TreeScope.Descendants, isEdit))
                {
                    // Writable, on screen, and left of the button on the same row; the address bar and search box are
                    // on other rows.
                    if (false == edit.Current.IsOffscreen
                        && true == edit.Current.IsEnabled
                        && true == edit.TryGetCurrentPattern(ValuePattern.Pattern, out object unused))
                    {
                        System.Windows.Rect field = edit.Current.BoundingRectangle;
                        double apart = Math.Abs((field.Top + field.Height / 2) - (button.Top + button.Height / 2));

                        if (field.Left < button.Left && apart < button.Height && apart < nearest)
                        {
                            nearest = apart;
                            retVal = edit;
                        }
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// Puts the path in the file name field and presses the accept button.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <param name="dialog">The dialog</param>
        /// <param name="field">Its file name field</param>
        /// <param name="file">The file or folder to answer with</param>
        /// <returns>True when the dialog closed</returns>
        private static bool AnswerByField(Application application, AutomationElement dialog, AutomationElement field, GPALFile file)
        {
            bool retVal = false;

            // One file is used as is. Several files are quoted and space-separated, as a multi-select dialog reads
            // them.
            var names = new List<string>();

            foreach (string name in file.Filenames)
                names.Add(1 == file.Count ? name : $"\"{name}\"");

            string answer = string.Join(" ", names);

            if (true == field.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
            {
                ((ValuePattern)valuePattern).SetValue(answer);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answered the file dialog with [{answer}]", application, GPALObjectType.Application);

                retVal = Accept(application, dialog);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "The file dialog's file name field cannot be written to, so the path could not be put in it.", application, GPALObjectType.Application);

            return retVal;
        }

        /// <summary>
        /// Walks the old folder picker's tree one path segment at a time, expanding each to reach the next, then
        /// presses the accept button.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <param name="dialog">The dialog</param>
        /// <param name="file">The folder to select</param>
        /// <returns>True when the dialog closed</returns>
        private static bool AnswerByTree(Application application, AutomationElement dialog, GPALFile file)
        {
            bool retVal = false;

            AutomationElement tree = dialog.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tree));

            if (null == tree)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The dialog has neither a file name field nor a folder tree, so it is not a common file dialog this can answer. {ElementHelper.WhatIsThere(dialog)}", application, GPALObjectType.Application);
            else
            {
                AutomationElement current = tree;
                string reached = string.Empty;

                foreach (string segment in file.Filename.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    AutomationElement next = ChildNamed(current, segment);

                    if (null == next)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The folder tree has no [{segment}] under [{(true == string.IsNullOrEmpty(reached) ? "the root" : reached)}], so [{file.Filename}] cannot be reached. {ElementHelper.WhatIsThere(current)}", application, GPALObjectType.Application);
                        current = null;
                        break;
                    }

                    Expand(next);

                    current = next;
                    reached = segment;
                }

                if (null != current)
                {
                    Select(current);

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Walked the folder tree to [{file.Filename}]", application, GPALObjectType.Application);

                    retVal = Accept(application, dialog);
                }
            }

            return retVal;
        }

        /// <summary>
        /// A tree item by name, among the children of the one given. A drive is matched on the letter it starts
        /// with, since the tree shows it as a label with the letter in brackets after it.
        /// </summary>
        /// <param name="parent">The item, or the tree, to look under</param>
        /// <param name="segment">The path segment to find</param>
        /// <returns>The item, null when there is no such child</returns>
        private static AutomationElement ChildNamed(AutomationElement parent, string segment)
        {
            AutomationElement retVal = parent.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
                new PropertyCondition(AutomationElement.NameProperty, segment)));

            if (null == retVal)
                foreach (AutomationElement child in parent.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem)))
                {
                    string name = child.Current.Name ?? string.Empty;

                    if (null == retVal && (true == name.EndsWith($"({segment})") || true == name.StartsWith($"{segment} ", StringComparison.OrdinalIgnoreCase)))
                        retVal = child;
                }

            return retVal;
        }

        /// <summary>
        /// Opens a tree item so its children exist to be found.
        /// </summary>
        /// <param name="item">The item to open</param>
        private static void Expand(AutomationElement item)
        {
            if (true == item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object pattern)
                && ExpandCollapseState.Collapsed == ((ExpandCollapsePattern)pattern).Current.ExpandCollapseState)
                ((ExpandCollapsePattern)pattern).Expand();
        }

        /// <summary>
        /// Selects a tree item, which is what the dialog reads when it is accepted.
        /// </summary>
        /// <param name="item">The item to select</param>
        private static void Select(AutomationElement item)
        {
            if (true == item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object pattern))
                ((SelectionItemPattern)pattern).Select();
        }

        /// <summary>
        /// Presses the dialog's accept button (AutomationId 1 whatever its label) and waits for the dialog to close.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <param name="dialog">The dialog to accept</param>
        /// <returns>True when the dialog closed</returns>
        private static bool Accept(Application application, AutomationElement dialog)
        {
            bool retVal = PressAndWait(application, dialog, AcceptButton(dialog), "accept");

            return retVal;
        }

        /// <summary>
        /// Presses Cancel (AutomationId 2) on a dialog that could not be answered; left open, a modal dialog takes
        /// every later click and keystroke.
        /// </summary>
        /// <param name="application">The Application that raised it</param>
        /// <param name="dialog">The dialog to close</param>
        private static void Cancel(Application application, AutomationElement dialog)
        {
            AutomationElement button = ButtonWithId(dialog, CANCEL_ID);

            if (true == PressAndWait(application, dialog, button, "cancel"))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "The file dialog could not be answered, so it was cancelled. The application is where it was before the dialog was raised.", application, GPALObjectType.Application);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "The file dialog could not be answered and could not be cancelled either, so it is still on screen and everything after this goes into it.", application, GPALObjectType.Application);
        }

        /// <summary>
        /// Presses a dialog button and waits for the dialog to close.
        /// </summary>
        /// <param name="application">The Application that raised the dialog</param>
        /// <param name="dialog">The dialog being closed</param>
        /// <param name="button">The button to press</param>
        /// <param name="what">What this button does, for the messages</param>
        /// <returns>True when the button was pressed and the dialog closed</returns>
        private static bool PressAndWait(Application application, AutomationElement dialog, AutomationElement button, string what)
        {
            bool retVal = false;
            bool pressed = false;

            // Read this while the dialog still exists; after it closes every query goes to a dead proxy.
            IntPtr handle = new IntPtr(dialog.Current.NativeWindowHandle);

            if (null == button)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The file dialog has no [{what}] button. [{ElementHelper.WhatIsThere(dialog)}]", application, GPALObjectType.Application);
            else if (true == button.TryGetCurrentPattern(InvokePattern.Pattern, out object pattern))
            {
                ((InvokePattern)pattern).Invoke();

                pressed = true;
            }
            else if (true == MsaaHelper.Press(application, button))
                pressed = true;
            else if (true == ElementHelper.HardwareReaches(application))
            {
                ElementHelper.HardwareClick(application, new GPALAutomationElement(button), ClickType.LeftClick, ModifierKeys.NONE);

                pressed = true;
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The file dialog's [{what}] button cannot be pressed and the mouse cannot reach it here.", application, GPALObjectType.Application);

            if (true == pressed)
            {
                retVal = Settled(handle);

                if (false == retVal)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The file dialog's [{what}] button was pressed but the dialog is still on screen after [{SETTLING_MS / 1000}]s.", application, GPALObjectType.Application);
            }

            return retVal;
        }

        /// <summary>
        /// Waits for the dialog to close, which shows it accepted the value.
        /// </summary>
        /// <param name="handle">The dialog's window handle, read while it still existed</param>
        /// <returns>True once the dialog has closed</returns>
        private static bool Settled(IntPtr handle)
        {
            bool retVal = false;
            Stopwatch waited = Stopwatch.StartNew();

            while (false == retVal && waited.ElapsedMilliseconds < SETTLING_MS)
            {
                retVal = IntPtr.Zero == handle || false == IsWindow(handle);

                if (false == retVal)
                    Thread.Sleep(BETWEEN_CLOSING_LOOKS);
            }

            return retVal;
        }

    }
}
