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

using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.GPALForm.GPALForm;
using static GenerallyPositive.Enums;
using GenerallyPositive.GPALForm;

namespace GenerallyPositive.GPALForm
{
    public partial class GPALForm
    {
        /// <summary>
        /// Define the initial width of your GPAL form
        /// </summary>
        /// <param name="width">Width in pixels</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithWidth(int width)
        {
            FormSettings.width = width;
            FormSettings.myForm.Width = width;
            // ensure the right edge is on-screen, but the left edge could be off-screen if the form is too wide
            if (FormSettings.left > (Screen.PrimaryScreen.Bounds.Width - width))
                FormSettings.left = Screen.PrimaryScreen.Bounds.Width - width - 2;

            FormSettings.myForm.InitialSize = new Size(FormSettings.myForm.Width, FormSettings.myForm.Height);

            return this;
        }
        /// <summary>
        /// Sets the width of every button on the form, in pixels.<br/>
        /// A button with its own .WithWidth keeps it. 0 restores full width. Existing buttons are resized too, so order
        /// relative to .WithFormControl does not matter, and it can change while the form is open.
        /// </summary>
        /// <param name="width">Width in pixels</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithButtonWidth(int width)
        {
            FormSettings.buttonWidth = width;

            foreach (GPALControl gPALControl in FormSettings.myForm.ControlList)
                if (gPALControl is GPALButton gPALButton
                    && 0 >= gPALButton.Width
                    && gPALButton.WindowsControl is Control windowsControl)
                {
                    if (0 < width)
                        ApplyWidth(width, windowsControl);
                    else
                        // No width restores full width by re-anchoring left and right.
                        GPALControl.OnUi(windowsControl, () =>
                            windowsControl.Anchor = windowsControl.Anchor | AnchorStyles.Left | AnchorStyles.Right);
                }

            return this;
        }
        /// <summary>
        /// Define the initial height of your GPAL form. Without it the form takes the height its content needs.
        /// </summary>
        /// <param name="height">Height in pixels</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithHeight(int height)
        {
            FormSettings.height = height;
            FormSettings.myForm.Height = height;
            // ensure the bottom is on-screen, but this could put the grab bar off-screen
            if (FormSettings.top > (Screen.PrimaryScreen.Bounds.Height - height))
                FormSettings.top = Screen.PrimaryScreen.Bounds.Height - height - 2;
            else
                FormSettings.top = (Screen.PrimaryScreen.Bounds.Height - height) / 2;  // center unless overridden by subsequent Top

            FormSettings.myForm.InitialSize = new Size(FormSettings.myForm.Width, FormSettings.myForm.Height);

            return this;
        }
        /// <summary>
        /// Define the initial coordinate of the top side of your GPAL form
        /// </summary>
        /// <param name="top"></param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithTop(int top)
        {
            FormSettings.top = top;
            FormSettings.myForm.Top = top;
            return this;
        }
        /// <summary>
        /// Define the initial coordinate of the left side of your GPAL form
        /// </summary>
        /// <param name="left"></param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithLeft(int left)
        {
            FormSettings.left = left;
            FormSettings.myForm.Left = left;
            return this;
        }
        /// <summary>
        /// Add a handler to call after a row of tokens is consumed and after all inputs are filled in with data.<br/>
        /// Only one CallAfterFillIn handler exists.<br/>
        /// Use FillInFrom[Database|File|GPALGrid] to loop thru data in your main workflow and then perform next workflow steps in the CallAfter handler.
        /// </summary>
        /// <param name="callAfterFillIn">Your callback</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions CallAfterFillIn(GPALForm.CallAfterFillInDelegate callAfterFillIn)
        {
            FormSettings.myForm.CallAfterFillIn = callAfterFillIn;
            return this;
        }
        /// <summary>
        /// Add a form control (GPALButton, GPALChart, etc.) to a GPALForm.<br/>
        /// Controls are displayed on one line in the order added.
        /// </summary>
        /// <example>
        /// <code>
        /// Form myForm = (Form)GPAL.Form
        ///     .WithTitle("Simple Form Test")
        ///     .WithFormControl(gPalButton1)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <param name="gPALControl"></param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithFormControl(GPALControl gPALControl)
        {
            return gPALControl switch
            {
                GPALButton btn => AddButton(btn),
                GPALChart ch => AddChart(ch),
                GPALComboBox cb => AddComboBox(cb),
                GPALDataGridView dgv => AddDataGridView(dgv),
                GPALDateTimePicker dtp => AddDateTimePicker(dtp),
                GPALCheckbox chk => AddCheckbox(chk),
                GPALFileSelector fs => AddFileSelector(fs),
                GPALInput inp => AddInput(inp),
                GPALLabel lbl => AddLabel(lbl),
                GPALLinkLabel lnk => AddLinkLabel(lnk),
                GPALSpacer space => AddSpacer(space),
                GPALListView lv => AddListView(lv),
                GPALMenuBar mb => AddMenuBar(mb),
                GPALNumericUpDown nud => AddNumericUpDown(nud),
                GPALPicture pic => AddPicture(pic),
                GPALProgressBar pb => AddProgressBar(pb),
                GPALSlider sl => AddSlider(sl),
                GPALRadioButton rb => AddRadioButton(rb),
                GPALRichTextBox rtb => AddRichTextBox(rtb),
                GPALStatusStrip ss => AddStatusStrip(ss),
                GPALToolbar tb => AddToolbar(tb),
                GPALTab tab => AddTab(tab),
                GPALSplitter sp => AddSplitter(sp),
                GPALGroupBox gb => AddGroupBox(gb),
                GPALTableLayoutPanel tlp => AddTableLayoutPanel(tlp),
                GPALTextArea ta => AddTextArea(ta),
                GPALTreeView tv => AddTreeView(tv),

                _ => this
            };
        }
        /// <summary>
        /// The title to display at the top of your form.
        /// </summary>
        /// <param name="title">Title to display</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithTitle(string title)
        {
            FormSettings.myForm.FormTitle = title;
            FormSettings.myForm.Text = title;
            return this;
        }
        /// <summary>
        /// The icon shown in the form's title bar and on the taskbar.<br/>
        /// An .ico is used as it is; any other picture (.png, .jpg, .bmp) is made into one.<br/>
        /// Without it the form shows the program's own icon when its project sets ApplicationIcon, and the GPAL
        /// robot head otherwise.
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.Form
        ///     .WithTitle("Order Entry")
        ///     .WithIcon(@"C:\icons\orders.ico")
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <param name="iconFile">The .ico, or a picture to make one from</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions WithIcon(GPALFile iconFile)
        {
            string path = iconFile?.Filename;

            if (false == System.IO.File.Exists(path))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Icon [{path}] was not found, the form keeps the icon it has", this, GPALObjectType.GPALForm);
            else if (true == path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                FormSettings.myForm.Icon = new Icon(path);
            else
                using (Bitmap picture = new Bitmap(path))
                    FormSettings.myForm.Icon = Icon.FromHandle(picture.GetHicon());

            return this;
        }
    }
}
