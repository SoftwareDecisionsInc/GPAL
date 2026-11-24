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
    /// A file/folder selector control instantiated with GPAL.FileSelector for use on GPAL forms.
    /// Displays a button which, when clicked, opens an <see cref="OpenFileDialog"/> or
    /// <see cref="FolderBrowserDialog"/> (depending on <see cref="WithSelectFolder(bool)"/>) and
    /// raises its callbacks with a <see cref="FileSelectorEventArgs"/> once the dialog closes.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALFileSelector gPalFileSelector = (GPALFileSelector)GPAL.FileSelector
    ///     .WithTitle("Select Input File")
    ///     .WithFilter("CSV files (*.csv)|*.csv")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALFileSelector : GPALControl, IAllowFileSelectorControlSettings
    {
        internal string _title = "Select File/Folder";
        internal string _initialDirectory = "";
        internal string _filter = "All files (*.*)|*.*";
        internal bool _selectFolder = false;       // true = FolderBrowserDialog, false = OpenFileDialog
        internal bool _multiSelect = false;
        internal bool _compact = false;
        internal string _placeholder = "";
        internal string _selectedPath = "";
        internal readonly List<string> _selectedPaths = new List<string>();

        internal GPALFileSelector()
        {
            ControlType = FormControlType.FileSelector;
        }

        /// <summary>
        /// Sets the title bar text of the selection dialog.
        /// </summary>
        /// <param name="title">The desired title. If null/whitespace, defaults to "Select File/Folder".</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithTitle(string title)
        {
            _title = string.IsNullOrWhiteSpace(title) ? "Select File/Folder" : title;
            return this;
        }

        /// <summary>
        /// Sets the directory the selection dialog initially opens to.
        /// </summary>
        /// <param name="path">The initial directory path.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithInitialDirectory([Directory] string path)
        {
            _initialDirectory = path;
            return this;
        }

        /// <summary>
        /// Sets the file type filter used by the <see cref="OpenFileDialog"/> when
        /// <see cref="WithSelectFolder(bool)"/> is <c>false</c>.
        /// </summary>
        /// <param name="filter">The dialog filter string. If null/whitespace, defaults to "All files (*.*)|*.*".</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithFilter(string filter)
        {
            _filter = string.IsNullOrWhiteSpace(filter) ? "All files (*.*)|*.*" : filter;
            return this;
        }

        /// <summary>
        /// Sets whether the control opens a <see cref="FolderBrowserDialog"/> (folder selection) or an
        /// <see cref="OpenFileDialog"/> (file selection).
        /// </summary>
        /// <param name="folderMode"><c>true</c> to select folders; <c>false</c> [default] to select files.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithSelectFolder(bool folderMode)
        {
            _selectFolder = folderMode;
            return this;
        }

        /// <summary>
        /// Sets whether the file dialog allows selecting multiple files. Has no effect when
        /// <see cref="WithSelectFolder(bool)"/> is <c>true</c>.
        /// </summary>
        /// <param name="allowMulti"><c>true</c> to allow multiple file selection.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithMultiSelect(bool allowMulti)
        {
            _multiSelect = allowMulti;
            return this;
        }
        /// <summary>
        /// When <c>true</c>, renders as an editable text field with a small "..." browse button on the right,
        /// allowing the user to type a path or URL directly as well as browse for a file or folder.
        /// When <c>false</c> [default], renders as a "Browse..." button on the left with a read-only path display.
        /// </summary>
        /// <param name="compact"><c>true</c> for inline text + "..." layout.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithCompact(bool compact)
        {
            _compact = compact;
            return this;
        }
        /// <summary>
        /// Sets placeholder hint text shown in the text field when it is empty. Only applies in compact mode.
        /// </summary>
        public IAllowFileSelectorControlSettings WithPlaceholder(string placeholder)
        {
            _placeholder = placeholder ?? "";
            return this;
        }

        /// <summary>
        /// Sets the path the selector starts on, as though it had already been chosen.<br/>
        /// <see cref="SelectedPath"/> reads it back, so a form can open on a sensible default instead of an
        /// empty box. <see cref="SetPath"/> does the same thing after the control is built.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALFileSelector chromeProfile = (GPALFileSelector)GPAL.FileSelector
        ///         .WithSelectFolder(true)
        ///         .WithSelectedPath(@"C:\profiles\chrome\signedin")
        ///         .WithName("chromeProfile")
        ///         .ToGPALObject();
        /// </code>
        /// </example>
        /// <param name="path">The path to start with</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowFileSelectorControlSettings WithSelectedPath([FilePath] string path)
        {
            _selectedPath = path ?? "";

            _selectedPaths.Clear();

            if (false == string.IsNullOrEmpty(_selectedPath))
                _selectedPaths.Add(_selectedPath);

            return this;
        }

        public string SelectedPath => _selectedPath;
        /// <summary>
        /// All paths selected in the most recent dialog.
        /// </summary>
        public IReadOnlyList<string> SelectedPaths => _selectedPaths.AsReadOnly();

        /// <summary>
        /// Records the paths chosen in the selection dialog and refreshes the displayed text via
        /// <see cref="SyncDisplay"/>. Called after the dialog is closed.
        /// </summary>
        /// <param name="paths">The selected paths, or <c>null</c>/empty if none were selected.</param>
        internal void UpdateSelection(string[] paths)
        {
            _selectedPaths.Clear();
            if (paths != null && paths.Length > 0)
            {
                _selectedPaths.AddRange(paths);
                _selectedPath = paths[0];
                SyncDisplay();
            }
            else
            {
                _selectedPath = "";
            }
        }
        /// <summary>
        /// Updates the selection via <see cref="UpdateSelection(string[])"/> and then invokes any
        /// registered <see cref="ControlEventType.Change"/>, <see cref="ControlEventType.Click"/>, or
        /// <see cref="ControlEventType.Default"/> callbacks with a <see cref="FileSelectorEventArgs"/>
        /// describing the dialog result. Called after the selection dialog is closed.
        /// </summary>
        /// <param name="cancelled"><c>true</c> if the dialog was cancelled.</param>
        /// <param name="paths">The selected paths, or <c>null</c>/empty if none were selected.</param>
        internal void NotifySelection(bool cancelled, string[] paths)
        {
            UpdateSelection(paths);

            var args = new FileSelectorEventArgs(cancelled, SelectedPath, paths);

            foreach (var (evt, handler) in Callbacks)
            {
                ControlEventType target = evt == ControlEventType.Default ? ControlEventType.Change : evt;

                if (target == ControlEventType.Change ||
                    target == ControlEventType.Click ||
                    target == ControlEventType.Default)
                {
                    ((EventHandler)handler)?.Invoke(this, args);
                }
            }
        }
        public void Clear()
        {
            _selectedPath = "";
            _selectedPaths.Clear();
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
            {
                PostToUi(ctrl, () => ctrl.Text = "");
            }
        }

        public void SetPath([FilePath] string path)
        {
            _selectedPath = path ?? "";
            _selectedPaths.Clear();
            if (!string.IsNullOrEmpty(_selectedPath))
                _selectedPaths.Add(_selectedPath);
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
            {
                PostToUi(ctrl, () => ctrl.Text = _selectedPath);
            }
        }

        private void SyncDisplay()
        {
            if (WindowsControl is Control ctrl && true == Alive(ctrl))
            {
                string display = _selectedPaths.Count > 0
                    ? string.Join("; ", _selectedPaths)
                    : "";

                PostToUi(ctrl, () => ctrl.Text = display);
            }
        }
    }
}
