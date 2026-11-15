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
    /// A table layout panel instantiated with GPAL.TableLayoutPanel for use on GPAL forms, used to
    /// arrange other controls in a configurable grid of rows and columns.
    /// </summary>
    public class GPALTableLayoutPanel : GPALControl, IAllowControlSettingsAndTableLayoutPanelSettings
    {
        internal GPALTableLayoutPanel()
        {
            ControlType = FormControlType.TableLayoutPanel;
        }

        // Values used before the form is built, as IsEnabled backs Enabled; the three properties below use them until
        // there is a WindowsControl. Defaults are the WinForms ones.
        private TableLayoutPanelGrowStyle growStyle = TableLayoutPanelGrowStyle.AddRows;
        private int columnCount = 0;
        private int rowCount = 0;
        /// <summary>
        /// The direction in which the panel automatically adds new rows or columns when it runs out of
        /// space, mirroring the underlying <see cref="TableLayoutPanel.GrowStyle"/>.
        /// </summary>
        /// <returns>The grow style currently in use.</returns>
        public TableLayoutPanelGrowStyle GrowStyle
        {
            get
            {
                TableLayoutPanelGrowStyle retVal = growStyle;

                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    retVal = OnUiRead(panel, () => panel.GrowStyle, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    OnUi(panel, () => panel.GrowStyle = value);

                growStyle = value;   // kept either way, so the value survives the control not being there yet
            }
        }
        /// <summary>
        /// The number of columns in the panel, mirroring the underlying <see cref="TableLayoutPanel.ColumnCount"/>.
        /// </summary>
        /// <returns>The column count currently in use.</returns>
        public int ColumnCount
        {
            get
            {
                int retVal = columnCount;

                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    retVal = OnUiRead(panel, () => panel.ColumnCount, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    OnUi(panel, () => panel.ColumnCount = value);

                columnCount = value;   // kept either way, so the value survives the control not being there yet
            }
        }

        /// <summary>
        /// The number of rows in the panel, mirroring the underlying <see cref="TableLayoutPanel.RowCount"/>.
        /// </summary>
        /// <returns>The row count currently in use.</returns>
        public int RowCount
        {
            get
            {
                int retVal = rowCount;

                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    retVal = OnUiRead(panel, () => panel.RowCount, retVal);

                return retVal;
            }
            set
            {
                if (WindowsControl is TableLayoutPanel panel && true == Alive(panel))
                    OnUi(panel, () => panel.RowCount = value);

                rowCount = value;   // kept either way, so the value survives the control not being there yet
            }
        }


        /// <summary>
        /// Sets the number of columns in the panel.
        /// </summary>
        /// <param name="columnCount">The desired column count.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettingsAndTableLayoutPanelSettings WithColumnCount(int columnCount)
        {
            ColumnCount = columnCount;
            return this;
        }

        /// <summary>
        /// Fluent interface to set the initial state of the control
        /// </summary>
        /// <param name="enabled">True [default]<br/>False</param>
        /// <returns>Fluent interface to define more settings</returns>
        public new IAllowControlSettingsAndTableLayoutPanelSettings WithEnabled(bool enabled = true)
        {
            this.Enabled = enabled;
            return this;
        }
        /// <summary>
        /// Sets the direction in which the panel automatically adds new rows or columns when it runs out
        /// of space.
        /// </summary>
        /// <param name="tableLayoutPanelGrowStyle">The desired grow style.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettingsAndTableLayoutPanelSettings WithGrowStyle(TableLayoutPanelGrowStyle tableLayoutPanelGrowStyle)
        {
            this.GrowStyle = tableLayoutPanelGrowStyle;
            return this;
        }

        /// <summary>
        /// Sets the number of rows in the panel.
        /// </summary>
        /// <param name="rowCount">The desired row count.</param>
        /// <returns>Fluent interface to define more settings.</returns>
        public IAllowControlSettingsAndTableLayoutPanelSettings WithRowCount(int rowCount)
        {
            RowCount = rowCount;
            return this;
        }
    }
}
