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

using DocumentFormat.OpenXml.Office.CustomUI;
using GenerallyPositive.Application;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using static GenerallyPositive.Browser.Browser;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPALForm.GPALForm;

namespace GenerallyPositive
{
    public partial class GPAL
    {
		[Component(Description = "An Excel workbook for the given file", Submenu = "Data")]
		public static IGPALExcel ExcelFor(GPALFile excelFile)
		{
			return new GPALExcel().WithFile(excelFile).ToGPALObject();
		}

		[Component(Description = "Credentials for the given service", Submenu = "Security")]
        public static ICredentials CredentialsFor(CredentialServiceType credentialServiceType)
        {
            return new Credentials().WithService(credentialServiceType).ToGPALObject();
        }
        [Component(Description = "A Google Sheets spreadsheet, opened by name or id", Submenu = "Google")]
        public static IGoogleSheets GoogleSheetsFor(string spreadsheetName)
        {
            return new GoogleSheets().WithSpreadsheet(spreadsheetName).ToGPALObject();
        }
        [Component(Description = "Google Drive, signed in with the given credentials", Submenu = "Google")]
        public static IGoogleDrive GoogleDriveFor(ICredentials credentials)
        {
            return new GoogleDrive().WithCredentials(credentials).ToGPALObject();
        }
        [Component(Description = "A grid whose rows are objects of the given class", Submenu = "Data")]
        public static IGPALGrid<T> GridForType<T>()
        {
            return new GPALGrid<T>();
        }
        /// <summary>
        /// Helper for shorthand notation
        /// </summary>
        /// <code>
        /// <example>
        /// GPAL.Browser
        ///     .WithBrowserType(BrowserType.Chrome)
        ///     .Get("ebay.com")
        ///     .LeftClick(GPAL.CssSelector("#gh-search-btn"))
        ///     .Close();
        /// </example>
        /// </code>
        /// <param name="css"></param>
        /// <param name="selectorName">[Optional] Name given to the selector</param>
        /// <returns></returns>
        [Component(Description = "A selector that finds an element by CSS, optionally named", Submenu = "Selectors")]
        public static Selector CssSelector(string css, string selectorName = null)
        {
            if (null != selectorName)
                return Selector.WithCSS(css).WithSelectorName(selectorName).ToGPALObject();
            else
                return Selector.WithCSS(css).ToGPALObject();
        }

        [Component(Description = "A selector that finds an element by XPath, optionally named", Submenu = "Selectors")]
        public static Selector XPathSelector(string xpath, string selectorName = null)
        {
            if (null != selectorName)
                return Selector.WithXPath(xpath).WithSelectorName(selectorName).ToGPALObject();
            else
                return Selector.WithXPath(xpath).ToGPALObject();
        }
        [Component(Description = "A GPALFile for the given path", Submenu = "Data")]
        public static GPALFile FileFor([FilePath] string path)
        {
            return File.WithFileName(path).ToGPALObject();
        }
        // Browser shorthand methods
        [Component(Description = "Start a browser with no window on the given url", Submenu = "Browser")]
        public static IBrowser BrowserGet(GPALUrl url)
        {
            return Browser
                .Get(url) // Headless navigation
                .ToGPALObject();

        }
        [Component(Description = "Start a browser window on the given url", Submenu = "Browser")]
        public static IBrowser BrowserGoto(GPALUrl url)
        {
            return Browser
                .GoTo(url) // Windowed navigation
                .ToGPALObject();
        }
        [Component(Description = "A button with the given text, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALButton ButtonFor(string text)
        {
            var c = new GPALButton { Name = buttonName + controlCount++.ToString() };
            c.IsDefault = false;
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A label with the given text, for a GPAL form", Submenu = "Form Controls/Text")]
        public static GPALLabel LabelFor(string text)
        {
            var c = new GPALLabel { Name = labelName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "Empty space this many pixels tall between two controls on a GPAL form", Submenu = "Form Controls/Layout")]
        public static GPALSpacer SpacerFor([Pixels] int pixels)
        {
            var c = new GPALSpacer { Name = spacerName + controlCount++.ToString() };
            c.Height = pixels;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A link for a GPAL form that opens this address in the default browser, showing the address as its text", Submenu = "Form Controls/Text")]
        public static GPALLinkLabel LinkLabelFor([URL] string url)
        {
            var c = new GPALLinkLabel { Name = linkLabelName + controlCount++.ToString() };
            c.Url = url;
            c.Text = url;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A one-line input box for a GPAL form, optionally with starting text", Submenu = "Form Controls/Text")]
        public static GPALInput InputFor(string text = "")
        {
            var c = new GPALInput { Name = inputName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A multi-line text box for a GPAL form, optionally with starting text", Submenu = "Form Controls/Text")]
        public static GPALTextArea TextAreaFor(string text = "")
        {
            var c = new GPALTextArea { Name = textAreaName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A rich text box for a GPAL form that renders Markdown, optionally with starting text", Submenu = "Form Controls/Text")]
        public static GPALRichTextBox RichTextBoxFor(string text = "")
        {
            var c = new GPALRichTextBox { Name = richTextBoxName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A checkbox with the given text, for a GPAL form, checked or not", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALCheckbox CheckboxFor(string text, bool isChecked = false)
        {
            var c = new GPALCheckbox { Name = checkboxName + controlCount++.ToString() };
            c.Text = text;
            c.IsChecked = isChecked;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A radio button with the given text, for a GPAL form, selected or not", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALRadioButton RadioButtonFor(string text, bool isChecked = false)
        {
            var c = new GPALRadioButton { Name = radioButtonName + controlCount++.ToString() };
            c.Text = text;
            c.IsChecked = isChecked;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A tab with the given title, for a GPAL form", Submenu = "Form Controls/Layout")]
        public static GPALTab TabFor(string text)
        {
            var c = new GPALTab { Name = radioButtonName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A chart for a GPAL form, optionally with a title", Submenu = "Form Controls/Lists and Data")]
        public static GPALChart ChartFor(string title = "")
        {
            var c = new GPALChart { Name = chartName + controlCount++.ToString() };
            if (!string.IsNullOrEmpty(title)) c.WithTitle(title);
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A drop-down list with the given items, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALComboBox ComboBoxFor(params string[] items)
        {
            var c = new GPALComboBox { Name = "ComboBox" + controlCount++.ToString() };
            if (items != null && items.Length > 0) c.WithItems(items);
            return c;
        }
        [Component(Description = "A data grid with the given columns, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALDataGridView DataGridViewFor(params string[] columns)
        {
            var c = new GPALDataGridView { Name = "DataGridView" + controlCount++.ToString() };
            if (columns != null && columns.Length > 0) c.WithColumns(columns);
            return c;
        }
        [Component(Description = "A list view with the given columns, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALListView ListViewFor(params string[] columns)
        {
            var c = new GPALListView { Name = "ListView" + controlCount++.ToString() };
            if (columns != null && columns.Length > 0) c.WithColumns(columns);
            return c;
        }
        [Component(Description = "A picture box showing the given image file, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALPicture PictureFor(GPALFile file)
        {
            var c = new GPALPicture { Name = "Picture" + controlCount++.ToString() };
            c.WithImage(file);
            return c;
        }
        [Component(Description = "A slider from 0 to the given maximum, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALSlider SliderFor(int maximum = 100)
        {
            var c = new GPALSlider { Name = "Slider" + controlCount++.ToString() };
            c.WithMaximum(maximum);
            return c;
        }
        [Component(Description = "A progress bar from 0 to the given maximum, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALProgressBar ProgressBarFor(int maximum = 100)
        {
            var c = new GPALProgressBar { Name = "ProgressBar" + controlCount++.ToString() };
            c.WithMaximum(maximum);
            return c;
        }
        [Component(Description = "A status strip with the given text, for a GPAL form", Submenu = "Form Controls/Menus and Bars")]
        public static GPALStatusStrip StatusStripFor(string text = "Ready")
        {
            var c = new GPALStatusStrip { Name = "StatusStrip" + controlCount++.ToString() };
            c.Text = text;
            return c;
        }
        [Component(Description = "A number box held between the given minimum and maximum, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALNumericUpDown NumericUpDownFor(decimal minimum = 0, decimal maximum = 100)
        {
            var c = new GPALNumericUpDown { Name = "NumericUpDown" + controlCount++.ToString() };
            c.WithMinimum(minimum);
            c.WithMaximum(maximum);
            return c;
        }
        [Component(Description = "A date and time picker for a GPAL form, optionally set to a value", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALDateTimePicker DateTimePickerFor(DateTime? value = null)
        {
            var c = new GPALDateTimePicker { Name = "DateTimePicker" + controlCount++.ToString() };
            if (value.HasValue) c.WithValue(value.Value);
            return c;
        }
        [Component(Description = "An empty tree view for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALTreeView TreeViewFor()
        {
            return new GPALTreeView { Name = "TreeView" + controlCount++.ToString() };
        }
        [Component(Description = "A file or folder picker with the given dialog title, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALFileSelector FileSelectorFor(string title = "Select File/Folder")
        {
            var c = new GPALFileSelector { Name = "FileSeleector" + controlCount++.ToString() };
            c.WithTitle(title);
            return c;
        }
        [Component(Description = "A layout panel with the given columns and rows, for a GPAL form", Submenu = "Form Controls/Layout")]
        public static GPALTableLayoutPanel TableLayoutPanelFor(int columns = 1, int rows = 1)
        {
            var c = new GPALTableLayoutPanel { Name = "TableLayoutPanel" + controlCount++.ToString() };
            c.WithColumnCount(columns);
            c.WithRowCount(rows);
            return c;
        }
    }
}
