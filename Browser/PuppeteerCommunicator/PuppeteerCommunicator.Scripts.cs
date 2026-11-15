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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerCommunicator
    {
        /// <summary>
        /// Registers a JavaScript snippet to be evaluated automatically on every new document load for the given session,
        /// using <c>Page.addScriptToEvaluateOnNewDocument</c>. When Puppeteer is not in use, the equivalent Selenium
        /// Chromium CDP command is issued instead via the underlying <see cref="OpenQA.Selenium.Chromium.ChromiumDriver"/>.
        /// </summary>
        /// <param name="scriptToInject">The JavaScript source code to run on each new document.</param>
        /// <param name="sessionId">The CDP session (or target ID) to inject the script into; if null, the effective session ID is resolved automatically.</param>
        /// <returns>A task that completes with the CDP-assigned script identifier (for later removal via <see cref="RemoveInjectedScript"/>), or throws if the CDP/Selenium command fails.</returns>
        public async Task<string> EnableFetchScriptInjection(string scriptToInject, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            string identifier = null;
            try
            {
                if (true == Browser.UseSelenium)
                {
                    // Use Selenium CDP command for non-Puppeteer case
                    identifier = BrowserHelper.SeleniumAddScriptToEvaluateOnNewDocument(Browser.BrowserSettings, scriptToInject);
                }
                else if (true == Browser.UsePuppeteer)
                {
                    // Chrome runs a registered script only in a session with the Page domain enabled. Enabling it
                    // again is harmless.
                    await SendCommand<object>(DevToolsMethods.PageEnable, new { }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId).ConfigureAwait(false);

                    // Use Puppeteer CDP command directly
                    var result = await SendCommand<object>(DevToolsMethods.PageAddScriptToEvaluateOnNewDocument, new
                    {
                        source = scriptToInject
                    }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId).ConfigureAwait(false);
                    identifier = result?.identifier?.ToString();

                    // Kept so every tab attached later gets it too, see PrepareSession
                    var registered = new RegisteredScript { Source = scriptToInject };
                    registered.Identifiers[sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId] = identifier;
                    lock (_registeredScripts)
                        _registeredScripts.Add(registered);
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Script injected via CDP for sessionId: [{sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to inject script via CDP", this, GPALObjectType.Puppeteer, ex);
                throw;
            }

            return identifier;
        }

        /// <summary>
        /// Removes a script previously registered via <see cref="EnableFetchScriptInjection"/>, using
        /// <c>Page.removeScriptToEvaluateOnNewDocument</c>.
        /// </summary>
        /// <param name="identifier">The CDP script identifier returned by <see cref="EnableFetchScriptInjection"/>.</param>
        /// <param name="sessionId">The CDP session (or target ID) to remove the script from; if null, the effective session ID is resolved automatically.</param>
        public async Task RemoveInjectedScript(string identifier, string sessionId = null)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return;
            }

            sessionId ??= GetEffectiveSessionId();
            try
            {
                if (true == Browser.UseSelenium)
                {
                    BrowserHelper.SeleniumRemoveScriptToEvaluateOnNewDocument(Browser.BrowserSettings, identifier);
                }
                else if (true == Browser.UsePuppeteer)
                {
                    await SendCommand<object>(DevToolsMethods.PageRemoveScriptToEvaluateOnNewDocument, new
                    {
                        identifier
                    }, sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId).ConfigureAwait(false);
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Injected script [{identifier}] removed via CDP for sessionId: [{sessionId ?? ((PuppeteerClient)PuppeteerClient)._currentTargetId}]", this, GPALObjectType.PuppeteerCommunicator);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to remove injected script via CDP", this, GPALObjectType.Puppeteer, ex);
                throw;
            }
        }

        /// <summary>
        /// A script registered to run on every new document, with the identifier Chrome gave it in each tab session.
        /// Chrome runs a registered script only in the session it was registered in, so <see cref="PrepareSession"/>
        /// registers every one of them on each tab GPAL attaches to.
        /// </summary>
        private class RegisteredScript
        {
            public string Source;
            public bool Injected;   // registered by InjectScript, so ClearInjectedScripts removes it
            public Dictionary<string, string> Identifiers = new Dictionary<string, string>();
        }

        /// <summary>
        /// Registers a script via <see cref="EnableFetchScriptInjection"/> and marks it as injected so it can later be
        /// removed by <see cref="ClearInjectedScripts"/>.
        /// </summary>
        public async Task<string> InjectScript(string scriptToInject, string sessionId = null)
        {
            var identifier = await EnableFetchScriptInjection(scriptToInject, sessionId).ConfigureAwait(false);

            lock (_registeredScripts)
            {
                var registered = _registeredScripts.LastOrDefault(r => r.Source == scriptToInject);
                if (null != registered)
                    registered.Injected = true;
            }

            return identifier;
        }

        /// <summary>
        /// Removes every script registered via <see cref="InjectScript"/> from every open tab it was registered on.
        /// </summary>
        public async Task ClearInjectedScripts(string sessionId = null)
        {
            List<RegisteredScript> injected;
            lock (_registeredScripts)
            {
                injected = _registeredScripts.Where(r => true == r.Injected).ToList();
                _registeredScripts.RemoveAll(r => true == r.Injected);
            }

            // Closed tabs took their scripts with them, so only the sessions still open are sent the removal
            var openSessions = new HashSet<string>(_windowSessionsQueue.ToArray()
                .SelectMany(w => w.TabQueue.ToArray())
                .Where(t => null != t.Value)
                .SelectMany(t => new[] { t.Key, t.Value }));

            foreach (var registered in injected)
            {
                foreach (var pair in registered.Identifiers)
                {
                    if (true == openSessions.Contains(pair.Key))
                        await RemoveInjectedScript(pair.Value, pair.Key).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Evaluates an arbitrary JavaScript expression in the current page context and returns its value.
        /// </summary>
        /// <param name="expression">The JavaScript expression to evaluate.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP Runtime.evaluate result, including the returned value.</returns>
        public async Task<dynamic> ExecuteJavaScript(string expression, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new {
                    expression,
                    contextId = CurrentContextId,
                    returnByValue = true,
                    awaitPromise = true
                }
                , sessionId).ConfigureAwait(false);
        }
        // Small helper to prevent JS injection / quoting issues
        /// <summary>
        /// Escapes a string for safe inclusion inside a single-quoted JavaScript string literal, escaping
        /// backslashes, single quotes, carriage returns and newlines.
        /// </summary>
        /// <param name="value">The raw string value to escape.</param>
        /// <returns>The escaped string, or an empty string if <paramref name="value"/> is null or empty.</returns>
        private static string EscapeJsString(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n");
        }

        // Implementation for "evaluate"
        /// <summary>
        /// Evaluates an XPath expression against the document and returns the first matching node.
        /// </summary>
        /// <param name="xpath">The XPath expression to evaluate.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP Runtime.evaluate result for the first matching node, or null if none match.</returns>
        /// <example>
        /// <code>
        /// await communicator.Evaluate("//button[@id='submit']");
        /// </code>
        /// </example>
        public async Task<dynamic> Evaluate(string xpath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate,
                new {
                    contextId = CurrentContextId,
                    expression = $"document.evaluate('{xpath}', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue"
                }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "evaluate-all"
        /// <summary>
        /// Evaluates an XPath expression against the document and returns all matching nodes as an array.
        /// </summary>
        /// <param name="xpath">The XPath expression to evaluate.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that resolves to the CDP Runtime.evaluate result containing an array of all matching nodes.</returns>
        /// <example>
        /// <code>
        /// await communicator.EvaluateAll("//a[@class='item']");
        /// </code>
        /// </example>
        public async Task<dynamic> EvaluateAll(string xpath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            return await SendCommand<object>(DevToolsMethods.RuntimeEvaluate, 
                new {
                    contextId = CurrentContextId,
                    expression = $"Array.from(document.evaluate('{xpath}', document, null, XPathResult.ORDERED_NODE_SNAPSHOT_TYPE, null))" 
                }, sessionId).ConfigureAwait(false);
        }
    }
}
