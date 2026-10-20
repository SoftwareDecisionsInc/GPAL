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
using System.Windows.Automation;

namespace GenerallyPositive.Application
{
    public interface IGPALAutomationElement
    {
        AutomationElement Ae { get; set; }
        IGPALElement Gae { get; set; }
  //      mUIA.System.Windows.Automation.AutomationElement Mae { get; set; }

        // These properties have the same names as the page element's, so a handler reads both the same way.
        /// <summary>Whether the element is selected, as GPALElement.Selected answers for a page.</summary>
        bool Selected { get; }

        /// <summary>A checkbox's state: true checked, false clear, null indeterminate.</summary>
        bool? Checked { get; }

        /// <summary>Whether the element is enabled.</summary>
        bool Enabled { get; }

        /// <summary>Whether the element is on screen.</summary>
        bool Displayed { get; }

        /// <summary>The element's text.</summary>
        string Text { get; }

        /// <summary>The control type, the window equivalent of a tag name.</summary>
        string TagName { get; }

        /// <summary>The element's name, which is how a window labels a control.</summary>
        string Name { get; }

        /// <summary>Screen coordinates of the element.</summary>
        System.Drawing.Point Location { get; }

        /// <summary>Dimensions of the element.</summary>
        System.Drawing.Size Size { get; }
    }
}
