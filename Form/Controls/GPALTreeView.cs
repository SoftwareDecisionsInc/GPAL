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
    /// A tree view control instantiated with GPAL.TreeView for use on GPAL forms.
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALTreeView gPalTree = (GPALTreeView)GPAL.TreeView
    ///     .WithCheckBoxes(true)
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALTreeView : GPALControl, IAllowTreeViewControlSettings
    {
        private bool _checkBoxes = false;
        private bool _showLines = true;
        private bool _showPlusMinus = true;

        internal GPALTreeView()
        {
            ControlType = FormControlType.TreeView;
        }

        /// <summary>
        /// Sets whether checkboxes are displayed next to each tree node.
        /// </summary>
        /// <param name="enable"><c>true</c> to show checkboxes; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowTreeViewControlSettings WithCheckBoxes(bool enable)
        {
            _checkBoxes = enable;
            return this;
        }

        /// <summary>
        /// Sets whether lines are drawn between sibling nodes and their parent.
        /// </summary>
        /// <param name="show"><c>true</c> to show lines; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowTreeViewControlSettings WithShowLines(bool show)
        {
            _showLines = show;
            return this;
        }

        /// <summary>
        /// Sets whether plus/minus expand buttons are displayed next to nodes that have children.
        /// </summary>
        /// <param name="show"><c>true</c> to show the plus/minus buttons; otherwise <c>false</c>.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowTreeViewControlSettings WithShowPlusMinus(bool show)
        {
            _showPlusMinus = show;
            return this;
        }

        /// <summary>
        /// Whether checkboxes are displayed next to each tree node, as set via <see cref="WithCheckBoxes(bool)"/>.
        /// </summary>
        public bool CheckBoxes => _checkBoxes;
        /// <summary>
        /// Whether lines are drawn between sibling nodes and their parent, as set via <see cref="WithShowLines(bool)"/>.
        /// </summary>
        public bool ShowLines => _showLines;
        /// <summary>
        /// Whether plus/minus expand buttons are displayed, as set via <see cref="WithShowPlusMinus(bool)"/>.
        /// </summary>
        public bool ShowPlusMinus => _showPlusMinus;

        /// <summary>
        /// Removes all nodes from the tree, thread-safe.
        /// </summary>
        public void ClearNodes()
        {
            // Also drop nodes added before the tree was built, so a clear means the same before and after.
            if (!(WindowsControl is TreeView tv) || false == Alive(tv))
            {
                lock (pendingNodes)
                    pendingNodes.Clear();
            }
            else
                OnUi(tv, () => tv.Nodes.Clear());
        }

        /// <summary>
        /// The node currently selected, or null when nothing is. Settable, so a workflow can put the user
        /// where it wants them.
        /// </summary>
        public GPALTreeNode SelectedNode
        {
            get
            {
                if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return null;

                TreeNode node = null;

                OnUi(tv, () => node = tv.SelectedNode);

                return null != node ? new GPALTreeNode(node) : null;
            }
            set
            {
                if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return;

                MethodInvoker select = () => tv.SelectedNode = value?.Node;

                OnUi(tv, select);
            }
        }

        /// <summary>
        /// Stops the tree redrawing until <see cref="EndUpdate"/>, so a large tree does not fill in row by row.
        /// </summary>
        public void BeginUpdate()
        {
            if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return;

            OnUi(tv, () => tv.BeginUpdate());
        }

        /// <summary>
        /// Lets the tree redraw again after <see cref="BeginUpdate"/>.
        /// </summary>
        public void EndUpdate()
        {
            if (!(WindowsControl is TreeView tv) || false == Alive(tv)) return;

            OnUi(tv, () => tv.EndUpdate());
        }

        /// <summary>
        /// Adds a top-level node and returns a <see cref="GPALTreeNode"/> for attaching children.
        /// Thread-safe.
        /// </summary>
        /// <param name="text">The node label.</param>
        public GPALTreeNode AddNode(string text)
        {
            TreeNode node = null;

            // A node added before the tree is built is created now and kept. The workflow gets a real node (AddChild,
            // Text, Tag, Enabled and Expand work), and the tree takes it with its children once built.
            if (!(WindowsControl is TreeView tv) || false == Alive(tv))
            {
                node = new TreeNode(text);

                lock (pendingNodes)
                    pendingNodes.Add(node);
            }
            else
                OnUi(tv, () => node = tv.Nodes.Add(text));

            return node != null ? new GPALTreeNode(node) : null;
        }

        // the top-level nodes added before the form built the tree, in the order they were added
        private readonly List<TreeNode> pendingNodes = new List<TreeNode>();

        /// <summary>
        /// The form has built the tree, so the nodes added before now go in.
        /// </summary>
        internal override void Realized()
        {
            if (WindowsControl is TreeView tv)
                lock (pendingNodes)
                {
                    foreach (TreeNode node in pendingNodes)
                        tv.Nodes.Add(node);

                    pendingNodes.Clear();
                }
        }
    }
}
