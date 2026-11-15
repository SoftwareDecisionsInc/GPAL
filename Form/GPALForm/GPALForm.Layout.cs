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

using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.GPALForm.GPALForm;
using static GenerallyPositive.Enums;
using GenerallyPositive.GPALForm;

namespace GenerallyPositive.GPALForm
{
    public partial class GPALForm
    {
        /// <summary>
        /// Applies a control's width, when a workflow asked for one, and stops it stretching.<br/>
        /// Every control is anchored left and right so it fills its cell, which is what makes a button span the
        /// whole form. Dropping those two anchors leaves the control at the width it was given and WinForms
        /// centers it in the cell. A control with no width keeps the anchor its Add method gave it.
        /// </summary>
        /// <param name="width">The width to give it, in pixels. Zero or less leaves the control alone</param>
        /// <param name="control">The Windows Forms control to size</param>
        private static void ApplyWidth(int width, Control control)
        {
            if (0 >= width)
                return;

            // Marshalled; the width can change while the form is open, from another thread.
            GPALControl.OnUi(control, () =>
            {
                control.Width = width;
                control.Anchor = control.Anchor & ~(AnchorStyles.Left | AnchorStyles.Right);
            });
        }

        /// <summary>
        /// Applies the tooltip text and auto-pop delay configured on a GPAL control to its underlying Windows Forms control.
        /// </summary>
        /// <param name="gpalControl">GPAL control supplying ToolTipText and ToolTipAutoPopDelayMs.</param>
        /// <param name="winControl">Underlying Windows Forms control to attach the tooltip to.</param>
        private void ApplyToolTip(GPALControl gpalControl, Control winControl)
        {
            if (!string.IsNullOrWhiteSpace(gpalControl.ToolTipText))
            {
                FormSettings.ToolTip.SetToolTip(winControl, gpalControl.ToolTipText);
                if (gpalControl.ToolTipAutoPopDelayMs != 5000)
                    FormSettings.ToolTip.AutoPopDelay = gpalControl.ToolTipAutoPopDelayMs;
            }
        }

        // Marker on a built control whose definition had a height, so the check does not need a name.
        internal const string SetHeightMarker = "gpal.height.set";

        /// <summary>
        /// Whether this control was given a height of its own, by name or by the marker put on it when it was
        /// realized. A control that was is laid out at that height rather than stretched or auto-sized.
        /// </summary>
        private static bool HasASetHeight(Control control, HashSet<string> sized)
        {
            return true == sized.Contains(control.Name ?? "")
                || SetHeightMarker.Equals(control.Tag as string);
        }

        /// <summary>
        /// Whether a control's row should absorb the spare height of the panel.<br/>
        /// True for containers, and for scrolling controls given no explicit height: grid, list, tree and
        /// multiline text boxes. False for everything else, and false for any control whose
        /// height was set with WithHeight, which is honoured as written.
        /// </summary>
        /// <param name="control">The control sitting in the row</param>
        /// <param name="sized">Names of controls that were given an explicit height</param>
        /// <returns>True when the row should grow with the form</returns>
        private static bool GrowsWithTheForm(Control control, HashSet<string> sized)
        {
            bool retVal = false;

            if (false == HasASetHeight(control, sized))
                // A container must take the spare height, or it shrinks to its preferred size (an AutoSize TabControl
                // is just a tab strip).
                // TableLayoutPanel is not listed: a nested one is a fixed-shape composite, such as a file selector, and
                // stretching it leaves its contents at the top of an empty area.
                retVal = control is TabControl
                      || control is SplitContainer
                      // Multiline only: a taller single-line TextBox shows no more text.
                      || (control is TextBoxBase textBox && true == textBox.Multiline)
                      || control is ListView
                      || control is TreeView
                      || control is DataGridView
                      || control is ListBox;

            return retVal;
        }

        /// <summary>
        /// Applies row styles to a panel that has none.<br/>
        /// A TableLayoutPanel with no RowStyles decides its own row heights and its own preferred height, and
        /// that preferred height is what AutoScroll sizes the scrollbar from. Stating the rows makes the
        /// preferred height the sum of the control heights, so a scrollbar appears only when the controls do
        /// not fit.<br/>
        /// Each control is pinned to its own row and given AutoSize. Controls returning true from
        /// <see cref="GrowsWithTheForm"/> get Percent instead and share the remainder. No filler row is added:
        /// AutoScroll sizes the scrollbar from the preferred height, which a Percent row inflates past the
        /// content, and unused height below AutoSize rows is simply left blank.<br/>
        /// Pinning is required: controls added without a cell position are placed by the layout engine as the
        /// table grows, and setting RowCount or ColumnCount afterwards reflows them.
        /// </summary>
        /// <param name="panel">The panel to settle, and any panel nested inside it</param>
        /// <param name="sized">Names of controls that were given an explicit height</param>
        private static void SettleRows(TableLayoutPanel panel, HashSet<string> sized)
        {
            if (null == panel) return;

            SettleInside(panel, sized);

            // A panel with explicit RowStyles is left as it was written, and so is a real grid: this stacks one
            // control per row in one column, which is not what a multi-column panel is for
            if (0 < panel.RowStyles.Count || 1 < panel.ColumnCount) return;

            // RowCount is not updated by Controls.Add, so it reads zero here
            Control[] inOrder = panel.Controls.Cast<Control>().ToArray();

            if (1 > inOrder.Length) return;

            panel.SuspendLayout();

            panel.ColumnCount = 1;
            panel.ColumnStyles.Clear();
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            panel.RowCount = inOrder.Length;

            for (int row = 0; row < inOrder.Length; row++)
            {
                panel.SetCellPosition(inOrder[row], new TableLayoutPanelCellPosition(0, row));


                // AutoSize sizes a row from the control PreferredSize, which for a SplitContainer, and for any
                // control given a height with WithHeight, is not the height it is meant to be. Those rows are set
                // to that height outright. Everything else keeps AutoSize, which is what a label or a button wants
                bool ownHeight = 0 < inOrder[row].Height
                             && (inOrder[row] is SplitContainer
                              || true == HasASetHeight(inOrder[row], sized));

                panel.RowStyles.Add(true == GrowsWithTheForm(inOrder[row], sized)
                                    ? new RowStyle(SizeType.Percent, 100f)
                                    : true == ownHeight
                                      // Include the margins in the row height, or the control is clipped.
                                      ? new RowStyle(SizeType.Absolute, inOrder[row].Height
                                                                     + inOrder[row].Margin.Top
                                                                     + inOrder[row].Margin.Bottom)
                                      : new RowStyle(SizeType.AutoSize));
            }


            panel.ResumeLayout(true);

            settled.Add(panel);

            // AutoScroll uses the preferred height, which exceeds the drawn rows. Re-check whenever the panel may have
            // resized and turn AutoScroll off when everything fits. Hook HandleCreated too, since nothing can be
            // measured before the form is shown.
            panel.HandleCreated += (sender, args) => ScrollOnlyWhenNeeded(panel);
            panel.SizeChanged += (sender, args) => ScrollOnlyWhenNeeded(panel);
            panel.VisibleChanged += (sender, args) => ScrollOnlyWhenNeeded(panel);

            ScrollOnlyWhenNeeded(panel);
        }

        // Walks every child of every kind. A tab panel sits under a TabPage, a splitter pane under a
        // SplitterPanel and a group box panel under the box, so anything looking only for TableLayoutPanel
        // children reaches none of them
        private static void SettleInside(Control parent, HashSet<string> sized)
        {
            foreach (Control child in parent.Controls)
                if (child is TableLayoutPanel nested)
                    SettleRows(nested, sized);
                else
                    SettleInside(child, sized);
        }
        // The panels this method laid out; only these may scroll. A skipped panel is a custom layout, such as the
        // two-column file selector.
        private static readonly HashSet<TableLayoutPanel> settled = new HashSet<TableLayoutPanel>();

        // walks every panel this settled and takes the scroll decision again, once sizes are real
        private static void MeasureAgain(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TableLayoutPanel panel && true == settled.Contains(panel))
                    ScrollOnlyWhenNeeded(panel);

                MeasureAgain(child);
            }
        }

        // guards the re-entry: turning AutoScroll on or off resizes the panel, which raises SizeChanged again
        private static readonly HashSet<TableLayoutPanel> measuring = new HashSet<TableLayoutPanel>();

        private static void ScrollOnlyWhenNeeded(TableLayoutPanel panel)
        {
            if (null == panel || false == panel.IsHandleCreated || true == measuring.Contains(panel)) return;

            measuring.Add(panel);

            try
            {
                // Measure against the width left after a scrollbar appears, or a scrollbar narrows the content, wrapped
                // text grows, and the overflow grows with it.
                int narrower = panel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                int wanted = panel.GetPreferredSize(new Size(0 < narrower ? narrower : panel.ClientSize.Width, 0)).Height;
                int have = panel.ClientSize.Height;
                bool overflowing = wanted > have;

                //GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Panel [{panel.Parent?.Name}] wants [{wanted}] has [{have}] rows [{panel.RowCount}] scroll [{overflowing}]");

                // Only a tab panel or the form's own panel scrolls. Scrollbars inside a splitter pane or group box are
                // worse than clipping; make the container taller instead.
                bool mayScroll = panel.Parent is TabPage || panel.Parent is Form;

                if (panel.AutoScroll != (overflowing && mayScroll))
                    panel.AutoScroll = overflowing && mayScroll;

                // Set the scrollable area explicitly; AutoScroll's preferred size leaves empty space to scroll into.
                panel.AutoScrollMinSize = true == (overflowing && mayScroll) ? new Size(0, wanted) : Size.Empty;

                // Nothing recalculates the rows once the panel has its final size; force the layout pass here.
                panel.PerformLayout();
            }
            finally
            {
                measuring.Remove(panel);
            }
        }

        // Controls whose height was set with WithHeight, which keep it rather than growing
        private HashSet<string> ControlsWithASetHeight()
        {
            HashSet<string> retVal = new HashSet<string>();

            foreach (GPALControl entry in FormSettings.myForm.ControlList)
                if (0 < entry.Height && false == string.IsNullOrEmpty(entry.Name))
                    retVal.Add(entry.Name);

            return retVal;
        }

        /// <summary>
        /// Settles the rows, fits the form's height to its content when no height was set, and applies any
        /// explicit top/left settings.
        /// </summary>
        private void SizeForm()
        {
            // apply explicit positioning regardless of whether width/height were set,
            // otherwise StartPosition defaults to WindowsDefaultLocation and Left/Top are ignored
            if (0 < FormSettings.top)
            {
                FormSettings.myForm.StartPosition = FormStartPosition.Manual;
                FormSettings.myForm.Top = FormSettings.top;
            }
            if (0 < FormSettings.left)
            {
                FormSettings.myForm.StartPosition = FormStartPosition.Manual;
                FormSettings.myForm.Left = FormSettings.left;
            }

            HashSet<string> sized = ControlsWithASetHeight();

            foreach (Control child in FormSettings.myForm.Controls)
                if (child is TableLayoutPanel root)
                    SettleRows(root, sized);

            if (0 == FormSettings.height)
                FitHeightToContent();

            // Shown is the first point the panels have their final size, so measure again there.
            FormSettings.myForm.Shown += (sender, args) => MeasureAgain(FormSettings.myForm);
        }

        // Height used to lay out a form with no height of its own, so labels can wrap before the form is measured.
        private const int HeightToLayOutAt = 300;

        /// <summary>
        /// Makes the form as tall as its content, for a form given no height. One layout pass at the form's width
        /// first, so every label that wraps has taken the height its text needs, then the panel's preferred height,
        /// plus the menu, tool and status strips docked above or below it, plus the window's own frame. It never
        /// takes more than the screen's working area; past that the panel scrolls.
        /// </summary>
        private void FitHeightToContent()
        {
            GPALFormForm form = FormSettings.myForm;
            TableLayoutPanel panel = form.Controls.OfType<TableLayoutPanel>().FirstOrDefault();

            if (null != panel)
            {
                if (HeightToLayOutAt > form.Height)
                    form.Height = HeightToLayOutAt;

                // the caller suspended layout and resumes it once this returns, so it is resumed for one pass here
                // and suspended again after
                form.ResumeLayout(true);

                int strips = form.Controls.Cast<Control>()
                    .Where(control => control != panel && (DockStyle.Top == control.Dock || DockStyle.Bottom == control.Dock))
                    .Sum(control => control.Height);
                int content = panel.GetPreferredSize(new Size(panel.ClientSize.Width, 0)).Height;
                int frame = form.Height - form.ClientSize.Height;
                int height = Math.Min(content + strips + frame, Screen.PrimaryScreen.WorkingArea.Height);

                form.InitialSize = new Size(form.Width, height);
                form.Height = height;

                form.SuspendLayout();
            }
        }
        // Below this width the label has not been laid out yet.
        private const int MinimumWidthToMeasure = 60;

        /// <summary>
        /// Sets a label's height to what its text needs at its current width, on every resize.
        /// <br/><br/>
        /// The width is read from the label, not the panel, which has no meaningful width during the build.
        /// <br/><br/>
        /// Set only on change, since setting it resizes the row, which can resize the label again.
        /// </summary>
        /// <param name="label">The label to fit.</param>
        private void FitLabelHeight(Label label)
        {
            // During layout a label is briefly a few pixels wide and one line wraps onto three. Do not fit until the
            // width is real.
            if (MinimumWidthToMeasure < label.Width)
            {
                Size measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                int wanted = 18 > measured.Height ? 18 : measured.Height;

                if (label.Height != wanted)
                    label.Height = wanted;
            }
        }
    }
}
