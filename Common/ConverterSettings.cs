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
using System.Xml;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public class ConverterSettings
    {
        public ConverterSettings()
        {
            InputDictionary = new List<Dictionary<object, dynamic>>();
            InDataFormat = new List<DataFormat>();
            InputGrid = new List<IGPALGrid<string>>();
            InputData = new List<string>();
            ColumnNames = new GPALGrid<string>();
            ExcelSheetNames = new GPALGrid<string>();
            ExcelRowsPerSheet = new GPALGrid<int>();
            RegisteredStructs = new HashSet<Type>();
            RegisteredStructs.Add(typeof(WaitTime));
        }

        internal HashSet<Type> RegisteredStructs { get; set; }

        internal XmlDocument InputXmlDocument { get; set; }
        internal XmlDocument OutputXmlDocument { get; set; }
        internal IGPALDatabase InputDatabase { get; set; }
        internal IGPALDatabase OutputDatabase { get; set; }
        internal List<IGPALGrid<string>> InputGrid { get; set; }
        internal IGPALGrid<string> OutputGrid { get; set; }
        internal IGPALGrid<string> ColumnNames { get; set; }
        internal IGPALGrid<string> ExcelSheetNames { get; set; }   // a list for each filename supplied, or one for all. If not supplied, generate column1, column2...
        internal IGPALGrid<int> ExcelRowsPerSheet { get; set; }   // a list for each filename supplied, or one for all. If not supplied, generate column1, column2...
        internal List<Dictionary<object, dynamic>> InputDictionary { get; set; }
        internal Dictionary<string, string> OutputDictionary { get; set; }
        internal List<string> InputData { get; set; }
        internal string OutputData { get; set; }
        internal GPALFile InputFile { get; set; }
        internal GPALFile OutputFile { get; set; }
        internal dynamic InputClass { get; set; }
        internal Type InputClassType { get; set; }
        internal Type InputClassElementType { get; set; }
        internal dynamic OutputClass { get; set; }
        internal Type OutputClassType { get; set; }
        internal Type OutputClassElementType { get; set; }
        internal List<DataFormat> InDataFormat { get; set; }
        internal DataFormat OutDataFormat { get; set; } = DataFormat.NOTSET;
        internal bool IgnoreFirstLineColumnHeaders { get; set; }  // overrides each individual file settings or used with non-file types
        internal bool FirstLineIsColumnHeaders { get; set; }  // overrides each individual file settings or used with non-file types; reset per-output-file during Convert()
        internal bool InputFirstLineIsColumnHeaders { get; set; }  // set via WithFirstLineHasColumnNames(); preserved across all SaveTo/AppendTo calls in a chain
        internal bool FieldsEnclosedInQuotes { get; set; }  // overrides each individual file settings or used with non-file types
        internal char? InDelimiter { get; set; }  // overrides each individual file settings or used with non-file types
        internal char? OutDelimiter { get; set; }  // overrides each individual file settings or used with non-file types

        public void ClearDerived()
        {
            InputData = new List<string>();
            InputDictionary = new List<Dictionary<object, dynamic>>();
            InputGrid = new List<IGPALGrid<string>>();

            OutputClass = null;
            OutputClassElementType = null;
            OutputClassType = null;
            OutputData = string.Empty;
            OutputDictionary = new Dictionary<string, string>();
            OutputGrid = GPAL.Grid.ToGPALObject();
        }
    }
}

