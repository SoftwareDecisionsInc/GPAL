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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Threading;
//using DocumentFormat.OpenXml.Bibliography;
//using DocumentFormat.OpenXml.Drawing;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Keys = OpenQA.Selenium.Keys;
using MatchType = GenerallyPositive.Enums.MatchType;

namespace GenerallyPositive.Browser
{
    internal partial class ElementHelper
    {
        public delegate ReadOnlyCollection<GPALElement> FindWebElementsByDelegate(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false);

        public static ReadOnlyCollection<GPALElement> FindWebElements(
            Browser browser,
            UnitOfWork currentUOW,
            Selector selector,
            out bool matchedAll,
            out List<GPALElement> matchedElements,
            GPALElement webElement = null,
            bool callIfHandler = true,
            bool isPersistent = false,
            bool publish = true)
        {
            int retries = 1;
            bool retry = false;
            int withAllThatMatch = int.MaxValue;

            // A selector that did not come through WithSelector, such as the next page button, has no browser yet
            if (null == selector.Browser)
                selector.Browser = browser;

            dynamic ourRetryElement = webElement;
            ReadOnlyCollection<GPALElement> retElems = new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
            string message = string.Empty;

            currentUOW.CurrentSelector = selector;
            lastFoundFullMessage = null;

            if (false == lastSelector?.Name.Equals(selector.Name))
            {
                listOfMessages.Clear();
                lastSelector = selector;
            }
            else if (null == lastSelector)
                lastSelector = selector;

            if (browser.persistentUOW != currentUOW)
                browser.CurrentUOW.CurrentSelector = selector;

            matchedAll = false;
            matchedElements = new List<GPALElement>();

            List<FindWebElementsByDelegate> functionList = new List<FindWebElementsByDelegate>
                {
                    null, FindWebElementsByCss, FindWebElementsByImage, FindWebElementsByText,
                    FindWebElementsByValue, FindWebElementsByXPath, FindWebElementsByName,
                    null, FindWebElementsByClassName, FindWebElementsByPlaceholder, FindWebElementsByHRef
                };

            foreach (var selectorPathEntry in selector.SelectorPaths)
            {
            tryAgain:   // KLUDGE: goto retries this selector path; an inner loop would capture the continue that moves to the next selector path
                int textLength = selectorPathEntry.SelectorPath.Length - 1;
                string abbreviatedText = selectorPathEntry.SelectorPath.Substring(0, Math.Min(7, textLength)) + "..." + selectorPathEntry.SelectorPath.Substring(Math.Max(0, textLength - 7));
                if (18 > selectorPathEntry.SelectorPath.Length)
                    abbreviatedText = selectorPathEntry.SelectorPath;

                int idx = GetIndexOfEnum<SelectorPathType>((int)selectorPathEntry.SelectorPathType);
                bool tryNextSelector = false;
                ReadOnlyCollection<GPALElement> allFoundElems = new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
                string errorMessage = $"Error finding elements for selector [{selector.Name}][{abbreviatedText}]";
                int selectorPathIdx = 0;

                try
                {
                    var tmpAllFoundElems = FindElements(browser, currentUOW, selectorPathEntry, selector, webElement, idx, functionList, isPersistent);

                    // Some finders return null for nothing found, such as an unmatched image or an OttoMagic XPath with
                    // no hits. Turn null into an empty collection so neither the return value nor the out param is null.
                    retElems = allFoundElems = tmpAllFoundElems ?? new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
                    matchedElements = allFoundElems.ToList();

                    if (0 < retElems?.Count)
                    {
                        // Puppeteer and OttoMagic return each GPALElement with its own Css; Selenium elements get one generated here
                        if (true == browser.UseSelenium)
                            foreach (GPALElement gPALElement in allFoundElems)
                                gPALElement.Css = GenerateCssSelector(browser, gPALElement.WebElement);

                        var matchingResult = ApplyCustomMatchCriteria(selector, allFoundElems, browser, matchedElements, out tryNextSelector);
                        matchedAll = matchingResult;

                        if (tryNextSelector)
                        {
                            retElems = new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
                            matchedElements.Clear();
                            continue;
                        }

                        List<GPALElement> simpleMatches = matchedElements;
                        // simple matching applies on top of custom matching, so pass in matchedELements which will be allfound if no custom matching
                        // every found element has to pass the custom criteria and the simple ones; simple matching
                        // always runs, since it gives the matched list
                        if (selector.MatchCriteria.Count > 0)
                        {
                            bool simpleMatchedAll = ApplySimpleMatching(selector, matchedElements, matchedElements, out simpleMatches);
                            matchedAll = matchingResult && simpleMatchedAll && 0 < simpleMatches.Count;
                        }
                        else
                            matchedAll = true;

                        matchedElements = simpleMatches;        // after simple matching, this will contain only matches

                        selectorPathEntry.WebSelectorFoundResults = allFoundElems?.ToList();
                        selectorPathEntry.WebSelectorMatchedResults = simpleMatches;

                        // if we use .WithAllThatMatch(number) we want up to that number, either in total or per page
                        // int.MaxValue means all rows, so the most specific scope that asked for a limit wins:
                        // unit of work, then browser, then global. if none did, we take them all.
                        if (int.MaxValue != currentUOW.WithAllThatMatch && 0 < currentUOW.WithAllThatMatch)
                            withAllThatMatch = currentUOW.WithAllThatMatch;
                        else if (int.MaxValue != browser.BrowserSettings.WithAllThatMatch && 0 < browser.BrowserSettings.WithAllThatMatch)
                            withAllThatMatch = browser.BrowserSettings.WithAllThatMatch;
                        else if (0 < GPAL.GPALSettings.WithAllThatMatch)
                            withAllThatMatch = GPAL.GPALSettings.WithAllThatMatch;   // int.MaxValue if it was never set

                        if (int.MaxValue != withAllThatMatch && 0 < simpleMatches?.Count)
                        {
                            List<GPALElement> tmpList = new List<GPALElement>();

                            // ensure we copy over only as many results as requested, or how many results we have
                            tmpList.AddRange(simpleMatches.Take(Math.Min(withAllThatMatch, matchedElements?.Count ?? 0)));

                            matchedElements = tmpList;
                        }

                        foreach (GPALElement element in retElems)
                        {
                            element.Browser = browser;
                            element.Selector = selector;
                        }
                    }
                }
                catch (StaleElementReferenceException sereex)
                {
                    message = errorMessage + $" StaleElementReferenceException: [{sereex.Message}]";
                    retry = ResolveWebElementIssue(browser, currentUOW, ref ourRetryElement, sereex, ref retries, message);
                }
                catch (ElementClickInterceptedException ecieex)
                {
                    message = errorMessage + $" ElementClickInterceptedException: [{ecieex.Message}]";
                    retry = ResolveWebElementIssue(browser, currentUOW, ref ourRetryElement, ecieex, ref retries, message);
                }
                catch (ElementNotInteractableException enieex)
                {
                    message = errorMessage + $" ElementNotInteractableException: [{enieex.Message}]";
                    retry = ResolveWebElementIssue(browser, currentUOW, ref ourRetryElement, enieex, ref retries, message);
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, browser, GPALObjectType.Browser, ex);
                    break;
                }

                if (true == retry && 0 < retries--)
                {
                    retry = false;
                    goto tryAgain;  // retry this selector path
                }

                selector.ElementsFoundAndMatchedCount = (int)(null != matchedElements ? matchedElements?.Count() : 0);

                // one line for the search: what it found, what of that matched when there were criteria,
                // and the selector path that was used
                string matchedPart = string.Empty;

                if (0 < retElems?.Count && (true == selector.AnyMatchCriteria() || Int32.MaxValue != withAllThatMatch))
                {
                    var matchCount = selector.MatchCriteria.Count > 0 ? selector.MatchCriteria.Count.ToString() : $"WithAllThatMatch({withAllThatMatch})";

                    if ((true == matchCount.Contains("WithAllThatMatch") && 0 < withAllThatMatch) || 0 < selector.MatchCriteria.Count)
                        matchedPart = $", [{matchedElements?.Count}] matched with [{matchCount}] match criteria";
                }

                message = $"[{retElems?.Count ?? 0}] elements found{matchedPart} for selector [{selector.Name}] using [{selectorPathEntry.SelectorPathType}] [{abbreviatedText}]";
                lastFoundFullMessage = $"[{retElems?.Count ?? 0}] elements found{matchedPart} for selector [{selector.Name}] using [{selectorPathEntry.SelectorPathType}] [{selectorPathEntry.SelectorPath}]";

                // A repeat of the same search is not published again. A WaitFor poll publishes nothing but still counts
                // as seen, for every selector path it tried, so the action's own search after the wait repeats none of it
                if (false == listOfMessages.Contains(message))
                {
                    listOfMessages.Add(message);

                    if (true == publish)
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, message, browser, GPALObjectType.Browser);
                }

                if (matchedAll || retElems?.Count > 0)
                    break;
                else if (++selectorPathIdx == selector.SelectorPaths.Count)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"No more selector paths. No elements found.", browser, GPALObjectType.Browser);
            }

            // false == callIfHandler will be the waitfor call
            if (true == callIfHandler)
                CallIfHandlers(browser, currentUOW, retElems, matchedElements, selector, matchedAll);

            return retElems;
        }

        // Helper to handle shadow DOM or normal search with retries
        /// <summary>
        /// Dispatches to the appropriate FindWebElementsBy* function for the given selector path's type
        /// (CSS, XPath, image, etc.), or to the Firefox shadow-DOM search when shadow root children are
        /// present for this unit of work.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="currentUOW">Current unit of work, used for shadow DOM context</param>
        /// <param name="selectorPathEntry">The selector path entry describing what/how to search</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="gPalElement">Optional element to scope/retry the search against</param>
        /// <param name="idx">Index into <paramref name="functionList"/> for the SelectorPathType's find function</param>
        /// <param name="functionList">Lookup table of FindWebElementsBy* delegates indexed by SelectorPathType</param>
        /// <param name="isPersistent">If true, searches within the persistent unit of work's context</param>
        /// <returns>The elements found for this selector path</returns>
        public static ReadOnlyCollection<GPALElement> FindElements(
            Browser browser,
            UnitOfWork currentUOW,
            SelectorSettings.SelectorPathEntry selectorPathEntry,
            Selector selector,
            GPALElement gPalElement,
            int idx,
            List<FindWebElementsByDelegate> functionList,
            bool isPersistent)
        {
            // .InElement found no containers, so there is nothing inside one to search
            bool noContainer = false;

            if (0 < browser.CurrentUOW.ContextPath?.Count && null == gPalElement)
            {
                // the containers .InElement named are found the first time a search runs inside them, so every
                // action after .InElement searches inside its container. Finding the innermost finds the ones it
                // sits in first. Persistent selectors, such as a cookie banner, search the page, not a container
                UnitOfWork.WebElementWithType notFound = browser.CurrentUOW.ContextPath
                    .LastOrDefault(step => ElementType.Element == step.elementType && null != step.selector && null == step.elements);

                if (false == isPersistent && null != notFound)
                    browser.RefreshContainers(notFound, false);

                if (true == browser.UseOttoMagic)
                    browser.MagicHelper.SwitchToDefaultContent();
                else if (true == browser.UsePuppeteer)
                    browser.PuppeteerClient.SwitchToDefaultContent().Execute();
                else if (true == browser.UseSelenium)
                    browser.BrowserDriver.SwitchTo().DefaultContent();

                IEnumerable<UnitOfWork.WebElementWithType> stepsToProcess;

                // puppeteer or selenium have to be iframe aware: process the full original path
                stepsToProcess = browser.CurrentUOW.ContextPath;

                // Now walk the (possibly filtered) steps
                foreach (UnitOfWork.WebElementWithType contextType in stepsToProcess)
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        if (true == browser.UseOttoMagic)
                            browser.MagicHelper.SwitchToElement(contextType.gPalElement.Css);
                        else if (true == browser.UsePuppeteer)
                            browser.PuppeteerClient.SwitchToFrame(contextType.gPalElement.Css).Execute();
                        else
                            browser.BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);

                        // We just switched into an iframe, so a parent element or shadow root is stale and throws
                        // "no such shadow root". Clear gPalElement; the next step sets it.
                        gPalElement = null;
                    }
                    else if (ElementType.ShadowRoot == contextType.elementType)
                    {
                        if (true == browser.UseOttoMagic)
                            browser.MagicHelper.SwitchToShadowRoot(contextType.gPalElement.Css);
                        else if (true == browser.UsePuppeteer)
                            browser.PuppeteerClient.SwitchToShadowRoot(contextType.gPalElement.Css).Execute();

                        // Scope to this step's shadow root, the one OttoMagic and Puppeteer just switched into.
                        // Selenium has no switch call; passing the ShadowRoot is the switch. Use this step's root,
                        // not the last one in the unit of work, or every step searches the innermost root.
                        gPalElement = contextType.gPalElement;
                    }
                    else if (ElementType.Element == contextType.elementType && false == isPersistent)
                    {
                        // The element for this pass when .InElement matched several, else the single match.
                        gPalElement = contextType.CurrentElement;   // this is what gets passed down

                        noContainer |= 0 == contextType.elements?.Count;
                    }
                }
            }

            int retries = true == noContainer ? 0 : 1;
            while (retries-- > 0)
            {
                try
                {
                    return functionList[idx](browser, selectorPathEntry, selector, gPalElement, isPersistent);
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error finding elements for selector [{selector.Name}]. Trying next selector path if defined.", browser, GPALObjectType.Browser, ex);
                    break; ;  // for retry handler
                }
            }

            return new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
        }
        // Helper to apply custom matching criteria
        /// <summary>
        /// Applies each Custom MatchCriteria entry's WebMatchingFunction to narrow (AND) or grow (OR) the set of
        /// matched elements, deduplicating results and honoring CallIfStatus.Terminate (throws),
        /// CallIfStatus.Handled (stops processing further criteria), and CallIfStatus.TryNextSelector
        /// (signals the caller to try the next selector path).
        /// </summary>
        /// <param name="selector">Selector whose Custom MatchCriteria entries are applied</param>
        /// <param name="allFoundElems">All elements found before custom matching</param>
        /// <param name="browser">Browser, used for diagnostic event publishing</param>
        /// <param name="matchedElements">Elements matched so far; updated in place with the resulting working set</param>
        /// <param name="tryNextSelector">Set to true if a custom match handler requested trying the next selector path</param>
        /// <param name="AndOrOr">True to AND (narrow) successive custom criteria, false to OR (union) them</param>
        /// <returns>True if all custom match criteria were satisfied (or none were defined)</returns>
        private static bool ApplyCustomMatchCriteria(
            Selector selector,
            ReadOnlyCollection<GPALElement> allFoundElems,
            Browser browser,
            List<GPALElement> matchedElements,
            out bool tryNextSelector,
            bool AndOrOr = true) // true = AND, false = OR
        {
            tryNextSelector = false;

            var universe = allFoundElems?.ToList();

            // initial working set
            var workingSet = matchedElements.Count > 0
                ? new List<GPALElement>(matchedElements)
                : new List<GPALElement>(universe);

            bool matchedAll = true; // default to true, if no custom match is applied to tell us otherwise

            foreach (var matchCriteriaEntry in selector.MatchCriteria)
            {
                if (matchCriteriaEntry.MatchType != MatchType.Custom)
                    continue;

                // Determine the source set
                var source = AndOrOr ? workingSet : universe;

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                    $"Applying custom match [{matchCriteriaEntry.WebMatchingFunction.GetInvocationList()[0].Method.Name}] " +
                    $"to {(AndOrOr ? "working set" : "full universe")} ({source.Count} elements)",
                    browser,
                    GPALObjectType.Browser);

                CallIfStatus handled = matchCriteriaEntry.WebMatchingFunction(
                    source.Cast<IGPALElement>().ToList(),
                    out List<IGPALElement> tempMatchedElemsRaw,
                    out bool roundMatchedAll,
                    selector,
                    matchCriteriaEntry.ExactMatch);
                // every custom function has to report all matched
                matchedAll = matchedAll && roundMatchedAll;
                List<GPALElement> tempMatchedElems = tempMatchedElemsRaw?.Cast<GPALElement>().ToList();

                // Deduplicate
                var matchedThisRound = new List<GPALElement>();
                var roundSet = new HashSet<GPALElement>();

                foreach (var elem in tempMatchedElems ?? new List<GPALElement>())
                {
                    if (roundSet.Add(elem))
                    {
                        matchedThisRound.Add(elem);

                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                            $"Element matched custom criteria [{matchCriteriaEntry.WebMatchingFunction.GetInvocationList()[0].Method.Name}] " +
                            $"TagName: [{elem.TagName}], Text: [{elem.Text}]",
                            browser,
                            GPALObjectType.Browser);
                    }
                }

                if (CallIfStatus.Terminate == handled)
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.EXCEPTION,
                        $"Custom match handler [{matchCriteriaEntry.WebMatchingFunction.GetInvocationList()[0].Method.Name}] requested program termination for selector [{selector.Name}].",
                        browser,
                        GPALObjectType.Browser);

                    throw new GPALException(
                        $"Custom match handler [{matchCriteriaEntry.WebMatchingFunction.GetInvocationList()[0].Method.Name}] requested program termination for selector [{selector.Name}].");
                }

                if (AndOrOr)
                {
                    // AND = shrink working set
                    workingSet = matchedThisRound;
                }
                else
                {
                    // OR = union into working set
                    var unionSet = new HashSet<GPALElement>(workingSet);
                    foreach (var elem in matchedThisRound)
                    {
                        if (unionSet.Add(elem))
                        {
                            workingSet.Add(elem);

                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                                $"Element added to OR working set from custom match: TagName: [{elem.TagName}], Text: [{elem.Text}]",
                                browser,
                                GPALObjectType.Browser);
                        }
                    }
                }

                if (CallIfStatus.Handled == handled)
                    break;

                if (CallIfStatus.TryNext == handled)
                {
                    tryNextSelector = true;
                    break;
                }
            }

            matchedElements.Clear();
            matchedElements.AddRange(workingSet);

            return matchedAll;
        }

        // Helper to apply simple matching criteria to elements after custom matching
        /// <summary>
        /// Applies each non-Custom MatchCriteria entry (Text, Href, Src, Placeholder, Value, Attribute, optionally
        /// Regex and/or ExactMatch) to narrow (AND) or grow (OR) the set of matched elements.
        /// </summary>
        /// <param name="selector">Selector whose simple MatchCriteria entries are applied</param>
        /// <param name="matchedElements">Elements matched so far (used as the initial working set if non-empty)</param>
        /// <param name="allFoundElems">All elements found before simple matching (used as the universe)</param>
        /// <param name="matchedElementsOut">Receives the resulting set of matched elements</param>
        /// <param name="AndOrOr">True to AND (narrow) successive criteria, false to OR (union) them</param>
        /// <returns>True if all simple match criteria were satisfied</returns>
        private static bool ApplySimpleMatching(
            Selector selector,
            List<GPALElement> matchedElements,
            List<GPALElement> allFoundElems,
            out List<GPALElement> matchedElementsOut,
            bool AndOrOr = true) // true = AND, false = OR
        {
            matchedElementsOut = new List<GPALElement>();
            var universe = allFoundElems?.ToList();

            // initial working set
            var workingSet = matchedElements.Count > 0
                ? new List<GPALElement>(matchedElements)
                : new List<GPALElement>(universe);

            foreach (var matchCriteriaEntry in selector.MatchCriteria)
            {
                if (matchCriteriaEntry.MatchType == MatchType.None || matchCriteriaEntry.MatchType == MatchType.Custom)
                    continue;

                // source for this round
                var source = AndOrOr ? workingSet : universe;
                var matchedThisRound = new List<GPALElement>();
                var roundSet = new HashSet<GPALElement>();

                foreach (var element in source)
                {
                    string elementStringToMatch = null;

                    if ("GPALElement" != element.TagName)
                    {
                        switch ((MatchType)((int)matchCriteriaEntry.MatchType & 0xFE))
                        {
                            case MatchType.Text:
                                elementStringToMatch = element.Text;
                                if (string.IsNullOrEmpty(elementStringToMatch))
                                    elementStringToMatch = element.GetAttribute("title");
                                if (string.IsNullOrEmpty(elementStringToMatch))
                                    elementStringToMatch = element.GetAttribute("value");
                                break;
                            case MatchType.Href:
                                elementStringToMatch = element.GetAttribute("href");
                                break;
                            case MatchType.Src:
                                elementStringToMatch = element.GetAttribute("src");
                                break;
                            case MatchType.Placeholder:
                                elementStringToMatch = element.GetAttribute("placeholder");
                                break;
                            case MatchType.Value:
                                elementStringToMatch = element.GetAttribute("value");
                                break;
                            case MatchType.Attribute:
                                elementStringToMatch = element.GetAttribute(selector.AttributeName);
                                break;
                        }

                        if (!string.IsNullOrEmpty(elementStringToMatch))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG,
                                $"String to match [{matchCriteriaEntry.StringToMatch}] element text [{elementStringToMatch}]");

                            bool isMatch = false;

                            if ((int)(matchCriteriaEntry.MatchType & MatchType.Regex) == 1)
                            {
                                var mc = Regex.Matches(elementStringToMatch, matchCriteriaEntry.StringToMatch);
                                if (mc.Count > 0)
                                    isMatch = true;
                            }
                            else
                            {
                                if (false == matchCriteriaEntry.ExactMatch && elementStringToMatch.Contains(matchCriteriaEntry.StringToMatch))
                                    isMatch = true;
                                else if (true == matchCriteriaEntry.ExactMatch && elementStringToMatch.Equals(matchCriteriaEntry.StringToMatch))
                                    isMatch = true;
                            }

                            if (isMatch && roundSet.Add(element))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Adding [{element.TagName}][{element.Text}]");
                                matchedThisRound.Add(element);
                            }
                        }
                    }
                    else
                    {
                        if (roundSet.Add(element))
                            matchedThisRound.Add(element);
                    }
                }

                if (AndOrOr)
                {
                    // AND = narrow
                    workingSet = matchedThisRound;
                }
                else
                {
                    // OR = union
                    var unionSet = new HashSet<GPALElement>(workingSet);
                    foreach (var elem in matchedThisRound)
                    {
                        if (unionSet.Add(elem))
                        {
                            workingSet.Add(elem);
                        }
                    }
                }
            }

            matchedElementsOut = workingSet;
            return universe.Count == matchedElementsOut.Count;
        }

        // The selector, unit of work and GPALSettings CallIf*Handled values are read only in this method, to decide whether the next handler level runs
        /// <summary>
        /// Invokes the CallIfFound or CallIfNotFound handler chain (Selector-level, then UnitOfWork-level, then
        /// global) depending on whether any elements were found/matched, bubbling to the next handler while it
        /// returns CallIfStatus.NotHandled, stopping at the first handler that returns Handled, and throwing if
        /// any handler returns Terminate.
        /// </summary>
        /// <param name="browser">Browser passed to the handlers</param>
        /// <param name="currentUOW">Current unit of work, whose CallIfFound/CallIfNotFound handlers may run</param>
        /// <param name="foundElements">All elements found for the selector</param>
        /// <param name="matchedElements">Elements that satisfied the selector's match criteria</param>
        /// <param name="selector">Selector whose CallIfFound/CallIfNotFound handlers may run</param>
        /// <param name="matchedAll">Whether all match criteria were satisfied</param>
        private static void CallIfHandlers(Browser browser, UnitOfWork currentUOW, ReadOnlyCollection<GPALElement> foundElements, List<GPALElement> matchedElements, Selector selector, bool matchedAll)
        {
            string str = null;
            string currentMethodName = null;
            int selectorsFoundOrMatched = selector.AnyMatchCriteria() ? matchedElements?.Count ?? 0 : foundElements?.Count ?? 0;
            UnitOfWork safeUOW;


            // bubble thru handlers based upon handler return value
            // 0 = not handled, bubble to next handler
            // 1 = handled and continue with the program
            // -1 = unexpected error and terminate program
            if (0 < selectorsFoundOrMatched)
            {
                if (0 != (selector.SelectorSettings.CallIfFound?.Count ?? 0))
                {
                    foreach (Browser.CallIfDelegate func in selector.SelectorSettings.CallIfFound)
                    {
                        if (true == selector.RemovedMethods.Contains(func))
                            continue;   // soft deleted by RemoveCallIfHandlerEverywhere

                        bool safeActionCalled = browser.CurrentUOW.ActionCalled;
                        safeUOW = browser.CurrentUOW;
                        browser.CurrentUOW.ActionCalled = true; // this will allow the callif method to immediately use browser.WaitFor

                        currentMethodName = func.Method.Name;
                        str = $"Browser Selector CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Selector CallIfFound [{func.Method.Name}]", browser, GPALObjectType.Browser);

                        selector.CallIfFoundHandled = func(browser, foundElements?.Cast<IGPALElement>().ToList(), matchedElements?.Cast<IGPALElement>().ToList(), selector, matchedAll);

                        browser.CurrentUOW = safeUOW;
                        browser.CurrentUOW.ActionCalled = safeActionCalled;

                        if (CallIfStatus.NotHandled == selector.CallIfFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == selector.CallIfFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }

                // selector.CallIfFoundHandled - if handled by selector handler, don't call UOW handler
                if (CallIfStatus.NotHandled == selector.CallIfFoundHandled && 0 < (currentUOW.CallIfFound?.Count ?? 0))
                {
                    foreach (Browser.CallIfDelegate func in currentUOW.CallIfFound)
                    {
                        if (true == selector.RemovedMethods.Contains(func))
                            continue;   // soft deleted by RemoveCallIfHandlerEverywhere

                        safeUOW = browser.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Browser UOW Selector CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking UOW CallIfFound [{func.Method.Name}]", browser, GPALObjectType.Browser);

                        currentUOW.CallIfFoundHandled = func(browser, foundElements?.Cast<IGPALElement>().ToList(), matchedElements?.Cast<IGPALElement>().ToList(), selector, matchedAll);

                        browser.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == currentUOW.CallIfFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == currentUOW.CallIfFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }

                // currentUOW.CallIfFoundHandled - if handled by UOW handler, don't call global handler
                if (CallIfStatus.NotHandled == selector.CallIfFoundHandled && 0 == currentUOW.CallIfFoundHandled && 0 != (GPAL.GPALSettings.CallIfFoundList?.Count ?? 0))
                {
                    foreach (Browser.CallIfDelegate func in GPAL.GPALSettings.CallIfFoundList)
                    {
                        if (true == selector.RemovedMethods.Contains(func))
                            continue;   // soft deleted by RemoveCallIfHandlerEverywhere

                        safeUOW = browser.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Global CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Global CallIfFound [{func.Method.Name}]", browser, GPALObjectType.Browser);

                        GPAL.GPALSettings.CallIfFoundHandled = func(browser, foundElements?.Cast<IGPALElement>().ToList(), matchedElements?.Cast<IGPALElement>().ToList(), selector, matchedAll);

                        browser.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == GPAL.GPALSettings.CallIfFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == (GPAL.GPALSettings.CallIfFoundHandled))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }
            }
            else
            {
                if (0 < selector.SelectorSettings.CallIfNotFound.Count)
                {
                    foreach (Browser.CallIfDelegate func in selector.SelectorSettings.CallIfNotFound)
                    {
                        if (true == selector.RemovedMethods.Contains(func))
                            continue;   // soft deleted by RemoveCallIfHandlerEverywhere

                        safeUOW = browser.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Browser Selector CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}[";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Selector CallIfNotFound [{func.Method.Name}]", browser, GPALObjectType.Browser);

                        selector.CallIfNotFoundHandled = func(browser, foundElements?.Cast<IGPALElement>().ToList(), matchedElements?.Cast<IGPALElement>().ToList(), selector, matchedAll);

                        browser.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == selector.CallIfNotFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == selector.CallIfNotFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }

                if (0 == selector.CallIfNotFoundHandled && 0 < currentUOW.CallIfNotFound.Count)
                {
                    foreach (Browser.CallIfDelegate func in currentUOW.CallIfNotFound)
                    {
                        if (true == selector.RemovedMethods.Contains(func))
                            continue;   // soft deleted by RemoveCallIfHandlerEverywhere

                        safeUOW = browser.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Browser UOW Selector CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking UOW CallIfNotFound [{func.Method.Name}]", browser, GPALObjectType.Browser);

                        currentUOW.CallIfNotFoundHandled = func(browser, foundElements?.Cast<IGPALElement>().ToList(), matchedElements?.Cast<IGPALElement>().ToList(), selector, matchedAll);

                        browser.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == currentUOW.CallIfNotFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == currentUOW.CallIfNotFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }
                // currentUOW.CallIfNotFoundHandled - if handled by UOW handler, don't call global handler
                if (0 == selector.CallIfNotFoundHandled && 0 == currentUOW.CallIfNotFoundHandled && 0 < GPAL.GPALSettings.CallIfNotFoundList.Count)
                {
                    foreach (Browser.CallIfDelegate func in GPAL.GPALSettings.CallIfNotFoundList)
                    {
                        if (true == selector.RemovedMethods.Contains(func))
                            continue;   // soft deleted by RemoveCallIfHandlerEverywhere

                        safeUOW = browser.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Global CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Global CallIfNotFound [{func.Method.Name}]", browser, GPALObjectType.Browser);

                        GPAL.GPALSettings.CallIfNotFoundHandled = func(browser, foundElements?.Cast<IGPALElement>().ToList(), matchedElements?.Cast<IGPALElement>().ToList(), selector, matchedAll);

                        browser.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == GPAL.GPALSettings.CallIfNotFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == GPAL.GPALSettings.CallIfNotFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }

                if (true == selector.StopOnNotFound || true == GPAL.StopOnNotFound)
                {
                    str = $@"{(true == selector.StopOnNotFound ? "selector." : "GPAL.")}StopOnNotFound set, terminating on Not Found element for [{selector.Name}][{selector.SelectorPaths[0].SelectorPath}]";
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                    throw new GPALException(str);
                }
            }
        }
        /// <summary>
        /// Finds elements matching a CSS selector path, dispatching to OttoMagic's QuerySelectors/QueryPersistentSelectors,
        /// Puppeteer's EvaluateSelector, or Selenium's FindElements(By.CssSelector), optionally scoped to <paramref name="webElement"/>.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the CSS selector string</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector query variant</param>
        /// <returns>The elements matching the CSS selector</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByCss(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .QueryPersistentSelectors(selectorPath.SelectorPath);
                else
                    result = browser.MagicHelper
                        .QuerySelectors(selectorPath.SelectorPath);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(selectorPath.SelectorPath));
                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                string css = selectorPath.SelectorPath;

                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.CssSelector(css))
                        // webelement is the shadowroot
                        //: browser.CurrentUOW.ShadowRoot?.WebElement != null
                        //    ? browser.CurrentUOW.ShadowRoot.WebElement.FindElements(By.CssSelector(css))
                        : browser.BrowserDriver.FindElements(By.CssSelector(css));

                // WebDriver gives only the host of a closed shadow root, so the search ran against the host. Nothing
                // found means the target is inside the closed root, so return the host, as OttoMagic does, and the
                // workflow can click at an offset. Not for persistent selectors: they are polled, and an
                // always-matching host would fire CallIf on every poll.
                if (0 == elems.Count && true == webElement?.IsClosedShadowRoot && false == isPersistent)
                    return new ReadOnlyCollection<GPALElement>(new List<GPALElement>() { webElement });

                GPALelems = ToGPALElements(browser, selector, elems);
            }

            return GPALelems;
        }
        /// <summary>
        /// Finds an element by matching the image at SelectorPath.SelectorPath against the screen, optionally
        /// scrolling (page down) until the image is found or the end of the page is reached, and wraps the
        /// matched screen rectangle in a pseudo GPALElement.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the path to the image file to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element used when checking for end-of-page during scrolling</param>
        /// <param name="isPersistent">Unused for image matching</param>
        /// <returns>A single-element collection wrapping the matched screen location, or null/empty if not found</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByImage(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;

            // Only hardware clicks need a screen capture. Everything else matches against the engine's capture of
            // the page, in page coordinates, so nothing outside the page can match and nothing is converted.
            bool inPage = true == browser.BrowserSettings.UseHeadless || true == browser.BrowserSettings.HiddenDesktop
                || false == UsesScreenCoordinates(browser, selector.InteractionType);

            ReadOnlyCollection<GPALElement> elems = null;

            Rectangle foundImageBounds = new Rectangle();
            List<GPALElement> retWebElement = new List<GPALElement>(); ;

            if (null == selectorPath.Image)
            {
                try
                {
                    selectorPath.Image = Image.FromFile(selectorPath.SelectorPath);
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to load image for [{selector.Name}].", browser, GPALObjectType.Browser, ex);
                    return null;
                }
            }

            // CaptureScreen reads ImageSearchInPage during this search; reset afterwards so later screenshots are
            // unaffected.
            browser.BrowserSettings.ImageSearchInPage = inPage;

            // With DEBUG on, every capture the search matches against is saved to the working folder, so a miss can be
            // seen: covered, wrapped, or scrolled past.
            bool saveCaptures = true == GPAL.GPALSettings.InformationHandlerEvents.HasFlag(GPALEventType.DEBUG);
            string captureName = $"ImageSearch.{browser.BrowserType}.{browser.AutomationEngine}.{Path.GetFileNameWithoutExtension(selectorPath.SelectorPath)}.png";

            foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, browser); // first check whether the image is already on screen

            if (true == saveCaptures)
                ((ImageHelper)GPAL.ImageHelper).SaveTo(captureName);

            // Page down looking for the image; skip when no image matcher is registered. No click on the body
            // first: every engine pages through its own protocol, so focus does not matter, and a Selenium click on
            // the body lands in its middle, often on a link.
            if (true == searchForElement && null != GPAL.ImageMatcher)
            {
                int pagesLeft = MaxScrollPages;

                // Not found, start from top of page
                if (true == foundImageBounds.IsEmpty)
                    _ = browser.PageTop;

                // Stop at MaxScrollPages as well as end of page, or a page whose bottom is never reported is paged
                // without end.
                while (true == foundImageBounds.IsEmpty && 0 < pagesLeft-- && false == IsEndOfPage(browser, webElement))
                {
                    _ = browser.PageDown;
                    Thread.Sleep(250);
                    foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, browser);

                    if (true == saveCaptures)
                        ((ImageHelper)GPAL.ImageHelper).SaveTo(captureName);
                }
            }

            browser.BrowserSettings.ImageSearchInPage = false;

            if (false == foundImageBounds.IsEmpty)
            {
                // Pseudo element for the match. Page matches are viewport coordinates, hardware screen matches are
                // screen coordinates; CoordinateSpace records which for the mouse code.
                retWebElement.Add(new GPALElement(foundImageBounds.Location, foundImageBounds.Size, selectorPath.SelectorPathType.ToString(), browser.BrowserDriver, "GPALElement")
                {
                    Browser = browser,
                    Selector = selector,
                    CoordinateSpace = true == inPage ? CoordinateSpace.Viewport : CoordinateSpace.Screen
                });
                elems = new ReadOnlyCollection<GPALElement>(retWebElement);

                // For a page match (not hardware), return the element under the match so later actions are ordinary
                // element actions on every engine. OttoMagic asks through execute-javascript, which needs Allow user
                // scripts on. With no element there, keep the point; the engine clicks it, or the hardware mouse when
                // hardware was asked for.
                if (true == inPage)
                {
                    ReadOnlyCollection<GPALElement> underMatch = ElementUnderImage(browser, retWebElement[0], selector);

                    if (0 < underMatch?.Count)
                        elems = underMatch;
                }
            }

            return elems;
        }
        /// <summary>
        /// Finds elements matching an XPath expression, dispatching to OttoMagic's EvaluateAll/EvaluateAllPersistent,
        /// Puppeteer's EvaluateSelector, or Selenium's FindElements(By.XPath), optionally scoped to <paramref name="webElement"/>.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the XPath expression</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector evaluation variant</param>
        /// <returns>The elements matching the XPath expression</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByXPath(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;

            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .EvaluateAllPersistent(selectorPath.SelectorPath);
                else
                    result = browser.MagicHelper
                        .EvaluateAll(selectorPath.SelectorPath);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(selectorPath.SelectorPath));

                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                string xpath = selectorPath.SelectorPath;

                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.XPath(xpath))
                    : browser.BrowserDriver.FindElements(By.XPath(xpath));

                // closed shadow root - see the CSS search, the host is all we can offer, and never for a
                // persistent selector
                if (0 == elems.Count && true == webElement?.IsClosedShadowRoot && false == isPersistent)
                    return new ReadOnlyCollection<GPALElement>(new List<GPALElement>() { webElement });

                GPALelems = ToGPALElements(browser, selector, elems);
            }

            return GPALelems;
        }
        /// <summary>
        /// Wraps found elements as GPALElements, reading every property for every element in one call.
        /// </summary>
        /// <remarks>
        /// Displayed means laid out and not hidden by style, a weaker test than the driver's. Location is document
        /// relative.
        /// </remarks>
        /// <param name="browser">Browser the elements were found in</param>
        /// <param name="selector">Selector that found them</param>
        /// <param name="elems">The elements the driver returned</param>
        /// <returns>The elements as GPALElements</returns>
        internal static ReadOnlyCollection<GPALElement> ToGPALElements(Browser browser, Selector selector, ReadOnlyCollection<IWebElement> elems)
        {
            List<GPALElement> gPALElements = new List<GPALElement>();

            if (0 < elems.Count)
            {
                string script = @"
                    return arguments[0].map(function (element) {
                        var rect = element.getBoundingClientRect();
                        var style = window.getComputedStyle(element);

                        return {
                            tag: element.tagName ? element.tagName.toLowerCase() : null,
                            text: element.innerText,
                            enabled: false === element.disabled || undefined === element.disabled,
                            selected: true === element.checked || true === element.selected,
                            indeterminate: true === element.indeterminate,
                            displayed: (0 < element.getClientRects().length) && 'hidden' !== style.visibility && 'none' !== style.display,
                            x: Math.round(rect.left + window.pageXOffset),
                            y: Math.round(rect.top + window.pageYOffset),
                            w: Math.round(rect.width),
                            h: Math.round(rect.height),
                            href: element.getAttribute('href'),
                            src: element.getAttribute('src'),
                            value: undefined !== element.value ? element.value : element.getAttribute('value'),
                            placeholder: element.getAttribute('placeholder'),
                            type: element.getAttribute('type')
                        };
                    });
                ";

                string pageUrl = browser.BrowserDriver.Url;
                object answered = ((IJavaScriptExecutor)browser.BrowserDriver).ExecuteScript(script, elems.ToList());
                List<object> properties = (answered as IEnumerable<object>)?.ToList();

                for (int idx = 0; idx < elems.Count; idx++)
                {
                    // If the script did not return every element, build each the slow way so none comes back empty.
                    GPALElement gPALElement = properties?.Count == elems.Count && properties[idx] is Dictionary<string, object> bag
                        ? new GPALElement(elems[idx], selector.AttributeName, bag, pageUrl)
                        : new GPALElement(elems[idx], selector.AttributeName);

                    gPALElement.Browser = browser;
                    gPALElement.Selector = selector;

                    gPALElements.Add(gPALElement);
                }
            }

            return new ReadOnlyCollection<GPALElement>(gPALElements);
        }
        /// <summary>
        /// Finds elements whose visible text contains SelectorPath.SelectorPath, by building an
        /// XPath <c>//*[contains(text(), '...')]</c> expression (relative to <paramref name="webElement"/> if given)
        /// and dispatching to OttoMagic, Puppeteer, or Selenium accordingly.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the text substring to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within (makes the XPath relative)</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector evaluation variant</param>
        /// <returns>The elements whose text contains the given substring</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByText(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;
            string xpathSelector = $"//*[contains(text(), '{selectorPath.SelectorPath}')]"; // Same XPath as Selenium

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .EvaluateAllPersistent(xpathSelector);
                else
                    result = browser.MagicHelper
                        .EvaluateAll(xpathSelector);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(xpathSelector));

                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                if (webElement != null)
                    xpathSelector = $".{xpathSelector}"; // Make relative XPath for webElement scope

                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.XPath(xpathSelector))
                    : browser.BrowserDriver.FindElements(By.XPath(xpathSelector));

                GPALelems = ToGPALElements(browser, selector, elems);
            }


            return GPALelems;
        }
        /// <summary>
        /// Finds anchor (&lt;a&gt;) elements whose href contains SelectorPath.SelectorPath, by building a CSS
        /// selector <c>a[href*='...']</c> for OttoMagic/Puppeteer, or using Selenium's FindElements(By.PartialLinkText).
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the href substring to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector query variant</param>
        /// <returns>The anchor elements whose href contains the given substring</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByHRef(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;
            string cssSelector = $"a[href*='{selectorPath.SelectorPath}']"; // Matches <a> elements with href containing the substring

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .QueryPersistentSelectors(cssSelector);
                else
                    result = browser.MagicHelper
                        .QuerySelectors(cssSelector);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(cssSelector));

                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.PartialLinkText(selectorPath.SelectorPath))
                    : browser.BrowserDriver.FindElements(By.PartialLinkText(selectorPath.SelectorPath));

                GPALelems = new ReadOnlyCollection<GPALElement>(
                    elems.Select(e => new GPALElement(e, selector.AttributeName) { Browser = browser, Selector = selector }).ToList()
                );
            }

            return GPALelems;
        }
        /// <summary>
        /// Finds elements with the CSS class SelectorPath.SelectorPath, by building a CSS selector
        /// <c>.classname</c> for OttoMagic/Puppeteer, or using Selenium's FindElements(By.CssSelector).
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the class name to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector query variant</param>
        /// <returns>The elements with the given CSS class</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByClassName(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;
            string cssSelector = $".{selectorPath.SelectorPath}";

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .QueryPersistentSelectors(cssSelector);
                else
                    result = browser.MagicHelper
                        .QuerySelectors(cssSelector);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(cssSelector));
                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.ClassName(selectorPath.SelectorPath))
                    : browser.BrowserDriver.FindElements(By.ClassName(selectorPath.SelectorPath));

                GPALelems = new ReadOnlyCollection<GPALElement>(
                    elems.Select(e => new GPALElement(e, selector.AttributeName) { Browser = browser, Selector = selector }).ToList()
                );
            }

            return GPALelems;
        }
        /// <summary>
        /// Finds elements with a "name" attribute equal to SelectorPath.SelectorPath, by building a CSS selector
        /// <c>[name='...']</c> for OttoMagic/Puppeteer, or using Selenium's FindElements(By.Name).
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the "name" attribute value to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector query variant</param>
        /// <returns>The elements with the given "name" attribute</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByName(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;
            string cssSelector = $"[name='{selectorPath.SelectorPath}']";

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .QueryPersistentSelectors(cssSelector);
                else
                    result = browser.MagicHelper
                        .QuerySelectors(cssSelector);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(cssSelector));

                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.Name(selectorPath.SelectorPath))
                    : browser.BrowserDriver.FindElements(By.Name(selectorPath.SelectorPath));

                GPALelems = new ReadOnlyCollection<GPALElement>(
                    elems.Select(e => new GPALElement(e, selector.AttributeName) { Browser = browser, Selector = selector }).ToList()
                );
            }

            return GPALelems;
        }
        /// <summary>
        /// Finds elements with a "value" attribute equal to SelectorPath.SelectorPath, by building a CSS selector
        /// <c>[value='...']</c> for OttoMagic/Puppeteer. Selenium has no By.Value, so this falls through to the
        /// CSS attribute selector for Selenium as well.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the "value" attribute value to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector query variant</param>
        /// <returns>The elements with the given "value" attribute</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByValue(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;
            string cssSelector = $"[value='{selectorPath.SelectorPath}']";

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .QueryPersistentSelectors(cssSelector);
                else
                    result = browser.MagicHelper
                        .QuerySelectors(cssSelector);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(cssSelector));
                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.CssSelector($"[value='{selectorPath.SelectorPath}']"))
                    : browser.BrowserDriver.FindElements(By.CssSelector($"[value='{selectorPath.SelectorPath}']"));

                GPALelems = new ReadOnlyCollection<GPALElement>(
                    elems.Select(e => new GPALElement(e, selector.AttributeName) { Browser = browser, Selector = selector }).ToList()
                );
            }

            return GPALelems;
        }
        /// <summary>
        /// Finds elements with a "placeholder" attribute equal to SelectorPath.SelectorPath, by building a CSS
        /// selector <c>[placeholder='...']</c> for OttoMagic, Puppeteer and Selenium alike.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selectorPath">Selector path containing the "placeholder" attribute value to match</param>
        /// <param name="selector">The owning Selector</param>
        /// <param name="webElement">Optional element to scope the search within</param>
        /// <param name="isPersistent">If true (OttoMagic only), uses the persistent-selector query variant</param>
        /// <returns>The elements with the given "placeholder" attribute</returns>
        private static ReadOnlyCollection<GPALElement> FindWebElementsByPlaceholder(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false)
        {
            bool searchForElement = selector.SearchForSelector;
            ReadOnlyCollection<IWebElement> elems;
            ReadOnlyCollection<GPALElement> GPALelems = null;
            // backslashes and quotes escaped, since placeholders are prose and "What's on your mind?" would end the css string early
            string cssSelector = $"[placeholder='{selectorPath.SelectorPath.Replace("\\", "\\\\").Replace("'", "\\'")}']"; // Any element with placeholder

            if (browser.UseOttoMagic)
            {
                List<GPALElement> result = null;

                if (false == webElement?.IsShadowRoot)
                    browser.MagicHelper.SwitchToElement(webElement.Css);

                if (true == isPersistent)
                    result = browser.MagicHelper
                        .QueryPersistentSelectors(cssSelector);
                else
                    result = browser.MagicHelper
                    .QuerySelectors(cssSelector);

                if (null != result)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result);
            }
            else if (true == browser.UsePuppeteer)
            {
                if (false == webElement?.IsShadowRoot)
                    browser.PuppeteerClient.InElement(webElement.Css).Execute();

                var result2 = Task.Run(() => browser.PuppeteerCommunicator.EvaluateSelector(cssSelector));
                if (null != result2)
                    GPALelems = new ReadOnlyCollection<GPALElement>(result2.Result);
            }
            else
            {
                elems = webElement?.WebElement != null
                    ? webElement.WebElement.FindElements(By.CssSelector(cssSelector))
                    : browser.BrowserDriver.FindElements(By.CssSelector(cssSelector));

                GPALelems = new ReadOnlyCollection<GPALElement>(
                    elems.Select(e => new GPALElement(e, selector.AttributeName) { Browser = browser, Selector = selector }).ToList()
                );
            }

            return GPALelems;
        }

        /// <summary>
        /// Publish specific log message that we are ignoring a selector marked for deletion or the selector search resulted in finding no elements
        /// In either case, we are continuing to the next selector(s), if any
        /// </summary>
        /// <param name="browser">Browser the warning is published for</param>
        /// <param name="selector"></param>
        /// <param name="webElements"></param>
        public static void PublishToEventHandler(Browser browser, Selector selector, List<GPALElement> webElements)
        {
            string msg = "";
            if (true == selector.DeleteMe)
                msg = $"Ignoring selector [{selector.Name}] marked for deletion. Continuing.";
            else if (null == webElements || 0 == webElements.Count)
                msg = $"No elements found for selector [{selector.Name}]. Continuing.";

            GPAL.PublishSimpleEvent(GPALEventType.WARNING, msg, browser, GPALObjectType.Browser);
        }
        /// <summary>
        /// Waits up to <paramref name="timeoutInTicks"/> for elements matching every Selector in the current
        /// unit of work's WithSelectorList, collecting the matched elements from each (up to WithAllThatMatch
        /// per selector). A selector marked DeleteMe, or one whose search gives no matched list, is skipped with a
        /// log message.
        /// </summary>
        /// <param name="browser">Browser whose CurrentUOW selectors are waited on</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds (WaitTime.Forever for indefinite)</param>
        /// <param name="matchedAll">Set to true if the last selector processed matched all its criteria</param>
        /// <returns>The combined list of matched elements across all selectors</returns>
        public static List<GPALElement> WaitFor(Browser browser, int timeoutInTicks, out bool matchedAll)
        {
            bool matchedAll2 = true;
            List<GPALElement> foundElements = null;
            List<GPALElement> matchedElements;

            foundElements = new List<GPALElement>();

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                WaitFor(browser, selector, timeoutInTicks, out matchedAll2, out matchedElements);


                // a selector an earlier CallIf handler marked for deletion is passed over
                if (true == selector.DeleteMe || null == matchedElements)
                {
                    PublishToEventHandler(browser, selector, matchedElements);
                    continue;
                }

                int rowCount = 0;

                foreach (GPALElement webElement in matchedElements)
                {
                    foundElements.Add(webElement);

                    if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                        break;
                }
            }

            matchedAll = matchedAll2;
            return foundElements;
        }
        /// <summary>
        /// Polls FindWebElements for <paramref name="selector"/> (sleeping up to 1 second between attempts) until
        /// it returns matched elements or <paramref name="timeoutInTicks"/> elapses (WaitTime.Forever waits
        /// indefinitely). If the unit of work also has a WaitForElementState set, it then waits for every element the
        /// selector matches to be in that state. For Hidden it searches once and goes straight to that wait, since an
        /// element that is not there is hidden.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selector">Selector to wait for</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds (WaitTime.Forever for indefinite)</param>
        /// <param name="matchedAll">Set to true if the selector's match criteria (and any WaitForElementState) were satisfied</param>
        /// <param name="matchedElements">Receives the elements that matched the selector's criteria</param>
        /// <param name="webElement">Optional element to scope/retry the search against</param>
        /// <returns>All elements found for the selector (before match-criteria filtering)</returns>
        private static List<GPALElement> WaitFor(Browser browser, Selector selector, int timeoutInTicks, out bool matchedAll, out List<GPALElement> matchedElements, GPALElement webElement = null)
        {
            bool matchedAll2 = true;
            List<GPALElement> matchedElements2;
            ReadOnlyCollection<GPALElement> elements;
            int orgTimeoutInTicks = timeoutInTicks;

            if (WaitTime.Forever == timeoutInTicks)
                timeoutInTicks = Int32.MaxValue;    // forever

            // the start of the wait, at DEBUG: INFO gets one line when the wait is over
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Waiting up to [{timeoutInTicks}] ms for elements matching selector [{selector.Name}][{selector.SelectorPath}]", browser, GPALObjectType.Browser);

            // waiting for Hidden needs no element to appear first
            bool waitForHidden = ElementState.Hidden == browser.CurrentUOW.WaitForElementState;

            var sw = Stopwatch.StartNew();
            do
            {
                // the polls publish nothing; the wait publishes one line when it is over
                elements = FindWebElements(browser, browser.CurrentUOW, selector,
                    out matchedAll2, out matchedElements2, webElement, false, publish: false);

                // sleep only while time remains, so the sleep time is never negative; a search that gives no list
                // sleeps too, so the loop does not spin
                if (0 == (matchedElements2?.Count ?? 0) && false == waitForHidden && WaitTime.Immediate != timeoutInTicks && sw.ElapsedMilliseconds < timeoutInTicks)
                    Thread.Sleep((int)Math.Min(1000, timeoutInTicks - sw.ElapsedMilliseconds));

            } while (sw.ElapsedMilliseconds < timeoutInTicks && false == waitForHidden &&
                     (matchedElements2 == null || matchedElements2.Count == 0));

            // what the last search found, with its path in full, how long the wait took and what it allowed
            string found = lastFoundFullMessage ?? $"[0] elements found for selector [{selector.Name}]";

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"{found} in [{sw.ElapsedMilliseconds}] ms of {(Int32.MaxValue == timeoutInTicks ? "[forever]" : $"[{timeoutInTicks}] ms")}", browser, GPALObjectType.Browser);

            matchedAll = matchedAll2;
            matchedElements = matchedElements2;         // always use matched, it's either all or those matched

            // with a WaitFor state set, wait for the matched elements to reach it; Hidden waits with none matched too
            if (ElementState.NotSet != browser.CurrentUOW.WaitForElementState && (0 < (matchedElements2?.Count ?? 0) || true == waitForHidden))
                matchedAll &= WaitForState(browser, selector, browser.CurrentUOW.WaitForElementState, orgTimeoutInTicks, webElement);

            return elements?.ToList<GPALElement>();
        }

        /// <summary>
        /// Waits for the elements of a selector to reach a state, finding them again about once a second, so the state
        /// is read from the page as it is now on every engine. An element the search no longer finds is hidden. A wait
        /// time below zero, when no WaitFor(timeInMs) was set, waits three seconds.
        /// </summary>
        /// <param name="browser">Browser to search in</param>
        /// <param name="selector">Selector whose elements are waited on</param>
        /// <param name="state">The state to wait for</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds (WaitTime.Forever for indefinite)</param>
        /// <param name="webElement">Optional element to scope the search to</param>
        /// <returns>True when every element the selector matches is in the state in time</returns>
        private static bool WaitForState(Browser browser, Selector selector, ElementState state, int timeoutInTicks, GPALElement webElement)
        {
            const int defaultStateWaitInTicks = 3000;
            bool inState = false;

            if (WaitTime.Forever == timeoutInTicks)
                timeoutInTicks = Int32.MaxValue;
            else if (timeoutInTicks < 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WaitFor(ElementState.[{state}]) needs a positive wait time but got [{timeoutInTicks}] ms (no .WaitFor(timeInMs) was set for this unit of work) - using the default of [{defaultStateWaitInTicks}] ms instead.", browser, GPALObjectType.Browser);
                timeoutInTicks = defaultStateWaitInTicks;
            }

            var sw = Stopwatch.StartNew();
            do
            {
                FindWebElements(browser, browser.CurrentUOW, selector, out bool _, out List<GPALElement> live, webElement, false);
                List<GPALElement> found = live ?? new List<GPALElement>();

                // Hidden holds when every element found is hidden, none found included; any other state needs elements
                inState = ElementState.Hidden == state
                    ? found.All(element => IsElementInState(browser, element, state))
                    : 0 < found.Count && found.All(element => IsElementInState(browser, element, state));

                if (false == inState && sw.ElapsedMilliseconds < timeoutInTicks)
                    Thread.Sleep((int)Math.Min(1000, timeoutInTicks - sw.ElapsedMilliseconds));

            } while (false == inState && sw.ElapsedMilliseconds < timeoutInTicks);

            if (false == inState)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Elements for selector [{selector.Name}] not in state [{state}] within [{timeoutInTicks}] ms.", browser, GPALObjectType.Browser);

            return inState;
        }

        /// <summary>
        /// Waits for the specified element to reach the given state.
        /// </summary>
        /// <param name="browser">The browser instance.</param>
        /// <param name="element">The element to wait for.</param>
        /// <param name="state">The desired element state.</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds; -1 for indefinite wait.</param>
        /// <returns>True if the element reached the state; false if timed out.</returns>
        public static bool WaitFor(Browser browser, GPALElement element, ElementState state, int timeoutInTicks)
        {
            const int defaultStateWaitInTicks = 3000;

            if (WaitTime.Forever == timeoutInTicks)
            {
                timeoutInTicks = Int32.MaxValue;
            }
            else if (timeoutInTicks < 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WaitFor(ElementState.[{state}]) needs a positive wait time but got [{timeoutInTicks}] ms (no .WaitFor(timeInMs) was set for this unit of work) - using the default of [{defaultStateWaitInTicks}] ms instead.", element, GPALObjectType.Other);
                timeoutInTicks = defaultStateWaitInTicks;
            }

            int retries = 2;
            bool retry = true;
            dynamic retryElement = element;
            int tmpTimeoutInTicks = timeoutInTicks;

            do
            {
                try
                {
                    int sleepTime = Math.Min(1000, tmpTimeoutInTicks);

                    Thread.Sleep(sleepTime);

                    if (IsElementInState(browser, element, state))
                        return true;

                    tmpTimeoutInTicks -= sleepTime;
                }
                catch (StaleElementReferenceException sereex)
                {
                    string message = $"StaleElementReferenceException while waiting for state [{state}]: [{sereex.Message}]";
                    retries--;
                    retry = ResolveWebElementIssue(browser, browser.CurrentUOW, ref retryElement, sereex, ref retries, message);
                    if (retry)
                        element = retryElement;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error while waiting for state [{state}]", element, GPALObjectType.Other, ex);
                    retry = false;
                }
            } while (tmpTimeoutInTicks > 0 && retry);

            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element not in state [{state}] within [{timeoutInTicks}] ms.", element, GPALObjectType.Other);
            return false;
        }

        /// <summary>
        /// Checks if an element is in the specified state.
        /// </summary>
        /// <param name="browser">The browser instance.</param>
        /// <param name="element">The element to check.</param>
        /// <param name="state">The state to verify.</param>
        /// <returns>True if the element is in the specified state; otherwise, false.</returns>
        private static bool IsElementInState(Browser browser, GPALElement element, ElementState state)
        {
            try
            {
                switch (state)
                {
                    case ElementState.Visible:
                        return element.Displayed;
                    case ElementState.Hidden:
                        return !element.Displayed;
                    case ElementState.Enabled:
                        return element.Enabled;
                    case ElementState.Disabled:
                        return !element.Enabled;
                    case ElementState.Checked:
                        return element.Selected || element.GetAttribute("checked") == "true";
                    case ElementState.Unchecked:
                        return !element.Selected && element.GetAttribute("checked") != "true";
                    case ElementState.Selected:
                        return element.Selected;
                    case ElementState.Unselected:
                        return !element.Selected;
                    case ElementState.Clickable:
                        return IsElementClickable(element);
                    case ElementState.Editable:
                        return IsElementEditable(element);
                    default:
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported element state [{state}].", element, GPALObjectType.Other);
                        return false;
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error checking state [{state}]", element, GPALObjectType.Other, ex);
                return false;
            }
        }

        /// <summary>
        /// Re-enters the iframe chain recorded in the current unit of work after the driver has been switched
        /// back to the top document. Selenium scopes both script evaluation and element lookup to whichever frame
        /// the driver is in, so anything that has to be measured against the top document has to put the context
        /// back when it is done.
        /// </summary>
        /// <param name="browser">Browser whose frame context should be restored</param>
        private static void RestoreFrameContext(Browser browser)
        {
            if (null == browser.CurrentUOW?.ContextPath)
                return;

            foreach (UnitOfWork.WebElementWithType contextType in browser.CurrentUOW.ContextPath)
                if (ElementType.IFrame == contextType.elementType && null != contextType.gPalElement?.WebElement)
                    browser.BrowserDriver.SwitchTo().Frame((IWebElement)contextType.gPalElement.WebElement);
        }
        /// <summary>
        /// Attempts to recover from a Selenium exception (stale element, click intercepted, element not visible)
        /// by refreshing the element reference, scrolling it into view, or dismissing overlays, then reports
        /// whether the caller should retry the failed operation.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="currentUOW">Current unit of work, used to re-find the element if needed</param>
        /// <param name="webOrGpalElement">The element involved, as a GPALElement or IWebElement; may be updated with a fresh reference</param>
        /// <param name="exception">The exception that triggered recovery</param>
        /// <param name="retries">Remaining retry count</param>
        /// <param name="errorMessage">Unused</param>
        /// <returns>True if recovery succeeded and the operation should be retried</returns>
        internal static bool ResolveWebElementIssue(Browser browser, UnitOfWork currentUOW, ref dynamic webOrGpalElement, Exception exception, ref int retries, string errorMessage)
        {
            // recovery actions are turned off
            if (true == GPAL.NoFallbackRecoveryActions)
                return false;

            bool shouldRetry = false;
            IWebElement iWebElement;
            GPALElement gPalElement = null;

            if (webOrGpalElement is GPALElement)
            {
                gPalElement = webOrGpalElement;
                iWebElement = gPalElement.WebElement;
            }
            else
            {
                iWebElement = webOrGpalElement;
                gPalElement = new GPALElement() { WebElement = iWebElement };
            }

            // Log the recovery attempt
            string recoveryMessage = $"Attempting recovery for [{exception.GetType().Name}]. Retries left: [{retries}]";
            GPAL.PublishSimpleEvent(GPALEventType.INFO, recoveryMessage, iWebElement, GPALObjectType.Other);

            try
            {
                if (exception is StaleElementReferenceException)
                {
                    // A stale element was replaced by the page, usually by a re-render. Find it again by its own css
                    // or xpath, so the retry acts on the same element, not the selector's first match. With no
                    // element the search went stale, so clear the selector's results and rerun it.
                    string path = false == string.IsNullOrEmpty(gPalElement.Css) ? gPalElement.Css : gPalElement.Xpath;

                    if (null == webOrGpalElement)
                    {
                        currentUOW.CurrentSelector.WebSelectorMatchedResults = null;
                        currentUOW.CurrentSelector.WebSelectorFoundResults = null;
                        shouldRetry = true;
                    }
                    else if (true == string.IsNullOrEmpty(path))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"An element went stale and has no css or xpath to find it again by", browser, GPALObjectType.Browser);
                    else
                    {
                        By byPath = false == string.IsNullOrEmpty(gPalElement.Css) ? By.CssSelector(path) : By.XPath(path);
                        IWebElement fresh = browser.BrowserDriver.FindElements(byPath).FirstOrDefault();

                        if (null == fresh)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element [{path}] is no longer on the page", browser, GPALObjectType.Browser);
                        else
                        {
                            gPalElement.WebElement = fresh;

                            if (false == webOrGpalElement is GPALElement)
                                webOrGpalElement = fresh;

                            shouldRetry = true;
                        }
                    }
                }
                else if (exception is ElementClickInterceptedException)
                {
                    // Scroll the element into view
                    ElementHelper.MoveTo(gPalElement, currentUOW.CurrentSelector.OffsetX, currentUOW.CurrentSelector.OffsetY, browser);

                    // Retry when the element is now visible and enabled
                    if (false == (shouldRetry = IsElementClickable(iWebElement)))
                    {
                        // CAVEAT: overlays are found by class names containing modal, overlay or popup, which can miss some and catch others
                        // Close or hide a displayed modal, overlay or popup
                        try
                        {
                            var overlays = browser.BrowserDriver.FindElements(By.XPath("//div[contains(@class, 'modal') or contains(@class, 'overlay') or contains(@class, 'popup')]"));
                            if (overlays.Any())
                            {
                                foreach (var overlay in overlays)
                                {
                                    if (overlay.Displayed)
                                    {
                                        var closeButton = browser.BrowserDriver.FindElements(By.XPath(".//button[contains(@class, 'close') or contains(text(), 'Close') or @aria-label='Close']"));
                                        if (closeButton.Any())
                                        {
                                            closeButton.First().Click();
                                            shouldRetry = IsElementClickable(iWebElement);
                                        }
                                        else
                                        {
                                            BrowserHelper.ExecuteJavaScriptObj("arguments[0].style.display='none';", browser, overlay);
                                        }
                                    }
                                }
                            }
                        }
                        catch (NoSuchElementException)
                        {
                            // No overlay found, proceed
                        }
                    }
                }
                else if (exception is ElementNotInteractableException)
                {
                    // Scroll the element into view
                    ElementHelper.MoveTo(gPalElement, currentUOW.CurrentSelector.OffsetX, currentUOW.CurrentSelector.OffsetY, browser);

                    // Still not visible and enabled: scroll up 100px, out from under a sticky header
                    if (false == IsElementClickable(iWebElement))
                    {
                        browser.BrowserSettings.SuppressJavaScriptFailures = true;
                        browser.ExecuteJavaScriptStr("window.scrollBy({ top: -100, behavior: 'instant' });");
                        browser.BrowserSettings.SuppressJavaScriptFailures = false;
                    }

                    shouldRetry = IsElementClickable(iWebElement);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log recovery failure
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Recovery failed", iWebElement, GPALObjectType.Other, ex);
                shouldRetry = false;
            }

            return shouldRetry && retries > 0;
        }
        public static string GetEnumNames<T>(T value) where T : struct, Enum
        {
            if (!typeof(T).IsDefined(typeof(FlagsAttribute), false))
                return value.ToString();

            var flags = Enum.GetValues(typeof(T))
                .Cast<T>()
                .Where(flag => !Equals(flag, default(T)) && value.HasFlag(flag))
                .Select(flag => flag.ToString());

            string retVal = string.Join(", ", flags);

            return false == string.IsNullOrEmpty(retVal) ? retVal : "NONE";
        }
        /// <summary>
        /// Generates a unique CSS selector path for <paramref name="webElement"/> by walking up the DOM tree
        /// from the element to the document root.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to generate a CSS selector for</param>
        /// <returns>A CSS selector that uniquely identifies the element</returns>
        public static string GenerateCssSelector(IBrowser browser, IWebElement webElement)
        {
            return BrowserHelper.ExecuteJavaScript(browser, @"
                function getCssPath(el) {
                    if (!(el instanceof Element))
                        return null;

                    var path = [];

                    while (el && el.nodeType === Node.ELEMENT_NODE) {

                        if (el.id) {
                            path.unshift('#' + CSS.escape(el.id));
                            break;
                        }

                        var selector = el.nodeName.toLowerCase();

                        var sibling = el;
                        var nth = 1;

                        while ((sibling = sibling.previousElementSibling) != null) {
                            if (sibling.nodeName === el.nodeName)
                                nth++;
                        }

                        selector += ':nth-of-type(' + nth + ')';

                        path.unshift(selector);

                        el = el.parentElement;
                    }

                    return path.join(' > ');
                }
                return getCssPath(arguments[0]);
                ", webElement);
        }
    }
}
