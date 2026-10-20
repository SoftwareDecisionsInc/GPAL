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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Application
{
    internal class ApplicationSettings
    {
        internal Application Application { get; set; }
        internal string ApplicationPath { get; set; }
        // name of this application instance, set by GPAL factory
        internal string Name { get; set;  }  = null;

        internal Process Process { get; set; } = null;
        // load images on a web page?
        internal bool LoadImages { get; set; } = false;

        // open pdf documents in external program?
        internal bool OpenPDFExternally { get; set; } = true;
        
        // open file save dialog
        internal bool PromptForDownload { get; set; } = true;

        internal string PlatformName { get; set; } = "Windows 10";

        internal bool Maximize { get; set; } = false;

        internal bool Minimize { get; set; } = false;

        internal Selector MySelector { get; set; } = null;

        internal AutomationElement RootAutomationElement { get; set; } = null;
        internal object ManagedRootAutomationElement { get; set; } = null;

        internal System.Windows.Forms.Form CurrentForm { get; set; } = null;
        internal int WaitForWindowTimeout { get; set; } = 10; // seconds
        internal bool UseHardware { get; set; } = false;    // instead of uiautomation, use hardware emulation
        internal bool NewInstance { get; set; } = false;    // launch rather than attach to a running copy

        // Set by WithHiddenDesktop: the program runs on its own Win32 desktop object. True from the start when the
        // workflow itself started on a hidden desktop.
        internal bool HiddenDesktop { get; set; } = null != GenerallyPositive.Browser.HiddenDesktop.ProcessDesktop;

        // The hidden desktop's name. Applications with the same name share it; no name gets its own. Set to the
        // workflow's own desktop when it started on a hidden one.
        internal string HiddenDesktopName { get; set; } = GenerallyPositive.Browser.HiddenDesktop.ProcessDesktop ?? GenerallyPositive.Browser.HiddenDesktop.DefaultName;

        // True when this program's desktop was made for it alone (WithHiddenDesktop with no name), so Close releases it.
        internal bool HiddenDesktopOwned { get; set; } = false;

        // Set once the hidden desktop's image warning has been published.
        internal bool HiddenDesktopImageReported { get; set; } = false;

        internal IntPtr LastWindowHandle { get; set; } = IntPtr.Zero;

        internal bool DeleteFileBeforeDownload { get; set; } = false;

        internal ArrayList Parameters 
        { 
            get; 
            set; 
        }

        internal ApplicationSettings(Application application)
        {
            Application = application;
            Parameters = new ArrayList();
        }
    }
}

