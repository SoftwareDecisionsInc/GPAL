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

namespace GenerallyPositive
{
    /// <summary>
    /// Thrown where GPAL deliberately ends the workflow, such as a CallIf handler returning
    /// CallIfStatus.Terminate or a driver that could not be started.<br/>
    /// The reason has always been published before this is thrown, so the uncaught handler reports the
    /// termination without repeating the message or printing a stack trace.
    /// </summary>
    public class GPALException : Exception
    {
        /// <summary>
        /// Creates a GPALException with the reason the workflow is ending.
        /// </summary>
        /// <param name="message">Reason for terminating, already published to the event handlers</param>
        public GPALException(string message) : base(message)
        {
        }
    }
}
