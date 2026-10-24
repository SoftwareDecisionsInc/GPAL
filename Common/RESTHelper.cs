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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Xml.Serialization;
using GenerallyPositive;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    internal class RESTHelper
    {
        internal int StatusCode { get; set; }

        // Seconds from the server's Retry-After header; zero when there was none (anything but a 429 or 503).
        internal int RetryAfterSeconds { get; set; }

        private RESTClient _restClient;

        // Turn UseCookies off. With it on, the handler's empty cookie jar replaces the caller's Cookie header.
        private HttpClient _httpClient { get; set; } = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            // Turn off HttpClient's 100-second timeout. The CancellationTokenSource built from HttpTimeout bounds every
            // send below and is the only timeout.
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        private readonly string _browserExtensionApiBase;

        // Default timeout for one attempt: enough for a slow API without a two-minute wait on a host that never
        // replies.
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        // Settable after the base url, so a ContinueAsRESTClient client can set it. See RESTClient.WithTimeout.
        internal TimeSpan HttpTimeout { get; set; } = DefaultTimeout;
        internal static Dictionary<ApiEndpoint, string> Endpoints { get; set; } = new Dictionary<ApiEndpoint, string>
        {
            [ApiEndpoint.Back] = "back",                       // history.back()
            [ApiEndpoint.CaptureVisibleTab] = "capture-visible-tab",    // for headles image matching
            [ApiEndpoint.CastDesktop] = "cast-desktop",
            [ApiEndpoint.CastTab] = "cast-tab",
            [ApiEndpoint.CheckNetworkIdle] = "check-network-idle", // ensure no more than 'maxConnections' are active - better than document.readyState in some scenarios
            [ApiEndpoint.CheckStatus] = "status",              // to test if the api is up
            [ApiEndpoint.ClearReferrer] = "clear-referrer",
            [ApiEndpoint.CloseBrowser] = "close-browser",
            [ApiEndpoint.CloseTab] = "close-tab",
            [ApiEndpoint.DeleteStorage] = "delete-storage",
            [ApiEndpoint.DragAndDrop] = "drag-and-drop",
            [ApiEndpoint.DownloadUrl] = "download-url",
            [ApiEndpoint.Evaluate] = "evaluate",               // xpath
            [ApiEndpoint.EvaluateAll] = "evaluate-all",        // xpath multiple elements
            [ApiEndpoint.EvaluatePersistent] = "evaluate-persistent",               // xpath
            [ApiEndpoint.EvaluateAllPersistent] = "evaluate-all-persistent",        // xpath multiple elements
            [ApiEndpoint.ExecuteJavaScript] = "execute-javascript",
            [ApiEndpoint.Fetch] = "fetch",
            [ApiEndpoint.FillInAppend] = "fill-in-append",
            [ApiEndpoint.FillInInsert] = "fill-in-insert",
            [ApiEndpoint.FillInOverwrite] = "fill-in-overwrite",
            [ApiEndpoint.FireChangeEvent] = "fire-change-event",
            [ApiEndpoint.Focus] = "focus",
            [ApiEndpoint.Forward] = "forward",                 // history.forward()
            [ApiEndpoint.FullScreen] = "fullscreen",
            [ApiEndpoint.GetAttribute] = "get-attribute",
            [ApiEndpoint.GetBoundingClientRect] = "get-bounding-client-rect",
            [ApiEndpoint.ElementFromPoint] = "element-from-point",
            [ApiEndpoint.GetContentAndCss] = "get-content-and-css",
            [ApiEndpoint.GetCssAttributes] = "get-css-attributes",
            [ApiEndpoint.GetCurrentUrl] = "get-current-url",
            [ApiEndpoint.GetDocumentStamp] = "get-document-stamp",
            [ApiEndpoint.GetHydrationScripts] = "get-hydration-scripts",
            [ApiEndpoint.GetDomAttributes] = "get-dom-attributes",
            [ApiEndpoint.GetDomProperties] = "get-dom-properties",
            [ApiEndpoint.GetElementAttributeHash] = "get-element-attribute-hash",
            [ApiEndpoint.GetPageSource] = "get-page-source",
            [ApiEndpoint.GetParentNode] = "get-parent-node",
            [ApiEndpoint.GetReadyStatus] = "get-ready-status",
            [ApiEndpoint.GetStatusCode] = "get-status-code",
            [ApiEndpoint.GetShadowRoot] = "get-shadow-root",
            [ApiEndpoint.GetStorage] = "get-storage",
            [ApiEndpoint.GetLanguages] = "get-languages",
            [ApiEndpoint.CaptureCalls] = "capture-calls",
            [ApiEndpoint.GetCapturedCalls] = "get-captured-calls",
            [ApiEndpoint.GetUserAgent] = "get-useragent",
            [ApiEndpoint.GoTo] = "goto",
            [ApiEndpoint.GoToTab] = "goto-tab",
            [ApiEndpoint.GetWindowRectangle] = "get-window-rectangle",
            [ApiEndpoint.HideElement] = "hide-element",
            [ApiEndpoint.Hover] = "hover",
            [ApiEndpoint.InjectScript] = "inject-script",
            [ApiEndpoint.ClearInjectedScripts] = "clear-injected-scripts",
            [ApiEndpoint.IsClickable] = "is-clickable",
            [ApiEndpoint.IsDisplayed] = "is-displayed",
            [ApiEndpoint.IsEnabled] = "is-enabled",
            [ApiEndpoint.IsEndOfPage] = "is-end-of-page",
            [ApiEndpoint.IsVisibleInViewport] = "is-visible-in-viewport",
            [ApiEndpoint.LeftClick] = "left-click",
            [ApiEndpoint.LeftClickAndDownload] = "left-click-download",
            [ApiEndpoint.LeftDoubleClick] = "left-double-click",
            [ApiEndpoint.Maximize] = "maximize",
            [ApiEndpoint.MiddleClick] = "middle-click",
            [ApiEndpoint.Minimize] = "minimize",
            [ApiEndpoint.MoveTo] = "move-to",
            [ApiEndpoint.NewTab] = "new-tab",
            [ApiEndpoint.NextTab] = "next-tab",
            [ApiEndpoint.NextWindow] = "next-window",
            [ApiEndpoint.Normal] = "normal",                   // restore window after fullscreen
            [ApiEndpoint.OverrideReferrer] = "override-referrer",
            [ApiEndpoint.PageDown] = "page-down",
            [ApiEndpoint.PageEnd] = "page-end",
            [ApiEndpoint.PageTop] = "page-top",
            [ApiEndpoint.PageUp] = "page-up",
            [ApiEndpoint.PressModifierKey] = "press-modifier-key",
            [ApiEndpoint.PreviousTab] = "previous-tab",
            [ApiEndpoint.PreviousWindow] = "previous-window",
            [ApiEndpoint.QuerySelector] = "query-selector",    // css
            [ApiEndpoint.QuerySelectors] = "query-selectors",  // css multiple elements
            [ApiEndpoint.QueryPersistentSelector] = "query-persistent-selector",    // css
            [ApiEndpoint.QueryPersistentSelectors] = "query-persistent-selectors",  // css multiple elements
            [ApiEndpoint.Refresh] = "refresh",
            [ApiEndpoint.ReleaseModifierKey] = "release-modifier-key",
            [ApiEndpoint.Restore] = "restore",
            [ApiEndpoint.RightClick] = "right-click",
            [ApiEndpoint.ScrollElement] = "scroll-element",
            [ApiEndpoint.ScrollIntoView] = "scroll-into-view",
            [ApiEndpoint.ScrollWindowByHorizontal] = "scroll-window",
            [ApiEndpoint.ScrollWindowByVertical] = "scroll-window",
            [ApiEndpoint.ScrollWindow] = "scroll-window",
            [ApiEndpoint.SelectClick] = "select-click",
            [ApiEndpoint.SendKey] = "send-key",
            [ApiEndpoint.SendString] = "send-string",
            [ApiEndpoint.SetAttribute] = "set-attribute",
            [ApiEndpoint.SetDownloadFilename] = "set-download-filename",
            [ApiEndpoint.SetGoogleReferrer] = "google-referrer",
            [ApiEndpoint.SetRange] = "set-range",
            [ApiEndpoint.SetStorage] = "set-storage",
            [ApiEndpoint.SetUserAgent] = "set-useragent",
            [ApiEndpoint.SetValueFromElement] = "set-value-from-element",
            [ApiEndpoint.StopCasting] = "stop-casting",
            [ApiEndpoint.SwitchToDefaultContent] = "switch-to-default-content",
            [ApiEndpoint.SwitchToElement] = "switch-to-element",
            [ApiEndpoint.SwitchToFrame] = "switch-to-frame",    // NOTE: puppeteer only, not used by rest, ottomagic runs in every frame
            [ApiEndpoint.SwitchToShadowRoot] = "switch-to-shadow-root",
            [ApiEndpoint.TabCount] = "tab-count",
            [ApiEndpoint.TopBrowser] = "top-browser",
            [ApiEndpoint.MoveToPoint] = "move-to-point",
            [ApiEndpoint.ClickPoint] = "click-point",
            [ApiEndpoint.TrustedLeftClick] = "trusted-left-click",
            [ApiEndpoint.TrustedMiddleClick] = "trusted-middle-click",
            [ApiEndpoint.Upload] = "upload",
            [ApiEndpoint.WindowInnerHeight] = "window-inner-height",
            [ApiEndpoint.WindowInnerWidth] = "window-inner-width",
            [ApiEndpoint.WindowOuterHeight] = "window-outer-height",
            [ApiEndpoint.WindowOuterWidth] = "window-outer-width",
            [ApiEndpoint.WindowPageOffsetX] = "window-page-xoffset",
            [ApiEndpoint.WindowPageOffsetY] = "window-page-yoffset",
            [ApiEndpoint.WindowScreenLeft] = "window-screen-left",
            [ApiEndpoint.WindowScreenTop] = "window-screen-top",
            [ApiEndpoint.OpenWindow] = "open-window",
            [ApiEndpoint.GoToWindow] = "goto-window",
            [ApiEndpoint.CloseWindow] = "close-window",
            [ApiEndpoint.GetCurrentWindow] = "get-current-window",
            #region GPAL STUDIO
            [ApiEndpoint.GetWorkflow] = "get-workflow",
            [ApiEndpoint.GetSettings] = "get-settings",
            [ApiEndpoint.GetGpalSettings] = "get-gpal-settings",
            [ApiEndpoint.GetBrowserSettings] = "get-browser-settings",
            #endregion GPAL STUDIO
        };

        public RESTHelper(string browserExtensionApiBase, RESTClient client, TimeSpan? httpTimeout = null)
        {
            if (browserExtensionApiBase == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    "Browser extension API base cannot be null.",
                    null, GPALObjectType.None);
            }
            _browserExtensionApiBase = browserExtensionApiBase;

            // Timeout for one attempt, from .WithAPIBase(url, timeout). Must exceed OttoMagic's own 10-second frame
            // wait in query, select and get-content-and-css. Timeout.InfiniteTimeSpan means no limit, for a long AI
            // call.
            HttpTimeout = httpTimeout ?? DefaultTimeout;
            _restClient = client;
        }

        public string GetEndpointUrl(ApiEndpoint endpoint)
        {
            return $"{_browserExtensionApiBase}{Endpoints[endpoint]}";
        }

        internal async Task<string> Call(ApiEndpoint endpoint, object parameters = null, string httpMethod = "GET", ContentEncoding contentEncoding = ContentEncoding.Json)
        {
            // No /status call first. A missing server fails the real call, which names itself.
            string url = GetEndpointUrl(endpoint);

            string result = await InternalCall(url, parameters, httpMethod, contentEncoding).ConfigureAwait(false);

            return result;
        }

        internal async Task<string> Call(string endpoint, object parameters = null, string httpMethod = "GET", ContentEncoding contentEncoding = ContentEncoding.Json)
        {
            // Use a full-url endpoint as is. A browser session's base is its origin, but pages call other origins, and
            // appending gave an invalid url.
            string url = true == endpoint?.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? endpoint
                : $"{_browserExtensionApiBase}{endpoint}";

            string result = await InternalCall(url, parameters, httpMethod, contentEncoding).ConfigureAwait(false);

            return result;
        }

        internal async Task<T> CallAndDeserialize<T>(ApiEndpoint endpoint, object parameters = null, string httpMethod = "GET", ContentEncoding contentEncoding = ContentEncoding.Json)
        {
//            string url = GetEndpointUrl(endpoint);
            string jsonResponse = await Call(endpoint, parameters, httpMethod, contentEncoding).ConfigureAwait(false);
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{endpoint}] result: [{jsonResponse}]");

            return string.IsNullOrEmpty(jsonResponse)
                ? default
                : JsonConvert.DeserializeObject<T>(jsonResponse);   // system.text.json can't deserialize as cleanly
        }

        /// <summary>
        /// Removes JSON string encoding from a response, layer by layer, until it is not an encoded string. An object
        /// or array is returned unchanged.
        /// </summary>
        /// <param name="response">The endpoint's response.</param>
        /// <returns>The decoded value.</returns>
        private static string DecodeJsonString(string response)
        {
            string retVal = response;

            while (false == string.IsNullOrEmpty(retVal)
                && true == retVal.StartsWith("\"") && true == retVal.EndsWith("\""))
            {
                try
                {
                    string peeled = JsonConvert.DeserializeObject<string>(retVal);

                    // Stop when decoding returns the same string.
                    if (peeled == retVal)
                        break;

                    retVal = peeled;
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    break;   // not an encoded string after all, so it is the value already
                }
            }

            return retVal;
        }

        internal async Task<T> CallAndDeserialize<T>(string endpoint, object parameters = null, string httpMethod = "GET", ContentEncoding contentEncoding = ContentEncoding.Json)
        {
            string jsonResponse = await Call(endpoint, parameters, httpMethod, contentEncoding).ConfigureAwait(false);

            // Decode the JSON string, escapes included, so an encoded string holding a quote and an encoded object both
            // arrive intact.
            jsonResponse = DecodeJsonString(jsonResponse);

            if (typeof(GPALElement) == typeof(T) || typeof(List<GPALElement>) == typeof(T))
            {
                var settings = new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> { new GPALElementConverter(), new PointConverter(), new SizeConverter() },
                    NullValueHandling = NullValueHandling.Ignore
                };
                try
                {
                    return JsonConvert.DeserializeObject<T>(jsonResponse, settings);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Exception calling [{endpoint}]", null, GPALObjectType.None, ex);
                }
            }
            else if(string.IsNullOrEmpty(jsonResponse))
{
                if (endpoint.Equals(Endpoints[ApiEndpoint.IsEndOfPage]) &&
                    typeof(T) == typeof(bool))
                {
                    return (T)(object)true;
                }

                return default;
            }

            if (typeof(T) == typeof(bool))
                if ("OK".Equals(jsonResponse) || "true".Equals(jsonResponse))
                    return (T)(object)true;
                else if ("NOTOK".Equals(jsonResponse) || "false".Equals(jsonResponse))
                    return (T)(object)false;
                else
                {
                    var msg = jsonResponse.Replace("\\", "");
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{msg}]", parameters, GPALObjectType.Other);
                    return (T)(object)false;
                }

            // OttoMagic returns an empty array for get-css-attributes when the element is not in the DOM, the normal
            // answer for a hidden element. Do not report it.
            if (endpoint.Equals(Endpoints[ApiEndpoint.GetCssAttributes]) && "[]".Equals(jsonResponse?.Trim()))
                return default;

            try
            {
                if (null != jsonResponse)
                    return JsonConvert.DeserializeObject<T>(jsonResponse);
                else
                    return default;
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                // A bare url or word is not JSON; for a string result it is the answer. A quoted JSON string goes
                // through DeserializeObject above.
                if (typeof(T) == typeof(string))
                    return (T)(object)jsonResponse;

                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Could not deserialize [{jsonResponse?.Substring(0, Math.Min(80, jsonResponse?.Length ?? 0))}] as [{typeof(T).Name}]",
                    null, GPALObjectType.None, ex);
                return default;
            }
        }

        private static T HandleUnsupportedContentType<T>(string contentType, string response)
        {
            GPAL.PublishSimpleEvent(
                GPALEventType.WARNING,
                $"Unsupported format: [{contentType}]. Unable to cast to [{typeof(T)}]. Returning raw response.",
                response,
                GPALObjectType.Other);
            return default;
        }

        // TODO: add out param to return the server status code
        // Headers .NET requires on HttpContent; all others go on the request.
        private static readonly string[] contentHeaders =
        {
            "content-type", "content-length", "content-encoding", "content-language", "content-location",
            "content-md5", "content-range", "content-disposition", "expires", "last-modified", "allow"
        };

        private static bool IsContentHeader(string name)
        {
            return Array.Exists(contentHeaders, content => content.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Appends <paramref name="parameters"/> to <paramref name="route"/> as a query string. A string is appended as
        /// is; a dictionary or object is read entry by entry, skipping nulls.
        /// </summary>
        /// <param name="route">The url so far, with or without a query already.</param>
        /// <param name="parameters">The value passed to WithParameters.</param>
        /// <returns>The url with the parameters appended.</returns>
        private static string AppendQueryString(string route, object parameters)
        {
            string query;

            if (parameters is string written)
            {
                query = written.TrimStart('?', '&');
            }
            else
            {
                IEnumerable<KeyValuePair<string, object>> pairs;

                if (parameters is IDictionary<string, object> dictionary)
                    pairs = dictionary;
                else
                    pairs = parameters.GetType().GetProperties()
                        .Select(property => new KeyValuePair<string, object>(property.Name, property.GetValue(parameters)));

                query = string.Join("&", pairs
                    .Where(pair => null != pair.Value)
                    .Select(pair => $"{HttpUtility.UrlEncode(pair.Key)}={HttpUtility.UrlEncode(pair.Value.ToString())}"));
            }

            string retVal = route;

            if (false == string.IsNullOrEmpty(query))
                retVal = true == route.Contains("?") ? $"{route}&{query}" : $"{route}?{query}";

            return retVal;
        }

        /// <summary>
        /// Reads the Retry-After header off a response and turns it into a number of seconds from now.
        /// </summary>
        /// <param name="response">The response that came back</param>
        /// <returns>The seconds to wait, zero when the header is absent or already in the past</returns>
        private static int ReadRetryAfter(HttpResponseMessage response)
        {
            int seconds = 0;

            // Retry-After can be seconds or an HTTP date; read both.
            if (null != response?.Headers?.RetryAfter)
            {
                if (true == response.Headers.RetryAfter.Delta.HasValue)
                    seconds = (int)Math.Ceiling(response.Headers.RetryAfter.Delta.Value.TotalSeconds);
                else if (true == response.Headers.RetryAfter.Date.HasValue)
                    seconds = (int)Math.Ceiling((response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalSeconds);
            }

            return 0 < seconds ? seconds : 0;
        }

        internal async Task<string> InternalCall(string route, object parameters = null, string httpMethod = "GET", ContentEncoding contentEncoding = ContentEncoding.Json)
        {
            string upperMethod = httpMethod.ToUpperInvariant();

            // GET and HEAD put parameters on the query string. .NET throws on a body for either, so the content branch
            // below has nothing to do.
            if (("GET" == upperMethod || "HEAD" == upperMethod) && null != parameters)
            {
                route = AppendQueryString(route, parameters);
                parameters = null;
            }

            // === RETRY ONCE ON TIMEOUT ===
            async Task<string> TryOnce()
            {
                using var cts = new System.Threading.CancellationTokenSource(HttpTimeout);
                HttpResponseMessage httpResponse = new HttpResponseMessage();
                string responseBody = "";

                try
                {
                    if (httpMethod.ToUpper() == "POST" &&
                        route.EndsWith("upload", StringComparison.OrdinalIgnoreCase))
                    {
                        var paramsDict = parameters as IDictionary<string, object>
                            ?? parameters?.GetType().GetProperties()
                                .ToDictionary(p => p.Name, p => p.GetValue(parameters));

                        paramsDict.TryGetValue("uploadPath", out var uploadPathObj);
                        string uploadPath = uploadPathObj?.ToString();

                        paramsDict.TryGetValue("elementId", out var elementIdObj);
                        string elementId = elementIdObj?.ToString();

                        if (!File.Exists(uploadPath))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                                $"upload file not found: [{uploadPath}]", null, GPALObjectType.None);
                            return "";
                        }

                        byte[] fileBytes = File.ReadAllBytes(uploadPath);
                        string base64Data = Convert.ToBase64String(fileBytes);
                        string mimeType = MimeKit.MimeTypes.GetMimeType(uploadPath);

                        var payload = new
                        {
                            elementId,
                            uploadPath,
                            mimeType,
                            base64Data
                        };

                        string jsonBody = System.Text.Json.JsonSerializer.Serialize(payload);
                        HttpContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                        var attachRequest = new HttpRequestMessage(HttpMethod.Post, route);
                        attachRequest.Content = content;

                        httpResponse = await _httpClient.SendAsync(attachRequest, cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        var request = new HttpRequestMessage(new HttpMethod(httpMethod.ToUpperInvariant()), route);

                        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                        foreach (var header in _restClient._headers)
                        {
                            // A content header must go on the content, or .NET throws "misused header name". Compare
                            // case-insensitively.
                            if (false == IsContentHeader(header.Key) && !request.Headers.Contains(header.Key))
                            {
                                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }

                        if (parameters != null)
                        {
                            // Send the caller's Content-Type, on the content. A site asking for form-urlencoded with a
                            // JSON body means it.
                            string contentType = RESTClient.GetContentType(contentEncoding);

                            foreach (var header in _restClient._headers)
                                if (true == "Content-Type".Equals(header.Key, StringComparison.OrdinalIgnoreCase)
                                    && false == string.IsNullOrWhiteSpace(header.Value))
                                    contentType = header.Value.Split(';')[0].Trim();   // the charset is StringContent's to add

                            object parms = parameters;

                            string parmsEncodingType = StringFormatDetector.DetectFormat(parameters.ToString());

                            if ("none".Equals(parmsEncodingType) && "application/json".Equals(contentType))
                            {
                                parms = System.Text.Json.JsonSerializer.Serialize(parameters);
                            }
                            else if ("none".Equals(parmsEncodingType) && "application/x-www-form-urlencoded".Equals(contentType))
                            {
                                if (!(parameters is string))
                                    parms = HttpUtility.UrlEncode(parameters.ToString());
                            }

                            string bodyText = parms?.ToString() ?? "";
                            request.Content = new StringContent(bodyText, System.Text.Encoding.UTF8, contentType);
                        }

                        httpResponse = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                    }

                    StatusCode = (int)httpResponse.StatusCode;
                    RetryAfterSeconds = ReadRetryAfter(httpResponse);

                    responseBody = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

                    // truncate the raw body for the debug event - this is only to get a sense of the contents when
                    // diagnosing downstream issues, not a full dump. Responses can be tens of MB, and interpolating
                    // the whole thing builds a giant string on every call, so cap it and note the real length.
                    const int maxRawLogChars = 200;
                    string rawForLog = string.IsNullOrEmpty(responseBody) || responseBody.Length <= maxRawLogChars
                        ? responseBody
                        : responseBody.Substring(0, maxRawLogChars) + $"...[truncated, {responseBody.Length} chars total]";

                    GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"raw result[{route}][{rawForLog}]", this, GPALObjectType.RestHelper);

                    string upperMethod = httpMethod.ToUpperInvariant();
                    if (string.IsNullOrEmpty(responseBody) &&
                        (upperMethod == "OPTIONS" || upperMethod == "HEAD"))
                    {
                        var headerDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                        foreach (var h in httpResponse.Headers)
                            headerDict[h.Key] = string.Join(", ", h.Value);

                        foreach (var h in httpResponse.Content.Headers)
                            if (!headerDict.ContainsKey(h.Key))
                                headerDict[h.Key] = string.Join(", ", h.Value);

                        responseBody = System.Text.Json.JsonSerializer.Serialize(headerDict,
                            new JsonSerializerOptions { WriteIndented = true });
                    }

                    if (false == httpResponse.IsSuccessStatusCode)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                            $"Request [{route}] failed with status code: [{StatusCode}] [{httpResponse.ReasonPhrase}]",
                            responseBody, GPALObjectType.None);
                    }

                    return responseBody;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                        $"Error in TryOnce for [{httpMethod}] [{route}]", null, GPALObjectType.RestClient, ex);
                    throw;
                }
            }

            // === TRY TWICE ===
            try
            {
                return await TryOnce().ConfigureAwait(false);
            }
            // Retry a transport error. Do not retry a cancellation; it used the whole timeout, and retrying would wait
            // it again.
            catch (Exception first) when (false == IsCancellation(first))
            {
                try
                {
                    await Task.Delay(2_000).ConfigureAwait(false);
                    return await TryOnce().ConfigureAwait(false); // second attempt
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                        $"Unable to communicate with: [{route}]",
                        null, GPALObjectType.None, ex);

                    // This browser's own endpoint unreachable twice means the browser is gone. A third-party service
                    // being down still returns nothing for the workflow to handle.
                    if (true == IsOurBrowser(route))
                        throw new GPALException($"{GPAL.MyMethodName()}: The browser at [{_browserExtensionApiBase}] did not answer [{route}] on either attempt, so there is nothing to drive");

                    return "";
                }
            }
            catch (Exception timedOut)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"Nothing answered [{route}] in [{TimeoutText()}], so the call was given up on",
                    null, GPALObjectType.None, timedOut);

                if (true == IsOurBrowser(route))
                    throw new GPALException($"{GPAL.MyMethodName()}: The browser at [{_browserExtensionApiBase}] did not answer [{route}] in [{TimeoutText()}], so there is nothing to drive");

                return "";
            }
        }
        /// <summary>
        /// True when the attempt timed out: TaskCanceledException, or the TimeoutException newer handlers wrap it in.
        /// Not retried.
        /// </summary>
        /// <param name="ex">The exception the attempt threw.</param>
        /// <returns>True when the attempt timed out.</returns>
        private static bool IsCancellation(Exception ex)
        {
            bool retVal = ex is OperationCanceledException || ex is TimeoutException;

            if (false == retVal && ex is HttpRequestException && null != ex.InnerException)
                retVal = ex.InnerException is OperationCanceledException || ex.InnerException is TimeoutException;

            return retVal;
        }

        /// <summary>
        /// The per-attempt timeout as text.
        /// </summary>
        /// <returns>The timeout in seconds, or "no limit".</returns>
        private string TimeoutText()
        {
            return System.Threading.Timeout.InfiniteTimeSpan == HttpTimeout
                ? "no limit"
                : $"{HttpTimeout.TotalSeconds:0.#} seconds";
        }

        /// <summary>
        /// True when an unanswered route means the browser is gone.<br/>
        /// The base url is shared with ContinueAsRESTClient sessions, so a base-url prefix check matches the session's
        /// site too. The browser's server runs locally and serves a known list of endpoints; a website does neither,
        /// and its silence is for the workflow to handle.
        /// </summary>
        /// <param name="route">The full url that was called</param>
        /// <returns>True when the route belongs to this browser's own REST server</returns>
        private bool IsOurBrowser(string route)
        {
            bool retVal = false;

            if (false == string.IsNullOrEmpty(_browserExtensionApiBase)
                && true == route.StartsWith(_browserExtensionApiBase, StringComparison.OrdinalIgnoreCase)
                && true == Uri.TryCreate(_browserExtensionApiBase, UriKind.Absolute, out Uri baseUri)
                && true == baseUri.IsLoopback)
            {
                string called = route.Substring(_browserExtensionApiBase.Length).Split('?')[0].Trim('/');

                retVal = Endpoints.Values.Any(known => known.Trim('/').Equals(called, StringComparison.OrdinalIgnoreCase));
            }

            return retVal;
        }

        public async Task<bool> CheckAPIStatus()
        {
            string route = $"{_browserExtensionApiBase}{Endpoints[ApiEndpoint.CheckStatus]}";
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    HttpResponseMessage response = await client.GetAsync(route).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    return responseBody.Equals("Server is running");
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
    }
    public static class StringFormatDetector
    {
        public static string DetectFormat(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "none";

            // Trim to avoid false negatives from whitespace
            var trimmed = input.Trim();

            // Check JSON
            if (IsJson(trimmed))
                return "json";

            // Check URL encoding
            if (IsUrlEncoded(trimmed))
                return "url";

            return "none";
        }

        internal static bool IsJson(string input)
        {
            if ((input.StartsWith("{") && input.EndsWith("}")) ||
                (input.StartsWith("[") && input.EndsWith("]")))
            {
                try
                {
                    using var doc = JsonDocument.Parse(input);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        private static bool IsUrlEncoded(string input)
        {
            try
            {
                var decoded = HttpUtility.UrlDecode(input);
                var reencoded = HttpUtility.UrlEncode(decoded);
                return string.Equals(input, reencoded, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }
}
