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
    /// A GPAL single line input instantiated with GPAL.Input for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALInput.WindowsControl.TextChanged event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALInput gPalInput = (GPALInput)GPAL.Input
    ///     .WithText(@"c:\drivers")
    ///     .WithCallback&lt;EventHandler&gt;(InputHandler); // on text changed event handler
    ///     
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalInput)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALInput : GPALControl, IAllowControlSettingsPlaceholderAndPassword
    {
        internal bool InputIsPassword { get; set; } = false;
        internal string PlaceHolder { get; set; }
        internal GPALInput()
        {
            ControlType = FormControlType.Input;
        }
        /// <summary>
        /// The input a password or other sensitive information.<br/>
        /// The input will be masked with a bullet character.
        /// CAVEAT: the getter sets the input to a password, so a Visual Studio hover over it does too, without stopping at a breakpoint here
        /// </summary>
        /// <returns>Fluent interface to define more settings</returns>
        /// <summary>
        /// Runtime access to show or hide what a password input holds, for a "show password" button.
        /// </summary>
        /// <returns>True - the text is readable<br/>False [default for a password input] - the text is bulleted</returns>
        public bool ShowPassword
        {
            get
            {
                return false == InputIsPassword;
            }
            set
            {
                InputIsPassword = false == value;

                if (WindowsControl is PlaceholderTextBox ptbInput && true == Alive(ptbInput))
                {
                    char passwordChar = true == value ? '\0' : '●';  // bullet

                    OnUi(ptbInput, () => ptbInput.PasswordChar = passwordChar);
                }
            }
        }

        public IAllowControlSettingsPlaceholderAndPassword IsPassword
        {
            get
            {
                InputIsPassword = true;
                if (true == String.IsNullOrEmpty(PlaceHolder))
                    PlaceHolder = "Enter Password";
                return this;
            }
        }
        public IAllowControlSettingsPlaceholderAndPassword WithPlaceholder(string PlaceholderText)
        {
            PlaceHolder = PlaceholderText;
            if (WindowsControl is PlaceholderTextBox tb && true == Alive(tb))
                tb.PlaceholderText = PlaceholderText;
            return this;
        }

        public override IAllowControlSettings WithReadOnly(bool readOnly = true)
        {
            IsReadOnly = readOnly;
            if (WindowsControl is PlaceholderTextBox tb && true == Alive(tb))
                tb.ReadOnly = readOnly;
            return this;
        }

    }
}
