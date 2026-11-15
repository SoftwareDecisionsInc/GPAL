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
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Sets the name under which this execution's result will be stored, for later retrieval via the WithXFromResult methods.
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithResultName(string name)
        {
            _nextResultName = name;
            return this;
        }

        /// <summary>
        /// Synchronously executes the configured request and returns the result as a string.
        /// </summary>
        /// <returns>The result of the executed request</returns>
        public string Execute()
        {
            return Task.Run(async () => await ExecuteAsync().ConfigureAwait(false)).GetAwaiter().GetResult();
        }

        public T Execute<T>()
        {
            try
            {
                Task<T> task = ExecuteAsync<T>();

                // WinForms compatibilty
                // This keeps the UI thread blocked, but allows the internal awaits 
                // to complete successfully on a background thread.
                return Task.Run(async () => await task.ConfigureAwait(false)).GetAwaiter().GetResult();
            }
            catch (GPALException)
            {
                // Rethrow: the communicator throws GPALException when the browser is gone, and default(T) would look
                // like an answer.
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to execute synchronously for type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, ex);
                return default(T);
            }
        }

        /// <summary>
        /// Executes the configured request, then returns this client so further fluent calls can configure the next request.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IPuppeteerClient AndThen()
        {
            Execute();
            return this;
        }

        public IPuppeteerClient AndThen<T>()
        {
            Execute<T>();
            return this;
        }

        /// <summary>
        /// Asynchronously executes the configured request and returns the result as a string.
        /// </summary>
        /// <returns>Task that resolves to the result of the executed request</returns>
        public async Task<string> ExecuteAsync()
        {
            var result = await ExecuteInternalAsync().ConfigureAwait(false);

            // Reset after the call, not when the endpoint is set, so parameters set before the endpoint are still sent.
            Reset();

            return result?.ToString() ?? "";
        }

        public async Task<T> ExecuteAsync<T>()
        {
            dynamic result = await ExecuteInternalAsync().ConfigureAwait(false); // Returns Task<dynamic>

            // Reset after the call, not when the endpoint is set, so parameters set before the endpoint are still sent.
            Reset();

            if (result == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "ExecuteInternalAsync returned null.", this, GPALObjectType.PuppeteerClient, null);
                return default(T);
            }

            if (typeof(T) == typeof(JObject))
            {
                if (result is JObject)
                {
                    return (T)(object)result;
                }
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Expected JObject but got [{result.GetType().Name}].", this, GPALObjectType.PuppeteerClient, null);
                return default(T);
            }

            // Try direct deserialization for non-JObject types (e.g., bool, string)
            try
            {
                if (result is T)
                {
                    return (T)result;
                }
                if (result is JToken)
                {
                    return JsonConvert.DeserializeObject<T>(result.ToString());
                }

                // A plain value from the communicator, such as a list for EvaluateAll or QuerySelectors, converted
                // through JSON, which turns a list into an array
                return JToken.FromObject(result).ToObject<T>();
            }
            catch //(Exception ex)
            {
                //GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize [{result}] to type [{typeof(T).Name}]. [{ex.Message}]", this, GPALObjectType.PuppeteerClient, ex);
                // Continue to try CDP structure
            }

            // Try CDP structure (result.value or result)
            if (result is JObject jObject)
            {
                JToken resultToken = jObject["result"];
                if (resultToken == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Result object is missing 'result' property.", this, GPALObjectType.PuppeteerClient, null);
                    return default(T);
                }

                JToken valueToken = resultToken["value"];
                if (valueToken != null)
                {
                    // Special handling for string
                    if (typeof(T) == typeof(string) && valueToken.Type == JTokenType.String)
                    {
                        return (T)(object)valueToken.ToString();
                    }

                    try
                    {
                        return JsonConvert.DeserializeObject<T>(valueToken.ToString());
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize result.value [{valueToken.ToString()}] to type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, ex);
                        return default(T);
                    }
                }

                try
                {
                    return JsonConvert.DeserializeObject<T>(resultToken.ToString());
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to deserialize result [{resultToken.ToString()}] to type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, ex);
                    return default(T);
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported result type [{result.GetType().Name}] for type [{typeof(T).Name}].", this, GPALObjectType.PuppeteerClient, null);
            return default(T);
        }

        /// <summary>
        /// Returns a collection of all named results recorded so far across this client's executions.
        /// </summary>
        /// <returns>Collection of execution results keyed by result name</returns>
        /// <summary>
        /// Removes all <c>WithWorkflow</c> steps. Steps otherwise stay on the client, so the same set can run in
        /// several While or Until loops.
        /// </summary>
        /// <example><code>client.ClearWorkflows().WithWorkflow(c =&gt; ...).While(() =&gt; ...);</code></example>
        /// <returns>Fluent interface to declare the next workflow</returns>
        public IAllowPuppeteerWorkflow ClearWorkflows()
        {
            // the two lists are index aligned, so one is never emptied without the other
            _workflows.Clear();
            _workflowStates.Clear();

            return this;
        }

        /// <summary>
        /// Removes results stored with <c>WithResultName</c>; <c>GetExecutionResults</c> then returns only later ones.
        /// </summary>
        /// <example><code>client.ClearResults();</code></example>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowPuppeteerExecution ClearResults()
        {
            _executionResults.Clear();

            return this;
        }
        /// <summary>
        /// All named results in stored order, without the tab's target id.
        /// </summary>
        /// <returns>The results, ready to be read by name or by position</returns>
        public ResultCollection GetExecutionResults()
        {
            return new ResultCollection(_executionResults.Select(entry => (entry.Name, entry.Result, entry.Iteration)).ToList(), _name);
        }

        /// <summary>
        /// Every result stored under <paramref name="name"/>, in order: five loop passes return five. With no name,
        /// returns all; index that by name for the last one.
        /// </summary>
        /// <example><code>int passes = client.GetExecutionResults("atBottom").Count;</code></example>
        /// <param name="name">The name given to WithResultName</param>
        /// <returns>What was stored under that name</returns>
        public IReadOnlyList<object> GetExecutionResults(string name)
        {
            return GetExecutionResults().GetAllResults(name);
        }

        /// <summary>
        /// Adds a workflow step that invokes the given action against this client.
        /// </summary>
        /// <param name="workflow">Action to run as a workflow step</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerWorkflow WithWorkflow(Action<PuppeteerClient> workflow)
        {
            // Store the current endpoint, parameters and result name with the step; they are restored before each pass.
            _workflowStates.Add((_endpoint, _parameters, _nextResultName));
            _workflows.Add(workflow);

            return this;
        }

        /// <summary>
        /// Sets the maximum time a subsequent While/Until loop may run before timing out.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerWorkflow WhileLoopTimeout(int timeoutMs)
        {
            _whileLoopTimeoutMs = timeoutMs;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of iterations a subsequent While/Until loop may run.
        /// </summary>
        /// <param name="maxIterations">Maximum number of iterations</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerWorkflow WhileLoopMaxIterations(int maxIterations)
        {
            _whileLoopMaxIterations = maxIterations;
            return this;
        }

        /// <summary>
        /// Configures execution to repeat while the given condition returns true, subject to WhileLoopTimeout/WhileLoopMaxIterations.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        /// <summary>
        /// Runs the declared workflow steps over and over, restoring each step's endpoint and parameters before
        /// it runs. Bounded by <see cref="WhileLoopMaxIterations(int)"/> and <see cref="WhileLoopTimeout(int)"/>,
        /// defaulting to 1000 passes and no time limit.
        /// </summary>
        /// <param name="condition">Checked once before each pass</param>
        /// <param name="until">True to run for as long as the condition is false, false to run for as long as it is true</param>
        private void RunWorkflowLoop(Func<bool> condition, bool until)
        {
            if (0 == _workflows.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No workflow steps declared for the {(true == until ? "Until" : "While")} loop.",
                    this, GPALObjectType.PuppeteerClient);

                Reset();

                return;
            }

            // Read max iterations and timeout now; Reset clears both.
            int maxIterations = 0 < _whileLoopMaxIterations ? _whileLoopMaxIterations : 1000;
            int timeoutMs = 0 < _whileLoopTimeoutMs ? _whileLoopTimeoutMs : int.MaxValue;

            // While runs for as long as the condition holds, Until for as long as it does not
            Func<bool> anotherPass = true == until ? () => false == condition() : condition;

            int iteration = 0;
            DateTime started = DateTime.UtcNow;

            while (true == anotherPass()
                && maxIterations > iteration
                && timeoutMs > (DateTime.UtcNow - started).TotalMilliseconds)
            {
                _currentIteration = iteration;

                for (int step = 0; step < _workflows.Count; step++)
                {
                    (string Endpoint, object Parameters, string NextResultName)? declaredAs = _workflowStates[step];

                    if (true == declaredAs.HasValue)
                        (_endpoint, _parameters, _nextResultName) = declaredAs.Value;
                    else
                        Reset();

                    _workflows[step](this);
                }

                iteration++;
            }

            if (maxIterations <= iteration)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Loop stopped at its maximum of [{maxIterations}] passes.",
                    this, GPALObjectType.PuppeteerClient);

            Reset();

            _currentIteration = 1;
        }
        public IAllowPuppeteerParametersOrExecution While(Func<bool> condition)
        {
            RunWorkflowLoop(condition, false);

            return this;
        }

        /// <summary>
        /// Configures execution to repeat until the given condition returns true, subject to WhileLoopTimeout/WhileLoopMaxIterations.
        /// </summary>
        /// <param name="condition">Predicate evaluated each iteration</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution Until(Func<bool> condition)
        {
            RunWorkflowLoop(condition, true);

            return this;
        }

        /// <summary>
        /// Returns the result of a previous execution at the given index in the execution history, or null if the index is out of range.
        /// </summary>
        /// <param name="index">Zero-based index into the execution history</param>
        /// <returns>The result at that index, or null</returns>
        private object GetResultFromIndex(int index)
        {
            if (index < 0 || index >= _executionResults.Count) return null;
            return _executionResults[index].Result;
        }

        /// <summary>
        /// Last result stored under name, or null. Matches RESTClient for WithXFromResult chains.
        /// </summary>
        /// <param name="name">The result name to look up</param>
        /// <returns>The matching result, or null if none is found</returns>
        private object GetResultFromName(string name)
        {
            return _executionResults.LastOrDefault(entry => entry.Name == name).Result;
        }

        /// <summary>
        /// Sets a single named parameter on the request, creating the parameters dictionary if needed.
        /// </summary>
        /// <param name="key">The parameter name</param>
        /// <param name="value">The parameter value</param>
        private void SetParameter(string key, object value)
        {
            if (_parameters == null) _parameters = new Dictionary<string, object>();
            ((Dictionary<string, object>)_parameters)[key] = value;
        }
    }
}
