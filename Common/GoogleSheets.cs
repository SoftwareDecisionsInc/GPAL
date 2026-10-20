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

    public class GoogleSheets : IGoogleSheets
    {
        internal string SpreadsheetId { get; private set; }
        internal string SheetName { get; private set; }
        internal string ReadRange { get; private set; }
        internal string WriteRange { get; private set; }
        internal IGPALGrid<string> ReadData { get; private set; }
        internal FormatType? OurFormatType { get; private set; }
        internal NumberFormatType? NumberFormatType { get; set; }
        //// post-v1: a custom number format pattern
        //internal string NumberFormatPattern { get; set; }
        internal object FormatValue { get; private set; }
        internal int? InsertPosition { get; private set; }
        internal string InsertSeparator { get; private set; }
        internal TriggerScheduleType TriggerSchedule { get; private set; }
        internal string AppsScriptProjectId { get; private set; }
        internal string ScriptName { get; private set; }
        internal string FunctionName { get; private set; }
        internal ScriptAccessType ScriptAccess { get; private set; } = ScriptAccessType.ME; // Restrict access by default
        internal ExecuteAsType ExecuteAs { get; private set; } = ExecuteAsType.USER_DEPLOYING;
        internal string DeploymentDescription { get; private set; } = "Web app deployment for script";
        internal int VersionNumber { get; private set; } = 1;
        internal GoogleSheetsConfig googleSheetsConfig { get; private set; }
        internal IRESTClient SheetsRESTClient { get; private set; }
        internal IRESTClient ScriptsRESTClient { get; private set; }
        internal ICredentials Credentials { get; private set; }
        internal GoogleCloud _googleCloud { get; private set; }

        internal GoogleSheets()
        {
            googleSheetsConfig = new GoogleSheetsConfig();
            SheetsRESTClient = GPAL.RESTClient.WithAPIBase(googleSheetsConfig.SheetsApiBase).ToGPALObject();
            ScriptsRESTClient = GPAL.RESTClient.WithAPIBase(googleSheetsConfig.ScriptsApiBase).ToGPALObject();
            _googleCloud = null;
        }

        private void InitializeMode()
        {
            bool hasAccessToken = !string.IsNullOrEmpty(((Credentials)Credentials)?.AccessToken);

            if (Credentials != null)
            {
                if (false == hasAccessToken)
                {
                    Credentials.FetchAccessToken(out string accessToken);
                }

                if (false == string.IsNullOrEmpty(((Credentials)Credentials)?.AccessToken))
                {
                    var testResponse = GPAL.RESTClient.WithAPIBase("https://oauth2.googleapis.com")
                        .WithEndpoint("/tokeninfo")
                        .WithParameters(new { access_token = ((Credentials)Credentials)?.AccessToken })
                        .Execute();
                    if (testResponse != null && !testResponse.Contains("error"))
                    {
                        googleSheetsConfig.UseAppsScript = false; // Switch to API mode
                        return;
                    }
                }
            }
            googleSheetsConfig.UseAppsScript = true; // Fallback to Apps Script mode
        }

        private string ResolveSheetName(object sheetNameOrId)
        {
            if (sheetNameOrId == null)
            {
                return SheetName ?? "Sheet1";
            }

            if (sheetNameOrId is string name)
            {
                return string.IsNullOrEmpty(name) ? SheetName ?? "Sheet1" : name;
            }
            else if (sheetNameOrId is int id)
            {
                var spreadsheetResponseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{SpreadsheetId}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (spreadsheetResponseJson != null)
                {
                    try
                    {
                        var spreadsheetResponse = JsonSerializer.Deserialize<SpreadsheetResponse>(spreadsheetResponseJson);
                        var targetSheet = spreadsheetResponse?.Sheets?.FirstOrDefault(s => s.Properties.SheetId == id);
                        if (targetSheet != null)
                        {
                            return targetSheet.Properties.Title;
                        }
                    }
                    catch { }
                }

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Sheet with ID [{id}] not found", this, GPALObjectType.GoogleSheets);
                return null;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Invalid sheet name or ID type", this, GPALObjectType.GoogleSheets);
                return null;
            }
        }

        private object ResolveSheetIdOrName(object sheetNameOrId)
        {
            if (sheetNameOrId == null)
            {
                return SheetName ?? "Sheet1";
            }

            if (sheetNameOrId is string name)
            {
                return string.IsNullOrEmpty(name) ? SheetName ?? "Sheet1" : name;
            }
            else if (sheetNameOrId is int id)
            {
                return id;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Invalid sheet name or ID type", this, GPALObjectType.GoogleSheets);
                return null;
            }
        }

        #region NEW
        private bool ReadDataFromRange()
        {
            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                return false;
            }

            if (string.IsNullOrEmpty(SheetName))
            {
                SheetName = "Sheet1";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default sheet name 'Sheet1'", this, GPALObjectType.GoogleSheets);
            }

            if (string.IsNullOrEmpty(ReadRange))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Range not set for reading data", this, GPALObjectType.GoogleSheets);
                return false;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("ReadRange", new { spreadsheetId = SpreadsheetId, sheetName = SheetName, range = ReadRange });
                object successObj = null;
                object dataObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj && response.TryGetValue("data", out  dataObj))
                {
                    try
                    {
                        var dataElement = JsonSerializer.Deserialize<JsonElement>(dataObj.ToString());
                        var appsScriptGrid = GPAL.GridForType<string>();
                        foreach (var row in dataElement.EnumerateArray())
                            appsScriptGrid.AddRow(row.EnumerateArray().Select(cell => JsonValueKind.String == cell.ValueKind ? cell.GetString() : cell.ToString()).ToList());
                        ReadData = appsScriptGrid;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to parse Apps Script read data", this, GPALObjectType.GoogleSheets, ex);
                        return false;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to read range via Apps Script", this, GPALObjectType.GoogleSheets);
                    return false;
                }
            }
            else
            {
                string endpoint = string.Format(googleSheetsConfig.ReadRangeEndpoint, SpreadsheetId, SheetName, ReadRange);
                var responseJson = SheetsRESTClient
                    .WithEndpoint(endpoint)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (responseJson != null)
                {
                    try
                    {
                        var valueRange = JsonSerializer.Deserialize<ValueRange>(responseJson);
                        var restGrid = GPAL.GridForType<string>();
                        foreach (var row in valueRange?.values ?? new List<List<object>>())
                            restGrid.AddRow(row.Select(cell => cell?.ToString()).ToList());
                        ReadData = restGrid;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to parse API read data", this, GPALObjectType.GoogleSheets, ex);
                        return false;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to read range via API", this, GPALObjectType.GoogleSheets);
                    return false;
                }
            }
        }

        private dynamic CallWebApp(string action, object parameters)
        {
            if (string.IsNullOrEmpty(googleSheetsConfig.WebAppUrl))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Web app URL not set", this, GPALObjectType.GoogleSheets);
                return null;
            }

            var payload = new { action, parameters };
            var responseJson = SheetsRESTClient
                .WithEndpoint(googleSheetsConfig.WebAppUrl)
                .WithParameters(payload)
                .WithHttpMethod("POST")
                .WithHeader("Content-Type", "application/json")
                .Execute();

            if (responseJson == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to call web app for action: [{action}]", this, GPALObjectType.GoogleSheets);
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(responseJson);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to parse web app response", this, GPALObjectType.GoogleSheets, ex);
                return null;
            }
        }

        public IAllowSheetSelection SaveTo(out IGPALGrid<string> gPalGrid)
        {
            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                gPalGrid = null;
                return this;
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        gPalGrid = null;
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No data to save; ReadData is null and no range specified", this, GPALObjectType.GoogleSheets);
                    gPalGrid = null;
                    return this;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ReadData.Rows}] rows of sheet [{SheetName}] to grid.", this, GPALObjectType.GoogleSheets);

            gPalGrid = ReadData;

            return this;
        }

        public IAllowDataOperations AppendToCell(string cell)
        {
            if (string.IsNullOrEmpty(cell))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cell reference cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(SheetName))
            {
                SheetName = "Sheet1";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default sheet name 'Sheet1'", this, GPALObjectType.GoogleSheets);
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to append; ReadData is null and no range specified", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }

            // Note: Flattening grid to string; assuming user intent for single cell operations
            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("AppendToCell", new { spreadsheetId = SpreadsheetId, sheetName = SheetName, cell, data = ReadData, separator = InsertSeparator });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to append to cell via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                string endpoint = string.Format(googleSheetsConfig.ReadRangeEndpoint, SpreadsheetId, SheetName, cell);
                var readResponseJson = SheetsRESTClient
                    .WithEndpoint(endpoint)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                string currentContent = "";
                if (readResponseJson != null)
                {
                    try
                    {
                        var valueRange = JsonSerializer.Deserialize<ValueRange>(readResponseJson);
                        currentContent = valueRange.values?.FirstOrDefault()?.FirstOrDefault()?.ToString() ?? "";
                    }
                    catch { }
                }

                string appendData = string.Join(InsertSeparator ?? "", ReadData?.SelectMany(row => row) ?? Enumerable.Empty<string>());
                string newContent = currentContent + appendData;

                var writeEndpoint = endpoint + googleSheetsConfig.ValueInputOption;
                var writeResponse = SheetsRESTClient
                    .WithEndpoint(writeEndpoint)
                    .WithParameters(new { values = new List<List<string>> { new List<string> { newContent } } })
                    .WithHttpMethod("PUT")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (writeResponse == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to append to cell", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowDataOperations PrependToCell(string cell)
        {
            if (string.IsNullOrEmpty(cell))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cell reference cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(SheetName))
            {
                SheetName = "Sheet1";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default sheet name 'Sheet1'", this, GPALObjectType.GoogleSheets);
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to prepend; ReadData is null and no range specified", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("PrependToCell", new { spreadsheetId = SpreadsheetId, sheetName = SheetName, cell, data = ReadData, separator = InsertSeparator });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to prepend to cell via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                string endpoint = string.Format(googleSheetsConfig.ReadRangeEndpoint, SpreadsheetId, SheetName, cell);
                var readResponseJson = SheetsRESTClient
                    .WithEndpoint(endpoint)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                string currentContent = "";
                if (readResponseJson != null)
                {
                    try
                    {
                        var valueRange = JsonSerializer.Deserialize<ValueRange>(readResponseJson);
                        currentContent = valueRange.values?.FirstOrDefault()?.FirstOrDefault()?.ToString() ?? "";
                    }
                    catch { }
                }

                string prependData = string.Join(InsertSeparator ?? "", ReadData?.SelectMany(row => row) ?? Enumerable.Empty<string>());
                string newContent = prependData + currentContent;

                var writeEndpoint = endpoint + googleSheetsConfig.ValueInputOption;
                var writeResponse = SheetsRESTClient
                    .WithEndpoint(writeEndpoint)
                    .WithParameters(new { values = new List<List<string>> { new List<string> { newContent } } })
                    .WithHttpMethod("PUT")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (writeResponse == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to prepend to cell", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowDataOperations InsertAtCell(string cell)
        {
            if (string.IsNullOrEmpty(cell))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cell reference cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(SheetName))
            {
                SheetName = "Sheet1";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default sheet name 'Sheet1'", this, GPALObjectType.GoogleSheets);
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to insert; ReadData is null and no range specified", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("InsertAtCell", new { spreadsheetId = SpreadsheetId, sheetName = SheetName, cell, data = ReadData, separator = InsertSeparator, position = InsertPosition });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to insert at cell via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                string endpoint = string.Format(googleSheetsConfig.ReadRangeEndpoint, SpreadsheetId, SheetName, cell);
                var readResponseJson = SheetsRESTClient
                    .WithEndpoint(endpoint)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                string currentContent = "";
                if (readResponseJson != null)
                {
                    try
                    {
                        var valueRange = JsonSerializer.Deserialize<ValueRange>(readResponseJson);
                        currentContent = valueRange.values?.FirstOrDefault()?.FirstOrDefault()?.ToString() ?? "";
                    }
                    catch { }
                }

                string insertData = string.Join(InsertSeparator ?? "", ReadData?.SelectMany(row => row) ?? Enumerable.Empty<string>());
                int pos = Math.Min(InsertPosition ?? 0, currentContent.Length);
                string newContent = currentContent.Substring(0, pos) + insertData + currentContent.Substring(pos);

                var writeEndpoint = endpoint + googleSheetsConfig.ValueInputOption;
                var writeResponse = SheetsRESTClient
                    .WithEndpoint(writeEndpoint)
                    .WithParameters(new { values = new List<List<string>> { new List<string> { newContent } } })
                    .WithHttpMethod("PUT")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (writeResponse == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to insert at cell", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowSheetOperations WithWriteRange(string writeRange)
        {
            if (string.IsNullOrEmpty(writeRange))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Range cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            WriteRange = writeRange;

            return this;
        }

        public IAllowDataOperations WriteToSheet(object sheetNameOrId)
        {
            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            string targetSheet = ResolveSheetName(sheetNameOrId);
            if (string.IsNullOrEmpty(targetSheet))
            {
                return this;
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to write; ReadData is null and no range specified", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("WriteRange", new { spreadsheetId = SpreadsheetId, sheetName = targetSheet, range = WriteRange, data = ReadData });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to write range via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                string endpoint = string.Format(googleSheetsConfig.WriteRangeEndpoint, SpreadsheetId, targetSheet, WriteRange) + googleSheetsConfig.ValueInputOption;
                var response = SheetsRESTClient
                    .WithEndpoint(endpoint)
                    .WithParameters(new { values = ReadData })
                    .WithHttpMethod("PUT")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to write range", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowDataOperations AppendToSheet(object sheetNameOrId)
        {
            object targetSheetOrId = ResolveSheetIdOrName(sheetNameOrId);
            if (targetSheetOrId == null)
            {
                return this;
            }

            if (string.IsNullOrEmpty(SpreadsheetId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to append.", this, GPALObjectType.GoogleSheets);
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to append.", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("AppendToSheet", new { spreadsheetId = SpreadsheetId, sheetNameOrId = targetSheetOrId, data = ReadData });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to append to sheet via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                string targetSheetName = ResolveSheetName(sheetNameOrId);
                if (string.IsNullOrEmpty(targetSheetName))
                {
                    return this;
                }
                string endpoint = string.Format(googleSheetsConfig.AppendRangeEndpoint, SpreadsheetId, targetSheetName, "A1") + "?valueInputOption=RAW&insertDataOption=INSERT_ROWS";
                var response = SheetsRESTClient
                    .WithEndpoint(endpoint)
                    .WithParameters(new { values = ReadData })
                    .WithHttpMethod("POST")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to append to sheet", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }
        #endregion NEW

        /// <summary>
        /// Reads the Apps Script gateway source embedded in this assembly; every Sheets operation posts to the web app
        /// deployed from it.
        /// <br/><br/>
        /// Embedded so Browser\gcode.js is the only copy.
        /// </summary>
        /// <returns>The script source.</returns>
        /// <summary>Resource name of the gateway script: RootNamespace plus its folder path.</summary>
        private const string GatewayScriptResource = "GenerallyPositive.Browser.gcode.js";

        private string ReadGatewayScript()
        {
            string retVal = null;

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GatewayScriptResource))
            {
                if (null == stream)
                {
                    string msg = $"[{GatewayScriptResource}] is not in this assembly, so there is no gateway script to deploy";

                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.GoogleSheets);

                    throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
                }

                using (StreamReader reader = new StreamReader(stream))
                    retVal = reader.ReadToEnd();
            }

            return retVal;
        }

        private void SetupAndDeployScript(string spreadsheetIdOrTitle)
        {
            string cloudProjectId = SetupCloudProject($"GPAL_Sheets_{Guid.NewGuid().ToString().Substring(0, 8)}");
            if (string.IsNullOrEmpty(cloudProjectId))
            {
                return;
            }

            string scriptTitle = $"GPAL_Script_{spreadsheetIdOrTitle}";
            string scriptId = GetOrCreateScriptProject(cloudProjectId, scriptTitle);
            if (string.IsNullOrEmpty(scriptId))
            {
                return;
            }

            // Check for existing deployment
            var deploymentResponse = ScriptsRESTClient
                .WithEndpoint($"/v1/projects/{scriptId}/deployments")
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (deploymentResponse != null)
            {
                try
                {
                    var deploymentData = JsonSerializer.Deserialize<Dictionary<string, object>>(deploymentResponse);
                    object deploymentsObj = null;
                    if (deploymentData.TryGetValue("deployments", out deploymentsObj))
                    {
                        var deployments = JsonSerializer.Deserialize<JsonElement>(deploymentsObj.ToString());
                        foreach (var deployment in deployments.EnumerateArray())
                        {
                            if (deployment.TryGetProperty("entryPoints", out var entryPoints))
                            {
                                foreach (var entry in entryPoints.EnumerateArray())
                                {
                                    if (entry.TryGetProperty("webApp", out var webApp) && webApp.TryGetProperty("url", out var url))
                                    {
                                        googleSheetsConfig.WebAppUrl = url.GetString();
                                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Found existing web app at: [{googleSheetsConfig.WebAppUrl}]", this, GPALObjectType.GoogleSheets);
                                        AppsScriptProjectId = scriptId;
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Hardcode the Apps Script content (or load from embedded resource)
            string scriptContent = ReadGatewayScript();

            this.WithProjectId(scriptId)
                .WithScriptName("main")
                .WithScriptAccess(ScriptAccessType.ME)
                .WithExecuteAs(ExecuteAsType.USER_DEPLOYING)
                .UploadScript(scriptContent)
                .DeployScriptAsWebApp(out string webAppUrl);

            if (!string.IsNullOrEmpty(webAppUrl))
            {
                googleSheetsConfig.WebAppUrl = webAppUrl;
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Apps Script deployed at: [{webAppUrl}]", this, GPALObjectType.GoogleSheets);
            }
        }

        /// <summary>
        /// Uses a hand-deployed Apps Script web app instead of the Sheets API. Only the url is needed: the script runs
        /// as its deployer, so no credentials, Cloud project or OAuth client.
        /// <br/><br/>
        /// Deploy GPAL's gateway script, the same one <see cref="WithCredentials"/> deploys.
        /// </summary>
        /// <param name="webAppUrl">The /exec url from the Apps Script deployment.</param>
        /// <returns>Fluent interface to choose a spreadsheet</returns>
        public IAllowSpreadsheetSelection WithWebAppUrl(string webAppUrl)
        {
            if (true == string.IsNullOrWhiteSpace(webAppUrl))
            {
                string msg = "No web app url was supplied, so there is no gateway to drive sheets through";

                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.GoogleSheets);

                throw new GPALException($"{GPAL.MyMethodName()}: {msg}");
            }

            googleSheetsConfig.WebAppUrl = webAppUrl;

            // Set UseAppsScript directly. InitializeMode only selects it when token validation fails, and this route
            // has no token.
            googleSheetsConfig.UseAppsScript = true;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Sheets run through the web app at [{webAppUrl}]", this, GPALObjectType.GoogleSheets);

            return this;
        }

        public IAllowSpreadsheetSelection WithCredentials(ICredentials credentials)
        {
            Credentials = credentials;
            Credentials.FetchAccessToken(out string accessToken);
            InitializeMode();
            return this;
        }

        public IAllowSheetSelection WithSpreadsheet(string spreadsheetIdOrTitle)
        {
            if (string.IsNullOrEmpty(spreadsheetIdOrTitle))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Spreadsheet ID or title cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            InitializeMode();

            if (googleSheetsConfig.UseAppsScript)
            {
                if (string.IsNullOrEmpty(googleSheetsConfig.WebAppUrl))
                {
                    SetupAndDeployScript(spreadsheetIdOrTitle);
                }

                var response = CallWebApp("WithSpreadsheet", new { spreadsheetIdOrTitle });
                object successObj = null;
                object idObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj && response.TryGetValue("spreadsheetId", out idObj))
                {
                    SpreadsheetId = idObj.ToString();
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to open or create spreadsheet [{spreadsheetIdOrTitle}] via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var responseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{spreadsheetIdOrTitle}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (!string.IsNullOrEmpty(responseJson))
                {
                    SpreadsheetId = spreadsheetIdOrTitle;
                }
                else
                {
                    var createRequest = new { properties = new { title = spreadsheetIdOrTitle } };
                    var createResponseJson = SheetsRESTClient
                        .WithEndpoint("/v4/spreadsheets")
                        .WithParameters(createRequest)
                        .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                        .WithHttpMethod("POST")
                        .Execute();

                    if (createResponseJson != null)
                    {
                        var createResponse = JsonSerializer.Deserialize<SpreadsheetResponse>(createResponseJson);
                        if (!string.IsNullOrEmpty(createResponse?.SpreadsheetId))
                        {
                            SpreadsheetId = createResponse.SpreadsheetId;
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to create spreadsheet [{spreadsheetIdOrTitle}]", this, GPALObjectType.GoogleSheets);
                        }
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to create spreadsheet [{spreadsheetIdOrTitle}]", this, GPALObjectType.GoogleSheets);
                    }
                }
            }

            return this;
        }

        public IAllowSheetOperations CreateSheet(string sheetName)
        {
            if (string.IsNullOrEmpty(sheetName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Sheet name cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("CreateSheet", new { spreadsheetId = SpreadsheetId, sheetName });
                object successObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj)
                {
                    SheetName = sheetName;
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create sheet via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var request = new
                {
                    requests = new[] { new { addSheet = new { properties = new { title = sheetName } } } }
                };
                var response = SheetsRESTClient
                    .WithEndpoint(string.Format(googleSheetsConfig.FormatRangeEndpoint, SpreadsheetId))
                    .WithParameters(request)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create sheet", this, GPALObjectType.GoogleSheets);
                }

                SheetName = sheetName;
            }

            return this;
        }

        public IAllowSheetSelection SetSheetName(string newSheetName)
        {
            if (string.IsNullOrEmpty(newSheetName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Title cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("WithTitle", new { spreadsheetId = SpreadsheetId, sheetName = SheetName, title = newSheetName });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to set sheet title via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var request = new
                {
                    requests = new[] { new { updateSheetProperties = new { properties = new { title = newSheetName }, fields = googleSheetsConfig.TitleField } } }
                };
                var response = SheetsRESTClient
                    .WithEndpoint(string.Format(googleSheetsConfig.FormatRangeEndpoint, SpreadsheetId))
                    .WithParameters(request)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to set sheet title", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }
        public IAllowSheetSelection SetSpreadsheetTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Title cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("WithTitle", new { spreadsheetId = SpreadsheetId, sheetName = SheetName, title });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to set sheet title via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var request = new
                {
                    requests = new[] { new { updateSpreadsheetProperties = new { properties = new { title }, fields = googleSheetsConfig.TitleField } } }
                };
                var response = SheetsRESTClient
                    .WithEndpoint(string.Format(googleSheetsConfig.FormatRangeEndpoint, SpreadsheetId))
                    .WithParameters(request)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to set sheet title", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowInDataOperations WithSheet(object sheetNameOrId)
        {
            if (sheetNameOrId == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Sheet name or ID cannot be null", this, GPALObjectType.GoogleSheets);
                return this;
            }

            string sheetName = null;
            int? sheetId = null;

            if (sheetNameOrId is string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Sheet name cannot be empty", this, GPALObjectType.GoogleSheets);
                    return this;
                }
                sheetName = name;
            }
            else if (sheetNameOrId is int id)
            {
                sheetId = id;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Invalid sheet name or ID type", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var action = sheetId.HasValue ? "WithSheetById" : "WithSheet";
                var response = CallWebApp(action, new { spreadsheetId = SpreadsheetId, sheetName, sheetId });
                object successObj = null;
                object nameObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj)
                {
                    if (response.TryGetValue("sheetName", out nameObj))
                    {
                        SheetName = nameObj.ToString();
                    }
                    else
                    {
                        SheetName = sheetName;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"Failed to activate sheet {(sheetId.HasValue ? $"with ID {sheetId}" : $"named {sheetName}")} via Apps Script",
                        this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var spreadsheetResponseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{SpreadsheetId}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                SpreadsheetResponse spreadsheetResponse = null;
                if (spreadsheetResponseJson != null)
                {
                    try
                    {
                        spreadsheetResponse = JsonSerializer.Deserialize<SpreadsheetResponse>(spreadsheetResponseJson);
                    }
                    catch { }
                }

                if (spreadsheetResponse == null || spreadsheetResponse.Sheets == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to retrieve spreadsheet info", this, GPALObjectType.GoogleSheets);
                    return this;
                }

                var targetSheet = sheetId.HasValue
                    ? spreadsheetResponse.Sheets.FirstOrDefault(s => s.Properties.SheetId == sheetId.Value)
                    : spreadsheetResponse.Sheets.FirstOrDefault(s => s.Properties.Title.Equals(sheetName, StringComparison.OrdinalIgnoreCase));

                if (targetSheet == null)
                {
                    if (sheetId.HasValue)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Sheet with ID [{sheetId}] not found", this, GPALObjectType.GoogleSheets);
                        return this;
                    }
                    else
                    {
                        CreateSheet(sheetName);
                        targetSheet = spreadsheetResponse.Sheets.FirstOrDefault(s => s.Properties.Title.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
                        if (targetSheet == null)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to create sheet [{sheetName}]", this, GPALObjectType.GoogleSheets);
                            return this;
                        }
                    }
                }

                int targetSheetId = targetSheet.Properties.SheetId;
                SheetName = targetSheet.Properties.Title;

                var activateRequest = new
                {
                    requests = new[]
                    {
                        new
                        {
                            updateSheetProperties = new
                            {
                                properties = new { sheetId = targetSheetId, hidden = false },
                                fields = googleSheetsConfig.HiddenField
                            }
                        }
                    }
                };

                var activateResponse = SheetsRESTClient
                    .WithEndpoint(string.Format(googleSheetsConfig.FormatRangeEndpoint, SpreadsheetId))
                    .WithParameters(activateRequest)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (activateResponse == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to activate sheet", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowDataOperations WithReadRange(string readRange)
        {
            if (string.IsNullOrEmpty(readRange))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Range cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            ReadRange = readRange;
            return this;
        }

        public IAllowDataOperations WithData(IGPALGrid<string> data)
        {
            ReadData = data;
            return this;
        }

        public IAllowSheetSelection DeleteSheet(object sheetNameOrId)
        {
            if (sheetNameOrId == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Sheet name or ID cannot be null", this, GPALObjectType.GoogleSheets);
                return this;
            }

            string sheetName = null;
            int? sheetId = null;

            if (sheetNameOrId is string name)
            {
                if (string.IsNullOrEmpty(name))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Sheet name cannot be empty", this, GPALObjectType.GoogleSheets);
                    return this;
                }
                sheetName = name;
            }
            else if (sheetNameOrId is int id)
            {
                sheetId = id;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Invalid sheet name or ID type", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var action = sheetId.HasValue ? "DeleteSheetById" : "DeleteSheet";
                var response = CallWebApp(action, new { spreadsheetId = SpreadsheetId, sheetName, sheetId });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"Failed to delete sheet {(sheetId.HasValue ? $"with ID {sheetId}" : $"named {sheetName}")} via Apps Script",
                        this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var spreadsheetResponseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{SpreadsheetId}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                SpreadsheetResponse spreadsheetResponse = null;
                if (spreadsheetResponseJson != null)
                {
                    try
                    {
                        spreadsheetResponse = JsonSerializer.Deserialize<SpreadsheetResponse>(spreadsheetResponseJson);
                    }
                    catch { }
                }

                if (spreadsheetResponse == null || spreadsheetResponse.Sheets == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to retrieve spreadsheet info", this, GPALObjectType.GoogleSheets);
                    return this;
                }

                var targetSheet = sheetId.HasValue
                    ? spreadsheetResponse.Sheets.FirstOrDefault(s => s.Properties.SheetId == sheetId.Value)
                    : spreadsheetResponse.Sheets.FirstOrDefault(s => s.Properties.Title.Equals(sheetName, StringComparison.OrdinalIgnoreCase));

                if (targetSheet == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"{(sheetId.HasValue ? $"Sheet with ID {sheetId}" : $"Sheet '{sheetName}'")} not found",
                        this, GPALObjectType.GoogleSheets);
                    return this;
                }

                var request = new
                {
                    requests = new[] { new { deleteSheet = new { sheetId = targetSheet.Properties.SheetId } } }
                };
                var response = SheetsRESTClient
                    .WithEndpoint(string.Format(googleSheetsConfig.FormatRangeEndpoint, SpreadsheetId))
                    .WithParameters(request)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"Failed to delete sheet {(sheetId.HasValue ? $"with ID {sheetId}" : $"named {sheetName}")}",
                        this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        public IAllowSheetSelection ListSheets(out IEnumerable<(string name, int sheetId)> sheets)
        {
            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("ListSheets", new { spreadsheetId = SpreadsheetId });
                object successObj = null;
                object sheetsObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj && response.TryGetValue("sheets", out sheetsObj))
                {
                    try
                    {
                        var sheetsElement = JsonSerializer.Deserialize<JsonElement>(sheetsObj.ToString());
                        sheets = sheetsElement.EnumerateArray().Select(s => (s.GetProperty("name").GetString(), s.GetProperty("sheetId").GetInt32()));
                        return this;
                    }
                    catch
                    {
                        sheets = Enumerable.Empty<(string, int)>();
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to parse sheets list via Apps Script", this, GPALObjectType.GoogleSheets);
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to list sheets via Apps Script", this, GPALObjectType.GoogleSheets);
                    sheets = Enumerable.Empty<(string, int)>();
                    return this;
                }
            }
            else
            {
                var responseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{SpreadsheetId}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (responseJson == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to list sheets", this, GPALObjectType.GoogleSheets);
                    sheets = Enumerable.Empty<(string, int)>();
                    return this;
                }

                try
                {
                    var response = JsonSerializer.Deserialize<SpreadsheetResponse>(responseJson);
                    if (response?.Sheets != null)
                    {
                        sheets = response.Sheets.Select(s => (s.Properties.Title, s.Properties.SheetId));
                    }
                    else
                    {
                        sheets = Enumerable.Empty<(string, int)>();
                    }
                }
                catch
                {
                    sheets = Enumerable.Empty<(string, int)>();
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to parse sheets list", this, GPALObjectType.GoogleSheets);
                }

                return this;
            }
        }

        public IAllowInsertSettings WithInsertPosition(int position)
        {
            InsertPosition = position;
            return this;
        }

        public IAllowInsertSettings WithInsertSeparator(string separator)
        {
            InsertSeparator = separator;
            return this;
        }

        public IAllowDataOperations CalculateSum(out string result)
        {
            if (ReadData == null && !string.IsNullOrEmpty(ReadRange))
            {
                ReadDataFromRange();
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("CalculateSum", new { data = ReadData });
                object successObj = null;
                object resultObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj && response.TryGetValue("result", out resultObj))
                {
                    result = resultObj.ToString();
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to calculate sum via Apps Script", this, GPALObjectType.GoogleSheets);
                    result = "0";
                }
            }
            else
            {
                result = ReadData?.SelectMany(row => row)
                    .Where(v => double.TryParse(v, out _))
                    .Sum(v => double.Parse(v))
                    .ToString() ?? "0";
            }

            return this;
        }

        public IAllowDataOperations CalculateCount(out string result)
        {
            if (ReadData == null && !string.IsNullOrEmpty(ReadRange))
            {
                ReadDataFromRange();
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("CalculateCount", new { data = ReadData });
                object successObj = null;
                object resultObj = null;
                if (response != null && response.TryGetValue("success", out successObj) && successObj is bool && (bool)successObj && response.TryGetValue("result", out resultObj))
                {
                    result = resultObj.ToString();
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to calculate count via Apps Script", this, GPALObjectType.GoogleSheets);
                    result = "0";
                }
            }
            else
            {
                result = ReadData?.SelectMany(row => row).Count().ToString() ?? "0";
            }

            return this;
        }

        public IAllowFormatSettings WithFormatType(FormatType formatType)
        {
            OurFormatType = formatType;
            return this;
        }

        public IAllowFormatRange WithFormatValue(object formatValue)
        {
            FormatValue = formatValue;
            return this;
        }

        public IAllowFormatRange WithAlignmentFormatValue(HorizontalAlignmentType alignment)
        {
            FormatValue = alignment;
            return this;
        }

        public IAllowFormatRange WithAlignmentFormatValue(VerticalAlignmentType alignment)
        {
            FormatValue = alignment;
            return this;
        }

        public IAllowFormatRange WithNumberFormatValue(NumberFormatType numberFormat)
        {
            NumberFormatType = numberFormat;
            return this;
        }

        public IAllowFormatRange WithTextRotationFormatValue(TextRotationType rotation)
        {
            FormatValue = rotation;
            return this;
        }

        public IAllowFormatRange WithColorFormatValue(System.Drawing.Color color)
        {
            FormatValue = color;
            return this;
        }

        public IAllowInDataOperations FormatRange(string range)
        {
            if (string.IsNullOrEmpty(range))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Range cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (OurFormatType == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Format type not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (googleSheetsConfig.UseAppsScript)
            {
                var response = CallWebApp("FormatRange", new
                {
                    spreadsheetId = SpreadsheetId,
                    sheetName = SheetName,
                    range,
                    formatType = OurFormatType.ToString(),
                    formatValue = FormatValue?.ToString(),
                    numberFormatType = NumberFormatType?.ToString(),
                    //// post-v1: a custom number format pattern
                    //numberFormatPattern = NumberFormatPattern
                });
                object successObj = null;
                if (response == null || !response.TryGetValue("success", out successObj) || !(successObj is bool) || !(bool)successObj)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to format range via Apps Script", this, GPALObjectType.GoogleSheets);
                }
            }
            else
            {
                var (targetSheetId, startRowIndex, endRowIndex, startColumnIndex, endColumnIndex) = ParseRange(range);
                var formatRequest = new
                {
                    requests = new[]
                    {
                        new
                        {
                            repeatCell = new
                            {
                                range = new { sheetId = targetSheetId, startRowIndex, endRowIndex, startColumnIndex, endColumnIndex },
                                cell = GetCellFormat(),
                                fields = GetFieldsForFormatType(OurFormatType.Value)
                            }
                        }
                    }
                };
                var response = SheetsRESTClient
                    .WithEndpoint(string.Format(googleSheetsConfig.FormatRangeEndpoint, SpreadsheetId))
                    .WithParameters(formatRequest)
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (response == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to format range", this, GPALObjectType.GoogleSheets);
                }
            }

            return this;
        }

        private object GetCellFormat()
        {
            if (FormatValue == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Format value not set for [{OurFormatType}]", this, GPALObjectType.GoogleSheets);
                return new { userEnteredFormat = new { } };
            }

            switch (OurFormatType)
            {
                case FormatType.Bold:
                    if (FormatValue is bool bold)
                        return new { userEnteredFormat = new { textFormat = new { bold } } };
                    break;
                case FormatType.Italic:
                    if (FormatValue is bool italic)
                        return new { userEnteredFormat = new { textFormat = new { italic } } };
                    break;
                case FormatType.Underline:
                    if (FormatValue is bool underline)
                        return new { userEnteredFormat = new { textFormat = new { underline } } };
                    break;
                case FormatType.Strikethrough:
                    if (FormatValue is bool strikethrough)
                        return new { userEnteredFormat = new { textFormat = new { strikethrough } } };
                    break;
                case FormatType.FontSize:
                    if (FormatValue is int fontSize)
                        return new { userEnteredFormat = new { textFormat = new { fontSize } } };
                    break;
                case FormatType.FontFamily:
                    if (FormatValue is string fontFamily)
                        return new { userEnteredFormat = new { textFormat = new { fontFamily } } };
                    break;
                case FormatType.ForegroundColor:
                    if (FormatValue is System.Drawing.Color color)
                        return new { userEnteredFormat = new { textFormat = new { foregroundColor = new { red = color.R / 255.0, green = color.G / 255.0, blue = color.B / 255.0 } } } };
                    break;
                case FormatType.CellBackgroundColor:
                    if (FormatValue is System.Drawing.Color bgColor)
                        return new { userEnteredFormat = new { backgroundColor = new { red = bgColor.R / 255.0, green = bgColor.G / 255.0, blue = bgColor.B / 255.0 } } };
                    break;
                case FormatType.NumberFormat:
                    if (NumberFormatType != null)
                        return new
                        {
                            userEnteredFormat = new
                            {
                                numberFormat = new
                                {
                                    type = NumberFormatType.Value.ToString().ToUpper(),
                                    //// post-v1: a custom number format pattern
                                    pattern = (string)null
                                }
                            }
                        };
                    break;
                case FormatType.HorizontalAlignment:
                    if (FormatValue is HorizontalAlignmentType hAlign)
                        return new { userEnteredFormat = new { horizontalAlignment = hAlign.ToString().ToUpper() } };
                    break;
                case FormatType.VerticalAlignment:
                    if (FormatValue is VerticalAlignmentType vAlign)
                        return new { userEnteredFormat = new { verticalAlignment = vAlign.ToString().ToUpper() } };
                    break;
                case FormatType.TextRotation:
                    if (FormatValue is TextRotationType rotation)
                    {
                        object textRotation = rotation switch
                        {
                            TextRotationType.TiltUp30 => new { degrees = 30 },
                            TextRotationType.TiltUp45 => new { degrees = 45 },
                            TextRotationType.TiltDown30 => new { degrees = -30 },
                            TextRotationType.TiltDown45 => new { degrees = -45 },
                            TextRotationType.UpsideDown => new { degrees = -180 },
                            TextRotationType.Vertical => new { vertical = true },
                            _ => new { degrees = 0 }
                        };
                        return new { userEnteredFormat = new { textFormat = new { textRotation } } };
                    }
                    break;
            }

            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Invalid format value for [{OurFormatType}]", this, GPALObjectType.GoogleSheets);
            return new { userEnteredFormat = new { } };
        }

        private string GetFieldsForFormatType(FormatType formatType)
        {
            string subfield;
            switch (formatType)
            {
                case FormatType.Bold:
                case FormatType.Italic:
                case FormatType.Underline:
                case FormatType.Strikethrough:
                case FormatType.FontSize:
                case FormatType.FontFamily:
                case FormatType.ForegroundColor:
                    subfield = "textFormat";
                    break;
                case FormatType.CellBackgroundColor:
                    subfield = "backgroundColor";
                    break;
                case FormatType.NumberFormat:
                    subfield = "numberFormat";
                    break;
                case FormatType.HorizontalAlignment:
                    subfield = "horizontalAlignment";
                    break;
                case FormatType.VerticalAlignment:
                    subfield = "verticalAlignment";
                    break;
                case FormatType.TextRotation:
                    subfield = "textFormat.textRotation";
                    break;
                default:
                    subfield = "";
                    break;
            }
            ;
            return string.IsNullOrEmpty(subfield) ? googleSheetsConfig.UserEnteredFormatField : $"{googleSheetsConfig.UserEnteredFormatField}.{subfield}";
        }

        private (int sheetId, int? startRowIndex, int? endRowIndex, int? startColumnIndex, int? endColumnIndex) ParseRange(string range)
        {
            int sheetId = 0; // Default to first sheet if no sheet specified
            string sheetNameFromRange = null;
            string rangePart = range;

            if (range.Contains("!"))
            {
                var parts = range.Split('!');
                sheetNameFromRange = parts[0].Trim('\'');
                rangePart = parts[1];
            }

            if (!string.IsNullOrEmpty(sheetNameFromRange))
            {
                var spreadsheetResponseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{SpreadsheetId}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (spreadsheetResponseJson != null)
                {
                    try
                    {
                        var spreadsheetResponse = JsonSerializer.Deserialize<SpreadsheetResponse>(spreadsheetResponseJson);
                        var targetSheet = spreadsheetResponse?.Sheets?.FirstOrDefault(s => s.Properties.Title.Equals(sheetNameFromRange, StringComparison.OrdinalIgnoreCase));
                        if (targetSheet != null)
                        {
                            sheetId = targetSheet.Properties.SheetId;
                        }
                    }
                    catch { }
                }
            }
            else if (!string.IsNullOrEmpty(SheetName))
            {
                // Use current SheetName to get sheetId
                var spreadsheetResponseJson = SheetsRESTClient
                    .WithEndpoint($"{googleSheetsConfig.SpreadsheetEndPoint}{SpreadsheetId}")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (spreadsheetResponseJson != null)
                {
                    try
                    {
                        var spreadsheetResponse = JsonSerializer.Deserialize<SpreadsheetResponse>(spreadsheetResponseJson);
                        var targetSheet = spreadsheetResponse?.Sheets?.FirstOrDefault(s => s.Properties.Title.Equals(SheetName, StringComparison.OrdinalIgnoreCase));
                        if (targetSheet != null)
                        {
                            sheetId = targetSheet.Properties.SheetId;
                        }
                    }
                    catch { }
                }
            }

            // Improved regex to handle more A1 notation cases: A1, A1:B2, A:A, 1:1, A1:A, etc.
            var match = System.Text.RegularExpressions.Regex.Match(rangePart, @"^([A-Z]+)?(\d+)?(?::([A-Z]+)?(\d+)?)?$");
            if (!match.Success)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Invalid range format: [{range}]", this, GPALObjectType.GoogleSheets);
                return (sheetId, null, null, null, null);
            }

            string startCol = match.Groups[1].Value;
            string startRowStr = match.Groups[2].Value;
            string endCol = match.Groups[3].Value;
            string endRowStr = match.Groups[4].Value;
            int? startRowIndex;
            if (string.IsNullOrEmpty(startRowStr))
            {
                startRowIndex = null;
            }
            else
            {
                startRowIndex = int.Parse(startRowStr) - 1;
            }

            int? endRowIndex;
            if (string.IsNullOrEmpty(endRowStr))
            {
                endRowIndex = null;
            }
            else
            {
                endRowIndex = int.Parse(endRowStr);
            }

            int? startColumnIndex;
            if (string.IsNullOrEmpty(startCol))
            {
                startColumnIndex = null;
            }
            else
            {
                startColumnIndex = ColumnToIndex(startCol);
            }

            int? endColumnIndex;
            if (string.IsNullOrEmpty(endCol))
            {
                endColumnIndex = null;
            }
            else
            {
                endColumnIndex = ColumnToIndex(endCol) + 1;
            }

            return (sheetId, startRowIndex, endRowIndex, startColumnIndex, endColumnIndex);
        }

        private int ColumnToIndex(string column)
        {
            int index = 0;
            foreach (char c in column.ToUpper())
            {
                index = index * 26 + (c - 'A' + 1);
            }
            return index - 1;
        }

        public IAllowTriggerSettings WithTriggerFunction(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Trigger function name is empty", this, GPALObjectType.GoogleSheets);
            }
            FunctionName = functionName;
            return this;
        }

        public IAllowTriggerSettings WithTriggerSchedule(TriggerScheduleType schedule)
        {
            TriggerSchedule = schedule;
            return this;
        }

        public IAllowDataOperations CreateTrigger(GoogleTriggerType triggerType)
        {
            if (string.IsNullOrEmpty(AppsScriptProjectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Apps Script project ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(FunctionName))
            {
                if (string.IsNullOrEmpty(ScriptName))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Trigger function name not set", this, GPALObjectType.GoogleSheets);
                    return this;
                }
                FunctionName = ScriptName;
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Using script name [{ScriptName}] as trigger function name", this, GPALObjectType.GoogleSheets);
            }

            var triggerData = new Dictionary<string, object> { { "functionName", FunctionName } };
            if (triggerType == GoogleTriggerType.TimeBased && TriggerSchedule != TriggerScheduleType.None)
            {
                triggerData["timeBased"] = new { type = TriggerSchedule.ToString().ToUpper() };
            }

            var response = ScriptsRESTClient
                .WithEndpoint(string.Format(googleSheetsConfig.TriggerEndpoint, AppsScriptProjectId))
                .WithParameters(triggerData)
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (response == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create trigger", this, GPALObjectType.GoogleSheets);
            }

            return this;
        }

        public string SetupCloudProject(string projectName)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cloud project name cannot be empty", this, GPALObjectType.GoogleSheets);
                return "";
            }

            if (Credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials not set", this, GPALObjectType.GoogleSheets);
                return "";
            }

            _googleCloud = new GoogleCloud(Credentials);
            string projectId = _googleCloud.CreateProject(projectName);
            if (string.IsNullOrEmpty(projectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create or retrieve cloud project", this, GPALObjectType.GoogleSheets);
                return "";
            }

            bool sheetsEnabled = _googleCloud.EnableApi(projectId, GoogleApi.Sheets);
            bool scriptsEnabled = _googleCloud.EnableApi(projectId, GoogleApi.Scripts);

            if (!sheetsEnabled || !scriptsEnabled)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to enable Sheets or Scripts API", this, GPALObjectType.GoogleSheets);
                return "";
            }

            return projectId;
        }

        public string GetOrCreateScriptProject(string cloudProjectId, string scriptTitle)
        {
            if (string.IsNullOrEmpty(cloudProjectId) || string.IsNullOrEmpty(scriptTitle))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cloud project ID or script title cannot be empty", this, GPALObjectType.GoogleSheets);
                return "";
            }

            if (Credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials not set", this, GPALObjectType.GoogleSheets);
                return "";
            }

            var listResponse = ScriptsRESTClient
                .WithEndpoint("/v1/projects")
                .WithParameters(new { pageSize = 50 })
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (listResponse != null)
            {
                try
                {
                    var listData = JsonSerializer.Deserialize<Dictionary<string, object>>(listResponse);
                    object projectsObj = null;
                    if (listData.TryGetValue("projects", out projectsObj))
                    {
                        var projects = JsonSerializer.Deserialize<JsonElement>(projectsObj.ToString());
                        foreach (var project in projects.EnumerateArray())
                        {
                            if (project.TryGetProperty("title", out var title) && title.GetString().Equals(scriptTitle) &&
                                project.TryGetProperty("scriptId", out var projectId))
                            {
                                return projectId.GetString();
                            }
                        }
                    }
                }
                catch { }
            }

            var scriptPayload = new { title = scriptTitle, parentId = cloudProjectId };
            var createResponse = ScriptsRESTClient
                .WithEndpoint("/v1/projects")
                .WithParameters(scriptPayload)
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (createResponse == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create Apps Script project", this, GPALObjectType.GoogleSheets);
                return "";
            }

            try
            {
                var scriptData = JsonSerializer.Deserialize<Dictionary<string, object>>(createResponse);
                object idObj = null;
                if (scriptData.TryGetValue("scriptId", out idObj))
                {
                    return idObj.ToString();
                }
            }
            catch { }

            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No Apps Script project ID returned", this, GPALObjectType.GoogleSheets);
            return "";
        }

        public IAllowScriptOperations WithProjectId(string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Apps Script project ID is empty", this, GPALObjectType.GoogleSheets);
            }
            AppsScriptProjectId = projectId;
            return this;
        }

        public IAllowScriptOperations WithScriptName(string scriptName)
        {
            if (string.IsNullOrEmpty(scriptName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Script name is empty", this, GPALObjectType.GoogleSheets);
            }
            ScriptName = scriptName;
            return this;
        }

        public IAllowScriptOperations WithScriptAccess(ScriptAccessType access)
        {
            ScriptAccess = access;
            return this;
        }

        public IAllowScriptOperations WithExecuteAs(ExecuteAsType executeAs)
        {
            ExecuteAs = executeAs;
            return this;
        }

        public IAllowScriptOperations WithDeploymentDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Deployment description is empty; using default", this, GPALObjectType.GoogleSheets);
                DeploymentDescription = "Web app deployment for script";
            }
            else
            {
                DeploymentDescription = description;
            }
            return this;
        }

        //// post-v1: pin a script version. DeployScriptAsWebApp overwrites VersionNumber, so a value set here is ignored.
        //public IAllowScriptOperations WithVersionNumber(int versionNumber)
        //{
        //    if (versionNumber < 1)
        //    {
        //        GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Version number must be positive; using default 1", this, GPALObjectType.GoogleSheets);
        //        VersionNumber = 1;
        //    }
        //    else
        //    {
        //        VersionNumber = versionNumber;
        //    }
        //    return this;
        //}

        public IAllowDeployWebApp UploadScript(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Script content cannot be empty", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(AppsScriptProjectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Apps Script project ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (Credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (string.IsNullOrEmpty(ScriptName))
            {
                ScriptName = "main";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Using default script name 'main'", this, GPALObjectType.GoogleSheets);
            }

            var response = ScriptsRESTClient
                .WithEndpoint(string.Format(googleSheetsConfig.ScriptContentEndpoint, AppsScriptProjectId))
                .WithParameters(new
                {
                    files = new[] { new { name = ScriptName, type = googleSheetsConfig.ScriptFileType, source = scriptContent } }
                })
                .WithHttpMethod("PUT")
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (response == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to upload script", this, GPALObjectType.GoogleSheets);
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Script uploaded to project [{AppsScriptProjectId}]", this, GPALObjectType.GoogleSheets);
            }

            return this;
        }

        public IAllowDeployWebApp UploadScript(GPALFile scriptFile)
        {
            if (scriptFile == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Script file is null", this, GPALObjectType.GoogleSheets);
                return this;
            }

            string scriptContent = "";
            try
            {
                scriptContent = File.ReadAllText(scriptFile.Filename);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to read script file", this, GPALObjectType.GoogleSheets, ex);
                return this;
            }

            return UploadScript(scriptContent);
        }

        public IAllowDataOperations DeployScriptAsWebApp(out string webAppUrl)
        {
            webAppUrl = "";
            if (string.IsNullOrEmpty(AppsScriptProjectId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Apps Script project ID not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (Credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials not set", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (ExecuteAs == ExecuteAsType.USER_ACCESSING && ScriptAccess != ScriptAccessType.ANYONE && ScriptAccess != ScriptAccessType.DOMAIN)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "ExecuteAs 'USER_ACCESSING' requires access 'ANYONE' or 'DOMAIN'; using 'USER_DEPLOYING'", this, GPALObjectType.GoogleSheets);
                ExecuteAs = ExecuteAsType.USER_DEPLOYING;
            }

            // First, create a version
            var versionPayload = new { description = "Version for deployment" };
            var versionResponse = ScriptsRESTClient
                .WithEndpoint($"/v1/projects/{AppsScriptProjectId}/versions")
                .WithParameters(versionPayload)
                .WithHttpMethod("POST")
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (versionResponse == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to create script version", this, GPALObjectType.GoogleSheets);
                return this;
            }

            try
            {
                var versionData = JsonSerializer.Deserialize<Dictionary<string, object>>(versionResponse);
                object versionObj = null;
                if (versionData.TryGetValue("versionNumber", out versionObj))
                {
                    VersionNumber = Convert.ToInt32(versionObj);
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No version number returned", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }
            catch
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to parse version response", this, GPALObjectType.GoogleSheets);
                return this;
            }

            string accessString = ScriptAccess switch
            {
                ScriptAccessType.ME => "ME",
                ScriptAccessType.ANYONE => "ANYONE",
                ScriptAccessType.ANYONE_ANONYMOUS => "ANYONE_ANONYMOUS",
                ScriptAccessType.DOMAIN => "DOMAIN",
                _ => "ME"
            };

            string executeAsString = ExecuteAs switch
            {
                ExecuteAsType.USER_ACCESSING => "USER_ACCESSING",
                ExecuteAsType.USER_DEPLOYING => "USER_DEPLOYING",
                _ => "USER_DEPLOYING"
            };

            var deploymentPayload = new
            {
                versionNumber = VersionNumber,
                manifestFileName = "appsscript",
                description = DeploymentDescription
            };

            var deployResponse = ScriptsRESTClient
                .WithEndpoint($"/v1/projects/{AppsScriptProjectId}/deployments")
                .WithParameters(deploymentPayload)
                .WithHttpMethod("POST")
                .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                .Execute();

            if (deployResponse == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to deploy script", this, GPALObjectType.GoogleSheets);
                return this;
            }

            try
            {
                var deployData = JsonSerializer.Deserialize<Dictionary<string, object>>(deployResponse);
                object deploymentIdObj = null;
                if (!deployData.TryGetValue("deploymentId", out deploymentIdObj))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No deployment ID returned", this, GPALObjectType.GoogleSheets);
                    return this;
                }

                string deploymentId = deploymentIdObj.ToString();
                var updatePayload = new
                {
                    deploymentConfig = new
                    {
                        scriptId = AppsScriptProjectId,
                        versionNumber = VersionNumber,
                        manifestFileName = "appsscript",
                        description = DeploymentDescription,
                        webApp = new { executeAs = executeAsString, whoHasAccess = accessString }
                    }
                };

                var updateResponse = ScriptsRESTClient
                    .WithEndpoint($"/v1/projects/{AppsScriptProjectId}/deployments/{deploymentId}")
                    .WithParameters(updatePayload)
                    .WithHttpMethod("PUT")
                    .WithHeader(googleSheetsConfig.AuthorizationHeader, $"{googleSheetsConfig.Bearer} {((Credentials)Credentials)?.AccessToken ?? ""}")
                    .Execute();

                if (updateResponse == null)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to update deployment", this, GPALObjectType.GoogleSheets);
                    return this;
                }

                var updateData = JsonSerializer.Deserialize<Dictionary<string, object>>(updateResponse);
                object entryPointsObj = null;
                if (updateData.TryGetValue("entryPoints", out entryPointsObj))
                {
                    var entryPoints = JsonSerializer.Deserialize<JsonElement>(entryPointsObj.ToString());
                    foreach (var entry in entryPoints.EnumerateArray())
                    {
                        if (entry.TryGetProperty("webApp", out var webApp) && webApp.TryGetProperty("url", out var url))
                        {
                            webAppUrl = url.GetString();
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Web app deployed at: [{webAppUrl}]", this, GPALObjectType.GoogleSheets);
                            return this;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Deployment error", this, GPALObjectType.GoogleSheets, ex);
            }

            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to retrieve web app URL", this, GPALObjectType.GoogleSheets);
            return this;
        }

        public IAllowSheetSelection SaveTo(GPALFile gPALFile)
        {
            if (gPALFile == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "GPAL file is null", this, GPALObjectType.GoogleSheets);
                return this;
            }

            if (ReadData == null)
            {
                if (!string.IsNullOrEmpty(ReadRange))
                {
                    if (!ReadDataFromRange())
                    {
                        return this;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No data to save; ReadData is null and no range specified", this, GPALObjectType.GoogleSheets);
                    return this;
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{ReadData.Rows}] rows of sheet [{SheetName}] to [{gPALFile.Filename}].", this, GPALObjectType.GoogleSheets);

            try
            {
                FileHelper.EnsureFolderFor(gPALFile.Filename);
                using (var writer = new StreamWriter(gPALFile.Filename))
                {
                    foreach (var row in ReadData)
                    {
                        writer.WriteLine(string.Join(",", row));
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to save to file", this, GPALObjectType.GoogleSheets, ex);
            }

            return this;
        }

        public IGoogleSheets ToGPALObject()
        {
            return this;
        }
    }

    internal class ValueRange
    {
        public string range { get; set; }
        public string majorDimension { get; set; }
        public List<List<object>> values { get; set; }
    }

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
