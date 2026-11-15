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
        /// Gets the shadow root for an elementId
        /// <example>GPALElement root = browser.MagicHelper.GetShadowRoot(elementId);</example>
        /// </summary>
        /// <param name="elementId">The elementId</param>
        /// <returns>The shadow root element</returns>
        public GPALElement GetShadowRoot(string elementId)
        {
            return Client.GetShadowRoot(elementId).Execute<GPALElement>();
        }

        /// <summary>
        /// Switches context to an element
        /// <example>browser.MagicHelper.InElement();</example>
        /// InFrame and InElement call the same endpoint.
        /// </summary>
        public void SwitchToElement(string elementId)
        {
            Client.SwitchToElement(elementId).Execute();
        }

        /// <summary>
        /// Switches context to iframe
        /// <example>browser.MagicHelper.InFrame();</example>
        /// InFrame and InElement call the same endpoint.
        /// </summary>
        public void InFrame(string elementId)
        {
            Client.SwitchToElement(elementId).Execute();
        }

        /// <summary>
        /// Get select menu option items
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public List<GPALElement> GetOptions(string elementId)
        {
            List<GPALElement> tmpElements = Client.GetOptions(elementId).Execute<List<GPALElement>>();
            SwitchToDefaultContent();
            return tmpElements;
        }
        /// <summary>
        /// Gets the parent node of an element by ID
        /// <example>string parent = browser.MagicHelper.GetParentNode("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>The parent node</returns>
        public GPALElement GetParentNode(string elementId)
        {
            return Client.GetParentNode(elementId).Execute<List<GPALElement>>()[0];
        }

        /// <summary>
        /// Gets the bounding client rect of an element by ID
        /// <example>string rect = browser.MagicHelper.GetBoundingClientRect("element123");</example>
        /// </summary>
        /// <returns>The bounding client rect</returns>
        /// <summary>
        /// Whether a click at this viewport point would reach the element.
        /// </summary>
        /// <param name="elementId">The element that should receive the click</param>
        /// <param name="x">Viewport x</param>
        /// <param name="y">Viewport y</param>
        /// <returns>True when the topmost element at this viewport point is the element or a descendant</returns>
        public bool ElementFromPoint(string elementId, int x, int y)
        {
            return true == Client.ElementFromPoint(elementId, x, y).Execute<bool>();
        }


        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>string elementId = browser.MagicHelper.QuerySelector(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>The element ID</returns>
        public GPALElement QuerySelector(string css)
        {
            var elem = Client.QuerySelector(css).Execute<GPALElement>();
            return elem;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>string[] elementIds = browser.MagicHelper.QuerySelectors(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> QuerySelectors(string css)
        {
            return Client.QuerySelectors(css).Execute<List<GPALElement>>();
        }

        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>string elementId = browser.MagicHelper.QuerySelector(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>The element ID</returns>
        public GPALElement QueryPersistentSelector(string css)
        {
            var persist = Client.QueryPersistentSelector(css).Execute<GPALElement>();

            return persist;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>string[] elementIds = browser.MagicHelper.QuerySelectors(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> QueryPersistentSelectors(string css)
        {
            return Client.QueryPersistentSelectors(css).Execute<List<GPALElement>>();
        }


        /// <summary>
        /// Switches to the default content
        /// <example>browser.MagicHelper.SwitchToDefaultContent();</example>
        /// </summary>
        public void SwitchToDefaultContent()
        {
            Client.SwitchToDefaultContent().Execute();
        }

        /// <summary>
        /// Switches to a shadow root by element ID
        /// <example>browser.MagicHelper.SwitchToShadowRoot("shadow123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void SwitchToShadowRoot(string elementId)
        {
            Client.SwitchToShadowRoot(elementId).Execute();
        }
    }
}
