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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using GenerallyPositive.Application;
using Microsoft.Win32.SafeHandles;
using OpenQA.Selenium;
using OpenQA.Selenium.DevTools;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class MagicHelper
    {
        /// <summary>
        /// Registers a script to run on every new document load. Persists until <see cref="ClearInjectedScripts"/> is called.
        /// <example>browser.MagicHelper.InjectScript("window.__hooked = true;");</example>
        /// </summary>
        /// <param name="script">The JavaScript code to inject</param>
        public void InjectScript(string script)
        {
            string answered = Client.InjectScript(script).Execute();

            // The extension returns the registered script id or an error. Warn on error, or a failed registration
            // goes unnoticed until the page misbehaves. Usually Allow user scripts is off.
            if (true == answered?.Contains("error"))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The extension did not register the script [{answered}]", this, GPALObjectType.Other);
        }

        /// <summary>
        /// Runs a script once on the current page and returns its result; <see cref="InjectScript"/> runs on every
        /// page load until cleared. Needs Allow user scripts on.
        /// <example>string title = browser.MagicHelper.ExecuteJavaScript("document.title");</example>
        /// </summary>
        /// <param name="script">The JavaScript code to run</param>
        /// <returns>What the script returned, as json</returns>
        public string ExecuteJavaScript(string script)
        {
            return Client.ExecuteJavaScript(script).Execute();
        }

        /// <summary>
        /// Page title, __NEXT_DATA__ and React Flight chunks as json.<br/>
        /// Read from the DOM rather than the HTML source, so no unescaping or HTML parsing.
        /// <example>string json = browser.MagicHelper.GetHydrationScripts();</example>
        /// </summary>
        /// <returns>Json holding title, nextData and flightChunks, or null when the page could not answer</returns>
        public string GetHydrationScripts()
        {
            return Client.GetHydrationScripts().Execute();
        }

        /// <summary>
        /// Removes all scripts previously registered via <see cref="InjectScript"/>.
        /// <example>browser.MagicHelper.ClearInjectedScripts();</example>
        /// </summary>
        public void ClearInjectedScripts()
        {
            Client.ClearInjectedScripts().Execute();
        }

        /// <summary>
        /// Evaluates an XPath expression to locate a single element
        /// <example>string elementId = browser.MagicHelper.Evaluate("//div[@id='example']");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>The element ID</returns>
        public GPALElement Evaluate(string xpath)
        {
            return Client.Evaluate(xpath).Execute<GPALElement>();
        }

        /// <summary>
        /// Evaluates an XPath expression to locate multiple elements
        /// <example>string[] elementIds = browser.MagicHelper.EvaluateAll("//div");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> EvaluateAll(string xpath)
        {
            return Client.EvaluateAll(xpath).Execute<List<GPALElement>>();
        }
        /// <summary>
        /// Evaluates an XPath expression to locate a single element
        /// <example>string elementId = browser.MagicHelper.Evaluate("//div[@id='example']");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>The element ID</returns>
        public GPALElement EvaluatePersistent(string xpath)
        {
            return Client.EvaluatePersistent(xpath).Execute<GPALElement>();
        }

        /// <summary>
        /// Evaluates an XPath expression to locate multiple elements
        /// <example>string[] elementIds = browser.MagicHelper.EvaluateAll("//div");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> EvaluateAllPersistent(string xpath)
        {
            return Client.EvaluateAllPersistent(xpath).Execute<List<GPALElement>>();
        }
    }
}
