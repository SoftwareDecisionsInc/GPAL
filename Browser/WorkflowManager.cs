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

using OpenQA.Selenium;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Manages multiple workflows and their execution in loops for the Browser class.
    /// </summary>
    internal class WorkflowManager
    {
        private readonly Browser _browser;

        internal WorkflowManager(Browser browser)
        {
            _browser = browser ?? throw new ArgumentNullException(nameof(browser)); // Internal, safe to throw
        }

        public IAllowWorkflowExecution WithWorkflow(Action<IBrowser> workflow)
        {
            if (workflow == null)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Workflow is null in WithWorkflow. Adding empty workflow.", _browser, GPALObjectType.Browser);
            _browser.BrowserSettings.Workflows.Add(workflow ?? (b => { }));
            return _browser;
        }

        public IAllowBrowserActionOrAnySelector While(Func<Browser, bool> predicate)
        {
            if (_browser.BrowserSettings.Workflows.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No workflows defined for While. Continuing without execution.", _browser, GPALObjectType.Browser);
                return _browser;
            }
            if (predicate == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Predicate is null for While. Continuing without execution.", _browser, GPALObjectType.Browser);
                return _browser;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Starting While loop with [{_browser.BrowserSettings.Workflows.Count}] workflow(s).", _browser, GPALObjectType.Browser);

            var startTime = DateTime.UtcNow;
            int iteration = 0;

            while (predicate(_browser) && (DateTime.UtcNow - startTime).TotalMilliseconds < _browser.BrowserSettings.WhileLoopTimeoutMs && iteration < _browser.BrowserSettings.WhileLoopMaxIterations)
            {
                _browser.WorkflowSetup(checkWaitFor: true, doMaintenance: true);
                foreach (var workflow in _browser.BrowserSettings.Workflows)
                    workflow(_browser);
                iteration++;
                Thread.Sleep(100);
            }

            if (iteration >= _browser.BrowserSettings.WhileLoopMaxIterations)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"While loop reached maximum iterations ([{_browser.BrowserSettings.WhileLoopMaxIterations}]).", _browser, GPALObjectType.Browser);
            else if ((DateTime.UtcNow - startTime).TotalMilliseconds >= _browser.BrowserSettings.WhileLoopTimeoutMs)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"While loop timed out after [{_browser.BrowserSettings.WhileLoopTimeoutMs}]ms.", _browser, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "While loop completed.", _browser, GPALObjectType.Browser);

            _browser.BrowserSettings.Workflows.Clear();
            return _browser;
        }

        public IAllowBrowserActionOrAnySelector Until(Selector selector)
        {
            if (_browser.BrowserSettings.Workflows.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No workflows defined for Until(selector). Continuing without execution.", _browser, GPALObjectType.Browser);
                return _browser;
            }
            if (selector == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Selector is null for Until. Continuing without execution.", _browser, GPALObjectType.Browser);
                return _browser;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Starting Until(selector) loop with [{_browser.BrowserSettings.Workflows.Count}] workflow(s).", _browser, GPALObjectType.Browser);

            var startTime = DateTime.UtcNow;
            int iteration = 0;
            bool elementsFound = false;

            while (!elementsFound && (DateTime.UtcNow - startTime).TotalMilliseconds < _browser.BrowserSettings.WhileLoopTimeoutMs && iteration < _browser.BrowserSettings.WhileLoopMaxIterations)
            {
                _browser.WorkflowSetup(checkWaitFor: true, doMaintenance: true);
                foreach (var workflow in _browser.BrowserSettings.Workflows)
                    workflow(_browser);

                var foundElements = ElementHelper.FindWebElements(_browser, _browser.CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements, _browser.CurrentUOW.ShadowRoot);
                elementsFound = foundElements != null && foundElements.Count > 0;

                iteration++;
                Thread.Sleep(100);
            }

            if (iteration >= _browser.BrowserSettings.WhileLoopMaxIterations)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Until(selector) loop reached maximum iterations ([{_browser.BrowserSettings.WhileLoopMaxIterations}]).", _browser, GPALObjectType.Browser);
            else if ((DateTime.UtcNow - startTime).TotalMilliseconds >= _browser.BrowserSettings.WhileLoopTimeoutMs)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Until(selector) loop timed out after [{_browser.BrowserSettings.WhileLoopTimeoutMs}]ms.", _browser, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Until(selector) loop completed.", _browser, GPALObjectType.Browser);

            _browser.BrowserSettings.Workflows.Clear();
            return _browser;
        }

        public IAllowBrowserActionOrAnySelector Until(Func<Browser, bool> predicate)
        {
            if (_browser.BrowserSettings.Workflows.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No workflows defined for Until(predicate). Continuing without execution.", _browser, GPALObjectType.Browser);
                return _browser;
            }
            if (predicate == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Predicate is null for Until. Continuing without execution.", _browser, GPALObjectType.Browser);
                return _browser;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Starting Until(predicate) loop with [{_browser.BrowserSettings.Workflows.Count}] workflow(s).", _browser, GPALObjectType.Browser);

            var startTime = DateTime.UtcNow;
            int iteration = 0;

            while (!predicate(_browser) && (DateTime.UtcNow - startTime).TotalMilliseconds < _browser.BrowserSettings.WhileLoopTimeoutMs && iteration < _browser.BrowserSettings.WhileLoopMaxIterations)
            {
                _browser.WorkflowSetup(checkWaitFor: true, doMaintenance: true);
                foreach (var workflow in _browser.BrowserSettings.Workflows)
                    workflow(_browser);
                iteration++;
                Thread.Sleep(100);
            }

            if (iteration >= _browser.BrowserSettings.WhileLoopMaxIterations)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Until(predicate) loop reached maximum iterations ([{_browser.BrowserSettings.WhileLoopMaxIterations}]).", _browser, GPALObjectType.Browser);
            else if ((DateTime.UtcNow - startTime).TotalMilliseconds >= _browser.BrowserSettings.WhileLoopTimeoutMs)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Until(predicate) loop timed out after [{_browser.BrowserSettings.WhileLoopTimeoutMs}]ms.", _browser, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Until(predicate) loop completed.", _browser, GPALObjectType.Browser);

            _browser.BrowserSettings.Workflows.Clear();
            return _browser;
        }
        public void Run()
        {
            if (_browser.BrowserSettings.Workflows.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No workflows defined for Run. Continuing without execution.", _browser, GPALObjectType.Browser);
                return;
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Starting Run with [{_browser.BrowserSettings.Workflows.Count}] workflow(s).", _browser, GPALObjectType.Browser);

            var workflows = _browser.BrowserSettings.Workflows.ToList();
            _browser.BrowserSettings.Workflows.Clear();

            foreach (var workflow in workflows)
            {
                try
                {
                    _browser.WorkflowSetup(checkWaitFor: true, doMaintenance: true);
                    workflow(_browser);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Workflow failed", _browser, GPALObjectType.Browser, ex);
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Run completed with [{workflows.Count}] workflow(s).", _browser, GPALObjectType.Browser);
        }
    }
}
