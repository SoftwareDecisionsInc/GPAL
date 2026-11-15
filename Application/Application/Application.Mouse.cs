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
        /// Hover the mouse over the current element found via the Selector. Only the first element per Selector found will be focused.<br/>
        /// Using UIAutomation, setFocus is first called on the item, then the mouse moved to hover over the element.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, for text inputs, focus is a click in the control. Otherwise, hardware emulation hovers the mouse over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then focus will interate over every {num} elements that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector Hover(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.Hover(this, CurrentUOW);
            return this;
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.LeftClick(this);
            return this;
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <param name="modifierKeys">Modifier key to hold during the click</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClick(Enums.ModifierKeys modifierKeys)
        {
            if (true == InAction(true))
                ElementHelper.LeftClick(this, modifierKeys);
            return this;
        }
        /// <summary>
        /// Left double click the element found for Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftDoubleClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.GenericClick(this, ClickType.LeftDoubleClick);
            return this;
        }
        /// <summary>
        /// Middle click the element found for Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector MiddleClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.GenericClick(this, ClickType.MiddleClick);
            return this;
        }
        /// <summary>
        /// Move the mouse cursor to the element found for Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector MoveTo(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.MoveTo(this);
            return this;
        }

        /// <summary>
        /// Performs a drag from the source element to the target element, both found via Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector DragAndDrop(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.DragAndDrop(this);
            return this;
        }
        /// <summary>
        /// Performs a drag from the source element to the target element, both found via Selector(s) defined in the UOW,
        /// while holding the given modifier key.
        /// </summary>
        /// <param name="modifierKeys">Modifier key to hold during the drag and drop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector DragAndDrop(ModifierKeys modifierKeys)
        {
            if (true == InAction(true))
                ElementHelper.DragAndDrop(this, modifierKeys);
            return this;
        }
        /// <summary>
        /// Right click the element found for Selector(s) defined in the UOW<br/>
        /// One right click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a right click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector RightClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.GenericClick(this, ClickType.RightClick);
            return this;
        }
    }
}
