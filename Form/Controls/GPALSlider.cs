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
    /// A GPAL slider instantiated with GPAL.Slider, for picking a number from a range by dragging.<br/>
    /// Callback EventHandler is invoked on the underlying TrackBar's ValueChanged event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALSlider volume = (GPALSlider)GPAL.Slider
    ///     .WithMinimum(0)
    ///     .WithMaximum(100)
    ///     .WithValue(50)
    ///     .WithTickFrequency(10)
    ///     .WithName("volume")
    ///     .ToGPALObject();
    ///
    /// // and later, from anywhere
    /// volume.Value = 75;
    /// </code>
    /// </example>
    public class GPALSlider : GPALControl, IAllowSliderControlSettings
    {
        private int _value = 0;
        private int _minimum = 0;
        private int _maximum = 100;
        private int _tickFrequency = 10;
        private Orientation _orientation = Orientation.Horizontal;

        internal GPALSlider()
        {
            ControlType = FormControlType.Slider;
        }

        /// <summary>
        /// Where the slider sits, clamped to the range it was given.
        /// </summary>
        /// <returns>The current value</returns>
        public int Value
        {
            get => OnUiRead(WindowsControl as TrackBar, () => ((TrackBar)WindowsControl).Value, _value);

            set
            {
                _value = value < _minimum ? _minimum : value > _maximum ? _maximum : value;

                SyncToControl();
            }
        }

        /// <summary>The lowest value the slider can be dragged to.</summary>
        public int Minimum => _minimum;

        /// <summary>The highest value the slider can be dragged to.</summary>
        public int Maximum => _maximum;

        /// <summary>How far apart the tick marks are.</summary>
        public int TickFrequency => _tickFrequency;

        /// <summary>Horizontal or vertical.</summary>
        public Orientation Orientation => _orientation;

        /// <summary>
        /// The lowest value the slider can be dragged to.
        /// </summary>
        /// <param name="min">The minimum. A value below it is raised to meet it</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithMinimum(int min)
        {
            _minimum = min;

            if (_value < min)
                _value = min;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// The highest value the slider can be dragged to.
        /// </summary>
        /// <param name="max">The maximum. A value above it is lowered to meet it</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithMaximum(int max)
        {
            _maximum = max;

            if (_value > max)
                _value = max;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// Where the slider starts.
        /// </summary>
        /// <param name="value">The value, clamped to the range</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithValue(int value)
        {
            Value = value;
            return this;
        }

        /// <summary>
        /// How far apart the tick marks are.
        /// </summary>
        /// <param name="tickFrequency">Ticks every this many units</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithTickFrequency(int tickFrequency)
        {
            _tickFrequency = tickFrequency;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// Horizontal or vertical.
        /// </summary>
        /// <param name="orientation">Horizontal by default</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowSliderControlSettings WithOrientation(Orientation orientation)
        {
            _orientation = orientation;

            SyncToControl();
            return this;
        }

        /// <summary>
        /// Set the range before the value, so the control's old range does not reject it.
        /// </summary>
        private void SyncToControl()
        {
            if (WindowsControl is TrackBar bar && true == Alive(bar))
                OnUi(bar, () =>
                {
                    bar.Minimum = _minimum;
                    bar.Maximum = _maximum;
                    bar.TickFrequency = 1 > _tickFrequency ? 1 : _tickFrequency;
                    bar.Orientation = _orientation;
                    bar.Value = _value;
                });
        }
    }
}
