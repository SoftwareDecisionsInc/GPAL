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
    /// A GPAL link instantiated with GPAL.LinkLabel or GPAL.LinkLabelFor for use on GPAL forms.<br/>
    /// Clicking it opens its address in the default browser: the one given with WithUrl, or its own text when that
    /// is an address. A callback given with WithCallback runs on the click as well.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALLinkLabel moreBots = (GPALLinkLabel)GPAL.LinkLabel
    ///     .WithUrl("https://gpal.sditx.com/docs/botw")
    ///     .WithText("More Bots of the Week");
    ///
    /// static GPALLinkLabel docs = GPAL.LinkLabelFor("https://gpal.sditx.com/");
    ///
    /// GPALForm myForm = (GPALForm)GPAL.Form
    ///     .WithTitle("About")
    ///     .WithFormControl(moreBots)
    ///     .WithFormControl(docs)
    ///     .ShowDialog();
    /// </code>
    /// </example>
    public class GPALLinkLabel : GPALControl, IAllowControlSettingsAndUrl
    {
        internal GPALLinkLabel()
        {
            ControlType = FormControlType.LinkLabel;
        }

        /// <summary>The address the link opens, or null to open its own text when that is an address.</summary>
        public string Url { get; internal set; }

        /// <summary>
        /// Sets the address the link opens in the default browser. Set it before WithText, since WithText
        /// moves on to the font settings.
        /// </summary>
        /// <param name="url">The address to open</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowControlSettingsAndUrl WithUrl([URL] string url)
        {
            Url = url;
            return this;
        }

        /// <summary>
        /// Opens the link's address in the default browser, the way clicking it does. Nothing happens when there
        /// is no address and the text is not one.
        /// </summary>
        internal void Open()
        {
            string target = false == string.IsNullOrWhiteSpace(Url) ? Url : Text;

            if (true == target?.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || true == target?.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                GPAL.Launcher.WithExecutable(target).Launch();
        }
    }
}
