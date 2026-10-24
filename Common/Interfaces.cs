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

using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using MimeKit;
using OpenQA.Selenium;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.DataVisualization.Charting;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPAL;
using static GenerallyPositive.GPALForm.GPALForm;
using static GenerallyPositive.GPALSettings;
using static GenerallyPositive.RESTClient;

namespace GenerallyPositive
{
    #region <Selector Settings>
    [Interface(Description = "Selector Settings")]
    public interface IAllowSelectorSettings : IAllowCallBack, IAllowToGPALObject<Selector>
    {
        //// post-v1: OCR
        //[Selector(Description = "Locate element with OCR using the text to find")]
        //IAllowSelectorSettings WithOCR(string textToFind);
        [Selector(Description = "Locate element using a CSS path (browser only)")]
        IAllowSelectorSettings WithCSS([Css] string CSS);
        [Selector(Description = "Locate element matching a partial or full HRef (browser only)")]
        IAllowSelectorSettings WithHRef(string HRefFullOrPartial);
        [Selector(Description = "Locate element using an XPath")]
        IAllowSelectorSettings WithXPath([XPath] string xpath);
        [Selector(Description = "Locate element searching visible text to find")]
        IAllowSelectorSettings WithText(string textToFind);
        [Selector(Description = "Locate element with the given value")]
        IAllowSelectorSettings WithValue(string valueToFind);
        [Selector(Description = "Locate element with the given name")]
        IAllowSelectorSettings WithName(string nameToFind);
        [Selector(Description = "Locate element with the given class name")]
        IAllowSelectorSettings WithClassName(string nameToFind);
        [Selector(Description = "Locate element with the given placeholder (browser only)")]
        IAllowSelectorSettings WithPlaceholder(string placeholderToFind);
        [Selector(Description = "Locate element using an AutomationID (application only)")]
        IAllowSelectorSettings WithAutomationID(string nameToFind);
        [Selector(Description = "Locate element loading the given image file to match")]
        IAllowSelectorSettings WithImage(GPALFile imageToFindPath);
        [Selector(Description = "Locate element loading the given image file to match")]
        IAllowSelectorSettings WithImage([Image] string dataURL);
        [Selector(Description = "Locate element using the supplied image to match")]
        IAllowSelectorSettings WithImage(Image imageToFine);
        [Settings(Description = "Name of selector (used in messages and column names)")]
        IAllowSelectorSettings WithSelectorName(string name);
        [Settings(Description = "X offset to click inside element, also starting X location in element for drag-and-drop")]
        IAllowSelectorSettings WithOffsetX([Pixels] int offsetX);
        [Settings(Description = "Y offset to click inside element, also starting X location in element for drag-and-drop")]
        IAllowSelectorSettings WithOffsetY([Pixels] int offsetY);
        [Settings(Description = "X delta for drag-and-drop")]
        IAllowSelectorSettings WithDeltaX([Pixels] int offsetX);
        [Settings(Description = "Y delta for drag-and-drop")]
        IAllowSelectorSettings WithDeltaY([Pixels] int offsetY);
        [Settings(Description = "Scroll down a page to see if the selector comes into view [image/ocr]")]
        IAllowSelectorSettings WithSearchForSelector(bool trueFalse = true);
        IAllowSelectorSettings WithHardware { [Settings(Description = "Use hardware emulation to interact with this element [mouse/keyboard]")] get; }
        IAllowSelectorSettings WithJavaScript { [Settings(Description = "Use javascript injection to interact with this element")] get; }
        IAllowSelectorSettings WithSelenium { [Settings(Description = "Use selenium to interact with this element [default]")] get; }
        [Settings(Description = "Smoothly move mouse to interact with this element [instead of jump to]. Implies .WithHardware")]
        IAllowSelectorSettings WithSimulateMouse(bool moveWithArc = false);
        [Settings(Description = "Stop the workflow if this selector is not found")]
        IAllowSelectorSettings WithStopOnNotFound(bool trueFalse = false);
        [Data(Description = "When retrieving data from the element, get data from the named attribute. [visible text is default]")]
        IAllowSelectorSettings UseAttribute(string attributeName);
        [Data(Description = "Located elements will match only if the text contains the supplied string")]

        IAllowSelectorSettings ContainsAttribute(string text);
        [Data(Description = "Located elements will match only if they have this named attribute")]
        IAllowSelectorSettings MatchAttribute(string href);
        [Data(Description = "Located elements will match only if the attribute name matches this regular expression")]
        IAllowSelectorSettings MatchAttributeRegex([Regex] string href);
        [Data(Description = "Located elements will match only if the text contains the supplied string")]

        IAllowSelectorSettings ContainsHRef(string text);
        [Data(Description = "Located elements will match only if they have this href")]
        IAllowSelectorSettings MatchHRef(string href);
        [Data(Description = "Located elements will match only if the href matches this regular expression")]
        IAllowSelectorSettings MatchHRefRegex([Regex] string href);
        [Data(Description = "Located elements will match only if the text contains the supplied string")]

        IAllowSelectorSettings ContainsPlaceholder(string text);
        [Data(Description = "Located elements will match only if the placeholder value matches the supplied string")]
        IAllowSelectorSettings MatchPlaceholder(string placeholder);
        [Data(Description = "Located elements will match only if the placebolder value matches this regular expression")]
        IAllowSelectorSettings MatchPlaceholderRegex(string placeholder);
        [Data(Description = "Located elements will match only if the text contains the supplied string")]

        IAllowSelectorSettings ContainsSrc(string text);
        [Data(Description = "Located elements will match only if the src matches the supplied string")]
        IAllowSelectorSettings MatchSrc(string src);
        [Data(Description = "Located elements will match only if the src matched the regular expression")]
        IAllowSelectorSettings MatchSrcRegex(string src);
        [Data(Description = "Located elements will match only if the text contains the supplied string")]

        IAllowSelectorSettings ContainsText(string text);
        [Data(Description = "Located elements will match only if the text matches the regular expression")]
        IAllowSelectorSettings MatchText(string text);
        [Data(Description = "Located elements will match only if the text matches the regular expression")]
        IAllowSelectorSettings MatchTextRegex(string text);

        [Data(Description = "Located elements will match only if the text contains the supplied string")]
        IAllowSelectorSettings ContainsValue(string text);
        [Data(Description = "Located elements will match only if the value matches the supplied string")]
        IAllowSelectorSettings MatchValue(string value);
        [Data(Description = "Located elements will match only if the value matches the regular expression")]
        IAllowSelectorSettings MatchValueRegex(string value);
        [Data(Description = "Custom Application UIAutomation Element matching method to examine and 'match' located elements using your algorithm.")]
        IAllowSelectorSettings MatchFunction(Selector.appMatchingFunction AppMatchingFunction);
        [Data(Description = "Custom browser webElement matching method to examine and 'match' located elements using your algorithm")]
        IAllowSelectorSettings MatchFunction(Selector.webMatchingFunction WebMatchingFunction);
    }
    public interface IAllowCallBack
    {
        // return values
        // 0 = not handled
        // 1 = handled
        // -1 = error
        [Callback(Description = "Selector based callback if this selector is found")]
        IAllowSelectorSettings CallIfFound(Browser.Browser.CallIfDelegate CallIfFound);
        [Callback(Description = "Selector based callback if this selector is NOT found")]
        IAllowSelectorSettings CallIfNotFound(Browser.Browser.CallIfDelegate CallIfNotFound);
        // TODO: might we want to have an element called directly after it is filled in rather than the whole row? NOT YET
        // IAllowAfterAnySelector CallAfterFillIn(Browser.Browser.CallAfterFillInDelegate CallAfterFillIn);
    }

    #endregion <Selector Settings>
    #region <GPAL Interfaces>
    [Interface(Description = "Global Settings")]
    public interface IAllowGPALSettings
    {
        [GPALSettings(Description = "Print GPAL published information or exception messages to the supplied database. One column for each GPALEventArgs property. ")]
        IAllowGPALSettings WithPublishToLogger(IGPALLogger logger);
        [GPALSettings(Description = "Stops publishing events to a logger, for a workflow that logged a stretch of work to its own place and is done with it")]
        IAllowGPALSettings RemoveLogger(IGPALLogger logger);
        [GPALSettings(Description = "Print GPAL published information or exception messages to the output window in VisualStudio. [true (default)]")]
        IAllowGPALSettings WithPublishToDebug(GPALEventType eventTypes = GPALEventType.INFO | GPALEventType.WARNING | GPALEventType.ERROR | GPALEventType.EXCEPTION | GPALEventType.NOTICE | GPALEventType.CAUTION | GPALEventType.FAILURE);
        [GPALSettings(Description = "Indicate which published events to print to the console. [None (default)]")]
        IAllowGPALSettings WithPublishToConsole(GPALEventType eventTypes = GPALEventType.INFO | GPALEventType.WARNING | GPALEventType.ERROR | GPALEventType.EXCEPTION | GPALEventType.NOTICE | GPALEventType.CAUTION | GPALEventType.FAILURE);
        [Settings(Description = "Write specified GPAL events to a text area on a form. [none (default)]")]
        IAllowGPALSettings WithPublishToControl(GPALTextArea control);
        [Settings(Description = "Which GPAL events reach an attached handler. [all (default)]")]
        IAllowGPALSettings WithHandlerEvents(GPALEventType eventTypes);
        [Settings(Description = "Which GPAL events reach the information handler. [all (default)]")]
        IAllowGPALSettings WithInformationHandlerEvents(GPALEventType eventTypes);
        [Settings(Description = "Which GPAL events reach the exception handler. [all (default)]")]
        IAllowGPALSettings WithExceptionHandlerEvents(GPALEventType eventTypes);
        [Settings(Description = "Which GPAL events reach the form control. [all (default)]")]
        IAllowGPALSettings WithControlEvents(GPALEventType eventTypes);
        [GPALSettings(Description = "Print stack track when GPAL prints it's own published messages (PublishToConsole/PublishToDebug == true). [false (default)]")]
        IAllowGPALSettings WithPublishStackTrace(bool trueFalse = true);
        [GPALSettings(Description = "Callback for failures a workflow can decide about, used by any browser without its own CallOnFail")]
        IAllowGPALSettings CallOnFail(Browser.Browser.CallOnFailDelegate callOnFail);
        [GPALSettings(Description = "Intercharacter typing delay [0ms (default), no pacing]")]
        IAllowGPALSettings WithTypingDelay(int delayInTicks = 0);
        [GPALSettings(Description = "Handler to receive GPAL Exception events")]
        IAllowGPALSettings WithExceptionHandler(EventHandler<GPALEventArgs> ExceptionEventHandler);
        [GPALSettings(Description = "Handler to receive GPAL Information events")]
        IAllowGPALSettings WithInformationHandler(EventHandler<GPALEventArgs> InformationEventHandler);
        [GPALSettings(Description = "Image matching similarity percentage. [80 (default)]")]
        IAllowGPALSettings WithImageMatchingPercentage(int matchPercent = 80);
        [GPALSettings(Description = "How many nodes a YAML payload may hold before GPAL stops writing aliases for shared references. Above it a shared reference is written out again instead of as an alias, which is what keeps a large flat collection from stalling. A graph that holds a cycle always keeps its aliases, because YAML cannot write a cycle without them. 0 never stops")]
        IAllowGPALSettings WithYamlAliasLimit(int nodeLimit = 5000);
        [GPALSettings(Description = "Simulate moving the mouse for every action (implies .WithHardware). [false (default)]")]
        IAllowGPALSettings WithSimulateMouseMovement(bool trueFalse = true);
        [GPALSettings(Description = "Stop workflow if any selector is not found")]
        IAllowGPALSettings WithStopOnNotFound(bool trueFalse = true);
        [GPALSettings(Description = "Global location for browser drivers. [./ (default)(same dir as exe)]")]
        IAllowGPALSettings WithDriverLocation([Directory(Description = "Global location for browser drivers. [./ (default)(same dir as exe)]")] string driverPath = "./");
        [GPALSettings(Description = "The folder GPAL writes its own throwaway files to")]
        IAllowGPALSettings WithTempDirectory([Directory(Description = "Where GPAL writes files it created itself and nobody asked to keep. [GPALfilesSafeToDelete beside the exe (default)]")] string tempPath);
        [GPALSettings(Description = "Set the default location to store temporary profiles if WithDefaultDataDir is not used. default [C:\\profiles]. Also saved in GPAL.yaml that loads on startup.")]
        IAllowGPALSettings WithTempProfileParentDirectory([Directory(Description = "Global location for temp profiles. [c:\\profiles (default)(saved in GPAL.yaml that is loaded on startup)]")] string tempProfileDirectory);
        [GPALSettings(Description = "Substitue string when data cannot be retrieved for an element [##UNABLE TO GET## (default)]")]
        IAllowGPALSettings WithDefaultErrorPlaceholder(string message = "##UNABLE TO GET##");
        [GPALSettings(Description = "Automatically update the Chrome webdriver when running a GPAL application. Manually deploy first Chrome driver, tho.")]
        IAllowGPALSettings WithAutomationEngine(AutomationEngine automationEngine = AutomationEngine.PuppeteerPort);
        [GPALSettings(Description = "Automatically update the Chrome webdriver when running a GPAL application. Manually deploy first Chrome driver, tho.")]
        IAllowGPALSettings WithAutoUpdateWebDriver(bool trueFalse = true);
        [GPALSettings(Description = "Do not try alternate methods to interact with an element if an error occurs.")]
        IAllowGPALSettings WithNoFallbackActions(bool trueFalse = true);
        [GPALSettings(Description = "Ticks to wait for element to show up before continuing. Applies to every element in every workflow. Override in a Unit of Work with .WaitFor().")]
        IAllowGPALSettings WithWaitFor([Milliseconds] int waitForInTicks);
        [GPALSettings(Description = "Global CallIfFound handler for all selectors in any Unit of Work")]
        IAllowGPALSettings CallIfFound(Browser.Browser.CallIfDelegate CallIfFound);
        [GPALSettings(Description = "Global CallIfFound handler for all selectors in any Unit of Work")]
        IAllowGPALSettings CallIfNotFound(Browser.Browser.CallIfDelegate CallIfNotFound);
        [GPALSettings(Description = "Global CallIfFound handler for all application selectors in any Unit of Work")]
        IAllowGPALSettings AppCallIfFound(Application.Application.CallIfDelegate CallIfFound);
        [GPALSettings(Description = "Global CallIfFound handler for all application selectors in any Unit of Work")]
        IAllowGPALSettings AppCallIfNotFound(Application.Application.CallIfDelegate CallIfNotFound);
    }
    #endregion <GPAL Interfaces>
    #region <Clipboard Interfaces>
    public interface IGPALClipboard : IAllowClipboardInput, IAllowClipboardSet, IAllowClipboardInto
        , IAllowToGPALObject<IGPALClipboard>
    { }
    public interface IAllowClipboardInput
    {
        [Settings(Description = "Text to put on the clipboard")]
        IAllowClipboardSet WithInput(string text);
        [Settings(Description = "Grid to put on the clipboard, rows as lines and cells separated by tabs")]
        IAllowClipboardSet WithInput(IGPALGrid<string> grid);
        [Settings(Description = "File whose text goes on the clipboard, or the file itself for a FileList")]
        IAllowClipboardSet WithInput(GPALFile file);
        [Action(Description = "Read the clipboard. Text when no type is given.")]
        IAllowClipboardInto GetClipboard(ClipboardType clipboardType = ClipboardType.Text);
        [Action(Description = "Empty the clipboard")]
        IGPALClipboard Clear();
    }
    public interface IAllowClipboardSet
    {
        [Action(Description = "Put what WithInput named on the clipboard. Text when no type is given.")]
        IGPALClipboard SetClipboard(ClipboardType clipboardType = ClipboardType.Text);
    }
    public interface IAllowClipboardInto
    {
        [Action(Description = "What is on the clipboard, as text")]
        IGPALClipboard SaveTo(out string text);
        [Action(Description = "What is on the clipboard, split into a grid")]
        IGPALClipboard SaveTo(out IGPALGrid<string> grid);
        [Action(Description = "Writes what is on the clipboard to a file")]
        IGPALClipboard SaveTo(GPALFile file);
    }
    #endregion <Clipboard Interfaces>
    #region <Database Interfaces>
    public interface IGPALDatabase : IAllowDatabaseSettings, IAllowParameterLength, IAllowPrecsionScaleAndSettings, IAllowParametersAndActions
        , IAllowParametersAndUsing, IAllowParametersAndInto, IAllowDatabaseExecute, IAllowToGPALObject<IGPALDatabase>, System.IDisposable
    {
        IGPALGrid<string> Tokens { [Data(Description = "The rows/columns returned from the database")] get; }
    }
    public interface IAllowParameterLength { [Settings(Description = "The length of the last parameter named", Submenu = "Parameters")] IAllowDatabaseSettings WithParameterLength(int columnLength); };
    public interface IAllowColumnPrecision { [Settings(Description = "The precision of the last parameter named", Submenu = "Parameters")] IAllowPrecsionScaleAndSettings WithParameterPrecision(int columnPrecision); };
    public interface IAllowColumnScale { [Settings(Description = "The scale of the last parameter named", Submenu = "Parameters")] IAllowPrecsionScaleAndSettings WithParameterScale(int columnScale); };
    public interface IAllowPrecsionScaleAndSettings : IAllowDatabaseSettings
        , IAllowColumnPrecision
        , IAllowColumnScale
    { }
    public interface IAllowDatabaseExecute
    {
        [Fluent(Description = "Per-statement breakdown (via SqlCommand.StatementCompleted) instead of the single aggregate count")]
        IAllowParametersAndActions Execute(out int rowCount);
        /// <summary>Per-statement breakdown (via SqlCommand.StatementCompleted) instead of the single aggregate count.</summary>
        [Fluent(Description = "Per-statement breakdown (via SqlCommand.StatementCompleted) instead of the single aggregate count")]
        IAllowParametersAndActions Execute(out List<int> rowCountsPerStatement);
    }
    /// <summary>
    /// SaveTo(...) already ran the read (an out parameter forces that), so chaining .Execute() afterward would
    /// just re-run the same command as a meaningless non-query. Chain more settings instead, or .ToGPALObject()
    /// to get back to the full interface for a new Create/Read/Update/Delete.
    /// </summary>
    public interface IAllowDatabaseGridSettings : IAllowDatabaseSettings
    {
        /// <summary>
        /// Only SaveTo(out IGPALGrid&lt;string&gt;) returns this - meaningless for SaveTo(DataTable)/SaveTo(DataSet),
        /// which already separate results by table. Optional to call: tells you how many of the grid's rows belong
        /// to each result set the query returned, in order, since a flat grid otherwise can't signal where one
        /// table's rows end and the next begin.
        /// </summary>
        [Data(Description = "Only SaveTo(out IGPALGrid<string>) returns this - meaningless for SaveTo(DataTable)/SaveTo(DataSet), which already separate results by table")]
        IAllowDatabaseSettings GetRowCount(out List<int> rowCountsPerTable);
    }
    /// <summary>
    /// Database settings: the connection, the command, and its parameter definitions, in any order.<br/>
    /// Parameter values, Execute, SaveTo, Using and the verbs are not here, so a chain cannot run before setup. Call
    /// ToGPALObject() to continue as <see cref="IGPALDatabase"/>.
    /// </summary>
    public interface IAllowDatabaseSettings : IAllowToGPALObject<IGPALDatabase>
    {
        [Settings(Description = "Set the connectrion string used to connect to the database", Submenu = "Connection")]
        IAllowDatabaseSettings WithConnectionString(string connectionString);
        [Settings(Description = "How long a command may run before the provider gives up. [the provider's own default]", Submenu = "Connection")]
        IAllowDatabaseSettings WithCommandTimeout([Seconds] int timeoutInSeconds);
        [Action(Description = "Close the connection and roll back a transaction still open on it. The database can be used again after.")]
        IAllowDatabaseSettings Close();
        [Settings(Description = "Set the username used to connect to the database", Submenu = "Connection")]
        IAllowDatabaseSettings WithUsername(string userName);
        [Settings(Description = "Set the password used to connect to the database", Submenu = "Connection")]
        IAllowDatabaseSettings WithPassword([Password] string password);
        [Settings(Description = "Set the database servername", Submenu = "Connection")]
        IAllowDatabaseSettings WithServerName(string serverName);
        [Settings(Description = "Set the database instance name", Submenu = "Connection")]
        IAllowDatabaseSettings WithDatabaseName(string databaseName);
        [Settings(Description = "Set the type of database GPAL is interacting with", Submenu = "Connection")]
        IAllowDatabaseSettings WithDatabaseType(DatabaseType databaseType);
        [Settings(Description = "Set the table name to query for input", Submenu = "SQL")]
        IAllowDatabaseSettings WithTableName(string tableName);
        [Settings(Description = "The SQL command to execute for the input data grid (tokens)", Submenu = "SQL")]
        IAllowDatabaseSettings WithSQLCommand(string sqlCommand);
        [Settings(Description = "The stored procedure to execute for the input data grid (tokens)", Submenu = "SQL")]
        IAllowDatabaseSettings WithStoredProcedure(string storedProcedure);
        [Settings(Description = "Add a parameter name to the parameter names list", Submenu = "Parameters")]
        IAllowDatabaseSettings WithParameterName(string parameterName);
        [Settings(Description = "Define a DateTime column", Submenu = "Parameters")]
        IAllowDatabaseSettings WithDatetimeParameter(string parameterName);
        [Settings(Description = "Define a Time column", Submenu = "Parameters")]
        IAllowDatabaseSettings WithTimeParameter(string parameterName);
        [Settings(Description = "Define a Timestamp column", Submenu = "Parameters")]
        IAllowDatabaseSettings WithTimestampParameter(string parameterName);
        [Settings(Description = "Define a Decimal column", Submenu = "Parameters")]
        IAllowPrecsionScaleAndSettings WithDecimalParameter(string parameterName);
        [Settings(Description = "Define a Float column [Double/Real]", Submenu = "Parameters")]
        IAllowDatabaseSettings WithFloatParameter(string parameterName);
        [Settings(Description = "Define an Image column", Submenu = "Parameters")]
        IAllowDatabaseSettings WithImageParameter(string parameterName);
        [Settings(Description = "Define an Integer column", Submenu = "Parameters")]
        IAllowDatabaseSettings WithIntegerParameter(string parameterName);
        [Settings(Description = "Define a Money column", Submenu = "Parameters")]
        IAllowDatabaseSettings WithMoneyParameter(string parameterName);
        [Settings(Description = "Define a VarChar column", Submenu = "Parameters")]
        IAllowParameterLength WithVarCharParameter(string parameterName);
        [Settings(Description = "Define an NVarChar column", Submenu = "Parameters")]
        IAllowParameterLength WithNVarCharParameter(string parameterName);
        [Settings(Description = "Define a Char column", Submenu = "Parameters")]
        IAllowParameterLength WithCharParameter(string parameterName);
        [Settings(Description = "Define an NChar column", Submenu = "Parameters")]
        IAllowParameterLength WithNCharParameter(string parameterName);
        [Settings(Description = "Define a Text column", Submenu = "Parameters")]
        IAllowParameterLength WithTextParameter(string parameterName);
        [Settings(Description = "Define a NText column", Submenu = "Parameters")]
        IAllowParameterLength WithNTextParameter(string parameterName);
        [Settings(Description = "Pass a table to a stored procedure as a table-valued parameter with this name", Submenu = "Parameters")]
        IAllowDatabaseSettings WithTableParameter(string parameterName); // Structured columns
        [Settings(Description = "How many rows to read", Submenu = "SQL")]
        IAllowDatabaseSettings WithRowCount(int rowCount);
        [Settings(Description = "The SQL that Create runs, in place of the one GPAL builds", Submenu = "SQL")]
        IAllowDatabaseSettings WithCreateAs(string createSQL);
        [Settings(Description = "The SQL that Read runs, in place of the one GPAL builds", Submenu = "SQL")]
        IAllowDatabaseSettings WithReadAs(string selectSQL);
        [Settings(Description = "The SQL that Update runs, in place of the one GPAL builds", Submenu = "SQL")]
        IAllowDatabaseSettings WithUpdateAs(string updateSQL);
        [Settings(Description = "The SQL that Delete runs, in place of the one GPAL builds", Submenu = "SQL")]
        IAllowDatabaseSettings WithDeleteAs(string deleteSQL);
        [Settings(Description = "Callback for a database error the workflow can decide about", Submenu = "SQL")]
        IAllowDatabaseSettings WithErrorCallback(GPALDatabase.CallOnError CallOnError);
        [Settings(Description = "The user-defined table type of the table parameter", Submenu = "Parameters")]
        IAllowDatabaseSettings WithUDTTName(string typeName); // Structured column UDTT name
    }
    public interface IAllowDatabaseInto
    {
        [Action(Description = "Reads a single value: the first column of the first row, without building a grid around it")]
        IAllowDatabaseGridSettings SaveTo(out IGPALGrid<string> dataGrid);
        [Action(Description = "Reads a single value: the first column of the first row, without building a grid around it")]
        IAllowDatabaseSettings SaveTo(out System.Data.DataTable dataTable);
        [Action(Description = "Reads a single value: the first column of the first row, without building a grid around it")]
        IAllowDatabaseSettings SaveTo(out System.Data.DataSet dataSet);
        [Action(Description = "Reads a single value: the first column of the first row, without building a grid around it")]
        IAllowDatabaseSettings SaveTo(out string value);
    }
    public interface IAllowDatabaseUsing
    {
        [Settings(Description = "The rows to write", Submenu = "SQL")]
        IAllowDatabaseExecute Using(IGPALGrid<string> dataGrid);
        [Settings(Description = "The rows to write", Submenu = "SQL")]
        IAllowDatabaseExecute Using(System.Data.DataTable dataTable);
    }
    public interface IAllowParameters
    {
        IAllowParameters ClearParameters { [Action(Description = "If you have already defined parameter objectss on a GPAL.Database, you can clear them and add new parameter objects;")] get; }
        [Settings(Description = "Add a parameter to the parameters list", Submenu = "Parameters")]
        IAllowParametersAndActions WithParameter(string parameter);
        [Settings(Description = "Add a parameter to the parameters list", Submenu = "Parameters")]
        IAllowParametersAndActions WithParameterGrid(IGPALGrid<string> parameter);
    }
    public interface IAllowDatabaseActions : IAllowDatabaseExecute
    {
        IAllowParametersAndUsing Create { [Action(Description = "Insert a row", Submenu = "Rows")] get; }
        IAllowParametersAndInto Read { [Action(Description = "Read rows", Submenu = "Rows")] get; }
        IAllowParametersAndUsing Update { [Action(Description = "Update rows", Submenu = "Rows")] get; }
        IAllowParametersAndUsing Delete { [Action(Description = "Delete rows", Submenu = "Rows")] get; }
        IAllowParametersAndActions BeginTransaction { [Action(Description = "Start a transaction", Submenu = "Transactions")] get; }
        IAllowDatabaseActions Rollback { [Action(Description = "Undo the transaction", Submenu = "Transactions")] get; }
        IAllowDatabaseActions Commit { [Action(Description = "Commit the transaction", Submenu = "Transactions")] get; }

    }
    public interface IAllowParametersAndActions
        : IAllowParameters
        , IAllowDatabaseActions
    { }
    public interface IAllowParametersAndUsing
        : IAllowParameters
        , IAllowDatabaseUsing
    { }
    public interface IAllowParametersAndInto
        : IAllowParameters
        , IAllowDatabaseInto
    { }

    #endregion <DatabaseInterfaces>
    #region Credentials
    /// <summary>
    /// The ways to supply credentials after WithService: a vault takes a username and password, a static key the key,
    /// an OAuth client an id and secret, a service account its key file.
    /// </summary>
    public interface IAllowCredentialMethod : IAllowUsernameOrKey, IAllowClientId
    {
        [Settings(Description = "The login for the credential service itself, built once and handed to every request that queries it")]
        IAllowCredentialSettings WithMasterCredentials(ICredentials masterCredentials);
        [Settings(Description = "The secret a site gives out when two-factor sign-in is turned on: the base32 key, or the otpauth:// URI in the setup QR code")]
        IAllowCredentialSettings WithOtpSecret([Password] string otpSecret);
        [Settings(Description = "Reads the one-time password secret from a picture of a site's setup QR code or a Google Authenticator export (needs GPAL.Image)")]
        IAllowCredentialSettings WithOtpQRCode(GPALFile qrCodeImage);
        [Settings(Description = "Reads the one-time password secret from a QR code the browser is showing (needs GPAL.Image)")]
        IAllowCredentialSettings WithOtpQRCode(GenerallyPositive.Browser.IBrowser browser);
    }

    public interface IAllowCredentialServiceType
    {
        [Settings(Description = "Selects which password manager or cloud identity service this credentials request targets")]
        IAllowCredentialMethod WithService(CredentialServiceType serviceType);
    }

    public interface IAllowUsernameOrKey : IAllowToGPALObject<ICredentials>
    {
        [Settings(Description = "Sets the username, either as a credential to retrieve directly (with CredentialServiceType.None) or as part of WithCredentials' master login")]
        IAllowCredentialsPassword WithUsername(string username);

        // Alternative to WithUsername/WithPassword/etc: supply the login (master) credentials for the
        // credential service itself (e.g. your Bitwarden/LastPass account), previously built with
        // GPAL.CredentialsFor(CredentialServiceType.None).WithUsername(...).WithPassword(...).

        // Static API key - supply the key directly, from an env variable, or from an internal REST endpoint:
        // GPAL.CredentialsFor(CredentialServiceType.StaticKey).WithServiceKey("sk-ant-...").ToGPALObject()
        [Settings(Description = "Service key is a custom string consisting of your ClientID:ClientSecret")]
        IAllowCredentialSettings WithServiceKey(string key);

        // Like WithServiceKey, but reads the key from an environment variable instead of a literal string:
        // GPAL.CredentialsFor(CredentialServiceType.StaticKey).WithKeyFromEnv("ANTKEY").ToGPALObject()
        [Settings(Description = "Like WithServiceKey, but reads the key from an environment variable (checking process, user, then machine scope) instead of taking it as a literal string")]
        IAllowCredentialSettings WithKeyFromEnv(string envVariableName);

        // Like WithServiceKey, but fetches the key via a GET request made with an already-configured RESTClient.
        // Must be followed by .WithEndpoint(endpoint) to specify which endpoint returns the key:
        // GPAL.CredentialsFor(CredentialServiceType.StaticKey).WithKeyFromApi(restClient).WithEndpoint("ANTKEY").ToGPALObject()
        [Settings(Description = "Like WithServiceKey, but fetches the key via a GET request made with the supplied, already-configured RESTClient")]
        IAllowKeyFromApiEndpoint WithKeyFromApi(IRESTClient restClient);
    }

    public interface IAllowKeyFromApiEndpoint
    {
        [Settings(Description = "Call this endpoint on the RESTClient given to WithKeyFromApi and use the answer as the service key")]
        IAllowCredentialSettings WithEndpoint(string endpoint);
    }

    public interface IAllowCredentialsPassword : IAllowToGPALObject<ICredentials>
    {
        [Settings(Description = "Sets the password, either as a credential to retrieve directly (with CredentialServiceType.None) or as part of WithCredentials' master login")]
        IAllowOAuthClient WithPassword([Password] string password);
    }
    public interface IAllowClientId
    {
        [Settings(Description = "Sets the OAuth client ID used to authenticate to ServiceType")]
        IAllowClientSecret WithClientId(string clientId);
        [Settings(Description = "Loads a Google Cloud service account key from a JSON key file, used to fetch an access token without an interactive OAuth flow")]
        IAllowCredentialSettings WithServiceAccountKey(GPALFile serviceAccountKeyFile);
        [Settings(Description = "Supplies a Google Cloud service account key as a raw JSON string, used to fetch an access token without an interactive OAuth flow")]
        IAllowCredentialSettings WithServiceAccountKey(string serviceAccountKey);
        [Settings(Description = "Supplies a Google Cloud service account key as a deserialized object, used to fetch an access token without an interactive OAuth flow")]
        IAllowCredentialSettings WithServiceAccountKey(GoogleServiceAccountKey serviceAccountKey);
    }
    public interface IAllowClientSecret
    {
        [Settings(Description = "Sets the OAuth client secret and derives ServiceKey as 'ClientId:ClientSecret'")]
        IAllowCredentialSettings WithClientSecret(string clientSecret);
    }
    public interface IAllowOAuthClient : IAllowClientId, IAllowToGPALObject<ICredentials>
    {
        [Settings(Description = "How the site expects to be told who you are, which decides what a browser given this credential does with it")]
        IAllowOAuthClient WithWebAuth(WebAuthType webAuthType);
        [Settings(Description = "A token the workflow already holds, presented according to the WebAuthType")]
        IAllowOAuthClient WithAccessToken(string accessToken);
        [Settings(Description = "Where the OAuth redirect lands, which has to be the URI registered with the provider")]
        IAllowOAuthClient WithAuthRedirectUrl([URL] string redirectUrl);
    }

    public interface IAllowCredentialSettings : IAllowGetCredentials, IAllowToGPALObject<ICredentials>
    {
        [Data(Description = "Find the vault item matching this search term, such as a site name or url")]
        IAllowCredentialSettings GetCredentialsFor(string target);
        [Settings(Description = "Restricts GetCredentialsFor/SaveTo results to vault items whose URL contains this domain")]
        IAllowCredentialSettings WithDomain(string domain);
        [Action(Description = "Looks up the credential matching.GetCredentialsFor's target (filtered by.WithDomain if set) and appends a [Username, Password] row to the given grid")]
        IAllowGetCredentials SaveTo(IGPALGrid<string> grid);
        [Action(Description = "Appends an [Issuer, Account, otpauth URI] row to the grid for each account the one-time password secret holds")]
        IAllowGetCredentials SaveOtpSecretsTo(IGPALGrid<string> grid);
        [Settings(Description = "Supplies a previously-obtained OAuth authorization code, skipping the interactive authorization step")]
        IAllowCredentialSettings WithAuthCode(string authCode);
        [Settings(Description = "Supplies a previously-obtained OAuth refresh token, used to fetch a new access token without re-authorizing")]
        IAllowCredentialSettings WithRefreshToken(string refreshToken);
        [Settings(Description = "Sets the OAuth scope to request when an access token is fetched")]
        IAllowCredentialSettings WithScope(OAuthScope scope);
    }

    public interface IAllowGetCredentials
    {
        // No methods, placeholder for Into completion
        [Action(Description = "Fetches a new access token for ServiceType using the credentials/configuration supplied so far, and stores it in AccessToken/IssueTime/ExpiresIn")]
        void FetchAccessToken(out string accessToken);
        [Action(Description = "Makes the one-time code an authenticator app would show right now for the secret set with WithOtpSecret")]
        void GetOtpCode(out string code);
    }

    public interface ICredentials : IAllowCredentialServiceType, IAllowCredentialMethod, IAllowClientSecret, IAllowUsernameOrKey, IAllowCredentialsPassword, IAllowOAuthClient, IAllowCredentialSettings, IAllowGetCredentials, IAllowKeyFromApiEndpoint
    {
        // Unifies all fluent interfaces
    }
    #endregion Credentials
    #region AI
    public interface IAllowAICredentials
    {
        [Settings(Description = "The API key for the AI provider")]
        IAllowAITask WithCredentials(ICredentials credentials);
        [Settings(Description = "The API key for the AI provider")]
        IAllowAITask WithCredentials(string staticApiKey);
    }
    public interface IAllowAIModel
    {
        // Accepts AIModel.ClaudeSonnet46 etc, or any freehand model ID string via implicit conversion.
        [Settings(Description = "Which model to call")]
        IAllowAITask WithModel(AIModel model);
    }
    public interface IAllowAITaskConfig
    {
        [Settings(Description = "What to ask the AI to do")]
        IAllowAIConfig WithTask(AITask task);
        [Settings(Description = "Settings for the task")]
        IAllowAIConfig WithTaskConfig(ClassificationConfig config);
        [Settings(Description = "The kind of classification")]
        IAllowAIConfig WithClassificationType(AIClassificationType type);
        [Settings(Description = "Your own categories for a custom classification")]
        IAllowAIConfig WithCustomClassification(ClassificationConfig config);
    }
    public interface IAllowAIOutput
    {
        [Settings(Description = "Where the answers go")]
        IAllowAIExecution WithOutputTo(out IGPALGrid<string> target);
        [Settings(Description = "Where the answers go")]
        IAllowAIExecution WithOutputTo(object target);
    }
    public interface IAllowAIConfig : IAllowAITaskConfig, IAllowAIOutput, IAllowToGPALObject<IGPALAI>
    {
        [Settings(Description = "What the AI works on: text, a file or a grid")]
        IAllowAIOutput WithInputFrom(object source);
        /// <summary>
        /// Like <see cref="WithInputFrom(object)"/>, but also wires the control's TextChanged event (debounced)
        /// to call Execute() again automatically, so output stays live as the user types.
        /// Must be called before the form is shown (the control's callbacks are read when the form is realized).
        /// </summary>
        IAllowAIOutput WithLiveInputFrom(GPALForm.GPALInput input, int debounceMilliseconds = 500);
        /// <summary>
        /// Like <see cref="WithInputFrom(object)"/>, but also wires the control's TextChanged event (debounced)
        /// to call Execute() again automatically, so output stays live as the user types.
        /// Must be called before the form is shown (the control's callbacks are read when the form is realized).
        /// </summary>
        IAllowAIOutput WithLiveInputFrom(GPALForm.GPALTextArea textArea, int debounceMilliseconds = 500);
    }
    public interface IAllowAIExecution : IAllowToGPALObject<IGPALAI>
    {
        [Callback(Description = "Callback for each answer the AI gives")]
        IAllowAIConfig CallAfterAIResponse(AIResponseDelegate callback);
        [Fluent(Description = "Send the task to the AI")]
        IAllowAIConfig Execute();

        // Reports "Sending request...", "Waiting for response...", "Response received", and any
        // error encountered, to the given status strip while the AI task runs.
        [Settings(Description = "A status strip to show progress in")]
        IAllowAIExecution WithStatusTo(GPALForm.GPALStatusStrip statusStrip);
    }
    public interface IAllowAITask : IAllowAICredentials, IAllowAIModel, IAllowToGPALObject<IGPALAI>
    {
        [Settings(Description = "What to ask the AI to do")]
        IAllowAIConfig WithTask(AITask task);
    }
    public interface IAllowAIProvider : IAllowToGPALObject<IGPALAI>
    {
        [Settings(Description = "Which AI provider to call")]
        IAllowAITask WithProvider(AIProviderType provider);
    }
    public interface IGPALAI : IAllowAICredentials, IAllowAIExecution, IAllowAIOutput, IAllowAIConfig, IAllowAITask, IAllowAIProvider
    {
    }
    #endregion AI
    #region <Converter Interfaces>
    public interface IGPALConverter : IAllowConverterInput, IAllowConverterSettings, IAllowConverterActions, IAllowToGPALObject<IGPALConverter>
        , IAllowConverterSettingsAndActions, IAllowConverterInputAndActions
    {
        //        internal ConverterSettings ConverterSettings { get; set; }
    }
    public interface IAllowConverterInput : IAllowToGPALObject<IGPALConverter>
    {
        [Settings(Description = "Get input data to convert using the Database object")]
        IAllowConverterSettingsAndActions WithInput(IGPALDatabase inputDatabase);
        [Settings(Description = "Input data is contained in this GPALGrid")]
        IAllowConverterSettingsAndActions WithInput(IGPALGrid<string> inputGrid);
        [Settings(Description = "Input data is in this string")]
        IAllowConverterSettingsAndActions WithInput(string inputData);
        [Settings(Description = "Input data file (extension denotes type, override using WithInputType)")]
        IAllowConverterSettingsAndActions WithInput(GPALFile inputFile);
        [Settings(Description = "Input class, uses reflection to map to output format")]
        IAllowConverterSettingsAndActions WithInput(dynamic inputClass);
        [Settings(Description = "[Global] Register a custom struct type for the converter to recognize. Structs cannot be reliably determined. If you have a custom struct to convert, register it first.")]
        IAllowConverterInput RegisterKnownStructType(Type knownStructType);
    }
    public interface IAllowConverterSettings : IAllowToGPALObject<IGPALConverter>
    {
        [Settings(Description = "Specify input data format")]
        IAllowConverterSettingsAndActions WithInputType(DataFormat inDataType);
        [Settings(Description = "Specify output data format")]
        IAllowConverterSettingsAndActions WithOutputType(DataFormat outDataType);
        [Settings(Description = "[Global] First line is a column name list (for flat files: csv, txt, prn)")]
        IAllowConverterSettingsAndActions WithFirstLineHasColumnNames(bool firstLineHeaders = true);
        [Settings(Description = "[Global] Ignore first line column name list (for flat files: csv, txt, prn)")]
        IAllowConverterSettingsAndActions WithIgnoreFirstLineColumnNames(bool ignoreFirstLine = true);
        [Settings(Description = "[Global] Each field is contained in quotes (for flat files: csv, txt, prn)")]
        IAllowConverterSettingsAndActions WithColumnsEnclosedInQuotes(bool fieldsEnclosedInQuote = true);
        [Settings(Description = "Column names for this conversion. Overrides the input source's own columns positionally; unsupplied columns are backfilled from the source.")]
        IAllowConverterSettingsAndActions WithColumnNames(List<string> columnNames);
        [Settings(Description = "Append a single column name for this conversion. Repeated calls build the list in order.")]
        IAllowConverterSettingsAndActions WithColumnName(string parameterName);
    }
    public interface IAllowConverterActions
    {
        [Action(Description = "Write the converted data into this database")]
        IAllowConverterInputAndActions SaveTo(IGPALDatabase outputDatabase);
        [Action(Description = "Write the converted data into this grid")]
        IAllowConverterInputAndActions SaveTo(ref IGPALGrid<string> outputGrid);
        [Action(Description = "Add the converted data to the end of this file, keeping what is already there")]
        IAllowConverterInputAndActions AppendTo(GPALFile outputFile);
        [Action(Description = "Write the converted data to this file, replacing what is there. The output format comes from the file's extension")]
        IAllowConverterInputAndActions SaveTo(GPALFile outputFile);
        [Action(Description = "Fill this object's properties from the converted data")]
        IAllowConverterInputAndActions SaveTo<T>(ref T outputClass);
        // RenderTo is not built. Commented out until implemented.
        //[Action(Description = "")]
        //IAllowConverterInputAndActions RenderTo(IGPALDatabase outputDatabase);
        //[Action(Description = "")]
        //IAllowConverterInputAndActions RenderTo(string outputData);
        //[Action(Description = "")]
        //IAllowConverterInputAndActions RenderTo(GPALFile outputFile);
        //[Action(Description = "")]
        //IAllowConverterInputAndActions RenderToClass(out dynamic outputClass);
        [Action(Description ="Pretty print the input to a new string.")]
        IAllowConverterInputAndActions PrettyPrintTo(out string outputData);
    }
    public interface IAllowBase64Save
    {
        [Action("Save to a string the decoded Base64 content")]
        IAllowBase64SaveOrInput SaveTo(string base64Output);
        [Action("Save to a file based upon content type")]
        IAllowBase64SaveOrInput SaveTo(GPALFile base64OutputFile);
    }

    public interface IAllowBase64SaveOrInput : IAllowBase64Save
        , IAllowConverterInput
    { }
    public interface IAllowConverterSettingsAndActions : IAllowConverterActions
        , IAllowConverterSettings
    { }
    public interface IAllowConverterInputAndActions : IAllowConverterActions
        , IAllowConverterInput
    { }

    #endregion <Converter Interfaces>
    #region <File Interfaces>
    public interface IGPALFile : IAllowFileSettings, IAllowFileActions, IAllowFileName
    {
        string First { get; }
        string Last { get; }
        GPALFile Next { get; }
        string Filename { get; }
        List<string> Filenames { get; }
        string DirectoryPart { get; }
        string FilePart { get; }
        List<string> DirectoryParts { get; }
        List<string> FileParts { get; }
        List<string> ReturnFilenames { get; }
        string SourceUrl { get; }
        int Count { get; }

    }
    internal interface IGPALFileInternal
    {
        internal GPALFileSettings FileSettings { get; set; }
        internal List<IGPALGrid<string>> TokenList { get; set; }
        internal List<string> FileData { get; set; }
        internal List<char?> Delimiter { get; set; }
        internal List<bool> FieldsEnclosedInQuotes { get; set; }
        internal List<bool> FirstLineIsColumnNames { get; set; }
        internal List<bool> IgnoreFirstLineColumnNames { get; set; }
        internal bool UseOneHeaderForAllFiles { get; set; }
        internal List<Dictionary<object, dynamic>> FileDictionary { get; set; }
        internal List<IGPALGrid<string>> FileGrid { get; set; }
        internal bool AlreadyTokenized { get; set; }
    }
    public interface IAllowFileSettings : IAllowFileName, IAllowToGPALObject<GPALFile>
    {
        [Settings(Description = "IF this file is delimited, the delimiter used.")]
        IAllowFileSettings WithDelimiter(char delimiter);
        [Settings(Description = "Add a column name to the column name list. (If no header in file or to override.)")]
        IAllowFileSettings WithColumnName(string parameterName);
        [Settings(Description = "Add column names to the column name list using the .WithDelimiter. Use this to add headers for multiple files.")]
        IAllowFileSettings WithColumnNames(string columnNames);
        [Settings(Description = "Add column names to the column name list using the supplied array. One column per element. Use this to add headers for multiple files.")]
        IAllowFileSettings WithColumnNames(string[] columnNames);
        [Settings(Description = "Each field is contained in quotes (for flat files: csv, txt, prn)")]
        IAllowFileSettings WithColumnsEnclosedInQuotes(bool fieldsEnclosedInQuote = true);
        [Settings(Description = "First line is a column name list (for flat files: csv, txt, prn)")]
        IAllowFileSettings WithFirstLineIsColumnNames(bool firstLineIsHeader = true);
        [Settings(Description = "Ignore first line header list")]
        IAllowFileSettings WithIgnoreFirstLineColumnNames(bool ignoreFirstLine = true);
        [Settings(Description = "True replaces the file; false, or not set, gives the next free filename. Used by Next and CopyTo.")]
        IAllowFileSettings WithOverwriteFile(bool overwriteFile);
        [Settings(Description = "One column list is defined, use it for all files in the file list. Without it each file takes the column list given for it, in file order.")]
        IAllowFileSettings WithUseSameColumnNamesForAllFiles(bool useHeaderForAllFiles = true);
        [Settings(Description = "Order in which files matched by a wildcard (e.g. *.csv) are added to the file list. Default is Name (alphabetical).")]
        IAllowFileSettings WithFileSortOrder(Enums.FileSortOrder fileSortOrder);
        [Settings(Description = "How Next builds a new unique filename (timestamp, date, or an incrementing counter). Default is CounterPadded.")]
        IAllowFileSettings WithNextFilePattern(Enums.NextFilePattern nextFilePattern = Enums.NextFilePattern.CounterPadded);
    }
    public interface IAllowFileActions
    {
        [Action(Description = "Copy the defined GPALFile(s) to a destination file or directory")]
        void CopyTo(string destination);
        [Action(Description = "Move the defined GPALFile(s) to a destination file or directory")]
        void MoveTo(string destination);
        [Action(Description = "Delete the destination file or files (wildcard) or all in the directory (no filename)")]
        void Delete(string destination);
        [Action(Description = "Delete the defined GPALFile underlying file(s)")]
        void Delete();
    }
    public interface IAllowFileName : IAllowToGPALObject<GPALFile>
    {
        [Settings(Description = "Add a filename to the GPALFile list. Actual output filenames added to end.")]
        IAllowFileSettings WithFileName([FilePath] string filename);   // a wildcard pattern expands to the matching files; an http/https url is fetched to a local file and used from there
        [Settings(Description = "A browser already holding the session a url needs. Set before .WithFileName. The file is fetched inside the page, or downloaded in a new tab if the page cannot return it.")]
        IAllowFileSettings WithBrowser(Browser.IBrowser browser);
    }
    #endregion <File Interfaces>
    #region Grid
    public interface IGPALGrid<T> : IAllowGridActions<T>, IEnumerable<List<T>> // , ICloneable
    {
        [Data(Description = "The rows as a list")]
        List<T> ToList();
        [Settings(Description = "Enable duplicate-row detection for this grid when it is passed into GetGrid (off by default).")]
        IGPALGrid<T> WithDedupeData(bool useDedupe = true);
    }
    public interface IAllowGridActions<T> : IAllowToGPALObject<IGPALGrid<T>>
    {
        [Action(Description = "Add the rows of another grid")]
        IAllowGridActions<T> Add(IGPALGrid<T> grid);
        [Action(Description = "Add a row")]
        IAllowGridActions<T> AddRow(List<T> row);
        [Action(Description = "Add a column")]
        IAllowGridActions<T> AddColumn(List<T> column);
        int Rows { [Data(Description = "How many rows the grid has")] get; }
        int Columns { [Data(Description = "How many columns the grid has")] get; }
        IAllowGridActions<T> AddCell(T value, int row, int column);
        T GetCell(int row, int column);
        [Action(Description = "Remove every row")]
        IAllowGridActions<T> Clear();
        [Action(Description = "Remove the first or the last row")]
        IAllowGridActions<T> DeleteRow(RowPosition rowPosition = RowPosition.Last);
        [Action(Description = "Read rows from a file")]
        IAllowGridActions<T> InputFrom(GPALFile input);
        [Action(Description = "Write the rows to a file")]
        IAllowGridActions<T> SaveTo(GPALFile output);
        [Action(Description = "Read rows from a database")]
        IAllowGridActions<T> InputFrom(IGPALDatabase input);
        [Action(Description = "Write the rows to a database")]
        IAllowGridActions<T> SaveTo(IGPALDatabase output);
        List<T> this[int index] { [Data(Description = "The row at the given index")] get; set; }
        T this[int row, int column] { get; set; }
        [Data(Description = "How many rows the grid has")]
        int Count();
        [Data(Description = "Whether any row holds the given value")]
        bool Contains(T value);
        [Action(Description = "A copy of the grid")]
        public object Clone();
    }
    #endregion
    #region <Email Interfaces>
    public interface IGPALMail : IAllowEmailEnvelopeAndActions, IAllowEmailServerSettingsAndName, IAllowNameEnvelopeAndActions, IAllowEmailEnvelope, IAllowToGPALObject<IGPALMail>
    {
        // Settings-file values, public so a form can show what LoadSettings loaded.
        string SMTPServerName { [Data(Description = "The SMTP server mail is sent through, as set by WithSMTPServerName or read from a settings file")] get; }
        int SMTPPortNum { [Data(Description = "The port used to reach SMTPServerName")] get; }
        string IMAPServerName { [Data(Description = "The IMAP server mail is read from, null when a POP server is the one named")] get; }
        int IMAPPortNum { [Data(Description = "The port used to reach IMAPServerName")] get; }
        string POPServerName { [Data(Description = "The POP server mail is read from, null when an IMAP server is the one named")] get; }
        int POPPortNum { [Data(Description = "The port used to reach POPServerName")] get; }
        string FromAddress { [Data(Description = "The address messages are sent from, as text, with the display name when there is one")] get; }
        // when the last Send went out. null says it did not, and LastError says why
        DateTime? Sent { [Data(Description = "When the last Send went out, null when it did not")] get; }
        // why the last Send or Receive failed, or null when it did not
        GPALMail.MailError LastError { [Data(Description = "The last failure, or null when the last call did not fail")] get; }
    }
    public interface IAllowEmailServerSettings : IAllowToGPALObject<IGPALMail>
    {
        [Settings(Description = "The IMAP server to receive from, by host name")]
        IAllowEmailServerSettingsAndName WithIMAPServerName(string serverName);
        [Settings(Description = "The port the IMAP server listens on")]
        IAllowEmailServerSettingsAndName WithIMAPPortNum([Port] int portNum);
        [Settings(Description = "The POP3 server to receive from, by host name")]
        IAllowEmailServerSettingsAndName WithPOPServerName(string serverName);
        [Settings(Description = "The port the POP3 server listens on")]
        IAllowEmailServerSettingsAndName WithPOPPortNum([Port] int portNum);
        [Settings(Description = "The SMTP server to send through, by host name")]
        IAllowEmailServerSettingsAndName WithSMTPServerName(string serverName);
        [Settings(Description = "The port the SMTP server listens on")]
        IAllowEmailServerSettingsAndName WithSMTPPortNum([Port] int portNum);
        [Settings(Description = "Send even when the server's certificate cannot be validated. Off unless asked for, and it means nothing checks who is on the other end")]
        IAllowEmailServerSettingsAndName WithAcceptAnyCertificate(bool acceptAnyCertificate);
        [Settings(Description = "The credential to authenticate with, whose username and password win over WithUserName and WithPassword")]
        IAllowEmailServerSettingsAndName WithCredentials(ICredentials credentials);
        [Settings(Description = "The account name to sign in to the mail server with")]
        IAllowEmailServerSettingsAndName WithUserName(string userName);
        [Settings(Description = "The password to sign in to the mail server with")]
        IAllowEmailServerSettingsAndName WithPassword([Password] string password);
    }
    public interface IAllowEmailActions
    {
        [Action(Description = "Send the message the envelope settings built")]
        IAllowEmailEnvelopeAndActions Send();
        [Action(Description = "Read waiting mail from the IMAP or POP server")]
        IAllowEmailEnvelopeAndActions Receive();
        [Action(Description = "Read the mail and hand back the messages")]
        IAllowEmailEnvelopeAndActions Receive(out List<ReceivedEmail> receivedEmails);
        [Action(Description = "Write every attachment on every message received into the given directory")]
        IAllowEmailEnvelopeAndActions SaveTo(GPALFile directory);
        [Action(Description = "Mark every message Receive collected as read on the server, in one connection. IMAP only.")]
        IAllowEmailEnvelopeAndActions MarkAllAsRead();
        [Action(Description = "Delete every message Receive collected from the server, in one connection. IMAP only.")]
        IAllowEmailEnvelopeAndActions DeleteAll();
        [Action(Description = "Empty the envelope so the next message starts from nothing")]
        IAllowEmailEnvelopeAndActions Clear();
        [Action(Description = "Read the mail server settings from a file, or from the default settings file when none is named")]
        IAllowEmailEnvelopeAndActions LoadSettings(GPALFile mailSettingsFile = null);
        [Action(Description = "Write the mail server settings to a file, or to the default settings file when none is named")]
        IAllowEmailEnvelopeAndActions SaveSettings(GPALFile mailSettingsFile = null);
    }
    public interface IAllowEmailEnvelope
    {
        [Settings(Description = "The address the mail is sent from")]
        IAllowNameEnvelopeAndActions WithFromEmailAddress([Email] string mailAddress);
        [Settings(Description = "Add a recipient")]
        IAllowNameEnvelopeAndActions WithToEmailAddress([Email] string mailAddress);
        [Settings(Description = "Add a copy recipient")]
        IAllowNameEnvelopeAndActions WithCcEmailAddress([Email] string mailAddress);
        [Settings(Description = "Add a blind copy recipient")]
        IAllowNameEnvelopeAndActions WithBccEmailAddress([Email] string mailAddress);
        [Settings(Description = "Add recipients, one per entry")]
        IAllowNameEnvelopeAndActions WithToEmailAddress(string[] emailParts);
        [Settings(Description = "Add copy recipients, one per entry")]
        IAllowNameEnvelopeAndActions WithCcEmailAddress(string[] emailParts);
        [Settings(Description = "Add blind copy recipients, one per entry")]
        IAllowNameEnvelopeAndActions WithBccEmailAddress(string[] emailParts);
        [Settings(Description = "Attach a file to the message. Call it again for each further attachment")]
        IAllowEmailEnvelopeAndActions WithAttachment(GPALFile attachmentFile);
        [Settings(Description = "The body text of the message")]
        IAllowEmailEnvelopeAndActions WithBody(string bodyText);
        [Settings(Description = "The subject line of the message")]
        IAllowEmailEnvelopeAndActions WithSubject(string subject);
    }
    public interface IAllowEmailReceiveOptions
    {
        [Settings(Description = "Fetch only the messages the server still has as unread. Every message when false, which is the default.")]
        IAllowEmailEnvelopeAndActions WithUnreadOnly(bool unreadOnly);
        [Settings(Description = "The most messages one Receive may fetch, newest first. Every message by default.")]
        IAllowEmailEnvelopeAndActions WithMaxMessages(int maxMessages);
    }
    public interface IAllowName { [Settings(Description = "Sets the display name on the address added just before this call, whether that was the from, a to, a cc or a bcc")] IAllowEmailEnvelopeAndActions WithName(string name); }
    public interface IAllowNameEnvelopeAndActions : IAllowName
        , IAllowEmailEnvelope
        , IAllowEmailReceiveOptions
        , IAllowEmailActions
        , IAllowToGPALObject<IGPALMail>
    { }
    public interface IAllowEmailEnvelopeAndActions : IAllowEmailEnvelope
        , IAllowEmailReceiveOptions
        , IAllowEmailActions
        , IAllowToGPALObject<IGPALMail>
    { }
    public interface IAllowEmailServerSettingsAndName : IAllowToGPALObject<IGPALMail>
        , IAllowEmailServerSettings
        , IAllowEmailEnvelope
        , IAllowEmailReceiveOptions
    { }
    #endregion <Email Interfaces>
    #region Logger
    public interface IGPALLogger : IAllowLoggingSettings, IAllowToGPALObject<IGPALLogger>
    {
        // Where the last entry was written, as recorded by the logger.
        string LogDirectory { [Data(Description = "The folder the last entry was written to, with DirectoryStructure applied, e.g. logs/231")] get; }
        string LogFilename { [Data(Description = "The name of the file the last entry was written to, with FilenamePattern applied, e.g. DeadMikes_20260819.log")] get; }
        string LogFullPath { [Data(Description = "LogDirectory and LogFilename joined: the file the last entry went into")] get; }
    }
    public interface IAllowLoggingSettings : IAllowToGPALObject<IGPALLogger>
    {
        [Settings(Description = "Write log entries to this database rather than to a file")]
        IAllowLoggingSettings WithDatabase(GPALDatabase database);
        [Settings(Description = "The directory the log files are written under")]
        IAllowLoggingSettings WithRootDirectory([Directory] string path);
        [Settings(Description = "How the directories under the root are named, by year, month, day and hour in the combination chosen. None writes them all in the root")]
        IAllowLoggingSettings WithDirectoryStructure(DirectoryStructure type);
        [Settings(Description = "The name of the log file")]
        IAllowLoggingSettings WithFilename(string name);
        [Settings(Description = "How the filename varies from one file to the next, by date and time down to the part chosen. None keeps one file")]
        IAllowLoggingSettings WithFilenamePattern(FilenamePattern pattern);
        [Settings(Description = "Which event types this logger writes, published or called directly. Defaults to all")]
        IAllowLoggingSettings WithLogEventTypes(GPALEventType logEventTypes);
        [Settings(Description = "The character written between the fields on a line")]
        IAllowLoggingSettings WithDelimiter(char delimiter);
        [Settings(Description = "How the date and time are written at the start of each entry")]
        IAllowLoggingSettings WithLoggingFormat(LogDateFormat logDateFormat);
    }
    #endregion Logger
    #region Excel
    // Core operations
    public interface IAllowExcelCore
    {
        [Action(Description = "Saves the loaded workbook(s) to the file(s) matching gpalFile's filenames (which may include wildcard patterns), or to _gpalFile if gpalFile is null")]
        IAllowExcelOperations SaveTo(GPALFile gpalFile);
        [Action(Description = "Append every row read into this grid. Pass null and GPAL makes one, the same as GetGrid")]
        IAllowExcelOperations SaveTo(ref IGPALGrid<string> grid);
        [Action(Description = "Write the range read into the sheet at this 1-based index, creating it if it is missing")]
        IAllowExcelOperations SaveTo([Index] int sheetIndex);
        [Action(Description = "Writes the previously read range data into the sheet named sheetName of every loaded workbook, creating the sheet if it does not exist")]
        IAllowExcelOperations SaveTo(string sheetName);
        [Action(Description = "Closes every loaded workbook, optionally saving each back to its source path first, and clears all cached state")]
        void Close(bool saveOnClose);
    }

    // File selection state
    public interface IAllowExcelFileSelection : IAllowExcelCore
    {
        [Settings(Description = "The workbook to read, by name or by a wildcard naming several")]
        IAllowExcelSheetSelection WithFile(GPALFile gpalFile);
        [Settings(Description = "Start a workbook of your own at this name, with one sheet and nothing in it. Nothing is written until SaveTo or Close(true)")]
        IAllowExcelSheetSelection WithNewFile(GPALFile gpalFile);
    }

    // Sheet selection state
    public interface IAllowExcelSheetSelection : IAllowExcelCore, IAllowToGPALObject<IGPALExcel>
    {
        [Settings(Description = "The sheet to work on, counting from 1, which is the sheet a workbook opens on")]
        IAllowExcelOperations WithSheet([Index] int sheetIndex);
        [Settings(Description = "The sheet to work on, by name")]
        IAllowExcelOperations WithSheet(string sheetName);
        [Settings(Description = "Add a sheet of this name and work on it. Called once per tab a workbook is to hold")]
        IAllowExcelOperations WithNewSheet(string sheetName);
    }

    // Comparison results state
    public interface IAllowExcelCompareSettings
    {
        [Data(Description = "Compares each previously read range against grid, recording any cell-level differences for retrieval via GetCompareResults")]
        IAllowExcelOperations CompareToGrid(IGPALGrid<string> grid);
        [Data(Description = "Compare each range read against the same range in another workbook, recording the differences")]
        IAllowExcelOperations CompareToFile(GPALFile gpalFile);
        [Data(Description = "Compare each range read against the same range on the selected sheet, recording the differences")]
        IAllowExcelOperations CompareToRange(string range);
    }

    // General operations state
    public interface IAllowExcelOperations : IAllowExcelSheetSelection, IAllowExcelCompareSettings
    {
        [Action(Description = "Write a grid into the sheet, at the cell WithCell named or A1. This is how data of your own gets into a workbook")]
        IAllowExcelOperations InputFrom(IGPALGrid<string> grid);
        [Settings(Description = "Read the cell range from the selected sheet of every loaded workbook, for the steps that follow")]
        IAllowExcelOperations WithRange(string range);
        [Action(Description = "Writes the range data previously read via WithRange (or another data-producing operation) to range on the currently selected sheet of every loaded workbook")]
        IAllowExcelOperations WriteRange(string range);
        [Action(Description = "Clears any range data read via WithRange, WithColumn, or WithRowNumber, along with any comparison differences accumulated by the CompareTo* methods")]
        IAllowExcelOperations ClearRanges();
        [Settings(Description = "The cell, such as B3, that the cell steps that follow work on")]
        IAllowExcelCellSettings WithCell(string cell);
        [Settings(Description = "Sets the value to search for in subsequent ReplaceWith operations")]
        IAllowExcelSearchAndReplaceSettings WithSearchValue(string value);
        [Settings(Description = "Read every value in the column from the selected sheet")]
        IAllowExcelOperations WithColumn(string column);
        [Settings(Description = "Read every value in the row from the selected sheet")]
        IAllowExcelOperations WithRowNumber(int rowNumber);
        [Action(Description = "Sums every numeric value across all previously read range data (non-numeric values are ignored)")]
        IAllowExcelOperations CalculateSum(out string result);
        [Action(Description = "Counts every non-empty value across all previously read range data")]
        IAllowExcelOperations CalculateCount(out string result);
        [Data(Description = "The differences the CompareTo steps found, as a grid")]
        IAllowExcelOperations GetCompareResults(out IGPALGrid<string> results);
    }

    // Cell operations state
    public interface IAllowExcelCellSettings : IAllowExcelCore
    {
        [Settings(Description = "Sets where InsertValue places its value relative to the current cell content: 0 inserts before the existing content, any other value inserts after")]
        IAllowExcelCellSettings WithInsertPosition(int position);
        [Settings(Description = "Sets the separator that InsertValue places between the inserted value and the cell's existing content")]
        IAllowExcelCellSettings WithInsertSeparator(string separator);
        [Action(Description = "Insert the value into the cell chosen with WithCell, joined to what is there by the WithInsertSeparator text")]
        IAllowExcelOperations InsertValue(string value);
        [Action(Description = "Sets the value of the cell selected via WithCell on every loaded workbook, replacing any existing content")]
        IAllowExcelOperations SetValue(string value);
        [Action(Description = "Appends value to the end of the existing content of the cell selected via WithCell on every loaded workbook")]
        IAllowExcelOperations AppendValue(string value);
        [Action(Description = "Prepends value to the beginning of the existing content of the cell selected via WithCell on every loaded workbook")]
        IAllowExcelOperations PrependValue(string value);
    }

    // Search and replace settings state
    public interface IAllowExcelSearchAndReplaceSettings
    {
        [Action(Description = "Replace every cell in the range holding the search value with this one")]
        IAllowExcelOperations ReplaceWith(string value);
        [Action(Description = "Every cell in the range holding the search value, as a grid of File, Sheet, Cell, Row, Column, with a header row. Empty apart from the header when nothing matched")]
        IAllowExcelOperations GetSearchResults(out IGPALGrid<string> results);
    }

    // Unified interface
    public interface IGPALExcel : IAllowExcelFileSelection, IAllowExcelSheetSelection, IAllowExcelOperations, IAllowExcelCellSettings, IAllowExcelSearchAndReplaceSettings, IAllowExcelCompareSettings, IAllowToGPALObject<IGPALExcel>
    {
    }
    #endregion Excel
    #region Launcher
    // written by Grok
    public interface IAllowExecutableSetup : IAllowToGPALObject<Launcher>
    {
        [Settings(Description = "The executable to run, or a url or document to open with whatever is registered for it")]
        IAllowExecutableSetup WithExecutable(string executable);
        [Settings(Description = "Add one argument. Called once per argument, so a path with a space in it needs nothing done to it")]
        IAllowExecutableSetup WithArgument(string argument);
        [Settings(Description = "A name for this launcher, used in the events it publishes")]
        IAllowExecutableSetup WithName(string argument);
        [Settings(Description = "The directory the process starts in. Set before Launch, because that is when the process is built")]
        IAllowExecutableSetup WithWorkingDirectory([Directory] string directory);
        [Settings(Description = "An environment variable the process starts with. Called once per variable")]
        IAllowExecutableSetup WithEnvironmentVariable(string name, string value);
        [Settings(Description = "Where each line the process writes to standard output goes")]
        IAllowExecutableSetup WithOutputTo(Action<string> outputHandler);
        [Settings(Description = "Where each line the process writes to standard error goes")]
        IAllowExecutableSetup WithErrorTo(Action<string> errorHandler);
        [Action(Description = "Start the process with everything set above it")]
        IAllowRunningExecutable Launch();
    }

    // Post-launch: Running state
    public interface IAllowRunningExecutable : IAllowToGPALObject<Launcher>, IAllowWaitingExecutable
    {
        // Process settings must be set before the process starts, so they are on IAllowExecutableSetup.
        [Action(Description = "Whether the process is still running")]
        bool IsRunning();
        [Action(Description = "Wait up to this many milliseconds for the process to end")]
        IAllowWaitingExecutable WaitForExit([Milliseconds] int milliseconds);
    }

    // Post-wait/exit: Limited to termination
    public interface IAllowWaitingExecutable
    {
        [Action(Description = "Stop the process. What it started keeps running; KillTree is for those")]
        void Kill();
        [Action(Description = "Stop the process and everything it started, children first so nothing is orphaned")]
        void KillTree();
    }
    #endregion Launcher
    #region REST
    // generated by grok
    public interface IAllowRESTEndpoint
    {
        [Settings(Description = "Sets the API base URL with download path", Submenu = "Request")]
        IAllowRESTEndpointDetails WithAPIBase([URL] string url);
        IAllowRESTEndpointDetails WithAPIBase(string url, TimeSpan httpTimeout);
        [Action(Description = "Load an OpenAPI description so its endpoints can be called by name")]
        IAllowRESTEndpointDetails LoadOpenAPIMap(string yamlOrUrl);
        [Action(Description = "Load an OpenAPI description so its endpoints can be called by name")]
        IAllowRESTEndpointDetails LoadOpenAPIMap(GPALFile openSpiMapFile);
    }

    public interface IAllowRESTEndpointDetails : IAllowRESTExecution, IAllowRESTParameters, IAllowToGPALObject<IRESTClient>
    {
        [Settings(Description = "Sets the endpoint as a string", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithEndpoint(string endpoint);
        [Settings(Description = "Sets the endpoint from an ApiEndpoint enum", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithEndpoint(ApiEndpoint endpoint);
        [Settings(Description = "Sets the result name for the next execution", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithResultName(string name);
        [Settings(Description = "Sets the client name", Submenu = "Request")]
        IAllowRESTEndpointDetails WithName(string name);
        [Action(Description = "List the endpoints the loaded OpenAPI description offers")]
        IAllowRESTEndpointDetails ListOpenAPIEndPoints();
    }

    public interface IAllowRESTParametersOrExecution : IAllowRESTParameters, IAllowRESTExecution, IAllowRESTScrollElement, IAllowCheckNetworkIdleOrExecution, IAllowRestMethods
    {
        [Settings(Description = "Sets the URL parameter", Submenu = "Navigation")]
        IAllowRESTParametersOrExecution WithUrl([URL] string url);
        [Data(Description = "Sets the URL from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithUrlFromResult([Index] int resultIndex);
        [Data(Description = "Sets the URL from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithUrlFromResult(string name);
        [Settings(Description = "Sets the tab ID parameter", Submenu = "Navigation")]
        IAllowRESTParametersOrExecution WithTabId(int tabId);
        [Data(Description = "Sets the tab ID from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithTabIdFromResult([Index] int resultIndex);
        [Data(Description = "Sets the tab ID from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithTabIdFromResult(string name);
        [Settings(Description = "Sets the window ID parameter", Submenu = "Navigation")]
        IAllowRESTParametersOrExecution WithWindowId(int windowId);
        [Data(Description = "Sets the window ID from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithWindowIdFromResult([Index] int resultIndex);
        [Data(Description = "Sets the window ID from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithWindowIdFromResult(string name);
        [Settings(Description = "Sets the storage path parameter", Submenu = "Storage")]
        IAllowRESTStorageOptions WithStorageType(WebsiteStorageType storageType);
        [Settings(Description = "Sets the key parameter", Submenu = "Storage")]
        IAllowRESTParametersOrExecution WithKey(string key);
        [Data(Description = "Sets the key from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithKeyFromResult([Index] int resultIndex);
        [Data(Description = "Sets the key from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithKeyFromResult(string name);
        [Settings(Description = "Sets the CSS parameter", Submenu = "Element")]
        IAllowRESTParametersOrExecution WithCss([Css] string css);
        [Data(Description = "Sets the CSS from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithCssFromResult([Index] int resultIndex);
        [Data(Description = "Sets the CSS from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithCssFromResult(string name);
        [ElementAssitant(Description = "Sets the element parameter")]
        IAllowRESTParametersOrExecution WithElement(IGPALElement gPALElement);
        [Settings(Description = "Sets the XPath parameter", Submenu = "Element")]
        IAllowRESTParametersOrExecution WithXPath([XPath] string xpath);
        [Data(Description = "Sets the XPath from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithXPathFromResult([Index] int resultIndex);
        [Data(Description = "Sets the XPath from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithXPathFromResult(string name);
        [Settings(Description = "Sets the pixels parameter", Submenu = "Drag and Scroll")]
        IAllowRESTParametersOrExecution WithPixels([Pixels] int pixels);
        [Data(Description = "Sets the pixels from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithPixelsFromResult([Index] int resultIndex);
        [Data(Description = "Sets the pixels from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithPixelsFromResult(string name);
        [Settings(Description = "Sets the referrer parameter", Submenu = "Navigation")]
        IAllowRESTParametersOrExecution WithReferrer(string referrer);
        [Data(Description = "Sets the referrer from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithReferrerFromResult([Index] int resultIndex);
        [Data(Description = "Sets the referrer from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithReferrerFromResult(string name);
        [Settings(Description = "Sets the text parameter", Submenu = "Element")]
        IAllowRESTExecution WithText(string text);
        [Data(Description = "Sets the text from a result by index", Submenu = "From Result")]
        IAllowRESTExecution WithTextFromResult([Index] int resultIndex);
        [Data(Description = "Sets the text from a result by name", Submenu = "From Result")]
        IAllowRESTExecution WithTextFromResult(string name);
        [Action(Description = "Where a file the browser downloads should end up")]
        IAllowRESTParametersOrExecution DownloadTo(GPALFile file);
        [Action(Description = "Saves the response to a file if the endpoint returns a downloadable content")]
        IAllowRESTParametersOrExecution SaveResultsTo(GPALFile file);
        [Settings(Description = "Sets the element ID parameter", Submenu = "Element")]
        IAllowRESTParametersOrExecution WithElementId(string elementId);
        [Data(Description = "Sets the element from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithElementIdFromResult([Index] int resultIndex);
        [Data(Description = "Sets the element from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithElementIdFromResult(string name);
        [Action(Description = "Sets the value of the destination element identified by destSelector from the source element identified by the preceding WithCss or WithElementId call")]
        IAllowRESTParametersOrExecution SetValueFrom([Css] string destSelector);
        [Data(Description = "Sets the horizontal pixels from a result by index", Submenu = "From Result")]
        IAllowRESTScrollElement WithHPixelsFromResult([Index] int resultIndex);
        [Data(Description = "Sets the horizontal pixels from a result by name", Submenu = "From Result")]
        IAllowRESTScrollElement WithHPixelsFromResult(string name);
        [Data(Description = "Sets the vertical pixels from a result by index", Submenu = "From Result")]
        IAllowRESTScrollElement WithVPixelsFromResult([Index] int resultIndex);
        [Data(Description = "Sets the vertical pixels from a result by name", Submenu = "From Result")]
        IAllowRESTScrollElement WithVPixelsFromResult(string name);
        [Settings(Description = "Sets the CSS parameter", Submenu = "Request")]
        IAllowRESTExecution WithDeviceName(string deviceNameOrId);

    }
    public interface IAllowRESTMethod
    {
        [Settings(Description = "Sets the HTTP method", Submenu = "Request")]
        IAllowRESTExecution WithHttpMethod(string method);
    }

    public interface IAllowRangeValue
    {
        [Settings(Description = "Specifies the value for SetRange operation", Submenu = "Element")]
        IAllowRESTExecution WithRangeValue(int rangeValue);
    }
    public interface IAllowRESTExecution : IAllowRESTMethod
    {
        [Fluent(Description = "Executes the request synchronously with type")]
        string Execute(bool publishEvent = true);
        [Fluent(Description = "Executes the request synchronously with type")]
        T Execute<T>(bool publishEvent = true);
        [Fluent(Description = "Executes and chains the request with type")]
        IRESTClient AndThen(bool publishEvent = true);
        [Fluent(Description = "Executes and chains the request with type")]
        IRESTClient AndThen<T>(bool publishEvent = true);
        [Action(Description = "Executes the request asynchronously with type")]
        Task<string> ExecuteAsync(bool publishEvent = true);
        [Action(Description = "Executes the request asynchronously with type")]
        Task<T> ExecuteAsync<T>(bool publishEvent = true);
        [Data(Description = "The results of the calls made so far")]
        ResultCollection GetExecutionResults();
        [Data(Description = "Every result stored under name, in the order it was stored, so a loop that ran five times answers with five")]
        IReadOnlyList<object> GetExecutionResults(string name);
        [Action(Description = "Empties what WithResultName has stored, so GetExecutionResults answers about what has happened since rather than everything since the client was made")]
        IAllowRESTExecution ClearResults();
        IAllowRESTExecution WithHeader(string name, string value);
        [Settings(Description = "How long one attempt is given before it is abandoned", Submenu = "Request")]
        IAllowRESTExecution WithTimeout(TimeSpan httpTimeout);
    }

    public interface IAllowRESTParameters : IAllowRESTExecution
    {
        [Settings(Description = "Sets parameters as a variable argument list", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithParameters(params object[] parameters);
        [Settings(Description = "Sets parameters as a single object", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithParameters(object parameters);
        [Data(Description = "Sets parameters from a result by index", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithParametersFromResult([Index] int resultIndex);
        [Data(Description = "Sets parameters from a result by name", Submenu = "From Result")]
        IAllowRESTParametersOrExecution WithParametersFromResult(string name);
        [Settings(Description = "Specifiy the encoiding type for posted parameters: json or urlencoded", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithEncoding(Enums.ContentEncoding encoding);
    }

    public interface IAllowRESTWorkflow
    {
        [Workflow(Description = "Adds a workflow definition")]
        IAllowRESTWorkflow WithWorkflow(Action<RESTClient> workflow);
        [Workflow(Description = "Gets the execution results")]
        IAllowRESTWorkflow ClearWorkflows();
        [Workflow(Description = "Executes workflows while condition holds")]
        IAllowRESTParametersOrExecution While(Func<bool> condition);
        [Workflow(Description = "Executes workflows until condition holds")]
        IAllowRESTParametersOrExecution Until(Func<bool> condition);
    }

    public interface IAllowCheckNetworkIdleOrExecution : IAllowRESTExecution
    {
        [Settings(Description = "Sets the prune interval in milliseconds", Submenu = "Network Idle")]
        IAllowCheckNetworkIdleOrExecution WithPruneMs([Milliseconds] int pruneMs);
        [Settings(Description = "Sets the maximum connections", Submenu = "Network Idle")]
        IAllowCheckNetworkIdleOrExecution WithMaxConnections(int maxConnections);
    }

    public interface IAllowRESTScrollElement : IAllowRESTExecution
    {
        [Settings(Description = "Sets the horizontal pixels parameter", Submenu = "Drag and Scroll")]
        IAllowRESTScrollElement WithHPixels([Pixels] int hPixels);
        [Settings(Description = "Sets the vertical pixels parameter", Submenu = "Drag and Scroll")]
        IAllowRESTScrollElement WithVPixels([Pixels] int vPixels);
    }

    public interface IAllowRESTDragAndDrop : IAllowRESTExecution
    {
        [Settings(Description = "Sets the horizontal drop offset for a drag-and-drop operation", Submenu = "Drag and Scroll")]
        IAllowRESTDragAndDrop WithDeltaX([Pixels] int deltaX);
        [Settings(Description = "Sets the vertical drop offset for a drag-and-drop operation", Submenu = "Drag and Scroll")]
        IAllowRESTDragAndDrop WithDeltaY([Pixels] int deltaY);
        [Settings(Description = "Sets the horizontal grab offset within the element for a drag-and-drop operation", Submenu = "Drag and Scroll")]
        IAllowRESTDragAndDrop WithOffsetX([Pixels] int offsetX);
        [Settings(Description = "Sets the vertical grab offset within the element for a drag-and-drop operation", Submenu = "Drag and Scroll")]
        IAllowRESTDragAndDrop WithOffsetY([Pixels] int offsetY);
    }

    public interface IAllowRESTAttribute : IAllowRESTExecution
    {
        [Data(Description = "Specifies the attribute name for get or set operation")]
        IAllowRESTAttribute WithAttribute(string attribute);
        [Settings(Description = "Specifies the value for set operation", Submenu = "Element")]
        IAllowRESTAttribute WithValue(string value);
    }

    public interface IAllowRESTInputText
    {
        [Settings(Description = "Sets the text parameter")]
        IAllowRESTExecution WithText(string text);
    }

    public interface IAllowSelectOptions
    {
        [Settings(Description = "Set the value to choose from the Select menu", Submenu = "Element")]
        IAllowRESTExecution WithSelectValue(string value);
        [Settings(Description = "Pick the option at this index", Submenu = "Element")]
        IAllowRESTExecution WithSelectIndex([Index] int index);
    }
    public interface IAllowRESTWithDownloadFilename
    {
        [Settings(Description = "The name the download is saved as", Submenu = "Files")]
        IAllowRESTExecution WithDownloadFile([FilePath] string downloadPath);
    }
    public interface IAllowRESTWithUploadFilename
    {
        [Settings(Description = "Sets the timeout in milliseconds", Submenu = "Files")]
        IAllowRESTExecution WithUploadFile([FilePath] string uploadFilenameAndPath);
        [Settings(Description = "Sets the timeout in milliseconds", Submenu = "Files")]
        IAllowRESTExecution WithUploadFiles(GPALFile uploadFilenamesAndPaths);
    }

    public interface IAllowRESTCaptureOptions : IAllowRESTExecution
    {
        [Settings(Description = "Records only the calls whose url holds this, so a page that talks to a dozen places narrows to the one worth having", Submenu = "Calls")]
        IAllowRESTCaptureOptions WithCallFilter(string urlFragment);
        [Settings(Description = "Forgets what was already recorded before starting, so a second capture is not read as the first", Submenu = "Calls")]
        IAllowRESTCaptureOptions WithClearCalls(bool clear = true);
    }

    public interface IAllowRESTStorageOptions : IAllowRESTExecution
    {
        [Browser(Description = "Sets the storage domain parameter")]
        IAllowRESTStorageOptions WithStorageDomain(string domain);
        [Browser(Description = "Sets the storage path parameter")]
        IAllowRESTStorageOptions WithStoragePath(string path);
        [Browser(Description = "Sets the storage key parameter")]
        IAllowRESTStorageOptions WithStorageKey(string key);
        [Browser(Description = "Sets the storage domain parameter")]
        IAllowRESTStorageOptions WithStorageStoreName(string storeName);
        [Settings(Description = "Sets the set storage data parameter", Submenu = "Request")]
        IAllowRESTStorageOptions WithData(string data);
        [Settings(Description = "Delete the storage on every origin, not only the current one", Submenu = "Storage")]
        IAllowRESTStorageOptions WithDeleteAcrossOrigins(bool trueFalse);
    }

    public interface IAllowRestMethods
    {
        // Single-parameter methods with direct Execute
        [Browser(Description = "Navigates browser history back", Submenu = "Navigation")]
        IAllowRESTExecution Back();
        [Action(Description = "Get a screenshot of the current tab visble contents", Submenu = "Page")]
        IAllowRESTExecution CaptureVisibleTab(ImageFormat imageFormat = ImageFormat.JPEG);
        [Browser(Description = "Closes a tab by URL", Submenu = "Tabs")]
        IAllowRESTExecution CloseTab(GPALUrl url = null);
        [Browser(Description = "Closes a tab by tab ID", Submenu = "Tabs")]
        IAllowRESTExecution CloseTab(int tabId);
        [Browser(Description = "Closes a window by URL, optionally switching to the main window", Submenu = "Windows")]
        IAllowRESTExecution CloseWindow(GPALUrl url = null);
        [Browser(Description = "Closes every window the browser has, which is how it is asked to quit", Submenu = "Windows")]
        IAllowRESTExecution CloseBrowser();
        [Browser(Description = "Closes a window by window ID, optionally switching to the main window", Submenu = "Windows")]
        IAllowRESTExecution CloseWindow(int tabId);
        [Action(Description = "Delete the specified storage type", Submenu = "Storage")]
        IAllowRESTStorageOptions DeleteStorage(WebsiteStorageType storageType);
        [Action(Description = "Executes JavaScript code", Submenu = "Page")]
        IAllowRESTParameters ExecuteJavaScript([JavaScript] string script);
        [Browser(Description = "Registers a script to run on every new document load", Submenu = "Scripts")]
        IAllowRESTParameters InjectScript([JavaScript] string script);
        [Browser(Description = "Removes all scripts previously registered via InjectScript", Submenu = "Scripts")]
        IAllowRESTParameters ClearInjectedScripts();
        [Action(Description = "Appends text to an element", Submenu = "Input")]
        IAllowRESTInputText FillInAppend(string elementId);
        [Action(Description = "Appends text to an element", Submenu = "Input")]
        IAllowRESTInputText FillInInsert(string elementId);
        [Action(Description = "Appends text to an element", Submenu = "Input")]
        IAllowRESTInputText FillInOverwrite(string elementId);
        [Browser(Description = "Navigates browser history forward", Submenu = "Navigation")]
        IAllowRESTExecution Forward();
        [Browser(Description = "Sets browser to full screen", Submenu = "Windows")]
        IAllowRESTExecution FullScreen();
        [Data(Description = "Gets the ready status", Submenu = "Page")]
        IAllowRESTExecution GetReadyStatus();
        [Data(Description = "Gets the HTTP status of the active tab's page", Submenu = "Page")]
        IAllowRESTExecution GetStatusCode();
        [Action(Description = "Checks if network is idle with max connections", Submenu = "Page")]
        IAllowRESTExecution CheckNetworkIdle(int maxConnections = 0);
        [Data(Description = "Gets the shadow root for a CSS selector", Submenu = "Find Elements")]
        IAllowRESTExecution GetShadowRoot([Css] string css);
        [Data(Description = "Get the specified storage type", Submenu = "Page")]
        IAllowRESTStorageOptions GetStorage(WebsiteStorageType storageType);
        [Browser(Description = "Gets the current URL", Submenu = "Navigation")]
        IAllowRESTExecution GetCurrentUrl();
        [Browser(Description = "Gets the current window's ID and URL", Submenu = "Windows")]
        IAllowRESTExecution GetCurrentWindow();
        [Data(Description = "Gets the current URL and the document identity in one call", Submenu = "Page")]
        IAllowRESTExecution GetDocumentStamp();
        [Data(Description = "Gets the page title, the __NEXT_DATA__ payload and any React Flight chunks, read from the document rather than its markup", Submenu = "Page")]
        IAllowRESTExecution GetHydrationScripts();
        [Data(Description = "Get the options in a Select manue", Submenu = "Element")]
        IAllowRESTExecution GetOptions(string elementId);
        [Data(Description = "Gets the browser's preferred languages, most preferred first", Submenu = "Page")]
        IAllowRESTExecution GetLanguages();
        [Action(Description = "Starts or stops recording what the page asks for", Submenu = "Page")]
        IAllowRESTCaptureOptions CaptureCalls(bool capture = true);
        [Data(Description = "Everything recorded so far, oldest first, as the json the extension keeps", Submenu = "Page")]
        IAllowRESTExecution GetCapturedCalls();
        [Data(Description = "Gets the user agent", Submenu = "Page")]
        IAllowRESTExecution GetUserAgent();
        [Browser(Description = "Navigates to a URL", Submenu = "Navigation")]
        IAllowRESTExecution GoTo(GPALUrl url);
        [Browser(Description = "Navigates to a tab by URL", Submenu = "Tabs")]
        IAllowRESTExecution GoToTab(GPALUrl url);
        [Browser(Description = "Navigates to a tab by tab ID", Submenu = "Tabs")]
        IAllowRESTExecution GoToTab(int tabId);
        [Browser(Description = "Switches to a window by URL", Submenu = "Windows")]
        IAllowRESTExecution GoToWindow(GPALUrl url);
        [Browser(Description = "Switches to a window by window ID", Submenu = "Windows")]
        IAllowRESTExecution GoToWindow(int tabId);
        [Browser(Description = "Maximizes the browser window", Submenu = "Windows")]
        IAllowRESTExecution Maximize();
        [Browser(Description = "Minimizes the browser window", Submenu = "Windows")]
        IAllowRESTExecution Minimize();
        [Action(Description = "Moves to an element", Submenu = "Mouse")]
        IAllowRESTExecution MoveTo(string elementId);
        [Action(Description = "Opens a new tab with optional URL", Submenu = "Tabs and Windows")]
        IAllowRESTExecution NewTab(GPALUrl url = null);
        [Browser(Description = "Switches to the next tab", Submenu = "Tabs")]
        IAllowRESTExecution NextTab();
        [Browser(Description = "How many tabs the browser actually has open, which is not the same as how many GPAL opened", Submenu = "Tabs")]
        IAllowRESTExecution TabCount();
        [Browser(Description = "Switches to the next window", Submenu = "Windows")]
        IAllowRESTExecution NextWindow();
        [Browser(Description = "Restores the browser window to normal", Submenu = "Windows")]
        IAllowRESTExecution Normal();
        [Action(Description = "Opens a new window with the specified URL", Submenu = "Tabs and Windows")]
        IAllowRESTExecution OpenWindow(GPALUrl url = null);
        [Action(Description = "Overrides the referrer URL", Submenu = "Referrer and User Agent")]
        IAllowRESTExecution OverrideReferrer(string referrer);
        [Scroll(Description = "Scrolls the page down by pages")]
        IAllowRESTExecution PageDown(int pagesToScroll = 1);
        [Scroll(Description = "Scrolls to the page end")]
        IAllowRESTExecution PageEnd();
        [Scroll(Description = "Scrolls to the page top")]
        IAllowRESTExecution PageTop();
        [Scroll(Description = "Scrolls the page up by pages")]
        IAllowRESTExecution PageUp(int pagesToScroll = 1);
        [Data(Description = "Press the modifier keys specified", Submenu = "Keyboard")]
        IAllowRESTExecution PressModifierKey(ModifierKeys modifierKeys);
        [Browser(Description = "Switches to the previous tab", Submenu = "Tabs")]
        IAllowRESTExecution PreviousTab();
        [Browser(Description = "Switches to the next window", Submenu = "Windows")]
        IAllowRESTExecution PreviousWindow();
        [Browser(Description = "Refreshes the current page", Submenu = "Navigation")]
        IAllowRESTExecution Refresh();
        [Data(Description = "Press the modifier keys specified", Submenu = "Keyboard")]
        IAllowRESTExecution ReleaseModifierKey(ModifierKeys modifierKeys);
        [Browser(Description = "Restores the browser window", Submenu = "Windows")]
        IAllowRESTExecution Restore();
        [Scroll(Description = "Scrolls the window horizontally by pixels")]
        IAllowRESTExecution ScrollWindowByHorizontal([Pixels] int pixels);
        [Scroll(Description = "Scrolls the window vertically by pixels")]
        IAllowRESTExecution ScrollWindowByVertical([Pixels] int pixels);
        /// <summary>
        /// Simulates typing in OttoMagic: dispatches key events character-by-character. Honors
        /// <see cref="GPAL.TypingDelay"/> when <paramref name="delayMs"/> is left at its default (0).
        /// See <see cref="GPAL.WithTypingDelay"/> for the SendString vs. FillIn guidance.
        /// </summary>
        IAllowRESTExecution SendString(string text, int delayMs = 0);
        [Data(Description = "Sends a key", Submenu = "Keyboard")]
        IAllowRESTExecution SendKey(byte vkcode);
        [Data(Description = "Starts setting an attribute of an element by ID", Submenu = "Keyboard")]
        IAllowRangeValue SetRange(string elementId);
        [Action(Description = "Set data for the specified storage type", Submenu = "Storage")]
        IAllowRESTStorageOptions SetStorage(WebsiteStorageType storageType);
        [Action(Description = "Overrides the browser's user agent string", Submenu = "Referrer and User Agent")]
        IAllowRESTExecution SetUserAgent(string userAgent);
        [Action(Description = "Overrides referrer stealthily - sets to google.com", Submenu = "Referrer and User Agent")]
        IAllowRESTExecution SetGoogleReferrer();
        [Browser(Description = "Switches to default content", Submenu = "Frames")]
        IAllowRESTExecution SwitchToDefaultContent();
        [Browser(Description = "Switches to an element to search for selectors contained", Submenu = "Frames")]
        IAllowRESTExecution SwitchToElement(string elementId);  // same as switch to iframe
        [Browser(Description = "Switches to a frame", Submenu = "Frames")]
        IAllowRESTExecution SwitchToFrame(string elementId);    // same as switch to element
        [Browser(Description = "Switches to a shadow root", Submenu = "Frames")]
        IAllowRESTExecution SwitchToShadowRoot(string elementId);
        [Data(Description = "Gets the workflow", Submenu = "Recorder")]
        IAllowRESTExecution GetWorkflow();
        [Data(Description = "Gets settings", Submenu = "Recorder")]
        IAllowRESTExecution GetSettings();
        [Data(Description = "Gets GPAL settings", Submenu = "Recorder")]
        IAllowRESTExecution GetGpalSettings();
        [Data(Description = "Gets browser settings", Submenu = "Recorder")]
        IAllowRESTExecution GetBrowserSettings();
        [Action(Description = "Fires a change event on an element by ID", Submenu = "Input")]
        IAllowRESTExecution FireChangeEvent(string elementId);
        [Action(Description = "Focuses an element by ID", Submenu = "Mouse")]
        IAllowRESTExecution Focus(string elementId);
        [Data(Description = "Gets the bounding client rect of an element by ID", Submenu = "Element")]
        IAllowRESTExecution GetBoundingClientRect(string elementId);
        IAllowRESTExecution ElementFromPoint(string elementId, int x, int y);
        IAllowRESTExecution TrustedLeftClick(int x, int y, int holdMs = 0);
        IAllowRESTExecution TrustedMiddleClick(int x, int y, int holdMs = 0);
        [Data(Description = "Gets the attribute hash of an element", Submenu = "Element")]
        IAllowRESTExecution GetElementAttributeHash(string elementId);
        [Action(Description = "The HTML of the page", Submenu = "Page")]
        IAllowRESTExecution GetPageSource();
        [Data(Description = "Gets the parent node of an element by ID", Submenu = "Find Elements")]
        IAllowRESTExecution GetParentNode(string elementId);
        [Data(Description = "The content and styles of the element", Submenu = "Element")]
        IAllowRESTExecution GetContentAndCss(string selector);
        [Data(Description = "The computed styles of the element", Submenu = "Element")]
        IAllowRESTExecution GetCssAttributes(string selector);
        [Data(Description = "The attributes of the element", Submenu = "Element")]
        IAllowRESTExecution GetDomAttributes(string selector);
        [Data(Description = "The DOM properties of the element", Submenu = "Element")]
        IAllowRESTExecution GetDomProperties(string selector);
        [Browser(Description = "The position and size of the browser window", Submenu = "Windows")]
        IAllowRESTExecution GetWindowRectangle();
        [Action(Description = "Hides an element by ID", Submenu = "Input")]
        IAllowRESTExecution HideElement(string elementId);
        [Data(Description = "Checks if an element is clickable by ID", Submenu = "Element")]
        IAllowRESTExecution IsClickable(string elementId);
        [Data(Description = "Checks if an element is displayed by ID", Submenu = "Element")]
        IAllowRESTExecution IsDisplayed(string elementId);
        [Data(Description = "Checks if an element is enabled by ID", Submenu = "Element")]
        IAllowRESTExecution IsEnabled(string elementId);
        [Scroll(Description = "Checks if the page is at the end")]
        IAllowRESTExecution IsEndOfPage();
        [Data(Description = "Checks if an element is visible in viewport by ID", Submenu = "Element")]
        IAllowRESTExecution IsVisibleInViewport(string elementId);
        [Action(Description = "Performs a left click on an element by ID", Submenu = "Clicks")]
        IAllowRESTExecution LeftClick(string elementId);
        [Action(Description = "Performs a left click and download on an element", Submenu = "Files")]
        IAllowRESTWithDownloadFilename LeftClickAndDownload(string elementId);
        [Action(Description = "The browser downloads a url itself, with no tab and no Save As", Submenu = "Files")]
        IAllowRESTWithDownloadFilename DownloadUrl(string url);
        [Action(Description = "The name the next download is saved as", Submenu = "Files")]
        IAllowRESTExecution SetDownloadFilename([FilePath] string downloadPath);
        [Action(Description = "Purely headless mode, attaches the file to the input[type=file]", Submenu = "Files")]
        IAllowRESTWithUploadFilename LeftClickAndUpload(string elementId);
        [Action(Description = "Performs a left double click on an element by ID", Submenu = "Clicks")]
        IAllowRESTExecution LeftDoubleClick(string elementId);
        [Action(Description = "Performs a left click on an element by ID", Submenu = "Clicks")]
        IAllowRESTExecution MiddleClick(string elementId);
        [Action(Description = "Performs a right click on an element by ID", Submenu = "Clicks")]
        IAllowRESTExecution RightClick(string elementId);
        [Scroll(Description = "Scrolls an element into view by ID")]
        IAllowRESTExecution ScrollIntoView(string elementId);
        [Data(Description = "Gets the window inner height", Submenu = "Window")]
        IAllowRESTExecution WindowInnerHeight();
        [Data(Description = "Gets the window inner width", Submenu = "Window")]
        IAllowRESTExecution WindowInnerWidth();
        [Data(Description = "Gets the window outer height", Submenu = "Window")]
        IAllowRESTExecution WindowOuterHeight();
        [Data(Description = "Gets the window outer width", Submenu = "Window")]
        IAllowRESTExecution WindowOuterWidth();
        [Fluent(Description = "Send an API request from inside the page, carrying the browser's cookies, TLS fingerprint and headers")]
        IAllowRESTParametersOrExecution Fetch([URL] string url);
        [Settings(Description = "Sets the verb of the request being fetched", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithVerb(string method);
        [Settings(Description = "Sets the body of the request being fetched", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithBody(string body);
        [Settings(Description = "Sets the content type of the body of the request being fetched", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithContentType(string contentType);
        [Settings(Description = "Sets extra headers for the request being fetched, flattened to name, value, name, value", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithHeaders(string[] headers);
        [Settings(Description = "True to bring the fetched response body back base64 encoded, which is what a file needs", Submenu = "Request")]
        IAllowRESTParametersOrExecution WithBytes(bool bytes);
        [Data(Description = "Gets the window page offset X", Submenu = "Window")]
        IAllowRESTExecution WindowPageOffsetX();
        [Data(Description = "Gets the window page offset Y", Submenu = "Window")]
        IAllowRESTExecution WindowPageOffsetY();
        [Data(Description = "Gets the window screen left position", Submenu = "Window")]
        IAllowRESTExecution WindowScreenLeft();
        [Data(Description = "Gets the window screen top position", Submenu = "Window")]
        IAllowRESTExecution WindowScreenTop();
        [Action(Description = "Hovers over an element by ID", Submenu = "Mouse")]
        IAllowRESTExecution Hover(string elementId);
        [Data(Description = "Evaluates an XPath expression", Submenu = "Find Elements")]
        IAllowRESTExecution EvaluatePersistent([XPath] string xpath);
        [Data(Description = "Evaluates an XPath expression for multiple elements", Submenu = "Find Elements")]
        IAllowRESTExecution EvaluateAllPersistent([XPath] string xpath);
        [Data(Description = "Queries a single element using a CSS selector", Submenu = "Find Elements")]
        IAllowRESTExecution QueryPersistentSelector([Css] string css);
        [Data(Description = "Queries multiple elements using a CSS selector", Submenu = "Find Elements")]
        IAllowRESTExecution QueryPersistentSelectors([Css] string css);
        [Data(Description = "Evaluates an XPath expression", Submenu = "Find Elements")]
        IAllowRESTExecution Evaluate([XPath] string xpath);
        [Data(Description = "Evaluates an XPath expression for multiple elements", Submenu = "Find Elements")]
        IAllowRESTExecution EvaluateAll([XPath] string xpath);
        [Data(Description = "Queries a single element using a CSS selector", Submenu = "Find Elements")]
        IAllowRESTExecution QuerySelector([Css] string css);
        [Data(Description = "Queries multiple elements using a CSS selector", Submenu = "Find Elements")]
        IAllowRESTExecution QuerySelectors([Css] string css);
        IAllowRESTExecution ScrollWindow(int hPixels, int vPixels);
        [Browser(Description = "Top the browser, bring it into focus", Submenu = "Windows")]
        IAllowRESTExecution TopBrowser();
        [Action(Description = "Clears the referrer", Submenu = "Referrer and User Agent")]
        IAllowRESTExecution ClearReferrer();


        // Multi-parameter methods with fluent steps
        [Action(Description = "Click a Select menu option", Submenu = "Clicks")]
        IAllowSelectOptions SelectClick(string elementId);
        [Action(Description = "Starts setting an attribute of an element by ID", Submenu = "Input")]
        IAllowRESTAttribute SetAttribute(string elementId);
        [Scroll(Description = "Starts scrolling an element by ID")]
        IAllowRESTScrollElement ScrollElement(string elementId);
        IAllowRESTExecution ScrollElement(int hPixels, int vPixels);
        [Action(Description = "Drags an element from its current position by the given offset and drops it", Submenu = "Mouse")]
        IAllowRESTDragAndDrop DragAndDrop(string elementId);
        [Data(Description = "Starts getting an attribute of an element by ID", Submenu = "Element")]
        IAllowRESTAttribute GetAttribute(string elementId); // Returns IAllowRESTAttribute, skips WithValue
    }

    public interface IRESTClient : IAllowRESTEndpoint, IAllowRESTEndpointDetails, IAllowRESTMethod, IAllowRESTScrollElement, IAllowRESTDragAndDrop, IAllowRESTExecution, IAllowRESTParametersOrExecution
        , IAllowCheckNetworkIdleOrExecution, IAllowRESTAttribute, IAllowRESTParameters, IAllowRESTInputText, IAllowRESTWorkflow, IAllowSelectOptions
        , IAllowRESTWithDownloadFilename, IAllowRESTWithUploadFilename, IAllowRESTStorageOptions, IAllowRESTCaptureOptions, IAllowRangeValue, IAllowRestMethods, IAllowToGPALObject<IRESTClient>
    {
        string Name { [Data(Description = "Gets or sets the client name")] get; }

        /// <summary>The HTTP status code the last call came back with.</summary>
        int StatusCode { [Data(Description = "The HTTP status code the last call came back with")] get; }

        /// <summary>Seconds the last call was told to wait before retrying; zero when not told.</summary>
        int RetryAfterSeconds { [Data(Description = "The seconds the last call was asked to wait before asking again, zero when it was not asked")] get; }
    }
    #endregion REST
    #region GPALElement
    public interface IGPALElement
    {
        // Full element contract. GPALElement implements every member, and signatures use only GPAL and BCL types, never
        // Selenium. Program against IGPALElement; cast to GPALElement only for plumbing (WrappedDriver, WebElement,
        // node ids, ToDictionary, Dispose).
        string TagName { get; }
        string Text { get; }
        bool Enabled { get; }
        bool Selected { get; }
        bool? Checked { get; }
        bool Displayed { get; }
        Point Location { get; }
        Size Size { get; }
        string GetAttribute(string attributeName);
        void SetAttribute(string attributeName, string value);
        string GetCssValue(string propertyName);
        string GetDomAttribute(string attributeName);
        string GetDomProperty(string propertyName);
        void Click();
        void Click(ModifierKeys modifierKeys);
        void MiddleClick(ModifierKeys modifierKeys = ModifierKeys.NONE);
        void Clear();
        void SendKeys(string text);
        void Submit();
        void Hide();
        Dictionary<string, object> Attributes { get; }
        Dictionary<string, object> CssAttributes { get; }
        Dictionary<string, object> DomAttributes { get; }
        Dictionary<string, object> DomProperties{ get; }
        string AttributeName { get; }
        string Href { get; }
        string Src { get; }
        string Value { get; }
        string Placeholder { get; }
        string Xpath { get; }
        XPathInfo[] Xpaths { get; }
        string Css { get; }
        string FullXPath { get; }
        //string SelectorName { get; set; }
        //int OffsetX { get; set; }
        //int OffsetY { get; set; }
        //int DeltaX { get; set; }
        //int DeltaY { get; set; }
        //string ImageData { get; set; }
        ClientRectangle BoundingRect { get; }
        string PageURL { get;  }
        GPALElement ShadowRoot { get; }
        dynamic WebElement { get; }
        bool IsClickable(bool publishEvent = true);
        bool IsDisabled();
        bool IsEnabled();

    }
    #endregion GPALElement
    #region Cryptography
    public interface IAllowCryptoGenerateIV
    {
        [Action(Description = "Generate a random initialization vector")]
        IAllowCryptoConfig GenerateIV(out byte[] iv);
    }

    public interface IAllowCryptoInput
    {
        [Settings(Description = "The data to work on")]
        IAllowCryptoAction WithInputData(byte[] data);
    }

    public interface IAllowCryptoEncrypt
    {
        [Action(Description = "Encrypt the data")]
        IAllowCryptoConfig Encrypt(out byte[] encryptedData);
    }

    public interface IAllowCryptoDecrypt
    {
        [Action(Description = "Decrypt the data")]
        IAllowCryptoConfig Decrypt(out string decryptedValue);
    }

    public interface IAllowCryptoSign
    {
        [Action(Description = "Sign the data")]
        IAllowCryptoConfig Sign(out byte[] signature);
    }

    public interface IAllowCryptoVerify
    {
        [Action(Description = "Check the signature against the data")]
        IAllowCryptoConfig Verify(out bool isValid);
    }

    public interface IAllowCryptoHash
    {
        [Action(Description = "Hash the data")]
        IAllowCryptoConfig Hash(out byte[] hash);
    }

    public interface IAllowCryptoGenerateKey
    {
        [Action(Description = "Generate a random key")]
        IAllowCryptoConfig GenerateKey(out byte[] key);
    }

    public interface IAllowCryptoConfig :
        IAllowCryptoInput,
        IAllowCryptoGenerateIV,
        IAllowCryptoGenerateKey,
        IAllowCryptoAction
    {
        [Settings(Description = "The algorithm to use")]
        IAllowCryptoConfig WithAlgorithm(CryptoAlgorithm alg);
        [Settings(Description = "The key to use")]
        IAllowCryptoConfig WithKey(byte[] key);
        [Settings(Description = "The initialization vector to use")]
        IAllowCryptoConfig WithIV(byte[] iv);
        [Settings(Description = "The cipher mode")]
        IAllowCryptoConfig WithMode(CryptoMode mode);
        [Settings(Description = "The padding mode")]
        IAllowCryptoConfig WithPadding(CryptoPadding padding);
        [Settings(Description = "The salt for deriving a key from a password")]
        IAllowCryptoConfig WithSalt(byte[] salt);
        [Settings(Description = "How many rounds of key derivation")]
        IAllowCryptoConfig WithIterationCount(int count);
        [Settings(Description = "The signature to verify")]
        IAllowCryptoConfig WithSignature(byte[] signature);
    }

    public interface IAllowCryptoKeyGenInput :
        IAllowCryptoInput,
        IAllowCryptoGenerateKey
    {
    }

    public interface IAllowCryptoAction :
        IAllowCryptoGenerateKey,
        IAllowCryptoGenerateIV,
        IAllowCryptoEncrypt,
        IAllowCryptoDecrypt,
        IAllowCryptoSign,
        IAllowCryptoVerify,
        IAllowCryptoHash
    {
    }

    public interface ICryptography : IAllowCryptoAction, IAllowCryptoKeyGenInput, IAllowCryptoConfig, IAllowToGPALObject<ICryptography>
    {
    }
    #endregion Cryptography
    #region Gherkin
    public interface IAllowGiven
    {
        [Action(Description = "Runs the 'Given' precondition step immediately")]
        IAllowWhen Given(Action step);
        IAllowWhen Given<T1>(Action<T1> step, T1 arg1);
        IAllowWhen Given<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2);
        IAllowWhen Given<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3);
    }

    public interface IAllowWhen
    {
        [Fluent(Description = "Runs the 'When' action step immediately")]
        IAllowAnd When(Action step);
        IAllowAnd When<T1>(Action<T1> step, T1 arg1);
        IAllowAnd When<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2);
        IAllowAnd When<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3);
    }

    public interface IAllowAnd
    {
        [Fluent(Description = "Runs an additional 'And' step immediately")]
        IAllowThen And(Action step);
        IAllowThen And<T1>(Action<T1> step, T1 arg1);
        IAllowThen And<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2);
        IAllowThen And<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3);
    }

    public interface IAllowThen : IAllowAnd
    {
        [Fluent(Description = "Runs the 'Then' assertion/outcome step immediately")]
        IAllowThen Then(Action step);
        IAllowThen Then<T1>(Action<T1> step, T1 arg1);
        IAllowThen Then<T1, T2>(Action<T1, T2> step, T1 arg1, T2 arg2);
        IAllowThen Then<T1, T2, T3>(Action<T1, T2, T3> step, T1 arg1, T2 arg2, T3 arg3);
    }
    #endregion Gherkin
    #region <Request Settings>
    [Interface(Description = "Describe an API request for .Fetch to issue from inside the page")]
    public interface IAllowRequestSettings : IAllowToGPALObject<GPALRequest>
    {
        [Settings(Description = "Replace a query parameter's value with the next numbered token")]
        IAllowRequestSettings WithTokenFor(string parameterName);
        [Settings(Description = "Path or full URL to request. A path rides the origin the browser is already on")]
        IAllowRequestSettings WithPath(string path);
        [Settings(Description = "HTTP method, for example GET, POST, PUT, DELETE. [GET (default)]")]
        IAllowRequestSettings WithHttpMethod(Enums.HttpVerb method);
        [Settings(Description = "Add a query parameter written as \"name=value\". Call once per parameter")]
        IAllowRequestSettings WithParameter(string nameValue);
        [Settings(Description = "Add a request header written as \"Name: value\". Call once per header")]
        IAllowRequestSettings WithHeader(string nameValue);
        [Settings(Description = "Body to send, used as-is")]
        IAllowRequestSettings WithBody(string body);
        [Settings(Description = "Content type of the body. [application/json (default)]")]
        IAllowRequestSettings WithContentType(Enums.ContentType contentType);
        [Settings(Description = "What this API calls its first page, which the page token counts on from. [0 (default)]")]
        IAllowRequestSettings WithFirstPage(int firstPage);
        [Settings(Description = "The marker replaced with the page number, wherever it appears in the path, a parameter value, a header value or the body. Give it a numbered token to hand GPAL one of the token positions. [{page} (default)]")]
        IAllowRequestSettings WithPageToken(string token = "{page}");
        [Settings(Description = "Handler called after each row of tokens is fetched, with that row's pages. Return -1 to stop")]
        IAllowRequestSettings CallAfterFetch(Browser.Browser.CallAfterFetchDelegate callAfterFetch);
        [Settings(Description = "Name of request (used in messages and errors)")]
        IAllowToGPALObject<GPALRequest> WithName(string name);
    }
    #endregion <Request Settings>
    public interface IAllowToGPALObject<TResult>
    {
        [Fluent(Description = "Ends a declaration or workflow, returning a GPAL object which can be assigned. Use instead of an explicit cast. [ Selector mySel = GPAL.Selector.WithCSS(css).ToGPALObject() ]")]
        TResult ToGPALObject();
    }

    #region YouTube
    public interface IGPALYouTube
    {
        // Upload phase
        [Settings(Description = "The Google account to upload as")]
        IGPALYouTube WithCredentials(ICredentials credentials);
        [Settings(Description = "The title of the video")]
        IGPALYouTube WithTitle(string title);
        [Settings(Description = "The description of the video")]
        IGPALYouTube WithDescription(string description);
        [Settings(Description = "The tags for the video")]
        IGPALYouTube WithTags(params string[] tags);
        [Settings(Description = "Public, unlisted or private")]
        IGPALYouTube WithPrivacy(Enums.YouTubePrivacy privacy);
        [Settings(Description = "The YouTube category for the video")]
        IGPALYouTube WithCategory(Enums.YouTubeCategory category);
        [Action(Description = "Upload the video file with the title, description and settings given")]
        Browser.YouTubeUploadResult Upload(GPALFile file);
        // Schedule phase: collect upload results, set poll interval, then ScheduleAt fires a background thread
        [Settings(Description = "The uploaded video the next steps work on")]
        IGPALYouTube WithVideo(Browser.YouTubeUploadResult result);
        [Settings(Description = "The uploaded videos the next steps work on")]
        IGPALYouTube WithVideos(IEnumerable<Browser.YouTubeUploadResult> results);
        [Wait(Description = "Wait for YouTube to finish processing, checking every so many seconds")]
        IGPALYouTube WaitForProcessing(int pollIntervalSeconds = 60);
        [Settings(Description = "Pipes scheduling status to a TextBox or RichTextBox on a WinForms form")]
        IGPALYouTube WithOutputTo(System.Windows.Forms.TextBoxBase control);
        [Action(Description = "Starts a background thread that polls each video's processing status and schedules a premiere as each one completes")]
        IGPALYouTube ScheduleAt(Browser.PremiereTime premiereTime);
        [Wait(Description = "Blocks the calling thread until ScheduleAt finishes scheduling all videos")]
        void WaitForCompletion();
        // Playlist phase
        [Settings(Description = "The playlist the next steps work on")]
        IGPALYouTube WithPlaylist(string playlistId);
        [Action(Description = "Create the playlist")]
        Browser.YouTubePlaylistResult CreatePlaylist();
        [Action(Description = "Add the videos to the playlist")]
        IGPALYouTube AddToPlaylist(string playlistId);
        // Lookup / Search phase
        [Settings(Description = "The id of an existing video to work on")]
        IGPALYouTube WithVideoId(string videoId);
        [Settings(Description = "What to search YouTube for")]
        IGPALYouTube WithSearchTerms(string query);
        [Settings(Description = "What to search YouTube for")]
        IGPALYouTube WithSearchTerms(IGPALGrid<string> grid);
        [Data(Description = "The videos the search found")]
        IGPALYouTube GetResults(out Browser.YouTubeVideoInfo info);
        [Data(Description = "The videos the search found")]
        IGPALYouTube GetResults(out System.Collections.Generic.List<Browser.YouTubeVideoInfo> results);
        // Channel management phase
        [Data(Description = "Every video on your channel, with its privacy, category and thumbnail")]
        IGPALYouTube GetMyVideos(out System.Collections.Generic.List<Browser.YouTubeVideoInfo> videos);
        [Data(Description = "Every playlist on your channel")]
        IGPALYouTube GetMyPlaylists(out System.Collections.Generic.List<Browser.YouTubePlaylistInfo> playlists);
        [Data(Description = "Every entry in the playlist, in playlist order")]
        IGPALYouTube GetPlaylistItems(out System.Collections.Generic.List<Browser.YouTubePlaylistItem> items);
        [Settings(Description = "The place in the playlist, counted from 0, that the videos are added at. [the end (default)]")]
        IGPALYouTube WithPosition(int position);
        [Action(Description = "Take the videos out of the playlist")]
        IGPALYouTube RemoveFromPlaylist();
        [Action(Description = "Change the video's title, description, tags, category or privacy, keeping whatever is not given")]
        IGPALYouTube UpdateVideo();
        [Action(Description = "Set the image as the video's thumbnail")]
        IGPALYouTube SetThumbnail(GPALFile image);
        [Action(Description = "Delete the video from YouTube")]
        IGPALYouTube DeleteVideo();
        [Action(Description = "Upload a new file in place of the video, keeping its details, thumbnail and playlist places")]
        Browser.YouTubeUploadResult ReplaceVideo(GPALFile newVideo);
    }
    #endregion YouTube

}
