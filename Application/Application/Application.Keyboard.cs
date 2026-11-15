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
        /// Append the data from the database to the end of the input Selectors defined in the UOW<br/>
        /// One token (one column in the current row) per per input.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, inputDatabase.Tokens, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Append the data from the input file to the end of the input Selectors defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per per input.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, ((IGPALFileInternal)inputFile).TokenList, WriteMode.Append);

            return this;
        }
       /// <summary>
        /// Append the data from the input grid to the end of the input Selectors defined in the UOW<br/>
        /// One token (one string from the current row) per per input.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, inputGrid, WriteMode.Append);

            return this;
        }
        /// <summary>
        /// Append the given text to the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(string textToUse)
        {
            if (true == InAction(true))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// FillIn data from the database overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);

            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Overwrite);

            return this;
        }
        /// <summary>
        /// FillIn data from the file overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, ((IGPALFileInternal)CurrentUOW.InputFile).TokenList, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// FillIn data from the grid overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, (IGPALGrid<string>)inputGrid, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// FillIn the text from the string overwriting text in the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(string textToUse)
        {
            if (true == InAction(true))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Focus the current element found via the Selector. Only the first element per Selector found will be focused.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, for text inputs, focus is a click in the control. Otherwise, hardware emulation hovers the mouse over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then focus will interate over every {num} elements that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector Focus(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.Focus(this);
            return this;
        }
        /// <summary>
        /// Insert the text from the string at the beginning of the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// Example: If three inputs are defined and three 'InsertFrom' are defined, then the text is assigned to the same ordinal input element found via the Selector(s)
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the corresponding Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(string textToUse)
        {
            if (true == InAction(true))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Replace the text in the first input in the UOW with what is on the clipboard, in the form GetClipboard
        /// named. FileList fills the copied paths; an image fills nothing.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(notesBox)
        ///         .FillInFrom(GPAL.Clipboard.GetClipboard(ClipboardType.Text));
        /// </code>
        /// </example>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(IAllowClipboardInto clipboard)
        {
            if (true == InAction(true))
            {
                string text = ((GPALClipboard)clipboard).TextToFill();

                if (null != text)
                    ElementHelper.FillInFrom(this, text, WriteMode.Overwrite);
            }

            return this;
        }
        /// <summary>
        /// Add what is on the clipboard to the end of the first input in the UOW, in the form GetClipboard named.
        /// FileList fills the copied paths; an image fills nothing.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(IAllowClipboardInto clipboard)
        {
            if (true == InAction(true))
            {
                string text = ((GPALClipboard)clipboard).TextToFill();

                if (null != text)
                    ElementHelper.FillInFrom(this, text, WriteMode.Append);
            }

            return this;
        }
        /// <summary>
        /// Insert what is on the clipboard at the caret of the first input in the UOW, in the form GetClipboard
        /// named. FileList fills the copied paths; an image fills nothing.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(IAllowClipboardInto clipboard)
        {
            if (true == InAction(true))
            {
                string text = ((GPALClipboard)clipboard).TextToFill();

                if (null != text)
                    ElementHelper.FillInFrom(this, text, WriteMode.Insert);
            }

            return this;
        }
        /// <summary>
        /// Insert the text from the database at the beginning of the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Insert the data from the grid at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, (IGPALGrid<string>)inputGrid, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Insert the data from the file at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, ((IGPALFileInternal)CurrentUOW.InputFile).TokenList, WriteMode.Insert);
            return this;
        }

        /// <summary>
        /// Press and hold a modifier key (Alt, Control, Shift) using hardware emulation.
        /// </summary>
        /// <param name="modifierKeys">Alt, Control, Shift</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector PressModifierKey(ModifierKeys modifierKeys)
        {
            // the real keyboard reaches only the desktop on screen
            if (false == ElementHelper.HardwareReaches(this))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{Browser.Browser.GetModifierKeys(modifierKeys)}] cannot be pressed on a hidden desktop, since it is the real keyboard. Peek at the desktop to use it.", this, GPALObjectType.Application);
            else
                HardwareHelper.PressModifierKey(modifierKeys);

            return this;
        }

        /// <summary>
        /// Release a previously pressed modifier key (Alt, Control, Shift) using hardware emulation.
        /// </summary>
        /// <param name="modifierKeys">Alt, Control, Shift</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            // nothing was pressed on a hidden desktop, so there is nothing to release
            if (true == ElementHelper.HardwareReaches(this))
                HardwareHelper.ReleaseModifierKeys(modifierKeys);

            return this;
        }

        /// <summary>
        /// Type special characters like Enter, Page Up, Page Down, Tab, etc.<br/>
        /// Use GPAL.VK constants for ease of access to commmon keys.<br/>
        /// For all Virtual Keycodes, visit:<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        /// </summary>
        /// <example>
        /// <code>
        /// // send CTRL-HOME
        ///     ...
        ///     myBrowser
        ///         .PressModifierKey(ModifierKeys.Control)
        ///         .SendKey(GPAL.VK_HOME);
        ///         .WaitFor(150)
        ///         .ReleaseModifierKey(ModifierKeys.Control)
        /// </code>
        /// </example>
        /// <param name="VKCode">Virtual Keycode</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector SendKey(byte VKCode)
        {
            if (true == InAction(true))
            {
                // the real keyboard reaches only the desktop on screen
                if (false == ElementHelper.HardwareReaches(this))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Key [{VKCode}] cannot be sent on a hidden desktop, since it is the real keyboard. Peek at the desktop to send it.", this, GPALObjectType.Application);
                else
                {
                    HardwareHelper.SendKey(VKCode);

                    // SendKey only posts the keystroke. Wait for the application to process it so a chain of sends
                    // arrives in order.
                    ElementHelper.SettleInput(this);
                }
            }

            return this;
        }
        /// <summary>
        /// Send the string using hardware emulation.
        /// </summary>
        /// <param name="textToSend">The text to send</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector SendString(string textToSend)
        {
            // the real keyboard reaches only the desktop on screen
            if (false == ElementHelper.HardwareReaches(this))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Text cannot be typed on a hidden desktop, since SendString is the real keyboard. Peek at the desktop to type, or use FillInFrom, which works there.", this, GPALObjectType.Application);
            else
            {
                HardwareHelper.SendString(textToSend, GPAL.TypingDelay);

                ElementHelper.SettleInput(this);
            }

            return this;
        }
        /// <summary>
        /// Set the value of a range-based control (e.g. a slider) found via Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="rangeValue">The value to set the range control to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllActionAndAnySelector SetRange(double rangeValue)
        {
            if (true == InAction(true))
                ElementHelper.SetRange(this, rangeValue);
            return this;
        }
    }
}
