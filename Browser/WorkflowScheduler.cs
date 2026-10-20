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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// The result of one <see cref="WorkflowScheduler"/> workflow: finished or not, duration, and the exception if
    /// it threw. In the order added; Index is the position.
    /// </summary>
    public class WorkflowRun
    {
        /// <summary>Where this workflow sat in the order they were added, counting from zero.</summary>
        public int Index { get; internal set; }
        /// <summary>True when the workflow ran to the end without throwing.</summary>
        public bool Succeeded { get; internal set; }
        /// <summary>How long it ran for, whether it finished or threw.</summary>
        public TimeSpan Duration { get; internal set; }
        /// <summary>What it threw, or null when it did not.</summary>
        public Exception Failure { get; internal set; }
    }

    /// <summary>
    /// Runs whole workflows in parallel, each creating and owning what it drives. The scheduler only starts them,
    /// limits how many run at once, spaces their starts and waits. It knows nothing of browsers, so one workflow
    /// can run Chrome on OttoMagic and another Firefox on Selenium.
    /// <br/><br/>
    /// For several steps against one browser you already have, use
    /// <see cref="Browser.WithWorkflow(Action{IBrowser})"/> and <see cref="Browser.Run"/> instead.
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Workflow
    ///     .WithWorkflow(() =&gt;
    ///     {
    ///         IBrowser b = GPAL.Browser
    ///             .WithBrowserType(BrowserType.Chrome)
    ///             .WithAutomationEngine(AutomationEngine.OttoMagic)
    ///             .GoTo("https://www.ebay.com")
    ///             .ToGPALObject();
    ///
    ///         b.WithSelector(GPAL.CssSelector("#gh-ac")).FillInFrom("laptop").SendKey(GPAL.VK_ENTER);
    ///         b.Close(true);
    ///     })
    ///     .WithWorkflow(() =&gt;
    ///     {
    ///         // a different browser and a different engine, because nothing is copied from anywhere
    ///         IBrowser b = GPAL.Browser
    ///             .WithBrowserType(BrowserType.FireFox)
    ///             .WithAutomationEngine(AutomationEngine.Selenium)
    ///             .GoTo("https://www.walmart.com")
    ///             .ToGPALObject();
    ///
    ///         b.Close(true);
    ///     })
    ///     .WithMaxAtOnce(2)
    ///     .Run(out List&lt;WorkflowRun&gt; runs);   // two at a time, and what became of each
    /// </code>
    /// </example>
    public class WorkflowScheduler : IAllowScheduledWorkflow, IAllowScheduledWorkflowExecution
    {
        private readonly List<Action> workflows = new List<Action>();
        private int maxAtOnce = 1;
        private int staggerMs = 250;
        private int timeoutMs = Timeout.Infinite;

        internal WorkflowScheduler()
        {
        }

        /// <summary>
        /// Maximum workflows running at once. The default, 1, runs them in sequence. A throttle for sites that object
        /// to several browsers at once.
        /// </summary>
        /// <param name="howMany">The most workflows to have running at any moment.</param>
        /// <returns>Fluent interface to add more workflows or run them.</returns>
        public IAllowScheduledWorkflowExecution WithMaxAtOnce(int howMany)
        {
            if (1 > howMany)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot run [{howMany}] at once, so one at a time it is", this, GPALObjectType.Browser);
                howMany = 1;
            }

            maxAtOnce = howMany;

            return this;
        }

        /// <summary>
        /// Minimum time between workflow starts. Workflows that create browsers find a free port and start a process
        /// first, and a dozen at once can all get the same answer. Default 250 ms. Use 0 when the workflows drive
        /// nothing external.
        /// </summary>
        /// <param name="delayInMs">The least time between two workflows starting.</param>
        /// <returns>Fluent interface to add more workflows or run them.</returns>
        public IAllowScheduledWorkflowExecution WithStaggerStart(int delayInMs)
        {
            if (0 > delayInMs)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot stagger by [{delayInMs}] ms, so they start together", this, GPALObjectType.Browser);
                delayInMs = 0;
            }

            staggerMs = delayInMs;

            return this;
        }

        /// <summary>
        /// How long Run waits for all workflows. Without it, a workflow that never returns blocks Run forever, and a
        /// form that called Run on its UI thread freezes.
        /// <br/><br/>
        /// Giving up does not stop a workflow; an abandoned one keeps running with its browser open. Run just
        /// returns and reports which were still running.
        /// </summary>
        /// <param name="timeoutInMs">How long to wait for them all. Zero or less waits forever, which is the default.</param>
        /// <returns>Fluent interface to add more workflows or run them.</returns>
        public IAllowScheduledWorkflowExecution WithTimeout(int timeoutInMs)
        {
            timeoutMs = 0 < timeoutInMs ? timeoutInMs : Timeout.Infinite;

            return this;
        }

        /// <summary>
        /// Adds a workflow. It takes no parameters, creates what it drives and closes what it creates.
        /// </summary>
        /// <param name="workflow">The workflow to run.</param>
        /// <returns>Fluent interface to add more workflows or run them.</returns>
        public IAllowScheduledWorkflowExecution WithWorkflow(Action workflow)
        {
            if (null == workflow)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Workflow is null in WithWorkflow, so an empty one was added instead", this, GPALObjectType.Browser);

            workflows.Add(workflow ?? (() => { }));

            return this;
        }

        /// <summary>
        /// Adds several workflows in order, as WithWorkflow does for each.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.Workflow
        ///         .WithWorkflows(urls.Select(url => new Action(() => ScrapeOne(url))))
        ///         .WithMaxAtOnce(4)
        ///         .Run(out List&lt;WorkflowRun&gt; runs);
        /// </code>
        /// </example>
        /// <param name="workflowsToAdd">The workflows to run.</param>
        /// <returns>Fluent interface to add more workflows or run them.</returns>
        public IAllowScheduledWorkflowExecution WithWorkflows(IEnumerable<Action> workflowsToAdd)
        {
            if (null == workflowsToAdd)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The list given to WithWorkflows is null, so no workflows were added", this, GPALObjectType.Browser);
            else
                foreach (Action workflow in workflowsToAdd)
                    WithWorkflow(workflow);

            return this;
        }

        /// <summary>
        /// Runs every added workflow and returns when all finish. One that throws is published and the rest keep
        /// running.
        /// </summary>
        /// <returns>Fluent interface to add more workflows or run them again.</returns>
        public IAllowScheduledWorkflowExecution Run()
        {
            return Run(out List<WorkflowRun> ignored);
        }

        /// <summary>
        /// Runs every added workflow and returns each result: finished or not, duration, and the exception if it
        /// threw, so the workflow can act on them, for example emailing about the failures.
        /// </summary>
        /// <param name="runs">Receives one entry per workflow, in the order they were added.</param>
        /// <returns>Fluent interface to add more workflows or run them again.</returns>
        public IAllowScheduledWorkflowExecution Run(out List<WorkflowRun> runs)
        {
            List<Action> running = workflows.ToList();
            workflows.Clear();

            WorkflowRun[] report = running.Select((w, index) => new WorkflowRun { Index = index }).ToArray();

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Running [{running.Count}] workflow(s), [{maxAtOnce}] at a time, [{staggerMs}] ms apart.", this, GPALObjectType.Browser);

            // When the latest workflow started, so the next waits until staggerMs after it.
            object startLine = new object();
            DateTime lastStart = DateTime.MinValue;

            // Not in a using block. An abandoned workflow still holds a slot and will call Release, which throws on
            // a disposed semaphore on a thread nobody watches. Disposed below once all have finished.
            SemaphoreSlim gate = new SemaphoreSlim(maxAtOnce, maxAtOnce);

            // Each workflow gets its own thread (LongRunning). A workflow blocks its thread throughout, and browser
            // connections answer on pool threads; the pool starts with one thread per core, so more workflows than
            // cores on the pool would starve them.
            Task[] started = running.Select((workflow, index) => Task.Factory.StartNew(() =>
            {
                gate.Wait();

                // Stagger here, after the slot is acquired, not in the task loop, so a workflow that waited for a
                // slot is still spaced from the one that started before it.
                if (0 < staggerMs)
                {
                    lock (startLine)
                    {
                        TimeSpan gap = TimeSpan.FromMilliseconds(staggerMs);
                        TimeSpan since = DateTime.Now - lastStart;

                        // lastStart starts at DateTime.MinValue, so the first workflow never waits.
                        if (since < gap)
                            Thread.Sleep(gap - since);

                        lastStart = DateTime.Now;
                    }
                }

                // each task writes only its own entry, so the report needs no gate of its own
                WorkflowRun mine = report[index];
                DateTime began = DateTime.Now;

                try
                {
                    workflow();
                    mine.Succeeded = true;
                }
                catch (Exception ex)
                {
                    mine.Failure = ex;

                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Workflow [{index + 1}] failed", this, GPALObjectType.Browser, ex);
                }
                finally
                {
                    mine.Duration = DateTime.Now - began;
                    gate.Release();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();

            bool allFinished = Task.WaitAll(started, timeoutMs);

            // Return copies, so the list shows the state when Run gave up; still-running workflows keep updating the
            // originals.
            runs = report.Select(run => new WorkflowRun
            {
                Index = run.Index,
                Succeeded = run.Succeeded,
                Duration = run.Duration,
                Failure = run.Failure ?? (true == run.Succeeded ? null : new TimeoutException($"Workflow [{run.Index + 1}] was still running after [{timeoutMs}] ms")),
            }).ToList();

            if (true == allFinished)
            {
                gate.Dispose();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"All [{running.Count}] workflow(s) finished, [{runs.Count(r => false == r.Succeeded)}] of them failed.", this, GPALObjectType.Browser);
            }
            else
                // The semaphore is not disposed on purpose: still-running workflows will call Release, which would
                // throw on an unwatched thread. It is garbage collected after they finish.
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Gave up waiting after [{timeoutMs}] ms with [{runs.Count(r => false == r.Succeeded)}] of [{running.Count}] workflow(s) still running. They keep going and keep their browsers.",
                    this, GPALObjectType.Browser);

            return this;
        }
    }
}
