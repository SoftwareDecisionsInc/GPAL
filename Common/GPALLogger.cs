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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{

    /// <summary>
    /// Provides configurable logging to a file (in any format supported by <see cref="GPALConverter"/>) or to a <see cref="GPALDatabase"/>.
    /// Configuration is performed via a fluent interface (<see cref="IAllowLoggingSettings"/>),
    /// allowing the root directory, directory structure, filename pattern, delimiter, log type, and date format to be set before calling <see cref="Log(string, string, LogType)"/>.
    /// Errors and warnings encountered while logging are reported via <see cref="GPAL.PublishSimpleEvent"/> rather than throwing exceptions.
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Logger
    ///     .WithRootDirectory(@"C:\Logs")
    ///     .WithDirectoryStructure(DirectoryStructure.YMD)
    ///     .WithFilename("app.log")
    ///     .WithFilenamePattern(FilenamePattern.YMDHMS)
    ///     .WithLoggingFormat(LogDateFormat.YMD_HMS)
    ///     .WithLogEventTypes(GPALEventType.All);
    /// </code>
    /// </example>
    public class GPALLogger : IGPALLogger
    {
        /// <summary>The root directory under which log files are written. Defaults to the current working directory.</summary>
        private string RootDirectory { get; set; }
        private DirectoryStructure DirectoryStructure { get; set; }
        private string Filename { get; set; }
        private FilenamePattern FilenamePattern { get; set; }
        private string Delimiter { get; set; }
        private GPALDatabase Database { get; set; }
        private LogDateFormat LogDateFormat { get; set; } = LogDateFormat.NOTSET;

        /// <summary>
        /// The folder the last entry was written to, with <see cref="DirectoryStructure"/> applied, e.g. logs\231.
        /// Null until something has been logged to a file.
        /// </summary>
        public string LogDirectory { get; private set; }
        /// <summary>
        /// The name of the file the last entry was written to, with <see cref="FilenamePattern"/> applied,
        /// e.g. DeadMikes_20260819.log. Null until something has been logged to a file.
        /// </summary>
        public string LogFilename { get; private set; }
        /// <summary>
        /// <see cref="LogDirectory"/> and <see cref="LogFilename"/> joined: the file the last entry went into.
        /// Null until something has been logged to a file.
        /// </summary>
        public string LogFullPath { get; private set; }

        private bool noTargetReported = false;

        // One converter per logger. A shared one would be reset by other loggers' writes mid-write.
        private GPALConverter converter = new GPALConverter();
        private GPALEventType LogEventTypes { get; set; } = GPALEventType.All;

        /// <summary>
        /// Returns this instance as an <see cref="IGPALLogger"/>, completing the fluent configuration chain.
        /// </summary>
        /// <returns>This logger, typed as <see cref="IGPALLogger"/>.</returns>
        public IGPALLogger ToGPALObject()
        {
            return this;
        }
        /// <summary>
        /// Initializes a new logger with default settings: root directory set to the current working directory,
        /// no directory structure (<see cref="DirectoryStructure.None"/>), and no filename pattern (<see cref="FilenamePattern.None"/>).
        /// </summary>
        internal GPALLogger()
        {
            IGPALConverter converter = GPAL.Converter.ToGPALObject();
            // Default values
            RootDirectory = Directory.GetCurrentDirectory();
            DirectoryStructure = DirectoryStructure.None;
            FilenamePattern = FilenamePattern.None;
        }
        /// <summary>
        /// Sets which event types this logger writes, whether GPAL or the workflow raised them. Default is all.
        /// </summary>
        /// <param name="logEventTypes">The event types to write</param>
        /// <returns>Fluent interface to define more logging settings</returns>
        public IAllowLoggingSettings WithLogEventTypes(GPALEventType logEventTypes)
        {
            LogEventTypes = logEventTypes;
            return this;
        }

        /// <summary>
        /// Configures the logger to write log entries to a <see cref="GPALDatabase"/> instead of a file.
        /// When set, <see cref="Log(string, string, LogType)"/> writes via <see cref="GPALConverter.SaveTo(IGPALDatabase)"/> instead of to a file on disk.
        /// </summary>
        /// <param name="database">The database to log entries to.</param>
        /// <returns>Fluent interface to continue logger configuration.</returns>
        public IAllowLoggingSettings WithDatabase(GPALDatabase database)
        {
            Database = database;
            return this;
        }
        /// <summary>
        /// Sets the root directory under which log files are written. Subdirectories may be appended below this
        /// based on <see cref="WithDirectoryStructure(DirectoryStructure)"/>. Defaults to the current working directory.
        /// </summary>
        /// <param name="path">The root directory path for log files.</param>
        /// <returns>Fluent interface to continue logger configuration.</returns>
        public IAllowLoggingSettings WithRootDirectory(string path)
        {
            RootDirectory = path;
            return this;
        }

        /// <summary>
        /// Sets the directory structure (e.g. year/month/day) created beneath the root directory for log files.
        /// The required directories are created automatically when a log entry is written.
        /// </summary>
        /// <param name="type">The directory structure to use.</param>
        /// <returns>Fluent interface to continue logger configuration.</returns>
        public IAllowLoggingSettings WithDirectoryStructure(DirectoryStructure type)
        {
            DirectoryStructure = type;
            return this;
        }

        /// <summary>
        /// Sets the base filename (including extension) used for log files. The extension determines the
        /// output <see cref="DataFormat"/> used by <see cref="GPALConverter"/> when writing log entries.
        /// </summary>
        /// <param name="name">The base filename, including extension (e.g. "app.log").</param>
        /// <returns>Fluent interface to continue logger configuration.</returns>
        public IAllowLoggingSettings WithFilename(string name)
        {
            Filename = name;
            return this;
        }

        /// <summary>
        /// Sets a timestamp pattern that is appended to the filename (before the extension) each time a log
        /// file path is computed, allowing log files to be split by time period (e.g. one file per day).
        /// </summary>
        /// <param name="pattern">The timestamp pattern to append to the filename.</param>
        /// <returns>Fluent interface to continue logger configuration.</returns>
        public IAllowLoggingSettings WithFilenamePattern(FilenamePattern pattern)
        {
            FilenamePattern = pattern;
            return this;
        }
        /// <summary>
        /// Sets the field delimiter used when writing log entries to a delimited text format.
        /// Also configures the underlying <see cref="GPALConverter"/>'s input delimiter to match.
        /// </summary>
        /// <param name="delimiter">The character used to separate fields in each log entry.</param>
        /// <returns>Fluent interface to continue logger configuration or logging.</returns>
        public IAllowLoggingSettings WithDelimiter(char delimiter)
        {
            Delimiter = delimiter.ToString();
            converter.ConverterSettings.InDelimiter = delimiter;
            return this;
        }
        /// <summary>
        /// Sets the date/time format applied to the timestamp prepended to each log entry by <see cref="Log(string, string, LogType)"/>.
        /// If not set, <see cref="LogDateFormat.MDY_HMS"/> is used.
        /// </summary>
        /// <param name="logDateFormat">The date/time format to use for log entry timestamps.</param>
        /// <returns>Fluent interface to continue logger configuration or logging.</returns>
        public IAllowLoggingSettings WithLoggingFormat(LogDateFormat logDateFormat)
        {
            LogDateFormat = logDateFormat;
            return this;
        }
        /// <summary>
        /// Computes the full path of the log file to write to, creating any directories required by
        /// <see cref="DirectoryStructure"/> (year/month/day/hour/Julian-day subdirectories under <see cref="RootDirectory"/>),
        /// and appending a timestamp derived from <see cref="FilenamePattern"/> to the filename if one is set.
        /// </summary>
        /// <returns>The full path of the log file to write the next entry to.</returns>
        private string GetLogFilePath()
        {
            string logDirectory = FileHelper.EnsureDirectoryEndsWithBackslash(RootDirectory);
            string year = DateTime.Now.ToString("yyyy");
            string month = DateTime.Now.ToString("MM");
            string day = DateTime.Now.ToString("dd");
            string hr = DateTime.Now.ToString("HH");
            string julian = DateTime.Now.DayOfYear.ToString("000");


            // Create directory if it doesn't exist
            Directory.CreateDirectory(logDirectory);

            // Adjust directory structure based on the selected type
            switch (DirectoryStructure)
            {
                case DirectoryStructure.YM:
                    // Create year and month directories
                    logDirectory = Path.Combine(logDirectory, year);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, month);
                    Directory.CreateDirectory(logDirectory);

                    break;
                case DirectoryStructure.YMD:
                    // Create year, month, and day directories
                    logDirectory = Path.Combine(logDirectory, year);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, month);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, day);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.YMDH:
                    // Create year, month, day, and hour directories
                    logDirectory = Path.Combine(logDirectory, year);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, month);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, day);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, hr);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.MDH:
                    logDirectory = Path.Combine(logDirectory, month);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, day);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, hr);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.MD:
                    logDirectory = Path.Combine(logDirectory, month);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, day);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.M:
                    logDirectory = Path.Combine(logDirectory, month);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.DH:
                    logDirectory = Path.Combine(logDirectory, day);
                    Directory.CreateDirectory(logDirectory);

                    logDirectory = Path.Combine(logDirectory, hr);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.D:
                    logDirectory = Path.Combine(logDirectory, day);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.H:
                    logDirectory = Path.Combine(logDirectory, hr);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.Julian:
                    logDirectory = Path.Combine(logDirectory, julian);
                    Directory.CreateDirectory(logDirectory);
                    break;
                case DirectoryStructure.None:
                    break;
                default:
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to figure out directory structure [{DirectoryStructure}]", this, GPALObjectType.Logger);
                    break;
            }

            // Generate filename based on pattern
            string timestamp = string.Empty;
            string fileExtension = Filename.Split('.').Last();
            string filenameWithoutExtension = Filename.Substring(0, Filename.LastIndexOf('.'));

            switch (FilenamePattern)
            {
                case FilenamePattern.YMDHMSm:
                    timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                    break;
                case FilenamePattern.YMDHMS:
                    timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    break;
                case FilenamePattern.YMDHM:
                    timestamp = DateTime.Now.ToString("yyyyMMddHHmm");
                    break;
                case FilenamePattern.YMDH:
                    timestamp = DateTime.Now.ToString("yyyyMMddHH");
                    break;
                case FilenamePattern.YMD:
                    timestamp = DateTime.Now.ToString("yyyyMMdd");
                    break;
                case FilenamePattern.YM:
                    timestamp = DateTime.Now.ToString("yyyyMM");
                    break;
                case FilenamePattern.MM:
                    timestamp = DateTime.Now.ToString("MM");
                    break;
                case FilenamePattern.Y:
                    timestamp = DateTime.Now.ToString("yyyy");
                    break;
                case FilenamePattern.MDHMSm:
                    timestamp = DateTime.Now.ToString("MMddHHmmssfff");
                    break;
                case FilenamePattern.MDHMS:
                    timestamp = DateTime.Now.ToString("MMddHHmmss");
                    break;
                case FilenamePattern.MDHM:
                    timestamp = DateTime.Now.ToString("MMddHHmm");
                    break;
                case FilenamePattern.MDH:
                    timestamp = DateTime.Now.ToString("MMddHH");
                    break;
                case FilenamePattern.DHMSm:
                    timestamp = DateTime.Now.ToString("ddHHmmssfff");
                    break;
                case FilenamePattern.DHMS:
                    timestamp = DateTime.Now.ToString("ddHHmmss");
                    break;
                case FilenamePattern.DHM:
                    timestamp = DateTime.Now.ToString("ddHHmm");
                    break;
                case FilenamePattern.DH:
                    timestamp = DateTime.Now.ToString("ddHH");
                    break;
                case FilenamePattern.D:
                    timestamp = DateTime.Now.Day.ToString();
                    break;
                case FilenamePattern.HMSm:
                    timestamp = DateTime.Now.ToString("HHmmssfff");
                    break;
                case FilenamePattern.HMS:
                    timestamp = DateTime.Now.ToString("HHmmss");
                    break;
                case FilenamePattern.HM:
                    timestamp = DateTime.Now.ToString("HHmm");
                    break;
                case FilenamePattern.H:
                    timestamp = DateTime.Now.ToString("HH");
                    break;
                case FilenamePattern.MSm:
                    timestamp = DateTime.Now.ToString("mmssfff");
                    break;
                case FilenamePattern.MS:
                    timestamp = DateTime.Now.ToString("mmss");
                    break;
                case FilenamePattern.M:
                    timestamp = DateTime.Now.ToString("mm");
                    break;
                case FilenamePattern.Sm:
                    timestamp = DateTime.Now.ToString("ssfff");
                    break;
                case FilenamePattern.S:
                    timestamp = DateTime.Now.ToString("ss");
                    break;
                case FilenamePattern.m:
                    timestamp = DateTime.Now.ToString("fff");
                    break;
                case FilenamePattern.Julian:
                    timestamp = julian;
                    break;
                case FilenamePattern.None:
                    return Path.Combine(logDirectory, Filename);
                default:
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to determine filename patter [{FilenamePattern}]. Using [{Filename}].", this, GPALObjectType.Logger);
                    return Path.Combine(logDirectory, Filename);
            }

            if (!string.IsNullOrEmpty(timestamp))
                filenameWithoutExtension = $"{filenameWithoutExtension}_{timestamp}";

            return Path.Combine(logDirectory, $"{filenameWithoutExtension}.{fileExtension}");
        }
        /// <summary>
        /// Translates a <see cref="LogDateFormat"/> value into the corresponding <see cref="DateTime"/> format string.
        /// </summary>
        /// <param name="format">The log date format to translate.</param>
        /// <returns>A .NET date/time format string suitable for use with <see cref="string.Format(string, object)"/>.</returns>
        /// <exception cref="ArgumentException">Thrown if <paramref name="format"/> is not a recognized <see cref="LogDateFormat"/> value.</exception>
        public static string GetFormatString(LogDateFormat format)
        {
            switch (format)
            {
                case LogDateFormat.YMD_HMS:
                    return "yyyy-MM-dd HH:mm:ss";
                case LogDateFormat.YMD_HMS_FFF:
                    return "yyyy-MM-dd HH:mm:ss.fff";
                case LogDateFormat.DMY_HMS:
                    return "dd-MM-yyyy HH:mm:ss";
                case LogDateFormat.MDY_HMS:
                    return "MM-dd-yyyy HH:mm:ss";
                case LogDateFormat.DMY:
                    return "dd-MM-yyyy";
                case LogDateFormat.MDY:
                    return "MM-dd-yyyy";
                case LogDateFormat.YMD:
                    return "yyyy-MM-dd";
                case LogDateFormat.HMS:
                    return "HH:mm:ss";
                case LogDateFormat.HM:
                    return "HH:mm";
                case LogDateFormat.MDY_Long:
                    return "MMMM dd, yyyy";
                default:
                    throw new ArgumentException("Unknown LogDateFormat value");
            }
        }
        // One writer at a time: GPAL.Workflow and the Puppeteer receive loop both publish. AppendAllText uses
        // FileShare.Read, so a concurrent write is refused and lost. Static, since two loggers can share a filename.
        private static readonly object writeLock = new object();

        /// <summary>
        /// Writes a log entry to the configured destination (file via <see cref="WithFilename(string)"/> or
        /// database via <see cref="WithDatabase(GPALDatabase)"/>). The entry is prefixed with a timestamp
        /// (formatted per <see cref="WithLoggingFormat(LogDateFormat)"/>), the current <see cref="LogType"/>,
        /// and the name of the calling method (resolved via stack trace, including unwrapping async state-machine
        /// method names). Errors are reported via <see cref="GPAL.PublishSimpleEvent"/> rather than thrown.
        /// </summary>
        /// <param name="logThis">The value or message to log.</param>
        /// <param name="raisedBy">Name of the method that raised the event; when empty, the calling method is read from the stack</param>
        /// <param name="entryType">The type this entry was published as. Passed in rather than read from the
        /// logger, so two threads cannot swap each other's severity between setting it and writing.</param>
        /// <returns>Fluent interface to continue logger configuration or further logging.</returns>
        /// <example>
        /// <code>
        /// GPAL.Logger
        ///     .WithRootDirectory(@"C:\Logs")
        ///     .WithFilename("app.log")
        ///     .WithLogEventTypes(GPALEventType.GPALALL);
        /// </code>
        /// </example>
        internal IAllowLoggingSettings Log(string logThis, string raisedBy, LogType entryType)
        {
            lock (writeLock)
                WriteEntry(logThis, raisedBy, entryType);

            return this;
        }

        private void WriteEntry(string logThis, string raisedBy, LogType entryType)
        {
            // LogType and GPALEventType share names, so map by name. Types not in LogEventTypes are not written.
            if (false == Enum.TryParse(entryType.ToString(), out GPALEventType logEventType) || false == LogEventTypes.HasFlag(logEventType))
                return;

            DateTime currentDate = DateTime.Now;
            string formattedDate;
            string logFilePath = null;

            // Use the publisher's name; the stack here always shows PublishSimpleEvent.
            string name = raisedBy;

            if (true == string.IsNullOrEmpty(name))
            {
                var st = new StackTrace();
                var sf = st.GetFrame(1);

                name = sf.GetMethod().Name;

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
            }

            // One event, one entry. Grids and classes go through the converter.
            List<string> entries = new List<string> { logThis ?? "NULL" };

            if (null != Filename)
            {
                logFilePath = GetLogFilePath();

                // Record where this entry was written, at write time. GetLogFilePath creates folders and reads the
                // clock, so calling it early creates empty folders, and after midnight it names a file never written.
                LogFullPath = logFilePath;
                LogDirectory = Path.GetDirectoryName(logFilePath);
                LogFilename = Path.GetFileName(logFilePath);

                if (LogDateFormat.NOTSET != LogDateFormat)
                    formattedDate = string.Format("{0:" + GetFormatString(LogDateFormat) + "}", currentDate);
                else
                    formattedDate = string.Format("{0:" + GetFormatString(LogDateFormat.MDY_HMS) + "}", currentDate);

                // Write the entry as plain text, without the converter: it would read a "name: value" line as YAML,
                // pick a format from the extension that cannot be appended a line at a time, and publish its own write
                // into this file. Convert a finished log separately.
                string separator = $" {Delimiter ?? "|"} ";
                StringBuilder lines = new StringBuilder();

                foreach (string entry in entries)
                    lines.Append($"{formattedDate}{separator}{entryType}{separator}{name}{separator}{entry}{Environment.NewLine}");

                FileHelper.EnsureFolderFor(logFilePath);
                File.AppendAllText(logFilePath, lines.ToString());
            }
            else if (null != Database)
            {
                // Write one value: the event type, then the entry. The database adds its own timestamp. The table needs
                // one insert parameter.
                IGPALGrid<string> rows = GPAL.Grid.ToGPALObject();

                foreach (string entry in entries)
                    rows.AddRow(new List<string> { $"{entryType} {Delimiter ?? "|"} {entry}" });

                converter.WithInput(rows);
                converter.SaveTo(Database);
            }
            else if (false == noTargetReported)
            {
                // No file or database to write to. Report it once; each report is another event and would repeat for
                // every event in the run.
                noTargetReported = true;

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Nothing logged: this logger was given no file and no database to write to", this, GPALObjectType.Logger);
            }

            // Do not publish that the entry was logged; that event would end up in another logger's file.
        }

        // Helper method to get week number
        private int GetWeekNumber(DateTime date)
        {
            return (date.DayOfYear - 1) / 7 + 1;
        }
    }
}

