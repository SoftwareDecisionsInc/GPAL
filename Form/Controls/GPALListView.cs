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
    /// A list view control capable of displaying data in Details, List, LargeIcon, SmallIcon, or Tile views.
    /// Supports columns, item selection, double-click activation and multi-select.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALListView fileResults = GPAL.ListView
    ///     .WithName("SavedFiles")
    ///     .WithColumns("Filename", "Size (KB)", "Saved At")
    ///     .WithView(ListViewMode.Details)
    ///     .WhenSelectedDo(selectedFile => Debug.WriteLine($"Selected: {selectedFile}"))
    ///     .WithToolTip("List of downloaded or processed files")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALListView : GPALControl, IAllowListViewControlSettings
    {
        private readonly List<string> columnHeaders = new List<string>();
        private readonly List<double> columnWeights = new List<double>();
        private ListViewMode viewMode = ListViewMode.Details;
        internal bool checkBoxes = false;
        private bool fullRowSelect = true;
        private bool multiSelect = false;
        private bool sortable = false;
        private bool tickAll = false;
        private bool tickingAll = false;

        internal GPALListView()
        {
            ControlType = FormControlType.ListView;
        }

        /// <summary>
        /// Shows a checkbox on every row. ControlEventType.ItemChecked reports changes.
        /// </summary>
        /// <param name="enable">True to show checkboxes.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithCheckBoxes(bool enable = true)
        {
            CheckBoxes = enable;
            return this;
        }

        /// <summary>
        /// Puts a checkbox on the first column header. Clicking it ticks every row that is not disabled, or clears every
        /// row, and it shows ticked whenever every row that is not disabled is ticked. Turns on row checkboxes too.
        /// </summary>
        /// <param name="enable">True to show the header checkbox.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithTickAll(bool enable = true)
        {
            tickAll = enable;

            if (true == enable)
                checkBoxes = true;

            return this;
        }

        /// <summary>
        /// Whether the first column header carries a tick-all checkbox, as set via <see cref="WithTickAll(bool)"/>.
        /// </summary>
        public bool TickAll => tickAll;

        // Ticks every row that is not disabled, or clears every row, then shows the result on the header box.
        internal void TickAllRows(GPALListViewControl lv)
        {
            bool tick = false == lv.HeaderChecked;

            tickingAll = true;

            foreach (ListViewItem item in lv.Items)
                item.Checked = true == tick && SystemColors.GrayText != item.ForeColor;

            tickingAll = false;

            SyncHeaderBox();
        }

        // The header box is ticked when there is a row that is not disabled and every one of them is ticked. Runs on
        // the UI thread; while the header is ticking every row, it waits for the end.
        internal void SyncHeaderBox()
        {
            if (WindowsControl is GPALListViewControl lv && true == tickAll && false == tickingAll)
            {
                List<ListViewItem> open = lv.Items.Cast<ListViewItem>().Where(item => SystemColors.GrayText != item.ForeColor).ToList();

                lv.HeaderChecked = 0 < open.Count && open.All(item => item.Checked);
            }
        }

        /// <summary>
        /// Whether rows carry a checkbox. Settable while the form is up.
        /// </summary>
        public bool CheckBoxes
        {
            get
            {
                return WindowsControl is ListView lv ? lv.CheckBoxes : checkBoxes;
            }
            set
            {
                checkBoxes = value;

                if (!(WindowsControl is ListView lv) || false == Alive(lv)) return;

                OnUi(lv, () => lv.CheckBoxes = value);
            }
        }

        /// <summary>
        /// Sets the column headers for the list view (used in <see cref="ListViewMode.Details"/> mode).
        /// </summary>
        /// <param name="headers">The column header names, in order.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithColumns(params string[] headers)
        {
            if (headers != null)
            {
                columnHeaders.AddRange(headers);
            }
            return this;
        }

        /// <summary>
        /// Sets how items are displayed (Details, List, LargeIcon, SmallIcon, or Tile). Defaults to <see cref="ListViewMode.Details"/>.
        /// </summary>
        /// <param name="view">The desired view mode.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithView(ListViewMode view)
        {
            viewMode = view;
            return this;
        }

        /// <summary>
        /// Sets whether selecting an item highlights the entire row. Defaults to <c>true</c>.
        /// </summary>
        /// <param name="enable"><c>true</c> to highlight the full row when selected; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithFullRowSelect(bool enable)
        {
            fullRowSelect = enable;
            return this;
        }

        /// <summary>
        /// Sets whether multiple items can be selected at once. Defaults to <c>false</c>.
        /// </summary>
        /// <param name="enable"><c>true</c> to allow multi-select; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithMultiSelect(bool enable)
        {
            multiSelect = enable;
            return this;
        }

        /// <summary>
        /// Enables click-to-sort on all columns. Clicking a column header sorts ascending;
        /// clicking again reverses to descending. Compares numeric, date, then string.
        /// </summary>
        public IAllowListViewControlSettings WithSortable(bool sortable = true)
        {
            this.sortable = sortable;
            return this;
        }

        /// <summary>
        /// Adds a proportional width weight for the next column (in declaration order).
        /// Weights do not need to sum to 1 -- they are normalized automatically.
        /// When any weight is set the ListView resizes columns proportionally on resize.
        /// </summary>
        public IAllowListViewControlSettings WithColumnWeight(double weight)
        {
            columnWeights.Add(weight);
            return this;
        }

        /// <summary>
        /// The column header names set via <see cref="WithColumns(string[])"/>.
        /// </summary>
        public IReadOnlyList<string> Columns => columnHeaders.AsReadOnly();

        /// <summary>
        /// Proportional column weights set via <see cref="WithColumnWeight(double)"/>.
        /// </summary>
        public IReadOnlyList<double> ColumnWeights => columnWeights.AsReadOnly();

        /// <summary>Whether column-click sorting is enabled.</summary>
        public bool Sortable => sortable;
        /// <summary>
        /// The view mode set via <see cref="WithView(ListViewMode)"/>, mapped to the underlying WinForms View.
        /// </summary>
        public View ViewMode
        {
            get
            {
                switch (viewMode)
                {
                    case ListViewMode.List:      return View.List;
                    case ListViewMode.LargeIcon: return View.LargeIcon;
                    case ListViewMode.SmallIcon: return View.SmallIcon;
                    case ListViewMode.Tile:      return View.Tile;
                    default:                     return View.Details;
                }
            }
        }
        /// <summary>
        /// Whether full-row selection is enabled, as set via <see cref="WithFullRowSelect(bool)"/>.
        /// </summary>
        public bool FullRowSelect => fullRowSelect;
        /// <summary>
        /// Whether multi-select is enabled, as set via <see cref="WithMultiSelect(bool)"/>.
        /// </summary>
        public bool MultiSelect => multiSelect;

        public int SelectedIndex
        {
            get
            {
                int retVal = -1;

                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () => 0 < lv.SelectedItems.Count ? lv.SelectedItems[0].Index : -1, retVal);

                return retVal;
            }
        }

        public void ClearItems()
        {
            if (!(WindowsControl is ListView lv) || false == Alive(lv)) return;
            OnUi(lv, () =>
            {
                lv.Items.Clear();
                SyncHeaderBox();
            });
        }

        /// <summary>
        /// Greys a row and makes it unselectable, keeping it in the list. By position, since text can repeat.
        /// </summary>
        /// <param name="index">The 0-based row to disable.</param>
        public void DisableItem([Index] int index)
        {
            SetItemEnabled(index, false);
        }

        /// <summary>
        /// Makes a disabled row selectable again.
        /// </summary>
        /// <param name="index">The 0-based row to enable.</param>
        public void EnableItem([Index] int index)
        {
            SetItemEnabled(index, true);
        }

        /// <summary>
        /// Whether the row at this position can be selected.
        /// </summary>
        /// <param name="index">The 0-based row to ask about.</param>
        /// <returns>True when the row can be selected, and when there is no such row.</returns>
        public bool IsItemEnabled([Index] int index)
        {
            bool retVal = true;

            if (!(WindowsControl is ListView lv) || false == Alive(lv)) return retVal;

            MethodInvoker read = () =>
            {
                if (0 <= index && index < lv.Items.Count)
                    retVal = SystemColors.GrayText != lv.Items[index].ForeColor;
            };

            OnUi(lv, read);

            return retVal;
        }

        // The row's color is its enabled state, so look and behavior always match.
        private void SetItemEnabled(int index, bool enabled)
        {
            if (!(WindowsControl is ListView lv) || false == Alive(lv)) return;

            MethodInvoker paint = () =>
            {
                if (0 > index || index >= lv.Items.Count) return;

                lv.Items[index].ForeColor = true == enabled ? SystemColors.WindowText : SystemColors.GrayText;

                if (false == enabled) lv.Items[index].Selected = false;

                SyncHeaderBox();
            };

            OnUi(lv, paint);
        }

        /// <summary>
        /// Ticks the checkbox on a row. By position, since text can repeat. ControlEventType.ItemChecked reports it
        /// like a click would.
        /// </summary>
        /// <param name="index">The 0-based row to tick.</param>
        public void CheckItem([Index] int index)
        {
            SetItemChecked(index, true);
        }

        /// <summary>
        /// Clears the checkbox on a row.
        /// </summary>
        /// <param name="index">The 0-based row to clear.</param>
        public void UncheckItem([Index] int index)
        {
            SetItemChecked(index, false);
        }

        // Sets the box on the UI thread; a row that does not exist is left alone.
        private void SetItemChecked(int index, bool isChecked)
        {
            if (WindowsControl is ListView lv && true == Alive(lv))
                OnUi(lv, () =>
                {
                    if (0 <= index && index < lv.Items.Count)
                        lv.Items[index].Checked = isChecked;
                });
        }

        public void AddItem(string title, params string[] subItems)
        {
            // Rows added before the list exists are kept until it does.
            if (!(WindowsControl is ListView lv) || false == Alive(lv))
            {
                pendingRows.Enqueue(new Tuple<string, string[]>(title, subItems ?? new string[0]));
                return;
            }

            var item = new ListViewItem(title);
            foreach (var s in subItems) item.SubItems.Add(s);
            OnUi(lv, () =>
            {
                lv.Items.Add(item);
                SyncHeaderBox();
            });
        }

        // what was added before the list existed, in the order it was added
        private readonly System.Collections.Concurrent.ConcurrentQueue<Tuple<string, string[]>> pendingRows =
            new System.Collections.Concurrent.ConcurrentQueue<Tuple<string, string[]>>();

        /// <summary>
        /// The form has built the list, so anything added before now goes in.
        /// </summary>
        internal override void Realized()
        {
            while (true == pendingRows.TryDequeue(out Tuple<string, string[]> held))
                AddItem(held.Item1, held.Item2);

            // A list on an unopened tab has no handle, so earlier fits were refused. Rows can be measured once the tab
            // is first shown.
            if (WindowsControl is ListView lv)
            {
                lv.HandleCreated += (sender, args) => LayOutColumns();
                lv.VisibleChanged += (sender, args) => LayOutColumns();
                lv.Resize += (sender, args) => LayOutColumns();
            }
        }


        /// <summary>
        /// Sets each column's share of the width, in column order.
        /// </summary>
        /// <remarks>
        /// Shares need not add up to anything and reflow as the form resizes. Integer arguments pick the pixel
        /// overload.
        /// </remarks>
        /// <example>
        /// <code>
        ///     .WithColumnWidth(0.2, 0.1, 0.7)   // shares
        ///     .WithColumnWidth(100, 60, -2)     // pixels, -2 fills the rest on the last column
        /// </code>
        /// </example>
        /// <param name="shares">The shares, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithColumnWidth(params double[] shares)
        {
            columnWeights.Clear();

            if (null != shares)
                columnWeights.AddRange(shares);

            return this;
        }

        // how wide each column is, in column order. Empty leaves every column as the control makes it
        internal readonly List<int> _columnWidths = new List<int>();

        /// <summary>
        /// Sets column widths in pixels, in column order; fewer widths than columns leaves the rest alone. -1 sizes a
        /// column to its widest content and -2 to the wider of that and its header, recalculated as rows are added or
        /// cleared.
        /// </summary>
        /// <param name="widths">The widths, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowListViewControlSettings WithColumnWidth(params int[] widths)
        {
            _columnWidths.Clear();

            if (null != widths)
                _columnWidths.AddRange(widths);

            ApplyColumnWidths();

            return this;
        }

        // Whether the workflow set any widths. With shares, the form reflows on resize and this does nothing.
        internal bool LastColumnFills => 0 == columnWeights.Count;

        // Margin after the last column, so narrowing the form never adds a horizontal scrollbar.
        internal const int FillMargin = 2;

        // narrow enough to be a column, wide enough to be clicked on
        private const int NarrowestColumn = 24;

        // Setting a column's width raises the resize event this runs from.
        private bool layingOut = false;

        /// <summary>
        /// Sets all column widths at once.<br/>
        /// An unset column gets an equal share of what the set widths leave.
        /// </summary>
        /// <remarks>
        /// Runs on handle creation, show and resize, not when rows are added.
        /// </remarks>
        internal void LayOutColumns()
        {
            if (!(WindowsControl is ListView lv) || false == Alive(lv) || 0 < columnWeights.Count)
                return;

            if (true == lv.InvokeRequired)
            {
                OnUi(lv, LayOutColumns);
                return;
            }

            if (false == lv.IsHandleCreated || 0 == lv.Columns.Count || true == layingOut)
                return;

            layingOut = true;

            try
            {
                int available = Math.Max(NarrowestColumn * lv.Columns.Count, lv.ClientSize.Width - FillMargin);
                int[] widths = new int[lv.Columns.Count];
                int named = 0;
                int unnamed = 0;

                // A set width is positive. A sentinel, zero or unset column shares the remainder.
                for (int column = 0; column < widths.Length; column++)
                    if (column < _columnWidths.Count && 0 < _columnWidths[column])
                    {
                        widths[column] = _columnWidths[column];
                        named += widths[column];
                    }
                    else
                        unnamed++;

                if (0 < unnamed)
                {
                    int each = Math.Max(NarrowestColumn, (available - named) / unnamed);

                    for (int column = 0; column < widths.Length; column++)
                        if (0 == widths[column])
                            widths[column] = each;
                }

                // Too wide: scale all columns down together.
                int total = 0;

                foreach (int width in widths)
                    total += width;

                if (available < total && 0 < total)
                {
                    for (int column = 0; column < widths.Length; column++)
                        widths[column] = Math.Max(NarrowestColumn, widths[column] * available / total);

                    total = 0;

                    foreach (int width in widths)
                        total += width;
                }

                // the last column ends the row at the edge
                widths[widths.Length - 1] += available - total;

                if (NarrowestColumn > widths[widths.Length - 1])
                    widths[widths.Length - 1] = NarrowestColumn;

                for (int column = 0; column < widths.Length; column++)
                    lv.Columns[column].Width = widths[column];
            }
            finally
            {
                layingOut = false;
            }
        }

        // Apply before the form is built and again after; an existing control will not be rebuilt.
        internal void ApplyColumnWidths()
        {
            LayOutColumns();
        }

        /// <summary>
        /// The text of the first selected item, or <c>null</c> if nothing is selected.
        /// </summary>
        public string SelectedText
        {
            get
            {
                string retVal = null;

                // Read on the UI thread with a guard, like SelectedIndex. Directly from a workflow thread it could
                // throw or be half-updated.
                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () => 0 < lv.SelectedItems.Count ? lv.SelectedItems[0].Text : null, retVal);

                return retVal;
            }
        }

        /// <summary>
        /// Which rows are checked, one bool per row, in the current row order: added order unless .WithSortable and a
        /// header click reordered them.<br/>
        /// A workflow can walk its own data by the same index. Empty when the list has no rows or is not on a form.
        /// </summary>
        /// <returns>One bool per row, true where the box is checked</returns>
        public IReadOnlyList<bool> CheckedItems
        {
            get
            {
                List<bool> retVal = new List<bool>();

                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () =>
                    {
                        List<bool> ticked = new List<bool>();

                        foreach (ListViewItem item in lv.Items)
                            ticked.Add(item.Checked);

                        return ticked;
                    }, retVal);

                return retVal.AsReadOnly();
            }
        }

        /// <summary>
        /// Which rows are selected, one bool per row, in the current row order.<br/>
        /// Selected is not checked; see <see cref="CheckedItems"/>. Use after .WithMultiSelect, since SelectedIndex
        /// only returns the first.
        /// </summary>
        /// <returns>One bool per row, true where the row is selected</returns>
        public IReadOnlyList<bool> SelectedItems
        {
            get
            {
                List<bool> retVal = new List<bool>();

                if (WindowsControl is ListView lv && true == Alive(lv))
                    retVal = OnUiRead(lv, () =>
                    {
                        List<bool> chosen = new List<bool>();

                        foreach (ListViewItem item in lv.Items)
                            chosen.Add(item.Selected);

                        return chosen;
                    }, retVal);

                return retVal.AsReadOnly();
            }
        }
    }
}
