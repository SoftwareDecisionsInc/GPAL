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
        /// Overrides the referrer URL
        /// <example>browser.MagicHelper.OverrideReferrer("https://google.com");</example>
        /// </summary>
        /// <param name="referrer">The referrer URL</param>
        public void OverrideReferrer(string referrer)
        {
            Client.OverrideReferrer(referrer).Execute();
        }

        /// <summary>
        /// Overrides the browser's user agent string
        /// <example>browser.MagicHelper.SetUserAgent("Mozilla/5.0 ...");</example>
        /// </summary>
        /// <param name="userAgent">The user agent string to use</param>
        public void SetUserAgent(string userAgent)
        {
            Client.SetUserAgent(userAgent).Execute();
        }

        /// <summary>
        /// Overrides referrer stealthily
        /// <example>browser.MagicHelper.SetGoogleReferrer();</example>
        /// </summary>
        public void SetGoogleReferrer()
        {
            Client.SetGoogleReferrer().Execute();
        }

        /// <summary>
        /// Sends an HTTP request from inside the page, with the browser's cookies, TLS fingerprint, header order
        /// and anti-bot clearance. A relative url resolves against the current page.
        /// <example>string json = browser.MagicHelper.Fetch("/api/search?page=1");</example>
        /// </summary>
        /// <param name="url">Url to request, relative or absolute</param>
        /// <param name="method">HTTP verb, GET when null</param>
        /// <param name="body">Request body, or null for none</param>
        /// <param name="contentType">Content type of the body</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="asBytes">True to bring the response body back base64 encoded, false for text</param>
        /// <returns>The response body, or null if the fetch could not be made</returns>
        public string Fetch(string url, string method = null, string body = null, string contentType = null, string[] headers = null, bool asBytes = false)
        {
            string retVal = Client
                .Fetch(url)
                .WithVerb(method)
                .WithBody(body)
                .WithContentType(contentType)
                .WithHeaders(headers)
                .WithBytes(asBytes)
                .Execute();

            // NOTOK means the request failed; return null, as the other engines do. The body comes back JSON encoded,
            // so NOTOK arrives in quotes.
            string answer = retVal?.Trim('"');
            return "NOTOK" == answer || "null" == answer ? null : retVal;
        }
        /// <summary>
        /// Clears the referrer header, and the browser's referrer, so later GoTo and Get calls send none
        /// <example>browser.MagicHelper.ClearReferrer();</example>
        /// </summary>
        public void ClearReferrer()
        {
            ((Browser)browser).BrowserSettings.Referrer = null;
            Client.ClearReferrer().Execute();
        }

        /// <summary>
        /// Gets the user agent
        /// <example>string userAgent = browser.MagicHelper.GetUserAgent();</example>
        /// </summary>
        /// <returns>The user agent string</returns>
        public string GetUserAgent()
        {
            return Client.GetUserAgent().Execute();
        }

        /// <summary>
        /// Gets the browser's preferred languages, most preferred first
        /// <example>string languages = browser.MagicHelper.GetLanguages();</example>
        /// </summary>
        /// <returns>The languages, comma separated</returns>
        public string GetLanguages()
        {
            return Client.GetLanguages().Execute();
        }

        /// <summary>
        /// Starts or stops recording network requests. The extension uses webRequest, so it records every request,
        /// not only page script's, across navigations until stopped.
        /// <example>browser.MagicHelper.CaptureCalls(true, "/api/search");</example>
        /// </summary>
        /// <param name="capture">True to record, false to stop</param>
        /// <param name="urlFragment">Record only urls holding this, or null for everything</param>
        /// <param name="clear">True to forget what was already recorded before starting</param>
        public void CaptureCalls(bool capture = true, string urlFragment = null, bool clear = false)
        {
            IAllowRESTCaptureOptions capturing = Client.CaptureCalls(capture);

            if (false == string.IsNullOrWhiteSpace(urlFragment)) capturing.WithCallFilter(urlFragment);
            if (true == clear) capturing.WithClearCalls(true);

            capturing.Execute();
        }

        /// <summary>
        /// Everything recorded so far, oldest first.
        /// <example>string json = browser.MagicHelper.GetCapturedCalls();</example>
        /// </summary>
        /// <returns>The recorded calls as json, holding capturing, filter, dropped and calls</returns>
        public string GetCapturedCalls()
        {
            return Client.GetCapturedCalls().Execute();
        }
    }
}
