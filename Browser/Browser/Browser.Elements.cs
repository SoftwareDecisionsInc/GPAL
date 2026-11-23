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
        /// Add a selector to the current UOW. Selectors make up a Unit of Work (UOW) to perform actions upon.<br/>
        /// A UOW is: All selectors defined between actions like GoTo, LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the CallIf handlers to create workflows to deal with found/not found selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// </summary>
        /// <example>
        /// <code>
        ///     Selector inputSearchSelector = (Selector)GPAL.Selector
        ///         .WithName("SearchInput")
        ///         .WithCSS("#gh-ac")
        ///         .WithXPath("//*[@id=\"gh-ac\"]")
        ///         .MatchPlaceholder("Search for anything");
        ///
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .GoTo("https://www.ebay.com/")
        ///         .WithWaitOnDocumentReady(true)
        ///         .WithSelector(inputSearchSelector)
        ///         .FillInFrom("Find me");
        /// </code>
        /// </example>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowAfterAnySelector WithSelector(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;  // in case it's being reused
            selector.DeleteMe = false;                  // Remove() latches this on and nothing else clears it, so a reused selector would be swept on its first maintenance pass
            selector.RemovedMethods.Clear();            // a handler that took itself out of play in an earlier workflow is back in play for this one
            if (true == CurrentUOW.ActionCalled)
            {
                // The new unit of work keeps the iframe context path (a list, one entry per nested iframe) and the shadow root
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot
                };

                CurrentUOW.Browser = this;
                selector.Browser = this;
            }

            ResolveInteractionType(selector);

            // if we are reusing, we can't use cached results in a new uow
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;

            BrowserSettings.CurrentSelector = selector;

            CurrentUOW.WithSelectorList.Add(BrowserSettings.CurrentSelector);
            return this;
        }
        /// <summary>
        /// Add literal data to a grid when using .GetGrid
        /// </summary>
        /// <param name="literalData"></param>
        /// <returns></returns>
        public IAllowAfterAnySelector WithSelector(string literalData)
        {
            if (true == CurrentUOW.ActionCalled)
            {
                // The new unit of work keeps the iframe context path (a list, one entry per nested iframe) and the shadow root
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;
                //                var shadowRootElement = CurrentUOW.ShadowRootGPALElement;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot
                };

                persistentUOW = new UnitOfWork();

                CurrentUOW.Browser = this;
                persistentUOW.Browser = this;
            }

            Selector tmpSel = GPAL.Selector.WithText(literalData).ToGPALObject();
            tmpSel.selectorSettings.SelectorType = SelectorType.Data;
            tmpSel.selectorSettings.Name = "Literal" + CurrentUOW.WithSelectorList.Select(s => SelectorType.Data == s.SelectorSettings.SelectorType).Count() + 1;
            CurrentUOW.WithSelectorList.Add(tmpSel);
            return this;
        }
        /// <summary>
        /// Add literal data to a grid by invoking this function when using .GetGrid
        /// </summary>
        /// <param name="dataFunction"></param>
        /// <returns></returns>
        public IAllowAfterAnySelector WithSelector(Func<string> dataFunction)
        {
            Selector tmpSel = GPAL.Selector.ToGPALObject();
            tmpSel.selectorSettings.SelectorType = SelectorType.DataFunc;
            tmpSel.selectorSettings.Name = dataFunction.Method.Name;
            tmpSel.selectorSettings.DataFunction = dataFunction;
            CurrentUOW.WithSelectorList.Add(tmpSel);
            return this;
        }

        /// <summary>
        /// Runs <paramref name="body"/> once per .InElement container, setting that entry's current index.
        /// Without .InElement, runs <paramref name="body"/> once.
        /// </summary>
        /// <param name="body">What to do inside one container</param>
        /// <returns>How many containers it walked, zero when there were none</returns>
        internal int WalkInElementIfAny(Action body)
        {
            UnitOfWork.WebElementWithType inElement = CurrentUOW.ContextPath?
                .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

            if (null == inElement)
            {
                body();

                return 0;
            }

            // .WithAllThatMatch already limited the containers in RefreshContainers; no limit here.
            for (int container = 0; container < inElement.elements.Count; container++)
            {
                inElement.current = container;

                body();
            }

            inElement.current = 0;

            return inElement.elements.Count;
        }

        /// <summary>
        /// Finds the .InElement containers again on the current page; after a next page the old ones find nothing.
        /// The .InElement entry and the steps after it are lifted off the context path during the search, so it
        /// searches in the steps before it and not inside the old container.
        /// </summary>
        /// <param name="inElement">The .InElement entry on the context path</param>
        /// <param name="outerAgain">True on a new page or pass: the containers this one sits in are found again too</param>
        internal void RefreshContainers(UnitOfWork.WebElementWithType inElement, bool outerAgain)
        {
            int at = CurrentUOW.ContextPath.IndexOf(inElement);
            List<UnitOfWork.WebElementWithType> lifted = CurrentUOW.ContextPath.GetRange(at, CurrentUOW.ContextPath.Count - at);
            Selector selectorBefore = CurrentUOW.CurrentSelector;

            // on a next page or a new pass the containers this one sits in are found again too
            if (true == outerAgain)
                foreach (UnitOfWork.WebElementWithType step in CurrentUOW.ContextPath.Take(at))
                    if (ElementType.Element == step.elementType && null != step.selector)
                        step.elements = null;

            CurrentUOW.ContextPath.RemoveRange(at, lifted.Count);

            // an earlier search inside a container leaves OttoMagic and Puppeteer inside it
            LeaveSearchedElement();

            // After a next page an interstitial may show first. Retry every 250 ms until containers appear or
            // .WaitFor runs out.
            List<GPALElement> containers = null;
            Stopwatch waited = Stopwatch.StartNew();
            long waitFor = WaitTime.Never < CurrentUOW.WaitForInMs ? (long)CurrentUOW.WaitForInMs : 0;

            do
            {
                ElementHelper.FindWebElements(this, CurrentUOW, inElement.selector, out _, out containers, null, false);

                if (0 < containers?.Count) break;

                if (waitFor > waited.ElapsedMilliseconds)
                    System.Threading.Thread.Sleep(250);
            }
            while (waitFor > waited.ElapsedMilliseconds);

            CurrentUOW.ContextPath.AddRange(lifted);

            // the search for the containers set the current selector; the action's own selector is put back
            CurrentUOW.CurrentSelector = selectorBefore;

            if (0 < containers?.Count)
            {
                // With .InElement, .WithAllThatMatch limits containers: ten cards, not ten prices per card.
                // Precedence: unit of work, browser, global.
                int wanted = int.MaxValue;

                if (int.MaxValue != CurrentUOW.WithAllThatMatch && 0 < CurrentUOW.WithAllThatMatch)
                    wanted = CurrentUOW.WithAllThatMatch;
                else if (int.MaxValue != BrowserSettings.WithAllThatMatch && 0 < BrowserSettings.WithAllThatMatch)
                    wanted = BrowserSettings.WithAllThatMatch;
                else if (0 < GPAL.GPALSettings.WithAllThatMatch)
                    wanted = GPAL.GPALSettings.WithAllThatMatch;

                if (int.MaxValue != wanted && wanted < containers.Count)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{wanted}] containers kept of [{containers.Count}] found for selector [{inElement.selector.Name}] with [WithAllThatMatch({wanted})]", this, GPALObjectType.Browser);

                    containers = containers.Take(wanted).ToList();
                }

                inElement.elements = containers;
                inElement.gPalElement = containers[0];
            }
            else
            {
                // an empty list, not null: the containers were looked for, so a search does not look again, and a
                // search inside them finds nothing
                inElement.elements = new List<GPALElement>();
                inElement.gPalElement = null;

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"InElement selector returned no results [{inElement.selector.Name}]", this, GPALObjectType.Browser);
            }

            inElement.current = 0;
        }
        /// <summary>
        /// Builds the grid from .InElement containers, one row per container, one cell per selector. A missing value
        /// gets a placeholder so rows stay aligned.<br/>
        /// Returns false without .InElement.
        /// </summary>
        /// <param name="tmpColumns">Receives one column per selector</param>
        /// <param name="selectorMeta">Receives the name and attribute of each column</param>
        /// <param name="pages">Page being read, for the line this writes</param>
        /// <returns>True when containers built the grid</returns>
        private bool RowsFromContainers(List<ReadOnlyCollection<GPALElement>> tmpColumns,
            List<(string Name, string AttributeName)> selectorMeta, int pages)
        {
            bool retVal = false;

            UnitOfWork.WebElementWithType inElement = CurrentUOW.ContextPath?
                .LastOrDefault(context => ElementType.Element == context.elementType && null != context.selector);

            if (null != inElement)
            {
                RefreshContainers(inElement, true);

                // With .InElement, rows come only from containers. No fallback to the column search, which causes
                // the misalignment .InElement prevents.
                retVal = true;
            }

            if (0 < inElement?.elements?.Count)
            {
                List<Selector> selectors = CurrentUOW.WithSelectorList;
                List<List<GPALElement>> building = selectors.Select(sel => new List<GPALElement>()).ToList();

                foreach (Selector sel in selectors)
                {
                    sel.WebSelectorFoundResults = null;
                    sel.WebSelectorMatchedResults = null;

                    selectorMeta.Add((sel.Name, sel.AttributeName));
                }

                int rows = WalkInElementIfAny(() =>
                {
                    for (int column = 0; column < selectors.Count; column++)
                    {
                        Selector sel = selectors[column];
                        GPALElement cell = null;

                        if (SelectorType.Selector == sel.selectorSettings.SelectorType)
                        {
                            ElementHelper.FindWebElements(this, CurrentUOW, sel, out _, out List<GPALElement> matchedElems);

                            // Use the first match in the container; extras do not spill into the next row.
                            if (0 < matchedElems?.Count)
                            {
                                cell = matchedElems[0];
                                cell.AttributeName = sel.AttributeName;
                            }
                        }
                        else if (SelectorType.Data == sel.selectorSettings.SelectorType)
                        {
                            cell = GPAL.Element;
                            cell.Text = sel.SelectorPath;
                        }
                        else if (SelectorType.DataFunc == sel.selectorSettings.SelectorType)
                        {
                            cell = GPAL.Element;
                            cell.Text = sel.selectorSettings.DataFunction();
                        }

                        // Nothing in this container: placeholder cell.
                        if (null == cell)
                            cell = new GPALElement(new Point(0, 0), new Size(1, 1), $"{GPAL.ErrorPlaceholder} : {sel.Name}", BrowserDriver, "GPALElement")
                            {
                                AttributeName = sel.AttributeName,
                                Browser = this,
                                Selector = sel
                            };

                        building[column].Add(cell);
                    }
                });

                foreach (List<GPALElement> column in building)
                    tmpColumns.Add(new ReadOnlyCollection<GPALElement>(column));

                CurrentUOW.RowCount = rows;
                CurrentUOW.ColCount = selectors.Count;

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Grid page [{pages + 1}] read [{rows}] containers of [{selectors.Count}] columns", this, GPALObjectType.Browser);
            }
            else if (true == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No containers for [{inElement.selector.Name}] on page [{pages + 1}], so no rows from it. The page was not read another way", this, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// InElement indicates the root element for following withselector element searches which would use a relative selector
        /// Example: on ebay, we could use a generic selector to get all descriptions, links, prices and images, but that assumes 1:1 correlation of the globally found items
        ///          or, we can find all the result spans, then search for the description, link, price and image relative to that containing div/span/element
        /// </summary>
        /// <param name="selector"></param>
        /// <returns>Fluent interface to chain more methods</returns>
        public IAllowAfterAnySelector InElement(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;

            // After an action, start a new unit of work that keeps the frame and shadow root context
            if (CurrentUOW.ActionCalled)
            {
                var previousShadowRoot = CurrentUOW.ShadowRoot;
                var previousContextPath = CurrentUOW.ContextPath;

                CurrentUOW = new UnitOfWork
                {
                    Browser = this,
                    ShadowRoot = previousShadowRoot,
                    ContextPath = previousContextPath  // preserve chain
                };

                persistentUOW = new UnitOfWork { Browser = this };
            }

            BrowserSettings.CurrentSelector = selector;
            CurrentUOW.InSelectorList.Add(selector);

            // --- Switch to clean context first ---
            //if (true == UseOttoMagic)
            //    MagicHelper?.SwitchToDefaultContent();
            //else if (true == UsePuppeteer)
            //    PuppeteerClient.SwitchToDefaultContent().Execute();
            //else
            //    BrowserDriver.SwitchTo().DefaultContent();

            // Walk existing context
            IEnumerable<UnitOfWork.WebElementWithType> stepsToProcess;

            stepsToProcess = CurrentUOW.ContextPath;

            GPALElement inElement = null;

            foreach (UnitOfWork.WebElementWithType contextType in stepsToProcess)
            {
                if (ElementType.IFrame == contextType.elementType)
                {
                    if (UsePuppeteer)
                        PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                    else
                        BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                    // a search context belongs to one document - anything scoped in the frame we just left is
                    // stale, and a shadow root from the parent throws "no such shadow root" if reused here
                    inElement = null;
                }
                else if (ElementType.ShadowRoot == contextType.elementType)
                {
                    if (UseOttoMagic)
                        MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                    else if (UsePuppeteer)
                        PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                    // scope to this step's shadow root, so a path with more than one shadow root walks through
                    // each in turn
                    inElement = contextType.gPalElement;
                }
                else if (ElementType.Element == contextType.elementType)
                {
                    inElement = contextType.gPalElement;   // this becomes the new root
                }
            }

            // Store the selector only. Containers are found when an action runs: .WithAllThatMatch comes later, and
            // a next page replaces them.
            var elementEntry = new UnitOfWork.WebElementWithType(selector, null, ElementType.Element);

            CurrentUOW.ContextPath ??= new List<UnitOfWork.WebElementWithType>();
            CurrentUOW.ContextPath.Add(elementEntry);

            return this;
        }

        /// <summary>
        /// Specify that the following .WithSelector(s) are contained in an iFrame specified by the Selector.<br/>
        /// Use .InMainDom() to get back to working with the main document.
        /// </summary>
        /// <example>
        /// <code>
        ///     static Selector captchaIframe = (Selector)GPAL.Selector
        ///         .WithCSS(@"#s0-69-captcha-ui > iframe");
        ///
        ///     static Selector captchaCheck = (Selector)GPAL.Selector
        ///         .WithXPath(@"//*[@id=""checkbox""]")
        ///         .WithHardware;
        ///
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.FireFox)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithWaitOnDocumentReady(true)
        ///         .GoTo(@"https://www.ebay.com/splashui/captcha")
        ///         .InFrame(captchaIframe)
        ///         .WithSelector(captchaCheck)
        ///         .LeftClick();
        /// </code>
        /// </example>
        /// <param name="selector">iFrame element Selector</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector InFrame(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;

            // After an action, start a new unit of work that keeps the frame and shadow root context
            if (CurrentUOW.ActionCalled)
            {
                var previousShadowRoot = CurrentUOW.ShadowRoot;
                var previousContextPath = CurrentUOW.ContextPath;

                CurrentUOW = new UnitOfWork
                {
                    Browser = this,
                    ShadowRoot = previousShadowRoot,
                    ContextPath = previousContextPath
                };

                persistentUOW = new UnitOfWork { Browser = this };
            }

            BrowserSettings.CurrentSelector = selector;
            CurrentUOW.InSelectorList.Add(selector);

            try
            {
                if (true == UseOttoMagic)
                    MagicHelper.SwitchToDefaultContent();
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else
                    this.BrowserDriver.SwitchTo().DefaultContent();

                IEnumerable<UnitOfWork.WebElementWithType> stepsToProcess;
                GPALElement inElement = null;

                stepsToProcess = CurrentUOW.ContextPath;

                // Walk the context path from the document: frames, shadow roots and elements
                foreach (UnitOfWork.WebElementWithType contextType in stepsToProcess)
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToElement(contextType.gPalElement.Css);
                        else if(true == UsePuppeteer)
                            PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                        else
                            BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                        // a search context belongs to one document - anything scoped in the frame we just left is
                        // stale, and a shadow root from the parent throws "no such shadow root" if reused here
                        inElement = null;
                    }
                    else if (ElementType.ShadowRoot == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                        else if (true == UsePuppeteer)
                            PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                        // scope to this step's shadow root, so a path with more than one shadow root walks through
                        // each in turn
                        inElement = contextType.gPalElement;
                    }
                }
            
                // the frame is waited for as an action waits for its selector
                if (WaitTime.Never < CurrentUOW.WaitForInMs)
                    ElementHelper.WaitFor(this, selector, CurrentUOW.WaitForInMs, out _, out _, CurrentUOW.ShadowRoot);

                // Find the iframe
                ElementHelper.FindWebElements(
                    this, CurrentUOW, selector, out _, out var tempElems, CurrentUOW.ShadowRoot);

                if (tempElems?.Count > 0)
                {
                    var iframeElement = new UnitOfWork.WebElementWithType(
                        selector, tempElems[0], ElementType.IFrame);

                    CurrentUOW.ContextPath ??= new List<UnitOfWork.WebElementWithType>();
                    CurrentUOW.ContextPath.Add(iframeElement);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to enter the frame", this, GPALObjectType.Browser, ex);
            }

            return this;
        }

        /// <summary>
        /// Specify that the following .WithSelectors are contained in an a shadow dom specified by the Selector.<br/>
        /// Use .InMainDom() to get back to working with the main document.
        /// Only CSS selectors work inside a shadow dom.
        /// </summary>
        /// <example>
        /// <code>
        ///     static Selector shadowRoot = (Selector)GPAL.Selector
        ///         .WithXPath(@"/html/body/article/p[3]")
        ///         .WithHardware
        ///         .WithSimulateMouse(true);
        ///         
        ///     static Selector shadowSpan = (Selector)GPAL.Selector
        ///         .WithCSS(@"span")
        ///         .WithHardware
        ///         .WithSimulateMouse(true);
        ///         
        ///     Browser myBrowser = (Browser)GPAL.Browser
        ///         .WithBrowserType(BrowserType.Chrome)
        ///         .WithDriverLocation(@"c:\drivers")
        ///         .WithWaitOnDocumentReady(true)
        ///         .GoTo(@"https://mdn.github.io/web-components-examples/word-count-web-component/")
        ///         .InShadowDom(shadowRoot)
        ///         .WithSelector(shadowSpan)
        ///         .Hover()
        ///         .WaitFor(5000);
        /// </code>
        /// </example>
        /// <param name="shadowDomSelector">Shadow dom element Selector</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector InShadowDom(Selector shadowDomSelector)
        {
            shadowDomSelector.ElementsFoundAndMatchedCount = 0;
            shadowDomSelector.WebSelectorFoundResults = null;
            shadowDomSelector.WebSelectorMatchedResults = null;

            if (CurrentUOW.ActionCalled)
            {
                var previousShadowRoot = CurrentUOW.ShadowRoot;
                var previousContextPath = CurrentUOW.ContextPath;

                CurrentUOW = new UnitOfWork
                {
                    Browser = this,
                    ShadowRoot = previousShadowRoot,
                    ContextPath = previousContextPath
                };

                persistentUOW = new UnitOfWork { Browser = this };
            }

            BrowserSettings.CurrentSelector = shadowDomSelector;
            CurrentUOW.InSelectorList.Add(shadowDomSelector);

            try
            {
                if (true == UseOttoMagic)
                    MagicHelper.SwitchToDefaultContent();
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else
                    this.BrowserDriver.SwitchTo().DefaultContent();

                GPALElement inElement = null;

                // Walk the context path from the document: frames, shadow roots and elements
                foreach (UnitOfWork.WebElementWithType contextType in CurrentUOW.ContextPath)
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.InFrame(contextType.gPalElement.Css);
                        else if (true == UsePuppeteer)
                            PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                        else
                            BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                        // a search context belongs to one document - anything scoped in the frame we just left is
                        // stale, and a shadow root from the parent throws "no such shadow root" if reused here
                        inElement = null;
                    }
                    else if (ElementType.ShadowRoot == contextType.elementType)
                    {
                        if (true == UseOttoMagic)
                            MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                        else if (true == UsePuppeteer)
                            PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                        // scope to this step's shadow root, so a path with more than one shadow root walks through
                        // each in turn
                        inElement = contextType.gPalElement;
                    }
                }

                // the host is waited for as an action waits for its selector
                if (WaitTime.Never < CurrentUOW.WaitForInMs)
                    ElementHelper.WaitFor(this, shadowDomSelector, CurrentUOW.WaitForInMs, out _, out _, inElement);

                ElementHelper.FindWebElements(
                    this, CurrentUOW, shadowDomSelector, out _, out var tempElems, inElement);

                if (tempElems?.Count > 0)
                {
                    var shadowElement = new UnitOfWork.WebElementWithType(
                        shadowDomSelector, tempElems[0], ElementType.ShadowRoot);

                    CurrentUOW.ShadowRoot = tempElems[0];
                    CurrentUOW.ShadowRoot.IsShadowRoot = true;

                    CurrentUOW.ContextPath ??= new List<UnitOfWork.WebElementWithType>();
                    CurrentUOW.ContextPath.Add(shadowElement);

                    // the engine searches inside the root from here; Selenium keeps the root object for it
                    if (true == UseOttoMagic)
                    {
                        // OttoMagic switches into the root by the host's Css; there is no root object to keep
                    }
                    else if (true == UsePuppeteer)
                    {
                        // Puppeteer switches into the root by the host's Css; there is no root object to keep
                    }
                    else if (true == UseSelenium)
                    {
                        ISearchContext shadowRoot = null;

                        // Use WebDriver's GetShadowRoot, which throws only when there is no open root.
                        // ExecuteJavaScriptObj returns null for both a closed root and a failed script, and a wrong null
                        // marks an open root closed, so searches return the host instead of its children.
                        try
                        {
                            shadowRoot = ((IWebElement)tempElems[0].WebElement).GetShadowRoot();
                        }
                        catch
                        {
                            // closed, handled below
                        }

                        if (null != shadowRoot)
                            CurrentUOW.ShadowRoot.WebElement = shadowRoot;  // dynamic - the search dispatches to it at runtime
                        else
                        {
                            CurrentUOW.ShadowRoot.IsClosedShadowRoot = true;
                            // webdriver cannot reach a closed shadow root: arguments[0].shadowRoot is null and
                            // GetShadowRoot throws. The host element stays in place, so clicks have a real position and
                            // searches stay scoped to the host instead of the whole document.
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Closed shadow root for selector [{shadowDomSelector.Name}]. Will return host element for searches inside.", this, GPALObjectType.Browser);
                        }
                    }
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to enter the shadow root", this, GPALObjectType.Browser, ex);
            }

            return this;
        }

        /// <summary>
        /// Specify that GPAL is no longer working in an iFram or shadow dom and reset back to the main document.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector InMainDom()
        {
            CurrentUOW.ShadowRoot = null;
            CurrentUOW.ContextPath?.Clear();

            try
            {
                if (true == UseOttoMagic)
                    MagicHelper.SwitchToDefaultContent();
                else if (true == UsePuppeteer)
                    PuppeteerClient.SwitchToDefaultContent().Execute();
                else if (true == UseSelenium)
                    this.BrowserDriver.SwitchTo().DefaultContent();

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "Returned to main document", CurrentUOW);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to return to the main document", this, GPALObjectType.Browser, ex);
            }

            return this;
        }

        /// <summary>
        /// Specifies the Next Page button used to retrieve multiple pages of data.<br/>
        /// Use with .GetPages(numOfPages) to specify how to get those next pages.<br/>
        /// .GetGrid or any .SaveTo[CSV|Excel|File] will treat the current UOW as output Selectors from which to retrieve data.<br/>
        /// Retrieving data will retrieve only the current page if nothing is specified.<br/>
        /// Also useful with .WithAllThatMatch to retrieve repeating results (like on eBay)<br/>
        /// See also .WithInfiniteScroll
        /// </summary>
        /// <param name="nextPageButtonselector">Next Page Button selector</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowWithPagesAndGridActions WithNextPageButton(Selector nextPageButtonselector)
        {
            CurrentUOW.NextPageButton = nextPageButtonselector;
            BrowserSettings.CurrentSelector = nextPageButtonselector;  // for use by .WithHardware
            return this;
        }
        /// <summary>
        /// Specified the number of pages to retrieve when using GetGrid or any .SaveTo[CSV|Excel|File]<br/>
        /// Must also specify the Next Page button [.WithNextPageButton(selector)] or Infinite scroll [.WithInfiniteScroll] to tell GPAL how to retrieve next pages.<br/>
        /// </summary>
        /// <param name="pageCount">Number of pages to retrieve.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowGetGridAndFillInFrom WithPages(int pageCount)
        {
            CurrentUOW.PageCount = pageCount;
            return this;
        }
        /// <summary>
        /// Add a persistent selector to the persistent UOW. Persistent selectors are always looked for whenever you perform an action on a UOW, like LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the PersistentCallIf handlers to create workflows to deal with the persistent selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// Only one persistent Unit of Work exists. All selectors are evaluated in the order they are defined.<br/>
        /// Example: For browser, a persistent selector could look for a popup panel like 'Accept all cookies' that displays on all pages and may block processing..
        /// Persistent selectors are looked for only in the base content (frame 0), where page popups come from. Use workflows to deal with elements in frames.
        /// </summary>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowBrowserSettingsOrGoTo WithPersistentSelector(Selector selector)
        {
            // After an action, start a new unit of work and an empty persistent one
            if (true == CurrentUOW.ActionCalled)
            {
                // Preserve context for new UnitOfWork
                var iframeRoot = CurrentUOW.ContextPath;
                var shadowRoot = CurrentUOW.ShadowRoot;

                CurrentUOW = new UnitOfWork
                {
                    ContextPath = iframeRoot,
                    ShadowRoot = shadowRoot,
                    Browser = this
                };

                persistentUOW = new UnitOfWork() { Browser = this };
            }

            selector.Browser = this;
            selector.DeleteMe = false;         // Remove() latches this on and nothing else clears it, so a reused persistent selector would be swept on its first maintenance pass
            selector.RemovedMethods.Clear();   // a handler that took itself out of play in an earlier workflow is back in play for this one

            ResolveInteractionType(selector);

            // if we are reusing, we can't use cached results in a new uow
            selector.WebSelectorFoundResults = null;
            selector.WebSelectorMatchedResults = null;
            
            persistentUOW.WithSelectorList.Add(selector);   // CAVEAT: the interaction type is resolved against the headless setting as it stands here; a WithHeadless after this leaves a hardware selector on hardware
            return this;
        }
        /// <summary>
        /// Indicates the Selectors refer to/match repeating, multiple elements on the page.<br/>
        /// Use after all your <b>.WithSelector</b> statements. Used two ways.<br/>
        /// For scraping: This is used to retrieve multiple rows of data from the webpage, up to rowCount.<br/>
        /// For input tokens/strings: This will repeat the same input into multiple elements, up to rowCount of lines.<br/>
        /// Called before any .WithSelector, or after an action, it sets the browser's value; otherwise it sets this unit of work's value.
        /// </summary>
        /// <param name="rowCount">Set total number of entries to retrieve (if this is longer than a page size).<br/>If less than the page size, retrieve the 'top rowCount' rows on each page for the number of pages specified using .WithPages(pageCount)<br/>int.MaxValue [default] work with all rows.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelectorExceptWithAll WithAllThatMatch(int rowCount = int.MaxValue)
        {
            // Before any selector, or after an action, this sets the browser's value; otherwise this unit of work's
            if (0 == CurrentUOW.WithSelectorList.Count || true == CurrentUOW.ActionCalled)
                BrowserSettings.WithAllThatMatch = rowCount;
            else
                CurrentUOW.WithAllThatMatch = rowCount;
            return this;
        }
        public IAllowBrowserActionOrAnySelector GetElements(out List<GPALElement> elements)
        {
            elements = new List<GPALElement>();

            if (true == CurrentUOW.ActionCalled)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Action has already been called. Not retrieving elements.", this, GPALObjectType.Browser);
                return this;
            }

            if (true == WorkflowSetup(WaitTime.Never < CurrentUOW.WaitForInMs, true))
                for (int pages = 0; pages < CurrentUOW.PageCount; pages++)
                {
                    foreach (Selector sel in CurrentUOW.WithSelectorList)
                        if (SelectorType.Selector == sel.SelectorType)
                        {
                            ElementHelper.FindWebElements(this, CurrentUOW, sel, out bool matchedAll, out List<GPALElement> matchedElems);
                            if (sel.WebSelectorFoundResults != null)
                                elements.AddRange(sel.WebSelectorFoundResults);
                        }

                    if (pages < CurrentUOW.PageCount - 1)
                    {
                        BrowserHelper.TopBrowser(this, false);
                        if (false == BrowserHelper.GotoNextPage(this))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No next page, breaking loop. Retrieved [{pages + 1}] out of [{CurrentUOW.PageCount}] pages.", this, GPALObjectType.Browser);
                            break;
                        }
                    }
                }

            return this;
        }
        /// <summary>
        /// Check to see if any elements marked themselves for removal
        /// Very important if we are iterating a list and it changes mid-iteration, c# will complain
        /// </summary>
        /// <param name="CurrentUOW"></param>
        private static void SelectorListMaintenanceSweep(UnitOfWork CurrentUOW)
        {
            for (int idx = 0; idx < CurrentUOW.WithSelectorList.Count; idx++)
                if (true == CurrentUOW.WithSelectorList[idx].DeleteMe)
                    CurrentUOW.WithSelectorList.RemoveAt(idx);

            for (int idx = 0; idx < CurrentUOW.InSelectorList.Count; idx++)
                if (true == CurrentUOW.InSelectorList[idx].DeleteMe)
                    CurrentUOW.InSelectorList.RemoveAt(idx);
        }
        /// <summary>
        /// Check for persistent elements which can show up at any time and interrupt the main workflow
        /// These are checked before any action is performed.
        /// </summary>
        private void CheckPersistentSelectors()
        {
            UnitOfWork safeUOW = CurrentUOW;

            // Find each persistent selector's elements; its CallIf handlers do the work
            if (0 < (persistentUOW.WithSelectorList?.Count ?? 0))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Checking [{persistentUOW.WithSelectorList?.Count}] persistent selector(s)");

                foreach (Selector selector in persistentUOW.WithSelectorList)
                {
                    if (SelectorType.Selector != selector.SelectorType)
                        continue;   // we don't look up data literals

                    // we cannot use cached results knowing we are a new uow/action
                    selector.WebSelectorFoundResults = null;
                    selector.WebSelectorMatchedResults = null;
                    CurrentUOW.CurrentSelector = selector;


                    ElementHelper.FindWebElements(this, persistentUOW, selector, out _, out _, null, true, true);
                }
            }
            CurrentUOW = safeUOW;
        }
        /// <summary>
        /// Helper method for GPAL applications to remove a handler from all UOWs. Envisioned to be used in a persistent handler, but can be used anyway imagined. This is not fluent.
        /// </summary>
        /// <example>
        /// <code>
        /// public static int FoundAllCookies(Browser browser, List&lt;GPALElement&gt; foundElements, List&lt;GPALElement&gt; matchedElements, Selector selector)
        /// {
        ///     int retVal = 0;
        ///     foreach (GPALElement webElement in foundElements)
        ///     {
        ///         if (true == webElement.Text.ToLower().Contains("accept"))   // "Accept all cookies"
        ///         {
        ///             webElement.Click();
        ///             selector.Browser.RemoveCallIfHandlerEverywhere(FoundAllCookies);
        ///             selector.Remove();
        ///             retVal = 1;
        ///         }
        ///     }
        ///     return retVal;
        /// }
        /// </code>
        /// </example>
        /// <param name="func">The CallIf handler to remove</param>
        public void RemoveCallIfHandlerEverywhere(CallIfDelegate func)
        {
            // soft delete. the handler stays in the list and gets skipped when the chain runs, which keeps a
            // reused selector's handlers intact for the next workflow and keeps us from mutating a list
            // CallIfHandlers is in the middle of iterating - this is nearly always called from inside a handler
            foreach (Selector selector in CurrentUOW.WithSelectorList)
                if (false == selector.RemovedMethods.Contains(func))
                    selector.RemovedMethods.Add(func);

            foreach (Selector selector in CurrentUOW.InSelectorList)
                if (false == selector.RemovedMethods.Contains(func))
                    selector.RemovedMethods.Add(func);

            foreach (Selector selector in persistentUOW.WithSelectorList)
                if (false == selector.RemovedMethods.Contains(func))
                    selector.RemovedMethods.Add(func);
        }
    }
}
