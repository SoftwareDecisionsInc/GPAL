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
        /// Sends an API request from inside the page, with the browser's cookies, TLS fingerprint, header order and
        /// anti-bot clearance.
        /// </summary>
        /// <param name="url">Url to request. A relative one resolves against wherever the browser currently is</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution Fetch(string url)
        {
            WithEndpoint(ApiEndpoint.Fetch);
            SetParameter("url", url);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser's preferred languages, most preferred first.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetLanguages()
        {
            WithEndpoint(ApiEndpoint.GetLanguages);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser's user agent string.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetUserAgent()
        {
            WithEndpoint(ApiEndpoint.GetUserAgent);
            return this;
        }

        /// <summary>
        /// Configure this client to give the next navigation this referrer, sent with that navigation only.
        /// </summary>
        /// <param name="referrer">The referrer URL to send</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution OverrideReferrer(string referrer)
        {
            WithEndpoint(ApiEndpoint.OverrideReferrer);
            WithReferrer(referrer);
            return this;
        }

        /// <summary>
        /// Configure this client to override the browser's user agent string for subsequent requests.
        /// </summary>
        /// <param name="userAgent">The user agent string to use</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetUserAgent(string userAgent)
        {
            WithEndpoint(ApiEndpoint.SetUserAgent);
            SetParameter("userAgent", userAgent);
            return this;
        }


        /// <summary>
        /// Configure this client to give the next navigation https://www.google.com/ as its referrer, to mimic arriving from a search engine.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution SetGoogleReferrer()
        {
            WithEndpoint(ApiEndpoint.SetGoogleReferrer);
            return this;
        }

        /// <summary>
        /// Configure this client to drop a referrer set for the next navigation.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ClearReferrer()
        {
            WithEndpoint(ApiEndpoint.ClearReferrer);
            return this;
        }
    }
}
