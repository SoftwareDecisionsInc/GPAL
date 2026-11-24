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
    /// A GPAL tab instantiated with GPAL.Tab for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALTab.WindowsControl.Clicked event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALTab gPalTab1 = (GPALTab)GPAL.Tab
    ///     .WithName("Tab1")
    ///     .WithText("Tab 1")
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.D1); // Alt-1
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithTab(gPalTab1)  // items after this are placed on this tab
    ///     .WithFormControl(gPalRadioButton1)
    ///     .WithFormControl(gPalRadioButton2)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALTab : GPALControl, IAllowControlSettings
    {
        internal GPALTab()
        {
            ControlType = FormControlType.Tab;
        }
        public override bool Enabled
        {
            get
            {
                // A GPALTab's control is its TabPage; the TabControl would answer for every tab.
                bool retVal = IsEnabled;

                if (WindowsControl is TabPage page && true == Alive(page))
                    retVal = OnUiRead(page, () => page.Enabled, retVal);

                return retVal;
            }
            set
            {
                IsEnabled = value;

                if (!(WindowsControl is TabPage page) || false == Alive(page)) return;

                OnUi(page, () => page.Enabled = value);
            }
        }
        /// <summary>
        /// Activate (switch to) this tab at runtime, bringing its controls into view.
        /// </summary>
        public void Activate()
        {
            // Marshalled like the other setters; this is called from a workflow thread.
            if (WindowsControl is TabPage tabPage && tabPage.Parent is TabControl tabControl)
                OnUi(tabControl, () => tabControl.SelectedTab = tabPage);
        }
    }
}
