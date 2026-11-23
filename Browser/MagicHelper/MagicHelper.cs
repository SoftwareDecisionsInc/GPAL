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
    public partial class MagicHelper : IMagicHelper
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
    }
}
