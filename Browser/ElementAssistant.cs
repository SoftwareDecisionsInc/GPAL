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

using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Browser ElementAssistant object with fluent methods to help you manipulate WebElements in Callback EventHandlers.<br/>
    /// Instantiated with GPAL.ElementAssistant()
    /// </summary>
    /// <example>
    /// <code>
    /// public static int CallIfFound(Browser browser, List&lt;GPALElement&gt; foundElements, List&lt;GPALElement&gt; matchedElements, Selector selector)
    /// {
    ///     elementAssistant = (ElementAssistant) GPAL.ElementAssistant(browser);
    /// 
    ///     foreach (IGPALElement webElement in matchedElements)
    ///     {
    ///         elementAssistant
    ///             .ForSelector(selector)  // for interaction type, offsetX and offsetY
    ///             .WithElement(webElement)
    ///             .WithJavscript // can override the selector interaction type here for this one elem
    ///             .LeftClick();
    ///     }
    /// }
    /// </code>
    /// </example>
    /// <returns>Fluent GPALElement assistant to use in callback EventHandler</returns>
    public class ElementAssistant : IAllowElementSettingsAndActions
    {
        private ElementSettings ElementSettings;// { get; set; }

        /// <summary>
        /// Creates a new ElementAssistant with default ElementSettings.<br/>
        /// Use GPAL.ElementAssistant() to obtain an instance instead of calling this directly.
        /// </summary>
        internal ElementAssistant()
        {
            ElementSettings = new ElementSettings();
        }
        /// <summary>
        /// Interact with the webelement using javascript
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions WithJavaScript
        {
            get
            {
                ElementSettings.InteractionType = InteractionType.JavaScript;
                return this;
            }
        }
        /// <summary>
        /// Interact with the webelement using hardware emulation
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions WithHardware
        {
            get
            {
                ElementSettings.InteractionType = InteractionType.Hardware;
                return this;
            }
        }
        /// <summary>
        /// Left click the webelement, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftClick()
        {
            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.LeftClick, ModifierKeys.NONE);
            BrowserHelper.CheckDocumentReady(ElementSettings.Browser);

            return this;
        }
        /// <summary>
        /// Left click the webelement, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <param name="modifierKeys">Optional modifier key press</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftClick(Enums.ModifierKeys modifierKeys)
        {
            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.LeftClick, modifierKeys);
            BrowserHelper.CheckDocumentReady(ElementSettings.Browser);

            return this;
        }

        /// <summary>
        /// Left click the webelement, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <param name="webElement">The webelement to click from your CallIfFound handler</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftClick(IGPALElement webElement)
        {
            ElementSettings.WebElement = (GPALElement)webElement;
            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.LeftClick, ModifierKeys.NONE);
            BrowserHelper.CheckDocumentReady(ElementSettings.Browser);

            return this;
        }

        /// <summary>
        /// Left click the webelement and download a file
        /// </summary>
        /// <param name="downloadTo">GPALFile to download to</param>
        /// <param name="webElement">The webelement to click from your CallIfFound handler</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftClickAndDownload(IGPALElement webElement, GPALFile downloadTo)
        {
            ElementSettings.WebElement = (GPALElement)webElement;
            Browser.LeftClickAndDownload(downloadTo, (GPALElement)webElement, ElementSettings.Selector);
            BrowserHelper.CheckDocumentReady(ElementSettings.Browser);

            return this;
        }

        /// <summary>
        /// Left double click the webelement.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftDoubleClick()
        {

            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.LeftDoubleClick, ModifierKeys.NONE);

            return this;
        }
        /// <summary>
        /// Left double click the webelement.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftDoubleClick(IGPALElement webElement)
        {
            ElementSettings.WebElement = (GPALElement)webElement;
            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.LeftDoubleClick, ModifierKeys.NONE);

            return this;
        }
        /// <summary>
        /// Left click the webelement, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions MiddleClick()
        {
            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.MiddleClick, ModifierKeys.NONE);
            BrowserHelper.CheckDocumentReady(ElementSettings.Browser);

            return this;
        }
        /// <summary>
        /// Right click the webelement.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions RightClick(IGPALElement webElement = null)
        {
            GPALElement webElementToUse = (GPALElement)webElement ?? ElementSettings.WebElement;

            ElementHelper.Click(ElementSettings.Browser, ElementSettings.Selector, ElementSettings.WebElement, ClickType.RightClick, ModifierKeys.NONE);

            Thread.Sleep(250);
            return this;
        }
        /// <summary>
        /// The selector associated with the webelement(s) you are acting upon.<br/>
        /// Used for the InteractionType, OffestX and OffsetY properties and Information and Exceptional channel messages.
        /// </summary>
        /// <param name="selector"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        internal void ForSelector(Selector selector)
        {
            ElementSettings.Selector = selector;
        }
        /// <summary>
        /// Define the element to act upon.
        /// </summary>
        /// <param name="GPALElement">The GPALElement to interact with</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions WithElement(IGPALElement GPALElement)
        {
            ElementSettings.WebElement = (GPALElement)GPALElement;
            return this;
        }
        /// <summary>
        /// Fill In (Ovewrite) text of the input webelement defined with .WithElement
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions FillInFrom(string inputText)
        {
            ElementHelper.FillInFrom(ElementSettings.Browser, inputText, WriteMode.Overwrite, ElementSettings.WebElement);
            //if (true == Browser.BrowserSettings.UseHeadless)
            //    ElementSettings.WebElement.SendKeys(inputText);
            //else if (InteractionType.JavaScript == ElementSettings.InteractionType)
            //    ElementHelper.JavaScriptFillInFrom(ElementSettings.Browser, ElementSettings.WebElement, inputText, WriteMode.Overwrite);
            //else if (InteractionType.Hardware == ElementSettings.InteractionType)
            //    ElementHelper.HardwareFillInFrom(Browser, ElementSettings.WebElement, 5, 5, inputText, WriteMode.Overwrite);   // KLUDGE: 5,5 is to click inside the control
            //else
            //    ElementSettings.WebElement.SendKeys(inputText);

            return this;
        }
        /// <summary>
        /// Insert text at the beginning of the input webelement defined with .WithElement
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions InsertFrom(string inputText)
        {
            ElementHelper.FillInFrom(ElementSettings.Browser, inputText, WriteMode.Insert, ElementSettings.WebElement);

            //if (true != Browser.BrowserSettings.UseHeadless)
            //{
            //    ElementSettings.WebElement.SendKeys(Keys.Control + "A");    // select all text and overwrite
            //    ElementSettings.WebElement.SendKeys(inputText);
            //}
            //else if (InteractionType.JavaScript == ElementSettings.InteractionType)
            //    ElementHelper.JavaScriptFillInFrom(ElementSettings.Browser, ElementSettings.WebElement, inputText, WriteMode.Insert);
            //else if (InteractionType.Hardware == ElementSettings.InteractionType)
            //    ElementHelper.HardwareFillInFrom(Browser, ElementSettings.WebElement, 5, 5, inputText, WriteMode.Insert);   // KLUDGE: 5,5 is to click inside the control
            //else
            //{
            //    ElementSettings.WebElement.SendKeys(Keys.Control + "A");    // select all text and overwrite
            //    ElementSettings.WebElement.SendKeys(inputText);
            //}
            return this;
        }
        /// <summary>
        /// Appends text to the end of the input webelement defined with .WithElement
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions AppendFrom(string inputText)
        {
            ElementHelper.FillInFrom(ElementSettings.Browser, inputText, WriteMode.Append, ElementSettings.WebElement);

            //if (true == Browser.BrowserSettings.UseHeadless)
            //    ElementSettings.WebElement.SendKeys(inputText);
            //else if (InteractionType.JavaScript == ElementSettings.InteractionType)
            //    ElementHelper.JavaScriptFillInFrom(ElementSettings.Browser, ElementSettings.WebElement, inputText, WriteMode.Append);
            //else if (InteractionType.Hardware == ElementSettings.InteractionType)
            //    ElementHelper.HardwareFillInFrom(Browser, ElementSettings.WebElement, 5, 5, inputText, WriteMode.Append);   // KLUDGE: 5,5 is to click inside the control
            //else
            //    ElementSettings.WebElement.SendKeys(inputText);

            return this;
        }
        /// <summary>
        /// Emulate pressing a special key like Home, End, Page Down.<br/>
        /// Use GPAL.VK_ defined virual keycodes or send any you want to send.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        /// </summary>
        /// <param name="keyCode"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions SendKey(byte keyCode)
        {
            ElementHelper.SendKey(Browser, keyCode);
            return this;
        }
        /// <summary>
        /// Send the string using hardware emulation.
        /// </summary>
        /// <param name="textToSend"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions SendString(string textToSend)
        {
            ElementHelper.SendString(Browser, textToSend);
            return this;
        }
        /// <summary>
        /// Press a modifier key: Alt, Control, Shift
        /// </summary>
        /// <param name="modifierKeys">Alt, Control, Shift</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions PressModifierKey(ModifierKeys modifierKeys)
        {
            if (ModifierKeys.NONE != modifierKeys)
            {
                if (true == ElementSettings.Browser.UseOttoMagic)
                    ElementSettings.Browser.MagicHelper.PressModifierKey(modifierKeys);
                else if (true == ElementSettings.Browser.UsePuppeteer)
                    ElementSettings.Browser.PuppeteerClient.PressModifierKey(modifierKeys);
                else
                    HardwareHelper.PressModifierKey(modifierKeys);
            }

            return this;
        }
        /// <summary>
        /// Release a modifier key: Alt, Control, Shift
        /// </summary>
        /// <param name="modifierKeys">Alt, Control, Shift</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            if (ModifierKeys.NONE != modifierKeys)
            {
                if (true == ElementSettings.Browser.UseOttoMagic)
                    ElementSettings.Browser.MagicHelper.ReleaseModifierKey(modifierKeys);
                else if (true == ElementSettings.Browser.UsePuppeteer)
                    ElementSettings.Browser.PuppeteerClient.ReleaseModifierKey(modifierKeys);
                else
                    HardwareHelper.ReleaseModifierKeys(modifierKeys);
            }
            return this;
        }
        /// <summary>
        /// Scroll the element horizontally by number of pixels.<br/>
        /// Negative numbers scroll left. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmount">Positive integer scrolls right.<br/>Negative integer scolls left.</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ScrollElementHorizontal(int scrollAmount)
        {
            ElementHelper.ScrollElement(Browser, scrollAmount, ScrollTypes.Horizontal);
            return this;
        }
        /// <summary>
        /// Scroll the element vertically by number of pixels.<br/>
        /// Negative numbers scroll up. <b>JavaScript only</b>
        /// </summary>
        /// <param name="scrollAmount">Positive integer scrolls down.<br/>Negative integer scolls down.</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ScrollElementVertical(int scrollAmount)
        {
            ElementHelper.ScrollElement(Browser, scrollAmount, ScrollTypes.Vertical);
            return this;
        }

        /// <summary>
        /// The Browser associated with this ElementAssistant.
        /// </summary>
        internal Browser Browser
        {
            get
            {
                return ElementSettings.Browser;
            }

            set
            {
                ElementSettings.Browser = value;
            }
        }

        /// <summary>
        /// Returns this ElementAssistant as itself.
        /// </summary>
        /// <returns>This ElementAssistant instance</returns>
        public ElementAssistant ToGPALObject()
        {
            return this;
        }
    }
}

