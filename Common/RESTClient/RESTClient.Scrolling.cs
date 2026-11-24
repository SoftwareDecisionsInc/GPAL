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
        /// Checks if the page is at the end
        /// <example>client.IsEndOfPage().Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution IsEndOfPage()
        {
            WithEndpoint(ApiEndpoint.IsEndOfPage);
            return this;
        }

        /// <summary>
        /// Scrolls an element into view by ID
        /// <example>client.ScrollIntoView("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollIntoView(string elementId)
        {
            WithEndpoint(ApiEndpoint.ScrollIntoView).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Scrolls an element by horizontal and vertical pixels
        /// <example>client.ScrollElement(100, 200).Execute();</example>
        /// </summary>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <param name="vPixels">The vertical pixels</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollElement(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollElement).WithHPixels(hPixels).WithVPixels(vPixels);
            return this;
        }

        /// <summary>
        /// Scrolls the page down by pages
        /// <example>client.PageDown(1).Execute();</example>
        /// </summary>
        /// <param name="pagesToScroll">Number of pages to scroll</param>
        /// <returns></returns>
        public IAllowRESTExecution PageDown(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageDown);
            _parameters = null == _parameters
                ? new { pagesToScroll }
                : MergeParameters(_parameters, new { pagesToScroll });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Scrolls to the page end
        /// <example>client.PageEnd().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PageEnd()
        {
            WithEndpoint(ApiEndpoint.PageEnd);
            return this;
        }

        /// <summary>
        /// Scrolls to the page top
        /// <example>client.PageTop().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PageTop()
        {
            WithEndpoint(ApiEndpoint.PageTop);
            return this;
        }

        /// <summary>
        /// Scrolls the page up by pages
        /// <example>client.PageUp(1).Execute();</example>
        /// </summary>
        /// <param name="pagesToScroll">Number of pages to scroll</param>
        /// <returns></returns>
        public IAllowRESTExecution PageUp(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageUp);
            _parameters = null == _parameters
                ? new { pagesToScroll }
                : MergeParameters(_parameters, new { pagesToScroll });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Starts scrolling an element by ID
        /// <example>client.ScrollElement("element123").WithHPixels(100).WithVPixels(200).Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTScrollElement ScrollElement(string elementId)
        {
            WithEndpoint(ApiEndpoint.ScrollElement).WithElementId(elementId);
            return this;
        }

        public IAllowRESTExecution ScrollWindow(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindow).WithHPixels(hPixels).WithVPixels(vPixels);
            return this;
        }
        /// <summary>
        /// Scrolls the window horizontally by pixels
        /// <example>client.ScrollWindowByHorizontal(100).Execute();</example>
        /// </summary>
        /// <param name="hPixels">Pixels to scroll horizontally</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollWindowByHorizontal(int hPixels)
        {
            int vPixels = 0;
            WithEndpoint(ApiEndpoint.ScrollWindow).WithHPixels(hPixels).WithVPixels(vPixels);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Scrolls the window vertically by pixels
        /// <example>client.ScrollWindowByVertical(100).Execute();</example>
        /// </summary>
        /// <param name="vPixels">Pixels to scroll vertically</param>
        /// <returns></returns>
        public IAllowRESTExecution ScrollWindowByVertical(int vPixels)
        {
            int hPixels = 0;
            WithEndpoint(ApiEndpoint.ScrollWindow).WithHPixels(hPixels).WithVPixels(vPixels);
            WithHttpMethod("POST");
            return this;
        }
    }
}
