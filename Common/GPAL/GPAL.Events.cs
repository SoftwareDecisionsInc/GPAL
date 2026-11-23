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
        /// Used to pass objects of interest when publishing an Information or Exception Channel message<br/>
        /// Properties are contextual to where the Information/Exception is published.<br/>
        /// Only objects relevant to that context will be passed as an event argument, the rest will be null.<br/>
        /// In this way, the programmer can examine aspects when the message is published and utilize those for logging or debugging or fault-tolerance handling.<br/>
        /// <b>Every event will have a GPALObject as applicable to the context publishing the message.</b>
        /// </summary>
        public class GPALEventArgs : System.EventArgs
        {
            public Enums.GPALEventType GPALEventType { get; set; }
            /// <summary>
            /// Current context's Application, Browser, GPALForm, UnitOfWork object
            /// </summary>
            public dynamic GPALObject { get; set; }
            /// <summary>
            /// GPALObjectType enumeration: None, Application, Browser, GPALForm, UnitOfWork
            /// </summary>
            public Enums.GPALObjectType GPALObjectType { get; set; }
            /// <summary>
            /// The Current Unit of Work for the GPALObject supplied.<br/>
            /// GPALObject (depending upon context) might also be the CurrentUOW, in which case it will also be here.
            /// </summary>
            public UnitOfWork CurrentUOW { get; set; }
            /// <summary>
            /// The Message being published to the Information/Exception channel.
            /// </summary>
            public string Message { get; set; }
            /// <summary>
            /// The Exception being published on the Exception channel.
            /// </summary>
            public Exception ExceptionRaised { get; set; }
            /// <summary>
            /// Every message is accompanied by an automatic screenshot in case this additional information provides some insight as to what GPAL is currently doing.
            /// </summary>
            public Image ScreenShot { get; set; }
            /// <summary>
            /// The browser driver instantiated and used to connect to the browser.
            /// </summary>
            public IWebDriver BrowserDriver { get; set; }
            /// <summary>
            /// The current Selector being used
            /// </summary>
            public Selector Selector { get; set; }
            /// <summary>
            /// The current web element
            /// </summary>
            public GPALElement GPALElement { get; set; }
            /// <summary>
            /// List of WebElements returned for the given Browser Selector
            /// </summary>
            public List<GPALElement> WebElements { get; set; }
            /// <summary>
            /// The current AutomationElement
            /// </summary>
            public AutomationElement AutomationElement { get; set; }
            /// <summary>
            /// List of AutomationElements returned for the given Application Selector
            /// </summary>
            public List<GPALAutomationElement> AutomationElements { get; set; }
            /// <summary>
            /// The SelectorPathEntry is the current path (css, xpath, text, image, etc) being evaluated/used to find/handle an element
            /// A selector may have multiple selector paths in order to evaluate/handle an element
            /// </summary>
            public SelectorSettings.SelectorPathEntry SelectorPathEntry { get; set; }
            /// <summary>
            /// GPALGrid [rows/columns] of tokens being used to fill input
            /// </summary>
            public IGPALGrid<string> Tokens { get; set; }
            /// <summary>
            /// List of Grids associated with GPALFile
            /// </summary>
            public List<IGPALGrid<string>> TokenList { get; set; }
            /// <summary>
            ///  SqlCommand which threw an exception
            /// </summary>
            public SqlCommand SqlCommand { get; set; }
            /// <summary>
            /// Any type of data that does not fite in the above, converter data, for example
            /// </summary>
            public dynamic Data { get; set; }
            /// <summary>
            /// The date/time this event is published
            /// </summary>
            public DateTime DateTimeStamp { get; set; }
        }

        internal static EventHandler<GPALEventArgs> ExceptionEventHandler { get; set; }
        internal static EventHandler<GPALEventArgs> InformationEventHandler { get; set; }
        /// <summary>
        /// Your Exception channel handler, if defined, GPAL will publish exceptions to this channel.<br/>
        /// <b>Used internally</b>
        /// </summary>
        /// <example>
        /// <code>
        /// // To publish a simple message with the base GPALObject,<br/>
        /// // use GPAL.PublishSimpleEvent
        /// GPAL.GPALEventArgs args = new GPAL.GPALEventArgs()
        /// {
        ///     Tokens = tokens,
        ///     DatabaseSettings = databaseSettings,
        ///     Message = $"Insert: Exception thrown: {ex.Message}",
        ///     ExceptionRaised = ex,
        ///     ScreenShot = GPAL.CaptureScreen()
        /// };
        /// GPAL.ExceptionHandler?.Invoke(tokens, args);
        /// </code>
        /// </example>
        internal static EventHandler<GPALEventArgs> ExceptionHandler
        {
            get
            {
                return ExceptionEventHandler;
            }
        }
        /// <summary>
        /// Your Information channel handler, if defined, GPAL will publish exceptions to this channel<br/>
        /// <b>Used internally</b>
        /// </summary>
        /// <example>
        /// <code>
        /// // To publish a simple message with the base GPALObject only,<br/>
        /// // use GPAL.PublishSimpleEvent
        /// GPAL.GPALEventArgs args = new GPAL.GPALEventArgs()
        /// {
        ///     GPALObject = application,
        ///     GPALObjectType = GPALObjectType.Application,
        ///     CurrentUOW = currentUOW,
        ///     Selector = selector,
        ///     Message = $"{GPAL.MyMethodName()}: Element has Zero Width and Height, skipping",
        ///     ScreenShot = GPAL.CaptureScreen()
        /// };
        /// GPAL.InformationHandler?.Invoke(application, args);
        /// </code>
        /// </example>
        internal static EventHandler<GPALEventArgs> InformationHandler
        {
            get
            {
                return InformationEventHandler;
            }
        }
        internal static List<IGPALLogger> PublishToLoggers
        {
            get
            {
                return GPALSettings.PublishToLoggers;
            }
        }
        // The text area set by WithPublishToControl, or null
        internal static GPALTextArea PublishControl
        {
            get
            {
                return GPALSettings.PublishControl;
            }
        }

        // Rolling line cap, so a long run does not grow the control without bound
        private const int PublishControlMaxLines = 2_000;

        internal static bool PublishToConsole
        {
            get
            {
                return GPALSettings.PublishToConsole;
            }
            set
            {
                GPALSettings.PublishToConsole = value;
            }
        }
        internal static bool PublishToDebug
        {
            get
            {
                return GPALSettings.PublishToDebug;
            }
        }
        internal static GPALEventType ConsoleEvents
        {
            get => GPALSettings.ConsoleEvents;
            set => GPALSettings.ConsoleEvents = value;
        }
        internal static GPALEventType DebugEvents
        {
            get => GPALSettings.DebugEvents;
        }
        internal static bool PublishStackTrace
        {
            get
            {
                return GPALSettings.PublishStackTrace;
            }
        }
        /// <summary>
        /// Take a screenshot
        /// </summary>
        /// <example>
        /// <code>
        /// Bitmap screenshot = GPAL.CaptureScreen();</code></example>
        /// <returns>Bitmap of the screen</returns>
        [Helper(Description = "Take a screenshot and keep it on the browser's settings")]
        public static IAllowGraphicSettings CaptureScreen(Browser.IBrowser browser)
        {
            ((ImageHelper)GPAL.ImageHelper).CaptureScreen(browser).ToBitmap(out ((Browser.Browser)browser).BrowserSettings.ScreenShot);

            return ImageHelper;
        }
        /// <summary>
        /// Returns the calling methods name (used in Information/Exception messages).<br/>
        /// Instead of harcoding method names, call MyMethodName.
        /// </summary>
        /// <example>
        /// <code>
        /// Message = $"{GPAL.MyMethodName()}: Exception: Unable to load image. {ex.Message}"
        /// </code>
        /// </example>
        /// <returns>
        /// Calling method's name
        /// </returns>
        // https://stackoverflow.com/questions/44153/can-you-use-reflection-to-find-the-name-of-the-currently-executing-method/67372375#67372375
        [MethodImpl(MethodImplOptions.NoInlining)]
        [Helper(Description = "The name of the method that called it, for event messages")]
        public static string MyMethodName()
        {
            var st = new StackTrace();
            var sf = st.GetFrame(1);
            string name = sf.GetMethod().Name;

            if (name.Equals("MoveNext"))
            {
                // We're inside an async method
                name = sf.GetMethod().ReflectedType.Name
                         .Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            else if (name.StartsWith("<"))
            {
                // a lambda, named <Method>b__N_N by the compiler; report it by the method it is in
                name = name.Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            else if (true == sf.GetMethod().IsSpecialName && (name.StartsWith("get_") || name.StartsWith("set_")))
            {
                // a property like PageDown or NextTab, report it by the property name
                name = name.Substring(4);
            }

            return name;
        }
        /// <summary>
        /// Publish a message to either the information channel or exception channel (if exception passed in)
        /// Publish will insert the calling methods name, so there is no need to call GPAL.MyMethodName()
        /// A screenshot is published as well as the Browser/Application object to do what you want with in your handler
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.PublishSimpleEvent(GPALEventType.INFO, "An Information Channel Message.", application, GPALObjectType.Application);
        /// GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "An Exception Channel Message.", browser, GPALObjectType.Browser, exception);
        /// </code>
        /// </example>
        /// <param name="gPALEventType">The event type to publish, such as INFO or EXCEPTION</param>
        /// <param name="msg">Message to publish</param>
        /// <param name="gPALObject">GPAL object [Application, Browser, GPALForm, UnitOfWork]</param>
        /// <param name="gPALObjectType">The type of GPAL object passed in</param>
        /// <param name="ex">[Optional]Exception raised</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        [Helper(Description = "Publish an event to the information or exception handlers")]
        public static void PublishSimpleEvent(GPALEventType gPALEventType, string msg, dynamic gPALObject = null, Enums.GPALObjectType gPALObjectType = GPALObjectType.None, Exception ex = null)
        {
            // Return before the costly StackTrace and reflection when no destination wants this event type; each
            // destination has its own mask (ConsoleEvents and DebugEvents for console and debug).
            // An event with an exception or of type EXCEPTION goes to the exception channel, decided once here for both
            // the mask and the handler.
            bool toExceptionChannel = null != ex || true == gPALEventType.HasFlag(GPALEventType.EXCEPTION);

            // Check only the handler for this event's channel.
            bool handlerWants = true == toExceptionChannel
                              ? null != ExceptionHandler && gpalSettings.ExceptionHandlerEvents.HasFlag(gPALEventType)
                              : null != InformationHandler && gpalSettings.InformationHandlerEvents.HasFlag(gPALEventType);
            bool consoleWants = true == GPAL.PublishToConsole && gpalSettings.ConsoleEvents.HasFlag(gPALEventType);
            bool debugWants = true == GPAL.PublishToDebug && gpalSettings.DebugEvents.HasFlag(gPALEventType);
            bool controlWants = null != GPAL.PublishControl && gpalSettings.ControlEvents.HasFlag(gPALEventType);
            bool loggerWants = 0 < GPAL.PublishToLoggers.Count;

            if (false == handlerWants && false == consoleWants && false == debugWants && false == controlWants && false == loggerWants)
                return;

            if (true == handlerWants || true == controlWants || true == consoleWants || true == debugWants || true == loggerWants)
            {
                var st = new StackTrace();
                var sf = st.GetFrame(1);
                string name = sf.GetMethod().Name;
                DateTime dateTime = DateTime.Now;
                string stackTrace = null != ex && true == GPAL.PublishStackTrace ? $"[{ex.StackTrace}]" : string.Empty;

                if (name.Equals("MoveNext"))
                {
                    // We're inside an async method
                    name = sf.GetMethod().ReflectedType.Name
                             .Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
                }
                else if (name.StartsWith("<"))
                {
                    // a lambda, named <Method>b__N_N by the compiler; log it by the method it is in
                    name = name.Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
                }
                else if (true == sf.GetMethod().IsSpecialName && (name.StartsWith("get_") || name.StartsWith("set_")))
                {
                    // a property like PageDown or NextTab, log it by the property name
                    name = name.Substring(4);
                }

                // padded inside the tag to the length of WARNING, CAUTION and FAILURE, so the method names line up down
                // the log. EXCEPTION and DEEPDEBUG run two past it, which makes an exception easy to spot
                string outMessage = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffff")}][{gPALEventType,-7}][{name}]: {msg}";
                Bitmap ss = null;

                // a screen image is captured only on error or exception
                if (GPALEventType.ERROR == gPALEventType || GPALEventType.EXCEPTION == gPALEventType)
                    if (false == inPublishEvent && false == handlingTermination)
                    {
                        try
                        {
                            inPublishEvent = true;  // avoid reentrant calls from events published in different paths calling capture screen
                            if (true == gpalSettings.TakeEventScreenshot)
                                if (typeof(Browser.Browser) == gPALObject?.GetType())
                                    ((ImageHelper)GPAL.ImageHelper).CaptureScreen(gPALObject).ToBitmap(out ss);
                                else
                                    ((ImageHelper)GPAL.ImageHelper).CaptureScreen().ToBitmap(out ss);
                        }
                        catch // (Exception ex2)
                        {
                            //    Console.WriteLine($"Unable to get screenshot in PublishSimpleEvent. [{ex2.Message}]");
                            //    Debug.WriteLine($"Unable to get screenshot in PublishSimpleEvent. [{ex2.Message}]");
                            // A failed screenshot is ignored; an OttoMagic run has nothing started to call capture-visible-tab.
                        }
                        finally
                        {
                            inPublishEvent = false;
                        }
                    }

                GPALEventArgs args = new GPALEventArgs()
                {
                    GPALEventType = gPALEventType,
                    GPALObject = gPALObject,
                    GPALObjectType = gPALObjectType,
                    CurrentUOW = (Enums.GPALObjectType.UnitOfWork == gPALObjectType ? (UnitOfWork)gPALObject : null),
                    Message = $"{name}: {msg}",
                    ScreenShot = ss,
                    ExceptionRaised = ex,
                    Data = (Enums.GPALObjectType.Converter == gPALObjectType ? gPALObject : string.Empty),
                    DateTimeStamp = dateTime
                };

                // Same mask as the early-out above
                if (true == handlerWants)
                {
                    if (true == toExceptionChannel)
                        ExceptionHandler?.Invoke(gPALObject, args);
                    else
                        InformationHandler?.Invoke(gPALObject, args);
                }

                if (true == controlWants)
                {
                    GPAL.PublishControl.AppendLine(outMessage, PublishControlMaxLines);

                    // Exception text on a second line, under the same DEBUG rule the console below uses
                    if (null != ex && true == gpalSettings.ControlEvents.HasFlag(GPALEventType.DEBUG))
                        GPAL.PublishControl.AppendLine($"[{ex.Message}]{stackTrace}", PublishControlMaxLines);
                }

                if (true == GPAL.PublishToConsole)
                {
                    // the GPAL REST API runs with GPAL_IS_SILENT=1 and must get nothing on stdio
                    if (gpalSettings.ConsoleEvents.HasFlag(gPALEventType) && Environment.GetEnvironmentVariable("GPAL_IS_SILENT") != "1")
                    {
                        Console.WriteLine(outMessage);

                        // Exception text and stack trace are only written when DEBUG is on.
                        if (null != ex && gpalSettings.ConsoleEvents.HasFlag(GPALEventType.DEBUG))
                            Console.WriteLine($"[{ex.Message}]{stackTrace}");
                    }
                }

                if (true == GPAL.PublishToDebug)
                {
                    if (gpalSettings.DebugEvents.HasFlag(gPALEventType) && Environment.GetEnvironmentVariable("GPAL_IS_SILENT") != "1")
                    {
                        Debug.WriteLine(outMessage);

                        // Exception text and stack trace only when DEBUG is on, as above.
                        if (null != ex && gpalSettings.DebugEvents.HasFlag(GPALEventType.DEBUG))
                            Debug.WriteLine($"[{ex.Message}]{stackTrace}");
                    }
                }

                // The logger decides where to write and whether it accepts this type. Pass the event's own type.
                // Guarded because the logger's own events would come straight back here.
                if (0 < GPAL.PublishToLoggers.Count && true == Enum.TryParse(gPALEventType.ToString(), out Enums.LogType logType))   // Enums. because selenium has a LogType too
                {
                    if (null == loggersWriting)
                        loggersWriting = new HashSet<IGPALLogger>();

                    // Loop over a copy; a logger raising an event could add another logger.
                    foreach (IGPALLogger logger in GPAL.PublishToLoggers.ToList())
                    {
                        // This logger raised this event itself while writing; skip it. Other loggers still get it.
                        if (true == loggersWriting.Contains(logger))
                            continue;

                        try
                        {
                            loggersWriting.Add(logger);

                            // Call Log through the GPALLogger class, not the interface: Log is internal, turning an
                            // event into a log entry.
                            GPALLogger writer = (GPALLogger)logger;

                            // Pass the event type with the entry so two threads cannot swap each other's severity.
                            writer.Log(msg, name, logType);

                            // Log the exception text as a separate DEBUG entry, as the console and debug blocks do. A
                            // log file may be the only record of an unattended run.
                            if (null != ex)
                                writer.Log($"[{ex.Message}]{stackTrace}", name, Enums.LogType.DEBUG);
                        }
                        catch
                        {
                            // A failing logger must not stop the other loggers or the run.
                        }
                        finally
                        {
                            loggersWriting.Remove(logger);
                        }
                    }
                }

                // Flush pending output. The operations that follow can block or delay it, and a workflow writing to the
                // console may not flush its own.
                if (Environment.GetEnvironmentVariable("GPAL_IS_SILENT") != "1")
                {
                    Console.Out.Flush();
                    Debug.Flush();
                }
            }
        }
        /// <summary>
        /// Emits an ASCII table for any 2-axis keyed dictionary as a single INFO event: row keys down the side,
        /// column keys across the top, one cell per (row, col) present in <paramref name="data"/>. Missing cells
        /// show <paramref name="emptyCell"/>. Row/column order is the natural order of the key type (enum value,
        /// numeric, or string). Cells render via <paramref name="formatCell"/>, defaulting to ToString().
        /// </summary>
        /// <param name="title">Heading shown above the table.</param>
        /// <param name="data">Values keyed by (row, col).</param>
        /// <param name="cornerLabel">Label for the top-left corner (the row axis name).</param>
        /// <param name="formatCell">Optional per-value formatter; defaults to ToString().</param>
        /// <param name="emptyCell">Text for a (row, col) pair absent from the data.</param>
        public static void EmitTable<TRow, TCol, TValue>(
            string title,
            IDictionary<(TRow row, TCol col), TValue> data,
            string cornerLabel = "",
            Func<TValue, string> formatCell = null,
            string emptyCell = "-")
        {
            if (null == data || 0 == data.Count)
                return;

            formatCell = formatCell ?? (v => v?.ToString() ?? string.Empty);

            var rows = data.Keys.Select(k => k.row).Distinct().OrderBy(r => r, Comparer<TRow>.Default).ToList();
            var cols = data.Keys.Select(k => k.col).Distinct().OrderBy(c => c, Comparer<TCol>.Default).ToList();

            string Cell(TRow r, TCol c) => data.TryGetValue((r, c), out var v) ? formatCell(v) : emptyCell;

            int rowHeaderWidth = cornerLabel.Length;
            foreach (var r in rows)
                rowHeaderWidth = Math.Max(rowHeaderWidth, (r?.ToString() ?? string.Empty).Length);

            var colWidth = new Dictionary<TCol, int>();
            foreach (var c in cols)
            {
                int w = (c?.ToString() ?? string.Empty).Length;
                foreach (var r in rows)
                    w = Math.Max(w, Cell(r, c).Length);
                colWidth[c] = w;
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"=== {title} ===");

            sb.Append(cornerLabel.PadRight(rowHeaderWidth));
            foreach (var c in cols)
                sb.Append(" | " + (c?.ToString() ?? string.Empty).PadRight(colWidth[c]));
            sb.AppendLine();

            sb.Append(new string('-', rowHeaderWidth));
            foreach (var c in cols)
                sb.Append("-+-" + new string('-', colWidth[c]));
            sb.AppendLine();

            foreach (var r in rows)
            {
                sb.Append((r?.ToString() ?? string.Empty).PadRight(rowHeaderWidth));
                foreach (var c in cols)
                    sb.Append(" | " + Cell(r, c).PadRight(colWidth[c]));
                sb.AppendLine();
            }

            // Publish as NOTICE, the workflow's channel, because the workflow asked for the table.
            PublishSimpleEvent(GPALEventType.NOTICE, sb.ToString());
        }
        /// <summary>
        /// Convenience wrapper over <see cref="EmitTable{TRow,TCol,TValue}"/> for test-program bt/ae[/headless]
        /// loops: browser types down the side, automation engines across the top, each cell the wall-clock
        /// runtime. Call once per headless state with its own title (e.g. "Window" and "Headless").
        /// </summary>
        public static void EmitRuntimeTable(string title, IDictionary<(BrowserType bt, AutomationEngine ae), TimeSpan> runtimes)
            => EmitTable(title + " runtime (min:sec.ms)", runtimes, "Browser",
                         ts => $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}");
        /// <summary>
        /// Print the published information or exception messages to the console.
        /// </summary>
        /// <param name="eventType">The event types to print to the console; NONE turns console output off [all types]</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Print specified GPAL events to the console. [none (default)]")]
        public static IAllowGPALSettings WithPublishToConsole(GPALEventType eventType = GPALEventType.INFO | GPALEventType.WARNING | GPALEventType.ERROR | GPALEventType.EXCEPTION | GPALEventType.NOTICE | GPALEventType.CAUTION | GPALEventType.FAILURE)
        {
            GPALSettings.ConsoleEvents = eventType;
            if (GPALEventType.NONE == eventType)
                GPALSettings.PublishToConsole = false;
            else
                GPALSettings.PublishToConsole = true;
            GPAL.PublishVersion();
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Writes GPAL events to a text area on a form, as <see cref="WithPublishToConsole"/> does to a
        /// console.<br/>
        /// An exception adds a second line carrying its own text, which requires DEBUG in
        /// <see cref="WithControlEvents"/>. All event types are written by default.<br/>
        /// Writing is queued and drained on the thread that owns the control, so publishing from a workflow
        /// thread is safe.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALTextArea logArea = (GPALTextArea)GPAL.TextArea.WithReadOnly(true).WithName("log").ToGPALObject();
        ///
        ///     GPAL.WithPublishToControl(logArea);
        /// </code>
        /// </example>
        /// <param name="control">The text area to write to, or null to stop writing to one</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Write specified GPAL events to a text area on a form. [none (default)]")]
        public static IAllowGPALSettings WithPublishToControl(GPALTextArea control)
        {
            GPALSettings.PublishControl = control;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Which event types reach the control given to <see cref="WithPublishToControl"/>. Defaults to all.<br/>
        /// DEBUG must be included for an exception to write its own text.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithControlEvents(GPALEventType.All &amp; ~GPALEventType.DEEPDEBUG);
        /// </code>
        /// </example>
        /// <param name="eventTypes">The events to write to the control</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach the form control. [all (default)]")]
        public static IAllowGPALSettings WithControlEvents(GPALEventType eventTypes)
        {
            GPALSettings.ControlEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Which event types reach the handler given to <see cref="WithInformationHandler"/>. Defaults to all.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithInformationHandlerEvents(GPALEventType.All &amp; ~GPALEventType.DEEPDEBUG);
        /// </code>
        /// </example>
        /// <param name="eventTypes">The events to hand to the information handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach the information handler. [all (default)]")]
        public static IAllowGPALSettings WithInformationHandlerEvents(GPALEventType eventTypes)
        {
            GPALSettings.InformationHandlerEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Which event types reach the handler given to <see cref="WithExceptionHandler"/>. Defaults to all.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithExceptionHandlerEvents(GPALEventType.EXCEPTION | GPALEventType.ERROR);
        /// </code>
        /// </example>
        /// <param name="eventTypes">The events to hand to the exception handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach the exception handler. [all (default)]")]
        public static IAllowGPALSettings WithExceptionHandlerEvents(GPALEventType eventTypes)
        {
            GPALSettings.ExceptionHandlerEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Indicate which event types to print to the console. Default is none
        /// </summary>
        /// <param name="eventType">The event types to print to debug; NONE turns debug output off [all types]</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Print specified GPAL events to the output window in VisualStudio. [none (default)]")]
        public static IAllowGPALSettings WithPublishToDebug(GPALEventType eventType = GPALEventType.INFO | GPALEventType.WARNING | GPALEventType.ERROR | GPALEventType.EXCEPTION | GPALEventType.NOTICE | GPALEventType.CAUTION | GPALEventType.FAILURE)
        {
            GPALSettings.DebugEvents = eventType;
            if (GPALEventType.NONE == eventType)
                GPALSettings.PublishToDebug = false;
            else
                GPALSettings.PublishToDebug = true;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Print the published information or exception messages to the supplied database.
        /// Uses Database Create sql
        /// One column for each property in GPALEventArgs
        /// </summary>
        /// <param name="logger">publish events to this logger, which writes them wherever it was pointed</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Publish GPAL events to a logger, which writes them to whatever it was given: a file or a database.")]
        public static IAllowGPALSettings WithPublishToLogger(IGPALLogger logger)
        {
            GPALSettings.WithPublishToLogger(logger);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Stops publishing events to a logger.
        /// </summary>
        /// <param name="logger">The logger to stop publishing to</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Stop publishing GPAL events to a logger that was previously given one.")]
        public static IAllowGPALSettings RemoveLogger(IGPALLogger logger)
        {
            GPALSettings.RemoveLogger(logger);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets which event types reach the handlers from <see cref="WithInformationHandler"/> and
        /// <see cref="WithExceptionHandler"/>. Default is all.<br/>
        /// Each wanted event costs a stack capture, and DEEPDEBUG alone is thousands per run, so an on-screen display
        /// should list only what it shows.
        /// </summary>
        /// <example>
        /// <code>
        ///     // everything except DEBUG and DEEPDEBUG, which come in thousands
        ///     GPAL.WithHandlerEvents(GPALEventType.All &amp; ~(GPALEventType.DEBUG | GPALEventType.DEEPDEBUG));
        /// </code>
        /// </example>
        /// <param name="eventTypes">The event types to pass to the handlers.</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach an attached Information or Exception handler. [All (default)]")]
        public static IAllowGPALSettings WithHandlerEvents(GPALEventType eventTypes)
        {
            GPALSettings.InformationHandlerEvents = eventTypes;
            GPALSettings.ExceptionHandlerEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Print the stack trace along with the published GPAL message
        /// </summary>
        /// <param name="trueFalse"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Print stack trace when PublishToConsole or PublishToDebug is true. [true (default)]")]
        public static IAllowGPALSettings WithPublishStackTrace(bool trueFalse = true)
        {
            GPALSettings.PublishStackTrace = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Set the handler to receive handled Exception messages from GPAL.<br/>
        /// GPAL also sets an unhandled exception handler and will publish unhandled exceptions to this channel as well.<br/>
        /// GPAL will terminate on unhandled exceptions after publishing to the channel.
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithExceptionHandler(ExceptionEventHandler)
        ///     .WithInformationHandler(InformationEventHandler)
        ///     .WithTypingDelay(50);
        /// </code></example>
        /// <param name="eventHandler">Your Exception channel event handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Handler to receive GPAL Exception events")]
        public static IAllowGPALSettings WithExceptionHandler(EventHandler<GPALEventArgs> eventHandler)
        {
            ExceptionEventHandler = eventHandler;
            Exception ex = new Exception("Welcome Exception :)");
            GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Welcome to GPAL [{Version}] Exception Channel", null, Enums.GPALObjectType.None, ex);
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Set the handler to receive Information messages from GPAL which you might like to know about.<br/>
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithExceptionHandler(ExceptionEventHandler)
        ///     .WithInformationHandler(InformationEventHandler)
        ///     .WithTypingDelay(50);
        /// </code></example>
        /// <param name="eventHandler">Your Information channel event handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Handler to receive GPAL Information events")]
        public static IAllowGPALSettings WithInformationHandler(EventHandler<GPALEventArgs> eventHandler)
        {
            InformationEventHandler = eventHandler;
            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Welcome to GPAL [{Version}] Information Channel");
            return (IAllowGPALSettings)GPALSettings;
        }

        [GPALSettings(Description = "Take a screenshot with each published event")]
        public static IAllowGPALSettings WithTakeEventScreenshot(bool trueOrFalse)
        {
            GPALSettings.TakeEventScreenshot = trueOrFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
    }
}
