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
        /// Gets the raw XML/source of the current page, intended for use when the current page is itself a sitemap.
        /// </summary>
        /// <param name="sitemapXML">Receives the page source</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetSiteMap(out string sitemapXML)
        {
            GetPageSource(out sitemapXML);
            return this;
        }

        Dictionary<string, List<string>> siteMaps = new Dictionary<string, List<string>>();

        /// <summary>
        /// The sitemap text read by the last <see cref="GetSiteMapUrls"/>, to tell an index from a urlset or an html
        /// page without reloading.<br/>
        /// Empty when served from cache; only urls are cached, since sitemaps can be megabytes.
        /// </summary>
        public string SiteMapSource { get; private set; } = string.Empty;

        // Why a sitemap walk found no urls, keyed like the sitemap cache. SaveSiteMapUrls writes it into the file.
        private readonly Dictionary<string, SitemapNote> _sitemapNotes = new Dictionary<string, SitemapNote>(StringComparer.OrdinalIgnoreCase);

        // Why the last walk found no urls; null when it found some.
        private SitemapNote _lastWalkNote = null;

        // Result code and message written when no urls were found.
        private class SitemapNote
        {
            internal string Result { get; set; }
            internal string Message { get; set; }
        }

        /// <summary>
        /// Clears the cached sitemap / LLM digest / hydration results for this browser, forcing the next
        /// Get*/Save* to re-walk or re-fetch instead of reusing an earlier result.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearResultCache()
        {
            _sitemapCache.Clear();
            _sitemapNotes.Clear();
            _digestCache.Clear();
            _hydrationCache.Clear();
            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Cleared sitemap/digest/hydration result cache.", this, GPALObjectType.Browser);
            return this;
        }

        // The sitemap WithSitemap named for the next GetSiteMapUrls or SaveSiteMapUrls; null reads from the current page
        private string _namedSitemap = null;

        /// <summary>
        /// Names the sitemap the next <see cref="GetSiteMapUrls"/> or <see cref="SaveSiteMapUrls"/> reads, xml or
        /// html. It is read without going to it, so a gzipped sitemap, which the browser would only download, is
        /// fetched and inflated. Used for walking from one sitemap to the next; to start from a page, GoTo it and
        /// leave this out.<br/><br/>
        /// A sitemap the browser has to go to (one on another origin, or an html sitemap built by script) leaves the
        /// browser on it. GoTo the page again when the walk is done.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.WithSitemap("https://www.walmart.com/sitemap_store_main_other.xml.gz")
        ///            .GetSiteMapUrls(out List&lt;string&gt; urls);
        /// </code>
        /// </example>
        /// <param name="sitemapUrl">The sitemap to read</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithSitemap(GPALUrl sitemapUrl)
        {
            _namedSitemap = sitemapUrl?.Url;
            return this;
        }

        /// <summary>
        /// Returns the urls one sitemap lists, child sitemaps and pages together. The sitemap is the one
        /// <see cref="WithSitemap"/> named; without it, the page the browser is on when that page is a sitemap,
        /// otherwise the sitemaps the site's robots.txt lists, otherwise its /sitemap.xml. The browser is back on
        /// the page it started on afterwards.
        /// </summary>
        /// <param name="sitemapUrls">Receives the urls the sitemap lists</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetSiteMapUrls(out List<string> sitemapUrls)
        {
            sitemapUrls = CachedSitemapUrls(_namedSitemap);

            // a name applies to one read
            _namedSitemap = null;

            return this;
        }

        /// <summary>
        /// Returns the sitemap URLs for the current origin, walking the site only on a cache miss so repeated
        /// calls (e.g. GetSiteMapUrls followed by SaveSiteMapUrls) do not re-crawl.
        /// </summary>
        private List<string> CachedSitemapUrls(string sitemapUrl = null)
        {
            // Cache by sitemap url (or origin when none is given), since a site has many sitemaps.
            string key = false == string.IsNullOrWhiteSpace(sitemapUrl) ? sitemapUrl : UrlHelper.GetOrigin(CurrentUrl);

            if (false == string.IsNullOrEmpty(key) && true == _sitemapCache.TryGetValue(key, out List<string> cached))
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Sitemap cache hit for [{key}] ([{cached.Count}] URLs).", this, GPALObjectType.Browser);

                // Restore the saved reason for an empty cached result too.
                _sitemapNotes.TryGetValue(key, out _lastWalkNote);

                // nothing was read, so there is no text to answer with
                SiteMapSource = string.Empty;

                return cached;
            }

            List<string> walked = WalkSitemapUrls(sitemapUrl);

            if (false == string.IsNullOrEmpty(key))
            {
                _sitemapCache[key] = walked;

                if (null == _lastWalkNote)
                    _sitemapNotes.Remove(key);
                else
                    _sitemapNotes[key] = _lastWalkNote;
            }

            return walked;
        }

        /// <summary>
        /// Reads a sitemap with a fetch from inside the page, falling back to navigating.<br/>
        /// A fetch is one request instead of a load, render and source read, and carries the browser's cookies,
        /// clearance and TLS.<br/>
        /// Navigation is used when the fetch cannot be made (a sitemap on another origin) or the result is not a
        /// sitemap (an html sitemap built by script).
        /// </summary>
        /// <param name="url">The sitemap to read</param>
        /// <returns>The sitemap text, or what the browser rendered when the fetch could not deliver</returns>
        private string ReadSitemap(string url)
        {
            string retVal = string.Empty;

            // Already on this sitemap: read the current page source. OttoMagic cannot fetch from an xml document,
            // and navigating would reload it.
            bool alreadyThere = true == UrlHelper.AreEquivalent(CurrentUrl, url);

            // Fetch only same-origin sitemaps; the browser blocks others, which would only add a failure before
            // navigating. Compare origins exactly, not with UrlHelper.AreEquivalent: www.harrods.com and
            // harrods.com differ (Harrods lists 422 sitemaps on harrods.com from www.harrods.com).
            // OttoMagic also cannot fetch while showing a rendered document such as robots.txt or a sitemap, where
            // no content script runs ("Failed to fetch"). Selenium and Puppeteer fetch in the document itself and
            // lack this limit.
            string onNow = (CurrentUrl ?? string.Empty).Split('?', '#')[0].TrimEnd('/').ToLower();
            bool onADocument = true == UseOttoMagic
                            && (true == onNow.EndsWith(".xml") || true == onNow.EndsWith(".txt") || true == onNow.EndsWith(".gz"));

            bool sameOrigin = false == alreadyThere
                           && false == onADocument
                           && true == string.Equals(UrlHelper.GetOrigin(CurrentUrl), UrlHelper.GetOrigin(url), StringComparison.OrdinalIgnoreCase);

            byte[] fetched = true == sameOrigin
                           ? BrowserHelper.FetchBytes(this, url, null, out string _)
                           : null;

            // The fetch sets ServerResponseCode like a navigation. Use the body only on 2xx; anything else is a
            // challenge or error page.
            bool answered = true == sameOrigin && 200 <= ServerResponseCode && 300 > ServerResponseCode;

            if (true == answered && null != fetched && 0 < fetched.Length)
                retVal = System.Text.Encoding.UTF8.GetString(fetched);

            // Check for <urlset or <sitemapindex first, so a large sitemap is not parsed twice. The heuristic is
            // for html sitemaps.
            bool looksLikeASitemap = false == string.IsNullOrWhiteSpace(retVal)
                                  && (true == retVal.Contains("<urlset")
                                      || true == retVal.Contains("<sitemapindex")
                                      || true == SitemapExtractor.IsValidSitemapContent(retVal));

            // Use the fetched text only if it looks like a sitemap; otherwise (error, challenge page, script-built
            // html sitemap) navigate and read the page source.
            if (true == alreadyThere)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The browser is already on [{url}], so its page is the sitemap", this, GPALObjectType.Browser);

                GetPageSource(out retVal);
            }
            else if (false == looksLikeASitemap)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, true == sameOrigin
                                                           ? $"The page did not hand over [{url}], HTTP [{ServerResponseCode}], so the browser goes there to read it"
                                                           : true == onADocument
                                                             ? $"The browser is on [{CurrentUrl}], a document rather than a page, which the extension cannot fetch from, so it goes to [{url}] instead"
                                                             : $"[{url}] is not on the origin the page is on, which a page may not fetch, so the browser goes there to read it",
                                        this, GPALObjectType.Browser);

                GoTo(url);
                GetPageSource(out retVal);
            }

            return retVal;
        }

        private List<string> WalkSitemapUrls(string sitemapUrl = null)
        {
            // Save the current url to return to. A named sitemap is read without visiting, and navigating to a .gz
            // aborts.
            string startedOn = CurrentUrl;

            bool named = false == string.IsNullOrWhiteSpace(sitemapUrl);

            // what the walk is about, for its messages: the named sitemap, or the page the browser is on
            string currentUrl = true == named ? sitemapUrl : startedOn;
            SitemapExtractor sme = new SitemapExtractor(this);
            IGPALGrid<string> retGrid = new GPALGrid<string>();
            List<string> potentialSitemapUrls = new List<string>();
            string pageSource = string.Empty;

            // a sitemap was read, even if it listed nothing
            bool readASitemap = false;

            _lastWalkNote = null;

            if (true == named)
                potentialSitemapUrls.Add(sitemapUrl);
            else
            {
                // The page the browser is on comes first. Only when it is not a sitemap is robots.txt asked, and
                // sitemap.xml after that.
                pageSource = ReadSitemap(startedOn);

                if (true == SitemapExtractor.IsValidSitemapContent(pageSource))
                {
                    retGrid.AddRow(sme.ExtractSitemapUrls(pageSource, Int32.MaxValue, out bool pageIsSitemap));
                    readASitemap = pageIsSitemap;
                }
                else
                    potentialSitemapUrls = sme.GetSitemapUrl(startedOn, out bool _);
            }

            foreach (string potentialSitemapUrl in potentialSitemapUrls)
            {
                // A gzipped sitemap is a binary download to the browser, so it is fetched and inflated instead
                pageSource = true == SitemapExtractor.IsGzipSitemapUrl(potentialSitemapUrl)
                           ? SitemapExtractor.FetchAndInflateGzipSitemap(potentialSitemapUrl, this)
                           : ReadSitemap(potentialSitemapUrl);

                // A bot-check interstitial (e.g. PerimeterX "Robot or human?") answered instead of the sitemap, so the
                // walk stops rather than reading the challenge page as one.
                if (true == SitemapExtractor.IsBotCheckPage(pageSource))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Bot-check interstitial detected at [{potentialSitemapUrl}], skipping sitemap walk.", this, GPALObjectType.Browser);

                    _lastWalkNote = new SitemapNote
                    {
                        Result = "bot-check-page",
                        Message = $"A bot-check interstitial answered for [{potentialSitemapUrl}] instead of a sitemap, so the walk stopped there and no urls were read. Run this one on a signed-in profile, or read the sitemap the site publishes by name."
                    };

                    break;
                }

                retGrid.AddRow(sme.ExtractSitemapUrls(pageSource, Int32.MaxValue, out bool wasSitemap));   // Int32.MaxValue reads every url in the sitemap
                readASitemap |= wasSitemap;
            }

            // A read from the page goes back to the page when it had to go to a sitemap. A named sitemap stays where
            // it was read, so a walk from one sitemap to the next goes back once, when it is done, not after every
            // sitemap.
            if (false == named)
            {
                string onNow = true == UseOttoMagic ? MagicHelper.GetCurrentUrl()
                             : true == BrowserSettings.UsePuppeteer ? PuppeteerClient.GetCurrentUrl().Execute()
                             : BrowserDriver.Url;

                if (false == UrlHelper.AreEquivalent(startedOn, onNow.Trim('\"')))
                    GoTo(startedOn);
            }

            List<string> sitemapUrls = retGrid.ToList();

            // Save the text in SiteMapSource, so the caller can tell the sitemap type without reloading.
            SiteMapSource = pageSource ?? string.Empty;

            // A sitemap that lists nothing is an answer, not a failure: said as INFO, and the reason kept for
            // SaveSiteMapUrls. Skipped if the bot-check set a reason.
            if (0 == sitemapUrls.Count && null == _lastWalkNote && true == readASitemap)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{currentUrl}] is a sitemap that lists no urls", this, GPALObjectType.Browser);

                _lastWalkNote = new SitemapNote
                {
                    Result = "empty-sitemap",
                    Message = $"[{currentUrl}] is a sitemap but lists no urls."
                };
            }

            // No urls and no sitemap read: warn with the url and sitemaps tried, and save the reason for
            // SaveSiteMapUrls. Skipped if the bot-check set a reason.
            if (0 == sitemapUrls.Count && null == _lastWalkNote)
            {
                string tried = true == named
                             ? sitemapUrl
                             : 0 == potentialSitemapUrls.Count
                               ? "nothing: the page is not a sitemap and robots.txt does not allow reading the site"
                               : $"the page, then {string.Join(", ", potentialSitemapUrls)}";

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No sitemap URLs found for [{currentUrl}], tried [{tried}]", this, GPALObjectType.Browser);

                _lastWalkNote = new SitemapNote
                {
                    Result = "no-sitemap-found",
                    Message = $"No sitemap was found for [{currentUrl}], so no urls were walked. Tried [{tried}]."
                };
            }

            return sitemapUrls;
        }
        /// <summary>
        /// Discovers and walks the site's sitemap(s), then saves the extracted URLs to a file. The output format
        /// (csv, txt, json, yaml, xml, ...) is chosen from the file extension by the converter, so one call turns
        /// a crawl into whatever downstream format you need.
        /// </summary>
        /// <param name="sitemapUrlsFile">The <see cref="GPALFile"/> to save the sitemap URLs to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveSiteMapUrls(GPALFile sitemapUrlsFile)
        {
            List<string> sitemapUrls = CachedSitemapUrls(_namedSitemap);

            // a name applies to one read
            _namedSitemap = null;

            // one URL per row, single "url" column, so the converter can emit any format from the file extension
            IGPALGrid<string> grid = new GPALGrid<string>();

            if (0 == sitemapUrls.Count)
            {
                // No urls: write a result entry with the reason instead of an empty file, in the format the file
                // extension selects.
                SitemapNote note = _lastWalkNote ?? new SitemapNote
                {
                    Result = "no-sitemap-found",
                    Message = $"No sitemap was found for [{UrlHelper.GetOrigin(CurrentUrl)}], so there are no urls to write."
                };

                grid.AddRow(new List<string> { note.Result, note.Message });

                GPAL.Converter
                    .WithInput(grid)
                    .WithColumnName("result")
                    .WithColumnName("message")
                    .SaveTo(sitemapUrlsFile);

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No sitemap URLs to save, so [{sitemapUrlsFile.Filename}] says [{note.Result}] instead", this, GPALObjectType.Browser);
            }
            else
            {
                foreach (string url in sitemapUrls)
                    grid.AddRow(new List<string> { url });

                GPAL.Converter
                    .WithInput(grid)
                    .WithColumnName("url")
                    .SaveTo(sitemapUrlsFile);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved [{sitemapUrls.Count}] sitemap URLs to [{sitemapUrlsFile.Filename}]", this, GPALObjectType.Browser);
            }

            return this;
        }
    }
}
