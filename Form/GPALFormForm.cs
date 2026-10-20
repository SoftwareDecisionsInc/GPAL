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
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPALForm.GPALForm;

namespace GenerallyPositive.GPALForm
{
    internal class GPALFormForm : System.Windows.Forms.Form
    {
        internal List<GPALControl> ControlList { get; set; } = null;
        internal string FormName { get; set; } = null;
        internal string FormTitle { get; set; } = null;
        internal GPALFile InputFile { get; set; }
        internal GPALDatabase InputDatabase { get; set; }
        internal CallAfterFillInDelegate CallAfterFillIn { get; set; }

        internal Size InitialSize { get; set; }

        internal GPALFormForm()
        {
            Icon icon = DefaultIcon();

            if (null != icon)
                Icon = icon;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int iconIndex, IntPtr[] largeIcons, IntPtr[] smallIcons, uint icons);

        /// <summary>
        /// The icon a form starts with, in place of the WinForms default: the program's own when its project names
        /// one with ApplicationIcon, and the GPAL robot head when it does not. WithIcon replaces either.
        /// </summary>
        /// <returns>The icon to show in the title bar and taskbar</returns>
        private static Icon DefaultIcon()
        {
            Icon result = null;
            string exe = System.Windows.Forms.Application.ExecutablePath;

            // With index -1, ExtractIconEx returns the icon count. An exe without ApplicationIcon has none, and
            // ExtractAssociatedIcon would return Windows' blank icon.
            if (0 < ExtractIconEx(exe, -1, null, null, 0))
                result = Icon.ExtractAssociatedIcon(exe);
            else
                using (System.IO.Stream head = typeof(GPALFormForm).Assembly.GetManifestResourceStream("GenerallyPositive.Form.gpal-head.ico"))
                    if (null != head)
                        result = new Icon(head);

            return result;
        }

        /// <summary>
        /// find the tab the current control is on and ensure it is in focus before focusing on the control
        /// </summary>
        /// <param name="controlEntry"></param>
        internal void SwitchToTab(GPALControl controlEntry)
        {
            Control control = (Control)controlEntry.WindowsControl;

            while (null != control.Parent)
            {
                if (typeof(TabPage) == control.GetType())
                    ((TabControl)control.Parent).SelectedTab = (TabPage)control;
                control = control.Parent;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, System.Windows.Forms.Keys keyData)
        {
            foreach (GPALControl controlEntry in ControlList)
                if (controlEntry.ShortCutKey == keyData)
                {
                    SwitchToTab(controlEntry);
                    switch (controlEntry.ControlType)
                    {
                        case FormControlType.Button:
                            ((Button)controlEntry.WindowsControl).PerformClick();
                            break;
                        case FormControlType.Checkbox:
                            ((CheckBox)controlEntry.WindowsControl).Select();
                            break;
                        case FormControlType.ComboBox:
                            var comboBox = (ComboBox)controlEntry.WindowsControl;
                            comboBox.Focus();
                            comboBox.DroppedDown = true; 
                            break;
                        case FormControlType.Label:
                            ((Label)controlEntry.WindowsControl).Select();
                            break;
                        case FormControlType.LinkLabel:
                            ((GPALLinkLabel)controlEntry).Open();
                            break;
                        case FormControlType.RadioButton:
                            ((RadioButton)controlEntry.WindowsControl).Select();
                            break;
                        case FormControlType.DateTimePicker:
                            ((DateTimePicker)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.NumericUpDown:
                            ((NumericUpDown)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.DataGridView:
                            ((DataGridView)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.ListView:
                            ((ListView)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.TreeView:
                            ((TreeView)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.Input:
                        case FormControlType.TextArea:
                            ((TextBox)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.Chart:
                            ((Chart)controlEntry.WindowsControl).Focus();
                            break;
                        case FormControlType.Tab:
                            ((TabPage)controlEntry.WindowsControl).Focus();
                            break;
                    }
                    return true;
                }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (InitialSize.Width > 0 && InitialSize.Height > 0)
            {
                // Return the fluent-set size as preferred
                return InitialSize;
            }

            // Fallback to default behavior if no explicit size was set
            return base.GetPreferredSize(proposedSize);
        }
    }
}

