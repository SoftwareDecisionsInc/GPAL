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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Application
{
    internal static class AutomationElementExtension
    {
        public static ReadOnlyCollection<GPALAutomationElement> FindElements(
            this AutomationElement element,
            string findByString,
            ConditionPropertyType conditionPropertyType,
            int idx = 0,
            bool isRoot = false)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            PropertyCondition condition = null;
            ControlType controlType = null;

            switch (conditionPropertyType)
            {
                case ConditionPropertyType.AutomationID:
                    condition = new PropertyCondition(AutomationElement.AutomationIdProperty, findByString);
                    break;
                case ConditionPropertyType.ClassName:
                    condition = new PropertyCondition(AutomationElement.ClassNameProperty, findByString);
                    break;
                case ConditionPropertyType.ControlType:
                    controlType = GetControlType(findByString);
                    condition = new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
                    break;
                case ConditionPropertyType.Name:
                    condition = new PropertyCondition(AutomationElement.NameProperty, findByString);
                    break;
                case ConditionPropertyType.Value:
                    condition = new PropertyCondition(ValuePattern.ValueProperty, findByString);
                    break;
                default:
                    throw new ArgumentException("Unsupported ConditionPropertyType");
            }

            TreeScope treeScope = isRoot ? TreeScope.Subtree : TreeScope.Children;

            List<GPALAutomationElement> tmpElems = new List<GPALAutomationElement>();

            if (isRoot)
            {
                AutomationElement found = element.FindFirst(treeScope, condition);
                if (found != null)
                    tmpElems.Add(new GPALAutomationElement(found));
            }
            else
            {
                // Your existing custom logic – kept unchanged
                List<AutomationElement> automationElements = GetAllChildren(element, idx, controlType);
                foreach (AutomationElement elem in automationElements)
                    tmpElems.Add(new GPALAutomationElement(elem));
            }

            return new ReadOnlyCollection<GPALAutomationElement>(tmpElems);
        }
        /*
        public static ReadOnlyCollection<GPALAutomationElement> FindElements(this AutomationElement element, string findByString, ConditionPropertyType conditionPropertyType, int idx = 0, bool isRoot = false)
        {
            ReadOnlyCollection<GPALAutomationElement> elems = null;
            List<GPALAutomationElement> tmpElems = new List<GPALAutomationElement>();
            PropertyCondition condition = null;
            ControlType controlType = null;

            switch (conditionPropertyType)
            {
                case ConditionPropertyType.AutomationID:
                    condition = new PropertyCondition(AutomationElement.AutomationIdProperty, findByString);
                    break;
                case ConditionPropertyType.ClassName:
                    condition = new PropertyCondition(AutomationElement.ClassNameProperty, findByString);
                    break;
                case ConditionPropertyType.ControlType:
                    controlType = GetControlType(findByString);
                    condition = new PropertyCondition(AutomationElement.ControlTypeProperty, controlType);
                    break;
                case ConditionPropertyType.Name:
                    condition = new PropertyCondition(AutomationElement.NameProperty, findByString);
                    break;
                case ConditionPropertyType.Value:
                    condition = new PropertyCondition(System.Windows.Automation.ValuePattern.ValueProperty, findByString);
                    break;
            }
            TreeScope treeScope = System.Windows.Automation.TreeScope.Children;
            if (true == isRoot)
                treeScope = System.Windows.Automation.TreeScope.Subtree;
            List<AutomationElement> automationElements = new List<AutomationElement>();
            AutomationElement automationElement;
            if (true == isRoot)
            {
                automationElement = element.FindFirst(treeScope, condition);
                tmpElems.Add(new GPALAutomationElement(automationElement));
            }
            else
            {
                //AutomationElementCollection automationElems;
                //    automationElems = element.FindAll(treeScope, condition);
                //    foreach (AutomationElement elem in automationElems)
                //        tmpElems.Add(new GPALAutomationElement(elem));
                //automationElement = element.FindFirst(treeScope, condition);

                automationElements = GetAllChildren(element, idx, controlType);
                foreach (AutomationElement elem in automationElements)
                    tmpElems.Add(new GPALAutomationElement(elem));


                //if (0 == automationElems.Count)
                //{
                //    treeScope = System.Windows.Automation.TreeScope.Subtree;
                //    automationElems = element.FindAll(treeScope, condition);
                //    if (0 == automationElems.Count)
                //    {
                //        treeScope = System.Windows.Automation.TreeScope.Descendants;
                //        automationElems = element.FindAll(treeScope, condition);
                //    }
                //}
                //foreach (AutomationElement elem in automationElems)
                //    tmpElems.Add(new GPALAutomationElement(elem));
            }

            elems = new ReadOnlyCollection<GPALAutomationElement>(tmpElems);
            return elems;
        }
        */
        private static List<AutomationElement> GetAllChildren(AutomationElement rootElement, int idx, ControlType controlType)
        {
            Condition condition = new PropertyCondition(AutomationElement.IsControlElementProperty, true);
            TreeWalker walker = new TreeWalker(condition);
            AutomationElement elementNode = walker.GetFirstChild(rootElement);
            int cnt = 0;

            if (null == elementNode) return null;

            List<AutomationElement> automationElements = new List<AutomationElement>();

            while (elementNode != null && cnt <= idx)   // only get as many children as we require
            {
                if (controlType == elementNode.Current.ControlType)
                {
                    automationElements.Add(elementNode);
                    cnt++;
                }

                elementNode = walker.GetNextSibling(elementNode);
            }

            return automationElements;
        }
        /// <summary>
        /// Returns the element's ValuePattern value, for short values (edit field, combo entry, spinner). Providers
        /// truncate long content; use <see cref="GetText"/> for a whole document.
        /// </summary>
        /// <param name="element">The element to read</param>
        /// <returns>The value, or null when the element has no ValuePattern</returns>
        public static string GetValue(this AutomationElement element)
        {
            string retVal = null;

            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj))
                retVal = ((ValuePattern)patternObj).Current.Value;

            return retVal;
        }

        public static string GetText(this AutomationElement element)
        {
            object patternObj;
            // LegacyIAccessiblePattern only accessible in UIACommWrapperX
            //if (element.TryGetCurrentPattern(LegacyIAccessiblePattern.Pattern, out patternObj))
            //{
            //    var legacyPattern = (LegacyIAccessiblePattern)patternObj;
            //    return legacyPattern.Current.Value;
            //}
            //else 
            // Try TextPattern first; it is the only pattern that returns a whole document. ValuePattern truncates long
            // content (a Scintilla editor returns exactly 4096 characters). A control with no TextPattern falls through
            // to ValuePattern.
            string retVal;
            string answeredBy;

            if (element.TryGetCurrentPattern(TextPattern.Pattern, out patternObj))
            {
                var textPattern = (TextPattern)patternObj;
                retVal = textPattern.DocumentRange.GetText(-1).TrimEnd('\r'); // often there is an extra '\r' hanging off the end.
                answeredBy = "TextPattern";
            }
            else if (element.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
            {
                var valuePattern = (ValuePattern)patternObj;
                retVal = valuePattern.Current.Value;
                answeredBy = "ValuePattern, which a provider may truncate";
            }
            else
            {
                retVal = element.Current.Name;
                answeredBy = "Name, because the control offers neither TextPattern nor ValuePattern";
            }

            // Publish which pattern answered; it decides whether long content is complete.
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{element.Current.LocalizedControlType}] text of [{retVal?.Length ?? 0}] characters came from {answeredBy}.", null, GPALObjectType.Application);

            return retVal;
        }

        internal static ControlType GetControlType(string controlTypeString)
        {
            if (null == controlTypeString) return null;

            AutomationControlType typeEnum;
            bool result = Enum.TryParse(controlTypeString, true, out typeEnum);
            if (result) typeEnum = (AutomationControlType)Enum.Parse(typeof(AutomationControlType), controlTypeString);
            int enumId = (int)typeEnum + 50000;
            ControlType controlType = ControlType.LookupById(enumId);

            return controlType;
        }
    }
}


