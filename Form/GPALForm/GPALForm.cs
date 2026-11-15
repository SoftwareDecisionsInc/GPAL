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
    public partial class GPALForm : IAllowFormSettingsAndActions, IAllowFormActionsAndFillIn, IAllowCallBack
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
    }
}
