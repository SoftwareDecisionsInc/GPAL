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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public partial class RESTClient
    {
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
                        // The response is a stream url or base64 content
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

                // Clear the chain on a failed call too, so its parameters do not go out with the next one
                Reset();

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

                            // Clear the chain on a failed call too, so its parameters do not go out with the next one
                            Reset();

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

                    var result = await _helper.Call(_endpoint, _parameters, _httpMethod, _contentEncoding).ConfigureAwait(false);
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

                // Clear the chain on a failed call too, so its parameters do not go out with the next one
                Reset();

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

                    T result = await _helper.CallAndDeserialize<T>(_endpoint, _parameters, _httpMethod, _contentEncoding).ConfigureAwait(false);
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

                // Clear the chain on a failed call too, so its parameters do not go out with the next one
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
    }
}
