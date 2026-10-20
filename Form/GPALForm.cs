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
    /// <summary>
    /// Form object that contains the fluent methods to create interactive forms.<br/>
    /// Instatiated using GPAL.Form
    /// </summary>
    /// <example>
    /// <code>
    /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
    ///     .WithName("button1")
    ///     .WithText("Exit")
    ///     .WithCallback&lt;EventHandler&gt;(ExitButtonClickHandler)
    ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);  // Alt-E
    ///     
    /// static void Main(string[] args)
    /// {
    ///     Form myForm = (Form)GPAL.Form
    ///         .WithTitle("Simple Form Test")
    ///         .WithFormControl(gPalButton1)
    ///         .ShowDialog();
    /// }
    ///     
    /// private static void ExitButtonClickHandler(object sender, EventArgs e)
    /// {
    ///     Control control = (Control)sender;
    ///     
    ///     while (null != control.Parent)
    ///         control = control.Parent;
    ///     control.Hide();
    /// }
    /// </code>
    /// </example>
    /// <returns>Fluent interface to create your GPAL GUI</returns>
    public class GPALForm : IAllowFormSettingsAndActions, IAllowFormActionsAndFillIn, IAllowCallBack
    {
        /// <summary>
        /// Delegate callback for the CallAfterFillIn EventHandler which will be invoked after each row of tokens is consumed.<br/>
        /// Invoked whenever FillInFrom/FilllInFromDatabase/FillInFrom are used
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
        ///     .WithName("Start Workflow")
        ///     .WithText("Start The Workflow")
        ///     .WithCallback&lt;EventHandler&gt;(StartButtonClickHandler)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.S);  // Alt-S
        ///     
        /// public static CallIfStatus CallAFterFillIn(GPALForm myForm, GPALGrid&lt;string&gt; tokens, int tokenIdx) // text filled in search box, click 'Search' button
        /// {
        ///     // with GPAL forms, you have direct access to each control
        ///     // start the workflow after the input has filled in
        ///     ((Button)GPalButton1.WindowsControl).Click();
        ///
        ///     return CallIfStatus.NotHandled;
        /// }
        /// </code>
        /// </example>
        /// <param name="myForm">The  GPALForm being filled in</param>
        /// <param name="tokens">The current GPALGrid&lt;string&gt; of tokens</param>
        /// <param name="tokenIdx">The current token row index</param>
        /// <returns>CallIfStatus.Terminate if the handler requests the application to exit, otherwise the return value is ignored</returns>
        public delegate CallIfStatus CallAfterFillInDelegate(GPALForm myForm, IGPALGrid<string> tokens, int tokenIdx);

        private readonly System.Windows.Forms.ToolTip _toolTip = new System.Windows.Forms.ToolTip
        {
            AutoPopDelay = 5000,
            InitialDelay = 500,
            ShowAlways = true
        };

        /// <summary>
        /// Return a GPALForm so that GPAL.Form does not have to be cast.
        /// </summary>
        /// <returns>A GPALForm object</returns>
        /// <example>
        /// <code>
        /// GPALForm myForm = GPAL.Form
        ///     .WithTitle("Simple Form Test")
        ///     .WithLeft(1500)
        ///     .WithTop(100)
        ///     .WithWidth(350)
        ///     .WithFormControl(gPALLabel)
        ///     .WithFormControl(gPalExitButton)
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        public dynamic ToGPALObject()
        {
            return this;
        }
        /// <summary>
        /// The name of your form
        /// Currently not used, but will be used for Information and Exceptional channel messages.
        /// </summary>
        public string FormName
        {
            get
            {
                return FormSettings.myForm.FormName;
            }
            internal set
            {
                FormSettings.myForm.FormName = value;
            }
        }

        private FormSettings FormSettings { get; set; }

        // set by the first GPAL form, so the text rendering warning is published once
        private static bool textRenderingWarned;

        internal GPALForm()
        {
            // GPAL chooses GDI text, which draws emoji, at its first use. A window made before that keeps Windows Forms
            // from taking the choice, so say why emoji will draw as empty boxes
            if (true == GPAL.TextRenderingRefused && false == textRenderingWarned)
            {
                textRenderingWarned = true;

                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "A window was made before GPAL was first used, so Windows Forms kept its own text rendering and emoji on GPAL controls draw as empty boxes. Use GPAL before starting a Timer or showing a window", this, GPALObjectType.GPALForm);
            }

            FormSettings = new FormSettings();
            FormSettings.myForm = new GPALFormForm()
            {
                ControlList = new List<GPALControl>(),
            };

            FormSettings.myForm.SuspendLayout();
            FormSettings.myForm.Left = FormSettings.left;
            FormSettings.myForm.Top = FormSettings.top;
            FormSettings.myForm.Width = FormSettings.width;
            FormSettings.myForm.Height = FormSettings.height;
            FormSettings.myForm.InitialSize = new Size(FormSettings.width, FormSettings.height);

            FormSettings.myForm.AutoSize = true;

            Size _initialPreferredSize = FormSettings.myForm.Size;

            FormSettings.TableLayoutPanel = new TableLayoutPanel()
            {
                //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoScroll = true,
            };
            FormSettings.myForm.Controls.Add(FormSettings.TableLayoutPanel);

            System.Windows.Forms.Application.EnableVisualStyles();
        }

        #region <Settings>
        internal GPALFormForm Form
        {
            get
            {
                return FormSettings.myForm;
            }
        }
        internal List<GPALControl> ControlList
        {
            get
            {
                return FormSettings.myForm.ControlList;
            }
        }
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
            // todo - how to handle out of bounds settings?
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
        /// The current left coordinate of the form, in pixels.
        /// </summary>
        public int Left => FormSettings.myForm.Left;
        /// <summary>
        /// The current top coordinate of the form, in pixels.
        /// </summary>
        public int Top => FormSettings.myForm.Top;
        /// <summary>
        /// The current width of the form, in pixels.
        /// </summary>
        public int Width => FormSettings.myForm.Width;
        /// <summary>
        /// The current height of the form, in pixels.
        /// </summary>
        public int Height => FormSettings.myForm.Height;
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
        #endregion <Settings>
        #region <Forms Actions>
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

                _ => this   // or throw new NotSupportedException(...)
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
        /// <summary>
        /// Fill in (overwrite) the forms inputs using tokens derived from the database.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputDatabase">GPAL Database object</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions FillInFrom(GPALDatabase inputDatabase)
        {
            FormSettings.myForm.InputDatabase = inputDatabase;
            DatabaseHelper.TokenizeDatabase(inputDatabase);
            FormHelper.FillInWithTokens(this, new List<IGPALGrid<string>> { inputDatabase.Tokens }, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Append to the end of the forms inputs using tokens derived from the database.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputDatabase">GPAL Database object</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions AppendFrom(GPALDatabase inputDatabase)
        {
            FormSettings.myForm.InputDatabase = inputDatabase;
            DatabaseHelper.TokenizeDatabase(inputDatabase);
            FormHelper.FillInWithTokens(this, new List<IGPALGrid<string>> { inputDatabase.Tokens }, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Insert at the beginning of the forms inputs using tokens derived from the database.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputDatabase">GPAL Database object</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions InsertFrom(GPALDatabase inputDatabase)
        {
            FormSettings.myForm.InputDatabase = inputDatabase;
            DatabaseHelper.TokenizeDatabase(inputDatabase);
            FormHelper.FillInWithTokens(this, new List<IGPALGrid<string>> { inputDatabase.Tokens }, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Fill in (overwrite) the forms inputs using tokens derived from the file.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputFile">GPAL File object</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions FillInFrom(GPALFile inputFile)
        {
            FormSettings.myForm.InputFile = inputFile;
            if (false == FileHelper.TokenizeFile(inputFile))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to tokenize file [{inputFile.Filename}]. Continuing.", inputFile, GPALObjectType.GPALFile);
                return this;
            }
            FormHelper.FillInWithTokens(this, ((IGPALFileInternal)inputFile).TokenList, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Append to the end of the forms inputs using tokens derived from the file.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputFile">GPAL File object</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions AppendFrom(GPALFile inputFile)
        {
            FormSettings.myForm.InputFile = inputFile;
            if (false == FileHelper.TokenizeFile(inputFile))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to tokenize file [{inputFile.Filename}]. Continuing.", inputFile, GPALObjectType.GPALFile);
                return this;
            }
            FormHelper.FillInWithTokens(this, ((IGPALFileInternal)inputFile).TokenList, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Insert at the beginning of the forms inputs using tokens derived from the file.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputFile">GPAL File object</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions InsertFrom(GPALFile inputFile)
        {
            FormSettings.myForm.InputFile = inputFile;
            if (false == FileHelper.TokenizeFile(inputFile))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to tokenize file [{inputFile.Filename}]. Continuing.", inputFile, GPALObjectType.GPALFile);
                return this;
            }
            FormHelper.FillInWithTokens(this, ((IGPALFileInternal)inputFile).TokenList, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Fill in (overwrite) the forms inputs using tokens derived from the GPALGrid.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputGrid">Input tokens rows/column in nested lists</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions FillInFrom(IGPALGrid<string> inputGrid)
        {
            FormHelper.FillInWithTokens(this, new List<IGPALGrid<string>> { inputGrid }, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Append to the end of the forms inputs using tokens derived from the GPALGrid.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputGrid">Input tokens rows/column in nested lists</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions AppendFrom(IGPALGrid<string> inputGrid)
        {
            FormHelper.FillInWithTokens(this, new List<IGPALGrid<string>> { inputGrid }, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Insert at the beginning of the forms inputs using tokens derived from the GPALGrid.<br/>
        /// Used after .Show()<br/>
        /// Use with CallAfterFillIn to automate your form
        /// </summary>
        /// <param name="inputGrid">Input tokens rows/column in nested lists</param>
        /// <returns>Fluent interface for defining your GPAL form</returns>
        public IAllowFormSettingsAndActions InsertFrom(IGPALGrid<string> inputGrid)
        {
            FormHelper.FillInWithTokens(this, new List<IGPALGrid<string>> { inputGrid }, WriteMode.Insert);
            return this;
        }
        // An exception from a button's workflow must not close the form. Publish it and keep the form open.
        private static bool workflowFailureHandled = false;

        private void KeepFormAliveOnWorkflowFailure()
        {
            if (true == workflowFailureHandled) return;

            workflowFailureHandled = true;

            // Only subscribe; the exception mode cannot be set once a control exists on the thread. WinForms sends
            // exceptions to an attached handler anyway.
            System.Windows.Forms.Application.ThreadException += (sender, args) =>
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"The form kept going after [{args.Exception?.GetType().Name}]", null, GPALObjectType.GPALForm, args.Exception);
        }

        /// <summary>
        /// Show the GPAL form as a modal. Use LAST in a form workflow.<br/>
        /// <b>NOTE:</b> This blocks workflows and FillIn methods, use Show() with FillIns
        /// </summary>
        /// <returns>Fluent interface allowing you to show or hide your GPAL form</returns>
        public IAllowFormActions ShowDialog()
        {
            KeepFormAliveOnWorkflowFailure();
            SizeForm();
            FormSettings.myForm.ResumeLayout();
            if (true == FormSettings.myForm.Visible)
                FormSettings.myForm.Hide();
            FormSettings.myForm.ShowDialog();

            return this;
        }
        /// <summary>
        /// Show the GPAL form so it may be automated by tokens. Use ShowDialog when the form workflow ends.<br/>
        /// </summary>
        /// <example>
        /// <code>
        /// GenerallyPositive.Form.Form myForm = (GenerallyPositive.Form.Form)GPAL.Form
        ///     .WithTitle("Simple Form Test")
        ///     .WithLeft(1500)
        ///     .WithTop(100)
        ///     .WithWidth(350)
        ///     .WithFormControl(gPALLabel)
        ///     .WithFormControl(gPalInput)
        ///     .WithFormControl(gPALTextArea)
        ///     .CallAfterFillIn(CallAFterFillIn)   // call after the form inputs are filled with tokens
        ///     .Show()
        ///     .FillInFrom(inputFile)  // fill in the two input using tokens (eg., "test, this")
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface which allows you to automate entering tokens</returns>
        public IAllowFormActionsAndFillIn Show()
        {
            KeepFormAliveOnWorkflowFailure();
            SizeForm();
            FormSettings.myForm.ResumeLayout();
            if (true == FormSettings.myForm.Visible)
                FormSettings.myForm.Hide();
            FormSettings.myForm.Show();

            return this;
        }
        /// <summary>
        /// Hide the GPAL form. This will essentially terminate the program if .ShowDialog() is the last method in main and hide is called via a button click handler.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
        ///     .WithName("button1")
        ///     .WithText("Exit")
        ///     .WithCallback&lt;EventHandler&gt;(ExitButtonClickHandler)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);  // Alt-E
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalButton1)
        ///         .ShowDialog();
        /// }
        ///     
        /// private static void ExitButtonClickHandler(object sender, EventArgs e)
        /// {
        ///     Control control = (Control)sender;
        ///     
        ///     while (null != control.Parent)
        ///         control = control.Parent;
        ///     control.Hide();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface which allows you to control your form.</returns>
        public IAllowFormActions Hide()
        {
            FormSettings.myForm.Hide();
            return this;
        }
        #endregion <Forms Actions>
        #region Helpers
        /// <summary>
        /// Applies a control's width, when a workflow asked for one, and stops it stretching.<br/>
        /// Every control is anchored left and right so it fills its cell, which is what makes a button span the
        /// whole form. Dropping those two anchors leaves the control at the width it was given and WinForms
        /// centers it in the cell. A control with no width keeps the anchor its Add method gave it.
        /// </summary>
        /// <param name="width">The width to give it, in pixels. Zero or less leaves the control alone</param>
        /// <param name="control">The Windows Forms control to size</param>
        private static void ApplyWidth(int width, Control control)
        {
            if (0 >= width)
                return;

            // Marshalled; the width can change while the form is open, from another thread.
            GPALControl.OnUi(control, () =>
            {
                control.Width = width;
                control.Anchor = control.Anchor & ~(AnchorStyles.Left | AnchorStyles.Right);
            });
        }

        /// <summary>
        /// Applies the tooltip text and auto-pop delay configured on a GPAL control to its underlying Windows Forms control.
        /// </summary>
        /// <param name="gpalControl">GPAL control supplying ToolTipText and ToolTipAutoPopDelayMs.</param>
        /// <param name="winControl">Underlying Windows Forms control to attach the tooltip to.</param>
        private void ApplyToolTip(GPALControl gpalControl, Control winControl)
        {
            if (!string.IsNullOrWhiteSpace(gpalControl.ToolTipText))
            {
                FormSettings.ToolTip.SetToolTip(winControl, gpalControl.ToolTipText);
                if (gpalControl.ToolTipAutoPopDelayMs != 5000)
                    FormSettings.ToolTip.AutoPopDelay = gpalControl.ToolTipAutoPopDelayMs;
            }
        }

        // Marker on a built control whose definition had a height, so the check does not need a name.
        internal const string SetHeightMarker = "gpal.height.set";

        /// <summary>
        /// Whether this control was given a height of its own, by name or by the marker put on it when it was
        /// realized. A control that was is laid out at that height rather than stretched or auto-sized.
        /// </summary>
        private static bool HasASetHeight(Control control, HashSet<string> sized)
        {
            return true == sized.Contains(control.Name ?? "")
                || SetHeightMarker.Equals(control.Tag as string);
        }

        /// <summary>
        /// Whether a control's row should absorb the spare height of the panel.<br/>
        /// True for containers, and for scrolling controls given no explicit height: grid, list, tree and
        /// multiline text boxes. False for everything else, and false for any control whose
        /// height was set with WithHeight, which is honoured as written.
        /// </summary>
        /// <param name="control">The control sitting in the row</param>
        /// <param name="sized">Names of controls that were given an explicit height</param>
        /// <returns>True when the row should grow with the form</returns>
        private static bool GrowsWithTheForm(Control control, HashSet<string> sized)
        {
            bool retVal = false;

            if (false == HasASetHeight(control, sized))
                // A container must take the spare height, or it shrinks to its preferred size (an AutoSize TabControl
                // is just a tab strip).
                // TableLayoutPanel is not listed: a nested one is a fixed-shape composite, such as a file selector, and
                // stretching it leaves its contents at the top of an empty area.
                retVal = control is TabControl
                      || control is SplitContainer
                      // Multiline only: a taller single-line TextBox shows no more text.
                      || (control is TextBoxBase textBox && true == textBox.Multiline)
                      || control is ListView
                      || control is TreeView
                      || control is DataGridView
                      || control is ListBox;

            return retVal;
        }

        /// <summary>
        /// Applies row styles to a panel that has none.<br/>
        /// A TableLayoutPanel with no RowStyles decides its own row heights and its own preferred height, and
        /// that preferred height is what AutoScroll sizes the scrollbar from. Stating the rows makes the
        /// preferred height the sum of the control heights, so a scrollbar appears only when the controls do
        /// not fit.<br/>
        /// Each control is pinned to its own row and given AutoSize. Controls returning true from
        /// <see cref="GrowsWithTheForm"/> get Percent instead and share the remainder. No filler row is added:
        /// AutoScroll sizes the scrollbar from the preferred height, which a Percent row inflates past the
        /// content, and unused height below AutoSize rows is simply left blank.<br/>
        /// Pinning is required: controls added without a cell position are placed by the layout engine as the
        /// table grows, and setting RowCount or ColumnCount afterwards reflows them.
        /// </summary>
        /// <param name="panel">The panel to settle, and any panel nested inside it</param>
        /// <param name="sized">Names of controls that were given an explicit height</param>
        private static void SettleRows(TableLayoutPanel panel, HashSet<string> sized)
        {
            if (null == panel) return;

            SettleInside(panel, sized);

            // A panel with explicit RowStyles is left as it was written, and so is a real grid: this stacks one
            // control per row in one column, which is not what a multi-column panel is for
            if (0 < panel.RowStyles.Count || 1 < panel.ColumnCount) return;

            // RowCount is not updated by Controls.Add, so it reads zero here
            Control[] inOrder = panel.Controls.Cast<Control>().ToArray();

            if (1 > inOrder.Length) return;

            panel.SuspendLayout();

            panel.ColumnCount = 1;
            panel.ColumnStyles.Clear();
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            panel.RowCount = inOrder.Length;

            for (int row = 0; row < inOrder.Length; row++)
            {
                panel.SetCellPosition(inOrder[row], new TableLayoutPanelCellPosition(0, row));


                // AutoSize sizes a row from the control PreferredSize, which for a SplitContainer, and for any
                // control given a height with WithHeight, is not the height it is meant to be. Those rows are set
                // to that height outright. Everything else keeps AutoSize, which is what a label or a button wants
                bool ownHeight = 0 < inOrder[row].Height
                             && (inOrder[row] is SplitContainer
                              || true == HasASetHeight(inOrder[row], sized));

                panel.RowStyles.Add(true == GrowsWithTheForm(inOrder[row], sized)
                                    ? new RowStyle(SizeType.Percent, 100f)
                                    : true == ownHeight
                                      // Include the margins in the row height, or the control is clipped.
                                      ? new RowStyle(SizeType.Absolute, inOrder[row].Height
                                                                     + inOrder[row].Margin.Top
                                                                     + inOrder[row].Margin.Bottom)
                                      : new RowStyle(SizeType.AutoSize));
            }


            panel.ResumeLayout(true);

            settled.Add(panel);

            // AutoScroll uses the preferred height, which exceeds the drawn rows. Re-check whenever the panel may have
            // resized and turn AutoScroll off when everything fits. Hook HandleCreated too, since nothing can be
            // measured before the form is shown.
            panel.HandleCreated += (sender, args) => ScrollOnlyWhenNeeded(panel);
            panel.SizeChanged += (sender, args) => ScrollOnlyWhenNeeded(panel);
            panel.VisibleChanged += (sender, args) => ScrollOnlyWhenNeeded(panel);

            ScrollOnlyWhenNeeded(panel);
        }

        // Walks every child of every kind. A tab panel sits under a TabPage, a splitter pane under a
        // SplitterPanel and a group box panel under the box, so anything looking only for TableLayoutPanel
        // children reaches none of them
        private static void SettleInside(Control parent, HashSet<string> sized)
        {
            foreach (Control child in parent.Controls)
                if (child is TableLayoutPanel nested)
                    SettleRows(nested, sized);
                else
                    SettleInside(child, sized);
        }
        // The panels this method laid out; only these may scroll. A skipped panel is a custom layout, such as the
        // two-column file selector.
        private static readonly HashSet<TableLayoutPanel> settled = new HashSet<TableLayoutPanel>();

        // walks every panel this settled and takes the scroll decision again, once sizes are real
        private static void MeasureAgain(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TableLayoutPanel panel && true == settled.Contains(panel))
                    ScrollOnlyWhenNeeded(panel);

                MeasureAgain(child);
            }
        }

        // guards the re-entry: turning AutoScroll on or off resizes the panel, which raises SizeChanged again
        private static readonly HashSet<TableLayoutPanel> measuring = new HashSet<TableLayoutPanel>();

        private static void ScrollOnlyWhenNeeded(TableLayoutPanel panel)
        {
            if (null == panel || false == panel.IsHandleCreated || true == measuring.Contains(panel)) return;

            measuring.Add(panel);

            try
            {
                // Measure against the width left after a scrollbar appears, or a scrollbar narrows the content, wrapped
                // text grows, and the overflow grows with it.
                int narrower = panel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                int wanted = panel.GetPreferredSize(new Size(0 < narrower ? narrower : panel.ClientSize.Width, 0)).Height;
                int have = panel.ClientSize.Height;
                bool overflowing = wanted > have;

                //GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Panel [{panel.Parent?.Name}] wants [{wanted}] has [{have}] rows [{panel.RowCount}] scroll [{overflowing}]");

                // Only a tab panel or the form's own panel scrolls. Scrollbars inside a splitter pane or group box are
                // worse than clipping; make the container taller instead.
                bool mayScroll = panel.Parent is TabPage || panel.Parent is Form;

                if (panel.AutoScroll != (overflowing && mayScroll))
                    panel.AutoScroll = overflowing && mayScroll;

                // Set the scrollable area explicitly; AutoScroll's preferred size leaves empty space to scroll into.
                panel.AutoScrollMinSize = true == (overflowing && mayScroll) ? new Size(0, wanted) : Size.Empty;

                // Nothing recalculates the rows once the panel has its final size; force the layout pass here.
                panel.PerformLayout();
            }
            finally
            {
                measuring.Remove(panel);
            }
        }

        // Controls whose height was set with WithHeight, which keep it rather than growing
        private HashSet<string> ControlsWithASetHeight()
        {
            HashSet<string> retVal = new HashSet<string>();

            foreach (GPALControl entry in FormSettings.myForm.ControlList)
                if (0 < entry.Height && false == string.IsNullOrEmpty(entry.Name))
                    retVal.Add(entry.Name);

            return retVal;
        }

        /// <summary>
        /// Settles the rows, fits the form's height to its content when no height was set, and applies any
        /// explicit top/left settings.
        /// </summary>
        private void SizeForm()
        {
            // apply explicit positioning regardless of whether width/height were set,
            // otherwise StartPosition defaults to WindowsDefaultLocation and Left/Top are ignored
            if (0 < FormSettings.top)
            {
                FormSettings.myForm.StartPosition = FormStartPosition.Manual;
                FormSettings.myForm.Top = FormSettings.top;
            }
            if (0 < FormSettings.left)
            {
                FormSettings.myForm.StartPosition = FormStartPosition.Manual;
                FormSettings.myForm.Left = FormSettings.left;
            }

            HashSet<string> sized = ControlsWithASetHeight();

            foreach (Control child in FormSettings.myForm.Controls)
                if (child is TableLayoutPanel root)
                    SettleRows(root, sized);

            if (0 == FormSettings.height)
                FitHeightToContent();

            // Shown is the first point the panels have their final size, so measure again there.
            FormSettings.myForm.Shown += (sender, args) => MeasureAgain(FormSettings.myForm);
        }

        // Height used to lay out a form with no height of its own, so labels can wrap before the form is measured.
        private const int HeightToLayOutAt = 300;

        /// <summary>
        /// Makes the form as tall as its content, for a form given no height. One layout pass at the form's width
        /// first, so every label that wraps has taken the height its text needs, then the panel's preferred height,
        /// plus the menu, tool and status strips docked above or below it, plus the window's own frame. It never
        /// takes more than the screen's working area; past that the panel scrolls.
        /// </summary>
        private void FitHeightToContent()
        {
            GPALFormForm form = FormSettings.myForm;
            TableLayoutPanel panel = form.Controls.OfType<TableLayoutPanel>().FirstOrDefault();

            if (null != panel)
            {
                if (HeightToLayOutAt > form.Height)
                    form.Height = HeightToLayOutAt;

                // the caller suspended layout and resumes it once this returns, so it is resumed for one pass here
                // and suspended again after
                form.ResumeLayout(true);

                int strips = form.Controls.Cast<Control>()
                    .Where(control => control != panel && (DockStyle.Top == control.Dock || DockStyle.Bottom == control.Dock))
                    .Sum(control => control.Height);
                int content = panel.GetPreferredSize(new Size(panel.ClientSize.Width, 0)).Height;
                int frame = form.Height - form.ClientSize.Height;
                int height = Math.Min(content + strips + frame, Screen.PrimaryScreen.WorkingArea.Height);

                form.InitialSize = new Size(form.Width, height);
                form.Height = height;

                form.SuspendLayout();
            }
        }
        #endregion Helpers
        #region Add Controls
        /// <summary>
        /// Creates the underlying Windows Forms Button for a GPALButton, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALButton">GPAL button definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddButton(GPALButton gPALButton)
        {
            FormSettings.myForm.ControlList.Add(gPALButton);
            Button button = new Button
            {
                Name = gPALButton.Name,
                Text = gPALButton.Text,
                //                AutoSize = true,
                // No Bottom, so the button keeps its own height rather than stretching to fill its row. The
                // four-sided anchor is for the scrolling controls - grid, list, tree, text boxes - which show
                // more content at a greater height
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALButton.Enabled,
                Height = 10 < gPALButton.Height ? gPALButton.Height : 20,
            };
           
            gPALButton.WindowsControl = button;

            ApplyWidth(0 < gPALButton.Width ? gPALButton.Width : FormSettings.buttonWidth, button);

            foreach (var (evt, handler) in gPALButton.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DoubleClick:
                        button.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        button.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(button);

            if (true == gPALButton.IsDefault)
            {
                FormSettings.myForm.AcceptButton = button;
                button.DialogResult = DialogResult.OK;
                button.TabIndex = 0;
            }

            if (!string.IsNullOrWhiteSpace(gPALButton.ToolTipText))
                ApplyToolTip(gPALButton, button);

            return this;
        }
        /// <summary>
        /// Adds a GPALChart's underlying Chart control to the form, sizes it, and wires its drag/drop and click callbacks and tooltip.
        /// </summary>
        /// <param name="gPALChart">GPAL chart definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddChart(GPALChart gPALChart)
        {
            FormSettings.myForm.ControlList.Add(gPALChart);
            // A control with no name has no AutomationId. The chart is already built, so name it here.
            ((Chart)gPALChart.WindowsControl).Name = gPALChart.Name;
            ((Chart)gPALChart.WindowsControl).Enabled = gPALChart.IsEnabled;

            FormSettings.TableLayoutPanel.Controls.Add((Chart)gPALChart.WindowsControl);
            ((Chart)gPALChart.WindowsControl).Height = 10 < gPALChart.Height ? gPALChart.Height : 150;

            Chart chart = gPALChart.WindowsControl;

            ApplyWidth(gPALChart.Width, chart);

            foreach (var (evt, handler) in gPALChart.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DragDrop:
                        chart.DragDrop += (DragEventHandler)handler;
                        break;

                    case ControlEventType.DragEnter:
                        chart.DragEnter += (DragEventHandler)handler;
                        break;

                    case ControlEventType.DragLeave:
                        chart.DragLeave += (EventHandler)handler;
                        break;

                    case ControlEventType.DragOver:
                        chart.DragOver += (DragEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        chart.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        chart.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            if (!string.IsNullOrWhiteSpace(gPALChart.ToolTipText))
                ApplyToolTip(gPALChart, chart);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms CheckBox for a GPALCheckbox, keeps its Checked state synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALCheckbox">GPAL checkbox definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddCheckbox(GPALCheckbox gPALCheckbox)
        {
            FormSettings.myForm.ControlList.Add(gPALCheckbox);
            CheckBox checkBox = new CheckBox
            {
                Name = gPALCheckbox.Name,
                Text = gPALCheckbox.Text,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Checked = gPALCheckbox.Checked,
                Enabled = gPALCheckbox.Enabled,
                Height = 10 < gPALCheckbox.Height ? gPALCheckbox.Height : 20,
            };
            gPALCheckbox.WindowsControl = checkBox;

            ApplyWidth(gPALCheckbox.Width, checkBox);

            // has to be in place for proper presentation, user can also hook into this for their purposes
            checkBox.CheckedChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALCheckbox.Checked = ((CheckBox)sender).Checked; }); // copy changed text up to control

            foreach (var (evt, handler) in gPALCheckbox.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.CheckedChanged:
                        checkBox.CheckedChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        checkBox.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        checkBox.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(checkBox);

            if (!string.IsNullOrWhiteSpace(gPALCheckbox.ToolTipText))
                ApplyToolTip(gPALCheckbox, checkBox);

            return this;
        }
        // The first selectable item, used when there is no fallback item.
        private static int FirstEnabledIndex(ComboBox combo, GPALComboBox gpalCombo)
        {
            int retVal = -1;

            for (int index = 0; index < combo.Items.Count && -1 == retVal; index++)
                if (false == gpalCombo.disabledItems.Contains(combo.Items[index].ToString()))
                    retVal = index;

            return retVal;
        }

        /// <summary>
        /// Creates the underlying Windows Forms ComboBox for a GPALComboBox, populates its items and selection, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gpalCombo">GPAL combo box definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddComboBox(GPALComboBox gpalCombo)
        {
            FormSettings.myForm.ControlList.Add(gpalCombo);

            var combo = new ComboBox
            {
                Name = gpalCombo.Name,
                Enabled = gpalCombo.IsEnabled,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Height = 10 < gpalCombo.Height ? gpalCombo.Height : 20,
            };

            // populate items
            foreach (string item in gpalCombo.items)
            {
                combo.Items.Add(item);
            }

            // restore selection if set — by name takes priority, then by index
            if (!string.IsNullOrEmpty(gpalCombo.selectedItem))
            {
                int idx = combo.Items.IndexOf(gpalCombo.selectedItem);
                if (0 <= idx) combo.SelectedIndex = idx;
            }
            else if (0 <= gpalCombo.selectedIndex && gpalCombo.selectedIndex < combo.Items.Count)
            {
                combo.SelectedIndex = gpalCombo.selectedIndex;
            }

            // A disabled item is drawn grey and cannot be chosen. Owner-draw is the only way to disable one WinForms
            // dropdown entry.
            combo.DrawMode = DrawMode.OwnerDrawFixed;

            combo.DrawItem += (sender, args) =>
            {
                if (0 > args.Index) return;

                string text = combo.Items[args.Index].ToString();
                bool disabled = true == gpalCombo.disabledItems.Contains(text);

                // Only selectable items get the highlight.
                if (false == disabled)
                    args.DrawBackground();
                else
                    using (SolidBrush background = new SolidBrush(SystemColors.Window))
                        args.Graphics.FillRectangle(background, args.Bounds);

                using (SolidBrush foreground = new SolidBrush(disabled ? SystemColors.GrayText : args.ForeColor))
                    args.Graphics.DrawString(text, args.Font, foreground, args.Bounds);

                if (false == disabled) args.DrawFocusRectangle();
            };

            // Restore the last allowed selection. SelectedIndexChanged fires for mouse, keyboard and code.
            combo.SelectedIndexChanged += (sender, args) =>
            {
                if (0 > combo.SelectedIndex) return;

                string text = combo.Items[combo.SelectedIndex].ToString();

                if (false == gpalCombo.disabledItems.Contains(text))
                {
                    gpalCombo.lastEnabledIndex = combo.SelectedIndex;
                    return;
                }

                GPAL.PublishSimpleEvent(Enums.GPALEventType.CAUTION, $"[{text}] does not apply to what is selected, so it cannot be chosen", null, Enums.GPALObjectType.GPALForm);

                combo.SelectedIndex = 0 <= gpalCombo.lastEnabledIndex && gpalCombo.lastEnabledIndex < combo.Items.Count
                    ? gpalCombo.lastEnabledIndex
                    : FirstEnabledIndex(combo, gpalCombo);
            };

            gpalCombo.lastEnabledIndex = combo.SelectedIndex;

            gpalCombo.WindowsControl = combo;

            ApplyWidth(gpalCombo.Width, combo);

            foreach (var (evt, handler) in gpalCombo.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.SelectedIndexChanged:
                        combo.SelectedIndexChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.ValueChanged:
                        combo.SelectedValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        combo.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            // Apply tooltip if set
            if (false == string.IsNullOrEmpty(gpalCombo.ToolTipText))
                ApplyToolTip(gpalCombo, combo);

            FormSettings.TableLayoutPanel.Controls.Add(combo);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms DataGridView for a GPALDataGridView, adds its columns, syncs data from the GPAL grid, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALDataGridView">GPAL data grid view definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddDataGridView(GPALDataGridView gPALDataGridView)
        {
            FormSettings.myForm.ControlList.Add(gPALDataGridView);

            DataGridView dgv = new DataGridView
            {
                Name = gPALDataGridView.Name,
                ReadOnly = gPALDataGridView.ReadOnly,
                AutoGenerateColumns = gPALDataGridView.AutoGenerateColumns,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALDataGridView.IsEnabled,
                AllowUserToAddRows = false,   // usually not wanted in dashboards
                RowHeadersVisible = false,    // optional – cleaner look
                AutoSize = true,
                Height = 10 < gPALDataGridView.Height ? gPALDataGridView.Height : 20,
            };

            // Add explicit columns if supplied
            foreach (string colName in gPALDataGridView.ColumnNames)
            {
                dgv.Columns.Add(colName, colName);
            }

            gPALDataGridView.WindowsControl = dgv;

            ApplyWidth(gPALDataGridView.Width, dgv);

            // Wire callbacks (same pattern as before)
            foreach (var (evt, handler) in gPALDataGridView.Callbacks)
            {
                if (handler == null) continue;

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.CellValueChanged:
                    case ControlEventType.Change:
                        dgv.CellValueChanged += (DataGridViewCellEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        dgv.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        dgv.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            // Populate from grid if already set during fluent chain
            gPALDataGridView.SyncGridToControl();   // ← important line
            gPALDataGridView.ApplyColumnWidths();
            gPALDataGridView.ApplyWrap();

            FormSettings.TableLayoutPanel.Controls.Add(dgv);

            // Recalculate on resize so the last column keeps ending at the control's edge; there is no width before the
            // handle exists.
            if (true == gPALDataGridView.LastColumnFills)
            {
                void fillLastColumn(object s, EventArgs e) => gPALDataGridView.FillLastColumn();

                dgv.Resize += fillLastColumn;
                dgv.HandleCreated += fillLastColumn;

                // Adding or removing rows toggles the vertical scrollbar and changes the last column's width without a
                // resize.
                dgv.RowsAdded += (s, e) => gPALDataGridView.FillLastColumn();
                dgv.RowsRemoved += (s, e) => gPALDataGridView.FillLastColumn();
            }

            if (!string.IsNullOrWhiteSpace(gPALDataGridView.ToolTipText))
                ApplyToolTip(gPALDataGridView, dgv);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms DateTimePicker for a GPALDateTimePicker, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALDateTimePicker">GPAL date/time picker definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddDateTimePicker(GPALDateTimePicker gPALDateTimePicker)
        {
            FormSettings.myForm.ControlList.Add(gPALDateTimePicker);

            DateTimePicker dtp = new DateTimePicker
            {
                Name = gPALDateTimePicker.Name,
                Value = gPALDateTimePicker.Value,
                Format = gPALDateTimePicker.Format,
                CustomFormat = gPALDateTimePicker.CustomFormat,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALDateTimePicker.IsEnabled,
                Height = 10 < gPALDateTimePicker.Height ? gPALDateTimePicker.Height : 10,
            };

            gPALDateTimePicker.WindowsControl = dtp;

            ApplyWidth(gPALDateTimePicker.Width, dtp);

            foreach (var (evt, handler) in gPALDateTimePicker.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.ValueChanged:
                    case ControlEventType.Change:
                        dtp.ValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        dtp.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(dtp);

            if (!string.IsNullOrWhiteSpace(gPALDateTimePicker.ToolTipText))
                ApplyToolTip(gPALDateTimePicker, dtp);

            return this;
        }
        /// <summary>
        /// Builds a Browse button + read-only path textbox pair for a GPALFileSelector, wires the file/folder dialog, callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALFileSelector">GPAL file selector definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddFileSelector(GPALFileSelector gPALFileSelector)
        {
            FormSettings.myForm.ControlList.Add(gPALFileSelector);

            if (gPALFileSelector._compact)
                return AddFileSelectorCompact(gPALFileSelector);

            Button browseButton = new Button
            {
                Name = gPALFileSelector.Name + "_Browse",
                Text = "Browse...",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Width = 80,
                Enabled = gPALFileSelector.IsEnabled
            };

            TextBox displayBox = new TextBox
            {
                Name = gPALFileSelector.Name + "_Path",
                ReadOnly = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                // Show the path from WithSelectedPath.
                Text = true == string.IsNullOrWhiteSpace(gPALFileSelector.SelectedPath)
                     ? "(click Browse to select)"
                     : gPALFileSelector.SelectedPath,
                Enabled = gPALFileSelector.IsEnabled
            };

            TableLayoutPanel container = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Top,          // full width, hugs a single row of content (no tall default panel gap)
                // As in the compact selector below: AutoSize would size the percent column from the path, and a long
                // path adds a horizontal scrollbar.
                Height = Math.Max(displayBox.PreferredHeight, browseButton.Height) + 4,
                Margin = new Padding(0)
            };
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            container.Controls.Add(browseButton, 0, 0);
            container.Controls.Add(displayBox, 1, 0);

            gPALFileSelector.WindowsControl = container;

            // NotifySelection delivers Default, Change and Click when the dialog closes, with FileSelectorEventArgs.
            // Wiring them to the box too would run the handler twice. Only TextChanged belongs to the box.
            foreach (var (evt, handler) in gPALFileSelector.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                if (ControlEventType.TextChanged == evt)
                    displayBox.TextChanged += (EventHandler)handler;
            }


            browseButton.Click += (s, e) =>
            {
                string[] selected = OpenBrowseDialog(gPALFileSelector);
                bool cancelled = selected == null || selected.Length == 0;
                gPALFileSelector.NotifySelection(cancelled, selected);
                displayBox.Text = cancelled ? "(selection cancelled)" : string.Join("; ", selected);
            };

            FormSettings.TableLayoutPanel.Controls.Add(container);

            if (!string.IsNullOrWhiteSpace(gPALFileSelector.ToolTipText))
                ApplyToolTip(gPALFileSelector, container);

            return this;
        }

        private IAllowFormSettingsAndActions AddFileSelectorCompact(GPALFileSelector gPALFileSelector)
        {
            PlaceholderTextBox textBox = new PlaceholderTextBox
            {
                Name            = gPALFileSelector.Name + "_Path",
                Anchor          = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled         = gPALFileSelector.IsEnabled,
                PlaceholderText = gPALFileSelector._placeholder,
                // Set after the placeholder so the path is not treated as placeholder text.
                Text            = gPALFileSelector.SelectedPath
            };

            Button dotButton = new Button
            {
                Name    = gPALFileSelector.Name + "_Browse",
                Text    = "...",
                Width   = 28,
                Anchor  = AnchorStyles.Top | AnchorStyles.Right,
                Enabled = gPALFileSelector.IsEnabled
            };

            TableLayoutPanel container = new TableLayoutPanel
            {
                Name = gPALFileSelector.Name,
                ColumnCount = 2,
                RowCount    = 1,
                Dock        = DockStyle.Top,   // full width, hugs a single row of content (no tall default panel gap)
                // No AutoSize. Docking to the top already takes the form's width, and AutoSize would let a long path
                // add a horizontal scrollbar that resizing never removes.
                Height      = Math.Max(textBox.PreferredHeight, dotButton.Height) + 4,
                Margin      = new Padding(0)
            };
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            container.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            container.Controls.Add(textBox,   0, 0);
            container.Controls.Add(dotButton, 1, 0);

            // WindowsControl points to the text box so SyncDisplay() and .Text access work naturally
            gPALFileSelector.WindowsControl = textBox;

            // NotifySelection delivers Default, Change and Click when the dialog closes, with FileSelectorEventArgs.
            // Wiring them to the box too would run the handler twice. Only TextChanged belongs to the box.
            foreach (var (evt, handler) in gPALFileSelector.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                if (ControlEventType.TextChanged == evt)
                    textBox.TextChanged += (EventHandler)handler;
            }


            textBox.TextChanged += (s, e) => gPALFileSelector._selectedPath = textBox.Text;

            dotButton.Click += (s, e) =>
            {
                string[] selected = OpenBrowseDialog(gPALFileSelector);
                bool cancelled = selected == null || selected.Length == 0;
                if (!cancelled)
                    textBox.Text = selected[0];
                gPALFileSelector.NotifySelection(cancelled, selected);
            };

            FormSettings.TableLayoutPanel.Controls.Add(container);

            if (!string.IsNullOrWhiteSpace(gPALFileSelector.ToolTipText))
                ApplyToolTip(gPALFileSelector, container);

            return this;
        }

        private static string[] OpenBrowseDialog(GPALFileSelector gPALFileSelector)
        {
            if (gPALFileSelector._selectFolder)
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description  = gPALFileSelector._title;
                    fbd.SelectedPath = gPALFileSelector._initialDirectory;
                    return fbd.ShowDialog() == DialogResult.OK ? new[] { fbd.SelectedPath } : null;
                }
            }
            else
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Title           = gPALFileSelector._title;
                    ofd.InitialDirectory = gPALFileSelector._initialDirectory;
                    ofd.Filter          = gPALFileSelector._filter;
                    ofd.Multiselect     = gPALFileSelector._multiSelect;
                    return ofd.ShowDialog() == DialogResult.OK ? ofd.FileNames : null;
                }
            }
        }
        /// <summary>
        /// Creates the underlying placeholder-aware textbox for a GPALInput, applies password masking if requested, keeps its Text synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALInput">GPAL input definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddInput(GPALInput gPALInput)
        {
            FormSettings.myForm.ControlList.Add(gPALInput);
            PlaceholderTextBox input = new PlaceholderTextBox
            {
                Name = gPALInput.Name,
                Text = gPALInput.Text,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALInput.Enabled,
                Height = 10 < gPALInput.Height ? gPALInput.Height : 20,
            };

            if (!string.IsNullOrEmpty(gPALInput.PlaceHolder))
                input.PlaceholderText = gPALInput.PlaceHolder;

            if (true == gPALInput.InputIsPassword)
                input.PasswordChar = '\u25CF';  // bullet

            if (true == gPALInput.IsReadOnly)
                input.ReadOnly = true;

            gPALInput.WindowsControl = input;

            ApplyWidth(gPALInput.Width, input);

            input.Text = gPALInput.Text;   // somehow text is getting truncated to one char from above

            input.TextChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALInput.Text = ((TextBox)sender).Text; }); // copy changed text up to control

            foreach (var (evt, handler) in gPALInput.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.TextChanged:
                        input.TextChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        input.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        input.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.KeyDown:
                        input.KeyDown += (KeyEventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(input);

            if (!string.IsNullOrWhiteSpace(gPALInput.ToolTipText))
                ApplyToolTip(gPALInput, input);

            return this;
        }
        // Below this width the label has not been laid out yet.
        private const int MinimumWidthToMeasure = 60;

        /// <summary>
        /// Sets a label's height to what its text needs at its current width, on every resize.
        /// <br/><br/>
        /// The width is read from the label, not the panel, which has no meaningful width during the build.
        /// <br/><br/>
        /// Set only on change, since setting it resizes the row, which can resize the label again.
        /// </summary>
        /// <param name="label">The label to fit.</param>
        private void FitLabelHeight(Label label)
        {
            // During layout a label is briefly a few pixels wide and one line wraps onto three. Do not fit until the
            // width is real.
            if (MinimumWidthToMeasure < label.Width)
            {
                Size measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

                int wanted = 18 > measured.Height ? 18 : measured.Height;

                if (label.Height != wanted)
                    label.Height = wanted;
            }
        }

        /// <summary>
        /// Creates the empty panel for a GPALSpacer: exactly its height, with no margin, so the gap is the number
        /// asked for. A label will not do, since the form never makes one shorter than a line of text.
        /// </summary>
        /// <param name="gPALSpacer">GPAL spacer to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddSpacer(GPALSpacer gPALSpacer)
        {
            FormSettings.myForm.ControlList.Add(gPALSpacer);
            Panel spacer = new Panel
            {
                Name = gPALSpacer.Name,
                Height = gPALSpacer.Height,
                Margin = Padding.Empty,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };

            gPALSpacer.WindowsControl = spacer;

            FormSettings.TableLayoutPanel.Controls.Add(spacer);

            return this;
        }

        /// <summary>
        /// Creates the Windows Forms LinkLabel for a GPALLinkLabel. Clicking it opens its address, and any callback
        /// given for Click runs as well; it sizes to its text the way a label does.
        /// </summary>
        /// <param name="gPALLinkLabel">GPAL link to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddLinkLabel(GPALLinkLabel gPALLinkLabel)
        {
            FormSettings.myForm.ControlList.Add(gPALLinkLabel);
            LinkLabel link = new LinkLabel
            {
                Name = gPALLinkLabel.Name,
                Enabled = gPALLinkLabel.IsEnabled,
                Text = gPALLinkLabel.Text,
                TextAlign = gPALLinkLabel.TextAlignment,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
            };
            // the size a label uses, and regular rather than bold, so it reads as a link beside the labels
            link.Font = new Font(gPALLinkLabel.FontType.FontFamily, 10f, FontStyle.Regular);
            link.Height = 18 < gPALLinkLabel.Height ? gPALLinkLabel.Height : 18;

            // sized to its text as the width changes, the same as a label, unless a height was asked for
            if (18 >= gPALLinkLabel.Height)
                link.SizeChanged += (sender, args) => FitLabelHeight(link);

            link.LinkClicked += (sender, args) => gPALLinkLabel.Open();

            gPALLinkLabel.WindowsControl = link;

            ApplyWidth(gPALLinkLabel.Width, link);

            foreach (var (evt, handler) in gPALLinkLabel.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DoubleClick:
                        link.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        link.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(link);

            if (!string.IsNullOrWhiteSpace(gPALLinkLabel.ToolTipText))
                ApplyToolTip(gPALLinkLabel, link);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms Label for a GPALLabel, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALLabel">GPAL label definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddLabel(GPALLabel gPALLabel)
        {
            FormSettings.myForm.ControlList.Add(gPALLabel);
            Label label = new Label
            {
                Name = gPALLabel.Name,
                Enabled = gPALLabel.IsEnabled,
                Text = gPALLabel.Text,
                Font = gPALLabel.FontType,
                TextAlign = gPALLabel.TextAlignment,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            label.Font = new Font(label.Font.FontFamily, 10f, FontStyle.Bold);

            label.AutoSize = false;
            label.Height = 18 < gPALLabel.Height ? gPALLabel.Height : 18;

            // A Label clips every wrapped line after the first. With no height set, it takes the height its text needs
            // and recalculates when the width changes.
            if (18 >= gPALLabel.Height)
                label.SizeChanged += (sender, args) => FitLabelHeight(label);

            gPALLabel.WindowsControl = label;

            ApplyWidth(gPALLabel.Width, label);

            foreach (var (evt, handler) in gPALLabel.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.DoubleClick:
                        label.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        label.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(label);

            if (!string.IsNullOrWhiteSpace(gPALLabel.ToolTipText))
                ApplyToolTip(gPALLabel, label);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms ListView for a GPALListView, adds its columns, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALListView">GPAL list view definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddListView(GPALListView gPALListView)
        {
            FormSettings.myForm.ControlList.Add(gPALListView);

            ListView listView = new ListView
            {
                Name = gPALListView.Name,
                View = gPALListView.ViewMode,
                FullRowSelect = gPALListView.FullRowSelect,
                MultiSelect = gPALListView.MultiSelect,
                CheckBoxes = gPALListView.checkBoxes,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALListView.IsEnabled,
                Height = 10 < gPALListView.Height ? gPALListView.Height: 150,
            };

            // Add columns
            foreach (string header in gPALListView.Columns)
            {
                listView.Columns.Add(header, -2, HorizontalAlignment.Left);  // auto-size
            }

            gPALListView.WindowsControl = listView;

            ApplyWidth(gPALListView.Width, listView);
            gPALListView.ApplyColumnWidths();

            // A greyed row cannot be selected. ListView has no cancellable selection event, so the selection is removed
            // as soon as it happens.
            listView.ItemSelectionChanged += (sender, args) =>
            {
                if (true == args.IsSelected && SystemColors.GrayText == args.Item.ForeColor)
                    args.Item.Selected = false;
            };

            foreach (var (evt, handler) in gPALListView.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.SelectedIndexChanged:
                    case ControlEventType.Change:
                        listView.SelectedIndexChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        listView.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        listView.Click += (EventHandler)handler;
                        break;

                    // Checking a row is not selecting it.
                    case ControlEventType.ItemChecked:
                        listView.ItemChecked += (ItemCheckedEventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(listView);

            if (gPALListView.Sortable)
            {
                var comparer = new ListViewItemComparer();
                listView.ListViewItemSorter = comparer;
                listView.ColumnClick += (s, e) =>
                {
                    if (comparer.Column == e.Column)
                        comparer.Order = comparer.Order == SortOrder.Ascending
                            ? SortOrder.Descending : SortOrder.Ascending;
                    else
                    {
                        comparer.Column = e.Column;
                        comparer.Order  = SortOrder.Ascending;
                    }
                    listView.Sort();
                };
            }

            if (gPALListView.ColumnWeights.Count > 0)
            {
                var weights = gPALListView.ColumnWeights;
                double total = 0;
                foreach (var w in weights) total += w;
                if (total <= 0) total = 1;

                void applyWeights(object s, EventArgs e)
                {
                    int available = listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
                    if (available <= 0) return;
                    for (int i = 0; i < weights.Count && i < listView.Columns.Count; i++)
                        listView.Columns[i].Width = (int)(available * weights[i] / total);
                }

                listView.Resize += applyWeights;
                listView.HandleCreated += applyWeights;
            }

            // Calculate all columns together once the control has a handle, and again on resize. A list on an unopened
            // tab has no handle until shown.
            if (true == gPALListView.LastColumnFills)
            {
                void layOutColumns(object s, EventArgs e) => gPALListView.LayOutColumns();

                listView.Resize += layOutColumns;
                listView.HandleCreated += layOutColumns;
            }

            if (!string.IsNullOrWhiteSpace(gPALListView.ToolTipText))
                ApplyToolTip(gPALListView, listView);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms NumericUpDown for a GPALNumericUpDown, applies its range/increment/decimal settings, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALNumericUpDown">GPAL numeric up-down definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddNumericUpDown(GPALNumericUpDown gPALNumericUpDown)
        {
            FormSettings.myForm.ControlList.Add(gPALNumericUpDown);

            NumericUpDown nud = new NumericUpDown
            {
                Name = gPALNumericUpDown.Name,
                Value = gPALNumericUpDown.Value,
                Minimum = gPALNumericUpDown.Minimum,
                Maximum = gPALNumericUpDown.Maximum,
                Increment = gPALNumericUpDown.Increment,
                DecimalPlaces = gPALNumericUpDown.DecimalPlaces,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALNumericUpDown.IsEnabled,
                Height = 10 < gPALNumericUpDown.Height ? gPALNumericUpDown.Height : 20,
            };

            gPALNumericUpDown.WindowsControl = nud;

            ApplyWidth(gPALNumericUpDown.Width, nud);

            foreach (var (evt, handler) in gPALNumericUpDown.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.ValueChanged:
                    case ControlEventType.Change:
                        nud.ValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        nud.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(nud);

            if (!string.IsNullOrWhiteSpace(gPALNumericUpDown.ToolTipText))
                ApplyToolTip(gPALNumericUpDown, nud);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms PictureBox for a GPALPicture, puts its image and size mode on, wires its click callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALPicture">GPAL picture definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddPicture(GPALPicture gPALPicture)
        {
            FormSettings.myForm.ControlList.Add(gPALPicture);

            PictureBox pictureBox = new PictureBox
            {
                Name = gPALPicture.Name,
                Enabled = gPALPicture.IsEnabled,
                Image = gPALPicture.Image,
                SizeMode = gPALPicture.SizeMode,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Height = 10 < gPALPicture.Height ? gPALPicture.Height : 150,
            };

            gPALPicture.WindowsControl = pictureBox;

            ApplyWidth(gPALPicture.Width, pictureBox);

            foreach (var (evt, handler) in gPALPicture.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        pictureBox.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        pictureBox.DoubleClick += (EventHandler)handler;
                        break;

                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(pictureBox);

            if (!string.IsNullOrWhiteSpace(gPALPicture.ToolTipText))
                ApplyToolTip(gPALPicture, pictureBox);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms TrackBar for a GPALSlider, applies its range, value, ticks and
        /// orientation, wires its callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALSlider">GPAL slider definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddSlider(GPALSlider gPALSlider)
        {
            FormSettings.myForm.ControlList.Add(gPALSlider);

            TrackBar slider = new TrackBar
            {
                Name = gPALSlider.Name,
                Minimum = gPALSlider.Minimum,
                Maximum = gPALSlider.Maximum,
                TickFrequency = 1 > gPALSlider.TickFrequency ? 1 : gPALSlider.TickFrequency,
                Orientation = gPALSlider.Orientation,
                Value = gPALSlider.Value,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALSlider.IsEnabled,
                Height = 10 < gPALSlider.Height ? gPALSlider.Height : 45,
            };

            gPALSlider.WindowsControl = slider;

            ApplyWidth(gPALSlider.Width, slider);

            foreach (var (evt, handler) in gPALSlider.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.ValueChanged:
                    case ControlEventType.Change:
                        slider.ValueChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Scroll:
                        slider.Scroll += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        slider.Click += (EventHandler)handler;
                        break;

                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(slider);

            if (!string.IsNullOrWhiteSpace(gPALSlider.ToolTipText))
                ApplyToolTip(gPALSlider, slider);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms ProgressBar for a GPALProgressBar, applies its range/value/style, wires its click callback and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALProgressBar">GPAL progress bar definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddProgressBar(GPALProgressBar gPALProgressBar)
        {
            FormSettings.myForm.ControlList.Add(gPALProgressBar);

            ProgressBar progressBar = new ProgressBar
            {
                Name = gPALProgressBar.Name,
                Minimum = gPALProgressBar.Minimum,
                Maximum = gPALProgressBar.Maximum,
                Value = gPALProgressBar.Value,
                Style = gPALProgressBar.Style,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Enabled = gPALProgressBar.IsEnabled,
                Height = 10 < gPALProgressBar.Height ? gPALProgressBar.Height : 20,
            };

            gPALProgressBar.WindowsControl = progressBar;

            ApplyWidth(gPALProgressBar.Width, progressBar);

            foreach (var (evt, handler) in gPALProgressBar.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.Click:
                        progressBar.Click += (EventHandler)handler;
                        break;

                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(progressBar);

            if (!string.IsNullOrWhiteSpace(gPALProgressBar.ToolTipText))
                ApplyToolTip(gPALProgressBar, progressBar);

            return this;
        }

        /// <summary>
        /// Creates the underlying Windows Forms RadioButton for a GPALRadioButton, keeps its Checked state synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALRadioButton">GPAL radio button definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddRadioButton(GPALRadioButton gPALRadioButton)
        {
            FormSettings.myForm.ControlList.Add(gPALRadioButton);
            RadioButton radioButton = new RadioButton
            {
                Name = gPALRadioButton.Name,
                Text = gPALRadioButton.Text,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Checked = gPALRadioButton.Checked,
                Enabled = gPALRadioButton.Enabled,
                Height = 10 < gPALRadioButton.Height ? gPALRadioButton.Height : 20,
            };
            gPALRadioButton.WindowsControl = radioButton;

            ApplyWidth(gPALRadioButton.Width, radioButton);
            radioButton.CheckedChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALRadioButton.Checked = ((RadioButton)sender).Checked; }); // copy changed text up to control

            foreach (var (evt, handler) in gPALRadioButton.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.CheckedChanged:
                        radioButton.CheckedChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        radioButton.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(radioButton);

            // One radio button per container is checked. WinForms unchecks the others only when one is checked after
            // it is in place, so two checked before the form was built would both show checked. The last one added
            // stays checked.
            if (true == radioButton.Checked)
                foreach (RadioButton other in radioButton.Parent.Controls.OfType<RadioButton>().ToList())
                    if (other != radioButton && true == other.Checked)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Radio buttons [{other.Name}] and [{radioButton.Name}] were both checked in the same container, so only [{radioButton.Name}] is", gPALRadioButton, GPALObjectType.Other);

                        other.Checked = false;
                    }

            if (!string.IsNullOrWhiteSpace(gPALRadioButton.ToolTipText))
                ApplyToolTip(gPALRadioButton, radioButton);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms StatusStrip and label for a GPALStatusStrip, wires its click callback and tooltip, and docks it to the bottom of the form.
        /// </summary>
        /// <param name="gPALStatusStrip">GPAL status strip definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddStatusStrip(GPALStatusStrip gPALStatusStrip)
        {
            FormSettings.myForm.ControlList.Add(gPALStatusStrip);

            StatusStrip statusStrip = new StatusStrip
            {
                Name = gPALStatusStrip.Name,
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                Enabled = gPALStatusStrip.IsEnabled,
                Height = 10 < gPALStatusStrip.Height ? gPALStatusStrip.Height : 20,
            };

            ToolStripStatusLabel statusLabel = new ToolStripStatusLabel
            {
                Text = gPALStatusStrip.Text,
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Height = 10 < gPALStatusStrip.Height ? gPALStatusStrip.Height : 20,
            };

            statusStrip.Items.Add(statusLabel);

            gPALStatusStrip.WindowsControl = statusStrip;

            foreach (var (evt, handler) in gPALStatusStrip.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Click:
                    case ControlEventType.Default:
                        statusStrip.ItemClicked += (sender, e) => ((EventHandler)handler)(sender, e);
                        break;

                        // ignore unsupported
                }
            }

            // StatusStrip is added directly to the form (not TableLayoutPanel)
            FormSettings.myForm.Controls.Add(statusStrip);

            if (!string.IsNullOrWhiteSpace(gPALStatusStrip.ToolTipText))
                ApplyToolTip(gPALStatusStrip, statusStrip);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms MenuStrip for a GPALMenuBar, wires item callbacks, and docks it to the top of the form.
        /// </summary>
        /// <param name="gPALMenuBar">GPAL menu bar definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddMenuBar(GPALMenuBar gPALMenuBar)
        {
            FormSettings.myForm.ControlList.Add(gPALMenuBar);

            MenuStrip menuStrip = new MenuStrip
            {
                Name = gPALMenuBar.Name,
                Enabled = gPALMenuBar.IsEnabled,
            };

            var topMenus = new System.Collections.Generic.Dictionary<string, ToolStripMenuItem>();
            foreach (var (topMenu, item, isSeparator) in gPALMenuBar.menuEntries)
            {
                string menuKey = string.IsNullOrWhiteSpace(topMenu) ? "" : topMenu;
                if (!topMenus.TryGetValue(menuKey, out ToolStripMenuItem topItem))
                {
                    topItem = new ToolStripMenuItem(menuKey);
                    topMenus[menuKey] = topItem;
                    menuStrip.Items.Add(topItem);
                }

                if (isSeparator)
                {
                    topItem.DropDownItems.Add(new ToolStripSeparator());
                    continue;
                }

                ToolStripMenuItem menuItem = new ToolStripMenuItem(item.Text);
                if (item.Image != null)
                    menuItem.Image = item.Image;
                if (item.ShortCutKey != System.Windows.Forms.Keys.None)
                {
                    menuItem.ShortcutKeys = item.ShortCutKey;
                    menuItem.ShowShortcutKeys = true;
                }
                // Wire each handler to its ForEvent event; Click is the default.
                foreach (var (evt, handler) in item.Callbacks)
                {
                    if (null == handler) continue; // skip invalid

                    switch (evt)
                    {
                        case ControlEventType.Default:
                        case ControlEventType.Click:
                            menuItem.Click += (EventHandler)handler;
                            break;

                        case ControlEventType.DoubleClick:
                            menuItem.DoubleClick += (EventHandler)handler;
                            break;
                            // ignore unsupported
                    }
                }
                if (!string.IsNullOrWhiteSpace(item.ToolTipText))
                    menuItem.ToolTipText = item.ToolTipText;

                // Apply the WithEnabled value set before the item existed.
                menuItem.Enabled = item.IsEnabled;
                item.WindowsControl = menuItem;
                topItem.DropDownItems.Add(menuItem);
            }

            gPALMenuBar.WindowsControl = menuStrip;

            // MenuStrip docks to the top of the form (not the TableLayoutPanel)
            FormSettings.myForm.Controls.Add(menuStrip);
            FormSettings.myForm.MainMenuStrip = menuStrip;

            // A top-docked MenuStrip must sit above any top-docked toolbar. Docked siblings fill from the
            // edge in reverse child-index order, so pushing the menu to the highest index keeps it outermost.
            FormSettings.myForm.Controls.SetChildIndex(menuStrip, FormSettings.myForm.Controls.Count - 1);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms ToolStrip for a GPALToolbar, wires each button's callback,
        /// image and tooltip, and docks it to the top of the form (below the menu bar).
        /// </summary>
        /// <param name="gPALToolbar">GPAL toolbar definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddToolbar(GPALToolbar gPALToolbar)
        {
            FormSettings.myForm.ControlList.Add(gPALToolbar);

            ToolStrip toolStrip = new ToolStrip
            {
                Name = gPALToolbar.Name,
                Enabled = gPALToolbar.IsEnabled,
            };

            foreach (var (item, isSeparator) in gPALToolbar.toolbarEntries)
            {
                if (isSeparator)
                {
                    toolStrip.Items.Add(new ToolStripSeparator());
                    continue;
                }

                ToolStripButton button = new ToolStripButton(item.Text);
                if (item.Image != null)
                {
                    button.Image = item.Image;
                    button.DisplayStyle = string.IsNullOrWhiteSpace(item.Text)
                        ? ToolStripItemDisplayStyle.Image
                        : ToolStripItemDisplayStyle.ImageAndText;
                }
                else
                    button.DisplayStyle = ToolStripItemDisplayStyle.Text;

                // As with the menu bar, use the requested event.
                foreach (var (evt, handler) in item.Callbacks)
                {
                    if (null == handler) continue; // skip invalid

                    switch (evt)
                    {
                        case ControlEventType.Default:
                        case ControlEventType.Click:
                            button.Click += (EventHandler)handler;
                            break;

                        case ControlEventType.DoubleClick:
                            button.DoubleClick += (EventHandler)handler;
                            break;
                            // ignore unsupported
                    }
                }
                if (!string.IsNullOrWhiteSpace(item.ToolTipText))
                    button.ToolTipText = item.ToolTipText;

                button.Enabled = item.IsEnabled;
                item.WindowsControl = button;
                toolStrip.Items.Add(button);
            }

            gPALToolbar.WindowsControl = toolStrip;

            // ToolStrip docks to the top of the form (not the TableLayoutPanel), below any MenuStrip.
            FormSettings.myForm.Controls.Add(toolStrip);
            if (FormSettings.myForm.MainMenuStrip != null)
                FormSettings.myForm.Controls.SetChildIndex(FormSettings.myForm.MainMenuStrip, FormSettings.myForm.Controls.Count - 1);

            return this;
        }
        /// <summary>
        /// Adds a new tab page to the form's TabControl (creating the TabControl on first use), and switches the active TableLayoutPanel to the new tab's panel so subsequent controls are added to it.
        /// </summary>
        /// <param name="gPALTab">GPAL tab definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTab(GPALTab gPALTab)
        {
            if (null == FormSettings.TabControl)
            {
                FormSettings.TabControl = new TabControl
                {
                    Name = "tabControl",
                    AutoSize = false,
                    //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                    Dock = DockStyle.Fill,
                    Multiline = true,

                };

                // A disabled TabPage greys its contents but can still be selected, so disable a tab by refusing the
                // switch.
                FormSettings.TabControl.Selecting += (sender, args) =>
                {
                    // Nothing selected yet means pages are still being added; do not refuse.
                    if (null == FormSettings.TabControl.SelectedTab) return;

                    if (null != args.TabPage && false == args.TabPage.Enabled) args.Cancel = true;
                };

                FormSettings.TableLayoutPanel.Controls.Add(FormSettings.TabControl);
            }

            TabPage tabPage = new TabPage
            {
                Name = gPALTab.Name,
                Text = gPALTab.Text,
                AutoSize = false,
                //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Dock = DockStyle.Fill,
                Enabled = gPALTab.Enabled
            };

            TableLayoutPanel tmpTableLayoutPanel = new TableLayoutPanel
            {
                //Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Dock = DockStyle.Fill,
                AutoSize = false,

                // As on the form's own panel: without this, an overfull tab hides its bottom controls with no
                // scrollbar.
                AutoScroll = true
            };

            gPALTab.WindowsControl = tabPage;

            FormSettings.myForm.ControlList.Add(gPALTab);

            foreach (var (evt, handler) in gPALTab.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Scroll:
                        tabPage.Scroll += (ScrollEventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        // The page's own Click is a click on its blank area, so it must be requested by name.
                        tabPage.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.Default:
                    case ControlEventType.SelectedIndexChanged:
                    case ControlEventType.Change:
                        // A tab's default event is becoming the visible tab. Only the TabControl raises that, naming
                        // the page, so each tab gets only its own.
                        FormSettings.TabControl.Selected += (sender, args) =>
                        {
                            if (args.TabPage == tabPage)
                                ((EventHandler)handler)(gPALTab, args);
                        };
                        break;
                        // ignore unsupported
                }
            }

            tabPage.Controls.Add(tmpTableLayoutPanel);
            FormSettings.TabControl.Controls.Add(tabPage);
            FormSettings.TableLayoutPanel = tmpTableLayoutPanel;

            if (!string.IsNullOrWhiteSpace(gPALTab.ToolTipText))
                ApplyToolTip(gPALTab, tabPage);

            return this;
        }
        /// <summary>
        /// Builds a GPALSplitter: a SplitContainer whose two panes each have a layout panel, filled with the controls
        /// before and after the pane marker.<br/>
        /// A pane can hold another splitter or a group box.
        /// </summary>
        /// <param name="gPALSplitter">Splitter definition to build.</param>

        private IAllowFormSettingsAndActions AddSplitter(GPALSplitter gPALSplitter)
        {
            FormSettings.myForm.ControlList.Add(gPALSplitter);

            SplitContainer sc = new SplitContainer
            {
                Name = gPALSplitter.Name,
                Enabled = gPALSplitter.IsEnabled,
                // a vertical bar gives left and right panes, a horizontal one top and bottom
                Orientation = true == gPALSplitter.IsHorizontalSplit ? Orientation.Vertical : Orientation.Horizontal,
                // Anchor instead of docking, like every other GPAL control: a docked SplitContainer in a
                // TableLayoutPanel cell loses its height. Its height is measured below once the panes are filled,
                // unless .WithHeight set it. Without its own height, its row takes the form's spare height, and
                // anchoring the bottom fills it, as a list view does.
                Anchor = 10 < gPALSplitter.Height
                    ? AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                    : AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Height = 10 < gPALSplitter.Height ? gPALSplitter.Height : 0,
                AutoScaleMode = AutoScaleMode.Inherit,
                // A set height means a fixed band rather than a container that takes the remaining space.
                Tag = 10 < gPALSplitter.Height ? SetHeightMarker : null
            };

            if (true == gPALSplitter.IsPercentage)
            {
                double pct = gPALSplitter.Percentage;

                // a proportion has to be reapplied as the form resizes, where a pixel distance stays put
                sc.SizeChanged += (s, e) =>
                {
                    int along = true == gPALSplitter.IsHorizontalSplit ? sc.Width : sc.Height;

                    if (along > sc.Panel1MinSize + sc.Panel2MinSize + sc.SplitterWidth)
                        sc.SplitterDistance = (int)(along * pct);
                };
            }
            else
                sc.SplitterDistance = gPALSplitter.Dimension;

            gPALSplitter.WindowsControl = sc;

            // The divider position is the default event when none is named.
            foreach (var (evt, handler) in gPALSplitter.Callbacks)
            {
                if (null == handler) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.SplitterMoved:
                        sc.SplitterMoved += (SplitterEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        sc.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        sc.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            TableLayoutPanel firstPane = MakeSplitPaneTlp();
            TableLayoutPanel secondPane = MakeSplitPaneTlp();

            sc.Panel1.Controls.Add(firstPane);
            sc.Panel2.Controls.Add(secondPane);
            FormSettings.TableLayoutPanel.Controls.Add(sc);

            FillContainer(gPALSplitter.FormControls, firstPane, secondPane, gPALSplitter.Name);


            // A SplitContainer cannot size to its contents, so measure it: side-by-side panes are as tall as the taller
            // one; stacked panes are both plus the bar. .WithHeight overrides this.
            if (10 >= gPALSplitter.Height)
            {
                // A pane's PreferredSize returns its current height; add up its controls' heights instead.
                Func<TableLayoutPanel, int> contentHeight = pane =>
                {
                    int total = pane.Padding.Top + pane.Padding.Bottom;

                    foreach (Control child in pane.Controls)
                        total += child.Height + child.Margin.Bottom;

                    return total;
                };


                sc.Height = true == gPALSplitter.IsHorizontalSplit
                    ? Math.Max(contentHeight(firstPane), contentHeight(secondPane))
                    : contentHeight(firstPane) + contentHeight(secondPane) + sc.SplitterWidth;
            }

            if (!string.IsNullOrWhiteSpace(gPALSplitter.ToolTipText))
                ApplyToolTip(gPALSplitter, sc);

            return this;
        }

        // what a GroupBox pads by on its own, kept here because the bottom is adjusted from it
        private const int GroupBoxPadding = 3;

        // how much further the bottom is padded so the last control's border clears the box's border
        private const int GroupBoxBottomClearance = 2;

        /// <summary>
        /// Realizes a GPALGroupBox: a titled box with its own layout panel holding everything that was added to it.
        /// </summary>
        /// <param name="gPALGroupBox">Group box definition to realize.</param>
        private IAllowFormSettingsAndActions AddGroupBox(GPALGroupBox gPALGroupBox)
        {
            FormSettings.myForm.ControlList.Add(gPALGroupBox);

            GroupBox gb = new GroupBox
            {
                Name = gPALGroupBox.Name,
                Text = gPALGroupBox.Text,
                Enabled = gPALGroupBox.IsEnabled,
                // Full width, only as tall as its contents. Docking to fill stops the box growing. NOTE: DockStyle is
                // not a flags enum, so Top | Bottom is Left.
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                // The inner panel measures short and the last control's bottom border falls under the box's border;
                // this extra padding brings it back.
                Padding = new Padding(GroupBoxPadding, GroupBoxPadding, GroupBoxPadding, GroupBoxPadding + GroupBoxBottomClearance),
            };

            // The inner panel must also size to its contents. A splitter pane fills instead, so this is not
            // MakeSplitPaneTlp.
            TableLayoutPanel inside = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                //AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
            };

            inside.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            gb.Controls.Add(inside);
            FormSettings.TableLayoutPanel.Controls.Add(gb);

            gPALGroupBox.WindowsControl = gb;

            FillContainer(gPALGroupBox.FormControls, inside, null, gPALGroupBox.Name);

            if (!string.IsNullOrWhiteSpace(gPALGroupBox.ToolTipText))
                ApplyToolTip(gPALGroupBox, gb);

            return this;
        }

        /// <summary>
        /// Adds a container's controls to its panel, switching to <paramref name="secondPane"/> at the pane marker.<br/>
        /// Each control is built by the same WithFormControl dispatch as a top-level control, so a container can hold
        /// anything a form can.
        /// </summary>
        /// <param name="formControls">What was added to the container, in order, marker included.</param>
        /// <param name="firstPane">Panel everything before the marker goes into.</param>
        /// <param name="secondPane">Panel everything after it goes into, null for a container with one pane.</param>
        /// <param name="name">Container name, for anything worth reporting.</param>
        private void FillContainer(List<object> formControls, TableLayoutPanel firstPane, TableLayoutPanel secondPane, string name)
        {
            TableLayoutPanel outerPanel = FormSettings.TableLayoutPanel;
            TabControl outerTabControl = FormSettings.TabControl;
            TableLayoutPanel current = firstPane;
            bool markerSeen = false;

            FormSettings.TabControl = null;

            foreach (object formControl in formControls)
            {
                if (formControl is GPALSpliter)
                {
                    if (null == secondPane)
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{name}] has no second pane, so the pane marker does nothing. Everything added stays in the one panel.");
                    else if (true == markerSeen)
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{name}] has more than one pane marker and a splitter has only two panes, so everything after the first marker stays in the second.");
                    else
                    {
                        current = secondPane;
                        markerSeen = true;
                    }

                    continue;
                }

                FormSettings.TableLayoutPanel = current;
                FormSettings.TabControl = null;
                WithFormControl((GPALControl)formControl);
            }

            FormSettings.TableLayoutPanel = outerPanel;
            FormSettings.TabControl = outerTabControl;
        }

        private TableLayoutPanel MakeSplitPaneTlp()
        {
            var tlp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ColumnCount = 1,
            };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            return tlp;
        }

        /// <summary>
        /// Creates the underlying Windows Forms TableLayoutPanel for a GPALTableLayoutPanel and applies its tooltip.
        /// </summary>
        /// <param name="gPALTableLayoutPanel">GPAL table layout panel definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTableLayoutPanel(GPALTableLayoutPanel gPALTableLayoutPanel)
        {
            FormSettings.myForm.ControlList.Add(gPALTableLayoutPanel);
            TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
            {
                Name = gPALTableLayoutPanel.Name,
                GrowStyle = gPALTableLayoutPanel.GrowStyle,
                AutoScroll = false,
                ColumnCount = gPALTableLayoutPanel.ColumnCount,
                RowCount = gPALTableLayoutPanel.RowCount,
                Enabled = gPALTableLayoutPanel.IsEnabled,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
                // todo : more settings
            };

            // Set WindowsControl, as every other Add does; otherwise the panel is dropped and later reads take the
            // not-yet-built path.
            gPALTableLayoutPanel.WindowsControl = tableLayoutPanel;

            FormSettings.TableLayoutPanel.Controls.Add(tableLayoutPanel);

            if (!string.IsNullOrWhiteSpace(gPALTableLayoutPanel.ToolTipText))
                ApplyToolTip(gPALTableLayoutPanel, tableLayoutPanel);

            return this;
        }

        /// <summary>
        /// Creates the underlying multi-line, word-wrapped textbox for a GPALTextArea, keeps its Text synced, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALTextArea">GPAL text area definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTextArea(GPALTextArea gPALTextArea)
        {
            FormSettings.myForm.ControlList.Add(gPALTextArea);
            PlaceholderTextBox textBox = new PlaceholderTextBox
            {
                Name = gPALTextArea.Name,
                Text = gPALTextArea.Text,
                Multiline = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Both,
                Height = 10 < gPALTextArea.Height ? gPALTextArea.Height : 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALTextArea.Enabled
            };
            if (true == gPALTextArea.IsReadOnly)
                textBox.ReadOnly = true;

            if (!string.IsNullOrEmpty(gPALTextArea.PlaceHolder))
                textBox.PlaceholderText = gPALTextArea.PlaceHolder;

            gPALTextArea.WindowsControl = textBox;

            ApplyWidth(gPALTextArea.Width, textBox);
            textBox.Text = gPALTextArea.Text;   // somehow text is getting truncated to one char from above

            textBox.TextChanged += new EventHandler(delegate (Object sender, EventArgs a) { gPALTextArea.Text = ((TextBox)sender).Text; }); // copy changed text up to control

            foreach (var (evt, handler) in gPALTextArea.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.TextChanged:
                        textBox.TextChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        textBox.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(textBox);

            if (!string.IsNullOrWhiteSpace(gPALTextArea.ToolTipText))
                ApplyToolTip(gPALTextArea, textBox);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms RichTextBox for a GPALRichTextBox, renders its initial Markdown,
        /// wires its tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALRichTextBox">GPAL rich text box definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddRichTextBox(GPALRichTextBox gPALRichTextBox)
        {
            FormSettings.myForm.ControlList.Add(gPALRichTextBox);
            RichTextBox richTextBox = new RichTextBox
            {
                Name = gPALRichTextBox.Name,
                Multiline = true,
                WordWrap = true,
                ReadOnly = gPALRichTextBox.IsReadOnly,
                ScrollBars = RichTextBoxScrollBars.Both,
                Height = 10 < gPALRichTextBox.Height ? gPALRichTextBox.Height : 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALRichTextBox.Enabled
            };
            gPALRichTextBox.WindowsControl = richTextBox;

            ApplyWidth(gPALRichTextBox.Width, richTextBox);
            GPALRichTextBox.RenderMarkdown(richTextBox, gPALRichTextBox.Text);

            // Do not copy the control's text back; it shows rendered Markdown, while Text holds the source.
            foreach (var (evt, handler) in gPALRichTextBox.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.TextChanged:
                        richTextBox.TextChanged += (EventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        richTextBox.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        richTextBox.Click += (EventHandler)handler;
                        break;

                    case ControlEventType.KeyDown:
                        richTextBox.KeyDown += (KeyEventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(richTextBox);

            if (!string.IsNullOrWhiteSpace(gPALRichTextBox.ToolTipText))
                ApplyToolTip(gPALRichTextBox, richTextBox);

            return this;
        }
        /// <summary>
        /// Creates the underlying Windows Forms TreeView for a GPALTreeView, applies its checkbox/line/plus-minus settings, wires callbacks and tooltip, and adds it to the form.
        /// </summary>
        /// <param name="gPALTreeView">GPAL tree view definition to realize.</param>
        /// <returns>Fluent interface for defining your GPAL form.</returns>
        private IAllowFormSettingsAndActions AddTreeView(GPALTreeView gPALTreeView)
        {
            FormSettings.myForm.ControlList.Add(gPALTreeView);

            TreeView tv = new TreeView
            {
                Name = gPALTreeView.Name,
                CheckBoxes = gPALTreeView.CheckBoxes,
                ShowLines = gPALTreeView.ShowLines,
                ShowPlusMinus = gPALTreeView.ShowPlusMinus,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right,
                Enabled = gPALTreeView.IsEnabled,
                Height = 10 < gPALTreeView.Height ? gPALTreeView.Height : 150,
            };

            // BeforeSelect can refuse outright, so a greyed node never becomes the selected one
            tv.BeforeSelect += (sender, args) =>
            {
                if (null != args.Node && SystemColors.GrayText == args.Node.ForeColor) args.Cancel = true;
            };

            gPALTreeView.WindowsControl = tv;

            ApplyWidth(gPALTreeView.Width, tv);

            foreach (var (evt, handler) in gPALTreeView.Callbacks)
            {
                if (handler == null) continue; // skip invalid

                switch (evt)
                {
                    case ControlEventType.Default:
                    case ControlEventType.AfterSelect:
                    case ControlEventType.Change:
                        tv.AfterSelect += (TreeViewEventHandler)handler;
                        break;

                    case ControlEventType.DoubleClick:
                        tv.DoubleClick += (EventHandler)handler;
                        break;

                    case ControlEventType.Click:
                        tv.Click += (EventHandler)handler;
                        break;
                        // ignore unsupported
                }
            }

            FormSettings.TableLayoutPanel.Controls.Add(tv);

            if (!string.IsNullOrWhiteSpace(gPALTreeView.ToolTipText))
                ApplyToolTip(gPALTreeView, tv);

            return this;
        }
        #endregion <private>
    }

    internal class ListViewItemComparer : System.Collections.IComparer
    {
        public int       Column { get; set; } = 0;
        public SortOrder Order  { get; set; } = SortOrder.Ascending;

        public int Compare(object x, object y)
        {
            var lx = (ListViewItem)x;
            var ly = (ListViewItem)y;

            string sx = Column == 0 ? lx.Text
                : Column < lx.SubItems.Count ? lx.SubItems[Column].Text : string.Empty;
            string sy = Column == 0 ? ly.Text
                : Column < ly.SubItems.Count ? ly.SubItems[Column].Text : string.Empty;

            int result;

            // Numeric (strip currency/percent symbols and commas)
            string nx = sx.Replace(",", "").Replace("$", "").Replace("%", "").Trim();
            string ny = sy.Replace(",", "").Replace("$", "").Replace("%", "").Trim();
            if (double.TryParse(nx, out double dx) && double.TryParse(ny, out double dy))
                result = dx.CompareTo(dy);
            // Date
            else if (System.DateTime.TryParse(sx, out System.DateTime dtx) &&
                     System.DateTime.TryParse(sy, out System.DateTime dty))
                result = dtx.CompareTo(dty);
            // String
            else
                result = string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);

            return Order == SortOrder.Ascending ? result : -result;
        }
    }
}

