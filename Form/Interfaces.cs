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
using OpenQA.Selenium;
using GenerallyPositive.Browser;
using static GenerallyPositive.GPALForm.GPALForm;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPAL;
using System.Windows.Forms.DataVisualization.Charting;
using System.Windows.Forms;
using System.Data;

namespace GenerallyPositive.GPALForm
{
    #region <FormInterfaces>
    public interface IAllowDefault
    {
        IAllowControlSettingsAndDefault Default { [Forms(Description = "Fluent interface to designate this button as the default button")] get; }
    }
    public interface IAllowFillInFrom
    {
        [Data(Description = "Fill in (overwrite) the forms inputs using tokens derived from the file")]
        IAllowFormSettingsAndActions FillInFrom(GPALFile inputFile);
        [Data(Description = "Append to the end of the forms inputs using tokens derived from the file")]
        IAllowFormSettingsAndActions AppendFrom(GPALFile inputFile);
        [Data(Description = "Insert at the beginning of the forms inputs using tokens derived from the file")]
        IAllowFormSettingsAndActions InsertFrom(GPALFile inputFile);

        [Data(Description = "Fill in (overwrite) the forms inputs using tokens derived from the database")]
        IAllowFormSettingsAndActions FillInFrom(GPALDatabase inputDatabase);
        [Data(Description = "Append to the end of the forms inputs using tokens derived from the database")]
        IAllowFormSettingsAndActions AppendFrom(GPALDatabase inputDatabase);
        [Data(Description = "Insert at the beginning of the forms inputs using tokens derived from the database")]
        IAllowFormSettingsAndActions InsertFrom(GPALDatabase inputDatabase);

        [Data(Description = "Fill in (overwrite) the forms inputs using tokens derived from the GPALGrid")]
        IAllowFormSettingsAndActions FillInFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Append to the end of the forms inputs using tokens derived from the GPALGrid")]
        IAllowFormSettingsAndActions AppendFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Insert at the beginning of the forms inputs using tokens derived from the GPALGrid")]
        IAllowFormSettingsAndActions InsertFrom(IGPALGrid<string> inputGrid);
    }
    public interface IAllowChartSettings
    {
        [Settings(Description = "Sets the series to plot in the chart")]
        IAllowControlSettingsAndChartSettings WithSeries(Series series);
        [Settings(Description = "Set the color palette of the chart bars")]
        IAllowControlSettingsAndChartSettings WithPalette(ChartColorPalette colorPalette);
        [Settings(Description = "The title to display at the top of the chart")]
        IAllowControlSettingsAndChartSettings WithTitle(string title);
    }
    public interface IAllowFontSettings
    {
        [Settings(Description = "The font style and size for the label display")]
        IAllowControlSettingsAndFontSettings WithFont(Font font);
        [Settings(Description = "The alignment of the text inside the label")]
        IAllowControlSettingsAndFontSettings WithTextAlignment(ContentAlignment contentAlignment);
        [Settings(Description = "Whether or not the label contents autosize to fit the label")]
        IAllowControlSettingsAndFontSettings WithAutoSize(bool autoSize);
    }
    public interface IAllowControlSettings
    {
        [Callback(Description = "Sets an eventhandler to attach to the Windows form control event")]
        IAllowForEventControlSettings WithCallback<TDelegate>(TDelegate handler)
            where TDelegate : Delegate;
        [Settings(Description = "Attach a handler that runs off the UI thread, so the form keeps painting while it works. GPAL controls set from it marshal back on their own.")]
        IAllowForEventControlSettings WithCallbackOffUIThread<TDelegate>(TDelegate handler)
            where TDelegate : Delegate;
        [Settings(Description = "Sets the height of the control, in pixels")]
        IAllowControlSettings WithHeight([Pixels] int height);
        [Settings(Description = "The width of the control in pixels. Without one the control stretches across its container; with one it keeps that width and sits centered")]
        IAllowControlSettings WithWidth([Pixels] int width);
        [Settings(Description = "Sets the name for the control")]
        IAllowControlSettings WithName(string name);
        [Settings(Description = "Set the text of the control")]
        IAllowControlSettings WithTag(object tag);
        [Settings(Description = "Whether the control responds to the user. Every control has it")]
        IAllowControlSettings WithEnabled(bool trueFalse = true);
        [Settings(Description = "Whether the user can change what the control holds. The controls that hold nothing ignore it")]
        IAllowControlSettings WithReadOnly(bool readOnly = true);
        //// post-v1: control alignment
        //IAllowControlSettings WithAlignment(ContentAlignment alignment);
        [Settings(Description = "Set the text of the control")]
        IAllowControlSettingsAndFontSettings WithText(string textToUse);
        [Settings(Description = "Set the keyboard shortcut that will activate this controls eventhandler defined with.WithCallback")]
        IAllowControlSettings WithShortcutKey(System.Windows.Forms.Keys shortCutKey);
        [Settings(Description = "Sets the tooltip text shown when hovering over the control")]
        IAllowToolTipDelay WithToolTip(string toolTipText);
        dynamic ToGPALObject();
    }
    public interface IAllowForEventControlSettings: IAllowControlSettings
    {
        [Callback(Description = "Specifies which event the most recently added WithCallback handler should be attached to, replacing its")]
        IAllowControlSettings ForEvent(ControlEventType eventType);
    }
    public interface IAllowFileSelectorControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the title bar text of the selection dialog")]
        IAllowFileSelectorControlSettings WithTitle(string title);
        [Settings(Description = "Sets the directory the selection dialog initially opens to")]
        IAllowFileSelectorControlSettings WithInitialDirectory([Directory] string path);
        [Settings(Description = "Sets the file type filter used by the OpenFileDialog when WithSelectFolder is false")]
        IAllowFileSelectorControlSettings WithFilter(string filter);
        [Settings(Description = "Sets whether the control opens a FolderBrowserDialog (folder selection) or an OpenFileDialog (file selection)")]
        IAllowFileSelectorControlSettings WithSelectFolder(bool folderMode);
        [Settings(Description = "Sets whether the file dialog allows selecting multiple files")]
        IAllowFileSelectorControlSettings WithMultiSelect(bool allowMulti);
        [Settings(Description = "Show an editable path box with a small browse button, so a path can be typed or picked")]
        IAllowFileSelectorControlSettings WithCompact(bool compact);
        [Settings(Description = "Sets placeholder hint text shown in the text field when it is empty")]
        IAllowFileSelectorControlSettings WithPlaceholder(string placeholder);
        [Settings(Description = "The path the selector starts on")]
        IAllowFileSelectorControlSettings WithSelectedPath([FilePath] string path);
    }
    public interface IAllowSplitterControlSettings : IAllowControlSettings
    {
        [Forms(Description = "A vertical bar with a left and a right pane, the left one this share of the width, so it keeps its proportion as the form resizes")]
        IAllowSplitterControlSettings HSplit([Pixels] int leftWidth);
        [Forms(Description = "A vertical bar with a left and a right pane, the left one this share of the width, so it keeps its proportion as the form resizes")]
        IAllowSplitterControlSettings HSplit(double leftPercent);
        [Forms(Description = "A horizontal bar with a top and a bottom pane, the top one this many pixels tall")]
        IAllowSplitterControlSettings VSplit([Pixels] int topHeight);
        [Forms(Description = "A horizontal bar with a top and a bottom pane, the top one this share of the height, so it keeps its proportion as the form resizes")]
        IAllowSplitterControlSettings VSplit(double topPercent);
        [Settings(Description = "Add a control to the splitter")]
        IAllowSplitterControlSettings WithFormControl(object gPALFormControl);
        // Redeclared because the base returns IAllowControlSettings, which lacks the panes and a typed ToGPALObject;
        // the chain would go dynamic and fail only at build.
        [Settings(Description = "A vertical bar with a left and a right pane, the left one this many pixels wide")]
        new IAllowSplitterControlSettings WithHeight([Pixels] int height);
        new GPALSplitter ToGPALObject();
    }
    public interface IAllowGroupBoxControlSettings : IAllowControlSettings
    {
        [Settings(Description = "The caption along the top of the box")]
        new IAllowGroupBoxControlSettings WithText(string text);
        [Settings(Description = "Add a control inside the box")]
        IAllowGroupBoxControlSettings WithFormControl(object gPALFormControl);
        new GPALGroupBox ToGPALObject();
    }
    public interface IAllowTreeViewControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets whether checkboxes are displayed next to each tree node")]
        IAllowTreeViewControlSettings WithCheckBoxes(bool enable);
        [Settings(Description = "Sets whether lines are drawn between sibling nodes and their parent")]
        IAllowTreeViewControlSettings WithShowLines(bool show);
        [Settings(Description = "Sets whether plus/minus expand buttons are displayed next to nodes that have children")]
        IAllowTreeViewControlSettings WithShowPlusMinus(bool show);
    }
    public interface IAllowDateTimePickerControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the initial date/time value")]
        IAllowDateTimePickerControlSettings WithValue(DateTime value);
        [Settings(Description = "Sets the display format")]
        IAllowDateTimePickerControlSettings WithFormat(DateTimePickerFormat format);
        [Settings(Description = "Sets a custom display format string, used when Format is Custom")]
        IAllowDateTimePickerControlSettings WithCustomFormat(string formatString);
    }
    public interface IAllowNumericUpDownControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the initial value")]
        IAllowNumericUpDownControlSettings WithValue(decimal value);
        [Settings(Description = "Sets the minimum allowed value")]
        IAllowNumericUpDownControlSettings WithMinimum(decimal min);
        [Settings(Description = "Sets the maximum allowed value")]
        IAllowNumericUpDownControlSettings WithMaximum(decimal max);
        [Settings(Description = "Sets the amount the value changes when the up/down spinner is clicked")]
        IAllowNumericUpDownControlSettings WithIncrement(decimal inc);
        [Settings(Description = "Sets how many decimal places are displayed")]
        IAllowNumericUpDownControlSettings WithDecimalPlaces(int places);
    }
    public interface IAllowListViewControlSettings : IAllowControlSettings
    {
        [Settings(Description = "The column headers of the list")]
        IAllowListViewControlSettings WithColumns(params string[] headers);
        [Settings(Description = "Sets the column headers for the list view (used in Details mode)")]
        IAllowListViewControlSettings WithCheckBoxes(bool enable = true);
        [Settings(Description = "Sets how items are displayed (Details, List, LargeIcon, SmallIcon, or Tile)")]
        IAllowListViewControlSettings WithView(ListViewMode view);
        [Settings(Description = "Sets whether selecting an item highlights the entire row")]
        IAllowListViewControlSettings WithFullRowSelect(bool enable);
        [Settings(Description = "Sets whether multiple items can be selected at once")]
        IAllowListViewControlSettings WithMultiSelect(bool enable);
        [Settings(Description = "How wide each column is, in column order and in pixels. -1 fits the widest item and -2 the wider of that and the header, both re-fit as rows are added or cleared. The last column takes any width the others leave, never narrower than what it was asked for")]
        IAllowListViewControlSettings WithColumnWidth(params int[] widths);
        [Settings(Description = "What share of the width each column takes, in column order. Shares reflow as the form is resized")]
        IAllowListViewControlSettings WithColumnWidth(params double[] shares);
        [Settings(Description = "How much of the spare width the next column takes when the list resizes")]
        IAllowListViewControlSettings WithColumnWeight(double weight);
        [Settings(Description = "Whether clicking a column header sorts the list by it")]
        IAllowListViewControlSettings WithSortable(bool sortable = true);
    }
    public interface IAllowStatusStripControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the initial status text")]
        new IAllowStatusStripControlSettings WithText(string text);
    }
    public interface IAllowMenuBarControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Opens (or reselects) a top-level menu")]
        IAllowMenuBarControlSettings WithMenu(string topMenu);
        [Settings(Description = "Adds an item to the currently open top-level menu")]
        IAllowMenuBarControlSettings WithItem(GPALBarItem item);
        [Settings(Description = "Adds a separator line to the currently open top-level menu")]
        IAllowMenuBarControlSettings WithSeparator();
    }
    public interface IAllowToolbarControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Adds a button to the toolbar")]
        IAllowToolbarControlSettings WithItem(GPALBarItem item);
        [Settings(Description = "Adds a vertical separator between toolbar buttons")]
        IAllowToolbarControlSettings WithSeparator();
    }
    public interface IAllowBarItemSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the item's display text")]
        new IAllowBarItemSettings WithText(string text);
        // Redeclared to return a bar item, keeping its chain available.
        [Settings(Description = "Greys the item and stops it being clicked")]
        new IAllowBarItemSettings WithEnabled(bool trueFalse = true);
        [Settings(Description = "Sets the item's icon from an in-memory image")]
        IAllowBarItemSettings WithImage(Image image);
        [Settings(Description = "Sets the item's icon by loading it from the file referenced by a GPALFile")]
        IAllowBarItemSettings WithImage(GPALFile file);
    }
    public interface IAllowRichTextBoxControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the initial Markdown source text")]
        new IAllowRichTextBoxControlSettings WithText(string text);
    }
    public interface IAllowComboBoxControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Appends the given items to the dropdown list, in order")]
        IAllowComboBoxControlSettings WithItems(params string[] itemArray);
        [Settings(Description = "Appends the given items to the dropdown list, in order")]
        IAllowComboBoxControlSettings WithItems(IEnumerable<string> itemCollection);
        [Settings(Description = "Appends the items from a non-generic array (e.g. the result of GetValues) to the dropdown list, converting each element to its string representation")]
        IAllowComboBoxControlSettings WithItems(Array itemArray);
        [Settings(Description = "Sets which item is initially selected, by index")]
        IAllowComboBoxControlSettings WithSelectedIndex([Index] int index);
        [Settings(Description = "Sets which item is initially selected, by its display text")]
        IAllowComboBoxControlSettings WithSelectedItem(string name);
    }
    public interface IAllowDataGridViewControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the column header names for the grid")]
        IAllowDataGridViewControlSettings WithColumns(params string[] columnNames);
        [Settings(Description = "Sets whether the grid's cells are read-only")]
        new IAllowDataGridViewControlSettings WithReadOnly(bool readOnly = true);
        [Settings(Description = "Sets whether columns should be auto-generated (named 'Column 1', 'Column 2', etc.) when no column names were provided via WithColumns")]
        IAllowDataGridViewControlSettings WithAutoGenerateColumns(bool autoGenerate);
        [Settings(Description = "How wide each column is, in column order and in pixels. A width that is not positive is ignored and the column is left as the grid sizes it. The last column takes any width the others leave, never narrower than what it was asked for")]
        IAllowDataGridViewControlSettings WithColumnWidth(params int[] widths);
        [Settings(Description = "What share of the width each column takes, in column order. Shares reflow as the form is resized")]
        IAllowDataGridViewControlSettings WithColumnWidth(params double[] shares);
        [Settings(Description = "Which columns wrap their text, in column order. Said bare, every column wraps. Rows grow to hold what wrapped")]
        IAllowDataGridViewControlSettings WithWrap(params bool[] wrap);
        [Settings(Description = "The rows to show, as a grid")]
        IAllowDataGridViewControlSettings WithInput(IGPALGrid<string> grid);
        [Settings(Description = "The rows to show, as a DataTable. Its columns name the grid's columns")]
        IAllowDataGridViewControlSettings WithInput(DataTable dataTable);
        [Settings(Description = "The rows to show, read by running the database's query. Its columns name the grid's columns")]
        IAllowDataGridViewControlSettings WithInput(IGPALDatabase database);
        [Settings(Description = "The rows to show, read from a file and split the way the file says")]
        IAllowDataGridViewControlSettings WithInput(GPALFile file);
        [Settings(Description = "The first row a range covers, counting from one. Unset reads from the top")]
        IAllowDataGridViewControlSettings WithStartRow(int row);
        [Settings(Description = "The first column a range covers, counting from one. Unset reads from the left")]
        IAllowDataGridViewControlSettings WithStartColumn(int column);
        [Settings(Description = "The last row a range covers, counting from one. Unset reads to the bottom")]
        IAllowDataGridViewControlSettings WithEndRow(int row);
        [Settings(Description = "The last column a range covers, counting from one. Unset reads to the right")]
        IAllowDataGridViewControlSettings WithEndColumn(int column);
        [Action(Description = "Read what the grid is showing, or the range of it named by the start and end settings")]
        IAllowDataGridViewControlSettings GetGrid(out IGPALGrid<string> grid);
    }
    public interface IAllowPictureControlSettings : IAllowControlSettings
    {
        [Settings(Description = "The image to display, read from a file.")]
        IAllowPictureControlSettings WithImage(GPALFile file);
        [Settings(Description = "The image to display.")]
        IAllowPictureControlSettings WithImage(System.Drawing.Image image);
        [Settings(Description = "How the image fits the control. Zoom by default, which scales it to fit and keeps its shape.")]
        IAllowPictureControlSettings WithSizeMode(PictureBoxSizeMode sizeMode);
    }
    public interface IAllowSliderControlSettings : IAllowControlSettings
    {
        [Settings(Description = "The lowest value the slider can be dragged to.")]
        IAllowSliderControlSettings WithMinimum(int min);
        [Settings(Description = "The highest value the slider can be dragged to.")]
        IAllowSliderControlSettings WithMaximum(int max);
        [Settings(Description = "Where the slider starts.")]
        IAllowSliderControlSettings WithValue(int value);
        [Settings(Description = "How far apart the tick marks are.")]
        IAllowSliderControlSettings WithTickFrequency(int tickFrequency);
        [Settings(Description = "Horizontal or vertical.")]
        IAllowSliderControlSettings WithOrientation(Orientation orientation);
    }
    public interface IAllowProgressBarControlSettings : IAllowControlSettings
    {
        [Settings(Description = "Sets the initial progress value, clamped to Minimum/Maximum, and syncs it to the underlying control")]
        IAllowProgressBarControlSettings WithValue(int value);
        [Settings(Description = "Sets the minimum progress value")]
        IAllowProgressBarControlSettings WithMinimum(int min);
        [Settings(Description = "Sets the maximum progress value")]
        IAllowProgressBarControlSettings WithMaximum(int max);
        [Settings(Description = "Sets the visual style of the progress bar (e.g. continuous, blocks, marquee)")]
        IAllowProgressBarControlSettings WithStyle(ProgressBarStyle style);
    }
    public interface IAllowTableLayoutPanelSettings
    {
        IAllowControlSettingsAndTableLayoutPanelSettings WithGrowStyle(TableLayoutPanelGrowStyle tableLayoutPanelGrowStyle);
        IAllowControlSettingsAndTableLayoutPanelSettings WithColumnCount(int columnCount);
        IAllowControlSettingsAndTableLayoutPanelSettings WithRowCount(int rowCount);
    }
    public interface IAllowCallBack
    {
        // return values
        // 0 = not handled
        // 1 = handled
        // -1 = error
        // IAllowAfterAnySelector CallIfFound(Form.CallIfDelegate callIfFound);
        // IAllowAfterAnySelector CallIfNotFound(Form.CallIfDelegate callIfNotFound);
        [Callback(Description = "Add a handler to call after a row of tokens is consumed and after all inputs are filled in with data")]
        IAllowFormSettingsAndActions CallAfterFillIn(GPALForm.CallAfterFillInDelegate callAfterFillIn);
    }
    public interface IAllowFormActions
    {
        [Action(Description = "Show the GPAL form so it may be automated by tokens")]
        IAllowFormActionsAndFillIn Show();
        [Action(Description = "Show the GPAL form as a modal")]
        IAllowFormActions ShowDialog();
        [Action(Description = "Hide the form. If ShowDialog is the last call in Main, hiding it from a button ends the program")]
        IAllowFormActions Hide();
        dynamic ToGPALObject();
    }
    public interface IAllowToolTipDelay : IAllowControlSettings
    {
        [Settings(Description = "Sets how long the tooltip set via WithToolTip remains visible")]
        IAllowControlSettings WithToolTipDelay([Milliseconds] int delayInMs);
    }
    public interface IAllowFormActionsAndFillIn
        : IAllowFillInFrom
        , IAllowFormActions
    { }

    public interface IAllowFormSettings
    {
        [Settings(Description = "Add a control to the form. Containers are built whole first, so a splitter or a group box is added the same way as anything else")]
        IAllowFormSettingsAndActions WithFormControl(GPALControl gPALFormControl);
        [Settings(Description = "Define the initial width of your GPAL form")]
        IAllowFormSettingsAndActions WithWidth([Pixels] int width);
        [Settings(Description = "The width every button on the form takes, in pixels, unless the button asked for its own. A button with a width keeps it and sits centered instead of stretching")]
        IAllowFormSettingsAndActions WithButtonWidth([Pixels] int width);
        [Settings(Description = "Define the initial height of your GPAL form")]
        IAllowFormSettingsAndActions WithHeight([Pixels] int height);
        [Settings(Description = "Define the initial coordinate of the top side of your GPAL form")]
        IAllowFormSettingsAndActions WithTop(int top);
        [Settings(Description = "Define the initial coordinate of the left side of your GPAL form")]
        IAllowFormSettingsAndActions WithLeft(int left);
        [Settings(Description = "The title to display at the top of your form")]
        IAllowFormSettingsAndActions WithTitle(string title);
        [Settings(Description = "The icon shown in the form's title bar and on the taskbar, an .ico or a picture to make one from. Without it the form shows the program's own icon, or the GPAL robot head")]
        IAllowFormSettingsAndActions WithIcon(GPALFile iconFile);
    }
    public interface IAllowControlSettingsAndChartSettings
        : IAllowControlSettings
        , IAllowChartSettings
    { }
    public interface IAllowControlSettingsAndFontSettings
        : IAllowControlSettings
        , IAllowFontSettings
    { }
    public interface IAllowControlSettingsAndPlaceholder
        : IAllowControlSettings
    {
        [Settings(Description = "Grey hint text shown while the box is empty")]
        IAllowControlSettingsAndPlaceholder WithPlaceholder(string placeholderText);
    }
    public interface IAllowControlSettingsAndTableLayoutPanelSettings
        : IAllowControlSettings
        , IAllowTableLayoutPanelSettings
    { }
    public interface IAllowControlSettingsPlaceholderAndPassword
        : IAllowControlSettings
    {
        [Settings(Description = "Grey hint text shown while the box is empty")]
        IAllowControlSettingsPlaceholderAndPassword WithPlaceholder(string placeholderText);
        IAllowControlSettingsPlaceholderAndPassword IsPassword { [Settings(Description = "Hide what is typed, for a password")] get; }
    }
    public interface IAllowControlSettingsAndUrl
        : IAllowControlSettingsAndFontSettings
    {
        [Settings(Description = "The address the link opens in the default browser. Without one, the link opens its own text when that is an address")]
        IAllowControlSettingsAndUrl WithUrl([URL] string url);
    }
    public interface IAllowControlSettingsAndChecked
        : IAllowControlSettings
    {
        [Settings(Description = "Sets whether the checkbox is checked")]
        IAllowControlSettingsAndChecked WithChecked(bool trueFalse = false);
    }
    public interface IAllowControlSettingsAndDefault
        : IAllowControlSettings
        , IAllowDefault
    { }
    public interface IAllowFormSettingsAndActions
        : IAllowFormSettings
        , IAllowFormActions
        , IAllowCallBack
    { }
    #endregion <FormInterfaces>
    #region <Atoms>
    // Control-specific settings return that control's settings interface to stay chainable.
    #endregion
}

