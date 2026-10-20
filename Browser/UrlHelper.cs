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
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.ExtendedProperties;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.Support.Extensions;
using static GenerallyPositive.Enums;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;

// TODO: add cookie object, create cookies using another fluent object as there are a lot of columns to create
// for now create exact matches, later we will designate pattern matching for domain, path, key and storename - wildcard characters will be required to prove intent
// if we see a wildcard char, the treat it as a pattern match....

// written by grok, modified by mbv
namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Static helper class that executes storage cleanup actions defined in a GPALUrl.
    /// Attempts to use CDP (Chrome DevTools Protocol) for pre-navigation cleanup when possible.
    /// Falls back to JavaScript execution after navigation if CDP is unavailable.
    /// Logs all operations and errors via GPAL.PublishSimpleEvent.
    /// Also has some helper methods:
    ///  AreEquivalent: compare two urls as being the same (www.google.com vs google.com)
    ///  CheckRobotsTxt: check a url to see if it's allowed by robots.txt and return the robots.txt - used by gpal, you do not need to call unless you create your own workflow
    ///  CheckRobotMetaTags: check a url to audit and output any Robot Meta tags - used by gpal, you do not need to call unless you create your own workflow
    /// </summary>
    public static class UrlHelper
    {
        static bool MatchesOrAny(this string filter, string value)
            => string.IsNullOrEmpty(filter) || filter == value;

        #region Storage Handling
        /*
        delete-storage/get-storage
  
            cookies - deletes all cookies
            cookies (domain) - deletes all cookies under the given domain
            cookies (domain, key) - deletes all cookies under the given domain with the name "key"
            cookies (domain, path) - deletes all cookies under the given domain AND path
            cookies (domain, key, path) - deletes all cookies under the given domain AND path with the name "key"
            *cookies (key) - deletes all cookies across all domains with the name "key"
            NOTE: There can be multiple cookies on a website with the same "name/key" where the only parameter that separates them is the path, hence its inclusion
 
 
            localStorage - deletes all localStorage
            localStorage (domain) - deletes all localStorage under the given domain
            localStorage (domain, key) - deletes all localStorage under the given domain with the name "key"
            *localStorage (key) - deletes all localStorage across all domains with the name "key"
 
            sessionStorage - deletes all sessionStorage
            sessionStorage (domain) - deletes all sessionStorage under the given domain
            sessionStorage (domain, key) - deletes all sessionStorage under the given domain with the name "key"
            sessionStorage (key) - deletes all sessionStorage across all domains with the name "key"
 
            cache - deletes all cache
            cache (domain) - deletes all cache under the given domain
            cache (domain, key) - deletes all cache under the given domain with the name "key"
            cache (key) - deletes all cache across all domains with the name "key"
 
            indexedDb - deletes all indexedDb entries across all domains (considered nuclear option, warned against doing)

            NOTE: cannot be done using simple API, must use chrome.debugger + Storage.clearDataForOrigin
                       This is the only mechanism that can wipe IndexedDB across arbitrary sites.
 
            Attach a debugger to each open tab
            For that tab’s origin, clear IndexedDB
            Repeat for every tab
            Repeat again as new tabs open
 
            indexedDb (domain) - deletes all indexedDb data under the given domain
            indexedDb (domain, path) - deletes database with the name "path" under the given domain
            indexedDb (domain, path, key) - deletes specific "key" object from the "path" database under the given domain
            NOTE: the parameter name path isn't quite accurate for indexedDb, its being used as a database-name here
         */

        // TODO: origin is different than domain, domain is the cookie domain like .youtube.com - we will match on the specifically
        //      origin is another filter to see whether that storage object applies to the origin
        /// <summary>
        /// Gives a storage read the same shape on every engine: the stored value for a keyed read, the key/value
        /// object for a wildcard read. An engine's wrapper around a keyed value is removed.
        /// <br/><br/>
        /// A stored JSON object is returned unchanged. It is a wrapper only when one key was read and the object
        /// holds only "value" and optionally "key".
        /// </summary>
        /// <param name="data">What the engine handed back.</param>
        /// <param name="key">The key that was asked for, or null/empty for a wildcard read.</param>
        /// <returns>The stored value for a keyed read, or the key/value object for a wildcard one.</returns>
        private static string UnwrapStorageRead(string data, string key)
        {
            string retVal = data;

            // Only unwrap keyed reads; unwrapping a wildcard read would drop all entries but one.
            if (false == string.IsNullOrEmpty(key) && false == string.IsNullOrWhiteSpace(data) && true == data.TrimStart().StartsWith("{"))
            {
                try
                {
                    Newtonsoft.Json.Linq.JObject wrapper = Newtonsoft.Json.Linq.JObject.Parse(data);

                    // A wrapper has "value" and nothing but "value" and "key". Any other object is stored data and
                    // is returned unchanged.
                    bool isWrapper = null != wrapper["value"]
                                  && wrapper.Properties().All(each => "value" == each.Name || "key" == each.Name);

                    if (true == isWrapper)
                    {
                        Newtonsoft.Json.Linq.JToken value = wrapper["value"];

                        // Return a stored string as is, anything else as JSON rather than Newtonsoft's ToString.
                        retVal = Newtonsoft.Json.Linq.JTokenType.String == value.Type
                            ? value.ToString()
                            : Newtonsoft.Json.JsonConvert.SerializeObject(value);
                    }
                }
                catch (Exception)
                {
                    // If the data cannot be parsed, return it unchanged.
                }
            }

            return retVal;
        }

        internal static bool TryPerformAction(IGPALUrl gpalUrl, BrowserSettings browserSettings, StorageAction storageActionParameters, out string data)
        {
            bool retVal = false;

            data = null;

            if (null == gpalUrl)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No GPALUrl supplied to run [{storageActionParameters.StorageType}] [{storageActionParameters.Action}] action", browserSettings, GPALObjectType.Other);
                return retVal;
            }
            else if (null == storageActionParameters)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No StorageAction parameters supplied to run on [{gpalUrl.Url}]", browserSettings, GPALObjectType.Other);
                return retVal;
            }

            string defaultOrigin = GetOrigin(gpalUrl.Url);
            IEnumerable<StorageAction> storageActions = null;

            // NOTE: null == wildcard, not set, choose all of that type
            if (null == storageActionParameters.Domain && null == storageActionParameters.Key && null == storageActionParameters.Path && null == storageActionParameters.StoreName
                && null == storageActionParameters.UserDefined)
            {
                if (WebsiteStorageType.notSet == storageActionParameters.StorageType) // run all
                    storageActions = gpalUrl.GetActions();
                else    // run all for specific storageType
                    storageActions = gpalUrl.GetActions().Where(a => storageActionParameters.StorageType == a.StorageType);
            }
            else
            {
                // refactored by Grok 4.20
                var p = storageActionParameters;

                storageActions = gpalUrl.GetActions().Where(a =>
                    a.StorageType == p.StorageType &&
                    p.Domain.MatchesOrAny(a.Domain) &&
                    p.Path.MatchesOrAny(a.Path) &&
                    p.Key.MatchesOrAny(a.Key) &&
                    p.StoreName.MatchesOrAny(a.StoreName) &&
                    p.UserDefined.MatchesOrAny(a.UserDefined)
                );
            }

            foreach (var action in storageActions)
            {
                string origin = action.Domain != null
                    ? MagicHelper.GetFullUrl(action.Domain, null, out bool _, true) // we just want the url, no robots.txt check
                    : defaultOrigin;

                var paramsDict = new Dictionary<string, object>();

                if (WebsiteStorageType.cookie == action.StorageType)
                    origin = UrlHelper.GetWildcardCookieDomain(origin);

                var details = new[]
                {
                    string.IsNullOrWhiteSpace(origin) ? null : $"d[{origin}]",
                    string.IsNullOrWhiteSpace(action.Path) ? null : $"p[{action.Path}]",
                    string.IsNullOrWhiteSpace(action.Key) ? null : $"k[{action.Key}]",
                    string.IsNullOrWhiteSpace(action.StoreName) ? null : $"sn[{action.StoreName}]"
                }
                .Where(s => s != null);

                string message = $"[{char.ToUpper(storageActionParameters.Action.ToString()[0])}{storageActionParameters.Action.ToString().Substring(1)}] [{action.StorageType}] for {string.Join(" ", details)}";
                SeleniumStorageHelper seleniumStorageHelper = null;

                if (true == browserSettings.UseSelenium)
                    seleniumStorageHelper = new SeleniumStorageHelper((Browser) browserSettings.Browser);

                if (WebsiteStorageAction.get == storageActionParameters.Action)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            data = browserSettings.MagicHelper.GetStorage(action.StorageType, origin, action.Path, action.Key, action.StoreName);
                        else if (true == browserSettings.UsePuppeteer)
                            data = browserSettings.PuppeteerClient.GetStorage(action.StorageType)
                                .WithStorageDomain(origin).WithStoragePath(action.Path).WithStorageKey(action.Key).WithStorageStoreName(action.StoreName)
                                .Execute();
                        else if (true == browserSettings.UseSelenium)
                        {
                            StorageAction sa = new StorageAction(action);   // preserve the original action for domain reuse
                            sa.Domain = origin; // use wildcards for cookie
                            data = seleniumStorageHelper.GetStorage(sa);
                        }

                        // every engine hands a stored value back encoded once as a json string, so one layer comes off
                        if (false == string.IsNullOrEmpty(data) && 1 < data.Length && data.StartsWith("\"") && data.EndsWith("\""))
                            data = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(data);

                        data = UnwrapStorageRead(data, action.Key);

                        GPAL.PublishSimpleEvent(GPALEventType.INFO,
                            message,
                            gpalUrl, GPALObjectType.Other);

                        retVal = true;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                            "FAILED:" + message,
                            gpalUrl, GPALObjectType.Other, ex);
                    }
                }
                else if (WebsiteStorageAction.delete == storageActionParameters.Action)
                {
                    // NOTE: all should return an int of number of deleted items?
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            retVal = browserSettings.MagicHelper.DeleteStorage(action.StorageType, action.DeleteAcrossOrigins, origin, action.Path, action.Key, action.StoreName);
                        else if (true == browserSettings.UsePuppeteer)
                            retVal = browserSettings.PuppeteerClient.DeleteStorage(action.StorageType)
                                .WithStorageDomain(origin).WithStoragePath(action.Path).WithStorageKey(action.Key).WithStorageStoreName(action.StoreName)
                                .Execute<bool>();
                        else if (true == browserSettings.UseSelenium)
                        {
                            StorageAction sa = new StorageAction(action);   // preserve the original action for domain reuse
                            sa.Domain = origin; // use wildcards for cookie
                            data = seleniumStorageHelper.DeleteStorage(sa).ToString();   // returns number of items deleted... maybe we return that as data for all?
                            retVal = false == "0".Equals(data);
                        }

                        GPAL.PublishSimpleEvent(GPALEventType.INFO,
                            message,
                            gpalUrl, GPALObjectType.Other);
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                            "FAILED:" + message,
                            gpalUrl, GPALObjectType.Other, ex);
                    }
                }
                else if (WebsiteStorageAction.set == storageActionParameters.Action)
                {
                    // NOTE: all should return bool for operation success
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            retVal = browserSettings.MagicHelper.SetStorage(action.StorageType, storageActionParameters.Data, origin, action.Path, action.Key, action.StoreName);
                        else if (true == browserSettings.UsePuppeteer)
                            retVal = browserSettings.PuppeteerClient.SetStorage(action.StorageType)
                                .WithStorageDomain(origin).WithStoragePath(action.Path).WithStorageKey(action.Key).WithStorageStoreName(action.StoreName).WithStorageData(storageActionParameters.Data)
                                .Execute<bool>();
                        else if (true == browserSettings.UseSelenium)
                        {
                            StorageAction sa = new StorageAction(action);   // preserve the original action for domain reuse
                            sa.Domain = origin; // use wildcards for cookie
                            sa.Data = storageActionParameters.Data;
                            retVal = seleniumStorageHelper.SetStorage(sa);
                        }

                        if (true == retVal)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                                message,
                                gpalUrl, GPALObjectType.Other);
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                                "FAILED:" + message,
                                gpalUrl, GPALObjectType.Other);

                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                            "FAILED:" + message,
                            gpalUrl, GPALObjectType.Other, ex);
                    }
                }
            }
            return retVal;
        }

        // ------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------

        /// <summary>
        /// Shortens a url for log messages: host without www, then the path, cut at 40 characters, no scheme or query
        /// string. Example:
        /// https://www.barnesandnoble.com/w/the-hunger-games-suzanne-collins/1100171585?ean=9780439023528 becomes
        /// barnesandnoble.com/w/the-hunger-games-su...
        /// </summary>
        /// <param name="url">The url to shorten</param>
        /// <returns>The short form, or the url as it was when it is not an absolute url</returns>
        internal static string ForMessage(string url)
        {
            string retVal = url;

            if (true == Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && false == string.IsNullOrEmpty(uri.Host))
            {
                string host = uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
                string shortUrl = host + uri.AbsolutePath.TrimEnd('/');

                retVal = 40 < shortUrl.Length ? shortUrl.Substring(0, 40) + "..." : shortUrl;
            }

            return retVal;
        }

        /// <summary>
        /// Extracts the origin (scheme + host) from a URL string.
        /// </summary>
        public static string GetOrigin(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
            return url; // fallback
        }
        /// <summary>
        /// Compares two urls by base only (scheme + host + path), ignoring the query string and fragment.
        /// </summary>
        public static bool SameBaseUrl(string url1, string url2)
        {
            if (Uri.TryCreate(url1, UriKind.Absolute, out var uri1) && Uri.TryCreate(url2, UriKind.Absolute, out var uri2))
                return string.Equals(uri1.GetLeftPart(UriPartial.Path), uri2.GetLeftPart(UriPartial.Path), StringComparison.OrdinalIgnoreCase);

            return false;
        }
        private static DomainParser _parser;

        private static DomainParser Parser
        {
            get
            {
                if (_parser == null)
                {
                    // the public suffix list, downloaded once per run and kept in memory, so nothing is written to
                    // disk. Built off the calling thread, since a form's UI thread waiting on it would deadlock
                    SimpleHttpRuleProvider rules = new SimpleHttpRuleProvider();

                    System.Threading.Tasks.Task.Run(() => rules.BuildAsync()).GetAwaiter().GetResult();

                    _parser = new DomainParser(rules);
                }
                return _parser;
            }
        }

        public static bool TryGetUri(string url, out Uri uri)
        {
            uri = null;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            url = url.Trim();

            // Add scheme if missing
            if (!url.Contains("://"))
                url = "https://" + url;

            return Uri.TryCreate(url, UriKind.Absolute, out uri);
        }

        /// <summary>
        /// Pass in a url, get a cookie 'domain' that will match it
        /// </summary>
        /// <param name="url">The url or host to find the cookie domain for. A leading dot is ignored.</param>
        /// <returns>The registrable domain with a leading dot, such as <c>.example.com</c>; the bare host when the public suffix list cannot resolve it; <c>null</c> for an empty url.</returns>
        public static string GetWildcardCookieDomain(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            url = url.Trim();

            if (url.StartsWith("."))
                url = url.Substring(1);

            if (!TryGetUri(url, out var uri))
                return url;

            try
            {
                var domainInfo = Parser.Parse(uri.Host);

                if (string.IsNullOrEmpty(domainInfo?.RegistrableDomain))
                    return uri.Host;

                return "." + domainInfo.RegistrableDomain;
            }
            catch
            {
                // Covers BOTH:
                // - Parser construction failure
                // - Parse() failure
                return uri.Host;
            }
        }
        /// <summary>
        /// Whether a cookie belongs to a domain, by the browser's rule. A host-only cookie (no leading dot) matches
        /// only its host; a domain cookie (leading dot) matches the domain and its subdomains.<br/>
        /// String equality would miss every host-only cookie, which session cookies almost always are.
        /// </summary>
        /// <param name="cookieDomain">The domain recorded on the cookie</param>
        /// <param name="domain">The domain being asked for, with or without the leading dot</param>
        /// <returns>True when the cookie belongs to that domain</returns>
        internal static bool CookieDomainMatches(string cookieDomain, string domain)
        {
            // No domain filter, or the cookie has no domain: keep it.
            bool retVal = true;

            if (false == string.IsNullOrWhiteSpace(domain) && false == string.IsNullOrWhiteSpace(cookieDomain))
            {
                string wanted = CookieHostOf(domain);
                string held = CookieHostOf(cookieDomain);

                retVal = true == held.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                      || true == held.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase);
            }

            return retVal;
        }

        /// <summary>
        /// Whether a cookie is sent to a host: the host is the cookie's domain or a subdomain of it.<br/>
        /// The reverse of <see cref="CookieDomainMatches"/>. A cookie on .example.com is sent to app.example.com.
        /// </summary>
        /// <param name="cookieDomain">The domain recorded on the cookie</param>
        /// <param name="host">The host the request is going to</param>
        /// <returns>True when the cookie belongs on a request to that host</returns>
        internal static bool CookieAppliesToHost(string cookieDomain, string host)
        {
            // The cookie has no domain or no host was given: keep it.
            bool retVal = true;

            if (false == string.IsNullOrWhiteSpace(cookieDomain) && false == string.IsNullOrWhiteSpace(host))
            {
                string bare = CookieHostOf(cookieDomain);
                string target = CookieHostOf(host);

                retVal = true == target.Equals(bare, StringComparison.OrdinalIgnoreCase)
                      || true == target.EndsWith("." + bare, StringComparison.OrdinalIgnoreCase);
            }

            return retVal;
        }

        /// <summary>
        /// The bare host out of a cookie domain, a url, or a domain already carrying its wildcard dot.
        /// </summary>
        /// <param name="value">What the caller named the domain with</param>
        /// <returns>The host, no leading dot and no scheme</returns>
        private static string CookieHostOf(string value)
        {
            string retVal = value.Trim().TrimStart('.');

            if (true == Uri.TryCreate(retVal, UriKind.Absolute, out Uri uri) && false == string.IsNullOrEmpty(uri.Host))
                retVal = uri.Host;

            return retVal;
        }
        #endregion Storage Handling
        #region ROBOTS.TXT and ROBOTS META TAG
        // written by ChatGPT
        // Cache per-host robots.txt
        private static readonly ConcurrentDictionary<string, string[]> RobotsCache =
            new ConcurrentDictionary<string, string[]>();

        // Cache per-URL final allow/deny decision
        private static readonly ConcurrentDictionary<string, bool> UrlDecisionCache =
            new ConcurrentDictionary<string, bool>();

        static bool inRobotsCheck = false;

        // File extensions that a browser can open (or download) but which are not HTML documents.
        // A robots meta tag can only live in the head of an HTML document, so looking for one on any
        // of these is a guaranteed miss and costs us a wait/timeout in the workflow.
        private static readonly HashSet<string> NonHtmlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // markup/data the browser renders or dumps as text
            ".xml", ".xsl", ".xslt", ".rss", ".atom", ".txt", ".json", ".jsonld", ".csv", ".tsv", ".yaml", ".yml", ".md",
            // documents
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".odt", ".ods", ".odp", ".epub",
            // images
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".svgz", ".ico", ".tif", ".tiff", ".avif", ".heic",
            // media
            ".mp3", ".wav", ".ogg", ".oga", ".flac", ".aac", ".m4a", ".mp4", ".m4v", ".webm", ".mov", ".avi", ".mkv", ".wmv", ".m3u8",
            // archives and binaries
            ".zip", ".gz", ".tgz", ".bz2", ".7z", ".rar", ".tar", ".exe", ".msi", ".dmg", ".apk", ".iso", ".bin",
            // code/assets served directly
            ".js", ".mjs", ".css", ".map", ".wasm",
            // fonts
            ".woff", ".woff2", ".ttf", ".otf", ".eot",
            // misc
            ".ics", ".vcf", ".sql", ".log"
        };

        /// <summary>
        /// Is this url pointing at a file the browser will open/download rather than an HTML page?
        /// Query strings and fragments are ignored, so /sitemap.xml?page=2 still counts as a file.
        /// </summary>
        /// <param name="url">The url to test</param>
        /// <returns>true = not an HTML document, skip any page level checks</returns>
        public static bool IsNonHtmlResource(string url)
        {
            if (true == string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
                string path = url;

                if (true == Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                {
                    // non http(s) schemes are never a page we can read meta tags from
                    if (false == uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                        false == uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                        return true;

                    path = uri.AbsolutePath;
                }
                else
                {
                    // strip query/fragment by hand when we were handed a partial url
                    int cut = path.IndexOfAny(new[] { '?', '#' });
                    if (0 <= cut)
                        path = path.Substring(0, cut);
                }

                string extension = System.IO.Path.GetExtension(path);

                if (true == string.IsNullOrWhiteSpace(extension))
                    return false;

                return NonHtmlExtensions.Contains(extension);
            }
            catch
            {
                // if we cannot parse it, treat it as a page and let the normal checks run
                return false;
            }
        }

        /// <summary>
        /// Check the robots.txt of a wehsite for access restrictions and sitemaps
        /// If httpclient is forbidden (403) then we will open robots.txt in a browser and read/parse from there
        /// </summary>
        /// <param name="url"></param>
        /// <param name="browser"></param>
        /// <param name="robotsTxt">Returns all lines of robots.txt into the List&lt;string&gt;</param>
        /// <returns>true = allowed, false = disallowed</returns>
        public static bool CheckRobotsTxt(string url, IBrowser browser, List<string> robotsTxt)
        {
            var uri = new Uri(url);
            string host = uri.Host;
            bool isAllowed = true;
            string[] lines;
            bool cachedResult = true;

            robotsTxt.Clear();

            if (true == inRobotsCheck)
            {
                if (UrlDecisionCache.TryGetValue(url, out cachedResult))
                {
                    RobotsCache.TryGetValue(host, out lines);
                    foreach (string line in lines)
                        robotsTxt.Add(line);

                    return cachedResult;
                }
                return true;
            }
            else
                // newtab and others will call getfullurl which will cause robots check recursion...
                inRobotsCheck = true;

            try
            {
                // Fast path: already processed URL
                if (UrlDecisionCache.TryGetValue(url, out cachedResult))
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.DEBUG,
                        $"Robots policy cache hit for [{url}] -> [{cachedResult}]");

                    RobotsCache.TryGetValue(host, out lines);
                    if (null != lines)
                        foreach (string line in lines)
                            robotsTxt.Add(line);

                    return cachedResult;
                }

                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $"Checking robots.txt for [{url}]");

                using (var client = new System.Net.Http.HttpClient())
                {
                    // Set User-Agent once
                    client.DefaultRequestHeaders.Add(
                        "User-Agent",
                        MagicHelper.GetUserAgentString(browser.BrowserType));

                    // -------------------------
                    // ROBOTS.TXT CHECK
                    // -------------------------

                    lines = RobotsCache.GetOrAdd(host, h =>
                    {
                        // use httpclient to get robots.txt - but we might be blocked
                        // backup is to get the robots.txt in the browser which will always work
                        try
                        {
                            var task = client.GetStringAsync($"{uri.Scheme}://{h}/robots.txt");

                            if (!task.Wait(TimeSpan.FromSeconds(10)))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{uri.Scheme}]://[{h}]/robots.txt request timed out after 10 seconds.");
                                throw new TimeoutException();
                            }

                            string robotsContent = task.Result;
                            return robotsContent
                                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .ToArray();
                        }
                        catch
                        {
                            // open robots.txt in a new tab, get page source and parse
                            string currentUrl = browser.CurrentUrl;
                            browser.NewTab($"{uri.Scheme}://{h}/robots.txt")/*.WaitFor(500)*/.GetPageSource(out string pageSource);
                            if (1 < ((Browser)browser).tabsWeOpened)
                                browser.CloseTab($"{uri.Scheme}://{h}/robots.txt");

                            ((Browser)browser).CurrentUrl = currentUrl;

                            if (true == string.IsNullOrEmpty(pageSource))
                            {
                                return Array.Empty<string>();
                            }
                            else
                            {
                                string content = pageSource;

                                // Check if the content is wrapped in HTML with a <pre> tag and extract the inner text if so
                                if (content.Contains("<pre"))
                                {
                                    int preStart = content.IndexOf("<pre", StringComparison.OrdinalIgnoreCase);
                                    if (preStart >= 0)
                                    {
                                        int start = content.IndexOf(">", preStart) + 1;
                                        int end = content.LastIndexOf("</pre>", StringComparison.OrdinalIgnoreCase);
                                        if (start > 0 && end > start)
                                        {
                                            content = content.Substring(start, end - start);
                                        }
                                    }
                                }

                                return content
                                    .Replace(@"\n", "\n").Replace(@"\r", "\r")
                                    .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(l => l.Trim())
                                    .ToArray();
                            }
                        }
                    });

                    bool userAgentAll = false;
                    var disallowedPaths = new System.Collections.Generic.List<string>();

                    if (null != lines)
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
                            {
                                userAgentAll = line.Substring(11).Trim() == "*";
                            }
                            else if (userAgentAll &&
                                     line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                            {
                                string path = line.Substring(9).Trim();

                                if (!string.IsNullOrEmpty(path))
                                    disallowedPaths.Add(path);
                            }

                            GPAL.PublishSimpleEvent(
                                GPALEventType.DEBUG,
                                $"Processed robots.txt line [{line}]");

                            robotsTxt.Add(line);
                        }

                    string relativePath = uri.PathAndQuery;

                    foreach (var rule in disallowedPaths)
                    {
                        if (relativePath.StartsWith(rule, StringComparison.OrdinalIgnoreCase))
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.WARNING,
                                $"ACCESS DENIED by robots.txt [Disallow: [{rule}]]"
                            );

                            isAllowed = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.WARNING,
                    $"Error checking robots.txt for [{url}]"
                , null, GPALObjectType.None, ex);

                isAllowed = true;
            }
            finally
            {
                inRobotsCheck = false;
            }

            // Store final decision
            UrlDecisionCache[url] = isAllowed;

            return isAllowed;
        }

        // Cache we maintain to respect URL once per directive
        private static System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _metaTagAuditCache;

        // Track last domain visited to clear cache when domain changes
        // NOTE: CAVEAT: this is problematic, do it every time until we understand why it is problematic
        private static string _lastDomain;

        /// <summary>
        /// Checks the robot meta tags on the page we are visiting
        /// But it's not really clear these apply to us, but some roboticists claim
        /// reading a page then reading the tags regarding search display or link following, somehow means 'do not scrape this page'
        /// but one has to scrape the page to read those, so they are technically not directive to not scrape, just don't display in search or follow links (various reasons why)
        /// but robots.txt really is the source for dis/allow.
        /// NOTE: CAVEAT: this incurs a wait in the workflow if the page does not have robot tags and times out (affects ottomagic)
        /// </summary>
        /// <param name="url"></param>
        /// <param name="browser"></param>
        /// <returns>true - always - this is an audit method</returns>
        [InProgress("Output website robot meta tags. Will respect .WithRespectRobotMetaTags(true) on noindex/nofollow/none. Ottomagic cannot retrived head.")]
        public static bool CheckRobotMetaTags(GPALUrl url, Browser browser)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Searching for robots meta tag");

            // specifically exclude blank pages and anything that is not an HTML document
            // (robots.txt, sitemap.xml, pdfs, images, downloads - none of them can carry a robots meta tag)
            if (true == url?.Url.Equals("about:blank") || true == IsNonHtmlResource(url?.Url))
                return true;

            bool isAllowed = true;

            var uri = new Uri(url.Url);
            string currentDomain = uri.Host;
            _metaTagAuditCache ??= new System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>();

            //Clear cache if domain changed
            if (false == UrlHelper.AreEquivalent(_lastDomain, currentDomain))
            {
                _metaTagAuditCache?.Clear();
                _lastDomain = currentDomain;
            }

            HashSet<string> loggedDirectives = _metaTagAuditCache.GetOrAdd(url.Url, _ => new HashSet<string>());

            if (0 == loggedDirectives.Count)
                // Only if browser given, do we proceed
                if (null != browser.Process || 0 != browser.BrowserSettings.ServiceDriverPid)
                {
                    try
                    {
                        string content = string.Empty;
                        int retries = 3;

                        if (true == browser.UseOttoMagic)
                        {
                            // head is in content 0
                            List<GPALElement> theHead = browser.MagicHelper.QuerySelectors("meta[name='robots']");

                            //string pageSource = string.Empty;
                            //browser.GetPageSource(out pageSource);

                            if (null != theHead)
                                foreach (GPALElement element in theHead)
                                {
                                    // KLUDGE: using this command on an element, it expects the browser, but magic doesn't add that, so we will
                                    element.Browser = browser;
                                    content = element.GetAttribute("content");
                                }
                        }
                        else
                        {
                            bool withReturn = false == browser.UsePuppeteer;

                            do
                            {
                                try
                                {
                                    content = BrowserHelper.ExecuteJavaScriptObj(
                                        $@"var div = document.querySelector(""meta[name='robots']""); {(true == withReturn ? "return " : "")} div?.getAttribute('content');",
                                        browser
                                    )?.ToString();

                                    break;
                                }
                                catch
                                {
                                    // Too fast we were? Retry, we shall
                                }
                            } while (0 < retries--);
                        }

                        if (null == content)
                            return true;

                        var directives = content.Split(',')
                                                .Select(d => d.Trim())
                                                .ToList();

                        foreach (var directive in directives)
                        {
                            //if (!loggedDirectives.Contains(directive))
                            {
                                if (directive == "noindex" || directive == "nofollow" || directive == "none" || directive == "index" || directive == "follow")
                                {
                                    loggedDirectives.Add(directive);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // error means default to isAllowed
                        isAllowed = true;
                    }
                }

            foreach (string directive in loggedDirectives)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Robots meta tag [{directive}]");

            isAllowed = (true == browser.BrowserSettings.RespectRobotMetaTags && (true == loggedDirectives.Contains("noindex") || true == loggedDirectives.Contains("nofollow") || true == loggedDirectives.Contains("none"))) ? false : true;

            return isAllowed;
        }
        #endregion ROBOTS.TXT and ROBOTS META TAG
        #region Helpers
        /// <summary>
        /// Checks if two URLs are equivalent after normalization
        /// (ignores www/non-www, trailing slashes, default ports, case on domain)
        /// </summary>
        public static bool AreEquivalent(string url1, string url2, bool treatHttpHttpsAsSame = true)
        {
            if (string.IsNullOrWhiteSpace(url1) || string.IsNullOrWhiteSpace(url2))
                return url1 == url2;

            // Quick exact match escape
            if (string.Equals(url1, url2, StringComparison.OrdinalIgnoreCase))
                return true;

            try
            {
                var uri1 = new Uri(url1, UriKind.Absolute);
                var uri2 = new Uri(url2, UriKind.Absolute);

                // Protocol equivalence
                if (treatHttpHttpsAsSame)
                {
                    string scheme1 = uri1.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "https" : uri1.Scheme;
                    string scheme2 = uri2.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "https" : uri2.Scheme;
                    if (scheme1 != scheme2)
                        return false;
                }
                else if (!string.Equals(uri1.Scheme, uri2.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Host equivalence: ignore www prefix and case
                string host1 = NormalizeHost(uri1.Host);
                string host2 = NormalizeHost(uri2.Host);
                if (host1 != host2)
                    return false;

                // Path equivalence: ignore trailing slash
                string path1 = NormalizePath(uri1.AbsolutePath);
                string path2 = NormalizePath(uri2.AbsolutePath);
                if (path1 != path2)
                    return false;

                // Query string: case-sensitive and order-sensitive (most conservative)
                if (uri1.Query != uri2.Query)
                    return false;

                // Fragment usually ignored for equivalence
                // (if you want to consider it, compare uri1.Fragment == uri2.Fragment)

                return true;
            }
            catch (UriFormatException)
            {
                // If either isn't a valid absolute URI, fall back to string compare
                return string.Equals(url1.TrimEnd('/'), url2.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static string GetNormalizedDomain(string url)
        {
            Uri uri = new Uri(url);
            string currentDomain = uri.Host;
            return NormalizeHost(currentDomain);
        }

        private static string NormalizeHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return host;

            host = host.TrimEnd('.').ToLowerInvariant();

            // Remove leading www. (and www2., www3., etc.)
            if (host.StartsWith("www", StringComparison.OrdinalIgnoreCase))
            {
                int dotIndex = host.IndexOf('.');
                if (dotIndex > 0 && dotIndex < 6) // www., www2., www10., etc.
                {
                    host = host.Substring(dotIndex + 1);
                }
            }

            return host;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";

            path = path.ToLowerInvariant(); // path case usually matters, but for sitemap equivalence often not

            // Remove trailing slash unless it's just "/"
            if (path.Length > 1 && path.EndsWith("/"))
                path = path.TrimEnd('/');

            return path;
        }
        #endregion Helpers
    }
}
