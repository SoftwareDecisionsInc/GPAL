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
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;

namespace GenerallyPositive
{
    internal class ExcelHelper
    {
        // Existing methods unchanged
        internal static void TokenizeExcelFile(GPALFile gPALFile)
        {
            foreach (string filename in gPALFile.Filenames)
            {
                try
                {
                    IGPALGrid<string> data = GPAL.Grid.ToGPALObject();

                    using (var workbook = new XLWorkbook(filename))
                    {
                        var worksheet = workbook.Worksheet(1);

                        foreach (var row in worksheet.RowsUsed())
                        {
                            var rowData = new List<string>();
                            foreach (var cell in row.CellsUsed())
                            {
                                rowData.Add(cell.GetString());
                            }
                            data.AddRow(rowData);
                        }
                        ((IGPALFileInternal)gPALFile).FileGrid.Add(data);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to tokenize file [{filename}]. Continuing.", gPALFile, Enums.GPALObjectType.GPALFile, ex);
                }
            }
        }

        internal static IGPALGrid<string> TokenizeExcelFile(string filename)
        {
            IGPALGrid<string> data = GPAL.Grid.ToGPALObject();

            try
            {
                using (var workbook = new XLWorkbook(filename))
                {
                    foreach (IXLWorksheet worksheet in workbook.Worksheets)
                    {
                        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

                        for (int r = 1; r <= lastRow; r++)
                        {
                            var rowData = new List<string>();
                            var row = worksheet.Row(r);

                            for (int c = 1; c <= lastCol; c++)
                            {
                                rowData.Add(row.Cell(c).GetString());
                            }

                            data.AddRow(rowData);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION,
                    $"Unable to tokenize file [{filename}]. Continuing.", null,
                    Enums.GPALObjectType.None, ex);
            }

            return data;
        }


        /// <summary>
        /// Copies the contents of a source worksheet to a destination worksheet.
        /// </summary>
        /// <param name="src">The source worksheet to copy from.</param>
        /// <param name="dest">The destination worksheet to copy to.</param>
        internal static void CopyWorksheet(IXLWorksheet src, IXLWorksheet dest)
        {
            try
            {
                // Clear the destination worksheet to avoid overlapping data
                dest.Clear();

                // Get all used cells from the source worksheet
                foreach (var row in src.RowsUsed())
                {
                    int rowIndex = row.RowNumber();
                    foreach (var cell in row.CellsUsed())
                    {
                        int colIndex = cell.Address.ColumnNumber;
                        // Copy the cell value to the same position in the destination worksheet
                        dest.Cell(rowIndex, colIndex).Value = cell.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to copy worksheet", null, Enums.GPALObjectType.None, ex);
            }
        }
        /// <summary>
        /// Writes data to a specified range in the Excel worksheet.
        /// </summary>
        /// <param name="worksheet">The worksheet to write to.</param>
        /// <param name="range">Range to write (e.g., "A1:B10").</param>
        /// <param name="data">The grid data to write.</param>
        internal static void WriteRange(IXLWorksheet worksheet, string range, IGPALGrid<string> data)
        {
            try
            {
                var rangeObj = worksheet.Range(range);
                var firstCell = rangeObj.FirstCell();

                int startRow = firstCell.Address.RowNumber;
                int startColumn = firstCell.Address.ColumnNumber;

                for (int rowIndex = 0; rowIndex < data.Count(); rowIndex++)
                {
                    var row = data[rowIndex];
                    for (int colIndex = 0; colIndex < row.Count; colIndex++)
                    {
                        worksheet.Cell(startRow + rowIndex, startColumn + colIndex).Value = row[colIndex];
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to write range [{range}] in worksheet", null, Enums.GPALObjectType.None, ex);
            }
        }

        // New concrete class for comparison results
        public class CellDifference
        {
            public string CellAddress { get; set; }
            public string OldValue { get; set; }
            public string NewValue { get; set; }
            public string SourceRange { get; set; } // New: Tracks the source range
            public string TargetRange { get; set; } // New: Tracks the target range
        }
        // New methods using XLWorkbook and concrete objects

        /// <summary>
        /// Compares two data ranges and returns differences.
        /// </summary>
        /// <param name="data1">First data range.</param>
        /// <param name="data2">Second data range.</param>
        /// <returns>List of CellDifference objects with cell address and differing values.</returns>
        internal static List<CellDifference> CompareRanges(IGPALGrid<string> data1, IGPALGrid<string> data2)
        {
            var differences = new List<CellDifference>();

            try
            {
                int maxRows = Math.Max(data1.Count(), data2.Count());
                int maxCols = Math.Max(data1.Any() ? data1[0].Count : 0, data2.Any() ? data2[0].Count : 0);

                for (int i = 0; i < maxRows; i++)
                {
                    for (int j = 0; j < maxCols; j++)
                    {
                        string value1 = i < data1.Count() && j < data1[i].Count ? data1[i][j] : null;
                        string value2 = i < data2.Count() && j < data2[i].Count ? data2[i][j] : null;

                        if (value1 != value2)
                        {
                            string cellAddress = $"{(char)('A' + j)}{i + 1}";
                            differences.Add(new CellDifference
                            {
                                CellAddress = cellAddress,
                                OldValue = value1 ?? "",
                                NewValue = value2 ?? ""
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to compare ranges", null, Enums.GPALObjectType.None, ex);
            }

            return differences;
        }


        internal static IGPALGrid<string> ReadRange(IXLWorksheet worksheet, string range)
        {
            IGPALGrid<string> data = GPAL.Grid.ToGPALObject();

            try
            {
                var xlRange = worksheet.Range(range);

                foreach (var row in xlRange.RowsUsed())
                {
                    var rowData = new List<string>();
                    foreach (var cell in row.CellsUsed())
                    {
                        rowData.Add(cell.GetString());
                    }
                    data.AddRow(rowData);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to extract range [{range}]", null, Enums.GPALObjectType.None, ex);
            }

            return data;
        }



        internal static List<string> GetColumnValues(IXLWorksheet worksheet, string column)
        {
            var values = new List<string>();

            try
            {
                var col = worksheet.Column(column);
                foreach (var cell in col.CellsUsed())
                {
                    values.Add(cell.GetString());
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to get values for column [{column}]", null, Enums.GPALObjectType.None, ex);
            }

            return values;
        }

        internal static List<string> GetRowValues(IXLWorksheet worksheet, int rowNumber)
        {
            var values = new List<string>();

            try
            {
                var row = worksheet.Row(rowNumber);
                foreach (var cell in row.CellsUsed())
                {
                    values.Add(cell.GetString());
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to get values for row [{rowNumber}]", null, Enums.GPALObjectType.None, ex);
            }

            return values;
        }

        /// <summary>
        /// Returns every cell in the range that holds this value, with its address, row and column: the find half of
        /// ReplaceWith.
        /// </summary>
        /// <param name="worksheet">The sheet to search</param>
        /// <param name="range">The range to search</param>
        /// <param name="searchValue">The value to look for</param>
        /// <returns>One entry per match; empty when the value is not in the range</returns>
        internal static List<(string Address, int Row, int Column)> SearchValue(IXLWorksheet worksheet, string range, string searchValue)
        {
            List<(string Address, int Row, int Column)> retVal = new List<(string Address, int Row, int Column)>();

            try
            {
                foreach (var cell in worksheet.Range(range).CellsUsed())
                    if (cell.GetString() == searchValue)
                        retVal.Add((cell.Address.ToString(), cell.Address.RowNumber, cell.Address.ColumnNumber));
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to search value in range [{range}]", null, Enums.GPALObjectType.None, ex);
            }

            return retVal;
        }

        internal static void SearchAndReplaceByValue(IXLWorksheet worksheet, string range, string searchValue, string replaceValue)
        {
            try
            {
                var xlRange = worksheet.Range(range);
                foreach (var cell in xlRange.CellsUsed())
                {
                    if (cell.GetString() == searchValue)
                    {
                        cell.Value = replaceValue;
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Failed to search and replace in range [{range}]", null, Enums.GPALObjectType.None, ex);
            }
        }







        /// <summary>
        /// Reconstructs an XLSX file from the tokenized grid and metadata that was created during parsing.
        /// </summary>
        /// <param name="outputPath">Full path where the new Excel file will be saved.</param>
        /// <param name="grid">The full tokenized grid (all sheets concatenated, header rows already removed if FirstLineIsColumnHeaders was true).</param>
        /// <param name="rowsPerSheet">Number of data rows per sheet (List&lt;int&gt; from FileSettings.ExcelRowsPerSheet).</param>
        /// <param name="columnNamesPerSheet">Column names for each sheet (List&lt;List&lt;string&gt;&gt; from myConverterSettings.ColumnNames).</param>
        /// <param name="sheetNames">Original sheet names (optional). If null or not enough entries, generic names will be used.</param>
        /// <param name="writeHeaders">If true, the column header row will be written to each sheet.</param>
        public static void WriteXlsxFromTokenizedData(
            string outputPath,
            IGPALGrid<string> grid,
            List<int> rowsPerSheet,
            IGPALGrid<string> columnNamesPerSheet,
            List<string> sheetNames,
            bool writeHeaders = true)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (rowsPerSheet == null) throw new ArgumentNullException(nameof(rowsPerSheet));
            if (columnNamesPerSheet == null) throw new ArgumentNullException(nameof(columnNamesPerSheet));

            using var workbook = new XLWorkbook();

            // A grid not read from a workbook has no sheet layout. Treat it as one sheet whose first row is the column
            // names; otherwise saving throws.
            if (0 == rowsPerSheet.Count && 0 < grid.Rows)
            {
                rowsPerSheet = new List<int> { grid.Rows };

                if (0 == columnNamesPerSheet.Rows)
                    columnNamesPerSheet.AddRow(new List<string>(grid[0]));
            }

            int currentRowInGrid = 0;

            for (int sheetIdx = 0; sheetIdx < rowsPerSheet.Count; sheetIdx++)
            {
                int totalRowsThisSheet = rowsPerSheet[sheetIdx];
                if (totalRowsThisSheet == 0) continue;

                var columnNames = columnNamesPerSheet[sheetIdx];

                string sheetName = sheetNames != null && sheetIdx < sheetNames.Count
                    ? sheetNames[sheetIdx]
                    : $"Sheet{sheetIdx + 1}";

                var worksheet = workbook.Worksheets.Add(sheetName);

                int destRow = 1;

                // Header
                if (writeHeaders)
                {
                    for (int col = 0; col < columnNames.Count; col++)
                        worksheet.Cell(1, col + 1).Value = columnNames[col];

                    worksheet.Row(1).Style.Font.Bold = true;
                    destRow = 2;
                }

                int startOffset = writeHeaders ? 1 : 0;
                int dataRowsToWrite = totalRowsThisSheet - startOffset;

                // === THIS IS THE ONLY IMPORTANT CHANGE ===
                for (int i = 0; i < dataRowsToWrite; i++)
                {
                    var sourceRow = grid[currentRowInGrid + startOffset + i];

                    // Always write ALL columns defined by the header, even if sourceRow has fewer items
                    for (int col = 0; col < columnNames.Count; col++)
                    {
                        string value = col < sourceRow.Count ? sourceRow[col] ?? string.Empty : string.Empty;
                        worksheet.Cell(destRow, col + 1).Value = value;
                    }

                    destRow++;
                }

                // Keep empty columns visible
                if (columnNames.Count > 0)
                    worksheet.Columns(1, columnNames.Count).AdjustToContents();

                currentRowInGrid += totalRowsThisSheet;
            }

            FileHelper.EnsureFolderFor(outputPath);
            workbook.SaveAs(outputPath);
        }
    }
}
