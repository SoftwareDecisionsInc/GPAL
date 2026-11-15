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
        /// Navigates to the next page of results, either by clicking/following the configured "next page"
        /// selector (or its href, if any), or, when InfiniteScroll is enabled, by scrolling to the bottom
        /// of the page and waiting for new content to load.
        /// </summary>
        /// <param name="browser">Browser to navigate</param>
        /// <returns>True if the next page action (click or scroll) was performed, false if no next page button was found</returns>
        public static bool GotoNextPage(Browser browser)
        {

            List<GPALElement> matchedElems = null;
            ReadOnlyCollection<GPALElement> elems = null;
            bool clicked = false;
            byte VK_END = 0x23;
            int withAllThatMatchSave = browser.CurrentUOW.WithAllThatMatch;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Getting next page.", browser, GPALObjectType.Browser);
            browser.WithAllThatMatch(1);


            // CAVEAT: an infinite scroll jumps to the bottom of the page, so a page that unrenders rows scrolled out of view can drop rows between reads
            if (false == browser.CurrentUOW.InfiniteScroll)
            {
                // we cannot used cached results knowing we are going to a new page
                browser.CurrentUOW.NextPageButton.WebSelectorFoundResults = null;
                browser.CurrentUOW.NextPageButton.WebSelectorMatchedResults = null;
                foreach (var sel in browser.CurrentUOW.WithSelectorList)
                {
                    sel.WebSelectorFoundResults = null;
                    sel.WebSelectorMatchedResults = null;
                }

                elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, browser.CurrentUOW.NextPageButton, out bool matchedAll, out matchedElems, null, false);

                // WithAllThatMatch(1) above limits this to one next page button
                if (null != elems && 0 < elems.Count && true == matchedAll)
                {
                    try
                    {

                        if (false == string.IsNullOrEmpty(elems[0].Href))
                        {
                            string href = elems[0].Href;
                            UnitOfWork saveUOW = browser.CurrentUOW;    // GoTo starts a new unit of work; this one is put back after
                            bool obeySave = browser.BrowserSettings.ObeyRobotsTxt;
                            browser.BrowserSettings.ObeyRobotsTxt = false;
                            browser.GoTo(href);    // checkdocumentready is in goto - do not perform robots check on navigation

                            // A site can show an interstitial, such as a device check, at its own url that is already
                            // document-ready. Poll until the url equals href, up to WaitForDocumentReadyTimeoutMs.
                            Stopwatch arriving = Stopwatch.StartNew();
                            string here = GetCurrentUrl(browser.BrowserSettings);

                            if (false == href.Equals(here, StringComparison.OrdinalIgnoreCase))
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting for [{href}], the page showing is [{here}]", browser, GPALObjectType.Browser);

                            while (false == href.Equals(here, StringComparison.OrdinalIgnoreCase)
                                && browser.BrowserSettings.WaitForDocumentReadyTimeoutMs > arriving.ElapsedMilliseconds)
                            {
                                System.Threading.Thread.Sleep(250);

                                here = GetCurrentUrl(browser.BrowserSettings);
                            }

                            if (false == href.Equals(here, StringComparison.OrdinalIgnoreCase))
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Gave up waiting for [{href}] after [{arriving.ElapsedMilliseconds}] ms, the page showing is [{here}]", browser, GPALObjectType.Browser);

                            browser.CurrentUOW = saveUOW;
                            browser.BrowserSettings.ObeyRobotsTxt = obeySave;
                            browser.BrowserSettings.CurrentURL = href;
                        }
                        else
                        {
                            // click the one next page button
                            elems[0].Click();
                            //CheckDocumentReady(browser);
                        }

                        clicked = true;
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Exception thrown", browser, GPALObjectType.Browser, ex);
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to find Next Page Button", browser, GPALObjectType.Browser);
                }
            }
            else // infinite scroll, go to page end and wait for load
            {
                if (false == browser.BrowserSettings.UseHeadless && (true == browser.BrowserSettings.UseHardware))
                    HardwareHelper.SendKey(VK_END);
                else
                    _ = browser.PageEnd;

                CheckDocumentReady(browser);

                clicked = true;
            }

            browser.WithAllThatMatch(withAllThatMatchSave);

            return clicked;
        }
        /// <summary>
        /// Gets the document ready state from OttoMagic. On nav-error, raise OnFail with GPALFailure.Navigation; an
        /// error page runs no content script, so waiting will not help.<br/>
        /// OttoMagic answers NOTOK until a new page loads, so navigating elsewhere recovers.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <param name="sessionToken">Token identifying this wait, so repeat messages can be suppressed</param>
        /// <returns>The ready state, or what the browser said about the failed navigation</returns>
        static string ReadyStatusOrNavError(Browser browser, string sessionToken)
        {
            string status = browser.MagicHelper.GetReadyStatus(sessionToken)?.Trim('"');

            if (true == status?.ToLower().Contains("nav-error"))
            {
                // GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The page did not load, so there is nothing to work with. [{status}]", browser, GPALObjectType.Browser);
                browser.RaiseOnFail(GPALFailure.Navigation, status);
            }

            return status;
        }
        /// <summary>
        /// Waits for the page to be ready before/after an action, using whichever signal the browser is configured for:
        /// document.readyState (via OttoMagic, Puppeteer or Selenium javascript), or network-idle detection.<br/>
        /// unless <paramref name="overrideSetting"/> is true.
        /// </summary>
        /// <param name="browser">Browser to check</param>
        /// <param name="overrideSetting">When true, waits for document.ready even if BrowserSettings.WaitOnDocumentReady is false</param>
        public static void CheckDocumentReady(Browser browser, bool overrideSetting = false)
        {
            bool hasURL = null != browser?.BrowserSettings.CurrentURL && false == "about:blank".Equals(browser.BrowserSettings.CurrentURL);

            // A loading page rejects scripts, so suppress JavaScript failures while waiting. The finally restores
            // the previous value rather than false, for nested calls and throws.
            bool wasSuppressed = true == browser?.BrowserSettings.SuppressJavaScriptFailures;
            string sessionToken = Guid.NewGuid().ToString();

            if (null != browser)
                browser.BrowserSettings.SuppressJavaScriptFailures = true;

            try
            {
                if ((true == browser?.BrowserSettings.WaitOnDocumentReady && true == hasURL) || true == overrideSetting)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting up to [{browser.BrowserSettings.WaitForDocumentReadyTimeoutMs}] ms on document.ready");

                    // nav-error means the navigation failed; stop instead of polling to the timeout.
                    if (true == browser?.UseOttoMagic)
                        WaitForValueOrTime(browser, "complete, interactive, nav-error", () => ReadyStatusOrNavError(browser, sessionToken), browser.BrowserSettings.WaitForDocumentReadyTimeoutMs);
                    else if (true == browser?.UsePuppeteer)
                        WaitForValueOrTime(browser, "complete, interactive", () => ((PuppeteerClient)browser.PuppeteerClient).GetReadyStatus(sessionToken).Execute<object>(), browser.BrowserSettings.WaitForDocumentReadyTimeoutMs);
                    else    // handles selenium
                        WaitForValueOrTime(browser, "complete, interactive", () => JsCheckReadyState(browser, sessionToken), browser.BrowserSettings.WaitForDocumentReadyTimeoutMs);
                }
                else if (true == browser.BrowserSettings.WaitOnNetworkIdle && true == hasURL)
                {
                    WaitForValueOrTime(browser, "true", () => WaitForNetworkIdle(browser, browser.BrowserSettings.NetworkIdleMaxConnections, browser.BrowserSettings.NetworkIdlePruneMs, sessionToken).ToString(), browser.BrowserSettings.NetworkIdleTimeoutMs, NetworkIdlePollMs);
                    }
                }
            finally
            {
                if (null != browser)
                    browser.BrowserSettings.SuppressJavaScriptFailures = wasSuppressed;
            }
        }

        /// <summary>
        /// Returns the current page URL, queried via OttoMagic, Puppeteer, or the Selenium WebDriver
        /// depending on the browser's automation engine.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings of the browser to query</param>
        /// <returns>The current page URL</returns>
        public static string GetCurrentUrl(BrowserSettings browserSettings)
        {
            string currentUrl = null;

            if (true == browserSettings.UseOttoMagic)
                currentUrl = browserSettings.MagicHelper.GetCurrentUrl();
            else if (true == browserSettings.UsePuppeteer)
                currentUrl = browserSettings.PuppeteerClient.GetCurrentUrl().Execute();
            else
                currentUrl = browserSettings.Browser.BrowserDriver.Url;

            // OttoMagic and Puppeteer return the raw JSON string, quotes included. Deserialize rather than trim so
            // escaped quotes in the url are unescaped.
            if (true == currentUrl?.StartsWith("\"") && true == currentUrl.EndsWith("\""))
                currentUrl = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(currentUrl);

            return currentUrl;
        }
        /// <summary>
        /// Use javascript to check the page document.ready state, only called from one place that does the timing checks
        /// While only used in GPAL for Selenium checking, it is defensively coded to handle all Automation Engines
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="sessionToken">Token used to suppress repeated log messages within the same wait session</param>
        /// <returns></returns>
        public static string JsCheckReadyState(dynamic browser, string sessionToken)
        {
            string readyState = "loading";

            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                supressedMessage = false;
                lastSessionToken = sessionToken;
            }

            try
            {
                if (browser.UseOttoMagic)
                {
                    readyState = browser.BrowserSettings.MagicHelper.GetReadyStatus();
                    if (null == readyState)
                        readyState = @"loading";
                    else
                        readyState = readyState.Trim('"');
                }
                else
                {
                    BrowserSettings settings = browser.BrowserSettings;

                    // Selenium on Chrome and Edge reads it on GPAL's own connection to the browser, so the check
                    // runs nothing through chromedriver that the page can catch
                    if (false == browser.UsePuppeteer && BrowserType.FireFox != settings.BrowserType)
                        settings.TabScripts ??= SeleniumTabScripts.Start(settings);

                    if (false == browser.UsePuppeteer && null != settings.TabScripts)
                        readyState = settings.TabScripts.ReadyState(CurrentTargetId(settings.BrowserDriver)) ?? "loading";
                    else
                    {
                        bool withReturn = false == browser.UsePuppeteer;

                        // read document.readyState in the page
                        readyState = ExecuteJavaScript(browser, $"{(true == withReturn ? "return " : "")}document.readyState");
                    }
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"Error executing JavaScript",
                    null, GPALObjectType.None, ex);
            }

            string currentErrorMessage = $"Document.Ready status [{readyState}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }

            return readyState;
        }

        /// <summary>
        /// Navigates the Selenium WebDriver to <paramref name="url"/> and inspects the browser's performance logs
        /// (Network.responseReceived events) to determine the HTTP status code of the navigation, storing it in
        /// browserSettings.ServerResponseCode (0 if no status could be determined, -1 on error).
        /// </summary>
        /// <param name="url">URL to navigate to</param>
        /// <param name="browserSettings">BrowserSettings of the browser to navigate, used to read performance logs and store the resulting status code</param>
        public static void SeleniumGoToUrl(GPALUrl url, BrowserSettings browserSettings)
        {
            // The browser never sends the #fragment, so the response's url has none; match without it.
            string target = url.ToString();
            int fragmentAt = target.IndexOf('#');
            string normalizedTarget = (0 <= fragmentAt ? target.Substring(0, fragmentAt) : target).TrimEnd('/').ToLowerInvariant();

            try
            {
                // 1. Perform navigation. The referrer goes with the navigation itself, which only CDP can send; the
                // driver's own navigate has no referrer. It is the one WithUseReferrer asked for, or the stealth
                // Google one, on every GoTo and Get until cleared. Firefox has no CDP, so it navigates without one.
                string referrer = BrowserType.FireFox == browserSettings.BrowserType ? null : browserSettings.Referrer;

                if (null == referrer)
                    browserSettings.BrowserDriver.Navigate().GoToUrl(url);
                else
                {
                    ExecuteCdp(browserSettings.BrowserDriver, "Page.navigate", new Dictionary<string, object> { { "url", url.Url }, { "referrer", referrer } });

                    // CDP returns once the new page commits, the driver's navigate once it has loaded. With both waits
                    // off the workflow asked for none, so it goes on from the commit.
                    if (true == browserSettings.WaitOnDocumentReady || true == browserSettings.WaitOnNetworkIdle)
                        CheckDocumentReady((Browser)browserSettings.Browser, true);
                }

                // wait 400 ms for the network events of a fast 304 or cached page to reach the log
                System.Threading.Thread.Sleep(400);

                // 2. Fetch performance logs (Chrome and Edge only; geckodriver does not support getLog)
                if (browserSettings.BrowserType != BrowserType.FireFox)
                {

                var performanceLogs = browserSettings.BrowserDriver.Manage().Logs.GetLog("performance");

                // The browser can land on another url than the one asked for, as when Chrome upgrades http to https,
                // so the response is looked for under the landed url too. It is read over CDP, since chromedriver's
                // own url read runs a script the page can catch.
                string landed = CurrentTargetInfo(browserSettings.BrowserDriver)?["url"]?.ToString() ?? "";
                int landedFragmentAt = landed.IndexOf('#');
                string normalizedLanded = (0 <= landedFragmentAt ? landed.Substring(0, landedFragmentAt) : landed).TrimEnd('/').ToLowerInvariant();

                int? bestStatus = null;
                string bestMatchedUrl = null;
                int candidatePriority = int.MaxValue;
                // the page responses the log did hold, named when none matched the target
                List<string> documentUrls = new List<string>();

                foreach (var entry in performanceLogs)
                {
                    string message = entry.Message;

                    if (message.IndexOf("Network.responseReceived", StringComparison.Ordinal) == -1)
                        continue;

                    if (message.IndexOf("\"type\":\"Document\"", StringComparison.Ordinal) != -1)
                        documentUrls.Add((string)Newtonsoft.Json.Linq.JObject.Parse(message).SelectToken("message.params.response.url"));

                    // Quick filter to reduce sub-resource noise
                    if (message.IndexOf("\"url\":\"", StringComparison.Ordinal) == -1 ||
                        (message.IndexOf(normalizedTarget, StringComparison.OrdinalIgnoreCase) == -1 &&
                         message.IndexOf(normalizedLanded, StringComparison.OrdinalIgnoreCase) == -1))
                        continue;

                    // Extract status
                    var statusMatch = System.Text.RegularExpressions.Regex.Match(
                        message,
                        @"""status(?:Code)?""\s*:\s*(\d+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant
                    );

                    if (!statusMatch.Success)
                        continue;

                    int status;
                    if (!int.TryParse(statusMatch.Groups[1].Value, out status))
                        continue;

                    if (status < 100 || status > 599)
                        continue;

                    // Priority: lower number = better candidate
                    int priority;
                    if (status == 304)
                        priority = 0;           // jackpot: cache hit
                    else if (status >= 200 && status < 300)
                        priority = 10;          // success
                    else if (status >= 300 && status < 400)
                        priority = 20;          // redirect (often intermediate)
                    else if (status >= 400 && status < 500)
                        priority = 30;          // client error (useful to know)
                    else if (status >= 500)
                        priority = 40;          // server error
                    else
                        priority = 50;

                    bool shouldUpdate = false;

                    if (!bestStatus.HasValue)
                    {
                        shouldUpdate = true;
                    }
                    else if (priority < candidatePriority)
                    {
                        shouldUpdate = true;
                    }
                    else if (priority == candidatePriority && status == 304)
                    {
                        shouldUpdate = true;
                    }

                    if (shouldUpdate)
                    {
                        bestStatus = status;
                        candidatePriority = priority;

                        // the matched url, kept for debugging
                        var urlMatch = System.Text.RegularExpressions.Regex.Match(message, @"""url"":""([^""]+)""");
                        if (urlMatch.Success)
                        {
                            bestMatchedUrl = urlMatch.Groups[1].Value;
                        }
                    }
                }

                if (bestStatus.HasValue)
                {
                    browserSettings.ServerResponseCode = bestStatus.Value;

                    // Optional: log what we decided (uncomment for debugging)
                    /*
                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEBUG,
                        $"Captured final status {bestStatus.Value} (priority {candidatePriority}) for ~{bestMatchedUrl ?? "unknown"}",
                        browserSettings,
                        GPALObjectType.Other
                    );
                    */
                }
                else
                {
                    // no status for this navigation in the log
                    browserSettings.ServerResponseCode = 0;
                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEBUG,
                        $"No valid HTTP status captured in performance logs for [{url}], [{performanceLogs.Count}] entries held page responses [{string.Join(", ", documentUrls)}]",
                        browserSettings,
                        GPALObjectType.Other
                    );
                }

                } // end Chrome/Edge only block
                else
                {
                    try
                    {
                        var result = ((OpenQA.Selenium.IJavaScriptExecutor)browserSettings.BrowserDriver)
                            .ExecuteScript("return performance.getEntriesByType('navigation')[0]?.responseStatus ?? 0;");
                        browserSettings.ServerResponseCode = result != null && int.TryParse(result.ToString(), out int ffStatus) && ffStatus > 0 ? ffStatus : 0;
                    }
                    catch
                    {
                        browserSettings.ServerResponseCode = 0;
                    }
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                browserSettings.ServerResponseCode = -1;
                GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"Unexpected error navigating to [{url}]",
                    browserSettings,
                    GPALObjectType.Other,
                    ex
                );
            }
        }

        /// <summary>
        /// Records the current document so <see cref="DocumentReplaced"/> can tell after a click whether a new page
        /// loaded, which shows before the url changes.<br/>
        /// Selenium keeps the html element; Puppeteer and OttoMagic keep a DocumentStamp.
        /// </summary>
        /// <returns>A token to hand to <see cref="DocumentReplaced"/>, null when this engine cannot mark one</returns>
        /// <summary>
        /// Returns performance.timeOrigin and location.href in one call, separated by a bar.<br/>
        /// A new timeOrigin means a new document, even at the same url, as when a form posts to itself; it does not
        /// change on a same-document navigation. One call saves a round trip per poll.
        /// </summary>
        /// <param name="browser">Browser to read</param>
        /// <returns>timeOrigin and url separated by a bar, or null when the page could not answer</returns>
        internal static string DocumentStamp(Browser browser)
        {
            // OttoMagic uses its own endpoint. ExecuteJavaScript there runs through userScripts.execute, which
            // requires the profile to allow user scripts
            string stamp = true == browser.UseOttoMagic
                ? browser.MagicHelper.GetDocumentStamp()
                : ExecuteJavaScriptObj("return performance.timeOrigin + '|' + location.href;", browser)?.ToString();

            return stamp;
        }

        // Split on the first bar; timeOrigin comes first and has none, the url may.
        private static string OriginFromStamp(string stamp)
        {
            int bar = stamp?.IndexOf('|') ?? -1;
            string origin = 0 <= bar ? stamp.Substring(0, bar) : null;

            return origin;
        }

        private static string UrlFromStamp(string stamp)
        {
            int bar = stamp?.IndexOf('|') ?? -1;
            string url = 0 <= bar ? stamp.Substring(bar + 1) : null;

            return url;
        }

        internal static object MarkDocument(Browser browser)
        {
            object mark = null;

            // Selenium marks the document with the html element and detects replacement by staleness, which
            // needs no script. The others take a stamp, which also carries the url the poll would otherwise
            // fetch separately
            if (true == browser.UseSelenium)
                mark = browser.BrowserDriver.FindElement(By.TagName("html"));
            else if (true == browser.UsePuppeteer || true == browser.UseOttoMagic)
                mark = DocumentStamp(browser);

            return mark;
        }
        /// <summary>
        /// True if the marked document was replaced. Selenium: reading the saved html element throws
        /// StaleElementReferenceException or NoSuchElementException. Puppeteer and OttoMagic: a new DocumentStamp
        /// has a different timeOrigin. With no mark, false, leaving only the url check.
        /// </summary>
        /// <param name="browser">Browser the mark came from</param>
        /// <param name="mark">Token from <see cref="MarkDocument"/></param>
        /// <returns>true when the page we marked is gone, false while it is still there or we cannot tell</returns>
        internal static bool DocumentReplaced(Browser browser, object mark)
        {
            bool replaced = false;

            if (true == browser.UseSelenium && mark is IWebElement html)
                try { _ = html.TagName; }
                catch (StaleElementReferenceException) { replaced = true; }
                catch (NoSuchElementException) { replaced = true; }
            else if (mark is string priorStamp)
            {
                string nowOrigin = OriginFromStamp(DocumentStamp(browser));
                string wasOrigin = OriginFromStamp(priorStamp);

                replaced = false == string.IsNullOrEmpty(nowOrigin)
                        && false == string.IsNullOrEmpty(wasOrigin)
                        && wasOrigin != nowOrigin;
            }

            return replaced;
        }
        /// <summary>
        /// After a click, polls until the document is replaced or the url changes, or
        /// <paramref name="timeOutAfterMs"/> passes.<br/>
        /// Replacement shows first and catches a slow load whose url still shows the old page; the url catches a
        /// same-document navigation. A missed navigation means the workflow reads the old page.<br/>
        /// Console and debug events are off while polling.
        /// </summary>
        /// <param name="browser">Browser to watch</param>
        /// <param name="priorUrl">Url the browser was on before the action that may have navigated</param>
        /// <param name="documentMark">Document held before the action, from <see cref="MarkDocument"/></param>
        /// <param name="timeOutAfterMs">How long to keep watching before deciding nothing navigated</param>
        /// <returns>true when the browser started going somewhere inside the grace period, false when it never did</returns>
        internal static bool WaitForNavigationOrTime(Browser browser, string priorUrl, object documentMark, int timeOutAfterMs)
        {
            bool navigated = false;
            bool documentGone = false;
            bool urlChanged = false;
            string lastUrl = priorUrl;
            DateTime timeout = DateTime.Now.AddMilliseconds(timeOutAfterMs);
            GPALEventType publishToConsole = GPAL.GPALSettings.ConsoleEvents;
            GPALEventType publishToDebug = GPAL.GPALSettings.DebugEvents;

            GPAL.GPALSettings.ConsoleEvents = GPALEventType.NONE;
            GPAL.GPALSettings.DebugEvents = GPALEventType.NONE;

            try
            {
                if (true == browser.IsAlive)
                    do
                    {
                        // One round trip for both signals. A stamp that cannot be read leaves the last known
                        // url in place and the loop repeats
                        if (documentMark is string priorStamp)
                        {
                            string nowStamp = DocumentStamp(browser);
                            string nowOrigin = OriginFromStamp(nowStamp);
                            string wasOrigin = OriginFromStamp(priorStamp);

                            // A missing origin on either side means undecided. Only two origins that both
                            // arrived and differ count as a replaced document
                            documentGone = false == string.IsNullOrEmpty(nowOrigin)
                                        && false == string.IsNullOrEmpty(wasOrigin)
                                        && wasOrigin != nowOrigin;

                            lastUrl = UrlFromStamp(nowStamp) ?? lastUrl;
                        }
                        else
                        {
                            documentGone = DocumentReplaced(browser, documentMark);
                            lastUrl = browser.GetSetCurrentUrl();
                        }

                        urlChanged = false == UrlHelper.AreEquivalent(priorUrl, lastUrl);

                        navigated = true == documentGone || true == urlChanged;

                        // only sleep if we are going round again, so a grace of 0 costs exactly one look
                        if (false == navigated && DateTime.Now < timeout)
                            Thread.Sleep(NavigationPollMs);
                    }
                    while (false == navigated && DateTime.Now < timeout);
            }
            finally
            {
                GPAL.GPALSettings.ConsoleEvents = publishToConsole;
                GPAL.GPALSettings.DebugEvents = publishToDebug;
            }

            // One DEBUG line per click, after events are back on: document replaced, url changed, and whether there
            // was a document mark.
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                $"Navigation watch [{(navigated ? "navigated" : "stayed")}] document [{(documentGone ? "replaced" : "same")}] url [{(urlChanged ? "changed" : "same")}] mark [{(documentMark is long mark ? mark.ToString() : null == documentMark ? "none" : "held")}] from [{priorUrl}] to [{lastUrl}]",
                browser, GPALObjectType.Browser);

            return navigated;
        }

        /// <summary>
        /// Method that will repeatedly call another method until it either times out or it gets the expected result string from the method
        /// the expected results can be a csv string if multiple values are acceptable
        /// this method is used to check document.ready status or to check for network idle conditions. so the expectes stringd are well known or user defined
        /// </summary>
        /// <param name="browser">Browser the waited-on value comes from</param>
        /// <param name="resultsExpected">string or csv string list of expected return results</param>
        /// <param name="action">method and parms to call repeatedly</param>
        /// <param name="timeOutAfterMs">time to wait in milliseconds for the expected result before timing out</param>
        /// <param name="pollMs">time to wait in milliseconds between calls</param>
        internal static void WaitForValueOrTime(Browser browser, string resultsExpected, Func<dynamic> action, int timeOutAfterMs, int pollMs = WaitForValuePollMs)
        {
            DateTime timeout = DateTime.Now.AddMilliseconds(timeOutAfterMs);
            string taskReturnValue;
            // Trim each expected value; callers put a space after each comma.
            string[] tokens = resultsExpected.Split(',').Select(token => token.Trim()).ToArray();

            if (true == browser.IsAlive)
                do
                {
                    dynamic result = action();

                    try
                    {
                        // a Task: wait for it and use its result
                        var task = (Task<string>)result; // throws when result is not a Task
                        task.GetAwaiter().GetResult(); // block until complete

                        // If generic Task<T>, get the Result property
                        var taskType = task.GetType();
                        if (taskType.IsGenericType)
                        {
                            var taskResult = taskType.GetProperty("Result")?.GetValue(task);
                            taskReturnValue = taskResult?.ToString() ?? resultsExpected;
                        }
                        else
                        {
                            taskReturnValue = tokens[0]; // non-generic Task
                        }
                    }
                    catch
                    {
                        // not a Task: use the value as a string
                        taskReturnValue = result?.ToString() ?? resultsExpected;
                    }

                    foreach (string token in tokens)
                        if (true == taskReturnValue.ToLower().Contains(token))
                            return;

                    Thread.Sleep(pollMs);
                }
                while (DateTime.Now < timeout);
        }

        /// <summary>
        /// Answers at once whether the network has been idle: at most <paramref name="maxConnections"/> requests in
        /// flight for the whole of the last <paramref name="pruneMs"/>. The caller asks again until it gets true or
        /// its own timeout ends.<br/>
        /// Puppeteer and OttoMagic ask their engine, which publishes what it found. Selenium looks at the javascript
        /// network monitor (window.__gpalNetworkMonitor); a page without it (Firefox after a navigation) gets it here,
        /// and is watched from then on.
        /// </summary>
        /// <param name="browser">Browser to monitor</param>
        /// <param name="maxConnections">The most requests in flight that still count as idle</param>
        /// <param name="pruneMs">How far back, in milliseconds, the in-flight count must have stayed at or below maxConnections</param>
        /// <param name="sessionToken">Token used to suppress repeated log messages within the same wait session</param>
        /// <returns>True if the network is idle, false otherwise</returns>
        public static bool WaitForNetworkIdle(Browser browser, int maxConnections, int pruneMs, string sessionToken)
        {
            bool retVal = false;

            if (true == browser.UsePuppeteer)
                retVal = browser.PuppeteerClient.CheckNetworkIdle(maxConnections).WithSessionToken(sessionToken).WithPruneMs(pruneMs).Execute<bool>();
            else if (true == browser.BrowserSettings.UseOttoMagic)
                retVal = true == browser.OttoMagicClient.WithEndpoint(ApiEndpoint.CheckNetworkIdle).WithMaxConnections(maxConnections).WithPruneMs(pruneMs).Execute()?.Contains("idle");
            else
                retVal = SeleniumNetworkIdle(browser, maxConnections, pruneMs, sessionToken);

            return retVal;
        }

        // The target info of the tab chromedriver is on, asked on chromedriver's own CDP session, which runs nothing in
        // the page
        private static Dictionary<string, object> CurrentTargetInfo(IWebDriver driver)
        {
            var current = ExecuteCdp(driver, "Target.getTargetInfo", new Dictionary<string, object>()) as Dictionary<string, object>;

            return current?["targetInfo"] as Dictionary<string, object>;
        }

        // The target id of the tab chromedriver is on
        private static string CurrentTargetId(IWebDriver driver)
        {
            return CurrentTargetInfo(driver)?["targetId"]?.ToString();
        }

        // Selenium has no engine to ask, so this looks at the page's monitor and publishes what it found.
        private static bool SeleniumNetworkIdle(Browser browser, int maxConnections, int pruneMs, string sessionToken)
        {
            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                supressedMessage = false;
                lastSessionToken = sessionToken;
            }

            string currentErrorMessage = $"Waiting for network to idle to [{maxConnections}] connections for [{pruneMs}] ms";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, browser, GPALObjectType.Browser);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }

            // Chrome and Edge count the tab's requests on GPAL's own connection to the browser, so an ask runs nothing
            // in the page. Firefox has no such connection and asks the page's monitor.
            if (BrowserType.FireFox != browser.BrowserSettings.BrowserType)
                browser.BrowserSettings.TabScripts ??= SeleniumTabScripts.Start(browser.BrowserSettings);

            bool retVal;

            if (null != browser.BrowserSettings.TabScripts)
                retVal = browser.BrowserSettings.TabScripts.CheckNetworkIdle(CurrentTargetId(browser.BrowserDriver), maxConnections, pruneMs);
            else
                // Idle when the page has been watched for pruneMs, is at or below maxConnections now, and has not
                // dropped from above maxConnections within the last pruneMs.
                retVal = Convert.ToBoolean(BrowserHelper.ExecuteJavaScriptObj(Browser.NetworkMonitorScript + $@"
                    const monitor = window.__gpalNetworkMonitor;
                    const windowStart = Date.now() - {pruneMs};
                    return monitor.since <= windowStart && monitor.inflight <= {maxConnections} && !(monitor.droppedBelow[{maxConnections + 1}] > windowStart);",
                    browser));

            currentErrorMessage = $"Network IS{(retVal ? "" : " NOT")} Idle. Status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, browser, GPALObjectType.Browser);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }
            return retVal;
        }
    }
}
