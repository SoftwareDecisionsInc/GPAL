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
        /// Gets the bounding client rect of an element by ID
        /// <example>client.GetBoundingClientRect("element123").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GetBoundingClientRect(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetBoundingClientRect).WithElementId(elementId);
            return this;
        }
        public IAllowRESTExecution GetContentAndCss(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetContentAndCss);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution GetCssAttributes(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetCssAttributes);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution GetDomAttributes(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetDomAttributes);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution GetDomProperties(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetDomProperties);
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Gets the attribute hash of an element
        /// <example>client.GetElementAttributeHash().Execute&lt;string&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetElementAttributeHash(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetElementAttributeHash).WithElementId(elementId);
            return this;
        }
        public IAllowRESTExecution GetPageSource()
        {
            WithEndpoint(ApiEndpoint.GetPageSource);
            return this;
        }

        /// <summary>
        /// Hides an element by ID
        /// <example>client.HideElement("element123").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution HideElement(string elementId)
        {
            WithEndpoint(ApiEndpoint.HideElement).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is clickable by ID
        /// <example>client.IsClickAble("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsClickable(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsClickable).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is displayed by ID
        /// <example>client.IsDisplayed("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsDisplayed(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsDisplayed).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is enabled by ID
        /// <example>client.IsEnabled("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsEnabled(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsEnabled).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Checks if an element is visible in viewport by ID
        /// <example>client.IsVisibleInViewport("element123").Execute&lt;bool&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTExecution IsVisibleInViewport(string elementId)
        {
            WithEndpoint(ApiEndpoint.IsVisibleInViewport).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Starts getting an attribute of an element by ID
        /// <example>client.GetAttribute("element123").WithAttribute("value").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTAttribute GetAttribute(string elementId)
        {
            WithEndpoint(ApiEndpoint.GetAttribute).WithElementId(elementId);
            return this;
        }

        /// <summary>
        /// Starts setting an attribute of an element by ID
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTAttribute SetAttribute(string elementId)
        {
            WithEndpoint(ApiEndpoint.SetAttribute).WithElementId(elementId);
            return this;
        }
    }
}
