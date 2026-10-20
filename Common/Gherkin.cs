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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GenerallyPositive.Application;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Gherkin-style (Given/When/And/Then) wrapper for writing GPAL workflows as readable behavior-driven scenarios, backed by a Browser or Application instance.
    /// </summary>
    internal class Gherkin : IAllowGiven, IAllowWhen, IAllowAnd, IAllowThen
    {
        private IBrowser _browser;
        private IApplication _application;

        /// <summary>
        /// Creates an empty Gherkin scenario. Call ForBrowser or ForApplication to attach the automation target before Given/When/And/Then.
        /// </summary>
        public Gherkin()
        {
        }

        /// <summary>
        /// Attaches a Browser instance to this scenario, created with the given browser type and WebDriver location.
        /// </summary>
        /// <param name="browserType">Browser to launch (Chrome, Edge, Firefox).</param>
        /// <param name="driverLocation">Directory containing the WebDriver executable for the chosen browser.</param>
        /// <returns>Fluent interface to continue building the Gherkin scenario.</returns>
        public Gherkin ForBrowser(BrowserType browserType, string driverLocation)
        {
            _browser = GPAL.Browser.WithBrowserType(browserType).WithDriverLocation(driverLocation).ToGPALObject(); ;
            return this;
        }

        /// <summary>
        /// Attaches a desktop Application instance to this scenario, launching the executable at the given path.
        /// </summary>
        /// <param name="appPath">Path to the application executable to launch.</param>
        /// <returns>Fluent interface to continue building the Gherkin scenario.</returns>
        public Gherkin ForApplication(string appPath)
        {
            _application = GPAL.Application.Open(appPath).ToGPALObject();
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Starting application: [{appPath}]", null, GPALObjectType.Application);
            return this;
        }

        /// <summary>
        /// Runs the "Given" precondition step immediately.
        /// </summary>
        /// <param name="step">Action representing the scenario's precondition.</param>
        /// <returns>Fluent interface to continue with When.</returns>
        public IAllowWhen Given(Action step)
        {
            step();
            return this;
        }

        /// <inheritdoc cref="Given(Action)"/>
        public IAllowWhen Given<T1>(Action<T1> step, T1 arg1)
        {
            step(arg1);
            return this;
        }

        /// <inheritdoc cref="Given(Action)"/>
        public IAllowWhen Given<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2)
        {
            step(arg1, arg2);
            return this;
        }

        /// <inheritdoc cref="Given(Action)"/>
        public IAllowWhen Given<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3)
        {
            step(arg1, arg2, arg3);
            return this;
        }

        /// <summary>
        /// Runs the "When" action step immediately.
        /// </summary>
        /// <param name="step">Action representing the scenario's action.</param>
        /// <returns>Fluent interface to continue with And.</returns>
        public IAllowAnd When(Action step)
        {
            step();
            return this;
        }

        /// <inheritdoc cref="When(Action)"/>
        public IAllowAnd When<T1>(Action<T1> step, T1 arg1)
        {
            step(arg1);
            return this;
        }

        /// <inheritdoc cref="When(Action)"/>
        public IAllowAnd When<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2)
        {
            step(arg1, arg2);
            return this;
        }

        /// <inheritdoc cref="When(Action)"/>
        public IAllowAnd When<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3)
        {
            step(arg1, arg2, arg3);
            return this;
        }

        /// <summary>
        /// Runs an additional "And" step immediately.
        /// </summary>
        /// <param name="step">Action representing the additional step.</param>
        /// <returns>Fluent interface to continue with Then or another And.</returns>
        public IAllowThen And(Action step)
        {
            step();
            return this;
        }

        /// <inheritdoc cref="And(Action)"/>
        public IAllowThen And<T1>(Action<T1> step, T1 arg1)
        {
            step(arg1);
            return this;
        }

        /// <inheritdoc cref="And(Action)"/>
        public IAllowThen And<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2)
        {
            step(arg1, arg2);
            return this;
        }

        /// <inheritdoc cref="And(Action)"/>
        public IAllowThen And<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3)
        {
            step(arg1, arg2, arg3);
            return this;
        }

        /// <summary>
        /// Runs the "Then" assertion/outcome step immediately.
        /// </summary>
        /// <param name="step">Action representing the scenario's expected outcome.</param>
        /// <returns>Fluent interface to continue with another Then.</returns>
        public IAllowThen Then(Action step)
        {
            step();
            return this;
        }

        /// <inheritdoc cref="Then(Action)"/>
        public IAllowThen Then<T1>(Action<T1> step, T1 arg1)
        {
            step(arg1);
            return this;
        }

        /// <inheritdoc cref="Then(Action)"/>
        public IAllowThen Then<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2)
        {
            step(arg1, arg2);
            return this;
        }

        /// <inheritdoc cref="Then(Action)"/>
        public IAllowThen Then<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3)
        {
            step(arg1, arg2, arg3);
            return this;
        }

        /// <summary>
        /// The Browser instance attached via ForBrowser, for use directly within step delegates.
        /// </summary>
        // Optional: Public Browser access for steps
        public IBrowser Browser => _browser;
    }
}

