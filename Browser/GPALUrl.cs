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
using System.Linq;
using System.Web;
using static GenerallyPositive.Enums;

// written by grok, modified by mbv
namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Represents a URL with optional pre-navigation storage cleanup / inspection actions.
    /// Uses fluent builder pattern with fail-forward design (logs issues instead of throwing exceptions).
    /// Supports implicit conversion to/from string (with warning on first implicit use).
    /// </summary>
    /// <remarks>
    /// Each WithStorageType starts a new storage action; the calls after it configure that action. RunGet, RunSet and
    /// RunDelete act only on declared actions.
    /// </remarks>
    /// <example>
    /// <code>
    /// GPALUrl url = "https://example.com";                       // or GPAL.Url.ForUrl("https://example.com")
    ///
    /// url.WithStorageType(WebsiteStorageType.cookie)
    ///    .WithStorageDomain("example.com")
    ///    .WithStorageKey("session");
    ///
    /// url.WithStorageType(WebsiteStorageType.localStorage)
    ///    .WithStorageKey("theme");
    ///
    /// browser.GoTo(url);                                          // the url becomes the one in context
    ///
    /// browser.WithStorageKey("theme")
    ///        .WithStorageData("dark")
    ///        .RunSet(WebsiteStorageType.localStorage);
    /// </code>
    /// </example>
    public class GPALUrl : IGPALUrl
    {
        private string _url;
        private readonly List<StorageAction> _actions = new List<StorageAction>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GPALUrl"/> class.
        /// </summary>
        /// <param name="url">Initial URL. If null/empty/whitespace, defaults to "https://google.com".</param>
        internal GPALUrl(string url = null)
        {
            _url = string.IsNullOrWhiteSpace(url) ? "https://google.com" : url;
        }

        /// <summary>
        /// Gets the target URL string.
        /// </summary>
        public string Url => _url;

        /// <summary>
        /// Returns a read-only view of the configured storage actions.
        /// </summary>
        IReadOnlyList<StorageAction> IGPALUrl.GetActions() => _actions.AsReadOnly();

        /// <summary>
        /// Implicitly converts <see cref="GPALUrl"/> to its string URL representation.
        /// </summary>
        public static implicit operator string(GPALUrl url) => url?._url;

        /// <summary>
        /// The URL, so a GPALUrl in a message or a comparison reads as the address and not the type name.
        /// </summary>
        /// <returns>The target URL</returns>
        public override string ToString() => _url;

        private static int implicitCount = 0;

        /// <summary>
        /// Implicitly converts a string to a GPALUrl. Implicit instances lack equality; use GPAL.Url to reuse one.
        /// </summary>
        public static implicit operator GPALUrl(string url)
        {
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Implicit GPALUrl created for [{url}]");

            // emit warning once.
            if (0 == implicitCount++)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Implicit GPALUrl conversions lack equality, even if Urls match. Create instance using GPAL.Url for reuse.");

            return new GPALUrl(url);
        }

        /// <summary>
        /// Changes (or sets) the target URL for this builder instance.
        /// </summary>
        /// <param name="url">The new URL. If null/empty/whitespace > defaults to "https://google.com".</param>
        /// <returns>Self (for fluent chaining).</returns>
        public IAllowGPALUrlStorageType ForUrl(string url)
        {
            _url = string.IsNullOrWhiteSpace(url) ? "https://google.com" : url;
            return this;
        }

        /// <summary>
        /// Specify the storage type for this storage action
        /// </summary>
        /// <param name="type">Type of storage action (cookies, localStorage, etc.).</param>
        /// <returns>Interface allowing further configuration of this action.</returns>
        public IAllowGPALUrlStorageItemConfig WithStorageType(WebsiteStorageType type)
        {
            _actions.Add(new StorageAction { StorageType = type });
            return this;
        }

        /// <summary>
        /// Specifies the data to set
        /// </summary>
        /// <param name="data">data (context sensistieve).</param>
        /// <returns>Interface for continued configuration.</returns>
        public IAllowGPALUrlStorageItemConfig WithStorageData(string data)
        {
            if (_actions.Count == 0 || string.IsNullOrWhiteSpace(data))
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].Data = data;

            return this;
        }

        /// <summary>
        /// Specifies the key for the most recently added storage action. Ignored when there is none or the key is
        /// empty.
        /// </summary>
        /// <param name="key">The storage key (e.g. "sessionId", "theme").</param>
        /// <returns>Interface for continued configuration.</returns>
        public IAllowGPALUrlStorageItemConfig WithStorageKey(string key)
        {
            if (_actions.Count == 0 || string.IsNullOrWhiteSpace(key))
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].Key = key;

            return this;
        }

        /// <summary>
        /// Specifies the path restriction for the most recently added storage action (mainly for cookies).
        /// </summary>
        /// <param name="path">Cookie path (e.g. "/account", "/").</param>
        /// <returns>Interface for continued configuration.</returns>
        public IAllowGPALUrlStorageItemConfig WithStoragePath(string path)
        {
            if (_actions.Count == 0 || string.IsNullOrWhiteSpace(path))
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].Path = path;

            return this;
        }

        /// <summary>
        /// Specifies the domain restriction for the most recently added storage action (mainly cookies).
        /// </summary>
        /// <param name="domain">Domain (e.g. ".example.com", "sub.example.com").</param>
        /// <returns>Interface for continued configuration.</returns>
        public IAllowGPALUrlStorageItemConfig WithStorageDomain(string domain)
        {
            if (_actions.Count == 0 || string.IsNullOrWhiteSpace(domain))
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].Domain = domain;

            return this;
        }

        /// <summary>
        /// Specifies the IndexedDB store name for the most recently added action.
        /// </summary>
        /// <param name="storeName">Name of the object store.</param>
        /// <returns>Interface for continued configuration.</returns>
        public IAllowGPALUrlStorageItemConfig WithStorageStoreName(string storeName)
        {
            if (_actions.Count == 0 || string.IsNullOrWhiteSpace(storeName))
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].StoreName = storeName;

            return this;
        }

        /// <summary>
        /// Specifies the data to set
        /// </summary>
        /// <param name="userDefined">The data for the last storage action added; ignored when empty.</param>
        /// <returns>Interface for continued configuration.</returns>
        public IAllowGPALUrlStorageItemConfig WithUserDefined(string userDefined)
        {
            if (_actions.Count == 0 || string.IsNullOrWhiteSpace(userDefined))
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].UserDefined = userDefined;

            return this;
        }

        public IAllowGPALUrlStorageItemConfig WithDeleteAcrossOrigins(bool trueOrFalse)
        {
            if (_actions.Count == 0)
                return this;

            int lastIndex = _actions.Count - 1;

            _actions[lastIndex].DeleteAcrossOrigins = trueOrFalse;

            return this;
        }

        /// <summary>
        /// Adds a fully-configured <see cref="StorageAction"/> to the list.
        /// </summary>
        /// <param name="storageAction">The action to add.</param>
        /// <returns>The updated list of actions (for convenience, though usually not needed).</returns>
        public List<StorageAction> Add(StorageAction storageAction)
        {
            _actions.Add(storageAction);
            return _actions;
        }

        /// <summary>
        /// Adds multiple fully-configured <see cref="StorageAction"/> items.
        /// </summary>
        /// <param name="storageActions">Actions to append.</param>
        /// <returns>The updated list of actions.</returns>
        public List<StorageAction> Add(List<StorageAction> storageActions)
        {
            _actions.AddRange(storageActions);
            return _actions;
        }

        /// <summary>
        /// Returns this instance typed as <see cref="IGPALUrl"/>.
        /// </summary>
        /// <returns>Self as interface.</returns>
        public IGPALUrl ToGPALObject()
        {
            return this;
        }
    }

    /// <summary>
    /// Represents a single storage-related action to perform before navigating to a URL.
    /// </summary>
    public class StorageAction
    {
        public StorageAction()
        {
        }

        public StorageAction(StorageAction action)
        {
            Action = action.Action;
            StorageType = action.StorageType;
            Key = action.Key;
            Path = action.Path;
            Domain = action.Domain;
            StoreName = action.StoreName;
            Data = action.Data;
            IncludeProvenance = action.IncludeProvenance;
        }
        /// <summary>
        /// The action to perform: "get", "set", or "delete".
        /// </summary>
        public WebsiteStorageAction Action { get; internal set; } = WebsiteStorageAction.notSet;

        /// <summary>
        /// The target storage type (cookies, localStorage, indexedDb, etc.).
        /// </summary>
        public WebsiteStorageType StorageType { get; internal set; } = WebsiteStorageType.notSet;

        /// <summary>
        /// Key name for localStorage, sessionStorage, IndexedDB, or cookie name.
        /// </summary>
        public string Key { get; internal set; }

        /// <summary>
        /// Path restriction (primarily for cookies).
        /// </summary>
        public string Path { get; internal set; }

        /// <summary>
        /// Domain restriction (primarily for cookies).
        /// </summary>
        public string Domain { get; internal set; }

        /// <summary>
        /// IndexedDB object store name.
        /// </summary>
        public string StoreName { get; internal set; }

        internal string _data = null;

        /// <summary>
        /// Value to get/set (used when Action = "get" or "set").
        /// NOTE: context sensitive (json) data
        /// NOTE: Data can vary by impelementation as exposed by the respective API
        /// </summary>
        public string Data 
        { 
            get => _data; 
            internal set => _data = value; 
        }
        
 
        /// <summary>
        /// For wildcard storage gets returning multiple elements.
        /// If set to true, return domain, path, storename (if applicable) and key to locate the returned value
        /// Otherwise, just return a list of string values
        /// </summary>
        public bool IncludeProvenance { get; internal set; }    // not currently used (correctly)

        /// <summary>
        ///  User defined data strictly used for filtering in GPAL, can store any data of value
        /// </summary>
        public string UserDefined { get; internal set; }

        /// <summary>
        /// OttoMagic Only: For delete operations without a domain, limit to origin?
        /// true = delete across origins
        /// false = delete only in the current origin (default)
        /// </summary>
        public bool DeleteAcrossOrigins { get; internal set; } = false;
    }
}
