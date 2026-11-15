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
    /// Empty space of a set height between two controls on a GPAL form, instantiated with GPAL.SpacerFor.
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Form
    ///     .WithFormControl(moreBotsLink)
    ///     .WithFormControl(GPAL.SpacerFor(12))
    ///     .WithFormControl(okButton)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALSpacer : GPALControl
    {
        internal GPALSpacer()
        {
            ControlType = FormControlType.Spacer;
        }
    }
}
