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

