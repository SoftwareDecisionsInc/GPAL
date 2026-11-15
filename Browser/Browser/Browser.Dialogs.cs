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
        // True once the dialog override is registered; later answer changes are written into the page.
        private bool _dialogRegistered = false;

        // The last answers written and the page url, to avoid rewriting the same answers to the same page.
        private string _dialogPushed = null;

        // Incremented per registration; the highest number wins whatever the script order.
        private int _dialogSeq = 0;

        /// <summary>
        /// Answers alert, confirm and prompt automatically so they do not block. True is OK (confirm returns true),
        /// false is Cancel. Without this, dialogs block as usual.
        /// <br/><br/>
        /// Selenium answers through a driver capability and Puppeteer through CDP, leaving the page's functions
        /// untouched and undetectable. On OttoMagic an open dialog blocks the extension's page thread, so GPAL
        /// replaces alert, confirm and prompt before the page runs; a page can detect that, so only OttoMagic does
        /// it.
        /// </summary>
        /// <param name="accept">True to accept, false to dismiss.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithDialogsAccepted(bool accept)
        {
            // Selenium reads this capability only at driver start, so warn if the browser is already running. Not
            // when prompt text is set: Selenium then answers in the page, where the answer can change.
            if (true == UseSelenium && null != BrowserSettings.BrowserDriver && false == InPageDialogs)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium takes this when the browser starts, and this one is already running, so dialogs are not answered. Call WithDialogsAccepted before the first GoTo", this, GPALObjectType.Browser);

            BrowserSettings.DialogsAccepted = accept;

            ArmDialogHandling();

            return this;
        }

        /// <summary>
        /// Text a prompt returns when dialogs are accepted. Unset, the prompt's default stands.
        /// </summary>
        /// <param name="text">The text to answer a prompt with.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector WithDialogText(string text)
        {
            BrowserSettings.DialogText = text;

            // Setting prompt text also turns on accepting dialogs, if WithDialogsAccepted was not called.
            if (null == BrowserSettings.DialogsAccepted)
                BrowserSettings.DialogsAccepted = true;

            ArmDialogHandling();

            return this;
        }

        /// <summary>
        /// Stops answering dialogs, so they block again. Unlike <see cref="WithDialogsAccepted(bool)"/>(false), which
        /// answers Cancel.
        /// <br/><br/>
        /// Puppeteer stops at once. On OttoMagic the replacements stay (the script cannot be removed alone) but pass
        /// calls to the originals; <see cref="ClearInjectedScripts"/> and navigate to remove them. Selenium cannot
        /// stop; the driver keeps the capability until it closes.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector ClearDialogHandling()
        {
            BrowserSettings.DialogsAccepted = null;
            BrowserSettings.DialogText = null;

            ArmDialogHandling();

            return this;
        }

        /// <summary>
        /// Sets up dialog answering on the current engine. Also called from WorkflowSetup, since the setting usually
        /// comes before the browser.
        /// </summary>
        internal void ArmDialogHandling()
        {
            // Return if the engine is unreachable, or dialogs are off and the override was never registered. A
            // registered override must be told to stop.
            if (false == EngineReachable || (null == BrowserSettings.DialogsAccepted && false == _dialogRegistered))
                return;

            if (true == InPageDialogs)
            {
                if (false == _dialogRegistered && null != BrowserSettings.DialogsAccepted)
                {
                    string script = DialogScript();

                    _dialogRegistered = true;

                    // The registered script covers new documents; also run it on an already loaded page.
                    InjectScript(script);

                    if (true == _navigated)
                        BrowserHelper.ExecuteJavaScript(this, script);

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answering dialogs in the page, accept [{BrowserSettings.DialogsAccepted}]", this, GPALObjectType.Browser);
                }

                // Each page's script sets the answers it was registered with, so they go stale when the workflow
                // changes them or the page changes. Rewrite when the answers or url changed since the last write.
                string written = DialogAnswers() + "@" + BrowserSettings.CurrentURL;

                if (true == _navigated && written != _dialogPushed)
                {
                    _dialogPushed = written;
                    PushDialogAnswers();
                }

                // OttoMagic only, once, after the first navigation: check the override is on the page. The
                // extension can silently refuse registration (usually Allow user scripts is off), and then the
                // first dialog blocks the workflow, so warn. Selenium registers over CDP, which cannot refuse.
                if (true == UseOttoMagic && true == _navigated && false == _dialogChecked)
                {
                    _dialogChecked = true;

                    // An expression, not a statement. OttoMagic evaluates the last expression, and a top-level
                    // return is a syntax error there
                    string seen = BrowserHelper.ExecuteJavaScript(this, "window.__gpalDialogSeq");

                    if (true == string.IsNullOrEmpty(seen) || "null" == seen)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The dialog override is not on the page, so dialogs will stop this workflow. The usual cause is user scripts being off; turn them on with the Turn On User Scripts button in OttoMagic's settings", this, GPALObjectType.Browser);
                }
            }
            else if (true == UsePuppeteer && null != BrowserSettings.DialogsAccepted && false == _dialogRegistered)
            {
                // Page.javascriptDialogOpening does not arrive until the domain is on, and the launch paths do
                // not turn it on
                _dialogRegistered = true;

                PuppeteerCommunicator.EnablePageEvents().GetAwaiter().GetResult();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Answering dialogs over CDP, accept [{BrowserSettings.DialogsAccepted}]", this, GPALObjectType.Browser);
            }

            // selenium answering yes or no is armed at launch, because UnhandledPromptBehavior is a capability
            // on the driver options and nothing after that can change it
        }

        /// <summary>
        /// True when the in-page override answers dialogs instead of the browser.
        /// <br/><br/>
        /// Always for OttoMagic, since a dialog blocks its extension's page thread. For Selenium, only with
        /// WithDialogText, since its capability cannot type prompt text. Never for Puppeteer, which sends prompt
        /// text over CDP.
        /// </summary>
        private bool InPageDialogs => true == UseOttoMagic
            || (true == UseSelenium && null != BrowserSettings.DialogText);

        // whether the override was found in place on the page after the first navigation
        private bool _dialogChecked = false;

        /// <summary>
        /// Writes the current answers into the open page, which ran the registered script with the old answers when
        /// it loaded.
        /// </summary>
        private void PushDialogAnswers()
        {
            // Write only if the override is already on the page (window.__gpalDialogSeq); never installs it.
            BrowserHelper.ExecuteJavaScript(this, "if (window.__gpalDialogSeq) { " + DialogAnswers() + " }");
        }

        /// <summary>
        /// JavaScript setting window.__gpalDialogAccept and window.__gpalDialogAnswer. Written to the page, and
        /// compared as a string to detect changes.
        /// </summary>
        /// <returns>JavaScript assigning the current answers.</returns>
        private string DialogAnswers()
        {
            string accept = null == BrowserSettings.DialogsAccepted
                ? "null"
                : true == BrowserSettings.DialogsAccepted ? "true" : "false";
            string typed = null == BrowserSettings.DialogText ? "null" : JsonConvert.SerializeObject(BrowserSettings.DialogText);

            return "window.__gpalDialogAccept = " + accept + "; window.__gpalDialogAnswer = " + typed + ";";
        }

        /// <summary>
        /// The script that replaces alert, confirm and prompt before the page runs, so no dialog opens. The highest
        /// registration number wins, so the newest answers apply whatever the script order.
        /// </summary>
        /// <returns>JavaScript source to register on every new document.</returns>
        private string DialogScript()
        {
            return
                "(function () {" +
                "  var seq = " + (++_dialogSeq) + ";" +
                "  if (window.__gpalDialogSeq >= seq) return;" +
                "  window.__gpalDialogSeq = seq;" +
                "  " + DialogAnswers() +
                "  var asText = Function.prototype.toString;" +
                "  function stand(name, fn) {" +
                "    Object.defineProperty(fn, 'name', { value: name, configurable: true });" +
                // Define the replacement where the original is. Chrome has these as own properties of window,
                // so one on Window.prototype would be shadowed and never called. It also looks like the original
                // to the page.
                "    var onto = Object.getOwnPropertyDescriptor(window, name) ? window : Window.prototype;" +
                "    Object.defineProperty(onto, name," +
                "      { value: fn, writable: true, enumerable: true, configurable: true });" +
                "  }" +
                // Keep the originals, for the replacements to call when dialog handling is off.
                "  var real = { alert: window.alert, confirm: window.confirm, prompt: window.prompt };" +
                "  function off() { return null === window.__gpalDialogAccept; }" +
                "  stand('alert', function (message) {" +
                "    if (off()) return real.alert.call(window, message);" +
                "  });" +
                "  stand('confirm', function (message) {" +
                "    if (off()) return real.confirm.call(window, message);" +
                "    return true === window.__gpalDialogAccept;" +
                "  });" +
                "  stand('prompt', function (message, preset) {" +
                "    if (off()) return real.prompt.call(window, message, preset);" +
                "    if (true !== window.__gpalDialogAccept) return null;" +
                "    return null !== window.__gpalDialogAnswer ? window.__gpalDialogAnswer : (preset || '');" +
                "  });" +
                // toString on the replacements returns "[native code]" like the originals. Harder to detect, not
                // undetectable.
                "  Function.prototype.toString = function () {" +
                "    if (this === window.alert || this === window.confirm || this === window.prompt" +
                "        || this === Function.prototype.toString)" +
                "      return 'function ' + this.name + '() { [native code] }';" +
                "    return asText.call(this);" +
                "  };" +
                "})();";
        }
    }
}
