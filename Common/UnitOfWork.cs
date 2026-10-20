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
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;
using GenerallyPositive.Browser;
using GenerallyPositive.Application;
using static GenerallyPositive.Browser.Browser;


namespace GenerallyPositive
{
    /// <summary>
    /// Everything revolves around the Unit of Work.<br/>
    /// A Unit of Work is defined as one or more selectors between actions (GoTo, Run, LeftClick, Hover, etc.)<br/>
    /// Use this only for debugging.
    /// </summary>
    public class UnitOfWork
    {
        /// <summary>
        /// List of With selectors
        /// Use .WithSelector to add selectors to this list
        /// </summary>
        /// <returns>List of selectors</returns>
        public List<Selector> WithSelectorList { get; internal set; } = null;

        // list of IN (nested) selectors defined
        /// <summary>
        /// List of iframe or shadow dom selectors. Set using .InFrame() or .InShadowDom()
        /// Use .InFrame to add frames to this list
        /// </summary>
        /// <returns>List of selectors</returns>
        public List<Selector> InSelectorList { get; internal set; } = null;
        /// <summary>
        /// The current selector being used from the current UOW. The selectors are iterated in declared order.
        /// </summary>
        /// <returns>The current selector</returns>
        public  Selector CurrentSelector { get; internal set; } = null;
        /// <summary>
        /// The next page button. When scraping multiple pages of results, this element is pressed for a new page of results.<br/>
        /// Use with .WithPages() to retrieve multiple pages of data.<br/>
        /// Use .WithNextPageButton to set this value
        /// Use .WithInfiniteScroll if there is no next page button, and to go to page end to load a new page of results.
        /// </summary>
        /// <returns>The next page button selector</returns>
        public Selector NextPageButton { get; internal set; } = null;
        /// <summary>
        /// Go to page end to get the next page of results.<br/>
        /// Use with .WithPages() to retrieve multiple pages of data.<br/>
        /// Use .WithNextPageButton() if there is a next page button for more results.
        /// Use .WithInfiniteScroll to set this value
        /// </summary>
        /// <returns>True if this page uses infinite scroll,<br/>False [default] this page is not infinite.</returns>
        public bool InfiniteScroll { get; internal set; } = false;
        /// <summary>
        /// Number of pages to retrieve. Default is 1 page.<br/>
        /// Use .WithPages() to set this value
        /// </summary>
        /// <returns>The number of pages set to retrieve.</returns>
        public int PageCount { get; internal set; } = 1;

        /// <summary>
        /// Output data grid [automatically created] of results retrieved using GetGrid(out GPALGrid) or<br/>
        /// Can also be a grid of data you create, set this using .WithGridToSave()<br/>
        /// All selectors in the UOW are columns, repeating elements on the page are rows.<br/>
        /// RetGrid is saved using Save/AppendToFile 
        /// </summary>
        /// <returns>The grid of data</returns>
        internal IGPALGrid<string> RetGrid { get; set; } = null;
        /// <summary>
        /// Input data grid retrieved from a file or database.<br/>
        /// A grid is rows and columns. Each column value is a token to consume.<br/>
        /// If not enough or too many tokens are present on a line for the defined elements, an Information Channel message will be broadcast indicating either not enough or too many tokens are supplied.
        /// </summary>
        /// <returns>A grid of tokens loaded from the data source</returns>
        public List<string> TokenList { get; internal set; } = null;

        /// <summary>
        /// add iframe and shadow dom elements in order to navigate them before finding a web element
        /// </summary>
        internal class WebElementWithType
        {
            internal Selector selector;
            internal GPALElement gPalElement;
            internal ElementType elementType;
            internal bool currentNode;

            // .InElement can match several elements, and later searches run inside each. These hold the matches and the
            // one being searched.
            internal List<GPALElement> elements;
            internal int current;

            // The element this step searches in: the current one when .InElement matched several, otherwise the single
            // element.
            internal GPALElement CurrentElement =>
                null != elements && current < elements.Count ? elements[current] : gPalElement;

            internal WebElementWithType(Selector selector, GPALElement webElement, ElementType elementType)
            {
                this.gPalElement = webElement;
                this.elementType = elementType;
                this.currentNode = true;
                this.selector = selector;
            }
        }
        internal List<WebElementWithType> ContextPath { get; set; }
        internal GPALElement ShadowRoot { get; set; }
        // response bodies from .Fetch, one per page per row of tokens, in the order they were requested
        internal List<string> FetchResults { get; set; } = new List<string>();
        // rows of values .Fetch drops into the request's numbered tokens, one row per set of requests
        internal IGPALGrid<string> FetchTokens { get; set; }
//        internal GPALElement ShadowRootGPALElement { get; set; }

        internal class ElementNode
        {
            public AutomationElement AutomationElement { get; internal set; }
            public Control ControlElement { get; internal set; }
            public GPALElement GPALElement { get; internal set; }
            public InteractionType InteractionType { get; internal set; } = InteractionType.Selenium;
            public int OffsetX { get; internal set; } = 0;
            public int OffsetY { get; internal set; } = 0;
        }
        // data grid of elements rows and columns
        // used with FillInFrom
        // used with FillInFromTokens
        internal GPALGrid<UnitOfWork.ElementNode> ElementGrid { get; set; } = null;
        /// <summary>
        /// Output header list, Defined using .WithHeader or, if not supplied, from the names given to selectors.<br/>
        /// If no headers are supplied, this is filled from selector names only on file save methods.<br/>
        /// If no headers or selector names are supplied, no header will be output.<br/>
        /// If not enough headers are supplied for the columns of output, an Information Channel message will be broadcast stating as much.
        /// </summary>
        /// <returns>A list of the headers defined.</returns>
        public List<string> HeaderList { get; internal set; } = null;
        // Column names from the control itself (a UIA TablePattern's headers). Separate from HeaderList, which
        // WithHeader clears, so they still name columns the workflow did not.
        public List<string> ControlHeaderList { get; internal set; } = null;
        internal SortedDictionary<int, string> MatchedRowIndexes { get; set; } = null;
        /// <summary>
        /// Only relevant if there is a match criteria or matching function.<br/>
        /// Of all the found selectors, was there only a partial match?<br/>
        /// If there is no match criteria/function, this is always false and matched and found results are the same.
        /// </summary>
        /// <returns>True if not all found matched.<br/>False if all found results matched the match criteria.</returns>
        public bool PartialMatch { get; internal set; } = false;

        // toggles, if BrowserAction is called, the next WithSelector will start a new UOW (old UOW saved but no longer in scope)
        /// <summary>
        /// Has an action been called on the current unit of work?<br/>
        /// If true, then the next .WithSelector() will create a new UOW.
        /// </summary>
        /// <returns>True if action has been called.<br/>False if no action has been called.</returns>
        public bool ActionCalled { get; internal set; } = false;
        /// <summary>
        /// Used to determine whether to create a new header list. if we already output a grid and want to add columns, we must be creating a new header list.
        /// </summary>
        internal bool GetGridCalled { get; set; } = false;
        /// <summary>
        /// Controls whether the selectors in scope are multiple elements on screen, and to get all<br/>
        /// Use .WithAllThatMatch to set this value
        /// </summary>
        /// <returns>True to match multiple elements on page.<br/>False to only match the first element(s) found.</returns>
        public int WithAllThatMatch { get; internal set; } = int.MaxValue;    // all rows, default

        /// <summary>
        /// WaitFor element to appear on page before calling action
        /// otherwise, the action is called and the element might not yet be rendered
        /// this adds overhead of looking for elements TWICE, once waiting for them, SECOND when they are used for the action
        /// WaitTime.Immediate (0) = do it once, WaitTime.Forever (-1) = int.MAX, WaitTime.Never (-2) = do not wait
        /// </summary>
        internal WaitTime WaitForInMs { get; set; } = WaitTime.Never;
        /// <summary>
        /// When a WaitFor lookup for this unit of work's selectors first found elements, otherwise null.<br/>
        /// Set only by CheckWaitFor, which runs after ActionCalled is set, so any selector added afterwards
        /// starts a fresh unit of work and this can never be carried over stale.
        /// </summary>
        internal DateTime? WaitForSatisfied { get; set; } = null;
        internal string WaitForTitle { get; set; } = null;
        internal string WaitForTitleRegex { get; set; } = null;
        internal ElementState WaitForElementState { get; set; } = ElementState.NotSet;

        internal int ColCount { get; set; } = 0;
        internal int RowCount { get; set; } = 0;

        internal Browser.Browser.CallAfterFillInDelegate CallAfterFillIn { get; set; }
        internal List<CallIfDelegate> CallIfFound { get; set; }
        internal List<CallIfDelegate> CallIfNotFound { get; set; }
        internal Application.Application.CallAfterFillInDelegate AppCallAfterFillIn { get; set; }
        internal List<Application.Application.CallIfDelegate> AppCallIfFound { get; set; }
        internal List<Application.Application.CallIfDelegate> AppCallIfNotFound { get; set; }

        public CallIfStatus CallIfFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        public CallIfStatus CallIfNotFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        public CallIfStatus AppCallIfFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        public CallIfStatus AppCallIfNotFoundHandled { get; internal set; } = CallIfStatus.NotHandled;

        /// <summary>
        /// The input File from which to consume tokens.<br/>
        /// This is set using any [Append/FillIn/Insert]FromFile method<br/>
        /// Tokens are loaded when the file is loaded.
        /// </summary>
        /// <returns>The input GPAL File</returns>
        public GPALFile InputFile { get; internal set; }
        /// <summary>
        /// The input database from which to consume tokens.<br/>
        /// This is set using any [Append/FillIn/Insert]FromDatabase method<br/>
        /// Tokens are loaded when the database is loaded.
        /// </summary>
        /// <returns>The input GPAL File</returns>
        public IGPALDatabase InputDatabase { get; internal set; }

        public Browser.Browser Browser { get; internal set; }

        internal UnitOfWork()
        {
            WithSelectorList = new List<Selector>();
            InSelectorList = new List<Selector>();
            HeaderList = new List<string>();
            ControlHeaderList = new List<string>();
            CallIfFound = new List<CallIfDelegate>();
            CallIfNotFound = new List<CallIfDelegate>();
            AppCallIfFound = new List<Application.Application.CallIfDelegate>();
            AppCallIfNotFound = new List<Application.Application.CallIfDelegate>();
            MatchedRowIndexes = new SortedDictionary<int, string>();
            ElementGrid = new GPALGrid<UnitOfWork.ElementNode>();
            ContextPath = new List<WebElementWithType>();
            WaitForInMs = GPAL.GPALSettings.WaitForInMs;  // if globally defined, then set the 'default' waitfor time
            ShadowRoot = null;
            RetGrid = GPAL.GridForType<string>();
        }

    }
}

