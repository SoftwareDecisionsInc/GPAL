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
        /// Clears the referrer
        /// <example>client.ClearReferrer().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution ClearReferrer()
        {
            WithEndpoint(ApiEndpoint.ClearReferrer);
            return this;
        }

        /// <summary>
        /// Sends an API request from inside the page, with the browser's cookies, TLS fingerprint, header order and
        /// anti-bot clearance. A relative url resolves against the current page.
        /// <example>client.Fetch("/api/search?page=1").Execute();</example>
        /// </summary>
        /// <param name="url">Url to request, relative or absolute</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution Fetch(string url)
        {
            WithEndpoint(ApiEndpoint.Fetch);
            _parameters = null == _parameters
                ? new { url }
                : MergeParameters(_parameters, new { url });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Gets the browser's preferred languages, most preferred first
        /// <example>client.GetLanguages().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetLanguages()
        {
            WithEndpoint(ApiEndpoint.GetLanguages);
            return this;
        }

        /// <summary>
        /// Starts or stops recording what the page asks for. Narrow it with
        /// <see cref="WithCallFilter"/> and start from nothing with <see cref="WithClearCalls"/>.
        /// <example>client.CaptureCalls(true).WithCallFilter("/api/search").Execute();</example>
        /// </summary>
        /// <param name="capture">True to record, false to stop</param>
        /// <returns>Fluent interface to narrow what is recorded, or to execute</returns>
        public IAllowRESTCaptureOptions CaptureCalls(bool capture = true)
        {
            WithEndpoint(ApiEndpoint.CaptureCalls);

            _parameters = null == _parameters
                ? new { capture }
                : MergeParameters(_parameters, new { capture });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Returns everything recorded so far, oldest first, as the extension's JSON.
        /// <example>string json = client.GetCapturedCalls().Execute();</example>
        /// </summary>
        /// <returns>Fluent interface to execute the request</returns>
        public IAllowRESTExecution GetCapturedCalls()
        {
            WithEndpoint(ApiEndpoint.GetCapturedCalls);
            return this;
        }

        /// <summary>
        /// Gets the user agent
        /// <example>client.GetUserAgent().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetUserAgent()
        {
            WithEndpoint(ApiEndpoint.GetUserAgent);
            return this;
        }

        /// <summary>
        /// Overrides the referrer URL
        /// <example>client.OverrideReferrr("https://google.com").Execute();</example>
        /// </summary>
        /// <param name="referrer">The referrer URL</param>
        /// <returns></returns>
        public IAllowRESTExecution OverrideReferrer(string referrer)
        {
            WithEndpoint(ApiEndpoint.OverrideReferrer).WithReferrer(referrer);
            return this;
        }

        /// <summary>
        /// Overrides the browser's user agent string
        /// <example>client.SetUserAgent("Mozilla/5.0 ...").Execute();</example>
        /// </summary>
        /// <param name="userAgent">The user agent string to use</param>
        /// <returns></returns>
        public IAllowRESTExecution SetUserAgent(string userAgent)
        {
            WithEndpoint(ApiEndpoint.SetUserAgent);
            _parameters = null == _parameters
                ? new { userAgent }
                : MergeParameters(_parameters, new { userAgent });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Overrides referrer stealthily - sets to google.com
        /// <example>client.SetGoogleReferrer().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution SetGoogleReferrer()
        {
            WithEndpoint(ApiEndpoint.SetGoogleReferrer);
            return this;
        }
    }
}
