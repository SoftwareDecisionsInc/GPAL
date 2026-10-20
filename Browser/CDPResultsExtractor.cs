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

// In CDPResultsExtractor.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    internal static class CdpResultExtractors
    {
        public static Dictionary<DevToolsMethods, Func<JObject, object>> Build(string protocolJson)
        {
            if (string.IsNullOrEmpty(protocolJson))
                throw new ArgumentException("Invalid protocol JSON");

            var protocol = JsonConvert.DeserializeObject<ProtocolRoot>(protocolJson);
            if (protocol == null)
                throw new ArgumentException("Failed to parse protocol JSON");

            var commandToReturns = protocol.Domains
                .SelectMany(d => (d.Commands ?? new List<ProtocolCommand>())
                    .Select(c => new
                    {
                        Key = $"{d.Domain}.{c.Name}",
                        ReturnNames = (c.Returns ?? new List<ProtocolReturn>())
                                      .Select(r => r.Name)
                                      .ToArray()
                    }))
                .ToDictionary(x => x.Key, x => x.ReturnNames, StringComparer.OrdinalIgnoreCase);

            var enumToCommand = Enum.GetValues(typeof(DevToolsMethods))
                .Cast<DevToolsMethods>()
                .ToDictionary(
                    m => m,
                    m =>
                    {
                        var field = m.GetType().GetField(m.ToString());
                        var attr = field?.GetCustomAttribute<CommandAttribute>();
                        return attr?.Name ?? m.ToString().Replace("_", ".");
                    },
                    EqualityComparer<DevToolsMethods>.Default);

            var overrides = new Dictionary<string, Func<JObject, object>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Page.getNavigationHistory"] = m =>
                {
                    var idx = (int?)m["currentIndex"] ?? -1;
                    var entries = m["entries"] as JArray;
                    if (entries != null && idx >= 0 && idx < entries.Count)
                    {
                        return entries[idx]?["url"]?.Value<string>();
                    }
                    else
                    {
                        return new { CurrentIndex = m["currentIndex"]?.Value<int>(), Entries = entries };
                    }

                },
                ["DOM.querySelectorAll"] = m => m["nodeIds"]?.ToObject<int[]>() ?? new int[0],
                ["Page.captureScreenshot"] = m => m["data"]?.Value<string>(),
                ["Target.createTarget"] = m => m["targetId"]?.Value<string>(),
                ["Target.attachToTarget"] = m => m["sessionId"]?.Value<string>(),
                ["Runtime.evaluate"] = m =>
                {
                    var result = m["result"];
                    if (result == null) return null;
                    var ro = result["result"];
                    if (ro?["objectId"] == null && ro["value"] != null && ro["type"]?.Value<string>() != "undefined")
                        return ro["value"].ToObject<object>();
                    return ro;
                },
                ["DOM.getDocument"] = m => m["root"],
                ["DOM.describeNode"] = m => m["node"],
                ["DOM.getAttributes"] = m => m["attributes"]?.ToObject<string[]>(),
                ["DOM.getBoxModel"] = m => m["model"],
                ["Runtime.getProperties"] = m => m["result"]?.ToObject<object[]>(),
                ["DOM.resolveNode"] = m => m["node"],
                ["Page.enable"] = m => null,
                ["DOM.enable"] = m => null,
                ["Network.enable"] = m => null,
                ["Emulation.setAutomationOverride"] = m => null,
                ["Input.dispatchMouseEvent"] = m => null,
                ["Input.insertText"] = m => null
            };

            var dict = new Dictionary<DevToolsMethods, Func<JObject, object>>();

            foreach (var kv in enumToCommand)
            {
                var method = kv.Key;
                var commandName = kv.Value;

                if (overrides.TryGetValue(commandName, out var overrideExtractor))
                {
                    dict[method] = overrideExtractor;
                    continue;
                }

                if (!commandToReturns.TryGetValue(commandName, out var returns) || returns.Length == 0)
                {
                    dict[method] = m => m.ToObject<object>(); // Return full result instead of null
                    continue;
                }

                if (returns.Length == 1)
                {
                    var name = returns[0];
                    dict[method] = m => m[name]?.ToObject<object>();
                }
                else
                {
                    var names = returns;
                    dict[method] = m =>
                    {
                        var result = new Dictionary<string, object>();
                        foreach (var n in names)
                        {
                            if (m[n] != null)
                                result[n] = m[n].ToObject<object>();
                        }
                        return result.Count > 0 ? result : m.ToObject<object>();
                    };
                }
            }

            foreach (var kv in enumToCommand.Where(kv => !dict.ContainsKey(kv.Key)))
            {
                dict[kv.Key] = m => m.ToObject<object>();
            }

            return dict;
        }

        public class ProtocolRoot
        {
            [JsonProperty("domains")]
            public List<ProtocolDomain> Domains { get; set; } = new List<ProtocolDomain>();
        }

        public class ProtocolDomain
        {
            [JsonProperty("domain")]
            public string Domain { get; set; } = string.Empty;

            [JsonProperty("commands")]
            public List<ProtocolCommand> Commands { get; set; } = new List<ProtocolCommand>();

            [JsonProperty("types")]
            public List<ProtocolType> Types { get; set; } = new List<ProtocolType>();
        }

        public class ProtocolCommand
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("returns")]
            public List<ProtocolReturn> Returns { get; set; } = new List<ProtocolReturn>();
        }

        public class ProtocolReturn
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;

            [JsonProperty("$ref")]
            public string Ref { get; set; } = string.Empty;
        }

        public class ProtocolType
        {
            [JsonProperty("id")]
            public string Id { get; set; } = string.Empty;
        }

        [AttributeUsage(AttributeTargets.Field)]
        public sealed class CommandAttribute : Attribute
        {
            public string Name { get; }
            public CommandAttribute(string name) => Name = name;
        }
    }
}
