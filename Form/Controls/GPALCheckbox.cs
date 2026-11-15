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
    /// A GPAL checkbox instantiateds with GPAL,Checkbox for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALCheckbox.WindowsControl.CheckChanged event.<br/>
    /// Default is not checked
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALCheckbox gPALCheckbox1 = (GPALCheckbox)GPAL.Checkbox
    ///     .WithChecked(true)
    ///     .WithText("Overwrite File")
    ///     .WithCallback&lt;EventHandler&gt;(CheckboxHandler); // on check changed event handler
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPALCheckbox1)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALCheckbox : GPALControl, IAllowControlSettingsAndChecked
    {
        internal GPALCheckbox()
        {
            ControlType = FormControlType.Checkbox;
        }
        /// <summary>
        /// Sets whether the checkbox is checked.
        /// </summary>
        /// <param name="isChecked">True<br/>False [default]</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndChecked WithChecked(bool isChecked)
        {
            this.Checked = isChecked;
            return this;
        }
        /// <summary>
        /// Runtime access to un/check the checkbox. Setting this writes through to the live control
        /// (if realized), marshalling to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        /// <returns>True [checked]<br/>False [default - unchecked]</returns>
        public bool Checked
        {
            get
            {
                bool retVal = IsChecked;

                if (WindowsControl is CheckBox cb && true == Alive(cb))
                    retVal = OnUiRead(cb, () => cb.Checked, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is CheckBox cb && true == Alive(cb))
                {
                    OnUi(cb, () => cb.Checked = value);
                }
                else
                {
                    IsChecked = value;
                }
            }
        }
    }
}
