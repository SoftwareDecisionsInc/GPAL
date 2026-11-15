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

//extern alias mUIA;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using static GenerallyPositive.Enums;
// The HiddenDesktop property hides the static HiddenDesktop class, so the class is used through this alias. Browser
// does the same.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    public partial class Application
    {
        /// <summary>
        /// Retrieve the current selectors' data (or a table/grid control's data) into a GPALGrid. The grid is
        /// passed by ref: pass an existing grid and GetGrid adds its rows into it (reuse one grid to accumulate
        /// across calls, it never replaces your reference); pass null to get a fresh grid back.<br/>
        /// GPAL also keeps the grid internally for any following SaveTo*/AppendTo* call.
        /// </summary>
        public IAllowWithHeaderOrFileActions GetGrid(ref IGPALGrid<string> returnGrid)
        {
            // Reading the grid is an action, as on the browser side, so the next WithSelector starts a new unit of
            // work. GetGridCalled tells WithHeader that headers set after it belong to this grid.
            CurrentUOW.ActionCalled = true;
            CurrentUOW.GetGridCalled = true;
            CurrentUOW.RetGrid.Clear();

            if (!TryGetGridFromControl())
                GetGridFromSelectors();

            // Do not take ownership of a grid the caller passed in:
            //  - null in -> hand them GPAL's internal grid.
            //  - grid in -> add our rows into THEIR grid and keep their reference so repeated GetGrid calls accumulate.
            if (null == returnGrid)
                returnGrid = (IGPALGrid<string>)CurrentUOW.RetGrid;
            else
                returnGrid.Add((IGPALGrid<string>)CurrentUOW.RetGrid);

            return this;
        }

        // Table-native: a single selector that resolves to a control exposing the UIA GridPattern
        // is read cell-by-cell via GetItem(row, col); TablePattern column headers become the header
        // row. Returns false when this is not a grid control so the selector-column path runs.
        private bool TryGetGridFromControl()
        {
            if (1 != CurrentUOW.WithSelectorList.Count
                || SelectorType.Selector != CurrentUOW.WithSelectorList[0].selectorSettings.SelectorType)
                return false;

            Selector sel = CurrentUOW.WithSelectorList[0];
            bool matchedAll; List<GPALAutomationElement> matched;
            var found = ElementHelper.FindElements(this, CurrentUOW, sel, out matchedAll, out matched);
            AutomationElement ae = (null != found && found.Count > 0) ? found[0].Ae : null;
            object gpObj;
            if (null == ae || !ae.TryGetCurrentPattern(GridPattern.Pattern, out gpObj))
                return false;

            GridPattern grid = (GridPattern)gpObj;
            int rows = grid.Current.RowCount;
            int cols = grid.Current.ColumnCount;

            List<string> headers = null;
            object tpObj;
            if (ae.TryGetCurrentPattern(TablePattern.Pattern, out tpObj))
            {
                AutomationElement[] colHeaders = ((TablePattern)tpObj).Current.GetColumnHeaders();
                if (null != colHeaders && colHeaders.Length > 0)
                {
                    headers = new List<string>();

                    // Read the header's Name first, then its text. Explorer's column headers are SplitButtons whose
                    // ValuePattern is empty.
                    foreach (AutomationElement h in colHeaders)
                        headers.Add(true == string.IsNullOrWhiteSpace(h.Current.Name) ? (h.GetText() ?? string.Empty) : h.Current.Name);
                }
            }

            // A list view without TablePattern still has a Header element with one HeaderItem per column; use those as
            // the column names.
            if (null == headers)
                headers = ColumnHeadersUnder(ae);

            int cap = (int.MaxValue == CurrentUOW.WithAllThatMatch)
                        ? int.MaxValue : CurrentUOW.WithAllThatMatch;

            for (int r = 0; r < rows && CurrentUOW.RetGrid.Count() < cap; r++)
            {
                var row = new List<string>(cols);
                for (int c = 0; c < cols; c++)
                {
                    string text;
                    try
                    {
                        AutomationElement cell = grid.GetItem(r, c);
                        text = (null != cell) ? (cell.GetText() ?? string.Empty) : GPAL.ErrorPlaceholder;
                    }
                    catch
                    {
                        text = GPAL.ErrorPlaceholder;
                    }
                    row.Add(text);
                }
                CurrentUOW.RetGrid.AddRow(row);
            }

            if (null != headers && headers.Count > 0)
            {
                CurrentUOW.ControlHeaderList = headers;

                if (null == CurrentUOW.HeaderList || 0 == CurrentUOW.HeaderList.Count)
                    CurrentUOW.HeaderList = headers;
            }

            // The control's column names, so a workflow can read them after GetGrid without opening the saved file.
            ColumnNames = null == headers ? new List<string>() : new List<string>(headers);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: [{sel.Name}] resolved to a UIA grid ([{rows}] x [{cols}]).", this, GPALObjectType.Application);
            return true;
        }

        /// <summary>
        /// Returns the column names from a control's Header element, one HeaderItem per column, for a control without
        /// TablePattern (usually a list view).
        /// </summary>
        /// <param name="element">The grid control to look under</param>
        /// <returns>The names in column order; null when the control shows none</returns>
        private static List<string> ColumnHeadersUnder(AutomationElement element)
        {
            List<string> retVal = null;

            AutomationElement header = element.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));

            if (null != header)
            {
                // Read every child of the header, not only HeaderItems: Explorer's columns are SplitButtons, and a list
                // view can use any control type.
                AutomationElementCollection items = header.FindAll(TreeScope.Children, Condition.TrueCondition);

                if (0 < items.Count)
                {
                    retVal = new List<string>();

                    foreach (AutomationElement item in items)
                        retVal.Add(true == string.IsNullOrWhiteSpace(item.Current.Name) ? (item.GetText() ?? string.Empty) : item.Current.Name);
                }
            }

            return retVal;
        }

        // Column-per-selector extraction: each selector is a column, repeating matches are the rows.
        // Mirrors the Browser column-zip (pad short columns, dedup rows, honor WithAllThatMatch and
        // multi-page paging) but reads cells with the UIA .Ae.GetText() the app side already uses.
        private void GetGridFromSelectors()
        {
            var names = new List<string>();
            foreach (Selector s in CurrentUOW.WithSelectorList)
                names.Add(s.Name);

            var seenRows = new HashSet<string>();
            bool capReached = false;
            int cap = (int.MaxValue == CurrentUOW.WithAllThatMatch)
                        ? int.MaxValue : CurrentUOW.WithAllThatMatch;

            for (int page = 0; page < CurrentUOW.PageCount && !capReached; page++)
            {
                var columns = new List<List<string>>();
                int maxRows = 0;

                foreach (Selector sel in CurrentUOW.WithSelectorList)
                {
                    var col = new List<string>();
                    if (SelectorType.Selector == sel.selectorSettings.SelectorType)
                    {
                        bool matchedAll; List<GPALAutomationElement> matched;
                        var found = ElementHelper.FindElements(this, CurrentUOW, sel, out matchedAll, out matched);
                        List<GPALAutomationElement> elems =
                            (!matchedAll && null != matched && matched.Count > 0)
                                ? matched
                                : (null != found ? found.ToList() : new List<GPALAutomationElement>());
                        foreach (GPALAutomationElement e in elems)
                            col.Add((null != e.Ae) ? (e.Ae.GetText() ?? string.Empty) : GPAL.ErrorPlaceholder);
                    }
                    else if (SelectorType.Data == sel.selectorSettings.SelectorType)
                        col.Add(sel.SelectorPath);
                    else if (SelectorType.DataFunc == sel.selectorSettings.SelectorType)
                        col.Add(sel.selectorSettings.DataFunction());

                    columns.Add(col);
                    if (col.Count > maxRows) maxRows = col.Count;
                }

                // pad short columns so every row has a cell per column
                for (int i = 0; i < columns.Count; i++)
                    while (columns[i].Count < maxRows)
                        columns[i].Add($"{GPAL.ErrorPlaceholder} : {names[i]}");

                for (int r = 0; r < maxRows && !capReached; r++)
                {
                    var row = new List<string>(columns.Count);
                    for (int c = 0; c < columns.Count; c++)
                        row.Add(columns[c][r]);

                    string key = string.Join("", row);
                    if (!seenRows.Add(key))
                        continue;   // duplicate row from a prior page/scroll pass
                    CurrentUOW.RetGrid.AddRow(row);
                    if (CurrentUOW.RetGrid.Count() >= cap)
                        capReached = true;
                }

                if (!capReached && page < CurrentUOW.PageCount - 1)
                    if (!ApplicationHelper.GotoNextPage(this))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: no next page after page [{page + 1}].", this, GPALObjectType.Application);
                        break;
                    }
            }

            if (null == CurrentUOW.HeaderList || 0 == CurrentUOW.HeaderList.Count)
                CurrentUOW.HeaderList = names;
        }

        /// <summary>Column header for exported data. Overrides selector names / table headers, in order.</summary>
        public IAllowWithHeaderOrFileActions WithHeader(string header)
        {
            if (true == CurrentUOW.GetGridCalled)
            {
                CurrentUOW.GetGridCalled = false;
                CurrentUOW.HeaderList.Clear();
            }
            CurrentUOW.HeaderList.Add(header);
            return this;
        }

        /// <summary>Sets a GPALGrid to be saved by a following SaveTo*.</summary>
        /// <remarks>
        /// Starts a new unit of work when the current one has already acted, as a selector does.
        /// </remarks>
        public IAllowWithHeaderOrFileActions WithGridToSave(IGPALGrid<string> gridToSave)
        {
            if (true == CurrentUOW.ActionCalled)
                CurrentUOW = new UnitOfWork();

            CurrentUOW.RetGrid = gridToSave;

            return this;
        }

        /// <summary>The control to click to load the next page when paging through results.</summary>
        public IAllowWithPagesAndGridActions WithNextPageButton(Selector selector)
        {
            CurrentUOW.NextPageButton = selector;
            return this;
        }

        /// <summary>Scroll to load more rows between pages. Not supported for desktop apps (single page).</summary>
        public IAllowWithPagesAndGridActions WithInfiniteScroll
        {
            get
            {
                CurrentUOW.InfiniteScroll = true;
                return this;
            }
        }

        /// <summary>Number of result pages to retrieve. Use with WithNextPageButton.</summary>
        public IAllowGetGridAndFillInFrom WithPages(int numberOfPages)
        {
            CurrentUOW.PageCount = numberOfPages;
            return this;
        }

        // Resolve the output column names for a grid save, handed to the Converter as column metadata
        // (WithColumnNames), NOT injected as a data row. Precedence, highest first, with a short higher list
        // topped up from the next level down:
        //   1. CurrentUOW.HeaderList  - the workflow's .WithHeader columns (override)
        //   2. the output GPALFile's ColumnList - the file's own columns (wildcard/.Next replication of the
        //      first entry happens at expansion time, so entry 0 is representative here)
        //   3. selector names - the ultimate global, always defined
        //   4. the control's own column names, e.g. a UIA TablePattern's headers
        //   5. generated column1..columnN - so the list is never narrower than the widest row, because a
        //      column with no name must never mean a column with no data
        // The result is then made unique: the writer keys cells by column name, so a repeated name would
        // collapse those columns into one holding the last value.
        // Whether the names are written is governed by the output file's FirstLineIsColumnNames.
        private List<string> ResolveHeaderColumns(GPALFile file)
        {
            List<string> selectorNames = CurrentUOW.WithSelectorList?.Where(s => null != s.Name).Select(s => s.Name).ToList()
                ?? new List<string>();

            List<string> fileColumns = new List<string>();
            IGPALGrid<string> columnList = ((IGPALFileInternal)file).FileSettings.ColumnList;
            if (null != columnList && 0 < columnList.Count() && null != columnList[0])
                fileColumns = new List<string>(columnList[0]);

            List<string> resolved = (null != CurrentUOW.HeaderList && 0 < CurrentUOW.HeaderList.Count)
                ? new List<string>(CurrentUOW.HeaderList)
                : new List<string>();
            for (int i = resolved.Count; i < fileColumns.Count; i++)
                resolved.Add(fileColumns[i]);
            for (int i = resolved.Count; i < selectorNames.Count; i++)
                resolved.Add(selectorNames[i]);

            // Prefer the control's own column names over generated ones. WithHeader clears HeaderList, so the control's
            // names are kept separately.
            List<string> controlHeaders = CurrentUOW.ControlHeaderList ?? new List<string>();

            for (int i = resolved.Count; i < controlHeaders.Count; i++)
                resolved.Add(controlHeaders[i]);

            // The converter only writes named columns. Generate names for extra columns instead of trimming the data.
            int widest = 0;

            if (null != CurrentUOW.RetGrid)
                foreach (List<string> row in CurrentUOW.RetGrid)
                    if (null != row && widest < row.Count)
                        widest = row.Count;

            for (int i = resolved.Count; i < widest; i++)
                resolved.Add($"column{i + 1}");

            return UniqueColumns(resolved);
        }

        /// <summary>
        /// Makes every column name non-blank and unique; the writer keys cells by name, so duplicates collapse into one
        /// column.
        /// </summary>
        /// <remarks>
        /// A blank name becomes columnN and a repeat gets a number, so Size, Size becomes Size, Size2.
        /// </remarks>
        /// <param name="columns">The resolved names, which may include blanks and repeats</param>
        /// <returns>The same names, each non-blank and unique</returns>
        private static List<string> UniqueColumns(List<string> columns)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            for (int i = 0; i < columns.Count; i++)
            {
                string name = true == string.IsNullOrWhiteSpace(columns[i]) ? $"column{i + 1}" : columns[i].Trim();
                string candidate = name;
                int repeat = 1;

                while (false == used.Add(candidate))
                {
                    repeat++;
                    candidate = $"{name}{repeat}";
                }

                result.Add(candidate);
            }

            return result;
        }

        /// <summary>
        /// Save the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension, e.g. .csv/.tsv/.txt/.xlsx/.json, or its explicit file settings).
        /// </summary>
        public IAllowAllActionAndAnySelector SaveTo(GPALFile file)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of window data to [{file?.Filename}].", this, GPALObjectType.Application);

            var converter = GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file));
            // Naming headers via WithHeader is clear intent to write them; declare it so the output emits a
            // header row unless the output file explicitly turns it off (WithFirstLineIsColumnNames(false)).
            if (null != CurrentUOW.HeaderList && 0 < CurrentUOW.HeaderList.Count)
                converter.WithFirstLineHasColumnNames(true);
            converter.SaveTo(file);
            return this;
        }

        /// <summary>
        /// Put the retrieved grid on the clipboard as text: rows as lines, cells separated by tabs, which a
        /// spreadsheet pastes as cells. A single control is a one-cell grid, so its text goes on as is.
        /// </summary>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(resultsList)
        ///         .GetGrid(ref grid)
        ///         .SaveTo(GPAL.Clipboard);
        /// </code>
        /// </example>
        /// <param name="clipboard">The clipboard, GPAL.Clipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllActionAndAnySelector SaveTo(IAllowClipboardInput clipboard)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Copying [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of window data to the clipboard.", this, GPALObjectType.Application);

            clipboard.WithInput(CurrentUOW.RetGrid).SetClipboard();
            return this;
        }

        /// <summary>
        /// Append the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension or explicit file settings). Headers are not repeated on append.
        /// </summary>
        public IAllowAllActionAndAnySelector AppendTo(GPALFile file)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Appending [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of window data to [{file?.Filename}].", this, GPALObjectType.Application);

            GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file)).AppendTo(file);
            return this;
        }

        /// <summary>
        /// The column names the last GetGrid read from the control. Empty when the control had none.
        /// </summary>
        public IReadOnlyList<string> ColumnNames { get; private set; } = new List<string>();
    }
}
