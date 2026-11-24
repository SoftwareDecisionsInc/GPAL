// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Apache License, Version 2.0
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms.
// =============================================================================

//extern alias mUIA;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using static GenerallyPositive.Enums;
// The HiddenDesktop property hides the static HiddenDesktop class, so the class is used through this alias. Browser
// does the same.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    public partial class Application
    {
        /// <summary>
        /// Add a persistent selector to the persistent UOW. Persistent selectors are always looked for whenever you perform an action on a UOW, like LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the PersistentCallIf handlers to create workflows to deal with the persistent selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// Only one persistent Unit of Work exists. All selectors are evaluated in the order they are defined.<br/>
        /// Example: For applications, a persistent selector could be used with a timer in the callback, when elapsed time occurs, invoke an action on that Selector (like Save work).
        /// </summary>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowPersistence WithPersistentSelector(Selector selector)
        {
            // A persistent selector added after an action starts a new persistent unit of work.
            if (true == CurrentUOW.ActionCalled)
            {
                persistentUOW = new UnitOfWork();
                persistenceList.Add(persistentUOW);
            }

            selector.Application = this;
            //persistentUOW.SelectorType = SelectorType.WithSelector;
            persistentUOW.WithSelectorList.Add(selector);
            return this;
        }
        /// <summary>
        /// Add a selector to the current UOW. Selectors make up a Unit of Work (UOW) to perform actions upon.<br/>
        /// A UOW is: All selectors defined between actions like Open, LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the CallIf handlers to create workflows to deal with found/not found selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// </summary>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowSelectorActionOrAnySelector WithSelector(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;  // in case it is reused
            selector.DeleteMe = false;                  // Remove() latches this on and nothing else clears it, so a reused selector would be swept on its next unit of work
            selector.RemovedMethods.Clear();            // a handler that took itself out of play in an earlier workflow is back in play for this one

            if (true == CurrentUOW.ActionCalled)
            {
                CurrentUOW = new UnitOfWork();
            }

            // The workflow's own selector is used, not a clone, so properties it reads such as ElementsFoundAndMatchedCount
            // are set on it.
            ApplicationSettings.MySelector = selector;

            CurrentUOW.WithSelectorList.Add(ApplicationSettings.MySelector);

            return this;
        }
        /// <summary>
        /// Indicates the Selectors refer to/match repeating, multiple elements on the page.<br/>
        /// Use after all your <b>.WithSelector</b> statements. Used two ways.<br/>
        /// For retrieving data: This is used to retrieve multiple rows of elements from the webpage, up to rowCount.<br/>
        /// For input tokens/strings: This will repeat the same input into multiple elements, up to rowCount of lines.<br/>
        /// For tokens, if columns repeat down the page, column 1 will be filled in with token 1, column 2 with token 2, etc.<br/>
        /// This will be repeated for each row.<br/>
        /// For extracting data from selectors using GetGrid/SaveTo[CSV/Excel/Tabbed] up to rowCount rows of elements matching the Selector will be retrieved for saving.<br/>
        /// Again, rowCount column 1 going down a page will be retrieved, column 2, etc. that is then pivoted to an output of rows/columns
        /// </summary>
        /// <param name="rowCount">The number of rows of matching elements to work with.<br/>int.MaxValue [default] work with all rows.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelectorExceptWithAll WithAllThatMatch(int rowCount = int.MaxValue)
        {
            CurrentUOW.WithAllThatMatch = rowCount;
            return this;
        }
    }
}
