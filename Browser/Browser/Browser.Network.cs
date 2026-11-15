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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// <summary>
        /// Returns a RESTClient with this browser's session: origin, cookies, user agent, Accept-Language and any
        /// .WithCredentials credential.<br/>
        /// It works after the browser closes. It does not share the browser's TLS, so a site that fingerprints TLS
        /// needs .Fetch.
        /// </summary>
        /// <param name="client">Receives the client, ready to be given an endpoint</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ContinueAsRESTClient(out IRESTClient client)
        {
            // API base is the browser's current origin.
            string origin = UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(BrowserSettings));
            RESTClient retVal = (RESTClient)GPAL.RESTClient.WithAPIBase(origin);

            // No page loaded means no origin or cookies; warn now rather than fail at the first request.
            if (true == string.IsNullOrEmpty(origin))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No page loaded, so there is no session to carry. Navigate first, then continue as a RESTClient", this, GPALObjectType.Browser);

            // Read this origin's cookies on a new GPALUrl, leaving the workflow's own storage actions alone.
            GPALUrl cookieUrl = new GPALUrl(origin);

            cookieUrl.Add(new StorageAction { StorageType = WebsiteStorageType.cookie, Action = WebsiteStorageAction.get });

            UrlHelper.TryPerformAction(cookieUrl, BrowserSettings,
                new StorageAction { StorageType = WebsiteStorageType.cookie, Action = WebsiteStorageAction.get },
                out string cookieJson);

            string cookieHeader = CookieHeaderFrom(cookieJson, origin, out int cookiesRead);

            if (false == string.IsNullOrEmpty(cookieHeader))
                retVal.WithHeader("Cookie", cookieHeader);

            // The user agent and Accept-Language actually sent, not the configured ones; some sites bind the session
            // to the user agent.
            string sentUserAgent = BrowserHelper.LiveUserAgent(this);
            string sentAcceptLanguage = BrowserHelper.LiveAcceptLanguage(this);

            retVal.WithHeader("User-Agent", sentUserAgent ?? BrowserHelper.ResolveUserAgent(BrowserSettings));

            if (false == string.IsNullOrEmpty(sentAcceptLanguage))
                retVal.WithHeader("Accept-Language", sentAcceptLanguage);

            // Copy the WithCredentials credential as an Authorization header. Basic auth is never a cookie, so
            // cookies alone get a 401.
            ICredentials browserCredential = BrowserSettings.Credentials;

            if (null != browserCredential)
            {
                Credentials credential = (Credentials)browserCredential;
                string authValue = BrowserHelper.AuthorizationHeader(credential, credential.WebAuthType);

                if (false == string.IsNullOrEmpty(authValue))
                    retVal.WithHeader(BrowserHelper.AuthorizationHeaderName(credential.WebAuthType), authValue);

            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Continuing as a RESTClient from [{BrowserSettings.BrowserType}], carrying its [{(null == sentUserAgent ? "configured" : "live")}] user agent, [{(string.IsNullOrEmpty(sentAcceptLanguage) ? "no accept language" : sentAcceptLanguage)}], [{cookieHeader?.Split(';').Length ?? 0}] of [{cookiesRead}] cookies for [{origin}] and [{(null == browserCredential ? "no credential" : ((Credentials)browserCredential).WebAuthType.ToString())}]", this, GPALObjectType.Browser);

            client = retVal;

            return this;
        }

        /// <summary>
        /// Builds a Cookie header from a cookie get's json, keeping only the given origin's cookies.
        /// </summary>
        /// <param name="cookieJson">What the cookie get returned</param>
        /// <param name="origin">The origin the header is being built for</param>
        /// <param name="cookiesRead">How many cookies the engine answered with, before narrowing</param>
        /// <returns>name=value pairs joined with "; ", or null when there were none</returns>
        private static string CookieHeaderFrom(string cookieJson, string origin, out int cookiesRead)
        {
            string retVal = null;

            cookiesRead = 0;

            try
            {
                if (false == string.IsNullOrEmpty(cookieJson))
                {
                    string host = true == Uri.TryCreate(origin, UriKind.Absolute, out Uri originUri) ? originUri.Host : null;
                    Newtonsoft.Json.Linq.JArray cookies = Newtonsoft.Json.Linq.JArray.Parse(cookieJson);
                    List<string> pairs = new List<string>();

                    cookiesRead = cookies.Count;

                    foreach (Newtonsoft.Json.Linq.JToken cookie in cookies)
                    {
                        // puppeteer calls the name Key, the other two call it name
                        string name = (string)(cookie["name"] ?? cookie["Name"] ?? cookie["Key"]);
                        string value = (string)(cookie["value"] ?? cookie["Value"]);
                        string domain = (string)(cookie["domain"] ?? cookie["Domain"]);

                        if (false == string.IsNullOrEmpty(name) && true == UrlHelper.CookieAppliesToHost(domain, host))
                            pairs.Add($"{name}={value}");
                    }

                    if (0 < pairs.Count)
                        retVal = string.Join("; ", pairs);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not read the cookies into a header", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
        /// <summary>
        /// Makes every GoTo and Get look like a click from a Google search: https://www.google.com/ becomes the
        /// browser's referrer, which <see cref="SetReferrer"/> sends with each GoTo's own navigation until
        /// WithUseReferrer clears or replaces it. Pages reached by clicking, iframes and what the page loads carry
        /// their real referrers, as they would after a real click. A referrer WithUseReferrer already set is kept.
        /// </summary>
        private void ApplyGoogleReferrer()
        {
            bool applied = false == (true == UseSelenium && BrowserType.FireFox == BrowserSettings.BrowserType);

            if (true == applied && null == BrowserSettings.Referrer)
                BrowserSettings.Referrer = "https://www.google.com/";

            if (true == applied)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Stealth Google Referrer applied.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot set a header, so the Google referrer was not applied", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// Tracks in-flight XHR/fetch requests on window.__gpalNetworkMonitor: inflight is the requests not yet
        /// finished, since is when the page started being watched, and droppedBelow[n] is when the in-flight count
        /// last dropped below n. One look at it answers whether the count stayed at or below a limit over a past
        /// stretch of time. Does nothing when the page already has it.
        /// </summary>
        internal const string NetworkMonitorScript = @"
                    (function() {
                        if (!window.__gpalNetworkMonitor) {
                            const monitor = window.__gpalNetworkMonitor = { inflight: 0, since: Date.now(), droppedBelow: {} };
                            const started = () => { monitor.inflight++; };
                            const finished = () => { monitor.droppedBelow[monitor.inflight] = Date.now(); monitor.inflight--; };

                            const origOpen = XMLHttpRequest.prototype.open;
                            XMLHttpRequest.prototype.open = function() {
                                this.addEventListener('loadstart', started);
                                this.addEventListener('loadend', finished);
                                origOpen.apply(this, arguments);
                            };

                            const origFetch = window.fetch;
                            window.fetch = function(...args) {
                                started();
                                return origFetch(...args).finally(finished);
                            };
                        }
                    })();
                ";
        /// <summary>
        /// Injects <see cref="NetworkMonitorScript"/>, used to detect network idle.
        /// </summary>
        private void ApplyNetworkMonitor()
        {
            string networkMonitorScript = NetworkMonitorScript;

            if (UsePuppeteer)
            {
                PuppeteerCommunicator.EnableFetchScriptInjection(networkMonitorScript, null)
                    .GetAwaiter().GetResult();
            }
            else if (BrowserType.FireFox == BrowserSettings.BrowserType)
            {
                // Firefox has no CDP preload scripts, so the monitor is injected after load with ExecuteScript.
                // The guard (function(){if(!window.__gpalNetworkMonitor)...}) makes re-injection safe on each navigation.
                ((OpenQA.Selenium.IJavaScriptExecutor)BrowserDriver).ExecuteScript(networkMonitorScript);
            }
            else
            {
                // Chrome and Edge count requests on GPAL's own connection to the browser, so the page gets no monitor
                BrowserSettings.TabScripts ??= SeleniumTabScripts.Start(BrowserSettings);
            }

            if (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Network idle counted from the browser's network events.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Network idle monitor script injected.", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// Injects scripts to hide signs of CDP-based automation: pins the Error stack to defeat stack-based CDP
        /// detection, and on Selenium overrides navigator.webdriver to undefined.
        /// </summary>
        private void ApplyCDPProtection()
        {
            // hide CDP port detection by adding a script that will always run when a page loads (before the website loads any detection scripts)
            // https://www.reddit.com/r/webscraping/comments/1evht3i/help_in_bypassing_cdp_detection/
            // https://github.com/rebrowser/rebrowser-patches
            // https://github.com/kaliiiiiiiiii/brotector/blob/master/brotector.js#L377 // string detections
            string antiAntiBotScript = @"
                        (function() {
                            var originalError = Error;
                            Object.defineProperty(Error.prototype, 'stack', {
                                configurable: false,
                                enumerable: true,
                                writable: true,
                                value: (function() {
                                    try {
                                        throw new originalError();
                                    } catch (e) {
                                        return e.stack;
                                    }
                                })()
                            });

                            // Pins each new error's stack as a plain value, so a page cannot swap in a getter that
                            // fires when CDP serializes the error. The rest of the error stays writable.
                            window.Error = new Proxy(originalError, {
                                construct(target, args, newTarget) {
                                    var instance = Reflect.construct(target, args, newTarget);
                                    Object.defineProperty(instance, 'stack', { value: instance.stack, writable: true, enumerable: false, configurable: false });
                                    return instance;
                                }
                            });
                        })();
                    ";

            if (true == UsePuppeteer)
            {
                PuppeteerCommunicator.EnableFetchScriptInjection(antiAntiBotScript, null).GetAwaiter().GetResult();
            }
            else if (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType)
            {
                BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, antiAntiBotScript);
            }

            // Selenium only: chromedriver launches with --enable-automation, which sets navigator.webdriver. GPAL
            // launches Puppeteer's browser without it, so it already reports false there.
            string antiAntiBotScript2 = "Object.defineProperty(navigator, 'webdriver', { get: () => undefined })";
            if (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType)
            {
                BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, antiAntiBotScript2);
            }

            // Both injections above need Puppeteer, or Selenium on Chrome or Edge.
            bool applied = true == UsePuppeteer || (true == UseSelenium && BrowserType.FireFox != BrowserSettings.BrowserType);

            if (true == applied)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Stealth CDP protection applied.", this, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot run a script on every new document, so CDP protection was not applied", this, GPALObjectType.Browser);
        }
        /// <summary>
        /// Applies the configured StealthType protections (CDP, network monitor, Google referrer, etc.) once
        /// per browser session, based on <see cref="BrowserSettings.StealthApplied"/>.
        /// </summary>
        private void CheckAndApplyStealth()
        {
            // hide CDP port detection by adding a script that will always run when a page loads (before the website loads any detection scripts)
            // https://www.reddit.com/r/webscraping/comments/1evht3i/help_in_bypassing_cdp_detection/
            // https://github.com/rebrowser/rebrowser-patches
            // https://github.com/kaliiiiiiiiii/brotector/blob/master/brotector.js#L377 // string detections

            if (false == BrowserSettings.StealthApplied)
            {
                if (false == UseOttoMagic && BrowserType.FireFox != BrowserType)
                {
                    if (BrowserSettings.StealthType.HasFlag(StealthType.CDP))
                        ApplyCDPProtection();
                }

                if (BrowserSettings.StealthType.HasFlag(StealthType.GoogleReferrer))
                {
                    // set referer to google to make us look like we are coming in from search results click
                    try 
                    { 
                        ApplyGoogleReferrer(); 
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex) 
                    { 
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Google Referrer stealth skipped", this, GPALObjectType.Browser, ex); 
                    }
                }
            }
            BrowserSettings.StealthApplied = true;
        }

        /// <summary>
        /// Limits <see cref="CaptureCalls(bool)"/> to calls whose url contains <paramref name="urlFragment"/>.<br/>
        /// Set it before capturing; earlier calls are not filtered.
        /// </summary>
        /// <param name="urlFragment">Any part of the url to keep, for example "/availability-api/"</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowCallTemplate WithCallFilter(string urlFragment)
        {
            BrowserSettings.CallFilter = urlFragment;

            return this;
        }

        /// <summary>
        /// Waits for a call matching WithCallFilter and returns it as a GPALRequest to send.<br/>
        /// Use it for values you cannot know ahead: a per-session token, a signed parameter, a header the page
        /// adds.<br/>
        /// The last match wins. Returns null if nothing matched in time.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .WithCallFilter("/availability-api/multiple-price-from")
        ///     .CaptureCallTemplate(out GPALRequest priceFrom);
        /// </code>
        /// </example>
        /// <param name="template">Receives the request the page made, or null when none arrived</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CaptureCallTemplate(out GPALRequest template)
        {
            template = null;

            // This engine cannot capture; say so now, not after 30 seconds.
            if (false == CanCaptureCalls)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No template: capturing calls is not available on [{BrowserSettings.AutomationEngine}]", this, GPALObjectType.Browser);

                return this;
            }

            CaptureCalls(true);

            GPALCall found = null;
            Stopwatch waited = Stopwatch.StartNew();

            while (null == found && CallTemplateWaitMilliseconds > waited.ElapsedMilliseconds)
            {
                CaptureCalls(out List<GPALCall> calls);

                // Last matching call. Calls captured before the filter was set are still listed, so filter again.
                foreach (GPALCall call in calls)
                    if (true == string.IsNullOrEmpty(BrowserSettings.CallFilter) || true == call.Url?.Contains(BrowserSettings.CallFilter))
                        found = call;

                if (null == found)
                    System.Threading.Thread.Sleep(250);
            }

            waited.Stop();

            if (null == found)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No call matching [{BrowserSettings.CallFilter}] in [{waited.ElapsedMilliseconds}]ms, so there is no template", this, GPALObjectType.Browser);

                return this;
            }

            template = (GPALRequest)GPAL.Request
                .WithPath(found.Url)
                .WithHttpMethod("POST".Equals(found.Method, StringComparison.OrdinalIgnoreCase) ? Enums.HttpVerb.Post : Enums.HttpVerb.Get)
                .WithBody(found.PostData)
                .WithName(BrowserSettings.CallFilter ?? found.Url);

            // Skip headers the browser sets itself; fetch refuses them.
            foreach (KeyValuePair<string, string> header in found.Headers)
                if (false == BrowserHelper.BrowserOwnedHeader(header.Key))
                    template.WithHeader($"{header.Key}: {header.Value}");

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Template taken from the page's own [{found.Method}] to [{found.Url}] after [{waited.ElapsedMilliseconds}]ms, with [{template.Headers.Count}] of its headers", this, GPALObjectType.Browser);

            return this;
        }

        // how long to wait for the page to make the call a template is wanted from
        private const int CallTemplateWaitMilliseconds = 30_000;

        // Puppeteer captures through CDP, OttoMagic through the extension's webRequest, Selenium through a recorder
        // script injected by the driver.
        private bool CanCaptureCalls => true == BrowserSettings.UsePuppeteer
                                     || true == BrowserSettings.UseOttoMagic
                                     || null != BrowserSettings.BrowserDriver;

        /// <summary>
        /// Records every request (method, url, resource type, body) until turned off, to find a site's API
        /// endpoints.<br/>
        /// Get them with <see cref="CaptureCalls(out List{GPALCall})"/>.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .CaptureCalls()
        ///     .GoTo("https://www.example.com/rooms")
        ///     .SaveTo(out List&lt;GPALCall&gt; calls);
        /// </code>
        /// </example>
        /// <param name="capture">True to record, false to stop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CaptureCalls(bool capture = true)
        {
            // Selenium with no driver cannot capture. Warn once per browser; CaptureCallTemplate calls this in a
            // loop.
            if (false == CanCaptureCalls)
            {
                if (false == captureUnavailableReported)
                {
                    captureUnavailableReported = true;

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Capturing calls is not available on [{BrowserSettings.AutomationEngine}]. It needs a browser that reports its requests, and this one has none started", this, GPALObjectType.Browser);
                }

                return this;
            }

            if (true == BrowserSettings.UseOttoMagic)
            {
                // webRequest records outside the page, so recording survives navigation and includes requests no
                // page script made.
                BrowserSettings.CaptureCalls = capture;

                MagicHelper.CaptureCalls(capture, BrowserSettings.CallFilter);
            }
            else if (false == BrowserSettings.UsePuppeteer && null != BrowserSettings.BrowserDriver)
            {
                // Selenium has no CDP event stream, so inject a recorder script. On Chrome and Edge it is also
                // registered for every new document. It is always injected into the current page, so calling this
                // after a navigation works.
                BrowserSettings.CaptureCalls = capture;

                if (true == capture)
                {
                    if (BrowserType.Chrome == BrowserSettings.BrowserType || BrowserType.Edge == BrowserSettings.BrowserType)
                        BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, BrowserHelper.CallRecorderScript);

                    BrowserHelper.ExecuteJavaScript(this, BrowserHelper.CallRecorderScript);
                }
            }
            else
            {
                // Store the setting first. This is usually called before the first GoTo, and reading
                // PuppeteerCommunicator would create one early.
                BrowserSettings.CaptureCalls = capture;

                // A Puppeteer communicator exists only once the browser runs, so enable capture now. Check
                // HasPuppeteerCommunicator; reading PuppeteerCommunicator creates one.
                if (true == BrowserSettings.HasPuppeteerCommunicator)
                    BrowserSettings.PuppeteerCommunicator.CaptureCalls(capture).GetAwaiter().GetResult();
            }

            return this;
        }

        /// <summary>
        /// Records what the page asks for and hands back everything recorded so far, so an endpoint can be read
        /// off a site rather than guessed at.<br/>
        /// Say it before going anywhere and say it again whenever you want what has happened since. Each call
        /// hands back its own list, so a workflow reading one is never reading what the browser is still writing.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .GoTo("https://www.example.com/rooms")
        ///     .CaptureCalls(out List&lt;GPALCall&gt; calls);
        /// </code>
        /// </example>
        /// <param name="calls">Receives the calls, empty when nothing was captured</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CaptureCalls(out List<GPALCall> calls)
        {
            CaptureCalls(true);

            calls = true == BrowserSettings.UsePuppeteer
                ? true == BrowserSettings.HasPuppeteerCommunicator
                    ? new List<GPALCall>(BrowserSettings.PuppeteerCommunicator.CapturedCalls)
                    : new List<GPALCall>()
                : true == BrowserSettings.UseOttoMagic
                    ? ExtensionRecordedCalls()
                    : RecordedCalls();

            // DEBUG, not INFO: CaptureCallTemplate calls this in a loop. It logs the result at INFO.
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Handing back [{calls.Count}] captured calls.", this, GPALObjectType.Browser);

            return this;
        }

        /// <summary>
        /// Clears the calls recorded so far. Recording continues; lists already returned are unchanged.
        /// </summary>
        /// <example>
        /// <code>
        /// browser.ClearCapturedCalls();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearCapturedCalls()
        {
            if (true == BrowserSettings.UseOttoMagic)
                // Resend the capture settings with clear = true; the extension empties its list and keeps recording.
                MagicHelper.CaptureCalls(BrowserSettings.CaptureCalls, BrowserSettings.CallFilter, true);
            else if (true == BrowserSettings.UsePuppeteer)
            {
                if (true == BrowserSettings.HasPuppeteerCommunicator)
                    BrowserSettings.PuppeteerCommunicator.ClearCapturedCalls();
            }
            else if (null != BrowserSettings.BrowserDriver)
                // Selenium stores the calls in the page (window.__gpalCalls); empty that array.
                BrowserHelper.ExecuteJavaScript(this, "window.__gpalCalls = []; return true;");

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Forgot the calls captured so far.", this, GPALObjectType.Browser);

            return this;
        }

        // Convert webRequest resource types to CDP names (main_frame to Document, xmlhttprequest to XHR), so
        // ResourceType matches across engines.
        private static string CdpResourceType(string webRequestType)
        {
            string retVal = webRequestType;

            switch (webRequestType)
            {
                case "main_frame":
                case "sub_frame": retVal = "Document"; break;
                case "xmlhttprequest": retVal = "XHR"; break;
                case "script": retVal = "Script"; break;
                case "image":
                case "imageset": retVal = "Image"; break;
                case "stylesheet": retVal = "Stylesheet"; break;
                case "font": retVal = "Font"; break;
                case "media": retVal = "Media"; break;
                case "websocket": retVal = "WebSocket"; break;
                case "ping": retVal = "Ping"; break;
                case "csp_report": retVal = "CSPViolationReport"; break;

                // webRequest reports XHR and fetch both as xmlhttprequest; mapping both to XHR loses nothing an API
                // search needs. "other" maps to "Other".
                case "other": retVal = "Other"; break;
            }

            return retVal;
        }

        // Reads the extension's recorded calls as GPALCall, like CDP. webRequest sees every request, so unlike the
        // Selenium recorder this includes documents, scripts and images.
        private List<GPALCall> ExtensionRecordedCalls()
        {
            List<GPALCall> retVal = new List<GPALCall>();

            string json = MagicHelper.GetCapturedCalls();

            if (true == string.IsNullOrWhiteSpace(json)) return retVal;

            // GetCapturedCalls hands back the recorder's snapshot as the json text of one object
            json = json.Trim();

            Newtonsoft.Json.Linq.JObject snapshot;

            // Unparseable json logs an error with the start of the text, so it is not mistaken for a page with no
            // calls.
            try
            {
                snapshot = Newtonsoft.Json.Linq.JObject.Parse(json);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"The captured calls could not be read. [{json.Length}] characters starting [{json.Substring(0, Math.Min(120, json.Length))}]", this, GPALObjectType.Browser, ex);

                return retVal;
            }

            // The recorder keeps the newest calls and counts dropped ones. Log the count, so too many calls can be
            // told from none.
            int dropped = (int?)snapshot["dropped"] ?? 0;

            if (0 < dropped)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The recorder dropped [{dropped}] older calls to stay within its limit", this, GPALObjectType.Browser);

            foreach (Newtonsoft.Json.Linq.JObject recorded in snapshot["calls"] ?? new Newtonsoft.Json.Linq.JArray())
            {
                GPALCall call = new GPALCall
                {
                    Url = recorded["url"]?.ToString(),
                    Method = recorded["method"]?.ToString(),
                    ResourceType = CdpResourceType(recorded["type"]?.ToString()),
                    PostData = recorded["postData"]?.ToString(),
                    Initiator = recorded["initiator"]?.ToString(),
                    Status = (int?)recorded["status"] ?? 0
                };

                if (recorded["headers"] is Newtonsoft.Json.Linq.JObject headers)
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> header in headers)
                        call.Headers[header.Key] = header.Value?.ToString();

                retVal.Add(call);
            }

            return retVal;
        }

        // Reads the Selenium recorder's calls from the page as GPALCall, like CDP. It sees only fetch and
        // XMLHttpRequest, not documents, scripts or images.
        private List<GPALCall> RecordedCalls()
        {
            List<GPALCall> retVal = new List<GPALCall>();

            if (null == BrowserSettings.BrowserDriver) return retVal;

            string json = BrowserHelper.ExecuteJavaScript(this, "return JSON.stringify(window.__gpalCalls || []);");

            if (true == string.IsNullOrWhiteSpace(json)) return retVal;

            foreach (Newtonsoft.Json.Linq.JObject recorded in Newtonsoft.Json.Linq.JArray.Parse(json))
            {
                GPALCall call = new GPALCall
                {
                    Url = recorded["url"]?.ToString(),
                    Method = recorded["method"]?.ToString(),
                    ResourceType = recorded["type"]?.ToString(),
                    PostData = recorded["postData"]?.ToString(),
                    Initiator = "script",
                    Status = (int?)recorded["status"] ?? 0
                };

                if (recorded["headers"] is Newtonsoft.Json.Linq.JObject headers)
                    foreach (KeyValuePair<string, Newtonsoft.Json.Linq.JToken> header in headers)
                        call.Headers[header.Key] = header.Value?.ToString();

                retVal.Add(call);
            }

            return retVal;
        }

        /// <summary>
        /// Sends an API request from inside the page, with the browser's cookies, TLS fingerprint, header order and
        /// anti-bot clearance. The body is kept for .SaveTo.<br/>
        /// .WithPages sends one request per page, the page number replacing .WithPageToken. .WithTokensFrom runs all
        /// pages once per row, filling numbered tokens from the row's columns. A CallAfterFetch handler is called
        /// with each row's pages.
        /// </summary>
        /// <example>
        /// <code>
        /// string json = null;
        ///
        /// browser
        ///     .GoTo("https://www.harrods.com/")
        ///     .WithTokensFrom(searchTerms)
        ///     .WithPages(20)
        ///     .Fetch(productSearch)
        ///     .SaveTo(ref json);
        /// </code>
        /// </example>
        /// <param name="request">The request to issue, built with GPAL.Request</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithHeaderOrFileActions Fetch(GPALRequest request)
        {
            CurrentUOW.FetchResults.Clear();

            if (null == request)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No request given to fetch.", this, GPALObjectType.Browser);
            else
                BrowserHelper.FetchWithTokens(this, request);

            InAction(false);
            return this;
        }
        /// <summary>
        /// Attempts to interpret <paramref name="number"/> as a 32-bit unsigned integer and decode it into a
        /// dotted-quad IPv4 address.
        /// </summary>
        /// <param name="number">String to attempt to decode as an IP address</param>
        /// <param name="ipAddress">Receives the decoded dotted-quad IP address, or null if decoding failed</param>
        /// <returns>True if <paramref name="number"/> was successfully decoded</returns>
        internal bool TryDecodeNumberToIpAddress(string number, out string ipAddress)
        {
            bool retVal = false;
            uint numberUInt;

            ipAddress = null;

            if (true == UInt32.TryParse(number, out numberUInt))
            {

                // Convert number to IPv4 bytes (network byte order)
                byte[] bytes = BitConverter.GetBytes(numberUInt);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes); // Ensure big-endian for IP

                // Create IP address from bytes
                try
                {
                    var ip = new System.Net.IPAddress(bytes);
                    ipAddress = ip.ToString(); // e.g., "192.168.1.1"

                    // Validate: Ensure it's a valid IPv4 address (4 octets, 0-255 range)
                    var octets = ipAddress.Split('.');
                    if (octets.Length != 4) return false;
                    if (octets.All(o => byte.TryParse(o, out byte value) && value <= 255))
                    {
                        try
                        {
                            var host = System.Net.Dns.GetHostEntry(ip);
                            //                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{number}] resolves to [{ip}][{host.HostName}].", this, GPALObjectType.Browser);
                            retVal = true;
                        }
                        catch (GPALException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{number}][{ip}] doesn't resolve to a hostname.", this, GPALObjectType.Browser, ex);
                        }
                    }

                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{number}] doesn't resolve to an IP address", this, GPALObjectType.Browser, ex);
                }
            }

            return retVal;
        }
        /// <summary>
        /// Applies the configured <see cref="BrowserSettings.Referrer"/> to the navigation GoTo is about to make, as
        /// that page's referrer only. Selenium and Puppeteer send it with the navigation; OttoMagic sets the header
        /// for that one request. If the referrer is set to an empty string, clears it instead; nothing outlives a
        /// navigation, so there is nothing in the browser to take back.
        /// </summary>
        internal void SetReferrer()
        {
            string message = $"[{BrowserSettings.Referrer}] referrer applied.";

            if (null != BrowserSettings.Referrer)
            {
                bool applied = true;

                // an empty string clears the setting
                if (true == string.Empty.Equals(BrowserSettings.Referrer))
                {
                    BrowserSettings.Referrer = null;
                    message = $"Cleared referrer.";
                }
                else if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.OverrideReferrer(BrowserSettings.Referrer);
                else if (true == UsePuppeteer)
                    PuppeteerClient.OverrideReferrer(BrowserSettings.Referrer).Execute();
                else if (BrowserType.FireFox == BrowserSettings.BrowserType)
                    applied = false;
                // Selenium: SeleniumGoToUrl sends it with the navigation

                if (true == applied)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message, this, GPALObjectType.Browser);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot set a header, so the referrer was not applied", this, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Applies the configured <see cref="BrowserSettings.OverrideUserAgent"/> as the browser's user agent for subsequent requests.
        /// If the user agent is set to an empty string, clears the override instead.
        /// </summary>
        internal void SetUserAgent()
        {
            string message = $"[{BrowserSettings.OverrideUserAgent}] user agent applied.";
            var parms = new Dictionary<string, object> { { "userAgent", BrowserSettings.OverrideUserAgent } };

            if (null != BrowserSettings.OverrideUserAgent)
            {
                bool applied = true;

                if (true == UsePuppeteer)
                    PuppeteerClient.SetUserAgent(BrowserSettings.OverrideUserAgent).Execute();
                else if (true == BrowserSettings.UseOttoMagic)
                    MagicHelper.SetUserAgent(BrowserSettings.OverrideUserAgent);
                else if (BrowserType.FireFox != BrowserSettings.BrowserType)
                    BrowserHelper.ExecuteCdp(BrowserDriver, "Network.setUserAgentOverride", parms);
                else
                    applied = false;

                // an empty string clears the setting
                if (true == string.Empty.Equals(BrowserSettings.OverrideUserAgent))
                {
                    BrowserSettings.OverrideUserAgent = null;
                    message = $"Cleared user agent override.";
                }

                if (true == applied)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message, this, GPALObjectType.Browser);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{BrowserSettings.BrowserType}] on [{BrowserSettings.AutomationEngine}] cannot set a user agent, so the override was not applied", this, GPALObjectType.Browser);
            }
        }
    }
}
