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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Configure this client to move the mouse to the element matching the given selector.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution MoveTo(string selector)
        {
            WithEndpoint(ApiEndpoint.MoveTo);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to left-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, element id, or a viewport Point for a target with no DOM handle</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.LeftClick);

            WithEndpoint(ApiEndpoint.LeftClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            return this;
        }

        /// <summary>
        /// Configure this client to middle-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution MiddleClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.MiddleClick);

            WithEndpoint(ApiEndpoint.MiddleClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);

            return this;
        }

        /// <summary>
        /// Configure this client to double left-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution LeftDoubleClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.LeftDoubleClick);

            WithEndpoint(ApiEndpoint.LeftDoubleClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);
            
            return this;
        }

        /// <summary>
        /// Configure this client to right-click the given element.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution RightClick(dynamic elementOrelementId)
        {
            if (elementOrelementId is System.Drawing.Point point)
                return WithClickPoint(point, ClickType.RightClick);

            WithEndpoint(ApiEndpoint.RightClick);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);
            
            return this;
        }

        /// <summary>
        /// Configure this client to open a select dropdown's options by clicking the element with the given handle; combine with WithSelectValue/WithSelectIndex to choose an option.
        /// </summary>
        /// <param name="elementHandle">Object id of the select element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerSelectOptions SelectClick(string elementHandle)   // objectid
        {
            WithEndpoint(ApiEndpoint.SelectClick);
            WithElementId(elementHandle);

            return this;
        }

        /// <summary>
        /// Configure this client to hover the mouse over the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Hover(string selector)
        {
            WithEndpoint(ApiEndpoint.Hover);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to move the mouse to a viewport coordinate instead of to an element, for targets
        /// with no DOM handle such as an image match.
        /// </summary>
        /// <param name="point">Viewport coordinate, in CSS pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution MoveTo(System.Drawing.Point point)
        {
            WithEndpoint(ApiEndpoint.MoveToPoint);
            SetParameter("x", point.X);
            SetParameter("y", point.Y);
            return this;
        }

        /// <summary>
        /// Configure this client to start a drag from the given element; combine with WithDestElementId or WithDeltaX/WithDeltaY to set the drop target.
        /// </summary>
        /// <param name="selector">CSS selector of the element to drag</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop DragAndDrop(string selector)
        {
            WithEndpoint(ApiEndpoint.DragAndDrop);
            WithElementId(selector);
            return this;
        }
    }
}
