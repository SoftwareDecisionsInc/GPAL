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
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Per-provider connection definition: base URL, endpoint, default model, auth style, and any
    /// extra headers. Stored in AIProvidersConfig.yaml; built-in defaults cover XAI, OpenAI, Anthropic.
    /// </summary>
    public class AIProviderDefinition
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string ChatEndpoint { get; set; }
        public string DefaultModel { get; set; }
        public string AuthHeader { get; set; }
        public string AuthPrefix { get; set; }
        public string ResponseFormat { get; set; }
        public Dictionary<string, string> AdditionalHeaders { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Loads and saves the AI provider definitions used by GPAL.AI.WithProvider.
    /// Stored as ./AIProvidersConfig.yaml. Built-in defaults for XAI, OpenAI, and Anthropic are always
    /// available even without a config file; the file extends or overrides them.
    /// Auto-loaded silently on startup if the file exists.
    /// </summary>
    public class AIProvidersConfig
    {
        private static string ConfigFilePath = "./AIProvidersConfig.yaml";

        public static AIProvidersConfig Current { get; private set; } = Default();

        public List<AIProviderDefinition> Providers { get; set; } = new List<AIProviderDefinition>();

        public AIProviderDefinition Find(AIProviderType type) =>
            Providers.FirstOrDefault(p => string.Equals(p.Name, type.Name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Loads AI provider definitions from AIProvidersConfig.yaml (or a custom path).
        /// If the file is missing, writes the built-in defaults to disk for the user to edit.
        /// Falls back to built-in defaults if the file is invalid.
        /// </summary>
        public static AIProvidersConfig Load(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            if (!File.Exists(ConfigFilePath))
            {
                Default().Save();
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ConfigFilePath}] not found. Default written for editing.", null, GPALObjectType.None);
                return Default();
            }
            try
            {
                var loaded = new AIProvidersConfig();
                GPAL.Converter
                    .WithInput((GPALFile)ConfigFilePath)
                    .SaveTo(ref loaded);
                if (loaded?.Providers != null && loaded.Providers.Count > 0)
                {
                    Current = loaded;
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{ConfigFilePath}] loaded ([{loaded.Providers.Count}] AI provider(s)).", null, GPALObjectType.None);
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to load AIProvidersConfig", null, GPALObjectType.None, ex);
            }
            return Default();
        }

        /// <summary>
        /// Saves this configuration to AIProvidersConfig.yaml (or a custom path).
        /// </summary>
        public void Save(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            GPAL.Converter
                .WithInput(this)
                .SaveTo((GPALFile)ConfigFilePath);
        }

        /// <summary>
        /// Called during GPAL startup: silently loads AIProvidersConfig.yaml if it exists.
        /// No warning if the file is absent -- built-in defaults are always available.
        /// </summary>
        internal static void TryAutoLoad()
        {
            if (!File.Exists(ConfigFilePath)) return;
            Current = Load();
        }

        private static AIProvidersConfig Default() => new AIProvidersConfig
        {
            Providers = new List<AIProviderDefinition>
            {
                new AIProviderDefinition
                {
                    Name = "XAI",
                    BaseUrl = "https://api.x.ai/v1",
                    ChatEndpoint = "/chat/completions",
                    DefaultModel = "grok-2-latest",
                    AuthHeader = "Authorization",
                    AuthPrefix = "Bearer ",
                    ResponseFormat = "openai",
                    AdditionalHeaders = new Dictionary<string, string>()
                },
                new AIProviderDefinition
                {
                    Name = "OpenAI",
                    BaseUrl = "https://api.openai.com/v1",
                    ChatEndpoint = "/chat/completions",
                    DefaultModel = "gpt-4o-mini",
                    AuthHeader = "Authorization",
                    AuthPrefix = "Bearer ",
                    ResponseFormat = "openai",
                    AdditionalHeaders = new Dictionary<string, string>()
                },
                new AIProviderDefinition
                {
                    Name = "Anthropic",
                    BaseUrl = "https://api.anthropic.com/v1",
                    ChatEndpoint = "/messages",
                    DefaultModel = "claude-sonnet-4-6",
                    AuthHeader = "x-api-key",
                    AuthPrefix = "",
                    ResponseFormat = "anthropic",
                    AdditionalHeaders = new Dictionary<string, string> { { "anthropic-version", "2023-06-01" } }
                }
            }
        };
    }
}
