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
    /// A GPAL button instantiated with GPAL.Button for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALButton.WindowsControl.Clicked event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
    ///     .Default
    ///     .WithName("button1")
    ///     .WithText("Exit")
    ///     .WithCallback&lt;EventHandler&gt;(ExitButtonClickHandler)
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);  // Alt-E
    ///
    /// static void Main(string[] args)
    /// {
    ///     GPALForm myForm = (GPALForm)GPAL.Form
    ///         .WithTitle("Simple Form Test")
    ///         .WithFormControl(gPalButton1)
    ///         .ShowDialog();
    /// }
    ///     
    /// private static void ExitButtonClickHandler(object sender, EventArgs e)
    /// {
    ///     Control control = (Control)sender;
    ///     
    ///     while (null != control.Parent)
    ///         control = control.Parent;
    ///     control.Hide();
    /// }
    /// </code>
    /// </example>
    public class GPALButton : GPALControl, IAllowControlSettingsAndDefault
    {
        /// <summary>
        /// Is this button the default Accept button for the form?<br/>
        /// <b>NOTE:</b> Do NOT define more than one default. Only the last one added to the form is the default.
        /// </summary>
        /// <returns>Your designation for the button, true or false.</returns>
        internal bool IsDefault { get; set; }
        internal GPALButton()
        {
            ControlType = FormControlType.Button;
            IsDefault = false;
        }
        /// <summary>
        /// Fluent interface to designate this button as the default button.<br/>
        /// <b>NOTE:</b> Do NOT define more than one default. Only the last one added to the form is the default.
        /// </summary>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndDefault Default
        {
            get
            {
                IsDefault = true;
                return this;
            }
        }
    }
}
