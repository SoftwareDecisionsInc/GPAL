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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.GPALForm
{
    /// <summary>
    /// A titled box around a set of controls, instantiated with GPAL.GroupBox and laid out like any other control.
    /// <example>
    /// <code>
    /// GPAL.GroupBox.WithText("Options")
    ///     .WithFormControl(GPAL.LabelFor("Retry Count:"))
    ///     .WithFormControl(gPALNumeric);
    /// </code>
    /// </example>
    /// </summary>
    public class GPALGroupBox : GPALControl, IAllowGroupBoxControlSettings
    {
        /// <summary>Everything inside the box, in order.</summary>
        internal List<object> FormControls { get; } = new List<object>();

        internal GPALGroupBox() { }

        /// <summary>
        /// The caption along the top of the box.
        /// </summary>
        /// <param name="text">Caption to display</param>
        /// <returns>Fluent interface to add the controls inside</returns>
        public new IAllowGroupBoxControlSettings WithText(string text)
        {
            Text = text;
            return this;
        }
        /// <summary>
        /// Add a control inside the box. A container can go in here too, so a group box can hold a splitter.
        /// </summary>
        /// <param name="gPALFormControl">A control or another container</param>
        /// <returns>Fluent interface to add more controls</returns>
        public IAllowGroupBoxControlSettings WithFormControl(object gPALFormControl)
        {
            FormControls.Add(gPALFormControl);
            return this;
        }
        /// <summary>
        /// Returns this group box so it can be handed to a form or to another container.
        /// </summary>
        /// <returns>This group box</returns>
        public new GPALGroupBox ToGPALObject()
        {
            return this;
        }
    }
}
