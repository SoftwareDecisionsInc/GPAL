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
        /// Scrolls every element matched by the selectors in the current unit of work, using the given amount, units, and direction.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are scrolled</param>
        /// <param name="amount">The scroll amount: a <see cref="ScrollAmount"/> for increment-based scrolling, or a percentage (double) for percent-based scrolling</param>
        /// <param name="incrementsPercent">Whether <paramref name="amount"/> represents increments or a percentage</param>
        /// <param name="horzVert">Whether to scroll horizontally or vertically</param>
        internal static void Scroll(Application application, object amount, ScrollTypes incrementsPercent, ScrollTypes horzVert)
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

                if (0 == elems.Count)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] matched no elements, so there was nothing to scroll.", application, GPALObjectType.Application);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        Scroll(application, elem.Ae, selector.InteractionType, amount, incrementsPercent, horzVert);
                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Moves a scrollbar through RangeValuePattern. Its value is a position between Minimum and Maximum, so a
        /// percentage is converted to that range and an increment to a SmallChange or LargeChange step.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="rangeValue">The element's RangeValuePattern</param>
        /// <param name="ae">The element, for naming it when something is refused</param>
        /// <param name="amount">A percentage when incrementsPercent is Percent, otherwise a ScrollAmount</param>
        /// <param name="incrementsPercent">Which of the two the amount is</param>
        private static void SetRangeValue(Application application, RangeValuePattern rangeValue, AutomationElement ae, object amount, ScrollTypes incrementsPercent)
        {
            var now = rangeValue.Current;

            if (true == now.IsReadOnly)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.LocalizedControlType}] reports its range as read only, so it cannot be scrolled.", application, GPALObjectType.Application);
            else
            {
                double span = now.Maximum - now.Minimum;
                double wanted;

                if (ScrollTypes.Percent == incrementsPercent)
                    wanted = now.Minimum + (((double)(int)amount / 100.0) * span);
                else
                {
                    // The enum gives step size and direction. A control that returns NaN for its step gets a hundredth
                    // (small) or a tenth (large) of its range.
                    ScrollAmount step = (ScrollAmount)amount;

                    bool large = ScrollAmount.LargeIncrement == step || ScrollAmount.LargeDecrement == step;
                    bool back = ScrollAmount.SmallDecrement == step || ScrollAmount.LargeDecrement == step;

                    double by = large ? now.LargeChange : now.SmallChange;

                    if (double.IsNaN(by) || 0 == by)
                        by = span * (large ? 0.1 : 0.01);

                    wanted = now.Value + (back ? -by : by);
                }

                // Clamp to the end of the range; the control rejects values outside it.
                wanted = wanted < now.Minimum ? now.Minimum : (wanted > now.Maximum ? now.Maximum : wanted);

                try
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"RangeValuePattern on [{ae.Current.ControlType.LocalizedControlType}]: range [{now.Minimum}] to [{now.Maximum}], value [{now.Value}], setting [{wanted}].", application, GPALObjectType.Application);

                    rangeValue.SetValue(wanted);

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"RangeValuePattern on [{ae.Current.ControlType.LocalizedControlType}]: value is now [{rangeValue.Current.Value}].", application, GPALObjectType.Application);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to set a value of [{wanted}] on control [{ae.Current.ControlType.LocalizedControlType}].", application, GPALObjectType.Application, ex);
                }
            }
        }

        /// <summary>
        /// Moves a ScrollPattern control to a percentage by stepping. SetScrollPercent animates one pixel at a time on
        /// some controls; increment scrolls land at once. Large steps while a whole view fits in the remaining
        /// distance, then small steps.<br/>
        /// ViewSize is the visible portion as a percentage, which is also one large step. The position is read after
        /// each step; a control that stops moving ends the walk.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="ae">The element to scroll</param>
        /// <param name="scrollPattern">The element's ScrollPattern</param>
        /// <param name="percent">Where to land, 0 to 100</param>
        /// <param name="horzVert">Which axis to move</param>
        private static void ScrollToPercent(Application application, AutomationElement ae, ScrollPattern scrollPattern, int percent, ScrollTypes horzVert)
        {
            bool vertical = ScrollTypes.Vertical == horzVert;
            var info = scrollPattern.Current;

            if (false == (vertical ? info.VerticallyScrollable : info.HorizontallyScrollable))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.LocalizedControlType}] is not scrollable {(vertical ? "vertically" : "horizontally")}.", application, GPALObjectType.Application);
            else
            {
                double page = vertical ? info.VerticalViewSize : info.HorizontalViewSize;
                double at = vertical ? info.VerticalScrollPercent : info.HorizontalScrollPercent;
                double was = -1;
                int steps = 0;

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: at [{at}], one view is [{page}] percent, walking to [{percent}].", application, GPALObjectType.Application);

                // whole views first
                while (page <= Math.Abs(percent - at) && at != was && steps < STEP_LIMIT)
                {
                    was = at;
                    at = OneStep(scrollPattern, vertical, percent > at, true);
                    steps++;
                }

                double off = Math.Abs(percent - at);
                was = -1;

                // then lines, stopping the moment a step no longer gets closer
                while (0 < off && at != was && steps < STEP_LIMIT)
                {
                    was = at;
                    at = OneStep(scrollPattern, vertical, percent > at, false);
                    steps++;

                    double closer = Math.Abs(percent - at);

                    if (closer >= off)
                        break;

                    off = closer;
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: landed at [{at}] after [{steps}] step(s).", application, GPALObjectType.Application);
            }
        }

        /// <summary>Steps a ScrollPattern once and answers with the new position on that axis.</summary>
        /// <param name="scrollPattern">The pattern to step</param>
        /// <param name="vertical">True to move vertically, false horizontally</param>
        /// <param name="forward">True to move down or right</param>
        /// <param name="large">True for a whole view, false for a line</param>
        /// <returns>The axis position as a percentage after the step</returns>
        private static double OneStep(ScrollPattern scrollPattern, bool vertical, bool forward, bool large)
        {
            ScrollAmount step = large
                ? (forward ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement)
                : (forward ? ScrollAmount.SmallIncrement : ScrollAmount.SmallDecrement);

            scrollPattern.Scroll(vertical ? ScrollAmount.NoAmount : step, vertical ? step : ScrollAmount.NoAmount);

            return vertical ? scrollPattern.Current.VerticalScrollPercent : scrollPattern.Current.HorizontalScrollPercent;
        }

        /// <summary>How many steps a percentage walk may take before it gives up on a control that will not land.</summary>
        private const int STEP_LIMIT = 500;

        /// <summary>
        /// Whether hardware input can reach the program: false on a hidden desktop, even while it is peeked at.
        /// </summary>
        /// <param name="application">The application being automated</param>
        /// <returns>True when the mouse and keyboard can reach it</returns>
        internal static bool HardwareReaches(Application application)
        {
            bool retVal = true;

            if (true == application.applicationSettings.HiddenDesktop)
                retVal = false;

            return retVal;
        }

        internal static void Scroll(Application application, AutomationElement ae, InteractionType interactionType, object amount, ScrollTypes incrementsPercent, ScrollTypes horzVert)
        {
            //UIAutomation only
            if ((Enums.InteractionType.Hardware == interactionType || true == application.applicationSettings.UseHardware) && true == HardwareReaches(application))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Hardware scrolling is not supported at this time. Find the scroll controls and click them instead.", application, GPALObjectType.Application);
            else
            {
                // the element is a scrollbar, so it only goes one direction horizontal or vertical
                if (true == ae.TryGetCurrentPattern(RangeValuePattern.Pattern, out object rangeValuePattern))
                {
                    SetRangeValue(application, (RangeValuePattern)rangeValuePattern, ae, amount, incrementsPercent);
                }
                else if (true == ae.TryGetCurrentPattern(ScrollPattern.Pattern, out object scrollPattern))
                {
                    if (ScrollTypes.Percent == incrementsPercent)
                        ScrollToPercent(application, ae, (ScrollPattern)scrollPattern, (int)amount, horzVert);
                    else
                    {
                        ScrollAmount hAmount = ScrollTypes.Horizontal == horzVert ? (ScrollAmount)amount : ScrollAmount.NoAmount;
                        ScrollAmount vAmount = ScrollTypes.Vertical == horzVert ? (ScrollAmount)amount : ScrollAmount.NoAmount;

                        try
                        {
                            var before = ((ScrollPattern)scrollPattern).Current;

                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: scrollable h [{before.HorizontallyScrollable}] v [{before.VerticallyScrollable}], at h [{before.HorizontalScrollPercent}] v [{before.VerticalScrollPercent}], applying h [{hAmount}] v [{vAmount}].", application, GPALObjectType.Application);

                            ((ScrollPattern)scrollPattern).Scroll(hAmount, vAmount);

                            var after = ((ScrollPattern)scrollPattern).Current;

                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: now at h [{after.HorizontalScrollPercent}] v [{after.VerticalScrollPercent}].", application, GPALObjectType.Application);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to Scroll on control [{ae.Current.ControlType.LocalizedControlType}].", application, GPALObjectType.Application, ex); ;
                        }
                    }
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.LocalizedControlType}] offers neither RangeValuePattern nor ScrollPattern, so UIAutomation cannot scroll it. Point the selector at its ScrollBar, which does.", application, GPALObjectType.Application);

            }
        }
    }
}
