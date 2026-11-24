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
    /// Two panes with a bar between them, instantiated with GPAL.Splitter and laid out like any other control.<br/>
    /// The controls of both panes are added in order, with GPAL.SplitRight or GPAL.SplitBottom marking where the
    /// first pane ends. Everything before the marker is the left or top pane, everything after it the right or
    /// bottom. With no marker at all, everything is in the first pane.<br/>
    /// A splitter is built completely before it is added, so it can hold another splitter, a group box or a plain
    /// control, nested exactly as written.
    /// <example>
    /// <code>
    /// GPAL.Splitter.HSplit(0.55)
    ///     .WithFormControl(GPAL.LabelFor("Automation Engine:"))
    ///     .WithFormControl(comboBoxAutomationEngine)
    ///     .WithFormControl(GPAL.SplitRight)
    ///     .WithFormControl(GPAL.LabelFor("Retry Count:"))
    ///     .WithFormControl(gPALNumeric);
    /// </code>
    /// </example>
    /// </summary>
    public class GPALSplitter : GPALControl, IAllowSplitterControlSettings
    {
        /// <summary>True for a vertical bar with left and right panes, false for a horizontal bar with top and bottom.</summary>
        internal bool IsHorizontalSplit { get; private set; } = true;
        /// <summary>Size of the first pane in pixels, used when <see cref="Percentage"/> is zero.</summary>
        internal int Dimension { get; private set; }
        /// <summary>Size of the first pane as 0.0 to 1.0 of the splitter, which keeps its proportion as the form resizes.</summary>
        internal double Percentage { get; private set; }
        internal bool IsPercentage => 0 < Percentage;
        /// <summary>Everything added, in order, with the pane marker still in place so the panes can be told apart.</summary>
        internal List<object> FormControls { get; } = new List<object>();

        internal GPALSplitter() { }

        /// <summary>
        /// How tall the splitter is. Without one it is measured from what the panes hold.
        /// </summary>
        /// <param name="height">Height in pixels</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public new IAllowSplitterControlSettings WithHeight([Pixels] int height)
        {
            Height = height;
            return this;
        }
        /// <summary>
        /// A vertical bar with a left and a right pane, the left one this many pixels wide.
        /// </summary>
        /// <param name="leftWidth">Width of the left pane in pixels</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings HSplit([Pixels] int leftWidth)
        {
            IsHorizontalSplit = true;
            Dimension = leftWidth;
            return this;
        }
        /// <summary>
        /// A vertical bar with a left and a right pane, the left one this share of the width, so it keeps its
        /// proportion as the form resizes.
        /// </summary>
        /// <param name="leftPercent">Share of the width for the left pane, 0.0 to 1.0</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings HSplit(double leftPercent)
        {
            IsHorizontalSplit = true;
            Percentage = leftPercent;
            return this;
        }
        /// <summary>
        /// A horizontal bar with a top and a bottom pane, the top one this many pixels tall.
        /// </summary>
        /// <param name="topHeight">Height of the top pane in pixels</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings VSplit([Pixels] int topHeight)
        {
            IsHorizontalSplit = false;
            Dimension = topHeight;
            return this;
        }
        /// <summary>
        /// A horizontal bar with a top and a bottom pane, the top one this share of the height, so it keeps its
        /// proportion as the form resizes.
        /// </summary>
        /// <param name="topPercent">Share of the height for the top pane, 0.0 to 1.0</param>
        /// <returns>Fluent interface to add the controls of both panes</returns>
        public IAllowSplitterControlSettings VSplit(double topPercent)
        {
            IsHorizontalSplit = false;
            Percentage = topPercent;
            return this;
        }
        /// <summary>
        /// Add a control to the splitter. Everything before GPAL.SplitRight or GPAL.SplitBottom belongs to the
        /// first pane, everything after it to the second.
        /// </summary>
        /// <param name="gPALFormControl">A control, another container, or the pane marker</param>
        /// <returns>Fluent interface to add more controls</returns>
        public IAllowSplitterControlSettings WithFormControl(object gPALFormControl)
        {
            FormControls.Add(gPALFormControl);
            return this;
        }
        /// <summary>
        /// Returns this splitter so it can be handed to a form or to another container.
        /// </summary>
        /// <returns>This splitter</returns>
        public new GPALSplitter ToGPALObject()
        {
            return this;
        }
    }
}
