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
    /// A GPAL radiobutton instantiated with GPAL.RadioButton for use GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALRadioButton.WindowsControl.CheckChanged event.<br/>
    /// Default is not selected
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALRadioButton gPalRadioButton1 = (GPALRadioButton)GPAL.RadioButton
    ///     .WithChecked(true)
    ///     .WithName("Radiobutton1")
    ///     .WithText("Chrome")
    ///     .WithCallback&lt;EventHandler&gt;(RadioButtonHandler); // on check changed event handler
    ///     
    /// static GPALRadioButton gPalRadioButton2 = (GPALRadioButton)GPAL.RadioButton
    ///     .WithName("Radiobutton2")
    ///     .WithText("Edge")
    ///     .WithCallback&lt;EventHandler&gt;(RadioButtonHandler); // on check changed event handler
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalRadioButton1)
    ///     .WithFormControl(gPalRadioButton2)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALRadioButton : GPALControl, IAllowControlSettingsAndChecked
    {
        internal GPALRadioButton()
        {
            ControlType = FormControlType.RadioButton;
        }
        /// <summary>
        /// Sets whether the radiobutton is checked.
        /// </summary>
        /// <param name="isChecked">True<br/>False [default]</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndChecked WithChecked(bool isChecked)
        {
            this.Checked = isChecked;
            return this;
        }
        /// <summary>
        /// Runtime access to un/check the radiobutton. Setting this writes through to the live control
        /// (if realized), marshalling to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        /// <returns>True [checked]<br/>False [default] - unchecked</returns>
        public bool Checked
        {
            get
            {
                bool retVal = IsChecked;

                if (WindowsControl is RadioButton rb && true == Alive(rb))
                    retVal = OnUiRead(rb, () => rb.Checked, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is RadioButton rb && true == Alive(rb))
                {
                    OnUi(rb, () => rb.Checked = value);
                }
                else
                {
                    IsChecked = value;
                }
            }
        }
    }
}
