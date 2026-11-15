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
        /// Resolves a CSS selector to a list of GPALElements via the puppeteer communicator.
        /// </summary>
        /// <param name="selector">CSS selector to evaluate</param>
        /// <returns>The matched elements</returns>
        private List<GPALElement> GetElements(string selector)
        {
            return _puppeteerCommunicator.EvaluateSelector(selector).Result;
        }

        /// <summary>
        /// Configure this client to retrieve the shadow root of the element matching the given CSS selector.
        /// </summary>
        /// <param name="css">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetShadowRoot(string css)
        {
            WithEndpoint(ApiEndpoint.GetShadowRoot);
            WithCss(css);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InElement(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context back to the main document.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InMainDom()
        {
            WithEndpoint(ApiEndpoint.SwitchToDefaultContent);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the shadow root of the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution InShadowDom(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToShadowRoot);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context back to the main document.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToDefaultContent()
        {
            WithEndpoint(ApiEndpoint.SwitchToDefaultContent);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToElement(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the iframe matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the iframe element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToFrame(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToFrame);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to switch context into the shadow root of the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SwitchToShadowRoot(string selector)
        {
            WithEndpoint(ApiEndpoint.SwitchToShadowRoot);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the parent node of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetParentNode(string selector)
        {
            WithEndpoint(ApiEndpoint.GetParentNode);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to find the first element matching the given CSS selector.
        /// </summary>
        /// <param name="css">CSS selector</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution QuerySelector(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelector);
            WithCss(css);
            return this;
        }

        /// <summary>
        /// Configure this client to find all elements matching the given CSS selector.
        /// </summary>
        /// <param name="css">CSS selector</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution QuerySelectors(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelectors);
            WithCss(css);
            return this;
        }
    }
}
