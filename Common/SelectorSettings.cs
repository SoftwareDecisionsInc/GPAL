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
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;
using static GenerallyPositive.Browser.Browser;
using System.Windows.Automation;
using GenerallyPositive.Application;

namespace GenerallyPositive
{
    /// <summary>
    /// Settings for the current selector. Use this only for debugging.
    /// </summary>
    public class SelectorSettings
    {
        internal string Name { get; set; } = null;  // intially asigned by GPAL factory
        /// <summary>
        /// An entry defining a selector locator, a selector path
        /// </summary>
        public class SelectorPathEntry
        {
            /// <summary>
            /// THe type of selector, css, xpath, text, etc.
            /// </summary>
            public SelectorPathType SelectorPathType { get; internal set; } = SelectorPathType.NotSet;
            /// <summary>
            /// The actual selector string
            /// </summary>
            public string SelectorPath { get; internal set; } = null;
            /// <summary>
            /// Web elements found for this selector
            /// </summary>
            public List<GPALElement> WebSelectorFoundResults { get; internal set; } = null;
            /// <summary>
            /// Web elements matched out of the found selectors
            /// </summary>
            public List<GPALElement> WebSelectorMatchedResults { get; internal set; } = null;
            /// <summary>
            /// UIAutomation elements found for this selector
            /// </summary>
            public List<GPALAutomationElement> AppSelectorFoundResults { get; internal set; } = null;
            /// <summary>
            /// UIAutomation elements matched out of the found selectors
            /// </summary>
            public List<GPALAutomationElement> AppSelectorMatchedResults { get; internal set; } = null;
            /// <summary>
            /// The image to match to create a psuedo element
            /// </summary>
            public Image Image { get; internal set; } = null;
            internal SelectorPathEntry()
            {
            }
        }
        internal class MatchCriteriaEntry
        {
            public string StringToMatch { get; set; } = null;
            internal MatchType MatchType { get; set; } = MatchType.None;
            internal bool ExactMatch { get; set; } = true;
            public Selector.webMatchingFunction WebMatchingFunction { get; set; } = null;
            public Selector.appMatchingFunction AppMatchingFunction { get; set; } = null;
        }
        /// <summary>
        /// Type of selector: A selector for an element, or literal data or a data function for dynamic data not from an element
        /// </summary>
        public SelectorType SelectorType { get; internal set; } = SelectorType.Selector;
        public Func<string> DataFunction { get; internal set; }
        internal List<SelectorPathEntry> SelectorPaths { get; set; } = null;
        internal List<MatchCriteriaEntry> MatchCriteria { get; set; } = null;
        internal string AttributeName { get; set; } = null;
        internal InteractionType InteractionType { get; set; } = InteractionType.Puppeteer;   // resolved for the engine in play, recomputed every time the selector is attached
        internal InteractionType? RequestedInteractionType { get; set; } = null;              // what the workflow explicitly asked for, null when it never said. never overwritten by resolution
        internal string HRef { get; set; } = null;
        internal int OffsetX { get; set; } = 0; // offset to click from the top left corner of the element found
        internal int OffsetY { get; set; } = 0; // offset to click from the top left corner of the element found
        internal int DeltaX { get; set; } = 0;  // drag-and-drop distance moved from the click point described by offsetX
        internal int DeltaY { get; set; } = 0;  // drag-and-drop distance moved from the click point described by offsetY
        internal bool DeleteMe { get; set; } = false; // to allow a CallIF handler to 'remove' a selector from the SelectorList
        internal bool SearchForSelector { get; set; } = true;
        public List<CallIfDelegate> CallIfFound { get; internal set; }
        public List<CallIfDelegate> CallIfNotFound { get; internal set; }
        internal List<CallIfDelegate> RemovedMethods { get; set; } // handlers RemoveCallIfHandlerEverywhere took out of play - a soft delete so the handler list survives to be reused and can be inspected
        internal List<Application.Application.CallIfDelegate> AppCallIfFound { get; set; }
        internal List<Application.Application.CallIfDelegate> AppCallIfNotFound { get; set; }
        public  CallIfStatus CallIfFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        public CallIfStatus CallIfNotFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        public CallIfStatus AppCallIfFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        public CallIfStatus AppCallIfNotFoundHandled { get; internal set; } = CallIfStatus.NotHandled;
        internal bool SimulateMouse { get; set; } = false;
        internal bool StopOnNotFound { get; set; } = false;
        internal int ElementsFoundAndMatchedCount { get; set; } = 0;

        internal SelectorSettings()
        {
            SelectorPaths = new List<SelectorPathEntry>();
            MatchCriteria = new List<MatchCriteriaEntry>();
            CallIfFound = new List<CallIfDelegate>();
            CallIfNotFound = new List<CallIfDelegate>();
            RemovedMethods = new List<CallIfDelegate>();
            AppCallIfFound = new List<Application.Application.CallIfDelegate>();
            AppCallIfNotFound = new List<Application.Application.CallIfDelegate>();
        }
    }
}

