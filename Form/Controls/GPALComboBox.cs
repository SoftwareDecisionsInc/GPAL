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
    /// A dropdown list (ComboBox) for selecting one option from a predefined set of choices.
    /// Selection changes trigger WhenSelectedDo callback(s).
    /// By default uses DropDownList style (no free text entry).
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALComboBox browserSelector = (GPALComboBox)GPAL.ComboBox
    ///     .WithName("BrowserChoice")
    ///     .WithItems("Chrome", "Edge", "Firefox")
    ///     .WithSelectedIndex(0)
    ///     .WhenSelectedDo(selected => 
    ///         Debug.WriteLine($"Browser changed to: {selected}"))
    ///     .WithToolTip("Select the target browser for automation")
    ///     .ToGPALObject();
    /// </code>
    /// </example>
    public class GPALComboBox : GPALControl, IAllowComboBoxControlSettings
    {
        internal readonly List<string> items = new List<string>();
        internal int selectedIndex = -1;
        internal string selectedItem = null;

        // Items that stay in the list but cannot be chosen. A greyed option is clearer than one that silently
        // disappears.
        internal readonly HashSet<string> disabledItems = new HashSet<string>();

        // what to fall back to when a disabled item is picked, so the box never sits on something invalid
        internal int lastEnabledIndex = -1;

        internal GPALComboBox() 
        {
            ControlType = FormControlType.ComboBox;
        }

        /// <summary>
        /// Appends the given items to the dropdown list, in order.
        /// </summary>
        /// <param name="itemArray">The items to add.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithItems(params string[] itemArray)
        {
            if (null != itemArray)
            {
                items.AddRange(itemArray);
                ShowItems(itemArray);
            }
            return this;
        }

        // Keep the stored and on-screen lists in step. Before the form is shown only the stored list exists.
        private void ShowItems(IEnumerable<string> itemsToShow)
        {
            if (!(WindowsControl is ComboBox cb) || false == Alive(cb)) return;

            object[] adding = itemsToShow.Cast<object>().ToArray();

            OnUi(cb, () => cb.Items.AddRange(adding));
        }

        /// <summary>
        /// Greys an item and makes it unselectable, keeping it in the list. For an option that does not apply given
        /// other choices.
        /// </summary>
        /// <param name="item">The item text to disable.</param>
        public void DisableItem(string item)
        {
            if (true == string.IsNullOrEmpty(item)) return;

            disabledItems.Add(item);
            Redraw();
        }

        /// <summary>
        /// Makes a disabled item choosable again.
        /// </summary>
        /// <param name="item">The item text to enable.</param>
        public void EnableItem(string item)
        {
            if (true == string.IsNullOrEmpty(item)) return;

            disabledItems.Remove(item);
            Redraw();
        }

        /// <summary>
        /// Whether the item is currently choosable.
        /// </summary>
        /// <param name="item">The item text to ask about.</param>
        /// <returns>True when the item can be chosen.</returns>
        public bool IsItemEnabled(string item)
        {
            return false == disabledItems.Contains(item);
        }

        private void Redraw()
        {
            if (!(WindowsControl is ComboBox cb) || false == Alive(cb)) return;

            OnUi(cb, () => cb.Invalidate());
        }

        /// <summary>
        /// Empties the dropdown, so its contents can be replaced with a list that suits what has been chosen
        /// elsewhere on the form. Clears what is on screen as well as what the form was built with.
        /// </summary>
        public void ClearItems()
        {
            items.Clear();
            disabledItems.Clear();
            selectedIndex = -1;
            selectedItem = null;
            lastEnabledIndex = -1;

            if (!(WindowsControl is ComboBox cb) || false == Alive(cb)) return;

            OnUi(cb, () => cb.Items.Clear());
        }

        /// <summary>
        /// Appends the given items to the dropdown list, in order.
        /// </summary>
        /// <param name="itemCollection">The items to add.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithItems(IEnumerable<string> itemCollection)
        {
            if (null != itemCollection)
            {
                items.AddRange(itemCollection);
            }
            return this;
        }

        /// <summary>
        /// Appends the items from a non-generic array (e.g. the result of <see cref="Enum.GetValues"/>)
        /// to the dropdown list, converting each element to its string representation.
        /// </summary>
        /// <param name="itemArray">The array whose elements will be added.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithItems(Array itemArray)
        {
            if (null != itemArray)
            {
                foreach (object item in itemArray)
                    items.Add(item?.ToString() ?? string.Empty);
            }
            return this;
        }

        /// <summary>
        /// Sets which item is initially selected, by index. Ignored if <paramref name="index"/> is out of
        /// range of the items added via <see cref="WithItems(string[])"/>.
        /// </summary>
        /// <param name="index">The 0-based index of the item to select.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithSelectedIndex([Index] int index)
        {
            selectedIndex = index;
            selectedItem = null;
            return this;
        }

        /// <summary>
        /// Sets which item is initially selected, by its display text. If the item is not found
        /// in the list the selection is left at the default (-1 / no selection).
        /// </summary>
        /// <param name="name">The exact display text of the item to pre-select.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowComboBoxControlSettings WithSelectedItem(string name)
        {
            selectedItem = name;
            selectedIndex = -1;
            return this;
        }

        /// <summary>
        /// The text of the currently selected item.
        /// </summary>
        /// <returns>The selected item's text, or <c>null</c> if no item is selected.</returns>
        public string SelectedValue
        {
            get
            {
                int idx = SelectedIndex;
                if ((0 > idx) || (idx >= items.Count))
                    return null;
                return items[idx];
            }
        }

        /// <summary>
        /// The 0-based index of the currently selected item, or -1 if none is selected. Setting this
        /// writes through to the live control (if realized), marshalling to the UI thread via
        /// <see cref="Control.Invoke(Delegate)"/> if required. Ignored if set out of range of the items
        /// added via <see cref="WithItems(string[])"/>.
        /// </summary>

        public int SelectedIndex
        {
            // Read on the UI thread when called from another thread, as the setters write on it.
            get
            {
                int retVal = selectedIndex;

                if (WindowsControl is ComboBox cb && true == Alive(cb))
                    retVal = OnUiRead(cb, () => cb.SelectedIndex, retVal);

                return retVal;
            }
            set
            {
                if ((0 > value) || (value >= items.Count)) return;

                selectedIndex = value;
                if (WindowsControl is ComboBox cb && true == Alive(cb))
                {
                    OnUi(cb, () => cb.SelectedIndex = value);
                }
            }
        }
    }
}
