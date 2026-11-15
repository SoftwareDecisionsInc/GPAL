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
    /// A toolbar docked below the menu bar, containing <see cref="GPALBarItem"/> buttons and optional
    /// separators.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALToolbar gPALToolbar = (GPALToolbar)GPAL.Toolbar
    ///     .WithItem(newItem)
    ///     .WithItem(openItem)
    ///     .WithSeparator()
    ///     .WithItem(runItem)
    ///     .WithName("MainToolbar")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALToolbar : GPALControl, IAllowToolbarControlSettings
    {
        internal readonly List<(GPALBarItem Item, bool IsSeparator)> toolbarEntries
            = new List<(GPALBarItem, bool)>();

        internal GPALToolbar()
        {
            ControlType = FormControlType.Toolbar;
        }

        /// <summary>
        /// Adds a button to the toolbar.
        /// </summary>
        /// <param name="item">The bar item to add as a toolbar button.</param>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowToolbarControlSettings WithItem(GPALBarItem item)
        {
            toolbarEntries.Add((item, false));
            return this;
        }

        /// <summary>
        /// Adds a vertical separator between toolbar buttons.
        /// </summary>
        /// <returns>Fluent interface to add more items.</returns>
        public IAllowToolbarControlSettings WithSeparator()
        {
            toolbarEntries.Add((null, true));
            return this;
        }
    }
}
