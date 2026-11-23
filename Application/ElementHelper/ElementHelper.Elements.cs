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
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Condition = System.Windows.Automation.Condition;
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    internal partial class ElementHelper
    {
        /// <summary>
        /// Finds AutomationElements matching the given selector, applies match criteria, and optionally invokes the
        /// selector's CallIfFound/CallIfNotFound handlers based on the results.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="currentUOW">The current unit of work driving this search</param>
        /// <param name="selector">The selector describing how to find and match elements</param>
        /// <param name="matchedAll">Set to true if all match criteria for the selector were satisfied</param>
        /// <param name="matchedElements">Receives the list of elements that matched the selector's match criteria</param>
        /// <param name="automationElement">Optional root AutomationElement to search within; defaults to the application's root element</param>
        /// <param name="callIfHandler">Whether to invoke the selector's CallIfFound/CallIfNotFound handlers based on the results</param>
        /// <returns>All elements found for the selector, regardless of whether they matched</returns>
        public static ReadOnlyCollection<GPALAutomationElement> FindElements(Application application, UnitOfWork currentUOW, Selector selector, out bool matchedAll, out List<GPALAutomationElement> matchedElements, AutomationElement automationElement = null, bool callIfHandler = true, bool publish = true)
        {
            ReadOnlyCollection<GPALAutomationElement> retElems = null;
            List<GPALAutomationElement> matchedElems = new List<GPALAutomationElement>();
            ReadOnlyCollection<GPALAutomationElement> allFoundElems = null;
            List<GPALAutomationElement> totalAllFoundElems = new List<GPALAutomationElement>();
            List<GPALAutomationElement> iterateOverTheseElements = null;
            bool tryNextSelector = false;
            string message = string.Empty;
            int retries = 1;
            bool retry = false;

            matchedAll = false;
            matchedElements = new List<GPALAutomationElement>();

            currentUOW.CurrentSelector = selector;
            lastFoundFullMessage = null;

            if (false == lastSelector?.Name.Equals(selector.Name))
            {
                listOfMessages.Clear();
                lastSelector = selector;
            }
            else if (null == lastSelector)
                lastSelector = selector;

            // !!!!!!!!!!!!!!! MUST MATCH THE ENUM ORDER !!!!!!!!!!!!!!!!!!
            // public enum SelectorPathType { NotSet = 0, Css = 1, Image = 2, Text = 4, Value = 8, Xpath = 16, Name = 32, AutomationID = 64, ClassName = 128 };
            List<FindElementsByDelegate> functionList = new List<FindElementsByDelegate> { null /* NotSet=0 */, FindElementsByCSS, FindElementsByImage, FindElementsByText, FindElementsByValue, FindElementsByXPath, FindElementsByName, FindElementsByAutomationID, FindElementsByClassName, FindElementsByPlaceholder };
            // iterate selectors, finding elements
            foreach (SelectorSettings.SelectorPathEntry selectorPathEntry in selector.SelectorPaths)
            {
            tryAgain:   // goto target that retries this selector path; an inner loop here would break the continue to the next selector path
                int textLength = selectorPathEntry.SelectorPath.Length - 1;
                string abbreviatedText = selectorPathEntry.SelectorPath.Substring(0, Math.Min(7, textLength)) + "..." + selectorPathEntry.SelectorPath.Substring(Math.Max(0, textLength - 7));
                if (18 > selectorPathEntry.SelectorPath.Length)
                    abbreviatedText = selectorPathEntry.SelectorPath;

                int idx = GetIndexOfEnum<SelectorPathType>((int)selectorPathEntry.SelectorPathType);
                tryNextSelector = false;
                string errorMessage = $"Error finding elements for selector [{selector.Name}][{abbreviatedText}]";

                try
                {
                    // A finder that throws counts as finding nothing, so the next selector path is tried, as in the
                    // browser version. A finder that returns null, such as an image not on screen, found nothing too.
                    try
                    {
                        allFoundElems = functionList[idx](application, selectorPathEntry, selector.SearchForSelector, automationElement);
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error finding elements for selector [{selector.Name}]. Trying next selector path if defined.", application, GPALObjectType.Application, ex);
                        allFoundElems = null;   // not the elements an earlier path found
                    }

                    allFoundElems ??= new ReadOnlyCollection<GPALAutomationElement>(new List<GPALAutomationElement>());

                    if (0 < (allFoundElems?.Count ?? 0))   // keep what this path found
                        totalAllFoundElems.AddRange(allFoundElems.ToList());

                    #region <Element Matching>
                    // as in the browser version, match functions run only when the path found something
                    if (0 < selector.MatchCriteria.Count && 0 < allFoundElems.Count)
                    {
                        // every found element has to pass the custom criteria and the simple ones, as in the browser version.
                        // Each custom function gets the elements the one before it kept.
                        bool customMatchedAll = true;
                        List<GPALAutomationElement> workingSet = allFoundElems?.ToList() ?? new List<GPALAutomationElement>();

                        foreach (SelectorSettings.MatchCriteriaEntry matchCriteriaEntry in selector.MatchCriteria)
                        {
                            if (MatchType.Custom == matchCriteriaEntry.MatchType)
                            {
                                // match function returns true or false for matchedAll
                                CallIfStatus handled = matchCriteriaEntry.AppMatchingFunction(workingSet, out matchedElems, out matchedAll, selector, matchCriteriaEntry.ExactMatch);
                                customMatchedAll = customMatchedAll && matchedAll;

                                if (CallIfStatus.Terminate == handled)
                                {
                                    string msg = $"Custom match handler [{matchCriteriaEntry.AppMatchingFunction.GetInvocationList()[0].Method.Name}] requested program termination for selector {selector.Name} path [{selectorPathEntry.SelectorPath}].";
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, msg, application, GPALObjectType.Application);
                                    throw new GPALException(msg);
                                }

                                // the next custom function narrows what this one kept
                                workingSet = (matchedElems ?? new List<GPALAutomationElement>()).Distinct().ToList();
                                matchedElements = workingSet;

                                if (CallIfStatus.Handled == handled)   // stop matching
                                    break;
                                else if (CallIfStatus.TryNext == handled)   // custom algorithm didn't like the result set and wants us to try the next selectorpath
                                {
                                    tryNextSelector = true;
                                    break;
                                }
                            }
                        }

                        // as in the browser version, a path the custom function turned down leaves nothing behind for the next path
                        if (true == tryNextSelector)
                        {
                            matchedElements = new List<GPALAutomationElement>();
                            totalAllFoundElems.RemoveAll(found => true == allFoundElems?.Contains(found));
                            continue;
                        }

                        // as in the browser version, simple matching applies on top of custom matching: with a
                        // custom criterion it narrows the custom matches, without one it narrows all found elements
                        if (false == selector.MatchCriteria.Any(entry => MatchType.Custom == entry.MatchType))
                            iterateOverTheseElements = new List<GPALAutomationElement>(allFoundElems);
                        else
                            iterateOverTheseElements = new List<GPALAutomationElement>(matchedElements);

                        // simple matching always runs, since it gives the matched list
                        bool simpleMatchedAll = ApplySimpleMatching(application, selector, iterateOverTheseElements, iterateOverTheseElements, out matchedElems);
                        matchedAll = customMatchedAll && simpleMatchedAll && 0 < matchedElems.Count;
                        matchedElements = matchedElems;     // out return list
                    }
                    else // no match criteria, or nothing found: matched is all found
                    {
                        matchedElems = allFoundElems?.ToList() ?? new List<GPALAutomationElement>();
                        matchedElements = matchedElems;     // out return list

                        // as in the browser version, all matched only when something was found
                        matchedAll = 0 < matchedElems.Count;
                    }
                    #endregion <Element Matching>

                }
                catch (GPALException)   // a custom match handler asked to terminate
                {
                    throw;
                }
                catch (Exception ex)    // matching failed; the search stops here, as in the browser version
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errorMessage, application, GPALObjectType.Application, ex);
                    break;
                }

                // return only WithAllThatMatch row count of those that matched (or from all)
                // matchedElems contains either All elememts or just the matched
                if (int.MaxValue != currentUOW.WithAllThatMatch && 0 < matchedElements?.Count)  // just WithAllThatMatch row count of elements
                {
                    int elemIdx = 0;
                    List<GPALAutomationElement> tmpList = new List<GPALAutomationElement>();

                    // ensure we copy over only as many results as requested, or how many results we have
                    while (elemIdx < Math.Min((int)currentUOW.WithAllThatMatch, (int)matchedElements?.Count))
                        tmpList.Add(matchedElements[elemIdx++]);

                    matchedElements = tmpList;
                }

                selectorPathEntry.AppSelectorFoundResults = allFoundElems?.ToList() ?? new List<GPALAutomationElement>();
                selectorPathEntry.AppSelectorMatchedResults = matchedElems;

                retElems = new ReadOnlyCollection<GPALAutomationElement>(totalAllFoundElems.Distinct().ToList());    // always return everything found

                foreach (GPALAutomationElement element in retElems)
                {
                    element.Application = application;
                    element.Selector = selector;
                }

                foreach (GPALAutomationElement element in matchedElements)
                {
                    element.Application = application;
                    element.Selector = selector;
                }

                if (true == retry && 0 < retries--)
                {
                    retry = false;
                    goto tryAgain;  // retry this selector path
                }

                selector.ElementsFoundAndMatchedCount = matchedElements.Count();

                // one line for the search: what it found, what of that matched when there were criteria,
                // and the selector path that was used
                string matchedPart = string.Empty;

                if (0 < retElems?.Count && (true == selector.AnyMatchCriteria() || Int32.MaxValue != currentUOW.WithAllThatMatch))
                {
                    var matchCount = selector.MatchCriteria.Count > 0 ? selector.MatchCriteria.Count.ToString() : $"WithAllThatMatch({currentUOW.WithAllThatMatch})";

                    if ((true == matchCount.Contains("WithAllThatMatch") && 1 < currentUOW.WithAllThatMatch) || 0 < selector.MatchCriteria.Count)
                        matchedPart = $", [{matchedElements.Count}] matched with [{matchCount}] match criteria";
                }

                message = $"[{retElems?.Count ?? 0}] elements found{matchedPart} for selector [{selector.Name}] using [{selectorPathEntry.SelectorPathType}] [{abbreviatedText}]";
                lastFoundFullMessage = $"[{retElems?.Count ?? 0}] elements found{matchedPart} for selector [{selector.Name}] using [{selectorPathEntry.SelectorPathType}] [{selectorPathEntry.SelectorPath}]";

                // A repeat of the same search is not published again. A WaitFor poll publishes nothing but still counts
                // as seen, for every selector path it tried, so the action's own search after the wait repeats none of it
                if (false == listOfMessages.Contains(message))
                {
                    listOfMessages.Add(message);

                    if (true == publish)
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, message, application, GPALObjectType.Application);
                }

                // CAVEAT: once a path finds elements, the later paths are not tried, even if they would find different elements.
                // For complex evaluations, roll your own matching function, 
                if (true == matchedAll || retElems?.Count > 0) // the first selector path that finds or matches elements ends the search
                    break;

            }   // foreach selectorpath

            // callIfHandler false finds the elements without running the selector's CallIf handlers, as WaitFor's
            // polls do; the action's own search passes the default true
            if (true == callIfHandler)
                CallIfHandlers(application, currentUOW, allFoundElems, matchedElements, selector, matchedAll);

            // When matching failed on the first path, retElems is null. Null-check both lists, as the browser version does.
            selector.ElementsFoundAndMatchedCount = true == selector.AnyMatchCriteria()
                ? (matchedElements?.Count() ?? 0)
                : (retElems?.Count ?? 0);

            return retElems;
        }

        /// <summary>
        /// Applies each non-Custom MatchCriteria entry (Text, Href, Src, Placeholder, Value, optionally Regex and/or
        /// ExactMatch) to narrow (AND) or grow (OR) the set of matched elements, as the browser version does.
        /// </summary>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="selector">Selector whose simple MatchCriteria entries are applied</param>
        /// <param name="matchedElements">Elements matched so far (used as the initial working set if non-empty)</param>
        /// <param name="allFoundElems">All elements found before simple matching (used as the universe)</param>
        /// <param name="matchedElementsOut">Receives the resulting set of matched elements</param>
        /// <param name="AndOrOr">True to AND (narrow) successive criteria, false to OR (union) them</param>
        /// <returns>True if every element in the universe satisfied the simple match criteria</returns>
        private static bool ApplySimpleMatching(
            Application application,
            Selector selector,
            List<GPALAutomationElement> matchedElements,
            List<GPALAutomationElement> allFoundElems,
            out List<GPALAutomationElement> matchedElementsOut,
            bool AndOrOr = true) // true = AND, false = OR
        {
            List<GPALAutomationElement> universe = allFoundElems.ToList();

            // initial working set
            List<GPALAutomationElement> workingSet = 0 < matchedElements.Count
                ? new List<GPALAutomationElement>(matchedElements)
                : new List<GPALAutomationElement>(universe);

            foreach (SelectorSettings.MatchCriteriaEntry matchCriteriaEntry in selector.MatchCriteria)
            {
                if (MatchType.None != matchCriteriaEntry.MatchType && MatchType.Custom != matchCriteriaEntry.MatchType)
                {
                    // AND narrows the working set, OR tests the whole universe
                    List<GPALAutomationElement> source = AndOrOr ? workingSet : universe;
                    List<GPALAutomationElement> matchedThisRound = new List<GPALAutomationElement>();
                    HashSet<GPALAutomationElement> roundSet = new HashSet<GPALAutomationElement>();

                    foreach (GPALAutomationElement element in source)
                    {
                        string elementStringToMatch = null;

                        // Mask out the REGEX 1 bit to get to the actual type
                        switch ((MatchType)((int)matchCriteriaEntry.MatchType & 0xFE))
                        {
                            case MatchType.Text:
                                elementStringToMatch = element.Ae.GetText();
                                break;

                            // Href, Src and Placeholder mean nothing on a desktop control, so match them
                            // against the text.
                            case MatchType.Href:
                            case MatchType.Src:
                            case MatchType.Placeholder:
                                elementStringToMatch = element.Ae.GetText();
                                break;

                            // Use the ValuePattern value on purpose: an exact match has a known length, so
                            // truncation cannot change the result. A control with no ValuePattern falls
                            // back to its text.
                            case MatchType.Value:
                                elementStringToMatch = element.Ae.GetValue() ?? element.Ae.GetText();
                                break;
                        }

                        // an element with no string to match does not match
                        if (false == string.IsNullOrEmpty(elementStringToMatch))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"String to match [{matchCriteriaEntry.StringToMatch}] element text [{elementStringToMatch}]", application, GPALObjectType.Application);

                            bool isMatch = false;

                            if (1 == ((int)MatchType.Regex & (int)matchCriteriaEntry.MatchType))
                            {
                                MatchCollection mc = Regex.Matches(elementStringToMatch, matchCriteriaEntry.StringToMatch);
                                if (0 < mc.Count)
                                    isMatch = true;
                            }
                            else
                            {
                                if (false == matchCriteriaEntry.ExactMatch && elementStringToMatch.Contains(matchCriteriaEntry.StringToMatch))
                                    isMatch = true;
                                else if (true == matchCriteriaEntry.ExactMatch && elementStringToMatch.Equals(matchCriteriaEntry.StringToMatch))
                                    isMatch = true;
                            }

                            if (true == isMatch && true == roundSet.Add(element))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Adding [{element.Name}][{elementStringToMatch}]", application, GPALObjectType.Application);
                                matchedThisRound.Add(element);
                            }
                        }
                    }

                    if (true == AndOrOr)
                    {
                        // AND = narrow
                        workingSet = matchedThisRound;
                    }
                    else
                    {
                        // OR = union
                        HashSet<GPALAutomationElement> unionSet = new HashSet<GPALAutomationElement>(workingSet);
                        foreach (GPALAutomationElement elem in matchedThisRound)
                            if (true == unionSet.Add(elem))
                                workingSet.Add(elem);
                    }
                }
            }

            matchedElementsOut = workingSet;
            return universe.Count == matchedElementsOut.Count;
        }

        // The *Handled fields hold the last return of each level's handlers; the next level runs only when that return is NotHandled.
        /// <summary>
        /// Invokes the selector's CallIfFound or CallIfNotFound handler depending on whether matching elements were found,
        /// publishing informational events about the outcome.
        /// </summary>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="currentUOW">The current unit of work driving this search</param>
        /// <param name="foundElements">All elements found for the selector</param>
        /// <param name="matchedElements">Elements that satisfied the selector's match criteria</param>
        /// <param name="selector">The selector whose handlers should be invoked</param>
        /// <param name="matchedAll">True if all match criteria for the selector were satisfied</param>
        private static void CallIfHandlers(Application application, UnitOfWork currentUOW, ReadOnlyCollection<GPALAutomationElement> foundElements, List<GPALAutomationElement> matchedElements, Selector selector, bool matchedAll)
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
                if (0 < selector.AppCallIfFound?.Count)
                {
                    foreach (Application.CallIfDelegate func in selector.AppCallIfFound)
                    {
                        bool safeActionCalled = application.CurrentUOW.ActionCalled;
                        safeUOW = application.CurrentUOW;
                        application.CurrentUOW.ActionCalled = true; // this will allow the callif method to immediately use application.WaitFor

                        currentMethodName = func.Method.Name;
                        str = $"Application Selector CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Selector CallIfFound [{func.Method.Name}]", application, GPALObjectType.Application);

                        selector.AppCallIfFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;
                        application.CurrentUOW.ActionCalled = safeActionCalled;

                        if (CallIfStatus.NotHandled == selector.AppCallIfFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == selector.AppCallIfFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                        throw new GPALException(str);
                    }
                }

                // selector.AppCallIfFoundHandled - if handled by selector handler, don't call UOW handler
                if (0 == selector.AppCallIfFoundHandled && 0 < currentUOW.AppCallIfFound.Count)
                {
                    foreach (Application.CallIfDelegate func in currentUOW.AppCallIfFound)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Application UOW Selector CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking UOW CallIfFound [{func.Method.Name}]", application, GPALObjectType.Application);

                        currentUOW.AppCallIfFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == currentUOW.AppCallIfFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == currentUOW.AppCallIfFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                        throw new GPALException(str);
                    }
                }

                // currentUOW.AppCallIfFoundHandled - if handled by UOW handler, don't call global handler
                if (CallIfStatus.NotHandled == selector.AppCallIfFoundHandled && 0 == currentUOW.AppCallIfFoundHandled && 0 < GPAL.GPALSettings.AppCallIfFoundList.Count)
                {
                    foreach (Application.CallIfDelegate func in GPAL.GPALSettings.AppCallIfFoundList)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Global CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Global CallIfFound [{func.Method.Name}]", application, GPALObjectType.Application);

                        GPAL.GPALSettings.AppCallIfFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == GPAL.GPALSettings.AppCallIfFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == GPAL.GPALSettings.AppCallIfFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                        throw new GPALException(str);
                    }
                }
            }
            else
            {
                if (0 < selector.AppCallIfNotFound.Count)
                {
                    foreach (Application.CallIfDelegate func in selector.AppCallIfNotFound)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Application Selector CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Selector CallIfNotFound [{func.Method.Name}]", application, GPALObjectType.Application);

                        selector.AppCallIfNotFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == selector.AppCallIfNotFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == selector.AppCallIfNotFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                        throw new GPALException(str);
                    }
                }

                if (0 == selector.AppCallIfNotFoundHandled && 0 < currentUOW.AppCallIfNotFound.Count)
                {
                    foreach (Application.CallIfDelegate func in currentUOW.AppCallIfNotFound)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Application UOW Selector CallIfNotFound handler [{currentMethodName}] requested program termination on selector ]{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking UOW CallIfNotFound [{func.Method.Name}]", application, GPALObjectType.Application);

                        currentUOW.AppCallIfNotFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == currentUOW.AppCallIfNotFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == currentUOW.AppCallIfNotFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                        throw new GPALException(str);
                    }
                }

                // currentUOW.AppCallIfNotFoundHandled - if handled by UOW handler, don't call global handler
                if (0 == selector.AppCallIfNotFoundHandled && 0 == currentUOW.AppCallIfNotFoundHandled && 0 < GPAL.GPALSettings.AppCallIfNotFoundList.Count)
                {
                    foreach (Application.CallIfDelegate func in GPAL.GPALSettings.AppCallIfNotFoundList)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Global CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Invoking Global CallIfNotFound [{func.Method.Name}]", application, GPALObjectType.Application);

                        GPAL.GPALSettings.AppCallIfNotFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == GPAL.GPALSettings.AppCallIfNotFoundHandled)
                            continue;
                        else
                            break;
                    }

                    if (CallIfStatus.Terminate == GPAL.GPALSettings.AppCallIfNotFoundHandled)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                        throw new GPALException(str);
                    }
                }

                if (true == selector.StopOnNotFound || true == GPAL.StopOnNotFound)
                {
                    str = $@"{(true == selector.StopOnNotFound ? "selector." : "GPAL.")}StopOnNotFound set, terminating on Not Found element for [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Application);
                    throw new GPALException(str);
                }
            }
        }
        /// <summary>
        /// Finds an element by locating an image (loaded from <see cref="SelectorPathEntry.SelectorPath"/>) on screen
        /// and wrapping its bounds in a pseudo <see cref="GPALAutomationElement"/>.
        /// </summary>
        /// <param name="application">The Application being searched</param>
        /// <param name="selectorPath">The selector path entry specifying the image file to locate</param>
        /// <param name="searchForSelector">When the image is not on screen, page down through the application's scrolling view looking for it</param>
        /// <param name="element">Currently unused</param>
        /// <returns>A collection containing one pseudo element for the matched image, or null if not found</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByImage(Application application, SelectorPathEntry selectorPath, bool searchForSelector = true, AutomationElement element = null)
        {
            ReadOnlyCollection<GPALAutomationElement> elems = null;

            Rectangle foundImageBounds = new Rectangle();
            List<GPALAutomationElement> retElement = new List<GPALAutomationElement>(); ;

            if (null == selectorPath.Image)
            {
                try
                {
                    selectorPath.Image = Image.FromFile(selectorPath.SelectorPath);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to load image for [{selectorPath.SelectorPath}]", application, GPALObjectType.Application, ex);
                    return null;
                }
            }

            Image template = selectorPath.Image;

            // The screen capture is the visible desktop, so a hidden one can only be matched while it is peeked at.
            if (true == application.applicationSettings.HiddenDesktop && false == Desktops.IsShowing)
            {
                if (false == application.applicationSettings.HiddenDesktopImageReported)
                {
                    application.applicationSettings.HiddenDesktopImageReported = true;
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Image matching reads the screen, and [{application.applicationSettings.HiddenDesktopName}] is not on it, so [{selectorPath.SelectorPath}] was not looked for. Peek at the desktop to match there.", application, GPALObjectType.Application);
                }

                return null;
            }

            foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, application); // first check whether the image is already on screen

            // Not on screen: start at the top of the scrolling view and page down until the image appears or the view
            // ends. With no image matcher registered, do not page through the view.
            if (true == searchForSelector && true == foundImageBounds.IsEmpty && null != GPAL.ImageMatcher)
            {
                ScrollPattern view = application.ScrollableView();

                if (null != view)
                {
                    // Page up to the top. SetScrollPercent animates one pixel at a time on some controls.
                    double was = -1;

                    while (0 < view.Current.VerticalScrollPercent && was != view.Current.VerticalScrollPercent)
                    {
                        was = view.Current.VerticalScrollPercent;
                        view.ScrollVertical(ScrollAmount.LargeDecrement);
                    }

                    Thread.Sleep(250);
                    foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, application);

                    // A page step that does not move the view counts as the end, so the search cannot run forever.
                    was = -1;

                    while (true == foundImageBounds.IsEmpty && 100 > Math.Round(view.Current.VerticalScrollPercent) && was != view.Current.VerticalScrollPercent)
                    {
                        was = view.Current.VerticalScrollPercent;
                        view.ScrollVertical(ScrollAmount.LargeIncrement);
                        Thread.Sleep(250);
                        foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, application);
                    }
                }
            }

            if (false == foundImageBounds.IsEmpty)
            {
                // create a psuedo automationelement to use in the subsequent methods
                retElement.Add(new GPALAutomationElement(null, new GPALElement(foundImageBounds.Location, foundImageBounds.Size, selectorPath.SelectorPathType.ToString(), null, "GPALElement")));
                elems = new ReadOnlyCollection<GPALAutomationElement>(retElement);

                // Unless the workflow asked for hardware, return the control under the image match so the next action
                // uses UIAutomation. Skip this on a hidden desktop; the match was made against the visible screen.
                Selector selector = application.CurrentUOW?.CurrentSelector;

                if (false == application.applicationSettings.HiddenDesktop
                    && false == application.applicationSettings.UseHardware
                    && Enums.InteractionType.Hardware != selector?.InteractionType)
                {
                    GPALAutomationElement under = ControlUnderImage(application, foundImageBounds, selector);

                    if (null != under)
                    {
                        under.ImageMatch = retElement[0];
                        elems = new ReadOnlyCollection<GPALAutomationElement>(new List<GPALAutomationElement> { under });
                    }
                }
            }

            return elems;
        }
        /// <summary>
        /// Returns the control under an image match: the element at the click point, or its nearest ancestor that can
        /// be acted on (offers Invoke, Toggle, Value, SelectionItem or ExpandCollapse, or is a button, menu item, check
        /// box, radio button, edit, hyperlink, combo box, list item, tab item, tree item, split button or data item).
        /// If nothing up to the top-level window qualifies, the element at the point is returned.<br/>
        /// Its window and process are not checked; it may be a UWP control, a dropped menu or a separate dialog.<br/>
        /// A click, hover or drag still goes to the matched point, stored in
        /// <see cref="GPALAutomationElement.ImageMatch"/>.
        /// </summary>
        /// <param name="application">The application the match was made in</param>
        /// <param name="match">The image match, in screen coordinates</param>
        /// <param name="selector">The selector that matched, for its offsets</param>
        /// <returns>The control, or null when there is none and only the point is left</returns>
        private static GPALAutomationElement ControlUnderImage(Application application, Rectangle match, Selector selector)
        {
            GPALAutomationElement retVal = null;

            // Both offsets zero: use the middle of the match, as the clicks do.
            int aimX = match.X + ((0 == (selector?.OffsetX ?? 0) && 0 == (selector?.OffsetY ?? 0)) ? match.Width / 2 : selector.OffsetX);
            int aimY = match.Y + ((0 == (selector?.OffsetX ?? 0) && 0 == (selector?.OffsetY ?? 0)) ? match.Height / 2 : selector.OffsetY);

            try
            {
                AutomationElement ae = AutomationElement.FromPoint(new System.Windows.Point(aimX, aimY));

                if (null != ae)
                {
                    // walked in the raw view, since WinForms hides whole controls from the control view. stops at a
                    // top level window, whose parent is the desktop
                    AutomationElement step = ae;
                    AutomationElement actionable = null;

                    while (null == actionable && null != step && false == Automation.Compare(step, AutomationElement.RootElement))
                    {
                        if (true == IsActionable(step))
                            actionable = step;
                        else
                            step = TreeWalker.RawViewWalker.GetParent(step);
                    }

                    retVal = new GPALAutomationElement(actionable ?? ae);
                }
            }
            catch (Exception ex)
            {
                // If the control disappears while being read, publish at DEBUG and return null; only the matched point
                // is used.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not read the control under the image match for [{selector?.Name}]", application, GPALObjectType.Application, ex);
            }

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, null != retVal
                ? $"Image match for [{selector?.Name}] is control [{retVal.Ae.Current.ControlType.ProgrammaticName}][{retVal.Ae.Current.Name}], clicked at the matched point"
                : $"Image match for [{selector?.Name}] has no control of its own under it, so only the point is used", application, GPALObjectType.Application);

            return retVal;
        }
        /// <summary>
        /// True when the control offers an action pattern or is a control type that is clicked or typed into.
        /// </summary>
        /// <param name="ae">The control to check</param>
        /// <returns>True when the control can be acted on</returns>
        private static bool IsActionable(AutomationElement ae)
        {
            ControlType type = ae.Current.ControlType;

            return true == (bool)ae.GetCurrentPropertyValue(AutomationElement.IsInvokePatternAvailableProperty)
                || true == (bool)ae.GetCurrentPropertyValue(AutomationElement.IsTogglePatternAvailableProperty)
                || true == (bool)ae.GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty)
                || true == (bool)ae.GetCurrentPropertyValue(AutomationElement.IsSelectionItemPatternAvailableProperty)
                || true == (bool)ae.GetCurrentPropertyValue(AutomationElement.IsExpandCollapsePatternAvailableProperty)
                || ControlType.Button == type || ControlType.MenuItem == type || ControlType.CheckBox == type
                || ControlType.RadioButton == type || ControlType.Edit == type || ControlType.Hyperlink == type
                || ControlType.ComboBox == type || ControlType.ListItem == type || ControlType.TabItem == type
                || ControlType.TreeItem == type || ControlType.SplitButton == type || ControlType.DataItem == type;
        }
        /// <summary>
        /// An application selector is XPath only, so a path set as CSS, such as a selector string read as CSS
        /// (a bare Edit or Window), is searched as an application XPath.
        /// </summary>
        /// <param name="application">The Application being searched</param>
        /// <param name="selectorPath">The selector path entry, searched as XPath</param>
        /// <param name="searchForElement">Passed to the XPath search</param>
        /// <param name="element">The element to search under, or null for the application's starting points</param>
        /// <returns>The elements the XPath search found</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByCSS(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            return FindElementsByXPath(application, selectorPath, searchForElement, element);
        }
        /// <summary>
        /// Placeholders are not something UIAutomation reports for desktop controls; publishes a warning and returns null.
        /// </summary>
        /// <param name="application">The Application being searched</param>
        /// <param name="selectorPath">The selector path entry (unused)</param>
        /// <param name="searchForElement">Unused</param>
        /// <param name="element">Unused</param>
        /// <returns>Always null</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByPlaceholder(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Find elements by placeholder is not supported for applications.", application, GPALObjectType.Application);
            return null;
        }

        /// <summary>
        /// Whether a selector path names a top level element rather than something inside the application's
        /// own window, which is any path starting at Desktop whose next step is not Window.
        /// </summary>
        /// <param name="selectorPath">The path to read</param>
        /// <returns>True when the walk should start at the desktop</returns>
        private static bool RootedAtDesktop(string selectorPath)
        {
            string[] tokens = (selectorPath ?? string.Empty).Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            return 1 < tokens.Length
                && "desktop" == tokens[0].ToLowerInvariant()
                && false == tokens[1].ToLowerInvariant().StartsWith("window");
        }

        /// <summary>
        /// Where a selector path starts searching, in the order to try.<br/>
        /// A path starts at the application's window, where /Desktop/Window/ matches that window. A path beginning
        /// /Desktop/ followed by anything but Window starts at the desktop instead, because popup menus, dropdown
        /// lists and dialogs are top level windows of their own, outside the application's window.<br/>
        /// On a hidden desktop, UIAutomation's desktop root is the visible desktop, so the search tries the
        /// application's window first, then every other window on the hidden desktop.
        /// </summary>
        /// <param name="application">The application being searched</param>
        /// <param name="selectorPath">The path about to be walked</param>
        /// <returns>The elements to try, in order</returns>
        private static List<AutomationElement> StartingPoints(Application application, string selectorPath)
        {
            List<AutomationElement> retVal = new List<AutomationElement>();

            if (false == RootedAtDesktop(selectorPath))
                retVal.Add(application.RootAutomationElement);
            else if (false == application.applicationSettings.HiddenDesktop)
                retVal.Add(AutomationElement.RootElement);
            else
            {
                // Try the application's own window first; most paths are inside it.
                retVal.Add(application.RootAutomationElement);

                foreach (IntPtr window in Desktops.WindowsOn(application.applicationSettings.HiddenDesktopName))
                {
                    AutomationElement onDesktop = AutomationElement.FromHandle(window);

                    if (null != onDesktop && false == onDesktop.Equals(application.RootAutomationElement))
                        retVal.Add(onDesktop);
                }
            }

            retVal.RemoveAll(each => null == each);

            return retVal;
        }

        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByXPath(
            Application application,
            SelectorPathEntry selectorPath,
            bool searchForElement = true,
            AutomationElement element = null)
        {
            if (string.IsNullOrWhiteSpace(selectorPath?.SelectorPath))
                return new ReadOnlyCollection<GPALAutomationElement>(Array.Empty<GPALAutomationElement>());

            List<AutomationElement> roots = null != element
                ? new List<AutomationElement> { element }
                : StartingPoints(application, selectorPath.SelectorPath);

            AutomationElement current = 0 < roots.Count ? roots[0] : null;

            if (current == null)
                return new ReadOnlyCollection<GPALAutomationElement>(Array.Empty<GPALAutomationElement>());

            string[] tokens = selectorPath.SelectorPath.Split('/');

            // the last step of a path is the element being asked for, not a step on the way to it
            int lastToken = Array.FindLastIndex(tokens, t => false == string.IsNullOrWhiteSpace(t));

            WalkFailure failure = new WalkFailure();
            List<AutomationElement> found = new List<AutomationElement>();

            foreach (AutomationElement root in roots)
            {
                found = WalkFrom(application, root, tokens, 0, lastToken, true, failure);

                if (0 < found.Count)
                    break;
            }

            // If a menu will not open, read the window's Win32 menu instead. On a desktop not receiving input this is
            // the only way.
            GPALAutomationElement throughTheMenu = 0 == found.Count ? MenuItemFor(application, selectorPath.SelectorPath) : null;

            if (0 == found.Count && null == throughTheMenu)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to locate element for Selector [{selectorPath.SelectorPath}], stopped at token [{failure.Token}]. {WhatIsThere(failure.Under ?? current)}", application, GPALObjectType.Application);

            List<GPALAutomationElement> retVal = new List<GPALAutomationElement>();

            foreach (AutomationElement each in found)
                retVal.Add(new GPALAutomationElement(each));

            if (null != throughTheMenu)
                retVal.Add(throughTheMenu);

            return new ReadOnlyCollection<GPALAutomationElement>(retVal);
        }

        /// <summary>
        /// The deepest token a path walk reached and the element it was searching under. With several branches, the one
        /// that got furthest is reported.
        /// </summary>
        private class WalkFailure
        {
            internal int Depth = -1;
            internal string Token;
            internal AutomationElement Under;
        }

        /// <summary>
        /// Walks the remaining path tokens, trying every child that matches a token, not just the first.
        /// </summary>
        /// <remarks>
        /// A dead end backs up and tries the next match of the same step. Every match of the last step is returned,
        /// from the first branch that reaches it.
        /// </remarks>
        /// <param name="application">The Application being searched</param>
        /// <param name="current">The element the walk has reached</param>
        /// <param name="tokens">Every step of the path</param>
        /// <param name="index">The step to match next</param>
        /// <param name="lastToken">Index of the step that names the element being asked for</param>
        /// <param name="firstToken">True while no step has matched yet, when the current element may be the match itself</param>
        /// <param name="failure">Carries the deepest step reached, for the message when nothing is found</param>
        /// <returns>Every element the path names; empty when no branch reaches the end</returns>
        private static List<AutomationElement> WalkFrom(Application application, AutomationElement current, string[] tokens, int index, int lastToken, bool firstToken, WalkFailure failure)
        {
            List<AutomationElement> result = new List<AutomationElement>();

            // Skip an empty step (leading slash) and Desktop; they have nothing to match.
            int next = index;
            bool descendant = false;
            Condition condition = null;

            while (next < tokens.Length && (true == string.IsNullOrWhiteSpace(tokens[next]) || "desktop" == tokens[next].Trim().ToLowerInvariant()))
            {
                // An empty step after another step is a descendant axis, as in Window//Button: search the next step
                // anywhere below. The leading-slash empty step is the first one.
                if (true == string.IsNullOrWhiteSpace(tokens[next]) && 0 < next)
                    descendant = true;

                next++;
            }

            // A step with no condition asks for something no element has, so it matches nothing
            if (next < tokens.Length)
                condition = ConditionFor(tokens[next]);

            // On a hidden desktop UIAutomation reports top level windows as Pane, so a Window step there also matches a
            // top level window on the step's other attributes. Null when that does not apply.
            Condition hiddenWindow = true == application.applicationSettings.HiddenDesktop ? WithoutWindowType(condition) : null;

            if (next >= tokens.Length)
                result.Add(current);
            else
            {
                List<AutomationElement> candidates = new List<AutomationElement>();

                // RootAutomationElement is the window itself, where the recorder roots its paths, so match the first
                // step against it before its children.
                if (true == firstToken && null != condition)
                {
                    AutomationElement self = current.FindFirst(TreeScope.Element, condition);

                    if (null == self && null != hiddenWindow && true == IsTopLevelOnHiddenDesktop(application, current) && true == Matches(current, hiddenWindow))
                        self = current;

                    if (null != self)
                        candidates.Add(self);
                }

                if (0 == candidates.Count && null != condition)
                    foreach (AutomationElement child in current.FindAll(true == descendant ? TreeScope.Descendants : TreeScope.Children, condition))
                        candidates.Add(child);

                // On the visible desktop a window the current window owns, such as a dialog, is one of its children. On a
                // hidden desktop UIAutomation leaves it out, so the windows it owns are matched here.
                if (0 == candidates.Count && null != hiddenWindow)
                    candidates = OwnedWindows(application, current, hiddenWindow, descendant);

                // If that finds nothing, try the raw view. WinForms leaves whole controls out of the control view (a
                // spinner on a tab page shows only its two buttons). Raw view second, so paths that resolve in the
                // control view still walk it.
                if (0 == candidates.Count && null != condition)
                    candidates = RawMatches(current, condition, descendant);

                // Pane[2] names only the second match; a position past the last match fails the step.
                int position = IndexFor(tokens[next]);

                if (0 < position)
                    candidates = position <= candidates.Count
                        ? new List<AutomationElement> { candidates[position - 1] }
                        : new List<AutomationElement>();

                if (0 == candidates.Count && next > failure.Depth)
                {
                    failure.Depth = next;
                    failure.Token = tokens[next];
                    failure.Under = current;
                }

                foreach (AutomationElement candidate in candidates)
                {
                    // Expand a menu item to reach its children, but never the target item: expanding can fall through
                    // to InvokePattern and click it.
                    if (candidate.Current.ControlType == ControlType.MenuItem && next < lastToken)
                        ExpandMenuItem(application, candidate);

                    List<AutomationElement> reached = WalkFrom(application, candidate, tokens, next + 1, lastToken, false, failure);

                    if (0 == reached.Count)
                        continue;

                    result.AddRange(reached);

                    // Before the last step, one branch is enough. Keep every match of the last step.
                    if (next < lastToken)
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// The rest of a step that asks for a Window, for matching a top level window on a hidden desktop, where
        /// UIAutomation reports it as a Pane.
        /// </summary>
        /// <param name="condition">What the step is asking for</param>
        /// <returns>The step's other attributes, TrueCondition when it has none; null when the step does not ask for a
        /// Window</returns>
        private static Condition WithoutWindowType(Condition condition)
        {
            Condition result = null;

            if (true == IsWindowType(condition))
                result = Condition.TrueCondition;
            else if (condition is AndCondition all && true == all.GetConditions().Any(IsWindowType))
            {
                Condition[] rest = all.GetConditions().Where(each => false == IsWindowType(each)).ToArray();
                result = 1 == rest.Length ? rest[0] : new AndCondition(rest);
            }

            return result;
        }

        /// <summary>
        /// Whether a condition is the Window control type.
        /// </summary>
        /// <param name="condition">The condition to test</param>
        /// <returns>True for the Window control type condition</returns>
        private static bool IsWindowType(Condition condition)
        {
            // A ControlType condition holds the type's id
            return condition is PropertyCondition property
                && AutomationElement.ControlTypeProperty == property.Property
                && property.Value is int id
                && ControlType.Window.Id == id;
        }

        /// <summary>
        /// The windows on the application's hidden desktop that an element's window owns, such as its dialogs, that
        /// match a step.
        /// </summary>
        /// <param name="application">The application on the hidden desktop</param>
        /// <param name="owner">The element whose windows are wanted</param>
        /// <param name="condition">What the step is asking for, without the Window type</param>
        /// <param name="descendant">True to take windows owned through other owned windows too</param>
        /// <returns>The matching windows, in the order the desktop lists them</returns>
        private static List<AutomationElement> OwnedWindows(Application application, AutomationElement owner, Condition condition, bool descendant)
        {
            List<AutomationElement> retVal = new List<AutomationElement>();
            IntPtr ownerHandle = new IntPtr(owner.Current.NativeWindowHandle);

            if (IntPtr.Zero != ownerHandle)
                foreach (IntPtr window in Desktops.WindowsOn(application.applicationSettings.HiddenDesktopName))
                {
                    bool owned = false;

                    // the window's owner, then that window's owner, as far as the axis allows
                    for (IntPtr above = Desktops.OwnerWindowOf(window); false == owned && IntPtr.Zero != above; above = true == descendant ? Desktops.OwnerWindowOf(above) : IntPtr.Zero)
                        owned = ownerHandle == above;

                    AutomationElement element = true == owned ? AutomationElement.FromHandle(window) : null;

                    if (null != element && true == Matches(element, condition))
                        retVal.Add(element);
                }

            return retVal;
        }

        /// <summary>
        /// Whether an element is a top level window on the application's hidden desktop.
        /// </summary>
        /// <param name="application">The application on the hidden desktop</param>
        /// <param name="element">The element to test</param>
        /// <returns>True when the element is one of the desktop's top level windows</returns>
        private static bool IsTopLevelOnHiddenDesktop(Application application, AutomationElement element)
        {
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            return IntPtr.Zero != handle && Desktops.WindowsOn(application.applicationSettings.HiddenDesktopName).Contains(handle);
        }

        /// <summary>
        /// Matches a path step in the raw view, for controls missing from the control view, which FindAll and FindFirst
        /// cannot reach.
        /// </summary>
        /// <param name="current">The element to search under</param>
        /// <param name="condition">What the step is asking for</param>
        /// <param name="descendant">True to search everything below, false for direct children only</param>
        /// <returns>The matches in document order; empty when the step matches nothing</returns>
        internal static List<AutomationElement> RawMatches(AutomationElement current, Condition condition, bool descendant)
        {
            List<AutomationElement> retVal = new List<AutomationElement>();
            TreeWalker walker = TreeWalker.RawViewWalker;

            for (AutomationElement child = walker.GetFirstChild(current); null != child; child = walker.GetNextSibling(child))
            {
                if (true == Matches(child, condition))
                    retVal.Add(child);

                if (true == descendant)
                    retVal.AddRange(RawMatches(child, condition, true));
            }

            return retVal;
        }

        /// <summary>
        /// Evaluates a condition against an element, for the raw view where UIAutomation will not.
        /// </summary>
        /// <param name="element">The element to test</param>
        /// <param name="condition">What the step is asking for</param>
        /// <returns>True when the element matches the step</returns>
        private static bool Matches(AutomationElement element, Condition condition)
        {
            bool retVal = false;

            if (ReferenceEquals(Condition.TrueCondition, condition))
                retVal = true;
            else if (condition is PropertyCondition property)
            {
                object wanted = property.Value;
                object actual = element.GetCurrentPropertyValue(property.Property);

                // A ControlType condition holds the type's id, but the element returns the ControlType object; compare
                // ids.
                retVal = actual is ControlType controlType && wanted is int wantedType
                    ? controlType.Id == wantedType
                    : Equals(actual, wanted);
            }
            else if (condition is AndCondition all)
            {
                retVal = true;

                foreach (Condition each in all.GetConditions())
                    if (false == Matches(element, each))
                    {
                        retVal = false;
                        break;
                    }
            }
            else if (condition is OrCondition any)
            {
                foreach (Condition each in any.GetConditions())
                    if (true == Matches(element, each))
                    {
                        retVal = true;
                        break;
                    }
            }
            else if (condition is NotCondition other)
                retVal = false == Matches(element, other.Condition);

            return retVal;
        }

        /// <summary>
        /// Reads the position out of a path step, so Pane[2] asks for the second Pane rather than any Pane.
        /// </summary>
        /// <param name="token">The step to read</param>
        /// <returns>The one based position, zero when the step names no position</returns>
        private static int IndexFor(string token)
        {
            int result = 0;
            int start = (token ?? string.Empty).IndexOf('[');

            // A step can have a position and attributes, as in Pane[2][@ClassName='x'], so read each bracket until one
            // holds a number.
            while (0 < start && 0 == result)
            {
                int end = token.IndexOf(']', start);

                if (0 > end)
                    break;

                if (true == int.TryParse(token.Substring(start + 1, end - start - 1), out int position) && 0 < position)
                    result = position;

                start = token.IndexOf('[', end);
            }

            return result;
        }

        /// <summary>
        /// Turns one path step, e.g. "Button[@AutomationId='Item 42002']", into the condition that matches it.
        /// </summary>
        /// <param name="token">The step to read</param>
        /// <returns>The condition; null when the step names a control type or attribute GPAL does not know, which no
        /// element matches</returns>
        private static Condition ConditionFor(string token)
        {
            Condition result = null;

            if (false == string.IsNullOrWhiteSpace(token))
            {
                string controlTypeStr = token;
                var conditions = new List<Condition>();

                // false once the step asks for an attribute or control type GPAL does not know
                bool known = true;

                int attrStart = token.IndexOf('[');
                if (attrStart > 0)
                {
                    controlTypeStr = token.Substring(0, attrStart);

                    // Read each bracket in turn: a position, as in Pane[2], which IndexFor reads, or an attribute
                    // [@Name='xxx'][@ClassName='yyy'] etc. Anything else is a step GPAL does not know.
                    string remaining = token.Substring(attrStart);
                    while (true == known && 0 < remaining.Length)
                    {
                        int end = remaining.IndexOf(']');

                        if ('[' != remaining[0] || -1 == end)
                        {
                            known = false;
                            break;
                        }

                        string inside = remaining.Substring(1, end - 1);
                        var parts = inside.StartsWith("@") ? inside.Substring(1).Split(new char[] { '=' }, 2) : null;

                        if (null == parts)
                        {
                            if (false == int.TryParse(inside, out int position) || 0 >= position)
                                known = false;
                        }
                        else if (parts.Length == 2)
                        {
                            string attrName = parts[0].Trim();
                            string value = parts[1].Trim().Trim('\'', '"');

                            Condition cond = attrName.ToLowerInvariant() switch
                            {
                                "name" => new PropertyCondition(AutomationElement.NameProperty, value),
                                "automationid" => new PropertyCondition(AutomationElement.AutomationIdProperty, value),
                                "classname" => new PropertyCondition(AutomationElement.ClassNameProperty, value),
                                _ => null
                            };

                            if (cond != null)
                                conditions.Add(cond);
                            else
                                known = false;
                        }
                        else
                            known = false;

                        // whitespace between and after the brackets is ignored
                        remaining = remaining.Substring(end + 1).TrimStart();
                    }
                }

                // Add ControlType condition; * is any control type
                if ("*" != controlTypeStr.Trim())
                {
                    ControlType controlType = GetControlType(controlTypeStr);
                    if (controlType != null)
                        conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
                    else
                        known = false;
                }

                if (true == known && 0 == conditions.Count)
                    result = Condition.TrueCondition;
                else if (true == known && 1 == conditions.Count)
                    result = conditions[0];
                else if (true == known)
                    result = new AndCondition(conditions.ToArray());
            }

            return result;
        }

        /// <summary>
        /// Resolves a UI Automation <see cref="ControlType"/> from a programmatic/control type name (e.g. "Button", "Edit", "ListItem"),
        /// trying <see cref="System.Windows.Automation.Peers.AutomationControlType"/> first and falling back to common aliases.
        /// </summary>
        /// <param name="programmaticName">The control type name to resolve</param>
        /// <returns>The matching <see cref="ControlType"/>, or null if none could be resolved</returns>
        private static ControlType GetControlType(string programmaticName)
        {
            if (string.IsNullOrWhiteSpace(programmaticName))
                return null;

            // Touch ControlType so its static constructor has run before LookupById.
            _ = ControlType.Button.ToString();

            // Try direct lookup by name via the known AutomationControlType enum + offset
            if (Enum.TryParse<System.Windows.Automation.Peers.AutomationControlType>(programmaticName, true, out var enumValue))
            {
                int id = (int)enumValue + 50000;   // Official offset for ControlType IDs
                return ControlType.LookupById(id);
            }

            // Fallback: try common aliases
            return programmaticName.ToLowerInvariant() switch
            {
                "window" => ControlType.Window,
                "pane" => ControlType.Pane,
                "button" => ControlType.Button,
                "edit" => ControlType.Edit,        // TextBox
                "textbox" => ControlType.Edit,
                "combobox" => ControlType.ComboBox,
                "list" => ControlType.List,
                "listitem" => ControlType.ListItem,
                "menuitem" => ControlType.MenuItem,
                "tab" => ControlType.Tab,
                "tabitem" => ControlType.TabItem,
                "tree" => ControlType.Tree,
                "treeitem" => ControlType.TreeItem,
                "document" => ControlType.Document,
                "group" => ControlType.Group,
                "checkbox" => ControlType.CheckBox,
                "radiobutton" => ControlType.RadioButton,
                "hyperlink" => ControlType.Hyperlink,
                "image" => ControlType.Image,
                "spinner" => ControlType.Spinner,
                "slider" => ControlType.Slider,
                "statusbar" => ControlType.StatusBar,
                "toolbar" => ControlType.ToolBar,
                "datagrid" => ControlType.DataGrid,
                "dataitem" => ControlType.DataItem,
                _ => null
            };
        }
        /// <summary>
        /// Finds elements matching the selector path's AutomationId via <see cref="FindElementsBy"/>.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selectorPath">The selector path entry containing the AutomationId to match</param>
        /// <param name="searchForElement">Forwarded to <see cref="FindElementsBy"/></param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The matching elements</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByAutomationID(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            return FindElementsBy(application, selectorPath, ConditionPropertyType.AutomationID, searchForElement, element);
        }
        /// <summary>
        /// Finds elements whose Value property matches the selector path's text via <see cref="FindElementsBy"/>.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selectorPath">The selector path entry containing the text to match</param>
        /// <param name="searchForElement">Forwarded to <see cref="FindElementsBy"/></param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The matching elements</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByText(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            return FindElementsBy(application, selectorPath, ConditionPropertyType.Value, searchForElement, element);   // CAVEAT: a Text path searches the Value property, the same as a Value path
        }
        /// <summary>
        /// Finds elements matching the selector path's Value property via <see cref="FindElementsBy"/>.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selectorPath">The selector path entry containing the value to match</param>
        /// <param name="searchForElement">Forwarded to <see cref="FindElementsBy"/></param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The matching elements</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByValue(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            return FindElementsBy(application, selectorPath, ConditionPropertyType.Value, searchForElement, element);
        }
        /// <summary>
        /// Finds elements matching the selector path's Name property via <see cref="FindElementsBy"/>.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selectorPath">The selector path entry containing the name to match</param>
        /// <param name="searchForElement">Forwarded to <see cref="FindElementsBy"/></param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The matching elements</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByName(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            return FindElementsBy(application, selectorPath, ConditionPropertyType.Name, searchForElement, element);
        }
        /// <summary>
        /// Finds elements matching the selector path's ClassName property via <see cref="FindElementsBy"/>.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selectorPath">The selector path entry containing the class name to match</param>
        /// <param name="searchForElement">Forwarded to <see cref="FindElementsBy"/></param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The matching elements</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByClassName(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            return FindElementsBy(application, selectorPath, ConditionPropertyType.ClassName, searchForElement, element);
        }
        /// <summary>
        /// Finds elements matching the selector path's value for the given <see cref="ConditionPropertyType"/>,
        /// searching within the given element (or the application's root element if none is provided).
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selectorPath">The selector path entry containing the value to match</param>
        /// <param name="conditionPropertyType">Which AutomationElement property to match against</param>
        /// <param name="searchForElement">Currently unused</param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The matching elements</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsBy(Application application, SelectorPathEntry selectorPath, ConditionPropertyType conditionPropertyType, bool searchForElement = true, AutomationElement element = null)
        {
            ReadOnlyCollection<GPALAutomationElement> elem;

            if (null != element)
                elem = element.FindElements(selectorPath.SelectorPath, conditionPropertyType);
            else
                elem = application.RootAutomationElement.FindElements(selectorPath.SelectorPath, conditionPropertyType);

            return elem;
        }
        /// <summary>
        /// Publish specific log message that we are ignoring a selector marked for deletion or the selector search resulted in finding no elements
        /// In either case, we are continuing to the next selector(s), if any
        /// </summary>
        /// <param name="application">The Application the selector belongs to, for publishing the event</param>
        /// <param name="selector"></param>
        /// <param name="elements"></param>
        internal static void PublishToEventHandler(Application application, Selector selector, List<GPALAutomationElement> elements)
        {
            string msg = "";
            if (true == selector.DeleteMe)
                msg = $"Ignoring selector {selector.Name} marked for deletion. Continuing.";
            else if (null == elements || 0 == elements.Count)
                msg = $"No elements found for selector {selector.Name}. Continuing.";

            GPAL.PublishSimpleEvent(GPALEventType.INFO, msg, application, GPALObjectType.Application);
        }
        /// <summary>
        /// Waits, up to the given timeout, for all selectors in the current unit of work to find/match elements.
        /// The wait runs no CallIfFound/CallIfNotFound handlers; the action after it does, as in the browser version.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds; -1 waits forever</param>
        /// <param name="matchedAll">True if all match criteria for all selectors were satisfied</param>
        /// <returns>The elements found/matched across all selectors in the current unit of work</returns>
        public static List<GPALAutomationElement> WaitFor(Application application, int timeoutInTicks, out bool matchedAll)
        {
            bool matchedAll2 = true;
            List<GPALAutomationElement> foundElements = null;
            List<GPALAutomationElement> tmpFoundElements = null;
            List<GPALAutomationElement> matchedElements;

            foundElements = new List<GPALAutomationElement>();

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                tmpFoundElements = WaitFor(application, selector, timeoutInTicks, out matchedAll2, out matchedElements);

                tmpFoundElements = matchedElements;

                // a selector an earlier CallIf handler marked for deletion is passed over
                if (true == selector.DeleteMe || null == tmpFoundElements)
                {
                    PublishToEventHandler(application, selector, tmpFoundElements);
                    continue;
                }

                int rowCount = 0;

                foreach (GPALAutomationElement element in tmpFoundElements)
                {
                    foundElements.Add(element);
                    if (++rowCount >= application.CurrentUOW.WithAllThatMatch)
                        break;
                }
            }

            matchedAll = matchedAll2;
            return foundElements;
        }

        /// <summary>
        /// Waits, up to the given timeout, for the given selector to find elements that match its criteria, polling
        /// roughly once per second.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selector">The selector to wait for</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds; -1 waits forever</param>
        /// <param name="matchedAll">True if the selector's match criteria were satisfied</param>
        /// <param name="matchedElements">Receives the elements that matched the selector's match criteria</param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The elements the last search found, before matching; empty when it found none, null when matching
        /// failed on the first selector path</returns>
        private static List<GPALAutomationElement> WaitFor(Application application, Selector selector, int timeoutInTicks, out bool matchedAll, out List<GPALAutomationElement> matchedElements, AutomationElement element = null)
        {
            bool matchedAll2 = true;
            List<GPALAutomationElement> matchedElements2;
            ReadOnlyCollection<GPALAutomationElement> elements;
            int orgTimeoutInTicks = timeoutInTicks;

            if (WaitTime.Forever == timeoutInTicks)
                timeoutInTicks = Int32.MaxValue;    // forever

            // the start of the wait, at DEBUG: INFO gets one line when the wait is over
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Waiting up to [{timeoutInTicks}] ms for elements matching selector [{selector.Name}][{selector.SelectorPath}]", application, GPALObjectType.Application);

            // Search until something matches the selector's criteria or the time is up, as in the browser version: a
            // control that is found but does not match is not what the wait is for. The clock counts the time the
            // searches take as well as the sleeps. The polls run no CallIf handlers; the action's own search runs them.
            var sw = Stopwatch.StartNew();
            do
            {
                // the polls publish nothing; the wait publishes one line when it is over
                elements = FindElements(application, application.CurrentUOW, selector, out matchedAll2, out matchedElements2, element, false, publish: false);

                // sleep only while time remains, so the sleep time is never negative
                if (0 == (matchedElements2?.Count ?? 0) && WaitTime.Immediate != timeoutInTicks && sw.ElapsedMilliseconds < timeoutInTicks)
                    Thread.Sleep((int)Math.Min(1000, timeoutInTicks - sw.ElapsedMilliseconds));

            } while (sw.ElapsedMilliseconds < timeoutInTicks && 0 == (matchedElements2?.Count ?? 0));

            // what the last search found, with its path in full, how long the wait took and what it allowed, as in the
            // browser version
            string found = lastFoundFullMessage ?? $"[0] elements found for selector [{selector.Name}]";

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"{found} in [{sw.ElapsedMilliseconds}] ms of {(Int32.MaxValue == timeoutInTicks ? "[forever]" : $"[{timeoutInTicks}] ms")}", application, GPALObjectType.Application);

            matchedAll = matchedAll2;                   // the last lookup's answer, as in the browser version
            matchedElements = matchedElements2;         // always use matched, it's either all or those matched

            // with a WaitFor state set, wait for each matched control to reach it, as in the browser version
            if (ElementState.NotSet != application.CurrentUOW.WaitForElementState)
                foreach (GPALAutomationElement control in matchedElements2 ?? new List<GPALAutomationElement>())
                    matchedAll &= WaitFor(application, control, application.CurrentUOW.WaitForElementState, orgTimeoutInTicks);

            return elements?.ToList<GPALAutomationElement>();
        }

        /// <summary>
        /// Waits for a control to reach the given state, checking about once a second, as the browser version waits
        /// for an element. A wait time below zero, when no WaitFor(timeInMs) was set, waits three seconds.
        /// </summary>
        /// <param name="application">The Application the control belongs to</param>
        /// <param name="control">The control to wait on</param>
        /// <param name="state">The state to wait for</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds (WaitTime.Forever for indefinite)</param>
        /// <returns>True when the control reached the state in time</returns>
        public static bool WaitFor(Application application, GPALAutomationElement control, ElementState state, int timeoutInTicks)
        {
            const int defaultStateWaitInTicks = 3000;
            bool inState = false;

            if (WaitTime.Forever == timeoutInTicks)
                timeoutInTicks = Int32.MaxValue;
            else if (timeoutInTicks < 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WaitFor(ElementState.[{state}]) needs a positive wait time but got [{timeoutInTicks}] ms (no .WaitFor(timeInMs) was set for this unit of work) - using the default of [{defaultStateWaitInTicks}] ms instead.", application, GPALObjectType.Application);
                timeoutInTicks = defaultStateWaitInTicks;
            }

            var sw = Stopwatch.StartNew();
            do
            {
                // a check that throws reads as not in the state and the wait goes on, as in the browser version
                inState = IsControlInState(application, control, state);

                if (false == inState && sw.ElapsedMilliseconds < timeoutInTicks)
                    Thread.Sleep((int)Math.Min(1000, timeoutInTicks - sw.ElapsedMilliseconds));

            } while (false == inState && sw.ElapsedMilliseconds < timeoutInTicks);

            if (false == inState)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Control [{NameOf(control)}] not in state [{state}] within [{timeoutInTicks}] ms.", application, GPALObjectType.Application);

            return inState;
        }

        /// <summary>
        /// Whether a control is in the given state, read through UIAutomation as the browser version reads an element.
        /// Checked is a toggle that is on or a selected item; Clickable is shown and enabled; Editable is enabled with a
        /// value or text that is not read only; Hidden is not shown, or gone.
        /// </summary>
        /// <param name="application">The Application, for the event a failed check publishes</param>
        /// <param name="control">The control to check</param>
        /// <param name="state">The state to check for</param>
        /// <returns>True when the control is in the state; false when it is not or the check threw</returns>
        private static bool IsControlInState(Application application, GPALAutomationElement control, ElementState state)
        {
            bool retVal = false;

            try
            {
                bool isChecked = ElementState.Checked == state || ElementState.Unchecked == state
                    ? true == control.Checked || true == control.Selected
                    : false;

                switch (state)
                {
                    case ElementState.Visible:
                        retVal = control.Displayed;
                        break;
                    case ElementState.Hidden:
                        retVal = false == control.Displayed;
                        break;
                    case ElementState.Enabled:
                        retVal = control.Enabled;
                        break;
                    case ElementState.Disabled:
                        retVal = false == control.Enabled;
                        break;
                    case ElementState.Checked:
                        retVal = isChecked;
                        break;
                    case ElementState.Unchecked:
                        retVal = false == isChecked;
                        break;
                    case ElementState.Selected:
                        retVal = control.Selected;
                        break;
                    case ElementState.Unselected:
                        retVal = false == control.Selected;
                        break;
                    case ElementState.Clickable:
                        retVal = control.Displayed && control.Enabled;
                        break;
                    case ElementState.Editable:
                        // a one-line box answers through ValuePattern, a multi-line one such as a document through
                        // TextPattern's read-only attribute
                        if (true == control.Enabled && true == control.Ae.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                            retVal = false == ((ValuePattern)valuePattern).Current.IsReadOnly;
                        else if (true == control.Enabled && true == control.Ae.TryGetCurrentPattern(TextPattern.Pattern, out object textPattern))
                            retVal = false.Equals(((TextPattern)textPattern).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute));
                        break;
                    default:
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unsupported element state [{state}].", control, GPALObjectType.Other);
                        break;
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (ElementNotAvailableException) when (ElementState.Hidden == state)
            {
                // a control that went away, a closed dialog for one, is hidden
                retVal = true;
            }
            catch (Exception ex)
            {
                // for any other state, a control that went away, or a pattern that refuses, answers false
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error checking state [{state}]", application, GPALObjectType.Application, ex);
                retVal = false;
            }

            return retVal;
        }

        /// <summary>
        /// Lists the children of the element where the path walk stopped, for the not-found message.
        /// </summary>
        /// <param name="parent">The element the walk reached; null when it had not left the desktop</param>
        /// <returns>The children as text, appended to the not-found message</returns>
        internal static string WhatIsThere(AutomationElement parent)
        {
            var names = new List<string>();
            string retVal;

            try
            {
                AutomationElement at = parent ?? AutomationElement.RootElement;

                foreach (AutomationElement child in at.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition))
                {
                    string controlType = child.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                    string automationId = child.Current.AutomationId;

                    string className = child.Current.ClassName;

                    names.Add(string.IsNullOrEmpty(automationId)
                        ? $"{controlType}[@ClassName='{className}'] '{child.Current.Name}'"
                        : $"{controlType}[@AutomationId='{automationId}'][@ClassName='{className}'] '{child.Current.Name}'");
                }

                retVal = 0 == names.Count
                    ? "It has no children."
                    : $"What is there: {string.Join(" | ", names)}";
            }
            catch (Exception ex)
            {
                retVal = $"Its children could not be read: {ex.Message}";
            }

            return retVal;
        }
        /// <summary>
        /// How many of the elements a selector found an action works on: one, unless WithAllThatMatch says otherwise.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <returns>The number requested, or one</returns>
        private static int HowManyToActOn(Application application)
        {
            return int.MaxValue == application.CurrentUOW.WithAllThatMatch ? 1 : application.CurrentUOW.WithAllThatMatch;
        }
    }
}
