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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;
using System.Net.NetworkInformation;
using System.Net;
using System.Text.RegularExpressions;

namespace GenerallyPositive.Browser
{
    public partial class BrowserHelper
    {
        /// <summary>
        /// Sends the browser's credentials once, before the first navigation.<br/>
        /// Basic, Proxy, Bearer and API key are set as session-wide extra headers on Chrome and Edge with Selenium
        /// or Puppeteer. Digest, Firefox and OttoMagic use <see cref="CredentialsInUrl"/>: one navigation with the
        /// credentials in the url, and the browser answers the challenge. Form does nothing here.<br/>
        /// Credentials are never published.
        /// </summary>
        /// <param name="browser">Browser holding the credentials</param>
        /// <param name="url">Where the workflow is about to go, whose origin the credentials are for</param>
        internal static void PresentCredentials(Browser browser, GPALUrl url)
        {
            Credentials credential = (Credentials)browser.BrowserSettings.Credentials;
            WebAuthType authType = credential.WebAuthType;
            string header = AuthorizationHeader(credential, authType);

            browser.BrowserSettings.CredentialsPresented = DateTime.UtcNow;

            // Form and None send nothing; the workflow handles a login form with selectors.
            if (WebAuthType.Form == authType || WebAuthType.None == authType)
                return;

            if (true == string.IsNullOrEmpty(header) || BrowserType.FireFox == browser.BrowserSettings.BrowserType || true == browser.UseOttoMagic)
                CredentialsInUrl(browser, url, credential);
            else
            {
                string name = AuthorizationHeaderName(authType);
                Dictionary<string, object> headers = new Dictionary<string, object> { { name, header } };

                if (true == browser.UsePuppeteer)
                    browser.PuppeteerCommunicator.SetExtraHttpHeaders(headers).GetAwaiter().GetResult();
                else
                    BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Network.setExtraHTTPHeaders",
                        new Dictionary<string, object> { { "headers", headers } });

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Credentials presented as [{authType}] for [{url?.Url}]", browser, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Builds the Authorization value for the auth types that can be settled before the first request is made.
        /// </summary>
        /// <param name="credential">Credentials to present</param>
        /// <param name="authType">How the site expects to be told</param>
        /// <returns>The header value, or null for an auth type no header can satisfy</returns>
        internal static string AuthorizationHeader(Credentials credential, WebAuthType authType)
        {
            string value = null;

            if (WebAuthType.Basic == authType || WebAuthType.Proxy == authType)
                value = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credential.Username}:{credential.Password}"));
            else if (WebAuthType.Bearer == authType)
                value = "Bearer " + credential.AccessToken;
            else if (WebAuthType.XAuthToken == authType || WebAuthType.XApiKey == authType || WebAuthType.ApiKey == authType)
                value = credential.AccessToken;      // their own header, so no scheme word in front of it

            return value;
        }

        /// <summary>
        /// The header an auth type is presented in.
        /// </summary>
        /// <param name="authType">How the site expects to be told</param>
        /// <returns>The header name</returns>
        internal static string AuthorizationHeaderName(WebAuthType authType)
        {
            string name = "Authorization";

            if (WebAuthType.Proxy == authType)
                name = "Proxy-Authorization";
            else if (WebAuthType.XAuthToken == authType)
                name = "X-Auth-Token";
            else if (WebAuthType.XApiKey == authType)
                name = "X-API-Key";
            else if (WebAuthType.ApiKey == authType)
                name = "api-key";

            return name;
        }
        /// <summary>
        /// Navigates once to the url with the credentials in it, so the browser answers the challenge and caches it
        /// per origin. The workflow's navigation then uses the plain url.
        /// </summary>
        /// <param name="browser">Browser to navigate</param>
        /// <param name="url">The url the workflow is about to visit, whose origin the credentials are for</param>
        /// <param name="credential">Credentials to carry</param>
        static void CredentialsInUrl(Browser browser, GPALUrl url, Credentials credential)
        {
            UriBuilder builder = new UriBuilder(url?.Url)
            {
                UserName = Uri.EscapeDataString(credential.Username ?? string.Empty),
                Password = Uri.EscapeDataString(credential.Password ?? string.Empty)
            };
            GPALUrl credentialed = new GPALUrl(builder.Uri.ToString());

            // Call the engines directly; .GoTo would publish the url, which holds the password.
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.GoTo(credentialed);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.GoTo(credentialed).Execute();
            else
                SeleniumGoToUrl(credentialed, browser.BrowserSettings);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Credentials presented to [{builder.Host}], answering its challenge", browser, GPALObjectType.Browser);
        }

        /// <summary>
        /// Sends a GPALRequest with fetch from inside the page, with the browser's cookies, TLS fingerprint, header
        /// order and anti-bot clearance. A relative path resolves against the current page.
        /// </summary>
        /// <summary>
        /// Runs the request once per row of the unit of work's token source, walking .WithPages pages within each
        /// row, and calls the request's CallAfterFetch handler with that row's pages as soon as they are in.<br/>
        /// With no token source this is one pass of the request exactly as it was written.
        /// </summary>
        /// <param name="browser">Browser whose page issues the requests</param>
        /// <param name="request">The request to issue</param>
        internal static void FetchWithTokens(Browser browser, GPALRequest request)
        {
            IGPALGrid<string> tokens = browser.CurrentUOW.FetchTokens;
            int required = request.TokenCount();
            string name = request.Name ?? request.Path;

            if (null == tokens && 0 < required)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Fetch of [{name}] uses [{required}] tokens but no token source was given. Use .WithTokensFrom before .Fetch.", browser, GPALObjectType.Browser);
            else
            {
                // no token source is still one pass, the request exactly as it was written
                int rowCount = tokens?.Rows ?? 1;

                for (int tokenIdx = 0; tokenIdx < rowCount; tokenIdx++)
                {
                    List<string> row = null == tokens ? null : tokens[tokenIdx];

                    // Skip a row with fewer values than the request has tokens, or an unreplaced token goes out in
                    // the body.
                    if (null != row && row.Count < required)
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Fetch of [{name}] needs [{required}] tokens but row [{tokenIdx + 1}] has [{row.Count}]. Skipping the row.", browser, GPALObjectType.Browser);
                    else
                        FetchRow(browser, request, row, tokenIdx);
                }
            }
        }
        /// <summary>
        /// Fetches every page for one row of tokens, appending each body to the unit of work so .SaveTo still hands
        /// back the whole run, then hands just this row's bodies to the request's CallAfterFetch handler.
        /// </summary>
        /// <param name="browser">Browser whose page issues the requests</param>
        /// <param name="request">The request to issue</param>
        /// <param name="row">Values for the request's numbered tokens, or null when there is no token source</param>
        /// <param name="tokenIdx">The current token row index</param>
        static void FetchRow(Browser browser, GPALRequest request, List<string> row, int tokenIdx)
        {
            int mark = browser.CurrentUOW.FetchResults.Count;
            string name = request.Name ?? request.Path;

            for (int page = 0; page < browser.CurrentUOW.PageCount; page++)
            {
                string where = null == row ? $"Page [{page + 1}]" : $"Row [{tokenIdx + 1}] page [{page + 1}]";

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Fetching [{name}]. {where}.", browser, GPALObjectType.Browser);

                string body = FetchInPage(browser, request, page, row);

                // A failed page does not stop the run. Its error goes into FetchResults for .SaveTo and
                // CallAfterFetch. Every page adds one entry, so position maps to row and page.
                if (null == body)
                {
                    string why = $"Fetch of [{name}] returned nothing on {where.ToLower()}.";

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, why, browser, GPALObjectType.Browser);
                    body = why;
                }
                else if (200 > browser.ServerResponseCode || 300 <= browser.ServerResponseCode)
                {
                    // On a non-2xx status keep the body, which usually explains the error, prefixed with the status
                    // message.
                    string why = $"Fetch of [{name}] returned HTTP [{browser.ServerResponseCode}] on {where.ToLower()}.";

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, why, browser, GPALObjectType.Browser);
                    body = why + Environment.NewLine + body;
                }

                browser.CurrentUOW.FetchResults.Add(body);
            }

            if (null != request.AfterFetch)
            {
                UnitOfWork safeUOW = browser.CurrentUOW;
                List<string> results = browser.CurrentUOW.FetchResults.GetRange(mark, browser.CurrentUOW.FetchResults.Count - mark);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallAfterFetch [{request.AfterFetch.Method.Name}]", browser, GPALObjectType.Browser);

                CallIfStatus handled = request.AfterFetch(browser, results, browser.CurrentUOW.FetchTokens, tokenIdx);

                // the handler can and probably will start its own unit of work, so restore ours
                browser.CurrentUOW = safeUOW;

                if (CallIfStatus.Terminate == handled)
                {
                    string str = $"CallAfterFetch handler [{request.AfterFetch.Method.Name}] requested program termination.";
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                    throw new GPALException($"{GPAL.MyMethodName()}: " + str);
                }
            }
        }
        /// <param name="browser">Browser whose page issues the request</param>
        /// <param name="request">The request to issue</param>
        /// <param name="page">Zero based page number, added to the request's page parameter</param>
        /// <param name="row">Values for the request's numbered tokens, or null when there is no token source</param>
        /// <returns>The response body, or null if the fetch could not be made</returns>
        internal static string FetchInPage(Browser browser, GPALRequest request, int page, List<string> row)
        {
            // Resolve the url, body and headers for this page and token row here; the engines only see a finished
            // url and plain values.
            string envelope = FetchEnvelope(browser, request.ResolveUrl(page, row), request.Method, request.ResolveBody(page, row), request.ContentType, request.HeaderPairs(page, row), false);

            return UnpackFetchResponse(browser, envelope);
        }
        /// <summary>
        /// Runs the fetch on the current engine and returns the page's envelope unchanged. The one place that knows
        /// how each engine is called.
        /// </summary>
        /// <param name="browser">Browser whose page issues the request</param>
        /// <param name="url">Url to request, relative to wherever the browser is or absolute</param>
        /// <param name="method">HTTP method, GET when null</param>
        /// <param name="body">Request body, or null for none</param>
        /// <param name="contentType">Content type of the body</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="asBytes">True to bring the body back base64 encoded, which is what a file needs</param>
        /// <returns>The envelope the page built, or null if the fetch could not be made</returns>
        static string FetchEnvelope(Browser browser, string url, string method, string body, string contentType, string[] headers, bool asBytes)
        {
            string envelope = null;

            if (true == browser.UseSelenium)
            {
                // Selenium throws when the page navigates away before the fetch answers ("Document was unloaded"),
                // where the other engines hand back a failure. Build the same failure envelope the page would.
                try
                {
                    envelope = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteAsyncScript(
                        FetchSetupScript(url, method, body, contentType, headers, asBytes) + @"
                        var done = arguments[arguments.length - 1];

                        fetch(target.toString(), options)
                            .then(gpalEnvelope)
                            .then(done)
                            .catch(function (error) { done(gpalFailure(error)); });
                    ") as string;
                }
                catch (JavaScriptException ex)
                {
                    envelope = Newtonsoft.Json.JsonConvert.SerializeObject(new { status = 0, body = (string)null, encoding = (string)null, lastModified = (string)null, error = $"{ex.Message} requesting {url}" });
                }
            }
            else if (true == browser.UsePuppeteer)
                envelope = browser.PuppeteerClient
                    .Fetch(url)
                    .WithVerb(method)
                    .WithBody(body)
                    .WithContentType(contentType)
                    .WithHeaders(headers)
                    .WithBytes(asBytes)
                    .Execute();
            else if (true == browser.UseOttoMagic)
                envelope = browser.MagicHelper.Fetch(url, method, body, contentType, headers, asBytes);
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Fetch is not implemented for [{browser.BrowserSettings.AutomationEngine}] yet.", browser, GPALObjectType.Browser);

            return envelope;
        }
        /// <summary>
        /// Reads the user agent from the running browser, not the launch settings.<br/>
        /// Some sites bind the session to it: Sinatra's Rack::Protection::SessionHijacking rejects the cookie when a
        /// later request sends a different user agent.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <returns>The user agent, or null when the engine could not answer</returns>
        internal static string LiveUserAgent(Browser browser)
        {
            string retVal = null;

            if (true == browser.UseSelenium)
                retVal = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteScript("return navigator.userAgent") as string;
            else if (true == browser.UsePuppeteer)
                retVal = browser.PuppeteerClient.GetUserAgent().Execute();
            else if (true == browser.UseOttoMagic)
                retVal = Unquoted(browser.MagicHelper.GetUserAgent());

            return retVal;
        }

        /// <summary>
        /// Removes the JSON quotes from a string returned through native messaging. A site that hashes the header
        /// needs exactly what the browser sent.
        /// </summary>
        /// <param name="value">What the endpoint answered</param>
        /// <returns>The value as the browser has it</returns>
        static string Unquoted(string value)
        {
            string retVal = value;

            while (false == string.IsNullOrEmpty(retVal) && true == retVal.StartsWith("\"") && true == retVal.EndsWith("\"") && 1 < retVal.Length)
            {
                string unwrapped = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

                if (unwrapped == retVal)
                    break;

                retVal = unwrapped;
            }

            return retVal;
        }
        /// <summary>
        /// Builds the Accept-Language header the browser sends, from navigator.languages.<br/>
        /// A site can bind the session to it like the user agent. Browsers expose the list, not the header, so
        /// <see cref="AcceptLanguageFrom"/> applies each browser's weighting.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <returns>The header value, or null when the engine could not answer</returns>
        internal static string LiveAcceptLanguage(Browser browser)
        {
            string languages = null;
            string retVal = null;

            if (true == browser.UseSelenium)
                languages = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteScript("return navigator.languages.join(',')") as string;
            else if (true == browser.UsePuppeteer)
                languages = browser.PuppeteerClient.GetLanguages().Execute();
            else if (true == browser.UseOttoMagic)
                languages = Unquoted(browser.MagicHelper.GetLanguages());

            if (false == string.IsNullOrWhiteSpace(languages))
                retVal = AcceptLanguageFrom(languages.Split(','), browser.BrowserSettings.BrowserType);

            return retVal;
        }

        /// <summary>
        /// Builds Accept-Language from a language list as the browser does: the first has no q and each next drops
        /// 0.1, giving en-US,en;q=0.9. Firefox lowercases the tags: en-us,en;q=0.9.<br/>
        /// Both copied from real traffic. A site hashing this header rejects a wrongly cased tag like a missing one.
        /// </summary>
        /// <param name="languages">The languages, most preferred first</param>
        /// <param name="browserType">Which browser's spelling to apply</param>
        /// <returns>The header value</returns>
        static string AcceptLanguageFrom(string[] languages, Enums.BrowserType browserType)
        {
            List<string> weighted = new List<string>();

            for (int index = 0; index < languages.Length; index++)
            {
                string language = languages[index].Trim();

                if (true == string.IsNullOrEmpty(language))
                    continue;

                if (Enums.BrowserType.FireFox == browserType)
                    language = language.ToLowerInvariant();

                if (0 == index)
                    weighted.Add(language);
                else
                {
                    // One decimal place. Drop a language whose weight reaches 0; q=0 tells the server never to use it.
                    double quality = Math.Round(1.0 - (0.1 * index), 1);

                    if (0 < quality)
                        weighted.Add($"{language};q={quality.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }

            return string.Join(",", weighted);
        }
        /// <summary>
        /// Fetches a url from inside the page and returns its bytes, with the browser's cookies, TLS fingerprint and
        /// clearance. Nothing navigates, so no tab or download prompt opens and the page stays put.<br/>
        /// The status goes in ServerResponseCode like a navigation's, so an If-Modified-Since caller can check for
        /// 304.
        /// </summary>
        /// <param name="browser">Browser whose page issues the request</param>
        /// <param name="url">Url of the file to fetch</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="lastModified">Answers the response's Last-Modified header, null when the server sent none</param>
        /// <returns>The file's bytes, or null when the fetch could not be made or the response carried no body</returns>
        internal static byte[] FetchBytes(Browser browser, string url, string[] headers, out string lastModified)
        {
            byte[] bytes = null;
            // Only GPAL calls this and every caller has a fallback, so failures are DEBUG, not ERROR.
            JObject response = UnpackFetchEnvelope(browser, FetchEnvelope(browser, url, "GET", null, null, headers, true), false);
            string encoded = response?["body"]?.ToString();

            lastModified = response?["lastModified"]?.ToString();

            if (false == string.IsNullOrEmpty(encoded))
                bytes = Convert.FromBase64String(encoded);

            return bytes;
        }
        /// <summary>
        /// Stores the envelope's status in ServerResponseCode, as a navigation does, and returns the body.
        /// </summary>
        /// <param name="browser">Browser that issued the request, whose ServerResponseCode is set</param>
        /// <param name="envelope">What the engine returned, or null when the fetch could not be made</param>
        /// <returns>The response body, or null if the fetch could not be made</returns>
        static string UnpackFetchResponse(Browser browser, string envelope)
        {
            return UnpackFetchEnvelope(browser, envelope, true)?["body"]?.ToString();
        }
        /// <summary>
        /// Parses what the page sent back and records the status on the browser the same way a navigation records
        /// one. Every engine answers with the same envelope, so this is the only place that knows its shape.
        /// </summary>
        /// <param name="browser">Browser that issued the request, whose ServerResponseCode is set</param>
        /// <param name="envelope">What the engine returned, or null when the fetch could not be made</param>
        /// <param name="requested">True when the workflow asked for this fetch. GPAL's own fetches have a fallback
        /// behind them, so their failures are published as DEBUG rather than ERROR</param>
        /// <returns>The parsed envelope, or null when nothing came back or its shape was not understood</returns>
        static JObject UnpackFetchEnvelope(Browser browser, string envelope, bool requested)
        {
            JObject response = null;
            GPALEventType failure = true == requested ? GPALEventType.ERROR : GPALEventType.DEBUG;

            // Reset ServerResponseCode to 0 so a failed fetch does not leave the previous 200 or 304.
            browser.ServerResponseCode = 0;

            if (true == string.IsNullOrEmpty(envelope))
                GPAL.PublishSimpleEvent(failure, $"[{browser.BrowserSettings.AutomationEngine}] returned nothing for the fetch, so the page never answered", browser, GPALObjectType.Browser);
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"Fetch envelope [{envelope}]", browser, GPALObjectType.Browser);

                // Check the envelope is a JSON object before parsing; if not, publish what arrived.
                if (false == envelope.TrimStart().StartsWith("{"))
                    GPAL.PublishSimpleEvent(failure, $"Fetch response was not understood. Received [{envelope}]", browser, GPALObjectType.Browser);
                else
                {
                    response = JObject.Parse(envelope);
                    browser.ServerResponseCode = response["status"]?.Value<int>() ?? 0;

                    // An error field means the page could not make the request; publish the reason so a refusal is
                    // distinguishable from a bug.
                    if (false == string.IsNullOrEmpty(response["error"]?.ToString()))
                        GPAL.PublishSimpleEvent(failure, $"The page could not make the request [{response["error"]}]", browser, GPALObjectType.Browser);
                }
            }

            return response;
        }
        /// <summary>
        /// Builds the JavaScript that turns the request into fetch's url and options. Each engine adds its own
        /// promise wait.<br/>
        /// Values are inserted as JSON literals so quotes or braces cannot break the script.
        /// </summary>
        /// <param name="url">Url to request, relative or absolute</param>
        /// <param name="method">HTTP method, GET when null</param>
        /// <param name="body">Request body, or null for none</param>
        /// <param name="contentType">Content type of the body</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="asBytes">True to bring the body back base64 encoded, which is what a file needs</param>
        /// <returns>Javascript leaving "target", "options" and "gpalEnvelope" in scope</returns>
        internal static string FetchSetupScript(string url, string method, string body, string contentType, string[] headers, bool asBytes)
        {
            return $@"
                var target = new URL({Newtonsoft.Json.JsonConvert.SerializeObject(url)}, location.href);
                var reqBody = {Newtonsoft.Json.JsonConvert.SerializeObject(body)};
                var contentType = {Newtonsoft.Json.JsonConvert.SerializeObject(contentType)};
                var headerPairs = {Newtonsoft.Json.JsonConvert.SerializeObject(headers ?? new string[0])};
                var asBytes = {(true == asBytes ? "true" : "false")};

                var options = {{ method: {Newtonsoft.Json.JsonConvert.SerializeObject(method ?? "GET")}, credentials: 'include', headers: {{}} }};
                for (var h = 0; h < headerPairs.length; h += 2)
                    options.headers[headerPairs[h]] = headerPairs[h + 1];

                if (reqBody) {{
                    options.body = reqBody;
                    if (!options.headers['Content-Type'])
                        options.headers['Content-Type'] = contentType;
                }}

                // One JSON envelope for every engine. With asBytes the body is read as an arraybuffer and base64
                // encoded, since text() mangles non-utf-8 bytes. Promises, not await, so it serves both Selenium's
                // callback and Puppeteer's awaited expression. String.fromCharCode runs over 32 KB slices; one call
                // with hundreds of thousands of arguments overflows the stack.
                function gpalEnvelope(response) {{
                    return (asBytes
                        ? response.arrayBuffer().then(function (buffer) {{
                            var bytes = new Uint8Array(buffer), binary = '', slice = 0x8000;
                            for (var i = 0; i < bytes.length; i += slice)
                                binary += String.fromCharCode.apply(null, bytes.subarray(i, i + slice));
                            return btoa(binary);
                        }})
                        : response.text()
                    ).then(function (payload) {{
                        return JSON.stringify({{
                            status: response.status,
                            body: payload,
                            encoding: asBytes ? 'base64' : null,
                            lastModified: response.headers.get('Last-Modified')
                        }});
                    }});
                }}

                // gpalFailure returns the envelope with status 0 and the error, so C# can tell a refusal from a bug.
                function gpalFailure(error) {{
                    return JSON.stringify({{
                        status: 0,
                        body: null,
                        encoding: null,
                        lastModified: null,
                        error: String((error && error.message) || error) + ' requesting ' + target.toString()
                    }});
                }}
            ";
        }

        /// <summary>
        /// True for a header the browser sets itself, which an in-page fetch cannot set.
        /// </summary>
        /// <param name="name">Header name</param>
        /// <returns>True when the browser owns it</returns>
        internal static bool BrowserOwnedHeader(string name)
        {
            return Array.Exists(browserOwnedHeaders, owned => owned.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // WaitForNetworkIdle through Selenium's network events, commented out: it needs a newer Selenium.WebDriver
        // network API
        //public static string WaitForNetworkIdle(Browser browser)
        //{
        //    var task = WaitForNetworkIdleAsyncInternal(browser);
        //    return task.GetAwaiter().GetResult();
        //}
        //public static async Task<string> WaitForNetworkIdleAsyncInternal(Browser browser)
        //{ 
        //    int timeoutMs = browser.BrowserSettings.NetworkIdleTimeoutMs;
        //    int maxConnections = browser.BrowserSettings.NetworkIdleMaxConnections;
        //    int pruneMs = browser.BrowserSettings.NetworkIdlePruneMs;
        //    IWebDriver driver = browser.BrowserDriver;


        //    if (!(driver is IDevTools devToolsDriver))
        //    {
        //        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "This driver does not support check network idle.", browser, GPALObjectType.Browser);
        //        return "true";
        //    }

        //    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting up to [{timeoutMs}] ms for network to idle to [{maxConnections}] connections for [{pruneMs}] ms", browser, GPALObjectType.Browser);

        //    var network = driver.Manage().Network;
        //    var requests = new ConcurrentBag<string>();
        //    bool isIdle = false;

        //    var activeRequests = new ConcurrentDictionary<string, byte>();

        //    // Handlers
        //    EventHandler<NetworkRequestSentEventArgs> requestSentHandler = (sender, e) =>
        //    {
        //        activeRequests[e.RequestId] = 0;
        //    };

        //    EventHandler<NetworkResponseReceivedEventArgs> responseReceivedHandler = (sender, e) =>
        //    {
        //        activeRequests.TryRemove(e.RequestId, out _);
        //    };

        //    // Attach
        //    network.NetworkRequestSent += requestSentHandler;
        //    network.NetworkResponseReceived += responseReceivedHandler;

        //    try
        //    {
        //        await network.StartMonitoring();

        //        var overallTimeout = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        //        DateTime? idleSince = null;

        //        while (DateTime.UtcNow < overallTimeout)
        //        {
        //            int openConnections = activeRequests.Count;

        //            if (openConnections <= maxConnections)
        //            {
        //                idleSince ??= DateTime.UtcNow; // mark when idle started
        //                if ((DateTime.UtcNow - idleSince.Value).TotalMilliseconds >= pruneMs)
        //                {
        //                    isIdle = true;
        //                    break;
        //                }
        //            }
        //            else
        //            {
        //                idleSince = null; // reset idle timer if activity resumed
        //            }

        //            await Task.Delay(250); // small polling interval
        //        }

        //        if (!isIdle)
        //            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Network idle timeout reached.", browser, GPALObjectType.Browser);
        //    }
        //    finally
        //    {
        //        network.NetworkRequestSent -= requestSentHandler;
        //        network.NetworkResponseReceived -= responseReceivedHandler;
        //        await network.StopMonitoring();
        //    }

        //    return isIdle.ToString();
        //}
        /// <summary>
        /// Returns all active TCP listener endpoints on this machine.
        /// </summary>
        /// <returns>Array of IPEndPoint for every active TCP listener</returns>
        public static IPEndPoint[] GetAllTcpListeners()
        {
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            return ipGlobalProperties.GetActiveTcpListeners();
        }
        /// <summary>
        /// Checks whether anything is currently listening on the given TCP port.
        /// </summary>
        /// <param name="port">Port number to check</param>
        /// <returns>True if a TCP listener is active on the port, false otherwise</returns>
        /// <example>
        /// <code>
        /// if (BrowserHelper.IsPortListening(9222))
        ///     GPAL.PublishSimpleEvent(GPALEventType.INFO, "CDP debug port already in use.");
        /// </code>
        /// </example>
        public static bool IsPortListening(int port)
        {
            IPEndPoint[] tcpListeners = GetAllTcpListeners();
            return tcpListeners.Any(ep => ep.Port == port);
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;     // network byte order in the low 16 bits
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        /// <summary>
        /// The process listening on a local TCP port, from the Windows TCP table. A browser's debug port is opened by
        /// the browser itself, so for a browser GPAL connected to rather than launched, this is its process.
        /// </summary>
        /// <param name="port">The port the process listens on</param>
        /// <returns>The listening process, or null when nothing listens on the port</returns>
        internal static Process PortOwnerProcess(int port)
        {
            Process retVal = null;
            int pid = 0;
            int size = 0;

            // The first call answers the size the table needs.
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);

            IntPtr table = Marshal.AllocHGlobal(size);

            try
            {
                if (0 == GetExtendedTcpTable(table, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0))
                {
                    int rows = Marshal.ReadInt32(table);
                    int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                    for (int i = 0; i < rows && 0 == pid; i++)
                    {
                        MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(table + 4 + i * rowSize, typeof(MIB_TCPROW_OWNER_PID));
                        int rowPort = (int)(((row.localPort & 0xFF) << 8) | ((row.localPort >> 8) & 0xFF));

                        if (port == rowPort)
                            pid = (int)row.owningPid;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(table);
            }

            if (0 < pid)
                retVal = Process.GetProcesses().FirstOrDefault(p => p.Id == pid);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Port [{port}] is held by process [{pid}], {(null != retVal ? "found" : "not running")}", null, GPALObjectType.Browser);

            return retVal;
        }
    }
}
