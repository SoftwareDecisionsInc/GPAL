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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace GenerallyPositive.Application
{
    /// <summary>
    /// Application workflow container for automation elements.
    /// </summary>
    public class GPALAutomationElement : IGPALAutomationElement
    {
        public Application Application { get; internal set; }
        public Selector Selector { get; internal set; }

        //        public mUIA.System.Windows.Automation.AutomationElement Mae { get; set; }
        /// <summary>
        /// UIAutomation element
        /// </summary>
        public AutomationElement Ae { get; set; }
        /// <summary>
        /// GPAL psuedo element.<br/>
        /// Only the following properties are actually used:<br/>
        /// Location, Size, Text, TagName, WrappedDriver (web driver);
        /// We create a psuedo element for image matching which does not have a real element
        /// </summary>
        public IGPALElement Gae { get; set; }
        /// <summary>
        /// The image match this control was found under, or null when it was not found by an image.<br/>
        /// A click, a hover or a drag goes to the matched point rather than to the control, since the control can
        /// be much larger than what was matched. Everything else, text, values, fill-in, uses the control.
        /// </summary>
        internal GPALAutomationElement ImageMatch { get; set; }
        /// <summary>
        /// Whether the element is selected, as <see cref="GPALElement.Selected"/> answers for a page.<br/>
        /// Rows, tabs and radio buttons use SelectionItemPattern; a checkbox uses TogglePattern. Otherwise the
        /// container is asked, then MSAA. If nothing answers, this returns false.
        /// </summary>
        public bool Selected
        {
            get
            {
                bool retVal = false;
                object pattern;

                if (null != Ae && true == Ae.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    retVal = ((SelectionItemPattern)pattern).Current.IsSelected;
                else if (null != Ae && true == Ae.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
                    retVal = ToggleState.On == ((TogglePattern)pattern).Current.ToggleState;
                else
                    retVal = ContainerSaysSelected(Ae) || true == MsaaHelper.Selected(Application, Ae);

                return retVal;
            }
        }

        /// <summary>
        /// True when a container above this element lists it as selected.
        /// </summary>
        /// <remarks>
        /// Equals compares UIAutomation runtime ids, so an element from GetSelection matches this one.
        /// </remarks>
        /// <param name="element">The element being asked about</param>
        /// <returns>True when a container above it names it as selected</returns>
        private static bool ContainerSaysSelected(AutomationElement element)
        {
            bool retVal = false;
            AutomationElement above = null == element ? null : TreeWalker.ControlViewWalker.GetParent(element);

            for (int level = 0; level < ContainerLevels && false == retVal && null != above; level++)
            {
                if (true == above.TryGetCurrentPattern(SelectionPattern.Pattern, out object pattern))
                    foreach (AutomationElement selected in ((SelectionPattern)pattern).Current.GetSelection())
                        if (true == element.Equals(selected))
                            retVal = true;

                above = ControlType.Window == above.Current.ControlType ? null : TreeWalker.ControlViewWalker.GetParent(above);
            }

            return retVal;
        }

        // How many parents up to look for a selection container. The Explorer ribbon has two elements between a tab and
        // its container.
        private const int ContainerLevels = 5;

        /// <summary>
        /// A checkbox's state: true checked, false clear, null indeterminate. A control with no toggle state also
        /// returns null. <see cref="Selected"/> returns false for indeterminate.
        /// </summary>
        public bool? Checked
        {
            get
            {
                bool? retVal = null;
                object pattern;

                if (null != Ae && true == Ae.TryGetCurrentPattern(TogglePattern.Pattern, out pattern))
                {
                    ToggleState state = ((TogglePattern)pattern).Current.ToggleState;

                    if (ToggleState.On == state)
                        retVal = true;
                    else if (ToggleState.Off == state)
                        retVal = false;
                }

                return retVal;
            }
        }

        /// <summary>Whether the element is enabled, from UIAutomation's IsEnabled.</summary>
        public bool Enabled => null != Ae && true == Ae.Current.IsEnabled;

        /// <summary>
        /// Whether the element is on screen, from UIAutomation's IsOffscreen.<br/>
        /// A control scrolled out of view or on a tab that is not showing is offscreen, which is the same
        /// question <see cref="GPALElement.Displayed"/> answers about a page.
        /// </summary>
        public bool Displayed => null != Ae && false == Ae.Current.IsOffscreen;

        /// <summary>
        /// The element's text, read through TextPattern where the control has one and ValuePattern otherwise,
        /// falling back to the name.<br/>
        /// A long value can come back cut: see the warning under Reading Data.
        /// </summary>
        public string Text => null == Ae ? string.Empty : (Ae.GetText() ?? string.Empty);

        /// <summary>
        /// The control type (Button, ListItem, Edit and so on), the window equivalent of a tag name. Programmatic, not
        /// localized.
        /// </summary>
        public string TagName => null == Ae
            ? string.Empty
            : (Ae.Current.ControlType?.ProgrammaticName ?? string.Empty).Replace("ControlType.", "");

        /// <summary>The element's name, which is how a window labels a control.</summary>
        public string Name => null == Ae ? string.Empty : (Ae.Current.Name ?? string.Empty);

        /// <summary>
        /// Screen coordinates of the element, the top left of its bounding rectangle.<br/>
        /// Always screen coordinates on this side: a window has no viewport to measure from.
        /// </summary>
        public System.Drawing.Point Location => null == Ae
            ? new System.Drawing.Point(0, 0)
            : new System.Drawing.Point((int)Ae.Current.BoundingRectangle.Left, (int)Ae.Current.BoundingRectangle.Top);

        /// <summary>Dimensions of the element, from its bounding rectangle.</summary>
        public System.Drawing.Size Size => null == Ae
            ? new System.Drawing.Size(0, 0)
            : new System.Drawing.Size((int)Ae.Current.BoundingRectangle.Width, (int)Ae.Current.BoundingRectangle.Height);

        /// <summary>
        /// The command a menu item sends, for an item reached through the window's own menu rather than through
        /// UIAutomation. Zero for everything else.
        /// </summary>
        internal int MenuCommand { get; set; } = 0;
        /// <summary>
        /// The window that menu belongs to, which is what the command is sent to.
        /// </summary>
        internal IntPtr MenuWindow { get; set; } = IntPtr.Zero;
        internal GPALAutomationElement()
        {

        }
        internal GPALAutomationElement(AutomationElement automationElement = null, IGPALElement gPALElement = null/*, mUIA.System.Windows.Automation.AutomationElement managedAutomationElement = null*/)
        {
            Ae = automationElement;
            Gae = gPALElement;
//            Mae = managedAutomationElement;
        }
    }
}

