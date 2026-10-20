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

//extern alias mUIA;
//extern alias mUIAtypes;
//using mUIA::System.Windows.Automation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation.Peers;
using GenerallyPositive;
using static GenerallyPositive.Enums;
using System.Drawing;
using System.Threading;

namespace GenerallyPositive.Application
{
    internal class ManagedAutomationElementHelper
    {
        /*
        public static ReadOnlyCollection<IGPALAutomationElement> FindElements(IApplication application, mUIA::System.Windows.Automation.AutomationElement element, string findByString, ConditionPropertyType conditionPropertyType, 
                                                                            int idx = 0, bool isRoot = false)
        {
            ReadOnlyCollection<IGPALAutomationElement> elems = null;
            List<IGPALAutomationElement> tmpElems = new List<IGPALAutomationElement>();
            mUIA::System.Windows.Automation.PropertyCondition condition = null;
            mUIAtypes::System.Windows.Automation.ControlType controlType = null;

            if (true == isRoot)
                element = (mUIA::System.Windows.Automation.AutomationElement)(application.ManagedRootAutomationElement = mUIA::System.Windows.Automation.AutomationElement.FromHandle(application.Process.Handle));

            switch (conditionPropertyType)
            {
                case ConditionPropertyType.AutomationID:
                    condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.AutomationElement.AutomationIdProperty, findByString);
                    break;
                case ConditionPropertyType.ClassID:
                    condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.AutomationElement.ClassNameProperty, findByString);
                    break;
                case ConditionPropertyType.ControlType:
                    controlType = GetControlType(findByString);
                    condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.AutomationElement.ControlTypeProperty, controlType);
                    break;
                case ConditionPropertyType.Name:
                    condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.AutomationElement.NameProperty, findByString);
                    break;
                case ConditionPropertyType.Value:
                    condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.ValuePattern.ValueProperty, findByString);
                    break;
            }
            mUIAtypes::System.Windows.Automation.TreeScope treeScope = mUIAtypes::System.Windows.Automation.TreeScope.Children;
            if (true == isRoot)
                treeScope = mUIAtypes::System.Windows.Automation.TreeScope.Subtree;

            List<mUIA::System.Windows.Automation.AutomationElement> automationElements = new List<mUIA::System.Windows.Automation.AutomationElement>();
            mUIA::System.Windows.Automation.AutomationElement automationElement;

            if (true == isRoot)
            {
                automationElement = element.FindFirst(treeScope, condition);
                tmpElems.Add(new GPALAutomationElement(null, null, automationElement));
            }
            else
            {
                automationElements = GetAllChildren(element, idx, controlType);
                foreach (mUIA::System.Windows.Automation.AutomationElement elem in automationElements)
                    tmpElems.Add(new GPALAutomationElement(null, null, elem));
            }

            elems = new ReadOnlyCollection<IGPALAutomationElement>(tmpElems);
            return elems;
        }
        public static List<mUIA::System.Windows.Automation.AutomationElement> GetAllChildren(mUIA::System.Windows.Automation.AutomationElement rootElement, int idx, mUIAtypes::System.Windows.Automation.ControlType controlType)
        {
            mUIA::System.Windows.Automation.Condition condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.AutomationElement.IsControlElementProperty, true);
            mUIA::System.Windows.Automation.TreeWalker walker = new mUIA::System.Windows.Automation.TreeWalker(condition);
            mUIA::System.Windows.Automation.AutomationElement elementNode = walker.GetFirstChild(rootElement);
            int cnt = 0;

            if (null == elementNode) return null;

            List<mUIA::System.Windows.Automation.AutomationElement> automationElements = new List<mUIA::System.Windows.Automation.AutomationElement>();

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
        public static string GetText(mUIA::System.Windows.Automation.AutomationElement element)
        {
            object patternObj;
            if (element.TryGetCurrentPattern(mUIA::System.Windows.Automation.ValuePattern.Pattern, out patternObj))
            {
                var valuePattern = (mUIA::System.Windows.Automation.ValuePattern)patternObj;
                return valuePattern.Current.Value;
            }
            else if (element.TryGetCurrentPattern(mUIA::System.Windows.Automation.TextPattern.Pattern, out patternObj))
            {
                var textPattern = (mUIA::System.Windows.Automation.TextPattern)patternObj;
                return textPattern.DocumentRange.GetText(-1).TrimEnd('\r'); // often there is an extra '\r' hanging off the end.
            }
            else
            {
                return element.Current.Name;
            }
        }

        internal static mUIAtypes::System.Windows.Automation.ControlType GetControlType(string controlTypeString)
        {
            mUIAtypes::System.Windows.Automation.ControlType retControlType;
            mUIAtypes::System.Windows.Automation.ControlType controlType;
            if (null == controlTypeString) return null;

            System.Windows.Automation.Peers.AutomationControlType typeEnum;
            bool result = Enum.TryParse(controlTypeString, true, out typeEnum);
            if (result) typeEnum = (AutomationControlType)Enum.Parse(typeof(AutomationControlType), controlTypeString);
            int enumId = (int)typeEnum + 50000;
            controlType = mUIAtypes::System.Windows.Automation.ControlType.LookupById(enumId);

            retControlType = mUIAtypes::System.Windows.Automation.ControlType.LookupById(controlType.Id);

            return retControlType;
        }
        public static Rectangle GetBoundingRect(object managedAutomationElement)
        {
            return new Rectangle(new Point((int)((mUIA::System.Windows.Automation.AutomationElement)managedAutomationElement).Current.BoundingRectangle.Location.X, (int)((mUIA::System.Windows.Automation.AutomationElement)managedAutomationElement).Current.BoundingRectangle.Location.Y), new Size((int)((mUIA::System.Windows.Automation.AutomationElement)managedAutomationElement).Current.BoundingRectangle.Size.Width, (int)((mUIA::System.Windows.Automation.AutomationElement)managedAutomationElement).Current.BoundingRectangle.Size.Height));
        }
        public static void ExpandMenuItem(IApplication application, object managedAutomationElement = null, bool simulateMouse = false)
        {
            mUIA::System.Windows.Automation.AutomationElement automationElement = (mUIA::System.Windows.Automation.AutomationElement)managedAutomationElement;

            if (true == GPAL.SimulateMouse || true == application.CurrentUOW.CurrentSelector?.SimulateMouse)
            {
                Rectangle rect = GetBoundingRect(managedAutomationElement);
                HardwareHelper.MoveMouse(rect.X, rect.Y, 10, 10);    // source didn't define values for rx, ry            }
                HardwareHelper.LeftClick(rect.X, rect.Y);
                Thread.Sleep(250); // wait for display before trying to find elements
            }
            else
            {
                try
                {
                    if (null != automationElement)
                    {
                        ExpandCollapsePattern pattern = automationElement.GetCurrentPattern(ExpandCollapsePattern.Pattern) as ExpandCollapsePattern;
                        pattern.Expand();
                    }
                    else
                    {
                        mUIA::System.Windows.Automation.ExpandCollapsePattern pattern = automationElement.GetCurrentPattern(mUIA::System.Windows.Automation.ExpandCollapsePattern.Pattern) as mUIA::System.Windows.Automation.ExpandCollapsePattern;
                        pattern.Expand();
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent($"Unable to expand menuitem for Control [{automationElement.Current.LocalizedControlType}], trying InvokePattern.", application, GPALObjectType.Application, ex);
                    try
                    {
                        if (null != automationElement)
                        {
                            InvokePattern pattern = automationElement.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                            pattern.Invoke();
                        }
                        else
                        {
                            mUIA::System.Windows.Automation.InvokePattern pattern = automationElement.GetCurrentPattern(mUIA::System.Windows.Automation.InvokePattern.Pattern) as mUIA::System.Windows.Automation.InvokePattern;
                            pattern.Invoke();
                        }
                    }
                    catch (Exception ex2)
                    {
                        GPAL.PublishSimpleEvent($"Unable to expand menuitem for Control [{automationElement.Current.LocalizedControlType}], using hardware fallback.", application, GPALObjectType.Application, ex2);

                        Random random = new Random(10007);

                        if (null != automationElement)
                        {
                            int offsetX = random.Next(1, (int)automationElement.Current.BoundingRectangle.Width - 2);
                            int offsetY = random.Next(1, (int)automationElement.Current.BoundingRectangle.Height - 2);
                            ElementHelper.HardwareClick(null, new GPALAutomationElement((System.Windows.Automation.AutomationElement)managedAutomationElement, null, automationElement), Enums.ClickType.LeftClick, Enums.ModifierKeys.None, offsetX, offsetY);
                        }
                        else
                        {
                            int offsetX = random.Next(1, (int)automationElement.Current.BoundingRectangle.Width - 2);
                            int offsetY = random.Next(1, (int)automationElement.Current.BoundingRectangle.Height - 2);
                            ElementHelper.HardwareClick(null, new GPALAutomationElement((System.Windows.Automation.AutomationElement)managedAutomationElement, null, automationElement), Enums.ClickType.LeftClick, Enums.ModifierKeys.None, offsetX, offsetY);
                        }
                    }
                }
            }
        }
        public static List<AutomationElement> GetAllChildren(AutomationElement rootElement, int idx, System.Windows.Automation.ControlType controlType)
        {
            mUIA::System.Windows.Automation.Condition condition = new mUIA::System.Windows.Automation.PropertyCondition(mUIA::System.Windows.Automation.AutomationElement.IsControlElementProperty, true);
            mUIA::System.Windows.Automation.TreeWalker walker = new mUIA::System.Windows.Automation.TreeWalker(condition);
            mUIA::System.Windows.Automation.AutomationElement elementNode = walker.GetFirstChild(rootElement);
            int cnt = 0;

            if (null == elementNode) return null;

            List<mUIA::System.Windows.Automation.AutomationElement> automationElements = new List<mUIA::System.Windows.Automation.AutomationElement>();

            while (elementNode != null && cnt <= idx)   // only get as many children as we require
            {
                //if (controlType == elementNode.Current.ControlType)
                //{
                //    automationElements.Add(elementNode);
                //    cnt++;
                //}

                elementNode = walker.GetNextSibling(elementNode);
            }
            return automationElements;
        }
        */
    }
}


