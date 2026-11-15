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
        /// Configure this client to navigate the browser back one page in history.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Back()
        {
            WithEndpoint(ApiEndpoint.Back);
            return this;
        }

        /// <summary>
        /// Configure this client to navigate the browser forward one page in history.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Forward()
        {
            WithEndpoint(ApiEndpoint.Forward);
            return this;
        }

        /// <summary>
        /// Configure this client to query the puppeteer server's ready status, optionally for a specific session token.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetReadyStatus()
        {
            return GetReadyStatus(null);
        }

        /// <summary>
        /// GetReadyStatus() plus a token identifying the wait loop, so a repeated status logs once per wait. Internal.
        /// </summary>
        /// <param name="sessionToken">Identifies the wait this reading belongs to</param>
        /// <returns>Fluent interface to execute the request</returns>
        internal IAllowPuppeteerExecution GetReadyStatus(string sessionToken)
        {
            WithEndpoint(ApiEndpoint.GetReadyStatus);

            if (false == string.IsNullOrEmpty(sessionToken))
                WithSessionToken(sessionToken);

            return this;
        }

        /// <summary>
        /// Configure this client to check whether network activity has gone idle.
        /// </summary>
        /// <param name="maxConnections">Maximum number of in-flight connections still considered idle</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution CheckNetworkIdle(int maxConnections = 0)
        {
            WithEndpoint(ApiEndpoint.CheckNetworkIdle);
            WithMaxConnections(maxConnections);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the current page URL.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetCurrentUrl()
        {
            WithEndpoint(ApiEndpoint.GetCurrentUrl);
            return this;
        }

        /// <summary>
        /// Configure this client to navigate the current tab to the given URL, resolving relative URLs and checking robots.txt restrictions.
        /// </summary>
        /// <param name="url">The URL to navigate to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoTo(GPALUrl url)
        {
            url.ForUrl(MagicHelper.GetFullUrl(url?.Url, _puppeteerCommunicator.Browser, out _puppeteerCommunicator.Browser._areRobotsAllowed));

            if (false == _puppeteerCommunicator.Browser.AreRobotsAllowed && true == _puppeteerCommunicator.Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url?.Url}] not allowed to robots and your honoring robots.txt. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerClient);
                url.ForUrl("https://google.com");
            }
            WithEndpoint(ApiEndpoint.GoTo);
            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to refresh the current page.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Refresh()
        {
            WithEndpoint(ApiEndpoint.Refresh);
            return this;
        }
    }
}
