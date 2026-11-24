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
    /// Event arguments passed to a <see cref="GPALFileSelector"/> callback when the file/folder
    /// selection dialog is closed, whether by the user picking a path or cancelling.
    /// </summary>
    public class FileSelectorEventArgs : EventArgs
    {
        /// <summary>
        /// <c>true</c> if the user dismissed the dialog without selecting a path.
        /// </summary>
        public bool Cancelled { get; }
        /// <summary>
        /// The first selected path, or an empty string if none was selected.
        /// </summary>
        public string SelectedPath { get; }
        /// <summary>
        /// All selected paths. Contains more than one entry only when the dialog allowed multi-select.
        /// </summary>
        public IReadOnlyList<string> SelectedPaths { get; }

        /// <summary>
        /// Creates the event arguments for a completed file selector dialog.
        /// </summary>
        /// <param name="cancelled"><c>true</c> if the dialog was cancelled.</param>
        /// <param name="selectedPath">The first selected path. Stored as an empty string if <c>null</c>.</param>
        /// <param name="selectedPaths">All selected paths. Stored as an empty list if <c>null</c> or empty.</param>
        public FileSelectorEventArgs(bool cancelled, string selectedPath, string[] selectedPaths)
        {
            Cancelled = cancelled;
            SelectedPath = selectedPath ?? "";
            if (selectedPaths == null || selectedPaths.Length == 0)
            {
                SelectedPaths = Array.AsReadOnly(Array.Empty<string>());
            }
            else
            {
                SelectedPaths = Array.AsReadOnly(selectedPaths);
            }
        }
    }
}
