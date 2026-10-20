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
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Fluent REST client for making API calls with a chained interface.
    /// Supports defining workflows, executing them in loops, and accessing results.
    /// Results are accessed via a non-generic indexer, requiring casting to the desired type.
    /// </summary>
    /// <example>
    /// <code>
    /// RESTClient client = GPAL.RESTClient
    ///     .WithAPIBase(browser.RestApiBaseUrl)
    ///     .WithName("JavaScriptClient")
    ///     // Execute JavaScript to get the page title and store it as "pageTitle"
    ///     .WithEndpoint(ApiEndpoint.ExecuteJavaScript) // Uses RESTHelper.Endpoints to map to "/execute-javascript"
    ///     .WithParameters("return document.title", new object[] { }) // Pass the JavaScript code and arguments
    ///     .WithResultName("pageTitle")
    ///     .AndThen&lt;string&gt;()
    ///     // Chain the result: use the page title in an input text operation
    ///     .WithEndpoint(ApiEndpoint.InputText) // Uses RESTHelper.Endpoints to map to "/input-text"
    ///     .WithXPath("//input[@id='search']")
    ///     .WithTextFromResult("pageTitle") // Uses the stored "pageTitle" result
    ///     .Execute();
    ///
    /// var results = client.GetExecutionResults();
    /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Page Title: [{(string)results["pageTitle"]}]");
    /// GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Page Title from iteration 0: [{(string)results["pageTitle", 0]}]");
    /// </code>
    /// </example>
    public class RESTClient : IRESTClient
    {
        private readonly List<Action<RESTClient>> _workflows = new List<Action<RESTClient>>();
        private readonly List<(string Endpoint, object Parameters, string HttpMethod, string NextResultName)?> _workflowStates = new List<(string, object, string, string)?>();
        private bool _customEndpoint = false;
        private string _endpoint;
        private object _parameters;
        private string _httpMethod = "GET";
        internal ContentEncoding _contentEncoding = ContentEncoding.Json; // Default to JSON
        private string _name;
        private string _saveFile { get; set; }
        private string _nextResultName;
        private readonly List<(string Name, object Result, int Iteration)> _executionResults = new List<(string Name, object Result, int Iteration)>();
        private RESTHelper _helper;
        private int _currentIteration;
        internal Dictionary<string, string> _headers = new Dictionary<string, string>();
        public int StatusCode
        {
            get => _helper.StatusCode;
        }

        public int RetryAfterSeconds
        {
            get => _helper.RetryAfterSeconds;
        }

        internal Dictionary<string, (string[] Mandatory, string[] Optional, Func<IDictionary<string, object>, bool> CustomValidation)>
            openApiParamRules = new Dictionary<string, (string[] Mandatory, string[] Optional, Func<IDictionary<string, object>, bool> CustomValidation)>(StringComparer.OrdinalIgnoreCase);

        Dictionary<ApiEndpoint, (string[] Mandatory, string[] Optional, Func<IDictionary<string, object>, bool> CustomValidation)> paramRules = new Dictionary<ApiEndpoint, (string[] Mandatory, string[] Optional, Func<IDictionary<string, object>, bool> CustomValidation)>
        {
            [ApiEndpoint.Back] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.CaptureVisibleTab] = (new string[] { }, new string[] { "windowId", "imageFormat" }, null),
            [ApiEndpoint.CastDesktop] = (new[] { "sinkName" }, new string[] { }, null),
            [ApiEndpoint.CastTab] = (new[] { "sinkName" }, new string[] { }, null),
            [ApiEndpoint.CheckNetworkIdle] = (
                Mandatory: new string[] { },
                Optional: new[] { "maxConnections", "pruneMs", "sessionToken" },
                CustomValidation: null
            ),
            [ApiEndpoint.CheckStatus] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.ClearReferrer] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.CloseBrowser] = (
                Mandatory: new string[] { },
                Optional: new string[] { },
                CustomValidation: null
            ),
            [ApiEndpoint.CloseTab] = (
                Mandatory: new string[] { },
                Optional: new[] { "tabId", "url" },
                CustomValidation: null
            ),
            [ApiEndpoint.CloseWindow] = (
                Mandatory: new string[] { },
                Optional: new[] { "windowId", "url" },
                CustomValidation: null
            ),
            // NOTE: magic allows no storageType to delete all storage by not specifying storageType
            // NOTE: at this point, we don't want to accidentally delete all, so we enforce storageType as mandatory, but all of a storageType can be deleted
            //      BROWSER.browsingData.remove({ origins }, {
            //          cache: true,
            //          cookies: true,
            //          localStorage: true,
            //          serviceWorkers: true
            //      });
            [ApiEndpoint.DeleteStorage] = (new[] { "storageType" }, new[] { "domain", "path", "key", "storeName" }, null),
            [ApiEndpoint.DownloadUrl] = (new[] { "url", "downloadPath" }, new string[] { }, null),
            [ApiEndpoint.DragAndDrop] = (new[] { "deltaX", "deltaY" }, new[] { "css", "xpath", "elementId", "offsetX", "offsetY" }, null),
            [ApiEndpoint.Evaluate] = (new string[] { }, new string[] { "xpath", "elementId" }, dict => true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.EvaluateAll] = (new string[] { }, new string[] { "xpath", "elementId" }, dict => true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.EvaluatePersistent] = (new string[] { }, new string[] { "xpath", "elementId" }, dict => true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.EvaluateAllPersistent] = (new string[] { }, new string[] { "xpath", "elementId" }, dict => true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            // OttoMagic reads "script" and Puppeteer reads "expression"; one rule checks both, so either is accepted.
            // "parameters" is optional.
            [ApiEndpoint.ExecuteJavaScript] = (new string[] { }, new[] { "script", "expression", "parameters" }, dict => true == dict.ContainsKey("script") || true == dict.ContainsKey("expression")),
            [ApiEndpoint.Fetch] = (new[] { "url" }, new[] { "method", "body", "contentType", "headers", "bytes" }, null), // GET with no body when unsaid, text unless bytes were asked for
            [ApiEndpoint.FillInAppend] = (new[] { "text" }, new string[] { "css", "xpath", "elementId", "gpalElements", "typingDelay" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.FillInInsert] = (new[] { "text" }, new string[] { "css", "xpath", "elementId", "gpalElements", "typingDelay" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.FillInOverwrite] = (new[] { "text" }, new string[] { "css", "xpath", "elementId", "gpalElements", "typingDelay" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.FireChangeEvent] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.Focus] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.Forward] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.FullScreen] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetAttribute] = (new[] { "attribute" }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.GetBoundingClientRect] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.ElementFromPoint] = (new[] { "x", "y" }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.GetBrowserSettings] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetCssAttributes] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.GetContentAndCss] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.GetCurrentUrl] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetCurrentWindow] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetDocumentStamp] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetHydrationScripts] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetDomAttributes] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.GetDomProperties] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.GetElementAttributeHash] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.GetGpalSettings] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetPageSource] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetParentNode] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.GetReadyStatus] = (new string[] { }, new string[] { "sessionToken" }, null),
            [ApiEndpoint.GetStatusCode] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetSettings] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetShadowRoot] = (new string[] { }, new string[] { "css", "xpath", "elementId" }, null),
            [ApiEndpoint.GetStorage] = (new[] { "storageType" }, new[] { "domain", "path", "key", "storeName" }, null),
            [ApiEndpoint.GetLanguages] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.CaptureCalls] = (new string[] { }, new[] { "capture", "filter", "clear" }, null),   // no filter records everything
            [ApiEndpoint.GetCapturedCalls] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetUserAgent] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GetWorkflow] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.GoTo] = (new[] { "url" }, new string[] { }, null),
            [ApiEndpoint.GoToTab] = (
                Mandatory: new string[] { },
                Optional: new[] { "tabId", "url" },
                CustomValidation: dict => true == dict.ContainsKey("tabId") || true == dict.ContainsKey("url")
            ),
            [ApiEndpoint.GoToWindow] = (
                Mandatory: new string[] { },
                Optional: new[] { "url", "windowId" },
                CustomValidation: dict => dict.ContainsKey("url") != dict.ContainsKey("windowId") // Exactly one must be present
            ),
            [ApiEndpoint.GetWindowRectangle] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.HideElement] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.Hover] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.InjectScript] = (new[] { "script" }, new string[] { }, null),
            [ApiEndpoint.ClearInjectedScripts] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.IsClickable] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.IsDisplayed] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.IsEnabled] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.IsEndOfPage] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.IsVisibleInViewport] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.LeftClick] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements", "modifiers" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.LeftClickAndDownload] = (new[] { "downloadPath" }, new string[] { "css", "xpath", "elementId", "gpalElements", "modifiers" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.LeftDoubleClick] = (new string [] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.Maximize] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.MiddleClick] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements", "modifiers" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.Minimize] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.MoveTo] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.NewTab] = (new string[] { }, new[] { "url" }, null),
            [ApiEndpoint.NextTab] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.NextWindow] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.Normal] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.OpenWindow] = (new string[] { }, new[] { "url" }, null),
            [ApiEndpoint.OverrideReferrer] = (new[] { "url" }, new string[] { }, null),
            [ApiEndpoint.PageDown] = (new string[] { }, new string[] { "pagesToScroll" }, null),
            [ApiEndpoint.PageEnd] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.PageTop] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.PageUp] = (new string[] { }, new string[] { "pagesToScroll" }, null),
            [ApiEndpoint.PressModifierKey] = (new[] { "modifierKeys" }, new string[] { }, null),
            [ApiEndpoint.PreviousTab] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.PreviousWindow] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.QuerySelector] = (new string[] { }, new string[] { "css", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.QuerySelectors] = (new string[] { }, new string[] { "css", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.QueryPersistentSelector] = (new string[] { }, new string[] { "css", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.QueryPersistentSelectors] = (new string[] { }, new string[] { "css", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.Refresh] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.ReleaseModifierKey] = (new[] { "modifierKeys" }, new string[] { }, null),
            [ApiEndpoint.Restore] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.RightClick] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements", "modifiers" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.ScrollWindowByHorizontal] = (new[] { "hPixels" }, new string[] { "vPixels" }, null),
            [ApiEndpoint.ScrollWindowByVertical] = (new[] { "vPixels" }, new string[] { "hPixels" }, null),
            [ApiEndpoint.ScrollElement] = (
                Mandatory: new string[] { },
                Optional: new string[] { "css", "xpath", "elementId", "gpalElements", "hPixels", "vPixels" },
                CustomValidation: null
            ),
            [ApiEndpoint.ScrollIntoView] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.ScrollWindow] = (
                Mandatory: new string[] { },
                Optional: new[] { "hPixels", "vPixels" },
                CustomValidation: null
            ),
            [ApiEndpoint.SelectClick] = ( new string[] { }, new[] { "css", "xpath", "elementId", "gpalElements", "selectIndex", "selectValue" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements") || true == dict.ContainsKey("selectIndex") || true == dict.ContainsKey("selectValue")),
            [ApiEndpoint.SendKey] = (new [] { "key", "code", "vk" }, new string[] { },  null),
            [ApiEndpoint.SendString] = (new[] { "text" }, new[] { "typingDelay" }, null),
            [ApiEndpoint.SetAttribute] = (new[] { "attribute", "value" }, new string[] { "css", "xpath", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.SetDownloadFilename] = (new[] { "downloadPath" }, new string[] { }, null),
            [ApiEndpoint.SetRange] = (new[] { "rangeValue" }, new string[] { "css", "xpath", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.SetStorage] = (new[] { "storageType", "key", "data" }, new[] { "domain", "path", "storeName" }, null), // local/sessionStorage are simple key/value pairs, so that is the only ones we can make mandatory
            [ApiEndpoint.SetStorageCache] = (new[] { "storageType", "key", "data", "storeName" }, new[] { "domain", "path" }, null),
            [ApiEndpoint.SetStorageIndexedDb] = (new[] { "storageType", "key", "data", "path", "storeName" }, new[] { "domain" }, null),
            [ApiEndpoint.SetUserAgent] = (new[] { "userAgent" }, new string[] { }, null),
            [ApiEndpoint.SetValueFromElement] = (new[] { "destSelector" }, new string[] { "css", "xpath", "elementId", "gpalElements" }, null),
            [ApiEndpoint.SetGoogleReferrer] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.StopCasting] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.SwitchToDefaultContent] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.SwitchToElement] = (new string[] { }, new string[] { "css", "xpath", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.SwitchToFrame] = (new string[] { }, new string[] { "css", "xpath", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.SwitchToShadowRoot] = (new string[] { }, new string[] { "css", "xpath", "elementId" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId")),
            [ApiEndpoint.TabCount] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.TopBrowser] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.MoveToPoint] = (new[] { "x", "y" }, new string[] { }, null),
            // Allow modifiers: the point click always sends them, including every Puppeteer point click (e.g. an image
            // match).
            [ApiEndpoint.ClickPoint] = (new[] { "x", "y" }, new string[] { "clickType", "modifiers" }, null),
            [ApiEndpoint.TrustedLeftClick] = (new[] { "x", "y" }, new string[] { "holdMs" }, null),   // the extension clicks a viewport point and holds for holdMs; it reads no element
            [ApiEndpoint.TrustedMiddleClick] = (new[] { "x", "y" }, new string[] { "holdMs" }, null), // the same, with the middle button
            [ApiEndpoint.Upload] = (new string[] { }, new string[] { "css", "xpath", "elementId", "gpalElements", "uploadPath", "uploadPaths" }, dict => true == dict.ContainsKey("css") || true == dict.ContainsKey("xpath") || true == dict.ContainsKey("elementId") || true == dict.ContainsKey("gpalElements")),
            [ApiEndpoint.WindowInnerHeight] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowInnerWidth] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowOuterHeight] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowOuterWidth] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowPageOffsetX] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowPageOffsetY] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowScreenLeft] = (new string[] { }, new string[] { }, null),
            [ApiEndpoint.WindowScreenTop] = (new string[] { }, new string[] { }, null),
        };


        internal RESTClient()
        {
        }

        /// <summary>
        /// Gets or sets the client name
        /// </summary>
        public string Name
        {
            get => _name;
            internal set => _name = value;
        }

        /// <summary>
        /// Sets the API base URL with download path
        /// </summary>
        /// <param name="url">The base URL</param>
        /// <returns></returns>
        public IAllowRESTEndpointDetails WithAPIBase(string url)
        {
            _helper = new RESTHelper(url, this);
            return this;
        }

        /// <summary>
        /// Sets the API base URL and how long one attempt is given before it is abandoned.
        /// </summary>
        /// <param name="url">The base URL</param>
        /// <param name="httpTimeout">How long one attempt is given. 60 seconds without this. Pass
        /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for no limit, which is what a call that
        /// takes as long as it takes needs</param>
        /// <returns></returns>
        public IAllowRESTEndpointDetails WithAPIBase(string url, TimeSpan httpTimeout)
        {
            _helper = new RESTHelper(url, this, httpTimeout);
            return this;
        }

        /// <summary>
        /// Sets the endpoint as a string
        /// </summary>
        /// <param name="endpoint">The endpoint string</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithEndpoint(string endpoint)
        {
            _endpoint = endpoint;
            _customEndpoint = true;
            return this;
        }

        /// <summary>
        /// Sets the endpoint from an ApiEndpoint enum
        /// </summary>
        /// <param name="endpoint">The ApiEndpoint enum value</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithEndpoint(ApiEndpoint endpoint)
        {
            if (null == _helper)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}RESTHelper not initialized. Call WithAPIBase first.",
                    null, GPALObjectType.None);
                _endpoint = "";
            }
            else if (false == RESTHelper.Endpoints.ContainsKey(endpoint))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}API endpoint [{endpoint}] not found in Endpoints dictionary. Using empty string.",
                    null, GPALObjectType.None);
                _endpoint = "";
            }
            else
            {
                _endpoint = RESTHelper.Endpoints[endpoint];
            }
            return this;
        }

        public IAllowRESTEndpointDetails LoadOpenAPIMap(string yamlOrUrl)
        {
            if (string.IsNullOrWhiteSpace(yamlOrUrl))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "LoadOpenAPIMap: yamlOrUrl is empty", null, GPALObjectType.Other);
                return this;
            }

            string yamlContent;
            string chosenBaseUrl = null;

            if (yamlOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // It's already a direct URL to the YAML
                try
                {
                    using var http = new HttpClient();
                    yamlContent = http.GetStringAsync(yamlOrUrl).GetAwaiter().GetResult();
                    chosenBaseUrl = ExtractBestServerUrl(yamlContent);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"Failed to download OpenAPI spec from [{yamlOrUrl}]", ex.Message, GPALObjectType.Other, ex);
                    return this;
                }
            }
            else
            {
                // Assume it's raw YAML content
                yamlContent = yamlOrUrl;
                chosenBaseUrl = ExtractBestServerUrl(yamlContent);
            }

            // Set the RESTHelper with the best base URL (just like WithAPIBase does)
            if (!string.IsNullOrWhiteSpace(chosenBaseUrl))
            {
                _helper = new RESTHelper(chosenBaseUrl, this);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Set API base to [{chosenBaseUrl}]");
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    "LoadOpenAPIMap could not determine API base from servers section", null, GPALObjectType.Other);
            }

            // Now load the rules
            LoadOpenApiRulesInternal(yamlContent);

            return this;
        }

        public IAllowRESTEndpointDetails LoadOpenAPIMap(GPALFile openApiMapFile)
        {

            try
            {
                string yamlContent = File.ReadAllText(openApiMapFile.Filename);
                return LoadOpenAPIMap(yamlContent);   // reuse the string overload
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Failed to read OpenAPI file [{openApiMapFile.Filename}]", ex.Message, GPALObjectType.Other, ex);
                return this;
            }
        }

        public void LoadOpenApiRulesInternal(string yamlContent)
        {
            try
            {
                var reader = new Microsoft.OpenApi.Readers.OpenApiStreamReader();
                using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(yamlContent));
                var document = reader.Read(stream, out var diagnostic);

                if (diagnostic.Errors.Any())
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"OpenAPI parse issues: [{diagnostic.Errors.Count}] [{string.Join(",",diagnostic.Errors)}]", diagnostic, GPALObjectType.Other);
                }

                openApiParamRules.Clear();

                foreach (var pathItem in document.Paths)
                {
                    foreach (var opKv in pathItem.Value.Operations)
                    {
                        var method = opKv.Key.ToString().ToUpperInvariant();
                        var path = pathItem.Key.TrimStart('/');
                        var ruleKey = $"{method} {path}";   // e.g. "GET /api/basicstore/products"

                        var mandatory = new List<string>();
                        var optional = new List<string>();

                        foreach (var param in opKv.Value.Parameters)
                        {
                            if (param.Required)
                                mandatory.Add(param.Name);
                            else
                                optional.Add(param.Name);
                        }

                        if (opKv.Value.RequestBody?.Required == true)
                            mandatory.Add("body");   // simple body marker

                        openApiParamRules[ruleKey] = (
                            Mandatory: mandatory.ToArray(),
                            Optional: optional.ToArray(),
                            CustomValidation: null
                        );
                    }
                }

                GPAL.PublishSimpleEvent(GPALEventType.INFO,
                    $"Loaded OpenAPI rules for [{openApiParamRules.Count}] operations from spec",
                    null, GPALObjectType.Other);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    "Failed to load OpenAPI parameter rules", yamlContent?.Length ?? 0, GPALObjectType.Other, ex);
            }
        }

        public IAllowRESTEndpointDetails ListOpenAPIEndPoints()
        {
            foreach (var key in openApiParamRules.Keys)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, key);

            return this;
        }

        /// <summary>
        /// Sets the result name for the next execution
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithResultName(string name)
        {
            _nextResultName = name;
            return this;
        }

        /// <summary>
        /// Sets the URL parameter
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithUrl(string url)
        {
            _parameters = new { url };
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the URL from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithUrlFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithUrlFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithUrl("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var url = null == previousResult ? "" : previousResult.ToString();
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the URL from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithUrlFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithUrlFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithUrl("");
            }
            var previousResult = resultEntry.Result;
            var url = null == previousResult ? "" : previousResult.ToString();
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the tab ID parameter
        /// </summary>
        /// <param name="tabId">The tab ID</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithTabId(int tabId)
        {
            _parameters = new { tabId };
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the tab ID from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithTabIdFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithTabIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithTabId(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var tabId = previousResult is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the tab ID from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithTabIdFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithTabIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithTabId(0);
            }
            var previousResult = resultEntry.Result;
            var tabId = previousResult is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the script parameter
        /// </summary>
        /// <param name="script">The script</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithScript(string script)
        {
            _parameters = new { script, parameters = _parameters };
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the key parameter
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithKey(string key)
        {
            _parameters = null == _parameters
                ? new { key }
                : MergeParameters(_parameters, new { key }); 
            
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the key from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithKeyFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithKeyFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithKey("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var key = null == previousResult ? "" : previousResult.ToString();
            return WithKey(key);
        }

        /// <summary>
        /// Sets the key from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithKeyFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithKeyFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithKey("");
            }
            var previousResult = resultEntry.Result;
            var key = null == previousResult ? "" : previousResult.ToString();
            return WithKey(key);
        }

        /// <summary>
        /// Sets the client name
        /// </summary>
        /// <param name="name">The client name</param>
        /// <returns></returns>
        public IAllowRESTEndpointDetails WithName(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets the CSS parameter
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithCss(string css)
        {
            _parameters = null == _parameters
                ? new { css }
                : MergeParameters(_parameters, new { css });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the CSS from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithCssFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithCssFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithCss("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var css = null == previousResult ? "" : previousResult.ToString();
            return WithCss(css);
        }

        /// <summary>
        /// Sets the CSS from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithCssFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithCssFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithCss("");
            }
            var previousResult = resultEntry.Result;
            var css = null == previousResult ? "" : previousResult.ToString();
            return WithCss(css);
        }

        /// <summary>
        /// Sets the element parameter
        /// </summary>
        /// <param name="gPALElement">The element</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElement(IGPALElement gPALElement)
        {
            List<GPALElement> gpalElements = new List<GPALElement>() { (GPALElement)gPALElement };

            _parameters = null == _parameters
                ? new { gpalElements }
                : MergeParameters(_parameters, new { gpalElements });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the element from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElementIdFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithElementFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithElementId("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            string elementId;

            // NOTE: what kind of enumerable, what is envisioned? - grok generated...
            if (previousResult is IEnumerable<object> enumerable && false == (previousResult is string))
            {
                elementId = null == enumerable.FirstOrDefault() ? "" : enumerable.FirstOrDefault().ToString();
            }
            else
            {
                elementId = null == previousResult ? "" : previousResult.ToString();
            }
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the element from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElementIdFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithElementFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithElementId("");
            }
            var previousResult = resultEntry.Result;
            string elementId;

            // NOTE: what kind of enumerable, what is envisioned? - grok generated...
            if (previousResult is IEnumerable<object> enumerable && false == (previousResult is string))
            {
                elementId = null == enumerable.FirstOrDefault() ? "" : enumerable.FirstOrDefault().ToString();
            }
            else
            {
                elementId = null == previousResult ? "" : previousResult.ToString();
            }
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the prune interval in milliseconds
        /// </summary>
        /// <param name="pruneMs">The prune interval in milliseconds</param>
        /// <returns></returns>
        public IAllowCheckNetworkIdleOrExecution WithPruneMs(int pruneMs)
        {
            _parameters = null == _parameters
                ? new { pruneMs }
                : MergeParameters(_parameters, new { pruneMs });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the maximum connections
        /// </summary>
        /// <param name="maxConnections">The maximum connections</param>
        /// <returns></returns>
        public IAllowCheckNetworkIdleOrExecution WithMaxConnections(int maxConnections)
        {
            _parameters = null == _parameters
                ? new { maxConnections }
                : MergeParameters(_parameters, new { maxConnections });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the file to upload
        /// </summary>
        /// <param name="uploadPath">The path of the file to upload</param>
        /// <returns></returns>
        public IAllowRESTExecution WithUploadFile(string uploadPath)
        {
            _parameters = null == _parameters
                ? new { uploadPath }
                : MergeParameters(_parameters, new { uploadPath });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the files to upload
        /// </summary>
        /// <param name="uploadPaths">The files to upload</param>
        /// <returns></returns>
        public IAllowRESTExecution WithUploadFiles(GPALFile uploadPaths)
        {
            _parameters = null == _parameters
                ? new { uploadPaths }
                : MergeParameters(_parameters, new { uploadPaths });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the XPath parameter
        /// </summary>
        /// <param name="xpath">The XPath</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithXPath(string xpath)
        {
            _parameters = null == _parameters
                ? new { xpath }
                : MergeParameters(_parameters, new { xpath });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the XPath from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithXPathFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithXPathFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithXPath("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var xpath = null == previousResult ? "" : previousResult.ToString();
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets the XPath from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithXPathFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithXPathFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithXPath("");
            }
            var previousResult = resultEntry.Result;
            var xpath = null == previousResult ? "" : previousResult.ToString();
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets parameters as a variable argument list
        /// </summary>
        /// <param name="parameters">The parameters</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParameters(params object[] parameters)
        {
            _parameters = null == _parameters
                ? parameters
                : MergeParameters(_parameters, parameters);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets parameters as a single object
        /// </summary>
        /// <param name="parameters">The parameters object</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParameters(object parameters)
        {
            _parameters = null == _parameters
                ? parameters
                : MergeParameters(_parameters, parameters);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets parameters from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParametersFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithParametersFromResult. Using null.",
                    null, GPALObjectType.None);
                _parameters = null;
            }
            else
            {
                _parameters = _executionResults[resultIndex].Result;
            }
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets parameters from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParametersFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithParametersFromResult. Using null.",
                    null, GPALObjectType.None);
                _parameters = null;
            }
            else
            {
                _parameters = resultEntry.Result;
            }
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the set storage data parameter
        /// </summary>
        /// <param name="data">The data to save in storage</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithData(string data)
        {
            _parameters = null == _parameters
                ? new { data }
                : MergeParameters(_parameters, new { data });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage store name parameter
        /// </summary>
        /// <param name="storeName">The storage store name</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageStoreName(string storeName)
        {
            _parameters = null == _parameters
                ? new { storeName }
                : MergeParameters(_parameters, new { storeName });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage domain parameter
        /// </summary>
        /// <param name="domain">The domain</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageDomain(string domain)
        {
            _parameters = null == _parameters
                ? new { domain }
                : MergeParameters(_parameters, new { domain });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage path parameter
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStoragePath(string path)
        {
            _parameters = null == _parameters
                ? new { path }
                : MergeParameters(_parameters, new { path });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage type parameter
        /// </summary>
        /// <param name="storageType">The website storage type</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageType(WebsiteStorageType storageType)
        {
            _parameters = null == _parameters
                ? new { storageType = storageType.ToString() }
                : MergeParameters(_parameters, new { storageType = storageType.ToString() });

            WithHttpMethod("POST");
            return this;
        }
        
        /// <summary>
        /// Sets the storage key parameter
        /// </summary>
        /// <param name="key">The path</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageKey(string key)
        {
            _parameters = null == _parameters
                ? new { key }
                : MergeParameters(_parameters, new { key });

            WithHttpMethod("POST");
            return this;
        }

        public IAllowRESTStorageOptions WithDeleteAcrossOrigins(bool crossOrigin)
        {
            _parameters = null == _parameters
                ? new { crossOrigin }
                : MergeParameters(_parameters, new { crossOrigin });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the pixels parameter
        /// </summary>
        /// <param name="pixels">The pixels value</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithPixels(int pixels)
        {
            _parameters = null == _parameters
                ? new { pixels }
                : MergeParameters(_parameters, new { pixels });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the pixels from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithPixelsFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithPixels(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var pixels = previousResult is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the pixels from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithPixelsFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithPixels(0);
            }
            var previousResult = resultEntry.Result;
            var pixels = previousResult is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the referrer parameter
        /// </summary>
        /// <param name="url">The referrer URL</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithReferrer(string url)
        {
            _parameters = null == _parameters
                ? new { url }
                : MergeParameters(_parameters, new { url });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the referrer from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithReferrerFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range. Using empty string.",
                    null, GPALObjectType.None);
                return WithReferrer("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var referrer = null == previousResult ? "" : previousResult.ToString();
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the referrer from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithReferrerFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found. Using empty string.",
                    null, GPALObjectType.None);
                return WithReferrer("");
            }
            var previousResult = resultEntry.Result;
            var referrer = null == previousResult ? "" : previousResult.ToString();
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the text parameter
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns></returns>
        public IAllowRESTExecution WithText(string text)
        {
            _parameters = null == _parameters
                ? new { text }
                : MergeParameters(_parameters, new { text });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the text from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTExecution WithTextFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithTextFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithText("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var text = null == previousResult ? "" : previousResult.ToString();
            return WithText(text);
        }

        /// <summary>
        /// Sets the text from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTExecution WithTextFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithTextFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithText("");
            }
            var previousResult = resultEntry.Result;
            var text = null == previousResult ? "" : previousResult.ToString();
            return WithText(text);
        }

        /// <summary>
        /// Sets where a downloaded file should be saved.<br/>
        /// Unlike SaveTo, this asks the browser, and engines differ: OttoMagic can name the file but not its folder, so
        /// the download watcher moves it here.
        /// <example>client.WithEndpoint(ApiEndpoint.LeftClickAndDownload).DownloadTo(file).Execute();</example>
        /// </summary>
        /// <param name="downloadPath">Where the downloaded file should be saved</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowRESTParametersOrExecution DownloadTo(GPALFile downloadPath)
        {
            if (null == downloadPath || null == downloadPath.Filenames || 0 == downloadPath.Filenames.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "DownloadTo requires a valid GPALFile with at least one filename.", null, GPALObjectType.None);
                return this;
            }

            _parameters = null == _parameters
                ? new { downloadPath }
                : MergeParameters(_parameters, new { downloadPath });
                        WithHttpMethod("POST");

            return this;
        }
        /// <summary>
        /// Saves the response to a file if the endpoint returns a downloadable content
        /// <example>client.WithEndpoint(ApiEndpoint.LeftClickAndDownload).SaveResultsTo(file).Execute();</example>
        /// </summary>
        /// <param name="file">The GPAL file defining the save location</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution SaveResultsTo(GPALFile file)
        {
            if (null == file || null == file.Filenames || 0 == file.Filenames.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "SaveResultsTo requires a valid GPALFile with at least one filename.", null, GPALObjectType.None);
                return this;
            }
            _saveFile = file.Filename; // Store filename for Execute

            return this;
        }
        /// <summary>
        /// Sets the HTTP method
        /// </summary>
        /// <param name="method">The HTTP method</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHttpMethod(string method)
        {
            _httpMethod = null == method ? "GET" : method;
            return this;
        }

        /// <summary>
        /// Sets the image format for captureVisibleTab
        /// </summary>
        /// <param name="format">ImageFormat enum value [JPEG (default) or PNG]</param>
        /// <returns></returns>
        internal IAllowRESTParametersOrExecution WithImageFormat(ImageFormat format)
        {
            string imageFormat = format.ToString();
            _parameters = null == _parameters
                ? new { imageFormat }
                : MergeParameters(_parameters, new { imageFormat });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Specifies the option index for select menu operations
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="index">The option index</param>
        /// <returns></returns>
        public IAllowRESTAttribute WithIndex(int index)
        {
            _parameters = null == _parameters
                ? new { index }
                : MergeParameters(_parameters, new { index });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the cast device (sink) name parameter
        /// </summary>
        /// <param name="sinkName">The name of the cast device (sink)</param>
        /// <returns></returns>
        public IAllowRESTExecution WithDeviceName(string sinkName)
        {
            _parameters = null == _parameters
                ? new { sinkName }
                : MergeParameters(_parameters, new { sinkName });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Publishes the operations a call makes, as the Puppeteer client does: going to a url, and waiting for the
        /// network to idle. Other endpoints read or set something and stay silent. Each has a method of its own, since
        /// the published line is headed by the method that published it.
        /// </summary>
        /// <param name="before">True before the call is made, false once it has answered</param>
        /// <param name="result">What the call answered, after it</param>
        private void PublishStep(bool before, object result)
        {
            if (RESTHelper.Endpoints[ApiEndpoint.GoTo] == _endpoint && true == before)
                NavigateToUrl();
            else if (RESTHelper.Endpoints[ApiEndpoint.CheckNetworkIdle] == _endpoint)
                NetworkIdle(before, result);
        }

        // Publishes the goto about to be sent
        private void NavigateToUrl()
        {
            string who = string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ";

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"{who}Go to [{ParameterValue("url")}]", this, GPALObjectType.RestClient);
        }

        // Publishes the network-idle check about to be sent, or what it answered
        private void NetworkIdle(bool before, object result)
        {
            string who = string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ";

            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                true == before ? $"{who}Waiting for the network to idle" : $"{who}Network is [{result?.ToString().Trim('"')}]",
                this, GPALObjectType.RestClient);
        }

        /// <summary>
        /// The value of one parameter of the call being built, whichever shape the parameters were given in.
        /// </summary>
        /// <param name="name">The parameter's name</param>
        /// <returns>Its value as text, or null when it was not given</returns>
        private string ParameterValue(string name)
        {
            object value = null;

            if (_parameters is IDictionary<string, object> named)
                named.TryGetValue(name, out value);
            else
                value = _parameters?.GetType().GetProperty(name)?.GetValue(_parameters);

            return value?.ToString();
        }

        /// <summary>
        /// Executes the request synchronously
        /// </summary>
        /// <returns>The result string</returns>
        public string Execute(bool publishEvent = true)
        {
            // No API base url. For a browser's own client, the call came before launch. Report it instead of a null
            // reference.
            if (null == _helper)
            {
                string noBase = $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No API base is set for endpoint [{_endpoint}]. A browser's own client has one once the browser is launched on an engine that runs one, which OttoMagicClient only is on the OttoMagic engines.";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, noBase, this, GPALObjectType.RestClient);

                throw new GPALException($"{GPAL.MyMethodName()}: {noBase}");
            }

            GPALEventType publishToConsole = GPAL.GPALSettings.ConsoleEvents;
            GPALEventType publishToDebug = GPAL.GPALSettings.DebugEvents;

            try
            {
                bool customApiPassedValidation = _customEndpoint;

                if (true == _customEndpoint && 0 < openApiParamRules.Count())
                {
                    string ruleKey = GetOpenApiRuleKey(_httpMethod, _endpoint);   // e.g. "GET /api/basicstore/products"
                    customApiPassedValidation = (false == string.IsNullOrEmpty(ruleKey) && true == openApiParamRules.ContainsKey(ruleKey) && true == ValidateOpenApiParameters(ruleKey));
                }

                if (true == customApiPassedValidation || true == ValidateParameters(RESTHelper.Endpoints.FirstOrDefault(kvp => kvp.Value == _endpoint).Key))
                {
                    if (false == publishEvent)
                    {
                        GPAL.GPALSettings.ConsoleEvents = GPALEventType.NONE;
                        GPAL.GPALSettings.DebugEvents = GPALEventType.NONE;
                    }

                    PublishStep(true, null);

                    var result = _helper.Call(_endpoint, _parameters, _httpMethod, _contentEncoding).Result;

                    PublishStep(false, result);

                    _executionResults.Add((_nextResultName, result, _currentIteration));
                    _nextResultName = null;

                    // Clear the chain after the call, not when the endpoint is set, so earlier parameters are still
                    // sent.
                    Reset();
                    if (null != _saveFile)
                        // Assume the response is a stream URL or base64 content for now
                        SaveResponseToFile(result, _saveFile);

                    return result;
                }
                else
                {
                    // Clear the chain even when the parameters were rejected, so they do not go out with the next call.
                    Reset();

                    return null;
                }
            }
            catch (AggregateException ex)
            {
                if (true == publishEvent)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Execution failed synchronously for endpoint [{_endpoint}].",
                        null, GPALObjectType.None, ex.InnerException);
                // An unreachable browser wraps a GPALException in the Result; rethrow it, since it is not an empty
                // result.
                if (ex.InnerException is GPALException)
                    throw ex.InnerException;

                return "";
            }
            finally
            {
                GPAL.GPALSettings.ConsoleEvents = publishToConsole;
                GPAL.GPALSettings.DebugEvents = publishToDebug;
            }
        }

        /// <summary>
        /// Executes the request synchronously with type
        /// </summary>
        /// <typeparam name="T">The result type</typeparam>
        /// <returns>The typed result</returns>
        public T Execute<T>(bool publishEvent = true)
        {
            // No API base url. For a browser's own client, the call came before launch. Report it instead of a null
            // reference.
            if (null == _helper)
            {
                string noBase = $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No API base is set for endpoint [{_endpoint}]. A browser's own client has one once the browser is launched on an engine that runs one, which OttoMagicClient only is on the OttoMagic engines.";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, noBase, this, GPALObjectType.RestClient);

                throw new GPALException($"{GPAL.MyMethodName()}: {noBase}");
            }

            GPALEventType publishToConsole = GPAL.GPALSettings.ConsoleEvents;
            GPALEventType publishToDebug = GPAL.GPALSettings.DebugEvents;
            T result = default;

            try
            {
                bool customApiPassedValidation = _customEndpoint;

                if (true == _customEndpoint && 0 < openApiParamRules.Count())
                {
                    string ruleKey = GetOpenApiRuleKey(_httpMethod, _endpoint);   // e.g. "GET /api/basicstore/products"
                    customApiPassedValidation = (false == string.IsNullOrEmpty(ruleKey) && true == openApiParamRules.ContainsKey(ruleKey) && true == ValidateOpenApiParameters(ruleKey));
                }

                if (true == customApiPassedValidation || true == ValidateParameters(RESTHelper.Endpoints.FirstOrDefault(kvp => kvp.Value == _endpoint).Key))
                {
                    if (false == publishEvent)
                    {
                        GPAL.GPALSettings.ConsoleEvents = GPALEventType.NONE;
                        GPAL.GPALSettings.DebugEvents = GPALEventType.NONE;
                    }

                    int retry = 1;

                    do
                    {
                        try
                        {
                            PublishStep(true, null);

                            result = _helper.CallAndDeserialize<T>(_endpoint, _parameters, _httpMethod, _contentEncoding).Result;

                            PublishStep(false, result);
                        }
                        catch (AggregateException ex)
                        {
                            if (true == publishEvent)
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                                $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Execution failed synchronously for endpoint [{_endpoint}].",
                                null, GPALObjectType.None, ex.InnerException);
                            // An unreachable browser wraps a GPALException in the Result; rethrow it, since it is not
                            // an empty result.
                            if (ex.InnerException is GPALException)
                                throw ex.InnerException;

                            return default;
                        }
                    }
                    while (null == result && 0 < --retry);

                    _executionResults.Add((_nextResultName, result, _currentIteration));
                    _nextResultName = null;

                    // Clear the chain after the call, not when the endpoint is set, so earlier parameters are still
                    // sent.
                    Reset();

                    if (_saveFile != null)
                        GPAL.Converter.WithInput(result).SaveTo(_saveFile);

                    return result;
                }

                // Clear the chain even when the parameters were rejected, so they do not go out with the next call.
                Reset();

                return default;
            }
            finally
            {
                GPAL.GPALSettings.ConsoleEvents = publishToConsole;
                GPAL.GPALSettings.DebugEvents = publishToDebug;
            }
        }

        /// <summary>
        /// Executes and chains the request
        /// </summary>
        /// <returns></returns>
        public IRESTClient AndThen(bool publishEvent = true)
        {
            var result = Execute(publishEvent);
            _executionResults.Add((_nextResultName, result, _currentIteration));
            Reset();
            return this;
        }

        /// <summary>
        /// Executes and chains the request with type
        /// </summary>
        /// <typeparam name="T">The result type</typeparam>
        /// <returns></returns>
        public IRESTClient AndThen<T>(bool publishEvent = true)
        {
            var result = Execute<T>(publishEvent);
            _executionResults.Add((_nextResultName, result, _currentIteration));
            Reset();
            return this;
        }

        /// <summary>
        /// Executes the request asynchronously and returns the raw response
        /// <example>await client.ExecuteAsync();</example>
        /// </summary>
        /// <returns>The response string</returns>
        public async Task<string> ExecuteAsync(bool publishEvent = true)
        {
            // No API base url. For a browser's own client, the call came before launch. Report it instead of a null
            // reference.
            if (null == _helper)
            {
                string noBase = $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No API base is set for endpoint [{_endpoint}]. A browser's own client has one once the browser is launched on an engine that runs one, which OttoMagicClient only is on the OttoMagic engines.";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, noBase, this, GPALObjectType.RestClient);

                throw new GPALException($"{GPAL.MyMethodName()}: {noBase}");
            }

            GPALEventType publishToConsole = GPAL.GPALSettings.ConsoleEvents;
            GPALEventType publishToDebug = GPAL.GPALSettings.DebugEvents;

            try
            {
                bool customApiPassedValidation = _customEndpoint;

                if (true == _customEndpoint && 0 < openApiParamRules.Count())
                {
                    string ruleKey = GetOpenApiRuleKey(_httpMethod, _endpoint);   // e.g. "GET /api/basicstore/products"
                    customApiPassedValidation = (false == string.IsNullOrEmpty(ruleKey) && true == openApiParamRules.ContainsKey(ruleKey) && true == ValidateOpenApiParameters(ruleKey));
                }

                if (true == customApiPassedValidation || true == ValidateParameters(RESTHelper.Endpoints.FirstOrDefault(kvp => kvp.Value == _endpoint).Key))
                {
                    if (false == publishEvent)
                    {
                        GPAL.GPALSettings.ConsoleEvents = GPALEventType.NONE;
                        GPAL.GPALSettings.DebugEvents = GPALEventType.NONE;
                    }

                    var result = await _helper.Call(_endpoint, _parameters, _httpMethod, _contentEncoding);
                    _executionResults.Add((_nextResultName, result, _currentIteration));
                    _nextResultName = null;

                    // Clear the chain after the call, not when the endpoint is set, so earlier parameters are still
                    // sent.
                    Reset();
                    return result;
                }
                else
                {
                // Clear the chain even when the parameters were rejected, so they do not go out with the next call.
                Reset();

                    return null;
                }
            }
            catch (AggregateException ex)
            {
                if (true == publishEvent)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Execution failed asynchronously for endpoint [{_endpoint}].",
                    null, GPALObjectType.None, ex.InnerException);
                // An unreachable browser wraps a GPALException in the Result; rethrow it, since it is not an empty
                // result.
                if (ex.InnerException is GPALException)
                    throw ex.InnerException;

                return null;
            }
            finally
            {
                GPAL.GPALSettings.ConsoleEvents = publishToConsole;
                GPAL.GPALSettings.DebugEvents = publishToDebug;
            }
        }

        /// <summary>
        /// Executes the request asynchronously with type
        /// </summary>
        /// <typeparam name="T">The result type</typeparam>
        /// <returns>The typed result</returns>
        public async Task<T> ExecuteAsync<T>(bool publishEvent = true)
        {
            // No API base url. For a browser's own client, the call came before launch. Report it instead of a null
            // reference.
            if (null == _helper)
            {
                string noBase = $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No API base is set for endpoint [{_endpoint}]. A browser's own client has one once the browser is launched on an engine that runs one, which OttoMagicClient only is on the OttoMagic engines.";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, noBase, this, GPALObjectType.RestClient);

                throw new GPALException($"{GPAL.MyMethodName()}: {noBase}");
            }

            GPALEventType publishToConsole = GPAL.GPALSettings.ConsoleEvents;
            GPALEventType publishToDebug = GPAL.GPALSettings.DebugEvents;

            try
            {
                bool customApiPassedValidation = _customEndpoint;

                if (true == _customEndpoint && 0 < openApiParamRules.Count())
                {
                    string ruleKey = GetOpenApiRuleKey(_httpMethod, _endpoint);   // e.g. "GET /api/basicstore/products"
                    customApiPassedValidation = (false == string.IsNullOrEmpty(ruleKey) && true == openApiParamRules.ContainsKey(ruleKey) && true == ValidateOpenApiParameters(ruleKey));
                }

                if (true == customApiPassedValidation || true == ValidateParameters(RESTHelper.Endpoints.FirstOrDefault(kvp => kvp.Value == _endpoint).Key))
                 {
                    if (false == publishEvent)
                    {
                        GPAL.GPALSettings.ConsoleEvents = GPALEventType.NONE;
                        GPAL.GPALSettings.DebugEvents = GPALEventType.NONE;
                    }

                    T result = await _helper.CallAndDeserialize<T>(_endpoint, _parameters, _httpMethod, _contentEncoding);
                    _executionResults.Add((_nextResultName, result, _currentIteration));
                    _nextResultName = null;

                    // Clear the chain after the call, not when the endpoint is set, so earlier parameters are still
                    // sent.
                    Reset();

                    if (_saveFile != null)
                        GPAL.Converter.WithInput(result).SaveTo(_saveFile);

                    return result;
                }

                // Clear the chain even when the parameters were rejected, so they do not go out with the next call.
                Reset();

                return default;
            }
            catch (Exception ex) // base Exception, which covers AggregateException and async failures alike
            {
                if (true == publishEvent)
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Execution failed asynchronously for endpoint [{_endpoint}].",
                    null, GPALObjectType.None, ex);
                return default;
            }
            finally
            {
                GPAL.GPALSettings.ConsoleEvents = publishToConsole;
                GPAL.GPALSettings.DebugEvents = publishToDebug;
            }
        }

        /// <summary>
        /// Converts the client to a GPAL object
        /// </summary>
        /// <returns>The GPAL object</returns>
        public IRESTClient ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Gets the execution results
        /// </summary>
        /// <returns>The result collection</returns>
        /// <summary>
        /// Clears the steps added with <c>WithWorkflow</c>, so the next While or Until loop runs only later steps.
        /// Uncleared steps stay on the client and can run again.
        /// </summary>
        /// <example><code>client.ClearWorkflows().WithWorkflow(c =&gt; ...).While(() =&gt; ...);</code></example>
        /// <returns>Fluent interface to declare the next workflow</returns>
        public IAllowRESTWorkflow ClearWorkflows()
        {
            // The two lists are index-aligned; clear both.
            _workflows.Clear();
            _workflowStates.Clear();

            return this;
        }

        /// <summary>
        /// Clears the results stored by <c>WithResultName</c>, so <c>GetExecutionResults</c> only returns later
        /// results.
        /// </summary>
        /// <example><code>client.ClearResults();</code></example>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowRESTExecution ClearResults()
        {
            _executionResults.Clear();

            return this;
        }
        public ResultCollection GetExecutionResults()
        {
            return new ResultCollection(_executionResults, _name);
        }

        /// <summary>
        /// Returns every result stored under <paramref name="name"/>, in order; a loop that ran five times returns
        /// five. No name returns the whole collection; indexing it by name returns the last result.
        /// </summary>
        /// <example><code>int passes = client.GetExecutionResults("atBottom").Count;</code></example>
        /// <param name="name">The name given to WithResultName</param>
        /// <returns>The results stored under that name</returns>
        public IReadOnlyList<object> GetExecutionResults(string name)
        {
            return GetExecutionResults().GetAllResults(name);
        }

        /// <summary>
        /// Adds a workflow definition
        /// <example>client.WithWorkflow(c => c.WithEndpoint(...).Execute()).While(...);</example>
        /// </summary>
        /// <param name="workflow">The workflow action</param>
        /// <returns></returns>
        public IAllowRESTWorkflow WithWorkflow(Action<RESTClient> workflow)
        {
            if (null == workflow)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Workflow cannot be null.",
                    null, GPALObjectType.None);
                return this;
            }

            _workflowStates.Add((_endpoint, _parameters, _httpMethod, _nextResultName));
            _workflows.Add(workflow);
            return this;
        }

        /// <summary>
        /// Executes workflows while condition holds
        /// <example>client.While(() => someCondition).Execute();</example>
        /// </summary>
        /// <param name="condition">The condition function</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution While(Func<bool> condition)
        {
            if (0 == _workflows.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No workflows defined for While loop.",
                    null, GPALObjectType.None);
                Reset();
                return this;
            }

            int iteration = 0;
            const int maxIterations = 1000;

            while (condition() && iteration < maxIterations)
            {
                _currentIteration = iteration;
                for (int i = 0; i < _workflows.Count; i++)
                {
                    var initialState = _workflowStates[i];
                    if (initialState.HasValue)
                    {
                        (_endpoint, _parameters, _httpMethod, _nextResultName) = initialState.Value;
                    }
                    else
                    {
                        Reset();
                    }
                    _workflows[i](this);
                }
                iteration++;
            }

            if (maxIterations <= iteration)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}While loop reached maximum iterations [{maxIterations}].",
                    null, GPALObjectType.None);
            }

            Reset();
            _currentIteration = 0;
            return this;
        }

        /// <summary>
        /// Executes workflows until condition holds
        /// </summary>
        /// <param name="condition">The condition function</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution Until(Func<bool> condition)
        {
            if (0 == _workflows.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No workflows defined for Until loop.",
                    null, GPALObjectType.None);
                Reset();
                return this;
            }

            int iteration = 0;
            const int maxIterations = 1000;

            while (false == condition() && iteration < maxIterations)
            {
                _currentIteration = iteration;
                for (int i = 0; i < _workflows.Count; i++)
                {
                    var initialState = _workflowStates[i];
                    if (initialState.HasValue)
                    {
                        (_endpoint, _parameters, _httpMethod, _nextResultName) = initialState.Value;
                    }
                    else
                    {
                        Reset();
                    }
                    _workflows[i](this);
                }
                iteration++;
            }

            if (maxIterations <= iteration)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Until loop reached maximum iterations [{maxIterations}].",
                    null, GPALObjectType.None);
            }

            Reset();
            _currentIteration = 0;
            return this;
        }

        /// <summary>
        /// Sets the element ID parameter
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElementId(string elementId)
        {
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Specifiy the encoiding type for posted parameters: json or urlencoded
        /// </summary>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithEncoding(ContentEncoding encoding)
        {
            _contentEncoding = encoding;
            return this;
        }
        internal static string GetContentType(ContentEncoding encoding)
        {
            return encoding switch
            {
                ContentEncoding.UrlEncoded => "application/x-www-form-urlencoded",
                ContentEncoding.Json => "application/json",
                _ => "application/json" // Default for unknown encodings
            };
        }
        /// <summary>
        /// Adds an HTTP header to the request
        /// <example>client.WithHeader("Authorization", "Bearer YOUR_ACCESS_TOKEN").Execute();</example>
        /// </summary>
        /// <param name="name">The header name</param>
        /// <param name="value">The header value</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHeader(string name, string value)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                _headers[name] = value;
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid name [{name}] or value [{value}], nothing added to the header.",null, GPALObjectType.None);

            return this;
        }

        /// <summary>
        /// Sets how long one attempt can take.
        /// </summary>
        /// <remarks>
        /// Default 60 seconds. InfiniteTimeSpan means no limit. A timeout is not retried.
        /// </remarks>
        /// <example>
        /// <code>
        /// browser.ContinueAsRESTClient(out IRESTClient client);
        ///
        /// string body = client
        ///     .WithTimeout(TimeSpan.FromSeconds(10))
        ///     .WithEndpoint("/api/prices")
        ///     .Execute(false);
        /// </code>
        /// </example>
        /// <param name="httpTimeout">How long one attempt can take</param>
        /// <returns>Fluent interface to continue building the call</returns>
        public IAllowRESTExecution WithTimeout(TimeSpan httpTimeout)
        {
            if (null == _helper)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No API base to set a timeout on. Call WithAPIBase first, or take a client from ContinueAsRESTClient",
                    null, GPALObjectType.None);
            else
                _helper.HttpTimeout = httpTimeout;

            return this;
        }

        /// <summary>
        /// Sets the header from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHeaderFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] out of range. Using empty values.",
                    null, GPALObjectType.None);
                return WithHeader("", "");
            }
            var result = _executionResults[resultIndex].Result?.ToString().Split(new[] { ':' }, 2);
            var name = result?.Length > 0 ? result[0] : "";
            var value = result?.Length > 1 ? result[1] : "";
            return WithHeader(name, value);
        }

        /// <summary>
        /// Sets the header from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHeaderFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found. Using empty values.",
                    null, GPALObjectType.None);
                return WithHeader("", "");
            }
            var result = resultEntry.Result?.ToString().Split(new[] { ':' }, 2);
            var headerName = result?.Length > 0 ? result[0] : "";
            var headerValue = result?.Length > 1 ? result[1] : "";
            return WithHeader(headerName, headerValue);
        }

        /// <summary>
        /// Sets the horizontal drop offset for a drag-and-drop operation
        /// </summary>
        /// <param name="deltaX">The horizontal offset, in pixels, from the element's current position</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithDeltaX(int deltaX)
        {
            _parameters = null == _parameters
                ? new { deltaX }
                : MergeParameters(_parameters, new { deltaX });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the vertical drop offset for a drag-and-drop operation
        /// </summary>
        /// <param name="deltaY">The vertical offset, in pixels, from the element's current position</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithDeltaY(int deltaY)
        {
            _parameters = null == _parameters
                ? new { deltaY }
                : MergeParameters(_parameters, new { deltaY });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the horizontal grab offset within the element for a drag-and-drop operation
        /// </summary>
        /// <param name="offsetX">Horizontal offset, in pixels, within the element to start the drag from</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithOffsetX(int offsetX)
        {
            _parameters = null == _parameters
                ? new { offsetX }
                : MergeParameters(_parameters, new { offsetX });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the vertical grab offset within the element for a drag-and-drop operation
        /// </summary>
        /// <param name="offsetY">Vertical offset, in pixels, within the element to start the drag from</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithOffsetY(int offsetY)
        {
            _parameters = null == _parameters
                ? new { offsetY }
                : MergeParameters(_parameters, new { offsetY });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels parameter
        /// </summary>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithHPixels(int hPixels)
        {
            _parameters = null == _parameters
                ? new { hPixels }
                : MergeParameters(_parameters, new { hPixels });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithHPixelsFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithHPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithHPixels(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var hPixels = previousResult is int i ? i : 0;
            return WithHPixels(hPixels);
        }

        /// <summary>
        /// Sets the horizontal pixels from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithHPixelsFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithHPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithHPixels(0);
            }
            var previousResult = resultEntry.Result;
            var hPixels = previousResult is int i ? i : 0;
            return WithHPixels(hPixels);
        }

        /// <summary>
        /// Sets the vertical pixels parameter
        /// </summary>
        /// <param name="vPixels">The vertical pixels</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithVPixels(int vPixels)
        {
            _parameters = null == _parameters
                ? new { vPixels }
                : MergeParameters(_parameters, new { vPixels });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithVPixelsFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithVPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithVPixels(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var vPixels = previousResult is int i ? i : 0;
            return WithVPixels(vPixels);
        }

        /// <summary>
        /// Sets the vertical pixels from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithVPixelsFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithVPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithVPixels(0);
            }
            var previousResult = resultEntry.Result;
            var vPixels = previousResult is int i ? i : 0;
            return WithVPixels(vPixels);
        }
        /// <summary>
        /// Get a screenshot of the current tab visble contents
        /// </summary>
        /// <returns>Base64 string</returns>
        public IAllowRESTExecution CaptureVisibleTab(ImageFormat imageFormat = ImageFormat.JPEG)
        {
            WithImageFormat(imageFormat);
            WithEndpoint(ApiEndpoint.CaptureVisibleTab);
            return this;
        }
        /// <summary>
        /// Sets where the next browser download is saved, without clicking anything.
        /// <example>client.SetDownloadFilename(@"c:\temp\file.pdf").Execute();</example>
        /// </summary>
        /// <param name="downloadPath">Where the file should be saved</param>
        /// <returns></returns>
        public IAllowRESTExecution SetDownloadFilename(string downloadPath)
        {
            WithEndpoint(ApiEndpoint.SetDownloadFilename);
            return WithDownloadFile(downloadPath);
        }
        public IAllowRESTExecution WithDownloadFile(string downloadPath)
        {
            // Set only the parameter, so the chained endpoint stays and LeftClickAndDownload(id).WithDownloadFile(path)
            // still clicks.
            _parameters = null == _parameters
                ? new { downloadPath }
                : MergeParameters(_parameters, new { downloadPath });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression
        /// <example>client.Evaluate("//div").Execute();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution Evaluate(string xpath)
        {
            WithEndpoint(ApiEndpoint.Evaluate).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression for multiple elements
        /// <example>client.EvaluateAll("//div").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution EvaluateAll(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluateAll).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression
        /// <example>client.Evaluate("//div").Execute();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution EvaluatePersistent(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluatePersistent).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Evaluates an XPath expression for multiple elements
        /// <example>client.EvaluateAll("//div").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns></returns>
        public IAllowRESTExecution EvaluateAllPersistent(string xpath)
        {
            WithEndpoint(ApiEndpoint.EvaluateAllPersistent).WithXPath(xpath);
            return this;
        }

        /// <summary>
        /// Clears the referrer
        /// <example>client.ClearReferrer().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution ClearReferrer()
        {
            WithEndpoint(ApiEndpoint.ClearReferrer);
            return this;
        }

        /// <summary>
        /// Fires a change event on an element by ID
        /// <example>client.FireChangeEvent("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution FireChangeEvent(string elementId)
        {
            WithEndpoint(ApiEndpoint.FireChangeEvent).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Focuses an element by ID
        /// <example>client.Focus("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution Focus(string elementId)
        {
            WithEndpoint(ApiEndpoint.Focus).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Gets the bounding client rect of an element by ID
        /// <example>client.GetBoundingClientRect("element123").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GetBoundingClientRect(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetBoundingClientRect).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks whether a click at this viewport point would reach the element or hit something covering it.
        /// </summary>
        /// <param name="elementId">The element that should receive the click</param>
        /// <param name="x">Viewport x</param>
        /// <param name="y">Viewport y</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowRESTExecution ElementFromPoint(string elementId, int x, int y)
        {
            WithEndpoint(ApiEndpoint.ElementFromPoint).WithElementId(elementId);

            _parameters = null == _parameters
                ? new { x, y }
                : MergeParameters(_parameters, new { x, y });

            return this;
        }

        /// <summary>
        /// Left-clicks a viewport point through the browser's input pipeline, as a trusted event. NOTOK when the
        /// browser has no debugger to attach (Firefox).
        /// <example>client.TrustedLeftClick(120, 340).Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns></returns>
        public IAllowRESTExecution TrustedLeftClick(int x, int y, int holdMs = 0)
        {
            WithEndpoint(ApiEndpoint.TrustedLeftClick);

            // holdMs is optional on the endpoint, so it only goes out when a hold was asked for
            object point = 0 < holdMs ? (object)new { x, y, holdMs } : new { x, y };

            _parameters = null == _parameters
                ? point
                : MergeParameters(_parameters, point);

            // POST: GPALRestAPI passes no GET parameters to the extension.
            WithHttpMethod("POST");

            return this;
        }

        /// <summary>
        /// Middle-clicks a viewport point through the browser's input pipeline, as a trusted event. NOTOK when the
        /// browser has no debugger to attach (Firefox).
        /// <example>client.TrustedMiddleClick(120, 340).Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns></returns>
        public IAllowRESTExecution TrustedMiddleClick(int x, int y, int holdMs = 0)
        {
            WithEndpoint(ApiEndpoint.TrustedMiddleClick);

            // holdMs is optional on the endpoint, so it only goes out when a hold was asked for
            object point = 0 < holdMs ? (object)new { x, y, holdMs } : new { x, y };

            _parameters = null == _parameters
                ? point
                : MergeParameters(_parameters, point);

            // POST: GPALRestAPI passes no GET parameters to the extension.
            WithHttpMethod("POST");

            return this;
        }
        public IAllowRESTExecution GetWindowRectangle()
        {
            WithEndpoint(ApiEndpoint.GetWindowRectangle);
            return this;
        }
        public IAllowRESTExecution GetContentAndCss(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetContentAndCss);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution GetCssAttributes(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetCssAttributes);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution GetDomAttributes(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetDomAttributes);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution GetDomProperties(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetDomProperties);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Gets the attribute hash of an element
        /// <example>client.GetElementAttributeHash().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetElementAttributeHash(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetElementAttributeHash).WithElementId(elementId);
            return this;
        }

       /// <summary>
        /// Get the options in a Select manue
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public IAllowRESTExecution GetOptions(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement).WithElementId(elementId).AndThen().WithCss("option");
            return this;
        }
        public IAllowRESTExecution GetPageSource()
        {
            WithEndpoint(ApiEndpoint.GetPageSource);
            return this;
        }
        /// <summary>
        /// Gets the parent node of an element by ID
        /// <example>client.GetParentNode("element123").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GetParentNode(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetParentNode).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Hides an element by ID
        /// <example>client.HideElement("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution HideElement(string elementId)
        {
            WithEndpoint(ApiEndpoint.HideElement).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is clickable by ID
        /// <example>client.IsClickAble("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsClickable(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsClickable).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is displayed by ID
        /// <example>client.IsDisplayed("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsDisplayed(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsDisplayed).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is enabled by ID
        /// <example>client.IsEnabled("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsEnabled(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsEnabled).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if the page is at the end
        /// <example>client.IsEndOfPage().Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution IsEndOfPage()
        {
            WithEndpoint(ApiEndpoint.IsEndOfPage);
            return this;
        }

        /// <summary>
        /// Checks if an element is visible in viewport by ID
        /// <example>client.IsVisibleInViewport("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsVisibleInViewport(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsVisibleInViewport).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>client.LeftClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution LeftClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.LeftClick).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Performs a left double click on an element by ID
        /// <example>client.LeftDoubleClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution LeftDoubleClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.LeftDoubleClick).WithElementId(elementId);
            return this;
        }
        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>client.LeftClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution MiddleClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.MiddleClick).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>client.QuerySelector(".class").Execute();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QuerySelector(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelector).WithCss(css);
            return this;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>client.QuerySelectors(".class").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QuerySelectors(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelectors).WithCss(css);
            return this;
        }

        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>client.QuerySelector(".class").Execute();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QueryPersistentSelector(string css)
        {
            WithEndpoint(ApiEndpoint.QueryPersistentSelector).WithCss(css);
            return this;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>client.QuerySelectors(".class").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QueryPersistentSelectors(string css)
        {
            WithEndpoint(ApiEndpoint.QueryPersistentSelectors).WithCss(css);
            return this;
        }

        /// <summary>
        /// Performs a right click on an element by ID
        /// <example>client.RightClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution RightClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.RightClick).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Scrolls an element into view by ID
        /// <example>client.ScrollIntoView("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollIntoView(string elementId)
        {
            WithEndpoint(ApiEndpoint.ScrollIntoView).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Scrolls an element by horizontal and vertical pixels
        /// <example>client.ScrollElement(100, 200).Execute();</example>
        /// </summary>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <param name="vPixels">The vertical pixels</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollElement(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollElement).WithHPixels(hPixels).WithVPixels(vPixels);
            return this;
        }

        /// <summary>
        /// Starts getting an attribute of an element by ID
        /// <example>client.GetAttribute("element123").WithAttribute("value").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTAttribute GetAttribute(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetAttribute).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Starts setting an attribute of an element by ID
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTAttribute SetAttribute(string elementId)
        {
            WithEndpoint(ApiEndpoint.SetAttribute).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Top the browser, bring it into focus. Used before performing any actions on the browser.
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution TopBrowser()
        {
            WithEndpoint(ApiEndpoint.TopBrowser);
            return this;
        }

        /// <summary>
        /// Specifies the attribute name for get or set operation
        /// <example>client.GetAttribute("element123").WithAttribute("value").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <returns></returns>
        public IAllowRESTAttribute WithAttribute(string attribute)
        {
            _parameters = null == _parameters
                ? new { attribute }
                : MergeParameters(_parameters, new { attribute });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Starts setting an attribute of an element by ID
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRangeValue SetRange(string elementId)
        {
            WithEndpoint(ApiEndpoint.SetRange).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Specifies the value for SetRange operation
        /// <example>client.SetRange("element123").WithRangeValue(63).Execute();</example>
        /// </summary>
        /// <param name="rangeValue">The range value</param>
        /// <returns></returns>
        public IAllowRESTExecution WithRangeValue(int rangeValue)
        {
            _parameters = null == _parameters
                ? new { rangeValue = rangeValue.ToString() }
                : MergeParameters(_parameters, new { rangeValue = rangeValue.ToString() });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Specifies the value for set operation
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="value">The attribute value</param>
        /// <returns></returns>
        public IAllowRESTAttribute WithValue(string value)
        {
            _parameters = null == _parameters
                ? new { value }
                : MergeParameters(_parameters, new { value });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Gets the window inner height
        /// <example>client.WindowInnerHeight().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowInnerHeight()
        {
            WithEndpoint(ApiEndpoint.WindowInnerHeight);
            return this;
        }

        /// <summary>
        /// Gets the window inner width
        /// <example>client.WindowInnerWidth().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowInnerWidth()
        {
            WithEndpoint(ApiEndpoint.WindowInnerWidth);
            return this;
        }

        /// <summary>
        /// Gets the window outer height
        /// <example>client.WindowOuterHeight().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowOuterHeight()
        {
            WithEndpoint(ApiEndpoint.WindowOuterHeight);
            return this;
        }

        /// <summary>
        /// Gets the window outer width
        /// <example>client.WindowOuterWidth().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowOuterWidth()
        {
            WithEndpoint(ApiEndpoint.WindowOuterWidth);
            return this;
        }

        /// <summary>
        /// Sends an API request from inside the page, with the browser's cookies, TLS fingerprint, header order and
        /// anti-bot clearance. A relative url resolves against the current page.
        /// <example>client.Fetch("/api/search?page=1").Execute();</example>
        /// </summary>
        /// <param name="url">Url to request, relative or absolute</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution Fetch(string url)
        {
            WithEndpoint(ApiEndpoint.Fetch);
            _parameters = null == _parameters
                ? new { url }
                : MergeParameters(_parameters, new { url });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the HTTP method of the fetched request, not of the REST call (.WithHttpMethod). Default GET.
        /// <example>client.Fetch(url).WithVerb("POST").Execute();</example>
        /// </summary>
        /// <param name="method">HTTP verb for the fetched request</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithVerb(string method)
        {
            _parameters = null == _parameters
                ? new { method }
                : MergeParameters(_parameters, new { method });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the body of the request being fetched.
        /// <example>client.Fetch(url).WithVerb("POST").WithBody(json).Execute();</example>
        /// </summary>
        /// <param name="body">Request body</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithBody(string body)
        {
            _parameters = null == _parameters
                ? new { body }
                : MergeParameters(_parameters, new { body });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the content type of the body of the request being fetched.
        /// <example>client.Fetch(url).WithContentType("application/json").Execute();</example>
        /// </summary>
        /// <param name="contentType">Content type</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithContentType(string contentType)
        {
            _parameters = null == _parameters
                ? new { contentType }
                : MergeParameters(_parameters, new { contentType });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets extra headers for the request being fetched, flattened to name, value, name, value.
        /// <example>client.Fetch(url).WithHeaders(new[] { "Accept", "application/json" }).Execute();</example>
        /// </summary>
        /// <param name="headers">Header names and values in pairs</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithHeaders(string[] headers)
        {
            _parameters = null == _parameters
                ? new { headers }
                : MergeParameters(_parameters, new { headers });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// True to return the body base64 encoded, as a file needs. As text, invalid UTF-8 bytes are replaced.
        /// <example>client.Fetch(url).WithBytes(true).Execute();</example>
        /// </summary>
        /// <param name="bytes">True for a base64 encoded body, false for text</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithBytes(bool bytes)
        {
            _parameters = null == _parameters
                ? new { bytes }
                : MergeParameters(_parameters, new { bytes });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Gets the window page offset X
        /// <example>client.WindowPageOffsetX().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowPageOffsetX()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetX);
            return this;
        }

        /// <summary>
        /// Gets the window page offset Y
        /// <example>client.WindowPageOffsetY().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowPageOffsetY()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetY);
            return this;
        }

        /// <summary>
        /// Gets the window screen left position
        /// <example>client.WindowScreenLeft().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowScreenLeft()
        {
            WithEndpoint(ApiEndpoint.WindowScreenLeft);
            return this;
        }

        /// <summary>
        /// Gets the window screen top position
        /// <example>client.WindowScreenTop().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowScreenTop()
        {
            WithEndpoint(ApiEndpoint.WindowScreenTop);
            return this;
        }

        /// <summary>
        /// Hovers over an element by ID
        /// <example>client.Hover("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution Hover(string elementId)
        {
            WithEndpoint(ApiEndpoint.Hover).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Resets the client state
        /// </summary>
        private void Reset()
        {
            _customEndpoint = false; ;
            _parameters = null;
            _httpMethod = "GET";
            _endpoint = null;
            // Clears the per-call parameters at the end of Execute and Execute<T>. Headers stay: Authorization, Cookie
            // and User-Agent belong to the client. Execute clears _nextResultName itself.
        }

        // grok left out a lot, so these are not in order
        /// <summary>
        /// Navigates browser history back
        /// <example>client.Back().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Back()
        {
            WithEndpoint(ApiEndpoint.Back);
            return this;
        }

        /// <summary>
        /// Checks if network is idle with max connections
        /// <example>client.CheckNetworkIdle(5).Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="maxConnections">Maximum connections allowed</param>
        /// <returns></returns>
        public IAllowRESTExecution CheckNetworkIdle(int maxConnections = 0)
        {
            WithEndpoint(ApiEndpoint.CheckNetworkIdle).WithMaxConnections(maxConnections);
            return this;
        }

        /// <summary>
        /// Closes a tab by tab ID
        /// <example>client.CloseTab(1).Execute();</example>
        /// </summary>
        /// <param name="tabId">The tab ID</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.CloseTab).WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Closes a tab by URL
        /// <example>client.CloseTab("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL (optional)</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseTab(GPALUrl url = null)
        {
            if (null != url)
                WithEndpoint(ApiEndpoint.CloseTab).WithUrl(url.Url);
            else
                WithEndpoint(ApiEndpoint.CloseTab);
            return this;
        }

        /// <summary>
        /// Executes JavaScript code
        /// <example>client.ExecuteJavaScript("return document.title").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="script">The JavaScript code</param>
        /// <returns></returns>
        public IAllowRESTParameters ExecuteJavaScript(string script)
        {
            WithEndpoint(ApiEndpoint.ExecuteJavaScript);
            _parameters = null == _parameters
                ? new { script }
                : MergeParameters(_parameters, new { script });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Registers a script to run on every new document load. Persists until <see cref="ClearInjectedScripts"/> is called.
        /// </summary>
        /// <param name="script">The JavaScript code to inject</param>
        /// <returns></returns>
        public IAllowRESTParameters InjectScript(string script)
        {
            WithEndpoint(ApiEndpoint.InjectScript);
            _parameters = null == _parameters
                ? new { script }
                : MergeParameters(_parameters, new { script });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Removes all scripts previously registered via <see cref="InjectScript(string)"/>.
        /// </summary>
        /// <returns></returns>
        public IAllowRESTParameters ClearInjectedScripts()
        {
            WithEndpoint(ApiEndpoint.ClearInjectedScripts);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Navigates browser history forward
        /// <example>client.Forward().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Forward()
        {
            WithEndpoint(ApiEndpoint.Forward);
            return this;
        }

        /// <summary>
        /// Sets browser to full screen
        /// <example>client.FullScreen().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution FullScreen()
        {
            WithEndpoint(ApiEndpoint.FullScreen);
            return this;
        }

        /// <summary>
        /// Gets browser settings
        /// <example>client.GetBrowserSettings().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetBrowserSettings()
        {
            WithEndpoint(ApiEndpoint.GetBrowserSettings);
            return this;
        }

        /// <summary>
        /// Gets the current URL
        /// <example>client.GetCurrentUrl().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetCurrentUrl()
        {
            WithEndpoint(ApiEndpoint.GetCurrentUrl);
            return this;
        }

        /// <summary>
        /// Gets the current URL and the document identity in one call.<br/>
        /// A form posting to itself and a route rewritten in place both build a new document at the same
        /// address, which the url alone does not distinguish.
        /// <example>client.GetDocumentStamp().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetDocumentStamp()
        {
            WithEndpoint(ApiEndpoint.GetDocumentStamp);
            return this;
        }

        /// <summary>
        /// Gets the page title, the __NEXT_DATA__ payload and any React Flight chunks, read from the document
        /// rather than its markup.
        /// <example>client.GetHydrationScripts().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetHydrationScripts()
        {
            WithEndpoint(ApiEndpoint.GetHydrationScripts);
            return this;
        }

        /// <summary>
        /// Gets GPAL settings
        /// <example>client.GetGpalSettings().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetGpalSettings()
        {
            WithEndpoint(ApiEndpoint.GetGpalSettings);
            return this;
        }

        /// <summary>
        /// Gets the ready status
        /// <example>client.GetReadyStatus().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetReadyStatus()
        {
            WithEndpoint(ApiEndpoint.GetReadyStatus);
            return this;
        }

        /// <summary>
        /// Gets the HTTP status of the active tab's page, 0 when it has none (no response yet, or the navigation
        /// failed outright).
        /// <example>client.GetStatusCode().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns>Fluent interface to execute the request</returns>
        public IAllowRESTExecution GetStatusCode()
        {
            WithEndpoint(ApiEndpoint.GetStatusCode);
            return this;
        }

        /// <summary>
        /// Gets settings
        /// <example>client.GetSettings().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetSettings()
        {
            WithEndpoint(ApiEndpoint.GetSettings);
            return this;
        }

        /// <summary>
        /// Gets the shadow root for a CSS selector
        /// <example>client.GetShadowRoot(".class").Execute();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution GetShadowRoot(string css)
        {
            WithEndpoint(ApiEndpoint.GetShadowRoot).WithElementId(css);
            return this;
        }

        /// <summary>
        /// Gets the browser's preferred languages, most preferred first
        /// <example>client.GetLanguages().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetLanguages()
        {
            WithEndpoint(ApiEndpoint.GetLanguages);
            return this;
        }

        /// <summary>
        /// Starts or stops recording what the page asks for. Narrow it with
        /// <see cref="WithCallFilter"/> and start from nothing with <see cref="WithClearCalls"/>.
        /// <example>client.CaptureCalls(true).WithCallFilter("/api/search").Execute();</example>
        /// </summary>
        /// <param name="capture">True to record, false to stop</param>
        /// <returns>Fluent interface to narrow what is recorded, or to execute</returns>
        public IAllowRESTCaptureOptions CaptureCalls(bool capture = true)
        {
            WithEndpoint(ApiEndpoint.CaptureCalls);

            _parameters = null == _parameters
                ? new { capture }
                : MergeParameters(_parameters, new { capture });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Records only calls whose url contains this text.
        /// </summary>
        /// <param name="urlFragment">Record only urls containing this</param>
        /// <returns>Fluent interface to narrow what is recorded, or to execute</returns>
        public IAllowRESTCaptureOptions WithCallFilter(string urlFragment)
        {
            _parameters = null == _parameters
                ? new { filter = urlFragment }
                : MergeParameters(_parameters, new { filter = urlFragment });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Clears earlier recordings before starting.
        /// </summary>
        /// <param name="clear">True to clear first</param>
        /// <returns>Fluent interface to narrow what is recorded, or to execute</returns>
        public IAllowRESTCaptureOptions WithClearCalls(bool clear = true)
        {
            _parameters = null == _parameters
                ? new { clear }
                : MergeParameters(_parameters, new { clear });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Returns everything recorded so far, oldest first, as the extension's JSON.
        /// <example>string json = client.GetCapturedCalls().Execute();</example>
        /// </summary>
        /// <returns>Fluent interface to execute the request</returns>
        public IAllowRESTExecution GetCapturedCalls()
        {
            WithEndpoint(ApiEndpoint.GetCapturedCalls);
            return this;
        }

        /// <summary>
        /// Gets the user agent
        /// <example>client.GetUserAgent().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetUserAgent()
        {
            WithEndpoint(ApiEndpoint.GetUserAgent);
            return this;
        }

        /// <summary>
        /// Gets the workflow
        /// <example>client.GetWorkflow().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetWorkflow()
        {
            WithEndpoint(ApiEndpoint.GetWorkflow);
            return this;
        }

        /// <summary>
        /// Navigates to a URL
        /// <example>client.Goto("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns></returns>
        public IAllowRESTExecution GoTo(GPALUrl url)
        {
            if (true == string.IsNullOrEmpty(url))
                url = new GPALUrl("google.com");

            WithEndpoint(ApiEndpoint.GoTo).WithUrl(url.Url);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Navigates to a tab by tab ID
        /// <example>client.GotoTab(1).Execute();</example>
        /// </summary>
        /// <param name="tabId">The tab ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.GoToTab).WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Navigates to a tab by URL
        /// <example>client.GotoTab("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToTab(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToTab).WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Appends text to an element
        /// <example>client.FillInAppend("input123").WithText("Hello").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTInputText FillInAppend(string elementId)
        {
            WithEndpoint(ApiEndpoint.FillInAppend).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Appends text to an element
        /// <example>client.FillInAppend("input123").WithText("Hello").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTInputText FillInInsert(string elementId)
        {
            WithEndpoint(ApiEndpoint.FillInInsert).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Appends text to an element
        /// <example>client.FillInAppend("input123").WithText("Hello").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTInputText FillInOverwrite(string elementId)
        {
            WithEndpoint(ApiEndpoint.FillInOverwrite).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Performs a left click and download on an element
        /// <example>client.LeftClickAndDownload("element123").SetDownloadFileName(fileName).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Interace chaining to SetDownloadFileName</returns>
        public IAllowRESTWithDownloadFilename LeftClickAndDownload(string elementId)
        {
            WithEndpoint(ApiEndpoint.LeftClickAndDownload).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Has the browser download a url itself: no tab opens and no Save As dialog is shown. The file is saved in
        /// the browser's download folder under the name WithDownloadFile gives.
        /// <example>client.DownloadUrl("https://example.com/file.zip").WithDownloadFile("file.zip").Execute();</example>
        /// </summary>
        /// <param name="url">The url to download</param>
        /// <returns>Interface chaining to WithDownloadFile</returns>
        public IAllowRESTWithDownloadFilename DownloadUrl(string url)
        {
            WithEndpoint(ApiEndpoint.DownloadUrl).WithUrl(url);
            return this;
        }

        /// <summary>
        /// Purely headless mode, attaches the file to the input[type=file]
        /// <example>client.LeftClickAndUpload("element123").WithUploadFile(filePath).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTWithUploadFilename LeftClickAndUpload(string elementId)
        {
            WithEndpoint(ApiEndpoint.Upload).WithElementId(elementId);
            return this;
        }
        
        /// <summary>
        /// Maximizes the browser window
        /// <example>client.Maximize().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Maximize()
        {
            WithEndpoint(ApiEndpoint.Maximize);
            return this;
        }

        /// <summary>
        /// Minimizes the browser window
        /// <example>client.Minimize().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Minimize()
        {
            WithEndpoint(ApiEndpoint.Minimize);
            return this;
        }

        /// <summary>
        /// Moves to an element
        /// <example>client.MoveTo("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution MoveTo(string elementId)
        {
            WithEndpoint(ApiEndpoint.MoveTo).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Opens a new tab with optional URL
        /// <example>client.NewTab("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL (optional)</param>
        /// <returns></returns>
        public IAllowRESTExecution NewTab(GPALUrl url)
        {

            WithEndpoint(ApiEndpoint.NewTab);

            if (null != url) 
                WithUrl(url?.Url);

            return this;
        }

        /// <summary>
        /// Switches to the next tab
        /// <example>client.NextTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution NextTab()
        {
            WithEndpoint(ApiEndpoint.NextTab);
            return this;
        }

        /// <summary>
        /// Returns how many tabs the browser has open, which can differ from how many GPAL opened: a browser closes a
        /// tab whose first navigation becomes a download.
        /// <example>int tabs = client.TabCount().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution TabCount()
        {
            WithEndpoint(ApiEndpoint.TabCount);
            return this;
        }

        /// <summary>
        /// Switches to the next window
        /// <example>client.NextTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution NextWindow()
        {
            WithEndpoint(ApiEndpoint.NextWindow);
            return this;
        }

        /// <summary>
        /// Restores the browser window to normal
        /// <example>client.Normal().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Normal()
        {
            WithEndpoint(ApiEndpoint.Normal);
            return this;
        }

        /// <summary>
        /// Overrides the referrer URL
        /// <example>client.OverrideReferrr("https://google.com").Execute();</example>
        /// </summary>
        /// <param name="referrer">The referrer URL</param>
        /// <returns></returns>
        public IAllowRESTExecution OverrideReferrer(string referrer)
        {
            WithEndpoint(ApiEndpoint.OverrideReferrer).WithReferrer(referrer);
            return this;
        }

        /// <summary>
        /// Overrides the browser's user agent string
        /// <example>client.SetUserAgent("Mozilla/5.0 ...").Execute();</example>
        /// </summary>
        /// <param name="userAgent">The user agent string to use</param>
        /// <returns></returns>
        public IAllowRESTExecution SetUserAgent(string userAgent)
        {
            WithEndpoint(ApiEndpoint.SetUserAgent);
            _parameters = null == _parameters
                ? new { userAgent }
                : MergeParameters(_parameters, new { userAgent });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Scrolls the page down by pages
        /// <example>client.PageDown(1).Execute();</example>
        /// </summary>
        /// <param name="pagesToScroll">Number of pages to scroll</param>
        /// <returns></returns>
        public IAllowRESTExecution PageDown(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageDown);
            _parameters = null == _parameters
                ? new { pagesToScroll }
                : MergeParameters(_parameters, new { pagesToScroll });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Scrolls to the page end
        /// <example>client.PageEnd().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PageEnd()
        {
            WithEndpoint(ApiEndpoint.PageEnd);
            return this;
        }

        /// <summary>
        /// Scrolls to the page top
        /// <example>client.PageTop().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PageTop()
        {
            WithEndpoint(ApiEndpoint.PageTop);
            return this;
        }

        /// <summary>
        /// Scrolls the page up by pages
        /// <example>client.PageUp(1).Execute();</example>
        /// </summary>
        /// <param name="pagesToScroll">Number of pages to scroll</param>
        /// <returns></returns>
        public IAllowRESTExecution PageUp(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageUp);
            _parameters = null == _parameters
                ? new { pagesToScroll }
                : MergeParameters(_parameters, new { pagesToScroll });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Press the modifier keys specified.
        /// Keep pressed until a release is sent.
        /// </summary>
        /// <param name="modifierKeys"></param>
        /// <returns></returns>
        public IAllowRESTExecution PressModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.PressModifierKey);
            _parameters = null == _parameters
                ? new { modifierKeys }
                : MergeParameters(_parameters, new { modifierKeys });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Switches to the previous tab
        /// <example>client.PreviousTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PreviousTab()
        {
            WithEndpoint(ApiEndpoint.PreviousTab);
            return this;
        }

        /// <summary>
        /// Switches to the next window
        /// <example>client.NextTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PreviousWindow()
        {
            WithEndpoint(ApiEndpoint.PreviousWindow);
            return this;
        }

        /// <summary>
        /// Refreshes the current page
        /// <example>client.Refresh().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Refresh()
        {
            WithEndpoint(ApiEndpoint.Refresh);
            return this;
        }

        /// <summary>
        /// Press the modifier keys specified.
        /// Keep pressed until a release is sent.
        /// </summary>
        /// <param name="modifierKeys"></param>
        /// <returns></returns>
        public IAllowRESTExecution ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            WithEndpoint(ApiEndpoint.ReleaseModifierKey);
            _parameters = null == _parameters
                ? new { modifierKeys }
                : MergeParameters(_parameters, new { modifierKeys });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Restores the browser window
        /// <example>client.Restore().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Restore()
        {
            WithEndpoint(ApiEndpoint.Restore);
            return this;
        }

        /// <summary>
        /// Drags an element from its current position by the given offset and drops it
        /// <example>client.DragAndDrop("element123").WithDeltaX(100).WithDeltaY(50).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop DragAndDrop(string elementId)
        {
            WithEndpoint(ApiEndpoint.DragAndDrop).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Starts scrolling an element by ID
        /// <example>client.ScrollElement("element123").WithHPixels(100).WithVPixels(200).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTScrollElement ScrollElement(string elementId)
        {
            WithEndpoint(ApiEndpoint.ScrollElement).WithElementId(elementId);
            return this;
        }

        public IAllowRESTExecution ScrollWindow(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindow).WithHPixels(hPixels).WithVPixels(vPixels);
            return this;
        }
        /// <summary>
        /// Scrolls the window horizontally by pixels
        /// <example>client.ScrollWindowByHorizontal(100).Execute();</example>
        /// </summary>
        /// <param name="hPixels">Pixels to scroll horizontally</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollWindowByHorizontal(int hPixels)
        {
            int vPixels = 0;
            WithEndpoint(ApiEndpoint.ScrollWindow).WithHPixels(hPixels).WithVPixels(vPixels);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Scrolls the window vertically by pixels
        /// <example>client.ScrollWindowByVertical(100).Execute();</example>
        /// </summary>
        /// <param name="vPixels">Pixels to scroll vertically</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollWindowByVertical(int vPixels)
        {
            int hPixels = 0;
            WithEndpoint(ApiEndpoint.ScrollWindow).WithHPixels(hPixels).WithVPixels(vPixels);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Click a Select menu option
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public IAllowSelectOptions SelectClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.SelectClick).WithElementId(elementId);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Set the value to choose from the Select menu
        /// </summary>
        /// <param name="selectValue">The value of the option to choose</param>
        /// <returns></returns>
        public IAllowRESTExecution WithSelectValue(string selectValue)
        {
            _parameters = null == _parameters
                ? new { selectValue }
                : MergeParameters(_parameters, new { selectValue });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution WithSelectIndex(int selectIndex)
        {
            _parameters = null == _parameters
                ? new { selectIndex }
                : MergeParameters(_parameters, new { selectIndex });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Sends a key
        /// <example>client.SendKey("Enter").Execute();</example>
        /// </summary>
        /// <param name="vkcode">The Windows virtual-key code of the key to send</param>
        /// <returns></returns>
        public IAllowRESTExecution SendKey(byte vkcode)
        {
            string key = null;
            string code = null;

            if (true == VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(vkcode, out key, out code))
            {
                WithEndpoint(ApiEndpoint.SendKey);
                _parameters = null == _parameters
                    ? new { key }
                    : MergeParameters(_parameters, new { key });
                _parameters = MergeParameters(_parameters, new { code });
                _parameters = MergeParameters(_parameters, new { vk = code });
                WithHttpMethod("POST");
            }
            return this;
        }

        /// <summary>
        /// Sends a string
        /// <example>client.SendString("Hello").Execute();</example>
        /// </summary>
        /// <param name="text">The text to send</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds. 0 sends the whole string atomically; greater than 0 dispatches character-by-character with this delay between characters.</param>
        /// <returns></returns>
        public IAllowRESTExecution SendString(string text, int delayMs = 0)
        {
            WithEndpoint(ApiEndpoint.SendString);
            _parameters = null == _parameters
                ? new { text, typingDelay = delayMs }
                : MergeParameters(_parameters, new { text, typingDelay = delayMs });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the value of the destination element identified by <paramref name="destSelector"/> from the
        /// source element identified by the preceding <see cref="WithCss(string)"/> or
        /// <see cref="WithElementId(string)"/> call.
        /// <example>client.WithElementId("srcSelector").SetValueFrom("destSelector").Execute();</example>
        /// </summary>
        /// <param name="destSelector">CSS selector identifying the destination element whose value is set</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution SetValueFrom(string destSelector)
        {
            WithEndpoint(ApiEndpoint.SetValueFromElement);
            _parameters = null == _parameters
                ? new { destSelector }
                : MergeParameters(_parameters, new { destSelector });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Overrides referrer stealthily - sets to google.com
        /// <example>client.SetGoogleReferrer().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution SetGoogleReferrer()
        {
            WithEndpoint(ApiEndpoint.SetGoogleReferrer);
            return this;
        }

        /// <summary>
        /// Switches to default content
        /// <example>client.SwitchToDefaultContent().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToDefaultContent()
        {
            WithEndpoint(ApiEndpoint.SwitchToDefaultContent);
            return this;
        }
        #region Window Handling
        /// <summary>
        /// Sets the window ID parameter
        /// <example>client.CloseWindow(123).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="windowId">The window ID</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithWindowId(int windowId)
        {
            _parameters = new { windowId };
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Sets the window ID from a result by index
        /// <example>client.GoToWindow().WithWindowIdFromResult(0).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithWindowIdFromResult(int resultIndex)
        {
            if (resultIndex < 0 || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithWindowIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithWindowId(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var windowId = previousResult is int i ? i : (int.TryParse(previousResult?.ToString(), out int parsed) ? parsed : 0);
            return WithWindowId(windowId);
        }
        /// <summary>
        /// Sets the window ID from a result by name
        /// <example>client.GoToWindow().WithWindowIdFromResult("windowResult").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithWindowIdFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (resultEntry.Result == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithWindowIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithWindowId(0);
            }
            var previousResult = resultEntry.Result;
            var windowId = previousResult is int i ? i : (int.TryParse(previousResult?.ToString(), out int parsed) ? parsed : 0);
            return WithWindowId(windowId);
        }
        /// <summary>
        /// Opens a new window with the specified URL
        /// <example>client.OpenWindow("https://example.com").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="url">The URL to open (optional, defaults to "https://google.com")</param>
        /// <returns></returns>
        public IAllowRESTExecution OpenWindow(GPALUrl url)
        {
            if (true == string.IsNullOrEmpty(url))
                url = new GPALUrl("google.com");

            WithEndpoint(ApiEndpoint.OpenWindow).WithUrl(url.Url);
            return this;
        }

        /// <summary>
        /// Switches to a window by URL
        /// <example>client.GoToWindow("https://example.com").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="url">The window URL</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToWindow).WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Switches to a window by window ID
        /// <example>client.GoToWindow(123).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="windowId">The window ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToWindow(int windowId)
        {
            WithEndpoint(ApiEndpoint.GoToWindow).WithWindowId(windowId);
            return this;
        }

        /// <summary>
        /// Closes every browser window, which makes the browser quit.
        /// <example>client.CloseBrowser().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution CloseBrowser()
        {
            WithEndpoint(ApiEndpoint.CloseBrowser);
            return this;
        }

        /// <summary>
        /// Closes a window by URL, optionally switching to the main window
        /// <example>client.CloseWindow("https://example.com").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="url">The window URL</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.CloseWindow);
            
            if (null != url)
                WithUrl(url?.Url);

            return this;
        }

        /// <summary>
        /// Closes a window by window ID, optionally switching to the main window
        /// <example>client.CloseWindow(123).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="windowId">The window ID</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseWindow(int windowId)
        {
            WithEndpoint(ApiEndpoint.CloseWindow).WithWindowId(windowId);
            return this;
        }

        /// <summary>
        /// Delete the specified storage type
        /// </summary>
        /// <param name="storageType"></param>
        /// <returns>fluent interface to chain parameters</returns>
        public IAllowRESTStorageOptions DeleteStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.DeleteStorage).WithStorageType(storageType);

            return this;
        }

        /// <summary>
        /// Get the specified storage type
        /// </summary>
        /// <param name="storageType"></param>
        /// <returns>fluent interface to chain parameters</returns>
        public IAllowRESTStorageOptions GetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.GetStorage).WithStorageType(storageType);

            return this;
        }

        /// <summary>
        /// Set data for the specified storage type
        /// </summary>
        /// <param name="storageType"></param>
        /// <returns>fluent interface to chain parameters</returns>
        public IAllowRESTStorageOptions SetStorage(WebsiteStorageType storageType)
        {
            WithEndpoint(ApiEndpoint.SetStorage).WithStorageType(storageType);

            return this;
        }
        /// <summary>
        /// Gets the current window's ID and URL
        /// <example>client.GetCurrentWindow().Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetCurrentWindow()
        {
            WithEndpoint(ApiEndpoint.GetCurrentWindow);
            return this;
        }
        #endregion Window Handling
        /// <summary>
        /// Switches to an element to search for selectors contained
        /// <example>client.SwitchToElement("div123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToElement(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Switches to a frame
        /// <example>client.SwitchToFrame("frame123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToFrame(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Switches to a shadow root
        /// <example>client.SwitchToShadowRoot("shadow123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToShadowRoot(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToShadowRoot);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Merges existing and additional parameters
        /// </summary>
        /// <param name="existing">The existing parameters</param>
        /// <param name="additional">The additional parameters</param>
        /// <returns>The merged parameters</returns>
        private object MergeParameters(object existing, object additional)
        {
            var dict = existing is IDictionary<string, object> existingDict
                ? new Dictionary<string, object>(existingDict)
                : existing.GetType().GetProperties()
                    .ToDictionary(p => p.Name, p => p.GetValue(existing));

            if (null != additional)
            {
                var additionalDict = additional is IDictionary<string, object> addDict
                ? addDict
                : additional?.GetType().GetProperties()
                    .ToDictionary(p => p.Name, p => p.GetValue(additional));

                foreach (var kvp in additionalDict)
                {
                    dict[kvp.Key] = kvp.Value;
                }
            }
            return dict;
        }


        internal bool ValidateParameters(ApiEndpoint endpoint, IDictionary<string, object> parmsToValidate = null)
        {
            try
            {
                var internalParms = _parameters as IDictionary<string, object>
                    ?? (null != _parameters ? _parameters.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(_parameters))
                    : new Dictionary<string, object>());

                var paramsDict = parmsToValidate ?? internalParms;

                if (false == paramRules.ContainsKey(endpoint))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No validation rules defined for endpoint [{endpoint}].",
                        null, GPALObjectType.None);
                    return true;
                }

                var (mandatory, optional, customValidation) = paramRules[endpoint];

                if (ApiEndpoint.SetStorage == endpoint)
                {
                    var storageType = paramsDict.ContainsKey("storageType") ? paramsDict["storageType"]?.ToString() : "";
                    if (true == "cache".Equals(storageType))
                        (mandatory, optional, customValidation) = paramRules[ApiEndpoint.SetStorageCache];
                    else if (true == "indexedDb".Equals(storageType))
                        (mandatory, optional, customValidation) = paramRules[ApiEndpoint.SetStorageIndexedDb];
                }

                var missingMandatory = mandatory.Where(p => false == paramsDict.ContainsKey(p)).ToArray();

                if (true == missingMandatory.Any())
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Missing required parameters for [{endpoint}]: [{string.Join(", ", missingMandatory)}].",
                        null, GPALObjectType.None);
                    return false;
                }

                if (null != customValidation && false == customValidation(paramsDict))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Custom validation failed for [{endpoint}].",
                        null, GPALObjectType.None);
                    return false;
                }

                // Ensure only allowed parameters (mandatory + optional) are present
                var allowedParams = mandatory.Concat(optional).ToHashSet();
                var invalidParams = paramsDict.Keys.Where(k => false == allowedParams.Contains(k)).ToArray();
                if (true == invalidParams.Any())
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Invalid parameters for [{endpoint}]: [{string.Join(", ", invalidParams)}].",
                        null, GPALObjectType.None);
                    return false;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to validate [{endpoint}]", this, GPALObjectType.RestClient, ex);
            }

            return true;
        }

        internal bool ValidateOpenApiParameters(string ruleKey, IDictionary<string, object> parmsToValidate = null)
        {
            try
            {
                var internalParms = _parameters as IDictionary<string, object>
                    ?? (null != _parameters ? _parameters.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(_parameters))
                    : new Dictionary<string, object>());

                var paramsDict = parmsToValidate ?? internalParms;

                if (false == openApiParamRules.ContainsKey(ruleKey))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No OpenAPI validation rules defined for [{ruleKey}].",
                        null, GPALObjectType.None);
                    return true;
                }

                var (mandatory, optional, customValidation) = openApiParamRules[ruleKey];

                var missingMandatory = mandatory.Where(p => false == paramsDict.ContainsKey(p)).ToArray();

                if (true == missingMandatory.Any())
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Missing required parameters for [{ruleKey}]: [{string.Join(", ", missingMandatory)}].",
                        null, GPALObjectType.None);
                    return false;
                }

                if (null != customValidation && false == customValidation(paramsDict))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Custom validation failed for OpenAPI endpoint [{ruleKey}].",
                        null, GPALObjectType.None);
                    return false;
                }

                // Ensure only allowed parameters are present
                var allowedParams = mandatory.Concat(optional).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var invalidParams = paramsDict.Keys.Where(k => false == allowedParams.Contains(k)).ToArray();

                if (true == invalidParams.Any())
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                        $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Invalid parameters for [{ruleKey}]: [{string.Join(", ", invalidParams)}].",
                        null, GPALObjectType.None);
                    return false;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"Unable to validate OpenAPI endpoint [{ruleKey}]", this, GPALObjectType.RestClient, ex);
            }

            return true;
        }
        /// <summary>
        /// Create a lookup key for the open api parameter rules
        /// </summary>
        /// <param name="httpMethod"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        private string GetOpenApiRuleKey(string httpMethod, string endpoint)
        {
            if (string.IsNullOrWhiteSpace(httpMethod) || string.IsNullOrWhiteSpace(endpoint))
                return null;

            var path = endpoint.TrimStart('/');
            return $"{httpMethod.ToUpperInvariant()} {path}";
        }

        /// <summary>
        /// Parse the Open API file, get a remote url or the first local if no remote
        /// </summary>
        /// <param name="yamlContent"></param>
        /// <returns></returns>
        private string ExtractBestServerUrl(string yamlContent)
        {
            try
            {
                var reader = new Microsoft.OpenApi.Readers.OpenApiStreamReader();
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(yamlContent));
                var document = reader.Read(stream, out _);

                if (document.Servers == null || document.Servers.Count == 0)
                    return null;

                // 1. Prefer remote (https) server
                var remote = document.Servers.FirstOrDefault(s =>
                    s.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

                if (remote != null)
                    return remote.Url.TrimEnd('/');

                // 2. Otherwise take the first one
                return document.Servers[0].Url.TrimEnd('/');
            }
            catch
            {
                return null;
            }
        }
        internal static void SaveResponseToFile(string response, string filePath)
        {
            try
            {
                // Handle Data URI (e.g., base64-encoded image) or direct HTTP URL
                if (response.StartsWith("data:"))
                {
                    // Handle base64-encoded data URI
                    var base64Part = response.Substring(response.IndexOf(",") + 1);
                    var bytes = Convert.FromBase64String(base64Part);
                    FileHelper.EnsureFolderFor(filePath);
                    System.IO.File.WriteAllBytes(filePath, bytes);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved base64 response to [{filePath}]", null, GPALObjectType.None);
                }
                else if (Uri.TryCreate(response, UriKind.Absolute, out var uri))
                {
                    // Handle direct HTTP(S) URL
                    FileHelper.EnsureFolderFor(filePath);

                    using (var client = new HttpClient())
                    using (var stream = client.GetStreamAsync(uri).Result)
                    using (var fileStream = System.IO.File.Create(filePath))
                    {
                        stream.CopyTo(fileStream);
                    }
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloaded response to [{filePath}]", null, GPALObjectType.None);
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported response format for saving to [{filePath}]", null, GPALObjectType.None);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to save response to [{filePath}]", null, GPALObjectType.None, ex);
            }
        }
    }
}
