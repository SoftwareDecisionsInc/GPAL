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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Data;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.GPALForm
{
    /// <summary>
    /// A menu bar docked to the top of a GPAL form. Open a top-level menu with
    /// <see cref="WithMenu(string)"/>, then attach <see cref="GPALBarItem"/>s to it with
    /// <see cref="WithItem(GPALBarItem)"/> (and optional <see cref="WithSeparator"/> rules). Items added
    /// after a WithMenu call belong to that menu until the next WithMenu.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALMenuBar gPALMenuBar = (GPALMenuBar)GPAL.MenuBar
    ///     .WithMenu("File")
    ///     .WithItem(saveItem)
    ///     .WithSeparator()
    ///     .WithItem(exitItem)
    ///     .WithMenu("Help")
    ///     .WithItem(aboutItem)
    ///     .WithName("MainMenu")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALMenuBar : GPALControl, IAllowMenuBarControlSettings
    {
        private string _currentMenu;
        internal readonly List<(string TopMenu, GPALBarItem Item, bool IsSeparator)> menuEntries
            = new List<(string, GPALBarItem, bool)>();

        internal GPALMenuBar()
        {
            ControlType = FormControlType.MenuBar;
        }

        /// <summary>
        /// Opens (or reselects) a top-level menu. Subsequent <see cref="WithItem(GPALBarItem)"/> and
        /// <see cref="WithSeparator"/> calls attach to this menu until the next WithMenu.
        /// </summary>
        /// <param name="topMenu">The top-level menu caption (e.g. "File", "Help").</param>
        /// <returns>Fluent interface to add items to this menu.</returns>
        public IAllowMenuBarControlSettings WithMenu(string topMenu)
        {
            _currentMenu = topMenu;
            return this;
        }

        /// <summary>
        /// Adds an item to the currently open top-level menu.
        /// </summary>
        /// <param name="item">The bar item to add.</param>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowMenuBarControlSettings WithItem(GPALBarItem item)
        {
            menuEntries.Add((_currentMenu, item, false));
            return this;
        }

        /// <summary>
        /// Adds a separator line to the currently open top-level menu.
        /// </summary>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowMenuBarControlSettings WithSeparator()
        {
            menuEntries.Add((_currentMenu, null, true));
            return this;
        }
    }
}
