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
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GenerallyPositive.GPALForm
{
    internal class FormSettings
    {
        internal TableLayoutPanel TableLayoutPanel { get; set; }
        internal TabControl TabControl { get; set; } = null;
        internal TabPage CurrentTabPage { get; set; }

        internal int width { get; set; } = 300;
        internal int height { get; set; } = 0;     // not set: the form fits its content
        internal int top { get; set; } = 300;
        internal int left { get; set; } = 640;

        // Default width for this form's buttons. 0 [default] stretches them across the form.
        internal int buttonWidth { get; set; } = 0;                                                                                                                                                                                                                                             

        internal GPALFormForm myForm { get; set; } = null;
        internal System.Windows.Forms.ToolTip ToolTip { get; } = new System.Windows.Forms.ToolTip
        {
            AutoPopDelay = 5000,
            InitialDelay = 500,
            ShowAlways = true
        };
    }
}

