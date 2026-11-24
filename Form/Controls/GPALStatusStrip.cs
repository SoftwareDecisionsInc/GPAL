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
    /// A status bar (usually docked at the bottom of the form) that displays real-time messages, 
    /// progress indicators, or simple key-value status pairs.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALStatusStrip statusBar = GPAL.StatusStrip
    ///     .WithName("MainStatus")
    ///     .WithStatusText("Ready - Idle")
    ///     .WithToolTip("Current application status")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALStatusStrip : GPALControl, IAllowStatusStripControlSettings
    {
        private string _text = "";

        internal GPALStatusStrip()
        {
            ControlType = FormControlType.StatusStrip;
        }

        /// <summary>
        /// The status text. Blank or whitespace falls back to "Ready". Setting this writes through to
        /// the first <see cref="ToolStripStatusLabel"/> of the live control (if realized), marshalling
        /// to the UI thread via <see cref="Control.Invoke(Delegate)"/> if required.
        /// </summary>
        public new string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                SyncTextToControl();
            }
        }

        /// <summary>
        /// Sets the initial status text. Blank or whitespace falls back to "Ready".
        /// </summary>
        /// <param name="text">The status text to display.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public new IAllowStatusStripControlSettings WithText(string text)
        {
            this.Text = text;
            return this;
        }

        /// <summary>
        /// Applies <see cref="_text"/> to the first <see cref="ToolStripStatusLabel"/> in the underlying
        /// <see cref="StatusStrip"/>'s items, marshalling to the UI thread via
        /// <see cref="Control.Invoke(Delegate)"/> if required. No-op if the underlying control is not a
        /// <see cref="StatusStrip"/> with at least one item, or that item is not a <see cref="ToolStripStatusLabel"/>.
        /// </summary>
        private void SyncTextToControl()
        {
            if (WindowsControl is StatusStrip strip && true == Alive(strip) && strip.Items.Count > 0)
            {
                ToolStripStatusLabel label = strip.Items[0] as ToolStripStatusLabel;
                if (label != null)
                {
                    PostToUi(strip, () => label.Text = _text);
                }
            }
        }
    }
}
