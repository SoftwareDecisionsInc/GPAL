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
        /// Scrolls the page down
        /// <example>browser.MagicHelper.PageDown();</example>
        /// </summary>
        public void PageDown()
        {
            Client.PageDown().Execute();
        }

        /// <summary>
        /// Scrolls to the page end
        /// <example>browser.MagicHelper.PageEnd();</example>
        /// </summary>
        public void PageEnd()
        {
            Client.PageEnd().Execute();
        }

        /// <summary>
        /// Scrolls to the page top
        /// <example>browser.MagicHelper.PageTop();</example>
        /// </summary>
        public void PageTop()
        {
            Client.PageTop().Execute();
        }

        /// <summary>
        /// Scrolls the page up
        /// <example>browser.MagicHelper.PageUp();</example>
        /// </summary>
        public void PageUp()
        {
            Client.PageUp().Execute();
        }

        /// <summary>
        /// Scrolls the window horizontally by pixels
        /// <example>browser.MagicHelper.ScrollWindowByHorizontal(100);</example>
        /// </summary>
        /// <param name="pixels">The horizontal pixels</param>
        public void ScrollWindowHorizontal(int pixels)
        {
            Client.ScrollWindowByHorizontal(pixels).Execute();
        }

        /// <summary>
        /// Scrolls the window vertically by pixels
        /// <example>browser.MagicHelper.ScrollWindowByVertical(100);</example>
        /// </summary>
        /// <param name="pixels">The vertical pixels</param>
        public void ScrollWindowVertical(int pixels)
        {
            Client.ScrollWindowByVertical(pixels).Execute();
        }
        /// <summary>
        /// Scrolls an element into view by ID
        /// <example>browser.MagicHelper.ScrollIntoView("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void ScrollIntoView(string elementId)
        {
            Client.ScrollIntoView(elementId).Execute();
        }

        /// <summary>
        /// Scrolls an element by horizontal and vertical pixels
        /// <example>browser.MagicHelper.ScrollElement(100, 200);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <param name="vPixels">The vertical pixels</param>
        public void ScrollElement(string elementId, int hPixels, int vPixels)
        {
            Client.ScrollElement(elementId).WithHPixels(hPixels).WithVPixels(vPixels).Execute();
        }

        /// <summary>
        /// Checks if the page is at the end
        /// <example>bool end = browser.MagicHelper.IsEndOfPage();</example>
        /// </summary>
        /// <returns>Whether the page is at the end</returns>
        public bool IsEndOfPage()
        {
            return Client.IsEndOfPage().Execute<bool>();
        }
        /// <summary>
        /// Scrolls the window by horizontal and vertical pixels
        /// <example>browser.MagicHelper.ScrollWindow(100, 200);</example>
        /// </summary>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <param name="vPixels">The vertical pixels</param>
        public void ScrollWindow(int hPixels, int vPixels)
        {
            Client.ScrollWindow(hPixels, vPixels).Execute();
        }
    }
}
