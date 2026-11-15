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
        /// Append data from the database to the end of any input Selectors defined in the UOW<br/>
        /// One token (one column in the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Insert the text from the database at the beginning of the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// FillIn data from the database overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Append the data from the input file to the end of any input Selectors defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (IGPALGrid<string> tokens in ((IGPALFileInternal)inputFile).TokenList)
                    BrowserHelper.FillInWithTokens(this, tokens, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// FillIn data from the file overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (IGPALGrid<string> tokens in ((IGPALFileInternal)CurrentUOW.InputFile).TokenList)
                    BrowserHelper.FillInWithTokens(this, tokens, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Insert the data from the file at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (IGPALGrid<string> tokens in ((IGPALFileInternal)inputFile).TokenList)
                    BrowserHelper.FillInWithTokens(this, tokens, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Append the data from the input grid to the end of any input Selectors defined in the UOW<br/>
        /// One token (one string from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, inputGrid, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// FillIn data from the grid overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, inputGrid, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Insert the data from the grid at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input. Inputs are filled by tokens in the order they are both declared.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                BrowserHelper.FillInWithTokens(this, inputGrid, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Append the given text to the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="text">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(string text)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.FillInFrom(this, text, WriteMode.Append);

            return this;
        }
        /// <summary>
        /// Overwrite all text in the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="text">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(string text)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.FillInFrom(this, text, WriteMode.Overwrite);

            return this;
        }
        /// <summary>
        /// Insert text at the beginning of the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// Example: If three inputs are defined and three 'InsertFrom' are defined, then the text is assigned to the same ordinal input element found via the Selector(s)
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the corresponding Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(string textToUse)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Overwrite all text in the first input in the UOW with what is on the clipboard, in the form GetClipboard
        /// named. FileList fills the copied paths; an image fills nothing.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <example>
        /// <code>
        ///     myBrowser
        ///         .WithSelector((Selector)"#notes")
        ///         .FillInFrom(GPAL.Clipboard.GetClipboard(ClipboardType.Text));
        /// </code>
        /// </example>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector FillInFrom(IAllowClipboardInto clipboard)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                string text = ((GPALClipboard)clipboard).TextToFill();

                if (null != text)
                    ElementHelper.FillInFrom(this, text, WriteMode.Overwrite);
            }

            return this;
        }
        /// <summary>
        /// Append what is on the clipboard to the first input in the UOW, in the form GetClipboard named. FileList
        /// fills the copied paths; an image fills nothing.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector AppendFrom(IAllowClipboardInto clipboard)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                string text = ((GPALClipboard)clipboard).TextToFill();

                if (null != text)
                    ElementHelper.FillInFrom(this, text, WriteMode.Append);
            }

            return this;
        }
        /// <summary>
        /// Insert what is on the clipboard into the first input in the UOW, in the form GetClipboard named. FileList
        /// fills the copied paths; an image fills nothing.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="clipboard">The clipboard, from GPAL.Clipboard.GetClipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InsertFrom(IAllowClipboardInto clipboard)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                string text = ((GPALClipboard)clipboard).TextToFill();

                if (null != text)
                    ElementHelper.FillInFrom(this, text, WriteMode.Insert);
            }

            return this;
        }
        /// <summary>
        /// Focus the current element found via the Selector. Only the first element per Selector found will be focused.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, for text inputs, focus is a click in the control. Otherwise, hardware emulation hovers the mouse over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then focus will interate over {num} elements that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector Focus(Selector selector)
        {
            if (null != selector)
                WithSelector(selector);
            // waitfor did not match if returns false
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.Focus(this);
            return this;
        }

        /// <summary>
        /// Sets the value of the element(s) matched by the preceding <see cref="WithSelector"/> call from the
        /// value of the element matched by <paramref name="selector"/>, i.e. <c>destElement.value =
        /// srcElement.value</c>. Uses OttoMagic, Puppeteer or JavaScript depending on the active engine, and
        /// fires "input"/"change" events on the destination so the page reacts to the new value.
        /// </summary>
        /// <param name="selector">Selector identifying the source element to read the value from</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SetValueFrom(Selector selector)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                ElementHelper.SetValueFromElement(this, selector);

            return this;
        }
        /// <summary>
        /// Type in the literal string provided.<br/>
        /// <b>NOTE:</b> If you press SHIFT then SendString a mixed-case string, SHIFT will be released.<br/>
        /// To send ALL CAPS, press SHIFT then send an all lowercase string.
        /// </summary>
        /// <param name="textToSend">Text to type</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SendString(string textToSend)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.SendString(this, textToSend);
            }

            return this;
        }
        /// <summary>
        /// Press the specified modifer keys (can be ORd)<br/>
        /// <b>NOTE:</b> If you SendString a mixed case string, the SHIFT key will be released if it is pressed.
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
        /// <param name="modifierKeys">Enumeration ModifierKeys, can be ORd togther</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector PressModifierKey(ModifierKeys modifierKeys)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Pressed [{GetModifierKeys(modifierKeys)}]", this, GPALObjectType.Browser);

            if (ModifierKeys.NONE != modifierKeys)
                if (false == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware))
                {
                    HardwareHelper.PressModifierKey(modifierKeys);
                }
                else if (true == UseOttoMagic)
                {
                    MagicHelper.PressModifierKey(modifierKeys);
                }
                else if (true == UsePuppeteer || (true == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware)))
                {
                    PuppeteerClient.PressModifierKey(modifierKeys).Execute();
                }
                else
                {
                    Actions actions = new Actions(BrowserDriver);
                    bool anyKeySet = false;

                    if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    {
                        actions.KeyDown(Keys.Alt);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Control))
                    {
                        actions.KeyDown(Keys.Control);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    {
                        actions.KeyDown(Keys.Shift);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    {
                        actions.KeyDown(Keys.Meta);
                        anyKeySet = true;
                    }

                    ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                    if (ModifierKeys.NONE != unsupported)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"PressModifierKey: [{GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", this, GPALObjectType.Browser);

                    if (true == anyKeySet)
                        actions.Perform();
                }

            return this;
        }
        /// <summary>
        /// Release the specified modifer keys (can be ORd)<br/>
        /// <b>NOTE:</b> If you SendString a mixed case string, the SHIFT key will be released if it is pressed.
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
        /// <param name="modifierKeys">Enumeration ModifierKeys, can be ORd togther</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Released [{GetModifierKeys(modifierKeys)}]", this, GPALObjectType.Browser);

            if (ModifierKeys.NONE != modifierKeys)
                if (false == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware))
                {
                    HardwareHelper.ReleaseModifierKeys(modifierKeys);
                }
                else if (true == UseOttoMagic)
                {
                    MagicHelper.ReleaseModifierKey(modifierKeys);
                }
                else if (true == UsePuppeteer || (true == BrowserSettings.UseHeadless && (true == BrowserSettings.UseHardware)))
                {
                    PuppeteerClient.ReleaseModifierKey(modifierKeys).Execute();
                }
                else
                {
                    Actions actions = new Actions(BrowserDriver);
                    bool anyKeySet = false;

                    if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    {
                        actions.KeyUp(Keys.Alt);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Control))
                    {
                        actions.KeyUp(Keys.Control);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    {
                        actions.KeyUp(Keys.Shift);
                        anyKeySet = true;
                    }
                    if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    {
                        actions.KeyUp(Keys.Meta);
                        anyKeySet = true;
                    }

                    ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                    if (ModifierKeys.NONE != unsupported)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ReleaseModifierKey: [{GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", this, GPALObjectType.Browser);

                    if (true == anyKeySet)
                        actions.Perform();
                }

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
        public IAllowBrowserActionOrAnySelector SendKey(byte VKCode)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
            {
                ElementHelper.SendKey(this, VKCode);
            }

            return this;
        }

        /// <summary>
        /// Sets the value of a range/slider input element matched by the current selector(s) by scrolling it
        /// into view and setting its value to <paramref name="rangeValue"/>.
        /// </summary>
        /// <param name="rangeValue">Value to set the range input to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SetRange(int rangeValue)
        {
            if (true == InAction(WaitTime.Never < CurrentUOW.WaitForInMs))
                foreach (Selector selector in CurrentUOW.WithSelectorList)
                {
                    ElementHelper.FindWebElements(this, CurrentUOW, selector, out _, out List<GPALElement> elems);
                    foreach (GPALElement elem in elems)
                    {
                        ElementHelper.ScrollIntoView(this, elem);

                        if (true == UseOttoMagic)
                            MagicHelper.SetRange(elem.Css, rangeValue);
                        else if (true == UsePuppeteer)
                            PuppeteerClient.SetRange(elem.Css).WithRangeValue(rangeValue).Execute();
                        else if (true == UseSelenium)
                        {
                            // Set the value and dispatch the change events so the UI updates
                            string script = @"
                            arguments[0].value = arguments[1];
                            arguments[0].dispatchEvent(new Event('input', { bubbles: true }));
                            arguments[0].dispatchEvent(new Event('change', { bubbles: true }));
                        ";

                            BrowserHelper.ExecuteJavaScript(this, script, elem.WebElement, rangeValue);
                        }
                    }
                }

            return this;
        }

        /// <summary>
        /// Converts a <see cref="ModifierKeys"/> flags value into a comma-separated, human-readable string of the set flags.
        /// </summary>
        /// <param name="modifierKeys">The modifier key flags to convert</param>
        /// <returns>"None" if no flags are set, otherwise a comma-separated list of the set flag names</returns>
        internal static string GetModifierKeys(ModifierKeys modifierKeys)
        {
            if (modifierKeys == ModifierKeys.NONE)
                return "None";

            var setFlags = Enum.GetValues(typeof(ModifierKeys))
                .Cast<ModifierKeys>()
                .Where(flag => flag != ModifierKeys.NONE && modifierKeys.HasFlag(flag))
                .Select(flag => flag.ToString());

            return string.Join(", ", setFlags);
        }
    }
}
