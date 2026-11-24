// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient : IPuppeteerClient
    {
        /// <summary>
        /// Workflow steps queued via WithWorkflow, run in order against this client by Run/While/Until.
        /// </summary>
        private readonly List<Action<PuppeteerClient>> _workflows = new List<Action<PuppeteerClient>>();
        /// <summary>
        /// Saved (endpoint, parameters, next result name) state for each workflow step, restored before each step executes.
        /// </summary>
        private  List<(string Endpoint, object Parameters, string NextResultName)?> _workflowStates { get; set; } = new List<(string, object, string)?>();
        /// <summary>
        /// True when the endpoint was set directly via WithEndpoint(string) rather than from an ApiEndpoint enum value.
        /// </summary>
        private bool _customEndpoint { get; set; } = false;
        /// <summary>
        /// The ApiEndpoint enum value selected for the current/next execution.
        /// </summary>
        private ApiEndpoint _apiEndpoint;
        /// <summary>
        /// The resolved endpoint path/URL to call for the current/next execution.
        /// </summary>
        private string _endpoint { get; set; }
        /// <summary>
        /// The parameters object to send to the endpoint for the current/next execution.
        /// </summary>
        private object _parameters { get; set; }
        /// <summary>
        /// The content encoding (e.g. Json) used for requests sent to the puppeteer communicator.
        /// </summary>
        internal ContentEncoding _contentEncoding = ContentEncoding.Json;
        /// <summary>
        /// The result name under which the current execution's result is stored, set via WithResultName/WithName.
        /// </summary>
        private string _name { get; set; }
        /// <summary>
        /// The default result name used when no name is explicitly set via WithResultName.
        /// </summary>
        private string _nextResultName { get; set; }    // null: results are kept only when asked, since kept results accumulate
        /// <summary>
        /// The file to save the response to, set via WithSaveFile, or null if the response should not be saved.
        /// </summary>
        private string _saveFile { get; set; }

        /// <summary>
        /// History of executed steps and their results, keyed by name, used by WithXFromResult/GetResultFromName/GetResultFromIndex.
        /// </summary>
        private readonly List<(string Name, object Result, int Iteration, string TargetId)> _executionResults = new List<(string Name, object Result, int Iteration, string TargetId)>();
        /// <summary>
        /// The PuppeteerCommunicator used to send commands to the puppeteer/CDP server.
        /// </summary>
        private PuppeteerCommunicator _puppeteerCommunicator { get; set; }
        /// <summary>
        /// The current iteration count for While/Until workflow loops.
        /// </summary>
        private int _currentIteration { get; set; }
        /// <summary>
        /// Extra HTTP headers to send with the request, set via WithHeader.
        /// </summary>
        internal Dictionary<string, string> _headers = new Dictionary<string, string>();
        /// <summary>
        /// Prune interval, in milliseconds, used by CheckNetworkIdle, set via WithPruneMs.
        /// </summary>
        private int _pruneMs = 3_000;
        /// <summary>
        /// Maximum number of in-flight connections allowed before the network is considered busy, used by CheckNetworkIdle.
        /// </summary>
        private int _maxConnections { get; set; }
        /// <summary>
        /// Horizontal pixel amount for scroll operations, set via WithHPixels.
        /// </summary>
        private int _hPixels { get; set; }
        /// <summary>
        /// Vertical pixel amount for scroll operations, set via WithVPixels.
        /// </summary>
        private int _vPixels { get; set; }
        /// <summary>
        /// The attribute name targeted by SetAttribute/GetAttribute, set via WithAttribute.
        /// </summary>
        private string _attribute { get; set; }
        /// <summary>
        /// The attribute value to set, used together with <see cref="_attribute"/>, set via WithValue.
        /// </summary>
        private string _value { get; set; }
        /// <summary>
        /// The text to send for SendString/FillIn operations, set via WithText.
        /// </summary>
        private string _text { get; set; }
        /// <summary>
        /// REST client used for validating endpoint configuration.
        /// </summary>
        private RESTClient restClient { get; set; }  // for validation
        /// <summary>
        /// The browser target/tab id that the current execution applies to.
        /// </summary>
        internal string _currentTargetId { get; set; }
        /// <summary>
        /// Maps each REST endpoint path to the async PuppeteerCommunicator method that implements it.
        /// </summary>
        private Dictionary<string, Func<PuppeteerCommunicator, IDictionary<string, object>, string, Task<object>>> endpointMap { get; set; }

        /// <summary>
        /// The predicate evaluated each iteration of a While/Until workflow loop.
        /// </summary>
        /// <summary>
        /// The kind of loop in effect: "While" or "Until".
        /// </summary>
        /// <summary>
        /// Maximum time, in milliseconds, a While/Until workflow loop may run before timing out.
        /// </summary>
        private int _whileLoopTimeoutMs { get; set; } // loop timeout
        /// <summary>
        /// Maximum number of iterations a While/Until workflow loop may run.
        /// </summary>
        private int _whileLoopMaxIterations { get; set; } // loop max iterations

        /// <summary>
        /// Creates a new PuppeteerClient with a default RESTClient and initialized endpoint map.<br/>
        /// One per browser, built by its PuppeteerCommunicator and reached through browser.PuppeteerClient.
        /// Holds the target it is bound to, so it cannot be shared between browsers.
        /// </summary>
        internal PuppeteerClient()
        {
            restClient = new RESTClient();
            init();
        }

        /// <summary>
        /// Returns this client cast to its public IPuppeteerClient interface.
        /// </summary>
        /// <returns>This client as an IPuppeteerClient</returns>
        public IPuppeteerClient ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// The display name assigned to this client via WithName.
        /// </summary>
        public string Name
        {
            get => _name;
        }

        /// <summary>
        /// Sets the PuppeteerCommunicator used to send commands to the puppeteer/CDP server.
        /// </summary>
        /// <param name="communicator">The communicator to use for this client</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerEndpointDetails WithPuppeteerCommunicator(PuppeteerCommunicator communicator)
        {
            _puppeteerCommunicator = communicator;
            //_currentTargetId = _puppeteerCommunicator.GetInitialTargetId().Result;  // Fetch initial target ID
            return this;
        }
        /// <summary>
        /// Sets a custom endpoint path to call directly, bypassing the built-in ApiEndpoint enum.
        /// </summary>
        /// <param name="endpoint">The endpoint path to call</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithEndpoint(string endpoint)
        {
            _endpoint = endpoint;
            _customEndpoint = true;
            return this;
        }

        /// <summary>
        /// Sets the endpoint to call, resolved from the given ApiEndpoint value. Parameters set before this
        /// call survive it, so the endpoint and its parameters can be named in either order.
        /// </summary>
        /// <param name="endpoint">The API endpoint to call</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithEndpoint(ApiEndpoint endpoint)
        {
            _apiEndpoint = endpoint;
            _endpoint = RESTHelper.Endpoints.TryGetValue(endpoint, out var ep) ? ep : "";
            return this;
        }

        /// <summary>
        /// Sets the session token parameter for the request.
        /// </summary>
        /// <param name="sessionToken">The session token to send</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithSessionToken(string sessionToken)
        {
            SetParameter("sessionToken", sessionToken);
            return this;
        }
        /// <summary>
        /// Resets per-request state (endpoint, parameters, timeouts, loop settings) so the client is ready to build a new request.
        /// </summary>
        private void Reset()
        {
            _endpoint = "";
            _parameters = null;
            _customEndpoint = false;
            _nextResultName = null;     // don't save results unless asked, potential memory leak
            _pruneMs = 3_000;
            _maxConnections = 0;
            _hPixels = 0;
            _vPixels = 0;
            _attribute = null;
            _value = null;
            _text = null;
            _currentIteration = 1;
            _whileLoopTimeoutMs = 0;
            _whileLoopMaxIterations = 0;
        }
        /// <summary>
        /// Executes the configured endpoint, handling While/Until looping, saving the response to file, and recording the result for later retrieval.
        /// </summary>
        /// <returns>Task that resolves to the raw result of the executed endpoint</returns>
        private async Task<object> ExecuteInternalAsync()
        {
            if (true == _puppeteerCommunicator._windowSessionsQueue.IsEmpty)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No browser windows open. Cannot call endpoint [{_endpoint}]", null, GPALObjectType.None);
                return null;
            }

            if (string.IsNullOrEmpty(_endpoint))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{_name}] No endpoint specified", null, GPALObjectType.None);
                return null;
            }

            var parameters = _parameters as IDictionary<string, object> ??
                             (_parameters?.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(_parameters)) ?? new Dictionary<string, object>());

            if (!_customEndpoint && !restClient.ValidateParameters(_apiEndpoint, parameters))
            {
                return null;
            }

            object result = await ExecuteEndpoint(_endpoint, parameters).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(_saveFile))
            {
                SaveResponseToFile(result?.ToString(), _saveFile);
            }

            if (!string.IsNullOrEmpty(_nextResultName))
            {
                _executionResults.Add((_nextResultName, result, _executionResults.Where(r => r.Name == _nextResultName && r.TargetId == _currentTargetId).Select(r => r.Iteration).DefaultIfEmpty(0).Max() + 1, _currentTargetId));
                _workflowStates.Add((_endpoint, _parameters, _nextResultName));

            }

            // Only a plain value can carry a status; an object value, such as GetContentAndCss returns, cannot be read as text
            string valueStr = ((result as JObject)?["result"]?["value"] as JValue)?.Value<string>();
            if (!string.IsNullOrEmpty(valueStr))
            {
                int statusCode = 0;
                try
                {
                    statusCode = (int) JObject.Parse(valueStr)["status"]?.Value<int>();
                }
                catch { }

                if (statusCode > 0)
                {
                    _puppeteerCommunicator.ServerResponseCode = statusCode;
                    if (statusCode < 200 || statusCode >= 300)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{_name}] Endpoint [{_endpoint}] returned HTTP [{statusCode}]", null, GPALObjectType.None);
                }
            }

            // Reset loop state 
            _whileLoopTimeoutMs = 0;
            _whileLoopMaxIterations = 0;

            return result;
        }

        /// <summary>
        /// Looks up and invokes the handler registered for the given endpoint, passing the current effective session id.
        /// </summary>
        /// <param name="endpoint">The endpoint path to invoke</param>
        /// <param name="parameters">Parameters to pass to the handler</param>
        /// <returns>Task that resolves to the handler's result, or null if no handler is registered</returns>
        private async Task<object> ExecuteEndpoint(string endpoint, IDictionary<string, object> parameters)
        {
            if (endpointMap.TryGetValue(endpoint, out var handler))
            {
                // always use the most recent target/session
                var sessionId = _puppeteerCommunicator.GetEffectiveSessionId();
                if (sessionId == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No active session for execution. Using null.", this, GPALObjectType.Puppeteer);
                }

                return await handler(_puppeteerCommunicator, parameters, sessionId).ConfigureAwait(false);
            }

            return null;
        }

        /// <summary>
        /// Saves the given response text to the specified file path.
        /// </summary>
        /// <param name="response">The response text to save</param>
        /// <param name="filePath">The file path to write to</param>
        private void SaveResponseToFile(string response, string filePath)
        {
            RESTClient.SaveResponseToFile(response, filePath);
        }
    }
}
