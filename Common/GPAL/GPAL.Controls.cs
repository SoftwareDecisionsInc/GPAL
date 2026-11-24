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
        /// <summary>
        /// Instantiates a new GPALForm for creating simple interactive user interfaces.
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
        public static IAllowFormSettings Form
        {
            [Fluent(Description = "Build a form of controls to give a workflow a user interface")]
            get
            {
                GPALForm.GPALForm tmpForms = new GPALForm.GPALForm()
                {
                    FormName = formName + formCount++.ToString(),
                };
                FormList.Add(tmpForms);
                return tmpForms;
            }
        }
        /// <summary>
        /// Instantiates a new GPALButton for use on GPAL forms.<br/>
        /// Callback EventHandler is invoked on the GPALButton.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
        ///     .Default
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
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndDefault Button
        {
            [Forms(Description = "A button for a GPAL form, calling back when it is clicked", Submenu = "Buttons and Choices")]
            get
            {
                GPALButton tmpControl = new GPALButton
                {
                    Name = buttonName + controlCount++.ToString(),
                    IsDefault = false
                };
                // CAVEAT: the Default getter sets IsDefault true; this line sets IsDefault false on the new button
                tmpControl.IsDefault = false;
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALChart for use on GPAL forms.<br/>
        /// Callback EventHandler is invoked on the GPALChart.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALChart gPalChart = (GPALChart)GPAL.Chart
        ///     .WithPalette(ChartColorPalette.Bright)
        ///     .WithCallback&lt;EventHandler&gt;(ChartHandler) // invoked when chart gets clicked 
        ///     .WithName("Chart 1");
        ///     
        /// static void Main(string[] args)
        /// {
        ///     GenerateBarSeries();
        ///     
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalChart)
        ///         .ShowDialog();
        /// }
        ///     
        /// private static void GenerateBarSeries()
        /// {
        ///     string[] seriesArray = { "Cat", "Dog", "Bird", "Monkey" };
        ///     int[] pointsArray = { 2, 1, 7, 5 };
        ///     
        ///     gPalChart.Titles.Add("Animals");
        ///     // Add series
        ///     for (int i = 0; i &lt; seriesArray.Length; i++)
        ///     {
        ///         Series series = gPalChart.Series.Add(seriesArray[i]);
        ///         // series.ChartType = SeriesChartType.Bar;
        ///         series.Points.Add(pointsArray[i]);
        ///         Legend legend = new Legend(seriesArray[i]);
        ///         // legend.Alignment = StringAlignment.Near;
        ///         gPalChart.Legends.Add(legend);
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndChartSettings Chart
        {
            [Forms(Description = "A chart for a GPAL form", Submenu = "Lists and Data")]
            get
            {
                GPALChart tmpControl = new GPALChart
                {
                    Name = chartName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALCheckbox for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALCheckbox.WindowsControl.CheckChanged event.<br/>
        /// Default is not checked
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALCheckbox gPALCheckbox1 = (GPALCheckbox)GPAL.Checkbox
        ///     .WithChecked(true)
        ///     .WithText("Overwrite File")
        ///     .WithCallback&lt;EventHandler&gt;(CheckboxHandler); // on check changed event handler
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPALCheckbox1)
        ///         .ShowDialog();
        /// }
        /// 
        /// private static void CheckboxHandler(object sender, EventArgs e)
        /// {
        ///     if (((CheckBox) sender).Checked)
        ///     {
        ///          gPALCheckbox2.Checked = false; // uncheck the other, to act like a radio button
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndChecked Checkbox
        {
            [Forms(Description = "A checkbox for a GPAL form, calling back when it is checked or cleared", Submenu = "Buttons and Choices")]
            get
            {
                GPALCheckbox tmpControl = new GPALCheckbox
                {
                    Name = checkboxName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALComboBox for use on a GPAL form.<br/>
        /// Callback(s) set via WhenSelectedDo are invoked on the underlying ComboBox.SelectedIndexChanged event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALComboBox browserSelector = (GPALComboBox)GPAL.ComboBox
        ///     .WithName("BrowserChoice")
        ///     .WithItems("Chrome", "Edge", "Firefox")
        ///     .WithSelectedIndex(0)
        ///     .WhenSelectedDo(selected => 
        ///     {
        ///         Debug.WriteLine($"Selected browser: {selected}");
        ///         // browserType = (BrowserType)Enum.Parse(typeof(BrowserType), selected);
        ///     })
        ///     .WithToolTip("Choose target browser for automation")
        ///     .ToGPALObject();
        ///
        /// // later in form building:
        /// GPALForm myForm = (GPALForm)GPAL.Form
        ///     .WithTitle("Automation Setup")
        ///     .WithFormControl(browserSelector)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the combo box</returns>
        public static IAllowComboBoxControlSettings ComboBox
        {
            [Forms(Description = "Instantiates a new GPALComboBox for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALComboBox tmpControl = new GPALComboBox
                {
                    Name = "ComboBox" + controlCount++.ToString()
                };
                // The form adds the control to its ControlList when it is placed on the form.
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALDataGridView for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALDataGridView.WindowsControl.CellValueChanged event (or other events when specified via .ForEvent).
        /// Supports tabular data display with columns and optional editing.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALDataGridView logGrid = (GPALDataGridView)GPAL.DataGridView
        ///     .WithName("LogGrid")
        ///     .WithColumns("Time", "Level", "Message")
        ///     .WithCallback&lt;DataGridViewCellEventHandler&gt;((s, e) => Debug.WriteLine("Cell changed"))
        ///     .WithToolTip("Execution log and results")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the data grid view</returns>
        public static IAllowDataGridViewControlSettings DataGridView
        {
            [Forms(Description = "Instantiates a new GPALDataGridView for use on a GPAL form", Submenu = "Lists and Data")] get
            {
                GPALDataGridView tmp = new GPALDataGridView
                {
                    Name = "DataGridView" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// Instantiates a new GPALDateTimePicker for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALDateTimePicker.WindowsControl.ValueChanged event (or other events when specified via .ForEvent).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALDateTimePicker startTime = (GPALDateTimePicker)GPAL.DateTimePicker
        ///     .WithName("StartTime")
        ///     .WithValue(DateTime.Now)
        ///     .WithFormat(DateTimePickerFormat.Custom)
        ///     .WithCustomFormat("yyyy-MM-dd HH:mm")
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine($"Date changed to {((DateTimePicker)s).Value}"))
        ///     .WithToolTip("Select start date and time")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the date/time picker</returns>
        public static IAllowDateTimePickerControlSettings DateTimePicker
        {
            [Forms(Description = "Instantiates a new GPALDateTimePicker for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALDateTimePicker tmp = new GPALDateTimePicker
                {
                    Name = "DateTimePicker" + controlCount++.ToString()
                };
                return tmp;
            }
        }
        /// <summary>
        /// Instantiates a new GPALFileSelector for use on a GPAL form.<br/>
        /// Opens a standard file or folder selection dialog when clicked.<br/>
        /// Callback EventHandler is invoked after a selection is made (or canceled), with FileSelectorEventArgs containing the selected path(s) and cancellation status.<br/>
        /// Selected path(s) available via SelectedPath / SelectedPaths properties.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALFileSelector driverPicker = (GPALFileSelector)GPAL.FileSelector
        ///     .WithName("WebDriverPath")
        ///     .WithTitle("Select WebDriver Folder")
        ///     .WithInitialDirectory(@"C:\")
        ///     .WithSelectFolder(true)  // true = folder browser, false = file dialog
        ///     .WithFilter("Executables (*.exe)|*.exe|All files (*.*)|*.*")
        ///     .WithMultiSelect(false)
        ///     .WithCallback&lt;EventHandler&gt;((s, args) =>
        ///     {
        ///         if (args.Cancelled)
        ///         {
        ///             Debug.WriteLine("Selection cancelled");
        ///         }
        ///         else
        ///         {
        ///             Debug.WriteLine($"Selected: {args.SelectedPath}");
        ///             if (args.SelectedPaths.Count > 1)
        ///             {
        ///                 Debug.WriteLine($"Multi-select: {string.Join(", ", args.SelectedPaths)}");
        ///             }
        ///         }
        ///     })
        ///     .WithToolTip("Click to browse for ChromeDriver / geckodriver folder")
        ///     .ToGPALObject();
        ///
        /// // later in form building:
        /// GPALForm myForm = (GPALForm)GPAL.Form
        ///     .WithTitle("File Selection Test")
        ///     .WithFormControl(driverPicker)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the file/folder selector</returns>
        public static IAllowFileSelectorControlSettings FileSelector
        {
            [Forms(Description = "Instantiates a new GPALFileSelector for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALFileSelector gfs = new GPALFileSelector
                {
                    Name = "FileSeleector" + controlCount++.ToString()
                };
                return gfs;
            }
        }
        /// <summary>
        /// Instantiates a new one-line GPALInput control for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALInput.WindowsControl.TextChanged event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALInput gPalInput = (GPALInput)GPAL.Input
        ///     .WithText(@"c:\drivers")
        ///     .WithCallback&lt;EventHandler&gt;(InputHandler); // on text changed event handler
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalInput)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsPlaceholderAndPassword Input
        {
            [Forms(Description = "A one-line input box for a GPAL form, calling back when its text changes", Submenu = "Text")]
            get
            {
                GPALInput tmpControl = new GPALInput
                {
                    Name = inputName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALLinkLabel for use on a GPAL form: a link that opens its address in the default
        /// browser when clicked.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALLinkLabel moreBots = (GPALLinkLabel)GPAL.LinkLabel
        ///     .WithUrl("https://gpal.sditx.com/docs/botw")
        ///     .WithText("More Bots of the Week");
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndUrl LinkLabel
        {
            [Forms(Description = "A link for a GPAL form that opens an address in the default browser", Submenu = "Text")]
            get
            {
                GPALLinkLabel tmpControl = new GPALLinkLabel
                {
                    Name = linkLabelName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALLabel for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALLabel.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALLabel gPalLabel = (GPALLabel)GPAL.Label
        ///     .WithText("Form Tester");
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalLabel)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndFontSettings Label
        {
            [Forms(Description = "A label for a GPAL form", Submenu = "Text")]
            get
            {
                GPALLabel tmpControl = new GPALLabel
                {
                    Name = labelName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALListView for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALListView.WindowsControl.SelectedIndexChanged event (or other events when specified via .ForEvent).<br/>
        /// Supports columns, different view modes (Details, List, etc.), selection, and double-click activation.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALListView fileResults = GPAL.ListView
        ///     .WithName("SavedFiles")
        ///     .WithColumns("Filename", "Size (KB)", "Date")
        ///     .WithView(ListViewMode.Details)
        ///     .WithFullRowSelect(true)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Selection changed"))
        ///     .ForEvent(ControlEventType.SelectedIndexChanged)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Item activated"))
        ///     .ForEvent(ControlEventType.DoubleClick)
        ///     .WithToolTip("List of processed or downloaded files")
        ///     .ToGPALObject();
        ///
        /// // later in form building:
        /// GPALForm myForm = (GPALForm)GPAL.Form
        ///     .WithTitle("File Results")
        ///     .WithFormControl(fileResults)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the list view</returns>
        public static IAllowListViewControlSettings ListView
        {
            [Forms(Description = "Instantiates a new GPALListView for use on a GPAL form", Submenu = "Lists and Data")] get
            {
                GPALListView tmp = new GPALListView
                {
                    Name = "ListView" + controlCount++.ToString()
                };
                return tmp;
            }
        }
        /// <summary>
        /// Instantiates a new GPALNumericUpDown for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALNumericUpDown.WindowsControl.ValueChanged event (or other events when specified via .ForEvent).
        /// Useful for selecting numeric values such as timeouts, retry counts, ports, etc.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALNumericUpDown timeoutSelector = (GPALNumericUpDown)GPAL.NumericUpDown
        ///     .WithName("TimeoutSeconds")
        ///     .WithMinimum(0)
        ///     .WithMaximum(300)
        ///     .WithValue(30)
        ///     .WithIncrement(5)
        ///     .WithDecimalPlaces(0)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine($"Timeout changed to {((NumericUpDown)s).Value}"))
        ///     .WithToolTip("Maximum wait time in seconds")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the numeric up/down control</returns>
        public static IAllowNumericUpDownControlSettings NumericUpDown
        {
            [Forms(Description = "Instantiates a new GPALNumericUpDown for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALNumericUpDown tmp = new GPALNumericUpDown
                {
                    Name = "NumericUpDown" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A picture control for GPAL forms.<br/>
        /// Shows an image, from a file or from an Image already in hand.<br/>
        /// The image can be replaced while the form is up, and the control repaints itself.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALPicture shot = (GPALPicture)GPAL.Picture
        ///     .WithImage("screenshot.png")
        ///     .WithSizeMode(PictureBoxSizeMode.Zoom)
        ///     .WithHeight(300)
        ///     .WithName("shot")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowPictureControlSettings Picture
        {
            [Forms(Description = "A picture control for GPAL forms", Submenu = "Lists and Data")] get
            {
                GPALPicture tmp = new GPALPicture
                {
                    Name = "Picture" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A slider control for GPAL forms.<br/>
        /// Picks a number from a range by dragging, and exposes that range to UIAutomation, which is what a
        /// workflow drives with SetRange.<br/>
        /// Callback (if set) is invoked on the ValueChanged event of the underlying TrackBar.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALSlider volume = (GPALSlider)GPAL.Slider
        ///     .WithMinimum(0)
        ///     .WithMaximum(100)
        ///     .WithValue(50)
        ///     .WithTickFrequency(10)
        ///     .WithName("volume")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowSliderControlSettings Slider
        {
            [Forms(Description = "A slider control for GPAL forms", Submenu = "Buttons and Choices")] get
            {
                GPALSlider tmp = new GPALSlider
                {
                    Name = "Slider" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A progress bar control for GPAL forms.<br/>
        /// Displays task completion percentage or indeterminate (marquee) activity.<br/>
        /// Callback (if set) is invoked on the ValueChanged event of the underlying ProgressBar.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALProgressBar downloadProgress = (GPALProgressBar)GPAL.ProgressBar
        ///     .WithName("DownloadProgress")
        ///     .WithMinimum(0)
        ///     .WithMaximum(100)
        ///     .WithValue(0)
        ///     .WithStyle(ProgressBarStyle.Continuous)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Progress updated"))
        ///     .WithToolTip("Current task progress")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowProgressBarControlSettings ProgressBar
        {
            [Forms(Description = "A progress bar control for GPAL forms", Submenu = "Lists and Data")] get
            {
                GPALProgressBar tmp = new GPALProgressBar
                {
                    Name = "ProgressBar" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// Instantiates a new GPALRadioButton for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALRadioButton.WindowsControl.CheckChanged event.<br/>
        /// Default is not selected
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALRadioButton gPalRadioButton1 = (GPALRadioButton)GPAL.RadioButton
        ///     .WithChecked(true)
        ///     .WithName("Radiobutton1")
        ///     .WithText("Chrome")
        ///     .WithCallback&lt;EventHandler&gt;(RadioButton1Handler); // on check changed event handler
        ///     
        /// static GPALRadioButton gPalRadioButton2 = (GPALRadioButton)GPAL.RadioButton
        ///     .WithName("Radiobutton2")
        ///     .WithText("Edge")
        ///     .WithCallback&lt;EventHandler&gt;(RadioButton2Handler); // on check changed event handler
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalRadioButton1)
        ///         .WithFormControl(gPalRadioButton2)
        ///         .ShowDialog();
        /// }
        /// 
        /// private static void RadioButtonHandler(object sender, EventArgs e)
        /// {
        ///     if (((CheckBox) sender).Checked)
        ///     {
        ///         // YOUR CODE HERE - you don't have to thave two handlers, you could have one
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndChecked RadioButton
        {
            [Forms(Description = "A radio button for a GPAL form, calling back when it is selected", Submenu = "Buttons and Choices")]
            get
            {
                GPALRadioButton tmpControl = new GPALRadioButton
                {
                    Name = radioButtonName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// A status strip control (docked at the bottom) for GPAL forms.<br/>
        /// Displays real-time status messages, browser info, file counts, etc.<br/>
        /// Callback (if set) is invoked on the primary StatusStrip event (usually ItemClicked or similar).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALStatusStrip appStatus = (GPALStatusStrip)GPAL.StatusStrip
        ///     .WithName("MainStatus")
        ///     .WithText("Ready")
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Status clicked"))
        ///     .WithToolTip("Application status bar")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowStatusStripControlSettings StatusStrip
        {
            [Forms(Description = "A status strip control (docked at the bottom) for GPAL forms", Submenu = "Menus and Bars")] get
            {
                GPALStatusStrip tmp = new GPALStatusStrip
                {
                    Name = "StatusStrip" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A menu bar docked to the top of a GPAL form.<br/>
        /// Open a top-level menu with WithMenu("TopMenu"), then attach GPAL.BarItem items with WithItem(item).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALMenuBar gPALMenuBar = (GPALMenuBar)GPAL.MenuBar
        ///     .WithMenu("File")
        ///     .WithItem(saveItem)
        ///     .WithSeparator()
        ///     .WithItem(exitItem)
        ///     .WithMenu("Help")
        ///     .WithItem(aboutItem)
        ///     .WithName("MainMenu")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowMenuBarControlSettings MenuBar
        {
            [Forms(Description = "A menu bar docked to the top of a GPAL form", Submenu = "Menus and Bars")] get
            {
                GPALMenuBar tmp = new GPALMenuBar
                {
                    Name = "MenuBar" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A single item shared by menu bars and toolbars: text, optional image, optional shortcut key,
        /// and a callback wired to its click.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALBarItem saveItem = (GPALBarItem)GPAL.BarItem
        ///     .WithText("Save")
        ///     .WithImage(saveIcon)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)
        ///     .WithCallback&lt;EventHandler&gt;(SaveHandler)
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowBarItemSettings BarItem
        {
            [Forms(Description = "A single item shared by menu bars and toolbars: text, optional image, optional shortcut key, and a callback wired to its click", Submenu = "Menus and Bars")] get
            {
                GPALBarItem tmp = new GPALBarItem
                {
                    Name = "BarItem" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A toolbar docked below the menu bar, holding GPAL.BarItem buttons and optional separators.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALToolbar gPALToolbar = (GPALToolbar)GPAL.Toolbar
        ///     .WithItem(newItem)
        ///     .WithSeparator()
        ///     .WithItem(runItem)
        ///     .WithName("MainToolbar")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowToolbarControlSettings Toolbar
        {
            [Forms(Description = "A toolbar docked below the menu bar, holding GPAL.BarItem buttons and optional separators", Submenu = "Menus and Bars")] get
            {
                GPALToolbar tmp = new GPALToolbar
                {
                    Name = "Toolbar" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// Instantiates a new GPALTab for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALTab.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALTab gPalTab1 = (GPALTab)GPAL.Tab
        ///     .WithName("Tab1")
        ///     .WithText("Tab 1")
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.D1); // Alt-1
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithTab(gPalTab1)  // items after this are placed on this tab
        ///         .WithFormControl(gPalRadioButton1)
        ///         .WithFormControl(gPalRadioButton2)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettings Tab
        {
            [Forms(Description = "A tab page for a GPAL form", Submenu = "Layout")]
            get
            {
                GPALTab tmpControl = new GPALTab
                {
                    Name = radioButtonName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }

        /// <summary>
        /// Instantiates a new GPALSplitter for use on a GPAL form: two panes with a bar between them, built whole
        /// and then laid out like any other control.<br/>
        /// Call .HSplit for a left and right pane or .VSplit for a top and bottom, add the controls of both panes
        /// in order, and mark where the first pane ends with <see cref="SplitRight"/> or <see cref="SplitBottom"/>.
        /// <example>
        /// <code>
        /// GPAL.Splitter.HSplit(0.55)
        ///     .WithFormControl(GPAL.LabelFor("Automation Engine:"))
        ///     .WithFormControl(comboBoxAutomationEngine)
        ///     .WithFormControl(GPAL.SplitRight)
        ///     .WithFormControl(gPALNumeric);
        /// </code>
        /// </example>
        /// </summary>
        public static IAllowSplitterControlSettings Splitter { [Forms(Description = "Instantiates a new GPALSplitter for use on a GPAL form: two panes with a bar between them, built whole and then laid out like any other control", Submenu = "Layout")] get => new GPALSplitter(); }

        /// <summary>
        /// Instantiates a new GPALGroupBox for use on a GPAL form: a titled box around a set of controls, laid out
        /// like any other control. A container can go inside it, so a group box can hold a splitter.
        /// </summary>
        public static IAllowGroupBoxControlSettings GroupBox { [Forms(Description = "Instantiates a new GPALGroupBox for use on a GPAL form: a titled box around a set of controls, laid out like any other control", Submenu = "Layout")] get => new GPALGroupBox(); }

        /// <summary>Marks the end of a splitter's left pane. Everything added after it is the right pane.</summary>
        public static GPALSpliter SplitRight { [Forms(Description = "Marks the end of a splitter's left pane", Submenu = "Layout")] get => new GPALSpliter(); }

        /// <summary>Marks the end of a splitter's top pane. Everything added after it is the bottom pane.</summary>
        public static GPALSpliter SplitBottom { [Forms(Description = "Marks the end of a splitter's top pane", Submenu = "Layout")] get => new GPALSpliter(); }

        /// <summary>
        /// Instantiates a new GPALTextArea for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALTextarea.WindowsControl.TextChanged event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALTextArea gPalTextArea = (GPALTextArea)GPAL.TextArea
        ///     .WithText("Type something here")
        ///     .WithCallback&lt;EventHandler&gt;(TextAreaHandler) // on text changed event handler
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.T);  // Alt-T
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalTextArea)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndPlaceholder TextArea
        {
            [Forms(Description = "A multi-line text box for a GPAL form, calling back when its text changes", Submenu = "Text")]
            get
            {
                GPALTextArea tmpControl = new GPALTextArea
                {
                    Name = textAreaName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALRichTextBox for use on a GPAL form.<br/>
        /// Renders Markdown (headings, **bold**, and bullet/numbered lists with nesting) - useful for
        /// displaying formatted AI responses or other structured text.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALRichTextBox gPalOutput = (GPALRichTextBox)GPAL.RichTextBox
        ///     .WithName("AIOutput")
        ///     .ToGPALObject();
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalOutput)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowRichTextBoxControlSettings RichTextBox
        {
            [Forms(Description = "A rich text box for a GPAL form that renders Markdown", Submenu = "Text")]
            get
            {
                GPALRichTextBox tmpControl = new GPALRichTextBox
                {
                    Name = richTextBoxName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALTreeView for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALTreeView.WindowsControl.AfterSelect event (or other events when specified via .ForEvent).
        /// Supports hierarchical node display (folders, categories, workflows, etc.).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALTreeView workflowTree = (GPALTreeView)GPAL.TreeView
        ///     .WithName("WorkflowSteps")
        ///     .WithCallback&lt;TreeViewEventHandler&gt;((s, e) => 
        ///     {
        ///         var tv = (TreeView)s;
        ///         Debug.WriteLine($"Selected node: {tv.SelectedNode?.Text}");
        ///     })
        ///     .WithToolTip("Workflow structure or directory tree")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the tree view</returns>
        public static IAllowTreeViewControlSettings TreeView
        {
            [Forms(Description = "Instantiates a new GPALTreeView for use on a GPAL form", Submenu = "Lists and Data")] get
            {
                GPALTreeView tmp = new GPALTreeView
                {
                    Name = "TreeView" + controlCount++.ToString()
                };
                return tmp;
            }
        }
    }
}
