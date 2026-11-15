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
    /// A single item shared by GPAL menu bars and toolbars. Because it derives from
    /// <see cref="GPALControl"/> it speaks the same one-parameter fluent vocabulary as every other
    /// control (WithCallback, WithShortcutKey, WithName, WithToolTip), and adds an optional image.
    /// The callback added via <see cref="GPALControl.WithCallback{TDelegate}(TDelegate)"/> is wired to
    /// the item's Click event; the shortcut set via <see cref="GPALControl.WithShortcutKey"/> is shown
    /// on menu items (toolbars have no per-button shortcut).
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALBarItem saveItem = (GPALBarItem)GPAL.BarItem
    ///     .WithText("Save")
    ///     .WithImage(saveIcon)
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)
    ///     .WithCallback&lt;EventHandler&gt;(SaveHandler)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALBarItem : GPALControl, IAllowBarItemSettings
    {
        internal System.Drawing.Image Image { get; set; }

        internal GPALBarItem()
        {
            ControlType = FormControlType.BarItem;
        }

        /// <summary>
        /// Greys the item and makes it unclickable, keeping the menu layout.
        /// </summary>
        /// <param name="trueFalse">False to grey the item out.</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public new IAllowBarItemSettings WithEnabled(bool trueFalse = true)
        {
            Enabled = trueFalse;
            return this;
        }

        /// <summary>
        /// Whether the item can be clicked. Readable and settable while the form is up, so a menu can follow
        /// what the workflow is doing.
        /// </summary>
        public override bool Enabled
        {
            get
            {
                return WindowsControl is ToolStripItem strip ? strip.Enabled : IsEnabled;
            }
            set
            {
                IsEnabled = value;

                if (!(WindowsControl is ToolStripItem strip)) return;

                // A ToolStripItem has no InvokeRequired; ask its owning strip.
                Control owner = strip.Owner;

                OnUiOrHere(owner, () => strip.Enabled = value);
            }
        }

        /// <summary>
        /// Sets the item's display text.
        /// </summary>
        /// <param name="text">The text to display on the menu item or toolbar button.</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public new IAllowBarItemSettings WithText(string text)
        {
            this.Text = text;
            return this;
        }

        /// <summary>
        /// Sets the item's icon from an in-memory image.
        /// </summary>
        /// <param name="image">The image to display (menu items show it left of the text; toolbar buttons show it on the button).</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public IAllowBarItemSettings WithImage(System.Drawing.Image image)
        {
            this.Image = image;
            return this;
        }

        /// <summary>
        /// Sets the item's icon by loading it from the file referenced by a <see cref="GPALFile"/>.
        /// </summary>
        /// <param name="file">The GPAL file whose first filename points to an image on disk.</param>
        /// <returns>Fluent interface to define more item settings.</returns>
        public IAllowBarItemSettings WithImage(GPALFile file)
        {
            try
            {
                string path = file?.Filenames?.FirstOrDefault() ?? (string)file;
                if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
                    this.Image = System.Drawing.Image.FromFile(path);
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Image file not found for bar item [{path}]", this, GPALObjectType.Other);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to load bar item image [{file?.Filename}]", this, GPALObjectType.Other, ex);
            }
            return this;
        }
    }
}
