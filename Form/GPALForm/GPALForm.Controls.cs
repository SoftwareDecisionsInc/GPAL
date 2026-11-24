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
        /// Creates the underlying Windows Forms Button for a GPALButton, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALButton">GPAL button definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddButton(GPALButton gPALButton)
        {
            FormSettings.myForm.ControlList.Add(gPALButton);
            Button button = new Button
            {
                Name = gPALButton.Name,
                Text = gPALButton.Text,
                //                AutoSize = true,
                // No Bottom, so the button keeps its own height rather than stretching to fill its row. The
                // four-sided anchor is for the scrolling controls - grid, list, tree, text boxes - which show
                // more content at a greater height
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALButton.Enabled,
                Height = 10 < gPALButton.Height ? gPALButton.Height : 20,
            };
           
            gPALButton.WindowsControl = button;

            ApplyWidth(0 < gPALButton.Width ? gPALButton.Width : FormSettings.buttonWidth, button);

            foreach (var (evt, handler) in gPALButton.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DoubleClick:
                        button.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        button.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(button);

            if (true == gPALButton.IsDefault)
            {
                FormSettings.myForm.AcceptButton = button;
                button.DialogResult = DialogResult.OK;
                button.TabIndex = 0;
            }

            if (!string.IsNullOrWhiteSpace(gPALButton.ToolTipText))
                ApplyToolTip(gPALButton, button);

            return this;
        }
        /// <summary>
        /// Adds a GPALChart's underlying Chart control to the form, sizes it, and wires its drag/drop and click callbacks and tooltip.
        /// </summary>
        /// <param name="gPALChart">GPAL chart definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddChart(GPALChart gPALChart)
        {
            FormSettings.myForm.ControlList.Add(gPALChart);
            // A control with no name has no AutomationId. The chart is already built, so name it here.
            ((Chart)gPALChart.WindowsControl).Name = gPALChart.Name;
            ((Chart)gPALChart.WindowsControl).Enabled = gPALChart.IsEnabled;

            FormSettings.TableLayoutPanel.Controls.Add((Chart)gPALChart.WindowsControl);
            ((Chart)gPALChart.WindowsControl).Height = 10 < gPALChart.Height ? gPALChart.Height : 150;

            Chart chart = gPALChart.WindowsControl;

            ApplyWidth(gPALChart.Width, chart);

            foreach (var (evt, handler) in gPALChart.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DragDrop:
                        chart.DragDrop += (DragEventHandler)handler;
                        break;

                    case ControlEventType.DragEnter:
                        chart.DragEnter += (DragEventHandler)handler;
                        break;

                    case ControlEventType.DragLeave:
                        chart.DragLeave += (EventHandler)handler;
                        break;

                    case ControlEventType.DragOver:
                        chart.DragOver += (DragEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        chart.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        chart.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            if (!string.IsNullOrWhiteSpace(gPALChart.ToolTipText))
                ApplyToolTip(gPALChart, chart);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms CheckBox for a GPALCheckbox, keeps its Checked state synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALCheckbox">GPAL checkbox definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddCheckbox(GPALCheckbox gPALCheckbox)
        {
            FormSettings.myForm.ControlList.Add(gPALCheckbox);
            CheckBox checkBox = new CheckBox
            {
                Name = gPALCheckbox.Name,
                Text = gPALCheckbox.Text,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Checked = gPALCheckbox.Checked,
                Enabled = gPALCheckbox.Enabled,
                Height = 10 < gPALCheckbox.Height ? gPALCheckbox.Height : 20,
            };
            gPALCheckbox.WindowsControl = checkBox;

            ApplyWidth(gPALCheckbox.Width, checkBox);

            // keeps the GPAL checkbox in step with the control; a workflow can also hook CheckedChanged
            checkBox.CheckedChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALCheckbox.Checked = ((CheckBox)sender).Checked; }); // copy the checked state up to the control

            foreach (var (evt, handler) in gPALCheckbox.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.CheckedChanged:
                        checkBox.CheckedChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        checkBox.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        checkBox.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(checkBox);

            if (!string.IsNullOrWhiteSpace(gPALCheckbox.ToolTipText))
                ApplyToolTip(gPALCheckbox, checkBox);

            return this;
        }
        // The first selectable item, used when there is no fallback item.
        private static int FirstEnabledIndex(ComboBox combo, GPALComboBox gpalCombo)
        {
            int retVal = -1;

            for (int index = 0; index < combo.Items.Count && -1 == retVal; index++)
                if (false == gpalCombo.disabledItems.Contains(combo.Items[index].ToString()))
                    retVal = index;

            return retVal;
        }

        /// <summary>
        /// Creates the underlying Windows Forms ComboBox for a GPALComboBox, populates its items and selection, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gpalCombo">GPAL combo box definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddComboBox(GPALComboBox gpalCombo)
        {
            FormSettings.myForm.ControlList.Add(gpalCombo);

            var combo = new ComboBox
            {
                Name = gpalCombo.Name,
                Enabled = gpalCombo.IsEnabled,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Height = 10 < gpalCombo.Height ? gpalCombo.Height : 20,
            };

            // populate items
            foreach (string item in gpalCombo.items)
            {
                combo.Items.Add(item);
            }

            // restore the selection if set: by name first, then by index
            if (!string.IsNullOrEmpty(gpalCombo.selectedItem))
            {
                int idx = combo.Items.IndexOf(gpalCombo.selectedItem);
                if (0 <= idx) combo.SelectedIndex = idx;
            }
            else if (0 <= gpalCombo.selectedIndex && gpalCombo.selectedIndex < combo.Items.Count)
            {
                combo.SelectedIndex = gpalCombo.selectedIndex;
            }

            // A disabled item is drawn grey and cannot be chosen. Owner-draw is the only way to disable one WinForms
            // dropdown entry.
            combo.DrawMode = DrawMode.OwnerDrawFixed;

            combo.DrawItem += (sender, args) =>
            {
                if (0 > args.Index) return;

                string text = combo.Items[args.Index].ToString();
                bool disabled = true == gpalCombo.disabledItems.Contains(text);

                // Only selectable items get the highlight.
                if (false == disabled)
                    args.DrawBackground();
                else
                    using (SolidBrush background = new SolidBrush(SystemColors.Window))
                        args.Graphics.FillRectangle(background, args.Bounds);

                using (SolidBrush foreground = new SolidBrush(disabled ? SystemColors.GrayText : args.ForeColor))
                    args.Graphics.DrawString(text, args.Font, foreground, args.Bounds);

                if (false == disabled) args.DrawFocusRectangle();
            };

            // Restore the last allowed selection. SelectedIndexChanged fires for mouse, keyboard and code.
            combo.SelectedIndexChanged += (sender, args) =>
            {
                if (0 > combo.SelectedIndex) return;

                string text = combo.Items[combo.SelectedIndex].ToString();

                if (false == gpalCombo.disabledItems.Contains(text))
                {
                    gpalCombo.lastEnabledIndex = combo.SelectedIndex;
                    return;
                }

                GPAL.PublishSimpleEvent(Enums.GPALEventType.CAUTION, $"[{text}] does not apply to what is selected, so it cannot be chosen", null, Enums.GPALObjectType.GPALForm);

                combo.SelectedIndex = 0 <= gpalCombo.lastEnabledIndex && gpalCombo.lastEnabledIndex < combo.Items.Count
                    ? gpalCombo.lastEnabledIndex
                    : FirstEnabledIndex(combo, gpalCombo);
            };

            gpalCombo.lastEnabledIndex = combo.SelectedIndex;

            gpalCombo.WindowsControl = combo;

            ApplyWidth(gpalCombo.Width, combo);

            foreach (var (evt, handler) in gpalCombo.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.SelectedIndexChanged:
                        combo.SelectedIndexChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.ValueChanged:
                        combo.SelectedValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        combo.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            // Apply tooltip if set
            if (false == string.IsNullOrEmpty(gpalCombo.ToolTipText))
                ApplyToolTip(gpalCombo, combo);

            FormSettings.TableLayoutPanel.Controls.Add(combo);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms DataGridView for a GPALDataGridView, adds its columns, syncs data from the GPAL grid, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALDataGridView">GPAL data grid view definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddDataGridView(GPALDataGridView gPALDataGridView)
        {
            FormSettings.myForm.ControlList.Add(gPALDataGridView);

            DataGridView dgv = new DataGridView
            {
                Name = gPALDataGridView.Name,
                ReadOnly = gPALDataGridView.ReadOnly,
                AutoGenerateColumns = gPALDataGridView.AutoGenerateColumns,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALDataGridView.IsEnabled,
                AllowUserToAddRows = false,   // usually not wanted in dashboards
                RowHeadersVisible = false,    // no row header column
                AutoSize = true,
                Height = 10 < gPALDataGridView.Height ? gPALDataGridView.Height : 20,
            };

            // Add explicit columns if supplied
            foreach (string colName in gPALDataGridView.ColumnNames)
            {
                dgv.Columns.Add(colName, colName);
            }

            gPALDataGridView.WindowsControl = dgv;

            ApplyWidth(gPALDataGridView.Width, dgv);

            // Wire callbacks
            foreach (var (evt, handler) in gPALDataGridView.Callbacks)
            {
                if (handler == null) continue;

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.CellValueChanged:
                    case ControlEventType.Change:
                        dgv.CellValueChanged += (DataGridViewCellEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        dgv.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        dgv.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            // Populate from grid if already set during fluent chain
            gPALDataGridView.SyncGridToControl();
            gPALDataGridView.ApplyColumnWidths();
            gPALDataGridView.ApplyWrap();

            FormSettings.TableLayoutPanel.Controls.Add(dgv);

            // Recalculate on resize so the last column keeps ending at the control's edge; there is no width before the
            // handle exists.
            if (true == gPALDataGridView.LastColumnFills)
            {
                void fillLastColumn(object s, EventArgs e) => gPALDataGridView.FillLastColumn();

                dgv.Resize += fillLastColumn;
                dgv.HandleCreated += fillLastColumn;

                // Adding or removing rows toggles the vertical scrollbar and changes the last column's width without a
                // resize.
                dgv.RowsAdded += (s, e) => gPALDataGridView.FillLastColumn();
                dgv.RowsRemoved += (s, e) => gPALDataGridView.FillLastColumn();
            }

            if (!string.IsNullOrWhiteSpace(gPALDataGridView.ToolTipText))
                ApplyToolTip(gPALDataGridView, dgv);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms DateTimePicker for a GPALDateTimePicker, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALDateTimePicker">GPAL date/time picker definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddDateTimePicker(GPALDateTimePicker gPALDateTimePicker)
        {
            FormSettings.myForm.ControlList.Add(gPALDateTimePicker);

            DateTimePicker dtp = new DateTimePicker
            {
                Name = gPALDateTimePicker.Name,
                Value = gPALDateTimePicker.Value,
                Format = gPALDateTimePicker.Format,
                CustomFormat = gPALDateTimePicker.CustomFormat,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALDateTimePicker.IsEnabled,
                Height = 10 < gPALDateTimePicker.Height ? gPALDateTimePicker.Height : 10,
            };

            gPALDateTimePicker.WindowsControl = dtp;

            ApplyWidth(gPALDateTimePicker.Width, dtp);

            foreach (var (evt, handler) in gPALDateTimePicker.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.ValueChanged:
                    case ControlEventType.Change:
                        dtp.ValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        dtp.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(dtp);

            if (!string.IsNullOrWhiteSpace(gPALDateTimePicker.ToolTipText))
                ApplyToolTip(gPALDateTimePicker, dtp);

            return this;
        }
        /// <summary>
        /// Builds a Browse button + read-only path textbox pair for a GPALFileSelector, wires the file/folder dialog, callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALFileSelector">GPAL file selector definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddFileSelector(GPALFileSelector gPALFileSelector)
        {
            FormSettings.myForm.ControlList.Add(gPALFileSelector);

            if (gPALFileSelector._compact)
                return AddFileSelectorCompact(gPALFileSelector);

            Button browseButton = new Button
            {
                Name = gPALFileSelector.Name + "_Browse",
                Text = "Browse...",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Width = 80,
                Enabled = gPALFileSelector.IsEnabled
            };

            TextBox displayBox = new TextBox
            {
                Name = gPALFileSelector.Name + "_Path",
                ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                // Show the path from WithSelectedPath.
                Text = true == string.IsNullOrWhiteSpace(gPALFileSelector.SelectedPath)
                     ? "(click Browse to select)"
                     : gPALFileSelector.SelectedPath,
                Enabled = gPALFileSelector.IsEnabled
            };

            TableLayoutPanel container = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Top,          // full width, hugs a single row of content (no tall default panel gap)
                // As in the compact selector below: AutoSize would size the percent column from the path, and a long
                // path adds a horizontal scrollbar.
                Height = Math.Max(displayBox.PreferredHeight, browseButton.Height) + 4,
                Margin = new Padding(0)
            };
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            container.Controls.Add(browseButton, 0, 0);
            container.Controls.Add(displayBox, 1, 0);

            gPALFileSelector.WindowsControl = container;

            // NotifySelection delivers Default, Change and Click when the dialog closes, with FileSelectorEventArgs.
            // Wiring them to the box too would run the handler twice. Only TextChanged belongs to the box.
            foreach (var (evt, handler) in gPALFileSelector.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                if (ControlEventType.TextChanged == evt)
                    displayBox.TextChanged += (EventHandler)handler;
            }


            browseButton.Click += (s, e) =>
            {
                string[] selected = OpenBrowseDialog(gPALFileSelector);
                bool cancelled = selected == null || selected.Length == 0;
                gPALFileSelector.NotifySelection(cancelled, selected);
                displayBox.Text = cancelled ? "(selection cancelled)" : string.Join("; ", selected);
            };

            FormSettings.TableLayoutPanel.Controls.Add(container);

            if (!string.IsNullOrWhiteSpace(gPALFileSelector.ToolTipText))
                ApplyToolTip(gPALFileSelector, container);

            return this;
        }

        private IAllowFormSettingsAndActions AddFileSelectorCompact(GPALFileSelector gPALFileSelector)
        {
            PlaceholderTextBox textBox = new PlaceholderTextBox
            {
                Name            = gPALFileSelector.Name + "_Path",
                Anchor          = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled         = gPALFileSelector.IsEnabled,
                PlaceholderText = gPALFileSelector._placeholder,
                // Set after the placeholder so the path is not treated as placeholder text.
                Text            = gPALFileSelector.SelectedPath
            };

            Button dotButton = new Button
            {
                Name    = gPALFileSelector.Name + "_Browse",
                Text    = "...",
                Width   = 28,
                Anchor  = AnchorStyles.Top | AnchorStyles.Right,
                Enabled = gPALFileSelector.IsEnabled
            };

            TableLayoutPanel container = new TableLayoutPanel
            {
                Name = gPALFileSelector.Name,
                ColumnCount = 2,
                RowCount    = 1,
                Dock        = DockStyle.Top,   // full width, hugs a single row of content (no tall default panel gap)
                // No AutoSize. Docking to the top already takes the form's width, and AutoSize would let a long path
                // add a horizontal scrollbar that resizing never removes.
                Height      = Math.Max(textBox.PreferredHeight, dotButton.Height) + 4,
                Margin      = new Padding(0)
            };
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            container.Controls.Add(textBox,   0, 0);
            container.Controls.Add(dotButton, 1, 0);

            // WindowsControl points to the text box so SyncDisplay() and .Text access work naturally
            gPALFileSelector.WindowsControl = textBox;

            // NotifySelection delivers Default, Change and Click when the dialog closes, with FileSelectorEventArgs.
            // Wiring them to the box too would run the handler twice. Only TextChanged belongs to the box.
            foreach (var (evt, handler) in gPALFileSelector.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                if (ControlEventType.TextChanged == evt)
                    textBox.TextChanged += (EventHandler)handler;
            }


            textBox.TextChanged += (s, e) => gPALFileSelector._selectedPath = textBox.RealText;

            dotButton.Click += (s, e) =>
            {
                string[] selected = OpenBrowseDialog(gPALFileSelector);
                bool cancelled = selected == null || selected.Length == 0;
                if (!cancelled)
                    textBox.Text = selected[0];
                gPALFileSelector.NotifySelection(cancelled, selected);
            };

            FormSettings.TableLayoutPanel.Controls.Add(container);

            if (!string.IsNullOrWhiteSpace(gPALFileSelector.ToolTipText))
                ApplyToolTip(gPALFileSelector, container);

            return this;
        }

        private static string[] OpenBrowseDialog(GPALFileSelector gPALFileSelector)
        {
            if (gPALFileSelector._selectFolder)
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description  = gPALFileSelector._title;
                    fbd.SelectedPath = gPALFileSelector._initialDirectory;
                    return fbd.ShowDialog() == DialogResult.OK ? new[] { fbd.SelectedPath } : null;
                }
            }
            else
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Title           = gPALFileSelector._title;
                    ofd.InitialDirectory = gPALFileSelector._initialDirectory;
                    ofd.Filter          = gPALFileSelector._filter;
                    ofd.Multiselect     = gPALFileSelector._multiSelect;
                    return ofd.ShowDialog() == DialogResult.OK ? ofd.FileNames : null;
                }
            }
        }
        /// <summary>
        /// Creates the underlying placeholder-aware textbox for a GPALInput, applies password masking if requested, keeps its Text synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALInput">GPAL input definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddInput(GPALInput gPALInput)
        {
            FormSettings.myForm.ControlList.Add(gPALInput);
            PlaceholderTextBox input = new PlaceholderTextBox
            {
                Name = gPALInput.Name,
                Text = gPALInput.Text,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALInput.Enabled,
                Height = 10 < gPALInput.Height ? gPALInput.Height : 20,
            };

            if (!string.IsNullOrEmpty(gPALInput.PlaceHolder))
                input.PlaceholderText = gPALInput.PlaceHolder;

            if (true == gPALInput.InputIsPassword)
                input.PasswordChar = '\u25CF';  // bullet

            if (true == gPALInput.IsReadOnly)
                input.ReadOnly = true;

            gPALInput.WindowsControl = input;

            ApplyWidth(gPALInput.Width, input);

            input.Text = gPALInput.Text;

            input.TextChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALInput.Text = ((PlaceholderTextBox)sender).RealText; }); // copy changed text up to control, never the placeholder

            foreach (var (evt, handler) in gPALInput.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.TextChanged:
                        input.TextChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        input.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        input.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.KeyDown:
                        input.KeyDown += (KeyEventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(input);

            if (!string.IsNullOrWhiteSpace(gPALInput.ToolTipText))
                ApplyToolTip(gPALInput, input);

            return this;
        }

        /// <summary>
        /// Creates the empty panel for a GPALSpacer: exactly its height, with no margin, so the gap is the number
        /// asked for. A label will not do, since the form never makes one shorter than a line of text.
        /// </summary>
        /// <param name="gPALSpacer">GPAL spacer to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddSpacer(GPALSpacer gPALSpacer)
        {
            FormSettings.myForm.ControlList.Add(gPALSpacer);
            Panel spacer = new Panel
            {
                Name = gPALSpacer.Name,
                Height = gPALSpacer.Height,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            gPALSpacer.WindowsControl = spacer;

            FormSettings.TableLayoutPanel.Controls.Add(spacer);

            return this;
        }

        /// <summary>
        /// Creates the Windows Forms LinkLabel for a GPALLinkLabel. Clicking it opens its address, and any callback
        /// given for Click runs as well; it sizes to its text the way a label does.
        /// </summary>
        /// <param name="gPALLinkLabel">GPAL link to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddLinkLabel(GPALLinkLabel gPALLinkLabel)
        {
            FormSettings.myForm.ControlList.Add(gPALLinkLabel);
            LinkLabel link = new LinkLabel
            {
                Name = gPALLinkLabel.Name,
                Enabled = gPALLinkLabel.IsEnabled,
                Text = gPALLinkLabel.Text,
                TextAlign = gPALLinkLabel.TextAlignment,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
            };
            // the size a label uses, and regular rather than bold, so it reads as a link beside the labels
            link.Font = new Font(gPALLinkLabel.FontType.FontFamily, 10f, FontStyle.Regular);
            link.Height = 18 < gPALLinkLabel.Height ? gPALLinkLabel.Height : 18;

            // sized to its text as the width changes, the same as a label, unless a height was asked for
            if (18 >= gPALLinkLabel.Height)
                link.SizeChanged += (sender, args) => FitLabelHeight(link);

            link.LinkClicked += (sender, args) => gPALLinkLabel.Open();

            gPALLinkLabel.WindowsControl = link;

            ApplyWidth(gPALLinkLabel.Width, link);

            foreach (var (evt, handler) in gPALLinkLabel.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DoubleClick:
                        link.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        link.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(link);

            if (!string.IsNullOrWhiteSpace(gPALLinkLabel.ToolTipText))
                ApplyToolTip(gPALLinkLabel, link);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms Label for a GPALLabel, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALLabel">GPAL label definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddLabel(GPALLabel gPALLabel)
        {
            FormSettings.myForm.ControlList.Add(gPALLabel);
            Label label = new Label
            {
                Name = gPALLabel.Name,
                Enabled = gPALLabel.IsEnabled,
                Text = gPALLabel.Text,
                Font = gPALLabel.FontType,
                TextAlign = gPALLabel.TextAlignment,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            label.Font = new Font(label.Font.FontFamily, 10f, FontStyle.Bold);

            label.AutoSize = false;
            label.Height = 18 < gPALLabel.Height ? gPALLabel.Height : 18;

            // A Label clips every wrapped line after the first. With no height set, it takes the height its text needs
            // and recalculates when the width changes.
            if (18 >= gPALLabel.Height)
                label.SizeChanged += (sender, args) => FitLabelHeight(label);

            gPALLabel.WindowsControl = label;

            ApplyWidth(gPALLabel.Width, label);

            foreach (var (evt, handler) in gPALLabel.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DoubleClick:
                        label.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        label.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(label);

            if (!string.IsNullOrWhiteSpace(gPALLabel.ToolTipText))
                ApplyToolTip(gPALLabel, label);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms ListView for a GPALListView, adds its columns, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALListView">GPAL list view definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddListView(GPALListView gPALListView)
        {
            FormSettings.myForm.ControlList.Add(gPALListView);

            GPALListViewControl listView = new GPALListViewControl
            {
                HeaderCheckBox = gPALListView.TickAll,
                Name = gPALListView.Name,
                View = gPALListView.ViewMode,
                FullRowSelect = gPALListView.FullRowSelect,
                MultiSelect = gPALListView.MultiSelect,
                CheckBoxes = gPALListView.checkBoxes,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALListView.IsEnabled,
                Height = 10 < gPALListView.Height ? gPALListView.Height: 150,
            };

            // Add columns
            foreach (string header in gPALListView.Columns)
            {
                listView.Columns.Add(header, -2, HorizontalAlignment.Left);  // auto-size
            }

            gPALListView.WindowsControl = listView;

            ApplyWidth(gPALListView.Width, listView);
            gPALListView.ApplyColumnWidths();

            // A greyed row cannot be selected. ListView has no cancellable selection event, so the selection is removed
            // as soon as it happens.
            listView.ItemSelectionChanged += (sender, args) =>
            {
                if (true == args.IsSelected && SystemColors.GrayText == args.Item.ForeColor)
                    args.Item.Selected = false;
            };

            // A greyed row cannot be ticked either; a tick it already carries can still be cleared.
            listView.ItemCheck += (sender, args) =>
            {
                if (CheckState.Checked == args.NewValue && SystemColors.GrayText == listView.Items[args.Index].ForeColor)
                    args.NewValue = args.CurrentValue;
            };

            // The header box ticks or clears the rows, and follows them when they are ticked one at a time.
            if (true == gPALListView.TickAll)
            {
                listView.HeaderBoxClicked += (sender, args) => gPALListView.TickAllRows(listView);
                listView.ItemChecked += (sender, args) => gPALListView.SyncHeaderBox();
            }

            foreach (var (evt, handler) in gPALListView.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.SelectedIndexChanged:
                    case ControlEventType.Change:
                        listView.SelectedIndexChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        listView.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        listView.Click += (EventHandler)handler;
                        break;

                    // Checking a row is not selecting it.
                    case ControlEventType.ItemChecked:
                        listView.ItemChecked += (ItemCheckedEventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(listView);

            if (gPALListView.Sortable)
            {
                var comparer = new ListViewItemComparer();
                listView.ListViewItemSorter = comparer;
                listView.ColumnClick += (s, e) =>
                {
                    if (comparer.Column == e.Column)
                        comparer.Order = comparer.Order == SortOrder.Ascending
                            ? SortOrder.Descending : SortOrder.Ascending;
                    else
                    {
                        comparer.Column = e.Column;
                        comparer.Order  = SortOrder.Ascending;
                    }
                    listView.Sort();
                };
            }

            if (gPALListView.ColumnWeights.Count > 0)
            {
                var weights = gPALListView.ColumnWeights;
                double total = 0;
                foreach (var w in weights) total += w;
                if (total <= 0) total = 1;

                void applyWeights(object s, EventArgs e)
                {
                    int available = listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                    if (available <= 0) return;
                    for (int i = 0; i < weights.Count && i < listView.Columns.Count; i++)
                        listView.Columns[i].Width = (int)(available * weights[i] / total);
                }

                listView.Resize += applyWeights;
                listView.HandleCreated += applyWeights;
            }

            // Calculate all columns together once the control has a handle, and again on resize. A list on an unopened
            // tab has no handle until shown.
            if (true == gPALListView.LastColumnFills)
            {
                void layOutColumns(object s, EventArgs e) => gPALListView.LayOutColumns();

                listView.Resize += layOutColumns;
                listView.HandleCreated += layOutColumns;
            }

            if (!string.IsNullOrWhiteSpace(gPALListView.ToolTipText))
                ApplyToolTip(gPALListView, listView);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms NumericUpDown for a GPALNumericUpDown, applies its range/increment/decimal settings, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALNumericUpDown">GPAL numeric up-down definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddNumericUpDown(GPALNumericUpDown gPALNumericUpDown)
        {
            FormSettings.myForm.ControlList.Add(gPALNumericUpDown);

            NumericUpDown nud = new NumericUpDown
            {
                Name = gPALNumericUpDown.Name,
                Value = gPALNumericUpDown.Value,
                Minimum = gPALNumericUpDown.Minimum,
                Maximum = gPALNumericUpDown.Maximum,
                Increment = gPALNumericUpDown.Increment,
                DecimalPlaces = gPALNumericUpDown.DecimalPlaces,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALNumericUpDown.IsEnabled,
                Height = 10 < gPALNumericUpDown.Height ? gPALNumericUpDown.Height : 20,
            };

            gPALNumericUpDown.WindowsControl = nud;

            ApplyWidth(gPALNumericUpDown.Width, nud);

            foreach (var (evt, handler) in gPALNumericUpDown.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.ValueChanged:
                    case ControlEventType.Change:
                        nud.ValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        nud.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(nud);

            if (!string.IsNullOrWhiteSpace(gPALNumericUpDown.ToolTipText))
                ApplyToolTip(gPALNumericUpDown, nud);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms PictureBox for a GPALPicture, puts its image and size mode on, wires its click callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALPicture">GPAL picture definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddPicture(GPALPicture gPALPicture)
        {
            FormSettings.myForm.ControlList.Add(gPALPicture);

            PictureBox pictureBox = new PictureBox
            {
                Name = gPALPicture.Name,
                Enabled = gPALPicture.IsEnabled,
                Image = gPALPicture.Image,
                SizeMode = gPALPicture.SizeMode,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Height = 10 < gPALPicture.Height ? gPALPicture.Height : 150,
            };

            gPALPicture.WindowsControl = pictureBox;

            ApplyWidth(gPALPicture.Width, pictureBox);

            foreach (var (evt, handler) in gPALPicture.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        pictureBox.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        pictureBox.DoubleClick += (EventHandler)handler;
                        break;

                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(pictureBox);

            if (!string.IsNullOrWhiteSpace(gPALPicture.ToolTipText))
                ApplyToolTip(gPALPicture, pictureBox);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms TrackBar for a GPALSlider, applies its range, value, ticks and
        /// orientation, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALSlider">GPAL slider definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddSlider(GPALSlider gPALSlider)
        {
            FormSettings.myForm.ControlList.Add(gPALSlider);

            TrackBar slider = new TrackBar
            {
                Name = gPALSlider.Name,
                Minimum = gPALSlider.Minimum,
                Maximum = gPALSlider.Maximum,
                TickFrequency = 1 > gPALSlider.TickFrequency ? 1 : gPALSlider.TickFrequency,
                Orientation = gPALSlider.Orientation,
                Value = gPALSlider.Value,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALSlider.IsEnabled,
                Height = 10 < gPALSlider.Height ? gPALSlider.Height : 45,
            };

            gPALSlider.WindowsControl = slider;

            ApplyWidth(gPALSlider.Width, slider);

            foreach (var (evt, handler) in gPALSlider.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.ValueChanged:
                    case ControlEventType.Change:
                        slider.ValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Scroll:
                        slider.Scroll += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        slider.Click += (EventHandler)handler;
                        break;

                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(slider);

            if (!string.IsNullOrWhiteSpace(gPALSlider.ToolTipText))
                ApplyToolTip(gPALSlider, slider);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms ProgressBar for a GPALProgressBar, applies its range/value/style, wires its click callback and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALProgressBar">GPAL progress bar definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddProgressBar(GPALProgressBar gPALProgressBar)
        {
            FormSettings.myForm.ControlList.Add(gPALProgressBar);

            ProgressBar progressBar = new ProgressBar
            {
                Name = gPALProgressBar.Name,
                Minimum = gPALProgressBar.Minimum,
                Maximum = gPALProgressBar.Maximum,
                Value = gPALProgressBar.Value,
                Style = gPALProgressBar.Style,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALProgressBar.IsEnabled,
                Height = 10 < gPALProgressBar.Height ? gPALProgressBar.Height : 20,
            };

            gPALProgressBar.WindowsControl = progressBar;

            ApplyWidth(gPALProgressBar.Width, progressBar);

            foreach (var (evt, handler) in gPALProgressBar.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        progressBar.Click += (EventHandler)handler;
                        break;

                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(progressBar);

            if (!string.IsNullOrWhiteSpace(gPALProgressBar.ToolTipText))
                ApplyToolTip(gPALProgressBar, progressBar);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms RadioButton for a GPALRadioButton, keeps its Checked state synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALRadioButton">GPAL radio button definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddRadioButton(GPALRadioButton gPALRadioButton)
        {
            FormSettings.myForm.ControlList.Add(gPALRadioButton);
            RadioButton radioButton = new RadioButton
            {
                Name = gPALRadioButton.Name,
                Text = gPALRadioButton.Text,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Checked = gPALRadioButton.Checked,
                Enabled = gPALRadioButton.Enabled,
                Height = 10 < gPALRadioButton.Height ? gPALRadioButton.Height : 20,
            };
            gPALRadioButton.WindowsControl = radioButton;

            ApplyWidth(gPALRadioButton.Width, radioButton);
            radioButton.CheckedChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALRadioButton.Checked = ((RadioButton)sender).Checked; }); // copy the checked state up to the control

            foreach (var (evt, handler) in gPALRadioButton.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.CheckedChanged:
                        radioButton.CheckedChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        radioButton.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(radioButton);

            // One radio button per container is checked. WinForms unchecks the others only when one is checked after
            // it is in place, so two checked before the form was built would both show checked. The last one added
            // stays checked.
            if (true == radioButton.Checked)
                foreach (RadioButton other in radioButton.Parent.Controls.OfType<RadioButton>().ToList())
                    if (other != radioButton && true == other.Checked)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Radio buttons [{other.Name}] and [{radioButton.Name}] were both checked in the same container, so only [{radioButton.Name}] is", gPALRadioButton, GPALObjectType.Other);

                        other.Checked = false;
                    }

            if (!string.IsNullOrWhiteSpace(gPALRadioButton.ToolTipText))
                ApplyToolTip(gPALRadioButton, radioButton);

            return this;
        }

        /// <summary>
        /// Creates the underlying multi-line, word-wrapped textbox for a GPALTextArea, keeps its Text synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALTextArea">GPAL text area definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTextArea(GPALTextArea gPALTextArea)
        {
            FormSettings.myForm.ControlList.Add(gPALTextArea);
            PlaceholderTextBox textBox = new PlaceholderTextBox
            {
                Name = gPALTextArea.Name,
                Text = gPALTextArea.Text,
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Both,
                Height = 10 < gPALTextArea.Height ? gPALTextArea.Height : 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALTextArea.Enabled
            };
            if (true == gPALTextArea.IsReadOnly)
                textBox.ReadOnly = true;

            if (!string.IsNullOrEmpty(gPALTextArea.PlaceHolder))
                textBox.PlaceholderText = gPALTextArea.PlaceHolder;

            gPALTextArea.WindowsControl = textBox;

            ApplyWidth(gPALTextArea.Width, textBox);
            textBox.Text = gPALTextArea.Text;

            textBox.TextChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALTextArea.Text = ((PlaceholderTextBox)sender).RealText; }); // copy changed text up to control, never the placeholder

            foreach (var (evt, handler) in gPALTextArea.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.TextChanged:
                        textBox.TextChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        textBox.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(textBox);

            if (!string.IsNullOrWhiteSpace(gPALTextArea.ToolTipText))
                ApplyToolTip(gPALTextArea, textBox);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms RichTextBox for a GPALRichTextBox, renders its initial Markdown,
        /// wires its tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALRichTextBox">GPAL rich text box definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddRichTextBox(GPALRichTextBox gPALRichTextBox)
        {
            FormSettings.myForm.ControlList.Add(gPALRichTextBox);
            RichTextBox richTextBox = new RichTextBox
            {
                Name = gPALRichTextBox.Name,
                Multiline = true,
                WordWrap = true,
                ReadOnly = gPALRichTextBox.IsReadOnly,
                ScrollBars = RichTextBoxScrollBars.Both,
                Height = 10 < gPALRichTextBox.Height ? gPALRichTextBox.Height : 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALRichTextBox.Enabled
            };
            gPALRichTextBox.WindowsControl = richTextBox;

            ApplyWidth(gPALRichTextBox.Width, richTextBox);
            GPALRichTextBox.RenderMarkdown(richTextBox, gPALRichTextBox.Text);

            // Do not copy the control's text back; it shows rendered Markdown, while Text holds the source.
            foreach (var (evt, handler) in gPALRichTextBox.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.TextChanged:
                        richTextBox.TextChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        richTextBox.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        richTextBox.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.KeyDown:
                        richTextBox.KeyDown += (KeyEventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(richTextBox);

            if (!string.IsNullOrWhiteSpace(gPALRichTextBox.ToolTipText))
                ApplyToolTip(gPALRichTextBox, richTextBox);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms TreeView for a GPALTreeView, applies its checkbox/line/plus-minus settings, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALTreeView">GPAL tree view definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTreeView(GPALTreeView gPALTreeView)
        {
            FormSettings.myForm.ControlList.Add(gPALTreeView);

            TreeView tv = new TreeView
            {
                Name = gPALTreeView.Name,
                CheckBoxes = gPALTreeView.CheckBoxes,
                ShowLines = gPALTreeView.ShowLines,
                ShowPlusMinus = gPALTreeView.ShowPlusMinus,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALTreeView.IsEnabled,
                Height = 10 < gPALTreeView.Height ? gPALTreeView.Height : 150,
            };

            // BeforeSelect can refuse outright, so a greyed node never becomes the selected one
            tv.BeforeSelect += (sender, args) =>
            {
                if (null != args.Node && SystemColors.GrayText == args.Node.ForeColor) args.Cancel = true;
            };

            gPALTreeView.WindowsControl = tv;

            ApplyWidth(gPALTreeView.Width, tv);

            foreach (var (evt, handler) in gPALTreeView.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.AfterSelect:
                    case ControlEventType.Change:
                        tv.AfterSelect += (TreeViewEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        tv.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        tv.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(tv);

            if (!string.IsNullOrWhiteSpace(gPALTreeView.ToolTipText))
                ApplyToolTip(gPALTreeView, tv);

            return this;
        }
    }

    internal class ListViewItemComparer : System.Collections.IComparer
    {
        public int       Column { get; set; } = 0;
        public SortOrder Order  { get; set; } = SortOrder.Ascending;

        public int Compare(object x, object y)
        {
            var lx = (ListViewItem)x;
            var ly = (ListViewItem)y;

            string sx = Column == 0 ? lx.Text
                : Column < lx.SubItems.Count ? lx.SubItems[Column].Text : string.Empty;
            string sy = Column == 0 ? ly.Text
                : Column < ly.SubItems.Count ? ly.SubItems[Column].Text : string.Empty;

            int result;

            // Numeric (strip currency/percent symbols and commas)
            string nx = sx.Replace(",", "").Replace("$", "").Replace("%", "").Trim();
            string ny = sy.Replace(",", "").Replace("$", "").Replace("%", "").Trim();
            if (double.TryParse(nx, out double dx) && double.TryParse(ny, out double dy))
                result = dx.CompareTo(dy);
            // Date
            else if (System.DateTime.TryParse(sx, out System.DateTime dtx) &&
                     System.DateTime.TryParse(sy, out System.DateTime dty))
                result = dtx.CompareTo(dty);
            // String
            else
                result = string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);

            return Order == SortOrder.Ascending ? result : -result;
        }
    }

    // The list view on a GPAL form. With HeaderCheckBox on, the first column header carries a checkbox. A click on it
    // is raised as HeaderBoxClicked and kept from the native list view, so GPALListView alone decides which rows it
    // ticks.
    internal class GPALListViewControl : ListView
    {
        private const int WM_NOTIFY              = 0x004E;
        private const int LVM_GETHEADER          = 0x101F;
        private const int HDM_GETITEMW           = 0x120B;
        private const int HDM_SETITEMW           = 0x120C;
        private const int HDN_ITEMSTATEICONCLICK = -316;
        private const int HDI_FORMAT             = 0x0004;
        private const int HDF_CHECKBOX           = 0x0040;
        private const int HDF_CHECKED            = 0x0080;
        private const int HDS_CHECKBOXES         = 0x0400;
        private const int GWL_STYLE              = -16;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NMHDR
        {
            public IntPtr  hwndFrom;
            public UIntPtr idFrom;
            public int     code;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct HDITEM
        {
            public int    mask;
            public int    cxy;
            public IntPtr pszText;
            public IntPtr hbm;
            public int    cchTextMax;
            public int    fmt;
            public IntPtr lParam;
            public int    iImage;
            public int    iOrder;
            public uint   type;
            public IntPtr pvFilter;
            public uint   state;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref HDITEM lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        private IntPtr header = IntPtr.Zero;
        private bool headerChecked = false;

        // set before the form is shown
        internal bool HeaderCheckBox { get; set; } = false;

        internal event EventHandler HeaderBoxClicked;

        // what the header box shows; kept until the header exists
        internal bool HeaderChecked
        {
            get
            {
                return headerChecked;
            }
            set
            {
                headerChecked = value;

                if (IntPtr.Zero != header)
                    PaintHeaderBox();
            }
        }

        // The handle is made again when the view or checkboxes change, so the box is put back each time.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            header = true == HeaderCheckBox ? SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero) : IntPtr.Zero;

            if (IntPtr.Zero != header)
            {
                SetWindowLong(header, GWL_STYLE, GetWindowLong(header, GWL_STYLE) | HDS_CHECKBOXES);
                PaintHeaderBox();
            }
        }

        // puts the checkbox on the first column header, ticked or clear
        private void PaintHeaderBox()
        {
            HDITEM item = new HDITEM { mask = HDI_FORMAT };

            if (0 < Columns.Count && IntPtr.Zero != SendMessage(header, HDM_GETITEMW, IntPtr.Zero, ref item))
            {
                item.fmt = (item.fmt | HDF_CHECKBOX) & ~HDF_CHECKED | (true == headerChecked ? HDF_CHECKED : 0);
                SendMessage(header, HDM_SETITEMW, IntPtr.Zero, ref item);
            }
        }

        protected override void WndProc(ref Message m)
        {
            NMHDR note = WM_NOTIFY == m.Msg && IntPtr.Zero != header
                ? (NMHDR)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(NMHDR))
                : default(NMHDR);

            if (HDN_ITEMSTATEICONCLICK == note.code && header == note.hwndFrom)
                HeaderBoxClicked?.Invoke(this, EventArgs.Empty);
            else
                base.WndProc(ref m);
        }
    }
}
