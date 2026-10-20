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
using static GenerallyPositive.GPALForm.GPALForm;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPAL;
using static GenerallyPositive.Application.Application;
using System.Windows.Automation;

namespace GenerallyPositive.Application
{
    #region <Application Interfaces>
    #region <Base Application Interfaces>
    public interface IAllowPersistentCallBack
    {
        // return values
        // 0 = not handled
        // 1 = handled
        // -1 = error
        [Callback(Description = "GLOBAL based callback if any selector in any UOW is found")]
        IAllowPersistentCallBack PersistentCallIfFound(Application.CallIfDelegate CallIfFound);
        [Callback(Description = "GLOBAL based callback if any selector in any UOW is NOT found")]
        IAllowPersistentCallBack PersistentCallIfNotFound(Application.CallIfDelegate CallIfNotFound);
    }

    public interface IAllowFileDialog
    {
        [Action(Description = "Click the element that raises an Open dialog, then answer it with this file or folder.", Submenu = "Clicks")]
        IAllowSelectorActionOrAnySelector LeftClickAndOpen(GPALFile file);
        [Action(Description = "Click the element that raises a Save dialog, then answer it with this file. An existing file of that name is deleted first, so the dialog never asks whether to replace it.", Submenu = "Clicks")]
        IAllowSelectorActionOrAnySelector LeftClickAndSave(GPALFile file);
        [Action(Description = "Answer a file dialog that is already on screen with this file or folder.", Submenu = "Windows")]
        IAllowSelectorActionOrAnySelector AnswerFileDialog(GPALFile file);
    }
    public interface IAllowFillInFrom
    {
        [Data(Description = "Replace the control's text with this literal string", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector FillInFrom(string textToUse);
        [Data(Description = "Add this literal string to the end of the control's text", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector AppendFrom(string textToUse);
        [Data(Description = "Insert this literal string at the control's caret", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector InsertFrom(string textToUse);

        [Data(Description = "Replace the control's text with what the file holds, a value per control matched", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector FillInFrom(GPALFile inputFile);
        [Data(Description = "Add what the file holds to the end of the control's text", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector AppendFrom(GPALFile inputFile);
        [Data(Description = "Insert what the file holds at the control's caret", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector InsertFrom(GPALFile inputFile);

        [Data(Description = "Replace the control's text with what the query returns, a value per control matched", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector FillInFrom(GPALDatabase inputDatabase);
        [Data(Description = "Add what the query returns to the end of the control's text", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector AppendFrom(GPALDatabase inputDatabase);
        [Data(Description = "Insert what the query returns at the control's caret", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector InsertFrom(GPALDatabase inputDatabase);

        [Data(Description = "Replace the control's text with the grid's values, a value per control matched", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector FillInFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Add the grid's values to the end of the control's text", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector AppendFrom(IGPALGrid<string> inputGrid);
        [Data(Description = "Insert the grid's values at the control's caret", Submenu = "Fill In")] IAllowSelectorActionOrAnySelector InsertFrom(IGPALGrid<string> inputGrid);
    }
    public interface IAllowClicks
    {
        [Action(Description = "Left click the element. Uses the selector given, or the one already on the unit of work", Submenu = "Clicks")] IAllowSelectorActionOrAnySelector LeftClick(Selector selector = null);
        [Action(Description = "Left click while holding Ctrl, Shift or Alt", Submenu = "Clicks")] IAllowSelectorActionOrAnySelector LeftClick(ModifierKeys modifierKeys);
        [Action(Description = "Double click the element. Uses the selector given, or the one already on the unit of work", Submenu = "Clicks")] IAllowSelectorActionOrAnySelector LeftDoubleClick(Selector selector = null);
        [Action(Description = "Middle click the element. Uses the selector given, or the one already on the unit of work", Submenu = "Clicks")] IAllowSelectorActionOrAnySelector MiddleClick(Selector selector = null);
        [Action(Description = "Right click the element. Uses the selector given, or the one already on the unit of work", Submenu = "Clicks")] IAllowSelectorActionOrAnySelector RightClick(Selector selector = null);
        [Action(Description = "Drag-and-drop using selector's DeltaX/DeltaY as the drag distance from the element's origin. Always uses hardware mouse events.", Submenu = "Mouse")] IAllowSelectorActionOrAnySelector DragAndDrop(Selector selector = null);
        [Action(Description = "Drag-and-drop while holding a modifier key.", Submenu = "Mouse")] IAllowSelectorActionOrAnySelector DragAndDrop(ModifierKeys modifierKeys);
    }
    public interface IAllowKeyboardActions
    {
        /// <summary>
        /// Simulates typing via hardware key events, one character at a time. Honors
        /// <see cref="GPAL.TypingDelay"/> (see <see cref="GPAL.WithTypingDelay"/>): 0 sends the
        /// string with no inter-character pacing, greater than 0 sleeps that many milliseconds
        /// between characters.
        /// </summary>
        [Data(Description = "Type this text, as if at the keyboard, into whatever has focus", Submenu = "Keyboard")] IAllowSelectorActionOrAnySelector SendString(string textToSend);
        [Data(Description = "Press one key by its Windows virtual key code", Submenu = "Keyboard")] IAllowSelectorActionOrAnySelector SendKey(byte VKCode);
        [Data(Description = "Press and hold a modifier key (Alt, Control, Shift) using hardware emulation", Submenu = "Keyboard")]
        IAllowSelectorActionOrAnySelector PressModifierKey(ModifierKeys modifierKeys);
        [Data(Description = "Release a previously pressed modifier key (Alt, Control, Shift) using hardware emulation", Submenu = "Keyboard")]
        IAllowSelectorActionOrAnySelector ReleaseModifierKey(ModifierKeys modifierKeys);
    }
    public interface IAllowScrolling
    {
        [Scroll(Description = "Scroll horizontally by one notch, this many times")] IAllowSelectorActionOrAnySelector ScrollHorizontalSmall(int iterations);
        [Scroll(Description = "Scroll horizontally by one page, this many times")] IAllowSelectorActionOrAnySelector ScrollHorizontalLarge(int iterations);
        [Scroll(Description = "Scroll horizontally to this percentage of the width")] IAllowSelectorActionOrAnySelector ScrollHorizontalPercent([Percent] int percent);
        [Scroll(Description = "Scroll vertically by one notch, this many times")] IAllowSelectorActionOrAnySelector ScrollVerticalSmall(int iterations);
        [Scroll(Description = "Scroll vertically by one page, this many times")] IAllowSelectorActionOrAnySelector ScrollVerticalLarge(int iterations);
        [Scroll(Description = "Scroll vertically to this percentage of the height")] IAllowSelectorActionOrAnySelector ScrollVerticalPercent([Percent] int percent);
    }
    public interface IAllowCallBack
    {
        // return values
        // 0 = not handled
        // 1 = handled
        // -1 = error
        [Callback(Description = "UOW based callback if any selector in the UOW is found")]
        IAllowAfterAnySelector CallIfFound(Application.CallIfDelegate CallIfFound);
        [Callback(Description = "UOW based callback if any selector in the UOW is NOT found")]
        IAllowAfterAnySelector CallIfNotFound(Application.CallIfDelegate CallIfNotFound);
        [Callback(Description = "UOW based callback when a row of input is consumed and entered.\nUseful for chaining to another workflow")]
        IAllowAfterAnySelector CallAfterFillIn(Application.CallAfterFillInDelegate CallAfterFillIn);
    }

    public interface IAllowSizeControl
    {
        [Action(Description = "Maximize the window matched by WaitForWindow")]
        IAllowAllApplicationAndAllSelector Maximize { [Action(Description = "Maximize the application to full-screen", Submenu = "Windows")] get; }
        [Action(Description = "Minimize the window matched by WaitForWindow")]
        IAllowAllApplicationAndAllSelector Minimize { [Action(Description = "Minimize the application to the taskbar", Submenu = "Windows")] get; }
        [Action(Description = "Restore the window matched by WaitForWindow to the size it had before it was maximized or minimized")]
        IAllowAllApplicationAndAllSelector Restore { [Action(Description = "Restore the application to 'original' size, unminimize", Submenu = "Windows")] get; }
        [Action(Description = "Resize the window matched by WaitForWindow to the given width and height in pixels.")]
        IAllowAllApplicationAndAllSelector ResizeWindow(int width, int height);
        [Action(Description = "Move the window matched by WaitForWindow to the given screen coordinates.")]
        IAllowAllApplicationAndAllSelector MoveWindow(int x, int y);
    }
    public interface IAllowWaitForWindow
    {
        [Wait(Description = "Wait for a window with this exact title, for up to WithWaitForWindowTimeout seconds")]
        IAllowAfterWaitForAndSizeControl WaitForWindow(string windowName);
        [Wait(Description = "Wait for a window whose title matches this regular expression, for up to WithWaitForWindowTimeout seconds")]
        IAllowAfterWaitForAndSizeControl WaitForWindowRegex([Regex] string windowNameRegex);
        [Wait(Description = "How long WaitForWindow and WaitForWindowRegex wait, in seconds")]
        IAllowAfterWaitForAndSizeControl WithWaitForWindowTimeout([Seconds] int waitTimeInSeconds);

    }
    public interface IAllowTabActions
    {
        [Action(Description = "Bring the tab named by the selector to the front", Submenu = "Windows")] IAllowAllActionAndAnySelector SwitchToTab(Selector selector = null);
    }
    public interface IAllowApplicationOpen
    {
        [Fluent(Description = "The application to drive, by the path to its executable")]
        IAllowAllExceptParameters Open([Directory()] string ApplicationPath);
    }
    public interface IAllowApplicationSettings
    {
        [Settings(Description = "Name to use in Information and Exception channel messages")]
        IAllowApplicationSettingsOrOpen WithName(string name);
        [Settings(Description = "Parameter to pass into the program (will be concatenated to one string)")]
        IAllowApplicationSettingsOrOpen WithParameter(string parameter);
        [Settings(Description = "Use hardwre emulation for the entire workflow")]
        IAllowApplicationSettingsOrOpen WithUseHardware(bool trueFalse);
        [Settings(Description = "Launch a new copy of the program rather than attaching to one already running")]
        IAllowApplicationSettingsOrOpen WithNewInstance(bool trueFalse);
        [Settings(Description = "Run the program on a Win32 desktop of its own, where it is driven by UIAutomation and takes no focus from you")]
        IAllowApplicationSettingsOrOpen WithHiddenDesktop(bool hiddenDesktop);
        [Settings(Description = "Run the program on the named Win32 desktop. Programs naming the same one share it")]
        IAllowApplicationSettingsOrOpen WithHiddenDesktop(string desktopName);
    }
    #endregion <Base Application Interfaces>
        #region <Derived Application Interfaces>
    public interface IAllowApplicationSettingsOrOpen : IAllowApplicationSettings, IAllowApplicationOpen
    { }
    public interface IAllowApplicationOpenOrParameters
    : IAllowApplicationOpen
    , IAllowApplicationSettings
    { }
    public interface IAllowAfterAnySelector
        : IAllowSelectorAction
        , IAllowCallBack
        , IAllowWithAllThatMatch
        , IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
    { }
    // NOTE: Add to this list what is added above
    public interface IAllowAfterAnySelectorExceptWithAll
        : IAllowSelectorAction
        , IAllowCallBack
        , IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
    { }

    public interface IAllowSelectorAction
        : IAllowClicks
        , IAllowFileDialog
        , IAllowFillInFrom
        , IAllowFocus
        , IAllowHover
        , IAllowMoveTo
        , IAllowScrolling
        , IAllowWaitFor
        , IAllowKeyboardActions
        , IAllowSetRange
        , IAllowToGPALObject<Application>
    { }

    public interface IAllowSelectorActionOrAnySelector
        : IAllowWithSelector
        , IAllowSelectorAction
        , IAllowWaitForWindow
        , IAllowWaitFor
        , IAllowCallBack
        , IAllowWithAllThatMatch
        , IAllowWithNextPageButtonOrGetGrid
        , IAllowWithInfiniteScroll
        , IAllowWithPages
    { }
    public interface IAllowWithHeaderOrFileActions
    : IAllowWithHeader
    , IAllowFileActions
    , IAllowAllActionAndAnySelector
    { }
    public interface IAllowFileActions
    {
        [File(Description = "Save the retrieved grid. Output format and delimiter are determined by the GPALFile (its extension, or explicit file settings).")]
        IAllowAllActionAndAnySelector SaveTo([GPALFile(Description = "The file to write")] GPALFile file);
        [File(Description = "Append the retrieved grid. Output format and delimiter are determined by the GPALFile (its extension, or explicit file settings).")]
        IAllowAllActionAndAnySelector AppendTo([GPALFile(Description = "The file to add to")] GPALFile file);
    }
    public interface IAllowAllActionAndAnySelector
        : IAllowApplicationAction
        , IAllowAnySelector
        , IAllowSelectorAction
        , IAllowWaitFor
        , IAllowWaitForWindow
    { }
    public interface IAllowAfterWaitForAndSizeControl
        : IAllowAfterAnySelector
        , IAllowSizeControl
        , IAllowCallBack
    { }
    public interface IAllowAllApplicationAndAllSelector
        : IAllowApplicationAction
        , IAllowAnySelector
        , IAllowAfterAnySelector
        , IAllowSizeControl
        , IAllowApplicationSettings
    { }
    public interface IAllowAllExceptParameters
        : IAllowApplicationAction
        , IAllowAnySelector
        , IAllowAfterAnySelector
        , IAllowSizeControl
    { }
    public interface IAllowApplicationAction
        : IAllowClose
        , IAllowTabActions
        , IAllowSizeControl
    { }
    public interface IAllowAnySelector
        : IAllowWithSelector
    { }
    public interface IAllowPersistence
    : IAllowWithPersistentSelector
    , IAllowPersistentCallBack
    { }

    #endregion <Derived Application Interfaces>
    #region <Application Atoms>
    public interface IAllowToGPALObject<TResult>
    {
        [Fluent(Description = "Ends a declaration or workflow, returning a GPAL object which can be assigned. Use instead of an explicit cast. [ Selector mySel = GPAL.Selector.WithCSS(css).ToGPALObject() ]")]
        TResult ToGPALObject();  
    }
    public interface IAllowWithPersistentSelector 
    {
        [Selector(Description = "A selector looked for on every action rather than only in this unit of work, so something that can appear at any time is handled wherever it appears")]
        IAllowPersistence WithPersistentSelector(Selector selector); 
    }
    public interface IAllowWithSelector 
    {
        [Selector(Description = "The element the next action works on")]
        IAllowSelectorActionOrAnySelector WithSelector(Selector selector); 
    }
    public interface IAllowClose 
    {
        [Action(Description = "Close the application", Submenu = "Windows")]
        void Close(); 
    }
    public interface IAllowFocus
    {
        [Action(Description = "Give the element keyboard focus", Submenu = "Mouse")] IAllowSelectorActionOrAnySelector Focus(Selector selector = null);
    }
    public interface IAllowHover
    {
        [Action(Description = "Move the pointer over the element without clicking", Submenu = "Mouse")] IAllowSelectorActionOrAnySelector Hover(Selector selector = null);
    }
    public interface IAllowWithAllThatMatch 
    {
        [Settings(Description = "How many matching elements to act on. Defaults to int.MaxValue, every one that matches")]
        IAllowAfterAnySelectorExceptWithAll WithAllThatMatch(int rowCount); 
    }
    public interface IAllowMoveTo
    {
        [Action(Description = "Move the pointer to the element", Submenu = "Mouse")] IAllowSelectorActionOrAnySelector MoveTo(Selector selector = null);
    }
    public interface IAllowGetGrid
    {
        [Action(Description = "Retrieve data and add it into this GPALGrid. Pass a grid configured with .WithDedupeData(true) to skip duplicate rows.  GPAL also keeps it internally for SaveTo*.")]
        IAllowWithHeaderOrFileActions GetGrid(ref IGPALGrid<string> returnGrid);
    }
    public interface IAllowWaitFor 
    {
        [Action(Description = "Wait for the selectors on this unit of work to appear before carrying on, in milliseconds")]
        IAllowAfterWaitForAndSizeControl WaitFor([Milliseconds] int waitForTimeInTicks); 
    }
    public interface IAllowWithHardware 
    { 
        IAllowAfterAnySelector WithHardware
        {
            [Settings(Description = "Use hardware emulation to interact with this element")] 
            get; 
        } 
    }
    public interface IAllowWithHeader 
    {
        [Settings(Description = "Column header for exported data. Use this when a selector was not given a name with .WithName, or instead of the name")]
        IAllowWithHeaderOrFileActions WithHeader(string header); 
    }
    public interface IAllowWithGridToSave
    {
        [Settings(Description = "The grid results are saved into, which could have been built by another workflow")]
        IAllowWithHeaderOrFileActions WithGridToSave(IGPALGrid<string> _);
    }
    public interface IAllowWithNextPageButton
    {
        [Data(Description = "When retrieving multiple pages of results, the control to click to load the next page.", Submenu = "Pages")]
        IAllowWithPagesAndGridActions WithNextPageButton(Selector selector);
    }
    public interface IAllowWithInfiniteScroll
    {
        IAllowWithPagesAndGridActions WithInfiniteScroll
        {
            [Data(Description = "When retrieving multiple pages of results, scroll to load more rows. Not supported for desktop applications.", Submenu = "Pages")]
            get;
        }
    }
    public interface IAllowWithPages
    {
        [Data(Description = "Number of result pages to retrieve when paging through results.", Submenu = "Pages")]
        IAllowGetGridAndFillInFrom WithPages(int numberOfPages);
    }
    public interface IAllowWithNextPageButtonOrGetGrid
        : IAllowWithNextPageButton
        , IAllowGetGrid
    { }
    public interface IAllowWithPagesAndGridActions
        : IAllowWithPages
        , IAllowGetGrid
    { }
    public interface IAllowGetGridAndFillInFrom
        : IAllowGetGrid
        , IAllowFillInFrom
    { }
    public interface IAllowSetRange
    {
        [Data(Description = "Set the value of an input slider, number and other range/numeric inputs", Submenu = "Keyboard")]
        IAllowAllActionAndAnySelector SetRange(double rangeValue);
    }

    #endregion <Application Atoms>
    public interface IApplication : IAllowAllActionAndAnySelector
        , IAllowAllApplicationAndAllSelector
        , IAllowSelectorActionOrAnySelector
        , IAllowSizeControl
        , IAllowAfterWaitForAndSizeControl
        , IAllowPersistence
        , IAllowAfterAnySelectorExceptWithAll
        , IAllowAllExceptParameters
        , IAllowApplicationOpenOrParameters
        , IAllowApplicationSettingsOrOpen
        , IAllowApplicationSettings
        , IAllowWithHeaderOrFileActions
        , IAllowWithPagesAndGridActions
        , IAllowGetGridAndFillInFrom
        , IAllowWithGridToSave
    {
        System.Diagnostics.Process Process { get; }
        object ManagedRootAutomationElement { get; }
        /// <summary>
        /// The starting AutomationElement for the application. All xpath searches start from this AutomationElement.
        /// </summary>
        AutomationElement RootAutomationElement { get; }
        [Scroll(Description = "Whether the application's scrollable view is at the bottom")]
        bool IsEndOfPage();
        /// <summary>
        /// A name you provide to be used when publishing events.<br/>
        /// Use .WithName to provide your own friendly name. If not supplied GPAL will assign the name application1, application2, etc<br/>
        /// This get is intended for debugging only.
        /// </summary>
        /// <returns>The name you supplied.</returns>
        string Name { get; }
        /// <summary>
        /// The path of the program you Opened.<br/>
        /// This get is intended for debugging only.
        /// </summary>
        /// <returns>The path you supplied.</returns>
        string ApplicationPath { get; }
        /// <summary>
        /// The hidden desktop this program runs on, or null when it is on the desktop you are looking at.
        /// </summary>
        /// <returns>The desktop, to peek at or to name.</returns>
        GenerallyPositive.Browser.IHiddenDesktop HiddenDesktop { get; }

        /// <summary>
        /// The column names the last GetGrid read, from TablePattern or the control's header row.<br/>
        /// Empty when the control had none; a saved file then uses generated names.
        /// </summary>
        /// <returns>The names in column order.</returns>
        IReadOnlyList<string> ColumnNames { get; }
    }
    #endregion <Application Interfaces>
    #region <Element Assistant Interfaces>
    public interface IAllowElementSettingsAndActions
    : IAllowElementSetting
    , IAllowElementActions
    { }
    public interface IAllowElementActions
    {
        [Action(Description = "Left click the GPALAutomationElement")]
        IAllowElementSettingsAndActions LeftClick();
        [Action(Description = "Left click the GPALAutomationElement, optionally emulating pressing a modifier key at the same time")]
        IAllowElementSettingsAndActions LeftClick(ModifierKeys modifierKeys);
        [Action(Description = "Left double click the GPALAutomationElement")]
        IAllowElementSettingsAndActions LeftDoubleClick();
        [Action(Description = "Right click the GPALAutomationElement")]
        IAllowElementSettingsAndActions RightClick();
        [Data(Description = "Fill In (Ovewrite) text of the input GPALAutomationElement defined with.WithElement")]
        IAllowElementSettingsAndActions FillInFrom(string inputText);
        [Data(Description = "Insert text at the beginning of the input GPALAutomationElement defined with.WithElement")]
        IAllowElementSettingsAndActions InsertFrom(string inputText);
        [Data(Description = "Appends text to the end of the input GPALAutomationElement defined with.WithElement")]
        IAllowElementSettingsAndActions AppendFrom(string inputText);
        [Action(Description = "Emulate pressing a special key like Home, End, Page Down")]
        IAllowElementSettingsAndActions SendKeyCode(byte keyCode);
        [Data(Description = "Press a modifier key: Alt, Control, Shift")]
        IAllowElementSettingsAndActions PressModifierKey(ModifierKeys modifierKeys);
        [Data(Description = "Release a modifier key: Alt, Control, Shift")]
        IAllowElementSettingsAndActions ReleaseModifierKey(ModifierKeys modifierKeys);
        [Scroll(Description = "Scroll the GPALAutomationElement horizontally by an increment amount")]
        IAllowElementSettingsAndActions ScrollHorizontal(System.Windows.Automation.ScrollAmount scrollAmount);
        [Scroll(Description = "Scroll the GPALAutomationElement horizontally to the given percentage of its scrollable range")]
        IAllowElementSettingsAndActions ScrollHorizontalPercent([Percent] int percent);
        [Scroll(Description = "Scroll the GPALAutomationElement vertically by an increment amount")]
        IAllowElementSettingsAndActions ScrollVertical(System.Windows.Automation.ScrollAmount scrollAmount);
        [Scroll(Description = "Scroll the GPALAutomationElement vertically to the given percentage of its scrollable range")]
        IAllowElementSettingsAndActions ScrollVerticalPercent([Percent] int percent);
    }
    public interface IAllowElementSetting
    {
        [ElementAssitant(Description = "Define the GPALAutomationElement to act upon")]
        IAllowElementSettingsAndActions WithElement(IGPALAutomationElement Element);
        IAllowElementSettingsAndActions WithHardware { [Settings(Description = "Interact with the GPALAutomationElement emulating hardware, not using UIAutomation;")] get; }
    }
    public interface IAllowForSelector { [Settings(Description = "The selector associated with the webelement(s) you are acting upon")] IAllowElementSetting ForSelector(Selector selector); };
    #endregion <Element Assistant Interfaces>


}

