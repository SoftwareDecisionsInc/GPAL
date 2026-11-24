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
    public partial class GPALConverter : IGPALConverter
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
                    _idleTimer = new System.Threading.Timer(_ => { lock (_timerLock) { DropReadInput(); } }, null, IdleTimeoutMs, System.Threading.Timeout.Infinite);
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

        // run by the idle timer: drops what was read from the input, to free it while the converter waits. A file,
        // database or class is read again on the next save; a string or grid given as the input is the input itself,
        // so it is kept for the next save
        private void DropReadInput()
        {
            List<string> givenText = ConverterSettings.InputData;
            List<IGPALGrid<string>> givenGrids = ConverterSettings.InputGrid;
            bool readAgain = null != ConverterSettings.InputFile || null != ConverterSettings.InputDatabase || null != (object)ConverterSettings.InputClass;

            ConverterSettings.ClearDerived();
            if (false == readAgain)
            {
                if (true == ConverterSettings.InDataFormat.Contains(DataFormat.GRID))
                    ConverterSettings.InputGrid = givenGrids;
                else
                    ConverterSettings.InputData = givenText;
            }
        }

        /// <summary>
        /// Returns this converter cast to <see cref="IGPALConverter"/> to begin a fluent conversion chain.
        /// </summary>
        /// <returns>This GPALConverter as an IGPALConverter</returns>
        public IGPALConverter ToGPALObject()
        {
            return this;
        }

        internal IAllowConverterInputAndActions Convert<T>(ref T outputClass, bool appendToFile = false)
        {
            CancelIdleTimer();
            ConverterHelper.ConverterSettings = ConverterSettings;

            #region <Input>
            IGPALGrid<string> outGrid = GPAL.Grid.ToGPALObject();
            ConverterSettings.OutputData = null;
            ConverterSettings.OutDelimiter = null;     // an earlier conversion's output delimiter is not this one's; each output file sets its own below
            // NOTE: ColumnNames is NOT cleared here. It is reset per WithInput and may hold caller-supplied
            // names (WithColumnName/s) that must survive across chained SaveTo calls; the source-derived
            // fallbacks below only fill it when the caller left it empty.
            IGPALGrid<string> columnNamesGiven = ConverterSettings.ColumnNames;
            bool namesGiven = null != columnNamesGiven && 0 < columnNamesGiven.Count();

            // A conversion from files to files reads its input files one at a time at the start of the output loop:
            // output i reads input i, and outputs that share an input read it once. Its data lists then hold that one
            // file, at index 0.
            bool readPerOutput = null != ConverterSettings.InputFile && null != ConverterSettings.OutputFile;

            // A grid from files reads one file at a time in the grid output below and adds its rows.
            bool readPerGridFile = null != ConverterSettings.InputFile && null == ConverterSettings.OutputFile
                && null == ConverterSettings.OutputDatabase && DataFormat.GRID == ConverterSettings.OutDataFormat
                && null != ConverterSettings.OutputGrid;

            // A database from files reads one file at a time in the database output below and writes its rows.
            bool readPerDatabaseFile = null != ConverterSettings.InputFile && null == ConverterSettings.OutputFile
                && null != ConverterSettings.OutputDatabase;

            // a missing input file leaves an object or collection converted to as it was
            bool inputFileMissing = false;

            if (null != ConverterSettings.InputFile)
            {
                if (true == File.Exists(ConverterSettings.InputFile))
                {
                    if (false == readPerOutput && false == readPerGridFile && false == readPerDatabaseFile && false == FileHelper.TokenizeFile(ConverterSettings.InputFile, ConverterSettings))
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
                        // a file whose unit gave no column names has an empty row, which names nothing
                        foreach (List<string> colRow in ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList)
                            if (0 < colRow.Count)
                                ConverterSettings.ColumnNames.AddRow(new List<string>(colRow));
                    }
                    ConverterSettings.ExcelRowsPerSheet = ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ExcelRowsPerSheet;
                    ConverterSettings.ExcelSheetNames = ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ExcelSheetNames;
                    ConverterSettings.FirstLineIsColumnHeaders = 0 < ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.FirstLineGiven.Count && true == ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.FirstLineGiven[0] ? ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.FirstLineIsColumnNames[0] : ConverterSettings.InputFirstLineIsColumnHeaders;   // default for non-file output paths; overridden per output file below
                    if (false == readPerOutput && false == readPerGridFile && false == readPerDatabaseFile)
                        ConverterSettings.InDelimiter = (0 < ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter.Count ? ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter[0] : null)
                            ?? ConverterHelper.GetDelimiterFromFormat(ConverterHelper.GetDataFormatFromExtension(ConverterSettings.InputFile.Filenames[0]));  // default for non-file output paths; overridden per input file below

                    // InDataFormat needs no repopulating here. It is filled by WithInput, one entry per input, and
                    // nothing empties it afterwards: the idle timer's ClearDerived drops the cached payloads, not
                    // the declared formats. So it survives every SaveTo/AppendTo in a chain on its own.
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File missing: [{ConverterSettings.InputFile.Filename}]", ConverterSettings, GPALObjectType.Other);
                    inputFileMissing = true;
                }
            }
            else if (0 < ConverterSettings.InputData.Count)     // we were given custom input data or type
            {
                // a string is read again on every save; what the save before this one read is cleared first, so a chain of
                // saves does not add its rows again. A class was read once, when it was given
                if (false == ConverterSettings.InDataFormat.Contains(DataFormat.CLASS))
                {
                    ConverterSettings.InputDictionary.Clear();
                    ConverterSettings.InputGrid.Clear();
                }

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
                        case DataFormat.SEMICOLON:
                        case DataFormat.TAB:
                            // text given as input keeps the spaces around its values, as its csv output does
                            ConverterSettings.InputDictionary.Clear();
                            ConverterSettings.InputDictionary.Add(ConverterHelper.ConvertDelimitedToDictionary(ConverterSettings, new StringReader(ConverterSettings.InputData[idx]), null, false));
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid);
                            ConverterSettings.InputGrid.Add(outGrid);
                            break;
                        case DataFormat.STRING:
                            // Text that is no other format splits on the comma, as a csv line does, a line of ragged width
                            // included. A quoted value keeps the comma in it, as "Hello, world" does, and drops the spaces
                            // outside its quotes; a value that is not quoted keeps the spaces around it, and a blank line is
                            // not a value. Only text whose lines split alike on two delimiters, which DetectFormat warns of, is
                            // read one value per line: there a line that is one quoted value, such as "Hello, world", takes
                            // off its quotes, and any other line, such as "Hi," he said, is kept as written
                            ConverterSettings.InputGrid.Add(GPAL.Grid.AddRow(new List<string> { ConverterSettings.InputData[idx] }).ToGPALObject());
                            ConverterSettings.InputDictionary.Clear();
                            if (1 < DataFormatDetector.MostColumnsDelimiters(ConverterSettings.InputData[idx], out _).Count)
                            {
                                Dictionary<object, dynamic> textLines = new Dictionary<object, dynamic>();
                                foreach (string textLine in ConverterSettings.InputData[idx].Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                                    if (false == string.IsNullOrWhiteSpace(textLine))
                                        textLines[$"GPALKEY{textLines.Count:D4}"] = 2 <= textLine.Trim().Length && true == textLine.Trim().StartsWith("\"") && true == textLine.Trim().EndsWith("\"")
                                            && false == textLine.Trim().Substring(1, textLine.Trim().Length - 2).Replace("\"\"", "").Contains("\"")
                                            ? ConverterHelper.SplitRespectingQuotes(textLine, '\n', false)
                                            : new List<string> { textLine };
                                ConverterSettings.InputDictionary.Add(textLines);
                            }
                            else
                                ConverterSettings.InputDictionary.Add(ConverterHelper.ConvertDelimitedToDictionary(ConverterSettings, new StringReader(ConverterSettings.InputData[idx]), null, false));
                            break;
                        case DataFormat.DICTIONARY:
                            break;
                        case DataFormat.JSON:
                            // DateParseHandling.None keeps a date string as the text written, where the default makes it a DateTime
                            // MaxDepth = null reads json nested past the reader's 64 levels, as html read into json is, to the
                            // converter's limit, past which the json is not read
                            CustomJsonConverter textConverter = new CustomJsonConverter(ConverterSettings.InputData[idx]);
                            var desObj = JsonConvert.DeserializeObject<dynamic>(ConverterSettings.InputData[idx], new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, MaxDepth = null, Converters = { textConverter } });
                            if (true == textConverter.TooDeep)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Json input [{idx + 1}] is nested deeper than [{ConverterHelper.NestingLimit}] levels, so it is not read", this, GPALObjectType.Converter);
                                desObj = null;
                            }
                            if (null != desObj)
                            {
                                Dictionary<object, dynamic> jsonDict;
                                if (desObj is IDictionary)
                                {
                                    // under "root", as the JSON file reader puts an object, so a string and a file convert alike
                                    jsonDict = new Dictionary<object, dynamic> { ["root"] = desObj };
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
                                ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid, DataFormat.JSON);
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
                            // the string is the XML itself, not a path to it
                            ConverterSettings.InputXmlDocument = new XmlDocument { PreserveWhitespace = true };
                            ConverterSettings.InputXmlDocument.LoadXml(ConverterSettings.InputData[idx]);
                            ConverterSettings.InputDictionary.Add(ConverterHelper.ConvertXMLToDictionary(ConverterSettings.InputXmlDocument));
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid, DataFormat.XML);
                            ConverterSettings.InputGrid.Add(outGrid);
                            break;
                        case DataFormat.YAML:
                            // a plain number, true, false or null keeps its type, as a JSON string's does; keys are text
                            var deserializer = new DeserializerBuilder()
                                .WithMaximumRecursion(ConverterHelper.NestingLimit)
                                .WithNodeDeserializer(new FileHelper.YamlPlainScalarDeserializer(false))
                                .WithNodeDeserializer(new FileHelper.YamlTextKeyMappingDeserializer())
                                .Build();

                            // yaml nested deeper than the converter reads is not read, as json is not
                            dynamic yamlObject = null;
                            bool yamlTooDeep = false;
                            try
                            {
                                yamlObject = deserializer.Deserialize<object>(ConverterSettings.InputData[idx]);
                            }
                            catch (MaximumRecursionLevelReachedException)
                            {
                                yamlTooDeep = true;
                            }
                            if (true == yamlTooDeep)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Yaml input [{idx + 1}] is nested deeper than [{ConverterHelper.NestingLimit}] levels, so it is not read", this, GPALObjectType.Converter);
                                return this;
                            }
                            var processedYamlDict = new Dictionary<dynamic, dynamic>();

                            if (yamlObject is IDictionary yamlMapping)
                            {
                                foreach (var key in yamlMapping.Keys)
                                {
                                    string keyStr = null == key ? "null_key" : FileHelper.YamlKeyText(key);
                                    processedYamlDict[keyStr] = FileHelper.ProcessDictionaryValue(yamlMapping[key]);
                                }
                            }
                            else if (yamlObject is IList yamlSequence)
                            {
                                // Root is a YAML sequence, not a mapping - synthesize row keys
                                // (same GPALKEYN convention used for headerless CSV rows).
                                int rootIdx = 0;
                                foreach (var item in yamlSequence)
                                {
                                    processedYamlDict[$"GPALKEY{rootIdx++:D4}"] = FileHelper.ProcessDictionaryValue(item);
                                }
                            }
                            else if (null != yamlObject)
                            {
                                // Root is a bare YAML scalar.
                                processedYamlDict["GPALKEY0000"] = FileHelper.ProcessDictionaryValue(yamlObject);
                            }
                            else
                            {
                                // a document holding only ~ or a comment is held as an empty JSON string is, a null under
                                // the reader's root
                                processedYamlDict["root"] = null;
                            }

                            // the grid holds the rows the string writes to csv, as for a YAML file
                            ConverterSettings.InputDictionary.Add(processedYamlDict);
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid, DataFormat.YAML);
                            ConverterSettings.InputGrid.Add(outGrid);
                            break;
                        case DataFormat.HTML:
                            ConverterSettings.InputDictionary = ConverterHelper.ConvertHtmlToDictionary(ConverterSettings.InputData[idx]);
                            // only the grid is kept: adding the csv text to InputData, the list this loop goes through, made a
                            // next pass with no format of its own
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid, DataFormat.HTML);
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
                ConverterSettings.InputDictionary.Clear();
                foreach (IGPALGrid<string> inGrid in ConverterSettings.InputGrid)
                {
                    // A grid whose first line is its column names gives those names, and that line is not a row
                    bool gridFirstLineNames = true == ConverterSettings.InputFirstLineIsColumnHeaders && 0 < inGrid.Rows;
                    List<string> columns = ResolveEffectiveColumns(true == gridFirstLineNames ? inGrid[0] : null);
                    bool haveColumns = 0 < columns.Count;

                    // One dictionary per grid (one grid == one input source), rows keyed under GPALKEY and their
                    // place among the rows, from 0 - the same shape delimited-file input produces - keyed by column
                    // name when columns are known.
                    Dictionary<object, dynamic> source = new Dictionary<object, dynamic>();
                    int lineNumber = 0;
                    foreach (List<string> gridRow in inGrid)
                    {
                        if (true == gridFirstLineNames && 0 == lineNumber)
                        {
                            lineNumber++;
                            continue;
                        }

                        if (haveColumns)
                        {
                            Dictionary<string, string> rowDict = new Dictionary<string, string>();
                            for (int i = 0; i < gridRow.Count; i++)
                                rowDict[i < columns.Count ? columns[i] : $"Column{i + 1}"] = gridRow[i];
                            source[$"GPALKEY{source.Count:D4}"] = rowDict;
                        }
                        else
                            source[$"GPALKEY{source.Count:D4}"] = new List<string>(gridRow);
                        lineNumber++;
                    }
                    ConverterSettings.InputDictionary.Add(source);
                }
            }
            else if (null != (object)ConverterSettings.InputClass && 0 < ConverterSettings.InDataFormat.Count)
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
                // InDataFormat still holds the class's format, given by WithInput; adding it again made a second output file
                ConverterSettings.InputData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out repopGrid, DataFormat.CLASS));
                ConverterSettings.InputGrid.Add(repopGrid);
            }
            #endregion <Input>
            #region <Output>
            StringBuilder stringBuilder = new StringBuilder();
            if (null != ConverterSettings.OutputFile)
            {
                // Pair each output file with an input "dataset" (one per input file, or one for class/grid/database/etc input):
                // output[i] is converted from input[min(i, inputFileCount-1)], so one input is written to every output
                // named. Several input files saved file by file to more output names than inputs: an output named past
                // the last input file has no input of its own and is not written, with a warning naming it.
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

                // with several input files, the output names past the last input file are each reported and taken off
                // the list written
                bool filePerFile = null != ConverterSettings.InputFile && 1 < inputFileCount;
                for (int extraIdx = inputFileCount; true == filePerFile && extraIdx < outputPaths.Count; extraIdx++)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{outputPaths[extraIdx]}] has no input of its own, as [{inputFileCount}] input files are given for [{outputPaths.Count}] output names, so it is not written", this, GPALObjectType.Converter);

                if (true == filePerFile && inputFileCount < outputPaths.Count)
                    outputPaths.RemoveRange(inputFileCount, outputPaths.Count - inputFileCount);

                int fileIdx = 0;
                int readInputIdx = -1;
                bool inputRead = false;
                DataFormat savedOutDataFormat = ConverterSettings.OutDataFormat;
                // each output starts with the column names there were before the outputs, so names found for one output
                // file do not go to the next
                IGPALGrid<string> namesBeforeOutputs = ConverterSettings.ColumnNames;
                foreach (string filename in outputPaths)
                {
                    ConverterSettings.ColumnNames = (IGPALGrid<string>)namesBeforeOutputs?.Clone();

                    // the folders the workflow named are made if they are not there, as every GPAL save does
                    FileHelper.EnsureFolderFor(filename);

                    int inputIdx = Math.Min(fileIdx, inputFileCount - 1);

                    // each output starts with no text, so it holds only its own input's text
                    stringBuilder.Clear();
                    ConverterSettings.OutputData = null;

                    // read the input file this output is converted from, unless the output before it read the same one.
                    // OutDataFormat is put back to what it was before the loop, so the read keeps what it kept when
                    // every file was read before the loop
                    if (true == readPerOutput && inputIdx != readInputIdx)
                    {
                        readInputIdx = inputIdx;
                        ConverterSettings.OutDataFormat = savedOutDataFormat;
                        inputRead = FileHelper.TokenizeFile(ConverterSettings.InputFile, ConverterSettings, inputIdx);
                        ConverterSettings.InputDictionary = ((IGPALFileInternal)ConverterSettings.InputFile).FileDictionary;
                        ConverterSettings.InputData = ((IGPALFileInternal)ConverterSettings.InputFile).FileData;
                        ConverterSettings.InputGrid = ((IGPALFileInternal)ConverterSettings.InputFile).FileGrid;

                        if (false == inputRead)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to tokenize input file [{inputIdx + 1}] of [{ConverterSettings.InputFile.Filename}], so [{filename}] is not written. Continuing.", ConverterSettings, GPALObjectType.Other);
                    }

                    // the index into the data lists: the one file read for this output, or this output's input
                    int dataIdx = true == readPerOutput ? 0 : inputIdx;

                    if (true == readPerOutput && false == inputRead)
                    {
                        fileIdx++;
                        continue;
                    }

                    // each input file splits on the delimiter given for it, else on the one its format names
                    if (null != ConverterSettings.InputFile && inputIdx < ConverterSettings.InputFile.Filenames.Count)
                        ConverterSettings.InDelimiter = (inputIdx < ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter.Count ? ((IGPALFileInternal)ConverterSettings.InputFile).Delimiter[inputIdx] : null)
                            ?? ConverterHelper.GetDelimiterFromFormat(ConverterHelper.GetDataFormatFromExtension(ConverterSettings.InputFile.Filenames[inputIdx]));

                    // each output file is written in the format its extension names. SaveTo and AppendTo set the format
                    // from the first file name only, so a .tsv named after a .csv in the same GPALFile is still a .tsv. A .txt
                    // or .log file names no format, so it is written in the output type WithOutputType gave, when it gave one
                    DataFormat extensionFormat = ConverterHelper.GetDataFormatFromExtension(filename);
                    ConverterSettings.OutDataFormat = DataFormat.LOG == extensionFormat && DataFormat.NOTSET != ConverterSettings.OutputTypeGiven
                        ? ConverterSettings.OutputTypeGiven
                        : extensionFormat;

                    // whether the output file names its own header setting, and whether this appends to a file that
                    // has content. A file made for an input past the named outputs, such as name_2.html, takes the header
                    // setting of the named file it is made from
                    int headerSettingIdx = Math.Min(fileIdx, outputFileSettings.Filenames.Count - 1);
                    bool outputHeaderGiven = headerSettingIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.FirstLineGiven.Count && true == ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.FirstLineGiven[headerSettingIdx];
                    bool appendingToContent = true == appendToFile && true == File.Exists(filename) && 0 < new FileInfo(filename).Length;

                    if (true == outputHeaderGiven)
                        // An explicit output-file setting (true OR false) always wins - this is how the user
                        // turns the header row off despite having named columns.
                        ConverterSettings.FirstLineIsColumnHeaders = ((IGPALFileInternal)ConverterSettings.OutputFile).FirstLineIsColumnNames[headerSettingIdx];
                    else
                        // No explicit setting: emit a header when we have column names to write (named via
                        // WithHeader / WithColumnName(s), or resolved from the source), or when the caller
                        // declared headers via WithFirstLineHasColumnNames. Naming columns implies wanting them.
                        ConverterSettings.FirstLineIsColumnHeaders =
                            (null != ConverterSettings.ColumnNames && 0 < ConverterSettings.ColumnNames.Count())
                            || ConverterSettings.InputFirstLineIsColumnHeaders;

                    // Headers are never repeated when appending to a file that has content; a missing or empty file
                    // gets the header as a saved one does
                    if (true == appendingToContent)
                        ConverterSettings.FirstLineIsColumnHeaders = false;

                    if (fileIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).FieldsEnclosedInQuotes.Count)
                        ConverterSettings.FieldsEnclosedInQuotes = ((IGPALFileInternal)ConverterSettings.OutputFile).FieldsEnclosedInQuotes[fileIdx];

                    // each output file of a delimited format, such as csv, tsv or prn, writes the delimiter given for it,
                    // else the delimiter of its own format, a .tsv after a .csv included. Html, json, yaml, xml, xlsx and
                    // text files have no delimiter to set, so one given for them is not used
                    char? outputFileDelimiter = true == ConverterHelper.IsDelimitedDataFormat(ConverterSettings.OutDataFormat) && fileIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).Delimiter.Count
                        ? ((IGPALFileInternal)ConverterSettings.OutputFile).Delimiter[fileIdx]
                        : null;
                    ConverterSettings.OutDelimiter = outputFileDelimiter ?? ConverterHelper.GetDelimiterFromFormat(ConverterSettings.OutDataFormat);

                    if (null != ConverterSettings.ColumnNames && 0 == ConverterSettings.ColumnNames.Count())
                    {
                        List<string> columns = new List<string>();

                        if ((ConverterHelper.IsDelimitedDataFormat(ConverterSettings.OutDataFormat) || DataFormat.XLSX == ConverterSettings.OutDataFormat) && null != ConverterSettings.OutputFile && null != ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList && fileIdx < ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList.Count() && 0 < ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList[fileIdx].Count)
                            foreach (string columnName in ((IGPALFileInternal)ConverterSettings.OutputFile).FileSettings.ColumnList[fileIdx])
                                columns.Add(columnName);
                        else if (inputIdx < ConverterSettings.InDataFormat.Count && (ConverterHelper.IsDelimitedDataFormat(ConverterSettings.InDataFormat[inputIdx]) || DataFormat.XLSX == ConverterSettings.InDataFormat[inputIdx]) && null != ConverterSettings.InputFile && null != ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList && inputIdx < ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList.Count() && 0 < ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList[inputIdx].Count)
                            foreach (string columnName in ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings.ColumnList[inputIdx])
                                columns.Add(columnName);

                        if (0 < columns.Count)
                            ConverterSettings.ColumnNames.AddRow(columns);
                    }

                    // The column names this output is written with: the ones given to the converter, else the output
                    // file's own, else its input file's own, else for an input that is not a file, such as a database,
                    // the ones it gave before the outputs. And whether its input's rows start with a header line.
                    GPALFileSettings inputFileSettings = null != ConverterSettings.InputFile ? ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings : null;
                    List<string> outputColumnNames = null;

                    if (true == namesGiven)
                        outputColumnNames = columnNamesGiven[0];
                    else if (fileIdx < outputFileSettings.ColumnList.Count() && 0 < outputFileSettings.ColumnList[fileIdx].Count)
                        outputColumnNames = outputFileSettings.ColumnList[fileIdx];
                    else if (null != inputFileSettings && inputIdx < ConverterSettings.InDataFormat.Count
                        && (ConverterHelper.IsDelimitedDataFormat(ConverterSettings.InDataFormat[inputIdx]) || DataFormat.XLSX == ConverterSettings.InDataFormat[inputIdx])
                        && inputIdx < inputFileSettings.ColumnList.Count() && 0 < inputFileSettings.ColumnList[inputIdx].Count)
                        outputColumnNames = inputFileSettings.ColumnList[inputIdx];
                    else if (null == inputFileSettings && null != namesBeforeOutputs && 0 < namesBeforeOutputs.Count())
                        outputColumnNames = namesBeforeOutputs[0];

                    // the column names given for this output file itself, which json, yaml and xml use as the names of the
                    // record fields, as a csv uses them for its columns
                    List<string> outputFileNames = fileIdx < outputFileSettings.ColumnList.Count() && 0 < outputFileSettings.ColumnList[fileIdx].Count
                        ? outputFileSettings.ColumnList[fileIdx]
                        : null;

                    bool inputHasHeaderLine = null != inputFileSettings && inputIdx < inputFileSettings.FirstLineGiven.Count && true == inputFileSettings.FirstLineGiven[inputIdx]
                        ? inputFileSettings.FirstLineIsColumnNames[inputIdx]
                        : ConverterSettings.InputFirstLineIsColumnHeaders;

                    // a workbook read with a header line keeps it as the header of an output given no header setting, as a
                    // delimited file's header line stays the header
                    if (false == outputHeaderGiven && false == appendingToContent && true == inputHasHeaderLine
                        && inputIdx < ConverterSettings.InDataFormat.Count && DataFormat.XLSX == ConverterSettings.InDataFormat[inputIdx])
                        ConverterSettings.FirstLineIsColumnHeaders = true;

                    switch (ConverterSettings.OutDataFormat)
                    {
                        case DataFormat.CUSTOM_DELIMITER:
                        case DataFormat.CARET:
                        case DataFormat.CSV:
                        case DataFormat.LOG:    // a finished line of text, written and read as it stands
                        case DataFormat.PIPE:
                        case DataFormat.PRN:
                        case DataFormat.SEMICOLON:
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
                                        // a file's rows as read, already split into values, so they are not split again; a .txt or
                                        // .log file's rows are its lines, or split on the delimiter given for it. Text given as
                                        // input is as it stands, so it is split below on its own delimiter, keeping the spaces
                                        // around its tokens
                                        if (null != ConverterSettings.InputFile && dataIdx < ConverterSettings.InputGrid.Count)
                                        {
                                            // A header written is this output's column names, else the input's header line,
                                            // and it takes that line's place. An input's header line stays the header unless
                                            // the output turns it off or this appends to a file with content, and it is never
                                            // written as a row
                                            IGPALGrid<string> inputRows = ConverterSettings.InputGrid[dataIdx];
                                            bool headerOn = true == ConverterSettings.FirstLineIsColumnHeaders
                                                || (false == outputHeaderGiven && false == appendingToContent && true == inputHasHeaderLine);
                                            bool writeHeader = true == headerOn && (null != outputColumnNames || (true == inputHasHeaderLine && 0 < inputRows.Rows));
                                            List<List<string>> lines = new List<List<string>>();

                                            int firstRow = true == inputHasHeaderLine ? 1 : 0;

                                            // a header with fewer names than the widest row gets names made for the other columns
                                            if (true == writeHeader)
                                            {
                                                List<string> namesWritten = outputColumnNames ?? inputRows[0];
                                                int widestRow = Enumerable.Range(firstRow, Math.Max(0, inputRows.Rows - firstRow)).Select(rowIdx => inputRows[rowIdx].Count).DefaultIfEmpty(0).Max();

                                                if (namesWritten.Count < widestRow)
                                                {
                                                    int namedCount = namesWritten.Count;
                                                    namesWritten = ConverterHelper.NamesForWidth(namesWritten, widestRow);
                                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{filename}] has [{namedCount}] column names for [{widestRow}] columns, so the others are named [{string.Join(",", namesWritten.Skip(namedCount))}]", this, GPALObjectType.Converter);
                                                }
                                                lines.Add(namesWritten);
                                            }

                                            for (int rowIdx = firstRow; rowIdx < inputRows.Rows; rowIdx++)
                                                lines.Add(inputRows[rowIdx]);

                                            StringBuilder lineBuilder = new StringBuilder();
                                            foreach (List<string> row in lines)
                                            {
                                                foreach (string str in row)
                                                    stringBuilder.Append(ConverterHelper.CreateCSVToken(str, ConverterSettings));   // if these should be wrapped in "", createcsvtoken will do that
                                                if (0 < stringBuilder.Length)
                                                    stringBuilder.Remove(stringBuilder.Length - 1, 1);  // remove trailing delimiter
                                                lineBuilder.AppendLine(stringBuilder.ToString());
                                                stringBuilder.Clear();
                                            }
                                            ConverterSettings.OutputData = lineBuilder.ToString();
                                        }
                                        else
                                        {
                                            if (dataIdx < ConverterSettings.InputData.Count)
                                                ConverterSettings.OutputData = ConverterSettings.InputData[dataIdx];
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
                                                        // a blank line is not a row, as the other outputs read it
                                                        if (true == string.IsNullOrWhiteSpace(line))
                                                            continue;
                                                        // a quoted token keeps the delimiter inside it; the spaces around tokens are kept
                                                        List<string> tokens = ConverterHelper.SplitRespectingQuotes(line, ConverterSettings.InDelimiter.Value, false);
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
                                        }
                                        stringBuilder.Append(ConverterSettings.OutputData);
                                        break;
                                    case DataFormat.HTML:
                                        //stringBuilder.Append(ConverterSettings.InputData[fileIdx]);  // NOTE: this feels like the wrong variable name
                                        //break;
                                    case DataFormat.CLASS:
                                    default:    // nested structure? use dictionary - maybe we can always use the dictionary?
                                        // an empty string given as the input has no records, so it writes nothing, as an empty file does
                                        if (0 < ConverterSettings.InputDictionary.Count)
                                            stringBuilder.Append(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out outGrid, ConverterSettings.InDataFormat[inputIdx]));
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
                                int dictIdx = InputWithin(dataIdx, ConverterSettings.InputDictionary.Count, "input");
                                object source = WithoutReaderRoot(ConverterSettings.InputDictionary[dictIdx], true == readPerOutput ? inputIdx : dictIdx);
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

                                // An object's list of records is held under GPALKEY keys at any depth, such as the
                                // People of a class or each list of a Dictionary<string, List<Item>>. Each is written
                                // as a list, as the top level is, not as an object with keys 0000, 0001
                                object SurrogatesToLists(object value)
                                {
                                    object written = value;
                                    if (value is Dictionary<object, dynamic> members && 0 < members.Count
                                        && true == members.Keys.All(key => System.Text.RegularExpressions.Regex.IsMatch(key?.ToString() ?? "", @"^GPALKEY\d+$")))
                                        written = members.Values.Select(entry => SurrogatesToLists((object)entry)).ToList();
                                    else if (value is Dictionary<object, dynamic> fields)
                                        written = fields.ToDictionary(field => field.Key, field => (dynamic)SurrogatesToLists((object)field.Value));
                                    else if (value is List<object> entries)
                                        written = entries.Select(SurrogatesToLists).ToList();
                                    else if (value is List<Dictionary<object, dynamic>> records)
                                        written = records.Select(record => SurrogatesToLists(record)).ToList();
                                    return written;
                                }
                                rawObject = SurrogatesToLists(rawObject);
                                // a csv or xlsx file holds rows, so one with none, such as a file that is only its header line,
                                // is an empty list, not an empty object, and so is an empty list given as the input or written to YAML,
                                // and an empty json array, which the json reader holds with no root, where it holds an object under root
                                int jsonFormatIdx = true == readPerOutput ? inputIdx : dictIdx;
                                if (rawObject is IDictionary noFields && 0 == noFields.Count
                                    && (true == ReadAsRows(jsonFormatIdx)
                                        || (jsonFormatIdx < ConverterSettings.InDataFormat.Count && DataFormat.JSON == ConverterSettings.InDataFormat[jsonFormatIdx] && true == ReferenceEquals(source, ConverterSettings.InputDictionary[dictIdx]))
                                        || (jsonFormatIdx < ConverterSettings.InDataFormat.Count && DataFormat.CLASS == ConverterSettings.InDataFormat[jsonFormatIdx]
                                            && true == ConverterHelper.IsEnumerableType(ConverterSettings.InputClassType) && false == ConverterHelper.IsDictionaryType(ConverterSettings.InputClassType))
                                        || (false == ReferenceEquals(source, ConverterSettings.InputDictionary[dictIdx]) && source is IDictionary && DataFormat.YAML == ConverterSettings.InDataFormat[jsonFormatIdx])))
                                    rawObject = new List<object>();

                                dynamic cleanObject = ConverterHelper.NormalizeForCleanJson(rawObject);
                                cleanObject = ConverterHelper.StripGpalKeys(cleanObject, ReadFromXml(true == readPerOutput ? inputIdx : dictIdx));
                                // a text null of a csv, xlsx, xml or html file or a grid is a null
                                if (true == ReadAsText(jsonFormatIdx))
                                    cleanObject = ConverterHelper.TextNullsAsNulls((object)cleanObject);
                                if (null != outputFileNames)
                                    cleanObject = NamedRecords((object)cleanObject, outputFileNames, filename);

                                var settings = new JsonSerializerSettings
                                {
                                    // Your existing settings, e.g. NamingStrategy if needed
                                    Converters =
                                        {
                                            new IPAddressJsonConverter(),
                                            new ComplexJsonConverter(),
                                            new UriJsonConverter(),
                                            new VersionJsonConverter(),
                                            // an enum is written by its name, a flags enum by its names, as the other formats write it
                                            new Newtonsoft.Json.Converters.StringEnumConverter(),
                                            // a decimal is written as its digits, 52000 and not 52000.0, as the other formats write it
                                            new DecimalJsonConverter(),
                                            // a double or a float is written as its shortest text, 3 and not 3.0, as the other formats write it
                                            new DoubleJsonConverter(),
                                            // new BigIntegerJsonConverter(), // only if needed
                                            // Add any others you already have (e.g. for quoted strings)
                                        },
                                    // a date is written in ISO 8601 round-trip form, 2019-03-04T00:00:00.0000000, as the other formats write it
                                    DateFormatString = "o",
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
                            // A delimited file, an xlsx file, a grid or a database writes the rows read from it. A class, json,
                            // yaml, xml or html input writes the rows and the header made from its records, as a csv output
                            // does; the grid read from it has no header row.
                            // This output's names name the first sheet; a sheet after it takes its own header line.
                            bool rowsFromDictionary = 0 < ConverterSettings.InputDictionary.Count
                                && inputIdx < ConverterSettings.InDataFormat.Count
                                && (DataFormat.CLASS == ConverterSettings.InDataFormat[inputIdx] || DataFormat.JSON == ConverterSettings.InDataFormat[inputIdx]
                                    || DataFormat.YAML == ConverterSettings.InDataFormat[inputIdx] || DataFormat.XML == ConverterSettings.InDataFormat[inputIdx]
                                    || DataFormat.HTML == ConverterSettings.InDataFormat[inputIdx]);
                            IGPALGrid<string> sheetGrid = dataIdx < ConverterSettings.InputGrid.Count ? ConverterSettings.InputGrid[dataIdx] : null;
                            bool sheetHeaderLines = inputHasHeaderLine;
                            IGPALGrid<string> outputColumnGrid = new GPALGrid<string>();
                            // the input's header line is the header unless the output turns it off, as for a csv output
                            bool sheetHeaderOn = true == ConverterSettings.FirstLineIsColumnHeaders
                                || (false == outputHeaderGiven && false == appendingToContent && true == inputHasHeaderLine);

                            if (true == rowsFromDictionary)
                            {
                                ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out sheetGrid, ConverterSettings.InDataFormat[inputIdx]);
                                sheetHeaderLines = ConverterSettings.FirstLineIsColumnHeaders;
                                sheetHeaderOn = ConverterSettings.FirstLineIsColumnHeaders;
                            }
                            else if (null != outputColumnNames)
                                outputColumnGrid.AddRow(outputColumnNames);

                            if (null != sheetGrid)
                                ExcelHelper.WriteXlsxFromTokenizedData(
                                        outputPath: filename,
                                        grid: sheetGrid, // clone if you don't want the helper to have a reference to your live grid
                                        rowsPerSheet: ConverterSettings.ExcelRowsPerSheet[dataIdx],
                                        columnNamesPerSheet: outputColumnGrid,
                                        sheetNames: ConverterSettings.ExcelSheetNames[dataIdx],
                                        gridHasHeaderLines: sheetHeaderLines,
                                        writeHeaders: sheetHeaderOn
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
                                    // nest a second one inside it. Read per output, every dictionary is this output's input file
                                    int formatIdx = true == readPerOutput ? inputIdx : dictCount;
                                    Dictionary<object, dynamic> content = WithoutReaderRoot(dict, formatIdx) as Dictionary<object, dynamic> ?? dict;

                                    // a page, read from html or its records read back from json or yaml, is written as the page
                                    if (DataFormat.HTML == ConverterSettings.InDataFormat[formatIdx]
                                        || ((DataFormat.JSON == ConverterSettings.InDataFormat[formatIdx] || DataFormat.YAML == ConverterSettings.InDataFormat[formatIdx])
                                            && true == ConverterHelper.HoldsPageRecords(content)))
                                        ConverterHelper.ConvertHtmlDictionaryToXml(content, rootElement); // append to rootElement in method
                                    else if (DataFormat.XML == ConverterSettings.InDataFormat[formatIdx])
                                        ConverterHelper.ConvertDictionaryToXmlElement(content, rootElement, false); // append to rootElement in method
                                    else
                                        ConverterHelper.ConvertDictionaryToXmlElement(null != outputFileNames ? (Dictionary<object, dynamic>)NamedRecords(content, outputFileNames, filename) : content, rootElement); // append to rootElement in method
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
                            // A class is written as it is, its members by name. A list or dictionary of the converter's own
                            // dictionaries, such as rows under GPALKEY keys, and a class whose output file names its fields,
                            // are written from the dictionary below, as json writes them
                            if (null != (object)ConverterSettings.InputClass && null == outputFileNames
                                && false == ConverterSettings.InputClass is List<Dictionary<object, dynamic>> && false == ConverterSettings.InputClass is Dictionary<object, dynamic>)
                            {
                                // members are written with their declared names, and a string that reads as a number,
                                // bool or null is quoted so it reads back as a string
                                var serializerBuilder = new SerializerBuilder()
                                    .WithMaximumRecursion(ConverterHelper.NestingLimit)
                                    .WithObjectGraphTraversalStrategyFactory((ti, tr, tc, depth)
                                        => new FullObjectGraphTraversalStrategy(ti, tr, depth, NullNamingConvention.Instance, new DefaultObjectFactory()))
                                    .WithQuotingNecessaryStrings()
                                    .WithEventEmitter(next => new ExplicitNullYamlEventEmitter(next))
                                    .WithEventEmitter(next => new HumanReadableAliasEventEmitter(next))
                                    .WithTypeConverter(new IPAddressYamlConverter())
                                    .WithTypeConverter(new ComplexYamlConverter())
                                    .WithTypeConverter(new NumberYamlConverter())
                                    .WithTypeConverter(new UriYamlConverter())
                                    .WithTypeConverter(new TextConverterYamlConverter())
                                    .WithTypeConverter(new NullableBigIntegerYamlConverter())
                                    .WithTypeConverter(new NullableGuidYamlConverter())
                                    .WithTypeConverter(new NullableTimeSpanYamlConverter())
                                    .WithTypeConverter(new VersionYamlConverter())
                                    .WithTypeConverter(new DateTimeOffsetYamlConverter())
                                    .WithTypeConverter(new QuotedStringYamlConverter())
                                    .WithTypeConverter(new WaitTimeYamlConverter())
                                    .WithTypeConverter(new MultiDimensionalArrayYamlConverter());

                                if (true == DropAliasesFor(ConverterSettings.InputClass))
                                    serializerBuilder = serializerBuilder.DisableAliases();

                                var serializer = serializerBuilder.Build();

                                // a list is written under a values: key, as a list written from the dictionary below is
                                object yamlRoot = ConverterSettings.InputClass;
                                if (ConverterSettings.InputClass is IEnumerable && false == ConverterSettings.InputClass is IDictionary && false == ConverterSettings.InputClass is string)
                                    yamlRoot = new Dictionary<string, object> { ["values"] = ConverterSettings.InputClass };

                                // data nested deeper than yaml writes is not written, and that is published
                                try
                                {
                                    ConverterSettings.OutputData = serializer.Serialize(yamlRoot);

                                    WriteYamlFile(filename, appendingToContent);
                                }
                                catch (MaximumRecursionLevelReachedException ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{filename}] is not written, as its data is nested deeper than [{ConverterHelper.NestingLimit}] levels", this, GPALObjectType.Converter, ex);
                                }
                            }
                            else if (0 < ConverterSettings.InputDictionary.Count)
                            {
                                int yamlIdx = InputWithin(dataIdx, ConverterSettings.InputDictionary.Count, "input");
                                bool largeGraph = DropAliasesFor(ConverterSettings.InputDictionary[yamlIdx]);

                                // a string that reads as a number, bool or null is quoted so it reads back as a string
                                var serializerBuilder = new SerializerBuilder()
                                    .WithMaximumRecursion(ConverterHelper.NestingLimit)
                                    .WithQuotingNecessaryStrings()
                                    .WithEventEmitter(next => new ExplicitNullYamlEventEmitter(next))
                                    .WithEventEmitter(next => new HumanReadableAliasEventEmitter(next))
                                    .WithTypeConverter(new IPAddressYamlConverter())
                                    .WithTypeConverter(new ComplexYamlConverter())
                                    .WithTypeConverter(new NumberYamlConverter())
                                    .WithTypeConverter(new UriYamlConverter())
                                    .WithTypeConverter(new TextConverterYamlConverter())
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
                                                    return ConverterHelper.XmlTextValue(textValue);
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

                                object rootValue = WithoutReaderRoot(ConverterSettings.InputDictionary[yamlIdx], true == readPerOutput ? inputIdx : yamlIdx);
                                // a csv or xlsx file with no rows, or an empty json array, as for JSON above
                                int yamlFormatIdx = true == readPerOutput ? inputIdx : yamlIdx;
                                if (rootValue is IDictionary noFields && 0 == noFields.Count
                                    && (true == ReadAsRows(yamlFormatIdx)
                                        || (yamlFormatIdx < ConverterSettings.InDataFormat.Count && DataFormat.JSON == ConverterSettings.InDataFormat[yamlFormatIdx] && true == ReferenceEquals(rootValue, ConverterSettings.InputDictionary[yamlIdx]))))
                                    rootValue = new List<object>();
                                dynamic cleanRoot = CleanForYaml(rootValue);
                                cleanRoot = ConverterHelper.StripGpalKeys(cleanRoot, ReadFromXml(true == readPerOutput ? inputIdx : yamlIdx));
                                // a text null of a csv, xlsx, xml or html file or a grid is a null
                                if (true == ReadAsText(yamlFormatIdx))
                                    cleanRoot = ConverterHelper.TextNullsAsNulls((object)cleanRoot);
                                if (null != outputFileNames)
                                    cleanRoot = NamedRecords((object)cleanRoot, outputFileNames, filename);

                                if (true == ConverterHelper.IsEnumerableObject(cleanRoot) && !(cleanRoot is IDictionary) && !(cleanRoot is string))
                                {
                                    cleanRoot = new Dictionary<object, object>
                                    {
                                        ["values"] = cleanRoot
                                    };
                                }

                                // data nested deeper than yaml writes is not written, and that is published
                                try
                                {
                                    ConverterSettings.OutputData = serializer.Serialize(cleanRoot);

                                    WriteYamlFile(filename, appendingToContent);
                                }
                                catch (MaximumRecursionLevelReachedException ex)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{filename}] is not written, as its data is nested deeper than [{ConverterHelper.NestingLimit}] levels", this, GPALObjectType.Converter, ex);
                                }
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
                                    case DataFormat.XLSX:   // a workbook's rows make a table, as a delimited file's do
                                        {
                                            var sb = new StringBuilder();

                                            var grid = ConverterSettings.InputGrid; // List<List<string>>
                                            if (grid == null || grid.Count == 0)
                                            {
                                                ConverterSettings.OutputData = "<p>No data</p>";
                                                break;
                                            }

                                            int gridIdx = InputWithin(dataIdx, grid.Count, "input grid");

                                            sb.AppendLine("<table border=\"1\" cellpadding=\"8\" cellspacing=\"0\" style=\"border-collapse: collapse; font-family: Arial, sans-serif; width: 100%;\">");

                                            // Determine headers
                                            List<string> headers = null;
                                            int dataStartRow = 0;

                                            // the rows of the table: the input's grid, or for a workbook of several sheets read with a
                                            // header line, one header line naming every sheet's columns and each sheet's rows under
                                            // their own names, as the csv output of the workbook writes them
                                            IGPALGrid<string> tableRows = grid[gridIdx];
                                            List<string> tableNames = outputColumnNames;

                                            if (DataFormat.XLSX == ConverterSettings.InDataFormat[inputIdx] && true == inputHasHeaderLine
                                                && 1 < ConverterSettings.ExcelRowsPerSheet[dataIdx].Count(rowCount => 0 < rowCount))
                                            {
                                                List<string> unionNames = new List<string>();
                                                List<List<int>> sheetColumns = new List<List<int>>();   // each sheet's column positions in the union
                                                int sheetStart = 0;

                                                foreach (int sheetRows in ConverterSettings.ExcelRowsPerSheet[dataIdx].Where(rowCount => 0 < rowCount))
                                                {
                                                    // a name takes the union column of that name this sheet has not used yet, else a new one;
                                                    // a name the sheet's header line repeats is made its own, B_1, as the reader makes it
                                                    List<int> positions = new List<int>();
                                                    foreach (string name in ConverterHelper.UniqueNames(grid[gridIdx][sheetStart], null))
                                                    {
                                                        int unionIdx = Enumerable.Range(0, unionNames.Count).Where(idx => unionNames[idx] == name && false == positions.Contains(idx)).DefaultIfEmpty(-1).First();
                                                        if (0 > unionIdx)
                                                        {
                                                            unionIdx = unionNames.Count;
                                                            unionNames.Add(name);
                                                        }
                                                        positions.Add(unionIdx);
                                                    }
                                                    sheetColumns.Add(positions);
                                                    sheetStart += sheetRows;
                                                }

                                                // the names the workbook's first sheet gave are every sheet's names now
                                                if (null != tableNames && true == tableNames.SequenceEqual(ConverterHelper.UniqueNames(grid[gridIdx][0], null)))
                                                    tableNames = unionNames;

                                                tableRows = new GPALGrid<string>();
                                                tableRows.AddRow(unionNames);
                                                sheetStart = 0;
                                                int sheetIdx = 0;

                                                foreach (int sheetRows in ConverterSettings.ExcelRowsPerSheet[dataIdx].Where(rowCount => 0 < rowCount))
                                                {
                                                    for (int rowIdx = sheetStart + 1; rowIdx < sheetStart + sheetRows; rowIdx++)
                                                    {
                                                        List<string> unionRow = Enumerable.Repeat(string.Empty, unionNames.Count).ToList();
                                                        List<string> sheetRow = grid[gridIdx][rowIdx];
                                                        for (int colIdx = 0; colIdx < sheetRow.Count && colIdx < sheetColumns[sheetIdx].Count; colIdx++)
                                                            unionRow[sheetColumns[sheetIdx][colIdx]] = sheetRow[colIdx];
                                                        tableRows.AddRow(unionRow);
                                                    }
                                                    sheetStart += sheetRows;
                                                    sheetIdx++;
                                                }
                                            }

                                            // the header is on as for a csv output: the output's own setting, else the names given,
                                            // else the input's header line. An output given WithFirstLineIsColumnNames(false) has
                                            // no header, and its input's header line is not written as a row
                                            bool headerOn = true == ConverterSettings.FirstLineIsColumnHeaders
                                                || (false == outputHeaderGiven && false == appendingToContent && true == inputHasHeaderLine);

                                            if (true == headerOn && null != tableNames)
                                            {
                                                headers = tableNames;
                                                // the names take the place of the input's header line
                                                dataStartRow = true == inputHasHeaderLine ? 1 : 0;
                                            }
                                            else if (true == headerOn && true == inputHasHeaderLine && 0 < grid.Count)
                                            {
                                                // only an input read with a header line has one; an input with none starts with a row
                                                headers = tableRows[0];
                                                dataStartRow = 1;
                                            }
                                            else if (true == inputHasHeaderLine)
                                                dataStartRow = 1;

                                            // the table is as wide as its widest row or its names, whichever is wider, so a row
                                            // wider than the names keeps its values, as the csv does, under names made for them
                                            int widestRow = tableRows.Skip(dataStartRow).Select(gridLine => gridLine.Count()).DefaultIfEmpty(0).Max();
                                            int colCount = Math.Max(headers?.Count ?? 0, widestRow);

                                            if (null != headers && headers.Count < colCount)
                                            {
                                                int namedCount = headers.Count;
                                                headers = ConverterHelper.NamesForWidth(headers, colCount);
                                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{filename}] has [{namedCount}] column names for [{colCount}] columns, so the others are named [{string.Join(",", headers.Skip(namedCount))}]", this, GPALObjectType.Converter);
                                            }

                                            // Emit <thead> if we have headers
                                            if (headers != null)
                                            {
                                                sb.AppendLine("  <thead>");
                                                sb.AppendLine("    <tr>");
                                                for (int colIdx = 0; colIdx < colCount; colIdx++)
                                                {
                                                    string safeHeader = System.Net.WebUtility.HtmlEncode(colIdx < headers.Count ? headers[colIdx] ?? "" : "");
                                                    sb.AppendLine($"      <th>{safeHeader}</th>");
                                                }
                                                sb.AppendLine("    </tr>");
                                                sb.AppendLine("  </thead>");
                                            }

                                            // Emit <tbody>
                                            sb.AppendLine("  <tbody>");

                                            for (int rowIdx = dataStartRow; rowIdx < tableRows.Count(); rowIdx++)
                                            {
                                                var row = tableRows[rowIdx];
                                                sb.AppendLine("    <tr>");

                                                // each row is padded to the table's width
                                                for (int colIdx = 0; colIdx < colCount; colIdx++)
                                                {
                                                    string cell = (colIdx < row.Count()) ? (row[colIdx].ToString() ?? "") : "";
                                                    // a workbook has no delimiter to trim
                                                    if (null != ConverterSettings.InDelimiter)
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
                                        int htmlDictCount = 0;
                                        foreach (Dictionary<object, dynamic> dict in ConverterSettings.InputDictionary)
                                        {
                                            // the JSON reader's "root" key is not the data's, so the page starts with what it holds;
                                            // an empty json or yaml document, a null under that key, gives an empty page.
                                            // Read per output, every dictionary is this output's input file
                                            int htmlFormatIdx = true == readPerOutput ? inputIdx : htmlDictCount++;
                                            object pageData = WithoutReaderRoot(dict, htmlFormatIdx);
                                            // a value alone, such as the 5 of a json file or a number given as the input, is a list
                                            // of one value, as a yaml file of one value is
                                            Dictionary<object, dynamic> content = null == pageData
                                                ? new Dictionary<object, dynamic>()
                                                : pageData as Dictionary<object, dynamic>
                                                    ?? (pageData is System.Collections.IDictionary || pageData is System.Collections.IList ? dict : new Dictionary<object, dynamic> { ["GPALKEY0000"] = pageData });

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
                                            // A list of rows with the same members, each a plain value, is a table: the header and
                                            // rows its csv output writes, as the table of a csv input. The list may sit under one
                                            // member, such as {"people": [...]} or <root><row>...</row></root>. Rows the csv writes
                                            // under a Key column are a table too: a dictionary of objects given as the input, such
                                            // as a Dictionary<string, Item>, and named lists of records: two or more, such as
                                            // {"clients": {"A": [...], "B": [...]}}, or one or more in a dictionary given as the
                                            // input, such as a Dictionary<Department, List<Employee>>. A list of such lists or
                                            // groups, such as [[{...}], [{...}, {...}]], is a table of all their records. An html
                                            // page and any other shape are written as a page
                                            DataFormat recordsFormat = ConverterSettings.InDataFormat[Math.Min(htmlFormatIdx, ConverterSettings.InDataFormat.Count - 1)];
                                            bool dictionaryGiven = DataFormat.CLASS == recordsFormat && null != ConverterSettings.InputClassType && true == ConverterHelper.IsDictionaryType(ConverterSettings.InputClassType);
                                            object shaped = ConverterHelper.StripGpalKeys(ConverterHelper.NormalizeForCleanJson(pageData), ReadFromXml(htmlFormatIdx));
                                            // the entries of a list, which a list inside an object reaches here as a dictionary
                                            // keyed 0000, 0001 and so on; null for anything else
                                            Func<object, List<object>> listEntries = value => value is IList entryList && false == value is string ? entryList.Cast<object>().ToList()
                                                : value is IDictionary numbered && 0 < numbered.Count && true == numbered.Keys.Cast<object>().All(key => true == Regex.IsMatch(key?.ToString() ?? "", @"^\d+$")) ? numbered.Values.Cast<object>().ToList()
                                                : null;
                                            // the dictionary given as the input, whose keys are its own even when they are numbers
                                            object givenDictionary = true == dictionaryGiven ? shaped : null;
                                            // the rows a value holds: the entries of a list, an entry that is itself a list of
                                            // records or named lists of records giving its records; the records of named lists of
                                            // records, two or more, or one or more in the dictionary given; the objects of the
                                            // dictionary given. null for anything else
                                            Func<object, List<object>> rowsIn = null;
                                            rowsIn = value =>
                                            {
                                                bool isGiven = null != givenDictionary && true == ReferenceEquals(givenDictionary, value);
                                                List<object> entries = true == isGiven ? null : listEntries(value);
                                                List<List<object>> groupsRows = null == entries && value is IDictionary groups && (true == isGiven ? 0 : 1) < groups.Count
                                                    ? groups.Values.Cast<object>().Select(group => rowsIn(group)).ToList()
                                                    : null;
                                                List<object> rows = null;
                                                if (null != entries)
                                                    rows = entries.SelectMany(entry => rowsIn(entry) is List<object> inner && 0 < inner.Count && true == inner.All(record => record is IDictionary) ? inner : new List<object> { entry }).ToList();
                                                else if (null != groupsRows && true == groupsRows.All(records => null != records && 0 < records.Count && true == records.All(record => record is IDictionary)))
                                                    rows = groupsRows.SelectMany(records => records).ToList();
                                                else if (true == isGiven && value is IDictionary objects && 0 < objects.Count && true == objects.Values.Cast<object>().All(entry => entry is IDictionary))
                                                    rows = objects.Values.Cast<object>().ToList();
                                                return rows;
                                            };
                                            List<object> tableRecords = null;
                                            while (null == tableRecords && null != shaped)
                                            {
                                                tableRecords = rowsIn(shaped);
                                                if (null == tableRecords)
                                                    shaped = shaped is IDictionary wrapper && 1 == wrapper.Count ? wrapper.Values.Cast<object>().First() : null;
                                            }
                                            List<string> firstMembers = tableRecords?.FirstOrDefault() is IDictionary firstRecord ? firstRecord.Keys.Cast<object>().Select(key => key?.ToString()).ToList() : null;
                                            bool isTable = DataFormat.HTML != recordsFormat && null != firstMembers && 0 < firstMembers.Count
                                                && true == tableRecords.All(record => record is IDictionary members && members.Count == firstMembers.Count
                                                    && true == members.Keys.Cast<object>().All(key => true == firstMembers.Contains(key?.ToString()))
                                                    && true == members.Values.Cast<object>().All(value => false == value is IDictionary && (value is string || false == value is IEnumerable)));

                                            if (true == isTable)
                                            {
                                                // the rows the csv writer makes from these records, its names first
                                                List<Dictionary<object, dynamic>> savedInput = ConverterSettings.InputDictionary;
                                                bool savedFirstLine = ConverterSettings.FirstLineIsColumnHeaders;
                                                bool savedNamesRow = ConverterSettings.GridHasNamesRow;
                                                ConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { dict };
                                                ConverterSettings.FirstLineIsColumnHeaders = true;
                                                ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out IGPALGrid<string> recordsGrid, recordsFormat);
                                                bool namesRow = ConverterSettings.GridHasNamesRow;
                                                ConverterSettings.InputDictionary = savedInput;
                                                ConverterSettings.FirstLineIsColumnHeaders = savedFirstLine;
                                                ConverterSettings.GridHasNamesRow = savedNamesRow;

                                                // the names are the header unless the output is given WithFirstLineIsColumnNames(false) or
                                                // this appends to a file with content, as a csv output does
                                                bool recordsHeaderOn = true == savedFirstLine || (false == outputHeaderGiven && false == appendingToContent);
                                                stringBuilder.AppendLine("<table border=\"1\" cellpadding=\"8\" cellspacing=\"0\" style=\"border-collapse: collapse; font-family: Arial, sans-serif; width: 100%;\">");
                                                if (true == namesRow && true == recordsHeaderOn && 0 < recordsGrid.Count())
                                                {
                                                    stringBuilder.AppendLine("  <thead>");
                                                    stringBuilder.AppendLine("    <tr>");
                                                    foreach (string name in recordsGrid[0])
                                                        stringBuilder.AppendLine($"      <th>{System.Net.WebUtility.HtmlEncode(name ?? "")}</th>");
                                                    stringBuilder.AppendLine("    </tr>");
                                                    stringBuilder.AppendLine("  </thead>");
                                                }
                                                stringBuilder.AppendLine("  <tbody>");
                                                for (int rowIdx = true == namesRow ? 1 : 0; rowIdx < recordsGrid.Count(); rowIdx++)
                                                {
                                                    stringBuilder.AppendLine("    <tr>");
                                                    foreach (string cell in recordsGrid[rowIdx])
                                                        stringBuilder.AppendLine($"      <td>{System.Net.WebUtility.HtmlEncode(cell ?? "")}</td>");
                                                    stringBuilder.AppendLine("    </tr>");
                                                }
                                                stringBuilder.AppendLine("  </tbody>");
                                                stringBuilder.AppendLine("</table>");
                                            }
                                            else
                                                stringBuilder.AppendLine(ConverterHelper.ConvertDictionaryToHtml(content));

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

                // the names found for the files are not kept; the next conversion starts with the ones given
                ConverterSettings.ColumnNames = columnNamesGiven;
            }
            else if (null != ConverterSettings.OutputDatabase)
            {
                // a grid is rows of parameters and the write runs once per row, so a row holding two values is
                // two parameters. an input with no grid behind it, a plain string, has no rows at all and used to
                // index an empty list here, so it becomes one row holding that value: the single column case
                // From files, each file is read in turn and its rows written, so every file goes to the database and one
                // file is held at a time
                if (true == readPerDatabaseFile)
                {
                    for (int fileIndex = 0; fileIndex < ConverterSettings.InputFile.Filenames.Count; fileIndex++)
                    {
                        if (true == FileHelper.TokenizeFile(ConverterSettings.InputFile, ConverterSettings, fileIndex))
                        {
                            GPALFileSettings databaseFileSettings = ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings;
                            DataFormat databaseFileFormat = ConverterHelper.GetDataFormatFromExtension(ConverterSettings.InputFile.Filenames[fileIndex]);
                            bool fileHasHeaderLine = fileIndex < databaseFileSettings.FirstLineGiven.Count && true == databaseFileSettings.FirstLineGiven[fileIndex]
                                ? databaseFileSettings.FirstLineIsColumnNames[fileIndex]
                                : ConverterSettings.InputFirstLineIsColumnHeaders;
                            int gridIndex = 0;

                            foreach (IGPALGrid<string> fileGrid in ((IGPALFileInternal)ConverterSettings.InputFile).FileGrid)
                            {
                                // A file read with a header line keeps its names line in its rows: a workbook at the top of
                                // each sheet, a delimited, xml or html file at the top. The names are not a row to write.
                                HashSet<int> headerRows = new HashSet<int>();

                                if (true == fileHasHeaderLine && DataFormat.XLSX == databaseFileFormat)
                                {
                                    int sheetStart = 0;

                                    // an empty sheet has no rows, so no header line
                                    foreach (int sheetRows in databaseFileSettings.ExcelRowsPerSheet[gridIndex])
                                    {
                                        if (0 < sheetRows)
                                            headerRows.Add(sheetStart);

                                        sheetStart += sheetRows;
                                    }
                                }
                                else if (true == fileHasHeaderLine
                                    && (DataFormat.CSV == databaseFileFormat || DataFormat.TAB == databaseFileFormat || DataFormat.PIPE == databaseFileFormat
                                        || DataFormat.CARET == databaseFileFormat || DataFormat.PRN == databaseFileFormat
                                        || DataFormat.XML == databaseFileFormat || DataFormat.HTML == databaseFileFormat))
                                    headerRows.Add(0);

                                IGPALGrid<string> rowsToWrite = fileGrid;

                                if (0 < headerRows.Count)
                                {
                                    rowsToWrite = GPAL.GridForType<string>();

                                    for (int rowIdx = 0; rowIdx < fileGrid.Rows; rowIdx++)
                                    {
                                        if (false == headerRows.Contains(rowIdx))
                                            rowsToWrite.AddRow(fileGrid[rowIdx]);
                                    }
                                }

                                ConverterSettings.OutputDatabase.Create.ClearParameters.WithParameterGrid(rowsToWrite).Execute(out int fileRowCount);
                                gridIndex++;
                            }
                        }
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to tokenize input file [{fileIndex + 1}] of [{ConverterSettings.InputFile.Filename}], so its rows are not written. Continuing.", ConverterSettings, GPALObjectType.Other);
                    }
                }
                else
                {
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
            }
            else if (DataFormat.GRID == ConverterSettings.OutDataFormat && null != ConverterSettings.OutputGrid
                     && (null != ConverterSettings.InputFile || true == ConverterSettings.InDataFormat.Contains(DataFormat.GRID)
                         || 0 < ConverterSettings.InputData.Count))
            {
                // An object or a string given as the input has its rows in its grid too, the rows its csv writes, made
                // when the object was given or the string was read; filled as a class, the grid took one value per row
                // rows in, rows out. the object output below reaches a caller's grid through a copy gated on its
                // own converted result having items, and for a grid that result is empty, so a file read into a
                // grid answered with nothing at all. these are the rows as they were read, and they have to be
                // taken here rather than after Convert returns because the settings are cleared on the way out.
                // From files, each file is read in turn, so one file is read at a time.
                // The grid starts with the column names, as the csv written with a header line does: a file or sheet whose
                // first line is its column names gives that line, and records that name their fields, such as those of a
                // json, yaml, xml or html file, a json string or a class, give those names. Each unit, a file, a sheet or
                // the one input, is its names, or none, and its rows. A unit writes its names before its rows, as each
                // file's own column names apply to that file, unless the same names were written already, so a later file's
                // header line that repeats them is not written again as a row
                List<KeyValuePair<List<string>, List<List<string>>>> units = new List<KeyValuePair<List<string>, List<List<string>>>>();
                bool savedFirstLine = ConverterSettings.FirstLineIsColumnHeaders;
                List<Dictionary<object, dynamic>> savedDictionary = ConverterSettings.InputDictionary;

                // the grid the csv writer makes from records with a header line, as a unit of its names and its rows
                void AddRecordsUnit(Dictionary<object, dynamic> records, DataFormat recordsFormat)
                {
                    ConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { records };
                    ConverterSettings.FirstLineIsColumnHeaders = true;
                    ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out IGPALGrid<string> recordsGrid, recordsFormat);
                    List<List<string>> recordRows = recordsGrid.Select(gridRow => new List<string>(gridRow)).ToList();
                    List<string> recordNames = true == ConverterSettings.GridHasNamesRow && 0 < recordRows.Count ? recordRows[0] : null;
                    units.Add(new KeyValuePair<List<string>, List<List<string>>>(recordNames, null != recordNames ? recordRows.Skip(1).ToList() : recordRows));
                }

                // the rows of a grid as read, its first row the names when the grid has a header line
                void AddGridUnit(IEnumerable<List<string>> gridRows, bool firstRowNames)
                {
                    List<List<string>> unitRows = gridRows.Select(gridRow => new List<string>(gridRow)).ToList();
                    List<string> unitNames = true == firstRowNames && 0 < unitRows.Count ? unitRows[0] : null;
                    units.Add(new KeyValuePair<List<string>, List<List<string>>>(unitNames, null != unitNames ? unitRows.Skip(1).ToList() : unitRows));
                }

                if (true == readPerGridFile)
                {
                    GPALFileSettings gridFileSettings = ((IGPALFileInternal)ConverterSettings.InputFile).FileSettings;
                    for (int fileIndex = 0; fileIndex < ConverterSettings.InputFile.Filenames.Count; fileIndex++)
                    {
                        if (true == FileHelper.TokenizeFile(ConverterSettings.InputFile, ConverterSettings, fileIndex))
                        {
                            DataFormat fileFormat = ConverterHelper.GetDataFormatFromExtension(ConverterSettings.InputFile.Filenames[fileIndex]);
                            bool fileHeaderLine = fileIndex < gridFileSettings.FirstLineGiven.Count && true == gridFileSettings.FirstLineGiven[fileIndex]
                                ? gridFileSettings.FirstLineIsColumnNames[fileIndex]
                                : ConverterSettings.InputFirstLineIsColumnHeaders;
                            List<IGPALGrid<string>> fileGrids = ((IGPALFileInternal)ConverterSettings.InputFile).FileGrid;

                            if (DataFormat.JSON == fileFormat || DataFormat.YAML == fileFormat || DataFormat.XML == fileFormat || DataFormat.HTML == fileFormat)
                                foreach (Dictionary<object, dynamic> records in ((IGPALFileInternal)ConverterSettings.InputFile).FileDictionary)
                                    AddRecordsUnit(records, fileFormat);
                            // each sheet of a workbook with header lines is a unit, its first row its names. The reader counts
                            // the rows of each sheet of each workbook it read
                            else if (DataFormat.XLSX == fileFormat && true == fileHeaderLine)
                                for (int gridIdx = 0; gridIdx < fileGrids.Count; gridIdx++)
                                {
                                    int sheetStart = 0;
                                    foreach (int sheetRowCount in gridFileSettings.ExcelRowsPerSheet[gridIdx])
                                    {
                                        if (0 < sheetRowCount)
                                            AddGridUnit(fileGrids[gridIdx].Skip(sheetStart).Take(sheetRowCount), true);
                                        sheetStart += sheetRowCount;
                                    }
                                }
                            else
                                foreach (IGPALGrid<string> inGrid in fileGrids)
                                    AddGridUnit(inGrid, true == fileHeaderLine && DataFormat.XLSX != fileFormat);
                        }
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to tokenize input file [{fileIndex + 1}] of [{ConverterSettings.InputFile.Filename}], so its rows are not in the grid. Continuing.", ConverterSettings, GPALObjectType.Other);
                    }
                }
                // one input given as records, such as a json, yaml, xml or html string or a class, gives its names
                else if (1 == ConverterSettings.InDataFormat.Count && 0 < ConverterSettings.InputDictionary.Count && null != ConverterSettings.InputDictionary[0]
                    && (DataFormat.JSON == ConverterSettings.InDataFormat[0] || DataFormat.YAML == ConverterSettings.InDataFormat[0] || DataFormat.XML == ConverterSettings.InDataFormat[0]
                        || DataFormat.HTML == ConverterSettings.InDataFormat[0] || DataFormat.CLASS == ConverterSettings.InDataFormat[0]))
                    AddRecordsUnit(ConverterSettings.InputDictionary[0], ConverterSettings.InDataFormat[0]);
                else
                    foreach (IGPALGrid<string> inGrid in ConverterSettings.InputGrid)
                        AddGridUnit(inGrid, false);

                ConverterSettings.FirstLineIsColumnHeaders = savedFirstLine;
                ConverterSettings.InputDictionary = savedDictionary;

                List<List<string>> namesWritten = new List<List<string>>();
                foreach (KeyValuePair<List<string>, List<List<string>>> unit in units)
                {
                    if (null != unit.Key && false == namesWritten.Any(names => true == names.SequenceEqual(unit.Key)))
                    {
                        ConverterSettings.OutputGrid.AddRow(unit.Key);
                        namesWritten.Add(unit.Key);
                    }
                    foreach (List<string> unitRow in unit.Value)
                        ConverterSettings.OutputGrid.AddRow(unitRow);
                }
            }
            else if (null != ConverterSettings.OutputClassType && false == inputFileMissing)
            {
                bool yamlDirectDone = false;
                // A YAML mapping, such as the values: key a list is written under, holds no items of a list, queue or stack:
                // YamlDotNet reads a mapping into a queue or stack as an empty one, so such a collection is filled from the
                // input's dictionary, as a List is
                bool yamlMappingIntoCollection = true == ConverterHelper.IsEnumerableType(ConverterSettings.OutputClassType)
                    && false == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType)
                    && 0 < ConverterSettings.InputDictionary.Count && null != ConverterSettings.InputDictionary[0]
                    && true == ConverterSettings.InputDictionary[0].Keys.Cast<object>().Any(key => false == (key?.ToString() ?? string.Empty).StartsWith("GPALKEY", StringComparison.Ordinal));
                // A list written to YAML is under its values: key, which is no member of a class: its rows are found in the
                // input's dictionary, so one object takes the first row, as from a YAML list. A dictionary, or a class with a
                // member named values, takes the key as it is
                bool yamlListUnderValues = false == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType)
                    && false == ConverterHelper.HasMember(ConverterSettings.OutputClassType, "values")
                    && 0 < ConverterSettings.InputDictionary.Count && null != ConverterSettings.InputDictionary[0]
                    && 1 == ConverterSettings.InputDictionary[0].Count && true == ConverterSettings.InputDictionary[0].ContainsKey("values")
                    && ConverterSettings.InputDictionary[0]["values"] is IList;
                if (ConverterSettings.InDataFormat.Count > 0 && ConverterSettings.InDataFormat[0] == DataFormat.YAML
                    && 0 < ConverterSettings.InputData.Count && false == yamlMappingIntoCollection && false == yamlListUnderValues)
                {
                    try
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithMaximumRecursion(ConverterHelper.NestingLimit)
                            .WithTypeConverter(new IPAddressYamlConverter())
                            .WithTypeConverter(new ComplexYamlConverter())
                            .WithTypeConverter(new NumberYamlConverter())
                            .WithTypeConverter(new UriYamlConverter())
                            .WithTypeConverter(new TextConverterYamlConverter())
                            .WithTypeConverter(new NullableBigIntegerYamlConverter())
                                .WithTypeConverter(new NullableGuidYamlConverter())
                                .WithTypeConverter(new NullableTimeSpanYamlConverter())
                                .WithTypeConverter(new VersionYamlConverter())
                                .WithTypeConverter(new DateTimeOffsetYamlConverter())
                            .WithTypeConverter(new QuotedStringYamlConverter())
                            .WithTypeConverter(new WaitTimeYamlConverter())
                            .WithTypeConverter(new KeyValuePairYamlConverter())
                            .WithTypeConverter(new MultiDimensionalArrayYamlConverter())
                            .WithNodeDeserializer(new FileHelper.YamlPlainScalarDeserializer(true))
                            .WithNodeDeserializer(new FileHelper.YamlTextKeyMappingDeserializer())
                            .EnablePrivateConstructors()
                            .IgnoreUnmatchedProperties()
                            // a key matches its property whatever its case, so Name: fills Name as name: does
                            .WithCaseInsensitivePropertyMatching()
                            .Build();

                        // The YAML read as plain mappings and lists, an alias the same mapping as its anchor
                        object yamlGraph = new DeserializerBuilder()
                            .WithMaximumRecursion(ConverterHelper.NestingLimit)
                            .WithNodeDeserializer(new FileHelper.YamlPlainScalarDeserializer(false))
                            .WithNodeDeserializer(new FileHelper.YamlTextKeyMappingDeserializer())
                            .Build()
                            .Deserialize<object>(ConverterSettings.InputData[0]);
                        // A record of a list, array or dictionary of a class that names none of its members but holds lists of
                        // its records gives those records, as from json, so such YAML is read as json is, below
                        Type yamlRowType = true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType)
                            ? ConverterHelper.DictionaryTypeArguments(ConverterSettings.OutputClassType)[1]
                            : true == ConverterHelper.IsEnumerableType(ConverterSettings.OutputClassType) ? ConverterHelper.CollectionItemType(ConverterSettings.OutputClassType) : null;
                        IEnumerable yamlRows = true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType) ? (yamlGraph as IDictionary)?.Values : yamlGraph as IList;
                        bool rowsHoldRecords = null != yamlRowType && null != yamlRows
                            && true == yamlRows.Cast<object>().Any(row => row is IDictionary record && 0 < record.Count
                                && false == record.Keys.Cast<object>().Any(recordKey => true == ConverterHelper.HasMember(yamlRowType, recordKey?.ToString() ?? string.Empty))
                                && true == record.Values.Cast<object>().Any(value => value is IList heldRows
                                    && true == heldRows.Cast<object>().Any(heldRow => heldRow is IDictionary heldRecord
                                        && true == heldRecord.Keys.Cast<object>().Any(heldKey => true == ConverterHelper.HasMember(yamlRowType, heldKey?.ToString() ?? string.Empty)))));

                        // One key that is no member of a class and holds its record or records, such as the root: of xml
                        // written to yaml, is a wrapper, read below as json's is; YamlDotNet would pass over the key and
                        // leave the class empty
                        bool wrappedForClass = null == yamlRowType && yamlGraph is IDictionary yamlTop && 1 == yamlTop.Count
                            && false == ConverterHelper.HasMember(ConverterSettings.OutputClassType, yamlTop.Keys.Cast<object>().First()?.ToString() ?? string.Empty)
                            // a member YAML names by another name, [YamlMember(Alias = ...)], is a member too
                            // whatever its case, as the reader matches names
                            && false == ConverterSettings.OutputClassType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                                .Where(member => member is PropertyInfo || member is FieldInfo)
                                .Any(member => member.GetCustomAttributes(typeof(YamlMemberAttribute), true).Cast<YamlMemberAttribute>()
                                    .Any(yamlMember => true == string.Equals(yamlMember.Alias, yamlTop.Keys.Cast<object>().First()?.ToString(), StringComparison.OrdinalIgnoreCase)))
                            && (yamlTop.Values.Cast<object>().First() is IDictionary || yamlTop.Values.Cast<object>().First() is IList);
                        // so is one key of a dictionary of records that holds the entries, records of the value type, rather
                        // than being one of them
                        wrappedForClass = true == wrappedForClass
                            || (true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType) && null != yamlRowType
                                && yamlGraph is IDictionary yamlEntries && 1 == yamlEntries.Count
                                && false == ConverterHelper.HasMember(yamlRowType, yamlEntries.Keys.Cast<object>().First()?.ToString() ?? string.Empty)
                                && yamlEntries.Values.Cast<object>().First() is IDictionary heldEntries && 0 < heldEntries.Count
                                && true == heldEntries.Values.Cast<object>().All(heldEntry => heldEntry is IDictionary heldRecord
                                    && true == heldRecord.Keys.Cast<object>().Any(memberKey => true == ConverterHelper.HasMember(yamlRowType, memberKey?.ToString() ?? string.Empty))));

                        if (false == rowsHoldRecords && false == wrappedForClass)
                            ConverterSettings.OutputClass = deserializer.Deserialize(ConverterSettings.InputData[0], ConverterSettings.OutputClassType);
                        // an empty document reads as null and goes on below, as an empty JSON file does
                        yamlDirectDone = false == rowsHoldRecords && false == wrappedForClass && null != (object)ConverterSettings.OutputClass;

                        // The YAML's mappings and lists are gone through with what was read. A record of a list, array or
                        // dictionary of a class that fills none of its members, such as Foo: 1 for an Item, is taken out,
                        // with the warning a json record gets. A member left null whose YAML is an alias of a mapping read
                        // into an object, such as the Back of a struct that names the object holding it, is that object:
                        // YamlDotNet sets such a back reference on a copy of the struct
                        if (true == yamlDirectDone)
                        {
                            // each mapping read into an object, and each object gone through once
                            Dictionary<object, object> objectOfMapping = new Dictionary<object, object>(ConverterHelper.ReferenceEqualityComparer.Instance);
                            HashSet<object> objectsDone = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);
                            List<string> givenNames = ConverterSettings.ColumnNames?[0];

                            // what was read with its records that fill nothing taken out and its back references set; an
                            // array is made new without them, a struct is given back changed
                            object TakeOutUnfilled(object read, object yaml)
                            {
                                Type readType = read?.GetType();
                                if (null == read || (false == readType.IsValueType && false == objectsDone.Add(read)))
                                    readType = null;
                                else if (yaml is IDictionary && false == readType.IsValueType && false == read is IEnumerable)
                                    objectOfMapping[yaml] = read;

                                if (null == readType || read is string || true == ConverterHelper.IsValueAlone(readType))
                                    readType = null;
                                else if (read is IDictionary readEntries && yaml is IDictionary yamlEntries)
                                {
                                    Type entryType = ConverterHelper.DictionaryTypeArguments(readType)[1];
                                    foreach (object entryKey in readEntries.Keys.Cast<object>().ToList())
                                    {
                                        object yamlEntry = yamlEntries.Keys.Cast<object>().Where(yamlKey => yamlKey?.ToString() == entryKey?.ToString()).Select(yamlKey => yamlEntries[yamlKey]).FirstOrDefault();
                                        if (true == ConverterHelper.FillsNothing(yamlEntry, entryType, givenNames, yamlGraph))
                                            readEntries.Remove(entryKey);
                                        else
                                            readEntries[entryKey] = TakeOutUnfilled(readEntries[entryKey], yamlEntry);
                                    }
                                }
                                else if (read is IList readItems && yaml is IList yamlItems)
                                {
                                    Type itemType = ConverterHelper.CollectionItemType(readType);
                                    List<object> keptItems = new List<object>();
                                    for (int itemIdx = 0; itemIdx < readItems.Count; itemIdx++)
                                    {
                                        object yamlItem = itemIdx < yamlItems.Count ? yamlItems[itemIdx] : null;
                                        if (false == ConverterHelper.FillsNothing(yamlItem, itemType, givenNames, yamlGraph))
                                            keptItems.Add(TakeOutUnfilled(readItems[itemIdx], yamlItem));
                                    }
                                    if (read is Array readArray)
                                    {
                                        Array keptArray = Array.CreateInstance(readType.GetElementType(), keptItems.Count);
                                        for (int keptIdx = 0; keptIdx < keptItems.Count; keptIdx++)
                                            keptArray.SetValue(keptItems[keptIdx], keptIdx);
                                        read = keptArray;
                                    }
                                    else
                                    {
                                        readItems.Clear();
                                        foreach (object keptItem in keptItems)
                                            readItems.Add(keptItem);
                                    }
                                }
                                else if (false == read is IEnumerable && yaml is IDictionary yamlMembers)
                                {
                                    foreach (MemberInfo member in readType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(property => true == property.CanRead && true == property.CanWrite && 0 == property.GetIndexParameters().Length)
                                        .Cast<MemberInfo>().Concat(readType.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(field => false == field.IsInitOnly)))
                                    {
                                        Type memberType = member is PropertyInfo memberProperty ? memberProperty.PropertyType : ((FieldInfo)member).FieldType;
                                        object memberValue = member is PropertyInfo readProperty ? readProperty.GetValue(read) : ((FieldInfo)member).GetValue(read);
                                        object yamlValue = yamlMembers.Keys.Cast<object>().Where(yamlKey => true == string.Equals(yamlKey?.ToString(), member.Name, StringComparison.OrdinalIgnoreCase))
                                            .Select(yamlKey => yamlMembers[yamlKey]).FirstOrDefault();
                                        object newValue = null == memberValue && null != yamlValue && true == objectOfMapping.ContainsKey(yamlValue) && true == memberType.IsInstanceOfType(objectOfMapping[yamlValue])
                                            ? objectOfMapping[yamlValue]
                                            : TakeOutUnfilled(memberValue, yamlValue);
                                        // a struct member is set back, as it was changed on a copy
                                        if (false == ReferenceEquals(newValue, memberValue) || (null != memberValue && true == memberValue.GetType().IsValueType && false == ConverterHelper.IsValueAlone(memberValue.GetType())))
                                        {
                                            if (member is PropertyInfo writeProperty)
                                                writeProperty.SetValue(read, newValue);
                                            else
                                                ((FieldInfo)member).SetValue(read, newValue);
                                        }
                                    }
                                }

                                return read;
                            }

                            ConverterSettings.OutputClass = TakeOutUnfilled(ConverterSettings.OutputClass, yamlGraph);
                        }
                    }
                    catch { }
                }

                if (!yamlDirectDone)
                {
                // XML input arrives with GPAL numeric key prefixes and all values wrapped in
                // List<Dict> + #text nodes. Strip/flatten the whole structure once before
                // any POCO reconstruction so CreateObjectFromDictionary sees clean keys and scalars.
                // An empty json or yaml document is held as a null under the reader's root. The class gets nothing from
                // it, as from an empty csv file, so that input is an empty dictionary here.
                List<Dictionary<object, dynamic>> classDictionaries = ConverterSettings.InputDictionary
                    .Select((dict, dictIdx) => null != dict && null == WithoutReaderRoot(dict, dictIdx) ? new Dictionary<object, dynamic>() : dict)
                    .ToList();
                object classInput = classDictionaries;
                if (ConverterSettings.InDataFormat.Count > 0 && ConverterSettings.InDataFormat[0] == DataFormat.XML)
                    classInput = ConverterHelper.StripGpalKeys(ConverterSettings.InputDictionary);

                // an array of two or more dimensions, such as int[,], is read as rows of rows, an int[][], and laid into the array
                if (true == ConverterSettings.OutputClassType.IsArray && 1 < ConverterSettings.OutputClassType.GetArrayRank() && 0 < ConverterSettings.InputDictionary.Count)
                {
                    object rows = ConverterHelper.ConvertToClass(classInput, ConverterHelper.RowsTypeOf(ConverterSettings.OutputClassType), classInput, ConverterSettings.ColumnNames?[0]);
                    ConverterSettings.OutputClass = null == rows ? null : ConverterHelper.ArrayFromRows(rows, ConverterSettings.OutputClassType, null);
                }
                else if (true == ConverterSettings.OutputClassType.IsGenericType && true == ConverterHelper.IsDictionaryType(ConverterSettings.OutputClassType) && 0 < ConverterSettings.InputDictionary.Count)
                {
                    ConverterSettings.OutputClass = ConverterHelper.ConvertToClass(classInput, ConverterSettings.OutputClassType, classInput, ConverterSettings.ColumnNames?[0]);  // NOTE: CAVEAT: hardcoded value
                }
                else if (true == ConverterSettings.OutputClassType.IsGenericType && null != ConverterSettings.OutputClassType.GetInterface("IEnumerable"))
                {
                    // Rows of values, such as a List<List<string>>, given rows that are each a list of values alone, as a
                    // headerless csv or a json list of lists gives, take them as they are
                    bool rowsAreValues = true;
                    if (typeof(List<string>) == ConverterSettings.OutputClassElementType)
                        for (int rowsIdx = 0; rowsIdx < classDictionaries.Count; rowsIdx++)
                        {
                            // a YAML file holds its rows under its values: key, as a list is written to it
                            bool yamlInput = rowsIdx < ConverterSettings.InDataFormat.Count && DataFormat.YAML == ConverterSettings.InDataFormat[rowsIdx];
                            Dictionary<object, dynamic> rowsDict = (true == yamlInput ? WithoutReaderRoot(classDictionaries[rowsIdx], rowsIdx) as Dictionary<object, dynamic> : null)
                                ?? classDictionaries[rowsIdx];
                            foreach (KeyValuePair<object, dynamic> rowEntry in rowsDict)
                            {
                                object row = rowEntry.Value;
                                IEnumerable rowValues = row is IDictionary rowFields ? (IEnumerable)rowFields.Values : row as IList;
                                rowsAreValues = true == rowsAreValues
                                    && true == Regex.IsMatch(rowEntry.Key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")
                                    && (false == row is IDictionary || true == ((IDictionary)row).Keys.Cast<object>().All(key => Regex.IsMatch(key?.ToString() ?? string.Empty, @"^(GPALKEY)?\d+$")))
                                    && (null == rowValues || true == rowValues.Cast<object>().All(value => false == value is IDictionary && false == value is IList));
                            }
                        }

                    // since we can have a dynamic type. we can't easily defined <T> inline, so we have to generate the method signature dynamically
                    if (ConverterSettings.OutputClassElementType == typeof(string))
                    {
                        var resultList = new List<string>();
                        foreach (var dict in classDictionaries)
                        {
                            resultList.AddRange(BuildHierarchicalStrings(dict));
                        }
                        ConverterSettings.OutputClass = resultList;
                    }
                    // Any other input given to rows of values, such as records or one object, gives the rows it writes to csv
                    // with no header line: one object one row, with the values of its nested records and lists, as in the grid
                    else if (typeof(List<string>) == ConverterSettings.OutputClassElementType && false == rowsAreValues)
                    {
                        List<List<string>> valueRows = new List<List<string>>();
                        List<Dictionary<object, dynamic>> savedRowsDictionary = ConverterSettings.InputDictionary;
                        bool savedRowsFirstLine = ConverterSettings.FirstLineIsColumnHeaders;

                        ConverterSettings.FirstLineIsColumnHeaders = false;
                        for (int rowsIdx = 0; rowsIdx < classDictionaries.Count; rowsIdx++)
                        {
                            ConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { classDictionaries[rowsIdx] };
                            ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(ConverterSettings, out IGPALGrid<string> rowsGrid,
                                rowsIdx < ConverterSettings.InDataFormat.Count ? ConverterSettings.InDataFormat[rowsIdx] : DataFormat.NOTSET);
                            valueRows.AddRange(rowsGrid.Select(gridRow => new List<string>(gridRow)));
                        }
                        ConverterSettings.FirstLineIsColumnHeaders = savedRowsFirstLine;
                        ConverterSettings.InputDictionary = savedRowsDictionary;
                        ConverterSettings.OutputClass = valueRows;
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

                            // json's, yaml's and xml's rows are found as they are for an array of arrays, so a List<int[]>
                            // and an int[][] take the same items; so are the rows of rows of arrays of two or more dimensions,
                            // as of a List<int[,]>, from any input
                            if ((0 < ConverterSettings.InDataFormat.Count
                                && (DataFormat.XML == ConverterSettings.InDataFormat[0] || DataFormat.YAML == ConverterSettings.InDataFormat[0]
                                    || DataFormat.JSON == ConverterSettings.InDataFormat[0]))
                                || 1 < elementType.GetArrayRank())
                                ConverterSettings.OutputClass = ConverterHelper.ConvertToClass(classInput, ConverterSettings.OutputClassType, classInput, ConverterSettings.ColumnNames?[0]);
                            else
                            {
                                var mi = typeof(ConverterHelper).GetMethod("CreateIEnumerableFromClass",
                                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static,
                                    null,
                                    new[] { typeof(ConverterSettings) },
                                    null);

                                var genericMethod = mi.MakeGenericMethod(new[] { elementType });
                                ConverterSettings.OutputClass = (T)genericMethod.Invoke(null, new object[] { ConverterSettings });
                            }
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to create IEnumerable for [{ConverterSettings.OutputClassType}]", null, GPALObjectType.Other, ex);
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
                            // classInput is the input as read, with xml's key prefixes and text nodes taken out
                            ConverterSettings.OutputClass = mi.Invoke(null, new object[] { classInput, ConverterSettings.OutputClassType, classInput, ConverterSettings.ColumnNames?[0] }); // NOTE: CAVEAT hardcoded value
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to create IEnumerable for [{ConverterSettings.OutputClassType}]", null, GPALObjectType.Other, ex);
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
                                Type elementType = outputClass.GetType().IsGenericType ? ConverterHelper.CollectionItemType(outputClass.GetType()) : null;
                                if (elementType != null)
                                {
                                    // each item of the converted collection, so a List<object>, which any value fits, takes
                                    // the items and not the collection as its one item
                                    foreach (var compatibleItem in ((IEnumerable)ConverterSettings.OutputClass).Cast<object>().SelectMany(convertedItem => ConvertToCompatibleItems(convertedItem, elementType)))
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
                        // the values as a List<string> takes them, in an array
                        var resultList = new List<string>();
                        foreach (var dict in classDictionaries)
                        {
                            resultList.AddRange(BuildHierarchicalStrings(dict));
                        }
                        ConverterSettings.OutputClass = resultList.ToArray();
                    }
                    else if (0 < ConverterSettings.InDataFormat.Count
                        && (DataFormat.XML == ConverterSettings.InDataFormat[0] || DataFormat.YAML == ConverterSettings.InDataFormat[0]
                            || DataFormat.JSON == ConverterSettings.InDataFormat[0]))
                    {
                        // xml's, yaml's and json's rows are found as a list's are, the list an object holds under one key,
                        // such as values:, among them, and the list is copied to the array
                        IList listItems = ConverterHelper.ConvertToClass(classInput, typeof(List<>).MakeGenericType(ConverterSettings.OutputClassElementType), classInput, ConverterSettings.ColumnNames?[0]) as IList;
                        Array itemArray = Array.CreateInstance(ConverterSettings.OutputClassElementType, listItems?.Count ?? 0);

                        listItems?.CopyTo(itemArray, 0);
                        ConverterSettings.OutputClass = itemArray;
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
                    {
                        // One object from a file of rows is filled from the first row, with a warning, and the rows after
                        // it are not read. An object, which takes the input as it is, and a collection take every row.
                        object singleInput = classInput;
                        bool holdsOne = typeof(object) != ConverterSettings.OutputClassType && false == ConverterHelper.IsEnumerableType(ConverterSettings.OutputClassType);

                        if (true == holdsOne && classInput is IList inputList && 1 == inputList.Count && inputList[0] is IDictionary rows && 1 < rows.Count
                            && rows.Keys.Cast<object>().All(key => key is string rowKey && rowKey.StartsWith("GPALKEY", StringComparison.Ordinal))
                            && rows.Values.Cast<object>().All(row => row is IDictionary))
                        {
                            singleInput = rows[rows.Keys.Cast<string>().OrderBy(rowKey => rowKey, StringComparer.Ordinal).First()];
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ConverterSettings.OutputClassType.Name}] holds one object, so it is filled from the first of [{rows.Count}] rows; save to a list to take them all", this, GPALObjectType.Converter);
                        }
                        // xml's rows are elements of one name repeated under its root, a name the class has no member for
                        else if (true == holdsOne && ConverterSettings.InDataFormat.Count > 0 && ConverterSettings.InDataFormat[0] == DataFormat.XML
                            && classInput is IList xmlList && 1 == xmlList.Count && xmlList[0] is IDictionary xmlRoot && 1 == xmlRoot.Count
                            && xmlRoot.Values.Cast<object>().First() is IDictionary xmlHolder && 1 == xmlHolder.Count
                            && xmlHolder.Values.Cast<object>().First() is IList xmlRows && 0 < xmlRows.Count && xmlRows.Cast<object>().All(row => row is IDictionary)
                            && false == ConverterHelper.HasMember(ConverterSettings.OutputClassType, xmlHolder.Keys.Cast<object>().First().ToString())
                            // the rows are of this class: a key of the first is one of its members; extra keys are passed over, as in any fill
                            && ((IDictionary)xmlRows[0]).Keys.Cast<object>().Any(rowKey => ConverterHelper.HasMember(ConverterSettings.OutputClassType, rowKey?.ToString() ?? string.Empty)))
                        {
                            singleInput = xmlRows[0];

                            if (1 < xmlRows.Count)
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ConverterSettings.OutputClassType.Name}] holds one object, so it is filled from the first of [{xmlRows.Count}] rows; save to a list to take them all", this, GPALObjectType.Converter);
                        }

                        singleObjectInput = singleInput;

                        ConverterSettings.OutputClass = ConverterHelper.ConvertToClass(singleInput, ConverterSettings.OutputClassType, singleInput, ConverterSettings.ColumnNames?[0]);  // NOTE: CAVEAT: hardcoded value
                    }
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
                    if ((object)outputClass != (object)ConverterSettings.OutputClass)
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
    }
}
