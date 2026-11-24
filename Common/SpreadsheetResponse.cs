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
using System.Reflection;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Wordprocessing;
using GenerallyPositive;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    internal class SpreadsheetResponse
    {
        public string SpreadsheetId { get; set; }
        public List<Sheet> Sheets { get; set; }

        public class Sheet
        {
            public SheetProperties Properties { get; set; }
        }

        public class SheetProperties
        {
            public string Title { get; set; }
            public int SheetId { get; set; }
        }
    }
}
