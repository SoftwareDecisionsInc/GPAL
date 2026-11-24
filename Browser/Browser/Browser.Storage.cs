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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// <summary>
        /// Reads data from browser storage (cookies, local/session storage, IndexedDB) using the domain/path/key
        /// configured by the preceding .WithStorage* calls.
        /// </summary>
        /// <param name="storageType">Type of storage to read from</param>
        /// <returns>Fluent interface to retrieve the result with .GetStorageData</returns>
        public IAllowStorageOutData RunGet(WebsiteStorageType storageType)
        {
            BrowserSettings.StorageActionCalled = true;
            BrowserSettings.StorageActionToRun.Action = WebsiteStorageAction.get;
            BrowserSettings.StorageActionToRun.StorageType = storageType;

            if (true == StorageRunsNow())
                UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, BrowserSettings.StorageActionToRun, out BrowserSettings.StorageActionToRun._data);

            return this;
        }
        /// <summary>
        /// Runs the next storage action (RunGet, RunSet or RunDelete) against <paramref name="url"/>'s storage
        /// definitions instead of the current page, without navigating. Applies to one action.<br/>
        /// Use it to clear a site's cookies before visiting.<br/>
        /// <b>NOTE:</b> Before the first GoTo or Get, a set or delete is held and runs when the browser starts,
        /// ahead of the first page.<br/>
        /// <b>NOTE:</b> Selenium on Firefox reaches only the current site's storage; there the action warns and finds
        /// nothing, so go to the site first.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPALUrl gucci = "https://www.gucci.com/us/en/";
        ///     gucci.WithStorageType(WebsiteStorageType.cookie).WithStorageDomain("gucci.com");
        ///
        ///     browser
        ///         .For(gucci)
        ///             .WithStorageKey("_abck")
        ///             .RunDelete(WebsiteStorageType.cookie)
        ///         .GoTo(gucci);
        /// </code>
        /// </example>
        /// <param name="url">The url whose storage definitions the next storage action uses.</param>
        /// <returns>Fluent interface to scope and run the storage action</returns>
        public IAllowStorageOptions For(GPALUrl url)
        {
            BrowserSettings.StorageForGPALUrl = url;

            // a new storage action, so nothing scoped for an earlier one carries over to this url
            BrowserSettings.StorageActionToRun = new StorageAction();
            BrowserSettings.StorageActionCalled = false;

            return this;
        }
        // Storage sets and deletes asked for before the browser started, each with the url it runs against (null
        // for the page the first GoTo or Get opens). Run once the engine answers, ahead of the first navigation.
        private readonly List<KeyValuePair<GPALUrl, StorageAction>> _pendingStorageActions = new List<KeyValuePair<GPALUrl, StorageAction>>();

        /// <summary>
        /// Whether a storage action runs now. A storage action never launches a browser: only the GoTo or Get that
        /// comes later knows whether it opens in a window or headless. Before the browser starts, a set or delete
        /// is held and run when it does; a get has nothing to read yet and warns. A browser that was closed, or
        /// whose Selenium driver died, throws in InAction.
        /// </summary>
        /// <returns>True when the action runs now</returns>
        private bool StorageRunsNow()
        {
            bool retVal = null != ClosedAt || true == HasBrowser;
            StorageAction asked = BrowserSettings.StorageActionToRun;

            if (true == retVal)
                InAction(false, false);
            else
            {
                if (WebsiteStorageAction.get == asked.Action)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No browser is running yet, so there is no [{asked.StorageType}] storage to read. Read it after the first GoTo or Get.", this, GPALObjectType.Browser);
                else
                {
                    _pendingStorageActions.Add(new KeyValuePair<GPALUrl, StorageAction>(BrowserSettings.StorageForGPALUrl, asked));

                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Holding [{asked.Action}] on [{asked.StorageType}] storage for [{BrowserSettings.StorageForGPALUrl?.Url ?? "the first page"}] until the browser starts", this, GPALObjectType.Browser);
                }

                // For applies to one action, and this was it
                BrowserSettings.StorageForGPALUrl = null;
            }

            return retVal;
        }

        /// <summary>
        /// Runs every storage set and delete held while the browser was still down. Called from WorkflowSetup once
        /// the engine answers and before the first navigation, so cookies deleted ahead of .GoTo() are gone when
        /// the first page loads.
        /// </summary>
        internal void ApplyPendingStorageActions()
        {
            if (0 < _pendingStorageActions.Count && true == EngineReachable)
            {
                List<KeyValuePair<GPALUrl, StorageAction>> held = new List<KeyValuePair<GPALUrl, StorageAction>>(_pendingStorageActions);

                _pendingStorageActions.Clear();

                foreach (KeyValuePair<GPALUrl, StorageAction> action in held)
                {
                    BrowserSettings.StorageForGPALUrl = action.Key;
                    UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, action.Value, out _);
                }
            }
        }
        /// <summary>
        /// The url a storage action runs against: the one For named, used once and cleared, or else the page the
        /// browser is on.
        /// </summary>
        /// <returns>The url whose storage definitions the action uses</returns>
        private GPALUrl StorageUrl()
        {
            GPALUrl retVal = BrowserSettings.StorageForGPALUrl ?? BrowserSettings.CurrentGPALUrl;

            // WebDriver on Firefox sees only the current site's storage; warn so the empty result is explained.
            if (null != BrowserSettings.StorageForGPALUrl && true == UseSelenium && BrowserType.FireFox == BrowserSettings.BrowserType
                && false == string.Equals(UrlHelper.GetOrigin(BrowserSettings.StorageForGPALUrl.Url), UrlHelper.GetOrigin(BrowserHelper.GetCurrentUrl(BrowserSettings)), StringComparison.OrdinalIgnoreCase))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Selenium on Firefox reaches only the storage of the site it is on, so nothing for [{BrowserSettings.StorageForGPALUrl.Url}] can be reached from [{BrowserHelper.GetCurrentUrl(BrowserSettings)}]. Run this after going there.", this, GPALObjectType.Browser);

            BrowserSettings.StorageForGPALUrl = null;

            return retVal;
        }
        /// <summary>
        /// Writes data to browser storage (cookies, local/session storage, IndexedDB) using the domain/path/key/data
        /// configured by the preceding .WithStorage* calls.
        /// </summary>
        /// <param name="storageType">Type of storage to write to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RunSet(WebsiteStorageType storageType)
        {
            BrowserSettings.StorageActionCalled = true;
            BrowserSettings.StorageActionToRun.Action = WebsiteStorageAction.set;
            BrowserSettings.StorageActionToRun.StorageType = storageType;

            if (true == StorageRunsNow())
                UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, BrowserSettings.StorageActionToRun, out _);

            return this;
        }
        /// <summary>
        /// Deletes data from browser storage (cookies, local/session storage, IndexedDB) using the domain/path/key
        /// configured by the preceding .WithStorage* calls.
        /// </summary>
        /// <param name="storageType">Type of storage to delete from</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector RunDelete(WebsiteStorageType storageType)
        {
            BrowserSettings.StorageActionCalled = true;
            BrowserSettings.StorageActionToRun.Action = WebsiteStorageAction.delete;
            BrowserSettings.StorageActionToRun.StorageType = storageType;

            if (true == StorageRunsNow())
                UrlHelper.TryPerformAction(StorageUrl(), BrowserSettings, BrowserSettings.StorageActionToRun, out _);

            return this;
        }

        /// <summary>
        /// Specifies the domain to scope the next storage Run* action to. Starts a new StorageAction if the
        /// previous one was already run.
        /// </summary>
        /// <param name="domain">Domain to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageDomain(string domain)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Domain = domain;

            return this;
        }

        /// <summary>
        /// Specifies the path to scope the next storage Run* action to. Starts a new StorageAction if the
        /// previous one was already run.
        /// </summary>
        /// <param name="path">Path to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStoragePath(string path)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Path = path;

            return this;
        }

        /// <summary>
        /// Specifies the key to scope the next storage Run* action to. Starts a new StorageAction if the
        /// previous one was already run.
        /// </summary>
        /// <param name="key">Key to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageKey(string key)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Key = key;

            return this;
        }

        /// <summary>
        /// Specifies the IndexedDB store name to scope the next storage Run* action to. Starts a new
        /// StorageAction if the previous one was already run.
        /// </summary>
        /// <param name="storeName">IndexedDB store name to scope the storage action to</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageStoreName(string storeName)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.StoreName = storeName;

            return this;
        }

        /// <summary>
        /// Specifies the data value to write for the next storage .RunSet action. Starts a new StorageAction
        /// if the previous one was already run.
        /// </summary>
        /// <param name="data">Data value to write to storage</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithStorageData(string data)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.Data = data;

            return this;
        }

        /// <summary>
        /// Specifies a user-defined filter string passed through to the storage action implementation. Starts
        /// a new StorageAction if the previous one was already run.
        /// </summary>
        /// <param name="userDefined">User-defined filter value</param>
        /// <returns>Fluent interface to specify more storage options or run the action</returns>
        public IAllowStorageOptions WithUserDefinedFilter(string userDefined)
        {
            if (true == BrowserSettings.StorageActionCalled)
            {
                BrowserSettings.StorageActionToRun = new StorageAction();
                BrowserSettings.StorageActionCalled = false;
            }

            BrowserSettings.StorageActionToRun.UserDefined = userDefined;

            return this;
        }

        /// <summary>
        /// Retrieves the data read by the most recent .RunGet storage action.
        /// </summary>
        /// <param name="data">Receives the data retrieved from storage</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector GetStorageData(out string data)
        {
            data = BrowserSettings.StorageActionToRun.Data;

            return this;
        }
    }
}
