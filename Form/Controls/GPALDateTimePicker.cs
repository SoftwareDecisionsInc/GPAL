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
    /// A date/time picker control instantiated with GPAL.DateTimePicker for use on GPAL forms.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALDateTimePicker gPalDate = (GPALDateTimePicker)GPAL.DateTimePicker
    ///     .WithFormat(DateTimePickerFormat.Short)
    ///     .WithValue(DateTime.Today)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALDateTimePicker : GPALControl, IAllowDateTimePickerControlSettings
    {
        private DateTime _value = DateTime.Now;
        private DateTimePickerFormat _format = DateTimePickerFormat.Long;
        private string _customFormat = "";

        internal GPALDateTimePicker()
        {
            ControlType = FormControlType.DateTimePicker;
        }

        /// <summary>
        /// Sets the initial date/time value. Defaults to the current date/time.
        /// </summary>
        /// <param name="value">The desired date/time.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDateTimePickerControlSettings WithValue(DateTime value)
        {
            _value = value;
            return this;
        }

        /// <summary>
        /// Sets the display format. Defaults to <see cref="DateTimePickerFormat.Long"/>. Use
        /// <see cref="DateTimePickerFormat.Custom"/> with <see cref="WithCustomFormat(string)"/> to
        /// specify a custom format string.
        /// </summary>
        /// <param name="format">The desired display format.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDateTimePickerControlSettings WithFormat(DateTimePickerFormat format)
        {
            _format = format;
            return this;
        }

        /// <summary>
        /// Sets a custom display format string, used when <see cref="Format"/> is <see cref="DateTimePickerFormat.Custom"/>.
        /// </summary>
        /// <param name="formatString">The custom .NET date/time format string.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDateTimePickerControlSettings WithCustomFormat(string formatString)
        {
            _customFormat = formatString;
            return this;
        }

        /// <summary>
        /// Runtime access to the date/time. Reads the live control once the form is up, so the date the user
        /// picked is the date a workflow gets, and falls back to <see cref="WithValue(DateTime)"/> before
        /// then. Setting writes through to the live control, marshalling to the UI thread when required.
        /// </summary>
        public DateTime Value
        {
            get
            {
                DateTime retVal = _value;

                if (WindowsControl is DateTimePicker dtp && true == Alive(dtp))
                    retVal = OnUiRead(dtp, () => dtp.Value, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is DateTimePicker dtp && true == Alive(dtp))
                {
                    OnUi(dtp, () => dtp.Value = value);
                }
                else
                {
                    _value = value;
                }
            }
        }
        /// <summary>
        /// The display format, as set via <see cref="WithFormat(DateTimePickerFormat)"/>.
        /// </summary>
        public DateTimePickerFormat Format => _format;
        /// <summary>
        /// The custom display format string, as set via <see cref="WithCustomFormat(string)"/>.
        /// </summary>
        public string CustomFormat => _customFormat;
    }
}
