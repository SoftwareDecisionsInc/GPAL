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
using System.Text.RegularExpressions;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// One named set of LLM-digest cleanup rules: which nodes are junk, where the main content
    /// usually lives, what base URL to use for relative sitemap links, and which post-conversion
    /// markdown cleanup passes to run. Sites drift over time, so these live in
    /// LLMDigestRules.yaml rather than being hardcoded.
    /// </summary>
    public class DigestRuleSet
    {
        /// <summary>Name of this rule set, e.g. "generic" or "indeed".</summary>
        public string Name { get; set; } = "generic";

        /// <summary>
        /// Substrings matched (case-insensitive) against the current page URL to decide whether
        /// this rule set applies. The "generic" rule set is the fallback and does not need any.
        /// </summary>
        public List<string> DomainPatterns { get; set; } = new List<string>();

        /// <summary>XPath expressions for nodes to strip before conversion (ads, nav, cookie banners, etc.).</summary>
        public List<string> JunkSelectors { get; set; } = new List<string>();

        /// <summary>XPath expressions tried in order to find the main content container.</summary>
        public List<string> MainContentCandidates { get; set; } = new List<string>();

        /// <summary>Base URL used to resolve relative links when a page is treated as a sitemap. Empty means derive it from the current page's URL.</summary>
        public string SitemapBaseUrl { get; set; } = "";

        /// <summary>Names of the post-conversion markdown cleanup passes to run, e.g. "PriceGluing".</summary>
        public List<string> EnabledPasses { get; set; } = new List<string>();
    }

    /// <summary>
    /// Loads and saves the named <see cref="DigestRuleSet"/>s used by <see cref="HtmlToOptimizedMarkdown"/>,
    /// stored as ./LLMDigestRules.yaml so site-specific tuning can be edited without recompiling.
    /// </summary>
    public class DigestRulesConfig
    {
        private static string ConfigFilePath = "./LLMDigestRules.yaml";

        /// <summary>
        /// Version stamped into the file, taken from the GPAL assembly version so the rules file records which
        /// build last wrote it. Falls back to the executing assembly version if <see cref="GPAL.Version"/> is not
        /// yet initialized.
        /// </summary>
        public static string CurrentSchemaVersion =>
            false == string.IsNullOrWhiteSpace(GPAL.Version)
                ? GPAL.Version
                : (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0");

        /// <summary>The GPAL version that last wrote the file.</summary>
        public string Version { get; set; } = null;

        /// <summary>GPAL-managed rule sets. Refreshed from <see cref="Default"/> on every load; edits here are not preserved.</summary>
        public List<DigestRuleSet> RuleSets { get; set; } = new List<DigestRuleSet>();

        /// <summary>User-owned rule sets. GPAL never modifies, removes, or re-adds these. Moving a GPAL rule set here "adopts" it.</summary>
        public List<DigestRuleSet> UserRuleSets { get; set; } = new List<DigestRuleSet>();

        private const string GpalBanner =
            "##### GPAL (managed by GPAL, refreshed from defaults on every run) #####";
        private const string UserBanner =
            "##### NEW (yours, GPAL never touches this section) #####";

        private static readonly string FileHeader =
            "# ==================================================================" + Environment.NewLine +
            "# GPAL LLM Digest Rules" + Environment.NewLine +
            "#" + Environment.NewLine +
            "# ruleSets     : GPAL owns these. They are regenerated from the built-in" + Environment.NewLine +
            "#                defaults whenever the GPAL version changes, so any edits" + Environment.NewLine +
            "#                made directly here are lost on the next version. Within a" + Environment.NewLine +
            "#                version this file is left as-is (it is the source of truth)." + Environment.NewLine +
            "# userRuleSets : Yours. GPAL never changes anything in this section." + Environment.NewLine +
            "#                To take ownership of a GPAL rule set, move the whole entry" + Environment.NewLine +
            "#                out of ruleSets and into userRuleSets. GPAL will not re-add" + Environment.NewLine +
            "#                a rule set whose name it finds there, so your copy wins." + Environment.NewLine +
            "# ==================================================================" + Environment.NewLine;

        /// <summary>
        /// Loads the digest rules config, refreshing the GPAL-managed section from <see cref="Default"/> while
        /// preserving the user section verbatim. Falls back to
        /// <see cref="Default"/> if the file cannot be read. If <paramref name="file"/> is provided it becomes the
        /// new default path for this session.
        /// </summary>
        public static DigestRulesConfig Load(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;

            // No file yet: write a fresh, fully-populated versioned config and return it.
            if (false == File.Exists(ConfigFilePath))
            {
                var fresh = Default();
                TryWrite(fresh);
                return fresh;
            }

            DigestRulesConfig loaded;
            try
            {
                loaded = new DigestRulesConfig();
                GPAL.Converter
                    .WithInput((GPALFile)ConfigFilePath)
                    .SaveTo(ref loaded);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to load LLM digest rules from file. Using defaults.", null, GPALObjectType.None, ex);
                return Default();
            }

            if (null == loaded)
                return Default();

            loaded.RuleSets = loaded.RuleSets ?? new List<DigestRuleSet>();
            loaded.UserRuleSets = loaded.UserRuleSets ?? new List<DigestRuleSet>();

            // The GPAL section is taken from the built-in defaults for this run, so a workflow always runs
            // the rules this build ships. A rule set the user has adopted into userRuleSets is never
            // re-added, so their copy is the one used for that name.
            var userNames = new HashSet<string>(
                loaded.UserRuleSets.Select(r => r?.Name).Where(n => false == string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            loaded.RuleSets = Default().RuleSets.Where(r => false == userNames.Contains(r.Name)).ToList();

            // Write the file only when the GPAL version changes. The user section is written back unchanged.
            if (false == string.Equals(loaded.Version, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                loaded.Version = CurrentSchemaVersion;
                TryWrite(loaded);
            }

            return loaded;
        }

        /// <summary>
        /// Writes a fresh config (GPAL defaults, empty user section) to the file as a starter for editing.
        /// If <paramref name="file"/> is provided it becomes the new default path for this session.
        /// </summary>
        public static void Save(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            WriteConfigFile(Default(), ConfigFilePath);
        }

        private static void TryWrite(DigestRulesConfig cfg)
        {
            try
            {
                WriteConfigFile(cfg, ConfigFilePath);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not write LLM digest rules file", null, GPALObjectType.None, ex);
            }
        }

        /// <summary>
        /// Serializes <paramref name="cfg"/> to YAML, decorates it with the header and the GPAL/NEW section
        /// banners, and writes it to <paramref name="path"/> only when the resulting content differs from what
        /// is already on disk (so a stable config does not churn the file every run).
        /// </summary>
        private static void WriteConfigFile(DigestRulesConfig cfg, string path)
        {
            // NOTE: the converter chooses its format from the file extension, so the temp file MUST end in
            // .yaml or it will serialize as CSV.
            string tmp = path + ".gpaltmp.yaml";
            GPAL.Converter.WithInput(cfg).SaveTo((GPALFile)tmp);

            string yaml = File.ReadAllText(tmp);
            try { File.Delete(tmp); } catch { /* best effort */ }

            // Attach the section banners just above their YAML keys (banners are YAML comments, ignored on read).
            yaml = Regex.Replace(yaml, @"(?m)^ruleSets:", GpalBanner + Environment.NewLine + "ruleSets:");
            yaml = Regex.Replace(yaml, @"(?m)^userRuleSets:", Environment.NewLine + UserBanner + Environment.NewLine + "userRuleSets:");

            string full = FileHeader + yaml;

            if (false == File.Exists(path) || File.ReadAllText(path) != full)
                File.WriteAllText(path, full);
        }

        /// <summary>
        /// Picks the rule set to use for <paramref name="url"/>: an explicit <paramref name="ruleSetName"/>
        /// wins if it exists, otherwise the first non-generic rule set whose DomainPatterns match the URL,
        /// otherwise "generic". User rule sets take precedence over GPAL ones with the same name. Any field
        /// left empty on the selected rule set falls back to "generic"'s value.
        /// </summary>
        public DigestRuleSet SelectFor(string url, string ruleSetName = null)
        {
            var user = UserRuleSets ?? new List<DigestRuleSet>();
            var gpal = RuleSets ?? new List<DigestRuleSet>();
            var all = user.Concat(gpal).ToList();   // user first, so an adopted rule set wins for its name

            var generic = user.FirstOrDefault(r => "generic" == r.Name)
                       ?? gpal.FirstOrDefault(r => "generic" == r.Name)
                       ?? new DigestRuleSet();

            DigestRuleSet selected = null;

            if (false == string.IsNullOrWhiteSpace(ruleSetName))
                selected = all.FirstOrDefault(r => string.Equals(r.Name, ruleSetName, StringComparison.OrdinalIgnoreCase));

            if (null == selected && false == string.IsNullOrWhiteSpace(url))
                selected = all.FirstOrDefault(r =>
                    "generic" != r.Name &&
                    null != r.DomainPatterns &&
                    r.DomainPatterns.Any(p => 0 <= url.IndexOf(p, StringComparison.OrdinalIgnoreCase)));

            if (null == selected || selected == generic)
                return generic;

            return new DigestRuleSet
            {
                Name = selected.Name,
                DomainPatterns = selected.DomainPatterns,
                // Strip the site's junk nodes plus generic's (script, style, noscript, iframe, cookie banners).
                JunkSelectors = generic.JunkSelectors.Concat(selected.JunkSelectors).Distinct().ToList(),

                // the site's own candidates are tried first and generic's are the fallback behind them
                MainContentCandidates = selected.MainContentCandidates.Concat(generic.MainContentCandidates).Distinct().ToList(),
                SitemapBaseUrl = false == string.IsNullOrWhiteSpace(selected.SitemapBaseUrl) ? selected.SitemapBaseUrl : generic.SitemapBaseUrl,
                EnabledPasses = 0 < selected.EnabledPasses.Count ? selected.EnabledPasses : generic.EnabledPasses
            };
        }

        /// <summary>
        /// Built-in rule sets, used when ./LLMDigestRules.yaml does not exist. Call <c>DigestRulesConfig.Save()</c>
        /// to write them out as a starting point for tuning.
        /// </summary>
        public static DigestRulesConfig Default()
        {
            var generic = new DigestRuleSet
            {
                Name = "generic",
                JunkSelectors = new List<string>
                {
                    // Universal high-confidence junk - safe on almost all sites
                    "//script",
                    "//style",
                    "//noscript",
                    "//iframe",
                    "//*[contains(@class,'cookie') or contains(@class,'consent') or contains(@class,'gdpr') or contains(@id,'cookie')]",

                    // Navigation & layout boilerplate (protected from main content)
                    "//nav",
                    "//header[not(ancestor::main or contains(@class,'hero') or contains(@class,'main') or contains(@class,'content'))]",
                    "//footer",
                    "//aside",
                    "//*[contains(@role,'navigation') or contains(@role,'banner') and not(ancestor::main)]",

                    // Ads, popups, skip links. 'ad' is matched as a whole class name; as a substring it would match
                    // shadow, gradient, padding, download and more. popup, modal and skip likewise:
                    // contains(@class,'popup') would match didomi-popup-open on BODY and remove the whole page.
                    "//*[contains(concat(' ', normalize-space(@class), ' '), ' ad ') or contains(concat(' ', normalize-space(@class), ' '), ' ads ')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' ad-') or contains(concat(' ', normalize-space(@class), ' '), ' ads-')" +
                    " or contains(@class,'advert')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' popup ') or contains(concat(' ', normalize-space(@class), ' '), ' popup-') or contains(concat(' ', normalize-space(@class), ' '), '-popup ')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' modal ') or contains(concat(' ', normalize-space(@class), ' '), ' modal-') or contains(concat(' ', normalize-space(@class), ' '), '-modal ')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' skip ') or contains(concat(' ', normalize-space(@class), ' '), ' skip-') or contains(concat(' ', normalize-space(@class), ' '), '-skip ')" +
                    " or contains(concat(' ', normalize-space(@id), ' '), ' skip-') or contains(concat(' ', normalize-space(@id), ' '), '-skip ') or normalize-space(@id) = 'skip'" +
                    " or contains(text(),'Skip to content')]",

                    // Social/share widgets (common noise)
                    "//*[contains(@class,'social') or contains(@class,'share') or contains(@class,'follow')]",

                    // Media player chrome (video.js and similar): control bars, screen-reader control text,
                    // caption/track displays, loading spinners, modal dialogs - all pure UI noise in a text digest
                    "//*[contains(@class,'vjs-control-bar') or contains(@class,'vjs-control-text') or contains(@class,'vjs-text-track-display') or contains(@class,'vjs-title-bar') or contains(@class,'vjs-loading-spinner') or contains(@class,'vjs-modal-dialog') or contains(@class,'vjs-menu') or contains(@class,'vjs-playback-rate')]",

                    // Accessibility helpers & legal text (minimal)
                    "//*[contains(@class,'accessibility') or contains(@class,'legal') or contains(text(),'©') and count(.//a) > 10]",

                    // Sidebars and widget rails. 'widget' is matched as a whole class or at a word boundary; as a
                    // substring it would match Amazon's celwidget on every content block. Still matches widget,
                    // widget-area, sidebar-widget and widget_text.
                    "//*[contains(@class, 'sidebar')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' widget ')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' widget-')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' widget_')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), '-widget ')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), '_widget ')" +
                    " or contains(@class, 'social')" +
                    " or contains(concat(' ', normalize-space(@class), ' '), ' popup ') or contains(concat(' ', normalize-space(@class), ' '), ' popup-') or contains(concat(' ', normalize-space(@class), ' '), '-popup ')" +
                    " or contains(concat(' ', normalize-space(@role), ' '), ' navigation ')]",

                    // Pagination / next-prev links with very short text
                    "//a[string-length(normalize-space(.)) < 15 and (contains(.,'next') or contains(.,'prev') or contains(.,'›') or contains(.,'«'))]"
                },
                MainContentCandidates = new List<string>
                {
                    "//main",
                    "//*[contains(@id, 'content') or contains(@class, 'content') or contains(@id, 'main') or contains(@class, 'main') or contains(@id, 'dp') or contains(@class, 'section') and contains(@class, 'spacing-none')]",
                    "//body"
                },
                SitemapBaseUrl = "",
                EnabledPasses = new List<string>
                {
                    "CollapseExcessiveBlanks",
                    "RemoveCountersAndEmptyIcons",
                    "RemoveEmptyImages",
                    "StripMediaChrome",
                    "ImageGridBreaker",
                    "ImageSpacing",
                    "RejoinLinkedImages",
                    "DedupeConsecutiveImages",
                    "ProductImageBrandPrice",
                    "PriceGluing",
                    "HeadingSpacing",
                    "StripEscapeArtifacts",
                    "DedupeSections",
                    "DropEmptyHeadings",
                    "SplitConsecutiveLinks",
                    "ResolveRelativeLinks"
                }
            };

            // Confidence: medium - Indeed uses dynamic class names; layout differs between search and detail pages
            var indeed = new DigestRuleSet
            {
                Name = "indeed",
                DomainPatterns = new List<string> { "indeed.com" },
                JunkSelectors = new List<string>
                {
                    "//div[contains(@class,'gnav')]",
                    "//div[contains(@class,'footer')]",
                    "//div[contains(@id,'popover')]",
                    "//div[contains(@class,'jobsearch-HiringInsights')]",
                    "//div[contains(@class,'jobsearch-CompanyInfoWithReview')]",
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='jobDescriptionText']",
                    "//div[contains(@class,'jobsearch-JobComponent')]",
                    "//div[contains(@class,'jobsearch-ResultsList')]"
                },
                SitemapBaseUrl = "https://www.indeed.com",
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - MediaWiki IDs and classes have been stable for many years
            var wikipedia = new DigestRuleSet
            {
                Name = "wikipedia",
                DomainPatterns = new List<string> { "wikipedia.org" },
                JunkSelectors = new List<string>
                {
                    "//div[@id='mw-navigation']",
                    "//div[@id='mw-head-base']",
                    "//div[@id='mw-page-base']",
                    "//div[@id='catlinks']",
                    "//div[@id='footer']",
                    "//*[contains(@class,'navbox')]",
                    "//*[contains(@class,'mw-editsection')]",
                    "//*[contains(@class,'reflist')]",
                    "//*[contains(@class,'sistersitebox')]",
                    "//*[contains(@class,'noprint')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='mw-content-text']"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - markdown-body and readme ID are longstanding; header/footer are semantic elements
            var github = new DigestRuleSet
            {
                Name = "github",
                DomainPatterns = new List<string> { "github.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'file-navigation')]",
                    "//*[contains(@class,'js-flash-container')]",
                    "//*[contains(@class,'signup-prompt')]",
                    "//*[contains(@class,'repository-topics')]",
                    "//*[contains(@class,'BorderGrid-cell') and contains(@class,'hide-sm')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//*[contains(@class,'markdown-body')]",
                    "//div[@id='readme']",
                    "//article"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - #question, #content, #sidebar, s-topbar have been stable across SO redesigns
            var stackoverflow = new DigestRuleSet
            {
                Name = "stackoverflow",
                DomainPatterns = new List<string> { "stackoverflow.com", "stackexchange.com" },
                JunkSelectors = new List<string>
                {
                    "//div[@id='left-sidebar']",
                    "//div[@id='sidebar']",
                    "//*[contains(@class,'s-topbar')]",
                    "//*[contains(@class,'post-menu')]",
                    "//*[contains(@class,'s-sidebarwidget')]",
                    "//*[contains(@class,'js-consent-banner')]",
                    "//div[@id='feed-link']",
                    "//div[@id='hot-network-questions')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='question']",
                    "//div[@id='content']"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - #dp, #centerCol, #nav-belt, #navFooter are longstanding Amazon product page IDs
            var amazon = new DigestRuleSet
            {
                Name = "amazon",
                DomainPatterns = new List<string> { "amazon.com" },
                JunkSelectors = new List<string>
                {
                    "//div[@id='nav-belt']",
                    "//div[@id='navFooter']",
                    "//div[@id='nav-subnav']",
                    "//div[@id='HeroQuickPromo']",
                    "//div[@id='rhf']",
                    "//*[contains(@id,'carousel')]",
                    "//*[contains(@class,'a-carousel-row')]",
                    "//div[@id='sims-consolidated-1']",
                    "//div[@id='sims-consolidated-2']"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='dp']",
                    "//div[@id='centerCol']"
                }
            };

            // Confidence: high - HN's table-based HTML has barely changed since launch; bgcolor and class names are hardcoded
            var hackernews = new DigestRuleSet
            {
                Name = "hackernews",
                DomainPatterns = new List<string> { "news.ycombinator.com" },
                JunkSelectors = new List<string>
                {
                    "//td[@bgcolor='#ff6600']",
                    "//span[@class='yclinks']",
                    "//tr[@class='spacer']"
                },
                MainContentCandidates = new List<string>
                {
                    "//table[@class='comment-tree']",
                    "//table[@class='fatitem']",
                    "//table[@class='itemlist']"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - old Reddit's table-based layout with stable IDs has not changed; must precede reddit entry so its domain pattern wins
            var redditOld = new DigestRuleSet
            {
                Name = "reddit-old",
                DomainPatterns = new List<string> { "old.reddit.com" },
                JunkSelectors = new List<string>
                {
                    "//div[@id='header']",
                    "//div[@id='footer']",
                    "//div[@class='side']",
                    "//div[@id='sr-header-area']"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@class='commentarea']",
                    "//div[@id='siteTable']",
                    "//div[@id='content']"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks" }
            };

            // Confidence: medium - modern Reddit uses React; shreddit-post/shreddit-comment are custom elements Reddit introduced (~2023) and have held; semantic fallbacks cover the rest
            var reddit = new DigestRuleSet
            {
                Name = "reddit",
                DomainPatterns = new List<string> { "reddit.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//aside",
                    "//footer"
                },
                MainContentCandidates = new List<string>
                {
                    "//shreddit-post",
                    "//div[@id='main-content']",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks" }
            };

            // Confidence: high - article tag is the primary content container; pw- prefixed classes are Medium's current design system and have been stable
            var medium = new DigestRuleSet
            {
                Name = "medium",
                DomainPatterns = new List<string> { "medium.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//nav",
                    "//*[contains(@class,'pw-responses')]",
                    "//*[contains(@class,'pw-post-tags')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//article"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: medium-high - available-content and subscription-widget are known Substack classes; paywall div is well-documented
            var substack = new DigestRuleSet
            {
                Name = "substack",
                DomainPatterns = new List<string> { "substack.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'subscription-widget')]",
                    "//*[contains(@class,'paywall')]",
                    "//*[contains(@class,'post-upsell')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[contains(@class,'available-content')]",
                    "//article"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - #glbfooter, #mainContent, #vi-desc, #gh-oa are longstanding eBay product page IDs
            var ebay = new DigestRuleSet
            {
                Name = "ebay",
                DomainPatterns = new List<string> { "ebay.com" },
                JunkSelectors = new List<string>
                {
                    "//div[@id='gh-oa']",
                    "//div[@id='glbfooter']",
                    "//div[@id='vi-recentlyViewed']",
                    "//*[contains(@id,'vi-recsalso')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='mainContent']",
                    "//div[@id='vi-desc']"
                }
            };

            // Confidence: medium - LinkedIn generates most class names; jobs-description and description section are known stable patterns for job pages
            var linkedin = new DigestRuleSet
            {
                Name = "linkedin",
                DomainPatterns = new List<string> { "linkedin.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//aside",
                    "//*[contains(@class,'similar-jobs')]",
                    "//*[contains(@class,'jobs-premium-upsell')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[contains(@class,'jobs-description')]",
                    "//section[contains(@class,'description')]",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: medium - #JobDescriptionContainer is known; HardsellOverlay and LoginModal are observed class names but could change
            var glassdoor = new DigestRuleSet
            {
                Name = "glassdoor",
                DomainPatterns = new List<string> { "glassdoor.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'LoginModal') or contains(@id,'LoginModal')]",
                    "//*[contains(@class,'HardsellOverlay')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='JobDescriptionContainer']",
                    "//div[@id='job-desc']",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: medium - job_description and job_desc are common ZipRecruiter patterns; semantic fallbacks cover misses
            var ziprecruiter = new DigestRuleSet
            {
                Name = "ziprecruiter",
                DomainPatterns = new List<string> { "ziprecruiter.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'suggested_jobs')]",
                    "//*[contains(@class,'email_signup')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[contains(@class,'job_description')]",
                    "//div[@id='job_desc']",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: medium - #maincontent is a known Walmart ID; RecommendedItems is an observed class; site has modernized and classes can shift
            var walmart = new DigestRuleSet
            {
                Name = "walmart",
                DomainPatterns = new List<string> { "walmart.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'RecommendedItems')]",
                    "//*[contains(@id,'recommendations')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='maincontent']",
                    "//main"
                }
            };

            // Confidence: medium - Reuters redesigns frequently; data-testid='article-body' is a known test attribute; article tag is the semantic fallback
            var reuters = new DigestRuleSet
            {
                Name = "reuters",
                DomainPatterns = new List<string> { "reuters.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'related-articles')]",
                    "//*[contains(@class,'newsletter-signup')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//*[@data-testid='article-body']",
                    "//article"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: medium - ArticleBody and RelatedStories are known AP News class names; site is relatively stable
            var apnews = new DigestRuleSet
            {
                Name = "apnews",
                DomainPatterns = new List<string> { "apnews.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'RelatedStories')]",
                    "//*[contains(@class,'Advertisement')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[contains(@class,'ArticleBody')]",
                    "//article"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: low-medium - Zillow is heavily React with generated classes; hdp prefix and zsg-modal are observed but fragile; main is the reliable fallback
            var zillow = new DigestRuleSet
            {
                Name = "zillow",
                DomainPatterns = new List<string> { "zillow.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'zsg-modal')]",
                    "//*[contains(@id,'contact-form')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[contains(@class,'hdp')]",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: medium - #content and home-main-stats are known Redfin patterns; more stable than Zillow but still React-heavy
            var redfin = new DigestRuleSet
            {
                Name = "redfin",
                DomainPatterns = new List<string> { "redfin.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//*[contains(@class,'ContactAgent')]",
                    "//*[contains(@class,'similar-homes')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[@id='content']",
                    "//div[contains(@class,'home-main-stats')]",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: high - MDN uses semantic HTML throughout; article, nav, aside, footer are all accurate; document-toc-container and on-github are known class names
            var mdn = new DigestRuleSet
            {
                Name = "mdn",
                DomainPatterns = new List<string> { "developer.mozilla.org" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//nav",
                    "//aside",
                    "//footer",
                    "//*[contains(@class,'document-toc-container')]",
                    "//*[contains(@class,'on-github')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//article",
                    "//main"
                },
                EnabledPasses = new List<string> { "CollapseExcessiveBlanks", "HeadingSpacing", "SplitConsecutiveLinks", "ResolveRelativeLinks" }
            };

            // Confidence: low-medium - Best Buy is React-heavy with generated class names. The sku-title / priceView
            // and shop-product-* patterns are observed and reasonably stable; //main is the reliable fallback. Giving
            // the converter a smaller, cleaner container also helps it avoid choking on the full-page markup.
            var bestbuy = new DigestRuleSet
            {
                Name = "bestbuy",
                DomainPatterns = new List<string> { "bestbuy.com" },
                JunkSelectors = new List<string>
                {
                    "//header",
                    "//footer",
                    "//nav",
                    "//*[contains(@class,'sponsored')]",
                    "//*[contains(@class,'carousel')]",
                    "//*[contains(@class,'recommendation')]",
                    "//*[contains(@class,'similar-products') or contains(@class,'related-products')]",
                    "//*[contains(@class,'customers-ultimately-bought')]"
                },
                MainContentCandidates = new List<string>
                {
                    "//div[contains(@class,'shop-product-details')]",
                    "//div[@id='shop-content']",
                    "//div[contains(@class,'column-left')]",
                    "//main"
                }
                // EnabledPasses intentionally left empty so it inherits the full "generic" pass list.
            };

            return new DigestRulesConfig
            {
                Version = CurrentSchemaVersion,
                UserRuleSets = new List<DigestRuleSet>(),
                RuleSets = new List<DigestRuleSet>
                {
                    generic, indeed, wikipedia, github, stackoverflow, amazon,
                    hackernews, redditOld, reddit, medium, substack,
                    ebay, linkedin, glassdoor, ziprecruiter, walmart,
                    reuters, apnews, zillow, redfin, mdn, bestbuy
                }
            };
        }
    }
}
