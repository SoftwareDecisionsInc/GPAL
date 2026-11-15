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
        /// Configure this client to scroll the page down by the given number of page heights.
        /// </summary>
        /// <param name="pagesToScroll">Number of page heights to scroll down</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageDown(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageDown);
            SetParameter("pagesToScroll", pagesToScroll);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll to the bottom of the page.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageEnd()
        {
            WithEndpoint(ApiEndpoint.PageEnd);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll to the top of the page.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageTop()
        {
            WithEndpoint(ApiEndpoint.PageTop);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the page up by the given number of page heights.
        /// </summary>
        /// <param name="pagesToScroll">Number of page heights to scroll up</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PageUp(int pagesToScroll = 1)
        {
            WithEndpoint(ApiEndpoint.PageUp);
            SetParameter("pagesToScroll", pagesToScroll);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the browser window horizontally by the given number of pixels.
        /// </summary>
        /// <param name="pixels">Number of pixels to scroll horizontally</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollWindowByHorizontal(int pixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindowByHorizontal);
            SetParameter("hPixels", pixels);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the browser window vertically by the given number of pixels.
        /// </summary>
        /// <param name="pixels">Number of pixels to scroll vertically</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollWindowByVertical(int pixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindowByVertical);
            SetParameter("vPixels", pixels);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the page is scrolled to its end.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsEndOfPage()
        {
            WithEndpoint(ApiEndpoint.IsEndOfPage);
            return this;
        }
        /// <summary>
        /// Configure this client to scroll the given element into view.
        /// </summary>
        /// <param name="elementOrelementId">A GPALElement, list of GPALElements, or element id identifying the target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollIntoView(dynamic elementOrelementId)
        {
            WithEndpoint(ApiEndpoint.ScrollIntoView);

            if (elementOrelementId is GPALElement element)
                WithElements(new List<GPALElement>() { element });
            else if (elementOrelementId is List<GPALElement> elements)
                WithElements(elements);
            else
                WithElementId(elementOrelementId);
            
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the browser window by the given horizontal and vertical pixel amounts.
        /// </summary>
        /// <param name="hPixels">Horizontal pixels to scroll</param>
        /// <param name="vPixels">Vertical pixels to scroll</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollWindow(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollWindow);
            WithHPixels(hPixels);
            WithVPixels(vPixels);
            return this;
        }
        /// <summary>
        /// Configure this client to scroll the given element; combine with WithHPixels/WithVPixels to specify the scroll amount.
        /// </summary>
        /// <param name="selector">CSS selector of the element to scroll</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerScrollElement ScrollElement(string selector)
        {
            WithEndpoint(ApiEndpoint.ScrollElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to scroll the previously specified element by the given horizontal and vertical pixel amounts.
        /// </summary>
        /// <param name="hPixels">Horizontal pixels to scroll</param>
        /// <param name="vPixels">Vertical pixels to scroll</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution ScrollElement(int hPixels, int vPixels)
        {
            WithEndpoint(ApiEndpoint.ScrollElement);
            _hPixels = hPixels;
            _vPixels = vPixels;
            return this;
        }
    }
}
