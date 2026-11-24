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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using GenerallyPositive.Application;
using Microsoft.Win32.SafeHandles;
using OpenQA.Selenium;
using OpenQA.Selenium.DevTools;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class MagicHelper
    {
        /// <summary>
        /// Press the specified modifier keys
        /// </summary>
        /// <param name="modifierKeys"></param>
        public void PressModifierKey(ModifierKeys modifierKeys)
        {
            Client.PressModifierKey(modifierKeys).Execute();
        }

        /// <summary>
        /// Press the specified modifier keys
        /// </summary>
        /// <param name="modifierKeys"></param>
        public void ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            Client.ReleaseModifierKey(modifierKeys).Execute();
        }

        /// <summary>
        /// Focuses an element by ID
        /// <example>browser.MagicHelper.Focus("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void Focus(string elementId)
        {
            Client.Focus(elementId).Execute();
        }

        /// <summary>
        /// Fires a change event on an element by ID
        /// <example>browser.MagicHelper.FireChangeEvent("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void FireChangeEvent(string elementId)
        {
            Client.FireChangeEvent(elementId).Execute();
        }

        public void SetRange(string elementId, int rangeValue)
        {
            Client.SetRange(elementId).WithRangeValue(rangeValue).Execute();
        }

        /// <summary>
        /// Sets the value of a destination element from a source element, by CSS selector
        /// <example>browser.MagicHelper.SetValueFromElement("#source", "#dest");</example>
        /// </summary>
        /// <param name="srcSelector">CSS selector identifying the source element to read the value from</param>
        /// <param name="destElementId">CSS selector identifying the destination element whose value is set</param>
        public void SetValueFromElement(string srcSelector, string destElementId)
        {
            Client.SetValueFrom(destElementId).WithElementId(srcSelector).Execute();
        }

        /// <summary>
        /// Append text to an element
        /// <example>browser.MagicHelper.FillInAppend("input123", "Hello World");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="text">The text to input</param>
        public void FillInAppend(string elementId, string text)
        {
            Client.FillInAppend(elementId).WithText(text).Execute();
        }

        /// <summary>
        /// Inserts text before the current value of an element
        /// <example>browser.MagicHelper.FillInInsert("input123", "Hello World");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="text">The text to input</param>
        public void FillInInsert(string elementId, string text)
        {
            Client.FillInInsert(elementId).WithText(text).Execute();
        }

        /// <summary>
        /// Overwrite the text of an element
        /// <example>browser.MagicHelper.FillInOverwrite("input123", "Hello World");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="text">The text to input</param>
        public void FillInOverwrite(string elementId, string text)
        {
            Client.FillInOverwrite(elementId).WithText(text).Execute();
        }

        /// <summary>
        /// Sends a single key
        /// <example>browser.MagicHelper.SendKey("Enter");</example>
        /// </summary>
        /// <param name="vkcode">The Windows virtual-key code of the key to send</param>
        public void SendKey(byte vkcode)
        {
            Client.SendKey(vkcode).Execute();
        }

        /// <summary>
        /// Sends a string of text
        /// <example>browser.MagicHelper.SendString("Hello World");</example>
        /// </summary>
        /// <param name="text">The text to send</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds, forwarded to OttoMagic as "typingDelay". 0 sends the whole string atomically.</param>
        public void SendString(string text, int delayMs = 0)
        {
            Client.SendString(text, delayMs).Execute();
        }
    }
}
