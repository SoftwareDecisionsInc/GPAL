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
        /// Delete the type of storage type specified, if any parameter is missing, then that is a wildcard to delete all, from that domain and down or across domains if omitted
        /// </summary>
        /// <param name="storageType">cookie, localStorage, sessopmStprage. indexedDb, cache</param>
        /// <param name="deleteAcrossOrigins">OttoMagic only: true = delete across origins if no domain supplied</param>
        /// <param name="domain">website domain only</param>
        /// <param name="path">path after the domain</param>
        /// <param name="key">object to delete</param>
        /// <param name="storeName">the storeName for indexedDb</param>
        /// <returns>true or false it worked</returns>
        public bool DeleteStorage(WebsiteStorageType storageType, bool deleteAcrossOrigins = false, string domain = null, string path = null, string key = null, string storeName = null)
        {
            IAllowRESTStorageOptions fluentObject = null;

            fluentObject = Client.DeleteStorage(storageType);

            if (false == string.IsNullOrEmpty(domain))
                fluentObject.WithStorageDomain(domain);

            if (false == string.IsNullOrEmpty(path))
                fluentObject.WithStoragePath(path);

            if (false == string.IsNullOrEmpty(key))
                fluentObject.WithStorageKey(key);

            if (false == string.IsNullOrEmpty(storeName))
                fluentObject.WithStorageStoreName(storeName);

            // delete-storage takes storageType, domain, path, key and storeName. No domain deletes across origins,
            // so deleteAcrossOrigins is not sent; the extension rejects a delete that sends it.
            string deleteResult = fluentObject.Execute();

            return null != deleteResult;
        }

        /// <summary>
        /// Get the type of storage type specified, if any parameter is missing, then that is a wildcard to get all, from that domain and down or across domains if omitted
        /// </summary>
        /// <param name="storageType">cookie, localStorage, sessopmStprage. indexedDb, cache</param>
        /// <param name="domain">website domain only</param>
        /// <param name="path">path after the domain</param>
        /// <param name="key">object to delete</param>
        /// <param name="storeName">IndexedDB object store name, or the cache name for cache storage</param>
        /// <returns>List&lt;StorageAction&gt;</returns>
        public string GetStorage(WebsiteStorageType storageType, string domain = null, string path = null, string key = null, string storeName = null)
        {
            IAllowRESTStorageOptions fluentObject = null;

            fluentObject = Client.GetStorage(storageType);

            if (false == string.IsNullOrEmpty(domain))
                fluentObject.WithStorageDomain(domain);

            if (false == string.IsNullOrEmpty(path))
                fluentObject.WithStoragePath(path);

            if (false == string.IsNullOrEmpty(key))
                fluentObject.WithStorageKey(key);

            if (false == string.IsNullOrEmpty(storeName))
                fluentObject.WithStorageStoreName(storeName);

            return fluentObject.Execute();
        }

        public bool SetStorage(WebsiteStorageType storageType, string data, string domain = null, string path = null, string key = null, string storeName = null)
        {

            IAllowRESTStorageOptions fluentObject = null;

            fluentObject = Client.SetStorage(storageType);

            if (false == string.IsNullOrEmpty(domain))
                fluentObject.WithStorageDomain(domain);

            if (false == string.IsNullOrEmpty(path))
                fluentObject.WithStoragePath(path);

            if (false == string.IsNullOrEmpty(key))
                fluentObject.WithStorageKey(key);

            if (false == string.IsNullOrEmpty(storeName))
                fluentObject.WithStorageStoreName(storeName);

            if (false == string.IsNullOrEmpty(data))
                fluentObject.WithData(data);

            return fluentObject.Execute<bool>();
        }
    }
}
