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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GenerallyPositive
{
    internal class GPALFileSettings
    {
        internal bool AlreadyTokenized { get; set; } = false;

        public List<string> Filenames { get; internal set; }
        public List<string> ReturnFilenames { get; internal set; }
        public string SourceUrl { get; internal set; }  // set when WithFileName was given a url, so the file knows what it is a copy of
        internal Dictionary<string, string> ExtractedFrom { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // a file taken out of a zip or gz, and the archive it came from, so SaveTo with no folder saves beside the archive
        internal Browser.IBrowser Browser { get; set; } // supplied by WithBrowser, used to reach a url our own connection cannot
        public int NextFileCount { get; internal set; } = 0;
        public IGPALGrid<string> ColumnList { get; internal set; }   // a list for each filename supplied, or one for all. If not supplied, generate column1, column2...
        public IGPALGrid<string> ExcelSheetNames { get; internal set; }   // a list for each filename supplied, or one for all. If not supplied, generate column1, column2...
        public IGPALGrid<int> ExcelRowsPerSheet { get; internal set; }   // a list for each filename supplied, or one for all. If not supplied, generate column1, column2...
        public int FileCount { get; internal set; } // count of user supplied filenames
        public List<char?> Delimiter { get; internal set; } = null;    // default to csv
        public List<IGPALGrid<string>> TokenList { get; internal set; }   // all the parsed out values from each file, in their own grids
        public List<bool> FieldsEnclosedInQuotes { get; internal set; }
        public List<bool> FirstLineIsColumnNames { get; internal set; }
        public List<bool> IgnoreFirstLineColumnNames { get; internal set; }
        public List<bool> OverwriteFile { get; internal set; }  // overwrite the file or use Next to save to a new file with a new filename
        public Enums.FileSortOrder FileSortOrder { get; internal set; } = Enums.FileSortOrder.NameAscending;  // order in which wildcard-matched files are added to Filenames
        public Enums.NextFilePattern NextFilePattern { get; internal set; } = Enums.NextFilePattern.CounterPadded;  // how Next builds a new unique filename
        public int NextPatternCounter { get; internal set; } = 0;  // running counter for the Counter/CounterPadded patterns
        public bool UseOneHeaderForAllFiles { get; internal set; }
        internal List<bool> FirstLineGiven { get; set; } = new List<bool>();   // per file, true when FirstLineIsColumnNames was given for it, so a file without one takes the converter's

        // A unit is a run of file names and the settings given after them, up to the next file name. Each setting covers
        // the names of its own unit in order, the last one given covers the rest of them, and a name's setting goes to
        // every file it added, all the files a wildcard matched. A unit that gives no setting of a kind leaves its files
        // without one. The per-file lists above are rebuilt from the units whenever a name or a setting is added.
        internal List<int> FilesPerName { get; set; } = new List<int>();   // how many files each file name added, in order
        internal List<int> UnitOfName { get; set; } = new List<int>();     // the unit each file name is in
        internal List<int> PlaceOfName { get; set; } = new List<int>();    // each file name's place in its unit, 0 for the first
        internal bool SettingSinceLastName { get; set; }                   // a setting was given after the last file name, so the next name starts a unit
        internal List<Dictionary<string, List<object>>> Units { get; set; } = new List<Dictionary<string, List<object>>>();   // the settings each unit gave, by kind, in order

        /// <summary>
        /// Adds a file name that added the given number of files. A name after settings starts a new unit; a name after
        /// another name, or after settings given before any name, joins the unit.
        /// </summary>
        /// <param name="files">The files the name added; a wildcard adds one per match</param>
        internal void AddName(int files)
        {
            if (0 == Units.Count || (0 < UnitOfName.Count && true == SettingSinceLastName))
                Units.Add(new Dictionary<string, List<object>>());

            SettingSinceLastName = false;

            PlaceOfName.Add(0 < UnitOfName.Count && Units.Count - 1 == UnitOfName[UnitOfName.Count - 1] ? PlaceOfName[PlaceOfName.Count - 1] + 1 : 0);
            UnitOfName.Add(Units.Count - 1);
            FilesPerName.Add(files);
            ApplyUnits();
        }
        /// <summary>
        /// The settings of a kind the current unit has given, in order, made empty for the first one.
        /// </summary>
        /// <param name="kind">The kind of setting, the name of the per-file list it fills</param>
        internal List<object> GivenInCurrentUnit(string kind)
        {
            if (0 == Units.Count)
                Units.Add(new Dictionary<string, List<object>>());

            Dictionary<string, List<object>> unit = Units[Units.Count - 1];

            if (false == unit.ContainsKey(kind))
                unit[kind] = new List<object>();

            SettingSinceLastName = true;
            return unit[kind];
        }
        /// <summary>
        /// The number of file names in the current unit.
        /// </summary>
        internal int NamesInCurrentUnit()
        {
            return UnitOfName.Count(unit => unit == Units.Count - 1);
        }
        /// <summary>
        /// Gives the current unit one more setting of a kind and rebuilds the per-file lists.
        /// </summary>
        /// <param name="kind">The kind of setting, the name of the per-file list it fills</param>
        /// <param name="value">The setting</param>
        internal void Give(string kind, object value)
        {
            GivenInCurrentUnit(kind).Add(value);
            ApplyUnits();
        }
        /// <summary>
        /// The setting of a kind for a file name: the one in its place in its unit, or the last one its unit gave.
        /// </summary>
        /// <param name="kind">The kind of setting</param>
        /// <param name="name">The file name's index</param>
        /// <returns>The setting, or null when the unit gave none</returns>
        private object SettingFor(string kind, int name)
        {
            object setting = null;

            if (true == Units[UnitOfName[name]].TryGetValue(kind, out List<object> given) && 0 < given.Count)
                setting = given[Math.Min(PlaceOfName[name], given.Count - 1)];

            return setting;
        }
        /// <summary>
        /// Rebuilds a per-file list of a kind from the units: each file gets its name's setting, or the unset value when
        /// its unit gave none. A list of a kind no unit gave stays empty, as when nothing was given.
        /// </summary>
        private void Fill<T>(List<T> list, string kind, T unset)
        {
            list.Clear();

            if (true == Units.Any(unit => unit.ContainsKey(kind)))
                for (int name = 0; name < FilesPerName.Count; name++)
                {
                    object setting = SettingFor(kind, name);
                    list.AddRange(Enumerable.Repeat(null == setting ? unset : (T)setting, FilesPerName[name]));
                }
        }
        /// <summary>
        /// Rebuilds every per-file list from the units. Column names given for a file mark its first line as column names
        /// when its unit did not say.
        /// </summary>
        internal void ApplyUnits()
        {
            Fill(Delimiter, nameof(Delimiter), null);
            Fill(FieldsEnclosedInQuotes, nameof(FieldsEnclosedInQuotes), false);
            Fill(IgnoreFirstLineColumnNames, nameof(IgnoreFirstLineColumnNames), false);
            Fill(OverwriteFile, nameof(OverwriteFile), false);

            FirstLineIsColumnNames.Clear();
            FirstLineGiven.Clear();
            ColumnList.Clear();

            bool anyFirstLine = Units.Any(unit => unit.ContainsKey(nameof(FirstLineIsColumnNames)) || unit.ContainsKey(nameof(ColumnList)));
            bool anyColumns = Units.Any(unit => unit.ContainsKey(nameof(ColumnList)));

            for (int name = 0; name < FilesPerName.Count; name++)
            {
                object firstLine = SettingFor(nameof(FirstLineIsColumnNames), name);
                List<string> columns = (List<string>)SettingFor(nameof(ColumnList), name);

                for (int i = 0; i < FilesPerName[name]; i++)
                {
                    if (true == anyFirstLine)
                    {
                        FirstLineIsColumnNames.Add(null != firstLine ? (bool)firstLine : null != columns);
                        FirstLineGiven.Add(null != firstLine || null != columns);
                    }

                    if (true == anyColumns)
                        ColumnList.AddRow(null != columns ? new List<string>(columns) : new List<string>());
                }
            }
        }
        public List<Dictionary<object, dynamic>> FileDictionary { get; internal set; }
        public List<string> FileData { get; internal set; }
        public List<IGPALGrid<string>> FileGrid { get; internal set; }

        public GPALFileSettings()
        {
            TokenList = new List<IGPALGrid<string>>();
            Filenames = new List<string>();
            ReturnFilenames = new List<string>();
            ColumnList = new GPALGrid<string>();
            Delimiter = new List<char?>();
            FieldsEnclosedInQuotes = new List<bool>();
            FirstLineIsColumnNames = new List<bool>();
            IgnoreFirstLineColumnNames = new List<bool>();
            FileDictionary = new List<Dictionary<object, dynamic>>();
            FileGrid = new List<IGPALGrid<string>>();
            FileData = new List<string>();
            ExcelRowsPerSheet = new GPALGrid<int>();
            ExcelSheetNames = new GPALGrid<string>();
            OverwriteFile = new List<bool>();
        }
    }
}

