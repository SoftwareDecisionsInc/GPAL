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
    /// A GPAL multiline textare instantiated with GPAL.TextArea for use on GPAL forms.<br/>
    /// Callback EventHandler is invoked on the GPALTextarea.WindowsControl.TextChanged event.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALTextArea gPalTextArea = (GPALTextArea)GPAL.TextArea
    ///     .WithText("Type something here")
    ///     .WithCallback&lt;EventHandler&gt;(TextAreaHandler) // on text changed event handler
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.T);  // Alt-T
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalTextArea)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALTextArea : GPALControl, IAllowControlSettingsAndPlaceholder
    {
        internal string PlaceHolder { get; set; }

        // Lines wait here for the control's owning thread. See AppendLine.

        internal GPALTextArea()
        {
            ControlType = FormControlType.TextArea;
        }

        public IAllowControlSettingsAndPlaceholder WithPlaceholder(string PlaceholderText)
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

        /// <summary>
        /// Appends <paramref name="text"/> as a new line, thread-safe. When <paramref name="maxLines"/> is
        /// greater than zero and the control already has that many lines, the oldest line is dropped first.
        /// </summary>
        /// <param name="text">The line to append.</param>
        /// <param name="maxLines">Rolling cap; 0 means unlimited.</param>
        public void AppendLine(string text, int maxLines = 0) => AppendLineCore(text, maxLines);

        /// <summary>
        /// Empties the control and anything queued for it, thread-safe.
        /// </summary>
        public void Clear() => ClearLinesCore();

        /// <summary>
        /// Scrolls the text area to the last line, thread-safe.
        /// </summary>
        public void ScrollToEnd() => ScrollToEndCore();
    }
}
