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
using System.Text.RegularExpressions;

namespace GenerallyPositive
{
    /// <summary>
    /// One request the page made, recorded while <c>.CaptureCalls</c> was on. A <see cref="GPALRequest"/> describes a
    /// request to send; this records one that was sent, so an endpoint can be read off a page.
    /// </summary>
    public class GPALCall
    {
        /// <summary>The full url requested.</summary>
        public string Url { get; set; }
        /// <summary>The HTTP method, GET, POST and so on.</summary>
        public string Method { get; set; }
        /// <summary>What the browser was fetching it as: XHR, Fetch, Document, Script, Image and the rest.</summary>
        public string ResourceType { get; set; }
        /// <summary>The request body, when it had one. Null for a GET.</summary>
        public string PostData { get; set; }
        /// <summary>What set it off: parser, script, preload or other.</summary>
        public string Initiator { get; set; }
        /// <summary>
        /// The HTTP status the page got, or 0 if no response yet.
        /// </summary>
        public int Status { get; set; }
        /// <summary>The browser's own id for the request, used to match the response to it.</summary>
        internal string RequestId { get; set; }
        /// <summary>
        /// The headers the page sent, including those its code added. APIs often need these; add them back with
        /// <c>.WithHeader</c>.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Describes an API request for .Fetch to send from inside the page, with the browser's cookies, TLS fingerprint,
    /// header order and anti-bot clearance.<br/>
    /// Built once and reused, like a Selector.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALRequest productSearch = (GPALRequest)GPAL.Request
    ///     .WithPath("/api/products/search")
    ///     .WithHttpMethod("POST")
    ///     .WithBody("{\"query\":\"{0}\",\"hitsPerPage\":{1},\"page\":{2}}")
    ///     .WithPageToken("{2}")           // the row fills {0} and {1}, GPAL fills {2}
    ///     .CallAfterFetch(ProcessSearch)
    ///     .WithName("product search");
    /// </code>
    /// </example>
    /// <remarks>
    /// {0}, {1} and on are the columns of the WithTokensFrom row. Substitution is plain text, anywhere .Fetch sends:
    /// path, parameter, header or body.
    /// </remarks>
    public class GPALRequest : IAllowRequestSettings
    {
        /// <summary>Path or full URL. A path resolves against the browser's current origin.</summary>
        public string Path { get; internal set; }

        /// <summary>HTTP method. Default GET.</summary>
        public string Method { get; internal set; } = "GET";

        /// <summary>Request body, sent as-is.</summary>
        public string Body { get; internal set; }

        /// <summary>Content type of the body.</summary>
        public string ContentType { get; internal set; } = "application/json";

        /// <summary>Query parameters, in the order they were added.</summary>
        public List<KeyValuePair<string, string>> Parameters { get; internal set; } = new List<KeyValuePair<string, string>>();

        /// <summary>Extra request headers, on top of whatever the browser sends for itself.</summary>
        public List<KeyValuePair<string, string>> Headers { get; internal set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// The API's first page number: zero for Algolia and most search backends, one for many others.
        /// </summary>
        public int FirstPage { get; internal set; }

        /// <summary>
        /// The placeholder replaced with the page number, anywhere .Fetch sends: path, parameter value, header value or
        /// body. Plain text replacement. Change it with .WithPageToken if the default appears in the payload.
        /// </summary>
        public string PageToken { get; internal set; } = "{page}";

        /// <summary>Called once per row of tokens, with that row's pages. Null when nothing was said.</summary>
        public Browser.Browser.CallAfterFetchDelegate AfterFetch { get; internal set; }

        /// <summary>Name used in messages and errors.</summary>
        public string Name { get; internal set; }

        internal GPALRequest()
        {
        }

        /// <summary>
        /// Returns this request, so a chain can be assigned without an explicit cast.
        /// </summary>
        /// <returns>The request</returns>
        public GPALRequest ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Path or full URL to request. A path such as "/api/search" is resolved against the origin the browser
        /// is already on, which is what a site proxying its own API needs.
        /// </summary>
        /// <param name="path">Path or full URL</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithPath(string path)
        {
            Path = path;
            return this;
        }

        /// <summary>
        /// HTTP method to use, for example GET, POST, PUT or DELETE. GET when nothing was said.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithHttpMethod(Enums.HttpVerb method)
        {
            Method = method;
            return this;
        }

        /// <summary>
        /// Adds a query parameter, written as "name=value". Call once per parameter.
        /// </summary>
        /// <param name="nameValue">Parameter as "name=value"</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithParameter(string nameValue)
        {
            Parameters.Add(SplitPair(nameValue, '='));
            return this;
        }

        /// <summary>
        /// Replaces the value of a query parameter with the next numbered token, so a request taken from a page
        /// can be varied without writing the url out again.<br/>
        /// The first call makes it {0}, the next {1}, matching the columns .WithTokensFrom supplies.
        /// </summary>
        /// <example>
        /// <code>
        /// browser
        ///     .WithCallFilter("/availability-api/multiple-price-from")
        ///     .CaptureCallTemplate(out GPALRequest priceFrom);
        ///
        /// priceFrom.WithTokenFor("startCheckDate");    // its value becomes {0}
        /// </code>
        /// </example>
        /// <remarks>
        /// Replaces named query parameters and JSON fields only, not path segments, array values, XML, GraphQL or
        /// protobuf. Every occurrence of the name takes the same token. Substitution is literal, so the row supplies
        /// encoded values. For other shapes, write the token into the path or body yourself.
        /// </remarks>
        /// <param name="parameterName">Name of the query parameter or JSON field whose value becomes a token</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithTokenFor(string parameterName)
        {
            // Use the next unused token number.
            int token = 0;

            while (true == Path?.Contains($"{{{token}}}") || true == Body?.Contains($"{{{token}}}"))
                token++;

            // The value can be in a url (name=value up to the next &) or a JSON body ("name":"value" or "name":value).
            string inUrl = $"([?&]{Regex.Escape(parameterName)}=)[^&]*";
            string inJson = "(\"" + Regex.Escape(parameterName) + "\"\\s*:\\s*)(\"[^\"]*\"|[^,}\\s]+)";

            if (false == string.IsNullOrEmpty(Path))
                Path = Regex.Replace(Path, inUrl, "${1}" + $"{{{token}}}");

            if (false == string.IsNullOrEmpty(Body))
            {
                Body = Regex.Replace(Body, inUrl, "${1}" + $"{{{token}}}");
                Body = Regex.Replace(Body, inJson, "${1}" + $"\"{{{token}}}\"");
            }

            return this;
        }

        /// <summary>
        /// Adds a request header, written as "Name: value". Call once per header. The browser sends its own
        /// headers regardless; these are added on top.
        /// </summary>
        /// <param name="nameValue">Header as "Name: value"</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithHeader(string nameValue)
        {
            Headers.Add(SplitPair(nameValue, ':'));
            return this;
        }

        /// <summary>
        /// Body to send, used as-is. Sets the content type to application/json unless .WithContentType says
        /// otherwise.
        /// </summary>
        /// <param name="body">Request body</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithBody(string body)
        {
            Body = body;
            return this;
        }

        /// <summary>
        /// Content type of the body. application/json when nothing was said.
        /// </summary>
        /// <param name="contentType">Content type</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithContentType(Enums.ContentType contentType)
        {
            ContentType = contentType;
            return this;
        }

        /// <summary>
        /// Sets the API's first page number. Zero for Algolia and most search backends, one for many others. Default
        /// zero.
        /// </summary>
        /// <param name="firstPage">Number of the first page</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithFirstPage(int firstPage)
        {
            FirstPage = firstPage;
            return this;
        }

        /// <summary>
        /// The marker replaced with the page number, wherever it appears in the path, a parameter value, a header
        /// value or the body. Pick your own when the default would collide with the payload.
        /// </summary>
        /// <param name="token">Marker to replace. [{page} (default)]</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings WithPageToken(string token = "{page}")
        {
            PageToken = token;
            return this;
        }

        /// <summary>
        /// Sets a handler called after each row of tokens is fetched, with that row's pages. It is on the request, not
        /// the browser, because .Fetch knows what it will request.<br/>
        /// Return -1 to stop.
        /// </summary>
        /// <param name="callAfterFetch">Handler to call once per row of tokens</param>
        /// <returns>Fluent interface to define the request more</returns>
        public IAllowRequestSettings CallAfterFetch(Browser.Browser.CallAfterFetchDelegate callAfterFetch)
        {
            AfterFetch = callAfterFetch;
            return this;
        }

        /// <summary>
        /// Names the request for messages and errors. Narrows the interface, so it goes last.
        /// </summary>
        /// <param name="name">Name of the request</param>
        /// <returns>Fluent interface to end the declaration</returns>
        public IAllowToGPALObject<GPALRequest> WithName(string name)
        {
            Name = name;
            return this;
        }

        /// <summary>
        /// Builds the url for one page: path plus parameters, with every token replaced.
        /// </summary>
        /// <param name="page">Zero based page number</param>
        /// <param name="row">Values for the numbered tokens, or null when there is no token source</param>
        /// <returns>Path with query string; still relative if the path was</returns>
        internal string ResolveUrl(int page, List<string> row)
        {
            List<string> query = new List<string>();

            foreach (KeyValuePair<string, string> parameter in Parameters)
                query.Add($"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(Resolve(parameter.Value ?? string.Empty, page, row))}");

            string path = Resolve(Path, page, row);

            return 0 == query.Count
                ? path
                : path + (path?.Contains("?") ?? false ? "&" : "?") + string.Join("&", query);
        }

        /// <summary>
        /// Returns the body for one page with every token replaced. The body is not parsed.
        /// </summary>
        /// <param name="page">Zero based page number</param>
        /// <param name="row">Values for the numbered tokens, or null when there is no token source</param>
        /// <returns>The body with its tokens replaced</returns>
        internal string ResolveBody(int page, List<string> row)
        {
            return Resolve(Body, page, row);
        }

        /// <summary>
        /// Replaces the page token with the page number (offset by the first page), then each numbered token with the
        /// row's matching column. The page token goes first, so it wins over a column with the same index.
        /// </summary>
        /// <param name="text">Text that may hold tokens</param>
        /// <param name="page">Zero based page number</param>
        /// <param name="row">Values for the numbered tokens, or null when there is no token source</param>
        /// <returns>The text with its tokens replaced</returns>
        string Resolve(string text, int page, List<string> row)
        {
            string resolved = text?.Replace(PageToken, (FirstPage + page).ToString());

            if (null != resolved && null != row)
                for (int token = 0; token < row.Count; token++)
                    resolved = resolved.Replace($"{{{token}}}", row[token] ?? string.Empty);

            return resolved;
        }

        /// <summary>
        /// Returns how many columns a row of tokens needs: one more than the highest numbered token, not counting the
        /// page token.
        /// </summary>
        /// <returns>Columns a row needs; zero when the request uses no numbered tokens</returns>
        internal int TokenCount()
        {
            int highest = -1;

            foreach (string text in TokenizedText())
                foreach (Match match in Regex.Matches(text ?? string.Empty, @"\{(\d+)\}"))
                    if (match.Value != PageToken)
                        highest = Math.Max(highest, int.Parse(match.Groups[1].Value));

            return highest + 1;
        }

        /// <summary>
        /// Every piece of this request a token can appear in: the path, each parameter value, each header value
        /// and the body.
        /// </summary>
        /// <returns>The text of the request, in no particular order</returns>
        List<string> TokenizedText()
        {
            List<string> text = new List<string> { Path, Body };

            foreach (KeyValuePair<string, string> parameter in Parameters)
                text.Add(parameter.Value);

            foreach (KeyValuePair<string, string> header in Headers)
                text.Add(header.Value);

            return text;
        }

        /// <summary>
        /// Flattens the headers to name, value, name, value, which is all the layers below need to rebuild them,
        /// with every token in a value already replaced.
        /// </summary>
        /// <param name="page">Zero based page number</param>
        /// <param name="row">Values for the numbered tokens, or null when there is no token source</param>
        /// <returns>Header names and values in pairs</returns>
        internal string[] HeaderPairs(int page, List<string> row)
        {
            List<string> pairs = new List<string>();

            foreach (KeyValuePair<string, string> header in Headers)
            {
                pairs.Add(header.Key);
                pairs.Add(Resolve(header.Value, page, row));
            }

            return pairs.ToArray();
        }

        /// <summary>
        /// Splits "name=value" or "Name: value" on its first separator, so a value containing the separator
        /// survives intact.
        /// </summary>
        /// <param name="pair">The text to split</param>
        /// <param name="separator">Character separating name from value</param>
        /// <returns>The name and value</returns>
        static KeyValuePair<string, string> SplitPair(string pair, char separator)
        {
            KeyValuePair<string, string> split = new KeyValuePair<string, string>(pair, string.Empty);
            int at = pair?.IndexOf(separator) ?? -1;

            if (0 <= at)
                split = new KeyValuePair<string, string>(pair.Substring(0, at).Trim(), pair.Substring(at + 1).Trim());

            return split;
        }
    }
}
