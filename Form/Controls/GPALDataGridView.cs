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
    /// A grid control instantiated with GPAL.DataGridView for use on GPAL forms, displaying the contents
    /// of an <see cref="IGPALGrid{T}"/> as rows and columns.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALDataGridView gPalDataGridView = (GPALDataGridView)GPAL.DataGridView
    ///     .WithColumns("Name", "Status")
    ///     .WithInput(myGrid)
    ///     .ToGPALObject();
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("Simple Form Test")
    ///     .WithFormControl(gPalDataGridView)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALDataGridView : GPALControl, IAllowDataGridViewControlSettings
    {
        private IGPALGrid<string> _grid;
        private readonly List<string> _columnNames = new List<string>();
        private bool _autoGenerateColumns = false;  // default false; generate only when asked or when no columns are set

        internal GPALDataGridView()
        {
            ControlType = FormControlType.DataGridView;
        }

        /// <summary>
        /// Sets the column header names for the grid. If not specified and
        /// <see cref="WithAutoGenerateColumns(bool)"/> is true, columns are auto-named "Column 1", "Column 2", etc.
        /// </summary>
        /// <param name="columnNames">The column header names, in order.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithColumns(params string[] columnNames)
        {
            if (columnNames != null)
            {
                _columnNames.AddRange(columnNames);
            }
            return this;
        }

        /// <summary>
        /// Sets whether the grid's cells are read-only.
        /// </summary>
        /// <param name="readOnly"><c>true</c> to make cells read-only; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public new IAllowDataGridViewControlSettings WithReadOnly(bool readOnly = true)
        {
            base.ReadOnly = readOnly;
            return this;
        }

        /// <summary>
        /// Sets whether columns should be auto-generated (named "Column 1", "Column 2", etc.) when no
        /// column names were provided via <see cref="WithColumns(string[])"/>.
        /// </summary>
        /// <param name="autoGenerate"><c>true</c> to auto-generate columns; otherwise <c>false</c> [default].</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithAutoGenerateColumns(bool autoGenerate)
        {
            _autoGenerateColumns = autoGenerate;
            return this;
        }

        /// <summary>
        /// Sets the data grid to display and immediately syncs it to the underlying Windows control via
        /// <see cref="SyncGridToControl"/>.
        /// </summary>
        /// <param name="grid">The grid data to display.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(IGPALGrid<string> grid)
        {
            this.CurrentGrid = grid;
            return this;
        }

        /// <summary>
        /// Rebuilds the underlying <see cref="DataGridView"/>'s columns and rows from <see cref="CurrentGrid"/>.
        /// Columns come from <see cref="ColumnNames"/> if set, otherwise auto-generated as "Column N" if
        /// <see cref="AutoGenerateColumns"/> is true. Rows are populated from each row of the grid, and
        /// columns/rows are auto-resized to fit. If the underlying control requires a thread-safe invoke,
        /// this re-invokes itself on the UI thread. No-op if the underlying control is not a
        /// <see cref="DataGridView"/> or no grid has been set.
        /// </summary>


        // Column widths in column order. Empty leaves the control's defaults.
        internal readonly List<int> _columnWidths = new List<int>();

        /// <summary>
        /// Sets column widths in pixels, in column order.
        /// </summary>
        /// <remarks>
        /// Fewer widths than columns leaves the rest as the grid sizes them. A width of zero or less is ignored. With a
        /// width for every column, the last one fills to the edge.
        /// </remarks>
        /// <param name="widths">The widths, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithColumnWidth(params int[] widths)
        {
            _columnWidths.Clear();

            if (null != widths)
                _columnWidths.AddRange(widths);

            ApplyColumnWidths();

            return this;
        }


        // Which columns wrap, in column order. Empty means none, the grid's default.
        internal readonly List<bool> _columnWrap = new List<bool>();

        /// <summary>
        /// Sets which columns wrap their text, in column order.<br/>
        /// No arguments wraps every column; fewer entries than columns leaves the rest unwrapped.
        /// </summary>
        /// <example>
        /// <code>
        ///     .WithWrap(false, true, false)   // the middle column wraps
        ///     .WithWrap()                     // all of them wrap
        /// </code>
        /// </example>
        /// <param name="wrap">True for each column that wraps, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithWrap(params bool[] wrap)
        {
            _columnWrap.Clear();
            wrapAsked = true;

            if (null != wrap)
                _columnWrap.AddRange(wrap);

            ApplyWrap();

            return this;
        }

        /// <summary>
        /// Applies the wrap setting and lets rows grow to fit. Applied before and after the form is built, as the
        /// widths are.
        /// </summary>
        internal void ApplyWrap()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv))
                return;

            if (true == dgv.InvokeRequired)
            {
                OnUi(dgv, ApplyWrap);
                return;
            }

            // No WithWrap call: nothing wraps. A call with no entries: everything wraps.
            bool everyColumn = 0 == _columnWrap.Count && true == wrapAsked;

            for (int column = 0; column < dgv.Columns.Count; column++)
            {
                bool wraps = true == everyColumn
                          || (column < _columnWrap.Count && true == _columnWrap[column]);

                dgv.Columns[column].DefaultCellStyle.WrapMode = true == wraps
                                                              ? DataGridViewTriState.True
                                                              : DataGridViewTriState.False;
            }

            dgv.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);
        }

        // Whether WithWrap was called; no entries means every column wraps.
        private bool wrapAsked = false;

        // what share of the width each column takes. Empty leaves the columns as the control makes them
        internal readonly List<double> _columnShares = new List<double>();

        /// <summary>
        /// Sets each column's share of the width, in column order.
        /// </summary>
        /// <remarks>
        /// Shares need not add up to anything and reflow as the form resizes. Integer arguments pick the pixel
        /// overload.
        /// </remarks>
        /// <example>
        /// <code>
        ///     .WithColumnWidth(0.5, 0.2, 0.3)   // shares
        ///     .WithColumnWidth(200, 80, 120)    // pixels
        /// </code>
        /// </example>
        /// <param name="shares">The shares, in column order</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithColumnWidth(params double[] shares)
        {
            _columnShares.Clear();

            if (null != shares)
                _columnShares.AddRange(shares);

            ApplyColumnWidths();

            return this;
        }

        // Apply before the form is built and again after; an existing control will not be rebuilt.
        internal void ApplyColumnWidths()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv))
                return;

            // Apply shares first; they set the whole control's sizing mode. A later pixel width holds until the next
            // resize.
            if (0 < _columnShares.Count)
            {
                dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

                for (int column = 0; column < _columnShares.Count && column < dgv.Columns.Count; column++)
                    dgv.Columns[column].FillWeight = (float)(0 < _columnShares[column] ? _columnShares[column] : 1);
            }

            if (0 == _columnWidths.Count)
                return;

            if (true == dgv.InvokeRequired)
            {
                OnUi(dgv, ApplyColumnWidths);
                return;
            }

            for (int column = 0; column < _columnWidths.Count && column < dgv.Columns.Count; column++)
                if (0 < _columnWidths[column])
                    dgv.Columns[column].Width = _columnWidths[column];

            FillLastColumn();
        }

        // True when every column has a pixel width; only then is the last column stretched to the edge. Partial widths
        // leave the rest to the control. Shares already reach the edge through Fill.
        internal bool LastColumnFills => 0 < _columnWidths.Count && _columnWidths.Count == (WindowsControl as DataGridView)?.Columns.Count && 0 == _columnShares.Count;

        // Setting a column width raises the resize event this runs from.
        private bool filling = false;

        /// <summary>
        /// Widens the last column to the control's edge.<br/>
        /// Never narrows: the requested width is the minimum, so a too-narrow control gets a scrollbar. The width is
        /// unknown until the handle exists, so this runs on every resize.
        /// </summary>
        internal void FillLastColumn()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv) || false == LastColumnFills)
                return;

            if (true == dgv.InvokeRequired)
            {
                OnUi(dgv, FillLastColumn);
                return;
            }

            int last = dgv.Columns.Count - 1;

            if (0 > last || true == filling)
                return;

            filling = true;

            try
            {
                // A Fill column ignores Width, so filling only applies while the grid sizes by pixels.
                dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;

                if (last < _columnWidths.Count && 0 < _columnWidths[last])
                    dgv.Columns[last].Width = _columnWidths[last];

                int used = 0;

                for (int before = 0; before < last; before++)
                    used += dgv.Columns[before].Width;

                // The grid's scrollbars are inside ClientSize, unlike a list view's. Subtract the vertical one, or a
                // scrolling grid's last column is too wide and adds a horizontal scrollbar.
                int verticalBar = 0;

                foreach (Control child in dgv.Controls)
                    if (child is VScrollBar bar && true == bar.Visible)
                        verticalBar = bar.Width;

                // The row header takes width but is not a column.
                int left = dgv.ClientSize.Width - used - (true == dgv.RowHeadersVisible ? dgv.RowHeadersWidth : 0) - verticalBar - GPALListView.FillMargin;

                if (left > dgv.Columns[last].Width)
                    dgv.Columns[last].Width = left;
            }
            finally
            {
                filling = false;
            }
        }

        /// <summary>
        /// Sets the rows from a DataTable; its column names become the grid's column names.
        /// </summary>
        /// <param name="dataTable">The table to show</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(DataTable dataTable)
        {
            IGPALGrid<string> rows = DatabaseHelper.DataTableToGrid(dataTable, out List<DatabaseSettings.DatabaseColumn> columns);

            // Set the column names first; a column with no name shows as its number.
            if (0 == _columnNames.Count)
                foreach (DatabaseSettings.DatabaseColumn column in columns)
                    _columnNames.Add(column.ColumnName);

            return WithInput(rows);
        }

        /// <summary>
        /// Sets the rows by running the database's query once, here.
        /// </summary>
        /// <remarks>
        /// The query runs once, when this is called; no connection stays open.
        /// </remarks>
        /// <example>
        /// <code>
        ///     GPAL.DataGridView
        ///         .WithInput(GPAL.Database
        ///             .WithServerName(@"HORNET2\DEADMIKE")
        ///             .WithDatabaseName("GPAL")
        ///             .WithDatabaseType(DatabaseType.SQLServer)
        ///             .WithTableName("Orders")
        ///             .ToGPALObject())
        ///         .WithName("orders")
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        /// <param name="database">The database whose query fills the grid</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(IGPALDatabase database)
        {
            DataTable table = null;

            if (null != database)
                database.SaveTo(out table);

            return WithInput(table);
        }

        /// <summary>
        /// Sets the rows from a file, split by its extension or settings, so a tab-delimited file and a spreadsheet
        /// both load.
        /// </summary>
        /// <param name="file">The file to read</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithInput(GPALFile file)
        {
            IGPALGrid<string> rows = GPAL.Grid.ToGPALObject();

            if (null != file)
            {
                FileHelper.TokenizeFile(file);
                List<IGPALGrid<string>> tokens = ((IGPALFileInternal)file).TokenList;

                // A file with several grids is shown one grid after another, in file order.
                if (null != tokens)
                    foreach (IGPALGrid<string> token in tokens)
                        foreach (List<string> row in token)
                            rows.AddRow(new List<string>(row));
            }

            return WithInput(rows);
        }

        // The range GetGrid reads, counted from one. An unset end means to the edge of the control.
        private int _startRow = 0;
        private int _startColumn = 0;
        private int _endRow = 0;
        private int _endColumn = 0;

        /// <summary>
        /// The first row a range covers, counting from one.
        /// </summary>
        /// <param name="row">The row to start at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithStartRow(int row)
        {
            _startRow = row;
            return this;
        }

        /// <summary>
        /// The first column a range covers, counting from one.
        /// </summary>
        /// <param name="column">The column to start at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithStartColumn(int column)
        {
            _startColumn = column;
            return this;
        }

        /// <summary>
        /// The last row a range covers, counting from one.
        /// </summary>
        /// <param name="row">The row to stop at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithEndRow(int row)
        {
            _endRow = row;
            return this;
        }

        /// <summary>
        /// The last column a range covers, counting from one.
        /// </summary>
        /// <param name="column">The column to stop at</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings WithEndColumn(int column)
        {
            _endColumn = column;
            return this;
        }

        /// <summary>
        /// Reads what the grid is showing, including headers and any user edits or sorting.
        /// </summary>
        /// <remarks>
        /// Reads the live control, including user edits and sorting; before the form is built it returns the grid it
        /// was given. Start and end count from one.
        /// </remarks>
        /// <param name="grid">Receives what was read</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowDataGridViewControlSettings GetGrid(out IGPALGrid<string> grid)
        {
            IGPALGrid<string> reading = GPAL.Grid.ToGPALObject();

            if (WindowsControl is DataGridView dgv && true == Alive(dgv))
            {
                if (true == dgv.InvokeRequired)
                    OnUi(dgv, () => ReadInto(dgv, reading));
                else
                    ReadInto(dgv, reading);
            }
            else if (null != _grid)
                foreach (List<string> row in _grid)
                    reading.AddRow(new List<string>(row));

            grid = reading;

            return this;
        }

        /// <summary>
        /// Copies the range out of the live control, one row per row and one cell per column.
        /// </summary>
        /// <param name="dgv">The control being read</param>
        /// <param name="reading">The grid being filled</param>
        private void ReadInto(DataGridView dgv, IGPALGrid<string> reading)
        {
            int firstRow = 1 > _startRow ? 1 : _startRow;
            int lastRow = 1 > _endRow ? dgv.Rows.Count : (_endRow > dgv.Rows.Count ? dgv.Rows.Count : _endRow);
            int firstColumn = 1 > _startColumn ? 1 : _startColumn;
            int lastColumn = 1 > _endColumn ? dgv.Columns.Count : (_endColumn > dgv.Columns.Count ? dgv.Columns.Count : _endColumn);

            // Column names first, so a saved file has headers; only columns in the range.
            if (0 < dgv.Columns.Count)
            {
                List<string> headers = new List<string>();

                for (int column = firstColumn; column <= lastColumn; column++)
                    headers.Add(dgv.Columns[column - 1].HeaderText ?? string.Empty);

                reading.AddRow(headers);
            }

            for (int row = firstRow; row <= lastRow; row++)
            {
                if (true == dgv.Rows[row - 1].IsNewRow)
                    continue;

                List<string> cells = new List<string>();

                for (int column = firstColumn; column <= lastColumn; column++)
                    cells.Add(dgv.Rows[row - 1].Cells[column - 1].Value?.ToString() ?? string.Empty);

                reading.AddRow(cells);
            }
        }

        internal void SyncGridToControl()
        {
            if (!(WindowsControl is DataGridView dgv) || false == Alive(dgv) || _grid == null)
            {
                return;
            }

            if (dgv.InvokeRequired)
            {
                OnUi(dgv, SyncGridToControl);
                return;
            }

            dgv.Rows.Clear();
            dgv.Columns.Clear();

            // Determine column count from first non-null row (defensive)
            int columnCount = 0;
            foreach (List<string> row in _grid)
            {
                if (row != null)
                {
                    columnCount = row.Count;
                    break;
                }
            }

            // Columns
            if (_columnNames.Count > 0)
            {
                int colIndex = 0;
                foreach (string header in _columnNames)
                {
                    if (colIndex >= columnCount) break;
                    dgv.Columns.Add(header, header);
                    colIndex++;
                }
            }
            else if (_autoGenerateColumns && columnCount > 0)
            {
                for (int i = 0; i < columnCount; i++)
                {
                    string header = $"Column {i + 1}";
                    dgv.Columns.Add(header, header);
                }
            }

            // Rows
            foreach (List<string> rowData in _grid)
            {
                if (rowData == null) continue;

                var dgvRow = new DataGridViewRow();
                var cells = new DataGridViewCell[rowData.Count];

                int colIndex = 0;
                foreach (string cellValue in rowData)
                {
                    cells[colIndex] = new DataGridViewTextBoxCell
                    {
                        Value = cellValue ?? ""
                    };
                    colIndex++;
                }

                dgvRow.Cells.AddRange(cells);
                dgv.Rows.Add(dgvRow);
            }

            dgv.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
            dgv.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);

            // Refilling the rows resized every column, so reapply the workflow's widths, as the list view does in
            // RefitSizedColumns.
            ApplyColumnWidths();
            ApplyWrap();
        }

        /// <summary>
        /// The grid data currently displayed, as set via <see cref="WithInput(IGPALGrid{string})"/>.
        /// Setting this refreshes the underlying control via <see cref="SyncGridToControl"/>.
        /// </summary>
        public IGPALGrid<string> CurrentGrid
        {
            get => _grid;
            set
            {
                _grid = value;
                SyncGridToControl();
            }
        }
        /// <summary>
        /// The column header names set via <see cref="WithColumns(string[])"/>.
        /// </summary>
        public IReadOnlyList<string> ColumnNames => _columnNames.AsReadOnly();
        /// <summary>
        /// Whether columns are auto-generated when no column names were provided, as set via
        /// <see cref="WithAutoGenerateColumns(bool)"/>.
        /// </summary>
        public bool AutoGenerateColumns => _autoGenerateColumns;
    }
}
