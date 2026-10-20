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
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using GenerallyPositive;
using static GenerallyPositive.Enums;

// wirtten by grok, updated by mbv
namespace GenerallyPositive.Browser
{
    internal class SitemapExtractor
    {
        private readonly Browser browser;

        internal SitemapExtractor(Browser browser)
        {
            this.browser = browser ?? throw new ArgumentNullException(nameof(browser));
        }

        /// <summary>
        /// True if <paramref name="url"/> points at a gzipped sitemap (ends in .gz, ignoring any query string).
        /// These are binary and must be downloaded + inflated rather than opened in the browser.
        /// </summary>
        internal static bool IsGzipSitemapUrl(string url)
        {
            if (true == string.IsNullOrWhiteSpace(url))
                return false;

            string path = url.Split('?', '#')[0].TrimEnd('/');
            return path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Asks the browser for a gzipped sitemap and inflates it to XML text. Large sites serve their sitemaps as
        /// .xml.gz; navigating the browser to one just triggers a binary download that hangs the WebDriver session
        /// (a 60s /url timeout in Selenium), so the bytes are fetched and decompressed instead.<br/>
        /// The fetch is made from inside the page, so it carries the browser's cookies, clearance and TLS, which a
        /// connection of our own does not have. Our own connection is the fallback, for the cross-origin case the
        /// page cannot fetch. If the bytes are not actually gzip (server already decompressed on the wire), they
        /// are returned as-is. Returns empty string on failure.
        /// </summary>
        internal static string FetchAndInflateGzipSitemap(string url, IBrowser browser)
        {
            try
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Fetching gzip sitemap [{url}]", typeof(SitemapExtractor), GPALObjectType.Other);

                // A page can fetch only its own origin, and gzip sitemaps are often elsewhere (bestbuy.com uses
                // sitemaps.bestbuy.com). Fetch from the page only when the origins match; otherwise it would fail,
                // log an error and fall through anyway.
                bool sameOrigin = string.Equals(UrlHelper.GetOrigin(browser.CurrentUrl), UrlHelper.GetOrigin(url), StringComparison.OrdinalIgnoreCase);

                byte[] data = true == sameOrigin
                            ? BrowserHelper.FetchBytes((Browser)browser, url, null, out string _)
                            : null;

                // Page fetch skipped or failed, so use our own HttpClient, which lacks the browser's session.
                if (null == data || 0 == data.Length)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The page did not hand over [{url}], asking for it on our own connection instead", typeof(SitemapExtractor), GPALObjectType.Other);

                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(60);
                        client.DefaultRequestHeaders.Add("User-Agent", MagicHelper.GetUserAgentString(browser.BrowserType));
                        data = client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                    }
                }

                if (null == data || 0 == data.Length)
                    return string.Empty;

                // gzip magic bytes: 0x1f 0x8b. If absent the payload is already plain (some servers inflate on the wire).
                bool isGzip = 2 <= data.Length && 0x1f == data[0] && 0x8b == data[1];

                if (false == isGzip)
                    return System.Text.Encoding.UTF8.GetString(data);

                using (var input = new MemoryStream(data))
                using (var gz = new GZipStream(input, CompressionMode.Decompress))
                using (var reader = new StreamReader(gz, System.Text.Encoding.UTF8))
                {
                    string xml = reader.ReadToEnd();
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Inflated gzip sitemap [{url}] [{data.Length}] -> [{xml.Length}] bytes", typeof(SitemapExtractor), GPALObjectType.Other);
                    return xml;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to fetch/inflate gzip sitemap [{url}]", typeof(SitemapExtractor), GPALObjectType.Other, ex);
                return string.Empty;
            }
        }

        // Public method: Extract URLs from already-fetched sitemap XML string
        // Caller must navigate to the URL and provide PageSource
        public List<string> ExtractSitemapUrls(
            string sitemapXmlContent,
            int limit,
            out bool urlIsSitemap)
        {
            if (string.IsNullOrWhiteSpace(sitemapXmlContent))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.WARNING,
                    "ExtractSitemapUrls called with empty/null content. Returning empty list.",
                    typeof(SitemapExtractor),
                    GPALObjectType.Other);

                urlIsSitemap = false;
                return new List<string>();
            }

            var allUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (IsValidSitemapContent(sitemapXmlContent))
            {
                urlIsSitemap = true;
                // Prefer XML path if possible
                if (sitemapXmlContent.Contains("<sitemapindex") || sitemapXmlContent.Contains("<urlset"))
                {
                    CollectUrlsFromXml(sitemapXmlContent, allUrls, limit);
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Treating [{browser.CurrentUrl}] html page as sitemap.", sitemapXmlContent, GPALObjectType.Other);
                    // HTML path
                    CollectUrlsFromHtml(sitemapXmlContent, allUrls, limit);
                }
            }
            else
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.WARNING,
                    "Content does not appear to be a valid sitemap (XML or HTML).",
                    sitemapXmlContent,
                    GPALObjectType.Other);

                urlIsSitemap = false;
            }

            return allUrls.Take(limit).ToList();
        }
        
        // Public method: Extract URLs recursively from initial sitemap XML string
        // (if it's an index, caller can call this repeatedly on sub-sitemaps)
        internal void CollectUrlsFromXml(
            string xmlContent,
            HashSet<string> collected,
            int limit)
        {
            if (collected.Count >= limit || string.IsNullOrWhiteSpace(xmlContent))
                return;

            string cleaned = xmlContent
                // Fix the most common literal escapes from browser/devtools copies
                .Replace("\\\"", "\"")     // \"  > "
                .Replace("\\\'", "'")      // \'  > '
                .Replace("\\n", "\n")      // \n  > actual newline
                .Replace("\\t", "\t")      // \t  > tab
                .Replace("\\r", "\r")      // \r  > carriage return
                .Replace("\\\\", "\\")     // \\  > single backslash (if doubled)
                                           // Optional: remove any leading/trailing control characters or BOM
                .Trim()
                .TrimStart('\uFEFF', '\u200B', '\u200C', '\u200D');

            // Step 1: Detect and extract real XML content if wrapped in browser viewer HTML
            // This handles both <div id="webkit-xml-viewer-source-xml">...</div> and loose <sitemapindex> inside
            var xmlStartMarkers = new[] { "<sitemapindex", "<urlset", "<?xml" };
            int xmlStart = -1;

            foreach (var marker in xmlStartMarkers)
            {
                xmlStart = cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (xmlStart >= 0) break;
            }

            if (xmlStart >= 0)
            {
                // Cut off everything before the actual XML starts
                cleaned = cleaned.Substring(xmlStart);


                // Also try to cut off trailing HTML (after </sitemapindex> or </urlset>)
                int xmlEnd = cleaned.LastIndexOf("</sitemapindex>", StringComparison.OrdinalIgnoreCase);
                if (xmlEnd < 0)
                    xmlEnd = cleaned.LastIndexOf("</urlset>", StringComparison.OrdinalIgnoreCase);

                if (xmlEnd >= 0)
                {
                    try
                    {
                        cleaned = cleaned.Substring(0, xmlEnd + "</sitemapindex>".Length);
                    }
                    catch
                    {
                        cleaned = cleaned.Substring(0, xmlEnd + "</urlset>".Length);
                    }
                }
            }

            // Step 2: Try to parse the (possibly cleaned) content as XML
            XDocument doc;
            try
            {
                doc = XDocument.Parse(cleaned);
            }
            catch (XmlException)
            {
                // A sitemap that breaks off before its end (served unfinished, or cut off in transit) still lists the
                // entries before the break, so those are read.
                int before = collected.Count;

                CollectLocsUntilBroken(cleaned, collected, limit);

                GPAL.PublishSimpleEvent(GPALEventType.INFO,
                    $"The sitemap breaks off before its end; [{collected.Count - before}] entries before the break were read",
                    typeof(SitemapExtractor), GPALObjectType.Other);

                return;
            }

            // Step 3: Proceed with normal extraction (unchanged)
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            XNamespace emptyNs = "";

            // Sitemap index?
            var sitemapElements = doc.Descendants(ns + "sitemap")
                                     .Concat(doc.Descendants(emptyNs + "sitemap"))
                                     .ToList();

            if (sitemapElements.Count > 0)
            {
                foreach (var sitemap in sitemapElements)
                {
                    if (collected.Count >= limit) return;

                    string loc = (sitemap.Element(ns + "loc") ?? sitemap.Element(emptyNs + "loc"))?.Value?.Trim();

                    if (!string.IsNullOrWhiteSpace(loc) && Uri.IsWellFormedUriString(loc, UriKind.Absolute))
                    {
                        collected.Add(loc);
                    }
                }
                return;
            }

            // Regular urlset
            var urlElements = doc.Descendants(ns + "url")
                                 .Concat(doc.Descendants(emptyNs + "url"))
                                 .ToList();

            foreach (var urlElem in urlElements)
            {
                if (collected.Count >= limit) return;

                string loc = (urlElem.Element(ns + "loc") ?? urlElem.Element(emptyNs + "loc"))?.Value?.Trim();

                if (!string.IsNullOrWhiteSpace(loc) && Uri.IsWellFormedUriString(loc, UriKind.Absolute))
                {
                    collected.Add(loc);
                }
            }
        }

        /// <summary>
        /// Reads the sitemap entries (&lt;loc&gt; in the sitemap namespace) of xml that breaks off before its end, up to
        /// the break. Image and video entries have their own namespaces and are not taken.
        /// </summary>
        /// <param name="xml">The sitemap text</param>
        /// <param name="collected">Receives the entries</param>
        /// <param name="limit">The most entries to collect</param>
        private static void CollectLocsUntilBroken(string xml, HashSet<string> collected, int limit)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };

            try
            {
                using (XmlReader reader = XmlReader.Create(new StringReader(xml), settings))
                {
                    while (collected.Count < limit && true == reader.Read())
                    {
                        bool isLoc = XmlNodeType.Element == reader.NodeType
                                  && "loc" == reader.LocalName
                                  && ("http://www.sitemaps.org/schemas/sitemap/0.9" == reader.NamespaceURI || string.Empty == reader.NamespaceURI);

                        if (true == isLoc)
                        {
                            string loc = reader.ReadElementContentAsString().Trim();

                            if (true == Uri.IsWellFormedUriString(loc, UriKind.Absolute))
                                collected.Add(loc);
                        }
                    }
                }
            }
            catch (XmlException)
            {
                // the break itself; every entry before it is already in
            }
        }

        // HTML sitemap parser (Indeed-style, table or list of links)
        internal void CollectUrlsFromHtml(
            string htmlContent,
            HashSet<string> collected,
            int limit)
        {
            if (collected.Count >= limit || string.IsNullOrWhiteSpace(htmlContent))
                return;

            try
            {
                var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                htmlDoc.LoadHtml(htmlContent);

                // Common patterns for sitemap HTML pages:
                // 1. <a href="..."> inside <td> or <li>
                // 2. Links in a table or ul/li structure

                // Find all <a> tags with href starting with http/https
                var links = htmlDoc.DocumentNode.SelectNodes("//a[@href]");

                if (links != null)
                {
                    foreach (var link in links)
                    {
                        if (collected.Count >= limit) break;

                        string href = link.GetAttributeValue("href", null).Replace(@"\""","");

                        if (!string.IsNullOrWhiteSpace(href) &&
                            (href.StartsWith("http://") || href.StartsWith("https://")) &&
                            Uri.IsWellFormedUriString(href, UriKind.Absolute))
                        {
                            // Optional: filter to likely sitemap URLs (xml, sitemap, etc.)
                            //if (href.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                            //    href.Contains("/sitemap") ||
                            //    href.Contains("/products") ||
                            //    href.Contains("/categories"))
                            {
                                collected.Add(href);
                            }
                        }
                    }
                }

                // If no links found, try looking inside <pre> or specific divs (fallback)
                if (collected.Count == 0)
                {
                    var preNodes = htmlDoc.DocumentNode.SelectNodes("//pre | //div[contains(@id,'source-xml')]");
                    if (preNodes != null)
                    {
                        foreach (var pre in preNodes)
                        {
                            // Extract potential URLs from text content
                            string text = pre.InnerText;
                            var urlMatches = System.Text.RegularExpressions.Regex.Matches(text, @"https?://[^\s<>\""]+");
                            foreach (System.Text.RegularExpressions.Match m in urlMatches)
                            {
                                if (collected.Count >= limit) break;
                                string url = m.Value.TrimEnd('.', ',', ';', ')');
                                if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
                                {
                                    collected.Add(url);
                                }
                            }
                        }
                    }
                }

                if (collected.Count == 0)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.WARNING,
                        "No valid URLs found in HTML sitemap content.",
                        typeof(SitemapExtractor),
                        GPALObjectType.Other);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.WARNING,
                    "Failed to parse HTML sitemap: [" + ex.Message + "]",
                    typeof(SitemapExtractor),
                    GPALObjectType.Other);
            }
        }

        static Dictionary<string, List<string>> returnedSitemaps = new Dictionary<string, List<string>>();

        // Helper: build initial sitemap URL from GPALUrl (still useful for caller)
        public List<string> GetSitemapUrl(GPALUrl domainUrl, out bool isFromRobotsTxt)
        {
            List<string> sitemapUrls = new List<string>();

            string inputUrl = domainUrl?.Url;
            string domain = ExtractDomain(inputUrl);
            isFromRobotsTxt = false;

            // have we already done this? return the cache
            // copy the cached list, so the merge below does not add to the list it is enumerating
            if (false == domainUrl?.Url.ToLower().EndsWith(".xml") && true == returnedSitemaps.ContainsKey(domain))
                sitemapUrls = new List<string>(returnedSitemaps[domain]);

            List<string> robotsTxt = new List<string>();

            // check if we have a robots.txt and get it back as a list<string>
            if (true == UrlHelper.CheckRobotsTxt(inputUrl, browser, robotsTxt))
            {
                string message = $"No sitemap listed in robots.txt for [{domain}]. Using sitemap.xml";

                sitemapUrls = ExtractSitemapsFromRobotsTxt(robotsTxt);
                if (0 == sitemapUrls.Count)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message);
                    sitemapUrls.Add("https://" + domain + "/sitemap.xml");
                }
                else if (0 < sitemapUrls.Count)
                {
                    message = $"Sitemap found in robots.txt.";
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message);
                    isFromRobotsTxt = true;
                }
            }
            // else, not allowed here, we are on about:blank

            // NOT SURE why i didn't want to overwrite?
            //if (0 == browser.RobotsTxt.Count)
            browser.RobotsTxt = robotsTxt;

            if (true == returnedSitemaps.ContainsKey(domain))
                foreach (string sitemapUrl in sitemapUrls)
                {
                    if (false == returnedSitemaps[domain].Contains(sitemapUrl))
                        returnedSitemaps[domain].Add(sitemapUrl);
                }
            else
                returnedSitemaps[domain] = sitemapUrls;

            return sitemapUrls;
        }
        /// <summary>
        /// Tests if content is a valid sitemap (raw XML, browser-wrapped XML, or HTML list of links).
        /// Returns true + cleaned usable content if yes; false + empty otherwise.
        /// </summary>
        /// <summary>
        /// True if the page source is a bot-check interstitial rather than real content - currently the
        /// PerimeterX "Robot or human?" challenge (its unique title plus the px-captcha injection point).
        /// This only recognizes the wall so the caller can stop and skip; it does not attempt to solve it.
        /// </summary>
        internal static bool IsBotCheckPage(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;
            // press and hold
            // #px-captcha - is the css for the button which is in a closed shadowdom
            bool isBotCheck = content.IndexOf("Robot or human?", StringComparison.OrdinalIgnoreCase) >= 0
                   && content.IndexOf("px-captcha", StringComparison.OrdinalIgnoreCase) >= 0;

            if (true == isBotCheck)
            {
                // "px-captcha" - trusted-left-click - holdMs 10_000
            }

            return isBotCheck;
        }

        internal static bool IsValidSitemapContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            string trimmed = content.Trim().Trim('\"');

            // 1. Looks like raw XML sitemap?
            if (trimmed.StartsWith("<?xml") || trimmed.Contains("<sitemapindex") || trimmed.Contains("<urlset"))
            {
                try
                {
                    XDocument.Parse(trimmed);
                    return true;
                }
                catch
                {
                    // Invalid XML — maybe html
                    //return false;
                }
            }

            // 2. Browser-wrapped XML viewer?
            if (trimmed.Contains("webkit-xml-viewer-source-xml") || trimmed.Contains("<div id=\"webkit-xml-viewer-source-xml\">") ||
                trimmed.Contains("<sitemapindex") || trimmed.Contains("<urlset") || trimmed.Contains("<loc>"))
            {
                // If it has the wrapper AND contains sitemap tags inside > valid
                return trimmed.Contains("<sitemapindex") || trimmed.Contains("<urlset") || trimmed.Contains("<loc>");
            }

            // 3. HTML sitemap (Indeed-style, list of links)?
            // Count absolute URLs, especially those ending in .xml or containing /sitemap
            var urlMatches = System.Text.RegularExpressions.Regex.Matches(trimmed, @"https?://[^\s""'<>\)\]]+");
            int totalUrls = urlMatches.Count;
            int xmlUrls = 0;

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{urlMatches.Count}] urls on potential sitemap page.");

            foreach (System.Text.RegularExpressions.Match m in urlMatches)
            {
                string url = m.Value.TrimEnd('.', ',', ';', ')', ']');
                if (url.ToLower().EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    url.ToLower().Contains("/sitemap"))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $">>>>> xml match [{url}]", content, GPALObjectType.Other);
                    xmlUrls++;
                }
            }

            // NOTE: hardcoded values - Heuristic: at least 5 URLs, and at least 2 look sitemap-like
            // NOTE: CAVEAT: KLUDGE: I'm not sure about the xml count, i'm not convinced they will have anything with xml in it...
            // TODO: add settings for these? fina a better heuristic?
            return totalUrls >= 10 && xmlUrls >= 2;
        }

        private string ExtractDomain(string url)
        {
            string host = url;

            try
            {
                var uri = new Uri(url, UriKind.RelativeOrAbsolute);
                if (uri.IsAbsoluteUri)
                    host = uri.Host;
            }
            catch
            {
                // If URI parsing fails, fall back to string-based extraction
            }

            string[] parts = host.Split('.');

            if (parts.Length <= 2)
                return host; // already a main domain or short TLD

            // Check if last part is numeric (IPv4-like)
            if (parts[parts.Length - 1].All(char.IsDigit))
                return host;

            // Check for common two-part TLDs like co.uk, co.in, com.au, etc.
            // If last two parts are both short (usually 2 chars), take last 3
            if (parts[parts.Length - 1].Length <= 3 && parts[parts.Length - 2].Length <= 3)
            {
                // Equivalent to parts.TakeLast(3)
                return string.Join(".", parts.Skip(parts.Length - 3).Take(3));
            }

            // Default: take last 2 parts
            // Equivalent to parts.TakeLast(2)
            return string.Join(".", parts.Skip(parts.Length - 2).Take(2));
        }

        /// <summary>
        /// Extracts all sitemap URLs from a robots.txt file provided as a List&lt;string&gt;.
        /// Returns absolute URLs only (ignores malformed or relative ones).
        /// </summary>
        /// <param name="robotsTxt">Lines of the robots.txt file</param>
        /// <returns>List of valid absolute sitemap URLs</returns>
        public List<string> ExtractSitemapsFromRobotsTxt(List<string> robotsTxt)
        {
            var sitemaps = new List<string>();

            if (robotsTxt == null || robotsTxt.Count == 0)
                return sitemaps;

            foreach (string line in robotsTxt)
            {
                // Skip empty or comment lines
                string trimmed = line?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                // Look for "Sitemap:" (case-insensitive, tolerant of extra spaces)
                if (trimmed.StartsWith("Sitemap:", StringComparison.OrdinalIgnoreCase))
                {
                    // Get everything after "Sitemap:"
                    string potentialUrl = trimmed.Substring("Sitemap:".Length).Trim();
                    int idx = potentialUrl.IndexOf("<");    // we got this xml as html, so we have a closing <tag>
                    if (-1 != idx)
                        potentialUrl = potentialUrl.Substring(0, idx);
                    else
                        potentialUrl = potentialUrl.Substring(0, potentialUrl.Length);

                    // Clean up any trailing junk (sometimes people add comments after URL)
                    int commentIndex = potentialUrl.IndexOf('#');
                    if (commentIndex >= 0)
                        potentialUrl = potentialUrl.Substring(0, commentIndex).Trim();

                    // Only keep valid absolute URLs
                    if (Uri.IsWellFormedUriString(potentialUrl, UriKind.Absolute) &&
                        (potentialUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         potentialUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                    {
                        sitemaps.Add(potentialUrl);
                    }
                }
            }

            return sitemaps;
        }
    }
}
