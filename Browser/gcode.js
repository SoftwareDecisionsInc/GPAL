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

function doPost(e) {
  try {
    const request = JSON.parse(e.postData.contents);
    const action = request.action;
    const params = request.parameters;
    let response = { success: true };

    switch (action) {
      case 'WithSpreadsheet':
        response = openOrCreateSpreadsheet(params.spreadsheetIdOrTitle);
        break;
      case 'CreateSheet':
        response = createSheet(params.spreadsheetId, params.sheetName);
        break;
      case 'WithSheet':
        response = activateSheet(params.spreadsheetId, params.sheetName);
        break;
      case 'WithSheetById':
        response = activateSheetById(params.spreadsheetId, params.sheetId);
        break;
      case 'AppendToCell':
        response = appendToCell(params.spreadsheetId, params.sheetName, params.cell, params.data, params.separator);
        break;
      case 'PrependToCell':
        response = prependToCell(params.spreadsheetId, params.sheetName, params.cell, params.data, params.separator);
        break;
      case 'InsertAtCell':
        response = insertAtCell(params.spreadsheetId, params.sheetName, params.cell, params.data, params.separator, params.position);
        break;
      case 'WriteRange':
        response = writeRange(params.spreadsheetId, params.sheetName, params.range, params.data);
        break;
      case 'ReadRange':
        response = readRange(params.spreadsheetId, params.sheetName, params.range);
        break;
      case 'FormatRange':
        response = formatRange(params.spreadsheetId, params.sheetName, params.range, params.formatType, params.formatValue, params.numberFormatType, params.numberFormatPattern);
        break;
      case 'DeleteSheet':
        response = deleteSheet(params.spreadsheetId, params.sheetName);
        break;
      case 'DeleteSheetById':
        response = deleteSheetById(params.spreadsheetId, params.sheetId);
        break;
      case 'ListSheets':
        response = listSheets(params.spreadsheetId);
        break;
      case 'WithTitle':
        response = setSheetTitle(params.spreadsheetId, params.sheetName, params.title);
        break;
      case 'CalculateSum':
        response = calculateSum(params.data);
        break;
      case 'CalculateCount':
        response = calculateCount(params.data);
        break;
      case 'AppendToSheet':
        response = appendToSheet(params.spreadsheetId, params.sheetNameOrId, params.data);
        break;
      default:
        throw new Error('Unknown action: ' + action);
    }

    return ContentService.createTextOutput(JSON.stringify(response))
      .setMimeType(ContentService.MimeType.JSON);
  } catch (error) {
    Logger.log('Error: ' + error.message);
    return ContentService.createTextOutput(
      JSON.stringify({ success: false, error: error.message })
    ).setMimeType(ContentService.MimeType.JSON);
  }
}

function openOrCreateSpreadsheet(spreadsheetIdOrTitle) {
  if (!spreadsheetIdOrTitle) throw new Error('Spreadsheet ID or title cannot be empty');
  try {
    let ss = SpreadsheetApp.openById(spreadsheetIdOrTitle);
    return { success: true, spreadsheetId: ss.getId() };
  } catch (e) {
    let ss = SpreadsheetApp.create(spreadsheetIdOrTitle);
    return { success: true, spreadsheetId: ss.getId() };
  }
}

function createSheet(spreadsheetId, sheetName) {
  if (!spreadsheetId || !sheetName) throw new Error('Spreadsheet ID or sheet name cannot be empty');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) {
    sheet = ss.insertSheet(sheetName);
  }
  return { success: true, sheetName: sheet.getName() };
}

function activateSheet(spreadsheetId, sheetName) {
  if (!spreadsheetId || !sheetName) throw new Error('Spreadsheet ID or sheet name cannot be empty');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) {
    sheet = ss.insertSheet(sheetName);
  }
  ss.setActiveSheet(sheet);
  return { success: true, sheetName: sheet.getName() };
}

function activateSheetById(spreadsheetId, sheetId) {
  if (!spreadsheetId || !sheetId) throw new Error('Spreadsheet ID or sheet ID cannot be empty');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheets().find(s => s.getSheetId() === parseInt(sheetId));
  if (!sheet) throw new Error('Sheet with ID ' + sheetId + ' not found');
  ss.setActiveSheet(sheet);
  return { success: true, sheetName: sheet.getName() };
}

function appendToCell(spreadsheetId, sheetName, cell, data, separator) {
  if (!spreadsheetId || !sheetName || !cell || !data) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  let rangeObj = sheet.getRange(cell);
  let currentContent = rangeObj.getValue() || '';
  let appendData = data.flat().join(separator || '');
  rangeObj.setValue(currentContent + appendData);
  return { success: true };
}

function prependToCell(spreadsheetId, sheetName, cell, data, separator) {
  if (!spreadsheetId || !sheetName || !cell || !data) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  let rangeObj = sheet.getRange(cell);
  let currentContent = rangeObj.getValue() || '';
  let prependData = data.flat().join(separator || '');
  rangeObj.setValue(prependData + currentContent);
  return { success: true };
}

function insertAtCell(spreadsheetId, sheetName, cell, data, separator, position) {
  if (!spreadsheetId || !sheetName || !cell || !data) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  let rangeObj = sheet.getRange(cell);
  let currentContent = rangeObj.getValue() || '';
  let insertData = data.flat().join(separator || '');
  position = parseInt(position) || 0;
  let newContent = currentContent.substring(0, position) + insertData + currentContent.substring(position);
  rangeObj.setValue(newContent);
  return { success: true };
}

function writeRange(spreadsheetId, sheetName, range, data) {
  if (!spreadsheetId || !sheetName || !range || !data) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  let rangeObj = sheet.getRange(range);
  rangeObj.setValues(data);
  return { success: true };
}

function readRange(spreadsheetId, sheetName, range) {
  if (!spreadsheetId || !sheetName || !range) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  let rangeObj = sheet.getRange(range);
  let data = rangeObj.getValues();
  return { success: true, data: data };
}

function formatRange(spreadsheetId, sheetName, range, formatType, formatValue, numberFormatType, numberFormatPattern) {
  if (!spreadsheetId || !sheetName || !range || !formatType) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  let rangeObj = sheet.getRange(range);

  switch (formatType) {
    case 'Bold':
      rangeObj.setFontWeight(formatValue ? 'bold' : 'normal');
      break;
    case 'Italic':
      rangeObj.setFontStyle(formatValue ? 'italic' : 'normal');
      break;
    case 'Underline':
      rangeObj.setFontLine(formatValue ? 'underline' : 'none');
      break;
    case 'Strikethrough':
      rangeObj.setFontLine(formatValue ? 'line-through' : 'none');
      break;
    case 'FontSize':
      rangeObj.setFontSize(parseInt(formatValue) || 10);
      break;
    case 'FontFamily':
      rangeObj.setFontFamily(formatValue || 'Arial');
      break;
    case 'ForegroundColor':
      rangeObj.setFontColor(formatValue);
      break;
    case 'CellBackgroundColor':
      rangeObj.setBackground(formatValue);
      break;
    case 'NumberFormat':
      rangeObj.setNumberFormat(numberFormatPattern || '0.00');
      break;
    case 'HorizontalAlignment':
      rangeObj.setHorizontalAlignment(formatValue || 'left');
      break;
    case 'VerticalAlignment':
      rangeObj.setVerticalAlignment(formatValue || 'top');
      break;
    case 'TextRotation':
      let degrees = 0;
      switch (formatValue) {
        case 'TiltUp30': degrees = 30; break;
        case 'TiltUp45': degrees = 45; break;
        case 'TiltDown30': degrees = -30; break;
        case 'TiltDown45': degrees = -45; break;
        case 'UpsideDown': degrees = -180; break;
        case 'Vertical': rangeObj.setVerticalText(true); break;
      }
      if (!rangeObj.getVerticalText()) rangeObj.setTextRotation(degrees);
      break;
    default:
      throw new Error('Unsupported format type: ' + formatType);
  }
  return { success: true };
}

function deleteSheet(spreadsheetId, sheetName) {
  if (!spreadsheetId || !sheetName) throw new Error('Spreadsheet ID or sheet name cannot be empty');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  ss.deleteSheet(sheet);
  return { success: true };
}

function deleteSheetById(spreadsheetId, sheetId) {
  if (!spreadsheetId || !sheetId) throw new Error('Spreadsheet ID or sheet ID cannot be empty');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheets().find(s => s.getSheetId() === parseInt(sheetId));
  if (!sheet) throw new Error('Sheet with ID ' + sheetId + ' not found');
  ss.deleteSheet(sheet);
  return { success: true };
}

function listSheets(spreadsheetId) {
  if (!spreadsheetId) throw new Error('Spreadsheet ID cannot be empty');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheets = ss.getSheets().map(s => ({ name: s.getName(), sheetId: s.getSheetId() }));
  return { success: true, sheets: sheets };
}

function setSheetTitle(spreadsheetId, sheetName, title) {
  if (!spreadsheetId || !sheetName || !title) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet = ss.getSheetByName(sheetName);
  if (!sheet) throw new Error('Sheet ' + sheetName + ' not found');
  sheet.setName(title);
  return { success: true };
}

function calculateSum(data) {
  if (!data) throw new Error('Data cannot be empty');
  let sum = data.flat().filter(v => !isNaN(parseFloat(v))).reduce((acc, v) => acc + parseFloat(v), 0);
  return { success: true, result: sum.toString() };
}

function calculateCount(data) {
  if (!data) throw new Error('Data cannot be empty');
  let count = data.flat().length;
  return { success: true, result: count.toString() };
}

function appendToSheet(spreadsheetId, sheetNameOrId, data) {
  if (!spreadsheetId || !sheetNameOrId || !data) throw new Error('Required parameters missing');
  let ss = SpreadsheetApp.openById(spreadsheetId);
  let sheet;
  if (typeof sheetNameOrId === 'string' && !isNaN(parseInt(sheetNameOrId))) {
    sheet = ss.getSheets().find(s => s.getSheetId() === parseInt(sheetNameOrId));
  } else {
    sheet = ss.getSheetByName(sheetNameOrId);
  }
  if (!sheet) throw new Error('Sheet ' + sheetNameOrId + ' not found');
  sheet.getDataRange().offset(sheet.getLastRow(), 0, data.length, data[0].length).setValues(data);
  return { success: true };
}
