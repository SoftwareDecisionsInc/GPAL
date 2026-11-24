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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;
using System.Net.NetworkInformation;
using System.Net;
using System.Text.RegularExpressions;

namespace GenerallyPositive.Browser
{
    public partial class BrowserHelper
    {
        /// <summary>
        /// Finds the elements matching the unit of work's selectors, arranges them into a grid of rows and columns,
        /// and fills each editable element with the corresponding token from <paramref name="tokens"/> using
        /// <paramref name="writeMode"/> (Overwrite, Insert or Append).<br/>
        /// Row i of elements takes row i of tokens, and CallAfterFillIn (if set) runs after each row. When rows of
        /// tokens remain, the elements are found again and filled from the next rows, until every row is used.
        /// </summary>
        /// <param name="browser">Browser whose CurrentUOW selectors are used to locate the input elements</param>
        /// <param name="tokens">Grid (rows of columns) of string values to fill into the matched elements</param>
        /// <param name="writeMode">How to write each token: Overwrite, Insert or Append</param>
        public static void FillInWithTokens(Browser browser, IGPALGrid<string> tokens, WriteMode writeMode)
        {
            int tokenIdx = 0;
            List<ReadOnlyCollection<GPALElement>> rowsOfColumns = new List<ReadOnlyCollection<GPALElement>>();
            List<ReadOnlyCollection<GPALElement>> rowsOfElements = new List<ReadOnlyCollection<GPALElement>>();
            // each selector's interaction settings, one entry per column of elements
            List<SmallSelectorNode> interactionInfo = new List<SmallSelectorNode>();

            while (true)
            {

                int rowCnt = 0;
                int lastRowCnt = 0;
                int elementIdx = 0;

                interactionInfo.Clear();    // each pass, after CallAfterFillIn returns, starts fresh
                rowsOfElements.Clear();
                rowsOfColumns.Clear();
                browser.CurrentUOW.MatchedRowIndexes.Clear();
                browser.CurrentUOW.ColCount = 0;
                browser.CurrentUOW.RowCount = 0;    // Math.Max below only ever raises it, so a pass that finds fewer elements than the last would size the grid for rows it has no elements to fill
                browser.CurrentUOW.PartialMatch = false;    // only ever |= true below, so one pass that did not match everything would put every later pass down the partial-match path

                // .InElement named containers: row i of elements is container i, found below as GetGrid finds them
                UnitOfWork.WebElementWithType inElement = browser.CurrentUOW.ContextPath?
                    .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

                // the context FillInFrom was called in, put back after CallAfterFillIn, which may call InMainDom
                List<UnitOfWork.WebElementWithType> passContextPath = browser.CurrentUOW.ContextPath?.ToList();

                if (null == inElement)
                {
                    foreach (Selector sel in browser.CurrentUOW.WithSelectorList)
                    {
                        if (SelectorType.Selector != sel.SelectorType)
                            continue;   // we don't look up data literals

                        List<GPALElement> matchedElems = null;
                        ReadOnlyCollection<GPALElement> tmpElems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, sel, out bool matchedAll, out matchedElems);
                        rowsOfElements.Add(tmpElems);
                        browser.CurrentUOW.ColCount++;

                        if (false == matchedAll)
                            browser.CurrentUOW.PartialMatch |= true;

                        if (0 < tmpElems?.Count && true == matchedAll)
                        {
                            // RowCount is the total count of elements found or just the WithAllThatMatch row count
                            browser.CurrentUOW.RowCount = Math.Max(browser.CurrentUOW.RowCount, (int.MaxValue == browser.CurrentUOW.WithAllThatMatch ? tmpElems.Count : browser.CurrentUOW.WithAllThatMatch));
                        }
                        else if (0 < matchedElems?.Count && false == matchedAll)
                        {
                            // partial match: record the matched row indexes. A row any selector matched is kept; selectors
                            // are ORed, never ANDed.
                            int tmpIdx = 0;
                            foreach (GPALElement webElement in matchedElems)
                                if (-1 != (tmpIdx = tmpElems.IndexOf(webElement)))
                                    if (false == browser.CurrentUOW.MatchedRowIndexes.Contains(new KeyValuePair<int, string>(tmpIdx, sel.AttributeName)))
                                        browser.CurrentUOW.MatchedRowIndexes.Add(tmpIdx, sel.AttributeName);

                            // the row count is the number of matched rows, or WithAllThatMatch when it is set
                            browser.CurrentUOW.RowCount = (int.MaxValue == browser.CurrentUOW.WithAllThatMatch ? browser.CurrentUOW.MatchedRowIndexes.Count : browser.CurrentUOW.WithAllThatMatch);
                        }

                        // if we returned elements, add an entry for this selector in interactionInfo, we consume it below
                        if (0 < matchedElems?.Count || 0 < tmpElems?.Count)
                            interactionInfo.Add(new SmallSelectorNode() { InteractionType = sel.InteractionType, OffsetX = sel.OffsetX, OffsetY = sel.OffsetY, DeltaX = sel.DeltaX, DeltaY = sel.DeltaY });
                    }
                }

                // we have partial matches, so construct the grid only from matched rows
                if (true == browser.CurrentUOW.PartialMatch)
                {
                    List<GPALElement> newColumn = new List<GPALElement>();

                    browser.CurrentUOW.RowCount = browser.CurrentUOW.MatchedRowIndexes.Count;
                    // build each column from the matched rows only
                    foreach (ReadOnlyCollection<GPALElement> webElements in rowsOfElements)
                    {
                        foreach (KeyValuePair<int, string> idx in browser.CurrentUOW.MatchedRowIndexes)
                            newColumn.Add(webElements[idx.Key]);
                        rowsOfColumns.Add(new ReadOnlyCollection<GPALElement>(newColumn));
                        newColumn = new List<GPALElement>();
                    }
                }
                else
                {
                    foreach (ReadOnlyCollection<GPALElement> webElements in rowsOfElements)
                        rowsOfColumns.Add(webElements);
                }

                // each pass finds new elements, so the old grid is stale
                browser.CurrentUOW.ElementGrid.Clear();

                // we should have one column for each selector (that found elements)
                if (0 != browser.CurrentUOW.ColCount)
                {
                    for (int cnt = 0; cnt < browser.CurrentUOW.RowCount; cnt++)
                        browser.CurrentUOW.ElementGrid.AddRow(new List<UnitOfWork.ElementNode>(browser.CurrentUOW.ColCount));    // create a row of columns for the number of elements found

                    int columnIdx = 0; ;  // start with the first column in the row

                    // pivot the columns into rows of the element grid
                    foreach (ReadOnlyCollection<GPALElement> column in rowsOfColumns)
                    {
                        rowCnt = lastRowCnt;    // if we are on subsequent pages, we have to add at the end of the list, up to WithAllThatMatch row count (or all rows)
                        foreach (GPALElement elem in column)
                        {
                            if (true == ElementHelper.IsElementEditable(elem))
                                browser.CurrentUOW.ElementGrid[rowCnt].Add(
                                    new UnitOfWork.ElementNode()
                                    {
                                        GPALElement = elem,
                                        InteractionType = interactionInfo[columnIdx].InteractionType,
                                        OffsetX = interactionInfo[columnIdx].OffsetX,
                                        OffsetY = interactionInfo[columnIdx].OffsetY
                                    });

                            rowCnt++;

                            // stop at the WithAllThatMatch row count; the grid has that many rows
                            if (int.MaxValue != browser.CurrentUOW.WithAllThatMatch && rowCnt >= browser.CurrentUOW.WithAllThatMatch)
                                break;
                        }

                        // each column that has elements has its own entry in interactionInfo
                        if (0 < column.Count)
                            columnIdx++;
                    }
                }

                // With .InElement the containers are found again for each pass, as GetGrid finds them for each page,
                // and each selector is found inside each container. A container without the element gets an empty
                // cell, and the fill below passes over it with its token, so token i still goes to column i
                if (null != inElement)
                {
                    browser.RefreshContainers(inElement, true);

                    List<Selector> selectors = browser.CurrentUOW.WithSelectorList
                        .Where(sel => SelectorType.Selector == sel.SelectorType).ToList();

                    browser.CurrentUOW.RowCount = browser.WalkInElementIfAny(() =>
                    {
                        List<UnitOfWork.ElementNode> row = new List<UnitOfWork.ElementNode>(selectors.Count);

                        foreach (Selector sel in selectors)
                        {
                            ElementHelper.FindWebElements(browser, browser.CurrentUOW, sel, out _, out List<GPALElement> matchedElems);

                            // the first match in the container, as GetGrid takes it
                            GPALElement elem = 0 < matchedElems?.Count ? matchedElems[0] : null;

                            row.Add(null != elem && true == ElementHelper.IsElementEditable(elem)
                                ? new UnitOfWork.ElementNode() { GPALElement = elem, InteractionType = sel.InteractionType, OffsetX = sel.OffsetX, OffsetY = sel.OffsetY }
                                : null);
                        }

                        browser.CurrentUOW.ElementGrid.AddRow(row);
                    });

                    // OttoMagic and Puppeteer fill an element by its Css, which names its place in the page, so they
                    // leave the last container searched
                    browser.LeaveSearchedElement();

                    browser.CurrentUOW.ColCount = selectors.Count;
                }

                int tokenIdxAtPassStart = tokenIdx;

                // fill the grid from the next row of tokens
                if (tokenIdx < tokens.Count())
                {
                    // row i of elements takes row i of tokens, a token per element, and CallAfterFillIn runs after each
                    // row; the next pass finds the elements again for the rows of tokens left
                    foreach (List<UnitOfWork.ElementNode> elementNode in browser.CurrentUOW.ElementGrid)    // get one row of elements
                    {
                        if (tokenIdx == tokens.Count())
                            break;

                        // a row WithAllThatMatch made room for but nothing filled takes no row of tokens; a container
                        // takes its row whatever it holds, so the rows after it stay with their containers
                        if (null == inElement && false == elementNode.Any(node => null != node))
                            continue;

                        elementIdx = 0;
                        List<string> currentRow = tokens[tokenIdx];

                        foreach (string token in currentRow)                                            // iterate over tokens, filling in the corresponding input/textarea webelement
                        {
                            if (elementIdx < elementNode.Count && null != elementNode[elementIdx])
                            {
                                ElementHelper.ScrollIntoView(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].InteractionType);

                                if (true == browser.UseOttoMagic)
                                {
                                    if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                        ElementHelper.HardwareFillInFrom(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                    else
                                        switch (writeMode)
                                        {
                                            case WriteMode.Append:
                                                browser.BrowserSettings.MagicHelper.FillInAppend(elementNode[elementIdx].GPALElement.Css, token.ToString());
                                                break;

                                            case WriteMode.Insert:
                                                browser.BrowserSettings.MagicHelper.FillInInsert(elementNode[elementIdx].GPALElement.Css, token.ToString());
                                                break;

                                            case WriteMode.Overwrite:
                                                browser.BrowserSettings.MagicHelper.FillInOverwrite(elementNode[elementIdx].GPALElement.Css, token.ToString());
                                                break;
                                        }
                                }
                                else if (true == browser.UsePuppeteer)
                                {
                                    if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                        ElementHelper.HardwareFillInFrom(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                    else
                                        switch (writeMode)
                                        {
                                            case WriteMode.Append:
                                                browser.PuppeteerClient.FillInAppend(elementNode[elementIdx].GPALElement.Css).WithText(token.ToString()).Execute();
                                                break;

                                            case WriteMode.Insert:
                                                browser.PuppeteerClient.FillInInsert(elementNode[elementIdx].GPALElement.Css).WithText(token.ToString()).Execute();
                                                break;

                                            case WriteMode.Overwrite:
                                                browser.PuppeteerClient.FillInOverwrite(elementNode[elementIdx].GPALElement.Css).WithText(token.ToString()).Execute();
                                                break;
                                        }
                                }
                                else    // selenium
                                {
                                    if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                        ElementHelper.HardwareFillInFrom(browser, elementNode[elementIdx].GPALElement, elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                    else if (InteractionType.JavaScript == elementNode[elementIdx].InteractionType)
                                        ElementHelper.JavaScriptFillInFrom(browser, elementNode[elementIdx].GPALElement, token.ToString(), writeMode);
                                    else
                                        ElementHelper.SeleniumFillInFrom(browser, elementNode[elementIdx].GPALElement, token.ToString(), writeMode);
                                }

                                elementIdx++;   // increment the column [element] count for each token. if we have a token, we are expecting elements for it.
                            }
                            else if (elementIdx < elementNode.Count)
                            {
                                // a container without this column's element: its token goes unused and the next
                                // token goes to the next column
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No element for token [{token}] in column [{elementIdx + 1}] of row [{tokenIdx + 1}]", browser, GPALObjectType.Browser);
                                elementIdx++;

                                continue;   // nothing was written, so there is nothing to report below
                            }
                            else
                                // more tokens than elements in this row
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unused token [{token}] from tokens [{string.Join(",", currentRow)}] in row [{tokenIdx + 1}]", browser, GPALObjectType.Browser);

                            try
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] text [{token.ToString()}] in element [{elementNode[elementIdx - 1].GPALElement.TagName}]", browser, GPALObjectType.Browser);
                            }
                            catch
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"NOT WRITTEN: [{writeMode}] text [{token.ToString()}] in element [NOT FOUND].", browser, GPALObjectType.Browser);
                            }
                        }

                        // fewer tokens than elements in this row: warn for each element left
                        for (; elementIdx < elementNode.Count; elementIdx++)
                            if (null != elementNode[elementIdx])
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No token for control: <[{elementNode[elementIdx].GPALElement.GetType()}]> : [{elementNode[elementIdx].GPALElement.Text}]", browser, GPALObjectType.Browser);

                        UnitOfWork safeUOW = browser.CurrentUOW;
                        CallIfStatus handled = 0;

                        // Only CallIfStatus.Terminate is acted on; any other return goes on to the next row of tokens
                        if (null != browser.CurrentUOW.CallAfterFillIn && (true == rowsOfColumns.Any() || null != inElement))
                        {
                            // with .InElement the handler's own searches run inside the container just filled; every
                            // container takes a row, so it is the row's place in this pass
                            if (null != inElement)
                                inElement.current = tokenIdx - tokenIdxAtPassStart;

                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallAfterFillIn [{browser.CurrentUOW.CallAfterFillIn.Method.Name}]", browser, GPALObjectType.Browser);
                            handled = browser.CurrentUOW.CallAfterFillIn(browser, tokens, tokenIdx);

                            if (null != inElement)
                                inElement.current = 0;
                        }

                        // KLUDGE: the CallAfterFillIn handler can set a new current unit of work, so this one is put back
                        browser.CurrentUOW = safeUOW;

                        // with .InElement the next row is in the next container, inside the context FillInFrom was
                        // called in, whatever the handler did to the context
                        if (null != inElement)
                        {
                            browser.CurrentUOW.ContextPath.Clear();
                            browser.CurrentUOW.ContextPath.AddRange(passContextPath);

                            // a search the handler ran inside the container leaves OttoMagic and Puppeteer inside it
                            browser.LeaveSearchedElement();
                        }

                        // Elements can go stale from CallAfterFillIn handler actions; the next pass finds them again
                        // test if elements still valid by testing the first one
                        //try
                        //{
                        //    ((UnitOfWork.ElementNode)(browser.CurrentUOW.ElementGrid[0])[0]).GPALElement.GetAttribute("tag");
                        //} 
                        //catch (Exception ex)   // failed, invalidate elementgrid
                        //{
                        //    browser.CurrentUOW.ElementGrid.Clear();
                        //    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CallAfterFillIn handler [{browser.CurrentUOW.CallAfterFillIn.GetInvocationList()[0].Method.Name}] invalidated browser elements, clearing cache. Exception follows: [{ex.Message}]", browser, GPALObjectType.Browser, ex);
                        //    break;
                        //}

                        if (CallIfStatus.Terminate == handled)
                        {
                            string str = $"CallAfterFillIn handler [{browser.CurrentUOW.CallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.";
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                            throw new GPALException($"{GPAL.MyMethodName()}: " + str);
                        }

                        tokenIdx++;
                    }
                }
                else
                    break;

                lastRowCnt += rowCnt;

                // out of tokens: stop. A pass that found no row to fill stops too, since the next pass finds the same.
                // Otherwise the next pass finds the same selectors again and fills them from the next row of tokens
                if (tokenIdx < tokens.Count() && tokenIdx == tokenIdxAtPassStart)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No elements to fill, so [{tokens.Count() - tokenIdx}] row(s) of tokens are not used", browser, GPALObjectType.Browser);

                if (tokenIdx == tokens.Count() || tokenIdx == tokenIdxAtPassStart)
                    break;
            }
        }
    }
}
