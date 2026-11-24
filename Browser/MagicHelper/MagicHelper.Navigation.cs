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
        /// Navigates browser history back
        /// <example>browser.MagicHelper.Back();</example>
        /// </summary>
        public void Back()
        {
            Client.Back().Execute();
        }
        /// <summary>
        /// Reports at once whether the network has been idle: at most maxConnections requests in flight for the
        /// whole of the last pruneMs. Ask again to wait for it.
        /// <br/><br/>
        /// Each parameter is optional and defaults to the browser setting: WithNetworkIdleMaxConnections and
        /// WithNetworkIdlePruneMs. Called with no arguments it matches what GPAL checks after a navigation.
        /// <example>bool idle = browser.MagicHelper.CheckNetworkIdle(5);</example>
        /// </summary>
        /// <param name="maxConnections">Connections that still count as idle</param>
        /// <param name="pruneMs">How far back, in milliseconds, the in-flight count must have stayed at or below maxConnections</param>
        /// <returns>Whether network is idle</returns>
        public string CheckNetworkIdle(int? maxConnections = null, int? pruneMs = null)
        {
            BrowserSettings settings = ((Browser)browser)?.BrowserSettings;

            // CheckNetworkIdle(int) returns an interface that does not expose the other two parameters, so the
            // endpoint is set directly
            string retVal = Client
                .WithEndpoint(ApiEndpoint.CheckNetworkIdle)
                .WithMaxConnections(maxConnections ?? settings?.NetworkIdleMaxConnections ?? 0)
                .WithPruneMs(pruneMs ?? settings?.NetworkIdlePruneMs ?? 3_000)
                .Execute();

            return retVal;
        }

        /// <summary>
        /// Navigates browser history forward
        /// <example>browser.MagicHelper.Forward();</example>
        /// </summary>
        public void Forward()
        {
            Client.Forward().Execute();
        }

        /// <summary>
        /// Navigates to a URL
        /// <example>string result = browser.MagicHelper.Get("https://example.com");</example>
        /// </summary>
        /// <param name="URL">The URL to navigate to</param>
        /// <returns>The navigation result</returns>
        public string Get(GPALUrl URL)
        {
            bool areRobotsAllowed = true;

            URL.ForUrl(GetFullUrl(URL?.Url, browser, out areRobotsAllowed));

            if (null != browser)
                ((Browser)browser)._areRobotsAllowed = areRobotsAllowed;

            var result = Client.GoTo(URL?.Url).Execute();
            browser.ServerResponseCode = GetStatusCode();

            return result;
        }

        /// <summary>
        /// Gets the current URL
        /// <example>string url = browser.MagicHelper.GetCurrentUrl();</example>
        /// </summary>
        /// <returns>The current URL</returns>
        public string GetCurrentUrl()
        {
            return Client.GetCurrentUrl().Execute();
        }

        /// <summary>
        /// Gets the ready status
        /// With no page loaded there is no content script, so this returns null, which counts as complete.
        /// <example>string status = browser.MagicHelper.GetReadyStatus();</example>
        /// </summary>
        /// <returns>The ready status</returns>
        public string GetReadyStatus()
        {
            return GetReadyStatus(null);
        }

        /// <summary>
        /// The session token identifies the calling wait loop, so a status repeated during one wait is logged once.
        /// The public overload does not take it.
        /// </summary>
        /// <param name="sessionToken">Identifies the wait this reading belongs to</param>
        /// <returns>The ready status</returns>
        internal string GetReadyStatus(string sessionToken)
        {
            string retVal = null;

            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                lastSessionToken = sessionToken;
            }

            dynamic obj = Client.GetReadyStatus().Execute();

            // NOTOK, which OttoMagic answers while no content script runs (mid navigation), passes through as the
            // status, so the wait keeps polling until the new page loads
            if ("null" != obj)
                retVal = obj ?? @"""loading""";
            else
                retVal = "complete";

            // each status read once, as DEBUG; CheckDocumentReady publishes the result of the wait
            string currentErrorMessage = $"Document.Ready status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, currentErrorMessage, null, GPALObjectType.None);
            }

            return retVal;
        }

        /// <summary>
        /// Navigates to a URL
        /// <example>TabTuple tab = browser.MagicHelper.Goto("https://example.com");</example>
        /// </summary>
        /// <param name="URL">The URL to navigate to</param>
        /// <returns>The tab details</returns>
        public TabTuple GoTo(GPALUrl URL)
        {
            if (null == URL)
                URL = new GPALUrl("google.com");

            bool areRobotsAllowed = true;

            URL.ForUrl(GetFullUrl(URL.Url, browser, out areRobotsAllowed));

            if (null != browser)
                ((Browser)browser)._areRobotsAllowed = (bool) areRobotsAllowed;

            TabTuple retVal = Client.GoTo(URL.Url).Execute<TabTuple>();
            browser.ServerResponseCode = GetStatusCode();

            return retVal;
        }
        /// <summary>
        /// Refreshes the current page
        /// <example>browser.MagicHelper.Refresh();</example>
        /// </summary>
        public void Refresh()
        {
            Client.Refresh().Execute();
        }

        /// <summary>
        /// The HTTP status of the active tab's page, as the extension saw its response. 0 when the page has none: no
        /// response yet, or the navigation failed outright.
        /// <example>int status = browser.MagicHelper.GetStatusCode();</example>
        /// </summary>
        /// <returns>The page's HTTP status, or 0</returns>
        public int GetStatusCode()
        {
            int retVal = Client.GetStatusCode().Execute<int>();

            return retVal;
        }

        /// <summary>
        /// Returns timeOrigin and url, separated by a bar.<br/>
        /// Its own endpoint, because ExecuteJavaScript needs Allow user scripts on and this does not.
        /// <example>string stamp = browser.MagicHelper.GetDocumentStamp();</example>
        /// </summary>
        /// <returns>timeOrigin and url separated by a bar, or null when the page could not answer</returns>
        public string GetDocumentStamp()
        {
            string retVal = Client.GetDocumentStamp().Execute();

            // Strip the JSON quotes, or the closing quote stays on the url and every click looks like a navigation.
            if (true == retVal?.StartsWith("\"") && true == retVal.EndsWith("\"") && 1 < retVal.Length)
                retVal = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

            return retVal;
        }

        public static string GetFullUrl(string url, IBrowser browser, out bool areRobotsAllowed, bool noRobotsCheck = false)
        {
            List<string> robotsTxt = new List<string>();
            string normalized = url;
            areRobotsAllowed = true;

            try
            {
                // Default to HTTPS only if no scheme supplied
                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    normalized = "https://" + url;
                }

                // files the browser merely opens or downloads (sitemap.xml, pdfs, images...) are not pages, skip the checks
                if (false == noRobotsCheck && true == ((Browser)browser).BrowserSettings.ObeyRobotsTxt && false == UrlHelper.IsNonHtmlResource(normalized))
                    areRobotsAllowed = UrlHelper.CheckRobotsTxt(normalized, browser, robotsTxt);

                if (true == url.StartsWith("https://"))
                    return url;

                if (string.IsNullOrWhiteSpace(url) || UrlHelper.AreEquivalent(url, "https://google.com"))
                    return "https://google.com";

            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error getting full url", browser, GPALObjectType.Other, ex);
            }

            return normalized;
        }
    }
}
