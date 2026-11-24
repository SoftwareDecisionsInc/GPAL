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
    }
}
