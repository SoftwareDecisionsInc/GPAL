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
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetMenu(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetSubMenu(IntPtr menu, int position);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMenuItemCount(IntPtr menu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMenuItemID(IntPtr menu, int position);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetMenuString(IntPtr menu, int item, System.Text.StringBuilder text, int count, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private const uint MF_BYPOSITION = 0x0400;
        private const int WM_COMMAND = 0x0111;

        /// <summary>
        /// The names a path asks for from a menu, in order, so Window/MenuBar/MenuItem[@Name='File']//MenuItem
        /// [@Name='Save As...'] reads as File then Save As.
        /// </summary>
        /// <param name="selectorPath">The path to read</param>
        /// <returns>The menu item names the path names, empty when it names none</returns>
        private static List<string> MenuNamesIn(string selectorPath)
        {
            List<string> retVal = new List<string>();

            foreach (Match step in Regex.Matches(selectorPath ?? string.Empty, @"MenuItem[^/]*?\[@Name='([^']*)'\]"))
                retVal.Add(step.Groups[1].Value);

            return retVal;
        }

        /// <summary>
        /// Returns a menu item's name without the access-key ampersand and the tab-separated shortcut text.
        /// </summary>
        /// <param name="menu">The menu holding it</param>
        /// <param name="position">Which item</param>
        /// <returns>The item's name</returns>
        private static string MenuItemName(IntPtr menu, int position)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder(256);

            GetMenuString(menu, position, text, text.Capacity, MF_BYPOSITION);

            string name = text.ToString().Replace("&", string.Empty);
            int shortcut = name.IndexOf('\t');

            return (0 <= shortcut ? name.Substring(0, shortcut) : name).Trim();
        }

        /// <summary>
        /// Returns the command ID for a path of menu items from the window's Win32 menu.
        /// </summary>
        /// <remarks>
        /// A menu opens only by mouse or keyboard, so on a hidden desktop it cannot be walked through UIAutomation.
        /// </remarks>
        /// <param name="menu">The menu to look in</param>
        /// <param name="names">The item names to follow, outermost first</param>
        /// <param name="index">Which of those names is being looked for</param>
        /// <returns>The command ID of the last named item; zero when the path does not lead to one</returns>
        private static int CommandForMenuPath(IntPtr menu, List<string> names, int index)
        {
            int retVal = 0;

            if (IntPtr.Zero != menu && index < names.Count)
                for (int position = 0; position < GetMenuItemCount(menu) && 0 == retVal; position++)
                {
                    if (false == string.Equals(MenuItemName(menu, position), names[index], StringComparison.OrdinalIgnoreCase))
                        continue;

                    IntPtr inside = GetSubMenu(menu, position);

                    retVal = index == names.Count - 1 && IntPtr.Zero == inside
                           ? GetMenuItemID(menu, position)
                           : CommandForMenuPath(inside, names, index + 1);
                }

            return retVal;
        }

        /// <summary>
        /// A menu item found through the window's menu, for a path UIAutomation could not walk.
        /// </summary>
        /// <param name="application">The application being searched</param>
        /// <param name="selectorPath">The path that found nothing</param>
        /// <returns>An element carrying the command that item sends, null when the path names no such item</returns>
        private static GPALAutomationElement MenuItemFor(Application application, string selectorPath)
        {
            GPALAutomationElement retVal = null;
            List<string> names = MenuNamesIn(selectorPath);
            AutomationElement root = application.RootAutomationElement;

            if (0 < names.Count && null != root)
            {
                IntPtr window = new IntPtr(root.Current.NativeWindowHandle);
                int command = CommandForMenuPath(GetMenu(window), names, 0);

                if (0 != command)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{string.Join(" then ", names)}] was not there to be walked, so it was read from the window's own menu, where it sends command [{command}].", application, GPALObjectType.Application);

                    retVal = new GPALAutomationElement { MenuCommand = command, MenuWindow = window };
                }
            }

            return retVal;
        }

        /// <summary>
        /// Expands a collapsed menu item (e.g. a submenu), trying ExpandCollapsePattern, then InvokePattern,
        /// then a hardware click fallback at a random point within the element. If <see cref="GPAL.SimulateMouse"/>
        /// or the current selector requests it, moves and clicks the hardware mouse over the element instead.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="automationElement">The menu item element to expand</param>
        public static void ExpandMenuItem(Application application, AutomationElement automationElement)
        {
            // the real mouse where it reaches; on a hidden desktop, the patterns below
            if ((true == GPAL.SimulateMouse || true == application.CurrentUOW.CurrentSelector?.SimulateMouse) && true == HardwareReaches(application))
            {
                HardwareHelper.MoveMouse((int)automationElement.Current.BoundingRectangle.X, (int)automationElement.Current.BoundingRectangle.Y, 10, 10);    // up to 10 pixels of random offset on each axis
                HardwareHelper.LeftClick((int)automationElement.Current.BoundingRectangle.X, (int)automationElement.Current.BoundingRectangle.Y);
                Thread.Sleep(250); // wait for display before trying to find elements
            }
            else
            {
                try
                {
                    ExpandCollapsePattern pattern = automationElement.GetCurrentPattern(ExpandCollapsePattern.Pattern) as ExpandCollapsePattern;
                    pattern.Expand();
                }
                catch (Exception)
                {
                    try
                    {
                        InvokePattern pattern = automationElement.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                        pattern.Invoke();
                    }
                    catch (Exception ex2)
                    {
                        // A menu with neither pattern only opens with the mouse, if at all. Give up quietly; the Win32
                        // menu is searched next. Do not query the element: invoking a menu item can remove it, and a
                        // removed element throws.
                        if (false == HardwareReaches(application))
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "A menu would not open through UIAutomation and the mouse cannot open it either, so what is on it is read from the window's own menu.", application, GPALObjectType.Application, ex2);
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to expand menuitem for Control [{automationElement.Current.LocalizedControlType}], using hardware fallback.", application, GPALObjectType.Application);

                            Random random = new Random(10007);

                            int offsetX = random.Next(1, (int)automationElement.Current.BoundingRectangle.Width - 2);
                            int offsetY = random.Next(1, (int)automationElement.Current.BoundingRectangle.Height - 2);
                            ElementHelper.HardwareClick(application, new GPALAutomationElement(automationElement), Enums.ClickType.LeftClick, Enums.ModifierKeys.NONE, offsetX, offsetY);
                        }
                    }
                }
            }
        }
    }
}
