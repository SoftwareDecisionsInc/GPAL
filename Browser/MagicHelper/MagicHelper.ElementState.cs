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
        /// Gets content and CSS for a elementId
        /// <example>string content = browser.MagicHelper.GetContentAndCss(elementId);</example>
        /// </summary>
        /// <param name="elementId">The elementId</param>
        /// <returns>The content and CSS</returns>
        public string GetContentAndCss(string elementId)
        {
            string retVal = Client.GetContentAndCss(elementId).Execute();

            // The extension returns a JSON string, quoted and escaped. Deserialize it, or the quotes print above and
            // below the page and escaped quotes stay in attribute values.
            if (true == retVal?.StartsWith("\"") && true == retVal.EndsWith("\"") && 1 < retVal.Length)
                retVal = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

            return retVal;
        }
        public Dictionary<string, object> GetCssAttributes(string elementId)
        {
            return Client.GetCssAttributes(elementId).Execute<Dictionary<string, object>>();
        }
        public Dictionary<string, object> GetDomAttributes(string elementId)
        {
            return Client.GetDomAttributes(elementId).Execute<Dictionary<string, object>>();
        }
        public Dictionary<string, object> GetDomProperties(string elementId)
        {
            return Client.GetDomProperties(elementId).Execute< Dictionary<string, object>>();
        }

        /// <summary>
        /// Gets the attribute hash of an element
        /// <example>string hash = browser.MagicHelper.GetElementAttributeHash();</example>
        /// </summary>
        /// <returns>The attribute hash</returns>
        public string GetElementAttributeHash(string elementId)
        {
            return Client.GetElementAttributeHash(elementId).Execute();
        }
        /// <summary>
        /// Get the html source of the current page
        /// </summary>
        /// <returns></returns>
        public string GetPageSource()
        {
            string answered = Client.GetPageSource().Execute();

            // A JSON null is the extension saying it had no page to read, as in a tab still loading. That is no source,
            // not the four characters n-u-l-l.
            string retVal = "null" == answered ? string.Empty : answered;

            return retVal;
        }

        /// <summary>
        /// Gets an attribute of an element by ID
        /// <example>string value = browser.MagicHelper.GetAttribute("element123", "value");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="attribute">The attribute name</param>
        /// <returns>The attribute value</returns>
        public string GetAttribute(string elementId, string attribute)
        {
            return Client.GetAttribute(elementId).WithAttribute(attribute).Execute();
        }

        /// <summary>
        /// Sets an attribute of an element by ID
        /// <example>browser.MagicHelper.SetAttribute("element123", "disabled", "true");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="attribute">The attribute name</param>
        /// <param name="value">The attribute value</param>
        public void SetAttribute(string elementId, string attribute, string value)
        {
            Client.SetAttribute(elementId).WithAttribute(attribute).WithValue(value).Execute();
        }

        /// <summary>
        /// Checks if an element is visible in viewport by ID
        /// <example>bool visible = browser.MagicHelper.IsVisibleInViewport("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Whether the element is visible</returns>
        public bool IsVisibleInViewport(string elementId)
        {
            return Client.IsVisibleInViewport(elementId).Execute<bool>();
        }

        public Rectangle GetBoundingClientRect(string elementId)
        {
            Rectangle rect = new Rectangle(0,0,0,0);
            var result = Client.GetBoundingClientRect(elementId).Execute<Dictionary<string, object>>();
            Dictionary<string, object> obj = null;

            if (null != result)
            {
                obj = (Dictionary<string, object>)result;

                // the extension answers with the element's DOMRect, lower case: x, y, width, height, top, right, bottom, left
                var left = obj.ContainsKey("left") ? Convert.ToInt32(obj["left"]) : 0;
                var top = obj.ContainsKey("top") ? Convert.ToInt32(obj["top"]) : 0;
                var width = obj.ContainsKey("width") ? Convert.ToInt32(obj["width"]) : 0;
                var height = obj.ContainsKey("height") ? Convert.ToInt32(obj["height"]) : 0;

                rect = new Rectangle(left, top, width, height);
            }

            return rect;
        }

        /// <summary>
        /// Hides an element by ID
        /// <example>browser.MagicHelper.HideElement("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void HideElement(string elementId)
        {
            Client.HideElement(elementId).Execute();
        }

        /// <summary>
        /// Checks if an element is clickable by ID
        /// GPAL passes one element, or a selector that finds one; a selector that finds several returns the first result.
        /// <example>bool clickable = browser.MagicHelper.IsClickAble("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Whether the element is clickable</returns>
        public bool IsClickAble(string elementId)
        {
            return Client.IsClickable(elementId).Execute<bool>();
        }
        /// <summary>
        /// Checks if an element is visible, can also just check element.Displayed
        /// GPAL passes one element, or a selector that finds one; a selector that finds several returns the first result.
        /// <example>bool displayed = browser.MagicHelper.IsDisplayed("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Whether the element is clickable</returns>
        public bool IsDisplayed(string elementId)
        {
            return Client.IsDisplayed(elementId).Execute<bool>();
        }
        /// <summary>
        /// Checks if an element is enabled
        /// GPAL passes one element, or a selector that finds one; a selector that finds several returns the first result.
        /// <example>bool enabled = browser.MagicHelper.IsEnabled("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Whether the element is clickable</returns>
        public bool IsEnabled(string elementId)
        {
            return Client.IsEnabled(elementId).Execute<bool>();
        }

        /// <summary>
        /// Calculates a consistent integer hash from a dictionary of attributes.
        /// </summary>
        /// <param name="attributes">A Dictionary&lt;string, object&gt; containing the attributes.</param>
        /// <returns>A 32-bit integer hash.</returns>
        public static int CalculateHash(Dictionary<string, object> attributes)
        {
            // 1. Format the attributes as name=value, sorted by name so the order is always the same.
            var attrList = attributes.OrderBy(kvp => kvp.Key)
                                     .Select(kvp => $"{kvp.Key}={kvp.Value}")
                                     .ToList();

            // 2. Join the sorted attributes into a single string.
            string str = string.Join("|", attrList);

            // 3. Hash it with the same arithmetic as the JavaScript hash.
            int hash = 0;
            foreach (char c in str)
            {
                hash = ((hash << 5) - hash + c) | 0;
            }

            return hash;
        }
    }
}
