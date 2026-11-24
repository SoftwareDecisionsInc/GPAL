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
    public partial class GPALConverter
    {
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
            ConverterSettings.OutputTypeGiven = DataFormat.NOTSET;
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
            ConverterSettings.OutputTypeGiven = DataFormat.NOTSET;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            // a prior file input would otherwise be converted instead, Convert looks at InputFile first
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = inputDatabase;
            ConverterSettings.InDataFormat.Add(DataFormat.DATABASE);

            return this;
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
            ConverterSettings.OutputTypeGiven = DataFormat.NOTSET;
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
        /// Sets the input to a raw string of data, clearing any previously configured input/output settings.<br/>
        /// The data format is found from the content (JSON/XML/YAML/CSV/etc.); delimited text splits on the delimiter found.
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
            ConverterSettings.OutputTypeGiven = DataFormat.NOTSET;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            // Clear the other input sources, as WithInput(grid) does, so Convert's dispatch (InputFile, then
            // InputDatabase) does not reprocess a prior file or database input instead of this one.
            ConverterSettings.InputFile = null;
            ConverterSettings.InputDatabase = null;
            ConverterSettings.InputData.Add(inputData);
            // the string's format is found from its content, and a delimited string splits on the delimiter found,
            // not on one left by an earlier input
            DataFormat inDataFormat = DataFormatDetector.DetectFormat(inputData);
            ConverterSettings.InDataFormat.Add(inDataFormat);
            ConverterSettings.InDelimiter = ConverterHelper.IsDelimitedDataFormat(inDataFormat) ? ConverterHelper.GetDelimiterFromFormat(inDataFormat) : null;

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
            ConverterSettings.OutputTypeGiven = DataFormat.NOTSET;
            ConverterSettings.ColumnNames = new GPALGrid<string>();  // new input starts a fresh column context; caller WithColumnName(s) set after this survive until the next WithInput
            ConverterSettings.InputDatabase = null;     // a prior database input is not this one
            ConverterSettings.InputFile = inputFile;
            foreach (string filename in inputFile.Filenames)
                ConverterSettings.InDataFormat.Add(ConverterHelper.GetDataFormatFromExtension(filename));
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

            // text held as an object, such as a helper's object parameter, is the same text: it goes where a string goes
            if (inputClass is string handedOverText)
                return WithInput(handedOverText);

            // a list holding one dictionary of rows under GPALKEY keys, the shape the converter holds one input's rows in,
            // is those rows, so json and xml write them as rows, as csv does, and not as a list holding a list
            // (an array of two or more dimensions has no list indexer, so a grid of one value is not looked into)
            if (inputClass is IList sourceList && 1 == sourceList.Count && false == (inputClass is Array heldGrid && 1 < heldGrid.Rank)
                && sourceList[0] is IDictionary sourceRows && 0 < sourceRows.Count
                && true == sourceRows.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY")))
                inputClass = sourceRows;

            CancelIdleTimer();
            ConverterSettings.InDataFormat = new System.Collections.Generic.List<GenerallyPositive.Enums.DataFormat>();
            ConverterSettings.ClearDerived();
            ConverterSettings.InputFirstLineIsColumnHeaders = false;
            ConverterSettings.OutputTypeGiven = DataFormat.NOTSET;
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
            // a value alone, such as a number or a date, is held under "root", as a json file of one value is read, and so
            // is a value written as the text of its converter, such as a System.Drawing.Point or a Font
            ConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { true == ConverterHelper.IsValueAlone(ConverterSettings.InputClassType)
                || true == ConverterHelper.IsTextStructType(ConverterSettings.InputClassType) || true == ConverterHelper.IsTextClassType(ConverterSettings.InputClassType)
                ? new Dictionary<object, dynamic> { ["root"] = inputClass }
                : ConverterHelper.ConvertClassToDictionary(inputClass, null != ConverterSettings.InputClassElementType ? ConverterSettings.InputClassElementType.ToString() : ConverterSettings.InputClassType.ToString(), visited) };
            ConverterSettings.InputData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out IGPALGrid<string> outGrid, DataFormat.CLASS));
            ConverterSettings.InDataFormat.Add(DataFormat.CLASS);
            ConverterSettings.InputGrid.Add(outGrid);

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

        // True when the input at this place was read from XML, or its format is not known; its dictionary then has the
        // XML reader's lists of one around every element
        private bool ReadFromXml(int index) => index >= ConverterSettings.InDataFormat.Count || DataFormat.XML == ConverterSettings.InDataFormat[index];

        // True when the input at this place holds only text, a delimited, xlsx, xml or html file or a grid, which writes a
        // null as the text null
        private bool ReadAsText(int index) => true == ReadAsRows(index) || (index < ConverterSettings.InDataFormat.Count
            && (DataFormat.GRID == ConverterSettings.InDataFormat[index] || DataFormat.XML == ConverterSettings.InDataFormat[index] || DataFormat.HTML == ConverterSettings.InDataFormat[index]));

        // True when the input at this place is a delimited or xlsx file, which holds rows
        private bool ReadAsRows(int index) => index < ConverterSettings.InDataFormat.Count
            && (true == ConverterHelper.IsDelimitedDataFormat(ConverterSettings.InDataFormat[index]) || DataFormat.XLSX == ConverterSettings.InDataFormat[index]);

        /// <summary>
        /// The data a JSON file or string held, without the "root" key the reader puts it under. The reader keeps an object
        /// it read (anything but a top-level array) under "root", so the file's dictionary always has a key to start
        /// from; that key is the reader's, not the data's. A JSON file that itself holds a single "root" key comes
        /// back as it was, since the reader wrapped that too. A value alone given as the input, such as a number or a Point, is
        /// held under "root" the same way. An empty YAML document is held as a null under "root", as an empty JSON file
        /// is, so a YAML mapping holding only a root key with a null value reads as an empty document too. A list written
        /// to YAML is under a values: key; a YAML file holding only that key gives the list, keyed by position as the
        /// reader keys a YAML list.
        /// </summary>
        /// <param name="dict">One input's dictionary</param>
        /// <param name="index">Which input it is, to read its format</param>
        /// <returns>The value under the reader's "root", the list under values:, or the dictionary as given</returns>
        private object WithoutReaderRoot(Dictionary<object, dynamic> dict, int index)
        {
            object retVal = dict;

            if (index < ConverterSettings.InDataFormat.Count
                && 1 == dict.Count
                && true == dict.ContainsKey("root")
                && ((DataFormat.JSON == ConverterSettings.InDataFormat[index] && (null != ConverterSettings.InputFile || 0 < ConverterSettings.InputData.Count))
                    || (DataFormat.YAML == ConverterSettings.InDataFormat[index] && null == dict["root"])
                    || (DataFormat.CLASS == ConverterSettings.InDataFormat[index] && (true == ConverterHelper.IsValueAlone(ConverterSettings.InputClassType)
                        || true == ConverterHelper.IsTextStructType(ConverterSettings.InputClassType) || true == ConverterHelper.IsTextClassType(ConverterSettings.InputClassType)))))
                retVal = dict["root"];
            else if (index < ConverterSettings.InDataFormat.Count && DataFormat.YAML == ConverterSettings.InDataFormat[index]
                && 1 == dict.Count && true == dict.ContainsKey("values") && dict["values"] is System.Collections.IList listed)
            {
                Dictionary<object, dynamic> rows = new Dictionary<object, dynamic>();
                int rowIdx = 0;
                foreach (object row in listed)
                    rows[$"GPALKEY{rowIdx++:D4}"] = row;
                retVal = rows;
            }

            return retVal;
        }

        /// <summary>
        /// The records of one input with their fields named by the column names given for the output file, in order, as a
        /// csv output names its columns. A record of fields has its fields renamed, a record of values, such as a row of a
        /// file with no header line, gets the names as its fields, and a plain value is a record of one field. A record
        /// with more fields than names keeps the others under names made for them, Column3 for the third, with a warning.
        /// The records are the GPALKEY entries or the list items of the data, or the data itself when it is one record.
        /// </summary>
        /// <param name="data">One input's data, as the output writes it</param>
        /// <param name="names">The column names given for the output file</param>
        /// <param name="filename">The output file, named in the warning</param>
        /// <returns>New data holding the renamed records</returns>
        private object NamedRecords(object data, List<string> names, string filename)
        {
            List<string> madeNames = new List<string>();

            object NamedRecord(object record)
            {
                List<object> values = record is IDictionary fields ? fields.Values.Cast<object>().ToList()
                    : record is IList items && false == items.Cast<object>().Any(item => item is IDictionary || item is IList) ? items.Cast<object>().ToList()
                    : null == record || true == ConverterHelper.IsSimpleType(record.GetType()) ? new List<object> { record }
                    : null;
                object named = record;

                if (null != values)
                {
                    List<string> fieldNames = ConverterHelper.NamesForWidth(names, values.Count);
                    Dictionary<object, dynamic> renamed = new Dictionary<object, dynamic>();

                    for (int fieldIdx = 0; fieldIdx < values.Count; fieldIdx++)
                        renamed[fieldNames[fieldIdx]] = values[fieldIdx];

                    foreach (string madeName in fieldNames.Skip(names.Count).Where(madeName => false == madeNames.Contains(madeName)))
                        madeNames.Add(madeName);

                    named = renamed;
                }

                return named;
            }

            object retVal;

            if (data is IDictionary entries && 0 < entries.Count && true == entries.Keys.Cast<object>().All(key => true == key?.ToString().StartsWith("GPALKEY")))
            {
                Dictionary<object, dynamic> renamedEntries = new Dictionary<object, dynamic>();

                foreach (DictionaryEntry entry in entries)
                    renamedEntries[entry.Key] = NamedRecord(entry.Value);

                retVal = renamedEntries;
            }
            else if (data is IList records && false == data is string)
                retVal = records.Cast<object>().Select(NamedRecord).ToList();
            else
                retVal = NamedRecord(data);

            if (0 < madeNames.Count)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{filename}] has [{names.Count}] column names for more fields, so the others are named [{string.Join(",", madeNames)}]", this, GPALObjectType.Converter);

            return retVal;
        }
    }
}
