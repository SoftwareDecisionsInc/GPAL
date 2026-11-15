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
    }
}
