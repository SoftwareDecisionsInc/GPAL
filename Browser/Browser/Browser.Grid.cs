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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// <summary>
        /// Retrieve the current UOW selectors data into a &lt;string&gt; of columns and rows to use with GPAL or however you want.<br/>
        /// using GenerallyPositive.Common;
        /// </summary>
        /// <example>
        /// <code>
        ///     ...
        ///     .WithSelector(descriptionColumn)
        ///     .WithSelector(priceColumn)
        ///     .WithSelector(linkColumn)
        ///     .WithSelector(imageColumn)
        ///     .WithAllThatMatch()
        ///     .WithNextPageButton(nextPageButton)
        ///     .WithPages(3)   // get three pages of results
        ///     .GetGrid(out retGrid);   // get grid for us
        ///     
        ///     myBrowser
        ///         .WithGridToSave(retGrid)
        ///         .WithHeader("Description")
        ///         .WithHeader("Price")
        ///         .WithHeader("Link")
        ///         .WithHeader("Image URL")
        ///         .SaveTo(@"c:\ebay.retrieved.columns.txt");   // save to file (.txt => tab-delimited)
        /// </code>
        /// </example>
        /// <param name="returnGrid">Output GPALGrid</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithHeaderOrFileActions GetGrid(ref IGPALGrid<string> returnGrid)
        {
            List<ReadOnlyCollection<GPALElement>> columns = new List<ReadOnlyCollection<GPALElement>>();
            List<ReadOnlyCollection<GPALElement>> tmpColumns = new List<ReadOnlyCollection<GPALElement>>();
            var selectorMeta = new List<(string Name, string AttributeName)>();
            List<GPALElement> matchedElems = new List<GPALElement>();
            dynamic foundElements = null;

            // GetGrid is an action, so the unit of work ends. GetGridCalled tells WithHeader that following headers
            // belong to this grid; SaveTo and WithHeader still read the grid from this unit of work.
            CurrentUOW.ActionCalled = true;
            CurrentUOW.GetGridCalled = true;
            CurrentUOW.RetGrid.Clear();

            // One selector that finds a <table> or <tr> rows is read as a table: a row per <tr>, a column per cell,
            // the <th> row as names. Anything else uses the column-per-selector code below.
            if (TryGetGridFromTable(ref returnGrid))
                return this;

            // Content-based row dedup, opt-in via .WithDedupeData(true) on the grid the caller passed in.
            // seenRows holds a key per row's extracted values. We seed it from the rows already in the caller's
            // grid so accumulation across GetGrid calls stays duplicate-free, not just within this one call.
            bool useDedupe = (returnGrid as GPALGrid<string>)?.UseDedupe ?? false;
            var seenRows = new HashSet<string>();
            if (true == useDedupe && null != returnGrid)
                foreach (List<string> existingRow in returnGrid)
                    seenRows.Add(RowDedupeKey(existingRow));
            bool capReached = false;

            for (int pages = 0; pages < CurrentUOW.PageCount && !capReached; pages++)
            {
                // With .InElement the selectors are relative, so do not wait on them page-wide; RowsFromContainers
                // waits for the containers.
                bool waitOnSelectors = null == CurrentUOW.ContextPath?
                    .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

                // check for persistent selectors on each new page
                if (!InAction(true == waitOnSelectors && WaitTime.Never < CurrentUOW.WaitForInMs))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "WaitFor did not find the elements for the grid. Not retrieving anything.", this, GPALObjectType.Browser);
                    return this;
                }

                tmpColumns.Clear();
                columns.Clear();
                CurrentUOW.MatchedRowIndexes.Clear();
                selectorMeta.Clear();

                BrowserHelper.CheckDocumentReady(this);

                // iterate over selectors
                // With .InElement each row is a container. RowsFromContainers fills the same columns, selectorMeta
                // and RowCount that the selector loop fills, and the code after it reads them the same way.
                if (true == RowsFromContainers(tmpColumns, selectorMeta, pages))
                {
                    columns = tmpColumns;
                }
                else
                foreach (Selector sel in CurrentUOW.WithSelectorList)
                {
                    // clear the selector's cached results; the search below fills them again
                    sel.WebSelectorFoundResults = null;
                    sel.WebSelectorMatchedResults = null;

                    selectorMeta.Add((sel.Name, sel.AttributeName));

                    // we have selectors for elements and data/datafunction literals
                    if (SelectorType.Selector == sel.selectorSettings.SelectorType)
                    {
                        bool matchedAll = false;

                        foundElements = ElementHelper.FindWebElements(this, CurrentUOW, sel, out matchedAll, out matchedElems);

                        // matched elements fill the column; with none, the column gets placeholder elements
                        if (matchedElems?.Count > 0)
                        {
                            foreach (GPALElement element in matchedElems)
                                element.AttributeName = sel.AttributeName;

                            tmpColumns.Add(new ReadOnlyCollection<GPALElement>(matchedElems));
                        }
                        else
                        {
                            var placeholder = new List<GPALElement>();
                            int rows = CurrentUOW.RowCount > 0 ? CurrentUOW.RowCount : 0;   // row count from the previous selector
                            if (0 < rows)
                            {
                                for (int i = 0; i < rows; i++)
                                    placeholder.Add(new GPALElement(new Point(0, 0), new Size(1, 1), $"{GPAL.ErrorPlaceholder} : {sel.Name}", BrowserDriver, "GPALElement") { AttributeName = sel.AttributeName, Browser = this, Selector = sel });
                                tmpColumns.Add(new ReadOnlyCollection<GPALElement>(placeholder));
                            }
                        }

                        if (matchedElems?.Count > 0)
                        {
                            CurrentUOW.ColCount++;

                            // set rowcount (found elements) for this iteration
                            if (matchedAll)     // found = matched
                                CurrentUOW.RowCount = Math.Max(CurrentUOW.RowCount, matchedElems.Count);
                            else if (matchedElems?.Count > 0)   // matched match criteria
                            {
                                CurrentUOW.PartialMatch = true;
                                int tmpIdx = 0;
                                foreach (var elem in matchedElems)
                                    if ((tmpIdx = matchedElems.IndexOf(elem)) != -1 && !CurrentUOW.MatchedRowIndexes.ContainsKey(tmpIdx))
                                        CurrentUOW.MatchedRowIndexes.Add(tmpIdx, sel.AttributeName);
                                CurrentUOW.RowCount = CurrentUOW.MatchedRowIndexes.Count;
                            }
                            else    // no elements: placeholder rows up to WithAllThatMatch; int.MaxValue (all) gives none
                                CurrentUOW.RowCount = int.MaxValue != CurrentUOW.WithAllThatMatch ? CurrentUOW.WithAllThatMatch : 0;
                        }
                    }
                    else if (SelectorType.Data == sel.selectorSettings.SelectorType)    // use literal data
                    {
                        CurrentUOW.ColCount++;
                        GPALElement tmpElement = GPAL.Element;
                        tmpElement.Text = sel.SelectorPath;
                        tmpColumns.Add(new ReadOnlyCollection<GPALElement>(new List<GPALElement>(1) { tmpElement }));
                    }
                    else if (SelectorType.DataFunc == sel.selectorSettings.SelectorType)    // use literal data provided by function
                    {
                        CurrentUOW.ColCount++;
                        GPALElement tmpElement = GPAL.Element;
                        tmpElement.Text = sel.selectorSettings.DataFunction();
                        tmpColumns.Add(new ReadOnlyCollection<GPALElement>(new List<GPALElement>(1) { tmpElement }));
                    }

                    // One log line per selector, including selectors that found nothing.
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Grid for [{sel.Name}] page [{pages + 1}] returned [{CurrentUOW.RowCount}] rows of [{tmpColumns.Count()}] columns", this, GPALObjectType.Browser);
                }

                if (CurrentUOW.PartialMatch)
                {
                    columns.Clear();
                    foreach (var colElems in tmpColumns)
                    {
                        var newCol = new List<GPALElement>();
                        // The matched indexes apply to every column, and columns differ in length (a literal column
                        // has one element). Skip indexes past the end; the padding loop fills short columns to
                        // targetRows.
                        foreach (var idx in CurrentUOW.MatchedRowIndexes.Keys)
                            if (idx < colElems.Count)
                                newCol.Add(colElems[idx]);
                        columns.Add(new ReadOnlyCollection<GPALElement>(newCol));
                    }
                }
                else if (0 == columns.Count)
                    columns = tmpColumns;

                // how many elements (rows of data) did we find on this page
                int pageRows = 0 < columns.Count ? columns.Max(c => c.Count) : 0;
                int targetRows = CurrentUOW.RowCount > 0 ? CurrentUOW.RowCount : pageRows;

                // apply WithAllThatMatch per-page cap (only when n fits within one page)
                // total-cap and dedup are handled row-by-row during population below
                if (int.MaxValue != CurrentUOW.WithAllThatMatch && CurrentUOW.WithAllThatMatch <= pageRows)
                    targetRows = Math.Min(targetRows, CurrentUOW.WithAllThatMatch);

                // pad every column to targetRows
                for (int i = 0; i < columns.Count; i++)
                {
                    var col = columns[i].ToList();
                    while (col.Count < targetRows)  // one of the selectors has more rows than this one, so create a placeholder
                    {
                        // the placeholder takes the first element's Css, so scroll into view has a target
                        var firstNonEmpty = columns.FirstOrDefault(c => c.Count > 0)?.FirstOrDefault();
                        var meta = selectorMeta[i];
                        col.Add(new GPALElement(new Point(0, 0), new Size(1, 1), $"{GPAL.ErrorPlaceholder} : {meta.Name}", BrowserDriver, "GPALElement") { AttributeName = meta.AttributeName, Browser = this, Css = firstNonEmpty?.Css, ElementBackendNodeId = (int)(firstNonEmpty?.ElementBackendNodeId) });
                    }
                    columns[i] = new ReadOnlyCollection<GPALElement>(col);
                }
                CurrentUOW.RowCount = targetRows;

                // Populate RetGrid row-major. Dedup (when opted in) is content-based: we key each row by its
                // extracted values, so a row is dropped if its data duplicates one already accumulated (seenRows
                // is seeded from the caller's grid) or one seen earlier this call.
                // WithAllThatMatch total-cap stops accumulation once we have enough unique rows
                for (int r = 0; r < CurrentUOW.RowCount && !capReached; r++)
                {
                    var row = new List<string>();
                    for (int c = 0; c < columns.Count; c++)
                    {
                        var elem = columns[c][r];

                        // whatever item we are scraping, bring it into view
                        // for UX
                        if (true == BrowserSettings.ScrollIntoView)
                            if (false == BrowserSettings.UseHeadless && false == "GPALElement".Equals(elem.TagName))
                                if (true == UseOttoMagic)
                                {
                                    if (false == string.IsNullOrEmpty(elem.Css))
                                        MagicHelper.ScrollIntoView(elem.Css);
                                }
                                else if (true == UsePuppeteer)
                                    PuppeteerClient.ScrollIntoView(elem).Execute();
                                else
                                    ElementHelper.ScrollIntoView(this, elem);

                        if (!string.IsNullOrEmpty(elem.AttributeName))
                            row.Add(elem.GetAttribute(elem.AttributeName));
                        else if (null != elem.TagName)
                        {
                            switch (elem.TagName?.ToLower())
                            {
                                case "a": row.Add(elem.GetAttribute("href")); break;
                                case "img": row.Add(elem.GetAttribute("src")); break;
                                default: row.Add(elem.Text); break;
                            }
                        }
                        else // literal data
                            row.Add(elem.Text);
                    }

                    // DEDUPE: skip this row if its data matches one already in the grid or seen this call
                    if (true == useDedupe && false == seenRows.Add(RowDedupeKey(row)))
                        continue;

                    CurrentUOW.RetGrid.AddRow(row);
                    if (int.MaxValue != CurrentUOW.WithAllThatMatch && CurrentUOW.WithAllThatMatch > pageRows
                        && CurrentUOW.RetGrid.Count() >= CurrentUOW.WithAllThatMatch)
                        capReached = true;
                }

                if (!capReached && pages < CurrentUOW.PageCount - 1)
                {
                    BrowserHelper.TopBrowser(this, false);
                    if (!BrowserHelper.GotoNextPage(this))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"No next page, breaking loop. Retrieved [{pages + 1}] out of [{CurrentUOW.PageCount}] pages.", this, GPALObjectType.Browser);
                        break;
                    }
                }
            }

            // Hand back the results without taking ownership of a grid the caller passed in:
            //  - null in  -> the caller just wants a grid, so give them GPAL's internal one.
            //  - grid in   -> the caller owns it (and may have configured it, e.g. WithDedupeData); add our rows
            //                 into THEIR grid and keep their reference so repeated GetGrid calls can accumulate.
            if (null == returnGrid)
                returnGrid = (IGPALGrid<string>)CurrentUOW.RetGrid;
            else
                returnGrid.Add((IGPALGrid<string>)CurrentUOW.RetGrid);

            return this;
        }

        /// <summary>
        /// Builds a dedup key for a grid row from its cell values. Uses the unit-separator character (U+001F),
        /// which does not occur in scraped text, so two rows collide only when every column value is identical.
        /// </summary>
        private static string RowDedupeKey(List<string> row)
        {
            return string.Join("\u001F", row);
        }

        // -------------------------------------------------------------------------------------
        // Table GetGrid, every engine. GetGrid calls it first. A single selector that resolves to a <table>
        // (or its <tr> rows) is read row by row with ElementHelper.FindWebElements, honoring colspan and
        // rowspan, taking headers from <th>, filling gaps with GPAL.ErrorPlaceholder, and filling
        // CurrentUOW.RetGrid for SaveTo* and paging.
        // OttoMagic/Puppeteer scope a child query by the parent element's Css, so the table
        // and its rows must carry a usable Css for those engines (Selenium scopes by IWebElement).
        // CAVEAT: the tr/td/th queries match descendants, so a table nested in a cell adds its rows and cells to the outer table.
        // -------------------------------------------------------------------------------------
        private bool TryGetGridFromTable(ref IGPALGrid<string> returnGrid)
        {
            // Only a single element-selector request can be table-native. Multiple selectors are the
            // column-per-selector zip, and literal Data/DataFunc columns are never tables.
            if (1 != CurrentUOW.WithSelectorList.Count
                || SelectorType.Selector != CurrentUOW.WithSelectorList[0].selectorSettings.SelectorType)
                return false;

            Selector sel = CurrentUOW.WithSelectorList[0];

            // Probe once to learn the target type. If it is not a <table> or a set of <tr> rows,
            // return false and GetGrid uses the column-per-selector path.
            bool matchedAll; List<GPALElement> matchedElems;
            ReadOnlyCollection<GPALElement> seeds = ElementHelper.FindWebElements(this, CurrentUOW, sel, out matchedAll, out matchedElems);
            string tag = (null != seeds && seeds.Count > 0) ? seeds[0].TagName?.ToLowerInvariant() : null;
            bool isTable = "table" == tag;
            bool isRows = "tr" == tag;
            if (!isTable && !isRows)
                return false;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: [{sel.Name}] resolved to <{tag}>; extracting the table structurally.", this, GPALObjectType.Browser);

            var seenRows = new HashSet<string>();
            List<string> headers = null;
            bool capReached = false;
            int cap = (int.MaxValue == CurrentUOW.WithAllThatMatch)
                        ? int.MaxValue : CurrentUOW.WithAllThatMatch;

            for (int page = 0; page < CurrentUOW.PageCount && !capReached; page++)
            {
                if (page > 0)
                {
                    seeds = ElementHelper.FindWebElements(this, CurrentUOW, sel, out matchedAll, out matchedElems);
                    if (null == seeds || 0 == seeds.Count)
                        break;
                }
                BrowserHelper.CheckDocumentReady(this);

                // Gather the body row elements for this page (and headers, once).
                var bodyRows = new List<GPALElement>();
                if (isTable)
                {
                    foreach (GPALElement table in seeds)
                    {
                        var headerRows = FindWithin(table, "thead tr");
                        var tableBody = FindWithin(table, "tbody tr");
                        if (0 == tableBody.Count && 0 == headerRows.Count)
                        {
                            // no thead/tbody: treat a leading all-<th> row as the header
                            var all = FindWithin(table, "tr");
                            if (all.Count > 0 && RowIsAllHeader(all[0]))
                            {
                                headerRows.Add(all[0]);
                                tableBody.AddRange(all.Skip(1));
                            }
                            else
                                tableBody.AddRange(all);
                        }
                        else if (0 == headerRows.Count && 0 < tableBody.Count && true == RowIsAllHeader(tableBody[0]))
                        {
                            // No thead and an all-<th> first row. The browser wraps loose rows in a tbody, so the
                            // header is the first tbody row.
                            headerRows.Add(tableBody[0]);
                            tableBody.RemoveAt(0);
                        }
                        if (null == headers && headerRows.Count > 0)
                        {
                            var hmatrix = BuildTableMatrix(headerRows);
                            if (hmatrix.Count > 0)
                                headers = hmatrix[0];   // first header row, span-aligned to the data
                        }
                        bodyRows.AddRange(tableBody);
                    }
                }
                else // the seeds themselves are the rows
                {
                    bodyRows.AddRange(seeds);
                }

                // Build a rectangular, span-aware matrix of cell text for these rows.
                List<List<string>> matrix = BuildTableMatrix(bodyRows);

                foreach (List<string> row in matrix)
                {
                    string key = string.Join("", row);
                    if (!seenRows.Add(key))
                        continue;   // duplicate row from a prior page/scroll pass
                    CurrentUOW.RetGrid.AddRow(row);
                    if (CurrentUOW.RetGrid.Count() >= cap)
                    {
                        capReached = true;
                        break;
                    }
                }

                if (!capReached && page < CurrentUOW.PageCount - 1)
                {
                    BrowserHelper.TopBrowser(this, false);
                    if (!BrowserHelper.GotoNextPage(this))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: no next page after page [{page + 1}].", this, GPALObjectType.Browser);
                        break;
                    }
                }
            }

            // Store the table's column names in ControlHeaderList, as the Application side does for controls.
            // WithHeader clears HeaderList, and ResolveHeaderColumns prefers these names over generated ones.
            if (null != headers && headers.Count > 0)
            {
                CurrentUOW.ControlHeaderList = headers;

                if (null == CurrentUOW.HeaderList || 0 == CurrentUOW.HeaderList.Count)
                    CurrentUOW.HeaderList = headers;
            }

            // Expose the table's column names on ColumnNames.
            ColumnNames = null == headers ? new List<string>() : new List<string>(headers);

            returnGrid = (IGPALGrid<string>)CurrentUOW.RetGrid;
            return true;
        }

        // Engine-agnostic scoped query: elements matching css *within* a parent element, using the
        // same ElementHelper.FindWebElements dispatch (Selenium/Puppeteer/OttoMagic) as the rest of GPAL.
        private List<GPALElement> FindWithin(GPALElement parent, string css)
        {
            Selector scoped = GPAL.Selector.WithCSS(css).ToGPALObject();
            bool matchedAll; List<GPALElement> matchedElems;
            ReadOnlyCollection<GPALElement> found = ElementHelper.FindWebElements(this, CurrentUOW, scoped, out matchedAll, out matchedElems, webElement: parent);

            // OttoMagic and Puppeteer search inside the parent by switching into it, and they find the next parent
            // inside wherever the last switch left them. Left in this row, the next row is not found, every row reads
            // the cells of this one, and the selectors after the grid search only this row.
            LeaveSearchedElement();

            return (null != found) ? found.ToList() : new List<GPALElement>();
        }

        /// <summary>
        /// OttoMagic and Puppeteer search inside an element by switching into it, and stay there. This goes back to
        /// the document, then into the unit of work's frames and shadow roots, as a search with a context does.
        /// Selenium searches from the element it is handed and is never inside one.
        /// </summary>
        internal void LeaveSearchedElement()
        {
            if (true == UseOttoMagic || true == UsePuppeteer)
            {
                if (true == UseOttoMagic)
                    MagicHelper.SwitchToDefaultContent();
                else
                    PuppeteerClient.SwitchToDefaultContent().Execute();

                foreach (UnitOfWork.WebElementWithType contextType in CurrentUOW.ContextPath ?? new List<UnitOfWork.WebElementWithType>())
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToElement(contextType.gPalElement.Css);
                        else
                            PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                    }
                    else if (ElementType.ShadowRoot == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                        else
                            PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();
                    }
                }
            }
        }

        // True when a <tr>'s cells are all <th> (a header row when there is no <thead>).
        private bool RowIsAllHeader(GPALElement tr)
        {
            var cells = FindWithin(tr, "td, th");
            if (0 == cells.Count)
                return false;
            foreach (GPALElement c in cells)
                if (!"th".Equals(c.TagName, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        // Turn <tr> elements into a rectangular grid of cell strings, honoring colspan and rowspan.
        // Spanned cells repeat their text across covered positions; structural gaps and short rows
        // are filled with GPAL.ErrorPlaceholder so every row has the same column count.
        private List<List<string>> BuildTableMatrix(List<GPALElement> rowElems)
        {
            var matrix = new List<List<string>>();
            // pending rowspans: column index -> (remaining future rows, text)
            var pending = new Dictionary<int, KeyValuePair<int, string>>();

            foreach (GPALElement tr in rowElems)
            {
                var cells = FindWithin(tr, "td, th");
                var row = new List<string>();
                int col = 0, ci = 0;

                while (ci < cells.Count || pending.Keys.Any(k => k >= col))
                {
                    KeyValuePair<int, string> carry;
                    if (pending.TryGetValue(col, out carry))
                    {
                        row.Add(carry.Value);
                        int left = carry.Key - 1;
                        if (left > 0) pending[col] = new KeyValuePair<int, string>(left, carry.Value);
                        else pending.Remove(col);
                        col++;
                        continue;
                    }

                    if (ci < cells.Count)
                    {
                        GPALElement cell = cells[ci++];
                        string text = CellText(cell);
                        int cspan = ParseSpan(cell, "colspan");
                        int rspan = ParseSpan(cell, "rowspan");
                        for (int c = 0; c < cspan; c++)
                        {
                            row.Add(text);
                            if (rspan > 1) pending[col] = new KeyValuePair<int, string>(rspan - 1, text);
                            col++;
                        }
                    }
                    else
                    {
                        // a rowspan carry sits further right but there is no cell here: keep alignment
                        row.Add(GPAL.ErrorPlaceholder);
                        col++;
                    }
                }

                matrix.Add(row);
            }

            // pad every row to the widest so the grid is rectangular
            int width = 0;
            foreach (var r in matrix) if (r.Count > width) width = r.Count;
            foreach (var r in matrix) while (r.Count < width) r.Add(GPAL.ErrorPlaceholder);

            return matrix;
        }

        private int ParseSpan(GPALElement cell, string attr)
        {
            int n;

            // An OttoMagic element carries no colspan or rowspan, so OttoMagic reads it from the page
            string v = true == UseOttoMagic ? MagicHelper.GetAttribute(cell.Css, attr) : cell.GetAttribute(attr);
            return (int.TryParse(v, out n) && n > 0) ? n : 1;
        }

        // Cell text read the same way the column-zip path reads cells, so both paths agree.
        private string CellText(GPALElement cell)
        {
            if (!string.IsNullOrEmpty(cell.AttributeName))
                return cell.GetAttribute(cell.AttributeName);
            switch (cell.TagName?.ToLowerInvariant())
            {
                case "a": return cell.GetAttribute("href");
                case "img": return cell.GetAttribute("src");
                default: return cell.Text ?? string.Empty;
            }
        }
        /// <summary>
        /// Hand back what .Fetch retrieved, as a JSON array holding one response body per page requested, so a
        /// single page and twenty pages come out in the same shape.
        /// </summary>
        /// <param name="data">Receives the JSON array of response bodies</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveTo(ref string data)
        {
            List<string> values = new List<string>();

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Handing back [{CurrentUOW.FetchResults.Count}] fetch responses.", this, GPALObjectType.Browser);

            // Always build a valid JSON array: a JSON body as is, anything else (an html block page, a body with a
            // leading http warning) as a JSON string.
            foreach (string body in CurrentUOW.FetchResults)
                values.Add(true == StringFormatDetector.IsJson(body.Trim()) ? body : Newtonsoft.Json.JsonConvert.SerializeObject(body));

            data = "[" + string.Join(",", values) + "]";
            return this;
        }
        /// <summary>
        /// Save the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension, e.g. .csv/.tsv/.txt/.xlsx/.json, or its explicit file settings).<br/>
        /// The single save syntax; the output format and delimiter come from the GPALFile.
        /// </summary>
        public IAllowBrowserActionOrAnySelector SaveTo(GPALFile file)
        {
            //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of page data to [{file?.Filename}].", this, GPALObjectType.Browser);

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
        /// spreadsheet pastes as cells. A single element is a one-cell grid, so its text goes on as is.
        /// </summary>
        /// <example>
        /// <code>
        ///     myBrowser
        ///         .WithSelector((Selector)"table.results")
        ///         .GetGrid(ref grid)
        ///         .SaveTo(GPAL.Clipboard);
        /// </code>
        /// </example>
        /// <param name="clipboard">The clipboard, GPAL.Clipboard</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector SaveTo(IAllowClipboardInput clipboard)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Copying [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of page data to the clipboard.", this, GPALObjectType.Browser);

            clipboard.WithInput(CurrentUOW.RetGrid).SetClipboard();
            return this;
        }

        /// <summary>
        /// Append the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension or explicit file settings). Headers are not repeated on append.
        /// </summary>
        public IAllowBrowserActionOrAnySelector AppendTo(GPALFile file)
        {
            //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Appending [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of page data to [{file?.Filename}].", this, GPALObjectType.Browser);

            GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file)).AppendTo(file);
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

            // Next, the control's or table's own names from ControlHeaderList, kept apart because WithHeader clears
            // HeaderList.
            List<string> controlHeaders = CurrentUOW.ControlHeaderList ?? new List<string>();

            for (int i = resolved.Count; i < controlHeaders.Count; i++)
                resolved.Add(controlHeaders[i]);

            // The converter writes only named columns, so add column1..columnN names to cover the widest row. No
            // data is dropped.
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
        /// Makes every column name non-blank and unique; the writer keys cells by name, so duplicates merge.
        /// </summary>
        /// <remarks>
        /// A blank name becomes columnN and a repeat gets a number, so Size, Size becomes Size, Size2.
        /// </remarks>
        /// <param name="columns">The names as resolved, which may hold blanks and repeats</param>
        /// <returns>The same names, each present and each used once</returns>
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
        /// Column names the last GetGrid read from a table's header row.<br/>
        /// Empty for column-per-selector reads or a table without headers; a saved file then uses selector names,
        /// then generated ones.
        /// </summary>
        public IReadOnlyList<string> ColumnNames { get; private set; } = new List<string>();
    }
}
