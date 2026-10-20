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

using GenerallyPositive.Application;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;
using System.Windows.Automation;
using System.Collections.ObjectModel;

namespace GenerallyPositive
{
    class ApplicationHelper
    {
        /// <summary>
        /// Advance to the next page of results during GetGrid paging. Clicks the configured
        /// NextPageButton control. Infinite scroll is not supported for desktop applications.
        /// </summary>
        /// <returns>true if a next page was requested; false if there is no next page.</returns>
        public static bool GotoNextPage(Application.Application application)
        {
            UnitOfWork uow = application.CurrentUOW;

            if (null != uow.NextPageButton)
            {
                bool matchedAll; List<GPALAutomationElement> matched;
                var found = ElementHelper.FindElements(application, uow, uow.NextPageButton, out matchedAll, out matched);
                if (null == found || 0 == found.Count)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "GetGrid: next-page control not found; stopping paging.", application, GPALObjectType.Application);
                    return false;
                }
                ElementHelper.Click(application, uow.NextPageButton, found[0], ClickType.LeftClick, ModifierKeys.NONE);
                return true;
            }

            if (uow.InfiniteScroll)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "GetGrid WithInfiniteScroll is not supported for desktop applications; retrieving a single page.", application, GPALObjectType.Application);

            return false;
        }

        /// <summary>
        /// Brings the given process's main window to the foreground/top of the z-order, if it is not already.
        /// </summary>
        /// <param name="process">The process whose main window should be brought to the top</param>
        public static void TopProcess(Process process)
        {
            // A launcher hands its work to a running process and exits, so it has no window to bring forward. Not a
            // failure; do not report it.
            if (null == process || true == process.HasExited || IntPtr.Zero == process.MainWindowHandle)
                return;

            try
            {
                IntPtr foreground = GetForegroundWindow();

                if (process.MainWindowHandle == foreground) return;

                // A menu, dropdown or modal dialog is its own window, so the foreground window is not the main window.
                // Bringing the main window to the top here would close the popup before its selector runs.
                GetWindowThreadProcessId(foreground, out int owner);

                if (owner == process.Id) return;
            }
            catch
            {
                // can't determine who is the top window, so go ahead and top it
            }

            try
            {
                // Show window https://docs.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow
                ShowWindow(process.MainWindowHandle, 1);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min

                //// Simulate an "VK_ALT" key press.
                //keybd_event((byte)VK_ALT, 0x45, EXTENDEDKEY | 0, 0);

                //// Simulate an "VK_ALT" key release.
                //keybd_event((byte)VK_ALT, 0x45, EXTENDEDKEY | KEYUP, 0);

                // Show window in forground.
                SetForegroundWindow(process.MainWindowHandle);
            }
            catch (Exception ex)
            {

                try
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to top process [{process.ProcessName}], attempting a hardware click to top.", null, GPALObjectType.None, ex);
                    Rectangle rect;
                    GetWindowRect(process.MainWindowHandle, out rect);
                    HardwareHelper.HardwareClick(rect.X + 10, rect.Y + 10, ClickType.LeftClick);
                }
                catch
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to top process, unable to get process information using GetWindowRect.", null, GPALObjectType.None, ex);
                }
            }
        }
        /// <summary>
        /// 
        /// Enter tokens into form controls.
        /// </summary>
        /// <param name="application"></param>
        /// <param name="tokens"></param>
        /// <param name="writeMode"></param>
        /// <returns>0 - we are done<br>1 - loop again</br></returns>
        public static void FillInWithTokens<T>(Application.Application application, IGPALGrid<T> tokens, WriteMode writeMode)
        {
            while (true)
            {
                int tokenIdx = 0;
                List<ReadOnlyCollection<GPALAutomationElement>> rowsOfColumns = new List<ReadOnlyCollection<GPALAutomationElement>>();
                List<ReadOnlyCollection<GPALAutomationElement>> rowsOfElements = new List<ReadOnlyCollection<GPALAutomationElement>>();
                // a column-wise list of the selector info to match with the column of matched/returned elements to know how to interact with that column of elemnts
                // of course selectors are always a 'column'
                List<SmallSelectorNode> interactionInfo = new List<SmallSelectorNode>();

                // waitfor did not match if returns false
                int rowCnt = 0;
                // A local, as in the browser version, so every grid fill starts from zero.
                int lastRowCnt = 0;
                int elementIdx = 0;

                rowsOfElements.Clear();
                rowsOfColumns.Clear();
                application.CurrentUOW.MatchedRowIndexes.Clear();
                application.CurrentUOW.ColCount = 0;
                application.CurrentUOW.RowCount = 0;        // Math.Max below only ever raises it, so a pass that finds fewer elements than the last would size the grid for rows it has no elements to fill
                application.CurrentUOW.PartialMatch = false; // only ever |= true below, so one pass that did not match everything would put every later pass down the partial-match path

                foreach (Selector sel in application.CurrentUOW.WithSelectorList)
                {
                    // retrieve one column, which can span multiple rows if it repeats
                    List<GPALAutomationElement> matchedElems;
                    ReadOnlyCollection<GPALAutomationElement> tmpElems = ElementHelper.FindElements(application, application.CurrentUOW, sel, out bool matchedAll, out matchedElems);
                    rowsOfElements.Add(tmpElems);
                    application.CurrentUOW.ColCount++;

                    if (false == matchedAll)
                        application.CurrentUOW.PartialMatch |= true;

                    if (0 < tmpElems?.Count && true == matchedAll)
                    {
                        // RowCount is the total count of elements found or just the WithAllThatMatch row count
                        application.CurrentUOW.RowCount = Math.Max(application.CurrentUOW.RowCount, (int.MaxValue == application.CurrentUOW.WithAllThatMatch ? tmpElems.Count : application.CurrentUOW.WithAllThatMatch));
                    }
                    else if (0 < matchedElems?.Count && false == matchedAll)
                    {
                        // partial match
                        int tmpIdx = 0;
                        // add the indices of the matched items
                        // when we build the grid, we will only get rows that matched
                        // CAVEAT: NO 'AND' on selector MATCH
                        // only 'OR' operator, all those that match on different selectors
                        foreach (GPALAutomationElement webElement in matchedElems)
                            if (-1 != (tmpIdx = tmpElems.IndexOf(webElement)))
                                if (false == application.CurrentUOW.MatchedRowIndexes.Contains(new KeyValuePair<int, string>(tmpIdx, sel.AttributeName)))
                                    application.CurrentUOW.MatchedRowIndexes.Add(tmpIdx, sel.AttributeName);

                        // matched row indexes is the row count we will extract data from on partial matches, so however many indices we have, that is our MAX row count
                        // up to WithAllThatMatch row count if not int.MaxValue
                        application.CurrentUOW.RowCount = (int.MaxValue == application.CurrentUOW.WithAllThatMatch ? application.CurrentUOW.MatchedRowIndexes.Count : application.CurrentUOW.WithAllThatMatch);
                    }

                    // if we returned elements, add an entry for this selector in interactionInfo, we consume it below
                    if (0 < matchedElems?.Count || 0 < tmpElems?.Count)
                        interactionInfo.Add(new SmallSelectorNode() { InteractionType = sel.InteractionType, OffsetX = sel.DeltaX, OffsetY = sel.DeltaY, DeltaX = sel.DeltaY, DeltaY = sel.DeltaY });
                }

                // we have partial matches from one or more selectors, so construct the grid only from matched rows
                if (true == application.CurrentUOW.PartialMatch)
                {
                    List<GPALAutomationElement> newColumn = new List<GPALAutomationElement>();

                    application.CurrentUOW.RowCount = application.CurrentUOW.MatchedRowIndexes.Count;
                    // get a list of all the elements in one column
                    // iterate thru each column pulling out matched rows
                    // create the return grid as only matched elements
                    foreach (ReadOnlyCollection<GPALAutomationElement> rowOfElements in rowsOfElements)
                    {
                        // create a newCOlumn from all elements via indexes where rows 'matched' on some selector value
                        // we have multiple selectors contributing to matched rows based upon that selectors 'match' criteria
                        foreach (KeyValuePair<int, string> idx in application.CurrentUOW.MatchedRowIndexes)
                            newColumn.Add(rowOfElements[idx.Key]);
                        rowsOfColumns.Add(new ReadOnlyCollection<GPALAutomationElement>(newColumn));
                        newColumn = new List<GPALAutomationElement>();
                    }
                }
                else
                {
                    foreach (ReadOnlyCollection<GPALAutomationElement> rowOfElements in rowsOfElements)
                        rowsOfColumns.Add(rowOfElements);
                }

                // if we are looping, we have a new 'page' and new elements to interact with, our old list is stale
                application.CurrentUOW.ElementGrid.Clear();

                // we should have one column for each selector (that found elements)
                if (0 != application.CurrentUOW.ColCount)
                {
                    for (int cnt = 0; cnt < application.CurrentUOW.RowCount; cnt++)
                        application.CurrentUOW.ElementGrid.AddRow(new List<UnitOfWork.ElementNode>(application.CurrentUOW.ColCount));    // create rows in the grid for each row we will return with capacity for colCount columns

                    int columnIdx = 0;  // start with the first column in the row

                    // grab a row of columns 
                    // construct our grid by pivoting the data from columns, to rows
                    // iterate each row of columns [elements], add a column (element) to each row of the element grid
                    foreach (ReadOnlyCollection<GPALAutomationElement> rowOfColumns in rowsOfColumns)
                    {
                        rowCnt = lastRowCnt;    // if we are on subsequent pages, we have to add at the end of the list, up to WithAllThatMatch row count (or all rows)
                                                // add this column to all rows
                        foreach (GPALAutomationElement elem in rowOfColumns)
                        {
                            switch (elem.Ae.Current.LocalizedControlType.ToLower())
                            {
                                case "pane":
                                case "text":
                                case "edit":
                                    // add the element we will work with as a column to the current row list
                                    application.CurrentUOW.ElementGrid[rowCnt++].Add(
                                        new UnitOfWork.ElementNode()
                                        {
                                            AutomationElement = elem.Ae,
                                            InteractionType = interactionInfo[columnIdx].InteractionType,
                                            OffsetX = interactionInfo[columnIdx].OffsetX,
                                            OffsetY = interactionInfo[columnIdx].OffsetY
                                        });
                                    break;

                                default:
                                    break;
                            }

                            // only add as many rows as was asked for
                            // all or only save up to withallthatmatch row count
                            if (rowCnt >= application.CurrentUOW.WithAllThatMatch)
                                break;
                        }
                    }
                }

                // TODO: CAVEAT: do we have concept of next page in apps? prolly? if we still have tokens and are done, shouldn't we get a next page or bail
                // BUG: IF WE consume all out tokens, why would we loop again (while true)

                // keep looping while we have tokens
                // but after dealing with the first round of tokens, with more to go
                if (tokenIdx < tokens.Count())
                {
                    // we now have our elements in rows and columns
                    // iterate over the tokens, in rows and columns to fill in the text
                    foreach (List<UnitOfWork.ElementNode> elementNode in application.CurrentUOW.ElementGrid)    // get one row of elements
                    {
                        elementIdx = 0;   // each row starts at its first column
                        List<T> currentRow = tokens[tokenIdx++];

                        foreach (T token in currentRow)                                            // iterate over tokens, filling in the corresponding input/textarea webelement
                        {
                            if (elementIdx < elementNode.Count && null != elementNode[elementIdx])
                            {
                                if (InteractionType.Hardware == elementNode[elementIdx].InteractionType)
                                    ElementHelper.HardwareFillInFrom(application, new GPALAutomationElement(elementNode[elementIdx].AutomationElement), elementNode[elementIdx].OffsetX, elementNode[elementIdx].OffsetY, token.ToString(), writeMode);
                                else
                                    ElementHelper.FillInFrom(application, elementNode[elementIdx].AutomationElement, elementNode[elementIdx], token.ToString(), writeMode);
                            }
                            else
                                // detect if we have too many tokens to consume and publish an information event [entirely possible this scenario is fine]
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unused token [{token}] from tokens [{string.Join(",", currentRow)}]", application, GPALObjectType.Application);
                            elementIdx++;   // increment the column [element] count for each token. if we have a token, we are expecting elements for it.
                        }

                        // detect if we do not have enough tokens for all the form fields [entirely possible this scenario is fine]
                        // we got here and finished our tokens but have not exhausted our columns [elements]
                        while (elementIdx < elementNode.Count && null != elementNode[elementIdx])
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No token for control: [{elementNode[elementIdx].AutomationElement.Current.LocalizedControlType}]", application, GPALObjectType.Application);
                            elementIdx++;   // once per control. Incrementing in the guard and again in the body skipped every other one and read past the end
                        }
                    }

                    UnitOfWork safeUOW = application.CurrentUOW;
                    CallIfStatus handled = CallIfStatus.NotHandled;

                    // CAVEAT: there is no concept of 'handled (1)' vs 'not handled (0)' but definitely can request to exit
                    // now that we have a list of grids, maybe we have 2/3, next token/grid ?
                    if (null != application.CurrentUOW.AppCallAfterFillIn)
                        handled = application.CurrentUOW.AppCallAfterFillIn(application, (IGPALGrid<string>)tokens, tokenIdx);

                    // CAVEAT: kludge - the call after handler can and probably will set a new current unit of work, but we need our old current unit
                    // the call after handler UOW is no longer in scope, so restore our UOW
                    application.CurrentUOW = safeUOW;

                    if (CallIfStatus.Terminate == handled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CallAfterFillIn handler requested program termination.", application, GPALObjectType.Application);
                        throw new GPALException($"{GPAL.MyMethodName()}: CallAfterFillIn handler requested program termination.");
                    }
                }
                else
                    break;   // we consumed all tokens move on

                lastRowCnt += rowCnt;   // the total number of rows we are returning, also when looping thru after the first time, add to the end after the last row

                // we are out of tokens, break out
                // if we have more tokens, shouldn't we go to another page?
                // CAVEAT: BUG: we are just looping back thru the same selectors and consuming more tokens, so does that mean CallAfterFillIn will do something and this is the behavior we want?
                // or do we break because we have more tokens then elements? or should we loop?
                // why did i put this in a while true loop?
                if (tokenIdx == tokens.Count())
                    break;   // continue, loop into us again, we have more tokens to consume
            }
            return;
        }
        /// <summary>
        /// Enter tokens into form controls for each grid of tokens in the list.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are filled in</param>
        /// <param name="tokenList">List of token grids, one call to <see cref="FillInWithTokens{T}"/> per grid</param>
        /// <param name="writeMode">How to write the token text into each control (overwrite/insert/append)</param>
        public static void FillInWithTokens(Application.Application application, List<IGPALGrid<string>> tokenList, WriteMode writeMode)
        {
            foreach (IGPALGrid<string> tokens in tokenList)
                FillInWithTokens(application, tokens, writeMode);
        }


        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern int GetWindowRect(IntPtr hwnd, out Rectangle rect);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>
        /// Resizes the window with the given handle, without moving it or changing its z-order.
        /// </summary>
        /// <param name="hwnd">Handle of the window to resize</param>
        /// <param name="width">New width in pixels</param>
        /// <param name="height">New height in pixels</param>
        internal static void ResizeWindow(IntPtr hwnd, int width, int height)
        {
            if (hwnd == IntPtr.Zero) return;
            const uint SWP_NOMOVE     = 0x0002;
            const uint SWP_NOZORDER   = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        /// <summary>
        /// Moves the window with the given handle to the specified screen coordinates, without resizing it or changing its z-order.
        /// </summary>
        /// <param name="hwnd">Handle of the window to move</param>
        /// <param name="x">New x coordinate of the window's top-left corner</param>
        /// <param name="y">New y coordinate of the window's top-left corner</param>
        internal static void MoveWindow(IntPtr hwnd, int x, int y)
        {
            if (hwnd == IntPtr.Zero) return;
            const uint SWP_NOSIZE     = 0x0001;
            const uint SWP_NOZORDER   = 0x0004;
            const uint SWP_NOACTIVATE = 0x0010;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}

