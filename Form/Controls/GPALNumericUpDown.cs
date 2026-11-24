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
    /// A numeric up/down spinner control instantiated with GPAL.NumericUpDown for use on GPAL forms.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALNumericUpDown gPalQuantity = (GPALNumericUpDown)GPAL.NumericUpDown
    ///     .WithMinimum(0)
    ///     .WithMaximum(10)
    ///     .WithIncrement(1)
    ///     .WithValue(1)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALNumericUpDown : GPALControl, IAllowNumericUpDownControlSettings
    {
        private decimal _value = 0m;
        private decimal _minimum = 0m;
        private decimal _maximum = 100m;
        private decimal _increment = 1m;
        private int _decimalPlaces = 0;

        internal GPALNumericUpDown()
        {
            ControlType = FormControlType.NumericUpDown;
        }

        /// <summary>
        /// Sets the initial value.
        /// </summary>
        /// <param name="value">The desired value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithValue(decimal value)
        {
            _value = value;
            return this;
        }

        /// <summary>
        /// Sets the minimum allowed value. Defaults to 0.
        /// </summary>
        /// <param name="min">The desired minimum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithMinimum(decimal min)
        {
            _minimum = min;
            return this;
        }

        /// <summary>
        /// Sets the maximum allowed value. Defaults to 100.
        /// </summary>
        /// <param name="max">The desired maximum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithMaximum(decimal max)
        {
            _maximum = max;
            return this;
        }

        /// <summary>
        /// Sets the amount the value changes when the up/down spinner is clicked. Defaults to 1.
        /// </summary>
        /// <param name="inc">The desired increment.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithIncrement(decimal inc)
        {
            _increment = inc;
            return this;
        }

        /// <summary>
        /// Sets how many decimal places are displayed. Defaults to 0.
        /// </summary>
        /// <param name="places">The number of decimal places to display.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowNumericUpDownControlSettings WithDecimalPlaces(int places)
        {
            _decimalPlaces = places;
            return this;
        }

        /// <summary>
        /// Runtime access to the value. Reads the live control once the form is up, so a value the user
        /// spun to is the value a workflow gets, and falls back to <see cref="WithValue(decimal)"/> before
        /// then. Setting writes through to the live control, marshalling to the UI thread when required.
        /// </summary>
        public decimal Value
        {
            get
            {
                decimal retVal = _value;

                if (WindowsControl is NumericUpDown nud && true == Alive(nud))
                    retVal = OnUiRead(nud, () => nud.Value, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is NumericUpDown nud && true == Alive(nud))
                {
                    OnUi(nud, () => nud.Value = value);
                }
                else
                {
                    _value = value;
                }
            }
        }
        /// <summary>
        /// The minimum allowed value, as set via <see cref="WithMinimum(decimal)"/>.
        /// </summary>
        public decimal Minimum => _minimum;
        /// <summary>
        /// The maximum allowed value, as set via <see cref="WithMaximum(decimal)"/>.
        /// </summary>
        public decimal Maximum => _maximum;
        /// <summary>
        /// The spinner increment, as set via <see cref="WithIncrement(decimal)"/>.
        /// </summary>
        public decimal Increment => _increment;
        /// <summary>
        /// The number of decimal places displayed, as set via <see cref="WithDecimalPlaces(int)"/>.
        /// </summary>
        public int DecimalPlaces => _decimalPlaces;
    }
}
