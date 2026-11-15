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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public partial class RESTClient
    {
        /// <summary>
        /// Evaluates an XPath expression
        /// <example>client.Evaluate("//div").Execute();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution Evaluate(string xpath)
        {
            WithEndpoint(ApiEndpoint.Evaluate).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression for multiple elements
        /// <example>client.EvaluateAll("//div").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution EvaluateAll(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluateAll).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression
        /// <example>client.Evaluate("//div").Execute();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution EvaluatePersistent(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluatePersistent).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression for multiple elements
        /// <example>client.EvaluateAll("//div").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution EvaluateAllPersistent(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluateAllPersistent).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Executes JavaScript code
        /// <example>client.ExecuteJavaScript("return document.title").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="script">The JavaScript code</param>
        /// <returns></returns>
        public IAllowRESTParameters ExecuteJavaScript(string script)
        {
            WithEndpoint(ApiEndpoint.ExecuteJavaScript);
            _parameters = null == _parameters
                ? new { script }
                : MergeParameters(_parameters, new { script });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Registers a script to run on every new document load. Persists until <see cref="ClearInjectedScripts"/> is called.
        /// </summary>
        /// <param name="script">The JavaScript code to inject</param>
        /// <returns></returns>
        public IAllowRESTParameters InjectScript(string script)
        {
            WithEndpoint(ApiEndpoint.InjectScript);
            _parameters = null == _parameters
                ? new { script }
                : MergeParameters(_parameters, new { script });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Removes all scripts previously registered via <see cref="InjectScript(string)"/>.
        /// </summary>
        /// <returns></returns>
        public IAllowRESTParameters ClearInjectedScripts()
        {
            WithEndpoint(ApiEndpoint.ClearInjectedScripts);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Gets the page title, the __NEXT_DATA__ payload and any React Flight chunks, read from the document
        /// rather than its markup.
        /// <example>client.GetHydrationScripts().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetHydrationScripts()
        {
            WithEndpoint(ApiEndpoint.GetHydrationScripts);
            return this;
        }
    }
}
