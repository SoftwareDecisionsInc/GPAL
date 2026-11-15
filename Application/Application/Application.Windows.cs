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
        /// Maximize the application to full-screen
        /// </summary>
        public IAllowAllApplicationAndAllSelector Maximize
        {
            get
            {
                if (null != Process)
                    ApplicationHelper.ShowWindow(Process.MainWindowHandle, 3);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min
                return this;
            }
        }
        /// <summary>
        /// Minimize the application to the taskbar
        /// </summary>
        public IAllowAllApplicationAndAllSelector Minimize
        {
            get
            {
                ApplicationHelper.ShowWindow(Process.MainWindowHandle, 2);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min
                return this;
            }
        }
        /// <summary>
        /// Restore the application to 'original' size, unminimize
        /// </summary>
        public IAllowAllApplicationAndAllSelector Restore
        {
            get
            {
                ApplicationHelper.ShowWindow(Process.MainWindowHandle, 1);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min
                return this;
            }
        }
        /// <summary>
        /// Resize the window last matched by WaitForWindow to the specified pixel dimensions.
        /// </summary>
        public IAllowAllApplicationAndAllSelector ResizeWindow(int width, int height)
        {
            if (true == InAction(true))
                ApplicationHelper.ResizeWindow(ApplicationSettings.LastWindowHandle, width, height);
            return this;
        }
        /// <summary>
        /// Move the window last matched by WaitForWindow to the specified screen coordinates.
        /// </summary>
        public IAllowAllApplicationAndAllSelector MoveWindow(int x, int y)
        {
            if (true == InAction(true))
                ApplicationHelper.MoveWindow(ApplicationSettings.LastWindowHandle, x, y);
            return this;
        }
        /// <summary>
        /// Switch to any TabControl defined in the current UOW. This will hardware click the tab control if the SelectionItemPattern fails to allow a click
        /// If .WithAllThatMatch is defined on the current UOW, GPAL will attempt to switch to {num}  tabs that matched the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllActionAndAnySelector SwitchToTab(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.SwitchToTab(this);
            return this;
        }
    }
}
