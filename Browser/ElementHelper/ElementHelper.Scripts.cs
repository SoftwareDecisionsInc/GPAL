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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Threading;
//using DocumentFormat.OpenXml.Bibliography;
//using DocumentFormat.OpenXml.Drawing;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Keys = OpenQA.Selenium.Keys;
using MatchType = GenerallyPositive.Enums.MatchType;

namespace GenerallyPositive.Browser
{
    internal partial class ElementHelper
    {
        /// <summary>
        /// Runs a JavaScript expression in the page and returns its value, or null if the engine could not run it.<br/>
        /// Selenium needs "return " in front; OttoMagic and Puppeteer take the expression as is, saving a second call.
        /// When OttoMagic cannot run scripts (usually Allow user scripts is off) it answers {"error": ...}; this
        /// returns null and warns once per run.
        /// </summary>
        /// <param name="browser">Browser to run the expression in</param>
        /// <param name="expression">A single javascript expression</param>
        /// <returns>What the expression produced, or null</returns>
        internal static string RunPageScript(Browser browser, string expression)
        {
            // trimmed, because a return followed by a line break returns nothing
            string retVal = BrowserHelper.ExecuteJavaScript(browser, true == browser.UseSelenium ? "return " + expression.Trim() : expression);

            if (true == browser.UseOttoMagic && true == retVal?.TrimStart().StartsWith("{\"error\""))
            {
                if (false == _userScriptsWarned)
                {
                    _userScriptsWarned = true;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"OttoMagic could not run a page script [{retVal}]. Turn on user scripts with the Turn On User Scripts button in OttoMagic's settings so GPAL can run its page scripts, such as using image matches as elements.", browser, GPALObjectType.Browser);
                }

                retVal = null;
            }
            else if ("fail" == retVal)
                retVal = null;

            return retVal;
        }
    }
}
