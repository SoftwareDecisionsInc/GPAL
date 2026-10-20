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
    public static class Native
    {
        // Constants
        public const uint DUPLICATE_SAME_ACCESS = 0x00000002;
        public const uint HANDLE_FLAG_INHERIT = 0x00000001;
        public const uint STARTF_USESTDHANDLES = 0x00000100;
        public const uint STARTF_USESHOWWINDOW = 0x00000001;
        public const uint CREATE_NO_WINDOW = 0x08000000;
        public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const uint STILL_ACTIVE = 259;
        public const ushort SW_HIDE = 0;
        public const ushort SW_SHOWNORMAL = 1;

        // Proc/thread attribute constant (if using STARTUPINFOEX)
        public const int PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002;

        // SECURITY_ATTRIBUTES
        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle; // BOOL as int
        }

        // STARTUPINFO (standard)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        // Extended STARTUPINFO for attribute list
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        // PROCESS_INFORMATION
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        // P/Invokes

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CreatePipe(
            out IntPtr hReadPipe,
            out IntPtr hWritePipe,
            ref SECURITY_ATTRIBUTES lpPipeAttributes,
            uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetHandleInformation(
            IntPtr hObject,
            uint dwMask,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetHandleInformation(
            IntPtr hObject,
            out uint lpdwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFOEX lpStartupInfoEx,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList,
            int dwAttributeCount,
            int dwFlags,
            ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(
            IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool PeekNamedPipe(
            IntPtr hNamedPipe,
            IntPtr lpBuffer,
            uint nBufferSize,
            IntPtr lpBytesRead,
            out uint lpTotalBytesAvail,
            IntPtr lpBytesLeftThisMessage);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetExitCodeProcess(
            IntPtr hProcess,
            out uint lpExitCode);

        // Added: TerminateProcess to allow force-killing a process handle
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(
            IntPtr hProcess,
            uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(
            IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);
    }
    public class WindowInfo
    {
        public string URL { get; set; }
        public bool ActiveWindow { get; set; }
    }
    public class WindowTuple
    {
        public string WindowId { get; set; }
        public string Url { get; set; }

        public WindowTuple(string windowId, string url)
        {
            WindowId = windowId;
            Url = url;
        }

        public WindowTuple()
        { }
    }

    public class TabTuple
    {
        public int TabId { get; set; }
        public string Url { get; set; }

        public TabTuple(int TabId, string Url)
        {
            this.TabId = TabId;
            this.Url = Url;
        }
        public TabTuple()
        { }
    }

    public class MagicHelper : IMagicHelper
    {
        internal MagicHelper(Browser browser)
        {
            this.browser = browser;
        }

        // GPALRestAPI announces its port and process id on a fixed UDP port only one listener can bind. This named,
        // machine-wide mutex makes each launch, in any GPAL application, wait until the previous one has its port.
        private static readonly Mutex ottoHandshakeGate = new Mutex(false, "GPAL.OttoMagic.PortHandshake");
        private const int OttoHandshakeGateTimeoutMs = 60_000;

        List<string> lastErrorMessage = new List<string>();
        bool supressedMessage = false;
        string lastSessionToken;
        IBrowser browser { get; set; } = null;

        private IRESTClient _client;
        private string _clientApiBase;

        /// <summary>
        /// This browser's REST client, available before launch.
        /// <br/><br/>
        /// The base url exists only after the GPALRestAPI port handshake and is set on the client once it does, so
        /// declaration order does not matter.
        /// </summary>
        internal IRESTClient Client
        {
            get
            {
                if (null == _client)
                    _client = (IRESTClient)GPAL.RESTClient;

                string url = ((Browser)browser)?.BrowserSettings.RestApiBaseUrl;

                // Update the url whenever it changes; a relaunched browser gets a new port.
                if (false == string.IsNullOrEmpty(url) && url != _clientApiBase)
                {
                    _client.WithAPIBase(url);
                    _clientApiBase = url;
                }

                return _client;
            }
        }

        #region Commands
        /// <summary>
        /// Navigates browser history back
        /// <example>browser.MagicHelper.Back();</example>
        /// </summary>
        public void Back()
        {
            Client.Back().Execute();
        }

        public string CaptureVisibleTab()
        {
            return Client.CaptureVisibleTab().Execute();
        }
        /// <summary>
        /// Reports at once whether the network has been idle: at most maxConnections requests in flight for the
        /// whole of the last pruneMs. Ask again to wait for it.
        /// <br/><br/>
        /// Each parameter is optional and defaults to the browser setting: WithNetworkIdleMaxConnections and
        /// WithNetworkIdlePruneMs. Called with no arguments it matches what GPAL checks after a navigation.
        /// <example>bool idle = browser.MagicHelper.CheckNetworkIdle(5);</example>
        /// </summary>
        /// <param name="maxConnections">Connections that still count as idle</param>
        /// <param name="pruneMs">How far back, in milliseconds, the in-flight count must have stayed at or below maxConnections</param>
        /// <returns>Whether network is idle</returns>
        public string CheckNetworkIdle(int? maxConnections = null, int? pruneMs = null)
        {
            BrowserSettings settings = ((Browser)browser)?.BrowserSettings;

            // CheckNetworkIdle(int) returns an interface that does not expose the other two parameters, so the
            // endpoint is set directly
            string retVal = Client
                .WithEndpoint(ApiEndpoint.CheckNetworkIdle)
                .WithMaxConnections(maxConnections ?? settings?.NetworkIdleMaxConnections ?? 0)
                .WithPruneMs(pruneMs ?? settings?.NetworkIdlePruneMs ?? 3_000)
                .Execute();

            return retVal;
        }

        /// <summary>
        /// Closes a tab by URL or tab ID
        /// <example>TabTuple closed = browser.MagicHelper.CloseTab("https://example.com");</example>
        /// </summary>
        /// <param name="URLorTabId">URL or tab ID to close</param>
        /// <returns>Closed tab details</returns>
        public TabTuple CloseTab(dynamic URLorTabId = null)
        {
            if (null == URLorTabId)
                return Client.CloseTab().Execute<TabTuple>();
            else if (URLorTabId is string v)
                return Client.CloseTab(v).Execute<TabTuple>();
            else if (URLorTabId is GPALUrl u)
                return Client.CloseTab(u).Execute<TabTuple>();
            else if (URLorTabId is int i)
                return Client.CloseTab(URLorTabId).Execute<TabTuple>();
            else
                return Client.CloseTab().Execute<TabTuple>();

        }

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

        /// <summary>
        /// Navigates browser history forward
        /// <example>browser.MagicHelper.Forward();</example>
        /// </summary>
        public void Forward()
        {
            Client.Forward().Execute();
        }

        /// <summary>
        /// Sets browser to full screen
        /// <example>browser.MagicHelper.FullScreen();</example>
        /// </summary>
        public void FullScreen()
        {
            Client.FullScreen().Execute();
        }

        /// <summary>
        /// Navigates to a URL
        /// <example>string result = browser.MagicHelper.Get("https://example.com");</example>
        /// </summary>
        /// <param name="URL">The URL to navigate to</param>
        /// <returns>The navigation result</returns>
        public string Get(GPALUrl URL)
        {
            bool areRobotsAllowed = true;

            URL.ForUrl(GetFullUrl(URL?.Url, browser, out areRobotsAllowed));

            if (null != browser)
                ((Browser)browser)._areRobotsAllowed = areRobotsAllowed;

            var result = Client.GoTo(URL?.Url).Execute();
            browser.ServerResponseCode = GetStatusCode();

            return result;
        }

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
        /// Gets the current URL
        /// <example>string url = browser.MagicHelper.GetCurrentUrl();</example>
        /// </summary>
        /// <returns>The current URL</returns>
        public string GetCurrentUrl()
        {
            return Client.GetCurrentUrl().Execute();
        }

        /// <summary>
        /// Gets the ready status
        /// NOTE: if no page is loaded, this will return null cause there is no content script. so it's "complete".
        /// <example>string status = browser.MagicHelper.GetReadyStatus();</example>
        /// </summary>
        /// <returns>The ready status</returns>
        public string GetReadyStatus()
        {
            return GetReadyStatus(null);
        }

        /// <summary>
        /// The session token identifies the calling wait loop, so a status repeated during one wait is logged once.
        /// The public overload does not take it.
        /// </summary>
        /// <param name="sessionToken">Identifies the wait this reading belongs to</param>
        /// <returns>The ready status</returns>
        internal string GetReadyStatus(string sessionToken)
        {
            string retVal = null;

            if (lastSessionToken != sessionToken)
            {
                lastErrorMessage.Clear();
                supressedMessage = false;
                lastSessionToken = sessionToken;
            }

            dynamic obj = Client.GetReadyStatus().Execute();
            if ("null" != obj && "NOTOK" != obj)
                retVal = obj ?? @"""loading""";
            else
                retVal = "complete";

            string currentErrorMessage = $"Document.Ready status [{retVal}]";

            if (false == lastErrorMessage.Contains(currentErrorMessage))
            {
                lastErrorMessage.Add(currentErrorMessage);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, currentErrorMessage, null, GPALObjectType.None);
                supressedMessage = false;
            }
            else if (false == supressedMessage)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", currentErrorMessage, GPALObjectType.Other);
                supressedMessage = true;
            }

            return retVal;
        }

        /// <summary>
        /// Gets the shadow root for an elementId
        /// <example>GPALElement root = browser.MagicHelper.GetShadowRoot(elementId);</example>
        /// </summary>
        /// <param name="elementId">The elementId</param>
        /// <returns>The shadow root element</returns>
        public GPALElement GetShadowRoot(string elementId)
        {
            return Client.GetShadowRoot(elementId).Execute<GPALElement>();
        }

        /// <summary>
        /// Navigates to a URL
        /// <example>TabTuple tab = browser.MagicHelper.Goto("https://example.com");</example>
        /// </summary>
        /// <param name="URL">The URL to navigate to</param>
        /// <returns>The tab details</returns>
        public TabTuple GoTo(GPALUrl URL)
        {
            if (null == URL)
                URL = new GPALUrl("google.com");

            bool areRobotsAllowed = true;

            URL.ForUrl(GetFullUrl(URL.Url, browser, out areRobotsAllowed));

            if (null != browser)
                ((Browser)browser)._areRobotsAllowed = (bool) areRobotsAllowed;

            TabTuple retVal = Client.GoTo(URL.Url).Execute<TabTuple>();
            browser.ServerResponseCode = GetStatusCode();

            return retVal;
        }

        /// <summary>
        /// Navigates to a tab by URL or tab ID
        /// <example>TabTuple tab = browser.MagicHelper.GotoTab("https://example.com");</example>
        /// </summary>
        /// <param name="URLorTabId">URL or tab ID to navigate to</param>
        /// <returns>The tab details</returns>
        public TabTuple GotoTab(dynamic URLorTabId)
        {
            if (URLorTabId is GPALUrl v)
            {
                TabTuple tt = Client.GoToTab(v).Execute<TabTuple>();
                browser.ServerResponseCode = GetStatusCode();
                return tt;
            }
            else if (URLorTabId is TabTuple tuple)
            {
                TabTuple tt = Client.GoToTab(tuple.TabId).Execute<TabTuple>();
                browser.ServerResponseCode = GetStatusCode();
                return tt;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"URLorTabId is unknown type [{URLorTabId.GetType()}]", this, GPALObjectType.Other);
                return new TabTuple();
            }
        }

        /// <summary>
        /// Switches context to an element
        /// <example>browser.MagicHelper.InElement();</example>
        /// NOTE: InFrame and InElement are identical in implementation
        /// </summary>
        public void SwitchToElement(string elementId)
        {
            Client.SwitchToElement(elementId).Execute();
        }

        /// <summary>
        /// Switches context to iframe
        /// <example>browser.MagicHelper.InFrame();</example>
        /// NOTE: InFrame and InElement are identical in implementation
        /// </summary>
        public void InFrame(string elementId)
        {
            Client.SwitchToElement(elementId).Execute();
        }

        /// <summary>
        /// Minimizes the browser window
        /// <example>browser.MagicHelper.Minimize();</example>
        /// </summary>
        public void Minimize()
        {
            Client.Minimize().Execute();
        }

        /// <summary>
        /// Maximizes the browser window
        /// <example>browser.MagicHelper.Maximize();</example>
        /// </summary>
        public void Maximize()
        {
            Client.Maximize().Execute();
        }

        /// <summary>
        /// Scrolls an element into view by ID
        /// <example>browser.MagicHelper.ScrollIntoView("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void MoveTo(string elementId)
        {
            Client.ScrollIntoView(elementId).Execute();
            Client.Hover(elementId).Execute();
        }

        /// <summary>
        /// Restores the browser window to normal
        /// <example>browser.MagicHelper.Normal();</example>
        /// </summary>
        public void Normal()
        {
            Client.Normal().Execute();
        }

        /// <summary>
        /// Opens a new tab with optional URL
        /// <example>TabTuple tab = browser.MagicHelper.NewTab("https://example.com");</example>
        /// </summary>
        /// <param name="URL">The URL to open</param>
        /// <returns>The tab details</returns>
        public TabTuple NewTab(GPALUrl URL = null)
        {
            if (null == URL)
                URL = new GPALUrl();

            URL.ForUrl(GetFullUrl(URL.Url, browser, out ((Browser)browser)._areRobotsAllowed));

            TabTuple retVal = Client.NewTab(URL.Url).Execute<TabTuple>();
            browser.ServerResponseCode = GetStatusCode();

            return retVal;
        }

        /// <summary>
        /// Switches to the next tab
        /// <example>TabTuple tab = browser.MagicHelper.NextTab();</example>
        /// </summary>
        /// <returns>The tab details</returns>
        public TabTuple NextTab()
        {
            return Client.NextTab().Execute<TabTuple>();
        }

        /// <summary>
        /// Number of tabs open in the browser. Can differ from what GPAL opened; the browser closes a tab whose
        /// first navigation becomes a download.
        /// <example>int tabs = browser.MagicHelper.TabCount();</example>
        /// </summary>
        /// <returns>The number of tabs open, or zero when the extension could not say</returns>
        public int TabCount()
        {
            return Client.TabCount().Execute<int>();
        }

        /// <summary>
        /// Switches to the next window
        /// <example>WindowTuple window = browser.MagicHelper.NextWindow();</example>
        /// </summary>
        /// <returns>The window details</returns>
        public WindowTuple NextWindow()
        {
            return Client.NextWindow().Execute<WindowTuple>();
        }

        /// <summary>
        /// Overrides the referrer URL
        /// <example>browser.MagicHelper.OverrideReferrer("https://google.com");</example>
        /// </summary>
        /// <param name="referrer">The referrer URL</param>
        public void OverrideReferrer(string referrer)
        {
            Client.OverrideReferrer(referrer).Execute();
        }

        /// <summary>
        /// Overrides the browser's user agent string
        /// <example>browser.MagicHelper.SetUserAgent("Mozilla/5.0 ...");</example>
        /// </summary>
        /// <param name="userAgent">The user agent string to use</param>
        public void SetUserAgent(string userAgent)
        {
            Client.SetUserAgent(userAgent).Execute();
        }

        /// <summary>
        /// Scrolls the page down
        /// <example>browser.MagicHelper.PageDown();</example>
        /// </summary>
        public void PageDown()
        {
            Client.PageDown().Execute();
        }

        /// <summary>
        /// Scrolls to the page end
        /// <example>browser.MagicHelper.PageEnd();</example>
        /// </summary>
        public void PageEnd()
        {
            Client.PageEnd().Execute();
        }

        /// <summary>
        /// Scrolls to the page top
        /// <example>browser.MagicHelper.PageTop();</example>
        /// </summary>
        public void PageTop()
        {
            Client.PageTop().Execute();
        }

        /// <summary>
        /// Scrolls the page up
        /// <example>browser.MagicHelper.PageUp();</example>
        /// </summary>
        public void PageUp()
        {
            Client.PageUp().Execute();
        }
        /// <summary>
        /// Press the specified modifier keys
        /// </summary>
        /// <param name="modifierKeys"></param>
        public void PressModifierKey(ModifierKeys modifierKeys)
        {
            Client.PressModifierKey(modifierKeys).Execute();
        }
        /// <summary>
        /// Switches to the previous tab
        /// <example>TabTuple tab = browser.MagicHelper.PreviousTab();</example>
        /// </summary>
        /// <returns>The tab details</returns>
        public TabTuple PreviousTab()
        {
            return Client.PreviousTab().Execute<TabTuple>();
        }


        /// <summary>
        /// Switches to the previous window
        /// <example>WindowTuple window = browser.MagicHelper.PreviousWindow();</example>
        /// </summary>
        /// <returns>The window details</returns>
        public WindowTuple PreviousWindow()
        {
            return Client.PreviousWindow().Execute<WindowTuple>();
        }
        /// <summary>
        /// Refreshes the current page
        /// <example>browser.MagicHelper.Refresh();</example>
        /// </summary>
        public void Refresh()
        {
            Client.Refresh().Execute();
        }

        /// <summary>
        /// Press the specified modifier keys
        /// </summary>
        /// <param name="modifierKeys"></param>
        public void ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            Client.ReleaseModifierKey(modifierKeys).Execute();
        }

        /// <summary>
        /// Restores the browser window
        /// <example>browser.MagicHelper.Restore();</example>
        /// </summary>
        public void Restore()
        {
            Client.Restore().Execute();
        }

        /// <summary>
        /// Scrolls the window horizontally by pixels
        /// <example>browser.MagicHelper.ScrollWindowByHorizontal(100);</example>
        /// </summary>
        /// <param name="pixels">The horizontal pixels</param>
        public void ScrollWindowHorizontal(int pixels)
        {
            Client.ScrollWindowByHorizontal(pixels).Execute();
        }

        /// <summary>
        /// Scrolls the window vertically by pixels
        /// <example>browser.MagicHelper.ScrollWindowByVertical(100);</example>
        /// </summary>
        /// <param name="pixels">The vertical pixels</param>
        public void ScrollWindowVertical(int pixels)
        {
            Client.ScrollWindowByVertical(pixels).Execute();
        }

        /// <summary>
        /// Overrides referrer stealthily
        /// <example>browser.MagicHelper.SetGoogleReferrer();</example>
        /// </summary>
        public void SetGoogleReferrer()
        {
            Client.SetGoogleReferrer().Execute();
        }

        /// <summary>
        /// Sets the filename for the next download.<br/>
        /// Only the name is sent: onDeterminingFilename accepts only a name relative to the browser's download
        /// folder and loses the name entirely given a full path. The download watcher moves the file to
        /// <paramref name="downloadPath"/>.
        /// <example>browser.MagicHelper.WithDownloadFile(@"c:\sdi\sample.pdf");   // the extension is sent sample.pdf</example>
        /// </summary>
        /// <param name="downloadPath">Where the file is to end up; the extension is given its filename</param>
        public void WithDownloadFile(string downloadPath)
        {
            Client.SetDownloadFilename(Path.GetFileName(downloadPath)).Execute();
        }

        /// <summary>
        /// Gets the window screen left position
        /// <example>int left = browser.MagicHelper.WindowScreenLeft();</example>
        /// </summary>
        /// <returns>The screen left position</returns>
        public int WindowScreenLeft()
        {
            return Client.WindowScreenLeft().Execute<int>();
        }

        /// <summary>
        /// Gets the window screen top position
        /// <example>int top = browser.MagicHelper.WindowScreenTop();</example>
        /// </summary>
        /// <returns>The screen top position</returns>
        public int WindowScreenTop()
        {
            return Client.WindowScreenTop().Execute<int>();
        }

        /// <summary>
        /// Focuses an element by ID
        /// <example>browser.MagicHelper.Focus("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void Focus(string elementId)
        {
            Client.Focus(elementId).Execute();
        }

        /// <summary>
        /// Fires a change event on an element by ID
        /// <example>browser.MagicHelper.FireChangeEvent("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void FireChangeEvent(string elementId)
        {
            Client.FireChangeEvent(elementId).Execute();
        }
        /// <summary>
        /// Returns the browser window inner/outer height/widge
        /// { y: window.innerHeight, x: window.width, height: window.outerHeight, width: window.outerWidth }
        /// </summary>
        /// <returns></returns>
        public Rectangle GetWindowRectangle()
        {
            Rectangle rect = new Rectangle();
            var result =  Client.GetWindowRectangle().Execute<Dictionary<string, object>>();
            Dictionary<string, object> obj = null;

            if (null != result)
            {
                obj = (Dictionary<string, object>)result;

                // the extension answers { X, Y, Width, Height }
                var left = obj.ContainsKey("X") ? Convert.ToInt32(obj["X"]) : 0;
                var top = obj.ContainsKey("Y") ? Convert.ToInt32(obj["Y"]) : 0;
                var width = obj.ContainsKey("Width") ? Convert.ToInt32(obj["Width"]) : 0;
                var height = obj.ContainsKey("Height") ? Convert.ToInt32(obj["Height"]) : 0;

                rect = new Rectangle(left, top, width, height);
            }

            return rect;
        }
        /// <summary>
        /// Scrolls an element into view by ID
        /// <example>browser.MagicHelper.ScrollIntoView("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void ScrollIntoView(string elementId)
        {
            Client.ScrollIntoView(elementId).Execute();
        }

        /// <summary>
        /// Scrolls an element by horizontal and vertical pixels
        /// <example>browser.MagicHelper.ScrollElement(100, 200);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <param name="vPixels">The vertical pixels</param>
        public void ScrollElement(string elementId, int hPixels, int vPixels)
        {
            Client.ScrollElement(elementId).WithHPixels(hPixels).WithVPixels(vPixels).Execute();
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
        /// Get select menu option items
        /// </summary>
        /// <param name="elementId"></param>
        /// <returns></returns>
        public List<GPALElement> GetOptions(string elementId)
        {
            List<GPALElement> tmpElements = Client.GetOptions(elementId).Execute<List<GPALElement>>();
            SwitchToDefaultContent();
            return tmpElements;
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
        /// Gets the parent node of an element by ID
        /// <example>string parent = browser.MagicHelper.GetParentNode("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>The parent node</returns>
        public GPALElement GetParentNode(string elementId)
        {
            return Client.GetParentNode(elementId).Execute<List<GPALElement>>()[0];
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

        public void SetRange(string elementId, int rangeValue)
        {
            Client.SetRange(elementId).WithRangeValue(rangeValue).Execute();
        }

        /// <summary>
        /// Sets the value of a destination element from a source element, by CSS selector
        /// <example>browser.MagicHelper.SetValueFromElement("#source", "#dest");</example>
        /// </summary>
        /// <param name="srcSelector">CSS selector identifying the source element to read the value from</param>
        /// <param name="destElementId">CSS selector identifying the destination element whose value is set</param>
        public void SetValueFromElement(string srcSelector, string destElementId)
        {
            Client.SetValueFrom(destElementId).WithElementId(srcSelector).Execute();
        }

        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>browser.MagicHelper.LeftClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void LeftClick(string elementId)
        {
            Client.LeftClick(elementId).Execute();
            Thread.Sleep(500);
        }

        /// <summary>
        /// Performs a left double click on an element by ID
        /// <example>browser.MagicHelper.LeftDoubleClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void LeftDoubleClick(string elementId)
        {
            Client.LeftDoubleClick(elementId).Execute();
        }

        /// <summary>
        /// Performs a left click on an element by ID
        /// <example>browser.MagicHelper.LeftClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void MiddleClick(string elementId)
        {
            Client.MiddleClick(elementId).Execute();
            Thread.Sleep(500);
        }

        /// <summary>
        /// Performs a right click on an element by ID
        /// <example>browser.MagicHelper.RightClick("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void RightClick(string elementId)
        {
            Client.RightClick(elementId).Execute();
        }

        public void SelectClick(string elementId, dynamic indexOrValue)
        {
            if (indexOrValue is int idx)
                Client.SelectClick(elementId).WithSelectIndex(idx).Execute();
            else
                Client.SelectClick(elementId).WithSelectValue(indexOrValue).Execute();
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

        /// <summary>
        /// Checks if the page is at the end
        /// <example>bool end = browser.MagicHelper.IsEndOfPage();</example>
        /// </summary>
        /// <returns>Whether the page is at the end</returns>
        public bool IsEndOfPage()
        {
            return Client.IsEndOfPage().Execute<bool>();
        }

        /// <summary>
        /// Hovers over an element by ID
        /// <example>browser.MagicHelper.Hover("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void Hover(string elementId)
        {
            Client.Hover(elementId).Execute();
        }

        /// <summary>
        /// Drags an element by ID and drops it at an offset from its current position
        /// <example>browser.MagicHelper.DragAndDrop("element123", 100, 50);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="deltaX">The horizontal offset, in pixels, to drop the element</param>
        /// <param name="deltaY">The vertical offset, in pixels, to drop the element</param>
        /// <param name="offsetX">Horizontal grab offset, in pixels, within the element to start the drag from</param>
        /// <param name="offsetY">Vertical grab offset, in pixels, within the element to start the drag from</param>
        public void DragAndDrop(string elementId, int deltaX, int deltaY, int offsetX = 0, int offsetY = 0)
        {
            Client.DragAndDrop(elementId).WithDeltaX(deltaX).WithDeltaY(deltaY).WithOffsetX(offsetX).WithOffsetY(offsetY).Execute();
        }

        /// <summary>
        /// Gets the window inner height
        /// <example>int height = browser.MagicHelper.WindowInnerHeight();</example>
        /// </summary>
        /// <returns>The inner height</returns>
        public int WindowInnerHeight()
        {
            return Client.WindowInnerHeight().Execute<int>();
        }

        /// <summary>
        /// Gets the window inner width
        /// <example>int width = browser.MagicHelper.WindowInnerWidth();</example>
        /// </summary>
        /// <returns>The inner width</returns>
        public int WindowInnerWidth()
        {
            return Client.WindowInnerWidth().Execute<int>();
        }

        /// <summary>
        /// Gets the window outer height
        /// <example>int height = browser.MagicHelper.WindowOuterHeight();</example>
        /// </summary>
        /// <returns>The outer height</returns>
        public int WindowOuterHeight()
        {
            return Client.WindowOuterHeight().Execute<int>();
        }

        /// <summary>
        /// Gets the window outer width
        /// <example>int width = browser.MagicHelper.WindowOuterWidth();</example>
        /// </summary>
        /// <returns>The outer width</returns>
        public int WindowOuterWidth()
        {
            return Client.WindowOuterWidth().Execute<int>();
        }

        /// <summary>
        /// Sends an HTTP request from inside the page, with the browser's cookies, TLS fingerprint, header order
        /// and anti-bot clearance. A relative url resolves against the current page.
        /// <example>string json = browser.MagicHelper.Fetch("/api/search?page=1");</example>
        /// </summary>
        /// <param name="url">Url to request, relative or absolute</param>
        /// <param name="method">HTTP verb, GET when null</param>
        /// <param name="body">Request body, or null for none</param>
        /// <param name="contentType">Content type of the body</param>
        /// <param name="headers">Extra request headers, flattened to name, value, name, value</param>
        /// <param name="asBytes">True to bring the response body back base64 encoded, false for text</param>
        /// <returns>The response body, or null if the fetch could not be made</returns>
        public string Fetch(string url, string method = null, string body = null, string contentType = null, string[] headers = null, bool asBytes = false)
        {
            string retVal = Client
                .Fetch(url)
                .WithVerb(method)
                .WithBody(body)
                .WithContentType(contentType)
                .WithHeaders(headers)
                .WithBytes(asBytes)
                .Execute();

            // NOTOK means the request failed; return null, as the other engines do.
            return "NOTOK" == retVal || "null" == retVal ? null : retVal;
        }

        /// <summary>
        /// Gets the window page offset X
        /// <example>int offset = browser.MagicHelper.WindowPageOffsetX();</example>
        /// </summary>
        /// <returns>The page offset X</returns>
        public int WindowPageOffsetX()
        {
            // Firefox reports the scroll with a fraction (9581.150390625), so it is read as a decimal and rounded
            return (int)Math.Round(Client.WindowPageOffsetX().Execute<double>());
        }

        /// <summary>
        /// Gets the window page offset Y
        /// <example>int offset = browser.MagicHelper.WindowPageOffsetY();</example>
        /// </summary>
        /// <returns>The page offset Y</returns>
        public int WindowPageOffsetY()
        {
            // Firefox reports the scroll with a fraction, so it is read as a decimal and rounded
            return (int)Math.Round(Client.WindowPageOffsetY().Execute<double>());
        }

        /// <summary>
        /// Gets the bounding client rect of an element by ID
        /// <example>string rect = browser.MagicHelper.GetBoundingClientRect("element123");</example>
        /// </summary>
        /// <returns>The bounding client rect</returns>
        /// <summary>
        /// Whether a click at this viewport point would reach the element.
        /// </summary>
        /// <param name="elementId">The element that should receive the click</param>
        /// <param name="x">Viewport x</param>
        /// <param name="y">Viewport y</param>
        /// <returns>True when the topmost element at this viewport point is the element or a descendant</returns>
        public bool ElementFromPoint(string elementId, int x, int y)
        {
            return true == Client.ElementFromPoint(elementId, x, y).Execute<bool>();
        }

        /// <summary>
        /// Clicks a viewport point through the browser's own input pipeline, so the page reads the event as trusted.
        /// <example>browser.MagicHelper.TrustedLeftClick(120, 340);</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns>True when the click was made, false where the browser has no debugger to attach</returns>
        public bool TrustedLeftClick(int x, int y, int holdMs = 0)
        {
            return true == Client.TrustedLeftClick(x, y, holdMs).Execute<bool>();
        }

        /// <summary>
        /// Middle clicks a viewport point through the browser's own input pipeline, so the page reads the event as trusted.
        /// <example>browser.MagicHelper.TrustedMiddleClick(120, 340);</example>
        /// </summary>
        /// <param name="x">Viewport x to click</param>
        /// <param name="y">Viewport y to click</param>
        /// <param name="holdMs">Milliseconds to hold the button down, 0 for an ordinary click</param>
        /// <returns>True when the click was made, false where the browser has no debugger to attach</returns>
        public bool TrustedMiddleClick(int x, int y, int holdMs = 0)
        {
            return true == Client.TrustedMiddleClick(x, y, holdMs).Execute<bool>();
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
        /// Registers a script to run on every new document load. Persists until <see cref="ClearInjectedScripts"/> is called.
        /// <example>browser.MagicHelper.InjectScript("window.__hooked = true;");</example>
        /// </summary>
        /// <param name="script">The JavaScript code to inject</param>
        public void InjectScript(string script)
        {
            string answered = Client.InjectScript(script).Execute();

            // The extension returns the registered script id or an error. Warn on error, or a failed registration
            // goes unnoticed until the page misbehaves. Usually Allow user scripts is off.
            if (true == answered?.Contains("error"))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The extension did not register the script [{answered}]", this, GPALObjectType.Other);
        }

        /// <summary>
        /// Runs a script once on the current page and returns its result; <see cref="InjectScript"/> runs on every
        /// page load until cleared. Needs Allow user scripts on.
        /// <example>string title = browser.MagicHelper.ExecuteJavaScript("document.title");</example>
        /// </summary>
        /// <param name="script">The JavaScript code to run</param>
        /// <returns>What the script returned, as json</returns>
        public string ExecuteJavaScript(string script)
        {
            return Client.ExecuteJavaScript(script).Execute();
        }

        /// <summary>
        /// The HTTP status of the active tab's page, as the extension saw its response. 0 when the page has none: no
        /// response yet, or the navigation failed outright.
        /// <example>int status = browser.MagicHelper.GetStatusCode();</example>
        /// </summary>
        /// <returns>The page's HTTP status, or 0</returns>
        public int GetStatusCode()
        {
            int retVal = Client.GetStatusCode().Execute<int>();

            return retVal;
        }

        /// <summary>
        /// Returns timeOrigin and url, separated by a bar.<br/>
        /// Its own endpoint, because ExecuteJavaScript needs Allow user scripts on and this does not.
        /// <example>string stamp = browser.MagicHelper.GetDocumentStamp();</example>
        /// </summary>
        /// <returns>timeOrigin and url separated by a bar, or null when the page could not answer</returns>
        public string GetDocumentStamp()
        {
            string retVal = Client.GetDocumentStamp().Execute();

            // Strip the JSON quotes, or the closing quote stays on the url and every click looks like a navigation.
            if (true == retVal?.StartsWith("\"") && true == retVal.EndsWith("\"") && 1 < retVal.Length)
                retVal = Newtonsoft.Json.JsonConvert.DeserializeObject<string>(retVal);

            return retVal;
        }

        /// <summary>
        /// Page title, __NEXT_DATA__ and React Flight chunks as json.<br/>
        /// Read from the DOM rather than the HTML source, so no unescaping or HTML parsing.
        /// <example>string json = browser.MagicHelper.GetHydrationScripts();</example>
        /// </summary>
        /// <returns>Json holding title, nextData and flightChunks, or null when the page could not answer</returns>
        public string GetHydrationScripts()
        {
            return Client.GetHydrationScripts().Execute();
        }

        /// <summary>
        /// Removes all scripts previously registered via <see cref="InjectScript"/>.
        /// <example>browser.MagicHelper.ClearInjectedScripts();</example>
        /// </summary>
        public void ClearInjectedScripts()
        {
            Client.ClearInjectedScripts().Execute();
        }

        /// <summary>
        /// Checks if an element is clickable by ID
        /// NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
        /// someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the first result
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
        /// NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
        /// someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the first result
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
        /// NOTE: when called from GPAL, we will always provide one element or selector that retrieves one element
        /// someone rolling their own and calling this endpoint outside gpal could result in multiple elements, so return the first result
        /// <example>bool enabled = browser.MagicHelper.IsEnabled("element123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <returns>Whether the element is clickable</returns>
        public bool IsEnabled(string elementId)
        {
            return Client.IsEnabled(elementId).Execute<bool>();
        }
        /// <summary>
        /// Clears the referrer header, and the browser's referrer, so later GoTo and Get calls send none
        /// <example>browser.MagicHelper.ClearReferrer();</example>
        /// </summary>
        public void ClearReferrer()
        {
            ((Browser)browser).BrowserSettings.Referrer = null;
            Client.ClearReferrer().Execute();
        }

        /// <summary>
        /// Evaluates an XPath expression to locate a single element
        /// <example>string elementId = browser.MagicHelper.Evaluate("//div[@id='example']");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>The element ID</returns>
        public GPALElement Evaluate(string xpath)
        {
            return Client.Evaluate(xpath).Execute<GPALElement>();
        }

        /// <summary>
        /// Evaluates an XPath expression to locate multiple elements
        /// <example>string[] elementIds = browser.MagicHelper.EvaluateAll("//div");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> EvaluateAll(string xpath)
        {
            return Client.EvaluateAll(xpath).Execute<List<GPALElement>>();
        }
        /// <summary>
        /// Evaluates an XPath expression to locate a single element
        /// <example>string elementId = browser.MagicHelper.Evaluate("//div[@id='example']");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>The element ID</returns>
        public GPALElement EvaluatePersistent(string xpath)
        {
            return Client.EvaluatePersistent(xpath).Execute<GPALElement>();
        }

        /// <summary>
        /// Evaluates an XPath expression to locate multiple elements
        /// <example>string[] elementIds = browser.MagicHelper.EvaluateAll("//div");</example>
        /// </summary>
        /// <param name="xpath">The XPath expression</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> EvaluateAllPersistent(string xpath)
        {
            return Client.EvaluateAllPersistent(xpath).Execute<List<GPALElement>>();
        }

        /// <summary>
        /// Gets the user agent
        /// <example>string userAgent = browser.MagicHelper.GetUserAgent();</example>
        /// </summary>
        /// <returns>The user agent string</returns>
        public string GetUserAgent()
        {
            return Client.GetUserAgent().Execute();
        }

        /// <summary>
        /// Gets the browser's preferred languages, most preferred first
        /// <example>string languages = browser.MagicHelper.GetLanguages();</example>
        /// </summary>
        /// <returns>The languages, comma separated</returns>
        public string GetLanguages()
        {
            return Client.GetLanguages().Execute();
        }

        /// <summary>
        /// Starts or stops recording network requests. The extension uses webRequest, so it records every request,
        /// not only page script's, across navigations until stopped.
        /// <example>browser.MagicHelper.CaptureCalls(true, "/api/search");</example>
        /// </summary>
        /// <param name="capture">True to record, false to stop</param>
        /// <param name="urlFragment">Record only urls holding this, or null for everything</param>
        /// <param name="clear">True to forget what was already recorded before starting</param>
        public void CaptureCalls(bool capture = true, string urlFragment = null, bool clear = false)
        {
            IAllowRESTCaptureOptions capturing = Client.CaptureCalls(capture);

            if (false == string.IsNullOrWhiteSpace(urlFragment)) capturing.WithCallFilter(urlFragment);
            if (true == clear) capturing.WithClearCalls(true);

            capturing.Execute();
        }

        /// <summary>
        /// Everything recorded so far, oldest first.
        /// <example>string json = browser.MagicHelper.GetCapturedCalls();</example>
        /// </summary>
        /// <returns>The recorded calls as json, holding capturing, filter, dropped and calls</returns>
        public string GetCapturedCalls()
        {
            return Client.GetCapturedCalls().Execute();
        }

        /// <summary>
        /// Append text to an element
        /// <example>browser.MagicHelper.FillInAppend("input123", "Hello World");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="text">The text to input</param>
        public void FillInAppend(string elementId, string text)
        {
            Client.FillInAppend(elementId).WithText(text).Execute();
        }

        /// <summary>
        /// Inserts text to an element (prolly the same as append for the moment)
        /// <example>browser.MagicHelper.FillInInsert("input123", "Hello World");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="text">The text to input</param>
        public void FillInInsert(string elementId, string text)
        {
            Client.FillInInsert(elementId).WithText(text).Execute();
        }

        /// <summary>
        /// Append text to an element
        /// <example>browser.MagicHelper.FillInOverwrite("input123", "Hello World");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="text">The text to input</param>
        public void FillInOverwrite(string elementId, string text)
        {
            Client.FillInOverwrite(elementId).WithText(text).Execute();
        }

        /// <summary>
        /// Performs a left click and download on an element by ID
        /// <example>browser.MagicHelper.LeftClickAndDownload("link123", filenameAndPath);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="filenameAndPath">Where the file the click downloads is to be saved; the extension is given its filename, see <see cref="WithDownloadFile"/></param>
        public void LeftClickAndDownload(string elementId, string filenameAndPath)
        {
            Client.LeftClickAndDownload(elementId).WithDownloadFile(Path.GetFileName(filenameAndPath)).Execute();
        }

        /// <summary>
        /// Has the browser download a url itself: no tab opens and no Save As dialog is shown, whatever the profile
        /// says about asking where to save.
        /// <example>browser.MagicHelper.DownloadUrl("https://example.com/file.zip", filenameAndPath);</example>
        /// </summary>
        /// <param name="url">The url to download</param>
        /// <param name="filenameAndPath">Where the file is to be saved; the extension is given its filename, see <see cref="WithDownloadFile"/></param>
        public void DownloadUrl(string url, string filenameAndPath)
        {
            Client.DownloadUrl(url).WithDownloadFile(Path.GetFileName(filenameAndPath)).Execute();
        }

        /// <summary>
        /// Performs a left click and upload on an element by ID
        /// <example>browser.MagicHelper.LeftClickAndUpload("link123", filename);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="filenameAndPath">Full path of the file to upload</param>
        public void LeftClickAndUpload(string elementId, string filenameAndPath)
        {
            Client.LeftClickAndUpload(elementId).WithUploadFile(filenameAndPath).Execute();
        }

        /// <summary>
        /// Performs a left click and upload on an element by ID
        /// <example>browser.MagicHelper.LeftClickAndUpload("link123", filename);</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        /// <param name="filenamesAndPaths">The file or files to upload</param>
        public void LeftClickAndUpload(string elementId, GPALFile filenamesAndPaths)
        {
            Client.LeftClickAndUpload(elementId).WithUploadFiles(filenamesAndPaths).Execute();
        }


        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>string elementId = browser.MagicHelper.QuerySelector(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>The element ID</returns>
        public GPALElement QuerySelector(string css)
        {
            var elem = Client.QuerySelector(css).Execute<GPALElement>();
            return elem;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>string[] elementIds = browser.MagicHelper.QuerySelectors(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> QuerySelectors(string css)
        {
            return Client.QuerySelectors(css).Execute<List<GPALElement>>();
        }

        /// <summary>
        /// Queries a single element using a CSS selector
        /// <example>string elementId = browser.MagicHelper.QuerySelector(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>The element ID</returns>
        public GPALElement QueryPersistentSelector(string css)
        {
            var persist = Client.QueryPersistentSelector(css).Execute<GPALElement>();

            return persist;
        }

        /// <summary>
        /// Queries multiple elements using a CSS selector
        /// <example>string[] elementIds = browser.MagicHelper.QuerySelectors(".example");</example>
        /// </summary>
        /// <param name="css">The CSS selector</param>
        /// <returns>Array of element IDs</returns>
        public List<GPALElement> QueryPersistentSelectors(string css)
        {
            return Client.QueryPersistentSelectors(css).Execute<List<GPALElement>>();
        }
        /// <summary>
        /// Scrolls the window by horizontal and vertical pixels
        /// <example>browser.MagicHelper.ScrollWindow(100, 200);</example>
        /// </summary>
        /// <param name="hPixels">The horizontal pixels</param>
        /// <param name="vPixels">The vertical pixels</param>
        public void ScrollWindow(int hPixels, int vPixels)
        {
            // Note: Requires RESTClient to implement ScrollWindow, or use ExecuteJavaScript as fallback
            Client.ScrollWindow(hPixels, vPixels).Execute();
        }

        /// <summary>
        /// Sends a single key
        /// <example>browser.MagicHelper.SendKey("Enter");</example>
        /// </summary>
        /// <param name="vkcode">The Windows virtual-key code of the key to send</param>
        public void SendKey(byte vkcode)
        {
            Client.SendKey(vkcode).Execute();
        }

        /// <summary>
        /// Sends a string of text
        /// <example>browser.MagicHelper.SendString("Hello World");</example>
        /// </summary>
        /// <param name="text">The text to send</param>
        /// <param name="delayMs">Inter-character pacing delay in milliseconds, forwarded to OttoMagic as "typingDelay". 0 sends the whole string atomically.</param>
        public void SendString(string text, int delayMs = 0)
        {
            Client.SendString(text, delayMs).Execute();
        }


        /// <summary>
        /// Switches to the default content
        /// <example>browser.MagicHelper.SwitchToDefaultContent();</example>
        /// </summary>
        public void SwitchToDefaultContent()
        {
            Client.SwitchToDefaultContent().Execute();
        }

        /// <summary>
        /// Switches to a shadow root by element ID
        /// <example>browser.MagicHelper.SwitchToShadowRoot("shadow123");</example>
        /// </summary>
        /// <param name="elementId">The element ID</param>
        public void SwitchToShadowRoot(string elementId)
        {
            Client.SwitchToShadowRoot(elementId).Execute();
        }

        #region Window Handling
        public WindowTuple OpenWindow(GPALUrl URL = null)
        {
            if (null == URL)
                URL = new GPALUrl();

            URL.ForUrl(GetFullUrl(URL.Url, browser, out ((Browser)browser)._areRobotsAllowed));

            WindowTuple retVal = Client.OpenWindow(URL.Url).Execute<WindowTuple>();
            browser.ServerResponseCode = GetStatusCode();

            return retVal;
        }

        public WindowTuple GoToWindow(dynamic urlOrId)
        {
            if (urlOrId is string v)
                return Client.GoToWindow(v).Execute<WindowTuple>();
            else
                return Client.GoToWindow(urlOrId.ToString()).Execute<WindowTuple>();
        }
        /// <summary>
        /// Closes the browser by closing all its windows, the only way an extension can quit it.
        /// <example>browser.MagicHelper.CloseBrowser();</example>
        /// </summary>
        /// <returns>The reply, sent first, since closing the last window stops the extension</returns>
        public string CloseBrowser()
        {
            return Client.CloseBrowser().Execute();
        }

        public WindowTuple CloseWindow(dynamic urlOrId)
        {
            if (urlOrId is string v)
                return Client.CloseWindow(v).Execute<WindowTuple>();
            else
                return Client.CloseWindow(urlOrId.ToString()).Execute<WindowTuple>();
        }

        public WindowTuple GetCurrentWindow()
        {
            return Client.GetCurrentWindow().Execute<WindowTuple>();
        }
        #endregion Window Handling
        #region GPAL Studio
        /// <summary>
        /// Gets browser settings
        /// <example>string settings = browser.MagicHelper.GetBrowserSettings();</example>
        /// </summary>
        /// <returns>The browser settings as a string</returns>
        public string GetBrowserSettings()
        {
            return Client.GetBrowserSettings().Execute();
        }

        /// <summary>
        /// Gets GPAL settings
        /// <example>string settings = browser.MagicHelper.GetGpalSettings();</example>
        /// </summary>
        /// <returns>The GPAL settings as a string</returns>
        public string GetGpalSettings()
        {
            return Client.GetGpalSettings().Execute();
        }

        /// <summary>
        /// Gets general settings
        /// <example>string settings = browser.MagicHelper.GetSettings();</example>
        /// </summary>
        /// <returns>The general settings as a string</returns>
        public string GetSettings()
        {
            return Client.GetSettings().Execute();
        }

        /// <summary>
        /// Gets the workflow
        /// <example>string workflow = browser.MagicHelper.GetWorkflow();</example>
        /// </summary>
        /// <returns>The workflow as a string</returns>
        public string GetWorkflow()
        {
            return Client.GetWorkflow().Execute();
        }
        #endregion GPAL Studio
        #endregion Commands
        #region Helpers
        /// <summary>
        /// Launches a browser with given settings
        /// <example>Process p = browser.MagicHelper.LaunchBrowser(new BrowserSettings(...), "https://example.com");</example>
        /// </summary>
        /// <returns>The browser process</returns>
        /// <summary>
        /// Builds a Unicode environment block for CreateProcess: this process's environment plus one variable. The
        /// caller frees it with Marshal.FreeHGlobal and passes CREATE_UNICODE_ENVIRONMENT.
        /// </summary>
        /// <param name="name">Variable to add or replace</param>
        /// <param name="value">Its value</param>
        /// <returns>The block, double null terminated</returns>
        internal static IntPtr EnvironmentBlockWith(string name, string value)
        {
            SortedDictionary<string, string> variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
                variables[(string)variable.Key] = (string)variable.Value;

            variables[name] = value;

            StringBuilder block = new StringBuilder();

            foreach (KeyValuePair<string, string> variable in variables)
                block.Append(variable.Key).Append('=').Append(variable.Value).Append('\0');

            block.Append('\0');

            return Marshal.StringToHGlobalUni(block.ToString());
        }

        /// <summary>
        /// Publishes each browser stdout or stderr line as DEEPDEBUG, keeping it out of normal output. A null line
        /// at end of stream is not published.
        /// </summary>
        /// <param name="browserSettings">Settings of the browser that wrote the line</param>
        /// <param name="data">The received line</param>
        static void PublishBrowserOutput(BrowserSettings browserSettings, DataReceivedEventArgs data)
        {
            if (null != data.Data)
                GPAL.PublishSimpleEvent(GPALEventType.DEEPDEBUG, $"[{browserSettings.BrowserType}] {data.Data}", browserSettings, GPALObjectType.Other);
        }
        /// <summary>
        /// firefox.exe from a command line starts a child and exits, and the child owns the window. Wait for the
        /// child and store it in browserSettings.Process; if none appears before the deadline, keep the launcher.
        /// </summary>
        /// <param name="browserSettings">The settings holding the process to swap</param>
        /// <param name="launcherPid">The process id the launch started</param>
        /// <param name="deadline">When to stop waiting, the same one the window wait uses</param>
        private static void AdoptFirefoxChild(BrowserSettings browserSettings, int launcherPid, DateTime deadline)
        {
            if (1 > launcherPid) return;

            while (DateTime.UtcNow < deadline)
            {
                int childPid = 0;

                try
                {
                    using (var searcher = new ManagementObjectSearcher(
                        $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {launcherPid} AND Name = 'firefox.exe'"))
                    {
                        foreach (ManagementObject found in searcher.Get())
                        {
                            childPid = Convert.ToInt32(found["ProcessId"]);
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    // A WMI failure leaves the launcher process in place
                    return;
                }

                if (0 != childPid)
                {
                    try
                    {
                        browserSettings.Process = System.Diagnostics.Process.GetProcessById(childPid);

                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"FireFox launcher [{launcherPid}] handed over to browser process [{childPid}]", browserSettings, GPALObjectType.Browser);

                        return;
                    }
                    catch (Exception)
                    {
                        // Exited between being listed and being opened
                    }
                }

                Thread.Sleep(100);
            }
        }

        public Process LaunchBrowser(Browser browser, GPALUrl URL, List<string> additionalArguments = null)
        {
            BrowserSettings browserSettings = browser.BrowserSettings;
            bool created = false;
            int secondsToWait = 30;
            TimeSpan timeout = TimeSpan.FromSeconds(secondsToWait);

            if (null == browserSettings)
            {
                // this should only be called when we are using Oauth credentials, to log someone in? we could require a browser passed into credentials, but why?
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "BrowserSettings parameter is null. Using chrome puppeteer port.", null, GPALObjectType.Other);
                browser = (Browser)GPAL.Browser.ToGPALObject();
                browserSettings = ((Browser)browser).BrowserSettings;
            }

            // Delete temp profiles left behind by killed runs. Runs on every launch, including ones with a supplied
            // profile.
            ChromeProfileManager.SweepAbandonedTempProfiles();

            if (null == URL || null == URL?.Url)
                URL = "about:blank";

            URL.ForUrl(GetFullUrl(URL?.Url, browser, out ((Browser)browser)._areRobotsAllowed, true));  // on launch we open to about:blank, don't do a robots.txt check on launch

            if (false == ((Browser)browser).AreRobotsAllowed && true == ((Browser)browser).BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Visiting [{URL?.Url}] is disallowed by robots.txt and your request to honor it. Not going to URL. Workflow will fail.", browser, GPALObjectType.Browser);
                URL.ForUrl("https://google.com");
            }

            bool headless = browserSettings.UseHeadless;
            var chromeArguments = additionalArguments ?? new List<string>();
            string browserPath;

            // Get browser executable path
            switch (browserSettings.BrowserType)
            {
                case BrowserType.Chrome:
                    browserPath = Puppeteer.GetChromePath();
                    break;
                case BrowserType.Edge:
                    browserPath = Puppeteer.GetEdgePath();
                    break;
                case BrowserType.FireFox:
                    browserPath = Puppeteer.GetFirefoxPath();
                    break;
                default:
                    Exception ex = new Exception("Unsupported Browser Type");
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported browser type [{browserSettings.BrowserType}]", browserSettings, GPALObjectType.Other, ex);
                    return null;
            }

            if (string.IsNullOrEmpty(browserPath) || false == File.Exists(browserPath))
            {
                string msg = $"[{browserSettings.BrowserType}] executable not found at [{browserPath}]";
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, browserSettings, GPALObjectType.Other);
                throw new NotFoundException(msg);
            }

            // Common arguments for Chrome/Edge
            if (browserSettings.BrowserType == BrowserType.Chrome || browserSettings.BrowserType == BrowserType.Edge)
            {
                string userAgent = null;

                // Profile settings
                if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                {
                    browserSettings.ProfileDataDirectory = BrowserHelper.GetBrowserProfileDirectory(browserSettings);
                    if (false == string.IsNullOrEmpty(browserSettings.ProfileName))
                    {
                        // FindProfileDirectory returns a directory or throws
                        // Quoted, as --user-data-dir below is. These arguments are joined into one command
                        // line and Chrome directory names contain spaces, so --profile-directory=Profile 4
                        // would split into a different profile plus a bare 4 that Chrome opens as a url
                        chromeArguments.Add($@"--profile-directory=""{BrowserHelper.FindProfileDirectory(browserSettings.ProfileDataDirectory, browserSettings.ProfileName)}""");
                    }
                }
                else if (true == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                {
                    browserSettings.ProfileDataDirectory = ChromeProfileManager.CreateTempUserProfile(
                        browserSettings.DownloadLocation ?? FileHelper.GetDefaultDownloadDirectory(browser),
                        browserSettings.PromptForDownload,
                        browserSettings.OpenPDFExternally,
                        browserSettings.LoadImages,
                        browserSettings.UseOttoMagic
                    );
                    browserSettings.TempProfileCreated = true;

                    // Chrome and Edge keep an extension only when it is installed through the browser, so a temp
                    // profile has no OttoMagic.
                    if (true == browserSettings.UseOttoMagic)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"OttoMagic needs a profile with it installed. [{browserSettings.BrowserType}] on a temporary profile has no OttoMagic. Give the workflow a profile, or use PuppeteerPort.", browser, GPALObjectType.Browser);
                }

                // Download prompt, download location and the PDF viewer are profile preferences the extension
                // cannot set, and there is no CDP here, so write them into the supplied profile. Browser.Close
                // restores them. Selenium and Puppeteer set these in BrowserHelper; Firefox in CreateUserJs below.
                if (false == browserSettings.TempProfileCreated && false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                    browserSettings.PreviousDownloadPreferences = ChromeProfileManager.ApplyDownloadPreferences(
                        browserSettings.ProfileDataDirectory,
                        browserSettings.DownloadLocation,
                        browserSettings.PromptForDownload,
                        browserSettings.OpenPDFExternally);

                // A profile can be open in only one browser. Launching onto an open one hands the command line to
                // it and exits, so the extension never loads and GPALRestAPI never reports a port. Refuse here, as
                // Selenium does.
                if (false == browserSettings.TempProfileCreated)
                    BrowserHelper.RefuseIfProfileIsOpen(browserSettings);

                // Set here, ahead of the arguments, so the browser starts with --remote-debugging-pipe
                if (AutomationEngine.PuppeteerPipe == browserSettings.AutomationEngine || AutomationEngine.PuppeteerPipeHW == browserSettings.AutomationEngine)
                    browserSettings.DebugPipe = true;

                // Debugging
                if (true == browserSettings.UsePuppeteer)
                {
                    if (true == browserSettings.DebugPipe)
                    {
                        // No --enable-unsafe-extension-debugging: GPAL sends no Extensions commands, and Chrome warns
                        // about the flag on screen. Port 0 has Chrome write the port it chose to DevToolsActivePort,
                        // where the protocol description is read
                        chromeArguments.Add("--remote-debugging-pipe --remote-debugging-port=0");

                        // Driven over a pipe, Chrome and Edge report navigator.webdriver true, which bot protection
                        // turns away; over a port they report false. The AutomationControlled switch keeps it false.
                        // Chrome puts a warning bar on the page for that switch, which shortens the page, and
                        // --test-type, which ChromeDriver passes on every launch, keeps the bar off
                        chromeArguments.Add("--disable-blink-features=AutomationControlled");
                        chromeArguments.Add("--test-type");
                    }
                    else
                    {
                        // Pick a free port; with a fixed 0xdead two browsers launched together share one session.
                        if (null == browserSettings.DebugPort)
                            browserSettings.DebugPort = BrowserHelper.FindFreePort();

                        browserSettings.PuppeteerUrl = $"http://localhost:{browserSettings.DebugPort}";
                        chromeArguments.Add($"--remote-debugging-port={browserSettings.DebugPort.Value}");
                    }

                    // Back and Forward load the page again instead of restoring it from the back/forward cache. Chrome
                    // holds a command sent to a restored page, such as a screenshot, until it draws, which after Back
                    // was never. Playwright launches Chromium this way for the same reason.
                    chromeArguments.Add("--disable-back-forward-cache");

                    // *******************************************************************
                    // NOTE: CAVEAT: suppose a time comes to run with some sort of extension, this isn't causing any harm, just annoying...
                    // not keen on disabling extensions, they make the bot look alive, but somehow we score low on recaptcha with ottomagic, no idea why, puppeteer is awesome
                    // *******************************************************************
                    // chromeArguments.Add("--disable-extensions");    // no ottomagic
                    // *******************************************************************
                    // *******************************************************************
                }

                // set windowsize if provided or not - new
                if (false == browserSettings.FullScreen &&
                    false == browserSettings.Maximize &&
                    false == browserSettings.Minimize)
                {
                    if (false == browserSettings.WindowSize.IsEmpty)
                    {
                        // explicit WindowSize provided - must set screen-info in headless=new
                        // otherwise CDP interactions fail with "Browser window not found"
                        chromeArguments.Add($"--window-size={browserSettings.WindowSize.Width},{browserSettings.WindowSize.Height}");

                        // WindowSize also holds the position, so several browsers can sit side by side. 0,0 is not
                        // sent.
                        if (0 != browserSettings.WindowSize.X || 0 != browserSettings.WindowSize.Y)
                            chromeArguments.Add($"--window-position={browserSettings.WindowSize.X},{browserSettings.WindowSize.Y}");
                    }
                    else if (browserSettings.UseHeadless)
                        chromeArguments.Add("--start-fullscreen");
                }
                else if (true == browserSettings.FullScreen)
                    chromeArguments.Add("--start-fullscreen");
                else if (true == browserSettings.Maximize)
                    chromeArguments.Add("--start-maximized");

                // Anti-bot detection
                //if (true == myBrowserSettings.UsePuppeteer || AutomationEngine.PuppeteerPortHW == myBrowserSettings.AutomationEngine)
                //{
                // chromeArguments.Add("--disable-blink-features=AutomationControlled");
                chromeArguments.Add("--excludeSwitches=enable-automation");
                chromeArguments.Add("--useAutomationExtension=false");
                //}

                // Core arguments to suppress popups and stabilize CDP
                chromeArguments.Add("--no-first-run");
                chromeArguments.Add("--no-default-browser-check");
                //chromeArguments.Add("--no-default-browser-check");
                //chromeArguments.Add("--disable-default-apps");
                //chromeArguments.Add("--disable-features=TranslateUI,FirstRun,ChromeWhatsNewUI,InterestCohort");
                //chromeArguments.Add("--disable-background-networking");
                //chromeArguments.Add("--disable-background-timer-throttling");
                //chromeArguments.Add("--disable-renderer-backgrounding");
                //chromeArguments.Add("--disable-backgrounding-occluded-windows");
                //chromeArguments.Add("--no-service-autorun");
                //chromeArguments.Add("--disable-client-side-phishing-detection");
                chromeArguments.Add("--disable-dev-shm-usage");

                // Edge signs a new profile into the Windows account and syncs favourites, settings and extensions,
                // each of which opens a page on install. Turn sync off for temp profiles only.
                if (true == browserSettings.TempProfileCreated)
                {
                    chromeArguments.Add("--disable-sync");
                    chromeArguments.Add("--disable-signin-promo");
                }

                // avoid chrome overlays on the rendered area
                chromeArguments.Add("--disable-infobars");
                chromeArguments.Add("--disable-session-crashed-bubble");
                chromeArguments.Add("--hide-crash-restore-bubble");
                chromeArguments.Add("--noerrdialogs");
                // Chromium honors only the last --disable-features switch, so build one. Edge shopping content
                // scripts keep changing retail pages, which hangs DOM.getDocument, so those features are disabled
                // below.
                string disableFeatures = "TranslateUI,Translate";

                // Temp profiles only: turn off the 46 MB of on-device models a new profile downloads in its first
                // 30 seconds.
                if (true == browserSettings.TempProfileCreated)
                    disableFeatures += ",OptimizationGuideModelDownloading,OptimizationHintsFetching,OptimizationTargetPrediction,OptimizationHints";

                if (BrowserType.Edge == browserSettings.BrowserType)
                {
                    disableFeatures += ",msShoppingTrigger,msShopping,msEdgeShoppingUI,msEdgeShoppingList";

                    // Edge implicit sign-in, which --disable-sync does not cover
                    if (true == browserSettings.TempProfileCreated)
                        disableFeatures += ",msImplicitSignin,msEdgeImplicitSignin,msIdentityImplicitSignin";
                }

                chromeArguments.Add($"--disable-features={disableFeatures}");
                chromeArguments.Add("--load-media-router-component-extension");
                chromeArguments.Add("--enable-media-router");
                chromeArguments.Add("--force-discovery");
    
                if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                    chromeArguments.Add($@"--user-data-dir=""{browserSettings.ProfileDataDirectory.Replace("\\", "/")}""");

                // Same switch the Selenium options path adds. On the command line so it works with any profile; the
                // extension and Puppeteer cannot set it after launch.
                chromeArguments.Add($"--blink-settings=imagesEnabled={browserSettings.LoadImages.ToString().ToLower()}");

                // Headless mode
                if (headless)
                {
                    chromeArguments.Add("--headless=new");
                    chromeArguments.Add("--disable-notifications");
                    chromeArguments.Add("--disable-gpu");
                    if (AutomationEngine.PuppeteerPipe == browserSettings.AutomationEngine || AutomationEngine.PuppeteerPipeHW == browserSettings.AutomationEngine)
                        chromeArguments.Add("--no-sandbox"); // REQUIRED FOR PIPE IN HEADLESS + TEMP PROFILE

                    // Fetch user-agent
                    userAgent = GetUserAgent(browserSettings, chromeArguments);

                    userAgent = userAgent?.Trim('"');

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Headless [{browserSettings.BrowserType}] using UserAgent [{userAgent}]", browserSettings, GPALObjectType.Other);

                    chromeArguments.Add($"--user-agent=\"{userAgent}\"");
                }
                else
                {
                    if (false == browserSettings.BlockPopUps)
                        chromeArguments.Add("--disable-popup-blocking");
                }

                // Dark mode
                if (browserSettings.StealthType.HasFlag(StealthType.DarkMode))
                    chromeArguments.Add("--force-dark-mode");

                // Safe browsing - ads latency to each browse
                //chromeArguments.Add("--safebrowsing.enabled");

                // full accessibility for using UIAutomation (it is a fallback for method: GetWindowRectangle to get the content dimensions
                // not needed, we don't use uiautomation to calculate browser coords
                // this creates extra work = slower browser response
                // chromeArguments.Add("--force-renderer-accessibility");
            }
            else if (browserSettings.BrowserType == BrowserType.FireFox)
            {
                // Firefox arguments
                var firefoxArguments = new List<string>();

                if (headless)
                {
                    firefoxArguments.Add("-headless");
                    firefoxArguments.Add($"-width={Puppeteer.GetScreenWidth()}");
                    firefoxArguments.Add($"-height={Puppeteer.GetScreenHeight()}");
                    firefoxArguments.Add("--disable-notifications");
                }
                // Headful Firefox gets no extra arguments; OttoMagic drives it through GPALRestAPI and needs no
                // debugging port. No --websocket-port: Firefox ignores the flag and opens the port number as a url.

                // Call firefoxhelper.CreateUserJs to load set these preferences
                // firefox has a unique requirement for loading a browser extension on demand
                // we have to create an .xpi file and get the extension id, then save it to a profile dir
                // then specify that profile dir
                if (true == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                {
                    browserSettings.ProfileDataDirectory = FirefoxProfileManager.CreateTempProfileDirectory();

                    // Mark the profile temporary, as the Chromium path does, so Browser.Close deletes it.
                    browserSettings.TempProfileCreated = true;

                    if (true == browserSettings.UseOttoMagic)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"OttoMagic needs a profile with it installed. [{browserSettings.BrowserType}] on a temporary profile has no OttoMagic. Give the workflow a profile, or use Selenium.", browser, GPALObjectType.Browser);
                }

                // As with Chromium, one browser per profile. A Firefox already on it takes the launch and this run
                // waits for a browser that never starts.
                if (false == browserSettings.TempProfileCreated)
                    BrowserHelper.RefuseIfProfileIsOpen(browserSettings);

                // Write the preferences to every profile. Snapshot a supplied profile first, for Browser.Close to
                // restore; a temp profile is deleted.
                if (false == browserSettings.TempProfileCreated)
                    browserSettings.PreviousFirefoxPreferences = FirefoxProfileManager.TakeSnapshot(browserSettings.ProfileDataDirectory);

                FirefoxProfileManager.CreateUserJs(browserSettings.ProfileDataDirectory, browserSettings);
                FirefoxProfileManager.NoteWrittenPreferences(browserSettings.PreviousFirefoxPreferences);

                if (false == string.IsNullOrEmpty(browserSettings.ProfileUserName) || false == string.IsNullOrEmpty(browserSettings.ProfileName))
                    firefoxArguments.Add($"-profile \"{(browserSettings.ProfileName ?? browserSettings.ProfileUserName).TrimEnd('\\')}\"");
                else if (false == string.IsNullOrEmpty(browserSettings.ProfileDataDirectory))
                    firefoxArguments.Add($"-profile \"{browserSettings.ProfileDataDirectory.TrimEnd('\\')}\"");

                chromeArguments = firefoxArguments; // Reuse chromeArguments for Process.Start
            }

            bool holdingHandshakeGate = false;   // set when this launch owns the OttoMagic port handshake
            UdpClient udpHandshake = null;       // out here so a launch that throws before the handshake still closes it

            // Launch browser
            try
            {
                // we will do a goto. for various reason this might not work and for various reasons this might work better

                // NOTE: we can't go to the real url on lauch, we have to apply stealth first :(
                if (null != URL && false == string.IsNullOrEmpty(URL.Url))
                    chromeArguments.Add(URL?.Url);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Running workflow for Automation Engine [{browserSettings.AutomationEngine}]", null, GPALObjectType.None);

                if (true == browserSettings.UseOttoMagic)
                {
                    // Wait until any launch in progress has its port before opening the UDP socket.
                    // AbandonedMutexException means the holder died and this launch now owns the mutex.
                    try { holdingHandshakeGate = ottoHandshakeGate.WaitOne(OttoHandshakeGateTimeoutMs); }
                    catch (AbandonedMutexException) { holdingHandshakeGate = true; }

                    if (false == holdingHandshakeGate)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Waited [{OttoHandshakeGateTimeoutMs}] ms for another OttoMagic browser to finish its port handshake. Launching anyway, this browser may not get a port of its own.", browserSettings, GPALObjectType.Browser);

                    udpHandshake = new UdpClient(47623);
                    udpHandshake.Client.ReceiveTimeout = 30000;

                    KeepFromChildren(udpHandshake);
                }

                int firefoxLauncherPid = 0;

                if (true == browserSettings.DebugPipe)
                {
                    var sa = new Native.SECURITY_ATTRIBUTES
                    {
                        nLength = Marshal.SizeOf<Native.SECURITY_ATTRIBUTES>(),
                        bInheritHandle = 1,
                        lpSecurityDescriptor = IntPtr.Zero
                    };

                    IntPtr hProcess = IntPtr.Zero;
                    IntPtr reservedPtr = IntPtr.Zero;
                    AnonymousPipeServerStream stdinPipe = null, stdoutPipe = null, stderrPipe = null;

                    try
                    {
                        // Create anonymous pipes. bInheritHandle is set to true via HandleInheritability.Inheritable
                        stdinPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable, 1048576);
                        stdoutPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable, 1048576);
                        stderrPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable, 1048576);

                        // Build command line. On Windows, Chrome's pipe mode takes the handles it reads commands from
                        // and writes replies to through --remote-debugging-io-pipes, read handle first. The browser
                        // inherits them with the same values.
                        var chromeCommandLine = new StringBuilder();
                        chromeCommandLine.Append($@"""{browserPath}""");
                        chromeCommandLine.Append(" ");
                        chromeCommandLine.Append(string.Join(" ", chromeArguments));
                        chromeCommandLine.Append($" --remote-debugging-io-pipes={stdinPipe.GetClientHandleAsString()},{stdoutPipe.GetClientHandleAsString()}");

                        // Create process
                        var startupInfo = new Native.STARTUPINFO
                        {
                            cb = (uint)Marshal.SizeOf<Native.STARTUPINFO>(),
                            // Set the desktop explicitly, since this launch calls CreateProcess directly. null
                            // means the caller's desktop.
                            lpDesktop = true == browserSettings.HiddenDesktop ? HiddenDesktop.StartupNameFor(browserSettings.HiddenDesktopName) : null,
                            dwFlags = Native.STARTF_USESHOWWINDOW | Native.STARTF_USESTDHANDLES, // Use standard handles flag
                            wShowWindow = (ushort)(headless ? Native.SW_HIDE : Native.SW_SHOWNORMAL),
                            hStdInput = IntPtr.Zero,
                            hStdOutput = IntPtr.Zero,
                            hStdError = stderrPipe.ClientSafePipeHandle.DangerousGetHandle(), // Assign to standard error
                        };

                        // Call CreateProcess with bInheritHandles set to true
                        created = Native.CreateProcess(
                            null,
                            chromeCommandLine.ToString(),
                            IntPtr.Zero,
                            IntPtr.Zero,
                            true, // Inherit handles
                            0,
                            IntPtr.Zero,
                            browserSettings.ProfileDataDirectory,
                            ref startupInfo,
                            out var processInfo2);

                        if (!created)
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "PuppeteerPipe CreateProcess failed.");

                        hProcess = processInfo2.hProcess;

                        // The browser holds its own copies of its pipe ends now. Closing GPAL's lets a read see the
                        // browser close its end.
                        stdinPipe.DisposeLocalCopyOfClientHandle();
                        stdoutPipe.DisposeLocalCopyOfClientHandle();

                        // Assign streams
                        browserSettings.InboundPipe = stdinPipe;
                        browserSettings.OutboundPipe = stdoutPipe;
                        browserSettings.ErrorPipe = stderrPipe;

                        // Capture stderr
                        string stderr = "";
                        var errorReader = new StreamReader(stderrPipe, Encoding.UTF8, true, 4096, false);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                char[] buffer = new char[4096];
                                int read;
                                while ((read = await errorReader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    stderr += new string(buffer, 0, read);
                                }
                                if (Native.GetExitCodeProcess(hProcess, out uint finalExitCode) && finalExitCode != 0)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Chrome exited with code: [{finalExitCode}], Stderr: [{stderr}]", browserSettings, GPALObjectType.Other);
                                }
                            }
                            catch (GPALException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error reading stderr", browserSettings, GPALObjectType.Other, ex);
                            }
                            finally
                            {
                                errorReader.Dispose();
                            }
                        });

                        try
                        {
                            // Wait/verify process
                            if (!Native.GetExitCodeProcess(hProcess, out uint exitCode))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get process exit code.");
                            if (exitCode != Native.STILL_ACTIVE)
                                throw new GPALException($"Chrome exited. Exit code: {exitCode}, Stderr: {stderr}, Check {Path.Combine(browserSettings.ProfileDataDirectory, "chrome_log.txt")}");

                            browserSettings.Process = Process.GetProcessById((int)processInfo2.dwProcessId);
                            browserSettings.Process.EnableRaisingEvents = true;
                        }
                        catch (ArgumentException ex)
                        {
                            if (Native.GetExitCodeProcess(hProcess, out uint exitCode) && exitCode != Native.STILL_ACTIVE)
                                throw new GPALException($"Chrome exited early. Exit code: {exitCode}, Stderr: {stderr}");
                            throw new GPALException($"Failed to get Chrome process by ID: {ex.Message}, Stderr: {stderr}");
                        }
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to launch Chrome with pipes", browserSettings, GPALObjectType.Other, ex);
                        throw;
                    }
                    finally
                    {
                        if (reservedPtr != IntPtr.Zero)
                            Marshal.FreeHGlobal(reservedPtr);
                        if (hProcess != IntPtr.Zero)
                            Native.CloseHandle(hProcess);
                        if (!created)
                        {
                            stdinPipe?.Dispose();
                            stdoutPipe?.Dispose();
                            stderrPipe?.Dispose();
                        }
                    }
                }
                else if (true == browserSettings.HiddenDesktop)
                {
                    // CreateProcess directly, because ProcessStartInfo cannot set lpDesktop, and without it the
                    // browser opens on the parent process's desktop. The cost is the browser's stdout and stderr,
                    // which elsewhere only reach DEEPDEBUG.
                    Native.STARTUPINFO hiddenStartup = new Native.STARTUPINFO
                    {
                        cb = (uint)Marshal.SizeOf<Native.STARTUPINFO>(),
                        lpDesktop = HiddenDesktop.StartupNameFor(browserSettings.HiddenDesktopName)
                    };

                    string hiddenCommandLine = $@"""{browserPath}"" {string.Join(" ", chromeArguments)}";

                    // Firefox's sandboxed GPU process fails to launch on a hidden desktop, and Firefox runs WebGL in
                    // it, so a page gets no WebGL context. MOZ_DISABLE_GPU_SANDBOX lifts that sandbox for this
                    // process only; a pref would be saved into the profile's prefs.js.
                    IntPtr environment = BrowserType.FireFox == browserSettings.BrowserType ? EnvironmentBlockWith("MOZ_DISABLE_GPU_SANDBOX", "1") : IntPtr.Zero;
                    Native.PROCESS_INFORMATION hiddenProcess;
                    int startError = 0;

                    try
                    {
                        if (false == Native.CreateProcess(null, hiddenCommandLine, IntPtr.Zero, IntPtr.Zero, false, IntPtr.Zero == environment ? 0 : Native.CREATE_UNICODE_ENVIRONMENT, environment, null, ref hiddenStartup, out hiddenProcess))
                            startError = Marshal.GetLastWin32Error();
                    }
                    finally
                    {
                        if (IntPtr.Zero != environment)
                            Marshal.FreeHGlobal(environment);
                    }

                    if (0 != startError)
                        throw new Win32Exception(startError, $"Could not start [{browserSettings.BrowserType}] on [{browserSettings.HiddenDesktopName}]");

                    Native.CloseHandle(hiddenProcess.hProcess);
                    Native.CloseHandle(hiddenProcess.hThread);

                    browserSettings.Process = Process.GetProcessById((int)hiddenProcess.dwProcessId);

                    // Capture the launcher PID immediately before it exits (Firefox only).
                    if (BrowserType.FireFox == browserSettings.BrowserType)
                        firefoxLauncherPid = browserSettings.Process?.Id ?? 0;
                }
                else
                {
                    browserSettings.Process = Process.Start(new ProcessStartInfo
                    {
                        FileName = browserPath,
                        Arguments = string.Join(" ", chromeArguments),
                        UseShellExecute = false,
                        // Chrome and Edge write "DevTools listening on ws://..." and internal ERROR lines to
                        // stderr. Redirect both streams so they stay out of the workflow's console.
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });

                    // Drain both streams or the browser stalls when the pipe fills. DevTools uses the port, so the
                    // lines only go to DEEPDEBUG.
                    browserSettings.Process.OutputDataReceived += (sender, data) => PublishBrowserOutput(browserSettings, data);
                    browserSettings.Process.ErrorDataReceived += (sender, data) => PublishBrowserOutput(browserSettings, data);
                    browserSettings.Process.BeginOutputReadLine();
                    browserSettings.Process.BeginErrorReadLine();

                    // Capture the launcher PID immediately before it exits (Firefox only).
                    if (BrowserType.FireFox == browserSettings.BrowserType)
                        firefoxLauncherPid = browserSettings.Process?.Id ?? 0;

                    DateTime deadline = DateTime.UtcNow + timeout;

                    // Firefox starts through a launcher that exits once the browser is up, so switch to the child,
                    // which owns the window, before the window wait and resize. ParentProcessId survives the
                    // launcher exiting.
                    if (BrowserType.FireFox == browserSettings.BrowserType)
                        AdoptFirefoxChild(browserSettings, firefoxLauncherPid, deadline);

                    // we have no window to check to ensure the browser launched in headless
                    // so check the process flag
                    // for windowed, wait for the window to open
                    if (true == headless)
                    {
                        if (true == browserSettings.Process.HasExited)
                        {
                            string msg = $"[{browserSettings.BrowserType}] did not start. Exiting.";
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg);
                            throw new TimeoutException(msg);
                        }
                    }
                    else
                    {
                        while (browserSettings.Process.MainWindowHandle == IntPtr.Zero && !browserSettings.Process.HasExited)
                        {
                            if (DateTime.UtcNow > deadline)
                            {
                                string msg = $"[{browserSettings.BrowserType}] did not start in [{secondsToWait}] seconds. Exiting.";
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg);
                                throw new TimeoutException(msg);
                            }

                            Thread.Sleep(100); // prevents busy-wait CPU spike
                        }

                        // Move and size the window now that it exists. Works for every browser; --window-size and
                        // --window-position are Chromium only, and Firefox has no command line position. The
                        // Chromium arguments still open the window at the right size, so it does not jump.
                        if (false == browserSettings.WindowSize.IsEmpty &&
                            false == browserSettings.FullScreen &&
                            false == browserSettings.Maximize &&
                            false == browserSettings.Minimize)
                        {
                            browserSettings.Process.Refresh();
                            WindowHelper.ResizeWindow(browserSettings.Process, browserSettings.WindowSize);
                        }
                    }
                }

                // On a hidden desktop Chrome and Edge open at their --window-size, but Firefox has no such
                // argument and Process.MainWindowHandle sees only this process's desktop, so its window is found
                // on the hidden desktop, then moved and sized as it is on the visible one
                if (true == browserSettings.HiddenDesktop &&
                    false == headless &&
                    BrowserType.FireFox == browserSettings.BrowserType &&
                    false == browserSettings.WindowSize.IsEmpty &&
                    false == browserSettings.FullScreen &&
                    false == browserSettings.Maximize &&
                    false == browserSettings.Minimize)
                {
                    DateTime windowDeadline = DateTime.UtcNow + timeout;

                    AdoptFirefoxChild(browserSettings, firefoxLauncherPid, windowDeadline);

                    IntPtr firefoxWindow = HiddenDesktop.MainWindowOf(browserSettings.HiddenDesktopName, browserSettings.Process.Id);

                    while (IntPtr.Zero == firefoxWindow && DateTime.UtcNow < windowDeadline && false == browserSettings.Process.HasExited)
                    {
                        Thread.Sleep(100);
                        firefoxWindow = HiddenDesktop.MainWindowOf(browserSettings.HiddenDesktopName, browserSettings.Process.Id);
                    }

                    if (IntPtr.Zero != firefoxWindow)
                        WindowHelper.ResizeWindow(firefoxWindow, browserSettings.WindowSize);
                }

                if (true == browserSettings.UseOttoMagic)
                {
                    try
                    {
                        var remoteEp = new IPEndPoint(IPAddress.Any, 0);
                        int restPort = 0;
                        int firstHeard = 0;
                        int launchedPid = browserSettings.Process?.Id ?? 0;

                        // Every browser with OttoMagic announces a port, so a message may be another browser's. It
                        // is ours when the announcing GPALRestAPI process descends from the browser we launched;
                        // others are skipped.
                        while (0 == restPort)
                        {
                            byte[] data;

                            // Receive timed out. If an untraceable announcement was heard, use the first and warn
                            // that it is a guess.
                            try { data = udpHandshake.Receive(ref remoteEp); }
                            catch (SocketException) when (0 != firstHeard)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No GPALRestAPI traced back to this browser, so port [{firstHeard}] is being used on the strength of it being the first one announced", browserSettings, GPALObjectType.Browser);

                                restPort = firstHeard;
                                break;
                            }

                            string[] said = Encoding.UTF8.GetString(data).Trim().Split(' ');

                            // GPALRestAPI sends "port processId". If it does not parse, throw here rather than fail
                            // later without a url.
                            if (2 > said.Length || false == int.TryParse(said[0], out int announced) || false == int.TryParse(said[1], out int hostPid))
                                throw new InvalidOperationException($"GPALRestAPI announced [{string.Join(" ", said)}], which is not a port and a process id");

                            if (0 == launchedPid || true == BrowserHelper.CameFromProcess(hostPid, launchedPid))
                                restPort = announced;
                            else
                            {
                                if (0 == firstHeard)
                                    firstHeard = announced;

                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Port [{announced}] was announced by another browser's GPALRestAPI [{hostPid}], so this launch is still waiting for its own", browserSettings, GPALObjectType.Browser);
                            }
                        }

                        browserSettings.RestApiBaseUrl = $"http://localhost:{restPort}/";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GPALRestAPI ready on port [{restPort}].", browserSettings, GPALObjectType.Browser);
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"GPALRestAPI never announced a port, so this browser cannot reach it", browserSettings, GPALObjectType.Browser, ex);
                        throw;
                    }
                    finally
                    {
                        udpHandshake?.Close();

                        // Port read and socket closed; release the mutex for the next launch.
                        if (true == holdingHandshakeGate)
                        {
                            ottoHandshakeGate.ReleaseMutex();
                            holdingHandshakeGate = false;
                        }
                    }

                    // Firefox launches via a wrapper that exits immediately; the real process is a child.
                    // Windows preserves ParentProcessId even after the parent exits, so we can query
                    // for the direct firefox.exe child of the launcher PID we captured at start.
                    if (firefoxLauncherPid > 0 &&
                        (browserSettings.Process == null || browserSettings.Process.HasExited))
                    {
                        try
                        {
                            using (var searcher = new ManagementObjectSearcher(
                                $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {firefoxLauncherPid} AND Name = 'firefox.exe'"))
                            {
                                foreach (ManagementObject obj in searcher.Get())
                                {
                                    int childPid = Convert.ToInt32(obj["ProcessId"]);
                                    try { browserSettings.Process = System.Diagnostics.Process.GetProcessById(childPid); break; }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                    }
                }
                else if (true == browserSettings.UsePuppeteer)
                {
                    // If Puppeteer cannot connect, nothing else will close the browser. Kill it and delete its temp
                    // profile, then rethrow.
                    try
                    {
                        _ = browserSettings.PuppeteerCommunicator;    // initialize before we start using
                    }
                    catch (GPALException)
                    {
                        BrowserHelper.KillProcessTree(browserSettings.Process?.Id ?? 0);

                        if (true == browserSettings.TempProfileCreated)
                            ChromeProfileManager.RemoveTempUserProfile(browserSettings.ProfileDataDirectory);

                        throw;
                    }
                }

                // Handle maximize/minimize
                if (browserSettings.Maximize)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            // Use maximize window via extension - the browser we just launched, not whoever's helper was called
                            browser.MagicHelper.Client.Maximize().Execute();
                        else if (true == browserSettings.UsePuppeteer)
                            browserSettings.PuppeteerCommunicator.Maximize().GetAwaiter().GetResult();
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to maximize browser window", browserSettings, GPALObjectType.Other, ex);
                    }
                }
                else if (browserSettings.Minimize)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            browser.MagicHelper.Client.Minimize().Execute();
                        else if (true == browserSettings.UsePuppeteer)
                            browserSettings.PuppeteerCommunicator.Minimize().GetAwaiter().GetResult();
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to minimize browser window", browserSettings, GPALObjectType.Other, ex);
                    }
                }
                else if (browserSettings.FullScreen)
                {
                    try
                    {
                        if (true == browserSettings.UseOttoMagic)
                            browser.MagicHelper.Client.FullScreen().Execute();
                        else if (true == browserSettings.UsePuppeteer)
                            browserSettings.PuppeteerCommunicator.FullScreen().GetAwaiter().GetResult();
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to full screen browser window", browserSettings, GPALObjectType.Other, ex);
                    }
                }

                // Executable, process id and port together, to tell a started browser from an attached one.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Browser [{browserSettings.BrowserType}] is process [{browserSettings.Process?.Id}] from [{browserPath}] on port [{browserSettings.DebugPort}] at [{browserSettings.PuppeteerUrl}]", browserSettings, GPALObjectType.Other);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Browser [{browserSettings.BrowserType}] launched{(true == browserSettings.UseHeadless ? " headless" : "")}.", browserSettings, GPALObjectType.Other);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Publish the exception and rethrow. Do not end the application: one failed browser would stop a
                // scheduler's other workflows or close a form. Uncaught, the unhandled exception handler cleans up
                // and exits.
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to launch browser [{browserSettings.BrowserType}] for URL [{URL}]", browserSettings, GPALObjectType.Other, ex);
                throw;
            }
            finally
            {
                // Close the UDP socket and release the mutex even when the launch failed before reading a port, or
                // the next OttoMagic launch fails on port 47623.
                udpHandshake?.Close();

                if (true == holdingHandshakeGate)
                    ottoHandshakeGate.ReleaseMutex();
            }

             return browserSettings.Process;
        }

        /// <summary>
        /// Brings the browser window to the top
        /// <example>bool success = browser.MagicHelper.TopBrowser(process);</example>
        /// </summary>
        /// <param name="process">The browser process</param>
        /// <returns>Whether the operation succeeded</returns>
        public bool TopBrowser(Process process) => WindowHelper.TopBrowser(process);

        /// <summary>
        /// Use the Star Wars API to generate random search terms for our google cookies we will create
        /// </summary>
        /// <returns></returns>
        public static string GetRandomStarWarsQuery()
        {
            IRESTClient restClient = GPAL.RESTClient.WithAPIBase("https://swapi.info/api/").ToGPALObject();

            var endpoints = new[]
            {
                ("people", 83, "name"),    // e.g., "Yoda"
                ("planets", 60, "name"),   // e.g., "Alderaan"
                ("films", 6, "title"),     // e.g., "A New Hope"
                ("species", 37, "name"),   // e.g., "Wookiee"
                ("vehicles", 39, "name"),  // e.g., "X-wing"
                ("starships", 36, "name")  // e.g., "Millennium Falcon"
            };

            string FetchQuery(string endpoint, int maxId, string field, int retries = 3)
            {
                for (int attempt = 0; attempt < retries; attempt++)
                {
                    int id = new Random().Next(1, maxId + 1);
                    var response = restClient.WithEndpoint($"{endpoint}/{id}").Execute<SwapiResponse>(false);
                    if (response != null)
                    {
                        return field == "name" ? response.name : response.title;
                    }
                    Thread.Sleep(100 * (int)Math.Pow(2, attempt)); // Backoff: 100ms, 200ms, 400ms
                }
                return null; // Let caller handle failure
            }

            // Select random primary endpoint
            var (endpoint, maxId, field) = endpoints[new Random().Next(endpoints.Length)];
            string query = FetchQuery(endpoint, maxId, field);

            // Fallback to another endpoint if null
            if (query == null)
            {
                var fallbackEndpoint = endpoints[new Random().Next(endpoints.Length)];
                query = FetchQuery(fallbackEndpoint.Item1, fallbackEndpoint.Item2, fallbackEndpoint.Item3);
                if (query == null) // Last resort, pick another
                {
                    fallbackEndpoint = endpoints[new Random().Next(endpoints.Length)];
                    query = FetchQuery(fallbackEndpoint.Item1, fallbackEndpoint.Item2, fallbackEndpoint.Item3) ?? "star wars"; // Absolute last fallback
                }
            }

            if (new Random().NextDouble() > 0.4) // 60% chance to add second term
            {
                var (otherEndpoint, otherMaxId, otherField) = endpoints[new Random().Next(endpoints.Length)];
                string otherQuery = FetchQuery(otherEndpoint, otherMaxId, otherField);
                if (otherQuery != null)
                {
                    query += $" {otherQuery}"; // e.g., "Yoda Millennium Falcon"
                }
                else
                {
                    var fallbackEndpoint = endpoints[new Random().Next(endpoints.Length)];
                    otherQuery = FetchQuery(fallbackEndpoint.Item1, fallbackEndpoint.Item2, fallbackEndpoint.Item3);
                    query += otherQuery != null ? $" {otherQuery}" : ""; // Skip if still null
                }
            }

            return query;
        }

        private class SwapiResponse
        {
            public string name { get; set; }
            public string title { get; set; }
        }

        public static string GetFullUrl(string url, IBrowser browser, out bool areRobotsAllowed, bool noRobotsCheck = false)
        {
            List<string> robotsTxt = new List<string>();
            string normalized = url;
            areRobotsAllowed = true;

            try
            {
                // Default to HTTPS only if no scheme supplied
                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    normalized = "https://" + url;
                }

                // files the browser merely opens or downloads (sitemap.xml, pdfs, images...) are not pages, skip the checks
                if (false == noRobotsCheck && true == ((Browser)browser).BrowserSettings.ObeyRobotsTxt && false == UrlHelper.IsNonHtmlResource(normalized))
                    areRobotsAllowed = UrlHelper.CheckRobotsTxt(normalized, browser, robotsTxt);

                if (true == url.StartsWith("https://"))
                    return url;

                if (string.IsNullOrWhiteSpace(url) || UrlHelper.AreEquivalent(url, "https://google.com"))
                    return "https://google.com";

            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error getting full url", browser, GPALObjectType.Other, ex);
            }

            return normalized;
        }

        /// <summary>
        /// Calculates a consistent integer hash from a dictionary of attributes.
        /// </summary>
        /// <param name="attributes">A Dictionary&lt;string, object&gt; containing the attributes.</param>
        /// <returns>A 32-bit integer hash.</returns>
        public static int CalculateHash(Dictionary<string, object> attributes)
        {
            // 1. Collect and format attributes into a list of strings.
            // We use LINQ's OrderBy to sort the dictionary entries by key
            // before converting them to strings, ensuring consistent order.
            var attrList = attributes.OrderBy(kvp => kvp.Key)
                                     .Select(kvp => $"{kvp.Key}={kvp.Value}")
                                     .ToList();

            // 2. Join the sorted attributes into a single string.
            string str = string.Join("|", attrList);

            // 3. Implement the custom hashing algorithm.
            int hash = 0;
            foreach (char c in str)
            {
                // The bitwise operations from the JS code are translated to C#
                hash = ((hash << 5) - hash + c) | 0;
            }

            return hash;
        }
        /// <summary>
        /// Builds the Chrome, Edge or Firefox user agent from the installed version in the registry.<br/>
        /// Static because it reads only the registry and GPALSettings templates and must work with no browser;
        /// GPAL.MagicHelper returns MagicHelpers[0], which needs one.
        /// </summary>
        /// <param name="browserType"></param>
        /// <returns></returns>
        public static string GetUserAgentString(BrowserType browserType)
        {
            // construct headless user-agent
            string chromeVersion;
            string edgeVersion;
            string firefoxVersion;
            string retval = null;

            if (BrowserType.Chrome == browserType)
            {
                DriverHelper.GetChromeVersion(out chromeVersion);
                // a dotless or empty version means detection failed; slicing to IndexOf(-1) would throw
                if (null != chromeVersion && 0 < chromeVersion.IndexOf('.'))
                    chromeVersion = chromeVersion.AsSpan().Slice(0, chromeVersion.IndexOf('.')).ToString(); // NOTE: fastest way to get the first token
                retval = String.Format(GPAL.GPALSettings.ChromeUserAgentTemplate, chromeVersion);
            }
            else if (BrowserType.Edge == browserType)
            {
                DriverHelper.GetEdgeVersion(out edgeVersion);
                // a dotless or empty version means detection failed; slicing to IndexOf(-1) would throw
                if (null != edgeVersion && 0 < edgeVersion.IndexOf('.'))
                    edgeVersion = edgeVersion.AsSpan().Slice(0, edgeVersion.IndexOf('.')).ToString(); // NOTE: fastest way to get the first token
                retval = String.Format(GPAL.GPALSettings.EdgeUserAgentTemplate, edgeVersion);
            }
            else if (BrowserType.FireFox == browserType)
            {
                DriverHelper.GetFirefoxVersion(out firefoxVersion);
                // a dotless or empty version means detection failed; slicing to IndexOf(-1) would throw
                if (null != firefoxVersion && 0 < firefoxVersion.IndexOf('.'))
                    firefoxVersion = firefoxVersion.AsSpan().Slice(0, firefoxVersion.IndexOf('.')).ToString(); // NOTE: fastest way to get the first token
                retval = String.Format(GPAL.GPALSettings.FirefoxUserAgentTemplate, firefoxVersion);
            }

            return retval;
        }

        /// <summary>
        /// Clears the inherit flag on the handshake socket. Windows sockets are inheritable by default, and a
        /// browser started while it is open would keep port 47623 bound for its lifetime, failing the next launch
        /// with an error that does not say why.
        /// </summary>
        /// <param name="client">The handshake socket to keep out of the children.</param>
        private static void KeepFromChildren(UdpClient client)
        {
            if (false == Native.SetHandleInformation(client.Client.Handle, Native.HANDLE_FLAG_INHERIT, 0))
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Could not take the inherit flag off the handshake socket, so the next OttoMagic launch may find [47623] still held",
                    null, GPALObjectType.Browser, new Win32Exception(Marshal.GetLastWin32Error()));
        }

        internal string GetUserAgent(BrowserSettings browserSettings, List<string> chromeArguments)
        {
            string userAgent = null;
            string browserPath = null;

            // Starting a browser to read its user agent is slow, so only with UserAgentFromBrowser. Otherwise
            // BrowserHelper.ResolveUserAgent, the string GPAL's own requests send.
            if (false == browserSettings.UserAgentFromBrowser)
                return BrowserHelper.ResolveUserAgent(browserSettings);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Getting user agent for headless use.", chromeArguments, GPALObjectType.Other);

            // Get browser executable path
            switch (browserSettings.BrowserType)
            {
                case BrowserType.Chrome:
                    browserPath = Puppeteer.GetChromePath();
                    break;
                case BrowserType.Edge:
                    browserPath = Puppeteer.GetEdgePath();
                    break;
                case BrowserType.FireFox:
                    browserPath = Puppeteer.GetFirefoxPath();
                    break;
                default:
                    Exception ex = new Exception("Unsupported Browser Type");
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unsupported browser type [{browserSettings.BrowserType}]", browser, GPALObjectType.Browser, ex);
                    return GetUserAgentString(browserSettings.BrowserType);
            }

            UdpClient probeHandshake = null;
            bool holdingProbeGate = false;

            try
            {
                List<string> ourArgs = new List<string>(chromeArguments);

                // same reason as the launch above: every browser gets its own port or they share a session
                if (null == browserSettings.DebugPort)
                    browserSettings.DebugPort = BrowserHelper.FindFreePort();

                if (BrowserType.FireFox != browserSettings.BrowserType)
                    ourArgs.Add($"--remote-debugging-port={browserSettings.DebugPort.Value}");
                else
                {
                    // Firefox is queried through GPALRestAPI, so open the handshake socket before starting it to
                    // receive its port. Otherwise the request goes to whichever browser is already running.
                    try { holdingProbeGate = ottoHandshakeGate.WaitOne(OttoHandshakeGateTimeoutMs); }
                    catch (AbandonedMutexException) { holdingProbeGate = true; }

                    probeHandshake = new UdpClient(47623);
                    probeHandshake.Client.ReceiveTimeout = 30000;

                    KeepFromChildren(probeHandshake);
                }

                browserSettings.Process = Process.Start(new ProcessStartInfo
                {
                    FileName = browserPath,
                    Arguments = string.Join(" ", ourArgs),
                    UseShellExecute = false
                });

                //                Thread.Sleep(3000);

                // NOTE: always use puppeteer to get the useragent, it's very reliable, except ff which doesn't use puppeteer
                if (BrowserType.FireFox == browserSettings.BrowserType)
                {
                    var probeEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    string probePortText = Encoding.UTF8.GetString(probeHandshake.Receive(ref probeEndPoint)).Trim();

                    // ask the browser we just launched, on the port it just announced
                    userAgent = true == int.TryParse(probePortText, out int probePort)
                        ? ((IRESTClient)GPAL.RESTClient.WithAPIBase($"http://localhost:{probePort}/").ToGPALObject()).GetUserAgent().Execute()
                        : Client.GetUserAgent().Execute();
                }
                else
                {
                    AutomationEngine aeSave = browserSettings.AutomationEngine;
                    browserSettings.AutomationEngine = AutomationEngine.PuppeteerPort;
                    _ = browserSettings.PuppeteerCommunicator;
                    userAgent = browserSettings.PuppeteerClient.GetUserAgent().Execute();
                    browserSettings.AutomationEngine = aeSave;
                }
                userAgent = userAgent.Replace("Headless", "");

                //if (true == browserSettings.UsePuppeteer)
                {
                    var parameters = new Dictionary<string, object>();
                    // gracefully close the browser, release resources, shutdown chrome
                    //browserSettings.PuppeteerCommunicator.SendCommand(DevToolsMethods.BrowserClose, parameters, null).GetAwaiter().GetResult();
                    browserSettings.PuppeteerCommunicator._readerReadyTcs?.SetCanceled();
                    if (false == browserSettings.PuppeteerCommunicator._usePipes)
                        browserSettings.PuppeteerCommunicator.CloseOutputWebSocketAsync().GetAwaiter();
                }

                // NOTE: KLUDGE: might not be required but we are, at least for puppeteer getting an error relaunching chrome after getting the useragent
                // - we added puppeteer browserclose rather than a hard kill
                // Lock file can not be created! Error code: 32
                // Failed to create a ProcessSingleton for your profile directory. This means that running multiple instances would start multiple browser processes rather than opening a new window in the existing process. Aborting now to avoid profile corruption.

                // kill the opened chrome and any children used to get the agent string
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/PID {browserSettings.Process.Id} /F /T",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi).WaitForExit();
                }
                catch { }

                Thread.Sleep(1_000);

                if (null != browserSettings.Process && false == browserSettings.Process.HasExited)
                {
                    try
                    {
                        browserSettings.Process.Kill();
                        browserSettings.Process.Close();
                    }
                    catch { }
                }

                Thread.Sleep(1_000);

                // GPALRestAPI is not killed here: it shuts itself down when the extension's port closes, and killing
                // it by name would take down every other browser's rest api too
                browserSettings.PuppeteerClient = null;
                browserSettings.PuppeteerCommunicator = null;
                browserSettings.Process = null;

            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                userAgent = GetUserAgentString(browserSettings.BrowserType);
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to fetch user-agent using [{userAgent}].", browserSettings, GPALObjectType.Other, ex);
            }
            finally
            {
                probeHandshake?.Close();

                // The user agent browser is closed; close the socket and release the mutex for the real launch.
                if (true == holdingProbeGate)
                    ottoHandshakeGate.ReleaseMutex();
            }

            return false == string.IsNullOrEmpty(userAgent) ? userAgent : GetUserAgentString(browserSettings.BrowserType);
        }
        #endregion Helpers
    }

    public class WindowHelper
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        private const int SW_RESTORE = 9;

        /// <summary>
        /// Brings the browser window to the foreground.
        /// Not useful in headlesss or OttoMagic mode where we are totally in control and in focus.
        /// <example>bool success = browser.MagicHelper.TopBrowser(process, processId);</example>
        /// </summary>
        /// <param name="process">The browser process</param>
        /// <param name="processId">Optional specific process ID or service ID to scan for windows</param>
        /// <returns>Whether the operation succeeded</returns>
        public static bool TopBrowser(Process process, int processId = 0)
        {
            if ((null == process || true == process?.HasExited) && 0 == processId)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to top browser. Browser has exited.", process, GPALObjectType.Other);
                return false;
            }

            IntPtr windowHandle = IntPtr.Zero;

            // 1. If a process ID is explicitly passed, scan the OS for its visible window
            if (processId > 0)
            {
                windowHandle = FindWindowByProcessId(processId);
            }

            // 2. Fallback to the process main window if the PID scan failed or wasn't requested
            if (IntPtr.Zero == windowHandle)
            {
                // not process.Handle: process handles cannot be passed to ShowWindow/SetForegroundWindow
                windowHandle = process?.MainWindowHandle ?? IntPtr.Zero;
            }

            if (IntPtr.Zero == windowHandle)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to top browser. window handle is IntPtr.Zero", process, GPALObjectType.Other);
                return false;
            }

            // Restore only a minimized window. SW_RESTORE on a maximized one returns it to its previous size,
            // undoing Maximize.
            if (true == IsIconic(windowHandle))
                ShowWindow(windowHandle, SW_RESTORE);

            bool success = SetForegroundWindow(windowHandle);
            if (false == success)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to top browser. SetForegroundWindow returned false.", process, GPALObjectType.Other);
            }

            return success;
        }

        private static IntPtr FindWindowByProcessId(int processId)
        {
            IntPtr foundHandle = IntPtr.Zero;

            // The visible browser window may belong to the target process OR a descendant of it. Firefox is the
            // classic case: geckodriver (the pid we are handed) spawns firefox.exe as a child, and the window
            // belongs to that child - a direct pid match finds nothing. So match any visible top-level window
            // whose owning pid is the target or a descendant. Prefer a titled window (the main browser window
            // has a title; content/helper windows generally do not) so we do not grab a stray popup.
            HashSet<int> targetPids = GetProcessAndDescendantIds(processId);

            EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
            {
                if (IsWindowVisible(hWnd))
                {
                    GetWindowThreadProcessId(hWnd, out uint windowPid);
                    if (targetPids.Contains((int)windowPid))
                    {
                        foundHandle = hWnd;   // remember the last match, but keep looking for a titled one
                        if (0 < GetWindowTextLength(hWnd))
                            return false;     // titled window - this is the main browser window, stop
                    }
                }
                return true; // Keep looking
            }, IntPtr.Zero);

            return foundHandle;
        }

        /// <summary>
        /// Returns the given process id plus the ids of all its descendant processes, walking the
        /// ParentProcessId chain via WMI. Used to locate a browser window that a launcher/driver process
        /// (e.g. geckodriver) owns only through a child process (firefox.exe). Falls back to just the root
        /// id if WMI is unavailable.
        /// </summary>
        private static HashSet<int> GetProcessAndDescendantIds(int rootPid)
        {
            var result = new HashSet<int> { rootPid };

            try
            {
                var frontier = new Queue<int>();
                frontier.Enqueue(rootPid);

                while (0 < frontier.Count)
                {
                    int parent = frontier.Dequeue();
                    using (var searcher = new ManagementObjectSearcher(
                        $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parent}"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            int childPid = Convert.ToInt32(obj["ProcessId"]);
                            if (result.Add(childPid))
                                frontier.Enqueue(childPid);
                        }
                    }
                }
            }
            catch { /* WMI unavailable - fall back to just the root pid */ }

            return result;
        }


        // Define the Windows API function
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        // Define the Windows API constant for getting the window's current position and size
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        // Define the RECT structure for GetWindowRect
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;

        /// <summary>
        /// Resizes and repositions a process's main window using optional parameters.
        /// </summary>
        /// <param name="process">The Process whose main window to resize.</param>
        /// <param name="inRect">The new left, top, width and height of the window, in screen pixels</param>
        public static void ResizeWindow(
            Process process,
            Rectangle inRect)
        {
            // Only a process with a main window on this process's desktop has one to resize
            if (IntPtr.Zero != process.MainWindowHandle)
                ResizeWindow(process.MainWindowHandle, inRect);
        }

        /// <summary>
        /// Resizes and repositions a window, such as one on a hidden desktop, which a Process cannot hand over.
        /// </summary>
        /// <param name="window">The window to resize.</param>
        /// <param name="inRect">The new left, top, width and height of the window, in screen pixels</param>
        public static void ResizeWindow(
            IntPtr window,
            Rectangle inRect)
        {
            int maxWwidth = Puppeteer.GetScreenWidthInt();
            int maxHeight = Puppeteer.GetScreenHeightInt();

            if (inRect.Width > maxWwidth)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Desired browser width [{inRect.Width}] is greater than the screen width [{maxWwidth}]");

            if (inRect.Height > maxHeight)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Desired browser height [{inRect.Height}] is greater than the screen height [{maxHeight}]");

            // Determine the flags for SetWindowPos based on null parameters
            uint uFlags = SWP_NOZORDER;

            // Call the Windows API function
            SetWindowPos(
                window,
                IntPtr.Zero, // No change to z-order
                inRect.Left,
                inRect.Top,
                inRect.Width,
                inRect.Height,
                uFlags);

            // what the window took, which a browser that restores its own saved size can change
            GetWindowRect(window, out RECT took);
            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Window [{window}] asked for [{inRect.Left}, {inRect.Top}, {inRect.Width}, {inRect.Height}] and is [{took.Left}, {took.Top}, {took.Right - took.Left}, {took.Bottom - took.Top}]");
        }
    }
}
