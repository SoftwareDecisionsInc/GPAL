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
        /// Scrolls an element into view by ID
        /// <example>browser.MagicHelper.ScrollIntoView("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void MoveTo(string elementId)
        {
            Client.ScrollIntoView(elementId).Execute();
            Client.Hover(elementId).Execute();
        }

        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>browser.MagicHelper.LeftClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void LeftClick(string elementId)
        {
            Client.LeftClick(elementId).Execute();
            Thread.Sleep(500);
        }

        /// <summary>
        /// Performs a left double click on an element by ID
        /// <example>browser.MagicHelper.LeftDoubleClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void LeftDoubleClick(string elementId)
        {
            Client.LeftDoubleClick(elementId).Execute();
        }

        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>browser.MagicHelper.LeftClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void MiddleClick(string elementId)
        {
            Client.MiddleClick(elementId).Execute();
            Thread.Sleep(500);
        }

        /// <summary>
        /// Performs a right click on an element by ID
        /// <example>browser.MagicHelper.RightClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void RightClick(string elementId)
        {
            Client.RightClick(elementId).Execute();
        }

        public void SelectClick(string elementId, dynamic indexOrValue)
        {
            if (indexOrValue is int idx)
                Client.SelectClick(elementId).WithSelectIndex(idx).Execute();
            else
                Client.SelectClick(elementId).WithSelectValue(indexOrValue).Execute();
        }

        /// <summary>
        /// Hovers over an element by ID
        /// <example>browser.MagicHelper.Hover("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void Hover(string elementId)
        {
            Client.Hover(elementId).Execute();
        }

        /// <summary>
        /// Drags an element by ID and drops it at an offset from its current position
        /// <example>browser.MagicHelper.DragAndDrop("element123", 100, 50);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="deltaX">The horizontal offset, in pixels, to drop the element</param>
        /// <param name="deltaY">The vertical offset, in pixels, to drop the element</param>
        /// <param name="offsetX">Horizontal grab offset, in pixels, within the element to start the drag from</param>
        /// <param name="offsetY">Vertical grab offset, in pixels, within the element to start the drag from</param>
        public void DragAndDrop(string elementId, int deltaX, int deltaY, int offsetX = 0, int offsetY = 0)
        {
            Client.DragAndDrop(elementId).WithDeltaX(deltaX).WithDeltaY(deltaY).WithOffsetX(offsetX).WithOffsetY(offsetY).Execute();
        }

        /// <summary>
        /// Clicks a viewport point through the browser's own input pipeline, so the page reads the event as trusted.
        /// <example>browser.MagicHelper.TrustedLeftClick(120, 340);</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns>True when the click was made, false where the browser has no debugger to attach</returns>
        public bool TrustedLeftClick(int x, int y, int holdMs = 0)
        {
            return true == Client.TrustedLeftClick(x, y, holdMs).Execute<bool>();
        }

        /// <summary>
        /// Middle clicks a viewport point through the browser's own input pipeline, so the page reads the event as trusted.
        /// <example>browser.MagicHelper.TrustedMiddleClick(120, 340);</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns>True when the click was made, false where the browser has no debugger to attach</returns>
        public bool TrustedMiddleClick(int x, int y, int holdMs = 0)
        {
            return true == Client.TrustedMiddleClick(x, y, holdMs).Execute<bool>();
        }
    }
}
