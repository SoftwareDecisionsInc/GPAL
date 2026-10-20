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

namespace GenerallyPositive.Browser
{

    internal class ElementHelper
    {
        public delegate ReadOnlyCollection<GPALElement> FindWebElementsByDelegate(Browser browser, SelectorPathEntry selectorPath, Selector selector, GPALElement webElement = null, bool isPersistent = false);

        // to avoid repeat messages for the same selector when waiting for a selector
        static List<string> listOfMessages = new List<string>();
        static bool supressedMessage = false;
        static Selector lastSelector = null;

        // how hard ScrollUntilVisible works before it gives up and lets the workflow carry on
        const int VisibilitySettleChecks = 4;    // rechecks after a scroll, to ride out a header sliding in
        const int VisibilitySettleMs = 100;      // wait between those rechecks
        const int MaxScrollPages = 40;           // screens to page through before giving up on finding it

        // grok refactor
        public static ReadOnlyCollection<GPALElement> FindWebElements(
            Browser browser,
            UnitOfWork currentUOW,
            Selector selector,
            out bool matchedAll,
            out List<GPALElement> matchedElements,
            GPALElement webElement = null,
            bool callIfHandler = true,
            bool isPersistent = false)
        {
            int retries = 1;
            bool retry = false;
            int withAllThatMatch = int.MaxValue;

            // next page button does not go thru .withselector to add browser. i suppose they could all just use this spot.
            if (null == selector.Browser)
                selector.Browser = browser;

            dynamic ourRetryElement = webElement;
            ReadOnlyCollection<GPALElement> retElems = new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
            string message = string.Empty;

            currentUOW.CurrentSelector = selector;

            if (false == lastSelector?.Name.Equals(selector.Name))
            {
                listOfMessages.Clear();
                supressedMessage = false;
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
            tryAgain:   // NOTE: CAVEAT: goto label - don't like it, but it has to happen sometimes. we cannot have any inner loop as that will break retrying the next selector
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
                    message = $"Searching for elements for selector [{selector.Name}] using [{selectorPathEntry.SelectorPathType}] [{abbreviatedText}]";

                    if (false == listOfMessages.Contains(message))
                    {
                        listOfMessages.Add(message);
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, message, browser, GPALObjectType.Browser);
                    }

                    var tmpAllFoundElems = FindElements(browser, currentUOW, selectorPathEntry, selector, webElement, idx, functionList, isPersistent);

                    // Some finders return null for nothing found, such as an unmatched image or an OttoMagic XPath with
                    // no hits. Turn null into an empty collection so neither the return value nor the out param is null.
                    retElems = allFoundElems = tmpAllFoundElems ?? new ReadOnlyCollection<GPALElement>(new List<GPALElement>());
                    matchedElements = allFoundElems.ToList();

                    if (0 < retElems?.Count)
                    {
                        // NOTE: puppeteer and ottomagic give us gpalelements with individual Css on each element, not so for selenium
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
                        if (selector.MatchCriteria.Count > 0)
                            matchedAll = ApplySimpleMatching(selector, matchedElements, matchedElements, out simpleMatches);
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
                    goto tryAgain;  // NOTE: CAVEAT: goto label
                }

                selector.ElementsFoundAndMatchedCount = (int)(null != matchedElements ? matchedElements?.Count() : 0);

                message = $"[{retElems?.Count ?? 0}] elements found for selector [{selector.Name}]";

                if (false == listOfMessages.Contains(message))
                {
                    listOfMessages.Add(message);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message, browser, GPALObjectType.Browser);

                    if (0 < retElems?.Count && (true == selector.AnyMatchCriteria() || Int32.MaxValue != withAllThatMatch))
                    {
                        var matchCount = selector.MatchCriteria.Count > 0 ? selector.MatchCriteria.Count.ToString() : $"WithAllThatMatch({withAllThatMatch})";

                        if ((true == matchCount.Contains("WithAllThatMatch") && 0 < withAllThatMatch) || 0 < selector.MatchCriteria.Count)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{matchedElements?.Count}] elements matched for selector [{selector.Name}] with [{matchCount}] match criteria", browser, GPALObjectType.Browser);
                    }
                }
                else if (false == supressedMessage)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", browser, GPALObjectType.Browser);
                    supressedMessage = true;
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
            if (0 < browser.CurrentUOW.ContextPath?.Count && null == gPalElement)
            {

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
                    else if (ElementType.Element == contextType.elementType)
                    {
                        // The element for this pass when .InElement matched several, else the single match.
                        gPalElement = contextType.CurrentElement;   // this is what gets passed down
                    }
                }
            }

            int retries = 1;
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
                    out matchedAll,
                    selector,
                    matchCriteriaEntry.ExactMatch);
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

        // TODO: what do these values mean outside of the bubble handlers?
        // currentUOW.callFoundHandled
        // currentUOW.callNotFoundHandled
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

            // TODO: do we care about matchedall?

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
                        str = $"Gloabl CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

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
                        str = $"Gloabl CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

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

            foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, browser); // first check, is image we seek already on-screen?

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
                // TODO: highlight the found element briefly with a border? .WithHighlightFoundImage
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
        /// per selector). Selectors marked DeleteMe, or for which CallIf handlers return null matched elements,
        /// are skipped (with a log message) and not waited on.
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


                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
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
        /// indefinitely). If the unit of work also has a WaitForElementState set, additionally waits for each
        /// matched element to reach that state.
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

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Waiting up to [{timeoutInTicks}] ms for elements matching selector [{selector.Name}][{selector.SelectorPath}]", browser, GPALObjectType.Browser);

            var sw = Stopwatch.StartNew();
            do
            {
                elements = FindWebElements(browser, browser.CurrentUOW, selector,
                    out matchedAll2, out matchedElements2, webElement, false);

                // we might've run out of time already and we could generate a negative sleep time
                if (0 == matchedElements2?.Count && WaitTime.Immediate != timeoutInTicks && sw.ElapsedMilliseconds < timeoutInTicks)
                    Thread.Sleep((int)Math.Min(1000, timeoutInTicks - sw.ElapsedMilliseconds));

            } while (sw.ElapsedMilliseconds < timeoutInTicks &&
                     (matchedElements2 == null || matchedElements2.Count == 0));

            matchedAll = matchedAll2;   // one of those found it or not :)
            matchedElements = matchedElements2;         // always use matched, it's either all or those matched

            // element found, did we set a waitfor state as well?
            if (ElementState.NotSet != browser.CurrentUOW.WaitForElementState)
                foreach (GPALElement gPALElement in matchedElements2)   // will be all if no match set
                    matchedAll &= WaitFor(browser, gPALElement, browser.CurrentUOW.WaitForElementState, orgTimeoutInTicks);

            return elements?.ToList<GPALElement>();
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
        /// Fill in text on an element using the technique defined in the selector
        /// do this for the entire unit of work
        /// </summary>
        /// <param name="browser">Browser whose CurrentUOW selectors are filled in</param>
        /// <param name="useText">One string of text to write</param>
        /// <param name="writeMode">Whether to overwrite, append to or insert before the current value</param>
        /// <param name="element">Element to fill in instead of the elements the selectors find; null to use the selectors</param>
        public static bool FillInFrom(Browser browser, string useText, WriteMode writeMode, GPALElement element = null)
        {
            bool retVal = false;    // did we fill in anything? useful for SendString to try this before sending to <body>
            ReadOnlyCollection<GPALElement> elems = null;
            List<GPALElement> matchedElements = null;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                if (null == element)
                    elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != element)
                {
                    var tmpElems = new List<GPALElement>();
                    tmpElems.Add(element);
                    elems = new ReadOnlyCollection<GPALElement>(tmpElems);
                }
                else if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                if (true == elems.Any())
                    handleFillIn(elems);

                if (true == retVal && null != browser.CurrentUOW.CallAfterFillIn && true == elems.Any())
                {
                    UnitOfWork safeUOW = browser.CurrentUOW;
                    CallIfStatus handled = 0;

                    // CAVEAT: there is no concept of 'handled (1)' vs 'not handled (0)' but definitely can request to exit
                    // call CallAfterFillIn, if defined and pass in the tokens we are currently using (which is just a string in this method)
                    IGPALGrid<string> tokens = GPAL.GridForType<string>();
                    tokens.AddRow(new List<string> { useText });

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking CallAfterFillIn [{browser.CurrentUOW.CallAfterFillIn.Method.Name}]", browser, GPALObjectType.Browser);
                    handled = browser.CurrentUOW.CallAfterFillIn(browser, tokens, 1);

                    // CAVEAT: kludge - the call after handler can and probably will set a new current unit of work, but we need our old current unit
                    // the call after handler UOW is no longer in scope, so restore our UOW
                    browser.CurrentUOW = safeUOW;

                    // CAVEAT: elements may go stale because of callafterfillin handler actions
                    // test if elements still valid by testing the first one
                    //try
                    //{
                    //    ((UnitOfWork.ElementNode)(browser.CurrentUOW.ElementGrid[0])[0]).GPALElement.GetAttribute("tag");
                    //}
                    //catch   // failed, invalidate elementgrid
                    //{
                    //    browser.CurrentUOW.ElementGrid.Clear();
                    //}

                    if (CallIfStatus.Terminate == handled)
                    {
                        string str = $"CallAfterFillIn handler [{browser.CurrentUOW.AppCallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, browser, GPALObjectType.Browser);
                        throw new GPALException($"" + str);
                    }
                }

                void handleFillIn(ReadOnlyCollection<GPALElement> elems)
                {
                   int rowCount = 0;

                   foreach (GPALElement elem in elems)
                   {
                        string type = elem.GetAttribute("type");
                        string abbreviatedText = useText[0] + "..." + useText[useText.Length - 1];

                        if (null == type || false == type.Equals("password"))
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] text [{abbreviatedText}] in element [{elem.TagName}][{selector.Name}]", browser, GPALObjectType.Browser);
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] XXXXXX in element [{elem.TagName}][{selector.Name}]", browser, GPALObjectType.Browser);

                        if (null != elem)
                        {
                            retVal = true;

                            // Use the hardware mouse and keyboard when hardware is on for the browser or globally, or
                            // for an image match with no element whose selector asked for hardware. Never headless or
                            // on a hidden desktop.
                            if (false == browser.BrowserSettings.UseHeadless && false == browser.BrowserSettings.HiddenDesktop
                                && (("GPALElement" == elem.TagName && InteractionType.Hardware == selector.InteractionType)
                                    || true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))
                                HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                            else if ("GPALElement" == elem.TagName)
                                // image match with no element under it - focus the coordinate and type
                                FillInFrom(browser, ToViewportPoint(browser, elem, selector), useText, writeMode);
                            else if (browser.UseOttoMagic)
                            {
                                if (false == browser.BrowserSettings.UseHeadless && InteractionType.Hardware == selector.InteractionType)
                                    ElementHelper.HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                                else
                                    switch (writeMode)
                                    {
                                        case WriteMode.Append:
                                            browser.MagicHelper.FillInAppend(elem.Css, useText);
                                            break;
                                        case WriteMode.Insert:
                                            browser.MagicHelper.FillInInsert(elem.Css, useText);
                                            break;
                                        case WriteMode.Overwrite:
                                            browser.MagicHelper.FillInOverwrite(elem.Css, useText);
                                            break;
                                    }

                                // browser.MagicHelper.FireChangeEvent(elem.Css);

                                if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                                    break;
                            }
                            else if (true == browser.UsePuppeteer)
                            {
                                if (false == browser.BrowserSettings.UseHeadless && InteractionType.Hardware == selector.InteractionType)
                                    ElementHelper.HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                                else
                                    switch (writeMode)
                                    {
                                        case WriteMode.Append:
                                            browser.PuppeteerClient.FillInAppend(elem).WithText(useText).Execute();
                                            break;
                                        case WriteMode.Insert:
                                            browser.PuppeteerClient.FillInInsert(elem).WithText(useText).Execute();
                                            break;
                                        case WriteMode.Overwrite:
                                            browser.PuppeteerClient.FillInOverwrite(elem).WithText(useText).Execute();
                                            break;
                                    }

                                // browser.PuppeteerClient.FireChangeEvent(elem.ElementHandle).Execute();

                                if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                                    break;
                            }
                            else if (true == browser.UseSelenium)
                            {
                                if (false == browser.BrowserSettings.UseHeadless && InteractionType.Hardware == selector.InteractionType)
                                    ElementHelper.HardwareFillInFrom(browser, elem, selector.OffsetX, selector.OffsetY, useText, writeMode);
                                else
                                    SeleniumFillInFrom(browser, elem, useText, writeMode);
                            }
                        }
                    }
                }
            }

            return retVal;
        }
        /// <summary>
        /// Maps a virtual keycode to a Selenium Keys constant (or character) and sends it to <paramref name="element"/>
        /// via Selenium's SendKeys.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to send the key to</param>
        /// <param name="keyCode">Virtual keycode to send (e.g. GPAL.VK_RETURN)</param>
        public static void SeleniumSendKeys(Browser browser, GPALElement element, byte keyCode)
        {
                // Map the byte keycode to Selenium Keys constants
                string keyToSend;

                switch (keyCode)
                {
                    case 0x12: // VK_ALT
                        keyToSend = Keys.Alt;
                        break;
                    case 0x2e: // VK_DELETE
                        keyToSend = Keys.Delete;
                        break;
                    case 0x79: // VK_F10
                        keyToSend = Keys.F10;
                        break;
                    case 0x21: // VK_PRIOR
                        keyToSend = Keys.PageUp;
                        break;
                    case 0x22: // VK_NEXT
                        keyToSend = Keys.PageDown;
                        break;
                    case 0x23: // VK_END
                        keyToSend = Keys.End;
                        break;
                    case 0x24: // VK_HOME
                        keyToSend = Keys.Home;
                        break;
                    case 0x28: // VK_DOWN
                        keyToSend = Keys.ArrowDown;
                        break;
                    case 0x09: // VK_TAB
                        keyToSend = Keys.Tab;
                        break;
                    case 0x0D: // VK_RETURN
                        keyToSend = Keys.Return;
                        break;
                    case 0x27: // VK_SHIFT_RIGHT
                        keyToSend = Keys.Shift;
                        break;
                    case 0xA0: // VK_SHIFT_LEFT
                        keyToSend = Keys.LeftShift;
                        break;
                    case 0xA2: // VK_CONTROL_LEFT
                        keyToSend = Keys.LeftControl;
                        break;
                    case 0xA3: // VK_CONTROL_RIGHT
                        keyToSend = Keys.Control;
                        break;
                    case 0x20: // VK_SPACE
                        keyToSend = Keys.Space;
                        break;
                    case 0x08: // VK_BACK
                        keyToSend = Keys.Backspace;
                        break;
                    default:
                        // If it's a character key, use fromCharCode method for alphanumeric keys
                        keyToSend = ((char)keyCode).ToString();
                        break;
                }

                // Find the element to send keys to
                element.SendKeys(keyToSend);
        }

        // Input types shown as a picker widget. Typing into them depends on segment order, separators and tab
        // behavior, which vary by browser and locale. All accept an ISO value in .value, so Selenium sets .value, as
        // OttoMagic and Puppeteer do.
        private static readonly HashSet<string> valueOnlyInputTypes = new HashSet<string>
        {
            "color", "date", "datetime-local", "month", "week", "time",
        };
        /// <summary>
        /// Fills in <paramref name="elem"/> using Selenium SendKeys, handling Overwrite/Append/Insert write modes
        /// and special-cased month/week/datetime-local input types. Falls back to <see cref="JavaScriptFillInFrom"/>
        /// if Selenium throws and fallback recovery is enabled.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="elem">Element to fill in</param>
        /// <param name="textToUse">Text to enter</param>
        /// <param name="writeMode">Overwrite, Append or Insert</param>
        public static void SeleniumFillInFrom(Browser browser, GPALElement elem, string textToUse, WriteMode writeMode)
        {
            // the ISO value goes to .value untouched - GetKeystrokeValueForInput exists to reshape it for typing
            if (true == valueOnlyInputTypes.Contains(elem.Type))
            {
                JavaScriptFillInFrom(browser, elem, textToUse, writeMode);
                return;
            }

            textToUse = ElementHelper.GetKeystrokeValueForInput(elem, textToUse);

            try
            {
                if (WriteMode.Overwrite == writeMode)
                    elem.Clear();
                else if (WriteMode.Append == writeMode)
                {
                    textToUse = elem.Text + textToUse;
                    elem.Clear();
                }

                elem.SendKeys(textToUse);   // fast path - whole string at once
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (false == GPAL.NoFallbackRecoveryActions)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Unable to fill in text. Falling back on javascript.", browser, GPALObjectType.Browser, ex);
                    JavaScriptFillInFrom(browser, elem, textToUse, writeMode);
                }
            }

        }


        /// <summary>
        /// Fill in text on an element using hardware emulation. The element is clicked on first to bring it in focus using offset x and y.
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="webElement"></param>
        /// <param name="offsetX"></param>
        /// <param name="offsetY"></param>
        /// <param name="text"></param>
        /// <param name="writeMode"></param>
        public static void HardwareFillInFrom(Browser browser, GPALElement webElement, int offsetX, int offsetY, string text, WriteMode writeMode)
        {
            string inputType = webElement.Type?.ToLowerInvariant() ?? "";

            // Special handling for native picker inputs
            bool isSpecialInput = new[] { "date", "time", "datetime-local", "month", "week", "color" }
                                  .Contains(inputType);

            HardwareFocus(browser, webElement, offsetX, offsetY);

            // If focusing the element could not be trusted (off-screen/odd coords, so no real hardware click
            // happened), do NOT type. Focus is likely still on whatever it was before - e.g. the browser's
            // address bar - and blasting keystrokes there is how a search term ends up in the URL bar.
            if (true == _lastPlacementQuestionable)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Skipping fill-in [{browser.CurrentUOW?.CurrentSelector?.Name}], questionable element placement.",
                    browser, GPALObjectType.Browser);
                return;
            }

            if (isSpecialInput)
            {
                // No Ctrl+A on Firefox: in a date or time widget it selects the whole page. Firefox overwrites
                // segments as typed anyway.
                if ("week" != inputType && BrowserType.FireFox != browser.BrowserSettings.BrowserType)
                {
                    // Aggressive clear for special inputs (they can be picky)
                    HardwareHelper.SendChar('a', ModifierKeys.Control, true);
                    Thread.Sleep(120);
                    HardwareHelper.SendKey(GPAL.VK_DELETE);
                    Thread.Sleep(80);
                }
            }
            else if (WriteMode.Overwrite == writeMode)
            {
                HardwareHelper.SendChar('a', ModifierKeys.Control, true);
                Thread.Sleep(150);
                //HardwareHelper.SendKey(GPAL.VK_DELETE);
            }
            else if (WriteMode.Append == writeMode)
            {
                HardwareHelper.SendKey(GPAL.VK_END, ModifierKeys.Control);
                Thread.Sleep(150);
            }
            else if (WriteMode.Insert == writeMode)
            {
                HardwareHelper.SendKey(GPAL.VK_HOME, ModifierKeys.Control);
                Thread.Sleep(150);
            }

            string textToType = GetKeystrokeValueForInput(webElement, text);
            switch (inputType)
            {
                case "color":
                    // the picker takes the six hex digits, not the css value, and anything else falls back to black
                    HardwareFillInColor(textToType.TrimStart('#'), browser.BrowserSettings.BrowserType);
                    break;
                case "datetime-local":
                    HardwareFillInDateTimeLocal(textToType, browser.BrowserSettings.BrowserType);
                    break;
                case "week":
                    HardwareFillInWeek(textToType);
                    break;
                case "month":
                    HardwareFillInMonth(textToType);
                    break;
                default:
                    HardwareHelper.SendString(textToType);
                    break;
            }
        }

        /// <summary>
        /// Fill in the week in the format expected by the control
        /// </summary>
        /// <param name="isoValue"></param>
        public static void HardwareFillInMonth(string isoValue)
        {
            var parts = isoValue.Split('/');
            string year = parts[0];
            string month = parts[1];
            HardwareHelper.SendString(month);   // "12"
            // TAB to move to year field
            HardwareHelper.SendKey(GPAL.VK_TAB);
            HardwareHelper.SendString(year);   // "2026"
        }
        
        /// <summary>
        /// Fill in the week in the format expected by the control
        /// </summary>
        /// <param name="isoValue"></param>
        public static void HardwareFillInWeek(string isoValue)
        {
            if (isoValue.Contains("-W"))
            {
                var parts = isoValue.Split('-');
                string year = parts[0];
                string week = parts[1].TrimStart('W');
                HardwareHelper.SendString($"{week}{year}");   // "31 2026"
            }
        }

        /// <summary>
        /// Type in the datetime in the format expected by the control
        /// </summary>
        /// <param name="isoValue"></param>
        /// <param name="browserType">Browser type; Firefox skips the tab between the date and the time</param>
        public static void HardwareFillInDateTimeLocal(string isoValue, BrowserType browserType)
        {
            // Convert ISO "2026-06-18T18:32" → US human format "06/18/2026 06:32 PM"
            if (!DateTime.TryParseExact(isoValue,
                                        "MM/dd/yyyy HH:mm",
                                        null,
                                        System.Globalization.DateTimeStyles.None,
                                        out DateTime dt))
            {
                dt = DateTime.Now; // fallback
            }

            string datePart = dt.ToString("MM/dd/yyyy");           // US date
            string timePart = dt.ToString("hh:mm");   // 06:32 pm  (lowercase a/p)
            string amPm = dt.ToString("tt");

            // Type date
            HardwareHelper.SendString(datePart);

            // firefox advances to the hour on its own once the year is typed, so tabbing here moves off the hour
            // and the time lands a segment late. the chromium widget still wants the tab
            if (BrowserType.FireFox != browserType)
            {
                // TAB to move to time field
                HardwareHelper.SendKey(GPAL.VK_TAB);
                Thread.Sleep(120);
            }

            // Type time in 12-hour format
            HardwareHelper.SendString(timePart);
            Thread.Sleep(120);

            // am/pm
            HardwareHelper.SendString(amPm);
        }

        /// <summary>
        /// CLicking on the color picker shows a popup with a slide and boxes to enter RGB, 3 tabs to get to R, then one tab each
        /// </summary>
        /// <param name="hexValue"></param>
        /// <param name="browserType">Browser type, which sets how many tabs reach the first RGB field (7 on Firefox, 3 otherwise)</param>
        public static void HardwareFillInColor(string hexValue, BrowserType browserType)
        {
            if (hexValue.Length != 6)
            {
                // fallback or error
                hexValue = "000000";
            }

            // Convert #RRGGBB → R, G, B decimals
            int r = Convert.ToInt32(hexValue.Substring(0, 2), 16);
            int g = Convert.ToInt32(hexValue.Substring(2, 2), 16);
            int b = Convert.ToInt32(hexValue.Substring(4, 2), 16);

            // Navigate into the RGB fields. Three tabs reaches the first one in the chromium picker. Firefox lists
            // hue, saturation and luminance ahead of red, green and blue, so it takes four more to clear them
            int tabsToFirstField = BrowserType.FireFox == browserType ? 7 : 3;

            for (int tab = 0; tab < tabsToFirstField; tab++)
                HardwareHelper.SendKey(GPAL.VK_TAB);

            // Enter R
            HardwareHelper.SendString(r.ToString());
            HardwareHelper.SendKey(GPAL.VK_TAB);

            // Enter G
            HardwareHelper.SendString(g.ToString());
            HardwareHelper.SendKey(GPAL.VK_TAB);

            // Enter B
            HardwareHelper.SendString(b.ToString());

            // Optional: Tab out + Enter to confirm / close picker
            //HardwareHelper.SendKey(GPAL.VK_TAB);
            HardwareHelper.SendKey(GPAL.VK_RETURN);   // or VK_ENTER
        }
        /// <summary>
        /// get a human typeable us local string to type into the special controls. color requires tabbing to get to the component values
        /// </summary>
        /// <param name="webElement"></param>
        /// <param name="isoValue"></param>
        /// <returns></returns>
        public static string GetKeystrokeValueForInput(GPALElement webElement, string isoValue)
        {
            if (string.IsNullOrEmpty(isoValue) || webElement?.TagName?.ToLowerInvariant() != "input")
                return isoValue;

            string type = webElement.Type?.ToLowerInvariant() ?? "";

            try
            {
                switch (type)
                {
                    case "date":
                        // ISO: 2026-06-18 US typing: 06/18/2026
                        if (DateTime.TryParseExact(isoValue, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                            return dt.ToString("MM/dd/yyyy");
                        return isoValue;

                    case "datetime-local":
                        // ISO: 2026-06-18T18:32 US: 06/18/2026 18:32
                        if (DateTime.TryParseExact(isoValue.Replace("T", " "), "yyyy-MM-dd HH:mm", null, System.Globalization.DateTimeStyles.None, out dt))
                            return dt.ToString("MM/dd/yyyy HH:mm");
                        return isoValue.Replace("T", " ");

                    case "month":
                        // ISO: 2026-12 12/2026  (most browsers accept this when typing)
                        return isoValue.Replace("-", "/");

                    case "week":
                        // 2026-W31 keep as-is or try "31 2026" — Wxx format is usually required
                        // Many browsers expect the ISO week format even when typing
                        return isoValue;   // safest starting point

                    case "time":
                        // The value is 24 hour HH:mm; the widget takes 12 hour plus AM/PM, so convert to "hh:mm tt".
                        if (DateTime.TryParseExact(isoValue, new[] { "HH:mm", "H:mm", "HH:mm:ss" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
                            return dt.ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);
                        return isoValue;

                    default:
                        return isoValue;
                }
            }
            catch
            {
                return isoValue;  // fallback
            }
        }

        /// <summary>
        /// Fill in text on an element (by setting value) using javascript inserted into the browser
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to fill in</param>
        /// <param name="text">Text to write</param>
        /// <param name="writeMode">Whether to overwrite, append to or insert before the current value</param>
        public static void JavaScriptFillInFrom(Browser browser, GPALElement webElement, string text, WriteMode writeMode)
        {
            IWebDriver browserDriver = browser.BrowserDriver;

            BrowserHelper.ExecuteJavaScript(browser, "arguments[0].focus();", webElement.WebElement);

            // Set .value, not the value attribute. The attribute is only the default; once the field has been typed
            // in, the browser submits the property.
            if (WriteMode.Overwrite == writeMode)
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1]", webElement.WebElement, text);
            else if (WriteMode.Append == writeMode)
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[0].value + arguments[1]", webElement.WebElement, text);
            else if (WriteMode.Insert == writeMode)
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1] + arguments[0].value", webElement.WebElement, text);

            // Fire input and change as typing does, so pages that track the value themselves (React and similar)
            // see it.
            // todo: make firing this an optional param
            BrowserHelper.ExecuteJavaScript(browser,
                "arguments[0].dispatchEvent(new Event('input', { bubbles: true })); arguments[0].dispatchEvent(new Event('change', { bubbles: true }));",
                webElement.WebElement);
        }
        /// <summary>
        /// Fill in text on an element (by setting value) using javascript inserted into the browser
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to fill in</param>
        /// <param name="text">Text to write</param>
        /// <param name="writeMode">Whether to overwrite, append to or insert before the current value</param>
        public static async void PuppeteerFillInFrom(Browser browser, GPALElement webElement, string text, WriteMode writeMode)
        {
            BrowserSettings browserSettings = browser.BrowserSettings;

            PuppeteerCommunicator puppeteerCommunicator = browserSettings.PuppeteerCommunicator;

            // https://stackoverflow.com/questions/11337353/correct-way-to-focus-an-element-in-selenium-webdriver-using-java
            // puppeteerCommunicator.Focus(webElement.Css);
            switch (writeMode)
            {
                case WriteMode.Append:
                    browser.PuppeteerClient.FillInAppend(webElement).WithText(text).Execute();
                    break;
                case WriteMode.Insert:
                    browser.PuppeteerClient.FillInInsert(webElement).WithText(text).Execute();
                    break;
                case WriteMode.Overwrite:
                    browser.PuppeteerClient.FillInOverwrite(webElement).WithText(text).Execute();
                    break;
            }
            VkCodeToDomKeyConverter.TryConvertAsciiToVkCode('\t', out byte vkCode);
            VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(vkCode, out string key, out string code);
            await puppeteerCommunicator.SendKey(key, code, vkCode);   // tab out, force change event to fire - todo: make firing this an optional param
        }

        /// <summary>
        /// Sends a single key press to the currently focused element using injected JavaScript, dispatching
        /// keydown/keypress/input/keyup events and inserting the character into editable elements directly.
        /// </summary>
        /// <param name="browser">Browser to send the key in</param>
        /// <param name="webElement">Unused; the script acts on document.activeElement</param>
        /// <param name="keycode">Virtual keycode to send (e.g. GPAL.VK_RETURN)</param>
        public static void JavaScriptSendKey(Browser browser, GPALElement webElement, byte keycode)
        {
            IWebDriver browserDriver = browser.BrowserDriver;
            if (true == VkCodeToDomKeyConverter.TryConvertVkCodeToDomKey(keycode, out string key, out string code))
            {
                string sendKeyScript = @"
                    function isElementEditable(target) {
                        if (!target || !(target instanceof Element)) return false;
                        if (target.disabled || target.readOnly) return false;

                        const tag = target.tagName.toUpperCase();
                        if (tag === 'TEXTAREA') return true;
                        if (tag === 'SELECT') return true;
                        if (tag === 'INPUT') {
                            const editableTypes = new Set(['text','email','password','search','number','url','tel',
                                                         'date','datetime-local','month','time','week','color']);
                            return editableTypes.has(target.type.toLowerCase());
                        }
                        if (target.isContentEditable === true || 
                            target.getAttribute('contenteditable') === 'true' ||
                            target.ownerDocument?.designMode === 'on') {
                            return true;
                        }
                        const style = window.getComputedStyle(target);
                        if (style.userModify === 'read-write' || style.webkitUserModify === 'read-write' || 
                            style.cursor === 'text') {
                            return true;
                        }
                        return false;
                    }

                    // === Parameters passed from C# ===
                    const key = arguments[0];           // e.g. 'a', 'Enter', 'Tab'
                    const code = arguments[1];          // e.g. 'KeyA', 'Enter'
                    const keyCode = arguments[2];       // numeric keyCode if needed (mostly legacy)

                    let target = document.activeElement;
                    if (!target || target === document.body) {
                        target = document;  // fallback
                    }

                    const eventInit = {
                        key: key,
                        code: code,
                        keyCode: keyCode,
                        bubbles: true,
                        cancelable: true,
                        composed: true
                    };

                    try {
                        // 1. Key events (for listeners)
                        target.dispatchEvent(new KeyboardEvent('keydown', eventInit));

                        if (key.length === 1) {  // printable char
                            target.dispatchEvent(new KeyboardEvent('keypress', eventInit));
                        }

                        // 2. Actually insert text (this is what most sites need)
                        const isEditable = isElementEditable(target);
                        if (isEditable) {
                            if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') {
                                const start = target.selectionStart ?? target.value.length;
                                const end = target.selectionEnd ?? target.value.length;
                                target.value = target.value.slice(0, start) + key + target.value.slice(end);
                                target.selectionStart = target.selectionEnd = start + key.length;
                            } 
                            else if (target.isContentEditable) {
                                // Simpler fallback for contenteditable
                                document.execCommand('insertText', false, key);  // still works in many places
                            }

                            // Fire input event (React, Vue, Angular, etc. listen to this)
                            target.dispatchEvent(new InputEvent('input', {
                                bubbles: true,
                                cancelable: true,
                                data: key,
                                inputType: 'insertText',
                                composed: true
                            }));

                            // Also fire change for some frameworks
                            target.dispatchEvent(new Event('change', { bubbles: true }));
                        }

                        // 3. Keyup
                        target.dispatchEvent(new KeyboardEvent('keyup', eventInit));

                        // Special case: Enter on button/submit
                        if (key === 'Enter' && (target.tagName === 'BUTTON' || 
                            (target.tagName === 'INPUT' && (target.type === 'submit' || target.type === 'button')))) {
                            target.click();
                        }

                    } catch (e) {
                        console.error('SendKey script error:', e);
                    }
                ";

                // Call it like this:
                BrowserHelper.ExecuteJavaScript(browser, sendKeyScript, key, code, keycode.ToString());  // example for 'A'
            }
        }
        /// <summary>
        /// Brings each element matched by the current unit of work's selectors into focus, consistently across
        /// engines: editable elements (text inputs, textareas, contenteditable - see
        /// <see cref="IsElementEditable"/>) are left-clicked, which both focuses them and places the caret,
        /// readying them for typing. Non-editable elements are focused without clicking, to avoid side effects
        /// (navigation, submit, toggle, etc.) from activating them.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void Focus(Browser browser)
        {
            List<GPALElement> matchedElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == matchedElements)
                {
                    PublishToEventHandler(browser, selector, matchedElements?.ToList());
                    continue;
                }

                int rowCount = 0;

                foreach (GPALElement elem in matchedElements)
                {
                    if (null != elem)
                    {
                        if (false == browser.BrowserSettings.UseHeadless && (true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))
                        {
                            HardwareFocus(browser, elem, selector.OffsetX, selector.OffsetY);
                        }
                        else if (true == IsElementEditable(elem))
                        {
                            // click to focus and place the caret, readying the element for typing
                            Click(browser, selector, elem, ClickType.LeftClick, ModifierKeys.NONE);
                        }
                        else if (true == browser.UseOttoMagic)
                        {
                            browser.MagicHelper.Focus(elem.Css);
                        }
                        else if (true == browser.UsePuppeteer)
                        {
                            browser.PuppeteerClient.Focus(elem.Css).Execute();
                        }
                        else if (true == browser.UseSelenium)
                        {
                            if (Enums.InteractionType.JavaScript == selector.InteractionType || true == browser.BrowserSettings.UseJavaScript) // selenium
                            {
                                JavaScriptFocus(browser, elem);
                            }
                            else if (elem.TagName.ToLower().Equals("input"))
                            {
                                elem.SendKeys(OpenQA.Selenium.Keys.Shift);
                                elem.SendKeys("");
                            }
                            else
                            {
                                try
                                {
                                    new Actions(browser.BrowserDriver).MoveToElement((IWebElement)elem.WebElement).Perform();
                                }
                                catch (GPALException)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    // With recovery on, fall back to JavaScript scrolling and publish a WARNING.
                                    // With recovery off, publish the Selenium exception as an EXCEPTION.
                                    if (false == GPAL.NoFallbackRecoveryActions)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium threw on selector [{selector.Name}], falling back to javascript.", browser, GPALObjectType.Browser);
                                        ElementHelper.ScrollIntoView(browser, elem);
                                    }
                                    else
                                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Selenium threw on selector [{selector.Name}], and recovery actions are off, so nothing was done about it.", browser, GPALObjectType.Browser, ex);
                                }
                            }
                        }

                        if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Forces focus onto <paramref name="webElement"/> using injected JavaScript, temporarily enabling it
        /// and giving it a tabindex if needed so it can receive focus.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to focus</param>
        private static void JavaScriptFocus(Browser browser, GPALElement webElement)
        {
            IWebDriver browserDriver = browser.BrowserDriver;

            string forceFocusScript = $@"
                function forceFocus(el) {{
                    // Remember original state
                    const wasDisabled = el.disabled;
                    const originalTabIndex = el.getAttribute('tabindex');

                    try {{
                        // 1. Enable if disabled
                        if (wasDisabled) el.disabled = false;

                        // 2. Make sure it can receive focus
                        if (el.tabIndex === -1 || !el.hasAttribute('tabindex')) {{
                            el.setAttribute('tabindex', '0');
                        }}

                        // 3. Focus it
                        el.focus({{ preventScroll: true }});

                        // 4. Small delay + aggressive fallback (helps with buttons, React, Shadow DOM, etc.)
                        setTimeout(() => {{
                            if (document.activeElement !== el) {{
                                el.focus();

                                // Dispatch synthetic focus events
                                el.dispatchEvent(new FocusEvent('focusin', {{ bubbles: true }}));
                                el.dispatchEvent(new FocusEvent('focus', {{ bubbles: false }}));
                            }}
                        }}, 0);

                        // Final check
                        return document.activeElement === el;

                    }} catch (err) {{
                        return false;
                    }} finally {{
                        // Restore original state
                        if (wasDisabled) el.disabled = true;
                        if (originalTabIndex !== null) {{
                            el.setAttribute('tabindex', originalTabIndex);
                        }} else if (el.getAttribute('tabindex') === '0') {{
                            el.removeAttribute('tabindex');
                        }}
                    }}
                }}
                forceFocus(arguments[0]);
            ";
            BrowserHelper.ExecuteJavaScript(browser, forceFocusScript, webElement.WebElement); // focus on element
        }
        /// <summary>
        /// Hides every element matched by the current unit of work's selectors by setting their CSS display to none.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void Hide(Browser browser)
        {
            List<GPALElement> matchedElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                foreach (GPALElement elem in elems)
                    Hide(browser, elem);
            }
        }
        /// <summary>
        /// Hides a single element by setting its CSS display to none, using OttoMagic, Puppeteer or JavaScript
        /// depending on the active engine.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to hide</param>
        public static void Hide(Browser browser, GPALElement element)
        {
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.HideElement(element.Css);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.HideElement(element.ElementBackendNodeId.ToString());
            else
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].style.display = 'none';", element.WebElement);

        }
        /// <summary>
        /// Directly sets a DOM attribute on every element matched by the current unit of work's selectors.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="attribute">Name of the attribute to set</param>
        /// <param name="value">Value to assign to the attribute</param>
        public static void SetAttribute(Browser browser, string attribute, string value)
        {
            List<GPALElement> matchedElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                foreach (GPALElement elem in elems)
                    SetAttribute(browser, elem, attribute, value);
            }
        }
        /// <summary>
        /// Directly sets a DOM attribute on a single element, using OttoMagic, Puppeteer or JavaScript depending
        /// on the active engine. Hardware automation has no equivalent operation, so it falls through to the
        /// JavaScript branch (whichever of Selenium/Puppeteer is configured underneath).
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to set the attribute on</param>
        /// <param name="attribute">Name of the attribute to set</param>
        /// <param name="value">Value to assign to the attribute</param>
        public static void SetAttribute(Browser browser, GPALElement element, string attribute, string value)
        {
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.SetAttribute(element.Css, attribute, value);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.SetAttribute(element.ElementBackendNodeId.ToString()).WithAttribute(attribute).WithValue(value).Execute();
            else
                BrowserHelper.ExecuteJavaScript(browser, "arguments[0].setAttribute(arguments[1], arguments[2]);", element.WebElement, attribute, value);
        }
        /// <summary>
        /// Sets the value of every element matched by the current unit of work's selectors from the value of
        /// the (first) element matched by <paramref name="srcSelector"/>.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="srcSelector">Selector identifying the source element to read the value from</param>
        public static void SetValueFromElement(Browser browser, Selector srcSelector)
        {
            List<GPALElement> matchedElements;

            ReadOnlyCollection<GPALElement> srcElems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, srcSelector, out bool srcMatchedAll, out List<GPALElement> srcMatchedElements);

            if (null != srcMatchedElements)
                srcElems = new ReadOnlyCollection<GPALElement>(srcMatchedElements);

            if (null == srcElems || 0 == srcElems.Count)
                return;

            GPALElement srcElem = srcElems[0];

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                foreach (GPALElement elem in elems)
                    SetValueFromElement(browser, elem, srcElem);
            }
        }
        /// <summary>
        /// Sets the value of a single destination element from a single source element, using OttoMagic,
        /// Puppeteer or JavaScript depending on the active engine. Fires "input" and "change" events on the
        /// destination element so the page reacts to the new value.
        /// </summary>
        /// <param name="browser">Browser the elements belong to</param>
        /// <param name="element">Destination element whose value is set</param>
        /// <param name="srcElement">Source element whose value is read</param>
        public static void SetValueFromElement(Browser browser, GPALElement element, GPALElement srcElement)
        {
            if (true == browser.UseOttoMagic)
                browser.MagicHelper.SetValueFromElement(srcElement.Css, element.Css);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.WithElementId(srcElement.Css).SetValueFrom(element.Css).Execute();
            else
                BrowserHelper.ExecuteJavaScript(browser,
                    "arguments[0].value = arguments[1].value; arguments[0].dispatchEvent(new Event('input', {bubbles: true})); arguments[0].dispatchEvent(new Event('change', {bubbles: true}));",
                    element.WebElement, srcElement.WebElement);
        }
        /// <summary>
        /// Moves the mouse to each element matched by the current unit of work's selectors, using hardware
        /// mouse movement, or OttoMagic/Puppeteer/Selenium native mouse moves. Each element is scrolled into view
        /// first. These are all real input-layer
        /// events, so the browser naturally updates CSS <c>:hover</c> state and fires
        /// mouseenter/mouseover/mouseleave - this is what reveals hover-triggered menus and tooltips, so there is
        /// no separate hover operation (see <see cref="Browser.Hover"/>, a fluent alias for this method).<br/>
        /// As a last resort, when no real cursor can be moved (e.g. plain Selenium in headless mode, or
        /// <see cref="Enums.InteractionType.JavaScript"/>), a synthetic "mouseover" event is dispatched via
        /// <see cref="JavaScriptHover"/> - this only fires JS mouseover listeners and does NOT trigger CSS
        /// <c>:hover</c>.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void MoveTo(Browser browser)
        {
            List<GPALElement> matchedElements;
            UnitOfWork currentUOW = browser.CurrentUOW;

            foreach (Selector selector in currentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements(browser, currentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler(browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                int rowCount = 0;
                int total = Math.Min(elems.Count, browser.CurrentUOW.WithAllThatMatch);
                object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

                foreach (GPALElement found in elems)
                {
                    if (null == found)
                        continue;

                    // Element from an image match: hover at the matched point, as a click does. OttoMagic cannot move
                    // to a point, so it hovers the element.
                    GPALElement elem = null != found.ImageMatch && false == browser.UseOttoMagic ? found.ImageMatch : found;

                    ScrollIntoView(browser, elem);

                    if (0 == elem.Size.Width && 0 == elem.Size.Height)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Element has Zero Width and Height, skipping.", browser, GPALObjectType.Browser);
                        continue;
                    }

                    // TagName "GPALElement" here is an image match with no element, only a point. Use the hardware
                    // mouse (not headless) if hardware is on for the browser, globally or on the selector
                    // (.WithHardware, as DragAndDrop does), or for such a match on OttoMagic, which cannot move to a
                    // point.
                    if (false == browser.BrowserSettings.UseHeadless
                        && (("GPALElement" == elem.TagName && true == browser.UseOttoMagic)
                            || true == browser.BrowserSettings.UseHardware
                            || true == GPAL.GPALSettings.UseHardware
                            || InteractionType.Hardware == selector.InteractionType))
                    {
                        HardwareMoveTo(browser, elem, selector);
                    }
                    else if ("GPALElement" == elem.TagName)
                    {
                        // Image match with no element and no hardware mouse: move the Puppeteer or Selenium mouse to
                        // the point.
                        MoveTo(browser, elem, selector);
                    }
                    else if (true == browser.UseOttoMagic || true == browser.UsePuppeteer)
                    {
                        MoveTo(elem, selector.OffsetX, selector.OffsetY, browser);
                    }
                    else if (Enums.InteractionType.JavaScript == selector.InteractionType || true == browser.BrowserSettings.UseJavaScript || true == browser.BrowserSettings.UseHeadless)
                    {
                        if (null != currentUOW.ShadowRoot)
                            JavaScriptHover(browser, currentUOW.ShadowRoot);
                        else
                            JavaScriptHover(browser, elem);
                    }
                    else // selenium with a real cursor
                    {
                        MoveTo(elem, selector.OffsetX, selector.OffsetY, browser);
                    }

                    if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                        break;
                }
            }
        }

        /// <summary>
        /// Checks, after each element in a found list, whether a click or hover loaded a new page. If so the remaining
        /// elements belong to the old page, so the loop stops and a warning is published.
        /// </summary>
        /// <param name="browser">Browser the list was found in</param>
        /// <param name="pageMark">Page held before the first element, from BrowserHelper.MarkDocument, null for a list of one</param>
        /// <param name="done">How many elements have been acted on</param>
        /// <param name="total">How many elements the loop means to act on</param>
        /// <returns>true when the page was replaced and elements remain</returns>
        private static bool PageReplaced(Browser browser, object pageMark, int done, int total)
        {
            bool replaced = null != pageMark && done < total && true == BrowserHelper.DocumentReplaced(browser, pageMark);

            if (true == replaced)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The page changed after [{done}] of [{total}] element(s), the rest were left alone", browser, GPALObjectType.Browser);

            return replaced;
        }

        /// <summary>
        /// Moves the Puppeteer or Selenium mouse to the point of an image match with no element, when there is no
        /// hardware mouse (in practice headless). Both send real input, so CSS :hover updates, unlike a JavaScript
        /// mouseover. OttoMagic cannot move to a point and only warns.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Image-matched surrogate to move to</param>
        /// <param name="selector">Selector providing the grab offset</param>
        private static void MoveTo(Browser browser, GPALElement element, Selector selector)
        {
            Point point = ToViewportPoint(browser, element, selector);

            if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.MoveTo(point).Execute();
            else if (true == browser.UseSelenium)
                new Actions(browser.BrowserDriver).MoveToLocation(point.X, point.Y).Perform();
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot hover the image match for selector [{selector.Name}] - OttoMagic has no coordinate mouse move and there is no hardware mouse available.", browser, GPALObjectType.Browser);
        }
        // per-thread so concurrent browsers do not share a generator, and seeded from a Guid because the
        // parameterless Random seeds off a low resolution clock - constructed back to back it repeats itself
        [ThreadStatic] private static Random _jitter;
        private static Random Jitter => _jitter ?? (_jitter = new Random(Guid.NewGuid().GetHashCode()));
        /// <summary>
        /// Offset from the top left of an image match to aim at when no offset is given: the center, moved up to 2
        /// pixels at random. Not used with .WithOffsetX/.WithOffsetY.
        /// </summary>
        /// <param name="size">Size of the matched image</param>
        /// <returns>Offset from the top left of the match</returns>
        private static Point ImageCenterOffset(Size size)
        {
            // never jitter so far that a tiny match is missed
            int jitterX = Math.Max(0, Math.Min(2, (size.Width / 2) - 1));
            int jitterY = Math.Max(0, Math.Min(2, (size.Height / 2) - 1));

            return new Point((size.Width / 2) + Jitter.Next(-jitterX, jitterX + 1),
                             (size.Height / 2) + Jitter.Next(-jitterY, jitterY + 1));
        }
        /// <summary>
        /// The css path of the first element an XPath selector matches, for an action that names its element by css,
        /// such as printing to PDF. Selenium and Puppeteer evaluate the XPath in the page and build the path there;
        /// OttoMagic finds the element and hands back the css path it carries.
        /// </summary>
        /// <param name="browser">Browser to look in</param>
        /// <param name="selector">The XPath selector</param>
        /// <returns>The css path, or null when nothing matches</returns>
        internal static string CssForXPath(Browser browser, Selector selector)
        {
            string retVal = null;
            string xpath = selector.SelectorPaths[0].SelectorPath;

            if (true == browser.UseOttoMagic)
            {
                ReadOnlyCollection<GPALElement> found = FindWebElementsByXPath(browser, selector.SelectorPaths[0], selector);

                retVal = 0 < (found?.Count ?? 0) ? found[0].Css : null;
            }
            else
            {
                // the XPath goes in as a JSON string, so its quotes cannot end the script's own
                string css = BrowserHelper.ExecuteJavaScript(browser, $@"
                    return (function (xpath) {{
                        var el = document.evaluate(xpath, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                        var path = [];

                        while (el && el.nodeType === Node.ELEMENT_NODE) {{
                            if (el.id) {{
                                path.unshift('#' + CSS.escape(el.id));
                                break;
                            }}

                            var part = el.nodeName.toLowerCase(), sibling = el, nth = 1;

                            while ((sibling = sibling.previousElementSibling) != null)
                                if (sibling.nodeName === el.nodeName)
                                    nth++;

                            path.unshift(part + ':nth-of-type(' + nth + ')');
                            el = el.parentElement;
                        }}

                        return path.join(' > ');
                    }})({Newtonsoft.Json.JsonConvert.SerializeObject(xpath)});");

                retVal = false == string.IsNullOrEmpty(css) ? css : null;
            }

            if (null == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"XPath selector [{selector.Name}] matched nothing on the page, so it was left out", browser, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// Css path of the element under an image selector's match, for actions that need css, such as printing to
        /// PDF. Uses <see cref="ElementUnderImage"/>. OttoMagic needs Allow user scripts on; without it this returns
        /// null and the image is skipped with a warning.
        /// </summary>
        /// <param name="browser">Browser to look in</param>
        /// <param name="selector">The image selector</param>
        /// <returns>The css path, or null when the picture is not there, has no element of its own, or cannot be asked</returns>
        internal static string CssUnderImage(Browser browser, Selector selector)
        {
            string retVal = null;
            ReadOnlyCollection<GPALElement> found = new ReadOnlyCollection<GPALElement>(selector.WebSelectorFoundResults);
            GPALElement first = 0 < (found?.Count ?? 0) ? found[0] : null;

            // headless and hidden desktop runs have already resolved the match to its element
            if (null != first && "GPALElement" == first.TagName)
            {
                ReadOnlyCollection<GPALElement> under = ElementUnderImage(browser, first, selector);

                first = 0 < (under?.Count ?? 0) ? under[0] : null;
            }

            if (false == string.IsNullOrEmpty(first?.Css))
                retVal = first.Css;
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Image selector [{selector.Name}] found no element of its own on the page, so it was left out", browser, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// The url for an image match: the nearest enclosing link's href, else the image's src. OttoMagic needs Allow
        /// user scripts on; without it this returns null and <see cref="RunPageScript"/> warns.
        /// </summary>
        /// <param name="match">The image match</param>
        /// <returns>The url, or null when there is none or it cannot be asked</returns>
        internal static string UrlUnderImage(GPALElement match)
        {
            string retVal = null;
            Browser browser = match.Browser;

            if (null == browser)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The url behind image match [{match.Selector?.Name}] cannot be read, since the match carries no browser to ask", null, GPALObjectType.None);
            else
            {
                Point aim = ToViewportPoint(browser, match, match.Selector);

                // the numbers go into the script rather than as arguments, since the engines pass arguments differently
                string url = RunPageScript(browser, $@"
                    (function (x, y) {{
                        var el = document.elementFromPoint(x, y);

                        if (!el)
                            return '';

                        var link = el.closest('a[href], area[href]');

                        return link ? link.href : (el.currentSrc || el.src || el.data || '');
                    }})({aim.X}, {aim.Y})");

                if (false == string.IsNullOrEmpty(url))
                    retVal = url;
            }

            return retVal;
        }

        /// <summary>
        /// Runs a JavaScript expression in the page and returns its value, or null if the engine could not run it.<br/>
        /// Selenium needs "return " in front; OttoMagic and Puppeteer take the expression as is, saving a second call.
        /// When OttoMagic cannot run scripts (usually Allow user scripts is off) it answers {"error": ...}; this
        /// returns null and warns once per run.
        /// </summary>
        /// <param name="browser">Browser to run the expression in</param>
        /// <param name="expression">A single javascript expression</param>
        /// <returns>What the expression produced, or null</returns>
        internal static string RunPageScript(Browser browser, string expression)
        {
            // trimmed, because a return followed by a line break returns nothing
            string retVal = BrowserHelper.ExecuteJavaScript(browser, true == browser.UseSelenium ? "return " + expression.Trim() : expression);

            if (true == browser.UseOttoMagic && true == retVal?.TrimStart().StartsWith("{\"error\""))
            {
                if (false == _userScriptsWarned)
                {
                    _userScriptsWarned = true;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"OttoMagic could not run a page script [{retVal}]. Turn on user scripts with the Turn On User Scripts button in OttoMagic's settings so GPAL can run its page scripts, such as using image matches as elements.", browser, GPALObjectType.Browser);
                }

                retVal = null;
            }
            else if ("fail" == retVal)
                retVal = null;

            return retVal;
        }

        // Set once RunPageScript has warned that OttoMagic cannot run scripts, so it warns once per run.
        private static bool _userScriptsWarned;

        /// <summary>
        /// The element under an image match. Calls elementFromPoint at the click point and walks up to the nearest
        /// actionable element: link, button, form field, label, clickable role, onclick or contenteditable. The point
        /// usually lands on an svg, path or span with no text, value or href. If nothing up the tree is actionable,
        /// the element at the point is used.<br/>
        /// No size limit: clicks, hovers and drags still go to the matched point in
        /// <see cref="GPALElement.ImageMatch"/>.<br/>
        /// Returns null for an iframe or shadow host, which a top document css selector cannot reach into.
        /// </summary>
        /// <param name="browser">Browser the match was made in</param>
        /// <param name="match">The image match</param>
        /// <param name="selector">The selector that matched, for its offsets</param>
        /// <returns>The element, or null when there is none and only the point is left</returns>
        private static ReadOnlyCollection<GPALElement> ElementUnderImage(Browser browser, GPALElement match, Selector selector)
        {
            ReadOnlyCollection<GPALElement> retVal = null;
            Point aim = ToViewportPoint(browser, match, selector);

            // the numbers go into the script rather than as arguments, since the engines pass arguments differently
            string css = RunPageScript(browser, $@"
                (function (x, y) {{
                    var el = document.elementFromPoint(x, y);

                    if (!el || 'IFRAME' === el.tagName || 'FRAME' === el.tagName || el.shadowRoot)
                        return '';

                    var actionable = el.closest('a[href], area[href], button, input, select, textarea, label, summary, ' +
                        '[role=button], [role=link], [role=menuitem], [role=menuitemcheckbox], [role=menuitemradio], ' +
                        '[role=tab], [role=checkbox], [role=radio], [role=switch], [role=option], [role=treeitem], ' +
                        '[onclick], [contenteditable]:not([contenteditable=false])');

                    if (actionable)
                        el = actionable;

                    var path = [];

                    while (el && el.nodeType === Node.ELEMENT_NODE) {{
                        if (el.id) {{
                            path.unshift('#' + CSS.escape(el.id));
                            break;
                        }}

                        var part = el.nodeName.toLowerCase(), sibling = el, nth = 1;

                        while ((sibling = sibling.previousElementSibling) != null)
                            if (sibling.nodeName === el.nodeName)
                                nth++;

                        path.unshift(part + ':nth-of-type(' + nth + ')');
                        el = el.parentElement;
                    }}

                    return path.join(' > ');
                }})({aim.X}, {aim.Y})");

            if (false == string.IsNullOrEmpty(css))
            {
                Selector cssSel = GPAL.Selector.WithCSS(css).ToGPALObject();

                retVal = FindWebElementsByCss(browser, cssSel.SelectorPaths[0], cssSel);

                if (0 < (retVal?.Count ?? 0))
                {
                    retVal[0].Css = css;
                    retVal[0].ImageMatch = match;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, 0 < retVal?.Count
                ? $"Image match for [{selector.Name}] is element [{css}], clicked at the matched point"
                : $"Image match for [{selector.Name}] has no element of its own under it, so only the point is used", browser, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// Checks the image match's click point still hits its element; something, usually an ad after a scroll, can
        /// cover it.<br/>
        /// If covered, scroll the element to the middle of the window, away from sticky headers, ad bars and cookie
        /// banners, and move the point with it. False if still covered. One page script, so a clear point costs one
        /// call.
        /// </summary>
        /// <param name="browser">Browser to ask</param>
        /// <param name="resolved">The element the image match resolved to</param>
        /// <param name="selector">The selector, for its name</param>
        /// <param name="aim">The viewport point to click, moved when the element was centred</param>
        /// <returns>True when the point is the element's, false when something still covers it</returns>
        private static bool PointIsClear(Browser browser, GPALElement resolved, Selector selector, ref Point aim)
        {
            bool retVal = true;

            string answer = RunPageScript(browser, $@"
                (function (css, x, y) {{
                    var el = document.querySelector(css);

                    if (!el)
                        return 'gone';

                    function hits(px, py) {{
                        var hit = document.elementFromPoint(px, py);
                        return !!hit && (hit === el || el.contains(hit));
                    }}

                    function named(px, py) {{
                        var hit = document.elementFromPoint(px, py);

                        if (!hit)
                            return 'nothing';

                        var classes = 'string' === typeof hit.className ? hit.className.trim().split(/\s+/).filter(Boolean).slice(0, 3).join('.') : '';

                        return hit.tagName.toLowerCase() + (hit.id ? '#' + hit.id : '') + (classes ? '.' + classes : '');
                    }}

                    if (hits(x, y))
                        return 'clear|0|0|';

                    var covering = named(x, y);
                    var before = el.getBoundingClientRect();

                    el.scrollIntoView({{ block: 'center', inline: 'nearest', behavior: 'instant' }});

                    var after = el.getBoundingClientRect();
                    var dx = Math.round(after.left - before.left), dy = Math.round(after.top - before.top);

                    return (hits(x + dx, y + dy) ? 'centred' : 'covered') + '|' + dx + '|' + dy + '|' + covering;
                }})({Newtonsoft.Json.JsonConvert.SerializeObject(resolved.Css)}, {aim.X}, {aim.Y})");

            // No answer or "gone": leave the point unchanged and return true.
            string[] parts = answer?.Split(new[] { '|' }, 4);

            if (4 == parts?.Length && ("centred" == parts[0] || "covered" == parts[0]))
            {
                aim = new Point(aim.X + Convert.ToInt32(parts[1]), aim.Y + Convert.ToInt32(parts[2]));

                if ("centred" == parts[0])
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{parts[3]}] covered selector [{selector.Name}] where its picture was, so the element was moved to the middle of the window and is clicked there", browser, GPALObjectType.Browser);
                else
                {
                    retVal = false;

                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, true == GPAL.NoFallbackRecoveryActions
                        ? $"[{parts[3]}] covers selector [{selector.Name}] even with it in the middle of the window, and recovery actions are off, so the point is clicked and [{parts[3]}] may take the click"
                        : $"[{parts[3]}] covers selector [{selector.Name}] even with it in the middle of the window, so the element itself is clicked instead of its picture", browser, GPALObjectType.Browser);
                }
            }

            return retVal;
        }

        /// <summary>
        /// Clicks the element an image match resolved to instead of the point: when something covers the point, and
        /// on Firefox with OttoMagic, which cannot click a point. The event goes to the element, so a cover cannot
        /// take it. Selenium and Puppeteer use a JavaScript click, since their native clicks are refused as
        /// intercepted or hit the cover.
        /// </summary>
        /// <param name="browser">Browser to click in</param>
        /// <param name="element">The element the image match resolved to</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        private static void ClickElementItself(Browser browser, GPALElement element, ClickType clickType, ModifierKeys modifierKeys)
        {
            if (true == browser.UseOttoMagic)
            {
                if (ModifierKeys.NONE != modifierKeys)
                    browser.MagicHelper.PressModifierKey(modifierKeys);

                switch (clickType)
                {
                    case ClickType.LeftDoubleClick:
                        browser.MagicHelper.LeftDoubleClick(element.Css);
                        break;
                    case ClickType.MiddleClick:
                        browser.MagicHelper.MiddleClick(element.Css);
                        break;
                    case ClickType.RightClick:
                        browser.MagicHelper.RightClick(element.Css);
                        break;
                    default:
                        browser.MagicHelper.LeftClick(element.Css);
                        break;
                }

                if (ModifierKeys.NONE != modifierKeys)
                    browser.MagicHelper.ReleaseModifierKey(modifierKeys);
            }
            else
                JavaScriptClick(browser, element, clickType, modifierKeys);
        }

        /// <summary>
        /// The viewport point to click for an image match: the middle unless the selector has an offset. A screen
        /// capture match has the content area origin subtracted. Assumes 100% scaling.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Image-matched surrogate</param>
        /// <param name="selector">Selector providing the offset</param>
        /// <returns>The point in viewport coordinates</returns>
        private static Point ToViewportPoint(Browser browser, GPALElement element, Selector selector)
        {
            // Both offsets zero means aim at the middle, as HardwareMoveTo does.
            Point aim = (0 == selector.OffsetX && 0 == selector.OffsetY)
                ? ImageCenterOffset(element.Size)
                : new Point(selector.OffsetX, selector.OffsetY);

            int pointX = element.Location.X + aim.X;
            int pointY = element.Location.Y + aim.Y;

            if (CoordinateSpace.Screen == element.CoordinateSpace)
            {
                Rectangle content = GetWindowRectangle(browser);
                pointX -= content.X;
                pointY -= content.Y;
            }

            return new Point(pointX, pointY);
        }
        /// <summary>
        /// Clicks a viewport point for an image match with no element, when there is no hardware mouse (headless).
        /// </summary>
        /// <param name="browser">Browser to click in</param>
        /// <param name="point">Viewport coordinate to click</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        private static void Click(Browser browser, Point point, ClickType clickType, ModifierKeys modifierKeys)
        {
            // OttoMagic's trusted click takes only a point and a button, no modifier keys. A modified left or middle
            // click on OttoMagic publishes a warning that it is made without the keys.
            if (true == browser.UseOttoMagic && ModifierKeys.NONE != modifierKeys
                && (ClickType.LeftClick == clickType || ClickType.MiddleClick == clickType))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{Browser.GetModifierKeys(modifierKeys)}] cannot be held for a [{clickType}] at a point on OttoMagic, whose trusted click carries no keys, so the click is made without them.", browser, GPALObjectType.Browser);

            if (true == browser.UsePuppeteer)
            {
                switch (clickType)
                {
                    case ClickType.LeftClick:
                        browser.PuppeteerClient.LeftClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                    case ClickType.LeftDoubleClick:
                        browser.PuppeteerClient.LeftDoubleClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                    case ClickType.MiddleClick:
                        browser.PuppeteerClient.MiddleClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                    case ClickType.RightClick:
                        browser.PuppeteerClient.RightClick(point).WithModifiers(modifierKeys).Execute();
                        break;
                }
            }
            else if (true == browser.UseSelenium && ClickType.MiddleClick == clickType && BrowserType.FireFox != browser.BrowserSettings.BrowserType)
            {
                // Selenium Actions has no middle button. On Chrome and Edge send it through CDP
                // Input.dispatchMouseEvent: move, press, release.
                int cdpModifiers = (int)(modifierKeys & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows | ModifierKeys.Shift));

                BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Input.dispatchMouseEvent", new Dictionary<string, object>
                {
                    ["type"] = "mouseMoved", ["x"] = point.X, ["y"] = point.Y, ["button"] = "none"
                });
                BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Input.dispatchMouseEvent", new Dictionary<string, object>
                {
                    ["type"] = "mousePressed", ["x"] = point.X, ["y"] = point.Y, ["button"] = "middle", ["buttons"] = 4, ["clickCount"] = 1, ["modifiers"] = cdpModifiers
                });
                BrowserHelper.ExecuteCdp(browser.BrowserDriver, "Input.dispatchMouseEvent", new Dictionary<string, object>
                {
                    ["type"] = "mouseReleased", ["x"] = point.X, ["y"] = point.Y, ["button"] = "middle", ["buttons"] = 0, ["clickCount"] = 1, ["modifiers"] = cdpModifiers
                });
            }
            else if (true == browser.UseSelenium && ClickType.MiddleClick == clickType && browser.BrowserDriver is IActionExecutor executor)
            {
                // Firefox: raw W3C actions, which geckodriver supports: move, press and release the middle button.
                PointerInputDevice mouse = new PointerInputDevice(PointerKind.Mouse, "middle-click");
                ActionSequence press = new ActionSequence(mouse, 0);

                press.AddAction(mouse.CreatePointerMove(CoordinateOrigin.Viewport, point.X, point.Y, TimeSpan.Zero));
                press.AddAction(mouse.CreatePointerDown(MouseButton.Middle));
                press.AddAction(mouse.CreatePointerUp(MouseButton.Middle));

                executor.PerformActions(new List<ActionSequence> { press });
                executor.ResetInputState();
            }
            else if (true == browser.UseSelenium)
            {
                Actions action = new Actions(browser.BrowserDriver).MoveToLocation(point.X, point.Y);

                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    action.KeyDown(OpenQA.Selenium.Keys.LeftAlt).KeyDown(OpenQA.Selenium.Keys.Alt);
                if (modifierKeys.HasFlag(ModifierKeys.Control))
                    action.KeyDown(OpenQA.Selenium.Keys.LeftControl).KeyDown(OpenQA.Selenium.Keys.Control);
                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    action.KeyDown(OpenQA.Selenium.Keys.LeftShift).KeyDown(OpenQA.Selenium.Keys.Shift);
                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    action.KeyDown(OpenQA.Selenium.Keys.Meta);

                switch (clickType)
                {
                    case ClickType.LeftClick:
                        action.Click();
                        break;
                    case ClickType.LeftDoubleClick:
                        action.DoubleClick();
                        break;
                    case ClickType.RightClick:
                        action.ContextClick();
                        break;
                    default:
                        // selenium's Actions has no middle click to bind to
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{clickType}] on an image match by coordinate is not supported by Selenium, performing a left click.", browser, GPALObjectType.Browser);
                        action.Click();
                        break;
                }

                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                    action.KeyUp(OpenQA.Selenium.Keys.LeftAlt).KeyUp(OpenQA.Selenium.Keys.Alt);
                if (modifierKeys.HasFlag(ModifierKeys.Control))
                    action.KeyUp(OpenQA.Selenium.Keys.LeftControl).KeyUp(OpenQA.Selenium.Keys.Control);
                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                    action.KeyUp(OpenQA.Selenium.Keys.LeftShift).KeyUp(OpenQA.Selenium.Keys.Shift);
                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                    action.KeyUp(OpenQA.Selenium.Keys.Meta);

                ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                if (ModifierKeys.NONE != unsupported)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{clickType}]: [{Browser.GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", browser, GPALObjectType.Browser);

                action.Build().Perform();
            }
            else if (ClickType.LeftClick != clickType && ClickType.MiddleClick != clickType)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{clickType}] on an image is not supported by OttoMagic, which clicks a point with the left or middle button only.", browser, GPALObjectType.Browser);
            else if (true == (ClickType.MiddleClick == clickType
                    ? browser.MagicHelper.TrustedMiddleClick(point.X, point.Y)
                    : browser.MagicHelper.TrustedLeftClick(point.X, point.Y)))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[Trusted][{clickType}] clicked at [{point.X},{point.Y}].", browser, GPALObjectType.Browser);
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[Trusted][{clickType}] at [{point.X},{point.Y}] was not made.", browser, GPALObjectType.Browser);
        }
        /// <summary>
        /// Types into an image match with no DOM handle, by clicking the coordinate to focus whatever is there and
        /// then sending the text to the focused element. Used when there is no hardware keyboard path available,
        /// which in practice means headless.
        /// </summary>
        /// <param name="browser">Browser to type in</param>
        /// <param name="point">Viewport coordinate to focus</param>
        /// <param name="text">Text to enter</param>
        /// <param name="writeMode">Append, Insert or Overwrite</param>
        private static void FillInFrom(Browser browser, Point point, string text, WriteMode writeMode)
        {
            // No element to set a value on, so click the point to focus it, then type.
            Click(browser, point, ClickType.LeftClick, ModifierKeys.NONE);

            if (true == browser.UsePuppeteer)
            {
                if (WriteMode.Overwrite == writeMode)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "[Overwrite] cannot clear an image match by coordinate, the text will be added to whatever is already there.", browser, GPALObjectType.Browser);

                browser.PuppeteerClient.SendString(text).Execute();
            }
            else if (true == browser.UseSelenium)
            {
                Actions actions = new Actions(browser.BrowserDriver);

                if (WriteMode.Overwrite == writeMode)
                    actions.KeyDown(Keys.Control).SendKeys("a").KeyUp(Keys.Control);

                actions.SendKeys(text).Perform();
            }
            else
            {
                // OttoMagic has no call to type into the focused element, so write the text with execute-javascript
                // (needs Allow user scripts on). It follows shadow roots to the focused element, uses the native
                // value setter so frameworks see the change, and fires input and change.
                string answer = RunPageScript(browser, $@"
                    (function (text, mode) {{
                        var el = document.activeElement;

                        while (el && el.shadowRoot && el.shadowRoot.activeElement)
                            el = el.shadowRoot.activeElement;

                        if (!el || el === document.body)
                            return 'nothing has focus';

                        if (el.isContentEditable) {{
                            if ('Overwrite' === mode)
                                document.execCommand('selectAll', false, null);
                            else if ('Append' === mode) {{
                                var range = document.createRange();
                                range.selectNodeContents(el);
                                range.collapse(false);
                                var selection = window.getSelection();
                                selection.removeAllRanges();
                                selection.addRange(range);
                            }}

                            document.execCommand('insertText', false, text);
                            return 'ok';
                        }}

                        var descriptor = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), 'value');

                        if (!descriptor || !descriptor.set)
                            return 'the focused ' + el.tagName.toLowerCase() + ' takes no text';

                        var current = el.value || '';
                        var next = 'Overwrite' === mode ? text : current + text;

                        if ('Insert' === mode && typeof el.selectionStart === 'number' && null !== el.selectionStart)
                            next = current.slice(0, el.selectionStart) + text + current.slice(el.selectionEnd);

                        descriptor.set.call(el, next);
                        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                        el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                        return 'ok';
                    }})({Newtonsoft.Json.JsonConvert.SerializeObject(text)}, {Newtonsoft.Json.JsonConvert.SerializeObject(writeMode.ToString())})");

                if ("ok" != answer)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot fill in the image match by coordinate. OttoMagic focused it with a trusted click, and could not write into it [{answer ?? "no answer"}].", browser, GPALObjectType.Browser);
            }
        }
        /// <summary>
        /// Moves the mouse to <paramref name="element"/> using OttoMagic, Puppeteer or Selenium Actions, with
        /// the given pixel offset. Falls back to <see cref="ScrollIntoView"/> on Selenium errors.
        /// </summary>
        /// <param name="element">Element to move to</param>
        /// <param name="offsetX">Horizontal pixel offset within the element</param>
        /// <param name="offsetY">Vertical pixel offset within the element</param>
        /// <param name="browser">Browser the element belongs to</param>
        private static void MoveTo(GPALElement element, int offsetX, int offsetY, Browser browser)
        {
            try
            {
                if (true == browser.UseOttoMagic)
                {
                    browser.MagicHelper.MoveTo(element.Css);
                }
                else if (true == browser.UsePuppeteer)
                {
                    browser.PuppeteerClient.MoveTo(element.ElementHandle).Execute();
                }
                else
                    new Actions(browser.BrowserDriver).MoveToElement(element.WebElement, offsetX, offsetY).Perform();
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (false == GPAL.NoFallbackRecoveryActions)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium threw during move to Selector [{browser.CurrentUOW.CurrentSelector?.Name}], falling back to javascript.", browser, GPALObjectType.Browser);
                    ElementHelper.ScrollIntoView(browser, element);
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Selenium threw during move to Selector [{browser.CurrentUOW.CurrentSelector?.Name}], and recovery actions are off, so nothing was done about it.", browser, GPALObjectType.Browser, ex);

            }

        }
        /// <summary>
        /// Focuses <paramref name="webElement"/> using hardware emulation: clicks editable elements to focus
        /// them, otherwise just moves the mouse onto the element.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to focus</param>
        /// <param name="OffsetX">Horizontal pixel offset within the element</param>
        /// <param name="OffsetY">Vertical pixel offset within the element</param>
        private static void HardwareFocus(Browser browser, GPALElement webElement, int OffsetX, int OffsetY)
        {
            // Scroll the element on screen here, not in every caller. Below the fold its screen Y passes
            // HardwareClick's coordinate check and the click hits whatever is there.
            ScrollIntoView(browser, webElement, Enums.InteractionType.Hardware);

            // IsElementEditable only counts the text-ish inputs, and the picker inputs take typing just as much.
            // without the click they never get focus, and the Ctrl+A that follows selects the whole document
            bool isPickerInput = valueOnlyInputTypes.Contains(webElement.Type?.ToLowerInvariant() ?? "");

            if (true == ElementHelper.IsElementEditable(webElement) || true == isPickerInput)
            {
                // Safe / desired: click to focus (includes text inputs, textareas, contenteditable, etc.)
                HardwareClick(browser, webElement, ClickType.LeftClick, ModifierKeys.NONE, browser.CurrentUOW.CurrentSelector);
            }
            else
            {
                // Only move — avoid activating menus, toggles, navigation, submits, file pickers
                HardwareMoveTo(browser, webElement, browser.CurrentUOW.CurrentSelector);
            }
        }
        /// <summary>
        /// Computes the absolute screen coordinates of <paramref name="element"/> (accounting for iframe nesting
        /// and the selector's offset) and moves the hardware mouse there. Same logic as hardware hover.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element (or pseudo-element from an image match) to move to</param>
        /// <param name="selector">Selector providing the offset and simulate-mouse settings</param>
        /// <returns>The absolute screen rectangle of the element</returns>
        private static Rectangle HardwareMoveTo(Browser browser, dynamic element, Selector selector)
        {
            Rectangle content = new Rectangle(), iframeRect = new Rectangle(0, 0, 0, 0);
            bool isImageMatch = "GPALElement" == (string)element.TagName;

            // OttoMagic and Selenium report document coordinates, which ScrollIntoView re-reads when it scrolls.
            // Puppeteer reports viewport coordinates, so always re-read it into document coordinates here; the scroll
            // comes off below.
            if (false == isImageMatch && true == browser.UsePuppeteer)
                RefreshLocation(browser, (GPALElement)element);

            Rectangle rect2 = new Rectangle(element.Location, element.Size);
            int offsetX = selector.OffsetX;
            int offsetY = selector.OffsetY;

            // Positions are document coordinates, so read the scroll offset to subtract below, or the mouse lands a
            // scroll offset away. Image matches skip this.
            Point scroll = Point.Empty;
            if (false == isImageMatch)
            {
                int pagex = 0;
                int pagey = 0;

                if (true == browser.UseOttoMagic)
                {
                    pagex = browser.MagicHelper.WindowPageOffsetX();
                    pagey = browser.MagicHelper.WindowPageOffsetY();
                    scroll = new Point(pagex, pagey);
                }
                else if (true == browser.UsePuppeteer)
                {
                    pagex = browser.PuppeteerClient.WindowPageOffsetX().Execute<int>();
                    pagey = browser.PuppeteerClient.WindowPageOffsetY().Execute<int>();
                    scroll = new Point(pagex, pagey);
                }
                else
                {
                    scroll = GetPageOffset(browser);
                }
            }

            // find the absolute screen position of the webelement
            // if it's a posuedo GPALElement, then it is an image match and those coords are already absolute
            // iframe or shadowdom nesting, get parent coords
            if (0 < browser.CurrentUOW.ContextPath?.Count)
            {
                foreach (UnitOfWork.WebElementWithType contextType in browser.CurrentUOW.ContextPath)
                {
                    if (ElementType.IFrame == contextType.elementType)
                    {
                        ClientRectangle tmpRect = contextType.gPalElement.BoundingRect;
                        iframeRect.X += (int)contextType.gPalElement.Location.X;
                        iframeRect.Y += (int)contextType.gPalElement.Location.Y;
                    }
                }
            }

            // if the click/hover offset is not specified, pick a point inside the element
            if (0 == offsetX && 0 == offsetY)
            {
                if ("GPALElement" == (string)element.TagName)
                {
                    // an image match points at one specific graphic, so aim at the middle of it
                    Point center = ImageCenterOffset(rect2.Size);
                    offsetX = center.X;
                    offsetY = center.Y;
                }
                else
                {
                    // Aim at the middle; elsewhere can hit padding, a border or a child that takes the click. Jitter
                    // up to 10% of width and height so the same pixel is not hit every time.
                    int spreadX = Math.Max(1, (int)rect2.Width / 10);
                    int spreadY = Math.Max(1, (int)rect2.Height / 10);

                    offsetX = (int)rect2.Width / 2 + Jitter.Next(-spreadX, spreadX + 1);
                    offsetY = (int)rect2.Height / 2 + Jitter.Next(-spreadY, spreadY + 1);
                }
            }

            content = GetWindowRectangle(browser);
            _lastContentArea = content;

            // Image match from a screen capture: already screen coordinates. From a visible-tab capture: add the
            // content area origin. Assumes 100% scaling.
            Point imageOrigin = Point.Empty;

            if ("GPALElement" == (string)element.TagName && CoordinateSpace.Viewport == ((GPALElement)element).CoordinateSpace)
                imageOrigin = new Point(content.X, content.Y);

            // === FINAL SCREEN COORDS
            int screenX = (int)(("GPALElement" == element.TagName)
                ? rect2.Left + offsetX + imageOrigin.X
                : rect2.Left + offsetX + iframeRect.X + content.X - scroll.X);

            int screenY = (int)(("GPALElement" == element.TagName)
                ? rect2.Top + offsetY + imageOrigin.Y
                : rect2.Top + offsetY + iframeRect.Y + content.Y - scroll.Y);

            // An element larger than the window cannot be fully on screen, so the point can fall outside it; a
            // stretched file input is the usual case. Only then move the point into the element's visible part. A
            // normal off-screen element is scrolled to instead.
            if ("GPALElement" != (string)element.TagName
                && true == IsBiggerThanViewport(new Size((int)rect2.Width, (int)rect2.Height), content)
                && false == content.Contains(screenX, screenY))
            {
                // Measure from the element's top-left. screenX and screenY already include the offset, so subtract
                // it first.
                Point origin = new Point(screenX - offsetX, screenY - offsetY);
                Rectangle onScreen = Rectangle.Intersect(new Rectangle(origin.X, origin.Y, (int)rect2.Width, (int)rect2.Height), content);

                if (false == onScreen.IsEmpty)
                {
                    // A point inside the rectangle may not reach the element: most of an oversized element is not
                    // its control, and something may be on top. Hit-test candidates with PointHitsElement and take
                    // the first that returns this element. Candidates sit at the vertical middle, first at insets
                    // from the right edge (hitTestInsets), then at fractions of the width (hitTestFractions).
                    bool verified = false;
                    int tryY = onScreen.Y + onScreen.Height / 2;   // vertically it fits, so the middle is the middle
                    List<int> candidates = new List<int>();

                    foreach (int inset in hitTestInsets)
                        if (inset < onScreen.Width)
                            candidates.Add(onScreen.Right - inset);

                    foreach (float fraction in hitTestFractions)
                        candidates.Add(onScreen.X + (int)(onScreen.Width * fraction));

                    foreach (int tryX in candidates)
                        if (true == PointHitsElement(browser, (GPALElement)element, new Point(tryX, tryY)))
                        {
                            screenX = tryX;
                            screenY = tryY;
                            verified = true;
                            break;
                        }

                    if (false == verified)
                    {
                        // No candidate hits the element, so the click will miss. Use the middle of the visible part
                        // and say so in the message.
                        screenX = onScreen.X + onScreen.Width / 2;
                        screenY = onScreen.Y + onScreen.Height / 2;
                    }

                    // Save the adjusted point as a WithOffset value so a workflow can put it in its selector. The
                    // click publishes it, not this method.
                    _lastClampedPoint = $"at [WithOffset({screenX - origin.X}, {screenY - origin.Y})]{(true == verified ? "" : ", which nothing on this element answered to")}, for window [{_lastOuterWindow.X}, {_lastOuterWindow.Y}, {_lastOuterWindow.Width}, {_lastOuterWindow.Height}], because the element measures [{(int)rect2.Width}x{(int)rect2.Height}], larger than the window";
                }
            }

            // === MOVE MOUSE ===
            if (true == GPAL.SimulateMouse || true == selector.SimulateMouse)
                HardwareHelper.MoveMouse(screenX, screenY, 10, 10);
            else
                HardwareHelper.MoveMouse(screenX, screenY);

            return new Rectangle(screenX, screenY, (int)rect2.Width, (int)rect2.Height);
        }
        /// <summary>
        /// Gets the page's current scroll offset (window.pageXOffset / window.pageYOffset).<br/>
        /// OttoMagic and Puppeteer both have a declared endpoint for it (window-page-xoffset); Selenium has no
        /// such call, so it runs the javascript itself.
        /// </summary>
        /// <param name="browser">Browser to read the scroll offset from</param>
        /// <returns>The page scroll offset, or 0,0 if it could not be read</returns>
        internal static Point GetPageOffset(Browser browser)
        {
            Point pageOffset = Point.Empty;

            if (true == browser.UseOttoMagic)
                pageOffset = new Point(browser.MagicHelper.WindowPageOffsetX(), browser.MagicHelper.WindowPageOffsetY());
            else if (true == browser.UsePuppeteer)
                pageOffset = new Point(browser.PuppeteerClient.WindowPageOffsetX().Execute<int>(), browser.PuppeteerClient.WindowPageOffsetY().Execute<int>());
            else if (BrowserHelper.ExecuteJavaScriptObj("return [window.top.pageXOffset, window.top.pageYOffset]", browser) is IList<object> offsets && 2 <= offsets.Count)
                pageOffset = new Point(Convert.ToInt32(offsets[0]), Convert.ToInt32(offsets[1]));

            return pageOffset;
        }
        /// <summary>
        /// True when the interaction clicks a screen coordinate, so the element must be on screen: the hardware
        /// engines and a Hardware selector. Everything else uses css or a handle and works off screen.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="interactionType">Interaction type in play, or null to take the current selector's</param>
        /// <returns>True if the interaction needs the element to be on-screen</returns>
        private static bool UsesScreenCoordinates(Browser browser, Enums.InteractionType? interactionType)
        {
            Enums.InteractionType? useType = interactionType ?? browser.CurrentUOW?.CurrentSelector?.InteractionType;

            return true == browser.BrowserSettings.UseHardware
                || true == GPAL.GPALSettings.UseHardware
                || Enums.InteractionType.Hardware == useType;
        }
        /// <summary>
        /// Scrolls <paramref name="element"/> into view if it is not already fully visible in the viewport,
        /// using OttoMagic, Puppeteer or injected JavaScript depending on the active engine, then confirms it
        /// really landed on-screen and pages the window to it when it did not.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to scroll into view</param>
        /// <param name="interactionType">Interaction the scroll is preparing for, or null to take the current selector's</param>
        /// <param name="forVisibility">True when the element must end up visible, as in a requested scroll; false
        /// when the scroll only prepares a coordinate click</param>
        internal static void ScrollIntoView(Browser browser, dynamic element, Enums.InteractionType? interactionType = null, bool forVisibility = false)
        {
            if ("GPALElement".Equals(((GPALElement)element).TagName))
                return; // image surrogate, it's already on-screen if matched

            // OttoMagic acts through javascript by css and does not need the element on screen. Skip the scroll
            // unless the interaction uses screen coordinates or forVisibility is set.
            if (true == browser.UseOttoMagic && false == forVisibility && false == UsesScreenCoordinates(browser, interactionType))
                return;

            // an element already wholly on screen is left where it is. scrolling it anyway costs the scroll, the
            // settle wait, the visibility check after it and a re-read of where it went, on every click
            if (true == IsVisibleInViewport(browser, (GPALElement)element))
                return;

            if (true == browser.UseOttoMagic)
                browser.MagicHelper.ScrollIntoView(((GPALElement)element).Css);
            else if (true == browser.UsePuppeteer)
                browser.PuppeteerClient.ScrollIntoView((GPALElement)element).Execute();
            else if (null != element)
            {
                string debounceScript = @"
                    // Best JS replacement for scrollIntoViewIfNeeded - do not scroll to if it's already on-screen
                    function scrollIntoViewIfNeeded(el) {
                        return new Promise(resolve => {
                            const io = new IntersectionObserver(([entry]) => {
                                io.disconnect();
                                if (entry.intersectionRatio < 1) {
                                    el.scrollIntoView({ behavior: 'instant', block: 'center', inline: 'center' });
                                }
                                resolve();
                            }, { threshold: 1 });
                            io.observe(el);
                        });
                    }

                    scrollIntoViewIfNeeded(arguments[0]);
                ";

                BrowserHelper.ExecuteJavaScript(browser, debounceScript, typeof(GPALElement) == element.GetType() ? ((GPALElement)element).WebElement : element);
                //BrowserHelper.ExecuteJavaScript(browser, "arguments[0].scrollIntoView()", element.WebElement);
            }
            Thread.Sleep(250);  // give the dom time to catch up before we start asking about it, we apparently can ask for page offset faster than it has it...

            // Scrolling the document does not move a fixed or sticky element, so it can still be off screen.
            // ScrollUntilVisible pages the window to it.
            ScrollUntilVisible(browser, (GPALElement)element);

            // The scroll made the stored position stale for a coordinate click, so re-read it. Not Puppeteer;
            // HardwareMoveTo re-reads it every time.
            if (false == browser.UsePuppeteer && true == UsesScreenCoordinates(browser, interactionType))
                RefreshLocation(browser, (GPALElement)element);
        }
        /// <summary>
        /// Left Click on a webelement
        /// NOTE: if clicktype is passed in, it will click that type - used only by browser.MiddleClick().
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="modifierKeys"></param>
        /// <param name="clickType">if clicktype is passed in, it will click that type - used only by MiddleClick()</param>
        /// <param name="trusted">ask the engine for a click the page reads as trusted - used only by StealthLeftClick()</param>
        public static void LeftClick(Browser browser, ModifierKeys modifierKeys = ModifierKeys.NONE, ClickType clickType = ClickType.LeftClick, bool trusted = false)
        {
            List<GPALElement> matchedElements;
            List<GPALElement> clickableElements;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == matchedElements)
                {
                    PublishToEventHandler(browser, selector, matchedElements);
                    continue;
                }

                // NOTE: _ALWAYS_ used matched elements, it will be the same as found elements if no match criteris
                // if match critieria, we want to use it cause it will be matched elems or none

                if (1 < matchedElements.Count)
                {
                    clickableElements = new List<GPALElement>();
                    foreach (GPALElement elem in matchedElements)
                        if (true == elem.IsClickable(false))     // no output from isClickable
                            clickableElements.Add(elem);

                    if (0 == clickableElements.Count)
                        clickableElements = matchedElements;
                }
                else
                    clickableElements = matchedElements;

               int rowCount = 0;
               int total = Math.Min(clickableElements.Count, browser.CurrentUOW.WithAllThatMatch);
               object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

               foreach (GPALElement elem in clickableElements)
               {
                    if (null != elem)
                    {
                        if (true == elem.TagName.ToLower().Equals("option"))
                            SelectClick(browser, selector, elem, SelectClickType.LeftClick, modifierKeys);
                        else
                            Click(browser, selector, elem, clickType, modifierKeys, trusted);

                        if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                            break;
                    }
                }

                //Thread.Sleep(500);
            }
        }
        /// <summary>
        /// Middle clicks each element matched by the current unit of work's selectors.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        public static void MiddleClick(Browser browser)
        {
            LeftClick(browser, ModifierKeys.NONE, ClickType.MiddleClick);
        }

        /// <summary>
        /// Scrolls each element matched by the current unit of work's selectors horizontally or vertically by
        /// the given pixel amount.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="amount">Pixels to scroll (negative scrolls left/up)</param>
        /// <param name="scrollTypes">Whether to scroll Horizontal or Vertical</param>
        public static void ScrollElement(Browser browser, int amount, ScrollTypes scrollTypes)
        {
            List<GPALElement> matchedElements;
            bool matchedAll = false;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                   int rowCount = 0;

                   foreach (GPALElement elem in matchedElements)
                   {
                        if (null != elem)
                        {
                            int hAmount = ScrollTypes.Horizontal == scrollTypes ? amount : 0;
                            int vAmount = ScrollTypes.Vertical == scrollTypes ? amount : 0;

                            // Bring the element on screen first, as a click does, or an element below the page
                            // bottom scrolls out of sight.
                            ScrollIntoView(browser, elem, null, true);

                            if (true == browser.UseOttoMagic)
                                browser.MagicHelper.ScrollElement(elem.Css, hAmount, vAmount);
                            else if (true == browser.UsePuppeteer)
                                browser.PuppeteerClient.ScrollElement(elem.Css).WithHPixels(hAmount).WithVPixels(vAmount).Execute();
                            else if (true == browser.UseSelenium)
                                BrowserHelper.ExecuteJavaScript(browser, $"arguments[0].scrollBy({{ left: {hAmount}, top: {vAmount}, behavior: 'instant' }})", elem.WebElement);

                            if (++rowCount >= browser.CurrentUOW.WithAllThatMatch)
                                break;
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Clicks every element matched by the current unit of work's selectors using the given click type,
        /// using SelectClick for option elements.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="clickType">Type of click to perform (e.g. LeftClick, RightClick)</param>
        /// <returns>True if at least one click was performed</returns>
        public static bool GenericClick(Browser browser, ClickType clickType)
        {
            List<GPALElement> matchedElements;
            bool matchedAll = false;
            bool clickPerformed = false;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                   int rowCount = 0;
                   int total = Math.Min(matchedElements.Count, browser.CurrentUOW.WithAllThatMatch);
                   object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

                   foreach (GPALElement elem in matchedElements)
                   {
                        if (null != elem)
                        {
                            if (true == elem.TagName.ToLower().Equals("option"))
                                SelectClick(browser, selector, elem, SelectClickType.LeftClick, ModifierKeys.NONE);
                            else
                                Click(browser, selector, elem, clickType, ModifierKeys.NONE);

                            clickPerformed = true;

                            if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                                break;
                        }
                    }
                }
            }
            return clickPerformed;
        }
        /// <summary>
        /// Clicks every element matched by the current unit of work's selectors, using the given select-click
        /// type for option elements and a plain left click for everything else.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="selectClickType">Select-menu click behavior (e.g. RandomSelect, SequentialSelect)</param>
        /// <returns>True if at least one click was performed</returns>
        public static bool GenericClick(Browser browser, SelectClickType selectClickType)
        {
            List<GPALElement> matchedElements;
            bool matchedAll = false;
            bool clickPerformed = false;

            foreach (Selector selector in browser.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                    int rowCount = 0;
                    int total = Math.Min(matchedElements.Count, browser.CurrentUOW.WithAllThatMatch);
                    object pageMark = 1 < total ? BrowserHelper.MarkDocument(browser) : null;

                    foreach (GPALElement elem in matchedElements)
                    {
                        if (null != elem)
                        {
                            if (true == elem.TagName.ToLower().Equals("option"))
                                SelectClick(browser, selector, elem, selectClickType, ModifierKeys.NONE);
                            else
                                Click(browser, selector, elem, ClickType.LeftClick, ModifierKeys.NONE);

                            clickPerformed = true;

                            if (++rowCount >= browser.CurrentUOW.WithAllThatMatch || true == PageReplaced(browser, pageMark, rowCount, total))
                                break;
                        }
                    }
                }
            }
            return clickPerformed;
        }

        // Click type note
        // NOTE: Special types for Select menus: RandomSelect will pick a random menu item, SequentialSelect will walk the list (w/ & w/0 wrapping)
        // written by grok with updates from mbv
        internal static class SelectClickHelper
        {
            // Static dictionary to track the current index for SequentialSelect and SequentialSelectWithWrap
            internal static readonly Dictionary<string, int> _sequentialSelectIndices = new Dictionary<string, int>();
            // Track the last parentNode identifier
            internal static string _lastParentNodeId = null;

            /// <summary>
            /// Computes a hash of all of <paramref name="element"/>'s attributes (sorted name=value pairs) using
            /// the same algorithm as <see cref="GetElementAttributeHashJavaScript"/>, so the two are interchangeable.
            /// </summary>
            /// <param name="element">Element to hash</param>
            /// <param name="browser">Browser the element belongs to (unused, kept for symmetry with the JS variant)</param>
            /// <returns>String hash of the element's attributes, or its GetHashCode() on failure</returns>
            internal static string GetElementAttributeHashCsharp(GPALElement element, Browser browser)
            {
                try
                {
                    // Get all attributes from the Attributes dictionary
                    var attrList = new List<string>();

                    foreach (var attrName in element.Attributes.Keys)
                    {
                        // Use GetAttribute to retrieve the actual value
                        string value = element.GetAttribute(attrName);
                        if (!string.IsNullOrEmpty(value))
                        {
                            attrList.Add($"{attrName}={value}");
                        }
                    }

                    // Sort attributes for consistent hashing
                    attrList.Sort();
                    string attrString = string.Join("|", attrList);

                    // Compute hash (same algorithm as JavaScript)
                    int hash = 0;
                    foreach (char c in attrString)
                    {
                        hash = ((hash << 5) - hash + c) | 0; // Matches JavaScript: hash = ((hash << 5) - hash + charCode) | 0
                    }

                    return hash.ToString();
                }
                catch
                {
                    return element.GetHashCode().ToString(); // Fallback to CitrixHashCode
                }
            }

            /// <summary>
            /// Computes a hash of all of <paramref name="element"/>'s attributes (sorted name=value pairs) by
            /// running the same hashing algorithm as <see cref="GetElementAttributeHashCsharp"/> in JavaScript.
            /// </summary>
            /// <param name="element">Element to hash</param>
            /// <param name="browser">Browser the element belongs to</param>
            /// <returns>String hash of the element's attributes, or its GetHashCode() on failure</returns>
            internal static string GetElementAttributeHashJavaScript(GPALElement element, Browser browser)
            {
                try
                {
                    if (false == browser.UseOttoMagic)
                    {
                        string jsScript = @"
                            let attrs = arguments[0].attributes;
                            let attrList = [];
                            for (let attr of attrs) {
                                attrList.push(attr.name + '=' + attr.value);
                            }
                            attrList.sort();    // Sort attributes for consistent hashing
                            let str = attrList.join('|');
                            let hash = 0;
                            for (let i = 0; i < str.length; i++) {
                                hash = ((hash << 5) - hash + str.charCodeAt(i)) | 0;
                            }
                            return hash.toString();
                        ";
                        return (string)BrowserHelper.ExecuteJavaScript(browser, jsScript, element.WebElement) ?? element.GetHashCode().ToString();
                    } else
                    {
                        return browser.OttoMagicClient.GetElementAttributeHash(element.Css).Execute().Replace("\"","");
                    }
                }
                catch
                {
                    return element.GetHashCode().ToString(); // Fallback to GetHashCode
                }
            }
        }
        /// <summary>
        /// click on a select list menu
        /// NOTE: Is modifier keys a thing when clicking a select menu?
        /// CAVEAT: it only applies to clicking on the parent, not the item and only applies to hardware clicks? Why did i code that?
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="selector"></param>
        /// <param name="webElement">option element in select menu</param>
        /// <param name="clickType"></param>
        /// <param name="modifierKeys"></param>

        public static void SelectClick(Browser browser, Selector selector, GPALElement webElement, SelectClickType clickType, ModifierKeys modifierKeys)
        {
            // refactored by grok to add random/sequential selects
            dynamic result = null;  // GPALElement or WebElement
            dynamic parentElem = null;  // GPALElement or WebElement
            SelectElement elem = null;
            string selectedValue = null;
            dynamic options = null;
            string elementId = null;

            // NOTE: since we have select menu options like select a random entry or select next or select previous, we track the select menu internally and track the currently selected item
            // in theory this allows us to track multiple select menus but may be overkill
            if (true == browser.UseOttoMagic)
                result = browser.MagicHelper.GetParentNode(webElement.Css);
            else if (true == browser.UsePuppeteer)
                result = browser.PuppeteerClient.GetParentNode(webElement.ElementHandle).Execute<GPALElement>();
            else // selenium
                result = BrowserHelper.ExecuteJavaScriptObj("return arguments[0].parentNode;", browser, webElement.WebElement);

            if (result != null)
            {
                if (true == browser.UseSelenium)
                {
                    parentElem = result;
                    elementId = parentElem.GetAttribute("id");
                }
                else
                {
                    parentElem = (GPALElement)result;
                    parentElem.Browser = browser;
                    elementId = parentElem.GetAttribute("id");
                }

                try
                {
                    // ScrollIntoView reads a GPALElement; Selenium's parent is a raw WebElement, so wrap it for the scroll
                    if (true == browser.UseSelenium)
                        ScrollIntoView(browser, new GPALElement((IWebElement)parentElem) { Browser = browser });
                    else
                        ScrollIntoView(browser, parentElem);

                    // Get a unique identifier based on attributes
                    if (true == browser.UseSelenium)
                    {
                        elem = new SelectElement(parentElem);

                        // Get all options in the select element
                        if (SelectClickType.LeftClick != clickType)
                        {
                            options = elem.Options;
                            if (options.Count == 0)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No options found in select element.", webElement, GPALObjectType.Other);
                                return;
                            }
                        }

                        // Use C# attribute iteration for non-JavaScript interaction types
                        if (string.IsNullOrEmpty(elementId))
                        {
                            //if (selector.InteractionType != Enums.InteractionType.JavaScript)
                            //    elementId = SelectClickHelper.GetElementAttributeHashCsharp(parentElem, browser);
                            //else
                                elementId = SelectClickHelper.GetElementAttributeHashJavaScript(parentElem, browser);
                        }
                    }
                    else
                    {
                        if (SelectClickType.LeftClick != clickType)
                        {
                            Selector optionsSelector = GPAL.CssSelector($"{parentElem.Css} > option").WithSelectorName("options").ToGPALObject();

                            options = FindWebElementsByCss(browser, optionsSelector.SelectorPaths[0], optionsSelector); // CAVEAT: hardcoded value, but it's our selector

                            if (0 == options.Count)
                            {
                                Click(browser, selector, webElement, ClickType.LeftClick, modifierKeys);
                                return;
                            }
                        }

                        if (string.IsNullOrEmpty(elementId))
                        {
                            if (true == browser.UseOttoMagic)
                                elementId = browser.MagicHelper.GetElementAttributeHash(parentElem.Css);
                            else if (true == browser.UsePuppeteer)
                                elementId = browser.PuppeteerClient.GetElementAttributeHash(parentElem.Css).Execute();
                        }
                    }

                    // Check if we're dealing with a new select element
                    
                    if (SelectClickHelper._lastParentNodeId != elementId &&
                        (clickType == SelectClickType.SelectNext || clickType == SelectClickType.SelectNextWithWrap))
                    {
                        SelectClickHelper._sequentialSelectIndices[elementId] = -1; // Reset index
                    }

                    SelectClickHelper._lastParentNodeId = elementId;

                    if (false == browser.BrowserSettings.UseHeadless && (true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware))
                    {
                        switch (clickType)
                        {
                            case SelectClickType.SelectRandom:
                                Random rand = new Random();
                                int randomIndex = rand.Next(0, options.Count);
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                HardwareClick(browser, parentElem, ClickType.LeftClick, modifierKeys, browser.CurrentUOW.CurrentSelector);
                                HardwareHelper.SendKey(GPAL.VK_HOME);
                                for (int i = 0; i < randomIndex; i++)
                                {
                                    Thread.Sleep(50);
                                    HardwareHelper.SendKey(GPAL.VK_DOWN);
                                }
                                HardwareHelper.SendKey(GPAL.VK_RETURN);
                                break;

                            case SelectClickType.SelectNext:
                            case SelectClickType.SelectNextWithWrap:
                            case SelectClickType.SelectPrevious:
                            case SelectClickType.SelectPreviousWithWrap:

                                // +1 = move down (next), -1 = move up (previous)
                                int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                clickType == SelectClickType.SelectPreviousWithWrap)
                                               ? -1 : 1;

                                bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                   clickType == SelectClickType.SelectPreviousWithWrap);

                                // Initialize to -1 on first use -> first Next -> index 0, first Previous -> last item
                                if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                {
                                    currentIndex = -1;
                                    SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                }

                                int nextIndex;

                                if (shouldWrap)
                                {
                                    // Proper wrapping that works when going backwards from 0
                                    nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                }
                                else
                                {
                                    // No wrap -> clamp to bounds
                                    nextIndex = currentIndex + incOrDec;
                                    if (nextIndex < 0)
                                        nextIndex = 0;
                                    else if (nextIndex >= options.Count)
                                        nextIndex = options.Count - 1;
                                }

                                // === Hardware emulation starts here ===
                                HardwareClick(browser, parentElem, ClickType.LeftClick, modifierKeys,
                                              browser.CurrentUOW.CurrentSelector);

                                // Always start from the top (HOME) — this matches real dropdown behavior
                                HardwareHelper.SendKey(GPAL.VK_HOME);
                                Thread.Sleep(50); // small delay after HOME

                                if (incOrDec > 0)
                                {
                                    // Going forward -> just press DOWN `nextIndex` times
                                    for (int i = 0; i < nextIndex; i++)
                                    {
                                        Thread.Sleep(50);
                                        HardwareHelper.SendKey(GPAL.VK_DOWN);
                                    }
                                }
                                else
                                {
                                    // Going backward -> go to end first, then go up the required steps
                                    // This is the most reliable way when using pure keyboard
                                    HardwareHelper.SendKey(GPAL.VK_END);
                                    Thread.Sleep(50);

                                    int stepsUp = options.Count - 1 - nextIndex; // how many UP arrows needed from bottom
                                    for (int i = 0; i < stepsUp; i++)
                                    {
                                        Thread.Sleep(50);
                                        HardwareHelper.SendKey(GPAL.VK_UP);
                                    }
                                }

                                // Confirm selection
                                HardwareHelper.SendKey(GPAL.VK_RETURN);

                                // Store the new index for next time
                                SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;

                                break;

                            default:
                                HardwareClick(browser, parentElem, ClickType.LeftClick, modifierKeys, browser.CurrentUOW.CurrentSelector);

                                // ReadOnlyCollection<GPALElement> optionElems = ElementHelper.FindWebElements(browser, browser.CurrentUOW, selector, out bool matchedAll, out List<GPALElement> matchedElements);
                                int index = Int32.Parse(webElement.GetAttribute("index"));  // optionElems[0].GetAttribute("index"));
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                HardwareHelper.SendKey(GPAL.VK_HOME);
                                for (int cnt = 0; cnt < index; cnt++)
                                {
                                    Thread.Sleep(50);
                                    HardwareHelper.SendKey(GPAL.VK_DOWN);
                                }
                                HardwareHelper.SendKey(GPAL.VK_RETURN);
                                break;
                        }
                    }
                    else if (true == browser.UseOttoMagic)
                    {
                        switch (clickType)
                        {
                            case SelectClickType.SelectRandom:
                                Random rand = new Random();
                                int randomIndex = rand.Next(0, options.Count);
                                selectedValue = options[randomIndex].GetAttribute("value");
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                browser.MagicHelper.SelectClick(parentElem.Css, selectedValue);
                                break;

                            case SelectClickType.SelectNext:
                            case SelectClickType.SelectNextWithWrap:
                            case SelectClickType.SelectPrevious:
                            case SelectClickType.SelectPreviousWithWrap:

                                // +1 for next, -1 for previous
                                int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                clickType == SelectClickType.SelectPreviousWithWrap)
                                               ? -1 : 1;

                                // true if we should wrap around
                                bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                   clickType == SelectClickType.SelectPreviousWithWrap);

                                // Initialize to -1 on first use (so first "next" goes to 0, first "prev" goes to last)
                                if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                {
                                    currentIndex = -1;
                                    SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                }

                                int nextIndex;

                                if (shouldWrap)
                                {
                                    // Safe modular arithmetic that works with negative numbers
                                    nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                }
                                else
                                {
                                    // Manual clamp: stay within [0, options.Count-1]
                                    nextIndex = currentIndex + incOrDec;
                                    if (nextIndex < 0)
                                        nextIndex = 0;
                                    else if (nextIndex >= options.Count)
                                        nextIndex = options.Count - 1;
                                }

                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;
                                selectedValue = options[nextIndex].GetAttribute("value");
                                browser.MagicHelper.SelectClick(parentElem.Css, selectedValue);

                                break;

                            default:
                                int index = Int32.Parse(webElement.GetAttribute("index"));
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                browser.MagicHelper.SelectClick(parentElem.Css, webElement.Value);
                                break;
                        }
                    }
                    else if (true == browser.UsePuppeteer)
                    {
                        switch (clickType)
                        {
                            case SelectClickType.SelectRandom:
                                Random rand = new Random();
                                int randomIndex = rand.Next(0, options.Count);
                                selectedValue = options[randomIndex].GetAttribute("value");
                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                // note, some weird c# late binding cannot find selectclick in the interface, thus the cast
                                ((PuppeteerClient)browser.PuppeteerClient).SelectClick((dynamic)parentElem.ElementHandle).WithSelectValue(selectedValue).Execute();
                                break;

                            case SelectClickType.SelectNext:
                            case SelectClickType.SelectNextWithWrap:
                            case SelectClickType.SelectPrevious:
                            case SelectClickType.SelectPreviousWithWrap:

                                // +1 for next, -1 for previous
                                int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                clickType == SelectClickType.SelectPreviousWithWrap)
                                               ? -1 : 1;

                                // true if we should wrap around
                                bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                   clickType == SelectClickType.SelectPreviousWithWrap);

                                // Initialize to -1 on first use (so first "next" goes to 0, first "prev" goes to last)
                                if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                {
                                    currentIndex = -1;
                                    SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                }

                                int nextIndex;

                                if (shouldWrap)
                                {
                                    // Safe modular arithmetic that works with negative numbers
                                    nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                }
                                else
                                {
                                    // Manual clamp: stay within [0, options.Count-1]
                                    nextIndex = currentIndex + incOrDec;
                                    if (nextIndex < 0)
                                        nextIndex = 0;
                                    else if (nextIndex >= options.Count)
                                        nextIndex = options.Count - 1;
                                }

                                // Store the new index and perform the selection
                                SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;
                                selectedValue = options[nextIndex].GetAttribute("value");
                                // note, some weird c# late binding cannot find selectclick in the interface, thus the cast
                                ((PuppeteerClient)browser.PuppeteerClient).SelectClick((dynamic)parentElem.ElementHandle).WithSelectValue(selectedValue).Execute();

                                break;

                            default:
                                int index = Int32.Parse(webElement.GetAttribute("index"));
                                SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                // Store the new index and perform the selection
                                // note, some weird c# late binding cannot find selectclick in the interface, thus the cast
                                ((PuppeteerClient)browser.PuppeteerClient).SelectClick((dynamic)parentElem.ElementHandle).WithSelectIndex(index).Execute();
                                break;
                        }
                    }
                    else if (true == browser.UseSelenium)
                    {

                        if (Enums.InteractionType.JavaScript == selector.InteractionType || browser.BrowserSettings.UseJavaScript)  // only selenium can have javascript option
                        {
                            switch (clickType)
                            {
                                case SelectClickType.SelectRandom:
                                    Random rand = new Random();
                                    int randomIndex = rand.Next(0, options.Count);
                                    // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                    selectedValue = options[randomIndex].GetAttribute("value");
                                    BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1];", parentElem, selectedValue);
                                    break;

                                case SelectClickType.SelectNext:
                                case SelectClickType.SelectNextWithWrap:
                                case SelectClickType.SelectPrevious:
                                case SelectClickType.SelectPreviousWithWrap:

                                    // +1 for next, -1 for previous
                                    int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                    clickType == SelectClickType.SelectPreviousWithWrap)
                                                   ? -1 : 1;

                                    // true if we should wrap around
                                    bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                       clickType == SelectClickType.SelectPreviousWithWrap);

                                    // Initialize to -1 on first use (so first "next" goes to 0, first "prev" goes to last)
                                    if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                    {
                                        currentIndex = -1;
                                        SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                    }

                                    int nextIndex;

                                    if (shouldWrap)
                                    {
                                        // Safe modular arithmetic that works with negative numbers
                                        nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                    }
                                    else
                                    {
                                        // Manual clamp: stay within [0, options.Count-1]
                                        nextIndex = currentIndex + incOrDec;
                                        if (nextIndex < 0)
                                            nextIndex = 0;
                                        else if (nextIndex >= options.Count)
                                            nextIndex = options.Count - 1;
                                    }

                                    // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;
                                    selectedValue = options[nextIndex].GetAttribute("value");
                                    BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1];", parentElem, selectedValue);
                                    break;

                                default:
                                    int index = Int32.Parse(webElement.WebElement.GetAttribute("index"));  // optionElems[0].GetAttribute("index"));
                                                                                                // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                    BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1].value", parentElem, webElement.WebElement);
                                    break;
                            }
                        }
                        else    // use native selenium rather than javascript injection
                            switch (clickType)
                            {
                                case SelectClickType.SelectRandom:
                                    Random rand = new Random();
                                    int randomIndex = rand.Next(0, options.Count);
                                    // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = randomIndex;
                                    elem.SelectByIndex(randomIndex);
                                    break;

                                case SelectClickType.SelectNext:
                                case SelectClickType.SelectNextWithWrap:
                                case SelectClickType.SelectPrevious:
                                case SelectClickType.SelectPreviousWithWrap:

                                    // Determine direction: +1 = next, -1 = previous
                                    int incOrDec = (clickType == SelectClickType.SelectPrevious ||
                                                    clickType == SelectClickType.SelectPreviousWithWrap)
                                                   ? -1 : 1;

                                    // Determine if we should wrap around
                                    bool shouldWrap = (clickType == SelectClickType.SelectNextWithWrap ||
                                                       clickType == SelectClickType.SelectPreviousWithWrap);

                                    // Get current index, default to -1 (so first "next" -> 0, first "prev" -> last)
                                    if (!SelectClickHelper._sequentialSelectIndices.TryGetValue(elementId, out int currentIndex))
                                    {
                                        currentIndex = -1;
                                        SelectClickHelper._sequentialSelectIndices[elementId] = currentIndex;
                                    }

                                    int nextIndex;

                                    if (shouldWrap)
                                    {
                                        // Safe wrap-around that works with negative numbers
                                        nextIndex = (currentIndex + incOrDec % options.Count + options.Count) % options.Count;
                                    }
                                    else
                                    {
                                        // No wrap: clamp to valid range
                                        nextIndex = currentIndex + incOrDec;
                                        if (nextIndex < 0)
                                            nextIndex = 0;
                                        else if (nextIndex >= options.Count)
                                            nextIndex = options.Count - 1;
                                    }

                                    // Perform the selection and store the new index
                                    elem.SelectByIndex(nextIndex);
                                    SelectClickHelper._sequentialSelectIndices[elementId] = nextIndex;

                                    break;

                                default:
                                    int index = Int32.Parse(webElement.WebElement.GetAttribute("index"));  // optionElems[0].GetAttribute("index"));
                                                                                                // Store the new index and perform the selection
                                    SelectClickHelper._sequentialSelectIndices[elementId] = index;
                                    elem.SelectByValue(webElement.GetAttribute("value"));
                                    break;
                            }
                    }
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!GPAL.NoFallbackRecoveryActions)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to Select using [{selector.InteractionType}]. Falling back on JavaScript.", webElement, GPALObjectType.Other, ex);
                        BrowserHelper.ExecuteJavaScript(browser, "arguments[0].value = arguments[1].value", true == browser.UseSelenium ? parentElem : ((GPALElement)parentElem).WebElement, webElement.WebElement);
                        SelectClickHelper._sequentialSelectIndices[elementId] = Int32.Parse(true == browser.UseSelenium ? webElement.WebElement.GetAttribute("index") : webElement.GetAttribute("index"));
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to Select using [{selector.InteractionType}].", webElement, GPALObjectType.Other, ex);
                    }
                }
                selectedValue = null != options ? options[SelectClickHelper._sequentialSelectIndices[elementId]].GetAttribute("value") : webElement.GetAttribute("value");
                string selectedText = null != options ? options[SelectClickHelper._sequentialSelectIndices[elementId]].Text : webElement.Text;
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{selector.InteractionType}] selected [{selectedText}] value [{selectedValue}] index [{SelectClickHelper._sequentialSelectIndices[elementId]}]");
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to click Parent Select for Option using [{selector.InteractionType}]. Clicking element [{selector.Name}].", webElement, GPALObjectType.Other);
                webElement.Click(modifierKeys);
            }
        }

        /// <summary>
        /// Scrolls <paramref name="webElement"/> into view and clicks it, choosing hardware, OttoMagic,
        /// JavaScript, Puppeteer or Selenium based on the selector's interaction type and browser settings,
        /// with fallback to JavaScript/Selenium/Puppeteer click on failure.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="selector">Selector that matched the element, used for interaction type and logging</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        /// <param name="trusted">ask the engine for a click the page reads as trusted - used only by StealthLeftClick()</param>
        public static void Click(Browser browser, Selector selector, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys, bool trusted = false)
        {
            bool dontPublish = false;

            // An element from an image match is clicked at the image, not its middle, which on a large element can
            // be far away. Keep the element in resolved to check the point against.
            GPALElement resolved = null;

            if (null != webElement.ImageMatch)
            {
                resolved = webElement;
                webElement = webElement.ImageMatch;
            }

            ScrollIntoView(browser, webElement, selector.InteractionType);


            //if (true == webElement.IsClickable()) // NOTE: no real utility to printing out if elemenent is clickable, we will try anyways, just slows us down a little
            {
                try
                {
                    if (false == browser.BrowserSettings.UseHeadless && false == browser.BrowserSettings.HiddenDesktop
                        && (Enums.InteractionType.Hardware == selector.InteractionType
                            || true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware)
                        )
                    {
                        // Position is current: ScrollIntoView re-reads it after a scroll, and HardwareMoveTo re-reads
                        // Puppeteer's.
                        HardwareClick(browser, webElement, clickType, modifierKeys, selector, true);
                        dontPublish = true;
                    }
                    else if ("GPALElement" == webElement.TagName)
                    {
                        // Image match without hardware: the engine clicks the point. If it resolved to an element,
                        // check the point still hits it (PointIsClear); an ad moved in by the scroll could be on top.
                        Point aim = ToViewportPoint(browser, webElement, selector);

                        // OttoMagic clicks a point through the browser debugger, which Firefox does not give
                        // extensions. So on Firefox it clicks the resolved element; this is the normal path, not a
                        // recovery. Its element middle click opens the link's tab itself, which Firefox does not do
                        // for a scripted click.
                        bool ottoOnFirefox = true == browser.UseOttoMagic && BrowserType.FireFox == browser.BrowserSettings.BrowserType;

                        if (null != resolved && true == ottoOnFirefox)
                            ClickElementItself(browser, resolved, clickType, modifierKeys);
                        else if (null != resolved && false == PointIsClear(browser, resolved, selector, ref aim) && false == GPAL.NoFallbackRecoveryActions)
                            ClickElementItself(browser, resolved, clickType, modifierKeys);
                        else
                            Click(browser, aim, clickType, modifierKeys);

                        // OttoMagic's point click publishes its own made or not made message, so skip the generic
                        // "clicked". Its element click publishes nothing, so publish it there.
                        dontPublish = true == browser.UseOttoMagic && false == (null != resolved && true == ottoOnFirefox);
                    }
                    else if (true == browser.UseOttoMagic)
                    {
                        bool clicked = false;

                        // A script click does not focus a field the way a real click does, so a field gets the
                        // trusted click too.
                        bool field = "input".Equals(webElement.TagName, StringComparison.OrdinalIgnoreCase)
                                  || "textarea".Equals(webElement.TagName, StringComparison.OrdinalIgnoreCase)
                                  || "select".Equals(webElement.TagName, StringComparison.OrdinalIgnoreCase);

                        // A selector that asked for hardware wants a real click at a point, its offset included. The
                        // mouse cannot reach this desktop, so the trusted click stands in for it.
                        bool askedHardware = Enums.InteractionType.Hardware == selector.RequestedInteractionType;

                        // A trusted click is at a viewport point, with no modifier keys.
                        if ((true == trusted || true == field || true == askedHardware) && ClickType.LeftClick == clickType && ModifierKeys.NONE == modifierKeys)
                        {
                            // The point has to be on screen. OttoMagic skips the scroll above for its own clicks, so
                            // scroll as the mouse would, then read the rect.
                            ScrollIntoView(browser, webElement, Enums.InteractionType.Hardware);

                            // OttoMagic keeps an element by a CSS path that can match its siblings too, so it would
                            // measure the first of them. An element found by one of the selector's own XPaths is
                            // measured by that XPath, which names it alone; not in a shadow root, where XPath does not
                            // reach.
                            bool inShadowRoot = null != browser.CurrentUOW.ContextPath
                                && browser.CurrentUOW.ContextPath.Exists(context => ElementType.ShadowRoot == context.elementType);
                            string locator = false == inShadowRoot
                                && false == string.IsNullOrEmpty(webElement.Xpath)
                                && selector.SelectorPaths.Exists(path => SelectorPathType.Xpath == path.SelectorPathType && path.SelectorPath == webElement.Xpath)
                                ? webElement.Xpath
                                : webElement.Css;

                            // Where each iframe on the way down sits in its page. The rect is measured from the element's
                            // own frame and the trusted click is a point in the tab, so these are added, as the hardware
                            // click does.
                            Point frames = Point.Empty;
                            bool inFrame = false;

                            if (null != browser.CurrentUOW.ContextPath)
                                foreach (UnitOfWork.WebElementWithType context in browser.CurrentUOW.ContextPath)
                                    if (ElementType.IFrame == context.elementType)
                                    {
                                        frames.Offset((int)context.gPalElement.Location.X, (int)context.gPalElement.Location.Y);
                                        inFrame = true;
                                    }

                            Rectangle rect = Rectangle.Empty;

                            // The point in the tab now. An iframe's position is from the top of its page, so the page's
                            // scroll comes off, as in the hardware click.
                            Func<Point> aimNow = () =>
                            {
                                rect = browser.MagicHelper.GetBoundingClientRect(locator);

                                Point point = (0 == selector.OffsetX && 0 == selector.OffsetY)
                                    ? new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)
                                    : new Point(rect.X + selector.OffsetX, rect.Y + selector.OffsetY);

                                if (true == inFrame)
                                    point.Offset(frames.X - browser.MagicHelper.WindowPageOffsetX(), frames.Y - browser.MagicHelper.WindowPageOffsetY());

                                return point;
                            };

                            Point aim = aimNow();

                            // Inside an iframe the visibility check above asks the frame, which can hold the element in
                            // view while the frame itself runs off the tab. Check the point against the tab, and scroll
                            // the element into view when it is outside; the browser scrolls the page holding the frame.
                            int tabWidth = browser.MagicHelper.WindowInnerWidth();
                            int tabHeight = browser.MagicHelper.WindowInnerHeight();

                            if (0 > aim.X || 0 > aim.Y || tabWidth <= aim.X || tabHeight <= aim.Y)
                            {
                                browser.MagicHelper.ScrollIntoView(locator);
                                Thread.Sleep(250);  // as ScrollIntoView, so the page has moved before it is measured

                                aim = aimNow();
                            }

                            clicked = 0 < rect.Width && 0 < rect.Height && true == browser.MagicHelper.TrustedLeftClick(aim.X, aim.Y);

                            if (true == clicked)
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[Trusted][{clickType}] on selector [{selector.Name}] at [{aim.X},{aim.Y}], element at [{rect.X},{rect.Y}] size [{rect.Width}x{rect.Height}].", browser, GPALObjectType.Browser);

                            // Only a click asked to be trusted warns; a field falls back to the script click quietly.
                            if (false == clicked && true == trusted)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[Trusted][{clickType}] on selector [{selector.Name}] was not made, falling back to [{clickType}].", browser, GPALObjectType.Browser);
                        }

                        if (false == clicked)
                        {
                            if (modifierKeys != ModifierKeys.NONE)
                                browser.MagicHelper.PressModifierKey(modifierKeys);

                            switch (clickType)
                            {
                                case ClickType.LeftClick:
                                    browser.MagicHelper.LeftClick(webElement.Css);
                                    break;
                                case ClickType.LeftDoubleClick:
                                    browser.MagicHelper.LeftDoubleClick(webElement.Css);
                                    break;
                                case ClickType.MiddleClick:
                                    browser.MagicHelper.MiddleClick(webElement.Css);
                                    break;
                                case ClickType.RightClick:
                                    browser.MagicHelper.RightClick(webElement.Css);
                                    break;
                            }

                            if (modifierKeys != ModifierKeys.NONE)
                                browser.MagicHelper.ReleaseModifierKey(modifierKeys);
                        }
                    }
                    else if (Enums.InteractionType.JavaScript == selector.InteractionType || true == browser.BrowserSettings.UseJavaScript) // selunium & puppeteer javascript override
                    {
                        if (false == JavaScriptClick(browser, webElement, clickType, modifierKeys) && false == GPAL.NoFallbackRecoveryActions)
                        {
                            if (true == browser.UseSelenium)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"JavaScript click failed on selector [{selector.Name}], falling back to selenium click.", browser, GPALObjectType.Browser);
                                SeleniumClick(browser, webElement, clickType, modifierKeys);
                            }
                            else
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"JavaScript click failed on selector [{selector.Name}], falling back to Puppeteer click.", browser, GPALObjectType.Browser);
                                PuppeteerClick(browser, webElement, clickType, modifierKeys);
                                dontPublish = true;
                            }
                        }
                    }
                    else if (true == browser.UsePuppeteer)
                    {
                        PuppeteerClick(browser, webElement, clickType, modifierKeys);
                        dontPublish = true;
                    }
                    else
                        SeleniumClick(browser, webElement, clickType, modifierKeys);

                    if (false == dontPublish)
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][{clickType.ToString()}] clicked with modifiers [{GetEnumNames(modifierKeys)}].", browser, GPALObjectType.Browser);

                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to [{clickType.ToString()}] click element [{webElement.TagName}] for selector [{selector.Name}]", browser, GPALObjectType.Browser, ex);
                }
            }
            //else
            //    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{webElement.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][{clickType.ToString()}] is NOT clickable.", browser, GPALObjectType.Browser);

            Thread.Sleep(500);  // CAVEAT: don't like this, but a click could be navigating and we might send the next request before the page is ready and even idle network or document.ready can fail

        }
        /// <summary>
        /// Clicks <paramref name="webElement"/> via the Puppeteer client using the given click type and modifiers.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click to perform (LeftClick, LeftDoubleClick, MiddleClick, RightClick)</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        public static void PuppeteerClick(Browser browser, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            switch (clickType)
            {
                case ClickType.LeftClick:
                    browser.PuppeteerClient.LeftClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
                case ClickType.LeftDoubleClick:
                    browser.PuppeteerClient.LeftDoubleClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
                case ClickType.MiddleClick:
                    browser.PuppeteerClient.MiddleClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
                case ClickType.RightClick:
                    browser.PuppeteerClient.RightClick(webElement).WithModifiers(modifierKeys).Execute();
                    break;
            }
        }
        /// <summary>
        /// Clicks <paramref name="webElement"/> using Selenium Actions, retrying with a fresh element lookup if
        /// a stale element exception occurs.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click to perform (LeftClick, LeftDoubleClick, MiddleClick, RightClick)</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        public static void SeleniumClick(Browser browser, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            int retries = 2;
            bool retry = false;
            bool clicked = false;   // set once any click lands, Selenium's or the javascript one
            dynamic retryElement = webElement;   // the GPALElement, so a stale one is found again by its own css or xpath
            Actions action = new Actions(browser.BrowserDriver);
            string errorMessage = $"Failed to {clickType.ToString()} element with text [{webElement.Text}]";

            do
            {
                try
                {
                    switch (clickType)
                    {
                        case ClickType.LeftClick:
                            if (ModifierKeys.NONE == modifierKeys)
                            {
                                webElement.iWebElement.Click();
                            }
                            else
                            {
                                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                                    action.KeyDown(OpenQA.Selenium.Keys.LeftAlt).KeyDown(OpenQA.Selenium.Keys.Alt);
                                if (modifierKeys.HasFlag(ModifierKeys.Control))
                                    action.KeyDown(OpenQA.Selenium.Keys.LeftControl).KeyDown(OpenQA.Selenium.Keys.Control);
                                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                                    action.KeyDown(OpenQA.Selenium.Keys.LeftShift).KeyDown(OpenQA.Selenium.Keys.Shift);
                                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                                    action.KeyDown(OpenQA.Selenium.Keys.Meta);

                                action.Click(webElement.iWebElement);

                                if (modifierKeys.HasFlag(ModifierKeys.Alt))
                                    action.KeyUp(OpenQA.Selenium.Keys.LeftAlt).KeyUp(OpenQA.Selenium.Keys.Alt);
                                if (modifierKeys.HasFlag(ModifierKeys.Control))
                                    action.KeyUp(OpenQA.Selenium.Keys.LeftControl).KeyUp(OpenQA.Selenium.Keys.Control);
                                if (modifierKeys.HasFlag(ModifierKeys.Shift))
                                    action.KeyUp(OpenQA.Selenium.Keys.LeftShift).KeyUp(OpenQA.Selenium.Keys.Shift);
                                if (modifierKeys.HasFlag(ModifierKeys.Windows))
                                    action.KeyUp(OpenQA.Selenium.Keys.Meta);

                                ModifierKeys unsupported = modifierKeys & (ModifierKeys.Application | ModifierKeys.ScrollLock);
                                if (ModifierKeys.NONE != unsupported)
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"LeftClick: [{Browser.GetModifierKeys(unsupported)}] is not supported via Selenium/WebDriver Actions and was ignored.", browser, GPALObjectType.Browser);

                                action.Build().Perform();
                            }
                            break;
                        case ClickType.LeftDoubleClick:
                            action.DoubleClick(webElement.iWebElement).Perform();
                            break;
                        case ClickType.MiddleClick:
                        {
                            // A real middle click (button 1), as OttoMagic and Puppeteer send. It fires auxclick, so
                            // pages that handle their own clicks see it.
                            PointerInputDevice mouse = new PointerInputDevice(PointerKind.Mouse, "default mouse");
                            ActionSequence middleClick = new ActionSequence(mouse);

                            middleClick.AddAction(mouse.CreatePointerMove(webElement.iWebElement, 0, 0, TimeSpan.Zero));
                            middleClick.AddAction(mouse.CreatePointerDown(MouseButton.Middle));
                            middleClick.AddAction(mouse.CreatePointerUp(MouseButton.Middle));

                            ((IActionExecutor)browser.BrowserDriver).PerformActions(new List<ActionSequence> { middleClick });

                            if (ModifierKeys.NONE != modifierKeys)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium cannot press [{Browser.GetModifierKeys(modifierKeys)}] with a middle click.", browser, GPALObjectType.Browser);

                            break;
                        }
                        case ClickType.RightClick:
                            action.ContextClick(webElement.iWebElement).Perform();
                            break;
                    }
                    clicked = true;
                    retry = false;
                }
                catch (StaleElementReferenceException sereex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, sereex);
                    retry = ResolveWebElementIssue(browser, browser.CurrentUOW, ref retryElement, sereex, ref retries, errorMessage);
                    retries--;
                }
                catch (ElementClickInterceptedException ecieex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, ecieex);

                    // Something covers the element, so Selenium would hit it again. The javascript click is the
                    // recovery; once it lands the element has been clicked and nothing is retried.
                    if (false == GPAL.NoFallbackRecoveryActions)
                        clicked = JavaScriptClick(browser, webElement, clickType, modifierKeys);

                    retry = false;
                }
                catch (ElementNotInteractableException enieex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, enieex);
                    retry = ResolveWebElementIssue(browser, browser.CurrentUOW, ref retryElement, enieex, ref retries, errorMessage);
                    retries--;
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    retries = 0;
                    retry = false;
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, webElement, GPALObjectType.Other, ex);
                }
            } while (retry && 0 < retries);

            // Whether the retries ran out or recovery said another try would not help, a click that never landed
            // falls back on javascript. A hidden element, which Selenium will not click, still takes one.
            if (false == clicked && false == GPAL.NoFallbackRecoveryActions)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Falling back on javascript click", webElement, GPALObjectType.Other);
                JavaScriptClick(browser, webElement, clickType, modifierKeys);
            }
        }
        // https://stackoverflow.com/questions/809057/how-do-i-programmatically-click-on-an-element-in-javascript
        // comment from above (not yet tested)
        //   set third last parameter of MouseEvent to true (meaning that metaKey (CMD) button was held down when you clicked), this would not open the tab in a background tab
        /// <summary>
        /// Perform a javascript click on an element by injecting javascript
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="webElement">Element to click</param>
        /// <param name="clickType">Type of click, mapped to the click, dblclick, contextmenu or auxclick event</param>
        /// <param name="modifierKeys">Modifier key(s) reported as held on the dispatched event</param>
        public static bool JavaScriptClick(Browser browser, GPALElement webElement, ClickType clickType, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            bool clicked = true;

            string clickString = clickType switch
            {
                ClickType.LeftClick => "click",
                ClickType.LeftDoubleClick => "dblclick",
                ClickType.RightClick => "contextmenu",
                ClickType.MiddleClick => "auxclick",   // This is the real middle-click event
                _ => "click"
            };
            int button = (true == "contextmenu".Equals(clickString) ? 2 : 0);
            bool shiftPressed = 0 != (modifierKeys & ModifierKeys.Shift);
            bool controlPressed = 0 != (modifierKeys & ModifierKeys.Control);
            bool altPressed = 0 != (modifierKeys & ModifierKeys.Alt);

            // Randomize click within 90% of the element's border
            var random = new Random();
            var margin = 0.05f; // 5% margin on each side (90% clickable area)
            var clickAreaWidth = (float)webElement.BoundingRect.Width * (1f - 2 * margin);
            var clickAreaHeight = (float)webElement.BoundingRect.Height * (1f - 2 * margin);
            var x = (float)webElement.BoundingRect.X + (float)webElement.BoundingRect.Width * margin + (float)random.NextDouble() * clickAreaWidth;
            var y = (float)webElement.BoundingRect.Y + (float)webElement.BoundingRect.Height * margin + (float)random.NextDouble() * clickAreaHeight;

            if (true == browser.UseSelenium)
            {
                IWebDriver browserDriver = browser.BrowserDriver;

                string clickScript = $@"
                        var theEvent = document.createEvent(""MouseEvent"");
                        theEvent.initMouseEvent(""{clickString}"", true, true, window, 0, 0, 0, {x}, {y}, {controlPressed.ToString().ToLower()}, {altPressed.ToString().ToLower()}, {shiftPressed.ToString().ToLower()}, false, 0, null);
                        arguments[0].dispatchEvent(theEvent);
                    ";

                try
                {
                    if (2 == button)    // contextmenu will not fire off via javascript, but SHIFT+F10 will but not in headless
                    {
                        JavaScriptFocus(browser, webElement);

                        if (false == browser.BrowserSettings.UseHeadless)
                            HardwareHelper.SendKey(GPAL.VK_F10, ModifierKeys.Shift);
                        else    // must use selenium in headless mode NOTE: Untested, bet this doesn't work... we only get here if this is a straight rightclick (like custom website)
                            new Actions(browserDriver).ContextClick((IWebElement)webElement.WebElement).Perform();
                    }
                    else
                        BrowserHelper.ExecuteJavaScript(browser, clickScript, webElement.WebElement); // click on element
                }
                catch (GPALException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to [{clickString}] element [{webElement.Text}]", webElement, GPALObjectType.Other, ex);
                    return false;
                }
            }
            else if (true == browser.UsePuppeteer)  // we should never be hear for ottomagic which is all javascript
            {
                string sessionId = browser.PuppeteerCommunicator.GetEffectiveSessionId();

                string selector = webElement.Css;

                string clickScript = null;

                clickScript = $@"
                        (() => {{
                            try {{
                                const sel =`{selector.Replace("'", "\\'")}`;

                                const el = document.querySelector(sel) || document.evaluate(sel, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                                if (!el) return false;

                                // clientX and clientY are viewport coordinates, so the scroll offset is not added
                                const rect = el.getBoundingClientRect();
                                const cx = rect.left + rect.width  / 2;
                                const cy = rect.top  + rect.height / 2;

                                const opts = {{ bubbles: true, cancelable: true, clientX: cx, clientY: cy, view: window, ctrlKey: {controlPressed.ToString().ToLower()}, altKey: {altPressed.ToString().ToLower()}, shiftKey: {shiftPressed.ToString().ToLower()} }};

                                // === LEFT CLICK (or double) ===
                                if ({(clickType == ClickType.LeftClick || clickType == ClickType.LeftDoubleClick).ToString().ToLower()}) {{
                                    const count = {(clickType == ClickType.LeftDoubleClick ? 2 : 1)};
                                    for (let i = 0; i < count; i++) {{
                                        el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 0, buttons: 1, detail: i+1}}));
                                        el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 0, buttons: 0, detail: i+1}}));
                                        el.dispatchEvent(new MouseEvent('click',     {{...opts, button: 0, detail: i+1}}));
                                    }}
                                    if (2 === count)
                                        el.dispatchEvent(new MouseEvent('dblclick', {{...opts, button: 0, detail: 2}}));
                                    return true;
                                }}

                                // === MIDDLE CLICK (eBay pop-under) ===
                                if ({(clickType == ClickType.MiddleClick).ToString().ToLower()}) {{
                                    el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 1, buttons: 4}}));
                                    el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 1, buttons: 0}}));
                                    el.dispatchEvent(new MouseEvent('click',     {{...opts, button: 1}}));
                                    return true;
                                }}

                                // === RIGHT CLICK (context menu) ===
                                if ({(clickType == ClickType.RightClick).ToString().ToLower()}) {{
                                    el.dispatchEvent(new MouseEvent('mousedown', {{...opts, button: 2, buttons: 2}}));
                                    el.dispatchEvent(new MouseEvent('mouseup',   {{...opts, button: 2, buttons: 0}}));
                                    el.dispatchEvent(new MouseEvent('contextmenu', {{...opts, button: 2}}));
                                    return true;
                                }}

                                return false;
                            }} catch(e) {{
                                return false;
                            }}
                        }})();
                ";

                dynamic evaluated = browser.PuppeteerCommunicator.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                {
                    expression = clickScript,
                    returnByValue = true
                }, sessionId).GetAwaiter().GetResult();

                // false when the element was not found or the script threw
                clicked = true == "true".Equals(evaluated?.result?.value?.ToString(), StringComparison.OrdinalIgnoreCase);

                //clickScript =
                //    $@"
                //        var theEvent = document.createEvent(""MouseEvent"");
                //        theEvent.initMouseEvent(""{clickString}"", true, true, window, 0, 0, 0, {x}, {y}, {controlPressed.ToString().ToLower()}, {altPressed.ToString().ToLower()}, {shiftPressed.ToString().ToLower()}, false, 0, null);
                //               let el = null;
                //                const sel = `{selector.Replace("'", "\\'")}`;

                //                const el = document.querySelector(sel) || document.evaluate(sel, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
                //                if (!el) return false;
                //                el.dispatchEvent(theEvent);
                //    ";


                //browser.PuppeteerCommunicator.SendCommand<object>(DevToolsMethods.RuntimeEvaluate, new
                //{
                //    expression = clickScript,
                //    returnByValue = true
                //}, sessionId).GetAwaiter().GetResult();

            }

            return clicked;
        }

        // https://www.javascripttutorial.net/dom/css/check-if-an-element-is-visible-in-the-viewport/
        /// <summary>
        /// Re-reads the element's position and size and stores them.<br/>
        /// Location is read when the element is found, so after a scroll a hardware click from it lands off the
        /// window, or is refused though the element is in view.<br/>
        /// getBoundingClientRect is viewport relative, but Location is kept in document coordinates for
        /// <see cref="HardwareMoveTo"/>, so OttoMagic and Puppeteer add the page scroll back; Selenium reads Location
        /// directly. If it cannot be read, the old position stays.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to re-read, updated in place</param>
        internal static void RefreshLocation(Browser browser, GPALElement element)
        {
            try
            {
                Rectangle live = new Rectangle(0, 0, 0, 0);

                if (true == browser.UsePuppeteer && 0 != element.ElementBackendNodeId)
                {
                    // Pass the backend node id, not ElementHandle (the objectId). This endpoint parses an integer,
                    // so an objectId fails and the stale position is kept.
                    live = browser.PuppeteerClient.GetBoundingClientRect(element.ElementBackendNodeId.ToString()).Execute<Rectangle>();

                    // Same as the OttoMagic branch below: add the page scroll for a top level element, not for one
                    // inside an iframe, whose stored position already includes it.
                    bool inPuppeteerFrame = browser.CurrentUOW?.ContextPath?.Any(context => ElementType.IFrame == context.elementType) ?? false;

                    if (0 < live.Width && 0 < live.Height && false == inPuppeteerFrame)
                        live.Offset(browser.PuppeteerClient.WindowPageOffsetX().Execute<int>(),
                                    browser.PuppeteerClient.WindowPageOffsetY().Execute<int>());
                }
                else if (true == browser.UseOttoMagic)
                {
                    live = browser.MagicHelper.GetBoundingClientRect(element.Css);

                    // getBoundingClientRect is relative to the element's own document. Add the page scroll for a
                    // top level element, since HardwareMoveTo subtracts it. Not inside an iframe: the iframe's
                    // stored position already includes it.
                    bool inFrame = browser.CurrentUOW?.ContextPath?.Any(context => ElementType.IFrame == context.elementType) ?? false;

                    if (0 < live.Width && 0 < live.Height && false == inFrame)
                        live.Offset(browser.MagicHelper.WindowPageOffsetX(), browser.MagicHelper.WindowPageOffsetY());
                }
                else if (null != element.WebElement)
                    live = new Rectangle(element.WebElement.Location, element.WebElement.Size);   // selenium reads these live, in document coordinates

                if (0 < live.Width && 0 < live.Height)
                {
                    element.Location = live.Location;
                    element.Size = live.Size;
                    element.BoundingRect = new ClientRectangle(element.Location, element.Size);
                }
                else
                    // The stored position is from when the element was found; after a scroll a hardware click there
                    // hits whatever is there now.
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not re-read where [{element.TagName}][{element.Css}] is, so it is still where it was when it was found", browser, GPALObjectType.Browser);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not re-read where [{element.TagName}] is, using the reading from when it was found", browser, GPALObjectType.Browser, ex);
            }
        }

        /// <summary>
        /// True if a click at this screen point reaches the element or a descendant. The rectangle does not show
        /// what covers it. False if the engine does not answer, as in <see cref="IsVisibleInViewport"/>.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">The element that should receive the click</param>
        /// <param name="screenPoint">The point being considered, in screen coordinates</param>
        /// <returns>True when the element, or something inside it, is what is at that point</returns>
        internal static bool PointHitsElement(Browser browser, GPALElement element, Point screenPoint)
        {
            bool retVal = false;

            try
            {
                // elementFromPoint takes viewport coordinates, so subtract the content area origin.
                Rectangle content = GetWindowRectangle(browser);
                int x = screenPoint.X - content.X;
                int y = screenPoint.Y - content.Y;

                if (true == browser.UsePuppeteer)
                    retVal = browser.BrowserSettings.PuppeteerCommunicator.ElementFromPoint(element, x, y).GetAwaiter().GetResult();
                else if (true == browser.UseOttoMagic)
                    retVal = browser.MagicHelper.ElementFromPoint(element.Css, x, y);
                else
                {
                    var result = BrowserHelper.ExecuteJavaScriptObj(
                        "var elem = arguments[0]; " +
                        "var hit = document.elementFromPoint(arguments[1], arguments[2]); " +
                        "return hit === elem || elem.contains(hit);", browser, element.WebElement, x, y);

                    retVal = null != result && true == Convert.ToBoolean(result);
                }

                //GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Asked what is at viewport [{x}, {y}] (screen [{screenPoint.X}, {screenPoint.Y}], content origin [{content.X}, {content.Y}]) for [{element.TagName}][{element.Css}]: [{retVal}]", browser, GPALObjectType.Browser);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not ask what is at [{screenPoint.X}, {screenPoint.Y}] for [{element.TagName}]", browser, GPALObjectType.Browser, ex);
            }

            return retVal;
        }

        // Pixel insets from the right edge of the visible part, tried before hitTestFractions. An element much wider
        // than the window overflows away from its anchored edge, and its working control sits at that edge. A
        // percentage of a 3782px element misses a button a few dozen pixels wide.
        private static readonly int[] hitTestInsets = { 24, 40, 60, 12 };

        // If no inset hits, try these fractions of the width, so an element anchored differently is still tried
        // across its whole width.
        private static readonly float[] hitTestFractions = { 0.75f, 0.50f, 0.25f, 0.10f };

        /// <summary>
        /// Checks whether <paramref name="element"/>'s bounding rectangle is fully within the current viewport.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to check</param>
        /// <returns>True if the element is fully visible in the viewport</returns>
        public static bool IsVisibleInViewport(Browser browser, GPALElement element)
        {
            bool retval = false;    // False if the check fails; callers use this to decide whether a coordinate click is safe.

            if (true == browser.UsePuppeteer)
                retval = browser.PuppeteerClient.IsVisibleInViewport(element).Execute<bool>();
            else if (true == browser.UseOttoMagic)
                retval = browser.MagicHelper.IsVisibleInViewport(element.Css);
            else
            {
                IWebDriver browserDriver = browser.BrowserDriver;

                var result = BrowserHelper.ExecuteJavaScriptObj(
                    "var elem = arguments[0]; " +
                    "const rect = elem.getBoundingClientRect(); " +
                    "return ( " +
                    "  rect.top >= 0 && " +
                    "  rect.left >= 0 && " +
                    "  rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) && " +
                    "  rect.right <= (window.innerWidth || document.documentElement.clientWidth) " +
                    ");", browser, element.WebElement);

                if (null != result)
                    retval = (bool)result;
            }

            return retval;
        }
        /// <summary>
        /// Checks whether the page is currently scrolled to its bottom.
        /// </summary>
        /// <param name="browser">Browser to check</param>
        /// <param name="element">Unused</param>
        /// <returns>True if the viewport's bottom edge is at or past the document's bottom</returns>
        public static bool IsEndOfPage(Browser browser, GPALElement element)
        {
            bool retval = true;

            if (true == browser.UsePuppeteer)
                retval = browser.PuppeteerClient.IsEndOfPage().Execute<bool>();
            else if (true == browser.UseOttoMagic)
                retval = browser.MagicHelper.IsEndOfPage();
            else
            {
                IWebDriver browserDriver = browser.BrowserDriver;
                // The same check Puppeteer and OttoMagic make. documentElement.scrollHeight, not body's, which is
                // short when html is the scroll container or body has margins, reporting end of page on page one.
                // Scroll positions can be fractional, so the bottom counts within 1px. A page whose html does not
                // scroll is measured on the first element that does.
                var result = BrowserHelper.ExecuteJavaScriptObj(@"
                    if ('hidden' !== getComputedStyle(document.documentElement).overflow)
                        return (window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 1;
                    const scrollable = Array.from(document.querySelectorAll('*')).find(
                        el => el.scrollHeight > el.clientHeight && getComputedStyle(el).overflowY !== 'hidden'
                    ) || document.documentElement;
                    return (scrollable.clientHeight + scrollable.scrollTop) >= scrollable.scrollHeight - 1;", browser, null);

                if (null != result)
                    retval = (bool)result;
            }

            return retval;
        }
        /// <summary>
        /// Checks whether <paramref name="element"/> is in the viewport, rechecking briefly before returning false.
        /// A revealed header slides in with a CSS transition, and one check mid-slide sees it off screen.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to check</param>
        /// <returns>True if the element was visible in the viewport within the settle window</returns>
        private static bool SettledIsVisibleInViewport(Browser browser, GPALElement element)
        {
            bool visible = IsVisibleInViewport(browser, element);
            int checksLeft = VisibilitySettleChecks;

            while (false == visible && 0 < checksLeft--)
            {
                Thread.Sleep(VisibilitySettleMs);
                visible = IsVisibleInViewport(browser, element);
            }

            return visible;
        }
        /// <summary>
        /// Whether <paramref name="elementSize"/> is too big to ever sit inside <paramref name="viewport"/>.
        /// </summary>
        /// <param name="elementSize">Size of the element</param>
        /// <param name="viewport">The browser's content area</param>
        /// <returns>True if the element cannot fit on screen in either direction</returns>
        private static bool IsBiggerThanViewport(Size elementSize, Rectangle viewport)
        {
            return elementSize.Width > viewport.Width || elementSize.Height > viewport.Height;
        }
        /// <summary>
        /// Brings <paramref name="element"/> on screen when <see cref="ScrollIntoView"/> could not: goes to the top,
        /// then pages down a screen at a time until it is in the viewport.<br/>
        /// Needed for fixed or sticky elements, which scrolling never moves; the site shows them at some position,
        /// usually the top. If none works, leaves the page at the top and warns.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element that has to be on-screen for a coordinate click to land</param>
        /// <returns>True if the element ended up visible in the viewport</returns>
        internal static bool ScrollUntilVisible(Browser browser, GPALElement element)
        {
            bool visible = SettledIsVisibleInViewport(browser, element);

            // IsVisibleInViewport wants the whole element inside the viewport, so an element bigger than the
            // window can never answer yes and paging the document looking for one only wastes the time
            if (false == visible && true == IsBiggerThanViewport(element.Size, GetWindowRectangle(browser)))
                visible = true;

            if (false == visible)
            {
                int pagesLeft = MaxScrollPages;

                _ = browser.PageTop;
                visible = SettledIsVisibleInViewport(browser, element);

                // Stop at MaxScrollPages as well as end of page, or a site whose scroll container cannot be
                // measured is paged end to end on every failed click.
                while (false == visible && 0 < pagesLeft-- && false == IsEndOfPage(browser, element))
                {
                    _ = browser.PageDown;
                    visible = SettledIsVisibleInViewport(browser, element);
                }

                if (false == visible)
                {
                    _ = browser.PageTop;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Could not scroll element [{element.TagName}][{element.Css}] into the viewport. Left the page at the top and continuing.", browser, GPALObjectType.Browser);
                }
            }

            return visible;
        }

        /// <summary>
        /// Finds the main window handle of a running browser process by type.
        /// </summary>
        /// <param name="browserType">Chrome, Edge or FireFox</param>
        /// <returns>The browser's main window handle, or IntPtr.Zero if none was found</returns>
        private static IntPtr GetWindowHandle(BrowserType browserType)
        {
            IntPtr hWnd = IntPtr.Zero;
            Process[] processes = null;

            if (BrowserType.Chrome == browserType)
                processes = Process.GetProcessesByName("chrome");
            else if (BrowserType.Edge == browserType)
                processes = Process.GetProcessesByName("msedge");
            else if (BrowserType.FireFox == browserType)
                processes = Process.GetProcessesByName("firefox");

            if (processes.Length > 0)
            {
                foreach (Process proc in processes)
                {
                    if (proc.MainWindowHandle == IntPtr.Zero)
                        continue;
                    else
                    {
                        hWnd = proc.MainWindowHandle;
                        break;
                    }
                }
            }
            return hWnd;
        }

        // Set true by HardwareClick when the element's screen placement was questionable (odd/off-screen coords,
        // so we could not do a real hardware click). HardwareFillInFrom reads it to refuse to type - otherwise a
        // failed click leaves focus wherever it was (e.g. the browser address bar) and keystrokes leak there.
        // ThreadStatic so concurrent browser threads do not clobber each other's last-click state.
        [ThreadStatic] private static bool _lastPlacementQuestionable;

        // set by HardwareMoveTo when it had to clamp the point into the element's visible part, read by the click
        // that follows so the message says where it actually clicked. null when nothing had to be done
        [ThreadStatic] private static string _lastClampedPoint;

        // the browser's content area as HardwareMoveTo last measured it, read by the click that follows it
        [ThreadStatic] private static Rectangle _lastContentArea;

        // Outer window position and size from the last GetWindowRectangle call. GetWindowRectangle returns the
        // content area for clicks, but WithWindowSize sets the outer window, so messages report that, in x, y,
        // width, height order.
        [ThreadStatic] private static Rectangle _lastOuterWindow;

        // One mouse per machine, and SendInput acts on whatever has focus. Topping a window and clicking in
        // it must therefore be atomic across workflow threads, or one thread tops its window between another
        // thread topping and clicking
        private static readonly object hardwareMouse = new object();

        /// <summary>
        /// Clicks <paramref name="element"/> using hardware mouse emulation at its computed screen coordinates,
        /// holding any requested modifier keys. Falls back to a JavaScript/Puppeteer/OttoMagic/Selenium left
        /// click if the computed coordinates look invalid.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element (or pseudo-element from an image match) to click</param>
        /// <param name="clickType">Type of click to perform</param>
        /// <param name="modifierKeys">Modifier key(s) to hold during the click</param>
        /// <param name="selector">Selector providing the offset and simulate-mouse settings</param>
        /// <param name="isClickable">Unused</param>
        public static void HardwareClick(Browser browser, dynamic element, ClickType clickType, ModifierKeys modifierKeys, Selector selector, bool isClickable = false)
        {
            Rectangle rect, iframeRect = new Rectangle(0, 0, 0, 0);
            bool clicked = false;
            _lastPlacementQuestionable = false;
            _lastClampedPoint = null;

            // Held across topping and clicking, which are one operation
            lock (hardwareMouse)
            try
            {
                BrowserHelper.TopBrowser(browser, false);

                if (ModifierKeys.NONE != modifierKeys)
                    HardwareHelper.PressModifierKey(modifierKeys);

                // element location calculations handled in hardwaremoveto, we use the rectangle computed there
                // will perform simulate mouse move
                rect = HardwareMoveTo(browser, element, selector);

                // The point must be inside the content area on every side; a positive-only check lets a
                // below-the-fold element through and the click lands on the taskbar or a window behind. Image
                // matches are already screen positions and skip this. Reuse the content area HardwareMoveTo just
                // measured rather than three more browser calls.
                Rectangle browserRect = _lastContentArea;
                bool insideBrowser = 0 < rect.Width && 0 < rect.Height
                                     && rect.X > browserRect.Left && rect.X < browserRect.Right
                                     && rect.Y > browserRect.Top && rect.Y < browserRect.Bottom;

                if ("GPALElement" == element.TagName || true == insideBrowser)
                {
                    // will do a jump to x,y
                    HardwareHelper.HardwareClick(rect.X, rect.Y, clickType);
                    clicked = true;
                }
                else
                {
                    // The element's screen placement is not trustworthy (off-screen/odd coords) so we could not
                    // perform a real hardware click. Flag it so a following hardware fill-in refuses to type.
                    _lastPlacementQuestionable = true;

                    if (false == GPAL.NoFallbackRecoveryActions)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element [{element.TagName}] for selector [{selector?.Name}] computed to [{rect.X}, {rect.Y}, {rect.Width}, {rect.Height}], outside the browser window [{browserRect.Left}, {browserRect.Top}, {browserRect.Right}, {browserRect.Bottom}]. No hardware click, falling back on javascript click.", browser, GPALObjectType.Browser);

                        if (ModifierKeys.NONE != modifierKeys)
                            HardwareHelper.ReleaseModifierKeys(modifierKeys);

                        if (true == browser.UseOttoMagic)
                            browser.MagicHelper.LeftClick(element.Css);
                        else if (true == browser.UsePuppeteer)
                            browser.PuppeteerClient.LeftClick(element).Execute();
                        else
                            JavaScriptClick(browser, element, clickType, modifierKeys);

                        clicked = true;
                    }
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Unable to get screen coordinates for webelement, continuing.", browser, GPALObjectType.Browser);
                }
            }
            catch
            {
            }
            finally
            {
                // Publish the click result, including the point if HardwareMoveTo adjusted it, so the workflow can
                // set that offset itself.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG,        // null == _lastClampedPoint ? GPALEventType.INFO : GPALEventType.WARNING,
                    $"Element [{element.TagName}] for selector [{selector.Name}] [{selector.InteractionType.ToString()}][{clickType.ToString()}]{(true == clicked ? "" : " [NOT]")} clicked{(null == _lastClampedPoint ? "" : " " + _lastClampedPoint)}.", browser, GPALObjectType.Browser);

                if (ModifierKeys.NONE != modifierKeys)
                    HardwareHelper.ReleaseModifierKeys(modifierKeys);

                // BrowserHelper.TopBrowser(browser, true);    // just move the mouse to a safe area off the browser area that can cause hover issues
            }
        }

        /// <summary>
        /// Drags each element matched by the current unit of work's selectors by the selector's DeltaX/DeltaY,
        /// using OttoMagic, Puppeteer, Selenium Actions, or hardware mouse emulation for image-matched elements.
        /// </summary>
        /// <param name="browser">Browser to act in</param>
        /// <param name="modifierKeys">Unused</param>
        public static void DragAndDrop(IBrowser browser, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            var driver = browser.BrowserDriver;
            Rectangle iframeRect = new Rectangle(0, 0, 0, 0);

            List<GPALElement> matchedElements;

            foreach (Selector selector in ((Browser)browser).CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALElement> elems = ElementHelper.FindWebElements((Browser)browser, ((Browser)browser).CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindWebElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems || 0 == elems.Count)
                {
                    PublishToEventHandler((Browser)browser, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALElement elem in elems)
                {
                    if (true == ((Browser)browser).BrowserSettings.UseHardware || InteractionType.Hardware == selector.InteractionType)
                    {
                        // Hardware drag: press, move the cursor smoothly (wind mouse) to the drop point, release. A
                        // remote desktop session can drop a drag whose cursor jumps. Holds HardwareClick's lock for the
                        // whole drag.
                        lock (hardwareMouse)
                        try
                        {
                            BrowserHelper.TopBrowser((Browser)browser, false);

                            // element location calculations handled in hardwaremoveto (iframe nesting, browser chrome,
                            // random in-element point when no offset), we use the rectangle computed there as the grab point
                            Rectangle rect = HardwareMoveTo((Browser)browser, elem, selector);

                            if ("GPALElement" == elem.TagName || (0 <= rect.Width && 0 <= rect.Height && 0 < rect.X && 0 < rect.Y))
                            {
                                int endX = rect.X + selector.DeltaX;
                                int endY = rect.Y + selector.DeltaY;

                                HardwareHelper.MouseDown(rect.X, rect.Y, ClickType.LeftClick);
                                HardwareHelper.MoveMouse(endX, endY, 4, 4, 0.5);
                                HardwareHelper.MouseUp(endX, endY, ClickType.LeftClick);
                            }
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Odd screen coordinates [{rect.X}, {rect.Y}, {rect.Width}, {rect.Height}] for hardware drag and drop, skipping element.", browser, GPALObjectType.Browser);
                        }
                        finally
                        {
                            // BrowserHelper.TopBrowser((Browser)browser, true);    // just move the mouse to a safe area off the browser area that can cause hover issues
                        }
                    }
                    else if (true == browser.UseOttoMagic)
                    {
                        browser.MagicHelper.DragAndDrop(elem.Css, selector.DeltaX, selector.DeltaY, selector.OffsetX, selector.OffsetY);
                    }
                    else if (true == browser.UsePuppeteer)
                    {
                        browser.PuppeteerClient.DragAndDrop(elem.ElementHandle).WithDeltaX(selector.DeltaX).WithDeltaY(selector.DeltaY).WithOffsetX(selector.OffsetX).WithOffsetY(selector.OffsetY).Execute();
                    }
                    else if (true == browser.UseSelenium)    // real WebElement, selenium has a handle to drag
                    {
                        // Firefox starts a native drag only from real OS input, so Selenium's pointer events never
                        // fire dragstart on an HTML5 draggable. Chrome and Edge do. So JavaScriptDragAndDrop only on
                        // Firefox, only for draggable="true". Mouse-driven dragging (map panning, jQuery UI) works
                        // with Actions everywhere.
                        if (BrowserType.FireFox == ((Browser)browser).BrowserSettings.BrowserType
                            && "true" == elem.WebElement?.GetAttribute("draggable"))
                            JavaScriptDragAndDrop((Browser)browser, elem, selector.DeltaX, selector.DeltaY, selector.OffsetX, selector.OffsetY);
                        else
                        {
                            // Press at the offset from the element's top-left, or its centre with no offset, as
                            // OttoMatic records it and the other engines use it. Then move in ten steps and release, as
                            // the Puppeteer drag does; one move would be sent by ChromeDriver as a single jump, so the
                            // page would see the element go straight to the drop point
                            const int steps = 10;
                            bool fromCentre = 0 == selector.OffsetX && 0 == selector.OffsetY;

                            // the element's place in the viewport and the viewport's size, which WebDriver moves are
                            // measured in
                            IList<object> box = (IList<object>)((IJavaScriptExecutor)driver).ExecuteScript(
                                "const r = arguments[0].getBoundingClientRect(); return [r.left, r.top, r.width, r.height, window.innerWidth, window.innerHeight];",
                                elem.iWebElement);

                            int left = (int)Math.Ceiling(Convert.ToDouble(box[0]));
                            int top = (int)Math.Ceiling(Convert.ToDouble(box[1]));
                            int right = (int)Math.Floor(Convert.ToDouble(box[0]) + Convert.ToDouble(box[2])) - 1;
                            int bottom = (int)Math.Floor(Convert.ToDouble(box[1]) + Convert.ToDouble(box[3])) - 1;
                            int viewWidth = Convert.ToInt32(box[4]);
                            int viewHeight = Convert.ToInt32(box[5]);

                            int startX = (int)(Convert.ToDouble(box[0]) + (true == fromCentre ? Convert.ToDouble(box[2]) / 2 : selector.OffsetX));
                            int startY = (int)(Convert.ToDouble(box[1]) + (true == fromCentre ? Convert.ToDouble(box[3]) / 2 : selector.OffsetY));
                            int askedX = startX;
                            int askedY = startY;

                            // WebDriver cannot move the pointer outside the page. When the drop point would be past an
                            // edge, the grab point moves inside the element, by as little as it takes, so the whole
                            // delta still fits: the start must be on the element and in the page, and so must start
                            // plus delta
                            int lowX = Math.Max(left, Math.Max(0, -selector.DeltaX));
                            int highX = Math.Min(right, Math.Min(viewWidth - 1, viewWidth - 1 - selector.DeltaX));
                            int lowY = Math.Max(top, Math.Max(0, -selector.DeltaY));
                            int highY = Math.Min(bottom, Math.Min(viewHeight - 1, viewHeight - 1 - selector.DeltaY));

                            if (lowX <= highX)
                                startX = Math.Max(lowX, Math.Min(highX, startX));
                            if (lowY <= highY)
                                startY = Math.Max(lowY, Math.Min(highY, startY));

                            if (startX != askedX || startY != askedY)
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"The drag for selector [{selector.Name}] grabs at [{startX}, {startY}] instead of [{askedX}, {askedY}], so its drop point is inside the page", browser, GPALObjectType.Browser);

                            // Only when no grab point on the element leaves room for the whole delta is the drop point
                            // brought back to the edge
                            int endX = Math.Max(0, Math.Min(viewWidth - 1, startX + selector.DeltaX));
                            int endY = Math.Max(0, Math.Min(viewHeight - 1, startY + selector.DeltaY));

                            if (endX != startX + selector.DeltaX || endY != startY + selector.DeltaY)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium cannot move the pointer outside the page, and no point on the element leaves room for the whole drag, so the drag for selector [{selector.Name}] stops at [{endX}, {endY}] instead of [{startX + selector.DeltaX}, {startY + selector.DeltaY}]", browser, GPALObjectType.Browser);

                            PointerInputDevice mouse = new PointerInputDevice(PointerKind.Mouse, "default mouse");
                            ActionSequence drag = new ActionSequence(mouse);

                            drag.AddAction(mouse.CreatePointerMove(CoordinateOrigin.Viewport, startX, startY, TimeSpan.Zero));
                            drag.AddAction(mouse.CreatePointerDown(MouseButton.Left));

                            for (int step = 1; step <= steps; step++)
                                drag.AddAction(mouse.CreatePointerMove(CoordinateOrigin.Viewport,
                                    startX + (endX - startX) * step / steps,
                                    startY + (endY - startY) * step / steps,
                                    TimeSpan.FromMilliseconds(15)));

                            drag.AddAction(mouse.CreatePointerUp(MouseButton.Left));

                            ((IActionExecutor)driver).PerformActions(new List<ActionSequence> { drag });
                        }
                    }

                    if (++rowCount >= ((Browser)browser).CurrentUOW.WithAllThatMatch)
                        break;
                }
            }
        }

        /// <summary>
        /// Performs an HTML5 drag and drop by dispatching the drag event sequence directly, rather than by
        /// moving a mouse. All five events share one DataTransfer, so whatever the page stashes during its own
        /// dragstart handler is still readable by its drop handler. Used for Firefox, whose native drag service
        /// only starts from real OS input and therefore never sees Selenium's synthesized pointer events.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to drag</param>
        /// <param name="deltaX">Horizontal distance, in pixels, to drop from the grab point</param>
        /// <param name="deltaY">Vertical distance, in pixels, to drop from the grab point</param>
        /// <param name="offsetX">Horizontal grab offset, in pixels, from the element's center</param>
        /// <param name="offsetY">Vertical grab offset, in pixels, from the element's center</param>
        /// <returns>True if the sequence was dispatched, false if nothing sat at the drop point</returns>
        public static bool JavaScriptDragAndDrop(Browser browser, GPALElement element, int deltaX, int deltaY, int offsetX, int offsetY)
        {
            // this can happen with a gpalelement
            if (null == element?.WebElement)
                return false;

            ScrollIntoView(browser, element);

            // grab point is the element center plus the offset, matching the puppeteer drag and drop
            string javaScript = @"
                var src = arguments[0];
                var offsetX = arguments[1], offsetY = arguments[2];
                var deltaX = arguments[3], deltaY = arguments[4];

                var rect = src.getBoundingClientRect();
                var startX = rect.left + rect.width / 2 + offsetX;
                var startY = rect.top + rect.height / 2 + offsetY;
                var endX = startX + deltaX;
                var endY = startY + deltaY;

                var target = document.elementFromPoint(endX, endY);

                if (!target)
                    return 'nothing at drop point ' + endX + ',' + endY;

                var dt = new DataTransfer();

                function fire(type, el, x, y) {
                    el.dispatchEvent(new DragEvent(type, {
                        bubbles: true, cancelable: true, composed: true,
                        clientX: x, clientY: y, dataTransfer: dt
                    }));
                }

                fire('dragstart', src, startX, startY);
                fire('dragenter', target, endX, endY);
                fire('dragover', target, endX, endY);
                fire('drop', target, endX, endY);
                fire('dragend', src, endX, endY);

                return 'ok';
            ";

            string result = BrowserHelper.ExecuteJavaScript(browser, javaScript, element.WebElement, offsetX, offsetY, deltaX, deltaY);

            if ("ok" != result?.Trim())
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"HTML5 drag and drop did not complete for selector [{element.Selector?.Name}] [{result}].", browser, GPALObjectType.Browser);
                return false;
            }

            return true;
        }
        /// <summary>
        /// Last-resort hover fallback: scrolls <paramref name="element"/> into view and dispatches a synthetic
        /// "mouseover" event on it via JavaScript. This only fires JS mouseover/mouseenter listeners - it does
        /// NOT update the renderer's CSS <c>:hover</c> state, since no real input event occurred. Used by
        /// <see cref="MoveTo(Browser)"/> only when no engine capable of a real cursor move (Hardware, OttoMagic,
        /// Puppeteer, or a non-headless Selenium session) is available.
        /// </summary>
        /// <param name="browser">Browser the element belongs to</param>
        /// <param name="element">Element to dispatch the mouseover event on</param>
        public static void JavaScriptHover(Browser browser, GPALElement element)
        {
            // this can happen with a gpalelement
            if (null == element.WebElement)
                return;

            ScrollIntoView(browser, element);

            // dispatch a mouseover event on the targetted element which is hover, not focus
            String javaScript = "var evObj = document.createEvent('MouseEvents');"
            + "evObj.initMouseEvent(\"mouseover\",true, false, window, 0, 0, 0, 0, 0, false, false, false, false, 0, null);"
            + "arguments[0].dispatchEvent(evObj);";

            BrowserHelper.ExecuteJavaScript(browser, javaScript, element.WebElement);
        }
        // for some reason we are having trouble getting the msedge bounding rect
        /// <summary>
        /// Finds a top-level window whose title contains <paramref name="windowTitle"/> and returns its
        /// screen rectangle via the Win32 GetWindowRect API.
        /// </summary>
        /// <param name="windowTitle">Substring to match against open window titles</param>
        /// <returns>The window's rectangle, or an empty Rectangle if no matching window was found</returns>
        private static Rectangle GetWindowRect(string windowTitle)
        {
            IntPtr hWnd = IntPtr.Zero;
            foreach (Process pList in Process.GetProcesses())
            {
                if (pList.MainWindowTitle.Contains(windowTitle))
                {
                    hWnd = pList.MainWindowHandle;
                    break;
                }
            }

            if (hWnd != IntPtr.Zero)
            {
                RECT rect;
                GetWindowRect(hWnd, out rect);
                return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            else
            {
                return Rectangle.Empty;
            }
        }

        /// <summary>
        /// Win32 GetWindowRect: retrieves the screen coordinates of a window's bounding rectangle.
        /// </summary>
        /// <param name="hWnd">Handle to the window</param>
        /// <param name="lpRect">Receives the window's bounding rectangle in screen coordinates</param>
        /// <returns>True on success</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
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
        /// As <see cref="GetWindowRectangle(Browser)"/>, also returning the outer window. The outer window
        /// includes the browser chrome; the content area is the origin for page coordinates.
        /// </summary>
        /// <param name="browser">Browser to measure.</param>
        /// <param name="outerWindow">Receives the whole window in screen coordinates.</param>
        /// <returns>The content area in screen coordinates.</returns>
        public static Rectangle GetWindowRectangle(Browser browser, out Rectangle outerWindow)
        {
            Rectangle retVal = GetWindowRectangle(browser);

            // Set by the read above and ThreadStatic, so it belongs to the calling workflow
            outerWindow = _lastOuterWindow;

            return retVal;
        }

        /// <summary>
        /// Computes the screen rectangle of the browser's content (viewport) area, preferring JavaScript/engine
        /// queries for window position and inner/outer dimensions, with a Win32 GetWindowRect fallback.
        /// </summary>
        /// <param name="browser">Browser to get the content rectangle for</param>
        /// <returns>The screen rectangle of the browser's viewport content area</returns>
        public static Rectangle GetWindowRectangle(Browser browser)
        {
            // Primary: Use JS to get screen pos and sizes (reliable, avoids UIA flakiness)
            bool jsSuccess = false;
            int screenX = 0, screenY = 0, outerHeight = 0, innerHeight = 0, outerWidth = 0, innerWidth = 0;

            try
            {
                bool withReturn = !browser.UsePuppeteer;
                string prefix = withReturn ? "return " : "";
                dynamic value = null;

                if (true == browser.UseOttoMagic)
                {
                    Rectangle rect = browser.MagicHelper.GetWindowRectangle();
                    // { y: window.innerHeight, x: window.innerWidth, height: window.outerHeight, width: window.outerWidth }
                    outerHeight = rect.Height;
                    innerHeight = rect.Y;
                    outerWidth = rect.Width;
                    innerWidth = rect.X;
                    screenX = browser.MagicHelper.WindowScreenLeft();
                    screenY = browser.MagicHelper.WindowScreenTop();
                    jsSuccess = true;
                }
                else if (true == browser.UsePuppeteer)  // NOTE: not using javascript for now
                {
                    outerHeight = browser.PuppeteerClient.WindowOuterHeight().Execute<int>();
                    innerHeight = browser.PuppeteerClient.WindowInnerHeight().Execute<int>();
                    outerWidth = browser.PuppeteerClient.WindowOuterWidth().Execute<int>();
                    innerWidth = browser.PuppeteerClient.WindowInnerWidth().Execute<int>();
                    screenX = browser.PuppeteerClient.WindowScreenLeft().Execute<int>();
                    screenY = browser.PuppeteerClient.WindowScreenTop().Execute<int>();
                    jsSuccess = true;
                }
                else
                {
                    string jsScript = $"{prefix} ["
                        + "window.screenLeft, "
                        + "window.screenTop, "
                        + "window.outerHeight, window.innerHeight, window.outerWidth, window.innerWidth]";

                    // Selenium runs script in the current frame, where innerHeight is the frame's, which puts the
                    // content area near the bottom of the screen. Measure in the top document, then restore the
                    // frame.
                    bool inFrame = browser.CurrentUOW?.ContextPath?.Any(context => ElementType.IFrame == context.elementType) ?? false;

                    try
                    {
                        if (true == inFrame)
                            browser.BrowserDriver.SwitchTo().DefaultContent();

                        value = BrowserHelper.ExecuteJavaScriptObj(jsScript, browser, null);
                    }
                    finally
                    {
                        if (true == inFrame)
                            RestoreFrameContext(browser);
                    }
                    IList<object> values;

                    if (value is Newtonsoft.Json.Linq.JArray jArray)
                    {
                        values = jArray.Select(token => (object)token).ToList();
                    }
                    else if (value is IList<object> list)
                    {
                        values = list;
                    }
                    else
                    {
                        // Fallback if unexpected format (e.g., dict); assume defaults
                        values = new List<object> { 0, 0, 0, 0, 0, 0 };
                    }

                    screenX = values.Count > 0 ? Convert.ToInt32(values[0]) : 0;
                    screenY = values.Count > 1 ? Convert.ToInt32(values[1]) : 0;
                    outerHeight = values.Count > 2 ? Convert.ToInt32(values[2]) : 0;
                    innerHeight = values.Count > 3 ? Convert.ToInt32(values[3]) : 0;
                    outerWidth = values.Count > 4 ? Convert.ToInt32(values[4]) : 0;
                    innerWidth = values.Count > 5 ? Convert.ToInt32(values[5]) : 0;
                    jsSuccess = true;
                }

                if (jsSuccess && innerWidth > 0 && innerHeight > 0)
                {
                    _lastOuterWindow = new Rectangle(screenX, screenY, outerWidth, outerHeight);

                    int navHeight = outerHeight - innerHeight;
                    int navWidth = outerWidth - innerWidth;
                    // The border is the same width left, right and bottom: border = (outerWidth - innerWidth) / 2.
                    // The height difference is top chrome plus bottom border, so content top = screenY + height
                    // difference - border. Otherwise every hardware click lands a border width low.
                    int border = navWidth / 2;
                    int contentLeft = screenX + border;
                    int contentTop = screenY + navHeight - border;
                    return new Rectangle(contentLeft, contentTop, innerWidth, innerHeight);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"JS execution failed for browser [{browser.BrowserSettings.BrowserType}]", browser, GPALObjectType.Browser, ex);
            }

            // Reached when every read above returned zero. No fallback measurement is attempted
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to get rectangle for [{browser.BrowserSettings.BrowserType}].", browser, GPALObjectType.Browser);

            return Rectangle.Empty;
        }
        /// <summary>
        /// The browser window's outer rectangle on screen: window.screenLeft/screenTop and outerWidth/outerHeight.
        /// <see cref="GetWindowRectangle"/> returns the content area instead. The outer top-left corner is a safe point
        /// to click to top the window without hitting a tab or the page.
        /// </summary>
        public static Rectangle GetWindowOuterRectangle(Browser browser)
        {
            int screenX = 0, screenY = 0, outerWidth = 0, outerHeight = 0;

            try
            {
                if (true == browser.UseOttoMagic)
                {
                    // { y: innerHeight, x: innerWidth, height: outerHeight, width: outerWidth }
                    Rectangle rect = browser.MagicHelper.GetWindowRectangle();
                    outerWidth = rect.Width;
                    outerHeight = rect.Height;
                    screenX = browser.MagicHelper.WindowScreenLeft();
                    screenY = browser.MagicHelper.WindowScreenTop();
                }
                else if (true == browser.UsePuppeteer)
                {
                    outerWidth = browser.PuppeteerClient.WindowOuterWidth().Execute<int>();
                    outerHeight = browser.PuppeteerClient.WindowOuterHeight().Execute<int>();
                    screenX = browser.PuppeteerClient.WindowScreenLeft().Execute<int>();
                    screenY = browser.PuppeteerClient.WindowScreenTop().Execute<int>();
                }
                else
                {
                    var value = BrowserHelper.ExecuteJavaScriptObj("return [window.screenLeft, window.screenTop, window.outerWidth, window.outerHeight]", browser, null);
                    IList<object> values;

                    if (value is Newtonsoft.Json.Linq.JArray jArray)
                        values = jArray.Select(token => (object)token).ToList();
                    else if (value is IList<object> list)
                        values = list;
                    else
                        values = new List<object> { 0, 0, 0, 0 };

                    screenX = values.Count > 0 ? Convert.ToInt32(values[0]) : 0;
                    screenY = values.Count > 1 ? Convert.ToInt32(values[1]) : 0;
                    outerWidth = values.Count > 2 ? Convert.ToInt32(values[2]) : 0;
                    outerHeight = values.Count > 3 ? Convert.ToInt32(values[3]) : 0;
                }

                return new Rectangle(screenX, screenY, outerWidth, outerHeight);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get outer window rectangle for [{browser.BrowserSettings.BrowserType}]", browser, GPALObjectType.Browser, ex);
                return new Rectangle(0, 0, 0, 0);
            }
        }
        /// <summary>
        /// Using selenium, get all Css Attributes for a given IWebElement
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="webElement"></param>
        /// <returns></returns>
        public static Dictionary<string, object> GetCssAttributes(Browser browser, IWebElement webElement)
        {
            var script = @"
                            const el = arguments[0];
                            const styles = window.getComputedStyle(el);
                            var props = {};

                            // 2. Values that are ""noise"" - we never want them in the result
                            const EMPTY_VALUES = new Set([
                                '', 'none', 'normal', 'inherit', 'initial', 'unset',
                                'auto', '0px', '0', 'transparent'
                            ]);

                            // 3. The only properties we ever care about
                            const RELEVANT = new Set([
                                'display', 'visibility', 'opacity', 'pointer-events', 'overflow', 'clip-path',
                                'position', 'top', 'left', 'right', 'bottom', 'z-index',
                                'width', 'height', 'min-width', 'min-height', 'max-width', 'max-height',
                                'margin', 'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
                                'padding', 'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
                                'border', 'border-width', 'border-style', 'border-color', 'box-sizing',
                                'flex', 'flex-direction', 'flex-wrap', 'flex-grow', 'flex-shrink', 'flex-basis',
                                'justify-content', 'align-items', 'align-self', 'order',
                                'grid', 'grid-template', 'grid-area', 'grid-column', 'grid-row',
                                'transform', 'transform-origin', 'transition', 'animation',
                                'cursor', 'user-select', 'content', 'filter'
                            ]);

                            // 4. Iterate only the properties that exist on the style object
                            for (let i = 0; i < styles.length; i++) {
                                const prop = styles[i];                  // e.g. ""display""
                                if (!RELEVANT.has(prop)) { continue; }   // skip everything else

                                const value = styles.getPropertyValue(prop).trim(); // ""block"", ""0.5"", etc.

                                // Skip empty / default values
                                if (!value || EMPTY_VALUES.has(value)) { continue; }

                                props[prop] = value;
                            }
                            return props;
                        ";

            var dict = BrowserHelper.ExecuteJavaScriptObj(script, browser, webElement) as Dictionary<string, object>;

            return dict?.ToDictionary(k => k.Key, v => v.Value ?? "");
        }
        /// <summary>
        ///  Using selenium, Returns **all** DOM attributes of the IWebElement (including data-*, aria-*, etc.).
        /// The dictionary contains the *raw* attribute values (string or null).
        /// </summary>
        public static Dictionary<string, object> GetDomAttributes(
            Browser browser,
            IWebElement webElement)
        {
            const string script = @"
                                    const cleanText = (str) => (str || '').replace(/\s+/g, ' ').trim();
                                    const el = arguments[0];
                                    const attrs = el.attributes;
                                    const result = {};
                                    for (let i = 0; i < attrs.length; i++) {
                                        const a = attrs[i];
                                        result[a.name] = a.value;   // value is always a string or empty
                                    }
                                    // Also add a few handy built-in properties that are not attributes
                                    result['id']        = el.id;
                                    result['className'] = el.className;
                                    result['tagName']   = el.tagName;
                                    result['innerText'] = cleanText(el.innerText);
                                    result['textContent'] = cleanText(el.textContent);
                                    return result;
                                ";

            var raw = BrowserHelper.ExecuteJavaScriptObj(script, browser, webElement)
                      as Dictionary<string, object>;

            // Normalise null -> empty string (mirrors your CSS method)
            return raw?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? ""
            ) ?? new Dictionary<string, object>();
        }
        /// <summary>
        /// Returns key JavaScript DOM properties of the element (tagName, value, innerHTML, etc.).
        /// Only includes properties that exist and are non-empty (mirrors your logic).
        /// </summary>
        public static Dictionary<string, object> GetDomProperties(
            Browser browser,
            IWebElement webElement)
        {
            const string script = @"
                                    const el = arguments[0];
                                    const props = {};

                                    if (""textContent"" in el) props.textContent = el.textContent;
                                    if (""innerHTML"" in el) props.innerHTML = el.innerHTML;
                                    if (""innerText"" in el) props.innerText = el.innerText;
                                    if (""outerHTML"" in el) props.outerHTML = el.outerHTML;
                                    if (""className"" in el) props.className = el.className;
                                    if (""disabled"" in el) props.disabled = el.disabled;

                                    return props;
                                ";

            var raw = BrowserHelper.ExecuteJavaScriptObj(script, browser, webElement)
                      as Dictionary<string, object>;

            // Normalize null -> "" (consistent with your CSS method)
            return raw?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value ?? ""
            ) ?? new Dictionary<string, object>();
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
            // no fallback
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
                    // Step 2: Scroll element into view
                    ElementHelper.MoveTo(gPalElement, currentUOW.CurrentSelector.OffsetX, currentUOW.CurrentSelector.OffsetY, browser);

                    // Step 3: Wait for element to be clickable (visible and enabled)
                    if (false == (shouldRetry = IsElementClickable(iWebElement)))
                    {
                        // CAVEAT: this uses a heuristic, no guarantees
                        // Step 4: Handle overlays (modals, popups)
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
                    // Step 2: Scroll element into view (replaces JavaScriptMoveTo)
                    ElementHelper.MoveTo(gPalElement, currentUOW.CurrentSelector.OffsetX, currentUOW.CurrentSelector.OffsetY, browser);

                    // Step 3: Wait for element to be clickable (visible and enabled)
                    // Step 4: Adjust for sticky elements (e.g., headers)
                    if (false == IsElementClickable(iWebElement))
                    {
                        browser.BrowserSettings.SuppressJavaScriptFailures = true;
                        browser.ExecuteJavaScriptStr("window.scrollBy({ top: -100, behavior: 'instant' });"); // Scroll up slightly if needed
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
        /// <summary>
        /// Checks whether a Selenium element is both displayed and enabled.
        /// </summary>
        /// <param name="webElement">Element to check</param>
        /// <returns>True if the element is displayed and enabled</returns>
        internal static bool IsElementClickable(IWebElement webElement)
        {
            return webElement.Displayed && webElement.Enabled;
        }
        internal static bool IsElementClickable(GPALElement element)
        {
            return element.Displayed && element.Enabled;
        }
        /// <summary>
        /// Heuristically determines whether <paramref name="elem"/> can be typed into, based on its tag, input
        /// type, CSS cursor, contenteditable attribute, and class names.
        /// </summary>
        /// <param name="elem">Element to check</param>
        /// <returns>True if the element appears editable (defaults to true if the check throws)</returns>
        public static bool IsElementEditable(GPALElement elem)
        {
            try
            {
                // Get the tag name and type
                string tag = elem?.WebElement?.TagName.ToUpperInvariant() ?? elem?.TagName.ToUpperInvariant();
                string type = string.Empty;
                if (tag == "INPUT")
                {
                    type = elem.GetAttribute("type")?.ToLower();
                }

                // Check CSS cursor property
                string cursor = elem?.GetCssValue("cursor")?.ToLower();
                if (cursor == "text")
                    return true;

                // Check tag/type/contentEditable
                bool retval = (tag == "INPUT" &&
                               (type == "text" || type == "email" || type == "password" || type == "search"))
                              || tag == "TEXTAREA"
                              || (tag == "DIV" && elem.GetAttribute("contenteditable")?.ToLower() == "true");

                // Optional: check for editable classes
                if (!retval)
                {
                    string classAttr = elem?.GetAttribute("class") ?? "";
                    if (null != classAttr)
                        retval = classAttr.Split(' ').Contains("Editable");
                }

                return retval;
            }
            catch
            {
                // Fail-forward: if anything throws, consider editable as the developer thinks it is
                return true;
            }
        }
        /// <summary>
        /// Sends a string of characters to the browser using hardware emulation, OttoMagic, Puppeteer, or
        /// JavaScript/Selenium key events on the currently matched elements (or the page body).<br/>
        /// Inter-character pacing is controlled by <see cref="GPAL.TypingDelay"/>: 0 sends the whole string
        /// atomically (per-engine fast path), greater than 0 dispatches character-by-character on every engine,
        /// with a randomized delay between Math.Min(25, TypingDelay) and TypingDelay milliseconds before each
        /// character - not a fixed pause, since a constant gap between every keystroke is itself a detectable
        /// signature.
        /// </summary>
        /// <param name="browser">Browser to send the string to</param>
        /// <param name="textToSend">Text to send</param>
        public static void SendString(Browser browser, string textToSend)
        {
            int delayMs = GPAL.TypingDelay;

            if (true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware)
                HardwareHelper.SendString(textToSend, delayMs);
            else if (true == browser.BrowserSettings.UseOttoMagic)
                browser.MagicHelper.SendString(textToSend, delayMs);
            else if (true == browser.BrowserSettings.UsePuppeteer)
                browser.PuppeteerClient.SendString(textToSend, delayMs);
            else
            {
                foreach (SelectorSettings.SelectorPathEntry selectorPathEntry in browser.CurrentUOW.CurrentSelector?.SelectorPaths)
                    if (null != selectorPathEntry.WebSelectorFoundResults)
                        foreach (GPALElement webElement in selectorPathEntry.WebSelectorFoundResults)
                            if (InteractionType.JavaScript == browser.CurrentUOW.CurrentSelector?.InteractionType || true == GPAL.GPALSettings.UseJavaScript)
                            {
                                if (0 == delayMs)
                                    BrowserHelper.ExecuteJavaScriptObj(@"
                                        function sendStringBlindly(text) {
                                            for (let char of text) {
                                                document.dispatchEvent(new KeyboardEvent('keydown', {{
                                                    key: char,
                                                    bubbles: true
                                                }}));
                                                document.dispatchEvent(new KeyboardEvent('keyup', {{
                                                    key: char,
                                                    bubbles: true
                                                }}));
                                            }
                                        }" +
                                        $"sendStringBlindly({textToSend});", browser);
                                else
                                {
                                    bool first = true;
                                    foreach (char c in textToSend)
                                    {
                                        if (false == first)
                                            Thread.Sleep(HardwareHelper.GetTypingDelay(delayMs));
                                        BrowserHelper.ExecuteJavaScriptObj(@"
                                            var char = arguments[0];
                                            document.dispatchEvent(new KeyboardEvent('keydown', { key: char, bubbles: true }));
                                            document.dispatchEvent(new KeyboardEvent('keyup', { key: char, bubbles: true }));",
                                            browser, c.ToString());
                                        first = false;
                                    }
                                }
                            }
                            else // selenium - use the first element we have found or the body if no selector was set
                            {
                                IWebElement body = browser.BrowserSettings.BrowserDriver.FindElement(By.TagName("body"));
                                // Use Actions to simulate typing each character with key down and key up
                                Actions actions = new Actions(browser.BrowserSettings.BrowserDriver);
                                bool first = true;
                                foreach (char c in textToSend)
                                {
                                    if (false == first && 0 < delayMs)
                                        actions.Pause(TimeSpan.FromMilliseconds(HardwareHelper.GetTypingDelay(delayMs)));
                                    actions
                                        .KeyDown(body, c.ToString())
                                        .KeyUp(body, c.ToString());
                                    first = false;
                                }
                                actions.Build().Perform();
                            }

            }
        }
        /// <summary>
        /// Sends a single virtual keycode using hardware emulation, OttoMagic, Puppeteer, or JavaScript/Selenium,
        /// depending on browser settings. With hardware emulation, VK_RETURN on a non-editable matched element
        /// clicks it instead of sending Enter.
        /// </summary>
        /// <param name="browser">Browser to send the key to</param>
        /// <param name="VKCode">Virtual keycode to send (e.g. GPAL.VK_RETURN)</param>
        public static void SendKey(Browser browser, byte VKCode)
        {
            if (true == browser.BrowserSettings.UseHardware || true == GPAL.GPALSettings.UseHardware)
            {
                if (GPAL.VK_RETURN == VKCode)
                {
                    var matched = browser.CurrentUOW.CurrentSelector?.WebSelectorMatchedResults?.FirstOrDefault();
                    if (false == ElementHelper.IsElementEditable(matched))
                    {
                        matched.Click();
                        return;
                    }
                }

                HardwareHelper.SendKey(VKCode);
            }
            else if (true == browser.BrowserSettings.UseOttoMagic)
                browser.MagicHelper.SendKey(VKCode);
            else if (true == browser.BrowserSettings.UsePuppeteer)
                browser.PuppeteerClient.SendKey(VKCode).Execute();
            else
            {
                {
                    foreach (SelectorSettings.SelectorPathEntry selectorPathEntry in browser.CurrentUOW.CurrentSelector?.SelectorPaths)
                        if (null != selectorPathEntry.WebSelectorFoundResults)
                            foreach (GPALElement webElement in selectorPathEntry.WebSelectorFoundResults)
                                if (InteractionType.JavaScript == browser.CurrentUOW.CurrentSelector?.InteractionType)
                                    ElementHelper.JavaScriptSendKey(browser, webElement, VKCode);
                                else
                                    ElementHelper.SeleniumSendKeys(browser, webElement, VKCode);
                }
            }
        }
        public static string GetEnumNames<T>(T value) where T : struct, Enum
        {
            if (!typeof(T).IsDefined(typeof(FlagsAttribute), false))
                return value.ToString(); // or throw, or return ""

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

