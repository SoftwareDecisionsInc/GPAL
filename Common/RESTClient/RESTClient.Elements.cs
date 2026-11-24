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
        /// Checks whether a click at this viewport point would reach the element or hit something covering it.
        /// </summary>
        /// <param name="elementId">The element that should receive the click</param>
        /// <param name="x">Viewport x</param>
        /// <param name="y">Viewport y</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowRESTExecution ElementFromPoint(string elementId, int x, int y)
        {
            WithEndpoint(ApiEndpoint.ElementFromPoint).WithElementId(elementId);

            _parameters = null == _parameters
                ? new { x, y }
                : MergeParameters(_parameters, new { x, y });

            return this;
        }

       /// <summary>
        /// Get the options in a Select manue
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public IAllowRESTExecution GetOptions(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement).WithElementId(elementId).AndThen().WithCss("option");
            return this;
        }
        /// <summary>
        /// Gets the parent node of an element by ID
        /// <example>client.GetParentNode("element123").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GetParentNode(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetParentNode).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>client.QuerySelector(".class").Execute();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QuerySelector(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelector).WithCss(css);
            return this;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>client.QuerySelectors(".class").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QuerySelectors(string css)
        {
            WithEndpoint(ApiEndpoint.QuerySelectors).WithCss(css);
            return this;
        }

        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>client.QuerySelector(".class").Execute();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QueryPersistentSelector(string css)
        {
            WithEndpoint(ApiEndpoint.QueryPersistentSelector).WithCss(css);
            return this;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>client.QuerySelectors(".class").Execute&lt;string[]&gt;();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution QueryPersistentSelectors(string css)
        {
            WithEndpoint(ApiEndpoint.QueryPersistentSelectors).WithCss(css);
            return this;
        }

        /// <summary>
        /// Gets the shadow root for a CSS selector
        /// <example>client.GetShadowRoot(".class").Execute();</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTExecution GetShadowRoot(string css)
        {
            WithEndpoint(ApiEndpoint.GetShadowRoot).WithElementId(css);
            return this;
        }

        /// <summary>
        /// Switches to default content
        /// <example>client.SwitchToDefaultContent().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToDefaultContent()
        {
            WithEndpoint(ApiEndpoint.SwitchToDefaultContent);
            return this;
        }
        /// <summary>
        /// Switches to an element to search for selectors contained
        /// <example>client.SwitchToElement("div123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToElement(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Switches to a frame
        /// <example>client.SwitchToFrame("frame123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToFrame(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToElement);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Switches to a shadow root
        /// <example>client.SwitchToShadowRoot("shadow123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution SwitchToShadowRoot(string elementId)
        {
            WithEndpoint(ApiEndpoint.SwitchToShadowRoot);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
    }
}
