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
    /// A handle to a tree node returned by <see cref="GPALTreeView.AddNode(string)"/>.
    /// Use it to add children and control expansion.
    /// </summary>
    public class GPALTreeNode
    {
        private readonly TreeNode _node;

        internal GPALTreeNode(TreeNode node) { _node = node; }

        // the node this handle stands for, so the tree can hand one back and be given one
        internal TreeNode Node => _node;

        /// <summary>
        /// The node's label.
        /// </summary>
        public string Text
        {
            get
            {
                TreeView tv = _node.TreeView;
                string retVal = null;

                GPALControl.OnUiOrHere(tv, () => retVal = _node.Text);

                return retVal;
            }
            set
            {
                TreeView tv = _node.TreeView;

                GPALControl.OnUiOrHere(tv, () => _node.Text = value);
            }
        }

        /// <summary>
        /// Anything the workflow wants to carry on the node: the selector it stands for, the record it came
        /// from, an id to look up later. Untouched by GPAL.
        /// </summary>
        public object Tag
        {
            get { return _node.Tag; }
            set { _node.Tag = value; }
        }

        /// <summary>
        /// Makes this the selected node, thread-safe.
        /// </summary>
        public void Select()
        {
            TreeView tv = _node.TreeView;

            if (null == tv) return;

            GPALControl.OnUiOrHere(tv, () => tv.SelectedNode = _node);
        }

        /// <summary>
        /// Hides this node's children, thread-safe.
        /// </summary>
        public void Collapse()
        {
            TreeView tv = _node.TreeView;

            GPALControl.OnUiOrHere(tv, () => _node.Collapse());
        }

        /// <summary>
        /// Adds a child node under this node, thread-safe.
        /// </summary>
        /// <param name="text">The child node label.</param>
        /// <returns>The new child node, for further nesting.</returns>
        public GPALTreeNode AddChild(string text)
        {
            TreeNode child = null;
            TreeView tv = _node.TreeView;
            GPALControl.OnUiOrHere(tv, () => child = _node.Nodes.Add(text));
            return child != null ? new GPALTreeNode(child) : null;
        }


        /// <summary>
        /// Whether this node can be selected. A disabled node stays on the tree, greyed. Its children are unchanged;
        /// disable them too for the whole branch.
        /// </summary>
        public bool Enabled
        {
            get
            {
                return SystemColors.GrayText != _node.ForeColor;
            }
            set
            {
                TreeView tv = _node.TreeView;

                // The color is the state; TreeNode has no Enabled property.
                MethodInvoker paint = () => _node.ForeColor = true == value ? SystemColors.WindowText : SystemColors.GrayText;

                GPALControl.OnUiOrHere(tv, paint);
            }
        }

        /// <summary>
        /// Expands this node to show its children, thread-safe.
        /// </summary>
        public void Expand()
        {
            TreeView tv = _node.TreeView;
            GPALControl.OnUiOrHere(tv, () => _node.Expand());
        }
    }
}
