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
        /// Executes JavaScript in the browser and stores the raw result object in JavaScriptResultObj.
        /// </summary>
        /// <param name="javascript">JavaScript to execute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ExecuteJavaScriptObj([JavaScript] string javascript)
        {
            JavaScriptResultObj = BrowserHelper.ExecuteJavaScriptObj(javascript, this);
            return this;
        }

        /// <summary>
        /// Executes JavaScript in the browser and stores the result as a string in JavaScriptResultStr.
        /// </summary>
        /// <param name="javascript">JavaScript to execute</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ExecuteJavaScriptStr([JavaScript] string javascript)
        {
            JavaScriptResultStr = BrowserHelper.ExecuteJavaScript(this, javascript);
            return this;
        }

        private readonly List<string> _injectedScriptIdentifiers = new List<string>();

        // Whether a script is registered with the driving engine, and so needs removing on close
        private bool _scriptsRegistered = false;
        private readonly List<string> _pendingInjectedScripts = new List<string>();

        /// <summary>
        /// Hands one script to whichever engine is driving this browser. The engine must already be reachable -
        /// <see cref="InjectScript(string)"/> holds the script until it is.
        /// </summary>
        /// <param name="script">JavaScript source code to run on every new document</param>
        private void RegisterInjectedScript(string script)
        {
            _scriptsRegistered = true;

            if (true == UseOttoMagic)
            {
                MagicHelper.InjectScript(script);
            }
            else if (true == UsePuppeteer)
            {
                PuppeteerCommunicator.InjectScript(script, null).GetAwaiter().GetResult();
            }
            else // selenium
            {
                var identifier = BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings, script);
                if (false == string.IsNullOrEmpty(identifier))
                    _injectedScriptIdentifiers.Add(identifier);
            }
        }

        /// <summary>
        /// Registers every script that <see cref="InjectScript(string)"/> held while the browser was still down.
        /// Called from WorkflowSetup once the engine answers and before the first navigation, so a script queued
        /// ahead of .GoTo() still runs on the first document.
        /// </summary>
        internal void ApplyPendingInjectedScripts()
        {
            if (0 < _pendingInjectedScripts.Count && true == EngineReachable)
            {
                foreach (var script in _pendingInjectedScripts)
                    RegisterInjectedScript(script);

                _pendingInjectedScripts.Clear();
            }
        }

        /// <summary>
        /// Registers a script (raw JS string) to run on every new document load. Callable before the first
        /// .GoTo()/.Get() (active from the first page load) or at any later point (applies to subsequent
        /// navigations). Persists until <see cref="ClearInjectedScripts"/> is called.
        /// </summary>
        /// <param name="script">JavaScript source code to run on every new document</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InjectScript([JavaScript] string script)
        {
            // no browser to inject into until the first .GoTo()/.Get() launches one, so the script is held
            // and registered the moment the engine answers, ahead of the first navigation
            if (false == EngineReachable)
                _pendingInjectedScripts.Add(script);
            else
                RegisterInjectedScript(script);

            return this;
        }

        /// <summary>
        /// Registers script(s) loaded from <paramref name="scriptFile"/> to run on every new document load - if
        /// multiple filenames are specified (e.g. via wildcard), all are loaded and injected as separate scripts.
        /// Persists until <see cref="ClearInjectedScripts"/> is called.
        /// </summary>
        /// <param name="scriptFile">GPALFile referencing one or more JavaScript files</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector InjectScript(GPALFile scriptFile)
        {
            foreach (var filename in scriptFile.Filenames)
                InjectScript(File.ReadAllText(filename));
            return this;
        }

        /// <summary>
        /// Removes all scripts previously registered via <see cref="InjectScript(string)"/> or
        /// <see cref="InjectScript(GPALFile)"/>.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearInjectedScripts()
        {
            // scripts still waiting on a browser go first, so one queued and then cleared never reaches the page
            _pendingInjectedScripts.Clear();

            if (true == EngineReachable)
            {
                if (true == UseOttoMagic)
                {
                    MagicHelper.ClearInjectedScripts();
                }
                else if (true == UsePuppeteer)
                {
                    PuppeteerCommunicator.ClearInjectedScripts().GetAwaiter().GetResult();
                }
                else // selenium
                {
                    foreach (var identifier in _injectedScriptIdentifiers)
                        BrowserHelper.SeleniumRemoveScriptToEvaluateOnNewDocument(BrowserSettings, identifier);
                    _injectedScriptIdentifiers.Clear();
                }
            }
            return this;
        }
        /// <summary>
        /// The result object returned by the most recent <see cref="ExecuteJavaScriptObj"/> call.
        /// </summary>
        public object JavaScriptResultObj
        {
            get => BrowserSettings.JavaScriptResultObj;
            internal set => BrowserSettings.JavaScriptResultObj = value;
        }
        /// <summary>
        /// The result string returned by the most recent <see cref="ExecuteJavaScriptStr"/> call.
        /// </summary>
        public string JavaScriptResultStr
        {
            get => BrowserSettings.JavaScriptResultStr;
            internal set => BrowserSettings.JavaScriptResultStr = value;
        }
        /// <summary>
        /// Extracts Next.js hydration data (e.g. __NEXT_DATA__) embedded in the current page's HTML.
        /// </summary>
        /// <param name="hydrationResult">Receives the extracted <see cref="NextJsHydrationResult"/></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetHydratedData(out NextJsHydrationResult hydrationResult)
        {
            hydrationResult = CachedHydration();
            return this;
        }

        /// <summary>
        /// Returns the hydration result for the current page, extracting it only on a cache miss so
        /// GetHydratedData followed by SaveHydratedData does not fetch and parse the page twice.
        /// </summary>
        private NextJsHydrationResult CachedHydration()
        {
            string key = GetSetCurrentUrl();

            if (false == string.IsNullOrEmpty(key) && true == _hydrationCache.TryGetValue(key, out NextJsHydrationResult cached))
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Hydrated data cache hit for [{UrlHelper.GetOrigin(CurrentUrl)}]", this, GPALObjectType.Browser);
                return cached;
            }

            // Read the hydration data from the document, without parsing HTML.
            NextJsHydrationResult result = NextJsHydrationExtractor.ExtractFromScripts(this, GetHydrationScripts());

            if (false == string.IsNullOrEmpty(key))
                _hydrationCache[key] = result;

            return result;
        }
        /// <summary>
        /// Returns the current page source for hydration extraction: the raw engine source with only the
        /// transport JSON-string escaping peeled off (so OttoMagic's wrapper is removed) and control characters
        /// stripped. Unlike <see cref="GetPageSource(out string)"/> it does NOT run the lenient unescape or
        /// HtmlDecode, because those collapse the <c>\"</c> inside embedded JSON such as <c>__NEXT_DATA__</c> and
        /// leave it unparseable. The peel stops as soon as real structural quotes appear, so the page's own
        /// escaped JSON is preserved intact and valid.
        /// </summary>
        /// <summary>
        /// The page title, __NEXT_DATA__ payload and React Flight chunks as one json string.<br/>
        /// No HTML is sent, so there is no escaping to undo. OttoMagic uses its own endpoint, so it does not need
        /// Allow user scripts.
        /// </summary>
        /// <returns>Json holding title, nextData and flightChunks, or empty when the page could not answer</returns>
        private string GetHydrationScripts()
        {
            string reported;

            if (true == UseOttoMagic)
                reported = MagicHelper.GetHydrationScripts();
            else if (true == UsePuppeteer)
                // Runtime.evaluate takes an expression, so the function is self-invoking
                reported = BrowserHelper.ExecuteJavaScriptObj(NextJsHydrationExtractor.HydrationScript, this)?.ToString();
            else
                // ExecuteScript takes a statement list and needs a return
                reported = BrowserHelper.ExecuteJavaScriptObj("return " + NextJsHydrationExtractor.HydrationScript + ";", this)?.ToString();

            return reported ?? string.Empty;
        }
        /// <summary>
        /// Extracts Next.js hydration data (e.g. __NEXT_DATA__) from the current page and saves it to a file.
        /// </summary>
        /// <param name="hydrationFile">The <see cref="GPALFile"/> to save the extracted hydration data to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveHydratedData(GPALFile hydrationFile)
        {
            NextJsHydrationExtractor.SaveToFile(CachedHydration(), hydrationFile.Filename);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved hydrated data [{hydrationFile.Filename}]", this, GPALObjectType.Browser);

            return this;
        }
        // The rule set WithDigestRules named; null picks one from the page's url
        private string _digestRules = null;

        /// <summary>
        /// Names the rule set from LLMDigestRules.yaml that <see cref="GetLLMDigest"/> and <see cref="SaveLLMDigest"/>
        /// use from here on, instead of the one picked from the page's url. Null goes back to picking it.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.WithDigestRules("generic")
        ///            .GetLLMDigest(out LLMDigestResult digest)
        ///            .SaveLLMDigest(digestFile);
        /// </code>
        /// </example>
        /// <param name="ruleSetName">The rule set's name, or null to pick one from the page's url</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithDigestRules(string ruleSetName)
        {
            _digestRules = ruleSetName;
            return this;
        }

        /// <summary>
        /// Converts the current page's HTML into an LLM-optimized markdown digest, with the rule set
        /// <see cref="WithDigestRules"/> named, or one picked from the page's url (falls back to "generic").
        /// </summary>
        /// <param name="digestResult">Receives the generated <see cref="LLMDigestResult"/></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetLLMDigest(out LLMDigestResult digestResult)
        {
            digestResult = CachedDigest(_digestRules);
            return this;
        }

        /// <summary>
        /// Returns the LLM digest for the current page (and rule set), building it only on a cache miss so
        /// GetLLMDigest followed by SaveLLMDigest does not fetch and convert the page twice.
        /// </summary>
        private LLMDigestResult CachedDigest(string ruleSetName)
        {
            string key = GetSetCurrentUrl() + "|" + (ruleSetName ?? string.Empty);

            if (true == _digestCache.TryGetValue(key, out LLMDigestResult cached))
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"LLM Digest cache hit for [{UrlHelper.GetOrigin(CurrentUrl)}]", this, GPALObjectType.Browser);
                return cached;
            }

            GetPageSource(out string pageSource);
            LLMDigestResult digestResult = HtmlToOptimizedMarkdown.CreateDigest(this, pageSource, ruleSetName);
            _digestCache[key] = digestResult;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"LLM Digest created for [{UrlHelper.GetOrigin(CurrentUrl)}]", this, GPALObjectType.Browser);

            return digestResult;
        }
        /// <summary>
        /// Converts the current page's HTML into an LLM-optimized markdown digest and saves it to a file, with the
        /// rule set <see cref="WithDigestRules"/> named, or one picked from the page's url (falls back to "generic").
        /// </summary>
        /// <param name="llmDigestFile">The <see cref="GPALFile"/> to save the digest to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveLLMDigest(GPALFile llmDigestFile)
        {
            HtmlToOptimizedMarkdown.SaveToFile(CachedDigest(_digestRules), llmDigestFile.Filename);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved LLM Digest [{llmDigestFile.Filename}]", this, GPALObjectType.Browser);

            return this;
        }
    }
}
