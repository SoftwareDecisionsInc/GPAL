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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using static GenerallyPositive.Enums;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization.ObjectGraphTraversalStrategies;
using YamlDotNet.Serialization.ObjectFactories;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using System.Reflection;
using Newtonsoft.Json;
using System.Collections;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace GenerallyPositive
{
    public class GPALConverter : IGPALConverter
    {
        private ConverterSettings _converterSettings = null;

        /// <summary>
        /// The settings (input/output data, formats, delimiters, registered types, etc.) backing this converter's fluent chain.
        /// </summary>
        internal ConverterSettings ConverterSettings
        {
            get
            {
                return _converterSettings;
            }

            set
            {
                _converterSettings = value;
            }
        }
        private System.Threading.Timer _idleTimer;
        private readonly object _timerLock = new object();
        private const int IdleTimeoutMs = 5000;

        internal GPALConverter()
        {
            ConverterSettings = new ConverterSettings();
        }

        private void ResetIdleTimer()
        {
            lock (_timerLock)
            {
                if (_idleTimer == null)
                    _idleTimer = new System.Threading.Timer(_ => { lock (_timerLock) { ConverterSettings.ClearDerived(); } }, null, IdleTimeoutMs, System.Threading.Timeout.Infinite);
                else
                    _idleTimer.Change(IdleTimeoutMs, System.Threading.Timeout.Infinite);
            }
        }

        private void CancelIdleTimer()
        {
            lock (_timerLock)
            {
                _idleTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
        }

        #region Fluent Interface
        /// <summary>
        /// Returns this converter cast to <see cref="IGPALConverter"/> to begin a fluent conversion chain.
        /// </summary>
        /// <returns>This GPALConverter as an IGPALConverter</returns>
        public IGPALConverter ToGPALObject()
        {
            return this;
        }
        /// <summary>
        /// Sets the input to a list of dictionaries (one per input "document"), clearing any previously configured input/output settings.
        /// </summary>
        /// <param name="inputDictionary">List of dictionaries to use as the conversion input</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithInput(List<Dictionary<object, dynamic>> inputDictionary)
        {
            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputClass = null;
            ConverterSettings.InputClassType = null;
            ConverterSettings.InputClassElementType = null;
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            // Clear the other input sources, as WithInput(grid) does, so Convert's dispatch (InputFile, then
            // InputDatabase) does not reprocess a prior file or database input instead of this one.
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = null;
            ConverterSettings.InputDictionary = inputDictionary;
            ConverterSettings.InDataFormat.Add(DataFormat.DICTIONARY);

            return this;
        }
        /// <summary>
        /// Sets the input to the contents of a GPAL database, clearing any previously configured input/output settings.
        /// </summary>
        /// <param name="inputDatabase">The database whose tokenized rows are used as the conversion input</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithInput(IGPALDatabase inputDatabase)
        {
            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputClass = null;
            ConverterSettings.InputClassType = null;
            ConverterSettings.InputClassElementType = null;
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            // a prior file input would otherwise be converted instead, Convert looks at InputFile first
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = inputDatabase;
            ConverterSettings.InDataFormat.Add(DataFormat.DATABASE);

            return this;
        }
        /// <summary>
        /// Converts the configured input to database format and writes the resulting rows into the given database.
        /// </summary>
        /// <param name="outputDatabase">The database to write the converted rows into</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterInputAndActions SaveTo(IGPALDatabase outputDatabase)
        {
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClass = null;
            ConverterSettings.OutputClassType = null;
            ConverterSettings.OutputClassElementType = null;
            ConverterSettings.OutputGrid = null;

            ConverterSettings.OutputDatabase = outputDatabase;
            ConverterSettings.OutDataFormat = DataFormat.DATABASE;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ConverterSettings.InDataFormat.FirstOrDefault()}] data to database.", this, GPALObjectType.Converter);
            return Convert(ref outputDatabase);
        }
        /// <summary>
        /// Sets the input to a GPAL grid of strings (GRID format, comma input delimiter), clearing any previously configured input/output settings.
        /// </summary>
        /// <param name="inputGrid">The grid of string values to use as the conversion input</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithInput(IGPALGrid<string> inputGrid)
        {
            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputClass = null;
            ConverterSettings.InputClassType = null;
            ConverterSettings.InputClassElementType = null;
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            // Clear the other input sources so Convert()'s dispatch (InputFile, then InputDatabase, then
            // InputGrid) actually reaches the grid branch - otherwise a prior file/database input would be
            // reprocessed. Mirrors WithInput(dynamic), which clears InputFile for the same reason.
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = null;
            ConverterSettings.InputGrid.Add(inputGrid);
            ConverterSettings.InDataFormat.Add(DataFormat.GRID);
            ConverterSettings.InDelimiter = ',';

            return this;
        }
        /// <summary>
        /// Converts the configured input to GRID format and writes the resulting rows/columns into the given grid.
        /// </summary>
        /// <param name="outputGrid">The grid to receive the converted rows; also used to determine the output element type</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterInputAndActions SaveTo(ref IGPALGrid<string> outputGrid)
        {
            ConverterSettings.OutputDatabase = null;
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClassType = outputGrid.GetType();
            ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetElementType();
            if (null == ConverterSettings.OutputClassElementType && 0 < ConverterSettings.OutputClassType.GetGenericArguments().Length)
                if (true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType))
                    ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetGenericArguments()[1];
                else
                    ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetGenericArguments()[0];
            ConverterSettings.OutputGrid = outputGrid;
            ConverterSettings.OutDataFormat = DataFormat.GRID;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ConverterSettings.InDataFormat.FirstOrDefault()}] data to grid.", this, GPALObjectType.Converter);

            Convert(ref outputGrid);

            return this;
        }
        /// <summary>
        /// Sets the input to a raw string of data, clearing any previously configured input/output settings.<br/>
        /// If an input delimiter has not been set, the data format is auto-detected from the content (JSON/XML/YAML/CSV/etc.).
        /// </summary>
        /// <param name="inputData">The raw input data (e.g. CSV, JSON, XML, YAML, delimited text)</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        /// <example>
        /// <code>
        /// string json = "{\"name\":\"Alice\",\"age\":30}";
        /// GPAL.Converter.ToGPALObject()
        ///     .WithInput(json)
        ///     .WithOutputType(DataFormat.YAML)
        ///     .SaveTo(out string yaml);
        /// </code>
        /// </example>
        public IAllowConverterSettingsAndActions WithInput(string inputData)
        {
            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputClass = null;
            ConverterSettings.InputClassType = null;
            ConverterSettings.InputClassElementType = null;
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            // Clear the other input sources, as WithInput(grid) does, so Convert's dispatch (InputFile, then
            // InputDatabase) does not reprocess a prior file or database input instead of this one.
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = null;
            ConverterSettings.InputData.Add(inputData);
            if (null != ConverterSettings.InDelimiter && true == ConverterSettings.InDelimiter.HasValue)
                ConverterSettings.InDataFormat.Add(GetDataFormat(ConverterSettings.InDelimiter.Value));
            else
                ConverterSettings.InDataFormat.Add(DataFormatDetector.DetectFormat(inputData));

            return this;
        }
        /// <summary>
        /// Sets the input to one or more files, clearing any previously configured input/output settings.<br/>
        /// The data format of each matching file is determined from its file extension.
        /// </summary>
        /// <param name="inputFile">The GPALFile (which may expand to multiple matching files) to use as the conversion input</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithInput(GPALFile inputFile)
        {
            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputClass = null;
            ConverterSettings.InputClassType = null;
            ConverterSettings.InputClassElementType = null;
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            ConverterSettings.InputDatabase = null;     // a prior database input is not this one
            ConverterSettings.InputFile = inputFile;
            foreach (string filename in inputFile.Filenames)
                ConverterSettings.InDataFormat.Add(ConverterHelper.GetDataFormatFromExtension(filename));
            return this;
        }
        /// <summary>
        /// Converts the configured input and appends the resulting data to the given output file (instead of overwriting it).
        /// </summary>
        /// <param name="outputFile">The file to append the converted data to; its data format is determined from its file extension</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterInputAndActions AppendTo(GPALFile outputFile)
        {
            ConverterSettings.OutputFile = outputFile;
            ConverterSettings.OutDataFormat = ConverterHelper.GetDataFormatFromExtension(outputFile.Filename);    // will iterate files in convert and determine each dataformat
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Appending [{ConverterSettings.InDataFormat.FirstOrDefault()}] data as [{ConverterSettings.OutDataFormat}] to [{outputFile.Filename}].", this, GPALObjectType.Converter);
            return Convert(ref outputFile, true);
        }
        /// <summary>
        /// Converts the configured input and writes (overwrites) the resulting data to the given output file.
        /// </summary>
        /// <param name="outputFile">The file to write the converted data to; its data format is determined from its file extension</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        /// <example>
        /// <code>
        /// GPAL.Converter
        ///     .WithInput((GPALFile)"input.csv")
        ///     .SaveTo((GPALFile)"output.json");
        /// </code>
        /// </example>
        public IAllowConverterInputAndActions SaveTo(GPALFile outputFile)
        {
            ConverterSettings.OutputFile = outputFile;
            ConverterSettings.OutDataFormat = ConverterHelper.GetDataFormatFromExtension(outputFile.Filename);    // will iterate files in convert and determine each dataformat
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ConverterSettings.InDataFormat.FirstOrDefault()}] data as [{ConverterSettings.OutDataFormat}] to [{outputFile.Filename}].", this, GPALObjectType.Converter);
            Convert(ref outputFile);
            outputFile.ReturnFilenames.Add(outputFile.Filename);    // return the filename we actually save to as a filename
            return this;
        }
        /// <summary>
        /// Sets the input to an arbitrary class instance (or collection), clearing any previously configured input/output settings.<br/>
        /// The instance is reflected/walked into a dictionary representation (CLASS format) that can then be converted to any output format.
        /// </summary>
        /// <param name="inputClass">The object, collection, or dictionary to use as the conversion input</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithInput(dynamic inputClass)
        {
            // a grid held as IAllowGridActions binds here rather than to the grid overload, and taken as an
            // object it serializes as the rows of cells it enumerates: a list of lists, with the column names
            // never read. It is the same grid either way, so it goes where a grid goes
            if (inputClass is IGPALGrid<string> handedOverGrid)
                return WithInput(handedOverGrid);

            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = null;     // a prior database input is not this one
            ConverterSettings.InputClass = inputClass;
            ConverterSettings.InputClassType = inputClass.GetType();
            ConverterSettings.InputClassElementType = ConverterSettings.InputClassType.GetElementType();
            if (null == ConverterSettings.InputClassElementType && ConverterSettings.InputClassType.IsGenericType)
            {
                var genericArgs = ConverterSettings.InputClassType.GetGenericArguments();
                ConverterSettings.InputClassElementType = ConverterHelper.IsDictionaryType(ConverterSettings.InputClassType) && genericArgs.Length > 1
                    ? genericArgs[1] // Use value type for dictionaries (e.g., SelectorSet)
                    : genericArgs.FirstOrDefault();
            }
            HashSet<object> visited = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);

            ConverterHelper.ConverterSettings = ConverterSettings;
            ConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { ConverterHelper.ConvertClassToDictionary(inputClass, null != ConverterSettings.InputClassElementType ? ConverterSettings.InputClassElementType.ToString() : ConverterSettings.InputClassType.ToString(), visited) };
            ConverterSettings.InputData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out IGPALGrid<string> outGrid));
            ConverterSettings.InDataFormat.Add(DataFormat.CLASS);
            ConverterSettings.InputGrid.Add(outGrid);

            return this;
        }
        public IAllowConverterInputAndActions SaveTo<T>(ref T outputClass)
        {
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClassType = outputClass.GetType();
            ConverterSettings.OutputClassElementType = ConverterSettings.OutputClassType.GetElementType();

            if (ConverterSettings.OutputClassElementType == null)
            {
                var genericArgs = ConverterSettings.OutputClassType.GetGenericArguments();

                if (genericArgs.Length > 0)
                {
                    // Check if it's a Dictionary<TKey, TValue>
                    if (ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType))
                    {
                        ConverterSettings.OutputClassElementType = genericArgs[1]; // TValue
                    }
                    // Check if it's any IGPALGrid<T>
                    else if (ConverterSettings.OutputClassType.IsGenericType &&
                             ConverterSettings.OutputClassType.GetGenericTypeDefinition() == typeof(IGPALGrid<>))
                    {
                        // For IGPALGrid<T>, the "element" type is List<T>
                        var elementT = genericArgs[0]; // T
                        ConverterSettings.OutputClassElementType = typeof(List<>).MakeGenericType(elementT);
                    }
                    // Fallback: assume it's IEnumerable<T> or similar
                    else
                    {
                        ConverterSettings.OutputClassElementType = genericArgs[0];
                    }
                }
            }

            ConverterSettings.OutDataFormat = DataFormat.CLASS;
            Convert(ref outputClass);

            return this;
        }
        /// <summary>
        /// Explicitly overrides the detected/assumed input data format.
        /// </summary>
        /// <param name="inDataFormat">The data format to treat the configured input as</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithInputType(DataFormat inDataFormat)
        {
            // Every .WithInput leaves one entry per real input, so overriding means replacing the last of them.
            // Appending made Convert believe there was an extra input, which generated a phantom output file and
            // then indexed past the inputs that actually exist.
            // The empty case is index safety only. Reaching it means this was called before any .WithInput, and
            // .WithInput starts a fresh list, so the value is discarded either way.
            if (0 == ConverterSettings.InDataFormat.Count)
                ConverterSettings.InDataFormat.Add(inDataFormat);
            else
                ConverterSettings.InDataFormat[ConverterSettings.InDataFormat.Count - 1] = inDataFormat;

            return this;
        }
        /// <summary>
        /// Explicitly sets the output data format to use when converting.
        /// </summary>
        /// <param name="outDataFormat">The data format to convert the input to</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithOutputType(DataFormat outDataFormat)
        {
            ConverterSettings.OutDataFormat = outDataFormat;
            return this;
        }
        /// <summary>
        /// Specifies whether the first line/row of delimited input contains column header names.
        /// </summary>
        /// <param name="firstLineHeaders">True if the first line/row contains column names (default true)</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithFirstLineHasColumnNames(bool firstLineHeaders = true)
        {
            ConverterSettings.FirstLineIsColumnHeaders = firstLineHeaders;
            ConverterSettings.InputFirstLineIsColumnHeaders = firstLineHeaders;
            return this;
        }
        /// <summary>
        /// Specifies whether the first line/row of delimited input should be ignored (skipped) rather than treated as data or column names.
        /// </summary>
        /// <param name="ignoreFirstLine">True to skip the first line/row of the input (default true)</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithIgnoreFirstLineColumnNames(bool ignoreFirstLine = true)
        {
            ConverterSettings.IgnoreFirstLineColumnHeaders = ignoreFirstLine;
            return this;
        }
        /// <summary>
        /// Specifies whether delimited fields should be wrapped in quotes when writing output.
        /// </summary>
        /// <param name="fieldsEnclosedInQuotes">True to enclose each output field in quotes (default true)</param>
        /// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        public IAllowConverterSettingsAndActions WithColumnsEnclosedInQuotes(bool fieldsEnclosedInQuotes = true)
        {
            ConverterSettings.FieldsEnclosedInQuotes = fieldsEnclosedInQuotes;
            return this;
        }

        /// <summary>
        /// Supplies the column names for this conversion. Caller-supplied names override the input source's
        /// own column names (a file's column list, a database's columns) positionally; any columns the caller
        /// does not supply are backfilled from the source. The names persist until the next WithInput. The
        /// source (e.g. the GPALFile) is never modified.
        /// </summary>
        /// <param name="columnNames">The ordered column names to use for this conversion.</param>
        /// <returns>Fluent interface to continue building the conversion.</returns>
        public IAllowConverterSettingsAndActions WithColumnNames(List<string> columnNames)
        {
            ConverterSettings.ColumnNames = new GPALGrid<string>();
            if (null != columnNames && 0 < columnNames.Count)
                ConverterSettings.ColumnNames.AddRow(new List<string>(columnNames));
            return this;
        }

        /// <summary>
        /// Appends a single column name to the conversion's column list. Repeated calls build the list in
        /// order. See <see cref="WithColumnNames(List{string})"/> for override/backfill semantics.
        /// </summary>
        /// <param name="columnName">The column name to append.</param>
        /// <returns>Fluent interface to continue building the conversion.</returns>
        public IAllowConverterSettingsAndActions WithColumnName(string columnName)
        {
            if (0 == ConverterSettings.ColumnNames.Count())
                ConverterSettings.ColumnNames.AddRow(new List<string>());
            ConverterSettings.ColumnNames[0].Add(columnName);
            return this;
        }

        /// <summary>
        /// Merges caller-supplied column names (WithColumnName/s) with an input source's own names: the
        /// caller's names take positions 0..N-1; any remaining positions are filled from the source. Neither
        /// the caller's list nor the source list is modified.
        /// </summary>
        /// <param name="sourceColumns">The input source's own column names, or null when the source has none.</param>
        /// <returns>The effective, ordered column list for this conversion.</returns>
        private List<string> ResolveEffectiveColumns(List<string> sourceColumns)
        {
            List<string> caller = (null != ConverterSettings.ColumnNames && 0 < ConverterSettings.ColumnNames.Count())
                ? ConverterSettings.ColumnNames[0]
                : null;
            if (null == caller || 0 == caller.Count)
                return (null != sourceColumns) ? new List<string>(sourceColumns) : new List<string>();

            List<string> effective = new List<string>(caller);
            if (null != sourceColumns)
                for (int i = effective.Count; i < sourceColumns.Count; i++)
                    effective.Add(sourceColumns[i]);
            return effective;
        }

        /// <summary>
        /// Pretty-prints JSON, YAML, XML, or HTML. 
        /// Other formats (CSV, TAB, PIPE, etc.) are returned as-is.
        /// </summary>
        /// <summary>
        /// Pretty prints the data based on the CURRENT Input Format (InDataFormat).
        /// Works great with your fluent chain: WithInput(...).PrettyPrintTo(...)
        /// </summary>
        public IAllowConverterInputAndActions PrettyPrintTo(out string outputData)
        {
            ConverterSettings.OutputFile = null;
            ConverterSettings.OutputClass = null;
            ConverterSettings.OutputClassType = null;
            ConverterSettings.OutputClassElementType = null;
            ConverterSettings.OutputGrid = null;

            // Use the Input format (already known from WithInput) for pretty printing
            if (ConverterSettings.InDataFormat.FirstOrDefault() != DataFormat.NOTSET)
                ConverterSettings.OutDataFormat = ConverterSettings.InDataFormat.FirstOrDefault();

            outputData = FormatPretty(ConverterSettings.InputData.FirstOrDefault());
            ConverterSettings.OutputData = outputData;

            return this;
        }

        #endregion Fluent Interface

        /// <summary>
        /// Whether this payload should be written without YAML aliases.
        /// </summary>
        /// <remarks>
        /// Aliases let YAML carry a graph rather than a tree, and they do two different jobs. A cycle cannot be
        /// written without them at all: YamlDotNet follows the graph until it gives up and throws. A merely shared
        /// reference does not need them, it only comes out shorter and reads back as one object instead of two.
        /// <br/><br/>
        /// Turning them on costs a pre-pass over the whole graph to find repeat references, and on a large flat
        /// collection, tens of thousands of sitemap urls say, that pre-pass is what stalls Serialize while there is
        /// nothing in it worth anchoring. So they are dropped for a payload that is large and has no cycle, and
        /// kept for everything else. A cycle keeps them whatever its size, because the alternative is not a longer
        /// document, it is an exception.
        /// <br/><br/>
        /// The size is GPAL.WithYamlAliasLimit, 5000 unless a workflow says otherwise, and 0 keeps aliases always.
        /// </remarks>
        /// <param name="payload">The object about to be serialized</param>
        /// <returns>True to serialize without aliases</returns>
        bool DropAliasesFor(object payload)
        {
            int limit = GPAL.GPALSettings.YamlAliasLimit;
            bool retVal = false;

            if (0 < limit)
            {
                int nodes = ConverterHelper.CountGraphNodes(payload, limit, out bool cyclic);

                retVal = false == cyclic && nodes >= limit;

                if (true == cyclic && nodes >= limit)
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.DEBUG, $"The payload holds a cycle, so its aliases are kept whatever its size. Without them YAML cannot write it at all", null, Enums.GPALObjectType.Converter);
                else if (true == retVal)
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.DEBUG, $"The payload reached the alias limit of [{limit}] nodes and holds no cycle, so a shared reference is written out again rather than as an alias. GPAL.WithYamlAliasLimit moves the limit", null, Enums.GPALObjectType.Converter);
            }

            return retVal;
        }

        /// <summary>
        /// The input that services this output, clamped to what is actually there. The input count comes from the
        /// declared formats, which can run ahead of the inputs themselves, so this keeps to the same rule the
        /// pairing already follows: the last input services any extra outputs. Warns when it has to clamp, since
        /// that means the declared inputs and the real ones disagree.
        /// </summary>
        /// <param name="inputIdx">Index the output was paired with</param>
        /// <param name="inputCount">How many inputs of the kind about to be read there really are</param>
        /// <param name="whatIsMissing">What is short, named for the message</param>
        /// <returns>An index inside the collection, or -1 when there is nothing to read</returns>
        int InputWithin(int inputIdx, int inputCount, string whatIsMissing)
        {
            int within = inputIdx;

            if (inputCount <= inputIdx)
            {
                within = inputCount - 1;
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Output [{inputIdx + 1}] has no {whatIsMissing} of its own, only [{inputCount}] given. Using the last one.", this, GPALObjectType.Converter);
            }

            return within;
        }

        internal IAllowConverterInputAndActions Convert<T>(ref T outputClass, bool appendToFile = false)
        {
            CancelIdleTimer();
            ConverterHelper.ConverterSettings = ConverterSettings;

            #region <Input>
            IGPALGrid<string> outGrid = GPAL.Grid.ToGPALObject();
            ConverterSettings.OutputData = null;
            // NOTE: ColumnNames is NOT cleared here. It is reset per WithInput and may hold caller-supplied
            // names (WithColumnName/s) that must survive across chained SaveTo calls; the source-derived
            // fallbacks below only fill it when the caller left it empty.

            if (null != ConverterSettings.InputFile)
            {
                if (true == File.Exists(ConverterSettings.InputFile))
                {
                    if (false == FileHelper.TokenizeFile(ConverterSettings.InputFile, ConverterSettings))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to tokenize file [{ConverterSettings.InputFile.Filename}]. Continuing.", ConverterSettings, GPALObjectType.Other);
                        return this;
                    }
                    ConverterSettings.InputDictionary = ((IGPALFileInternal)ConverterSettings.InputFile).FileDictionary;  // CAVEAT: what about multiple files in the GPALFile?
                    ConverterSettings.InputData = ((IGPALFileInternal)ConverterSettings.InputFile).FileData;
                    ConverterSettings.InputGrid = ((IGPALFileInternal)ConverterSettings.InputFile).FileGrid;
                    // Only fall back to the file's own column list when the caller did not supply columns,
                    // and copy it (never alias) so the file's ColumnList is never mutated by this conversion.
                    if (ConverterHelper.IsDelimitedDataFormat(ConverterHelper.GetDataFormatFromExtension(ConverterSettings.InputFile.Filename))
                        && 0 == ConverterSettings.ColumnNames.Count())
                    {
                        ConverterSettings.ColumnNames = new GPALGrid<string>();
                        foreach (List<string> colRow in ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList)
                            ConverterSettings.ColumnNames.AddRow(new List<string>(colRow));
                    }
                    ConverterSettings.ExcelRowsPerSheet = ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ExcelRowsPerSheet;
                    ConverterSettings.ExcelSheetNames = ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ExcelSheetNames;
                    ConverterSettings.FirstLineIsColumnHeaders = (bool)(0 < ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.FirstLineIsColumnNames.Count() ? ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.FirstLineIsColumnNames?[0] : ConverterSettings.InputFirstLineIsColumnHeaders);   // default for non-file output paths; overridden per output file below
                    ConverterSettings.InDelimiter = ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter[0];  // default for non-file output paths; overridden per input file below

                    // InDataFormat needs no repopulating here. It is filled by WithInput, one entry per input, and
                    // nothing empties it afterwards: the idle timer's ClearDerived drops the cached payloads, not
                    // the declared formats. So it survives every SaveTo/AppendTo in a chain on its own.
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File missing: [{ConverterSettings.InputFile.Filename}]", ConverterSettings, GPALObjectType.Other);
            }
            else if (0 < ConverterSettings.InputData.Count)     // we were given custom input data or type
            {
                for (int idx = 0; idx < ConverterSettings.InputData.Count; idx++)
                {
                    switch (ConverterSettings.InDataFormat[idx])
                    {
                        case DataFormat.CUSTOM_DELIMITER:
                        case DataFormat.CARET:
                        case DataFormat.CSV:
                        case DataFormat.LOG:    // a finished line of text, written and read as it stands
                        case DataFormat.PIPE:
                        case DataFormat.PRN:
                        case DataFormat.TAB:
                            ConverterSettings.InputDictionary.Clear();
                            ConverterSettings.InputDictionary.Add(ConverterHelper.ConvertDelimitedToDictionary(ConverterSettings, new StringReader(ConverterSettings.InputData[idx])));
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid);
                            ConverterSettings.InputGrid.Add(outGrid);
                            break;
                        case DataFormat.STRING:
                            ConverterSettings.InputGrid.Add(GPAL.Grid.AddRow(new List<string> { ConverterSettings.InputData[idx] }).ToGPALObject());
                            ConverterSettings.InputDictionary.Clear();
                            ConverterSettings.InputDictionary.Add(ConverterHelper.ConvertDelimitedToDictionary(ConverterSettings, new StringReader(ConverterSettings.InputData[idx])));
                            break;
                        case DataFormat.DICTIONARY:
                            break;
                        case DataFormat.JSON:
                            var desObj = JsonConvert.DeserializeObject<dynamic>(ConverterSettings.InputData[idx], new JsonConverter[] { new CustomJsonConverter() });
                            if (null != desObj)
                            {
                                Dictionary<object, dynamic> jsonDict;
                                if (desObj is IDictionary)
                                {
                                    jsonDict = (Dictionary<object, dynamic>)desObj;
                                }
                                else if (desObj is IList jsonList)
                                {
                                    // Root is a JSON array, not an object - synthesize row keys
                                    // (same GPALKEYN convention used for headerless CSV rows and YAML sequence roots).
                                    jsonDict = new Dictionary<object, dynamic>();
                                    int rootIdx = 0;
                                    foreach (var item in jsonList)
                                    {
                                        jsonDict[$"GPALKEY{rootIdx++:D4}"] = item;
                                    }
                                }
                                else
                                {
                                    // Root is a bare JSON scalar.
                                    jsonDict = new Dictionary<object, dynamic> { ["GPALKEY0000"] = desObj };
                                }

                                ConverterSettings.InputDictionary.Add(jsonDict);
                                ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid);
                                ConverterSettings.InputGrid.Add(outGrid);
                            }
                            else
                                return this;

                            break;
                        //// post-v1: PDF
                        //case DataFormat.PDF:
                        //    break;
                        case DataFormat.XLSX:
                            break;
                        case DataFormat.XML:
                            ConverterSettings.InputXmlDocument.PreserveWhitespace = true;
                            ConverterSettings.InputXmlDocument.Load(ConverterSettings.InputData[idx]);
                            ConverterSettings.InputDictionary.Add(ConverterHelper.ConvertXMLToDictionary(ConverterSettings.InputXmlDocument));
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid);
                            ConverterSettings.InputGrid.Add(outGrid);
                            break;
                        case DataFormat.YAML:
                            var deserializer = new DeserializerBuilder()
                                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                .Build();

                            dynamic yamlObject = deserializer.Deserialize<object>(ConverterSettings.InputData[idx]);
                            var yamlGridRows = new List<List<string>>();
                            var processedYamlDict = new Dictionary<dynamic, dynamic>();

                            if (yamlObject is IDictionary yamlMapping)
                            {
                                foreach (var key in yamlMapping.Keys)
                                {
                                    string keyStr = key?.ToString() ?? "null_key";
                                    processedYamlDict[keyStr] = FileHelper.ProcessDictionaryValue(yamlMapping[key], yamlGridRows);
                                }
                            }
                            else if (yamlObject is IList yamlSequence)
                            {
                                // Root is a YAML sequence, not a mapping - synthesize row keys
                                // (same GPALKEYN convention used for headerless CSV rows).
                                int rootIdx = 0;
                                foreach (var item in yamlSequence)
                                {
                                    processedYamlDict[$"GPALKEY{rootIdx++:D4}"] = FileHelper.ProcessDictionaryValue(item, yamlGridRows);
                                }
                            }
                            else
                            {
                                // Root is a bare YAML scalar.
                                processedYamlDict["GPALKEY0000"] = FileHelper.ProcessDictionaryValue(yamlObject, yamlGridRows);
                            }

                            var yamlGridRow = new GPALGrid<string>();
                            foreach (var row in yamlGridRows)
                            {
                                yamlGridRow.AddRow(row);
                            }

                            ConverterSettings.InputDictionary.Add(processedYamlDict);
                            ConverterSettings.InputGrid.Add(yamlGridRow);
                            break;
                        case DataFormat.HTML:
                            ConverterSettings.InputDictionary = ConverterHelper.ConvertHtmlToDictionary(ConverterSettings.InputData[idx]);
                            ConverterSettings.InputData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid));
                            ConverterSettings.InputGrid.Add(outGrid);
                            break;
                    }
                }
            }
            else if (null != ConverterSettings.InputDatabase)
            {
                List<string> dbColumns = new List<string>();

                // TODO: convert to other types
                DatabaseHelper.TokenizeDatabase(null, ConverterSettings.InputDatabase);
                ConverterSettings.InputGrid.Add(ConverterSettings.InputDatabase.Tokens);
                if (0 < ((GPALDatabase)ConverterSettings.InputDatabase).DatabaseSettings.ColumnList.Count)  // no columns means no results
                    foreach (DatabaseSettings.DatabaseColumn column in ((GPALDatabase)ConverterSettings.InputDatabase).DatabaseSettings.ColumnList)
                        dbColumns.Add(column.ColumnName);

                // Caller-supplied columns override the database's own columns positionally; the database is
                // just the fallback/backfill, exactly like a file's column list.
                List<string> columns = ResolveEffectiveColumns(dbColumns);
                if (0 == ConverterSettings.ColumnNames.Count() && 0 < columns.Count)
                    ConverterSettings.ColumnNames.AddRow(new List<string>(columns));

                ConverterSettings.InputDictionary = ConverterHelper.ConvertGridToDictionary(columns, ConverterSettings.InputDatabase.Tokens);
            }
            else if (null != ConverterSettings.InputGrid && 0 < ConverterSettings.InputGrid.Count)
            {
                // Caller-supplied columns key the rows. For a grid the "source" names are the browser's
                // selector names, which the converter cannot see, so the browser tops HeaderList up from
                // selector names before handing the grid over; any names still missing here are generated.
                List<string> columns = ResolveEffectiveColumns(null);
                bool haveColumns = 0 < columns.Count;

                ConverterSettings.InputDictionary.Clear();
                foreach (IGPALGrid<string> inGrid in ConverterSettings.InputGrid)
                {
                    // One dictionary per grid (one grid == one input source), rows keyed under GPALKEY - the
                    // same shape delimited-file input produces - keyed by column name when columns are known.
                    Dictionary<object, dynamic> source = new Dictionary<object, dynamic>();
                    int lineNumber = 0;
                    foreach (List<string> gridRow in inGrid)
                    {
                        if (haveColumns)
                        {
                            Dictionary<string, string> rowDict = new Dictionary<string, string>();
                            for (int i = 0; i < gridRow.Count; i++)
                                rowDict[i < columns.Count ? columns[i] : $"Column{i + 1}"] = gridRow[i];
                            source[$"GPALKEY{lineNumber:D4}"] = rowDict;
                        }
                        else
                            source[$"GPALKEY{lineNumber:D4}"] = new List<string>(gridRow);
                        lineNumber++;
                    }
                    ConverterSettings.InputDictionary.Add(source);
                }
            }
            else if (null != ConverterSettings.InputClass && 0 < ConverterSettings.InDataFormat.Count)
            {
                // Re-derive from persisted InputClass for chained SaveTo calls (same role as InputFile re-tokenization above)
                HashSet<object> visited = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);
                IGPALGrid<string> repopGrid;
                ConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> {
                    ConverterHelper.ConvertClassToDictionary(
                        ConverterSettings.InputClass,
                        null != ConverterSettings.InputClassElementType
                            ? ConverterSettings.InputClassElementType.ToString()
                            : ConverterSettings.InputClassType.ToString(),
                        visited)
                };
                ConverterSettings.InputData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out repopGrid));
                ConverterSettings.InDataFormat.Add(DataFormat.CLASS);
                ConverterSettings.InputGrid.Add(repopGrid);
            }
            #endregion <Input>
            #region <Output>
            StringBuilder stringBuilder = new StringBuilder();
            if (null != ConverterSettings.OutputFile)
            {
                // Pair each output file with an input "dataset" (one per input file, or one for class/grid/database/etc input):
                // output[i] is converted from input[min(i, inputFileCount-1)] - the last input services any extra outputs.
                // If there are more inputs than outputs, grow the output filename list using the last user-supplied
                // output filename as a naming pattern (name_2.ext, name_3.ext, ...).
                int inputFileCount = Math.Max(1, ConverterSettings.InDataFormat.Count);
                GPALFileSettings outputFileSettings = ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings;

                // worked out fresh for this conversion and never written back. Filenames holds what the workflow
                // supplied and nothing else, so the pattern is always a real filename rather than one of these,
                // and a GPALFile reused across conversions does not grow a longer list every time
                List<string> outputPaths = new List<string>(outputFileSettings.Filenames);

                if (0 < outputPaths.Count && outputPaths.Count < inputFileCount)
                {
                    string pattern = outputPaths[outputPaths.Count - 1];
                    string patternDirectory = Path.GetDirectoryName(pattern);
                    string patternName = Path.GetFileNameWithoutExtension(pattern);
                    string patternExtension = Path.GetExtension(pattern);

                    for (int newFileIdx = outputPaths.Count; newFileIdx < inputFileCount; newFileIdx++)
                    {
                        string generatedFilename = Path.Combine(patternDirectory, $"{patternName}_{newFileIdx + 1}{patternExtension}");
                        outputPaths.Add(generatedFilename);
                        ConverterSettings.OutputFile.ReturnFilenames.Add(generatedFilename);
                    }
                }

                int fileIdx = 0;
                DataFormat savedOutDataFormat = ConverterSettings.OutDataFormat;
                foreach (string filename in outputPaths)
                {
                    // the folders the workflow named are made if they are not there, as every GPAL save does
                    FileHelper.EnsureFolderFor(filename);

                    int inputIdx = Math.Min(fileIdx, inputFileCount - 1);

                    // each input file may have its own delimiter (e.g. mixing .csv and .tab inputs) - use the one paired with this output
                    if (null != ConverterSettings.InputFile && inputIdx < ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter.Count)
                        ConverterSettings.InDelimiter = ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter[inputIdx];

                    // if this is set going into this, we are being asked to override figuring it out for each file and will use this one set by the programmer
                    if (DataFormat.NOTSET == savedOutDataFormat)
                        ConverterSettings.OutDataFormat = ConverterHelper.GetDataFormatFromExtension(filename);

                    /* - not sure, assume the output delim is the same as input? seems outdated logic
                    if (0 < ConverterSettings.OutputFile.FileSettings.Delimiter.Count() && true == ConverterSettings.OutputFile.FileSettings.Delimiter[fileIdx].HasValue)
                        ConverterSettings.OutDelimiter = ConverterSettings.OutputFile.FileSettings.Delimiter[fileIdx].Value;
                    else 
                    */
                    if (fileIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).FirstLineIsColumnNames.Count)
                        // An explicit output-file setting (true OR false) always wins - this is how the user
                        // turns the header row off despite having named columns.
                        ConverterSettings.FirstLineIsColumnHeaders = ((IGPALFileInternal)ConverterSettings.OutputFile).FirstLineIsColumnNames[fileIdx];
                    else
                        // No explicit setting: emit a header when we have column names to write (named via
                        // WithHeader / WithColumnName(s), or resolved from the source), or when the caller
                        // declared headers via WithFirstLineHasColumnNames. Naming columns implies wanting them.
                        ConverterSettings.FirstLineIsColumnHeaders =
                            (null != ConverterSettings.ColumnNames && 0 < ConverterSettings.ColumnNames.Count())
                            || ConverterSettings.InputFirstLineIsColumnHeaders;

                    // Headers are never repeated when appending to an existing file.
                    if (true == appendToFile)
                        ConverterSettings.FirstLineIsColumnHeaders = false;

                    if (fileIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).FieldsEnclosedInQuotes.Count)
                        ConverterSettings.FieldsEnclosedInQuotes = ((IGPALFileInternal)ConverterSettings.OutputFile).FieldsEnclosedInQuotes[fileIdx];

                    if (null == ConverterSettings.OutDelimiter)
                        ConverterSettings.OutDelimiter = ConverterHelper.GetDelimiterFromFormat(ConverterSettings.OutDataFormat);

                    if (null != ConverterSettings.ColumnNames && 0 == ConverterSettings.ColumnNames.Count())
                    {
                        List<string> columns = new List<string>();

                        if ((ConverterHelper.IsDelimitedDataFormat(ConverterSettings.OutDataFormat) || DataFormat.XLSX == ConverterSettings.OutDataFormat) && null != ConverterSettings.OutputFile && null != ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList && fileIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList.Count())
                            foreach (string columnName in ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList[fileIdx])
                                columns.Add(columnName);
                        else if (inputIdx < ConverterSettings.InDataFormat.Count && (ConverterHelper.IsDelimitedDataFormat(ConverterSettings.InDataFormat[inputIdx]) || DataFormat.XLSX == ConverterSettings.InDataFormat[inputIdx]) && null != ConverterSettings.InputFile && null != ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList && inputIdx < ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList.Count())
                            foreach (string columnName in ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList[inputIdx])
                                columns.Add(columnName);

                        if (0 < columns.Count)
                            ConverterSettings.ColumnNames.AddRow(columns);
                    }

                    switch (ConverterSettings.OutDataFormat)
                    {
                        case DataFormat.CUSTOM_DELIMITER:
                        case DataFormat.CARET:
                        case DataFormat.CSV:
                        case DataFormat.LOG:    // a finished line of text, written and read as it stands
                        case DataFormat.PIPE:
                        case DataFormat.PRN:
                        case DataFormat.TAB:
                        case DataFormat.NOTSET:
                            if (0 < ConverterSettings.InDataFormat.Count)
                            {
                                switch (ConverterSettings.InDataFormat[inputIdx])
                                {
                                    case DataFormat.CARET:
                                    case DataFormat.COLON:
                                    case DataFormat.CSV:
                                    case DataFormat.CUSTOM_DELIMITER:
                                    case DataFormat.DOT:
                                    //case DataFormat.HTML:
                                    case DataFormat.HYPHEN:
                                    case DataFormat.LOG:    // a finished line of text, written and read as it stands
                                    case DataFormat.PIPE:
                                    case DataFormat.SEMICOLON:
                                    case DataFormat.SPACE:
                                    case DataFormat.TAB:
                                    case DataFormat.PRN:
                                        if (inputIdx < ConverterSettings.InputData.Count)
                                            ConverterSettings.OutputData = ConverterSettings.InputData[inputIdx];
                                        else
                                            ConverterSettings.OutputData = File.ReadAllText(ConverterSettings.InputFile.Filenames[inputIdx]);
                                        if (null != ConverterSettings.OutputData && null != ConverterSettings.InDelimiter && true == ConverterSettings.InDelimiter.HasValue)
                                        {
                                            using (StringReader reader = new StringReader(ConverterSettings.OutputData))
                                            {
                                                string line;
                                                StringBuilder lineBuilder = new StringBuilder();
                                                while ((line = reader.ReadLine()) != null)
                                                {
                                                    string[] tokens = line.Split(ConverterSettings.InDelimiter.Value);
                                                    foreach (string str in tokens)
                                                        stringBuilder.Append(ConverterHelper.CreateCSVToken(str, ConverterSettings));   // if these should be wrapped in "", createcsvtoken will do that
                                                    if (0 < stringBuilder.Length)
                                                        stringBuilder.Remove(stringBuilder.Length - 1, 1);  // remove trailing inDelimiter
                                                    lineBuilder.AppendLine(stringBuilder.ToString());
                                                    stringBuilder.Clear();
                                                }
                                                ConverterSettings.OutputData = lineBuilder.ToString();
                                            }
                                        }
                                        stringBuilder.Append(ConverterSettings.OutputData);
                                        break;
                                    case DataFormat.HTML:
                                        //stringBuilder.Append(ConverterSettings.InputData[fileIdx]);  // NOTE: this feels like the wrong variable name
                                        //break;
                                    case DataFormat.CLASS:
                                    default:    // nested structure? use dictionary - maybe we can always use the dictionary?
                                        stringBuilder.Append(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid));
                                        break;
                                }
                                if (true == appendToFile)
                                    File.AppendAllText(filename, stringBuilder.ToString());
                                else
                                    File.WriteAllText(filename, stringBuilder.ToString());
                            }
                            else    // not sure this will ever hit...
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File [{ConverterSettings.InputFile}] does not exist(?) as no DATA FORMAT has been set.", this, GPALObjectType.Converter);
                            break;
                        case DataFormat.JSON:
                            if (ConverterSettings.InputDictionary.Count > 0)
                            {
                                int dictIdx = InputWithin(inputIdx, ConverterSettings.InputDictionary.Count, "input");
                                object source = WithoutReaderRoot(ConverterSettings.InputDictionary[dictIdx], dictIdx);
                                var rootDict = source as IDictionary;

                                object rawObject;

                                if (rootDict != null && ConverterHelper.IsArrayLike(rootDict))
                                {
                                    var list = new List<object>();
                                    foreach (DictionaryEntry e in rootDict)
                                    {
                                        string k = e.Key.ToString();
                                        if (k.StartsWith("GPALKEY"))
                                        {
                                            list.Add(e.Value);
                                        }
                                    }
                                    rawObject = list;
                                }
                                else
                                {
                                    rawObject = rootDict ?? source;
                                }

                                dynamic cleanObject = ConverterHelper.NormalizeForCleanJson(rawObject);
                                cleanObject = ConverterHelper.StripGpalKeys(cleanObject);

                                var settings = new JsonSerializerSettings
                                {
                                    // Your existing settings, e.g. NamingStrategy if needed
                                    Converters =
                                        {
                                            new IPAddressJsonConverter(),
                                            new ComplexJsonConverter(),
                                            new UriJsonConverter(),
                                            new VersionJsonConverter(),
                                            // new BigIntegerJsonConverter(), // only if needed
                                            // Add any others you already have (e.g. for quoted strings)
                                        },
                                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore,  // ignore self referencing classes/structs as an error
                                    Formatting = Newtonsoft.Json.Formatting.Indented // optional, for readable output
                                };

                                ConverterSettings.OutputData = JsonConvert.SerializeObject(cleanObject, settings);

                                if (appendToFile)
                                    File.AppendAllText(filename, ConverterSettings.OutputData);
                                else
                                    File.WriteAllText(filename, ConverterSettings.OutputData);
                            }
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No input data to write to [{filename}]", this, GPALObjectType.Converter);
                            break;
                        //// post-v1: PDF
                        //case DataFormat.PDF:
                        //    break;
                        case DataFormat.XLSX:
                            if (inputIdx < ConverterSettings.InputGrid.Count)
                                ExcelHelper.WriteXlsxFromTokenizedData(
                                        outputPath: filename,
                                        grid: ConverterSettings.InputGrid[inputIdx], // clone if you don't want the helper to have a reference to your live grid
                                        rowsPerSheet: ConverterSettings.ExcelRowsPerSheet[inputIdx],
                                        columnNamesPerSheet: ConverterSettings.ColumnNames,
                                        sheetNames: ConverterSettings.ExcelSheetNames[inputIdx],
                                        writeHeaders: ConverterSettings.FirstLineIsColumnHeaders // or true if you always want headers
                                    );
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No input data to write to [{filename}]", this, GPALObjectType.Converter);
                            break;
                        case DataFormat.XML:
                            if (0 < ConverterSettings.InputDictionary.Count)
                            {
                                ConverterSettings.OutputXmlDocument = new XmlDocument();

                                // Convert the dictionary to XML using recursion
                                var rootElement = ConverterSettings.OutputXmlDocument.CreateElement("root");
                                ConverterSettings.OutputXmlDocument.AppendChild(rootElement);

                                // get existing file, find root, then start adding to it
                                if (true == appendToFile)
                                {
                                    XmlDocument xmlDocument = new XmlDocument();
                                    try
                                    {
                                        xmlDocument.Load(filename);
                                        ConverterSettings.OutputXmlDocument = xmlDocument;
                                        rootElement = (XmlElement)xmlDocument.GetElementsByTagName("root")[0];
                                    }
                                    catch (Exception)
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File [{filename}] does not exist to append XML to, writing new.", this, GPALObjectType.Converter);
                                    }
                                }

                                int dictCount = 0;
                                foreach (Dictionary<object, dynamic> dict in ConverterSettings.InputDictionary)
                                {
                                    // the document's own <root> holds the data, so the JSON reader's "root" key would
                                    // nest a second one inside it
                                    Dictionary<object, dynamic> content = WithoutReaderRoot(dict, dictCount) as Dictionary<object, dynamic> ?? dict;

                                    if (DataFormat.HTML == ConverterSettings.InDataFormat[dictCount])
                                        ConverterHelper.ConvertHtmlDictionaryToXml(content, rootElement); // append to rootElement in method
                                    else if (DataFormat.XML == ConverterSettings.InDataFormat[dictCount])
                                        ConverterHelper.ConvertDictionaryToXmlElement(content, rootElement, false); // append to rootElement in method
                                    else
                                        ConverterHelper.ConvertDictionaryToXmlElement(content, rootElement); // append to rootElement in method
                                    dictCount++;
                                }

                                // NOTE: Preserve original XML header(s)
                                string inputFilename = ConverterSettings.InputFile?.Filename; // the original source file

                                if (true == ConverterSettings.InputFile?.Filename.ToLower().EndsWith("xml"))
                                {
                                    string header = ConverterHelper.GetXmlHeaderFromFile(inputFilename);

                                    // Write header from original document
                                    if (0 < header.Length)
                                        using (var writer = new StreamWriter(filename, append: false))
                                        {
                                            writer.Write(header);

                                            using (var xmlWriter = XmlWriter.Create(writer, new XmlWriterSettings
                                            {
                                                OmitXmlDeclaration = true,
                                                Indent = true,
                                                Encoding = writer.Encoding
                                            }))
                                            {
                                                ConverterSettings.OutputXmlDocument.Save(xmlWriter);
                                            }
                                        }
                                    else
                                        ConverterSettings.OutputXmlDocument.Save(filename);
                                }
                                else
                                    ConverterSettings.OutputXmlDocument.Save(filename);
                            }
                            break;
                        case DataFormat.YAML:
                            if (ConverterSettings.InputClass != null)
                            {
                                var serializerBuilder = new SerializerBuilder()
                                    .WithObjectGraphTraversalStrategyFactory((ti, tr, tc, depth)
                                        => new FullObjectGraphTraversalStrategy(ti, tr, depth, CamelCaseNamingConvention.Instance, new DefaultObjectFactory()))
                                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                    .WithEventEmitter(next => new ExplicitNullYamlEventEmitter(next))
                                    .WithEventEmitter(next => new HumanReadableAliasEventEmitter(next))
                                    .WithTypeConverter(new IPAddressYamlConverter())
                                    .WithTypeConverter(new ComplexYamlConverter())
                                    .WithTypeConverter(new UriYamlConverter())
                                    .WithTypeConverter(new NullableBigIntegerYamlConverter())
                                    .WithTypeConverter(new NullableGuidYamlConverter())
                                    .WithTypeConverter(new NullableTimeSpanYamlConverter())
                                    .WithTypeConverter(new VersionYamlConverter())
                                    .WithTypeConverter(new DateTimeOffsetYamlConverter())
                                    .WithTypeConverter(new QuotedStringYamlConverter())
                                    .WithTypeConverter(new WaitTimeYamlConverter());

                                if (true == DropAliasesFor(ConverterSettings.InputClass))
                                    serializerBuilder = serializerBuilder.DisableAliases();

                                var serializer = serializerBuilder.Build();

                                ConverterSettings.OutputData = serializer.Serialize(ConverterSettings.InputClass);

                                if (true == appendToFile)
                                    File.AppendAllText(filename, ConverterSettings.OutputData);
                                else
                                    File.WriteAllText(filename, ConverterSettings.OutputData);
                            }
                            else if (0 < ConverterSettings.InputDictionary.Count)
                            {
                                int yamlIdx = InputWithin(inputIdx, ConverterSettings.InputDictionary.Count, "input");
                                bool largeGraph = DropAliasesFor(ConverterSettings.InputDictionary[yamlIdx]);

                                var serializerBuilder = new SerializerBuilder()
                                    .WithEventEmitter(next => new ExplicitNullYamlEventEmitter(next))
                                    .WithEventEmitter(next => new HumanReadableAliasEventEmitter(next))
                                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                    .WithTypeConverter(new IPAddressYamlConverter())
                                    .WithTypeConverter(new ComplexYamlConverter())
                                    .WithTypeConverter(new UriYamlConverter())
                                    .WithTypeConverter(new NullableBigIntegerYamlConverter())
                                    .WithTypeConverter(new NullableGuidYamlConverter())
                                    .WithTypeConverter(new NullableTimeSpanYamlConverter())
                                    .WithTypeConverter(new VersionYamlConverter())
                                    .WithTypeConverter(new DateTimeOffsetYamlConverter())
                                    .WithTypeConverter(new QuotedStringYamlConverter())
                                    .WithTypeConverter(new WaitTimeYamlConverter());

                                if (true == largeGraph)
                                    serializerBuilder = serializerBuilder.DisableAliases();

                                var serializer = serializerBuilder.Build();

                                object CleanForYaml(object input)
                                {
                                    // a row built from column names is a Dictionary<string, string>, which is not a
                                    // Dictionary<object, dynamic>, so it missed the mapping branch below and fell into
                                    // the enumerable one, where a dictionary enumerates as the key/value pairs it holds
                                    // and serialized as those pairs instead of as a mapping
                                    if (input is IDictionary otherShapedDict && false == (input is Dictionary<object, dynamic>))
                                    {
                                        Dictionary<object, dynamic> asGpalDict = new Dictionary<object, dynamic>();

                                        foreach (DictionaryEntry entry in otherShapedDict)
                                            asGpalDict[entry.Key] = entry.Value;

                                        input = asGpalDict;
                                    }

                                    // A key that is itself an object cannot be a YAML mapping key the serializer writes;
                                    // it wrote the key's type name and an empty value. Written as a list of Key/Value
                                    // pairs instead, as JSON writes it.
                                    if (input is Dictionary<object, dynamic> keyed && true == keyed.Keys.Any(k => null != k && false == ConverterHelper.IsSimpleType(k.GetType())))
                                    {
                                        var pairs = new List<object>();

                                        foreach (var pair in keyed)
                                            pairs.Add(new Dictionary<object, dynamic> { ["Key"] = CleanForYaml(pair.Key), ["Value"] = CleanForYaml(pair.Value) });

                                        return pairs;
                                    }

                                    if (input is Dictionary<object, dynamic> d)
                                    {
                                        // First: detect if this dict came from XML (has @keys or #text)
                                        bool isXmlDerived = d.Keys.Cast<object>().Any(k =>
                                            k.ToString() == "#text" || k.ToString().StartsWith("@"));

                                        if (ConverterHelper.IsArrayLike(d))
                                        {
                                            // Surrogate array - collect in order
                                            var list = new List<object>();
                                            foreach (var p in d)
                                            {
                                                string k = p.Key.ToString();

                                                if (k.StartsWith("GPALKEY"))
                                                {
                                                    list.Add(CleanForYaml(p.Value));
                                                }
                                            }
                                            return list;
                                        }
                                        else
                                        {
                                            // Normal object
                                            var cleanDict = new Dictionary<object, dynamic>();

                                            string textValue = null;
                                            if (d.TryGetValue("#text", out var txt) && isXmlDerived)
                                            {
                                                textValue = txt?.ToString();
                                            }

                                            // Collect attributes (remove @ prefix)
                                            foreach (var p in d)
                                            {
                                                string k = p.Key.ToString();

                                                k = k.Replace(@"GPAL_", "");

                                                if (k.StartsWith("@") && isXmlDerived)
                                                {
                                                    cleanDict[k.Substring(1)] = p.Value;
                                                }
                                            }

                                            // Collect child elements
                                            foreach (var p in d)
                                            {
                                                string k = p.Key.ToString();

                                                k = k.Replace(@"GPAL_", "");

                                                if (k == "#text" || k.StartsWith("@") || k.StartsWith("GPALKEY"))
                                                    continue;

                                                cleanDict[k] = CleanForYaml(p.Value);
                                            }

                                            // Flatten leaf nodes: if only text + attributes -> add "value"
                                            if (isXmlDerived && textValue != null)
                                            {
                                                if (cleanDict.Count == 0)
                                                {
                                                    // Pure text node -> flatten to scalar
                                                    if (double.TryParse(textValue, out double num))
                                                        return num;
                                                    return textValue;
                                                }
                                                else
                                                {
                                                    // Has attributes -> add value field
                                                    cleanDict["value"] = textValue;
                                                }
                                            }

                                            return cleanDict;
                                        }
                                    }
                                    else if (input is IEnumerable enumerable && !(input is string))
                                    {
                                        var list = new List<object>();
                                        foreach (var item in enumerable)
                                        {
                                            list.Add(CleanForYaml(item));
                                        }
                                        return list;
                                    }

                                    return input;
                                }

                                object rootValue = WithoutReaderRoot(ConverterSettings.InputDictionary[yamlIdx], yamlIdx);
                                dynamic cleanRoot = CleanForYaml(rootValue);
                                cleanRoot = ConverterHelper.StripGpalKeys(cleanRoot);

                                if (true == ConverterHelper.IsEnumerableObject(cleanRoot) && !(cleanRoot is IDictionary) && !(cleanRoot is string))
                                {
                                    cleanRoot = new Dictionary<object, object>
                                    {
                                        ["values"] = cleanRoot
                                    };
                                }

                                ConverterSettings.OutputData = serializer.Serialize(cleanRoot);

                                if (true == appendToFile)
                                    File.AppendAllText(filename, ConverterSettings.OutputData);
                                else
                                    File.WriteAllText(filename, ConverterSettings.OutputData);
                            }
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No input data to write to [{filename}]", this, GPALObjectType.Converter);
                            break;
                        case DataFormat.HTML:
                            if (0 < ConverterSettings.InputDictionary.Count)
                            {
                                switch (ConverterSettings.InDataFormat[inputIdx])
                                {
                                    case DataFormat.CARET:
                                    case DataFormat.COLON:
                                    case DataFormat.CSV:
                                    case DataFormat.CUSTOM_DELIMITER:
                                    case DataFormat.DOT:
                                    case DataFormat.HYPHEN:
                                    case DataFormat.LOG:    // a finished line of text, written and read as it stands
                                    case DataFormat.PIPE:
                                    case DataFormat.SEMICOLON:
                                    case DataFormat.SPACE:
                                    case DataFormat.TAB:
                                    case DataFormat.PRN:
                                        {
                                            var sb = new StringBuilder();

                                            var grid = ConverterSettings.InputGrid; // List<List<string>>
                                            if (grid == null || grid.Count == 0)
                                            {
                                                ConverterSettings.OutputData = "<p>No data</p>";
                                                break;
                                            }

                                            int gridIdx = InputWithin(inputIdx, grid.Count, "input grid");

                                            sb.AppendLine("<table border=\"1\" cellpadding=\"8\" cellspacing=\"0\" style=\"border-collapse: collapse; font-family: Arial, sans-serif; width: 100%;\">");

                                            // Determine headers
                                            List<string> headers = null;
                                            int dataStartRow = 0;

                                            if (ConverterSettings.ColumnNames?.Count() > fileIdx  && ConverterSettings.ColumnNames[fileIdx].Count > 0)
                                            {
                                                headers = ConverterSettings.ColumnNames[fileIdx];
                                                dataStartRow = 0;
                                            }
                                            else if (ConverterSettings.FirstLineIsColumnHeaders && grid.Count > 0)
                                            {
                                                headers = grid[gridIdx][0];
                                                dataStartRow = 1;
                                            }

                                            // Emit <thead> if we have headers
                                            if (headers != null)
                                            {
                                                sb.AppendLine("  <thead>");
                                                sb.AppendLine("    <tr>");
                                                foreach (var h in headers)
                                                {
                                                    string safeHeader = System.Net.WebUtility.HtmlEncode(h ?? "");
                                                    sb.AppendLine($"      <th>{safeHeader}</th>");
                                                }
                                                sb.AppendLine("    </tr>");
                                                sb.AppendLine("  </thead>");
                                            }

                                            // Emit <tbody>
                                            sb.AppendLine("  <tbody>");

                                            for (int rowIdx = dataStartRow; rowIdx < grid[gridIdx].Count(); rowIdx++)
                                            {
                                                var row = grid[gridIdx][rowIdx];
                                                sb.AppendLine("    <tr>");

                                                // Pad or truncate row to match column count if headers exist
                                                int colCount = headers?.Count ?? row.Count();
                                                for (int colIdx = 0; colIdx < colCount; colIdx++)
                                                {
                                                    string cell = (colIdx < row.Count()) ? (row[colIdx].ToString() ?? "") : "";
                                                    cell = cell.Trim(ConverterSettings.InDelimiter.Value);
                                                    string safeCell = System.Net.WebUtility.HtmlEncode(cell);
                                                    sb.AppendLine($"      <td>{safeCell}</td>");
                                                }

                                                sb.AppendLine("    </tr>");
                                            }

                                            sb.AppendLine("  </tbody>");
                                            sb.AppendLine("</table>");

                                            ConverterSettings.OutputData = sb.ToString();
                                        }
                                        break;
                                    default:
                                        foreach (Dictionary<object, dynamic> dict in ConverterSettings.InputDictionary)
                                        {
                                            // TODO: CAVEAT: not sure what is going on, we seem to be duplicating the page in our dictionary, so skip if we already wrote out </body>
                                            // Fix loading the dictioanry with multiple copies
                                            if (true == ConverterSettings.OutputData?.Contains("</body>"))
                                                break;

                                            try
                                            {
                                                if (dict["tag"].ToString() == "#text")  // htmlnodes contain text as child nodes, but also a duplicate #text tag following every tag
                                                    continue;
                                            }
                                            catch
                                            {
                                                // it's ok, move on...
                                            }
                                            stringBuilder.AppendLine(ConverterHelper.ConvertDictionaryToHtml(dict));

                                            if (0 < stringBuilder.Length)
                                            {
                                                ConverterSettings.OutputData += stringBuilder.ToString();
                                                stringBuilder.Clear();
                                            }
                                        }
                                        break;
                                }

                                if (true == appendToFile)
                                    File.AppendAllText(filename, ConverterSettings.OutputData);
                                else
                                    File.WriteAllText(filename, ConverterSettings.OutputData);
                            }
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"No input data to write to [{filename}]", this, GPALObjectType.Converter);
                            break;
                    }
                    fileIdx++;
                }
            }
            else if (null != ConverterSettings.OutputDatabase)
            {
                // a grid is rows of parameters and the write runs once per row, so a row holding two values is
                // two parameters. an input with no grid behind it, a plain string, has no rows at all and used to
                // index an empty list here, so it becomes one row holding that value: the single column case
                IGPALGrid<string> parameterGrid;

                if (0 < ConverterSettings.InputGrid.Count && null != ConverterSettings.InputGrid[0])
                    parameterGrid = ConverterSettings.InputGrid[0];
                else
                {
                    parameterGrid = GPAL.Grid.ToGPALObject();

                    foreach (string inputValue in ConverterSettings.InputData)
                        parameterGrid.AddRow(new List<string> { inputValue });
                }

                ConverterSettings.OutputDatabase.Create.ClearParameters.WithParameterGrid(parameterGrid).Execute(out int rowCOunt);
            }
            else if (DataFormat.GRID == ConverterSettings.OutDataFormat && null != ConverterSettings.OutputGrid
                     && (null != ConverterSettings.InputFile || true == ConverterSettings.InDataFormat.Contains(DataFormat.GRID)))
            {
                // rows in, rows out. the object output below reaches a caller's grid through a copy gated on its
                // own converted result having items, and for a grid that result is empty, so a file read into a
                // grid answered with nothing at all. these are the rows as they were read, and they have to be
                // taken here rather than after Convert returns because the settings are cleared on the way out
                foreach (IGPALGrid<string> inGrid in ConverterSettings.InputGrid)
                    foreach (List<string> gridRow in inGrid)
                        ConverterSettings.OutputGrid.AddRow(new List<string>(gridRow));
            }
            else if (null != ConverterSettings.OutputClassType)
            {
                bool yamlDirectDone = false;
                if (ConverterSettings.InDataFormat.Count > 0 && ConverterSettings.InDataFormat[0] == DataFormat.YAML
                    && ConverterSettings.InputData.Count > 0)
                {
                    // Struct types can't be pre-allocated during deserialization, so a YAML alias that
                    // points back through a struct creates infinite recursion — StackOverflowException
                    // which can't be caught. Pre-scan for aliases; if any exist fall through to COFD.
                    bool hasAliases = false;
                    var preParser = new Parser(new System.IO.StringReader(ConverterSettings.InputData[0]));
                    while (preParser.MoveNext())
                    {
                        if (preParser.Current is AnchorAlias)
                        {
                            hasAliases = true;
                            break;
                        }
                    }

                    if (!hasAliases)
                    {
                        try
                        {
                            var deserializer = new DeserializerBuilder()
                                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                .WithTypeConverter(new IPAddressYamlConverter())
                                .WithTypeConverter(new ComplexYamlConverter())
                                .WithTypeConverter(new UriYamlConverter())
                                .WithTypeConverter(new NullableBigIntegerYamlConverter())
                                    .WithTypeConverter(new NullableGuidYamlConverter())
                                    .WithTypeConverter(new NullableTimeSpanYamlConverter())
                                    .WithTypeConverter(new VersionYamlConverter())
                                    .WithTypeConverter(new DateTimeOffsetYamlConverter())
                                .WithTypeConverter(new QuotedStringYamlConverter())
                                .WithTypeConverter(new WaitTimeYamlConverter())
                                .IgnoreUnmatchedProperties()
                                .Build();

                            ConverterSettings.OutputClass = deserializer.Deserialize(ConverterSettings.InputData[0], ConverterSettings.OutputClassType);
                            yamlDirectDone = true;
                        }
                        catch { }
                    }
                }

                if (!yamlDirectDone)
                {
                // XML input arrives with GPAL numeric key prefixes and all values wrapped in
                // List<Dict> + #text nodes. Strip/flatten the whole structure once before
                // any POCO reconstruction so CreateObjectFromDictionary sees clean keys and scalars.
                object classInput = ConverterSettings.InputDictionary;
                if (ConverterSettings.InDataFormat.Count > 0 && ConverterSettings.InDataFormat[0] == DataFormat.XML)
                    classInput = ConverterHelper.StripGpalKeys(ConverterSettings.InputDictionary);

                if (true == ConverterSettings.OutputClassType.IsGenericType && true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType) && 0 < ConverterSettings.InputDictionary.Count)
                {
                    ConverterSettings.OutputClass = ConverterHelper.ConvertToClass(classInput, ConverterSettings.OutputClassType, classInput, ConverterSettings.ColumnNames?[0]);  // NOTE: CAVEAT: hardcoded value
                }
                else if (true == ConverterSettings.OutputClassType.IsGenericType && null != ConverterSettings.OutputClassType.GetInterface("IEnumerable"))
                {
                    // since we can have a dynamic type. we can't easily defined <T> inline, so we have to generate the method signature dynamically
                    if (ConverterSettings.OutputClassElementType == typeof(string))
                    {
                        var resultList = new List<string>();
                        foreach (var dict in ConverterSettings.InputDictionary)
                        {
                            resultList.AddRange(BuildHierarchicalStrings(dict));
                        }
                        ConverterSettings.OutputClass = resultList;
                    }
                    else if (true == ConverterSettings.OutputClassElementType.IsArray)
                    {
                        try
                        {
                            // Ensure OutputClassElementType is valid
                            Type elementType = ConverterSettings.OutputClassElementType;
                            if (elementType == null && ConverterSettings.OutputClassType.IsGenericType)
                            {
                                var genericArgs = ConverterSettings.OutputClassType.GetGenericArguments();
                                elementType = genericArgs.Length > 1 ? genericArgs[1] : genericArgs.FirstOrDefault() ?? typeof(object);
                            }
                            if (elementType == null)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"OutputClassElementType is null for [{ConverterSettings.OutputClassType}]", null, GPALObjectType.Other);
                                ConverterSettings.OutputClass = default(T);
                                return this;
                            }

                            var mi = typeof(ConverterHelper).GetMethod("CreateIEnumerableFromClass",
                                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { typeof(ConverterSettings) },
                                null);

                            var genericMethod = mi.MakeGenericMethod(new[] { elementType });
                            ConverterSettings.OutputClass = (T)genericMethod.Invoke(null, new object[] { ConverterSettings });
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to create IEnumerable for [{ConverterSettings.OutputClassType}]: [{ex.Message}]", null, GPALObjectType.Other, ex);
                            ConverterSettings.OutputClass = default(T);
                            return this;
                        }
                    }
                    else
                    {
                        try
                        {
                            // Ensure OutputClassElementType is valid
                            Type elementType = ConverterSettings.OutputClassElementType;
                            if (elementType == null && ConverterSettings.OutputClassType.IsGenericType)
                            {
                                var genericArgs = ConverterSettings.OutputClassType.GetGenericArguments();
                                elementType = genericArgs.Length > 1 ? genericArgs[1] : genericArgs.FirstOrDefault() ?? typeof(object);
                            }
                            if (elementType == null)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"OutputClassElementType is null for [{ConverterSettings.OutputClassType}]", null, GPALObjectType.Other);
                                ConverterSettings.OutputClass = default(T);
                                return this;
                            }

                            // Get the MethodInfo for ConvertToClass
                            var mi = typeof(ConverterHelper).GetMethod(
                                "ConvertToClass",
                                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { typeof(object), typeof(Type), typeof(object), typeof(List<string>) }, // Parameters dynamic dictList, Type targetType, dynamic parentDictionary, List<string> columnNames
                                null);

                            // Invoke the method
                            ConverterSettings.OutputClass = mi.Invoke(null, new object[] { ConverterSettings.InputDictionary, ConverterSettings.OutputClassType, ConverterSettings.InputDictionary, ConverterSettings.ColumnNames?[0] }); // NOTE: CAVEAT hardcoded value
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to create IEnumerable for [{ConverterSettings.OutputClassType}]: [{ex.Message}]", null, GPALObjectType.Other, ex);
                            ConverterSettings.OutputClass = default(T);
                            return this;
                        }
                    }
                    MethodInfo addMethod = ConverterSettings.OutputClassType.GetMethod("Add",
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { ConverterSettings.OutputClassElementType }, null);
                    MethodInfo addRowMethod = ConverterSettings.OutputClassType.GetMethod("AddRow",
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(List<>).MakeGenericType(ConverterSettings.OutputClassElementType) }, null);
                    MethodInfo enqueueMethod = ConverterSettings.OutputClassType.GetMethod("Enqueue",
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { ConverterSettings.OutputClassElementType }, null);
                    MethodInfo pushMethod = ConverterSettings.OutputClassType.GetMethod("Push",
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { ConverterSettings.OutputClassElementType }, null);

                    // It's an IEnumerable<OutputClass>
                    int count = 0;
                    try
                    {
                        count = ConverterSettings.OutputClass.Count;
                    }
                    catch
                    {
                        foreach (dynamic item in ConverterSettings.OutputClass)
                            count++;
                    }

                    try
                    {
                        if (0 < count)
                        {
                            if (null != addRowMethod)   // GPAGrid
                            {
                                if (null == ConverterSettings.InputData && null != ConverterSettings.InputFile)
                                {
                                    string directory = Path.GetDirectoryName(ConverterSettings.InputFile.Filenames[0]);
                                    string filenamepart = Path.GetFileName(ConverterSettings.InputFile.Filenames[0]);

                                    string filename2 = Directory.GetFiles(false == string.IsNullOrEmpty(directory) ? directory : @".\", filenamepart).FirstOrDefault();
                                    if (false == string.IsNullOrEmpty(filename2))
                                    {
                                        try
                                        {
                                            ConverterSettings.InputData.Add(File.ReadAllText(filename2));
                                        }
                                        catch (Exception ex)
                                        {
                                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to read entire file [{filename2}] into memory. Output may be empty depending on output type.", this, GPALObjectType.Converter, ex);
                                        }
                                    }
                                }

                                // COPY ConverterSettings.OutputClass to the ref outputClass.
                                // once, not once per input: the inputs were read and merged at the top of
                                // Convert, so by here there is one result for the lot. looping InputData again
                                // copied that whole result over for every input there had been.
                                    foreach (var item in ConverterSettings.OutputClass)
                                    {
                                        if (null != addRowMethod)   // GPALGrid
                                        {
                                            addRowMethod.Invoke(outputClass, new object[] { new List<string>() { (dynamic)item } });
                                            /* BUG - CAVEAT: duplicating data
                                            ConverterSettings.OutputData = inData;
                                            if (null != ConverterSettings.InDelimiter && null != ConverterSettings.OutDelimiter)    // CAVEAT: BUG: NOTE: Should this be a list of delim?
                                                ConverterSettings.OutputData = inData.Replace(ConverterSettings.InDelimiter.Value, ConverterSettings.OutDelimiter.Value);
                                            string[] lines = ConverterSettings.OutputData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                                            // Enumerate over the lines using foreach loop
                                            foreach (object line in lines)
                                                addRowMethod.Invoke(outputClass, new object[] { new List<object>() { (dynamic)line } });
                                            */
                                        }
                                    }
                            }
                            else if (null != addMethod || null != enqueueMethod || null != pushMethod)
                            {
                                Type elementType = outputClass.GetType().IsGenericType ? outputClass.GetType().GetGenericArguments()[0] : null;
                                if (elementType != null)
                                {
                                    foreach (var compatibleItem in ConvertToCompatibleItems(ConverterSettings.OutputClass, elementType))
                                    {
                                        if (null != addMethod)
                                            addMethod.Invoke(outputClass, new object[] { compatibleItem });
                                        else if (null != enqueueMethod)
                                            enqueueMethod.Invoke(outputClass, new object[] { compatibleItem });
                                        else if (null != pushMethod)
                                            pushMethod.Invoke(outputClass, new object[] { compatibleItem });
                                    }
                                }
                            }
                            else
                                // we've already converted to this type, might that be the case for a gpal grid, too?
                                outputClass = (T)(object)ConverterSettings.OutputClass;

                            return this;
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Can't find 'Add/Enqueue/Push' method for [{ConverterSettings.OutputClassType}]", ConverterSettings, GPALObjectType.Other, ex);
                    }

                    // not correct but what we are doing
                    // ConverterSettings.OutputClass = ConverterHelper.CreateListFromClass<type>(ConverterSettings.InputDictionary);
                }
                else if (ConverterSettings.OutputClassType.IsGenericType && ConverterSettings.OutputClassType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    // since we can have a dynamic type. we can't easily defined <T> inline, so we have to generate the method signature dynamically
                    if (ConverterSettings.OutputClassElementType == typeof(string))
                    {
                        var resultList = new List<string>();
                        foreach (var dict in ConverterSettings.InputDictionary)
                        {
                            // Build hierarchical strings with full paths
                            resultList.AddRange(BuildHierarchicalStrings(dict));
                        }
                        ConverterSettings.OutputClass = resultList;
                        ConverterSettings.OutputClass = (T)(object)resultList;
                    }
                    else
                    {
                        var mi = typeof(ConverterHelper).GetMethod("CreateListFromClass",
                            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(ConverterSettings) },
                            null);
                        var fooRef = mi.MakeGenericMethod(new[] { ConverterSettings.OutputClassElementType });
                        ConverterSettings.OutputClass = fooRef.Invoke(new ConverterHelper(), new object[] { ConverterSettings });
                    }
                }
                else if (ConverterSettings.OutputClassType.IsArray && ConverterSettings.OutputClassType.GetElementType() == ConverterSettings.OutputClassElementType)
                {
                    // It's an array of OutputClass[]
                    // since we can have a dynamic type. we can't easily defined <T> inline, so we have to generate the method signature dynamically
                    if (ConverterSettings.OutputClassElementType == typeof(string))
                    {
                        var resultList = new List<string>();
                        foreach (var dict in ConverterSettings.InputDictionary)
                        {
                            foreach (var kvp in dict)
                            {
                                resultList.Add(kvp.Value?.ToString() ?? string.Empty);
                            }
                        }
                        ConverterSettings.OutputClass = resultList;
                    }
                    else
                    {
                        var mi = typeof(ConverterHelper).GetMethod("CreateIEnumerableFromClass",
                            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                            null,
                            new[] { typeof(ConverterSettings) },
                            null);

                        var fooRef = mi.MakeGenericMethod(new[] { ConverterSettings.OutputClassElementType });
                        ConverterSettings.OutputClass = fooRef.Invoke(new ConverterHelper(), new dynamic[] { ConverterSettings });
                    }
                }
                //else if (ConverterSettings.OutputClassType == typeof(string))
                //{
                //    outputClass = (T)(object)ConverterSettings.InputData.ToString();
                //}
                else if (ConverterSettings.OutputClassType == ConverterSettings.OutputClassType) // to prevent last else from firing, just yet... how to check this tho...
                {
                    // It's a single OutputClass
                    if (0 < ConverterSettings.InputDictionary.Count)
                        ConverterSettings.OutputClass = ConverterHelper.ConvertToClass(classInput, ConverterSettings.OutputClassType, classInput, ConverterSettings.ColumnNames?[0]);  // NOTE: CAVEAT: hardcoded value
                    //                    ConverterHelper.LoadClassFromDictionary(ConverterSettings.OutputClass, ConverterSettings.InputDictionary[0]);
                }
                else
                {
                    // Unknown type
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unknown type [{ConverterSettings.OutputClass}].", this, GPALObjectType.Converter);
                }
                } // end else (non-YAML path)

                try
                {
                    if (outputClass != ConverterSettings.OutputClass)
                        outputClass = (T)ConverterSettings.OutputClass;
                } catch (Exception)
                {
                    // outputClass may already be populated? so then return it without setting it
                }
            }
            else if (ConverterSettings.OutDataFormat == DataFormat.STRING)
            {
                outputClass = (T)(object)ConverterSettings.InputData;
            }
            #endregion <Output>

            ResetIdleTimer();

            if (null != ConverterSettings.InputFile)
            {
                ((IGPALFileInternal)ConverterSettings.InputFile).FileDictionary.Clear();
                ((IGPALFileInternal)ConverterSettings.InputFile).FileData.Clear();
                ((IGPALFileInternal)ConverterSettings.InputFile).FileGrid.Clear();
                ((IGPALFileInternal)ConverterSettings.InputFile).AlreadyTokenized = false;
            }

            return this;
        }

        /// <summary>
        /// Registers a struct type as a "known" type for class/dictionary conversion, so the converter can correctly
        /// recognize and reconstruct instances of it (rather than treating it as a generic nested dictionary).
        /// </summary>
        /// <param name="knownStructType">The struct Type to register</param>
        /// <returns>Fluent interface to continue configuring the converter input</returns>
        public IAllowConverterInput RegisterKnownStructType(Type knownStructType)
        {
            ConverterSettings.RegisteredStructs.Add(knownStructType);
            return this;
        }

        // RenderTo is the presentation half of the converter, where SaveTo re-encodes losslessly and RenderTo
        // would produce a document the data cannot be read back out of: PDF, PNG, an ASCII or markdown table,
        // an HTML report. None of it is built. All four threw NotImplementedException, so a workflow could
        // write a chain that compiled and then fell over at run time. Commented out, along with their
        // declarations in Interfaces.cs, until there is something behind them.
        //
        // If it is ever built, the cheapest first step is already half written: GPAL.EmitTable and
        // EmitRuntimeTable render an ASCII table with the destination hardcoded to the console.

        ///// <summary>
        ///// Renders the configured input as a visual document (e.g. PDF or image) rather than a text data format,
        ///// storing the rendered output in the given database. Not yet implemented.
        ///// </summary>
        ///// <param name="outputDatabase">The database that would receive the rendered output</param>
        ///// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        //public IAllowConverterInputAndActions RenderTo(IGPALDatabase outputDatabase)
        //{
        //    throw new NotImplementedException();
        //}

        ///// <summary>
        ///// Renders the configured input as a visual document (e.g. PDF or image) rather than a text data format,
        ///// storing the rendered output in the given string. Not yet implemented.
        ///// </summary>
        ///// <param name="outputData">The string that would receive the rendered output</param>
        ///// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        //public IAllowConverterInputAndActions RenderTo(string outputData)
        //{
        //    throw new NotImplementedException();
        //}

        ///// <summary>
        ///// Renders the configured input as a visual document (e.g. PDF or image) rather than a text data format,
        ///// writing the rendered output to the given file. Not yet implemented.
        ///// </summary>
        ///// <param name="outputFile">The file that would receive the rendered output</param>
        ///// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        //public IAllowConverterInputAndActions RenderTo(GPALFile outputFile)
        //{
        //    throw new NotImplementedException();
        //}

        ///// <summary>
        ///// Renders the configured input as a visual document (e.g. PDF or image) rather than a text data format,
        ///// storing the rendered output in the given class instance. Not yet implemented.
        ///// </summary>
        ///// <param name="outputClass">The object that would receive the rendered output</param>
        ///// <returns>Fluent interface to continue building the conversion (e.g. call SaveTo/AppendTo, or AndThen)</returns>
        //public IAllowConverterInputAndActions RenderToClass(out dynamic outputClass)    // NOTE: would this be a thing different from savetoclass?
        //{
        //    throw new NotImplementedException();
        //}
        #region Helpers
        /// <summary>
        /// The data a JSON file held, without the "root" key the file reader puts it under. The reader keeps an object
        /// it read (anything but a top-level array) under "root", so the file's dictionary always has a key to start
        /// from; that key is the reader's, not the data's. A JSON file that itself holds a single "root" key comes
        /// back as it was, since the reader wrapped that too.
        /// </summary>
        /// <param name="dict">One input's dictionary</param>
        /// <param name="index">Which input it is, to read its format</param>
        /// <returns>The value under the reader's "root", or the dictionary as given</returns>
        private object WithoutReaderRoot(Dictionary<object, dynamic> dict, int index)
        {
            object retVal = dict;

            if (null != ConverterSettings.InputFile
                && index < ConverterSettings.InDataFormat.Count
                && DataFormat.JSON == ConverterSettings.InDataFormat[index]
                && 1 == dict.Count
                && true == dict.ContainsKey("root"))
                retVal = dict["root"];

            return retVal;
        }
        /// <summary>
        /// Maps a single delimiter character to its corresponding <see cref="DataFormat"/> (e.g. ',' to CSV, '\t' to TAB).
        /// Returns CUSTOM_DELIMITER for any character not recognized as a known delimiter.
        /// </summary>
        /// <param name="delimiter">The delimiter character to map</param>
        /// <returns>The DataFormat corresponding to the delimiter</returns>
        internal static DataFormat GetDataFormat(char delimiter)
        {
            switch (delimiter)
            {
                case ('^'):
                    return DataFormat.CARET;
                case (':'):
                    return DataFormat.COLON;
                case (','):
                    return DataFormat.CSV;
                case ('.'):
                    return DataFormat.DOT;
                case ('-'):
                    return DataFormat.HYPHEN;
                case ('|'):
                    return DataFormat.PIPE;
                case (' '):
                    return DataFormat.SPACE;
                case (';'):
                    return DataFormat.SEMICOLON;
                case ('\t'):
                    return DataFormat.TAB;
                default:
                    return DataFormat.CUSTOM_DELIMITER;
            }
        }
       
        /// <summary>
        /// Recursively flattens a dictionary or list (e.g. parsed JSON/YAML) into a list of "path=value" strings,
        /// using dot-separated keys and "[index]" segments to represent nesting.
        /// </summary>
        /// <param name="dictOrList">The dictionary, list, or scalar value to flatten</param>
        /// <param name="prefix">The path prefix to prepend to keys at this level (used during recursion)</param>
        /// <returns>A flat list of "path=value" strings representing the hierarchical input</returns>
        private List<string> BuildHierarchicalStrings(dynamic dictOrList, string prefix = "")
        {
            var result = new List<string>();
            if (dictOrList == null) return result;

            // Handle dictionaries (like reporting, uses)
            if (ConverterHelper.IsDictionaryType(dictOrList.GetType()))
            {
                foreach (var kvp in dictOrList)
                {
                    string key = kvp.Key.ToString();
                    dynamic value = kvp.Value;
                    string newPrefix = string.IsNullOrEmpty(prefix) ? "" : $"{prefix}.{key}";

                    if (ConverterHelper.IsDictionaryType(value.GetType()))
                    {
                        result.AddRange(BuildHierarchicalStrings(value));
                    }
                    else if (ConverterHelper.IsEnumerableType(value.GetType()) && value.GetType() != typeof(string))
                    {
                        int index = 0;
                        foreach (var item in value)
                        {
                            string indexedPrefix = $"{newPrefix}[{index}]";
                            result.AddRange(BuildHierarchicalStrings(item));
                            index++;
                        }
                    }
                    else
                    {
                        // Leaf node: add the key-value pair with the full path
                        if (false == string.IsNullOrEmpty(newPrefix))
                            result.Add($"{newPrefix}={value?.ToString() ?? ""}");
                        else
                            result.Add($"{value?.ToString() ?? ""}");
                    }
                }
            }
            // Handle lists (like stored_procs)
            else if (ConverterHelper.IsEnumerableType(dictOrList.GetType()) && dictOrList.GetType() != typeof(string))
            {
                int index = 0;
                foreach (var item in dictOrList)
                {
                    string indexedPrefix = $"{prefix}[{index}]";
                    result.AddRange(BuildHierarchicalStrings(item, indexedPrefix));
                    index++;
                }
            }
            else
            {
                // Leaf node: add the key-value pair with the full path
                result.Add($"{prefix}={dictOrList?.ToString() ?? ""}");
            }

            return result;
        }
        /// <summary>
        /// Recursively walks a source object (which may be a scalar, collection, or dictionary) and collects all
        /// values that are (or can be converted to) the given target type.
        /// </summary>
        /// <param name="sourceItem">The source value, collection, or dictionary to walk</param>
        /// <param name="targetType">The type that returned items must be, or be convertible to</param>
        /// <returns>A flat sequence of values assignable to (or converted to) targetType</returns>
        private static IEnumerable<object> ConvertToCompatibleItems(object sourceItem, Type targetType)
        {
            var results = new List<object>();

            if (sourceItem == null)
                return results;

            // Direct compatibility check
            if (targetType.IsAssignableFrom(sourceItem.GetType()))
            {
                results.Add(sourceItem);
                return results;
            }

            // Handle collections
            if (sourceItem is IEnumerable enumerable && !(sourceItem is string))
            {
                foreach (var nestedItem in enumerable)
                {
                    results.AddRange(ConvertToCompatibleItems(nestedItem, targetType));
                }
                return results;
            }

            // Handle dictionaries
            if (sourceItem is IDictionary dict)
            {
                foreach (var value in dict.Values)
                {
                    if (value != null && targetType.IsAssignableFrom(value.GetType()))
                    {
                        results.Add(value);
                    }
                    else if (value != null)
                    {
                        results.AddRange(ConvertToCompatibleItems(value, targetType));
                    }
                }
                return results;
            }

            // Try type conversion
            var convertedItems = new List<object>();
            var converter = TypeDescriptor.GetConverter(targetType);
            if (converter != null && converter.CanConvertFrom(sourceItem.GetType()))
            {
                try
                {
                    var converted = converter.ConvertFrom(sourceItem);
                    convertedItems.Add(converted);
                }
                catch
                {
                    // Skip items that can't be converted
                }
            }
            else if (targetType == typeof(string))
            {
                try
                {
                    convertedItems.Add(sourceItem.ToString());
                }
                catch
                {
                    // Skip items that can't be converted
                }
            }

            results.AddRange(convertedItems);
            return results;
        }

        internal static class DataFormatDetector
        {
            // Helper to detect the best DataFormat from input string.
            // Focuses on XML, JSON, YAML, CSV first, then others via heuristics.
            // Never throws - returns NOTSET on failure, publishes GPAL warnings.
            // Assumptions: Trims input, checks startsWith/patterns, tries light parsing.
            // For delimiters (CSV, TAB, etc.): Checks first few lines for consistent column count.
            // Add NuGets if needed: Newtonsoft.Json (JSON), YamlDotNet (YAML full parse - optional here).
            /// <summary>
            /// Inspects a raw input string and guesses its <see cref="DataFormat"/> (XML, JSON, YAML, HTML, PDF, a delimited
            /// format, DATABASE, CLASS, or DICTIONARY), falling back to STRING if nothing matches. Never throws; publishes a
            /// GPAL warning/debug event and returns NOTSET/STRING on ambiguous or empty input.
            /// </summary>
            /// <param name="input">The raw input string to inspect</param>
            /// <returns>The detected DataFormat, or STRING if no specific format could be determined</returns>
            public static DataFormat DetectFormat(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.WARNING,
                        "DetectFormat called with empty/null input. Returning NOTSET.",
                        typeof(DataFormatDetector),
                        GPALObjectType.Other);

                    return DataFormat.NOTSET;
                }

                string trimmed = input.Trim();

                // Early check: is this likely base64-encoded data?
                //if (IsLikelyBase64(trimmed))
                //{
                //    return DataFormat.BASE64;
                //}

                string firstLine = trimmed.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                // XML: Starts with <, valid tags (light check)
                if (trimmed.StartsWith("<") && IsLikelyXml(trimmed))
                {
                    return DataFormat.XML;
                }

                // JSON: Starts with { or [, try parse
                if ((trimmed.StartsWith("{") || trimmed.StartsWith("[")) && IsValidJson(trimmed))
                {
                    return DataFormat.JSON;
                }

                // YAML: Key: value patterns, no {/[ start, try basic check (full parse if YamlDotNet added)
                if (!trimmed.StartsWith("{") && !trimmed.StartsWith("[") && !trimmed.StartsWith("<") && IsLikelyYaml(trimmed))
                {
                    return DataFormat.YAML;
                }

                // HTML: Starts with <html> or <!DOCTYPE html> or has <body> etc.
                if (trimmed.StartsWith("<!DOCTYPE html>") || trimmed.StartsWith("<html") || Regex.IsMatch(trimmed, @"<body|</body|<head|</head", RegexOptions.IgnoreCase))
                {
                    return DataFormat.HTML;
                }

                //// post-v1: PDF
                //// PDF: Starts with %PDF- (binary-ish, but string might be base64 or partial)
                //if (trimmed.StartsWith("%PDF-"))
                //{
                //    return DataFormat.PDF;
                //}

                // Tabular/Delimited: CSV, TAB, PIPE, SEMICOLON, SPACE/PRN, CARET, HYPHEN, COLON, DOT, CUSTOM_DELIMITER
                DataFormat delimFormat = DetectDelimiterFormat(trimmed);
                if (delimFormat != DataFormat.NOTSET)
                {
                    return delimFormat;
                }

                // Others: DATABASE (SQL dump?), GRID (maybe table-like), DICTIONARY (key=value?), CLASS (C# code?), STRING (fallback)
                // For now, fallback to STRING if none match
                if (Regex.IsMatch(firstLine, @"^\s*CREATE TABLE|SELECT|INSERT|UPDATE", RegexOptions.IgnoreCase))
                {
                    return DataFormat.DATABASE;
                }
                else if (Regex.IsMatch(trimmed, @"class\s+\w+\s*{|public\s+class", RegexOptions.IgnoreCase))
                {
                    return DataFormat.CLASS;
                }
                else if (IsKeyValueLike(trimmed, "="))
                {
                    return DataFormat.DICTIONARY;
                }

                GPAL.PublishSimpleEvent(
                    GPALEventType.DEBUG,
                    "No clear DataFormat detected for input starting with [" + firstLine.Substring(0, Math.Min(50, firstLine.Length)) + "]. Returning STRING.",
                    input,
                    GPALObjectType.Other);

                return DataFormat.STRING;
            }

            // Returns true if the string strongly resembles base64 encoding
            /// <summary>
            /// Returns true if the given string strongly resembles base64-encoded data (only base64 characters,
            /// plausible length/padding, and sufficient character variety).
            /// </summary>
            /// <param name="s">The string to inspect</param>
            /// <returns>True if the string looks like base64-encoded data</returns>
            private static bool IsLikelyBase64(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                if (s.Length < 12) return false;  // too short to be useful base64

                string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

                // must contain only base64 characters
                foreach (char c in s)
                {
                    if (alphabet.IndexOf(c) == -1)
                    {
                        return false;
                    }
                }

                // length should be multiple of 4 (or close, allowing for missing padding)
                int mod = s.Length % 4;
                if (mod != 0)
                {
                    // accept common unpadded or incorrectly padded cases
                    if (mod != 2 && mod != 0)
                    {
                        return false;
                    }
                }

                // rough entropy check: base64 is never all same character or very low variety
                // (this is optional but helps reject garbage)
                int unique = new HashSet<char>(s).Count;
                if (unique < 5 && s.Length > 20)
                {
                    return false;
                }

                return true;
            }
            /// <summary>
            /// Returns true if the given string can be parsed as XML (a light, full-document XmlReader pass).
            /// </summary>
            /// <param name="input">The string to inspect</param>
            /// <returns>True if the input parses successfully as XML</returns>
            private static bool IsLikelyXml(string input)
            {
                try
                {
                    using (var reader = XmlReader.Create(new StringReader(input)))
                    {
                        while (reader.Read()) { } // Light parse
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Returns true if the given string can be parsed as JSON.
            /// </summary>
            /// <param name="input">The string to inspect</param>
            /// <returns>True if the input parses successfully as JSON</returns>
            private static bool IsValidJson(string input)
            {
                try
                {
                    JsonConvert.DeserializeObject(input); // Or use System.Text.Json.JsonDocument.Parse(input)
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Returns true if the given string looks like YAML, based on a heuristic count of "key: value", indented,
            /// and "- " list-item lines.
            /// </summary>
            /// <param name="input">The string to inspect</param>
            /// <returns>True if the input has enough YAML-like lines to be treated as YAML</returns>
            private static bool IsLikelyYaml(string input)
            {
                // Basic heuristic: lines with key: value, indents, no XML/JSON starts
                // If YamlDotNet added: try YamlStream.Load(new StringReader(input))
                var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int yamlPatterns = lines.Count(line => Regex.IsMatch(line.Trim(), @"^[\w\-]+:\s*(.+|$)") || line.StartsWith("  ") || line.StartsWith("- "));
                return yamlPatterns > 2 && lines.Length > 1; // Threshold
            }

            /// <summary>
            /// Inspects up to the first 5 lines of the input and tries each known delimiter character (comma, tab, pipe,
            /// semicolon, space, caret, hyphen, colon, dot) for a consistent column count, falling back to CUSTOM_DELIMITER
            /// or GRID (fixed-width) if no single delimiter is consistent.
            /// </summary>
            /// <param name="input">The raw input string to inspect</param>
            /// <returns>The detected delimited/grid DataFormat, or NOTSET if none could be determined</returns>
            private static DataFormat DetectDelimiterFormat(string input)
            {
                var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Take(5) // Check header + few rows
                                 .ToArray();

                if (lines.Length < 2) return DataFormat.NOTSET; // Need at least header + data

                // Common delimiters
                var delimiters = new Dictionary<char, DataFormat>
            {
                { ',', DataFormat.CSV },
                { '\t', DataFormat.TAB },
                { '|', DataFormat.PIPE },
                { ';', DataFormat.SEMICOLON },
                { ' ', DataFormat.SPACE }, // PRN is space-delimited
                { '^', DataFormat.CARET },
                { '-', DataFormat.HYPHEN },
                { ':', DataFormat.COLON },
                { '.', DataFormat.DOT }
            };

                foreach (var kvp in delimiters)
                {
                    if (IsConsistentDelimiter(lines, kvp.Key))
                    {
                        if (kvp.Key == ' ') return DataFormat.PRN; // Special for space
                        return kvp.Value;
                    }
                }

                // Custom if mixed but consistent columns
                if (lines.Select(line => line.Split(new[] { ',', ';', '\t', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length).Distinct().Count() == 1)
                {
                    return DataFormat.CUSTOM_DELIMITER;
                }

                // Grid if looks like fixed-width columns
                if (lines.All(line => line.Length == lines[0].Length) && Regex.IsMatch(lines[0], @"[a-zA-Z0-9]+\s+[a-zA-Z0-9]+"))
                {
                    return DataFormat.GRID;
                }

                return DataFormat.NOTSET;
            }

            /// <summary>
            /// Returns true if every line produces the same (greater than one) column count when split on the given
            /// delimiter character.
            /// </summary>
            /// <param name="lines">The lines of input to check</param>
            /// <param name="delim">The delimiter character to test</param>
            /// <returns>True if the delimiter splits every line into the same number of columns (more than one)</returns>
            private static bool IsConsistentDelimiter(string[] lines, char delim)
            {
                var colCounts = lines.Select(line => line.Count(c => c == delim) + 1) // Columns = delim count + 1
                                     .ToArray();

                // Consistent if all rows have same col count, and >1 cols, and not too few delims
                return colCounts.Distinct().Count() == 1 && colCounts[0] > 1 && colCounts[0] == colCounts.Max();
            }

            /// <summary>
            /// Returns true if more than two lines look like "key&lt;separator&gt;value" pairs (i.e. contain the separator
            /// exactly once).
            /// </summary>
            /// <param name="input">The input string to inspect</param>
            /// <param name="separator">The key/value separator to look for (e.g. "=")</param>
            /// <returns>True if the input looks like key/value pair data</returns>
            private static bool IsKeyValueLike(string input, string separator)
            {
                var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return lines.Count(line => line.Contains(separator) && line.Split(separator.ToCharArray()).Length == 2) > 2;
            }
        }

        /// <summary>
        /// Pretty-prints the given input string according to the converter's current input data format
        /// (JSON, XML, HTML, or YAML get re-serialized with indentation; other formats are returned unchanged).
        /// Falls back to returning the input unchanged if pretty-printing fails for any reason.
        /// </summary>
        /// <param name="input">The raw input string to pretty-print</param>
        /// <returns>The pretty-printed string, or the original input if it could not be pretty-printed</returns>
        private string FormatPretty(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var format = ConverterSettings.InDataFormat.FirstOrDefault();

            try
            {
                switch (format)
                {
                    case DataFormat.JSON:
                        var jsonOptions = new System.Text.Json.JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        var jsonElement = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(input);
                        return System.Text.Json.JsonSerializer.Serialize(jsonElement, jsonOptions);

                    case DataFormat.XML:
                        var xmlDoc = System.Xml.Linq.XDocument.Parse(input, System.Xml.Linq.LoadOptions.PreserveWhitespace);
                        return xmlDoc.ToString(System.Xml.Linq.SaveOptions.None);

                    case DataFormat.HTML:
                        // Prefer HtmlAgilityPack for real HTML, fallback to XDocument
                        try
                        {
                            var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                            htmlDoc.LoadHtml(input);
                            return htmlDoc.DocumentNode.WriteContentTo(); // or .OuterHtml with formatting
                        }
                        catch
                        {
                            var xDoc = System.Xml.Linq.XDocument.Parse(input, System.Xml.Linq.LoadOptions.PreserveWhitespace);
                            return xDoc.ToString(System.Xml.Linq.SaveOptions.None);
                        }

                    case DataFormat.YAML:
                        var deserializer = new DeserializerBuilder().Build();
                        var serializer = new SerializerBuilder()
                            .WithIndentedSequences()
                            .WithEventEmitter(next => new HumanReadableAliasEventEmitter(next))
                            .Build();
                        var obj = deserializer.Deserialize<object>(input);
                        return serializer.Serialize(obj);

                    // These formats cannot/should not be pretty-printed
                    case DataFormat.CSV:
                    case DataFormat.TAB:
                    case DataFormat.LOG:    // a finished line of text, written and read as it stands
                    case DataFormat.PIPE:
                    case DataFormat.COLON:
                    case DataFormat.SEMICOLON:
                    case DataFormat.SPACE:
                    case DataFormat.HYPHEN:
                    case DataFormat.CARET:
                    case DataFormat.PRN:
                    case DataFormat.BASE64:
                    case DataFormat.DATABASE:
                    case DataFormat.XLSX:
                    //// post-v1: PDF case DataFormat.PDF:
                    case DataFormat.STRING:
                    case DataFormat.CLASS:
                    case DataFormat.DICTIONARY:
                    case DataFormat.DOT:
                    case DataFormat.GRID:
                        return input;

                    default:
                        return input;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"PrettyPrint failed for [{format}] [{ex.Message}]");
                return input; // safe fallback
            }
        }
        #endregion Helpers
    }
}


