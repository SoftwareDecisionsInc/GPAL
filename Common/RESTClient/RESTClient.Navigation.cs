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
        // Publishes the goto about to be sent
        private void NavigateToUrl()
        {
            string who = string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ";

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"{who}Go to [{ParameterValue("url")}]", this, GPALObjectType.RestClient);
        }

        // Publishes the network-idle check about to be sent, or what it answered
        private void NetworkIdle(bool before, object result)
        {
            string who = string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ";

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                true == before ? $"{who}Waiting for the network to idle" : $"{who}Network is [{result?.ToString().Trim('"')}]",
                this, GPALObjectType.RestClient);
        }

        /// <summary>
        /// Navigates browser history back
        /// <example>client.Back().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Back()
        {
            WithEndpoint(ApiEndpoint.Back);
            return this;
        }

        /// <summary>
        /// Checks if network is idle with max connections
        /// <example>client.CheckNetworkIdle(5).Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="maxConnections">Maximum connections allowed</param>
        /// <returns></returns>
        public IAllowRESTExecution CheckNetworkIdle(int maxConnections = 0)
        {
            WithEndpoint(ApiEndpoint.CheckNetworkIdle).WithMaxConnections(maxConnections);
            return this;
        }

        /// <summary>
        /// Navigates browser history forward
        /// <example>client.Forward().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Forward()
        {
            WithEndpoint(ApiEndpoint.Forward);
            return this;
        }

        /// <summary>
        /// Gets the current URL
        /// <example>client.GetCurrentUrl().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetCurrentUrl()
        {
            WithEndpoint(ApiEndpoint.GetCurrentUrl);
            return this;
        }

        /// <summary>
        /// Gets the current URL and the document identity in one call.<br/>
        /// A form posting to itself and a route rewritten in place both build a new document at the same
        /// address, which the url alone does not distinguish.
        /// <example>client.GetDocumentStamp().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetDocumentStamp()
        {
            WithEndpoint(ApiEndpoint.GetDocumentStamp);
            return this;
        }

        /// <summary>
        /// Gets the ready status
        /// <example>client.GetReadyStatus().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetReadyStatus()
        {
            WithEndpoint(ApiEndpoint.GetReadyStatus);
            return this;
        }

        /// <summary>
        /// Gets the HTTP status of the active tab's page, 0 when it has none (no response yet, or the navigation
        /// failed outright).
        /// <example>client.GetStatusCode().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns>Fluent interface to execute the request</returns>
        public IAllowRESTExecution GetStatusCode()
        {
            WithEndpoint(ApiEndpoint.GetStatusCode);
            return this;
        }

        /// <summary>
        /// Navigates to a URL
        /// <example>client.Goto("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns></returns>
        public IAllowRESTExecution GoTo(GPALUrl url)
        {
            if (true == string.IsNullOrEmpty(url))
                url = new GPALUrl("google.com");

            WithEndpoint(ApiEndpoint.GoTo).WithUrl(url.Url);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Refreshes the current page
        /// <example>client.Refresh().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Refresh()
        {
            WithEndpoint(ApiEndpoint.Refresh);
            return this;
        }
    }
}
