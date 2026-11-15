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
    /// A GPAL lable instantiated with GPAL.Label for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALLabel.WindowsControl.Clicked event.<br/>
    /// Labels autosize to fit the form.
    /// </summary>
    /// <example>
    /// <code>
    /// Font font = new Font("Times New Roman", 12.0f);
    /// 
    /// static GPALLabel gPalLabel = (GPALLabel)GPAL.Label
    ///     .WithText("Form Tester")
    ///     .WithFont(font)
    ///     .WithTextAlignment(ContentAlignment.TopCenter);
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalLabel)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALLabel : GPALControl, IAllowControlSettingsAndFontSettings
    {
        internal GPALLabel()
        {
            ControlType = FormControlType.Label;
        }
    }
}
