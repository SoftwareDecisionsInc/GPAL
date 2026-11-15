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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Configure this client to execute the given JavaScript in the page.
        /// </summary>
        /// <param name="script">The JavaScript to execute</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParameters ExecuteJavaScript(string script)
        {
            WithEndpoint(ApiEndpoint.ExecuteJavaScript);
            SetParameter("expression", script);
            return this;
        }

        /// <summary>
        /// Configure this client to inject the given JavaScript into the page.
        /// </summary>
        /// <param name="script">The JavaScript to inject</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InjectScript(string script)
        {
            WithEndpoint(ApiEndpoint.InjectScript);
            SetParameter("script", script);
            return this;
        }

        /// <summary>
        /// Configure this client to remove all previously injected scripts (via <see cref="InjectScript(string)"/>).
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ClearInjectedScripts()
        {
            WithEndpoint(ApiEndpoint.ClearInjectedScripts);
            return this;
        }

        /// <summary>
        /// Configure this client to evaluate the given XPath expression and return the first matching node.
        /// </summary>
        /// <param name="xpath">XPath expression</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Evaluate(string xpath)
        {
            WithEndpoint(ApiEndpoint.Evaluate);
            WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Configure this client to evaluate the given XPath expression and return all matching nodes.
        /// </summary>
        /// <param name="xpath">XPath expression</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution EvaluateAll(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluateAll);
            WithXPath(xpath);
            return this;
        }
    }
}
