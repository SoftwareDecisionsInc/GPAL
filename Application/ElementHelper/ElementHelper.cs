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
        delegate ReadOnlyCollection<GPALAutomationElement> FindElementsByDelegate(Application application, SelectorPathEntry selectorPath, bool searchForSelector = true, AutomationElement element = null);
        // virtual keycodes: https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes?redirectedfrom=MSDN
        // scan codes: https://en.wikipedia.org/wiki/Scancode
        static byte ScanCode {get; } = 0;

        // to avoid repeat messages for the same selector when waiting for a selector
        static List<string> listOfMessages = new List<string>();
        static Selector lastSelector = null;
        // What the last FindElements on this thread found, with the selector path in full, for WaitFor to publish once
        // its wait is over; the wait's line is the only one that gives the path whole. Per thread, so applications run
        // in parallel do not publish one another's searches
        [ThreadStatic] static string lastFoundFullMessage;
    }
}
