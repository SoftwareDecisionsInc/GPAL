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
        /// Moves the mouse to each element matched by the selectors in currentUOW. A hidden desktop has no
        /// mouse, so there it publishes a warning instead.
        /// </summary>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="currentUOW">The unit of work whose selectors are used, the current one or the persistent one</param>
        public static void Hover(Application application, UnitOfWork currentUOW)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in currentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, currentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        if (0 == elem.Ae.Current.BoundingRectangle.Width && 0 == elem.Ae.Current.BoundingRectangle.Height)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element has Zero Width and Height, skipping", application, GPALObjectType.Application);
                            continue;
                        }

                        // Hover is only a mouse position, so a hidden desktop cannot be hovered. Report it instead of
                        // moving the real pointer.
                        if (false == HardwareReaches(application))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] cannot be moved to on a hidden desktop, since a hover is a mouse position and UIAutomation has no equivalent. Peek at the desktop to hover, or click, which does work there.", application, GPALObjectType.Application);
                        }
                        else if (Enums.InteractionType.Hardware == selector.InteractionType || true == application.applicationSettings.UseHardware)
                        {
                            // same as hardware moveto
                            HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is
                        }
                        else if (0 != elem.Ae.Current.BoundingRectangle.Width && 0 != elem.Ae.Current.BoundingRectangle.Height)
                        {
                            try
                            {
                                elem.Ae.SetFocus();
                                HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is
                            }
                            catch (Exception)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Focusing [{selector.Name}] threw, so it is moved to without the focus.", application, GPALObjectType.Application);
                                HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is
                            }
                        }

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Moves the mouse cursor to each element matched by the selectors in the current unit of work, using
        /// hardware emulation or UI Automation focus depending on the selector's interaction type.
        /// </summary>
        /// <param name="application">The Application whose UOW elements the cursor is moved to</param>
        public static void MoveTo(Application application)
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

                int rowCount = 0;

                // Moving to an element uses the mouse only. A hidden desktop cannot do this (or right click).
                if (false == HardwareReaches(application))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] cannot be moved to on a hidden desktop, since moving the pointer is the mouse and UIAutomation has no equivalent. Peek at the desktop to move there, or click, which does work.", application, GPALObjectType.Application);
                else
                    foreach (GPALAutomationElement elem in elems)
                    {
                        if (null != elem.Gae || null != elem.Ae)
                            HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
            }
        }

        /// <summary>
        /// Moves the mouse pointer to the element, at the given offset from its top left corner.<br/>
        /// UIAutomation has no pointer position, so this is a mouse move and nothing else. Focus is the
        /// programmatic call and is a separate action.
        /// </summary>
        /// <param name="element">The element to move to</param>
        /// <param name="offsetX">X offset within the element</param>
        /// <param name="offsetY">Y offset within the element</param>
        /// <param name="appName">Application name, for the message when the move fails</param>
        private static void MoveTo(AutomationElement element, int offsetX, int offsetY, string appName)
        {
            try
            {
                HardwareMoveTo(null, new GPALAutomationElement(element), offsetX, offsetY);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"(ElementHelper): unable to move the mouse to the element in Application [{appName}].", element, GPALObjectType.Other, ex);
            }
        }
        /// <summary>
        /// Moves the mouse cursor to the given element's location plus offset, using hardware emulation. Same as hardware hover.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to move to</param>
        /// <param name="OffsetX">X offset, relative to the element's location</param>
        /// <param name="OffsetY">Y offset, relative to the element's location</param>
        private static System.Drawing.Point HardwareMoveTo(Application application, GPALAutomationElement element, int OffsetX, int OffsetY)
        {
            Rectangle rect2 = new Rectangle(), iframeRect = new Rectangle(0, 0, 0, 0);

            // Include the size. Without it the rectangle is a point and every click lands on the top-left border.
            if (null != element.Gae)
            {
                rect2.Location = element.Gae.Location;
                rect2.Size = element.Gae.Size;
            }
            else
            {
                rect2.Location = new System.Drawing.Point((int) element.Ae.Current.BoundingRectangle.Location.X, (int) element.Ae.Current.BoundingRectangle.Location.Y);
                rect2.Size = new System.Drawing.Size((int) element.Ae.Current.BoundingRectangle.Width, (int) element.Ae.Current.BoundingRectangle.Height);

                // The browser's scroll-into-view code, commented out; an application element is not scrolled into view before the click.
                //if (true == application.CurrentUOW.CurrentSelector?.SearchForSelector)
                //{
                //    if (false == IsVisibleInViewport(element))
                //    {
                //        SendKey(GPAL.VK_HOME);   // home - go to document top, then scroll down

                //        while (false == IsVisibleInViewport(element) && false == IsEndOfPage(element))
                //        {
                //            SendKey(GPAL.VK_NEXT);   // pagedown
                //            Thread.Sleep(125);
                //        }
                //        rect2 = GetAbsCoordinates(application.BrowserSettings.BrowserType, element);
                //    }
                //}
            }

            // Where inside the element to aim. Zero offset means roughly the middle, as on the browser side; the corner
            // is the border or a row's blank margin. The jitter keeps repeated clicks off a single pixel.
            int offsetX = OffsetX;
            int offsetY = OffsetY;

            if (0 == offsetX && 0 == offsetY)
            {
                int spreadX = Math.Max(0, Math.Min(2, (rect2.Width / 2) - 1));
                int spreadY = Math.Max(0, Math.Min(2, (rect2.Height / 2) - 1));

                offsetX = rect2.Width / 2 + Jitter.Next(-spreadX, spreadX + 1);
                offsetY = rect2.Height / 2 + Jitter.Next(-spreadY, spreadY + 1);
            }

            System.Drawing.Point aimedAt = new System.Drawing.Point(rect2.X + offsetX, rect2.Y + offsetY);

            // A control with no size: use UIAutomation's clickable point. Fallback only, since it ignores the
            // workflow's offset.
            if ((0 == rect2.Width || 0 == rect2.Height)
                && null != element.Ae
                && true == element.Ae.TryGetClickablePoint(out System.Windows.Point clickablePoint))
                aimedAt = new System.Drawing.Point((int)clickablePoint.X, (int)clickablePoint.Y);

            if (true == GPAL.SimulateMouse || true == application.CurrentUOW?.CurrentSelector?.SimulateMouse)
                HardwareHelper.MoveMouse(aimedAt.X, aimedAt.Y, 10, 10);    // up to 10 pixels of random offset on each axis
            else
                HardwareHelper.MoveMouse(aimedAt.X, aimedAt.Y);

            // Return the point the pointer moved to so the caller clicks there.
            return aimedAt;
        }

        private static Random _jitter;
        private static Random Jitter => _jitter ?? (_jitter = new Random(Guid.NewGuid().GetHashCode()));
        /// <summary>
        /// Left clicks each element matched by the unit of work's selectors.
        /// </summary>
        /// <param name="application">The Application whose selectors are clicked</param>
        /// <param name="modifierKeys">Keys held down during the click</param>
        public static void LeftClick(Application application, ModifierKeys modifierKeys = ModifierKeys.NONE)
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
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);    // either All or just those that matched

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    Click(application, selector, elem, ClickType.LeftClick, modifierKeys);

                    if (++rowCount >= HowManyToActOn(application))
                        break;
                }
            }
        }
        /// <summary>
        /// Performs the given click type on every element matched by the selectors in the current unit of work.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are clicked</param>
        /// <param name="clickType">The type of click to perform (left, double, middle, right, etc.)</param>
        /// <returns>True if at least one click was performed</returns>
        public static bool GenericClick(Application application, ClickType clickType)
        {
            List<GPALAutomationElement> matchedElements;
            bool matchedAll = false;
            bool clickPerformed = false;

            foreach (Selector sel in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != sel.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindElements(application, application.CurrentUOW, sel, out matchedAll, out matchedElements);

                if (0 == (matchedElements?.Count ?? 0))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{sel.Name}] matched no elements, so there was nothing to [{clickType}].", application, GPALObjectType.Application);

                if (null != matchedElements)
                {
                    int rowCount = 0;

                    foreach (GPALAutomationElement elem in matchedElements)
                    {
                        Click(application, sel, elem, clickType, ModifierKeys.NONE);
                        clickPerformed = true;

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
            return clickPerformed;
        }
        /// <summary>
        /// Clicks the given element, dispatching to hardware emulation or UI Automation invocation depending on
        /// the selector's interaction type and whether the element is a GPALAutomationElement (Gae).
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="selector">The selector that found the element, used for offsets and interaction type</param>
        /// <param name="element">The element to click</param>
        /// <param name="clickType">The type of click to perform (left, double, middle, right, etc.)</param>
        /// <param name="modifierKeys">Modifier key to hold during the click</param>
        public static void Click(Application application, Selector selector, GPALAutomationElement element, ClickType clickType, ModifierKeys modifierKeys)
        {
            int offsetX, offsetY;

            // Click a control found by image at the matched point, not its middle. On a desktop a point is clicked with
            // the mouse.
            if (null != element.ImageMatch)
                element = element.ImageMatch;
            offsetX = selector.SelectorSettings.OffsetX;
            offsetY = selector.SelectorSettings.OffsetY;

            // A menu item found through the Win32 menu is clicked by sending its command.
            if (0 != element.MenuCommand)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Selector [{selector.Name}] is a menu item, so command [{element.MenuCommand}] was sent to the window it belongs to.", application, GPALObjectType.Application);

                PostMessage(element.MenuWindow, WM_COMMAND, new IntPtr(element.MenuCommand), IntPtr.Zero);
            }
            // Right click has no UIAutomation pattern and needs the mouse, so a hidden desktop cannot be right-clicked.
            else if (ClickType.RightClick == clickType && false == HardwareReaches(application))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] cannot be right clicked on a hidden desktop, since UIAutomation offers no pattern for one. Peek at the desktop to right click there.", application, GPALObjectType.Application);
            }
            else if ((Enums.InteractionType.Hardware == selector.InteractionType || null != element.Gae || true == application.applicationSettings.UseHardware || ClickType.RightClick == clickType)
                     && true == HardwareReaches(application))
                HardwareClick(application, element, clickType, modifierKeys, offsetX, offsetY);
            else if (null != element.Ae)
            {
                UIAutomationClick(application, selector, element, clickType, modifierKeys);
            }
            // An image match is a screen position, so only the mouse can click it, and the mouse cannot reach a hidden
            // desktop.
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] is an image match, which only the mouse can click, and the mouse cannot reach [{application.applicationSettings.HiddenDesktopName}]. Peek at the desktop to click there.", application, GPALObjectType.Application);
        }

        /// <summary>
        /// Clicks through UIAutomation when the control offers a pattern, otherwise with the mouse.<br/>
        /// Invoke is the control's <i>default action</i>, not always a left click. On a list, tree, data or tab item a
        /// left click selects and a double click activates. So a left click on a selectable item uses
        /// SelectionItemPattern.Select, and a double click uses Invoke. A double click on an item with children and no
        /// Invoke, such as a WinForms tree item, opens or closes it through ExpandCollapsePattern.<br/>
        /// Middle, right and modified clicks have no pattern (Shift range selection has none), so they use the mouse.<br/>
        /// A control with no pattern uses the mouse; a pattern that throws falls back to the mouse and reports it.<br/>
        /// <b>NOTE:</b> a control that can only be clicked with the mouse cannot be driven on a hidden desktop.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="selector">The selector that found the element, used for offsets on the mouse route</param>
        /// <param name="element">The element to click</param>
        /// <param name="clickType">The type of click to perform</param>
        /// <param name="modifierKeys">Modifier key to hold on the mouse route</param>
        public static void UIAutomationClick(Application application, Selector selector, GPALAutomationElement element, ClickType clickType, ModifierKeys modifierKeys)
        {
            object objPattern;
            bool invoked = false;
            string control = element.Ae.Current.LocalizedControlType;

            if (ModifierKeys.NONE != modifierKeys)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"A click holding [{Browser.Browser.GetModifierKeys(modifierKeys)}] has no UIAutomation equivalent, so [{control}] is clicked with the mouse.", application, GPALObjectType.Application);
            else if (ClickType.LeftClick == clickType && true == element.Ae.TryGetCurrentPattern(SelectionItemPattern.Pattern, out objPattern))
            {
                // A left click on a selectable item selects it. Invoke is the double-click action, so do not use it
                // here.
                try
                {
                    ((SelectionItemPattern)objPattern).Select();
                    invoked = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked through SelectionItemPattern.", application, GPALObjectType.Application);
                }
                catch (Exception)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"SelectionItemPattern threw on control [{control}], falling back to the mouse.", application, GPALObjectType.Application);
                }
            }
            else if ((ClickType.LeftClick == clickType || ClickType.LeftDoubleClick == clickType)
                     && true == element.Ae.TryGetCurrentPattern(InvokePattern.Pattern, out objPattern))
            {
                // Invoke: a left click on a button, or a double click on anything with InvokePattern.
                try
                {
                    ((InvokePattern)objPattern).Invoke();
                    invoked = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked through InvokePattern.", application, GPALObjectType.Application);
                }
                catch (Exception)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"InvokePattern threw on control [{control}], falling back to the mouse.", application, GPALObjectType.Application);
                }
            }
            else if (ClickType.LeftDoubleClick == clickType
                     && true == element.Ae.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out objPattern)
                     && ExpandCollapseState.LeafNode != ((ExpandCollapsePattern)objPattern).Current.ExpandCollapseState)
            {
                // A double click on a tree item with children opens it, or closes it when open. A WinForms tree item
                // offers no InvokePattern, so this is the double click without the mouse.
                try
                {
                    ExpandCollapsePattern expandCollapse = (ExpandCollapsePattern)objPattern;

                    if (ExpandCollapseState.Expanded == expandCollapse.Current.ExpandCollapseState)
                        expandCollapse.Collapse();
                    else
                        expandCollapse.Expand();

                    invoked = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked through ExpandCollapsePattern.", application, GPALObjectType.Application);
                }
                catch (Exception)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ExpandCollapsePattern threw on control [{control}], falling back to the mouse.", application, GPALObjectType.Application);
                }
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Control [{control}] offers no pattern for a [{clickType}], so the mouse is how it is clicked.", application, GPALObjectType.Application);

            // No InvokePattern, or anything but a plain left click: use the mouse.
            if (false == invoked)
                HardwareClick(application, element, clickType, modifierKeys, selector?.OffsetX ?? 5, selector?.OffsetY ?? 5); // with no selector, the offset is 5, 5
        }

        /// <summary>
        /// Returns an element's name for click messages, or its control type when it has no name.
        /// </summary>
        /// <param name="element">The element being clicked</param>
        /// <returns>The name, or the control type when there is no name</returns>
        private static string NameOf(GPALAutomationElement element)
        {
            string retVal = null == element || null == element.Ae ? "nothing" : element.Ae.Current.Name;

            if (true == string.IsNullOrWhiteSpace(retVal) && null != element && null != element.Ae)
                retVal = element.Ae.Current.ControlType.ProgrammaticName;

            return retVal;
        }

        /// <summary>
        /// Clicks the given element using hardware emulation: moves the mouse to the element (at the given offset,
        /// or a random point within it if no offset is given) and performs the click, optionally holding a modifier key.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to click</param>
        /// <param name="clickType">The type of click to perform (left, double, middle, right, etc.)</param>
        /// <param name="modifierKeys">Modifier key to hold during the click</param>
        /// <param name="OffsetX">X offset, relative to the element, to click at; if 0 and <paramref name="OffsetY"/> is 0, a random point within the element is used</param>
        /// <param name="OffsetY">Y offset, relative to the element, to click at; if 0 and <paramref name="OffsetX"/> is 0, a random point within the element is used</param>
        public static void HardwareClick(Application application, GPALAutomationElement element, ClickType clickType, ModifierKeys modifierKeys, int OffsetX = 0, int OffsetY = 0)
        {
            // the real mouse reaches only the desktop on screen
            if (false == HardwareReaches(application))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element [{NameOf(element)}] cannot be [{clickType}] clicked with the mouse on a hidden desktop. Peek at the desktop to click it there.", application, GPALObjectType.Application);
            else
                try
                {
                    if (ModifierKeys.NONE != modifierKeys)
                        HardwareHelper.PressModifierKey(modifierKeys);

                    // The move returns the point it aimed at, so the click lands where the pointer is.
                    System.Drawing.Point clickAt = HardwareMoveTo(application, element, OffsetX, OffsetY);

                    // Publish between modifier down and up, where the click happens.
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked with modifiers [{Browser.Browser.GetModifierKeys(modifierKeys)}] at [{clickAt.X},{clickAt.Y}] with the mouse.", application, GPALObjectType.Application);

                    HardwareHelper.HardwareClick(clickAt.X, clickAt.Y, clickType);
                }
                catch
                {

                }
                finally
                {
                    if (ModifierKeys.NONE != modifierKeys)
                        HardwareHelper.ReleaseModifierKeys(modifierKeys);
                }
        }
        /// <summary>
        /// Performs a drag and drop on each element matched by the selectors in the current unit of work, using
        /// hardware mouse down/up at the element's location plus the selector's offset and delta, optionally holding a modifier key.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are dragged</param>
        /// <param name="modifierKeys">Modifier key to hold during the drag and drop</param>
        public static void DragAndDrop(Application application, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            var selectors = new List<Selector>();

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
                if (SelectorType.Selector == selector.SelectorType)
                    selectors.Add(selector);

            // Two selectors give the ends of the drag: grab the first, release over the second. The distance comes from
            // the elements' positions, so it survives resizes, re-sorts and other screens. A drag is the real mouse,
            // which reaches only the desktop on screen
            if (false == HardwareReaches(application))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "A drag and drop cannot be done on a hidden desktop, since it is the real mouse. Peek at the desktop to drag there.", application, GPALObjectType.Application);
            else if (2 == selectors.Count)
                DragToTarget(application, selectors[0], selectors[1], modifierKeys);
            else
                DragByDelta(application, modifierKeys);
        }

        /// <summary>
        /// Drags the first selector's element onto the second's.
        /// </summary>
        /// <remarks>
        /// Each end is the element's middle unless its selector gives an offset. The target's DeltaX/DeltaY are added
        /// to the drop point.
        /// </remarks>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="source">The selector for the element to drag</param>
        /// <param name="target">The selector for the element to drop it on</param>
        /// <param name="modifierKeys">Modifier key to hold for the duration of the drag</param>
        private static void DragToTarget(Application application, Selector source, Selector target, ModifierKeys modifierKeys)
        {
            Rectangle from = FirstRectangle(application, source);
            Rectangle to = FirstRectangle(application, target);

            if (true == from.IsEmpty || true == to.IsEmpty)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"A drag needs both ends. Selector [{source.Name}] {(true == from.IsEmpty ? "matched nothing" : "matched")} and selector [{target.Name}] {(true == to.IsEmpty ? "matched nothing" : "matched")}.", application, GPALObjectType.Application);
            else
            {
                // Grab the middle unless the selector gives an offset. The corner of a list row is blank margin, and
                // dragging from there makes a rubber-band selection.
                int grabX = from.X + (0 == source.OffsetX ? from.Width / 2 : source.OffsetX);
                int grabY = from.Y + (0 == source.OffsetY ? from.Height / 2 : source.OffsetY);

                int dropX = to.X + (0 == target.OffsetX ? to.Width / 2 : target.OffsetX) + target.DeltaX;
                int dropY = to.Y + (0 == target.OffsetY ? to.Height / 2 : target.OffsetY) + target.DeltaY;

                // A held key is system-wide; release it in a finally.
                try
                {
                    HardwareHelper.ReleaseStrayModifiers(modifierKeys);

                    if (ModifierKeys.NONE != modifierKeys)
                        HardwareHelper.PressModifierKey(modifierKeys);

                    HardwareHelper.MouseDown(grabX, grabY, ClickType.LeftClick);
                    HardwareHelper.DragTravel(dropX, dropY, true == GPAL.SimulateMouse || true == source.SimulateMouse);
                    HardwareHelper.MouseUp(dropX, dropY, ClickType.LeftClick);
                }
                finally
                {
                    if (ModifierKeys.NONE != modifierKeys)
                        HardwareHelper.ReleaseModifierKeys(modifierKeys);
                }

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Dragged [{source.Name}] from [{grabX},{grabY}] onto [{target.Name}] at [{dropX},{dropY}]", application, GPALObjectType.Application);
            }
        }

        /// <summary>
        /// Where the first element a selector matched sits on screen.
        /// </summary>
        /// <param name="application">The Application being searched</param>
        /// <param name="selector">The selector to resolve</param>
        /// <returns>The element's screen rectangle, empty when the selector matched nothing</returns>
        private static Rectangle FirstRectangle(Application application, Selector selector)
        {
            Rectangle retVal = Rectangle.Empty;

            ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out List<GPALAutomationElement> matchedElements);

            if (null != matchedElements && 0 < matchedElements.Count)
                elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

            if (false == selector.DeleteMe && null != elems && 0 < elems.Count)
            {
                // a control found under an image is dragged from, or dropped on, where the picture is
                GPALAutomationElement elem = elems[0].ImageMatch ?? elems[0];

                if (null != elem.Gae)
                    retVal = new Rectangle(elem.Gae.Location, elem.Gae.Size);
                else
                    retVal = new Rectangle(
                        new System.Drawing.Point((int)elem.Ae.Current.BoundingRectangle.X, (int)elem.Ae.Current.BoundingRectangle.Y),
                        new System.Drawing.Size((int)elem.Ae.Current.BoundingRectangle.Width, (int)elem.Ae.Current.BoundingRectangle.Height));
            }
            else
                PublishToEventHandler(application, selector, elems?.ToList());

            return retVal;
        }

        /// <summary>
        /// Drags each matched element by its selector's distance, for a destination that is not an element: a slider, a
        /// splitter, a rubber-band selection.
        /// </summary>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="modifierKeys">Modifier key to hold for the duration of the drag</param>
        private static void DragByDelta(Application application, ModifierKeys modifierKeys)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement found in elems)
                {
                    Rectangle rect;

                    // a control found under an image is dragged from where the picture is
                    GPALAutomationElement elem = found.ImageMatch ?? found;

                    if (null != elem.Gae)
                        rect = new Rectangle(elem.Gae.Location, elem.Gae.Size);
                    else
                        rect = new Rectangle(
                            new System.Drawing.Point((int)elem.Ae.Current.BoundingRectangle.X, (int)elem.Ae.Current.BoundingRectangle.Y),
                            new System.Drawing.Size((int)elem.Ae.Current.BoundingRectangle.Width, (int)elem.Ae.Current.BoundingRectangle.Height));

                    // Mouse down at element origin + OffsetX/Y, move by DeltaX/Y, mouse up. Windows only starts a drag
                    // when the pointer moves past its threshold with the button held; down then up is a click. The
                    // browser side does the same.
                    // Grab the middle unless the selector gives an offset, as the two-selector drag does.
                    int grabX = rect.X + (0 == selector.OffsetX ? rect.Width / 2 : selector.OffsetX);
                    int grabY = rect.Y + (0 == selector.OffsetY ? rect.Height / 2 : selector.OffsetY);

                    int endX = grabX + selector.DeltaX;
                    int endY = grabY + selector.DeltaY;

                    // a held key is machine wide, so it is released in a finally whatever happens in between
                    try
                    {
                        HardwareHelper.ReleaseStrayModifiers(modifierKeys);

                        if (ModifierKeys.NONE != modifierKeys)
                            HardwareHelper.PressModifierKey(modifierKeys);

                        HardwareHelper.MouseDown(grabX, grabY, ClickType.LeftClick);
                        HardwareHelper.DragTravel(endX, endY, true == GPAL.SimulateMouse || true == selector.SimulateMouse);
                        HardwareHelper.MouseUp(endX, endY, ClickType.LeftClick);
                    }
                    finally
                    {
                        if (ModifierKeys.NONE != modifierKeys)
                            HardwareHelper.ReleaseModifierKeys(modifierKeys);
                    }

                    if (++rowCount >= HowManyToActOn(application))
                        break;
                }
            }
        }
    }
}
