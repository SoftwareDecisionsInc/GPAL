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
        public IAllowPuppeteerEndpointDetails WithSaveFile(GPALFile file)
        {
            _saveFile = file?.Filename;
            return this;
        }

        /// <summary>
        /// Sets the display name for this client.
        /// </summary>
        /// <param name="name">The name to assign</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerEndpointDetails WithName(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets the url parameter for the request.
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithUrl(string url)
        {
            SetParameter("url", url);
            return this;
        }

        /// <summary>
        /// Sets the image format parameter for the request (e.g. for CaptureVisibleTab).
        /// </summary>
        /// <param name="imageFormat">The image format to request</param>
        /// <returns>Fluent interface to continue building the request</returns>
        internal IAllowPuppeteerParametersOrExecution WithImageFormat(ImageFormat imageFormat)
        {
            SetParameter("imageFormat", imageFormat.ToString());
            return this;
        }

        /// <summary>
        /// Sets the url parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithUrlFromResult(int resultIndex)
        {
            var url = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the url parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithUrlFromResult(string name)
        {
            var url = GetResultFromName(name)?.ToString() ?? "";
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the tab id parameter for the request.
        /// </summary>
        /// <param name="tabId">The tab id</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithTabId(int tabId)
        {
            SetParameter("tabId", tabId);
            return this;
        }

        /// <summary>
        /// Sets the tab id parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithTabIdFromResult(int resultIndex)
        {
            var tabId = GetResultFromIndex(resultIndex) is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the tab id parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithTabIdFromResult(string name)
        {
            var tabId = GetResultFromName(name) is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the window id parameter for the request.
        /// </summary>
        /// <param name="windowId">The window id</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithWindowId(int windowId)
        {
            SetParameter("windowId", windowId);
            return this;
        }

        /// <summary>
        /// Sets the window id parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithWindowIdFromResult(int resultIndex)
        {
            var windowId = GetResultFromIndex(resultIndex) is int i ? i : 0;
            return WithWindowId(windowId);
        }

        /// <summary>
        /// Sets the window id parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithWindowIdFromResult(string name)
        {
            var windowId = GetResultFromName(name) is int i ? i : 0;
            return WithWindowId(windowId);
        }

        /// <summary>
        /// Sets the storage domain parameter for storage operations.
        /// </summary>
        /// <param name="domain">The domain to scope the storage operation to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageDomain(string domain)
        {
            SetParameter("domain", domain);
            return this;
        }
        /// <summary>
        /// Sets the storage path parameter for storage operations.
        /// </summary>
        /// <param name="path">The cookie/storage path to scope the operation to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStoragePath(string path)
        {
            SetParameter("path", path);
            return this;
        }
        /// <summary>
        /// Sets the storage key parameter for storage operations.
        /// </summary>
        /// <param name="key">The storage key</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageKey(string key)
        {
            SetParameter("key", key);
            return this;
        }
        /// <summary>
        /// Sets the storage store name parameter for IndexedDB storage operations.
        /// </summary>
        /// <param name="storeName">The object store name</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageStoreName(string storeName)
        {
            SetParameter("storeName", storeName);
            return this;
        }
        /// <summary>
        /// Sets the storage data parameter for storage operations.
        /// </summary>
        /// <param name="data">The data to store</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithStorageData(string data)
        {
            SetParameter("data", data);
            return this;
        }
        /// <summary>
        /// Sets whether storage should be deleted across all origins, not just the current one.
        /// </summary>
        /// <param name="trueOrFalse">True to delete storage across all origins</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerStorageOptions WithDeleteAcrossOrigins(bool trueOrFalse)
        {
            SetParameter("crossOrigin", trueOrFalse);
            return this;
        }
        /// <summary>
        /// Sets the key parameter for the request.
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithKey(string key)
        {
            SetParameter("key", key);
            return this;
        }

        /// <summary>
        /// Sets the key parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithKeyFromResult(int resultIndex)
        {
            var key = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithKey(key);
        }

        /// <summary>
        /// Sets the key parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithKeyFromResult(string name)
        {
            var key = GetResultFromName(name)?.ToString() ?? "";
            return WithKey(key);
        }

        /// <summary>
        /// Sets the css selector parameter for the request.
        /// </summary>
        /// <param name="css">CSS selector</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithCss(string css)
        {
            SetParameter("css", css);
            return this;
        }

        /// <summary>
        /// Sets the css selector parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithCssFromResult(int resultIndex)
        {
            var css = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithCss(css);
        }

        /// <summary>
        /// Sets the css selector parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithCssFromResult(string name)
        {
            var css = GetResultFromName(name)?.ToString() ?? "";
            return WithCss(css);
        }

        /// <summary>
        /// Sets the gpalElements parameter, identifying the elements the request should act on.
        /// </summary>
        /// <param name="gpalElements">The elements to act on</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElements(List<GPALElement> gpalElements)
        {
            SetParameter("gpalElements", gpalElements);
            return this;
        }

        /// <summary>
        /// Sets the elementId parameter from the result at the given index of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementFromResult(int resultIndex)
        {
            var elementId = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the elementId parameter from the named result of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementFromResult(string name)
        {
            var elementId = GetResultFromName(name)?.ToString() ?? "";
            return WithElementId(elementId);
        }
        /// <summary>
        /// Sets the iteration for an endpoint to retrieve from the results when using WithXXXFromResults
        /// </summary>
        /// <param name="iteration"></param>
        /// <returns></returns>
        public IAllowPuppeteerParametersOrExecution WithIteration(int iteration)
        {
            _currentIteration = iteration > 0 ? iteration : 1;
            return this;
        }
        /// <summary>
        /// Sets the xpath parameter for the request.
        /// </summary>
        /// <param name="xpath">XPath expression</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithXPath(string xpath)
        {
            SetParameter("xpath", xpath);
            return this;
        }

        /// <summary>
        /// Sets the xpath parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithXPathFromResult(int resultIndex)
        {
            var xpath = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets the xpath parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithXPathFromResult(string name)
        {
            var xpath = GetResultFromName(name)?.ToString() ?? "";
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets the pixels parameter for the request.
        /// </summary>
        /// <param name="pixels">Number of pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithPixels(int pixels)
        {
            SetParameter("pixels", pixels);
            return this;
        }

        /// <summary>
        /// Sets the pixels parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithPixelsFromResult(int resultIndex)
        {
            var pixels = GetResultFromIndex(resultIndex) is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the pixels parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithPixelsFromResult(string name)
        {
            var pixels = GetResultFromName(name) is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the referrer parameter for the request.
        /// </summary>
        /// <param name="referrer">The referrer URL</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithReferrer(string referrer)
        {
            // the name the endpoint rules check for, shared with the REST client
            SetParameter("url", referrer);
            return this;
        }

        /// <summary>
        /// Sets the referrer parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithReferrerFromResult(int resultIndex)
        {
            var referrer = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the referrer parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithReferrerFromResult(string name)
        {
            var referrer = GetResultFromName(name)?.ToString() ?? "";
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the text parameter for the request.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithText(string text)
        {
            _text = text;
            SetParameter("text", text);
            return this;
        }

        /// <summary>
        /// Sets the text parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithTextFromResult(int resultIndex)
        {
            var text = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithText(text);
        }

        /// <summary>
        /// Sets the text parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithTextFromResult(string name)
        {
            var text = GetResultFromName(name)?.ToString() ?? "";
            return WithText(text);
        }
        /// <summary>
        /// Sets the elementId parameter for the request, resolving CSS selectors to an element handle if needed.
        /// </summary>
        /// <param name="selector">An element id/object id, or a CSS selector to resolve</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementId(string selector)
        {
            bool isObjectId = System.Text.RegularExpressions.Regex.IsMatch(selector, @"^-?\d+\.\d+\.\d+$");

            if (true/* == isObjectId*/)
            {
                SetParameter("elementId", selector);
            }
            //else // we have to switch to css when interfacing with gpal, so insert it as it was called
            //{
            //    List<GPALElement> queryResponse = GetElements(selector);
            //    SetParameter("elementId", queryResponse?[0]?.ElementHandle);
            //}
            return this;
        }

        /// <summary>
        /// Sets the elementId parameter from the result at the given index of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementIdFromResult(int resultIndex)
        {
            var elementId = GetResultFromIndex(resultIndex)?.ToString() ?? "";
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the elementId parameter from the named result of a previous execution, then calls WithElementId.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithElementIdFromResult(string name)
        {
            var elementId = GetResultFromName(name)?.ToString() ?? "";
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the horizontal pixels parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithHPixelsFromResult(int resultIndex)
        {
            _hPixels = GetResultFromIndex(resultIndex) is int i ? i : 0;
            SetParameter("hPixels", _hPixels);
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithHPixelsFromResult(string name)
        {
            _hPixels = GetResultFromName(name) is int i ? i : 0;
            SetParameter("hPixels", _hPixels);
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels parameter from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithVPixelsFromResult(int resultIndex)
        {
            _vPixels = GetResultFromIndex(resultIndex) is int i ? i : 0;
            SetParameter("vPixels", _vPixels);
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels parameter from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithVPixelsFromResult(string name)
        {
            _vPixels = GetResultFromName(name) is int i ? i : 0;
            SetParameter("vPixels", _vPixels);
            return this;
        }

        /// <summary>
        /// Sets an extra HTTP header to send with the request.
        /// </summary>
        /// <param name="name">Header name</param>
        /// <param name="value">Header value</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithHeader(string name, string value)
        {
            _headers[name] = value;
            return this;
        }

        /// <summary>
        /// Sets the request parameters as an ordinal array.
        /// </summary>
        /// <param name="parameters">Ordinal parameter values</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParameters(params object[] parameters)
        {
            SetParameter("parameters", parameters); // ordinal array
            return this;
        }

        /// <summary>
        /// Sets the request parameters from an object with named fields.
        /// </summary>
        /// <param name="parameters">Object whose properties are the named parameters</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParameters(object parameters)
        {
            SetParameter("parameters", parameters); // object with named fields
            return this;
        }

        /// <summary>
        /// Sets the request parameters from the result at the given index of a previous execution.
        /// </summary>
        /// <param name="resultIndex">Index into the execution results</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParametersFromResult(int resultIndex)
        {
            SetParameter("parameters", GetResultFromIndex(resultIndex));
            return this;
        }

        /// <summary>
        /// Sets the request parameters from the named result of a previous execution.
        /// </summary>
        /// <param name="name">Name of a previous execution result</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithParametersFromResult(string name)
        {
            SetParameter("parameters", GetResultFromName(name));
            return this;
        }

        /// <summary>
        /// Sets the content encoding used for requests sent to the puppeteer communicator.
        /// </summary>
        /// <param name="encoding">The content encoding to use</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithEncoding(ContentEncoding encoding)
        {
            _contentEncoding = encoding;
            return this;
        }

        /// <summary>
        /// Sets the prune interval, in milliseconds, used by CheckNetworkIdle.
        /// </summary>
        /// <param name="pruneMs">Prune interval in milliseconds</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerCheckNetworkIdleOrExecution WithPruneMs(int pruneMs)
        {
            _pruneMs = pruneMs;
            return this;
        }

        /// <summary>
        /// Sets the maximum number of in-flight connections allowed before the network is considered busy, used by CheckNetworkIdle.
        /// </summary>
        /// <param name="maxConnections">Maximum number of in-flight connections</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerCheckNetworkIdleOrExecution WithMaxConnections(int maxConnections)
        {
            _maxConnections = maxConnections;
            return this;
        }

        /// <summary>
        /// Sets the modifier keys parameter for the request.
        /// </summary>
        /// <param name="modifierKeys">Modifier keys to hold during the action</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithModifiers(ModifierKeys modifierKeys)
        {
            SetParameter("modifiers", GetModifierBitmask(modifierKeys).ToString());
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels parameter for the request.
        /// </summary>
        /// <param name="hPixels">Number of horizontal pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerScrollElement WithHPixels(int hPixels)
        {
            SetParameter("hPixels", hPixels);
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels parameter for the request.
        /// </summary>
        /// <param name="vPixels">Number of vertical pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerScrollElement WithVPixels(int vPixels)
        {
            SetParameter("vPixels", vPixels);
            return this;
        }

        /// <summary>
        /// Sets the horizontal drag delta parameter for a drag and drop.
        /// </summary>
        /// <param name="deltaX">Horizontal drag distance, in pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithDeltaX(int deltaX)
        {
            SetParameter("deltaX", deltaX);
            return this;
        }

        /// <summary>
        /// Sets the vertical drag delta parameter for a drag and drop.
        /// </summary>
        /// <param name="deltaY">Vertical drag distance, in pixels</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithDeltaY(int deltaY)
        {
            SetParameter("deltaY", deltaY);
            return this;
        }

        /// <summary>
        /// Sets the horizontal grab offset within the element for a drag and drop.
        /// </summary>
        /// <param name="offsetX">Horizontal grab offset, in pixels, within the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithOffsetX(int offsetX)
        {
            SetParameter("offsetX", offsetX);
            return this;
        }

        /// <summary>
        /// Sets the vertical grab offset within the element for a drag and drop.
        /// </summary>
        /// <param name="offsetY">Vertical grab offset, in pixels, within the element</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerDragAndDrop WithOffsetY(int offsetY)
        {
            SetParameter("offsetY", offsetY);
            return this;
        }

        /// <summary>
        /// Sets the attribute name parameter for SetAttribute/GetAttribute requests.
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute WithAttribute(string attribute)
        {
            _attribute = attribute;
            SetParameter("attribute", attribute);
            return this;
        }

        /// <summary>
        /// Sets the attribute value parameter for SetAttribute requests.
        /// </summary>
        /// <param name="value">The value to set</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerAttribute WithValue(string value)
        {
            _value = value;
            SetParameter("value", value);
            return this;
        }

        /// <summary>
        /// Sets the destination element id parameter for a drag and drop.
        /// </summary>
        /// <param name="destElementId">Element id of the drop target</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithDestElementId(string destElementId)
        {
            SetParameter("destElementId", destElementId);
            return this;
        }

        /// <summary>
        /// Sets the CSS parameter
        /// </summary>
        /// <param name="sinkName">The device name sent to the server as the sinkName parameter</param>
        /// <returns></returns>
        public IAllowPuppeteerExecution WithDeviceName(string sinkName)
        {
            SetParameter("sinkName", sinkName);
            return this;
        }

        /// <summary>
        /// HTTP method for the request. GET when nothing was said.
        /// </summary>
        /// <param name="method">HTTP method</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithVerb(string method)
        {
            SetParameter("method", method);
            return this;
        }

        /// <summary>
        /// Body to send with the request.
        /// </summary>
        /// <param name="body">Request body</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithBody(string body)
        {
            SetParameter("body", body);
            return this;
        }

        /// <summary>
        /// Content type of the body.
        /// </summary>
        /// <param name="contentType">Content type</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithContentType(string contentType)
        {
            SetParameter("contentType", contentType);
            return this;
        }

        /// <summary>
        /// Extra request headers, flattened to name, value, name, value.
        /// </summary>
        /// <param name="headers">Header names and values in pairs</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithHeaders(string[] headers)
        {
            SetParameter("headers", headers);
            return this;
        }

        /// <summary>
        /// True to bring the response body back base64 encoded, which is what a file needs. Text is the default,
        /// and reading a file as text would put a replacement character wherever a byte is not valid utf-8.
        /// </summary>
        /// <param name="asBytes">True for a base64 encoded body, false for text</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerParametersOrExecution WithBytes(bool asBytes)
        {
            SetParameter("bytes", asBytes);
            return this;
        }
        /// <summary>
        /// Set the value to choose from the Select menu
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public IAllowPuppeteerExecution WithSelectValue(string value)
        {
            SetParameter("selectValue", value);
            return this;
        }
        /// <summary>
        /// Sets the index of the option to choose from a Select menu.
        /// </summary>
        /// <param name="index">Zero-based index of the option to choose</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithSelectIndex(int index)
        {
            SetParameter("selectIndex", index);
            return this;
        }

        /// <summary>
        /// Points the request at the coordinate click endpoint. Shared by the click methods when they are handed
        /// a Point rather than an element, which happens for a target with no DOM handle such as an image match.
        /// </summary>
        /// <param name="point">Viewport coordinate, in CSS pixels</param>
        /// <param name="clickType">Which button, and whether this is a double click</param>
        /// <returns>Fluent interface to continue building the request</returns>
        private IAllowPuppeteerParametersOrExecution WithClickPoint(System.Drawing.Point point, ClickType clickType)
        {
            WithEndpoint(ApiEndpoint.ClickPoint);
            SetParameter("x", point.X);
            SetParameter("y", point.Y);
            SetParameter("clickType", (int)clickType);
            return this;
        }

        /// <summary>
        /// Sets the range value parameter for a SetRange request.
        /// </summary>
        /// <param name="rangeValue">The value to set on the range input</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WithRangeValue(int rangeValue)
        {
            SetParameter("rangeValue", rangeValue.ToString());
            return this;
        }
    }
}
