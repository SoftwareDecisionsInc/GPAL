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
        /// Sets the URL parameter
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithUrl(string url)
        {
            _parameters = new { url };
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the URL from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithUrlFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithUrlFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithUrl("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var url = null == previousResult ? "" : previousResult.ToString();
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the URL from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithUrlFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithUrlFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithUrl("");
            }
            var previousResult = resultEntry.Result;
            var url = null == previousResult ? "" : previousResult.ToString();
            return WithUrl(url);
        }

        /// <summary>
        /// Sets the tab ID parameter
        /// </summary>
        /// <param name="tabId">The tab ID</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithTabId(int tabId)
        {
            _parameters = new { tabId };
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the tab ID from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithTabIdFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithTabIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithTabId(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var tabId = previousResult is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the tab ID from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithTabIdFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithTabIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithTabId(0);
            }
            var previousResult = resultEntry.Result;
            var tabId = previousResult is int i ? i : 0;
            return WithTabId(tabId);
        }

        /// <summary>
        /// Sets the script parameter
        /// </summary>
        /// <param name="script">The script</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithScript(string script)
        {
            _parameters = new { script, parameters = _parameters };
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the key parameter
        /// </summary>
        /// <param name="key">The key</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithKey(string key)
        {
            _parameters = null == _parameters
                ? new { key }
                : MergeParameters(_parameters, new { key }); 
            
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the key from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithKeyFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithKeyFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithKey("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var key = null == previousResult ? "" : previousResult.ToString();
            return WithKey(key);
        }

        /// <summary>
        /// Sets the key from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithKeyFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithKeyFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithKey("");
            }
            var previousResult = resultEntry.Result;
            var key = null == previousResult ? "" : previousResult.ToString();
            return WithKey(key);
        }

        /// <summary>
        /// Sets the client name
        /// </summary>
        /// <param name="name">The client name</param>
        /// <returns></returns>
        public IAllowRESTEndpointDetails WithName(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets the CSS parameter
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithCss(string css)
        {
            _parameters = null == _parameters
                ? new { css }
                : MergeParameters(_parameters, new { css });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the CSS from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithCssFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithCssFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithCss("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var css = null == previousResult ? "" : previousResult.ToString();
            return WithCss(css);
        }

        /// <summary>
        /// Sets the CSS from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithCssFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithCssFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithCss("");
            }
            var previousResult = resultEntry.Result;
            var css = null == previousResult ? "" : previousResult.ToString();
            return WithCss(css);
        }

        /// <summary>
        /// Sets the element parameter
        /// </summary>
        /// <param name="gPALElement">The element</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElement(IGPALElement gPALElement)
        {
            List<GPALElement> gpalElements = new List<GPALElement>() { (GPALElement)gPALElement };

            _parameters = null == _parameters
                ? new { gpalElements }
                : MergeParameters(_parameters, new { gpalElements });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the element from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElementIdFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithElementFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithElementId("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            string elementId;

            // A list result supplies its first item as the element id
            if (previousResult is IEnumerable<object> enumerable && false == (previousResult is string))
            {
                elementId = null == enumerable.FirstOrDefault() ? "" : enumerable.FirstOrDefault().ToString();
            }
            else
            {
                elementId = null == previousResult ? "" : previousResult.ToString();
            }
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the element from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElementIdFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithElementFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithElementId("");
            }
            var previousResult = resultEntry.Result;
            string elementId;

            // A list result supplies its first item as the element id
            if (previousResult is IEnumerable<object> enumerable && false == (previousResult is string))
            {
                elementId = null == enumerable.FirstOrDefault() ? "" : enumerable.FirstOrDefault().ToString();
            }
            else
            {
                elementId = null == previousResult ? "" : previousResult.ToString();
            }
            return WithElementId(elementId);
        }

        /// <summary>
        /// Sets the prune interval in milliseconds
        /// </summary>
        /// <param name="pruneMs">The prune interval in milliseconds</param>
        /// <returns></returns>
        public IAllowCheckNetworkIdleOrExecution WithPruneMs(int pruneMs)
        {
            _parameters = null == _parameters
                ? new { pruneMs }
                : MergeParameters(_parameters, new { pruneMs });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the maximum connections
        /// </summary>
        /// <param name="maxConnections">The maximum connections</param>
        /// <returns></returns>
        public IAllowCheckNetworkIdleOrExecution WithMaxConnections(int maxConnections)
        {
            _parameters = null == _parameters
                ? new { maxConnections }
                : MergeParameters(_parameters, new { maxConnections });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the file to upload
        /// </summary>
        /// <param name="uploadPath">The path of the file to upload</param>
        /// <returns></returns>
        public IAllowRESTExecution WithUploadFile(string uploadPath)
        {
            _parameters = null == _parameters
                ? new { uploadPath }
                : MergeParameters(_parameters, new { uploadPath });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the files to upload
        /// </summary>
        /// <param name="uploadPaths">The files to upload</param>
        /// <returns></returns>
        public IAllowRESTExecution WithUploadFiles(GPALFile uploadPaths)
        {
            _parameters = null == _parameters
                ? new { uploadPaths }
                : MergeParameters(_parameters, new { uploadPaths });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the XPath parameter
        /// </summary>
        /// <param name="xpath">The XPath</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithXPath(string xpath)
        {
            _parameters = null == _parameters
                ? new { xpath }
                : MergeParameters(_parameters, new { xpath });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the XPath from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithXPathFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithXPathFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithXPath("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var xpath = null == previousResult ? "" : previousResult.ToString();
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets the XPath from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithXPathFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithXPathFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithXPath("");
            }
            var previousResult = resultEntry.Result;
            var xpath = null == previousResult ? "" : previousResult.ToString();
            return WithXPath(xpath);
        }

        /// <summary>
        /// Sets parameters as a variable argument list
        /// </summary>
        /// <param name="parameters">The parameters</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParameters(params object[] parameters)
        {
            _parameters = null == _parameters
                ? parameters
                : MergeParameters(_parameters, parameters);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets parameters as a single object
        /// </summary>
        /// <param name="parameters">The parameters object</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParameters(object parameters)
        {
            _parameters = null == _parameters
                ? parameters
                : MergeParameters(_parameters, parameters);
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets parameters from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParametersFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithParametersFromResult. Using null.",
                    null, GPALObjectType.None);
                _parameters = null;
            }
            else
            {
                _parameters = _executionResults[resultIndex].Result;
            }
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets parameters from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithParametersFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithParametersFromResult. Using null.",
                    null, GPALObjectType.None);
                _parameters = null;
            }
            else
            {
                _parameters = resultEntry.Result;
            }
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the set storage data parameter
        /// </summary>
        /// <param name="data">The data to save in storage</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithData(string data)
        {
            _parameters = null == _parameters
                ? new { data }
                : MergeParameters(_parameters, new { data });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage store name parameter
        /// </summary>
        /// <param name="storeName">The storage store name</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageStoreName(string storeName)
        {
            _parameters = null == _parameters
                ? new { storeName }
                : MergeParameters(_parameters, new { storeName });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage domain parameter
        /// </summary>
        /// <param name="domain">The domain</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageDomain(string domain)
        {
            _parameters = null == _parameters
                ? new { domain }
                : MergeParameters(_parameters, new { domain });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage path parameter
        /// </summary>
        /// <param name="path">The path</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStoragePath(string path)
        {
            _parameters = null == _parameters
                ? new { path }
                : MergeParameters(_parameters, new { path });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the storage type parameter
        /// </summary>
        /// <param name="storageType">The website storage type</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageType(WebsiteStorageType storageType)
        {
            _parameters = null == _parameters
                ? new { storageType = storageType.ToString() }
                : MergeParameters(_parameters, new { storageType = storageType.ToString() });

            WithHttpMethod("POST");
            return this;
        }
        
        /// <summary>
        /// Sets the storage key parameter
        /// </summary>
        /// <param name="key">The path</param>
        /// <returns></returns>
        public IAllowRESTStorageOptions WithStorageKey(string key)
        {
            _parameters = null == _parameters
                ? new { key }
                : MergeParameters(_parameters, new { key });

            WithHttpMethod("POST");
            return this;
        }

        public IAllowRESTStorageOptions WithDeleteAcrossOrigins(bool crossOrigin)
        {
            _parameters = null == _parameters
                ? new { crossOrigin }
                : MergeParameters(_parameters, new { crossOrigin });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the pixels parameter
        /// </summary>
        /// <param name="pixels">The pixels value</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithPixels(int pixels)
        {
            _parameters = null == _parameters
                ? new { pixels }
                : MergeParameters(_parameters, new { pixels });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the pixels from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithPixelsFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithPixels(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var pixels = previousResult is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the pixels from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithPixelsFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithPixels(0);
            }
            var previousResult = resultEntry.Result;
            var pixels = previousResult is int i ? i : 0;
            return WithPixels(pixels);
        }

        /// <summary>
        /// Sets the referrer parameter
        /// </summary>
        /// <param name="url">The referrer URL</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithReferrer(string url)
        {
            _parameters = null == _parameters
                ? new { url }
                : MergeParameters(_parameters, new { url });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the referrer from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithReferrerFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range. Using empty string.",
                    null, GPALObjectType.None);
                return WithReferrer("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var referrer = null == previousResult ? "" : previousResult.ToString();
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the referrer from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithReferrerFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found. Using empty string.",
                    null, GPALObjectType.None);
                return WithReferrer("");
            }
            var previousResult = resultEntry.Result;
            var referrer = null == previousResult ? "" : previousResult.ToString();
            return WithReferrer(referrer);
        }

        /// <summary>
        /// Sets the text parameter
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns></returns>
        public IAllowRESTExecution WithText(string text)
        {
            _parameters = null == _parameters
                ? new { text }
                : MergeParameters(_parameters, new { text });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the text from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTExecution WithTextFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithTextFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithText("");
            }
            var previousResult = _executionResults[resultIndex].Result;
            var text = null == previousResult ? "" : previousResult.ToString();
            return WithText(text);
        }

        /// <summary>
        /// Sets the text from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTExecution WithTextFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithTextFromResult. Using empty string.",
                    null, GPALObjectType.None);
                return WithText("");
            }
            var previousResult = resultEntry.Result;
            var text = null == previousResult ? "" : previousResult.ToString();
            return WithText(text);
        }
        /// <summary>
        /// Sets the HTTP method
        /// </summary>
        /// <param name="method">The HTTP method</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHttpMethod(string method)
        {
            _httpMethod = null == method ? "GET" : method;
            return this;
        }

        /// <summary>
        /// Sets the image format for captureVisibleTab
        /// </summary>
        /// <param name="format">ImageFormat enum value [JPEG (default) or PNG]</param>
        /// <returns></returns>
        internal IAllowRESTParametersOrExecution WithImageFormat(ImageFormat format)
        {
            string imageFormat = format.ToString();
            _parameters = null == _parameters
                ? new { imageFormat }
                : MergeParameters(_parameters, new { imageFormat });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Specifies the option index for select menu operations
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="index">The option index</param>
        /// <returns></returns>
        public IAllowRESTAttribute WithIndex(int index)
        {
            _parameters = null == _parameters
                ? new { index }
                : MergeParameters(_parameters, new { index });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the cast device (sink) name parameter
        /// </summary>
        /// <param name="sinkName">The name of the cast device (sink)</param>
        /// <returns></returns>
        public IAllowRESTExecution WithDeviceName(string sinkName)
        {
            _parameters = null == _parameters
                ? new { sinkName }
                : MergeParameters(_parameters, new { sinkName });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the element ID parameter
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithElementId(string elementId)
        {
            _parameters = null == _parameters
                ? new { elementId }
                : MergeParameters(_parameters, new { elementId });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Specifiy the encoiding type for posted parameters: json or urlencoded
        /// </summary>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithEncoding(ContentEncoding encoding)
        {
            _contentEncoding = encoding;
            return this;
        }
        /// <summary>
        /// Adds an HTTP header to the request
        /// <example>client.WithHeader("Authorization", "Bearer YOUR_ACCESS_TOKEN").Execute();</example>
        /// </summary>
        /// <param name="name">The header name</param>
        /// <param name="value">The header value</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHeader(string name, string value)
        {
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
            {
                _headers[name] = value;
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Invalid name [{name}] or value [{value}], nothing added to the header.",null, GPALObjectType.None);

            return this;
        }

        /// <summary>
        /// Sets how long one attempt can take.
        /// </summary>
        /// <remarks>
        /// Default 60 seconds. InfiniteTimeSpan means no limit. A timeout is not retried.
        /// </remarks>
        /// <example>
        /// <code>
        /// browser.ContinueAsRESTClient(out IRESTClient client);
        ///
        /// string body = client
        ///     .WithTimeout(TimeSpan.FromSeconds(10))
        ///     .WithEndpoint("/api/prices")
        ///     .Execute(false);
        /// </code>
        /// </example>
        /// <param name="httpTimeout">How long one attempt can take</param>
        /// <returns>Fluent interface to continue building the call</returns>
        public IAllowRESTExecution WithTimeout(TimeSpan httpTimeout)
        {
            if (null == _helper)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}No API base to set a timeout on. Call WithAPIBase first, or take a client from ContinueAsRESTClient",
                    null, GPALObjectType.None);
            else
                _helper.HttpTimeout = httpTimeout;

            return this;
        }

        /// <summary>
        /// Sets the header from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHeaderFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] out of range. Using empty values.",
                    null, GPALObjectType.None);
                return WithHeader("", "");
            }
            var result = _executionResults[resultIndex].Result?.ToString().Split(new[] { ':' }, 2);
            var name = result?.Length > 0 ? result[0] : "";
            var value = result?.Length > 1 ? result[1] : "";
            return WithHeader(name, value);
        }

        /// <summary>
        /// Sets the header from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTExecution WithHeaderFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found. Using empty values.",
                    null, GPALObjectType.None);
                return WithHeader("", "");
            }
            var result = resultEntry.Result?.ToString().Split(new[] { ':' }, 2);
            var headerName = result?.Length > 0 ? result[0] : "";
            var headerValue = result?.Length > 1 ? result[1] : "";
            return WithHeader(headerName, headerValue);
        }

        /// <summary>
        /// Sets the horizontal drop offset for a drag-and-drop operation
        /// </summary>
        /// <param name="deltaX">The horizontal offset, in pixels, from the element's current position</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithDeltaX(int deltaX)
        {
            _parameters = null == _parameters
                ? new { deltaX }
                : MergeParameters(_parameters, new { deltaX });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the vertical drop offset for a drag-and-drop operation
        /// </summary>
        /// <param name="deltaY">The vertical offset, in pixels, from the element's current position</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithDeltaY(int deltaY)
        {
            _parameters = null == _parameters
                ? new { deltaY }
                : MergeParameters(_parameters, new { deltaY });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the horizontal grab offset within the element for a drag-and-drop operation
        /// </summary>
        /// <param name="offsetX">Horizontal offset, in pixels, within the element to start the drag from</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithOffsetX(int offsetX)
        {
            _parameters = null == _parameters
                ? new { offsetX }
                : MergeParameters(_parameters, new { offsetX });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the vertical grab offset within the element for a drag-and-drop operation
        /// </summary>
        /// <param name="offsetY">Vertical offset, in pixels, within the element to start the drag from</param>
        /// <returns></returns>
        public IAllowRESTDragAndDrop WithOffsetY(int offsetY)
        {
            _parameters = null == _parameters
                ? new { offsetY }
                : MergeParameters(_parameters, new { offsetY });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels parameter
        /// </summary>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithHPixels(int hPixels)
        {
            _parameters = null == _parameters
                ? new { hPixels }
                : MergeParameters(_parameters, new { hPixels });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the horizontal pixels from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithHPixelsFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithHPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithHPixels(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var hPixels = previousResult is int i ? i : 0;
            return WithHPixels(hPixels);
        }

        /// <summary>
        /// Sets the horizontal pixels from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithHPixelsFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithHPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithHPixels(0);
            }
            var previousResult = resultEntry.Result;
            var hPixels = previousResult is int i ? i : 0;
            return WithHPixels(hPixels);
        }

        /// <summary>
        /// Sets the vertical pixels parameter
        /// </summary>
        /// <param name="vPixels">The vertical pixels</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithVPixels(int vPixels)
        {
            _parameters = null == _parameters
                ? new { vPixels }
                : MergeParameters(_parameters, new { vPixels });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the vertical pixels from a result by index
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithVPixelsFromResult(int resultIndex)
        {
            if (0 > resultIndex || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithVPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithVPixels(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var vPixels = previousResult is int i ? i : 0;
            return WithVPixels(vPixels);
        }

        /// <summary>
        /// Sets the vertical pixels from a result by name
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTScrollElement WithVPixelsFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (null == resultEntry.Result)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithVPixelsFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithVPixels(0);
            }
            var previousResult = resultEntry.Result;
            var vPixels = previousResult is int i ? i : 0;
            return WithVPixels(vPixels);
        }
        public IAllowRESTExecution WithDownloadFile(string downloadPath)
        {
            // Set only the parameter, so the chained endpoint stays and LeftClickAndDownload(id).WithDownloadFile(path)
            // still clicks.
            _parameters = null == _parameters
                ? new { downloadPath }
                : MergeParameters(_parameters, new { downloadPath });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Specifies the attribute name for get or set operation
        /// <example>client.GetAttribute("element123").WithAttribute("value").Execute&lt;string&gt;();</example>
        /// </summary>
        /// <param name="attribute">The attribute name</param>
        /// <returns></returns>
        public IAllowRESTAttribute WithAttribute(string attribute)
        {
            _parameters = null == _parameters
                ? new { attribute }
                : MergeParameters(_parameters, new { attribute });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Specifies the value for SetRange operation
        /// <example>client.SetRange("element123").WithRangeValue(63).Execute();</example>
        /// </summary>
        /// <param name="rangeValue">The range value</param>
        /// <returns></returns>
        public IAllowRESTExecution WithRangeValue(int rangeValue)
        {
            _parameters = null == _parameters
                ? new { rangeValue = rangeValue.ToString() }
                : MergeParameters(_parameters, new { rangeValue = rangeValue.ToString() });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Specifies the value for set operation
        /// <example>client.SetAttribute("element123").WithAttribute("disabled").WithValue("true").Execute();</example>
        /// </summary>
        /// <param name="value">The attribute value</param>
        /// <returns></returns>
        public IAllowRESTAttribute WithValue(string value)
        {
            _parameters = null == _parameters
                ? new { value }
                : MergeParameters(_parameters, new { value });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the HTTP method of the fetched request, not of the REST call (.WithHttpMethod). Default GET.
        /// <example>client.Fetch(url).WithVerb("POST").Execute();</example>
        /// </summary>
        /// <param name="method">HTTP verb for the fetched request</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithVerb(string method)
        {
            _parameters = null == _parameters
                ? new { method }
                : MergeParameters(_parameters, new { method });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the body of the request being fetched.
        /// <example>client.Fetch(url).WithVerb("POST").WithBody(json).Execute();</example>
        /// </summary>
        /// <param name="body">Request body</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithBody(string body)
        {
            _parameters = null == _parameters
                ? new { body }
                : MergeParameters(_parameters, new { body });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets the content type of the body of the request being fetched.
        /// <example>client.Fetch(url).WithContentType("application/json").Execute();</example>
        /// </summary>
        /// <param name="contentType">Content type</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithContentType(string contentType)
        {
            _parameters = null == _parameters
                ? new { contentType }
                : MergeParameters(_parameters, new { contentType });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Sets extra headers for the request being fetched, flattened to name, value, name, value.
        /// <example>client.Fetch(url).WithHeaders(new[] { "Accept", "application/json" }).Execute();</example>
        /// </summary>
        /// <param name="headers">Header names and values in pairs</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithHeaders(string[] headers)
        {
            _parameters = null == _parameters
                ? new { headers }
                : MergeParameters(_parameters, new { headers });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// True to return the body base64 encoded, as a file needs. As text, invalid UTF-8 bytes are replaced.
        /// <example>client.Fetch(url).WithBytes(true).Execute();</example>
        /// </summary>
        /// <param name="bytes">True for a base64 encoded body, false for text</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithBytes(bool bytes)
        {
            _parameters = null == _parameters
                ? new { bytes }
                : MergeParameters(_parameters, new { bytes });
            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Records only calls whose url contains this text.
        /// </summary>
        /// <param name="urlFragment">Record only urls containing this</param>
        /// <returns>Fluent interface to narrow what is recorded, or to execute</returns>
        public IAllowRESTCaptureOptions WithCallFilter(string urlFragment)
        {
            _parameters = null == _parameters
                ? new { filter = urlFragment }
                : MergeParameters(_parameters, new { filter = urlFragment });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Clears earlier recordings before starting.
        /// </summary>
        /// <param name="clear">True to clear first</param>
        /// <returns>Fluent interface to narrow what is recorded, or to execute</returns>
        public IAllowRESTCaptureOptions WithClearCalls(bool clear = true)
        {
            _parameters = null == _parameters
                ? new { clear }
                : MergeParameters(_parameters, new { clear });

            WithHttpMethod("POST");
            return this;
        }

        /// <summary>
        /// Set the value to choose from the Select menu
        /// </summary>
        /// <param name="selectValue">The value of the option to choose</param>
        /// <returns></returns>
        public IAllowRESTExecution WithSelectValue(string selectValue)
        {
            _parameters = null == _parameters
                ? new { selectValue }
                : MergeParameters(_parameters, new { selectValue });
            WithHttpMethod("POST");
            return this;
        }
        public IAllowRESTExecution WithSelectIndex(int selectIndex)
        {
            _parameters = null == _parameters
                ? new { selectIndex }
                : MergeParameters(_parameters, new { selectIndex });
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Sets the window ID parameter
        /// <example>client.CloseWindow(123).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="windowId">The window ID</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithWindowId(int windowId)
        {
            _parameters = new { windowId };
            WithHttpMethod("POST");
            return this;
        }
        /// <summary>
        /// Sets the window ID from a result by index
        /// <example>client.GoToWindow().WithWindowIdFromResult(0).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="resultIndex">The result index</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithWindowIdFromResult(int resultIndex)
        {
            if (resultIndex < 0 || resultIndex >= _executionResults.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result index [{resultIndex}] is out of range for WithWindowIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithWindowId(0);
            }
            var previousResult = _executionResults[resultIndex].Result;
            var windowId = previousResult is int i ? i : (int.TryParse(previousResult?.ToString(), out int parsed) ? parsed : 0);
            return WithWindowId(windowId);
        }
        /// <summary>
        /// Sets the window ID from a result by name
        /// <example>client.GoToWindow().WithWindowIdFromResult("windowResult").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="name">The result name</param>
        /// <returns></returns>
        public IAllowRESTParametersOrExecution WithWindowIdFromResult(string name)
        {
            var resultEntry = _executionResults.LastOrDefault(entry => entry.Name == name);
            if (resultEntry.Result == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"{(string.IsNullOrEmpty(_name) ? "" : $"[{_name}] ")}Result name [{name}] not found for WithWindowIdFromResult. Using 0.",
                    null, GPALObjectType.None);
                return WithWindowId(0);
            }
            var previousResult = resultEntry.Result;
            var windowId = previousResult is int i ? i : (int.TryParse(previousResult?.ToString(), out int parsed) ? parsed : 0);
            return WithWindowId(windowId);
        }
    }
}
