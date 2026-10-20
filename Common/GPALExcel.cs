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

using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Provides fluent, file-based automation of Excel workbooks: opening one or more workbooks
    /// (including wildcard-matched sets of files), selecting a sheet, reading/writing ranges, columns,
    /// rows and individual cells, performing simple aggregate calculations (sum/count), comparing ranges
    /// against a grid, another file, or another range, and saving results back to a file, grid, or sheet.
    /// All operations apply to every workbook currently loaded via <see cref="WithFile(GPALFile)"/>, so a
    /// single chain can drive many files at once. Errors are reported via <see cref="GPAL.PublishSimpleEvent"/>
    /// rather than thrown.
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Excel
    ///     .WithFile("data.xlsx")
    ///     .WithSheet("Sheet1")
    ///     .WithRange("A1:B10")
    ///     .SaveTo(myGrid);
    /// </code>
    /// </example>
    public class GPALExcel : IGPALExcel
    {
        private GPALFile _gpalFile;
        private List<string> _filePaths;
        private Dictionary<string, XLWorkbook> _workbooks;
        private string _currentSheetName;
        private int _currentSheetIndex { get; set; }
        List<ExcelHelper.CellDifference> _differences;
        private int? _insertPosition;
        private string _insertSeparator;
        private string _replaceValue;
        private string _currentCell;
        private bool _operationPerformed;
        private bool _isClosed;
        private List<(string Sheet, string Range, IGPALGrid<string> Data)> _rangesData;

        /// <summary>
        /// Creates a new <see cref="GPALExcel"/> instance with no workbooks loaded and default
        /// insert settings (insert position 0, separator " ").
        /// </summary>
        internal GPALExcel()
        {
            _filePaths = new List<string>();
            _workbooks = new Dictionary<string, XLWorkbook>();
            _rangesData = new List<(string Sheet, string Range, IGPALGrid<string> Data)>();
            _differences = new List<ExcelHelper.CellDifference>();
            _insertPosition = 0;
            _insertSeparator = " ";
            _operationPerformed = false;
        }

        /// <summary>
        /// If this instance was previously closed via <see cref="Close(bool)"/>, clears all cached state
        /// (loaded workbooks, file paths, range data, comparison differences, current sheet/cell/insert
        /// settings) and reloads the workbooks from <see cref="_gpalFile"/> via <see cref="WithFile(GPALFile)"/>,
        /// allowing a closed instance to be reused. Called at the start of every operation that requires
        /// the workbooks to be open.
        /// </summary>
        private void ReopenIfClosed()
        {
            if (_isClosed)
            {
                _workbooks.Clear();
                _filePaths.Clear();
                _rangesData.Clear();
                _differences.Clear();
                _currentSheetName = null;
                _currentSheetIndex = -1;
                _insertPosition = 0;
                _insertSeparator = " ";
                _currentCell = null;
                _isClosed = false;

                WithFile(_gpalFile);

                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $"Reopening already closed file [{_gpalFile?.Filenames?.FirstOrDefault() ?? "unknown"}]",
                    this,
                    GPALObjectType.Other);
            }
        }

        /// <summary>
        /// Opens the workbook at <paramref name="path"/> with ClosedXML and caches it in
        /// <see cref="_workbooks"/>, keyed by path. If a workbook for that path is already loaded,
        /// this is a no-op.
        /// </summary>
        /// <param name="path">Full path to the .xlsx workbook to load.</param>
        private void LoadFile(string path)
        {
            if (!_workbooks.ContainsKey(path))
            {
                try
                {
                    _workbooks[path] = new XLWorkbook(path);
                    GPAL.PublishSimpleEvent(
                        GPALEventType.INFO,
                        $"Loaded workbook [{path}]",
                        this,
                        GPALObjectType.GPALExcel);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to load workbook [{path}]",
                        this,
                        GPALObjectType.GPALExcel, ex);
                }
            }
        }

        /// <summary>
        /// Returns this instance as an <see cref="IGPALExcel"/>, completing the fluent configuration chain.
        /// </summary>
        public IGPALExcel ToGPALObject() => this;

        /// <summary>
        /// Resets this instance and loads the workbook(s) matching <paramref name="gpalFile"/>'s filenames
        /// (which may include wildcard patterns), opening each matched file with ClosedXML. Replaces any
        /// previously loaded workbooks, range data, and comparison results.
        /// </summary>
        /// <param name="gpalFile">The file (or wildcard pattern) identifying the workbook(s) to load.</param>
        /// <returns>Fluent interface to select a sheet for further Excel operations.</returns>
        public IAllowExcelSheetSelection WithFile(GPALFile gpalFile)
        {
            if (gpalFile == null)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "GPALFile cannot be null",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                _gpalFile = gpalFile;
                _filePaths.Clear();
                _workbooks.Clear();
                _rangesData.Clear();
                _differences.Clear();
                _currentSheetName = null;
                _currentSheetIndex = -1;
                _insertPosition = 0;
                _insertSeparator = " ";
                _currentCell = null;
                _isClosed = false;
                _operationPerformed = false;

                foreach (var pattern in gpalFile.Filenames)
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(pattern) ?? Directory.GetCurrentDirectory();
                        string fileName = Path.GetFileName(pattern);
                        var matchingFiles = Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly);
                        _filePaths.AddRange(matchingFiles);
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to resolve wildcard pattern [{pattern}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }

                _filePaths = _filePaths.Distinct().ToList();
                foreach (var path in _filePaths)
                {
                    LoadFile(path);
                }
            }

            return this;
        }

        /// <summary>
        /// Creates a new workbook with one empty sheet, to be saved as <paramref name="gpalFile"/>. Nothing is written
        /// until <see cref="SaveTo(GPALFile)"/> or <see cref="Close(bool)"/> with true.
        /// </summary>
        /// <param name="gpalFile">The file name to save the workbook as.</param>
        /// <returns>Fluent interface to pick or add a sheet.</returns>
        public IAllowExcelSheetSelection WithNewFile(GPALFile gpalFile)
        {
            if (gpalFile == null)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "GPALFile cannot be null",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                string path = gpalFile.Last ?? gpalFile.Filename;

                _gpalFile = gpalFile;
                _filePaths = new List<string> { path };
                _workbooks.Clear();
                _rangesData.Clear();
                _differences.Clear();
                _currentSheetName = null;
                _currentSheetIndex = -1;
                _insertPosition = 0;
                _insertSeparator = " ";
                _currentCell = null;
                _isClosed = false;
                _operationPerformed = false;

                XLWorkbook made = new XLWorkbook();

                made.Worksheets.Add("Sheet1");

                _workbooks[path] = made;
                _currentSheetIndex = 1;

                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $"Started workbook [{path}] with one sheet",
                    this,
                    GPALObjectType.GPALExcel);
            }

            return this;
        }

        /// <summary>
        /// Adds a sheet named <paramref name="sheetName"/> to every loaded workbook and works on it from here.
        /// A workbook already holding a sheet of that name keeps the one it has.
        /// </summary>
        /// <param name="sheetName">The name for the new sheet.</param>
        /// <returns>Fluent interface to perform Excel operations on the new sheet.</returns>
        public IAllowExcelOperations WithNewSheet(string sheetName)
        {
            ReopenIfClosed();

            if (true == string.IsNullOrWhiteSpace(sheetName))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Sheet name cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else if (!_workbooks.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "No workbooks loaded",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                    if (false == kvp.Value.TryGetWorksheet(sheetName, out IXLWorksheet _))
                        kvp.Value.Worksheets.Add(sheetName);

                _currentSheetName = sheetName;
                _currentSheetIndex = -1;
                _currentCell = null;
                _rangesData.Clear();
            }

            return this;
        }

        /// <summary>
        /// Writes <paramref name="grid"/> into the current sheet at the <see cref="WithCell(string)"/> cell, or A1.
        /// </summary>
        /// <param name="grid">The rows to write.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations InputFrom(IGPALGrid<string> grid)
        {
            ReopenIfClosed();

            _operationPerformed = true;

            if (null == grid || 0 == grid.Rows)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "No rows to write into the sheet",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else if (!_workbooks.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "No workbooks loaded",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                string startCell = _currentCell ?? "A1";

                foreach (var kvp in _workbooks)
                {
                    IXLWorksheet worksheet = GetWorksheet(kvp.Value);

                    if (null == worksheet)
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"No sheet selected in [{kvp.Key}], so there is nowhere to write the rows",
                            this,
                            GPALObjectType.GPALExcel);
                    else
                    {
                        IXLCell anchorCell = worksheet.Cell(startCell);
                        int firstRow = anchorCell.Address.RowNumber;
                        int firstColumn = anchorCell.Address.ColumnNumber;
                        int rowOffset = 0;

                        foreach (List<string> row in grid)
                        {
                            for (int column = 0; column < row.Count; column++)
                                worksheet.Cell(firstRow + rowOffset, firstColumn + column).Value = row[column];

                            rowOffset++;
                        }

                        GPAL.PublishSimpleEvent(
                            GPALEventType.INFO,
                            $"Wrote [{grid.Rows}] rows into sheet [{worksheet.Name}] of [{kvp.Key}] from [{startCell}]",
                            this,
                            GPALObjectType.GPALExcel);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Selects the sheet at the given 1-based index for subsequent Excel operations on every loaded workbook.
        /// </summary>
        /// <param name="sheetIndex">The 1-based index of the sheet to select.</param>
        /// <returns>Fluent interface to perform Excel operations on the selected sheet.</returns>
        public IAllowExcelOperations WithSheet(int sheetIndex)
        {
            ReopenIfClosed();

            if (!_workbooks.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "No workbooks loaded",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {

                int indexErrors = 0;

                foreach (var kvp in _workbooks)
                    if (1 > sheetIndex || sheetIndex > kvp.Value.Worksheets.Count)
                        indexErrors += 1;

                if (indexErrors == _workbooks.Count)
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Invalid sheet index. No workbook contains sheet [{sheetIndex}]",
                        this,
                        GPALObjectType.GPALExcel);
                else
                {
                    _currentSheetName = null;
                    _currentSheetIndex = sheetIndex;
                }
            }

            return this;
        }

        /// <summary>
        /// Selects the sheet with the given name (case-insensitive) for subsequent Excel operations on every loaded workbook.
        /// </summary>
        /// <param name="sheetName">The name of the sheet to select.</param>
        /// <returns>Fluent interface to perform Excel operations on the selected sheet.</returns>
        public IAllowExcelOperations WithSheet(string sheetName)
        {
            ReopenIfClosed();

            if (string.IsNullOrEmpty(sheetName))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Sheet name cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                int nameErrors = 0;

                foreach (var kvp in _workbooks)
                {
                    if (!kvp.Value.Worksheets.Any(ws => ws.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase)))
                        nameErrors += 1;
                }

                if (nameErrors == _workbooks.Count)
                    GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Sheet [{sheetName}] not found in any workbooks",
                    this,
                    GPALObjectType.GPALExcel);
                else
                {
                    _currentSheetName = sheetName;
                    _currentSheetIndex = -1;
                }
            }

            return this;
        }
        /// <summary>
        /// Reads the given cell range from the currently selected sheet of every loaded workbook and stores
        /// the data for use by subsequent operations (e.g. <see cref="SaveTo(ref IGPALGrid{string})"/>,
        /// <see cref="WriteRange(string)"/>, comparisons). If a previous read/write operation has already
        /// been performed on this instance, prior range data and comparison differences are cleared first.
        /// </summary>
        /// <param name="range">The cell range to read (e.g. "A1:B10").</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations WithRange(string range)
        {
            ReopenIfClosed();

            if (string.IsNullOrEmpty(range))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Range cannot be empty",
                    this,
                    GPALObjectType.Other);
            }
            else
            {
                if (true == _operationPerformed)
                {
                    _rangesData.Clear();
                    _operationPerformed = false;
                    _differences.Clear();
                }

                foreach (var kvp in _workbooks)
                {
                    var worksheet = GetWorksheet(kvp.Value);
                    try
                    {
                        if (worksheet != null)
                        {
                            var rangeData = ExcelHelper.ReadRange(worksheet, range);
                            _rangesData.Add((_currentSheetName ?? _currentSheetIndex.ToString() ?? "Sheet1", range, rangeData));
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot read range [{range}] from [{worksheet?.Name ?? "unknown"}]: Invalid sheet",
                                this,
                                GPALObjectType.Other);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to read range [{range}] from [{worksheet?.Name ?? "unknown"}]",
                            this,
                            GPALObjectType.Other, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Writes the range data previously read via <see cref="WithRange(string)"/> (or another
        /// data-producing operation) to <paramref name="range"/> on the currently selected sheet of
        /// every loaded workbook.
        /// </summary>
        /// <param name="range">The destination cell range to write to (e.g. "A1:B10").</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations WriteRange(string range)
        {
            ReopenIfClosed();

            _operationPerformed = true;

            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"No data to write for range [{range}]",
                    this,
                    GPALObjectType.Other);
            }
            else if (string.IsNullOrEmpty(range))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Range cannot be empty",
                    this,
                    GPALObjectType.Other);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            foreach (var rangeData in _rangesData)
                            {
                                ExcelHelper.WriteRange(worksheet, range, rangeData.Data);
                            }
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot write range [{range}] to [{kvp.Key}]: Invalid sheet",
                                this,
                                GPALObjectType.Other);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to write range [{range}] to [{kvp.Key}]",
                            this,
                            GPALObjectType.Other, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Clears any range data read via <see cref="WithRange(string)"/>, <see cref="WithColumn(string)"/>,
        /// or <see cref="WithRowNumber(int)"/>, along with any comparison differences accumulated by the
        /// CompareTo* methods.
        /// </summary>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations ClearRanges()
        {
            ReopenIfClosed();
            _rangesData.Clear();
            _differences.Clear();
            GPAL.PublishSimpleEvent(
                GPALEventType.INFO,
                "Cleared range data and comparison differences",
                this,
                GPALObjectType.GPALExcel);

            return this;
        }
        /// <summary>
        /// Selects a single cell address (e.g. "B3") on the currently selected sheet as the target for
        /// subsequent cell operations such as <see cref="SetValue(string)"/>, <see cref="InsertValue(string)"/>,
        /// <see cref="AppendValue(string)"/>, and <see cref="PrependValue(string)"/>.
        /// </summary>
        /// <param name="cell">The cell address to target.</param>
        /// <returns>Fluent interface to define cell-related settings or perform Excel operations.</returns>
        public IAllowExcelCellSettings WithCell(string cell)
        {
            ReopenIfClosed();

            if (string.IsNullOrEmpty(cell))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Cell address cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {

                _currentCell = cell;
            }

            return this;
        }

        /// <summary>
        /// Sets the value to search for in subsequent <see cref="ReplaceWith(string)"/> operations.
        /// </summary>
        /// <param name="value">The value to search for.</param>
        /// <returns>Fluent interface to define search-and-replace settings or perform Excel operations.</returns>
        public IAllowExcelSearchAndReplaceSettings WithSearchValue(string value)
        {
            ReopenIfClosed();

            if (string.IsNullOrEmpty(value))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Search value cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
                _replaceValue = value;

            return this;
        }

        /// <summary>
        /// Reads every value in the given column from the currently selected sheet of the first loaded
        /// workbook that has the selected sheet, storing the result (one value per row) for use by
        /// subsequent operations. If a previous read/write operation has already been performed on this
        /// instance, prior range data and comparison differences are cleared first.
        /// </summary>
        /// <param name="column">The column letter(s) to read (e.g. "A").</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations WithColumn(string column)
        {
            ReopenIfClosed();

            if (true == _operationPerformed)
            {
                _rangesData.Clear();
                _operationPerformed = false;
                _differences.Clear();
            }

            if (string.IsNullOrEmpty(column))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Column cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            var values = ExcelHelper.GetColumnValues(worksheet, column);
                            var grid = GPAL.GridForType<string>();
                            foreach (var value in values)
                                grid.AddRow(new List<string> { value });
                            _rangesData.Add((_currentSheetName ?? _currentSheetIndex.ToString() ?? "Sheet1", column, grid));
                            break;
                        }
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Cannot read column [{column}] from [{kvp.Key}]: Invalid sheet",
                            this,
                            GPALObjectType.GPALExcel);
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to read column [{column}] from [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Reads every value in the given row from the currently selected sheet of the first loaded
        /// workbook that has the selected sheet, storing the result (a single row of values) for use by
        /// subsequent operations. If a previous read/write operation has already been performed on this
        /// instance, prior range data and comparison differences are cleared first.
        /// </summary>
        /// <param name="rowNumber">The 1-based row number to read.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations WithRowNumber(int rowNumber)
        {
            ReopenIfClosed();

            if (true == _operationPerformed)
            {
                _rangesData.Clear();
                _operationPerformed = false;
                _differences.Clear();
            }

            if (rowNumber < 1)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Invalid row number [{rowNumber}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            var values = ExcelHelper.GetRowValues(worksheet, rowNumber);
                            var grid = GPAL.GridForType<string>();
                            grid.AddRow(values.ToList());
                            _rangesData.Add((_currentSheetName ?? _currentSheetIndex.ToString() ?? "Sheet1", $"Row{rowNumber}", grid));
                            break;
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot read row [{rowNumber}] from [{kvp.Key}]: Invalid sheet",
                                this,
                                GPALObjectType.GPALExcel);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to read row [{rowNumber}] from [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Sums every numeric value across all previously read range data (non-numeric values are ignored).
        /// </summary>
        /// <param name="result">The sum as a string, or "0" if no range data is available or the sum fails.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations CalculateSum(out string result)
        {
            result = "0";
            ReopenIfClosed();

            _operationPerformed = true;

            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "No data to sum",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                try
                {
                    var sum = _rangesData
                        .SelectMany(rd => rd.Data.SelectMany(row => row))
                        .Where(v => double.TryParse(v, out _))
                        .Sum(v => double.Parse(v));
                    result = sum.ToString();
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to calculate sum",
                        this,
                        GPALObjectType.GPALExcel, ex);
                }
            }

            return this;
        }
        /// <summary>
        /// Counts every non-empty value across all previously read range data.
        /// </summary>
        /// <param name="result">The count as a string, or "0" if no range data is available or the count fails.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations CalculateCount(out string result)
        {
            result = "0";
            ReopenIfClosed();

            _operationPerformed = true;

            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "No data to count",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                try
                {
                    var count = _rangesData
                        .SelectMany(rd => rd.Data.SelectMany(row => row))
                        .Count(v => !string.IsNullOrEmpty(v));
                    result = count.ToString();
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to calculate count",
                        this,
                        GPALObjectType.GPALExcel, ex);
                }
            }

            return this;
        }
        /// <summary>
        /// Compares each previously read range against <paramref name="grid"/>, recording any cell-level
        /// differences for retrieval via <see cref="GetCompareResults(out IGPALGrid{string})"/>.
        /// </summary>
        /// <param name="grid">The grid to compare each read range against.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations CompareToGrid(IGPALGrid<string> grid)
        {
            ReopenIfClosed();
            _operationPerformed = true;

            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                "No range defined for comparison",
                this,
                GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var data2 = grid;
                        foreach (var rangeData in _rangesData)
                        {
                            try
                            {
                                var ws1 = GetWorksheet(kvp.Value, rangeData.Sheet);
                                if (ws1 == null)
                                {
                                    GPAL.PublishSimpleEvent(
                                    GPALEventType.ERROR,
                                    $"Invalid sheet for comparison in [{kvp.Key}]: [{rangeData.Sheet}]",
                                    this,
                                    GPALObjectType.GPALExcel);
                                    continue;
                                }
                                var data1 = ExcelHelper.ReadRange(ws1, rangeData.Range);
                                var differences = ExcelHelper.CompareRanges(data1, data2);
                                foreach (var diff in differences)
                                {
                                    _differences.Add(new ExcelHelper.CellDifference
                                    {
                                        CellAddress = diff.CellAddress,
                                        OldValue = diff.OldValue,
                                        NewValue = diff.NewValue,
                                        SourceRange = rangeData.Range,
                                        TargetRange = "Grid"
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Failed to compare range [{rangeData.Range}] in [{kvp.Key}]",
                                this,
                                GPALObjectType.GPALExcel, ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to compare with grid in [{kvp.Key}]",
                        this,
                        GPALObjectType.GPALExcel, ex);
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Compares each previously read range against the same range/sheet in the workbook(s) matching
        /// <paramref name="gpalFile"/>'s filenames (which may include wildcard patterns), recording any
        /// cell-level differences for retrieval via <see cref="GetCompareResults(out IGPALGrid{string})"/>.
        /// </summary>
        /// <param name="gpalFile">The file (or wildcard pattern) identifying the workbook(s) to compare against.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations CompareToFile(GPALFile gpalFile)
        {
            ReopenIfClosed();
            _operationPerformed = true;

            if (gpalFile == null)
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                "Comparison file cannot be null",
                this,
                GPALObjectType.GPALExcel);
            }
            else if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                "No range defined for comparison",
                this,
                GPALObjectType.GPALExcel);
            }
            else
            {
                try
                {
                    var comparePaths = new List<string>();
                    foreach (var pattern in gpalFile.Filenames)
                    {
                        try
                        {
                            string directory = Path.GetDirectoryName(pattern) ?? Directory.GetCurrentDirectory();
                            string fileName = Path.GetFileName(pattern);
                            var matchingFiles = Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly);
                            comparePaths.AddRange(matchingFiles);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to resolve wildcard pattern [{pattern}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                        }
                    }
                    comparePaths = comparePaths.Distinct().ToList();

                    foreach (var kvp in _workbooks)
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet == null)
                        {
                            GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Invalid sheet in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel);
                            continue;
                        }
                        foreach (var comparePath in comparePaths)
                        {
                            try
                            {
                                using var compareWorkbook = new XLWorkbook(comparePath);
                                // Look up by name only when one was set; otherwise use the index.
                                var compareWorksheet = true == string.IsNullOrEmpty(_currentSheetName)
                                    ? GetWorksheet(compareWorkbook, _currentSheetIndex)
                                    : GetWorksheet(compareWorkbook, _currentSheetName) ?? GetWorksheet(compareWorkbook, _currentSheetIndex);
                                if (compareWorksheet == null)
                                {
                                    GPAL.PublishSimpleEvent(
                                    GPALEventType.ERROR,
                                    $"Sheet [{_currentSheetName ?? _currentSheetIndex.ToString()}] not found in [{comparePath}]",
                                    this,
                                    GPALObjectType.GPALExcel);
                                    continue;
                                }
                                foreach (var rangeData in _rangesData)
                                {
                                    try
                                    {
                                        var data1 = ExcelHelper.ReadRange(worksheet, rangeData.Range);
                                        var data2 = ExcelHelper.ReadRange(compareWorksheet, rangeData.Range);
                                        var differences = ExcelHelper.CompareRanges(data1, data2);
                                        foreach (var diff in differences)
                                        {
                                            _differences.Add(new ExcelHelper.CellDifference
                                            {
                                                CellAddress = diff.CellAddress,
                                                OldValue = diff.OldValue,
                                                NewValue = diff.NewValue,
                                                SourceRange = rangeData.Range,
                                                TargetRange = $"{comparePath}:{rangeData.Range}"
                                            });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        GPAL.PublishSimpleEvent(
                                        GPALEventType.ERROR,
                                        $"Failed to compare range [{rangeData.Range}] in [{kvp.Key}] with [{comparePath}]",
                                        this,
                                        GPALObjectType.GPALExcel, ex);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Failed to compare [{kvp.Key}] with [{comparePath}]",
                                this,
                                GPALObjectType.GPALExcel, ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(
                    GPALEventType.EXCEPTION,
                    $"Failed to compare with files [{string.Join(", ", gpalFile.Filenames)}]",
                    this,
                    GPALObjectType.GPALExcel, ex);
                }
            }
            return this;
        }
        /// <summary>
        /// Compares each previously read range against <paramref name="range"/> on the currently selected
        /// sheet of every loaded workbook, recording any cell-level differences for retrieval via
        /// <see cref="GetCompareResults(out IGPALGrid{string})"/>.
        /// </summary>
        /// <param name="range">The cell range to compare each previously read range against (e.g. "A1:B10").</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations CompareToRange(string range)
        {
            ReopenIfClosed();
            _operationPerformed = true;

            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                "No initial range defined for comparison",
                this,
                GPALObjectType.GPALExcel);
            }
            else if (string.IsNullOrEmpty(range))
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                "Comparison range cannot be empty",
                this,
                GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var ws2 = GetWorksheet(kvp.Value);
                        if (ws2 == null)
                        {
                            GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Invalid sheet for comparison in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel);
                            continue;
                        }
                        var data2 = ExcelHelper.ReadRange(ws2, range);
                        foreach (var rangeData in _rangesData)
                        {
                            try
                            {
                                var ws1 = GetWorksheet(kvp.Value, rangeData.Sheet);
                                if (ws1 == null)
                                {
                                    GPAL.PublishSimpleEvent(
                                    GPALEventType.ERROR,
                                    $"Invalid sheet for comparison in [{kvp.Key}]: [{rangeData.Sheet}]",
                                    this,
                                    GPALObjectType.GPALExcel);
                                    continue;
                                }
                                var data1 = ExcelHelper.ReadRange(ws1, rangeData.Range);
                                var differences = ExcelHelper.CompareRanges(data1, data2);
                                foreach (var diff in differences)
                                {
                                    _differences.Add(new ExcelHelper.CellDifference
                                    {
                                        CellAddress = diff.CellAddress,
                                        OldValue = diff.OldValue,
                                        NewValue = diff.NewValue,
                                        SourceRange = rangeData.Range,
                                        TargetRange = range
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Failed to compare range [{rangeData.Range}] in [{kvp.Key}]",
                                this,
                                GPALObjectType.GPALExcel, ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to compare ranges in [{kvp.Key}]",
                        this,
                        GPALObjectType.GPALExcel, ex);
                    }
                }
            }
            return this;
        }
        /// <summary>
        /// Returns the cell-level differences accumulated by the CompareTo* methods
        /// (<see cref="CompareToGrid(IGPALGrid{string})"/>, <see cref="CompareToFile(GPALFile)"/>,
        /// <see cref="CompareToRange(string)"/>) as a grid with columns:
        /// File, Sheet, Cell, SourceRange, TargetRange, OldValue, NewValue.
        /// </summary>
        /// <param name="results">The comparison results, with a header row even if no differences were found.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations GetCompareResults(out IGPALGrid<string> results)
        {
            ReopenIfClosed();

            results = GPAL.GridForType<string>();
            results.AddRow(new List<string> { "File", "Sheet", "Cell", "SourceRange", "TargetRange", "OldValue", "NewValue" });

            try
            {
                if (_differences.Any())
                {
                    foreach (var diff in _differences)
                    {
                        results.AddRow(new List<string>
                        {
                            _filePaths.FirstOrDefault() ?? "unknown",
                            _currentSheetName ?? _currentSheetIndex.ToString(),   // the sheet's name, else its index
                            diff.CellAddress,
                            diff.SourceRange,
                            diff.TargetRange,
                            diff.OldValue,
                            diff.NewValue
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Failed to get results",
                    this,
                    GPALObjectType.GPALExcel, ex);
            }

            return this;
        }
        /// <summary>
        /// Sets where <see cref="InsertValue(string)"/> places its value relative to the current cell
        /// content: 0 inserts before the existing content, any other value inserts after.
        /// </summary>
        /// <param name="position">The insert position (0 = before existing content, otherwise after).</param>
        /// <returns>Fluent interface to define cell-related settings or perform Excel operations.</returns>
        public IAllowExcelCellSettings WithInsertPosition(int position)
        {
            ReopenIfClosed();

            if (position < 0)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Invalid insert position [{position}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
                _insertPosition = position;

            return this;
        }

        /// <summary>
        /// Sets the separator that <see cref="InsertValue(string)"/> places between the inserted value
        /// and the cell's existing content. Defaults to a single space.
        /// </summary>
        /// <param name="separator">The separator string to use.</param>
        /// <returns>Fluent interface to define cell-related settings or perform Excel operations.</returns>
        public IAllowExcelCellSettings WithInsertSeparator(string separator)
        {
            ReopenIfClosed();

            if (string.IsNullOrEmpty(separator))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Insert separator cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
                _insertSeparator = separator;

            return this;
        }

        /// <summary>
        /// Inserts <paramref name="value"/> into the cell selected via <see cref="WithCell(string)"/> on
        /// every loaded workbook, combined with the cell's existing content using the separator from
        /// <see cref="WithInsertSeparator(string)"/> and positioned according to <see cref="WithInsertPosition(int)"/>.
        /// Requires <see cref="WithCell(string)"/>, <see cref="WithInsertPosition(int)"/>, and
        /// <see cref="WithInsertSeparator(string)"/> to have been called first.
        /// </summary>
        /// <param name="value">The value to insert.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations InsertValue(string value)
        {
            ReopenIfClosed();

            _operationPerformed = true;

            if (_insertPosition == null || _insertSeparator == null || string.IsNullOrEmpty(_currentCell))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Missing insert position, separator, or cell for value [{value}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {

                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            var cell = worksheet.Cell(_currentCell);
                            var currentValue = cell.GetString();
                            var newValue = _insertPosition == 0 ? $"{value}{_insertSeparator}{currentValue}" : $"{currentValue}{_insertSeparator}{value}";
                            cell.SetValue(newValue);
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot insert value [{value}] in [{kvp.Key}]: Invalid sheet",
                                this,
                                GPALObjectType.GPALExcel);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to insert value [{value}] at cell [{_currentCell}] in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Sets the value of the cell selected via <see cref="WithCell(string)"/> on every loaded
        /// workbook, replacing any existing content.
        /// </summary>
        /// <param name="value">The value to set.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations SetValue(string value)
        {
            ReopenIfClosed();

            _operationPerformed = true;

            if (string.IsNullOrEmpty(_currentCell))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"No cell specified for setting value [{value}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            worksheet.Cell(_currentCell).SetValue(value);
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot set value [{value}] in [{kvp.Key}]: Invalid sheet",
                                this,
                                GPALObjectType.GPALExcel);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to set value [{value}] at cell [{_currentCell}] in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Appends <paramref name="value"/> to the end of the existing content of the cell selected via
        /// <see cref="WithCell(string)"/> on every loaded workbook.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations AppendValue(string value)
        {
            ReopenIfClosed();

            _operationPerformed = true;

            if (string.IsNullOrEmpty(_currentCell))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"No cell specified for appending value [{value}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            var cell = worksheet.Cell(_currentCell);
                            var currentValue = cell.GetString();
                            cell.SetValue(currentValue + value);
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot append value [{value}] in [{kvp.Key}]: Invalid sheet",
                                this,
                                GPALObjectType.GPALExcel);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to append value [{value}] at cell [{_currentCell}] in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Prepends <paramref name="value"/> to the beginning of the existing content of the cell selected
        /// via <see cref="WithCell(string)"/> on every loaded workbook.
        /// </summary>
        /// <param name="value">The value to prepend.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations PrependValue(string value)
        {
            ReopenIfClosed();

            _operationPerformed = true;

            if (string.IsNullOrEmpty(_currentCell))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"No cell specified for prepending value [{value}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet != null)
                        {
                            var cell = worksheet.Cell(_currentCell);
                            var currentValue = cell.GetString();
                            cell.SetValue(value + currentValue);
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(
                                GPALEventType.ERROR,
                                $"Cannot prepend value [{value}] in [{kvp.Key}]: Invalid sheet",
                                this,
                                GPALObjectType.GPALExcel);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to prepend value [{value}] at cell [{_currentCell}] in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Replaces every occurrence of the value set via <see cref="WithSearchValue(string)"/> with
        /// <paramref name="value"/>, within each previously read range, on the currently selected sheet
        /// of every loaded workbook.
        /// </summary>
        /// <param name="value">The replacement value.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations ReplaceWith(string value)
        {
            ReopenIfClosed();
            _operationPerformed = true;

            if (string.IsNullOrEmpty(_replaceValue))
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                $"No search value for replace with [{value}]",
                this,
                GPALObjectType.GPALExcel);
            }
            else if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                GPALEventType.ERROR,
                $"No range defined for replace with [{value}]",
                this,
                GPALObjectType.GPALExcel);
            }
            else
            {
                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);
                        if (worksheet == null)
                        {
                            GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Cannot replace in [{kvp.Key}]: Invalid sheet",
                            this,
                            GPALObjectType.GPALExcel);
                        }
                        else
                        {
                            foreach (var rangeData in _rangesData)
                            {
                                ExcelHelper.SearchAndReplaceByValue(worksheet, rangeData.Range, _replaceValue, value);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to replace [{_replaceValue}] with [{value}] in [{kvp.Key}]",
                        this,
                        GPALObjectType.GPALExcel, ex);
                    }
                }
            }
            return this;
        }
        /// <summary>
        /// Returns every cell in the range holding the search value, as a grid laid out like
        /// <see cref="GetCompareResults"/>: a header row, then one row per match.<br/>
        /// Uses .WithRange and .WithSearchValue, as ReplaceWith does. Each row includes the file and sheet, since every
        /// loaded workbook is searched.
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.ExcelFor(book)
        ///     .WithRange("A1:Z1")
        ///     .WithSearchValue("Amount")
        ///     .GetSearchResults(out IGPALGrid&lt;string&gt; hits);
        ///
        /// // header row, then File | Sheet | Cell | Row | Column
        /// string amountColumn = 1 &lt; hits.Rows ? hits[1][4] : null;
        /// </code>
        /// </example>
        /// <param name="results">Receives the matches; only the header row when nothing matched</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations GetSearchResults(out IGPALGrid<string> results)
        {
            ReopenIfClosed();

            results = GPAL.GridForType<string>();
            results.AddRow(new List<string> { "File", "Sheet", "Cell", "Row", "Column" });

            if (string.IsNullOrEmpty(_replaceValue))
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No search value, so there is nothing to find. Call .WithSearchValue first", this, GPALObjectType.GPALExcel);
            else if (!_rangesData.Any())
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No range defined, so there is nothing to search. Call .WithRange first", this, GPALObjectType.GPALExcel);
            else
            {
                int found = 0;

                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = GetWorksheet(kvp.Value);

                        if (null == worksheet)
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Cannot search in [{kvp.Key}]: Invalid sheet", this, GPALObjectType.GPALExcel);
                        else
                            foreach (var rangeData in _rangesData)
                                foreach (var hit in ExcelHelper.SearchValue(worksheet, rangeData.Range, _replaceValue))
                                {
                                    results.AddRow(new List<string>
                                    {
                                        kvp.Key,
                                        _currentSheetName ?? _currentSheetIndex.ToString(),
                                        hit.Address,
                                        hit.Row.ToString(),
                                        hit.Column.ToString()
                                    });

                                    found++;
                                }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed searching for [{_replaceValue}] in [{kvp.Key}]", this, GPALObjectType.GPALExcel, ex);
                    }
                }

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{_replaceValue}] found [{found}] cell(s)", this, GPALObjectType.GPALExcel);
            }

            return this;
        }

        /// <summary>
        /// Saves the loaded workbook(s) to the file(s) matching <paramref name="gpalFile"/>'s filenames
        /// (which may include wildcard patterns), or to <see cref="_gpalFile"/> if <paramref name="gpalFile"/>
        /// is null. A workbook whose source path is among the resolved targets is saved in place; otherwise
        /// its sheets are copied into a new workbook saved at each resolved target path.
        /// </summary>
        /// <param name="gpalFile">The file (or wildcard pattern) identifying where to save, or <c>null</c> to save back to the originally loaded file(s).</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations SaveTo(GPALFile gpalFile)
        {
            _operationPerformed = true;
            gpalFile = gpalFile ?? _gpalFile;

            if (gpalFile == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No GPALFile specified for save", this, GPALObjectType.GPALExcel);
            }
            else if (!_workbooks.Any())
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No workbooks loaded to save", this, GPALObjectType.GPALExcel);
            }
            else
            {
                try
                {
                    var targetPaths = new List<string>();
                    foreach (var pattern in gpalFile.Filenames)
                    {
                        try
                        {
                            // A name without a wildcard is the file to write. Only a pattern is matched against
                            // existing files.
                            if (false == pattern.Contains("*") && false == pattern.Contains("?"))
                            {
                                targetPaths.Add(pattern);
                                continue;
                            }

                            string directory = Path.GetDirectoryName(pattern) ?? Directory.GetCurrentDirectory();
                            string fileName = Path.GetFileName(pattern);
                            var matchingFiles = Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly);
                            targetPaths.AddRange(matchingFiles);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to resolve save path [{pattern}]", this, GPALObjectType.GPALExcel, ex);
                        }
                    }
                    targetPaths = targetPaths.Distinct().ToList();

                    if (!targetPaths.Any())
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No valid target paths resolved from [{string.Join(", ", gpalFile.Filenames)}]", this, GPALObjectType.GPALExcel);
                    }
                    else
                    {
                        foreach (var kvp in _workbooks)
                        {
                            try
                            {
                                if (targetPaths.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                                {
                                    try
                                    {
                                        FileHelper.EnsureFolderFor(kvp.Key);
                                        kvp.Value.SaveAs(kvp.Key);
                                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved workbook [{kvp.Key}] to [{kvp.Key}]", this, GPALObjectType.GPALExcel);
                                    }
                                    catch (Exception ex)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to save workbook [{kvp.Key}] to [{kvp.Key}]", this, GPALObjectType.GPALExcel, ex);
                                    }
                                }
                                else
                                {
                                    foreach (var targetPath in targetPaths)
                                    {
                                        try
                                        {
                                            using var targetWorkbook = new XLWorkbook();
                                            foreach (var worksheet in kvp.Value.Worksheets)
                                            {
                                                var targetWorksheet = targetWorkbook.Worksheets.Add(worksheet.Name);
                                                ExcelHelper.CopyWorksheet(worksheet, targetWorksheet);
                                            }
                                            FileHelper.EnsureFolderFor(targetPath);
                                            targetWorkbook.SaveAs(targetPath);
                                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saved unmatched workbook [{kvp.Key}] to [{targetPath}]", this, GPALObjectType.GPALExcel);
                                        }
                                        catch (Exception ex)
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to save unmatched workbook [{kvp.Key}] to [{targetPath}]", this, GPALObjectType.GPALExcel, ex);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to process workbook [{kvp.Key}]", this, GPALObjectType.GPALExcel, ex);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to resolve save paths for [{string.Join(", ", gpalFile.Filenames)}]", this, GPALObjectType.GPALExcel, ex);
                }
            }

            return this;
        }
        /// <summary>
        /// Appends every row of the read range to <paramref name="grid"/>, keeping its rows and settings such as
        /// <see cref="IGPALGrid{T}.WithDedupeData"/>. Pass null to have GPAL create one.
        /// </summary>
        /// <param name="grid">The grid to append the rows to, or null for GPAL to create one.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations SaveTo(ref IGPALGrid<string> grid)
        {
            if (null == grid)
                grid = GPAL.GridForType<string>();

            {
                if (!_workbooks.Any() && _filePaths.Any())
                {
                    foreach (var path in _filePaths)
                        LoadFile(path);
                }
                if (!_rangesData.Any())
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        "No data to save to grid",
                        this,
                        GPALObjectType.GPALExcel);
                }
                else
                {
                    _operationPerformed = true;

                    GPAL.PublishSimpleEvent(
                        GPALEventType.INFO,
                        $"Saving [{_rangesData.Sum(rangeData => rangeData.Data.Count())}] rows from [{_rangesData.Count}] ranges to grid.",
                        this,
                        GPALObjectType.GPALExcel);

                    try
                    {
                        foreach (var rangeData in _rangesData)
                        {
                            foreach (var row in rangeData.Data)
                                grid.AddRow(row);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to save to grid",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Writes the previously read range data into the sheet named <paramref name="sheetName"/> of every
        /// loaded workbook, creating the sheet if it does not exist. If a cell was selected via
        /// <see cref="WithCell(string)"/>, only the first value of the last read range is written to that
        /// cell; otherwise the full data is written starting at the last read range's address.
        /// </summary>
        /// <param name="sheetName">The name of the destination sheet.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations SaveTo(string sheetName)
        {
            if (!_workbooks.Any() && _filePaths.Any())
            {
                foreach (var path in _filePaths)
                    LoadFile(path);
            }
            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"No data to write to sheet [{sheetName}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else if (string.IsNullOrEmpty(sheetName))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    "Sheet name cannot be empty",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                _operationPerformed = true;

                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $"Saving [{_rangesData.Count}] ranges to sheet [{sheetName}] of [{_workbooks.Count}] workbooks.",
                    this,
                    GPALObjectType.GPALExcel);

                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = kvp.Value.Worksheets.Any(ws => ws.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase))
                            ? kvp.Value.Worksheet(sheetName)
                            : kvp.Value.Worksheets.Add(sheetName);
                        string target;
                        if (!string.IsNullOrEmpty(_currentCell))
                        {
                            target = _currentCell;
                            worksheet.Cell(target).SetValue(_rangesData.Last().Data.SelectMany(row => row).FirstOrDefault() ?? "");
                        }
                        else
                        {
                            target = _rangesData.Last().Range;
                            foreach (var rangeData in _rangesData)
                            {
                                ExcelHelper.WriteRange(worksheet, rangeData.Range, rangeData.Data);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to write to sheet [{sheetName}] in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Writes the previously read range data into the sheet at the given 1-based index of every loaded
        /// workbook, creating the sheet (named "Sheet{sheetIndex}") if it does not exist. If a cell was
        /// selected via <see cref="WithCell(string)"/>, only the first value of the last read range is
        /// written to that cell; otherwise the full data is written starting at the last read range's address.
        /// </summary>
        /// <param name="sheetIndex">The 1-based index of the destination sheet.</param>
        /// <returns>Fluent interface to perform further Excel operations.</returns>
        public IAllowExcelOperations SaveTo(int sheetIndex)
        {
            if (!_workbooks.Any() && _filePaths.Any())
            {
                foreach (var path in _filePaths)
                    LoadFile(path);
            }
            if (!_rangesData.Any())
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"No data to write to sheet index [{sheetIndex}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else if (sheetIndex < 1)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Invalid sheet index [{sheetIndex}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                _operationPerformed = true;

                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $"Saving [{_rangesData.Count}] ranges to sheet index [{sheetIndex}] of [{_workbooks.Count}] workbooks.",
                    this,
                    GPALObjectType.GPALExcel);

                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        var worksheet = kvp.Value.Worksheets.ElementAtOrDefault(sheetIndex - 1) ?? kvp.Value.Worksheets.Add($"Sheet{sheetIndex}");
                        string target;
                        if (!string.IsNullOrEmpty(_currentCell))
                        {
                            target = _currentCell;
                            worksheet.Cell(target).SetValue(_rangesData.Last().Data.SelectMany(row => row).FirstOrDefault() ?? "");
                        }
                        else
                        {
                            target = _rangesData.Last().Range;
                            foreach (var rangeData in _rangesData)
                            {
                                ExcelHelper.WriteRange(worksheet, rangeData.Range, rangeData.Data);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to write to sheet index [{sheetIndex}] in [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// Closes every loaded workbook, optionally saving each back to its source path first, and clears
        /// all cached state. A subsequent operation on this instance will reload the workbooks via
        /// <see cref="ReopenIfClosed"/>. If this instance is already closed, this is a no-op.
        /// </summary>
        /// <param name="saveOnClose"><c>true</c> to save each workbook to its original path before closing; otherwise <c>false</c>.</param>
        public void Close(bool saveOnClose)
        {
            if (_isClosed)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.INFO,
                    $"GPALExcel instance already closed [{_gpalFile?.Filenames?.FirstOrDefault() ?? "unknown"}]",
                    this,
                    GPALObjectType.GPALExcel);
            }
            else
            {
                _operationPerformed = false;

                foreach (var kvp in _workbooks)
                {
                    try
                    {
                        if (saveOnClose)
                        {
                            FileHelper.EnsureFolderFor(kvp.Key);
                            kvp.Value.SaveAs(kvp.Key);
                        }
                        kvp.Value.Dispose();
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to close workbook [{kvp.Key}]",
                            this,
                            GPALObjectType.GPALExcel, ex);
                    }
                }
                _workbooks.Clear();
                _filePaths.Clear();
                _rangesData.Clear();
                _currentSheetName = null;
                _currentSheetIndex = -1;
                _insertPosition = 0;
                _insertSeparator = " ";
                _currentCell = null;
                _isClosed = true;
            }
        }

        /// <summary>
        /// Returns the currently selected sheet (by name via <see cref="WithSheet(string)"/> or by index via
        /// <see cref="WithSheet(int)"/>) within <paramref name="workbook"/>, or <c>null</c> if no sheet has
        /// been selected or the selected sheet does not exist in this workbook.
        /// </summary>
        /// <param name="workbook">The workbook to search.</param>
        /// <returns>The selected worksheet, or <c>null</c> if not found.</returns>
        private IXLWorksheet GetWorksheet(XLWorkbook workbook)
        {
            if (!string.IsNullOrEmpty(_currentSheetName))
            {
                return workbook.Worksheets.FirstOrDefault(ws => ws.Name.Equals(_currentSheetName, StringComparison.OrdinalIgnoreCase));
            }
            if (-1 != _currentSheetIndex)
            {
                // WithSheet is 1-based; Worksheets is 0-based.
                return workbook.Worksheets.ElementAtOrDefault(_currentSheetIndex - 1);
            }
            return null;
        }

        /// <summary>
        /// Looks up a worksheet in <paramref name="workbook"/> by name; if no worksheet has that name and
        /// <paramref name="sheetIdentifier"/> parses as a positive integer, falls back to looking it up as
        /// a 1-based sheet index.
        /// </summary>
        /// <param name="workbook">The workbook to search.</param>
        /// <param name="sheetIdentifier">A sheet name, or a 1-based sheet index as a string.</param>
        /// <returns>The matching worksheet, or <c>null</c> if not found.</returns>
        internal static IXLWorksheet GetWorksheet(XLWorkbook workbook, string sheetIdentifier)
        {
            try
            {
                if (workbook.TryGetWorksheet(sheetIdentifier, out IXLWorksheet worksheet))
                {
                    return worksheet;
                }

                if (int.TryParse(sheetIdentifier, out int sheetIndex) && sheetIndex > 0)
                {
                    try
                    {
                        worksheet = workbook.Worksheet(sheetIndex);
                        return worksheet;
                    }
                    catch
                    {
                    }
                }

                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"Worksheet [{sheetIdentifier}] not found in workbook and no default name provided.");
                return null;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to get worksheet [{sheetIdentifier}]", null, Enums.GPALObjectType.None, ex);
                return null;
            }
        }

        /// <summary>
        /// Looks up the worksheet at the given 1-based index in <paramref name="workbook"/>.
        /// </summary>
        /// <param name="workbook">The workbook to search.</param>
        /// <param name="sheetIndex">The 1-based index of the worksheet.</param>
        /// <returns>The matching worksheet, or <c>null</c> if not found.</returns>
        internal static IXLWorksheet GetWorksheet(XLWorkbook workbook, int sheetIndex)
        {
            try
            {
                if (null != workbook.Worksheet(sheetIndex))
                    return workbook.Worksheet(sheetIndex);

                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"Worksheet [{sheetIndex}] not found in workbook and no default name provided.");
                return null;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to get worksheet [{sheetIndex}]", null, Enums.GPALObjectType.None, ex);
                return null;
            }
        }
    }
}
