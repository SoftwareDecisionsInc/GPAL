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
using System.Text;
using System.Text.RegularExpressions;
using GenerallyPositive.Browser;
using HtmlAgilityPack;
using ReverseMarkdown;
using static GenerallyPositive.Enums;

// written by grok
// inspired by https://substack.thewebscraping.club/p/why-scraping-return-markdown-llm-ai?utm_source=post-email-title&publication_id=1023328&post_id=184656666
// Why LLM-Ready Scrapers Return Content in Markdown: A Deep Dive
namespace GenerallyPositive
{
    public class LLMDigestResult
    {
        public bool Success { get; set; }
        public string Markdown { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = "Untitled";
        public int OriginalLength { get; set; }
        public int CleanedLength { get; set; }
        public string Message { get; set; } = string.Empty;

        /// <summary>Name of the <see cref="DigestRuleSet"/> (from LLMDigestRules.yaml) used to build this digest.</summary>
        public string RuleSetUsed { get; set; } = "generic";
    }

    internal static class HtmlToOptimizedMarkdown
    {
        /// <summary>
        /// Converts the page's HTML into an LLM-ready markdown digest. The cleanup rules (junk
        /// selectors, main-content candidates, sitemap base URL, enabled cleanup passes) come from
        /// LLMDigestRules.yaml via <see cref="DigestRulesConfig"/>, selected by <paramref name="ruleSetName"/>
        /// if given, otherwise by matching the current page URL, otherwise "generic".
        /// </summary>
        public static LLMDigestResult CreateDigest(Browser.Browser browser, string htmlSource, string ruleSetName = null)
        {
            var result = new LLMDigestResult { OriginalLength = htmlSource?.Length ?? 0 };

            if (string.IsNullOrWhiteSpace(htmlSource))
            {
                result.Message = "Empty input";
                return result;
            }

            try
            {
                result.Url = browser.GetSetCurrentUrl();

                DigestRuleSet rules = DigestRulesConfig.Load().SelectFor(result.Url, ruleSetName);
                result.RuleSetUsed = rules.Name;

                var doc = new HtmlDocument();
                doc.OptionAutoCloseOnEnd = true;
                doc.OptionFixNestedTags = true;
                doc.LoadHtml(htmlSource);

                var products = doc.DocumentNode.SelectNodes("//a[contains(@href,'/p/')]");
                if (products != null)
                {
                    foreach (var link in products)
                    {
                        var price = link.SelectSingleNode(".//span[contains(@class,'price') or contains(text(),'$')] | .//following-sibling::*[contains(text(),'$')]");
                        if (price != null && !string.IsNullOrWhiteSpace(price.InnerText))
                        {
                            var priceNode = doc.CreateElement("span");
                            priceNode.InnerHtml = $" <strong>{price.InnerText.Trim()}</strong>";
                            link.ParentNode.InsertAfter(priceNode, link);
                        }
                    }
                }

                // Extract title if possible
                var titleNode = doc.DocumentNode.SelectSingleNode("//title");
                if (titleNode != null) result.Title = titleNode.InnerText.Trim();

                // Aggressive cleanup
                RemoveJunkNodes(doc, rules.JunkSelectors);

                // Try to find main content area (heuristic). The candidates name ids and classes, so this has to
                // run while the document still has them
                string mainContentHtml = ExtractMainContent(doc, rules.MainContentCandidates) ?? doc.DocumentNode.InnerHtml;

                // Strip attributes only after the main content is chosen; the candidates match on ids and classes.
                mainContentHtml = StripAttributes(mainContentHtml);

                // Convert to Markdown
                var config = new Config
                {
                    GithubFlavored = true
                };
                config.Formatting.RemoveComments = true;
                config.Links.SmartHref = true;
                // Aggressive unknown tag handling
                config.Tags.Unknown = Config.UnknownTagsOption.Bypass;

                var converter = new Converter(config);

                if (true == result.Url.ToLower().Contains("sitemap") ||    // does url contain 'sitemap' it just might be
                    true == result.Url.ToLower().EndsWith(".xml") ||
                    true == SitemapExtractor.IsValidSitemapContent(mainContentHtml))
                {
                    // Handle as plain-text sitemap (no HTML converter needed)
                    string sitemapBaseUrl = string.IsNullOrWhiteSpace(rules.SitemapBaseUrl)
                        ? DeriveOrigin(result.Url)
                        : rules.SitemapBaseUrl;

                    result.Markdown = ExtractSitemapAsMarkdown(mainContentHtml, sitemapBaseUrl);
                    // Optionally log or tag: result.IsSitemap = true;
                }
                else
                {
                    result.Markdown = converter.Convert(mainContentHtml);
                }

                // The cleanup passes below match on LF, so CRLF runs are invisible to them: three CRLFs are
                // not a run of three LFs. The sitemap route builds with AppendLine (CRLF on Windows) and the
                // html converter returns LF. SaveToFile applies the platform ending once, on write
                result.Markdown = result.Markdown?.Replace("\r\n", "\n").Replace("\r", "\n") ?? string.Empty;

                // === POST-CONVERSION READABILITY CLEANUP ===

                // 1. Collapse runs of four or more blank lines; keep section gaps. Matching five or more let exactly
                // four through.
                if (rules.EnabledPasses.Contains("CollapseExcessiveBlanks"))
                    result.Markdown = Regex.Replace(result.Markdown, @"(\r?\n\s*){4,}", "\n\n\n", RegexOptions.Multiline);

                // 2. Remove counters and empty icons
                if (rules.EnabledPasses.Contains("RemoveCountersAndEmptyIcons"))
                {
                    result.Markdown = Regex.Replace(result.Markdown, @"\(\d+\)", "", RegexOptions.Multiline);
                    result.Markdown = Regex.Replace(result.Markdown, @"\[\]\(.*?\)\s*|\[\]\s*", "", RegexOptions.Multiline);
                }

                // 2a. Drop images that never resolved a URL (lazy-loaded placeholders come through as
                // ![alt]() ) and the stray "!" / "!!" bangs such removals leave behind. These dominate the
                // noise on image-heavy pages (carousels, hero grids) and carry zero information for an LLM.
                if (rules.EnabledPasses.Contains("RemoveEmptyImages"))
                {
                    result.Markdown = Regex.Replace(result.Markdown, @"!\[[^\]]*\]\(\s*\)", "", RegexOptions.Multiline);
                    result.Markdown = Regex.Replace(result.Markdown, @"!{2,}", "", RegexOptions.Multiline);
                }

                // 2b. Strip standalone media-player control text (video.js and friends emit lines like
                // "Play", "Unmute", "Current Time 0:00", "Duration 0:14", "Stream Type LIVE", "Fullscreen").
                // The vjs- junk selectors remove most of it at the HTML stage; this catches any that survive
                // or come from non-video.js players.
                if (rules.EnabledPasses.Contains("StripMediaChrome"))
                    result.Markdown = Regex.Replace(result.Markdown,
                        @"(?im)^[ \t]*(Play|Pause|Unmute|Mute|Fullscreen|Exit Fullscreen|Picture-in-Picture|Playback Rate|Captions|Subtitles|Descriptions|Chapters|Watch|Stream Type\s+LIVE|Current Time\s+[\d:]+|Duration\s+[\d:]+|Remaining Time\s+-?[\d:]+|Loaded:\s*[\d.%]*|Progress:\s*[\d.%]*|Seek to live[^\r\n]*|This is a modal window[^\r\n]*|(Beginning|End) of dialog[^\r\n]*|/|-?\d{1,2}:\d{2})[ \t]*\r?$",
                        "", RegexOptions.Multiline);

                // 3. Nuclear grid breaker: repeatedly split ALL consecutive images until none remain
                // Insert \n after ) in every occurrence of ")[!\["
                if (rules.EnabledPasses.Contains("ImageGridBreaker"))
                {
                    string pattern = @")[!\[";
                    int startIndex = 0;

                    // Guard the search start: only ONE newline is inserted per match, so we advance by 2
                    // (past the ) and the inserted \n). The while-guard keeps startIndex within bounds so
                    // IndexOf can never be handed a startIndex past the (now longer) string and throw.
                    while (startIndex <= result.Markdown.Length)
                    {
                        int found = result.Markdown.IndexOf(pattern, startIndex);
                        if (found == -1)
                            break;

                        // Insert \n right after the ) — i.e. after position found
                        result.Markdown = result.Markdown.Insert(found + 1, "\n");

                        // Resume just past the ) and the newline we inserted so we do not re-match here
                        startIndex = found + 2;
                    }
                }

                // 4. Ensure every image ends with at least two newlines (breathing room). The (?!\]) guard
                // skips images that are the inner part of a linked image ( [![alt](img)](url) ) so we do not
                // split the link's closing ](url) onto its own line.
                if (rules.EnabledPasses.Contains("ImageSpacing"))
                    result.Markdown = Regex.Replace(result.Markdown,
                        @"(!\[.*?\]\(.*?\))(?!\n{2,})(?!\])",
                        "$1\n\n",
                        RegexOptions.Multiline);

                // 4-fix. Rejoin any linked image whose closing ](url) still got separated onto its own line
                // (by an earlier pass or by the converter). Turns "[![alt](img)\n\n](url)" back into
                // "[![alt](img)](url)" so the product link stays intact instead of leaving an orphan ](url).
                if (rules.EnabledPasses.Contains("RejoinLinkedImages"))
                    result.Markdown = Regex.Replace(result.Markdown,
                        @"(!\[[^\]]*\]\([^)]*\))\s*\n\s*(\]\([^)]*\))",
                        "$1$2",
                        RegexOptions.Multiline);

                // 4a. Collapse runs of the exact same image (identical alt AND url) that carousels/sliders
                // emit repeatedly. Blank lines between images are preserved and do not break a run.
                if (rules.EnabledPasses.Contains("DedupeConsecutiveImages"))
                    result.Markdown = DedupeConsecutiveImageLines(result.Markdown);

                // 5. Product fix: image > bold brand/price
                if (rules.EnabledPasses.Contains("ProductImageBrandPrice"))
                    result.Markdown = Regex.Replace(result.Markdown,
                        @"\[!\[([^\]]*)\]\([^\)]+\)]\(([^)]+)\)\s*\n?\s*\[([^\]]+)\]",
                        "![$1]($2)\n\n**$3**",
                        RegexOptions.Multiline);

                // 6. Glue prices to brand if separated
                if (rules.EnabledPasses.Contains("PriceGluing"))
                    result.Markdown = Regex.Replace(result.Markdown,
                        @"(\*\*[A-Za-z& ]+?\*\*)\s*\n\s*(\$[\d,]+(?:\.\d{2})?)",
                        "$1 $2",
                        RegexOptions.Multiline);

                // 7. Headings spacing
                if (rules.EnabledPasses.Contains("HeadingSpacing"))
                {
                    result.Markdown = Regex.Replace(result.Markdown, @"(^[ \t]*#[^\n]+)", "\n\n$1\n", RegexOptions.Multiline);
                    result.Markdown = Regex.Replace(result.Markdown, @"(^[ \t]*##[^\n]+)", "\n$1\n", RegexOptions.Multiline);
                }

                // 8. Strip leaked script/JSON artifacts: literal escape sequences (backslash-n/t/r as text,
                // which never occur in readable prose) and orphaned JS array/string fragments like "]) or ']).
                // These leak from review/data widgets whose content is not a real <script> element.
                if (rules.EnabledPasses.Contains("StripEscapeArtifacts"))
                {
                    result.Markdown = Regex.Replace(result.Markdown, @"(\\[nrtbf])+", " ");
                    result.Markdown = Regex.Replace(result.Markdown, @"[""']\]\)", "");
                    // drop lines left with nothing but whitespace/quotes/brackets after the above
                    result.Markdown = Regex.Replace(result.Markdown, @"(?m)^[ \t""'\]\)]*\r?$", "");
                }

                // 9. Collapse duplicate sections: pages that ship a no-JS fallback plus a hydrated copy render the
                // same heading + body twice (e.g. Nike "Back to School"/"TRENDING"). Keep one copy per section.
                if (rules.EnabledPasses.Contains("DedupeSections"))
                    result.Markdown = DedupeSections(result.Markdown);

                // 10. Drop headings whose section has no content (e.g. a "## Reviews" whose reviews were
                // JS-loaded and stripped), while keeping parent headings that still have populated subsections.
                if (rules.EnabledPasses.Contains("DropEmptyHeadings"))
                    result.Markdown = DropEmptyHeadings(result.Markdown);

                // 11. One link per line: split between adjacent links, with or without a space between. Nav lists
                // convert to [a](url)[b](url), card grids to [a](url) [b](url).
                if (rules.EnabledPasses.Contains("SplitConsecutiveLinks"))
                    result.Markdown = Regex.Replace(result.Markdown, @"\)[ \t]*\[(?=[^\]\n]*\]\()", ")\n[", RegexOptions.Multiline);

                // 12. Prefix root-relative links with the page's origin; a digest is read away from the page.
                if (rules.EnabledPasses.Contains("ResolveRelativeLinks"))
                {
                    string digestOrigin = DeriveOrigin(result.Url)?.TrimEnd('/');

                    if (false == string.IsNullOrWhiteSpace(digestOrigin))
                        result.Markdown = Regex.Replace(result.Markdown, @"\]\((/[^)\s]*)\)", "](" + digestOrigin + "$1)");
                }

                // Final light trim (allow up to 2 newlines for section breaks)
                result.Markdown = result.Markdown.Trim();
                result.Markdown = Regex.Replace(result.Markdown, @"\n{4,}", "\n\n", RegexOptions.Multiline);

                result.CleanedLength = result.Markdown.Length;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;

                // Last-ditch recovery: some pages make a library step throw (e.g. ReverseMarkdown can throw
                // "startIndex cannot be larger than length of string" on certain malformed HTML). Rather than
                // fail the whole digest - which aborts a batch walk - salvage the readable text so the page
                // still yields something usable and the run continues.
                try
                {
                    string fallback = HtmlToPlainText(htmlSource);
                    if (false == string.IsNullOrWhiteSpace(fallback))
                    {
                        result.Markdown = fallback;
                        result.CleanedLength = fallback.Length;
                        result.Success = true;
                        result.Message = "Recovered as plain text after error: " + ex.Message;
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                            $"Digest conversion failed, recovered [{result.Url}] as plain text.",
                            null, GPALObjectType.None, ex);
                    }
                }
                catch
                {
                    // keep the original failure if even plain-text extraction cannot run
                }
            }

            return result;
        }

        /// <summary>
        /// Strips HTML to readable, entity-decoded plain text with normalized inline whitespace. Used as the
        /// graceful fallback when the markdown converter throws on a page.
        /// </summary>
        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Drop non-text nodes so their contents never leak into the salvaged text.
                foreach (var node in doc.DocumentNode.SelectNodes("//script | //style | //noscript")?.ToList() ?? new List<HtmlNode>())
                {
                    try { node.Remove(); } catch { /* ignore */ }
                }

                // Raw InnerText glues block elements together ("Black Friday in JulyEnds Sunday"). Insert a
                // newline after each block-level element and after <br> so the salvaged text stays readable.
                string markup = doc.DocumentNode.InnerHtml ?? string.Empty;
                markup = Regex.Replace(markup,
                    @"(?i)</(p|div|li|ul|ol|section|article|header|footer|nav|main|aside|tr|table|h[1-6]|blockquote|figure|figcaption)\s*>|<br\s*/?>",
                    "$0\n");

                var stripped = new HtmlDocument();
                stripped.LoadHtml(markup);
                string text = HtmlEntity.DeEntitize(stripped.DocumentNode.InnerText ?? string.Empty);

                // collapse runs of spaces/tabs, tidy the inserted breaks, keep paragraph gaps
                text = Regex.Replace(text, @"[ \t]+", " ");
                text = Regex.Replace(text, @" *\n *", "\n");
                text = Regex.Replace(text, @"\n{3,}", "\n\n");
                return text.Trim();
            }
            catch
            {
                return html;
            }
        }

        /// <summary>
        /// Collapses consecutive occurrences of the identical image line (same alt and URL) down to one.
        /// Blank lines between the duplicates are kept and do not reset the run, so a slider that renders the
        /// same image several times with spacing in between still de-duplicates. Non-image, non-blank lines
        /// reset the run so genuinely different content is never merged.
        /// </summary>
        private static string DedupeConsecutiveImageLines(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            var sb = new StringBuilder(markdown.Length);
            string lastImage = null;

            foreach (var line in markdown.Split('\n'))
            {
                string trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    // blanks pass through untouched and do not break a run of duplicate images
                    sb.Append(line).Append('\n');
                    continue;
                }

                bool isImage = trimmed.StartsWith("![");

                if (isImage && trimmed == lastImage)
                    continue;   // exact duplicate of the previous image - drop it

                sb.Append(line).Append('\n');
                lastImage = isImage ? trimmed : null;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Removes repeated heading-led sections that carry the same content (a no-JS fallback rendered next to
        /// the hydrated copy). Sections are keyed by their text with images stripped and whitespace normalized;
        /// when two keys match, the longer block (usually the one with real image URLs) is kept and the other is
        /// dropped. Content before the first heading is never touched, and short sections are left alone to avoid
        /// merging trivially-similar blocks.
        /// </summary>
        private static string DedupeSections(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            var lines = markdown.Split('\n');

            var headingIdx = new List<int>();
            for (int i = 0; i < lines.Length; i++)
                if (Regex.IsMatch(lines[i], @"^\s*#{1,6}\s"))
                    headingIdx.Add(i);

            if (headingIdx.Count < 2)
                return markdown;   // nothing to compare

            // Build the block ranges: an optional preamble before the first heading, then one block per heading.
            var ranges = new List<(int start, int end)>();
            if (headingIdx[0] > 0)
                ranges.Add((0, headingIdx[0]));
            for (int k = 0; k < headingIdx.Count; k++)
                ranges.Add((headingIdx[k], k + 1 < headingIdx.Count ? headingIdx[k + 1] : lines.Length));

            string Text((int start, int end) r)
            {
                var sb = new StringBuilder();
                for (int j = r.start; j < r.end; j++) sb.Append(lines[j]).Append('\n');
                return sb.ToString();
            }

            string Key(string block)
            {
                string s = Regex.Replace(block, @"!\[[^\]]*\]\([^)]*\)", "");   // drop images
                s = Regex.Replace(s, @"\s+", " ");
                return s.Trim().ToLowerInvariant();
            }

            var texts = ranges.Select(Text).ToList();
            bool hasPreamble = headingIdx[0] > 0;

            // For each de-dupable key, remember the index of the longest block carrying it.
            var bestForKey = new Dictionary<string, int>();
            for (int i = 0; i < ranges.Count; i++)
            {
                if (i == 0 && hasPreamble) continue;   // preamble is unique, never deduped
                string key = Key(texts[i]);
                if (key.Length < 40) continue;         // too small to safely treat as a duplicate section
                if (false == bestForKey.TryGetValue(key, out int cur) || texts[i].Trim().Length > texts[cur].Trim().Length)
                    bestForKey[key] = i;
            }

            var dropped = new HashSet<int>();
            for (int i = 0; i < ranges.Count; i++)
            {
                if (i == 0 && hasPreamble) continue;
                string key = Key(texts[i]);
                if (key.Length < 40) continue;
                if (bestForKey.TryGetValue(key, out int keep) && keep != i)
                    dropped.Add(i);
            }

            if (0 == dropped.Count)
                return markdown;

            var outSb = new StringBuilder();
            for (int i = 0; i < ranges.Count; i++)
                if (false == dropped.Contains(i))
                    outSb.Append(texts[i]);

            return outSb.ToString();
        }

        /// <summary>
        /// Drops heading lines whose section is empty. A heading at level L is empty when every line up to the
        /// next heading of level &lt;= L (or end of document) is blank, ignoring deeper sub-headings (whose own
        /// emptiness is judged separately). This removes leftover headings like a "## Reviews" whose body was
        /// JS-loaded and stripped, while keeping parent headings that still have populated subsections.
        /// </summary>
        private static string DropEmptyHeadings(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return markdown;

            var lines = markdown.Split('\n');
            var drop = new bool[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^\s*(#{1,6})\s");
                if (false == m.Success) continue;

                int level = m.Groups[1].Value.Length;
                bool hasContent = false;

                for (int j = i + 1; j < lines.Length; j++)
                {
                    var hm = Regex.Match(lines[j], @"^\s*(#{1,6})\s");
                    if (hm.Success)
                    {
                        if (hm.Groups[1].Value.Length <= level) break;   // end of this heading's section
                        continue;                                        // deeper heading: not our content
                    }
                    if (false == string.IsNullOrWhiteSpace(lines[j])) { hasContent = true; break; }
                }

                if (false == hasContent) drop[i] = true;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
                if (false == drop[i]) sb.Append(lines[i]).Append('\n');

            return sb.ToString();
        }

        private static void RemoveJunkNodes(HtmlDocument doc, List<string> junkSelectors)
        {
            foreach (var selector in junkSelectors)
            {
                var nodes = doc.DocumentNode.SelectNodes(selector)?.ToList() ?? new List<HtmlNode>();
                foreach (var node in nodes)
                {
                    // A junk rule must never match html or body. A body class such as didomi-popup-open would remove
                    // the whole document.
                    if (true == "html".Equals(node.Name, StringComparison.OrdinalIgnoreCase)
                        || true == "body".Equals(node.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"A junk selector matched the [{node.Name}] element itself and was not applied to it: [{selector}]", null, GPALObjectType.None);
                        continue;
                    }

                    try { node.Remove(); } catch { /* ignore */ }
                }
            }

        }

        /// <summary>
        /// Removes every attribute except href, src and alt, the only ones the markdown uses. Runs on the chosen main
        /// content, since the MainContentCandidates match on ids and classes.
        /// </summary>
        /// <param name="html">The html to strip</param>
        /// <returns>The html without the other attributes</returns>
        private static string StripAttributes(string html)
        {
            string retVal = html;

            if (false == string.IsNullOrWhiteSpace(html))
            {
                var doc = new HtmlDocument();
                doc.OptionAutoCloseOnEnd = true;
                doc.OptionFixNestedTags = true;
                doc.LoadHtml(html);

                string[] attrsToKeep = new[] { "href", "src", "alt" };

                foreach (var node in doc.DocumentNode.DescendantsAndSelf().ToList())
                {
                    if (node.Attributes.Count == 0) continue;
                    var attrsToRemove = node.Attributes.Where(a => !attrsToKeep.Contains(a.Name)).ToList();
                    foreach (var attr in attrsToRemove) node.Attributes.Remove(attr);
                }

                retVal = doc.DocumentNode.InnerHtml;
            }

            return retVal;
        }

        private static string ExtractSitemapAsMarkdown(string html, string baseUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Sitemap Extract");
            sb.AppendLine("");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Find the main content container (h1 + div + h2 + p's)
            var mainContainer = doc.DocumentNode.SelectSingleNode("//h1/ancestor::div") ?? doc.DocumentNode;

            // Get title from <h1>
            var titleNode = mainContainer.SelectSingleNode(".//h1");
            if (titleNode != null)
            {
                sb.AppendLine($"## {titleNode.InnerText.Trim()}");
                sb.AppendLine("");
            }

            // Get intro text (List from ... links)
            var introDiv = mainContainer.SelectSingleNode(".//div[not(@class)]"); // first generic div
            if (introDiv != null)
            {
                sb.AppendLine(introDiv.InnerText.Trim());
                sb.AppendLine("");
            }

            // Get last updated
            var updatedNode = mainContainer.SelectSingleNode(".//h2[contains(text(), 'Last updated')]");
            if (updatedNode != null)
            {
                sb.AppendLine($"*{updatedNode.InnerText.Trim()}*");
                sb.AppendLine("");
            }

            // Extract all <p> entries (each is a link + timestamp)
            var entries = mainContainer.SelectNodes(".//p");
            if (entries != null && entries.Count > 0)
            {
                foreach (var p in entries)
                {
                    var link = p.SelectSingleNode(".//a");
                    if (link != null)
                    {
                        string title = link.InnerText.Trim();
                        string url = link.GetAttributeValue("href", "");

                        if (!string.IsNullOrEmpty(url))
                        {
                            // Make full URL if relative
                            if (!url.StartsWith("http"))
                                url = baseUrl + url;

                            sb.AppendLine($"- [{title}]({url})");
                        }
                        else
                        {
                            sb.AppendLine($"- {title}");
                        }
                    }
                    else
                    {
                        // Timestamp or other text in <p>
                        sb.AppendLine($"  *{p.InnerText.Trim()}*");
                    }

                    sb.AppendLine("");
                }
            }
            else
            {
                // Fallback: if no <p>, try text nodes or links directly
                var links = mainContainer.SelectNodes(".//a");
                if (links != null)
                {
                    foreach (var link in links)
                    {
                        string title = link.InnerText.Trim();
                        string url = link.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(url))
                        {
                            if (!url.StartsWith("http"))
                                url = baseUrl + url;

                            sb.AppendLine($"- [{title}]({url})");
                        }
                    }
                }
            }

            string final = sb.ToString();

            // Final cleanup
            final = Regex.Replace(final, @"(\r?\n\s*){4,}", "\n\n", RegexOptions.Multiline);
            final = Regex.Replace(final, @"^\s*$\n", "", RegexOptions.Multiline);
            final = final.Trim();

            return final;
        }

        private static string ExtractMainContent(HtmlDocument doc, List<string> mainContentCandidates)
        {
            // Configured candidates: main, content wrappers, body fallback - tried in order
            var candidates = mainContentCandidates
                .Select(selector => doc.DocumentNode.SelectSingleNode(selector))
                .Where(n => n != null)
                .ToList();

            // Add all large divs/section with reasonable size
            var largeBlocks = doc.DocumentNode.SelectNodes("//div | //section")
                ?.Where(n => n.InnerText.Length > 500 || n.SelectNodes(".//img")?.Count > 3)
                ?.ToList() ?? new List<HtmlNode>();
            candidates.AddRange(largeBlocks);

            if (candidates.Any())
            {
                // Score: text length + images + prices ($) + links
                var best = candidates
                    .OrderByDescending(n => n.InnerText.Length)
                    .ThenByDescending(n => n.SelectNodes(".//img")?.Count ?? 0)
                    .ThenByDescending(n => n.InnerText.Count(c => c == '$'))
                    .ThenByDescending(n => n.SelectNodes(".//a")?.Count ?? 0)
                    .FirstOrDefault();

                if (best != null)
                {
                    return best.InnerHtml;
                }
            }

            // Additional fallback cleanup: remove empty nodes, short meaningless lines, duplicates
            var allTextNodes = doc.DocumentNode.SelectNodes("//text()")
                ?.Select(n => n.InnerText.Trim())
                ?.Where(t => !string.IsNullOrWhiteSpace(t) && t.Length > 5) // skip very short
                ?.ToList() ?? new List<string>();

            // Remove duplicates (common in Amazon skeletons)
            var uniqueText = new HashSet<string>(allTextNodes);
            var cleanedText = string.Join("\n\n", uniqueText);

            // If still empty, return full ParsedText as last resort
            if (string.IsNullOrWhiteSpace(cleanedText))
            {
                cleanedText = doc.ParsedText.Trim();
            }

            return cleanedText;
        }

        private static int ScoreContentDensity(HtmlNode node)
        {
            if (node == null) return 0;
            var textLen = node.InnerText?.Trim().Length ?? 0;
            var htmlLen = node.InnerHtml?.Length ?? 0;
            return htmlLen > 0 ? textLen * 100 / htmlLen : 0; // rough % text vs markup
        }

        /// <summary>
        /// Returns the scheme+host of <paramref name="url"/> (e.g. "https://example.com") for resolving
        /// relative sitemap links when no SitemapBaseUrl is configured for the matched rule set.
        /// </summary>
        private static string DeriveOrigin(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return $"{uri.Scheme}://{uri.Authority}";

            return "";
        }

        public static void SaveToFile(LLMDigestResult digest, string filePath)
        {
            if (!digest.Success)
            {
                // Do not abort the caller's run over a single unrecoverable page. Write a visible stub so the
                // failure is recorded on disk and a batch walk moves on to the next URL.
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Digest failed for [{digest.Url}] [{digest.Message}], writing stub file.", null, GPALObjectType.None);
                FileHelper.EnsureFolderFor(filePath);
                File.WriteAllText(filePath, $"# Digest failed [{digest.Url}]{Environment.NewLine}{Environment.NewLine}{digest.Message}{Environment.NewLine}");
                return;
            }

            // Built line by line. A multi-line @"" literal writes this file's own indentation into the output
            string header = string.Join(Environment.NewLine, new[]
            {
                $"# {digest.Title} [{digest.Url}]",
                $"**Extracted digest** - Original length: {digest.OriginalLength / 1024} KB -> Cleaned Markdown: about {digest.CleanedLength / 1024} KB",
                "---",
                ""
            });

            // The digest is held in LF throughout; the platform ending is applied here
            string body = (digest.Markdown ?? string.Empty).Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

            FileHelper.EnsureFolderFor(filePath);
            File.WriteAllText(filePath, header + body);
        }
    }
}
