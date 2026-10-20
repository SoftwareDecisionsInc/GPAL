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

namespace GenerallyPositive.Application
{
    /// <summary>
    /// Application ElementAssistant with fluent methods to help you manipulate AutomationElements in callback EventHandlers.<br/>
    /// Instantiated with GPAL.ElementAssistant
    /// </summary>
    /// <example>
    /// <code>
    /// // <b>CallIfFound</b> handler
    /// public static int FoundNotepadPane(Application application, List&lt;GPALAutomationElement&gt; foundElements, List&lt;GPALAutomationElement&gt; matchedElements, Selector selector)
    /// {
    ///     ElementAssistant elementAssistant = (ElementAssistant) GPAL.ElementAssistant(application);
    /// 
    ///     foreach (IGPALAutomationElement element in foundElements)
    ///     {
    ///         elementAssistant
    ///             .ForSelector(selector)  // for interaction type, offsetX and offsetY
    ///             .WithElement(element)
    ///             .WithHardware  // can override the selector interaction type here for this one elem
    ///             .LeftClick();
    ///     }
    ///     return 0;
    /// }
    /// </code>
    /// </example>
    /// <returns>Fluent AutomationElement assistant to use in callback EventHandler</returns>
    public class ElementAssistant : IAllowForSelector, IAllowElementSettingsAndActions
    {
        /// <summary>
        /// The settings (selector, automation element, interaction type, application) backing this ElementAssistant.
        /// </summary>
        private ElementSettings ElementSettings { get; set; }

        /// <summary>
        /// Creates a new ElementAssistant with default ElementSettings.<br/>
        /// Use GPAL.ElementAssistant() to obtain an instance instead of calling this directly.
        /// </summary>
        internal ElementAssistant()
        {
            ElementSettings = new ElementSettings();
        }
        /// <summary>
        /// Interact with the GPALAutomationElement emulating hardware, not using UIAutomation;
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
        /// Left click the GPALAutomationElement.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftClick()
        {
            if (InteractionType.Hardware == ElementSettings.InteractionType)
                ElementHelper.HardwareClick(Application, ElementSettings.AutomationElement, ClickType.LeftClick, ModifierKeys.NONE);
            else
                ElementHelper.UIAutomationClick(Application, ElementSettings.Selector, ElementSettings.AutomationElement, ClickType.LeftClick, ModifierKeys.NONE);

            Thread.Sleep(250);
            return this;
        }
        /// <summary>
        /// Left click the GPALAutomationElement, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <param name="modifierKeys">Optional modifier key press</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftClick(Enums.ModifierKeys modifierKeys)
        {
            if (InteractionType.Hardware == ElementSettings.InteractionType)
                ElementHelper.HardwareClick(Application, ElementSettings.AutomationElement, ClickType.LeftClick, modifierKeys);
            else
                ElementHelper.UIAutomationClick(Application, ElementSettings.Selector, ElementSettings.AutomationElement, ClickType.LeftClick, modifierKeys);

            Thread.Sleep(250);
            return this;
        }
        /// <summary>
        /// Left double click the GPALAutomationElement.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions LeftDoubleClick()
        {
            if (InteractionType.Hardware == ElementSettings.InteractionType)
                ElementHelper.HardwareClick(Application, ElementSettings.AutomationElement, ClickType.LeftDoubleClick, ModifierKeys.NONE);
            else
            {
                ElementHelper.UIAutomationClick(Application, ElementSettings.Selector, ElementSettings.AutomationElement, ClickType.LeftDoubleClick, ModifierKeys.NONE);
            }

            Thread.Sleep(250);
            return this;
        }
        /// <summary>
        /// Right click the GPALAutomationElement.
        /// </summary>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions RightClick()
        {
            if (InteractionType.Hardware == ElementSettings.InteractionType)
                ElementHelper.HardwareClick(Application, ElementSettings.AutomationElement, ClickType.RightClick, ModifierKeys.NONE);
            else
            {
                ElementHelper.UIAutomationClick(Application, ElementSettings.Selector, ElementSettings.AutomationElement, ClickType.RightClick, ModifierKeys.NONE);
            }

            Thread.Sleep(250);
            return this;
        }
        /// <summary>
        /// Scroll the GPALAutomationElement horizontally by an increment amount.
        /// </summary>
        /// <param name="scrollAmount">Amount to scroll (e.g. LargeIncrement/SmallDecrement)</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ScrollHorizontal(System.Windows.Automation.ScrollAmount scrollAmount)
        {
            ElementHelper.Scroll(Application, scrollAmount, ScrollTypes.Increments, ScrollTypes.Horizontal);
            return this;
        }

        /// <summary>
        /// Scroll the GPALAutomationElement horizontally to the given percentage of its scrollable range.
        /// </summary>
        /// <param name="percent">Percentage of the total horizontal content area [0-100]</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ScrollHorizontalPercent(int percent)
        {
            ElementHelper.Scroll(Application, percent, ScrollTypes.Percent, ScrollTypes.Horizontal);
            return this;
        }

        /// <summary>
        /// Scroll the GPALAutomationElement vertically by an increment amount.
        /// </summary>
        /// <param name="scrollAmount">Amount to scroll (e.g. LargeIncrement/SmallDecrement)</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ScrollVertical(System.Windows.Automation.ScrollAmount scrollAmount)
        {
            ElementHelper.Scroll(Application, scrollAmount, ScrollTypes.Increments, ScrollTypes.Vertical);
            return this;
        }

        /// <summary>
        /// Scroll the GPALAutomationElement vertically to the given percentage of its scrollable range.
        /// </summary>
        /// <param name="percent">Percentage of the total vertical content area [0-100]</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions ScrollVerticalPercent(int percent)
        {
            ElementHelper.Scroll(Application, percent, ScrollTypes.Percent, ScrollTypes.Vertical);
            return this;
        }
        /// <summary>
        /// The selector associated with the webelement(s) you are acting upon.<br/>
        /// Used for the OffestX and OffsetY properties and Information and Exceptional channel messages.
        /// </summary>
        /// <param name="selector"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSetting ForSelector(Selector selector)
        {
            ElementSettings.Selector = selector;
            ElementSettings.InteractionType = selector.InteractionType;
            return this;
        }
        /// <summary>
        /// Define the GPALAutomationElement to act upon.
        /// </summary>
        /// <param name="AutomationElement">The GPALAutomationElement to interact with</param>
        /// <returns>Fluent interface to write your element handling workflow</returns>

        public IAllowElementSettingsAndActions WithElement(IGPALAutomationElement AutomationElement)
        {
            ElementSettings.AutomationElement = (GPALAutomationElement)AutomationElement;
            return this;
        }
        /// <summary>
        /// Fill In (Ovewrite) text of the input GPALAutomationElement defined with .WithElement
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions FillInFrom(string inputText)
        {
            UnitOfWork.ElementNode elementNode = new UnitOfWork.ElementNode() { OffsetX = (int)(ElementSettings.Selector?.OffsetX ?? 5), OffsetY = (int)(ElementSettings.Selector?.OffsetY ?? 5), InteractionType = ElementSettings.InteractionType }; // if .ForSelector is not provided, default to 5
            ElementHelper.FillInFrom(Application, ElementSettings.AutomationElement.Ae, elementNode, inputText, WriteMode.Overwrite);

            return this;
        }
        /// <summary>
        /// Insert text at the beginning of the input GPALAutomationElement defined with .WithElement
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions InsertFrom(string inputText)
        {
            ElementHelper.FillInFrom(Application, inputText, WriteMode.Insert);

            return this;
        }
        /// <summary>
        /// Appends text to the end of the input GPALAutomationElement defined with .WithElement
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions AppendFrom(string inputText)
        {
            UnitOfWork.ElementNode elementNode = new UnitOfWork.ElementNode() { OffsetX = (int)(ElementSettings.Selector?.OffsetX ?? 5), OffsetY = (int)(ElementSettings.Selector?.OffsetY ?? 5), InteractionType = ElementSettings.InteractionType }; // if .ForSelector is not provided, default to 5
            ElementHelper.FillInFrom(Application, ElementSettings.AutomationElement.Ae, elementNode, inputText, WriteMode.Append);

            return this;
        }
        /// <summary>
        /// Emulate pressing a special key like Home, End, Page Down.<br/>
        /// Use GPAL.VK_ defined virual keycodes or send any you want to send.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        /// </summary>
        /// <param name="keyCode"></param>
        /// <returns>Fluent interface to write your element handling workflow</returns>
        public IAllowElementSettingsAndActions SendKeyCode(byte keyCode)
        {
            HardwareHelper.SendKey(keyCode);
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
                HardwareHelper.PressModifierKey(modifierKeys);
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
                HardwareHelper.ReleaseModifierKeys(modifierKeys);
            return this;
        }
        /// <summary>
        /// The Application associated with this ElementAssistant.
        /// </summary>
        internal Application Application
        {
            get
            {
                return ElementSettings.Application;
            }

            set
            {
                ElementSettings.Application = value;
            }
        }
    }
}

