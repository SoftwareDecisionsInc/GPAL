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
        /// Scroll the element horizontally in a small increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll left.<br/>Positive scroll right.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollHorizontalSmall(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)
                    ElementHelper.Scroll(this, ScrollAmount.SmallIncrement, ScrollTypes.Increments, ScrollTypes.Horizontal);

                for (; 0 > iterations; iterations++)
                    ElementHelper.Scroll(this, ScrollAmount.SmallDecrement, ScrollTypes.Increments, ScrollTypes.Horizontal);
            }

            return this;
        }
        /// <summary>
        /// Scroll the element horizontally in a large increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll left.<br/>Positive scroll right.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollHorizontalLarge(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)
                    ElementHelper.Scroll(this, ScrollAmount.LargeIncrement, ScrollTypes.Increments, ScrollTypes.Horizontal);

                for (; 0 > iterations; iterations++)
                    ElementHelper.Scroll(this, ScrollAmount.LargeDecrement, ScrollTypes.Increments, ScrollTypes.Horizontal);
            }

            return this;
        }
        /// <summary>
        /// Element's horizontal scroll position as a percentage of the total content area within the AutomationElement.<br/>
        /// </summary>
        /// <param name="percent">Percentage of the total content area [0-100]</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollHorizontalPercent([Percent] int percent)
        {
            if (true == InAction(true))
                ElementHelper.Scroll(this, InRange(percent), ScrollTypes.Percent, ScrollTypes.Horizontal);

            return this;
        }

        /// <summary>
        /// Clamps a scroll percentage to 0-100, the range UIAutomation measures against the control's current extent.
        /// An out-of-range value is set to the nearest end with a warning.
        /// </summary>
        /// <param name="percent">The requested percentage</param>
        /// <returns>The same value, or the nearest end of the range</returns>
        private int InRange(int percent)
        {
            int retVal = 0 > percent ? 0 : (100 < percent ? 100 : percent);

            if (retVal != percent)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"A scroll percentage is 0 to 100, so [{percent}] was taken as [{retVal}]", this, GPALObjectType.Application);

            return retVal;
        }
        /// <summary>
        /// Scroll the element vertically in a small increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll up.<br/>Positive scroll down.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollVerticalSmall(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)    // + = down
                    ElementHelper.Scroll(this, ScrollAmount.SmallIncrement, ScrollTypes.Increments, ScrollTypes.Vertical);

                for (; 0 > iterations; iterations++)    // - = up
                    ElementHelper.Scroll(this, ScrollAmount.SmallDecrement, ScrollTypes.Increments, ScrollTypes.Vertical);
            }

            return this;
        }
        /// <summary>
        /// Scroll the element vertically in a large increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll up.<br/>Positive scroll down.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollVerticalLarge(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)
                    ElementHelper.Scroll(this, ScrollAmount.LargeIncrement, ScrollTypes.Increments, ScrollTypes.Vertical);

                for (; 0 > iterations; iterations++)
                    ElementHelper.Scroll(this, ScrollAmount.LargeDecrement, ScrollTypes.Increments, ScrollTypes.Vertical);
            }

            return this;
        }
        /// <summary>
        /// Element's vertical scroll position as a percentage of the total content area within the AutomationElement.<br/>
        /// </summary>
        /// <param name="percent">Percentage of the total content area [0-100]</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollVerticalPercent([Percent] int percent)
        {
            if (true == InAction(true))
                ElementHelper.Scroll(this, InRange(percent), ScrollTypes.Percent, ScrollTypes.Vertical);

            return this;
        }
        /// <summary>
        /// True when the application's scrollable view is at the bottom, like the browser's IsEndOfPage. The view is
        /// the focused control or its nearest scrolling ancestor; with no focus, the first vertically scrolling control
        /// in the window. 100 percent is the bottom.
        /// <br/><br/>
        /// A window with nothing that scrolls counts as at the end, as the browser does.
        /// </summary>
        /// <example>
        /// <code>
        ///     while (false == application.IsEndOfPage())
        ///         application
        ///             .WithSelector(listSelector)
        ///             .ScrollVerticalLarge(1);
        /// </code>
        /// </example>
        /// <returns>True at the bottom, or when nothing scrolls</returns>
        public bool IsEndOfPage()
        {
            bool retVal = true;
            ScrollPattern view = ScrollableView();

            if (null != view)
            {
                ScrollPattern.ScrollPatternInformation at = view.Current;

                // a control that stops scrolling reports NoScroll, -1, rather than a position
                retVal = false == at.VerticallyScrollable || 100 <= Math.Round(at.VerticalScrollPercent);
            }

            return retVal;
        }

        /// <summary>
        /// Returns the ScrollPattern IsEndOfPage reads: the focused control or its nearest vertically scrolling
        /// ancestor, otherwise the first such control in the window. Focus in another program is ignored.
        /// </summary>
        /// <returns>The pattern, or null when nothing in the window scrolls</returns>
        internal ScrollPattern ScrollableView()
        {
            ScrollPattern retVal = null;
            AutomationElement root = RootAutomationElement;

            if (null != root)
            {
                AutomationElement focused = AutomationElement.FocusedElement;

                if (null != focused && focused.Current.ProcessId != root.Current.ProcessId)
                    focused = null;

                // the raw view, since WinForms leaves whole controls out of the control view
                for (AutomationElement at = focused; null != at && null == retVal; at = TreeWalker.RawViewWalker.GetParent(at))
                    if (true == at.TryGetCurrentPattern(ScrollPattern.Pattern, out object pattern) && true == ((ScrollPattern)pattern).Current.VerticallyScrollable)
                        retVal = (ScrollPattern)pattern;

                if (null == retVal)
                {
                    AutomationElement first = root.FindFirst(TreeScope.Subtree, new AndCondition(
                        new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true),
                        new PropertyCondition(ScrollPattern.VerticallyScrollableProperty, true)));

                    if (null != first)
                        retVal = (ScrollPattern)first.GetCurrentPattern(ScrollPattern.Pattern);
                }
            }

            return retVal;
        }
    }
}
