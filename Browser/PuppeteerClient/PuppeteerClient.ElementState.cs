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
        /// Configure this client to retrieve the bounding client rectangle of the given element.
        /// </summary>
        /// <param name="backendNodeId">Backend node id of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetBoundingClientRect(string backendNodeId)
        {
            WithEndpoint(ApiEndpoint.GetBoundingClientRect);
            WithElementId(backendNodeId);
            return this;
        }

        /// <summary>
        /// Configure this client to compute a hash of the given element's attributes, useful for detecting element changes.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetElementAttributeHash(string selector)
        {
            WithEndpoint(ApiEndpoint.GetElementAttributeHash);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the current page's HTML source.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetPageSource()
        {
            WithEndpoint(ApiEndpoint.GetPageSource);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the text content and computed CSS of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetContentAndCss(string selector)
        {
            WithEndpoint(ApiEndpoint.GetContentAndCss);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the CSS attributes of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetCssAttributes(string selector)
        {
            WithEndpoint(ApiEndpoint.GetCssAttributes);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the DOM attributes of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetDomAttributes(string selector)
        {
            WithEndpoint(ApiEndpoint.GetDomAttributes);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the DOM properties of the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetDomProperties(string selector)
        {
            WithEndpoint(ApiEndpoint.GetDomProperties);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to hide the given element.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution HideElement(string selector)
        {
            WithEndpoint(ApiEndpoint.HideElement);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is clickable.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsClickable(string selector)
        {
            WithEndpoint(ApiEndpoint.IsClickable);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is displayed.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsDisplayed(string selector)
        {
            WithEndpoint(ApiEndpoint.IsDisplayed);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is enabled.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsEnabled(string selector)
        {
            WithEndpoint(ApiEndpoint.IsEnabled);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the given element is visible in the viewport.
        /// </summary>
        /// <param name="element">The element to check</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsVisibleInViewport(IGPALElement element)
        {
            WithEndpoint(ApiEndpoint.IsVisibleInViewport);
           SetParameter("gpalElements", new List<GPALElement>() { (GPALElement)element });
            return this;
        }

        /// <summary>
        /// Configure this client to check whether the element with the given backend node id is visible in the viewport.
        /// </summary>
        /// <param name="backendNodeId">Backend node id of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution IsVisibleInViewport(string backendNodeId)
        {
            WithEndpoint(ApiEndpoint.IsVisibleInViewport);
            WithElementId(backendNodeId);
            return this;
        }

        /// <summary>
        /// Configure this client to set an attribute on the given element; combine with WithAttribute and WithValue to specify the attribute and value.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute SetAttribute(string selector)
        {
            WithEndpoint(ApiEndpoint.SetAttribute);
            WithElementId(selector);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve an attribute from the given element; combine with WithAttribute to specify which attribute.
        /// </summary>
        /// <param name="selector">CSS selector of the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute GetAttribute(string selector)
        {
            WithEndpoint(ApiEndpoint.GetAttribute);
            WithElementId(selector);
            return this;
        }
    }
}
