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
    public partial class RESTClient : IRESTClient
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
            // OttoMagic deletes every kind of storage when storageType is missing, so storageType is mandatory here;
            // all storage of one storageType can still be deleted
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
        /// Converts the client to a GPAL object
        /// </summary>
        /// <returns>The GPAL object</returns>
        public IRESTClient ToGPALObject()
        {
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
