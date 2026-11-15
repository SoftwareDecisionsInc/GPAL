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
    public class GoogleSheetsConfig
    {
        public string SheetsApiBase { get; set; } = "https://sheets.googleapis.com";
        public string ScriptsApiBase { get; set; } = "https://script.googleapis.com";
        public string SpreadsheetEndPoint { get; set; } = "/v4/spreadsheets/";
        public string ReadRangeEndpoint { get; set; } = "/v4/spreadsheets/{0}/values/{1}!{2}";
        public string WriteRangeEndpoint { get; set; } = "/v4/spreadsheets/{0}/values/{1}!{2}";
        public string AppendRangeEndpoint { get; set; } = "/v4/spreadsheets/{0}/values/{1}!{2}:append";
        public string FormatRangeEndpoint { get; set; } = "/v4/spreadsheets/{0}:batchUpdate";
        public string ScriptContentEndpoint { get; set; } = "/v1/projects/{0}/content";
        public string TriggerEndpoint { get; set; } = "/v1/projects/{0}/triggers";
        public string AuthorizationHeader { get; set; } = "Authorization";
        public string ValueInputOption { get; set; } = "?valueInputOption=RAW";
        public string ScriptFileType { get; set; } = "SERVER_JS";
        public string TitleField { get; set; } = "title";
        public string HiddenField { get; set; } = "hidden";
        public string Bearer { get; set; } = "Bearer";
        public string UserEnteredFormatField { get; set; } = "userEnteredFormat";
        public bool UseAppsScript { get; set; } = true; // Default to Apps Script mode
        public string WebAppUrl { get; set; } // Store the deployed web app URL
    }
}
