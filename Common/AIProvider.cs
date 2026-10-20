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
using System.Text.Json;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public class AIProvider
    {
        private readonly AIProviderType _providerType;
        private readonly IRESTClient _restClient;
        private readonly ICredentials _credentials;
        private readonly ClassificationConfig _config;
        private readonly AIProviderDefinition _definition;

        // A 429 is the provider's rate limit. Wait for Retry-After, capped, and send again.
        private const int RATE_LIMIT_ATTEMPTS = 3;
        private const int RATE_LIMIT_DEFAULT_WAIT = 5;
        private const int RATE_LIMIT_MAX_WAIT = 60;

        public AIProvider(AIProviderType providerType, IRESTClient restClient, ICredentials credentials, ClassificationConfig config)
        {
            _providerType = providerType;
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
            _credentials = credentials;
            _config = config ?? new ClassificationConfig();
            AIProvidersConfig.Load();
            _definition = AIProvidersConfig.Current.Find(providerType);
            if (_definition == null)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No provider definition found for [{providerType}]. Add it to AIProvidersConfig.yaml.", null, GPALObjectType.AIProvider);
        }

        public string Call(string prompt)
        {
            try
            {
                if (string.IsNullOrEmpty(prompt))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Prompt cannot be empty.", this, GPALObjectType.AIProvider);
                    return string.Empty;
                }

                string endpoint = GetEndpoint();
                string apiBase = GetApiBase();

                string accessToken = null;
                if (_credentials != null)
                {
                    _credentials.FetchAccessToken(out accessToken);
                    if (string.IsNullOrEmpty(accessToken))
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to login using credentials. No access token granted.", this, GPALObjectType.AIProvider);
                }

                var body = BuildRequestBody(prompt);

                string response = Send(apiBase, endpoint, accessToken, body);

                int attempt = 1;

                while (429 == _restClient.StatusCode && attempt < RATE_LIMIT_ATTEMPTS)
                {
                    int wait = RateLimitWait();

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{_providerType}] is rate limiting. Waiting [{wait}] seconds, then attempt [{attempt + 1}] of [{RATE_LIMIT_ATTEMPTS}]", this, GPALObjectType.AIProvider);

                    System.Threading.Thread.Sleep(wait * 1000);

                    response = Send(apiBase, endpoint, accessToken, body);
                    attempt++;
                }

                if (429 == _restClient.StatusCode)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{_providerType}] is still rate limiting after [{RATE_LIMIT_ATTEMPTS}] attempts.", this, GPALObjectType.AIProvider);
                    return string.Empty;
                }

                if (string.IsNullOrEmpty(response))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Empty response from AI provider.", this, GPALObjectType.AIProvider);
                    return string.Empty;
                }

                string completion = ExtractCompletion(response);
                if (string.IsNullOrEmpty(completion))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to extract completion from [{_providerType}] response: [{response}]", this, GPALObjectType.AIProvider);
                    return string.Empty;
                }

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Received response from [{_providerType}]", this, GPALObjectType.AIProvider);
                return completion;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to call AI provider", this, GPALObjectType.AIProvider, ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Builds and sends one request. The chain is cleared after sending, so a retry builds a new one.
        /// </summary>
        /// <param name="apiBase">The provider's base URL</param>
        /// <param name="endpoint">The chat endpoint</param>
        /// <param name="accessToken">The key or token, empty when the provider needs none</param>
        /// <param name="body">The request body</param>
        /// <returns>The raw response</returns>
        private string Send(string apiBase, string endpoint, string accessToken, Dictionary<string, object> body)
        {
            // NOTE: WithEndpoint() resets the client's headers, so headers must be added
            // after WithAPIBase/WithEndpoint, not before.
            var requestBuilder = (IAllowRESTParametersOrExecution)_restClient
                .WithAPIBase(apiBase, System.Threading.Timeout.InfiniteTimeSpan)
                .WithEndpoint(endpoint);

            if (!string.IsNullOrEmpty(accessToken))
            {
                var headerName = _definition?.AuthHeader ?? "Authorization";
                var prefix = _definition?.AuthPrefix ?? "Bearer ";
                _restClient.WithHeader(headerName, string.IsNullOrEmpty(prefix) ? accessToken : $"{prefix}{accessToken}");
            }

            if (_definition?.AdditionalHeaders != null)
            {
                foreach (var h in _definition.AdditionalHeaders)
                    _restClient.WithHeader(h.Key, h.Value);
            }

            string response = requestBuilder
                .WithParameters(body)
                .Execute();

            return response;
        }

        /// <summary>
        /// Returns the seconds to wait from the provider's Retry-After header.
        /// </summary>
        /// <returns>Seconds to wait, or a default when there is no header. Always capped.</returns>
        private int RateLimitWait()
        {
            int wait = _restClient.RetryAfterSeconds;

            if (0 >= wait)
                wait = RATE_LIMIT_DEFAULT_WAIT;

            if (RATE_LIMIT_MAX_WAIT < wait)
                wait = RATE_LIMIT_MAX_WAIT;

            return wait;
        }

        private Dictionary<string, object> BuildRequestBody(string prompt)
        {
            var messages = new List<object> { new Dictionary<string, object> { ["role"] = "user", ["content"] = prompt } };

            var body = new Dictionary<string, object>
            {
                ["model"] = GetModel(),
                ["messages"] = messages
            };

            if (_definition?.ResponseFormat == "anthropic")
            {
                body["max_tokens"] = GetMaxTokens();
            }

            if (_config.AdditionalParameters != null)
            {
                foreach (var param in _config.AdditionalParameters)
                {
                    if (IsReservedParameter(param.Key))
                        continue;
                    body[param.Key] = param.Value;
                }
            }

            return body;
        }

        private static bool IsReservedParameter(string key)
        {
            return key == "Endpoint" || key == "Model" || key == "MaxTokens" || key == "AnthropicVersion";
        }

        private int GetMaxTokens()
        {
            if (_config?.AdditionalParameters?.ContainsKey("MaxTokens") == true
                && int.TryParse(_config.AdditionalParameters["MaxTokens"].ToString(), out int maxTokens))
            {
                return maxTokens;
            }
            return 1024;
        }

        private string GetModel()
        {
            if (_config?.AdditionalParameters?.ContainsKey("Model") == true)
                return _config.AdditionalParameters["Model"].ToString();
            return _definition?.DefaultModel;
        }

        private string GetApiBase() => _definition?.BaseUrl;

        private string GetEndpoint()
        {
            if (_config?.AdditionalParameters?.ContainsKey("Endpoint") == true)
                return _config.AdditionalParameters["Endpoint"].ToString();
            return _definition?.ChatEndpoint ?? "/chat/completions";
        }

        private string ExtractCompletion(string response)
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (_definition?.ResponseFormat == "anthropic")
            {
                if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
                {
                    var first = content[0];
                    if (first.TryGetProperty("text", out var text))
                        return text.GetString();
                }
                return null;
            }

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var msgContent))
                    return msgContent.GetString();
            }
            return null;
        }
    }
}
