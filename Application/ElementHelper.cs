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

    internal class ElementHelper
    {
        delegate ReadOnlyCollection<GPALAutomationElement> FindElementsByDelegate(Application application, SelectorPathEntry selectorPath, bool searchForSelector = true, AutomationElement element = null);
        // virtual keycodes: https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes?redirectedfrom=MSDN
        // scan codes: https://en.wikipedia.org/wiki/Scancode
        static byte ScanCode {get; } = 0;

        // to avoid repeat messages for the same selector when waiting for a selector
        static List<string> listOfMessages = new List<string>();
        static bool supressedMessage = false;
        static Selector lastSelector = null;

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
        public static ReadOnlyCollection<GPALAutomationElement> FindElements(Application application, UnitOfWork currentUOW, Selector selector, out bool matchedAll, out List<GPALAutomationElement> matchedElements, AutomationElement automationElement = null, bool callIfHandler = true)
        {
            int foundCount = 0;

            string elementStringToMatch = null;
            ReadOnlyCollection<GPALAutomationElement> retElems = null;
            List<GPALAutomationElement> matchedElems = new List<GPALAutomationElement>();
            MatchCollection mc = null;
            ReadOnlyCollection<GPALAutomationElement> allFoundElems = null;
            List<GPALAutomationElement> totalAllFoundElems = new List<GPALAutomationElement>();
            List<GPALAutomationElement> iterateOverTheseElements = null;
            bool tryNextSelector = false;
            int criteriaCnt = 0;
            string message = string.Empty;
            int retries = 1;
            bool retry = false;

            matchedAll = false;
            matchedElements = new List<GPALAutomationElement>();

            currentUOW.CurrentSelector = selector;

            if (false == lastSelector?.Name.Equals(selector.Name))
            {
                listOfMessages.Clear();
                supressedMessage = false;
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
            tryAgain:   // NOTE: CAVEAT: goto label - don't like it, but it has to happen sometimes. we cannot have any inner loop as that will break retrying the next selector
                int textLength = selectorPathEntry.SelectorPath.Length - 1;
                string abbreviatedText = selectorPathEntry.SelectorPath.Substring(0, Math.Min(7, textLength)) + "..." + selectorPathEntry.SelectorPath.Substring(Math.Max(0, textLength - 7));
                if (18 > selectorPathEntry.SelectorPath.Length)
                    abbreviatedText = selectorPathEntry.SelectorPath;

                int idx = GetIndexOfEnum<SelectorPathType>((int)selectorPathEntry.SelectorPathType);
                tryNextSelector = false;
                string errorMessage = $"Error finding elements for selector [{selector.Name}][{abbreviatedText}]";

                try
                {
                    message = $"Searching for elements for selector [{selector.Name}] using [{selectorPathEntry.SelectorPathType}] [{abbreviatedText}]";

                    if (false == listOfMessages.Contains(message))
                    {
                        listOfMessages.Add(message);
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, message, application, GPALObjectType.Application);
                    }

                    allFoundElems = functionList[idx](application, selectorPathEntry, selector.SearchForSelector, automationElement);

                    // if we get null back, we will retry once just to make sure
                    if (0 < (allFoundElems?.Count ?? 0))   // did not find any elements
                        totalAllFoundElems.AddRange(allFoundElems.ToList());

                    #region <Element Matching>
                    if (0 < selector.MatchCriteria.Count)
                    {
                        foreach (SelectorSettings.MatchCriteriaEntry matchCriteriaEntry in selector.MatchCriteria)
                        {
                            if (MatchType.Custom == matchCriteriaEntry.MatchType)
                            {
                                // match function returns true or false for matchedAll
                                CallIfStatus handled = matchCriteriaEntry.AppMatchingFunction(allFoundElems.ToList(), out matchedElems, out matchedAll, selector, matchCriteriaEntry.ExactMatch);

                                if (CallIfStatus.Terminate == handled)
                                {
                                    string msg = $"Custom match handler [{matchCriteriaEntry.AppMatchingFunction.GetInvocationList()[0].Method.Name}] requested program termination for selector {selector.Name} path [{selectorPathEntry.SelectorPath}].";
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, msg, application, GPALObjectType.Application);
                                    throw new GPALException(msg);
                                }

                                // always return matched elements, it's either all or just those matched
                                matchedElements.AddRange(matchedElems);
                                matchedElements = matchedElements.Distinct().ToList();  // dedupe   // what order these will be in over iterations is anyone's guess, but they will be on one page

                                if (CallIfStatus.Handled == handled)   // stop matching
                                    break;
                                else if (CallIfStatus.TryNext == handled)   // custom algorithm didn't like the result set and wants us to try the next selectorpath
                                {
                                    tryNextSelector = true;
                                    break;
                                }
                            }
                        }

                        if (true == tryNextSelector)
                            continue;

                        // if our custom match matched items, then apply further matching to these items
                        // else apply matching to all found elements
                        if (0 == matchedElements.Count)
                            iterateOverTheseElements = new List<GPALAutomationElement>(allFoundElems);
                        else
                            iterateOverTheseElements = new List<GPALAutomationElement>(matchedElements);

                        // we are done with idx, we can reuse, used as index into findelement method array
                        for (idx = 0; idx < iterateOverTheseElements.Count; idx++)
                        {
                            GPALAutomationElement automationElement2 = iterateOverTheseElements[idx];
                            criteriaCnt = 0;

                            for (int matchIdx = 0; matchIdx < selector.MatchCriteria.Count; matchIdx++)
                            {
                                SelectorSettings.MatchCriteriaEntry matchCriteriaEntry = selector.MatchCriteria[matchIdx];

                                if (MatchType.None != matchCriteriaEntry.MatchType)
                                {
                                    criteriaCnt++;  // keep count of how many match criteria we have, ALL must match on the element, we should have as many matched elements as criteria for ALL to match

                                    // Mask out the REGEX 1 bit to get to the actual type
                                    switch ((MatchType)((int)matchCriteriaEntry.MatchType & 0XFE))
                                    {
                                        case MatchType.Text:
                                            elementStringToMatch = automationElement2.Ae.GetText();
                                            break;

                                        // Href, Src and Placeholder mean nothing on a desktop control, so match them
                                        // against the text.
                                        case MatchType.Href:
                                        case MatchType.Src:
                                        case MatchType.Placeholder:
                                            elementStringToMatch = automationElement2.Ae.GetText();
                                            break;

                                        // Use the ValuePattern value on purpose: an exact match has a known length, so
                                        // truncation cannot change the result. A control with no ValuePattern falls
                                        // back to its text.
                                        case MatchType.Value:
                                            elementStringToMatch = automationElement2.Ae.GetValue() ?? automationElement2.Ae.GetText();
                                            break;
                                    }

                                    if (1 == ((int)MatchType.Regex & (int)matchCriteriaEntry.MatchType))
                                    {

                                        mc = Regex.Matches(elementStringToMatch, matchCriteriaEntry.StringToMatch);
                                        if (0 < mc.Count)
                                            matchedElems.Add(automationElement2);
                                    }
                                    else
                                    {
                                        if (false == matchCriteriaEntry.ExactMatch && elementStringToMatch.Contains(matchCriteriaEntry.StringToMatch))
                                            matchedElems.Add(automationElement2);
                                        else if (true == matchCriteriaEntry.ExactMatch && elementStringToMatch.Equals(matchCriteriaEntry.StringToMatch))
                                            matchedElems.Add(automationElement2);
                                    }
                                }
                            }
                            if (criteriaCnt == matchedElems.Count)  // we have one element that must be added once for every match, if not, it doesn't match all criteria, so try the next element
                                matchedElements.AddRange(matchedElems.Distinct());     // add this match to the out return list (dedupe it first)
                        }
                        matchedElements = matchedElements.Distinct().ToList();  // dedupe   // what order these will be in over iterations is anyone's guess, but they will be on one page
                        matchedAll = (allFoundElems.Count == foundCount);
                    }
                    else // no match criteria - all found
                    {
                        matchedAll = true;
                        matchedElems = allFoundElems?.ToList() ?? new List<GPALAutomationElement>();
                        matchedElements = matchedElems;     // out return list
                    }
                    #endregion <Element Matching>

                }
                catch (Exception ex) // element not found for some reason
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Exception thrown trying to find elements for selector [{selector.Name}]. Continuing.", currentUOW, GPALObjectType.UnitOfWork, ex);
                    continue;
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
                    goto tryAgain;  // NOTE: CAVEAT: goto label
                }

                selector.ElementsFoundAndMatchedCount = matchedElements.Count();

                message = $"[{retElems?.Count ?? 0}] elements found for selector [{selector.Name}]";

                if (false == listOfMessages.Contains(message))
                {
                    listOfMessages.Add(message);
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, message, application, GPALObjectType.Application);

                    if (0 < retElems?.Count && (true == selector.AnyMatchCriteria() || Int32.MaxValue != currentUOW.WithAllThatMatch))
                    {
                        var matchCount = selector.MatchCriteria.Count > 0 ? selector.MatchCriteria.Count.ToString() : $"WithAllThatMatch({currentUOW.WithAllThatMatch})";

                        if ((true == matchCount.Contains("WithAllThatMatch") && 1 < currentUOW.WithAllThatMatch) || 0 < selector.MatchCriteria.Count)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{matchedElements.Count}] elements matched for selector [{selector.Name}] with [{matchCount}] match criteria", application, GPALObjectType.Application);
                    }
                }
                else if (false == supressedMessage)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", application, GPALObjectType.Application);
                    supressedMessage = true;
                }

                // CAVEAT:if one path matches all elements found, are we done? maybe the next path finds different elements we want to add?
                // TODO: do we have a fluent switch 'TryAllPaths' or 'StopOnSuccess'
                // For complex evaluations, roll your own matching function, 
                if (true == matchedAll || retElems?.Count > 0) // for now, if a selector path matches one match criteria, we consider ourselves done with matching
                    break;

            }   // foreach selectorpath

            // false == callIfHandler will be the waitfor call
            if (true == callIfHandler)
                CallIfHandlers(application, currentUOW, allFoundElems, matchedElements, selector, matchedAll);

            // When every path of a selector threw, both lists are null. Null-check them, as the browser version does.
            selector.ElementsFoundAndMatchedCount = true == selector.AnyMatchCriteria()
                ? (matchedElements?.Count() ?? 0)
                : (retElems?.Count ?? 0);

            return retElems;
        }

        // TODO: what do these values mean outside of the bubble handlers?
        // currentUOW.callFoundHandled
        // currentUOW.callNotFoundHandled
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
            UnitOfWork safeUOW;

            // TODO: do we care about matchedall?

            // bubble thru handlers based upon handler return value
            // 0 = not handled, bubble to next handler
            // 1 = handled and continue with the program
            // -1 = unexpected error and terminate program
            if (0 < (foundElements?.Count ?? 0))
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

                        currentUOW.CallIfNotFoundHandled = func(application, foundElements?.Cast<IGPALAutomationElement>().ToList(), matchedElements?.Cast<IGPALAutomationElement>().ToList(), selector, matchedAll);

                        application.CurrentUOW = safeUOW;

                        if (CallIfStatus.NotHandled == currentUOW.CallIfNotFoundHandled)
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
                if (0 == currentUOW.AppCallIfFoundHandled && 0 < GPAL.GPALSettings.AppCallIfFoundList.Count)
                {
                    foreach (Application.CallIfDelegate func in GPAL.GPALSettings.AppCallIfFoundList)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Gloabl CallIfFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

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
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Browser);
                        throw new GPALException(str);
                    }
                }
            }
            else
            {
                if (0 < selector.AppCallIfNotFound.Count)
                {
                    foreach (Application.CallIfDelegate func in currentUOW.AppCallIfNotFound)
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

                    if (CallIfStatus.Terminate == currentUOW.AppCallIfNotFoundHandled)
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
                if (0 == currentUOW.AppCallIfNotFoundHandled && 0 < GPAL.GPALSettings.AppCallIfNotFoundList.Count)
                {
                    foreach (Application.CallIfDelegate func in GPAL.GPALSettings.AppCallIfNotFoundList)
                    {
                        safeUOW = application.CurrentUOW;

                        currentMethodName = func.Method.Name;
                        str = $"Gloabl CallIfNotFound handler [{currentMethodName}] requested program termination on selector [{selector.Name}] [{selector.SelectorPaths[0].SelectorPath}]";

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
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, str, application, GPALObjectType.Browser);
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

            foundImageBounds = ((ImageHelper)GPAL.ImageHelper).FindImage(selectorPath.SelectorPath, application); // first check, is image we seek already on-screen?

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
        /// CSS selectors are not supported for desktop applications; publishes a warning and returns null.
        /// </summary>
        /// <param name="application">The Application being searched</param>
        /// <param name="selectorPath">The selector path entry (unused)</param>
        /// <param name="searchForElement">Unused</param>
        /// <param name="element">Unused</param>
        /// <returns>Always null</returns>
        private static ReadOnlyCollection<GPALAutomationElement> FindElementsByCSS(Application application, SelectorPathEntry selectorPath, bool searchForElement = true, AutomationElement element = null)
        {
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Find elements by CSS is not supported for applications.", application, GPALObjectType.Application);
            return null;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetMenu(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetSubMenu(IntPtr menu, int position);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMenuItemCount(IntPtr menu);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMenuItemID(IntPtr menu, int position);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetMenuString(IntPtr menu, int item, System.Text.StringBuilder text, int count, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private const uint MF_BYPOSITION = 0x0400;
        private const int WM_COMMAND = 0x0111;

        /// <summary>
        /// The names a path asks for from a menu, in order, so Window/MenuBar/MenuItem[@Name='File']//MenuItem
        /// [@Name='Save As...'] reads as File then Save As.
        /// </summary>
        /// <param name="selectorPath">The path to read</param>
        /// <returns>The menu item names the path names, empty when it names none</returns>
        private static List<string> MenuNamesIn(string selectorPath)
        {
            List<string> retVal = new List<string>();

            foreach (Match step in Regex.Matches(selectorPath ?? string.Empty, @"MenuItem[^/]*?\[@Name='([^']*)'\]"))
                retVal.Add(step.Groups[1].Value);

            return retVal;
        }

        /// <summary>
        /// Returns a menu item's name without the access-key ampersand and the tab-separated shortcut text.
        /// </summary>
        /// <param name="menu">The menu holding it</param>
        /// <param name="position">Which item</param>
        /// <returns>The item's name</returns>
        private static string MenuItemName(IntPtr menu, int position)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder(256);

            GetMenuString(menu, position, text, text.Capacity, MF_BYPOSITION);

            string name = text.ToString().Replace("&", string.Empty);
            int shortcut = name.IndexOf('\t');

            return (0 <= shortcut ? name.Substring(0, shortcut) : name).Trim();
        }

        /// <summary>
        /// Returns the command ID for a path of menu items from the window's Win32 menu.
        /// </summary>
        /// <remarks>
        /// A menu opens only by mouse or keyboard, so on a hidden desktop it cannot be walked through UIAutomation.
        /// </remarks>
        /// <param name="menu">The menu to look in</param>
        /// <param name="names">The item names to follow, outermost first</param>
        /// <param name="index">Which of those names is being looked for</param>
        /// <returns>The command ID of the last named item; zero when the path does not lead to one</returns>
        private static int CommandForMenuPath(IntPtr menu, List<string> names, int index)
        {
            int retVal = 0;

            if (IntPtr.Zero != menu && index < names.Count)
                for (int position = 0; position < GetMenuItemCount(menu) && 0 == retVal; position++)
                {
                    if (false == string.Equals(MenuItemName(menu, position), names[index], StringComparison.OrdinalIgnoreCase))
                        continue;

                    IntPtr inside = GetSubMenu(menu, position);

                    retVal = index == names.Count - 1 && IntPtr.Zero == inside
                           ? GetMenuItemID(menu, position)
                           : CommandForMenuPath(inside, names, index + 1);
                }

            return retVal;
        }

        /// <summary>
        /// A menu item found through the window's menu, for a path UIAutomation could not walk.
        /// </summary>
        /// <param name="application">The application being searched</param>
        /// <param name="selectorPath">The path that found nothing</param>
        /// <returns>An element carrying the command that item sends, null when the path names no such item</returns>
        private static GPALAutomationElement MenuItemFor(Application application, string selectorPath)
        {
            GPALAutomationElement retVal = null;
            List<string> names = MenuNamesIn(selectorPath);
            AutomationElement root = application.RootAutomationElement;

            if (0 < names.Count && null != root)
            {
                IntPtr window = new IntPtr(root.Current.NativeWindowHandle);
                int command = CommandForMenuPath(GetMenu(window), names, 0);

                if (0 != command)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{string.Join(" then ", names)}] was not there to be walked, so it was read from the window's own menu, where it sends command [{command}].", application, GPALObjectType.Application);

                    retVal = new GPALAutomationElement { MenuCommand = command, MenuWindow = window };
                }
            }

            return retVal;
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

            while (next < tokens.Length && null == (condition = ConditionFor(tokens[next])))
            {
                // An empty step after another step is a descendant axis, as in Window//Button: search the next step
                // anywhere below. The leading-slash empty step is the first one.
                if (true == string.IsNullOrWhiteSpace(tokens[next]) && 0 < next)
                    descendant = true;

                next++;
            }

            if (next >= tokens.Length)
                result.Add(current);
            else
            {
                List<AutomationElement> candidates = new List<AutomationElement>();

                // RootAutomationElement is the window itself, where the recorder roots its paths, so match the first
                // step against it before its children.
                if (true == firstToken)
                {
                    AutomationElement self = current.FindFirst(TreeScope.Element, condition);

                    if (null != self)
                        candidates.Add(self);
                }

                if (0 == candidates.Count)
                    foreach (AutomationElement child in current.FindAll(true == descendant ? TreeScope.Descendants : TreeScope.Children, condition))
                        candidates.Add(child);

                // If that finds nothing, try the raw view. WinForms leaves whole controls out of the control view (a
                // spinner on a tab page shows only its two buttons). Raw view second, so paths that resolve in the
                // control view still walk it.
                if (0 == candidates.Count)
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
        /// <returns>The condition, null when the step names nothing that can be matched</returns>
        private static Condition ConditionFor(string token)
        {
            Condition result = null;

            if (false == string.IsNullOrWhiteSpace(token))
            {
                string controlTypeStr = token;
                var conditions = new List<Condition>();

                int attrStart = token.IndexOf('[');
                if (attrStart > 0)
                {
                    controlTypeStr = token.Substring(0, attrStart);

                    // Parse attributes [@Name='xxx'][@ClassName='yyy'] etc.
                    string remaining = token.Substring(attrStart);
                    while (remaining.Contains("[@"))
                    {
                        int start = remaining.IndexOf("[@");
                        int end = remaining.IndexOf(']', start);
                        if (end == -1) break;

                        string attrSegment = remaining.Substring(start + 2, end - start - 2);

                        var parts = attrSegment.Split(new char[] { '=' }, 2);

                        if (parts.Length == 2)
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
                        }

                        remaining = remaining.Substring(end + 1);
                    }
                }

                // Add ControlType condition
                ControlType controlType = GetControlType(controlTypeStr);
                if (controlType != null)
                    conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));

                if (1 == conditions.Count)
                    result = conditions[0];
                else if (1 < conditions.Count)
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

            // Force initialization of the static constructor (important quirk!)
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
            return FindElementsBy(application, selectorPath, ConditionPropertyType.Value, searchForElement, element);   // TODO: CAVEAT: KLUDGE: is this correct? Value?
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
        /// Waits, up to the given timeout, for all selectors in the current unit of work to find/match elements,
        /// invoking each selector's CallIfFound/CallIfNotFound handlers along the way.
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

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
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
        /// Waits, up to the given timeout, for the given selector to find elements, polling roughly once per second.
        /// </summary>
        /// <param name="application">The Application to search within</param>
        /// <param name="selector">The selector to wait for</param>
        /// <param name="timeoutInTicks">Timeout in milliseconds; -1 waits forever</param>
        /// <param name="matchedAll">True if the selector's match criteria were satisfied</param>
        /// <param name="matchedElements">Receives the elements that matched the selector's match criteria</param>
        /// <param name="element">Optional AutomationElement to search within; defaults to the application's root element</param>
        /// <returns>The elements found for the selector, or null if none were found before the timeout elapsed</returns>
        private static List<GPALAutomationElement> WaitFor(Application application, Selector selector, int timeoutInTicks, out bool matchedAll, out List<GPALAutomationElement> matchedElements, AutomationElement element = null)
        {
            bool matchedAll2 = true, matchedAll3 = true;
            List<GPALAutomationElement> matchedElements2;
            ReadOnlyCollection<GPALAutomationElement> elements = FindElements(application, application.CurrentUOW, selector, out matchedAll2, out matchedElements2, element);

            if (WaitTime.Forever == timeoutInTicks)
                timeoutInTicks = Int32.MaxValue;    // forever

            // keep looking while we have time left and nothing has turned up. FindElements returns an empty
            // collection (never null) when it finds nothing, so this has to test the count, not the reference.
            for (; 0 < timeoutInTicks && 0 == (elements?.Count ?? 0); timeoutInTicks -= 1000)
            {
                elements = FindElements(application, application.CurrentUOW, selector, out matchedAll3, out matchedElements2, element);
                if (0 == elements.Count)
                    Thread.Sleep(Math.Min(1000, timeoutInTicks));
                else
                    break;
            }

            matchedAll = (matchedAll2 | matchedAll3);   // one of those found it or not :)
            matchedElements = matchedElements2;         // always use matched, it's either all or those matched

            return elements?.ToList<GPALAutomationElement>();
        }

        /// <summary>
        /// Fill in text on an element using the technique defined in the selector
        /// do this for the entire unit of work
        /// </summary>
        /// <param name="application">The Application whose selectors are filled in</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">How the text is written into the element</param>
        public static void FillInFrom(Application application, string useText, WriteMode writeMode)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        bool isPassword = elem.Ae.Current.IsPassword;

                        // Log empty, one- and two-character strings in full.
                        string abbreviatedText = 2 < (useText?.Length ?? 0)
                            ? useText[0] + "..." + useText[useText.Length - 1]
                            : useText;

                        if (false == isPassword)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] text [{abbreviatedText}] in element [{elem.Ae.Current.LocalizedControlType}][{selector.Name}]", application, GPALObjectType.Application);
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{writeMode}] XXXXXX in element [{elem.Ae.Current.LocalizedControlType}][{selector.Name}]", application, GPALObjectType.Application);

                        FillInFrom(application, elem.Ae, new UnitOfWork.ElementNode() { OffsetX = selector.OffsetX, OffsetY = selector.OffsetY }, useText, writeMode);
                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }

                UnitOfWork safeUOW = application.CurrentUOW;
                CallIfStatus handled = 0;

                // CAVEAT: there is no concept of 'handled (1)' vs 'not handled (0)' but definitely can request to exit
                IGPALGrid<string> tokens = GPAL.Grid.ToGPALObject();
                tokens.AddRow(new List<string> { useText });
                if (null != application.CurrentUOW.AppCallAfterFillIn)
                    handled = application.CurrentUOW.AppCallAfterFillIn(application, tokens, 1);

                // CAVEAT: kludge - the call after handler can and probably will set a new current unit of work, but we need our old current unit
                // the call after handler UOW is no longer in scope, so restore our UOW
                application.CurrentUOW = safeUOW;

                if (CallIfStatus.Terminate == handled)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"CallAfterFillIn handler [{application.CurrentUOW.AppCallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.", application, GPALObjectType.Application);
                    throw new GPALException($"CallAfterFillIn handler [{application.CurrentUOW.AppCallAfterFillIn.GetInvocationList()[0].Method.Name}] requested program termination.");
                }
            }
        }
        /// <summary>
        /// Scrolls every element matched by the selectors in the current unit of work, using the given amount, units, and direction.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are scrolled</param>
        /// <param name="amount">The scroll amount: a <see cref="ScrollAmount"/> for increment-based scrolling, or a percentage (double) for percent-based scrolling</param>
        /// <param name="incrementsPercent">Whether <paramref name="amount"/> represents increments or a percentage</param>
        /// <param name="horzVert">Whether to scroll horizontally or vertically</param>
        internal static void Scroll(Application application, object amount, ScrollTypes incrementsPercent, ScrollTypes horzVert)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                if (0 == elems.Count)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] matched no elements, so there was nothing to scroll.", application, GPALObjectType.Application);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        Scroll(application, elem.Ae, selector.InteractionType, amount, incrementsPercent, horzVert);
                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Moves a scrollbar through RangeValuePattern. Its value is a position between Minimum and Maximum, so a
        /// percentage is converted to that range and an increment to a SmallChange or LargeChange step.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="rangeValue">The element's RangeValuePattern</param>
        /// <param name="ae">The element, for naming it when something is refused</param>
        /// <param name="amount">A percentage when incrementsPercent is Percent, otherwise a ScrollAmount</param>
        /// <param name="incrementsPercent">Which of the two the amount is</param>
        private static void SetRangeValue(Application application, RangeValuePattern rangeValue, AutomationElement ae, object amount, ScrollTypes incrementsPercent)
        {
            var now = rangeValue.Current;

            if (true == now.IsReadOnly)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.LocalizedControlType}] reports its range as read only, so it cannot be scrolled.", application, GPALObjectType.Application);
            else
            {
                double span = now.Maximum - now.Minimum;
                double wanted;

                if (ScrollTypes.Percent == incrementsPercent)
                    wanted = now.Minimum + (((double)(int)amount / 100.0) * span);
                else
                {
                    // The enum gives step size and direction. A control that returns NaN for its step gets a hundredth
                    // (small) or a tenth (large) of its range.
                    ScrollAmount step = (ScrollAmount)amount;

                    bool large = ScrollAmount.LargeIncrement == step || ScrollAmount.LargeDecrement == step;
                    bool back = ScrollAmount.SmallDecrement == step || ScrollAmount.LargeDecrement == step;

                    double by = large ? now.LargeChange : now.SmallChange;

                    if (double.IsNaN(by) || 0 == by)
                        by = span * (large ? 0.1 : 0.01);

                    wanted = now.Value + (back ? -by : by);
                }

                // Clamp to the end of the range; the control rejects values outside it.
                wanted = wanted < now.Minimum ? now.Minimum : (wanted > now.Maximum ? now.Maximum : wanted);

                try
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"RangeValuePattern on [{ae.Current.ControlType.LocalizedControlType}]: range [{now.Minimum}] to [{now.Maximum}], value [{now.Value}], setting [{wanted}].", application, GPALObjectType.Application);

                    rangeValue.SetValue(wanted);

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"RangeValuePattern on [{ae.Current.ControlType.LocalizedControlType}]: value is now [{rangeValue.Current.Value}].", application, GPALObjectType.Application);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to set a value of [{wanted}] on control [{ae.Current.ControlType.LocalizedControlType}].", application, GPALObjectType.Application, ex);
                }
            }
        }

        /// <summary>
        /// Moves a ScrollPattern control to a percentage by stepping. SetScrollPercent animates one pixel at a time on
        /// some controls; increment scrolls land at once. Large steps while a whole view fits in the remaining
        /// distance, then small steps.<br/>
        /// ViewSize is the visible portion as a percentage, which is also one large step. The position is read after
        /// each step; a control that stops moving ends the walk.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="ae">The element to scroll</param>
        /// <param name="scrollPattern">The element's ScrollPattern</param>
        /// <param name="percent">Where to land, 0 to 100</param>
        /// <param name="horzVert">Which axis to move</param>
        private static void ScrollToPercent(Application application, AutomationElement ae, ScrollPattern scrollPattern, int percent, ScrollTypes horzVert)
        {
            bool vertical = ScrollTypes.Vertical == horzVert;
            var info = scrollPattern.Current;

            if (false == (vertical ? info.VerticallyScrollable : info.HorizontallyScrollable))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.LocalizedControlType}] is not scrollable {(vertical ? "vertically" : "horizontally")}.", application, GPALObjectType.Application);
            else
            {
                double page = vertical ? info.VerticalViewSize : info.HorizontalViewSize;
                double at = vertical ? info.VerticalScrollPercent : info.HorizontalScrollPercent;
                double was = -1;
                int steps = 0;

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: at [{at}], one view is [{page}] percent, walking to [{percent}].", application, GPALObjectType.Application);

                // whole views first
                while (page <= Math.Abs(percent - at) && at != was && steps < STEP_LIMIT)
                {
                    was = at;
                    at = OneStep(scrollPattern, vertical, percent > at, true);
                    steps++;
                }

                double off = Math.Abs(percent - at);
                was = -1;

                // then lines, stopping the moment a step no longer gets closer
                while (0 < off && at != was && steps < STEP_LIMIT)
                {
                    was = at;
                    at = OneStep(scrollPattern, vertical, percent > at, false);
                    steps++;

                    double closer = Math.Abs(percent - at);

                    if (closer >= off)
                        break;

                    off = closer;
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: landed at [{at}] after [{steps}] step(s).", application, GPALObjectType.Application);
            }
        }

        /// <summary>Steps a ScrollPattern once and answers with the new position on that axis.</summary>
        /// <param name="scrollPattern">The pattern to step</param>
        /// <param name="vertical">True to move vertically, false horizontally</param>
        /// <param name="forward">True to move down or right</param>
        /// <param name="large">True for a whole view, false for a line</param>
        /// <returns>The axis position as a percentage after the step</returns>
        private static double OneStep(ScrollPattern scrollPattern, bool vertical, bool forward, bool large)
        {
            ScrollAmount step = large
                ? (forward ? ScrollAmount.LargeIncrement : ScrollAmount.LargeDecrement)
                : (forward ? ScrollAmount.SmallIncrement : ScrollAmount.SmallDecrement);

            scrollPattern.Scroll(vertical ? ScrollAmount.NoAmount : step, vertical ? step : ScrollAmount.NoAmount);

            return vertical ? scrollPattern.Current.VerticalScrollPercent : scrollPattern.Current.HorizontalScrollPercent;
        }

        /// <summary>How many steps a percentage walk may take before it gives up on a control that will not land.</summary>
        private const int STEP_LIMIT = 500;

        /// <summary>
        /// Whether hardware input can reach the program: false on a hidden desktop, even while it is peeked at.
        /// </summary>
        /// <param name="application">The application being automated</param>
        /// <returns>True when the mouse and keyboard can reach it</returns>
        internal static bool HardwareReaches(Application application)
        {
            bool retVal = true;

            if (true == application.applicationSettings.HiddenDesktop)
                retVal = false;

            return retVal;
        }

        internal static void Scroll(Application application, AutomationElement ae, InteractionType interactionType, object amount, ScrollTypes incrementsPercent, ScrollTypes horzVert)
        {
            //UIAutomation only
            if ((Enums.InteractionType.Hardware == interactionType || true == application.applicationSettings.UseHardware) && true == HardwareReaches(application))
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Hardware scrolling is not supported at this time. Find the scroll controls and click them instead.", application, GPALObjectType.Application);
            else
            {
                // the element is a scrollbar, so it only goes one direction horizontal or vertical
                if (true == ae.TryGetCurrentPattern(RangeValuePattern.Pattern, out object rangeValuePattern))
                {
                    SetRangeValue(application, (RangeValuePattern)rangeValuePattern, ae, amount, incrementsPercent);
                }
                else if (true == ae.TryGetCurrentPattern(ScrollPattern.Pattern, out object scrollPattern))
                {
                    if (ScrollTypes.Percent == incrementsPercent)
                        ScrollToPercent(application, ae, (ScrollPattern)scrollPattern, (int)amount, horzVert);
                    else
                    {
                        ScrollAmount hAmount = ScrollTypes.Horizontal == horzVert ? (ScrollAmount)amount : ScrollAmount.NoAmount;
                        ScrollAmount vAmount = ScrollTypes.Vertical == horzVert ? (ScrollAmount)amount : ScrollAmount.NoAmount;

                        try
                        {
                            var before = ((ScrollPattern)scrollPattern).Current;

                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: scrollable h [{before.HorizontallyScrollable}] v [{before.VerticallyScrollable}], at h [{before.HorizontalScrollPercent}] v [{before.VerticalScrollPercent}], applying h [{hAmount}] v [{vAmount}].", application, GPALObjectType.Application);

                            ((ScrollPattern)scrollPattern).Scroll(hAmount, vAmount);

                            var after = ((ScrollPattern)scrollPattern).Current;

                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ScrollPattern on [{ae.Current.ControlType.LocalizedControlType}]: now at h [{after.HorizontalScrollPercent}] v [{after.VerticalScrollPercent}].", application, GPALObjectType.Application);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to Scroll on control [{ae.Current.ControlType.LocalizedControlType}].", application, GPALObjectType.Application, ex); ;
                        }
                    }
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.LocalizedControlType}] offers neither RangeValuePattern nor ScrollPattern, so UIAutomation cannot scroll it. Point the selector at its ScrollBar, which does.", application, GPALObjectType.Application);

            }
        }
        /// <summary>
        /// Fills in the given AutomationElement's value using its ValuePattern (UI Automation), or falls back to
        /// hardware emulation if the element doesn't support ValuePattern or hardware interaction is requested.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="ae">The AutomationElement to fill in</param>
        /// <param name="elementNode">The element node describing interaction type and click offsets, used for hardware fallback</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">How to write the text into the control (overwrite/insert/append)</param>
        internal static void FillInFrom(Application application, AutomationElement ae, UnitOfWork.ElementNode elementNode, string useText, WriteMode writeMode)
        {
            bool taken = false;

            if ((Enums.InteractionType.Hardware == elementNode.InteractionType || true == application.applicationSettings.UseHardware) && true == HardwareReaches(application))
            {
                HardwareFillInFrom(application, new GPALAutomationElement(ae), elementNode.OffsetX, elementNode.OffsetY, useText, writeMode);

                taken = true;
            }

            if (false == taken)
            {
                // LegacyIAccessiblePattern is only accessible via the UIAComWrapperX nuget package
                /*
                if (true == ae.TryGetCurrentPattern(System.Windows.Automation.LegacyIAccessiblePattern.Pattern, out object legacyPattern))
                {
                    // Set focus for input functionality and begin.
                    try
                    {
                        ae.SetFocus();
                    }
                    catch (Exception ex)
                    {
                        GPAL.GPALEventArgs args = new GPAL.GPALEventArgs()
                        {
                            GPALObject = application,
                            GPALObjectType = GPALObjectType.Application,
                            CurrentUOW = application.CurrentUOW,
                            AutomationElement = ae,
                            Message = $"Unable to set focus on Control {ae.Current.ControlType.LocalizedControlType}.",
                            ExceptionRaised = ex,
                            ScreenShot = ImageHelper.CaptureScreen(),
                            DateTimeStamp = DateTime.Now
                        };

                        GPAL.ExceptionHandler?.Invoke(application, args);
                    }
                    if (WriteMode.Overwrite == writeMode)
                        ((LegacyIAccessiblePattern)legacyPattern).SetValue(useText);
                    else if (WriteMode.Append == writeMode)
                    {
                        string content = ((LegacyIAccessiblePattern)legacyPattern).Current.Value;
                        content += useText;
                        ((LegacyIAccessiblePattern)legacyPattern).SetValue(content);
                    }
                    else if (WriteMode.Insert == writeMode)
                    {
                        string content = ((LegacyIAccessiblePattern)legacyPattern).Current.Value;
                        useText += content;
                        ((LegacyIAccessiblePattern)legacyPattern).SetValue(content);
                    }
                }
                else
                */
                // A control can offer ValuePattern and throw on write (a multi-line edit does). Try the write and fall
                // through on failure.
                if (true == ae.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                {
                    // Set focus for input functionality and begin.
                    try
                    {
                        ae.SetFocus();
                    }
                    catch (Exception)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to set focus on control [{ae.Current.ControlType.LocalizedControlType}], so the value is written to it without the focus.", application, GPALObjectType.Application);
                    }

                    try
                    {
                        string content = WriteMode.Overwrite == writeMode ? string.Empty : ((ValuePattern)valuePattern).Current.Value;

                        ((ValuePattern)valuePattern).SetValue(WriteMode.Append == writeMode ? content + useText
                                                            : WriteMode.Insert == writeMode ? useText + content
                                                            : useText);

                        taken = true;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ValuePattern on [{ae.Current.ControlType.ProgrammaticName}] would not take the text, so the next way of writing it is tried.", application, GPALObjectType.Application, ex);
                    }
                }

                // A control with no writable value is still a window, and a window holds its text.
                if (false == taken)
                    taken = SetNativeText(application, ae, useText, writeMode);

                if (false == taken)
                {
                    // Typing here is recovery, so NoFallbackRecoveryActions turns it off.
                    if (false == GPAL.NoFallbackRecoveryActions && true == HardwareReaches(application))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{ae.Current.ControlType.ProgrammaticName}] offers no way to write text into it, so it is typed into instead.", application, GPALObjectType.Application);

                        HardwareFillInFrom(application, new GPALAutomationElement(ae), elementNode.OffsetX, elementNode.OffsetY, useText, writeMode);
                    }
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ae.Current.ControlType.ProgrammaticName}] offers no way to write text into it, so it was left as it was.", application, GPALObjectType.Application);
                }
            }
        }
        /// <summary>
        /// Fills in text on the given element using hardware emulation: focuses/clicks the element, positions the
        /// caret/selection according to <paramref name="writeMode"/>, then sends the text as keystrokes.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to fill in</param>
        /// <param name="offsetX">X offset, relative to the element, to click before typing</param>
        /// <param name="offsetY">Y offset, relative to the element, to click before typing</param>
        /// <param name="text">The text to send</param>
        /// <param name="writeMode">How to write the text into the control (overwrite/insert/append)</param>
        public static void HardwareFillInFrom(Application application, GPALAutomationElement element, int offsetX, int offsetY, string text, WriteMode writeMode)
        {
            ApplicationHelper.TopProcess(application.Process);

            HardwareFocus(application, element, offsetX, offsetY);

            if (WriteMode.Overwrite == writeMode)
            {
                // clear the input, send CTRL-a (select all), then delete *altho we can prolly just typeover*
                HardwareHelper.SendChar('a', ModifierKeys.Control, true);
                Thread.Sleep(150);
                //                HardwareHelper.SendKey(GPAL.VK_DELETE);
            }
            else if (WriteMode.Append == writeMode)   // CTRL-End goto end of document
            {
                HardwareHelper.SendKey(GPAL.VK_END, ModifierKeys.Control);
                Thread.Sleep(150);
            }
            else if (WriteMode.Insert == writeMode)   // CTRL-End goto start of document
            {
                HardwareHelper.SendKey(GPAL.VK_HOME, ModifierKeys.Control);
                Thread.Sleep(150);
            }

            HardwareHelper.SendString(text, GPAL.TypingDelay);

            SettleInput(application);
        }

        /// <summary>
        /// Waits until the application has processed the input just sent.
        /// </summary>
        /// <remarks>
        /// keybd_event returns before the application reads the key. Returns once the process is input idle, or after
        /// INPUT_SETTLE_MS.
        /// </remarks>
        /// <param name="application">The Application the input was sent to</param>
        internal static void SettleInput(Application application)
        {
            try
            {
                application?.Process?.WaitForInputIdle(INPUT_SETTLE_MS);
            }
            catch (Exception)
            {
                // A process with no message loop, or one that has exited, has nothing to wait on. Neither is a failure.
            }
        }

        // Maximum wait for an application to process input. Only reached by a process that never reports idle.
        private const int INPUT_SETTLE_MS = 2_000;

        /// <summary>
        /// Focuses each element matched by the unit of work's selectors, with UIAutomation SetFocus, or with a
        /// hardware click when hardware is asked for.
        /// </summary>
        /// <param name="application">The Application whose selectors are focused</param>
        public static void Focus(Application application)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        if ((Enums.InteractionType.Hardware == selector.InteractionType || true == application.applicationSettings.UseHardware) && true == HardwareReaches(application))
                        {
                            int offsetX, offsetY;
                            offsetX = selector.SelectorSettings.OffsetX;
                            offsetY = selector.SelectorSettings.OffsetY;

                            HardwareFocus(application, elem, offsetX, offsetY);   // hardwre focus will try to click in text inputs
                        }
                        else
                        {

                            try
                            {
                                if (null != elem.Ae)
                                    elem.Ae.SetFocus();
                                else
                                    HardwareFocus(application, elem, 0, 0);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Focus threw on selector [{selector.Name}], so the focus did not move.", application, GPALObjectType.Application, ex);
                            }
                        }

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Moves the mouse to each element matched by the selectors in currentUOW. A hidden desktop has no
        /// mouse, so there it publishes a warning instead.
        /// </summary>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="currentUOW">The unit of work whose selectors are used, the current one or the persistent one</param>
        public static void Hover(Application application, UnitOfWork currentUOW)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in currentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, currentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    if (null != elem.Ae)
                    {
                        if (0 == elem.Ae.Current.BoundingRectangle.Width && 0 == elem.Ae.Current.BoundingRectangle.Height)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Element has Zero Width and Height, skipping", application, GPALObjectType.Application);
                            continue;
                        }

                        // Hover is only a mouse position, so a hidden desktop cannot be hovered. Report it instead of
                        // moving the real pointer.
                        if (false == HardwareReaches(application))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] cannot be moved to on a hidden desktop, since a hover is a mouse position and UIAutomation has no equivalent. Peek at the desktop to hover, or click, which does work there.", application, GPALObjectType.Application);
                        }
                        else if (Enums.InteractionType.Hardware == selector.InteractionType || true == application.applicationSettings.UseHardware)
                        {
                            // same as hardware moveto
                            HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is
                        }
                        else if (0 != elem.Ae.Current.BoundingRectangle.Width && 0 != elem.Ae.Current.BoundingRectangle.Height)
                        {
                            try
                            {
                                elem.Ae.SetFocus();
                                HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is
                            }
                            catch (Exception)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Focusing [{selector.Name}] threw, so it is moved to without the focus.", application, GPALObjectType.Application);
                                HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is
                            }
                        }

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
        }
        /// <summary>
        /// Moves the mouse cursor to each element matched by the selectors in the current unit of work, using
        /// hardware emulation or UI Automation focus depending on the selector's interaction type.
        /// </summary>
        /// <param name="application">The Application whose UOW elements the cursor is moved to</param>
        public static void MoveTo(Application application)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                // Moving to an element uses the mouse only. A hidden desktop cannot do this (or right click).
                if (false == HardwareReaches(application))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] cannot be moved to on a hidden desktop, since moving the pointer is the mouse and UIAutomation has no equivalent. Peek at the desktop to move there, or click, which does work.", application, GPALObjectType.Application);
                else
                    foreach (GPALAutomationElement elem in elems)
                    {
                        if (null != elem.Gae || null != elem.Ae)
                            HardwareMoveTo(application, elem.ImageMatch ?? elem, selector.OffsetX, selector.OffsetY);   // an image match moves to where the picture is

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
            }
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
        /// Moves the mouse pointer to the element, at the given offset from its top left corner.<br/>
        /// UIAutomation has no pointer position, so this is a mouse move and nothing else. Focus is the
        /// programmatic call and is a separate action.
        /// </summary>
        /// <param name="element">The element to move to</param>
        /// <param name="offsetX">X offset within the element</param>
        /// <param name="offsetY">Y offset within the element</param>
        /// <param name="appName">Application name, for the message when the move fails</param>
        private static void MoveTo(AutomationElement element, int offsetX, int offsetY, string appName)
        {
            try
            {
                HardwareMoveTo(null, new GPALAutomationElement(element), offsetX, offsetY);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"(ElementHelper): unable to move the mouse to the element in Application [{appName}].", element, GPALObjectType.Other, ex);
            }
        }
        /// <summary>
        /// Focuses the given element using hardware emulation: left-clicks it if it's a Text control, otherwise just moves the mouse to it.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to focus</param>
        /// <param name="OffsetX">X offset, relative to the element, to click/move to</param>
        /// <param name="OffsetY">Y offset, relative to the element, to click/move to</param>
        private static void HardwareFocus(Application application, GPALAutomationElement element, int OffsetX, int OffsetY)
        {
            if (null != element.Ae && ControlType.Text == element.Ae.Current.ControlType)
                HardwareClick(application, element, ClickType.LeftClick, ModifierKeys.NONE, OffsetX, OffsetY);
            else
                HardwareMoveTo(application, element, OffsetX, OffsetY);
        }
        /// <summary>
        /// Moves the mouse cursor to the given element's location plus offset, using hardware emulation. Same as hardware hover.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to move to</param>
        /// <param name="OffsetX">X offset, relative to the element's location</param>
        /// <param name="OffsetY">Y offset, relative to the element's location</param>
        private static System.Drawing.Point HardwareMoveTo(Application application, GPALAutomationElement element, int OffsetX, int OffsetY)
        {
            Rectangle rect2 = new Rectangle(), iframeRect = new Rectangle(0, 0, 0, 0);

            // Include the size. Without it the rectangle is a point and every click lands on the top-left border.
            if (null != element.Gae)
            {
                rect2.Location = element.Gae.Location;
                rect2.Size = element.Gae.Size;
            }
            else
            {
                rect2.Location = new System.Drawing.Point((int) element.Ae.Current.BoundingRectangle.Location.X, (int) element.Ae.Current.BoundingRectangle.Location.Y);
                rect2.Size = new System.Drawing.Size((int) element.Ae.Current.BoundingRectangle.Width, (int) element.Ae.Current.BoundingRectangle.Height);

                // TODO: does this mean anything in an application?
                //if (true == application.CurrentUOW.CurrentSelector?.SearchForSelector)
                //{
                //    // TODO: CAVEAT: is this good? we have to try to scroll the element on screen, but what happens if it can't find it?
                //    if (false == IsVisibleInViewport(element))
                //    {
                //        SendKey(GPAL.VK_HOME);   // home - go to document top, then scroll down

                //        while (false == IsVisibleInViewport(element) && false == IsEndOfPage(element))
                //        {
                //            SendKey(GPAL.VK_NEXT);   // pagedown
                //            Thread.Sleep(125);
                //        }
                //        rect2 = GetAbsCoordinates(application.BrowserSettings.BrowserType, element);
                //    }
                //}
            }

            // Where inside the element to aim. Zero offset means roughly the middle, as on the browser side; the corner
            // is the border or a row's blank margin. The jitter keeps repeated clicks off a single pixel.
            int offsetX = OffsetX;
            int offsetY = OffsetY;

            if (0 == offsetX && 0 == offsetY)
            {
                int spreadX = Math.Max(0, Math.Min(2, (rect2.Width / 2) - 1));
                int spreadY = Math.Max(0, Math.Min(2, (rect2.Height / 2) - 1));

                offsetX = rect2.Width / 2 + Jitter.Next(-spreadX, spreadX + 1);
                offsetY = rect2.Height / 2 + Jitter.Next(-spreadY, spreadY + 1);
            }

            System.Drawing.Point aimedAt = new System.Drawing.Point(rect2.X + offsetX, rect2.Y + offsetY);

            // A control with no size: use UIAutomation's clickable point. Fallback only, since it ignores the
            // workflow's offset.
            if ((0 == rect2.Width || 0 == rect2.Height)
                && null != element.Ae
                && true == element.Ae.TryGetClickablePoint(out System.Windows.Point clickablePoint))
                aimedAt = new System.Drawing.Point((int)clickablePoint.X, (int)clickablePoint.Y);

            if (true == GPAL.SimulateMouse || true == application.CurrentUOW?.CurrentSelector?.SimulateMouse)
                HardwareHelper.MoveMouse(aimedAt.X, aimedAt.Y, 10, 10);    // source didn't define values for rx, ry
            else
                HardwareHelper.MoveMouse(aimedAt.X, aimedAt.Y);

            // Return the point the pointer moved to so the caller clicks there.
            return aimedAt;
        }

        private static Random _jitter;
        private static Random Jitter => _jitter ?? (_jitter = new Random(Guid.NewGuid().GetHashCode()));
        /// <summary>
        /// Left clicks each element matched by the unit of work's selectors.
        /// </summary>
        /// <param name="application">The Application whose selectors are clicked</param>
        /// <param name="modifierKeys">Keys held down during the click</param>
        public static void LeftClick(Application application, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;   // we don't look up data literals

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                // FindElements calls CallIF handlers which can handle an action and delete a node, check here before moving on
                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);    // either All or just those that matched

                int rowCount = 0;

                foreach (GPALAutomationElement elem in elems)
                {
                    Click(application, selector, elem, ClickType.LeftClick, modifierKeys);

                    if (++rowCount >= HowManyToActOn(application))
                        break;
                }
            }
        }
        /// <summary>
        /// Performs the given click type on every element matched by the selectors in the current unit of work.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are clicked</param>
        /// <param name="clickType">The type of click to perform (left, double, middle, right, etc.)</param>
        /// <returns>True if at least one click was performed</returns>
        public static bool GenericClick(Application application, ClickType clickType)
        {
            List<GPALAutomationElement> matchedElements;
            bool matchedAll = false;
            bool clickPerformed = false;

            foreach (Selector sel in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != sel.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindElements(application, application.CurrentUOW, sel, out matchedAll, out matchedElements);

                if (0 == (matchedElements?.Count ?? 0))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{sel.Name}] matched no elements, so there was nothing to [{clickType}].", application, GPALObjectType.Application);

                if (null != matchedElements)
                {
                    int rowCount = 0;

                    foreach (GPALAutomationElement elem in matchedElements)
                    {
                        Click(application, sel, elem, clickType, ModifierKeys.NONE);
                        clickPerformed = true;

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }
            return clickPerformed;
        }
        /// <summary>
        /// Clicks the given element, dispatching to hardware emulation or UI Automation invocation depending on
        /// the selector's interaction type and whether the element is a GPALAutomationElement (Gae).
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="selector">The selector that found the element, used for offsets and interaction type</param>
        /// <param name="element">The element to click</param>
        /// <param name="clickType">The type of click to perform (left, double, middle, right, etc.)</param>
        /// <param name="modifierKeys">Modifier key to hold during the click</param>
        public static void Click(Application application, Selector selector, GPALAutomationElement element, ClickType clickType, ModifierKeys modifierKeys)
        {
            int offsetX, offsetY;

            // Click a control found by image at the matched point, not its middle. On a desktop a point is clicked with
            // the mouse.
            if (null != element.ImageMatch)
                element = element.ImageMatch;
            offsetX = selector.SelectorSettings.OffsetX;
            offsetY = selector.SelectorSettings.OffsetY;

            // A menu item found through the Win32 menu is clicked by sending its command.
            if (0 != element.MenuCommand)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Selector [{selector.Name}] is a menu item, so command [{element.MenuCommand}] was sent to the window it belongs to.", application, GPALObjectType.Application);

                PostMessage(element.MenuWindow, WM_COMMAND, new IntPtr(element.MenuCommand), IntPtr.Zero);
            }
            // Right click has no UIAutomation pattern and needs the mouse, so a hidden desktop cannot be right-clicked.
            else if (ClickType.RightClick == clickType && false == HardwareReaches(application))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] cannot be right clicked on a hidden desktop, since UIAutomation offers no pattern for one. Peek at the desktop to right click there.", application, GPALObjectType.Application);
            }
            else if ((Enums.InteractionType.Hardware == selector.InteractionType || null != element.Gae || true == application.applicationSettings.UseHardware || ClickType.RightClick == clickType)
                     && true == HardwareReaches(application))
                HardwareClick(application, element, clickType, modifierKeys, offsetX, offsetY);
            else if (null != element.Ae)
            {
                UIAutomationClick(application, selector, element, clickType, modifierKeys);
            }
            // An image match is a screen position, so only the mouse can click it, and the mouse cannot reach a hidden
            // desktop.
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selector [{selector.Name}] is an image match, which only the mouse can click, and the mouse cannot reach [{application.applicationSettings.HiddenDesktopName}]. Peek at the desktop to click there.", application, GPALObjectType.Application);
        }

        /// <summary>
        /// Clicks through UIAutomation when the control offers a pattern, otherwise with the mouse.<br/>
        /// Invoke is the control's <i>default action</i>, not always a left click. On a list, tree, data or tab item a
        /// left click selects and a double click activates. So a left click on a selectable item uses
        /// SelectionItemPattern.Select, and a double click uses Invoke. A double click on an item with children and no
        /// Invoke, such as a WinForms tree item, opens or closes it through ExpandCollapsePattern.<br/>
        /// Middle, right and modified clicks have no pattern (Shift range selection has none), so they use the mouse.<br/>
        /// A control with no pattern uses the mouse; a pattern that throws falls back to the mouse and reports it.<br/>
        /// <b>NOTE:</b> a control that can only be clicked with the mouse cannot be driven on a hidden desktop.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="selector">The selector that found the element, used for offsets on the mouse route</param>
        /// <param name="element">The element to click</param>
        /// <param name="clickType">The type of click to perform</param>
        /// <param name="modifierKeys">Modifier key to hold on the mouse route</param>
        public static void UIAutomationClick(Application application, Selector selector, GPALAutomationElement element, ClickType clickType, ModifierKeys modifierKeys)
        {
            object objPattern;
            bool invoked = false;
            string control = element.Ae.Current.LocalizedControlType;

            if (ModifierKeys.NONE != modifierKeys)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"A click holding [{Browser.Browser.GetModifierKeys(modifierKeys)}] has no UIAutomation equivalent, so [{control}] is clicked with the mouse.", application, GPALObjectType.Application);
            else if (ClickType.LeftClick == clickType && true == element.Ae.TryGetCurrentPattern(SelectionItemPattern.Pattern, out objPattern))
            {
                // A left click on a selectable item selects it. Invoke is the double-click action, so do not use it
                // here.
                try
                {
                    ((SelectionItemPattern)objPattern).Select();
                    invoked = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked through SelectionItemPattern.", application, GPALObjectType.Application);
                }
                catch (Exception)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"SelectionItemPattern threw on control [{control}], falling back to the mouse.", application, GPALObjectType.Application);
                }
            }
            else if ((ClickType.LeftClick == clickType || ClickType.LeftDoubleClick == clickType)
                     && true == element.Ae.TryGetCurrentPattern(InvokePattern.Pattern, out objPattern))
            {
                // Invoke: a left click on a button, or a double click on anything with InvokePattern.
                try
                {
                    ((InvokePattern)objPattern).Invoke();
                    invoked = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked through InvokePattern.", application, GPALObjectType.Application);
                }
                catch (Exception)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"InvokePattern threw on control [{control}], falling back to the mouse.", application, GPALObjectType.Application);
                }
            }
            else if (ClickType.LeftDoubleClick == clickType
                     && true == element.Ae.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out objPattern)
                     && ExpandCollapseState.LeafNode != ((ExpandCollapsePattern)objPattern).Current.ExpandCollapseState)
            {
                // A double click on a tree item with children opens it, or closes it when open. A WinForms tree item
                // offers no InvokePattern, so this is the double click without the mouse.
                try
                {
                    ExpandCollapsePattern expandCollapse = (ExpandCollapsePattern)objPattern;

                    if (ExpandCollapseState.Expanded == expandCollapse.Current.ExpandCollapseState)
                        expandCollapse.Collapse();
                    else
                        expandCollapse.Expand();

                    invoked = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked through ExpandCollapsePattern.", application, GPALObjectType.Application);
                }
                catch (Exception)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ExpandCollapsePattern threw on control [{control}], falling back to the mouse.", application, GPALObjectType.Application);
                }
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Control [{control}] offers no pattern for a [{clickType}], so the mouse is how it is clicked.", application, GPALObjectType.Application);

            // No InvokePattern, or anything but a plain left click: use the mouse.
            if (false == invoked)
                HardwareClick(application, element, clickType, modifierKeys, selector?.OffsetX ?? 5, selector?.OffsetY ?? 5); // CAVEAT: if .ForSelector is not provided, default to 5
        }

        /// <summary>
        /// Returns an element's name for click messages, or its control type when it has no name.
        /// </summary>
        /// <param name="element">The element being clicked</param>
        /// <returns>The name, or the control type when there is no name</returns>
        private static string NameOf(GPALAutomationElement element)
        {
            string retVal = null == element || null == element.Ae ? "nothing" : element.Ae.Current.Name;

            if (true == string.IsNullOrWhiteSpace(retVal) && null != element && null != element.Ae)
                retVal = element.Ae.Current.ControlType.ProgrammaticName;

            return retVal;
        }

        /// <summary>
        /// Clicks the given element using hardware emulation: moves the mouse to the element (at the given offset,
        /// or a random point within it if no offset is given) and performs the click, optionally holding a modifier key.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="element">The element to click</param>
        /// <param name="clickType">The type of click to perform (left, double, middle, right, etc.)</param>
        /// <param name="modifierKeys">Modifier key to hold during the click</param>
        /// <param name="OffsetX">X offset, relative to the element, to click at; if 0 and <paramref name="OffsetY"/> is 0, a random point within the element is used</param>
        /// <param name="OffsetY">Y offset, relative to the element, to click at; if 0 and <paramref name="OffsetX"/> is 0, a random point within the element is used</param>
        public static void HardwareClick(Application application, GPALAutomationElement element, ClickType clickType, ModifierKeys modifierKeys, int OffsetX = 0, int OffsetY = 0)
        {
            try
            {
                if (ModifierKeys.NONE != modifierKeys)
                    HardwareHelper.PressModifierKey(modifierKeys);

                // The move returns the point it aimed at, so the click lands where the pointer is.
                System.Drawing.Point clickAt = HardwareMoveTo(application, element, OffsetX, OffsetY);

                // Publish between modifier down and up, where the click happens.
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Element [{NameOf(element)}] [{clickType}] clicked with modifiers [{Browser.Browser.GetModifierKeys(modifierKeys)}] at [{clickAt.X},{clickAt.Y}] with the mouse.", application, GPALObjectType.Application);

                HardwareHelper.HardwareClick(clickAt.X, clickAt.Y, clickType);
            }
            catch
            {

            }
            finally
            {
                if (ModifierKeys.NONE != modifierKeys)
                    HardwareHelper.ReleaseModifierKeys(modifierKeys);
            }
        }
        /// <summary>
        /// Performs a drag and drop on each element matched by the selectors in the current unit of work, using
        /// hardware mouse down/up at the element's location plus the selector's offset and delta, optionally holding a modifier key.
        /// </summary>
        /// <param name="application">The Application whose UOW elements are dragged</param>
        /// <param name="modifierKeys">Modifier key to hold during the drag and drop</param>
        public static void DragAndDrop(Application application, ModifierKeys modifierKeys = ModifierKeys.NONE)
        {
            var selectors = new List<Selector>();

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
                if (SelectorType.Selector == selector.SelectorType)
                    selectors.Add(selector);

            // Two selectors give the ends of the drag: grab the first, release over the second. The distance comes from
            // the elements' positions, so it survives resizes, re-sorts and other screens.
            if (2 == selectors.Count)
                DragToTarget(application, selectors[0], selectors[1], modifierKeys);
            else
                DragByDelta(application, modifierKeys);
        }

        /// <summary>
        /// Drags the first selector's element onto the second's.
        /// </summary>
        /// <remarks>
        /// Each end is the element's middle unless its selector gives an offset. The target's DeltaX/DeltaY are added
        /// to the drop point.
        /// </remarks>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="source">The selector for the element to drag</param>
        /// <param name="target">The selector for the element to drop it on</param>
        /// <param name="modifierKeys">Modifier key to hold for the duration of the drag</param>
        private static void DragToTarget(Application application, Selector source, Selector target, ModifierKeys modifierKeys)
        {
            Rectangle from = FirstRectangle(application, source);
            Rectangle to = FirstRectangle(application, target);

            if (true == from.IsEmpty || true == to.IsEmpty)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"A drag needs both ends. Selector [{source.Name}] {(true == from.IsEmpty ? "matched nothing" : "matched")} and selector [{target.Name}] {(true == to.IsEmpty ? "matched nothing" : "matched")}.", application, GPALObjectType.Application);
            else
            {
                // Grab the middle unless the selector gives an offset. The corner of a list row is blank margin, and
                // dragging from there makes a rubber-band selection.
                int grabX = from.X + (0 == source.OffsetX ? from.Width / 2 : source.OffsetX);
                int grabY = from.Y + (0 == source.OffsetY ? from.Height / 2 : source.OffsetY);

                int dropX = to.X + (0 == target.OffsetX ? to.Width / 2 : target.OffsetX) + target.DeltaX;
                int dropY = to.Y + (0 == target.OffsetY ? to.Height / 2 : target.OffsetY) + target.DeltaY;

                // A held key is system-wide; release it in a finally.
                try
                {
                    HardwareHelper.ReleaseStrayModifiers(modifierKeys);

                    if (ModifierKeys.NONE != modifierKeys)
                        HardwareHelper.PressModifierKey(modifierKeys);

                    HardwareHelper.MouseDown(grabX, grabY, ClickType.LeftClick);
                    HardwareHelper.DragTravel(dropX, dropY, true == GPAL.SimulateMouse || true == source.SimulateMouse);
                    HardwareHelper.MouseUp(dropX, dropY, ClickType.LeftClick);
                }
                finally
                {
                    if (ModifierKeys.NONE != modifierKeys)
                        HardwareHelper.ReleaseModifierKeys(modifierKeys);
                }

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Dragged [{source.Name}] from [{grabX},{grabY}] onto [{target.Name}] at [{dropX},{dropY}]", application, GPALObjectType.Application);
            }
        }

        /// <summary>
        /// Where the first element a selector matched sits on screen.
        /// </summary>
        /// <param name="application">The Application being searched</param>
        /// <param name="selector">The selector to resolve</param>
        /// <returns>The element's screen rectangle, empty when the selector matched nothing</returns>
        private static Rectangle FirstRectangle(Application application, Selector selector)
        {
            Rectangle retVal = Rectangle.Empty;

            ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out List<GPALAutomationElement> matchedElements);

            if (null != matchedElements && 0 < matchedElements.Count)
                elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

            if (false == selector.DeleteMe && null != elems && 0 < elems.Count)
            {
                // a control found under an image is dragged from, or dropped on, where the picture is
                GPALAutomationElement elem = elems[0].ImageMatch ?? elems[0];

                if (null != elem.Gae)
                    retVal = new Rectangle(elem.Gae.Location, elem.Gae.Size);
                else
                    retVal = new Rectangle(
                        new System.Drawing.Point((int)elem.Ae.Current.BoundingRectangle.X, (int)elem.Ae.Current.BoundingRectangle.Y),
                        new System.Drawing.Size((int)elem.Ae.Current.BoundingRectangle.Width, (int)elem.Ae.Current.BoundingRectangle.Height));
            }
            else
                PublishToEventHandler(application, selector, elems?.ToList());

            return retVal;
        }

        /// <summary>
        /// Drags each matched element by its selector's distance, for a destination that is not an element: a slider, a
        /// splitter, a rubber-band selection.
        /// </summary>
        /// <param name="application">The Application the elements belong to</param>
        /// <param name="modifierKeys">Modifier key to hold for the duration of the drag</param>
        private static void DragByDelta(Application application, ModifierKeys modifierKeys)
        {
            List<GPALAutomationElement> matchedElements;

            foreach (Selector selector in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != selector.SelectorType)
                    continue;

                ReadOnlyCollection<GPALAutomationElement> elems = ElementHelper.FindElements(application, application.CurrentUOW, selector, out bool matchedAll, out matchedElements);

                if (true == selector.DeleteMe || null == elems)
                {
                    PublishToEventHandler(application, selector, elems?.ToList());
                    continue;
                }

                if (null != matchedElements)
                    elems = new ReadOnlyCollection<GPALAutomationElement>(matchedElements);

                int rowCount = 0;

                foreach (GPALAutomationElement found in elems)
                {
                    Rectangle rect;

                    // a control found under an image is dragged from where the picture is
                    GPALAutomationElement elem = found.ImageMatch ?? found;

                    if (null != elem.Gae)
                        rect = new Rectangle(elem.Gae.Location, elem.Gae.Size);
                    else
                        rect = new Rectangle(
                            new System.Drawing.Point((int)elem.Ae.Current.BoundingRectangle.X, (int)elem.Ae.Current.BoundingRectangle.Y),
                            new System.Drawing.Size((int)elem.Ae.Current.BoundingRectangle.Width, (int)elem.Ae.Current.BoundingRectangle.Height));

                    // Mouse down at element origin + OffsetX/Y, move by DeltaX/Y, mouse up. Windows only starts a drag
                    // when the pointer moves past its threshold with the button held; down then up is a click. The
                    // browser side does the same.
                    // Grab the middle unless the selector gives an offset, as the two-selector drag does.
                    int grabX = rect.X + (0 == selector.OffsetX ? rect.Width / 2 : selector.OffsetX);
                    int grabY = rect.Y + (0 == selector.OffsetY ? rect.Height / 2 : selector.OffsetY);

                    int endX = grabX + selector.DeltaX;
                    int endY = grabY + selector.DeltaY;

                    // a held key is machine wide, so it is released in a finally whatever happens in between
                    try
                    {
                        HardwareHelper.ReleaseStrayModifiers(modifierKeys);

                        if (ModifierKeys.NONE != modifierKeys)
                            HardwareHelper.PressModifierKey(modifierKeys);

                        HardwareHelper.MouseDown(grabX, grabY, ClickType.LeftClick);
                        HardwareHelper.DragTravel(endX, endY, true == GPAL.SimulateMouse || true == selector.SimulateMouse);
                        HardwareHelper.MouseUp(endX, endY, ClickType.LeftClick);
                    }
                    finally
                    {
                        if (ModifierKeys.NONE != modifierKeys)
                            HardwareHelper.ReleaseModifierKeys(modifierKeys);
                    }

                    if (++rowCount >= HowManyToActOn(application))
                        break;
                }
            }
        }

        /// <summary>
        /// Expands a collapsed menu item (e.g. a submenu), trying ExpandCollapsePattern, then InvokePattern,
        /// then a hardware click fallback at a random point within the element. If <see cref="GPAL.SimulateMouse"/>
        /// or the current selector requests it, moves and clicks the hardware mouse over the element instead.
        /// </summary>
        /// <param name="application">The Application the element belongs to</param>
        /// <param name="automationElement">The menu item element to expand</param>
        public static void ExpandMenuItem(Application application, AutomationElement automationElement)
        {
            if (true == GPAL.SimulateMouse || true == application.CurrentUOW.CurrentSelector?.SimulateMouse)
            {
                HardwareHelper.MoveMouse((int)automationElement.Current.BoundingRectangle.X, (int)automationElement.Current.BoundingRectangle.Y, 10, 10);    // source didn't define values for rx, ry            }
                HardwareHelper.LeftClick((int)automationElement.Current.BoundingRectangle.X, (int)automationElement.Current.BoundingRectangle.Y);
                Thread.Sleep(250); // wait for display before trying to find elements
            }
            else
            {
                try
                {
                    ExpandCollapsePattern pattern = automationElement.GetCurrentPattern(ExpandCollapsePattern.Pattern) as ExpandCollapsePattern;
                    pattern.Expand();
                }
                catch (Exception)
                {
                    try
                    {
                        InvokePattern pattern = automationElement.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                        pattern.Invoke();
                    }
                    catch (Exception ex2)
                    {
                        // A menu with neither pattern only opens with the mouse, if at all. Give up quietly; the Win32
                        // menu is searched next. Do not query the element: invoking a menu item can remove it, and a
                        // removed element throws.
                        if (false == HardwareReaches(application))
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, "A menu would not open through UIAutomation and the mouse cannot open it either, so what is on it is read from the window's own menu.", application, GPALObjectType.Application, ex2);
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to expand menuitem for Control [{automationElement.Current.LocalizedControlType}], using hardware fallback.", application, GPALObjectType.Application);

                            Random random = new Random(10007);

                            int offsetX = random.Next(1, (int)automationElement.Current.BoundingRectangle.Width - 2);
                            int offsetY = random.Next(1, (int)automationElement.Current.BoundingRectangle.Height - 2);
                            ElementHelper.HardwareClick(null, new GPALAutomationElement(automationElement), Enums.ClickType.LeftClick, Enums.ModifierKeys.NONE, offsetX, offsetY);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Switch to whatever tab controls are defined in the current UOW
        /// </summary>
        /// <param name="application"></param>
        public static void SwitchToTab(Application application)
        {
            List<GPALAutomationElement> matchedElements;
            bool matchedAll = false;

            foreach (Selector sel in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != sel.SelectorType)
                    continue;   // we don't look up data literals

                ElementHelper.FindElements(application, application.CurrentUOW, sel, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                    int rowCount = 0;

                    // A tab is switchable because it offers selection. LocalizedControlType is in the user's language,
                    // so it is not tested.
                    foreach (GPALAutomationElement elem in matchedElements)
                    {
                        if (null == elem.Ae)
                            continue;

                        if (true == elem.Ae.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selectionPattern))
                        {
                            try
                            {
                                // Publish the tab, whether it was already selected, and that the pattern switched it;
                                // the switch is invisible on screen.
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Switching to [{elem.Ae.Current.Name}] through SelectionItemPattern, which reads Selected [{elem.Selected}] before the switch.", application, GPALObjectType.Application);

                                ((SelectionItemPattern)selectionPattern).Select();

                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{elem.Ae.Current.Name}] reads Selected [{elem.Selected}] after the switch.", application, GPALObjectType.Application);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to use SelectionPattern to select the tab [{elem.Ae.Current.Name}], falling back on hardware click.", application, GPALObjectType.Application, ex);

                                if (true == MsaaHelper.Select(application, elem.Ae))
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Switched to [{elem.Ae.Current.Name}] through MSAA.", application, GPALObjectType.Application);
                                else if (true == HardwareReaches(application))
                                    HardwareClick(application, elem, ClickType.LeftClick, ModifierKeys.NONE, sel.OffsetX, sel.OffsetY);
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The tab [{elem.Ae.Current.Name}] was not switched to. Neither SelectionPattern nor MSAA would select it and the mouse cannot reach it here.", application, GPALObjectType.Application);
                            }
                        }
                        else if (true == MsaaHelper.Select(application, elem.Ae))
                        {
                            // This runs only when the tab reads as selected afterwards, so the switch is confirmed. A
                            // refusal costs one read and falls through to the mouse.
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Switched to [{elem.Ae.Current.Name}] through MSAA.", application, GPALObjectType.Application);
                        }
                        else if (true == HardwareReaches(application))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{elem.Ae.Current.Name}] publishes no selection through UIAutomation or MSAA, so the tab is switched to with the mouse.", application, GPALObjectType.Application);

                            HardwareClick(application, elem, ClickType.LeftClick, ModifierKeys.NONE, sel.OffsetX, sel.OffsetY);
                        }
                        else
                        {
                            // No selection pattern and no mouse: warn, since the workflow is still on its original tab.
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The tab [{elem.Ae.Current.Name}] was not switched to. It publishes no selection through UIAutomation or MSAA, so switching it is the mouse, and the mouse cannot reach [{application.applicationSettings.HiddenDesktopName}] while it is hidden.", application, GPALObjectType.Application);
                        }

                        if (++rowCount >= HowManyToActOn(application))
                            break;
                    }
                }
            }

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

        /// <summary>
        /// Returns the element a value can be written to: the found element, or the first element inside it that
        /// accepts a value.
        /// </summary>
        /// <param name="application">The Application being driven</param>
        /// <param name="element">The element the selector found</param>
        /// <returns>The element to write to; the given element when it accepts a value itself</returns>
        private static AutomationElement WritableFor(Application application, AutomationElement element)
        {
            AutomationElement retVal = element;

            if (false == element.TryGetCurrentPattern(RangeValuePattern.Pattern, out object unused)
                && false == element.TryGetCurrentPattern(ValuePattern.Pattern, out unused))
            {
                AutomationElement inside = element.FindFirst(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.IsRangeValuePatternAvailableProperty, true),
                    new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true)));

                if (null != inside)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] carries no value of its own, so the [{inside.Current.ControlType.ProgrammaticName}] inside it is what takes one.", application, GPALObjectType.Application);

                    retVal = inside;
                }
            }

            return retVal;
        }

        // Search the control's child windows. WinForms publishes a spinner as just two buttons, so its edit box is
        // found through Win32.
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr lParam);

        private delegate bool EnumChildProc(IntPtr handle, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SendMessage(IntPtr handle, int message, int wParam, System.Text.StringBuilder lParam);

        private const int WM_KILLFOCUS = 0x0008;
        private const int WM_SETTEXT = 0x000C;
        private const int WM_GETTEXT = 0x000D;
        private const int WM_GETTEXTLENGTH = 0x000E;
        private const int EM_SETSEL = 0x00B1;
        private const int EM_REPLACESEL = 0x00C2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private const int SCI_GETLENGTH = 2006;
        private const int SCI_GETCODEPAGE = 2137;
        private const int SCI_SETSEL = 2160;
        private const int SCI_REPLACESEL = 2170;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        /// <summary>
        /// Inserts text at the start or the end of a Scintilla editor (Notepad++), through Scintilla's own messages.
        /// </summary>
        /// <remarks>
        /// Scintilla reads EM_REPLACESEL's text as 8-bit characters, and Windows hands it UTF-16, so it takes the first
        /// character only. SCI_REPLACESEL takes the text in the document's encoding, and Windows copies nothing across
        /// processes for a message it does not know, so the text is written into the editor's process first.
        /// </remarks>
        /// <param name="handle">The Scintilla window</param>
        /// <param name="atEnd">True to add after what is there, false to put it before</param>
        /// <param name="useText">The text to insert</param>
        private static void ScintillaInsert(IntPtr handle, bool atEnd, string useText)
        {
            IntPtr at = true == atEnd ? SendMessage(handle, SCI_GETLENGTH, IntPtr.Zero, IntPtr.Zero) : IntPtr.Zero;
            bool utf8 = 65001 == SendMessage(handle, SCI_GETCODEPAGE, IntPtr.Zero, IntPtr.Zero).ToInt32();
            byte[] bytes = (true == utf8 ? System.Text.Encoding.UTF8 : System.Text.Encoding.Default).GetBytes(useText + "\0");

            GetWindowThreadProcessId(handle, out uint processId);

            IntPtr process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE, false, processId);

            // Nothing written means nothing sent; the read-back afterwards finds the text missing and it is typed.
            if (IntPtr.Zero != process)
            {
                IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, new UIntPtr((uint)bytes.Length), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (IntPtr.Zero != remote)
                {
                    if (true == WriteProcessMemory(process, remote, bytes, new UIntPtr((uint)bytes.Length), out UIntPtr unused))
                    {
                        SendMessage(handle, SCI_SETSEL, at, at);
                        SendMessage(handle, SCI_REPLACESEL, IntPtr.Zero, remote);
                    }

                    VirtualFreeEx(process, remote, UIntPtr.Zero, MEM_RELEASE);
                }

                CloseHandle(process);
            }
        }

        /// <summary>
        /// The text a window is holding, read from the window rather than from anything it publishes.
        /// </summary>
        /// <param name="handle">The window to read</param>
        /// <returns>Its text, empty when it has none</returns>
        private static string NativeTextOf(IntPtr handle)
        {
            int length = SendMessage(handle, WM_GETTEXTLENGTH, 0, null);
            System.Text.StringBuilder text = new System.Text.StringBuilder(length + 1);

            SendMessage(handle, WM_GETTEXT, text.Capacity, text);

            return text.ToString();
        }

        /// <summary>
        /// Whether two texts are the same once their line ends are made alike; a rich edit keeps CR where an edit keeps
        /// CR LF.
        /// </summary>
        /// <param name="held">The text read back from the window</param>
        /// <param name="wanted">The text it should hold</param>
        /// <returns>True when they match line for line</returns>
        private static bool SameLines(string held, string wanted)
        {
            return string.Equals(held.Replace("\r\n", "\n").Replace('\r', '\n'), wanted.Replace("\r\n", "\n").Replace('\r', '\n'));
        }

        /// <summary>
        /// Writes text through the window holding it, for a control with no writable value, such as a multi-line edit
        /// (Notepad's text area). No input is used, so it works on a hidden desktop.
        /// </summary>
        /// <remarks>
        /// For edits whose ValuePattern is read only, such as Notepad's. A replace uses WM_SETTEXT. Append and Insert
        /// put the caret at the end or the start and insert there with EM_REPLACESEL, so the text already there is
        /// never written back.
        /// </remarks>
        /// <param name="application">The application being automated</param>
        /// <param name="element">The control to write into</param>
        /// <param name="useText">The text to write</param>
        /// <param name="writeMode">Whether it replaces what is there, goes before it or goes after it</param>
        /// <returns>True when the window holds the text as asked</returns>
        private static bool SetNativeText(Application application, AutomationElement element, string useText, WriteMode writeMode)
        {
            bool retVal = false;
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            // WM_SETTEXT replaces the whole text, so adding to it would mean writing back what was there, and a window
            // can store part of a write and still return TRUE. EM_REPLACESEL inserts at the caret and leaves the rest
            // alone. Read back, with line ends made alike: a rich edit keeps a line end as CR alone.
            if (WriteMode.Overwrite != writeMode && IntPtr.Zero != handle)
            {
                string before = NativeTextOf(handle);
                System.Text.StringBuilder className = new System.Text.StringBuilder(64);

                GetClassName(handle, className, className.Capacity);

                if ("Scintilla" == className.ToString())
                    ScintillaInsert(handle, WriteMode.Append == writeMode, useText);
                else
                {
                    IntPtr at = new IntPtr(WriteMode.Append == writeMode ? before.Length : 0);   // a position past the end is the end

                    SendMessage(handle, EM_SETSEL, at, at);
                    SendMessage(handle, EM_REPLACESEL, new IntPtr(1), useText);
                }

                retVal = SameLines(NativeTextOf(handle), WriteMode.Append == writeMode ? before + useText : useText + before);

                if (true == retVal)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] publishes no value that can be written, so the text was inserted through the window holding it.", application, GPALObjectType.Application);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{element.Current.ControlType.ProgrammaticName}] did not take the text inserted through its window, so it is typed into instead.", application, GPALObjectType.Application);
            }
            else if (IntPtr.Zero != handle)
            {
                // Read the text back instead of trusting TRUE. A window that reads the message as bytes stores only the
                // first character of a wide string and still returns TRUE.
                bool answered = IntPtr.Zero != SendMessage(handle, WM_SETTEXT, IntPtr.Zero, useText);

                retVal = true == answered
                      && (true == string.IsNullOrEmpty(useText) || true == NativeTextOf(handle).Contains(useText));

                if (true == retVal)
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] publishes no value that can be written, so the text was written to the window holding it.", application, GPALObjectType.Application);
                else if (true == answered)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{element.Current.ControlType.ProgrammaticName}] said it took the text written to its window but is not holding it, so it is typed into instead.", application, GPALObjectType.Application);
            }

            return retVal;
        }

        /// <summary>
        /// Returns the child window inside a control that holds its value, when UIAutomation shows no value in the
        /// tree.
        /// </summary>
        /// <remarks>
        /// A WinForms spinner lists only its buttons in UIAutomation; its edit is reached by window handle.
        /// </remarks>
        /// <param name="element">The control that offered no value of its own</param>
        /// <returns>The element for the window holding the value; null when no child window holds one</returns>
        private static AutomationElement NativeWritableFor(AutomationElement element)
        {
            AutomationElement retVal = null;
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            if (IntPtr.Zero != handle)
            {
                List<IntPtr> inside = new List<IntPtr>();

                EnumChildWindows(handle, (child, unused) => { inside.Add(child); return true; }, IntPtr.Zero);

                foreach (IntPtr child in inside)
                {
                    AutomationElement fromWindow = AutomationElement.FromHandle(child);

                    // Use ValuePattern only. These windows also offer RangeValuePattern with a wrong range that throws
                    // for any useful value.
                    if (true == fromWindow.TryGetCurrentPattern(ValuePattern.Pattern, out object unused))
                    {
                        retVal = fromWindow;
                        break;
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// Tells a control it lost focus so it reads what was written, as when a person types a number and clicks away.
        /// </summary>
        /// <remarks>
        /// A spinner reads its box only when it loses focus.
        /// </remarks>
        /// <param name="element">The element that was written into</param>
        private static void Commit(AutomationElement element)
        {
            IntPtr handle = new IntPtr(element.Current.NativeWindowHandle);

            if (IntPtr.Zero != handle)
                SendMessage(handle, WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Writes a value into the window inside a control that publishes no value to UIAutomation, then tells the box
        /// focus has left so a spinner reads it.
        /// </summary>
        /// <param name="application">The application being automated</param>
        /// <param name="element">The control that offered no value of its own</param>
        /// <param name="value">The value to write</param>
        /// <returns>True when a child window took the value</returns>
        private static bool SetNativeValue(Application application, AutomationElement element, string value)
        {
            bool retVal = false;
            AutomationElement writable = NativeWritableFor(element);

            if (null != writable && true == writable.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
            {
                ((ValuePattern)valuePattern).SetValue(value);

                Commit(writable);

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.ControlType.ProgrammaticName}] keeps its value in a window UIAutomation does not list, so [{value}] was written there and the control was told to read it.", application, GPALObjectType.Application);

                retVal = true;
            }

            return retVal;
        }

        public static void SetRange(Application application, double rangeValue)
        {
            List<GPALAutomationElement> matchedElements;
            bool matchedAll = false;

            foreach (Selector sel in application.CurrentUOW.WithSelectorList)
            {
                if (SelectorType.Selector != sel.SelectorType)
                    continue;

                ElementHelper.FindElements(application, application.CurrentUOW, sel, out matchedAll, out matchedElements);

                if (null != matchedElements)
                {
                    foreach (GPALAutomationElement elem in matchedElements)
                    {
                        if (null == elem.Ae) continue;

                        // Which part of a spinner holds the value depends on the framework, so try the named element
                        // first and the elements inside it second.
                        AutomationElement writable = WritableFor(application, elem.Ae);
                        bool taken = false;

                        // Try each way of setting the value until one works. A control need not honor a pattern it
                        // offers: a WinForms spinner's RangeValuePattern has the wrong range.
                        if (true == writable.TryGetCurrentPattern(RangeValuePattern.Pattern, out object rangeValuePattern))
                        {
                            try
                            {
                                ((RangeValuePattern)rangeValuePattern).SetValue(rangeValue);

                                taken = true;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"RangeValuePattern on [{writable.Current.ControlType.ProgrammaticName}] refused [{rangeValue}], so the next way of setting it is tried.", application, GPALObjectType.Application, ex);
                            }
                        }

                        if (false == taken && true == writable.TryGetCurrentPattern(ValuePattern.Pattern, out object valuePattern))
                        {
                            try
                            {
                                ((ValuePattern)valuePattern).SetValue(rangeValue.ToString());

                                // The box may belong to a spinner, and the value does not take effect until the control
                                // reads it.
                                Commit(writable);

                                taken = true;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"ValuePattern on [{writable.Current.ControlType.ProgrammaticName}] refused [{rangeValue}], so the next way of setting it is tried.", application, GPALObjectType.Application, ex);
                            }
                        }

                        // If both fail, write to the child window. A WinForms spinner's edit box is a window Windows
                        // returns on request.
                        if (false == taken)
                            taken = SetNativeValue(application, elem.Ae, rangeValue.ToString());

                        // If that fails, type the value with hardware input.
                        if (false == taken)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{elem.Ae.Current.ControlType.ProgrammaticName}] offers no value to set through UIAutomation, so it is typed into instead.", application, GPALObjectType.Application);

                            HardwareFillInFrom(application, elem, sel.OffsetX, sel.OffsetY, rangeValue.ToString(), WriteMode.Overwrite);
                        }
                    }
                }
            }
        }
    }
}



