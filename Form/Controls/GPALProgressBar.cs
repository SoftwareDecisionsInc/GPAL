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
    /// A progress indicator control that shows completion percentage or indeterminate activity.
    /// Supports continuous bar, marquee animation and basic value tracking.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALProgressBar taskProgress = GPAL.ProgressBar
    ///     .WithName("DownloadProgress")
    ///     .WithMaximum(100)
    ///     .WithValue(42)
    ///     .WithStyle(ProgressBarStyle.Continuous)
    ///     .WithToolTip("Shows current task progress")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALProgressBar : GPALControl, IAllowProgressBarControlSettings
    {
        private int _value = 0;
        private int _minimum = 0;
        private int _maximum = 100;
        private ProgressBarStyle _style = ProgressBarStyle.Blocks;

        internal GPALProgressBar()
        {
            ControlType = FormControlType.ProgressBar;
        }

        /// <summary>
        /// Sets the initial progress value, clamped to <see cref="Minimum"/>/<see cref="Maximum"/>, and
        /// syncs it to the underlying control.
        /// </summary>
        /// <param name="value">The desired progress value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithValue(int value)
        {
            this.Value = value;
            return this;
        }

        /// <summary>
        /// Sets the minimum progress value. If <see cref="Value"/> is below this, it is raised to match.
        /// </summary>
        /// <param name="min">The desired minimum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithMinimum(int min)
        {
            _minimum = min;
            if (_value < min) _value = min;
            SyncValueToControl();
            return this;
        }

        /// <summary>
        /// Sets the maximum progress value. If <see cref="Value"/> exceeds this, it is lowered to match.
        /// </summary>
        /// <param name="max">The desired maximum value.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithMaximum(int max)
        {
            _maximum = max;
            if (_value > max) _value = max;
            SyncValueToControl();
            return this;
        }

        /// <summary>
        /// Sets the visual style of the progress bar (e.g. continuous, blocks, marquee).
        /// </summary>
        /// <param name="style">The desired progress bar style.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowProgressBarControlSettings WithStyle(ProgressBarStyle style)
        {
            this.Style = style;
            return this;
        }

        /// <summary>
        /// Runtime access to advance the progress value by <paramref name="step"/>, clamped to
        /// <see cref="Minimum"/>/<see cref="Maximum"/>.
        /// </summary>
        /// <param name="step">The amount to add to the current value. Defaults to 1.</param>
        public void Increment(int step = 1)
        {
            this.Value += step;
        }

        /// <summary>
        /// Applies <see cref="Value"/> to the underlying <see cref="ProgressBar"/>, marshalling to the UI
        /// thread via <see cref="Control.Invoke(Delegate)"/> if required. No-op if the underlying control
        /// is not a <see cref="ProgressBar"/>.
        /// </summary>
        private void SyncValueToControl()
        {
            if (WindowsControl is ProgressBar pb && true == Alive(pb))
            {
                OnUi(pb, () => Apply(pb));
            }
        }

        /// <summary>
        /// Sets the range before the value, so the control's old range does not reject it. An out-of-range Value
        /// throws, which a bar whose maximum grows would hit.
        /// </summary>
        /// <param name="pb">The control to update.</param>
        private void Apply(ProgressBar pb)
        {
            // Set Maximum first when increasing and Minimum first when decreasing; the control rejects a minimum above
            // its maximum.
            if (pb.Maximum < _maximum)
            {
                pb.Maximum = _maximum;
                pb.Minimum = _minimum;
            }
            else
            {
                pb.Minimum = _minimum;
                pb.Maximum = _maximum;
            }

            pb.Value = _value;
        }

        /// <summary>
        /// Applies <see cref="Style"/> to the underlying <see cref="ProgressBar"/>, marshalling to the UI
        /// thread via <see cref="Control.Invoke(Delegate)"/> if required. No-op if the underlying control
        /// is not a <see cref="ProgressBar"/>.
        /// </summary>
        private void SyncStyleToControl()
        {
            if (WindowsControl is ProgressBar pb && true == Alive(pb))
            {
                OnUi(pb, () => pb.Style = _style);
            }
        }

        /// <summary>
        /// The current progress value, clamped to <see cref="Minimum"/>/<see cref="Maximum"/>. Setting this
        /// writes through to the live control (if realized), marshalling to the UI thread via
        /// <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public int Value
        {
            get => _value;
            set
            {
                _value = Math.Max(_minimum, Math.Min(_maximum, value));
                SyncValueToControl();
            }
        }
        /// <summary>
        /// The minimum progress value, as set via <see cref="WithMinimum(int)"/>.
        /// </summary>
        public int Minimum => _minimum;
        /// <summary>
        /// The maximum progress value, as set via <see cref="WithMaximum(int)"/>.
        /// </summary>
        public int Maximum => _maximum;
        /// <summary>
        /// The current visual style. Setting this writes through to the live control (if realized),
        /// marshalling to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public ProgressBarStyle Style
        {
            get => _style;
            set
            {
                _style = value;
                SyncStyleToControl();
            }
        }
    }
}
