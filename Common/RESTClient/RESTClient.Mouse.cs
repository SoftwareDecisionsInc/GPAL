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
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public partial class RESTClient
    {
        /// <summary>
        /// Left-clicks a viewport point through the browser's input pipeline, as a trusted event. NOTOK when the
        /// browser has no debugger to attach (Firefox).
        /// <example>client.TrustedLeftClick(120, 340).Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns></returns>
        public IAllowRESTExecution TrustedLeftClick(int x, int y, int holdMs = 0)
        {
            WithEndpoint(ApiEndpoint.TrustedLeftClick);

            // holdMs is optional on the endpoint, so it only goes out when a hold was asked for
            object point = 0 < holdMs ? (object)new { x, y, holdMs } : new { x, y };

            _parameters = null == _parameters
                ? point
                : MergeParameters(_parameters, point);

            // POST: GPALRestAPI passes no GET parameters to the extension.
            WithHttpMethod("POST");

            return this;
        }

        /// <summary>
        /// Middle-clicks a viewport point through the browser's input pipeline, as a trusted event. NOTOK when the
        /// browser has no debugger to attach (Firefox).
        /// <example>client.TrustedMiddleClick(120, 340).Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns></returns>
        public IAllowRESTExecution TrustedMiddleClick(int x, int y, int holdMs = 0)
        {
            WithEndpoint(ApiEndpoint.TrustedMiddleClick);

            // holdMs is optional on the endpoint, so it only goes out when a hold was asked for
            object point = 0 < holdMs ? (object)new { x, y, holdMs } : new { x, y };

            _parameters = null == _parameters
                ? point
                : MergeParameters(_parameters, point);

            // POST: GPALRestAPI passes no GET parameters to the extension.
            WithHttpMethod("POST");

            return this;
        }

        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>client.LeftClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution LeftClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.LeftClick).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Performs a left double click on an element by ID
        /// <example>client.LeftDoubleClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution LeftDoubleClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.LeftDoubleClick).WithElementId(elementId);
            return this;
        }
        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>client.LeftClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution MiddleClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.MiddleClick).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Performs a right click on an element by ID
        /// <example>client.RightClick("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution RightClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.RightClick).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Hovers over an element by ID
        /// <example>client.Hover("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution Hover(string elementId)
        {
            WithEndpoint(ApiEndpoint.Hover).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Moves to an element
        /// <example>client.MoveTo("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution MoveTo(string elementId)
        {
            WithEndpoint(ApiEndpoint.MoveTo).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Drags an element from its current position by the given offset and drops it
        /// <example>client.DragAndDrop("element123").WithDeltaX(100).WithDeltaY(50).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop DragAndDrop(string elementId)
        {
            WithEndpoint(ApiEndpoint.DragAndDrop).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Click a Select menu option
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public IAllowSelectOptions SelectClick(string elementId)
        {
            WithEndpoint(ApiEndpoint.SelectClick).WithElementId(elementId);
            WithHttpMethod("POST");
            return this;
        }
    }
}
