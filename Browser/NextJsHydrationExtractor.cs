// written by grok
// inspired by the Web Scraping Club article + React Flight discussions

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GenerallyPositive.Browser;
using System.Text.Json;   // for Browser instance if needed
using Newtonsoft.Json.Linq;

namespace GenerallyPositive
{
    public class NextJsHydrationResult
    {
        public bool Success { get; set; }
        public string RawNextDataJson { get; set; } = string.Empty;     // from __NEXT_DATA__ if present
        public List<string> FlightChunks { get; set; } = new List<string>();         // raw self.__next_f.push(...) contents
        public string ParsedStructuredData { get; set; } = string.Empty; // future: cleaned/parsed version
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = "Untitled";
        public int HydrationPayloadLength { get; set; }   // characters of hydration script content, not of the page
        public string Message { get; set; } = string.Empty;
        public bool IsPagesRouter { get; set; }      // true if __NEXT_DATA__ was found
        public bool IsAppRouter { get; set; }        // true if flight chunks were found

        private JsonDocument _parsedJson;

        public JsonDocument GetJson()
        {
            if (_parsedJson != null)
                return _parsedJson;

            if (string.IsNullOrWhiteSpace(RawNextDataJson))
            {
                Message = "RawNextDataJson is empty";
                return null;
            }

            try
            {
                _parsedJson = JsonDocument.Parse(RawNextDataJson);
                Message = "JSON parsed successfully";
                return _parsedJson;
            }
            catch (JsonException ex)
            {
                Message = $"JSON parse failed: [{ex.Message}]";

                // Show where it fails
                int errorPos = (int)(ex.LineNumber > 0 ? ex.LineNumber : 0);
                string snippet = RawNextDataJson.Length > 300
                    ? RawNextDataJson.Substring(0, 300) + "..."
                    : RawNextDataJson;

                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, Message);
                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"First 300 chars: [{snippet}]");

                return null;
            }
            catch (Exception ex)
            {
                Message = $"Unexpected error parsing JSON: {ex.Message}";
                return null;
            }
        }

        public void DisposeJson()
        {
            _parsedJson?.Dispose();
            _parsedJson = null;
        }
    }

    internal static class NextJsHydrationExtractor
    {
        // Reads the title, __NEXT_DATA__ and the Flight chunks from the live DOM, not the HTML. Script textContent
        // needs no unescaping, and all three come back as one JSON string.
        internal const string HydrationScript =
            "(function(){" +
            "  var nextData = document.querySelector('script#__NEXT_DATA__');" +
            "  var heading = document.querySelector('h1');" +
            "  var chunks = [];" +
            "  var scripts = document.querySelectorAll('script');" +
            "  for (var idx = 0; idx < scripts.length; idx++) {" +
            "    var text = scripts[idx].textContent;" +
            "    if (text && text.indexOf('self.__next_f.push') >= 0) chunks.push(text.trim());" +
            "  }" +
            "  return JSON.stringify({" +
            "    title: document.title || (heading ? heading.textContent.trim() : null)," +
            "    nextData: nextData ? nextData.textContent.trim() : null," +
            "    flightChunks: chunks" +
            "  });" +
            "})()";

        /// <summary>
        /// Builds the hydration result from the json <see cref="HydrationScript"/> produced.<br/>
        /// No HTML is parsed and no escaping is undone; payloads arrive as the page wrote them.
        /// </summary>
        /// <param name="browser">Browser the page belongs to</param>
        /// <param name="scriptsJson">Json holding title, nextData and flightChunks</param>
        /// <returns>The hydration result, with Success false when the page reported nothing</returns>
        public static NextJsHydrationResult ExtractFromScripts(Browser.Browser browser, string scriptsJson)
        {
            var result = new NextJsHydrationResult
            {
                HydrationPayloadLength = scriptsJson?.Length ?? 0,
                Url = browser.GetSetCurrentUrl()
            };

            if (string.IsNullOrWhiteSpace(scriptsJson))
            {
                result.Message = "The page reported nothing to extract";
                return result;
            }

            try
            {
                JObject reported = JObject.Parse(scriptsJson);

                result.Title = (string)reported["title"] ?? "Untitled";

                string nextData = (string)reported["nextData"];

                if (false == string.IsNullOrWhiteSpace(nextData))
                {
                    result.IsPagesRouter = true;
                    result.RawNextDataJson = nextData;
                    result.ParsedStructuredData = nextData;
                    result.Success = true;
                }

                var chunks = reported["flightChunks"] as JArray;

                if (null != chunks && 0 < chunks.Count)
                {
                    result.IsAppRouter = true;
                    result.FlightChunks = chunks.Select(chunk => (string)chunk).ToList();
                    result.ParsedStructuredData = DecodeFlightChunks(result.FlightChunks)
                                               ?? string.Join(Environment.NewLine, result.FlightChunks);
                    result.Success = true;
                    result.Message = $"Found {result.FlightChunks.Count} React Flight chunks (App Router)";
                }
                else if (false == result.Success)
                    result.Message = "No __NEXT_DATA__ or self.__next_f.push found. Not a detectable Next.js site or SSR disabled.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
            }

            return result;
        }
        /// <summary>
        /// Decodes a React Server Components (App Router) Flight stream into navigable JSON. The stream arrives as
        /// a series of <c>self.__next_f.push([1,"..."])</c> calls whose string payloads concatenate into a
        /// newline-separated list of <c>id:value</c> rows. This peels the payloads (JSON-decoding each push
        /// argument), reassembles the stream, and buckets the rows: "data" (the page model / props - the useful
        /// part), "modules" (I[...] client component references), and "other" (typed/unparseable rows). Returns
        /// null if nothing decodable was found, so the caller can fall back to the raw chunks.
        /// </summary>
        internal static string DecodeFlightChunks(List<string> flightScripts)
        {
            if (flightScripts == null || flightScripts.Count == 0)
                return null;

            try
            {
                // 1. Pull the string payload out of every push([...]) and concatenate in document order.
                var streamBuilder = new StringBuilder();
                foreach (var script in flightScripts)
                    foreach (var arr in ExtractPushArrays(script))
                    {
                        try
                        {
                            using (var doc = JsonDocument.Parse(arr))
                            {
                                var root = doc.RootElement;
                                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2 &&
                                    root[1].ValueKind == JsonValueKind.String)
                                    streamBuilder.Append(root[1].GetString());
                            }
                        }
                        catch { /* skip a malformed push, keep going */ }
                    }

                string stream = streamBuilder.ToString();
                if (string.IsNullOrWhiteSpace(stream))
                    return null;

                // 2. Split into id:value rows and bucket them.
                var data = new List<string>();
                var modules = new List<string>();
                var other = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                string UniqueKey(string id)
                {
                    string k = id; int n = 1;
                    while (seen.Contains(k)) k = id + "_" + (++n);
                    seen.Add(k);
                    return k;
                }

                string curId = null;
                var curContent = new StringBuilder();

                void Flush()
                {
                    if (curId == null) return;

                    string content = curContent.ToString();
                    string key = UniqueKey(curId);

                    if (IsValidJson(content))
                        data.Add($"\"{key}\":{content}");
                    else if (content.Length > 1 && char.IsLetter(content[0]) && IsValidJson(content.Substring(1)))
                    {
                        string val = content.Substring(1);
                        if ('I' == content[0]) modules.Add($"\"{key}\":{val}");
                        else other.Add($"\"{key}\":{val}");
                    }
                    else
                        other.Add($"\"{key}\":{JsonSerializer.Serialize(content)}");

                    curId = null;
                    curContent.Clear();
                }

                foreach (var line in stream.Split('\n'))
                {
                    int colon = line.IndexOf(':');
                    string prefix = colon > 0 ? line.Substring(0, colon) : null;
                    bool isRowStart = prefix != null && Regex.IsMatch(prefix, "^[0-9a-fA-F]+$");

                    if (isRowStart)
                    {
                        Flush();
                        curId = prefix;
                        curContent.Append(line.Substring(colon + 1));
                    }
                    else if (curId != null)
                    {
                        // continuation of a rare multi-line value
                        curContent.Append('\n').Append(line);
                    }
                }
                Flush();

                if (0 == data.Count && 0 == modules.Count && 0 == other.Count)
                    return null;

                // 3. Assemble one navigable object and pretty-print it.
                var combined = new StringBuilder();
                combined.Append("{\"data\":{").Append(string.Join(",", data)).Append("}");
                combined.Append(",\"modules\":{").Append(string.Join(",", modules)).Append("}");
                if (other.Count > 0)
                    combined.Append(",\"other\":{").Append(string.Join(",", other)).Append("}");
                combined.Append("}");

                return PrettyPrintOrRaw(combined.ToString());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Yields each <c>[...]</c> argument array from the <c>self.__next_f.push(...)</c> calls in a script,
        /// scanning with string/bracket awareness so brackets and quotes inside the payload do not confuse it.
        /// </summary>
        private static IEnumerable<string> ExtractPushArrays(string script)
        {
            if (string.IsNullOrEmpty(script))
                yield break;

            int idx = 0;
            while ((idx = script.IndexOf("__next_f.push(", idx, StringComparison.Ordinal)) >= 0)
            {
                int open = script.IndexOf('[', idx);
                if (open < 0)
                    yield break;

                int depth = 0;
                bool inStr = false, esc = false, closed = false;
                int end = -1;

                for (int i = open; i < script.Length; i++)
                {
                    char c = script[i];
                    if (inStr)
                    {
                        if (esc) esc = false;
                        else if ('\\' == c) esc = true;
                        else if ('"' == c) inStr = false;
                    }
                    else
                    {
                        if ('"' == c) inStr = true;
                        else if ('[' == c) depth++;
                        else if (']' == c) { depth--; if (0 == depth) { end = i; closed = true; break; } }
                    }
                }

                if (false == closed)
                    yield break;

                yield return script.Substring(open, end - open + 1);
                idx = end + 1;
            }
        }

        private static bool IsValidJson(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            char c = s[0];
            // fast reject: JSON values start with one of these
            if ('{' != c && '[' != c && '"' != c && '-' != c && false == char.IsDigit(c)
                && "tfn".IndexOf(c) < 0)
                return false;
            try { using (JsonDocument.Parse(s)) return true; }
            catch { return false; }
        }

        public static void SaveToFile(NextJsHydrationResult result, string filePath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"# Next.js Hydration Data - {result.Title}");
            sb.AppendLine($"URL: {result.Url}");
            sb.AppendLine($"Type: {DescribeType(result)}");
            sb.AppendLine($"Saved: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Hydration payload length: {result.HydrationPayloadLength / 1024} KB");
            sb.AppendLine("---");

            if (result.IsPagesRouter && !string.IsNullOrEmpty(result.RawNextDataJson))
            {
                sb.AppendLine("## __NEXT_DATA__ (JSON)");
                sb.AppendLine(PrettyPrintOrRaw(result.RawNextDataJson));
            }
            else if (result.IsAppRouter)
            {
                sb.AppendLine($"## React Flight (decoded from {result.FlightChunks.Count} chunks)");
                sb.AppendLine(result.ParsedStructuredData);
            }
            else
            {
                // Nothing detected: record why, so the file is not just an unexplained empty header.
                sb.AppendLine(string.IsNullOrWhiteSpace(result.Message)
                    ? "No Next.js hydration data (__NEXT_DATA__ or self.__next_f.push) found on this page."
                    : result.Message);
            }

            FileHelper.EnsureFolderFor(filePath);
            File.WriteAllText(filePath, sb.ToString());
        }

        /// <summary>Router type as text for the file header, including when nothing was found.</summary>
        private static string DescribeType(NextJsHydrationResult result)
        {
            if (result.IsPagesRouter) return "Pages Router (__NEXT_DATA__)";
            if (result.IsAppRouter) return "App Router (React Flight)";
            return "None detected (no Next.js SSR hydration data)";
        }

        /// <summary>
        /// Returns the JSON indented and readable when it parses, or the original string unchanged when it does
        /// not (so a malformed payload is still preserved rather than lost).
        /// </summary>
        private static string PrettyPrintOrRaw(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                    return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
            }
            catch
            {
                return json;
            }
        }

    }
}