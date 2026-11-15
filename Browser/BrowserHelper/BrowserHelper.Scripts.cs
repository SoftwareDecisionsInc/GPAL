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
        /// Registers a script to be evaluated automatically on every new document load, using the Selenium
        /// Chromium CDP command <c>Page.addScriptToEvaluateOnNewDocument</c>. Chrome/Edge only.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings for the browser to inject the script into</param>
        /// <param name="script">The JavaScript source code to run on each new document</param>
        /// <returns>The CDP-assigned script identifier (for later removal via <see cref="SeleniumRemoveScriptToEvaluateOnNewDocument"/>), or null if not supported for this BrowserType</returns>
        public static string SeleniumAddScriptToEvaluateOnNewDocument(BrowserSettings browserSettings, string script)
        {
            if (BrowserType.Chrome != browserSettings.BrowserType && BrowserType.Edge != browserSettings.BrowserType)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] does not support Page.addScriptToEvaluateOnNewDocument", browserSettings.Browser, GPALObjectType.Browser);
                return null;
            }

            var result = BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object>
            {
                { "source", script }
            }) as Dictionary<string, object>;

            string identifier = result?["identifier"]?.ToString();

            // chromedriver registered it on the current tab only; tabs opened later get it from TabScripts
            browserSettings.TabScripts ??= SeleniumTabScripts.Start(browserSettings);
            browserSettings.TabScripts?.Add(script, identifier);

            return identifier;
        }

        /// <summary>
        /// Removes a script previously registered via <see cref="SeleniumAddScriptToEvaluateOnNewDocument"/>, using
        /// the Selenium Chromium CDP command <c>Page.removeScriptToEvaluateOnNewDocument</c>. Chrome/Edge only.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings for the browser to remove the script from</param>
        /// <param name="identifier">The CDP script identifier returned by <see cref="SeleniumAddScriptToEvaluateOnNewDocument"/></param>
        public static void SeleniumRemoveScriptToEvaluateOnNewDocument(BrowserSettings browserSettings, string identifier)
        {
            if (BrowserType.Chrome != browserSettings.BrowserType && BrowserType.Edge != browserSettings.BrowserType)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{browserSettings.BrowserType}] does not support Page.removeScriptToEvaluateOnNewDocument", browserSettings.Browser, GPALObjectType.Browser);
                return;
            }

            BrowserHelper.ExecuteCdp(browserSettings.BrowserDriver, "Page.removeScriptToEvaluateOnNewDocument", new Dictionary<string, object>
            {
                { "identifier", identifier }
            });

            browserSettings.TabScripts?.Remove(identifier);
        }
        /// <summary>
        /// Executes javascript in the page and returns the raw result object (no string conversion).<br/>
        /// Routed through OttoMagic's ExecuteJavaScript, Puppeteer's ExecuteJavaScript or Selenium's IJavaScriptExecutor
        /// depending on the browser's automation engine.
        /// </summary>
        /// <param name="executeMe">The javascript to execute</param>
        /// <param name="browser">Browser to execute the javascript in</param>
        /// <param name="args">Optional arguments passed to the script; OttoMagic does not pass them</param>
        /// <returns>The raw result of the javascript execution, or null on failure</returns>
        public static object ExecuteJavaScriptObj(string executeMe, IBrowser browser, params object[] args)
        {
            dynamic retVal = null;

            try
            {
                if (browser.UseOttoMagic)
                {
                    retVal = browser.MagicHelper.ExecuteJavaScript(executeMe);
                }
                else if (true == browser.UsePuppeteer)
                {
                    retVal = browser.PuppeteerClient.ExecuteJavaScript(executeMe).WithParameters(args).Execute<object>();
                    retVal = retVal.result.value;
                }
                else // selenium
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)browser.BrowserDriver;
                    retVal = js.ExecuteScript(executeMe, args);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Use this browser's SuppressJavaScriptFailures; a non-Browser argument always publishes.
                bool suppressed = true == (browser as Browser)?.BrowserSettings.SuppressJavaScriptFailures;

                if (false == suppressed)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $@"Failed to execute javascript. Continuing.", browser, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
        /// <summary>
        /// Executes javascript in the page and returns the result as a string.<br/>
        /// Routed through Puppeteer's ExecuteJavaScript or Selenium's IJavaScriptExecutor depending on the browser's
        /// automation engine. Returns "fail" if execution throws.
        /// </summary>
        /// <returns>The string result of the javascript execution, or "fail" on failure</returns>
        /// <example>
        /// <code>
        /// string title = BrowserHelper.ExecuteJavaScript(browser, "return document.title");
        /// </code>
        /// </example>
        /// <summary>
        /// Returns a script result as a plain value on every engine, unwrapping Puppeteer's CDP RemoteObject and any
        /// JSON encoding.
        /// </summary>
        /// <param name="result">What the engine handed back.</param>
        /// <returns>The value the script returned.</returns>
        private static string UnwrapScriptResult(string result)
        {
            string retVal = result;


            // Unwrap only a RemoteObject: type and value, with no keys beyond type, value, description, className
            // and subtype. Any other object is the script's own return value.
            if (false == string.IsNullOrWhiteSpace(retVal) && true == retVal.TrimStart().StartsWith("{"))
            {
                try
                {
                    Newtonsoft.Json.Linq.JObject remote = Newtonsoft.Json.Linq.JObject.Parse(retVal);

                    if (null != remote["type"] && null != remote["value"]
                        && remote.Properties().All(each => "type" == each.Name || "value" == each.Name || "description" == each.Name || "className" == each.Name || "subtype" == each.Name))
                    {
                        Newtonsoft.Json.Linq.JToken value = remote["value"];

                        retVal = Newtonsoft.Json.Linq.JTokenType.String == value.Type
                            ? value.ToString()
                            : Newtonsoft.Json.JsonConvert.SerializeObject(value);
                    }
                }
                catch (Exception)
                {
                    // If unwrapping fails, return the result unchanged.
                }
            }

            // Decode JSON string layers until it is no longer quoted.
            while (false == string.IsNullOrEmpty(retVal) && retVal.StartsWith("\"") && retVal.EndsWith("\""))
            {
                try
                {
                    string peeled = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

                    if (peeled == retVal)
                        break;

                    retVal = peeled;
                }
                catch (Exception)
                {
                    break;   // not an encoded string after all, so it is the value already
                }
            }

            return retVal;
        }

        /// <summary>
        /// Runs a script and returns its result.
        /// <br/><br/>
        /// Selenium runs it as a function body and needs a return. OttoMagic and Puppeteer evaluate it like eval,
        /// where V8 rejects a return; when they report "Illegal return statement", the script is wrapped in a
        /// function and rerun. Either style works on all three.
        /// <br/><br/>
        /// The script text is never inspected.
        /// </summary>
        /// <param name="browser">Browser to run the script in</param>
        /// <param name="executeMe">The script</param>
        /// <param name="args">Arguments the script refers to, where the engine supports them</param>
        /// <returns>What the script produced</returns>
        public static string ExecuteJavaScript(IBrowser browser, string executeMe, params object[] args)
        {
            string retVal = RunScript(browser, executeMe, args);

            // V8 raises this message only for a return outside a function
            if (false == browser.UseSelenium && true == retVal?.Contains("Illegal return statement"))
                retVal = RunScript(browser, "(function(){" + Environment.NewLine + executeMe + Environment.NewLine + "})()", args);

            return retVal;
        }

        private static string RunScript(IBrowser browser, string executeMe, params object[] args)
        {
            dynamic retVal = null;

            try
            {
                if (browser.UseOttoMagic)
                {
                    retVal = browser.MagicHelper.ExecuteJavaScript(executeMe);
                }
                else if (true == browser.UsePuppeteer)
                {
                    if (true == args.Any())
                        retVal = browser.PuppeteerClient.ExecuteJavaScript(executeMe).WithParameters(args).Execute<object>();
                    else
                        retVal = browser.PuppeteerClient.ExecuteJavaScript(executeMe).Execute<object>();

                    // the evaluate reply's result, whole
                    retVal = ((Newtonsoft.Json.Linq.JObject)retVal.result).ToObject<object>();
                }
                else // selenium
                {
                    IJavaScriptExecutor js = (IJavaScriptExecutor)browser.BrowserDriver;
                    if (true == args?.Any())
                        retVal = js.ExecuteScript(executeMe, args);
                    else
                        retVal = js.ExecuteScript(executeMe);


                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Use this browser's SuppressJavaScriptFailures; a non-Browser argument always publishes.
                bool suppressed = true == (browser as Browser)?.BrowserSettings.SuppressJavaScriptFailures;

                if (false == suppressed)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $@"Failed to execute javascript. Continuing.", browser, GPALObjectType.Browser, ex);
                retVal = "fail";
            }

            return UnwrapScriptResult(retVal?.ToString());
        }

        /// <summary>
        /// Sends a CDP command through a Selenium driver and returns the result.<br/>
        /// ChromeDriver and EdgeDriver use ExecuteCdpCommand. On a hidden desktop the driver is a RemoteWebDriver
        /// with no ExecuteCdpCommand, so the command goes through the custom command GetWebDriver registers.
        /// </summary>
        /// <param name="driver">The Selenium driver of a Chrome or Edge browser</param>
        /// <param name="command">The CDP command, e.g. Page.setDownloadBehavior</param>
        /// <param name="parameters">The command's parameters</param>
        /// <returns>What the browser returned for the command</returns>
        internal static object ExecuteCdp(IWebDriver driver, string command, Dictionary<string, object> parameters)
        {
            object result = null;

            if (driver is ChromiumDriver chromium)
                result = chromium.ExecuteCdpCommand(command, parameters);
            else
                result = ((ICustomDriverCommandExecutor)driver).ExecuteCustomDriverCommand(HiddenCdpCommand, new Dictionary<string, object> { ["cmd"] = command, ["params"] = parameters });

            return result;
        }
    }
}
