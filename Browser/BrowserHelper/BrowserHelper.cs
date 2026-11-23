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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;
using System.Net.NetworkInformation;
using System.Net;
using System.Text.RegularExpressions;

namespace GenerallyPositive.Browser
{
    public partial class BrowserHelper
    {
        static bool alreadyUpdating = false;

        static List<string> lastErrorMessage = new List<string>();
        static string lastSessionToken;

        /// <summary>
        /// Records network calls on Selenium by wrapping fetch and XMLHttpRequest in the page and pushing each call
        /// onto window.__gpalCalls. Puppeteer uses CDP instead.<br/>
        /// Documents, scripts and images are not recorded.
        /// </summary>
        internal const string CallRecorderScript = @"
            (function () {
                if (window.__gpalCalls) return;

                window.__gpalCalls = [];

                var record = function (url, method, headers, body, type) {
                    var call = { url: String(url), method: method || 'GET', type: type, headers: headers || {}, postData: body || null, status: 0 };
                    window.__gpalCalls.push(call);
                    return call;
                };

                var nativeFetch = window.fetch;

                if (nativeFetch)
                    window.fetch = function (input, init) {
                        var url = input && input.url ? input.url : input;
                        var headers = {};
                        var options = init || {};

                        // a Headers object, a plain object or an array of pairs, all of which fetch accepts
                        if (options.headers && options.headers.forEach)
                            options.headers.forEach(function (v, k) { headers[k] = v; });
                        else if (options.headers)
                            Object.keys(options.headers).forEach(function (k) { headers[k] = options.headers[k]; });

                        var call = record(url, options.method, headers, options.body, 'Fetch');

                        return nativeFetch.apply(this, arguments).then(function (response) {
                            call.status = response.status;
                            return response;
                        }, function (error) {
                            call.status = 0;
                            throw error;
                        });
                    };

                var nativeOpen = XMLHttpRequest.prototype.open;
                var nativeSend = XMLHttpRequest.prototype.send;
                var nativeSetHeader = XMLHttpRequest.prototype.setRequestHeader;

                XMLHttpRequest.prototype.open = function (method, url) {
                    this.__gpalCall = { method: method, url: url, headers: {} };
                    return nativeOpen.apply(this, arguments);
                };

                XMLHttpRequest.prototype.setRequestHeader = function (name, value) {
                    if (this.__gpalCall) this.__gpalCall.headers[name] = value;
                    return nativeSetHeader.apply(this, arguments);
                };

                XMLHttpRequest.prototype.send = function (body) {
                    var pending = this.__gpalCall;

                    if (pending) {
                        var call = record(pending.url, pending.method, pending.headers, body, 'XHR');
                        this.addEventListener('loadend', function () { call.status = this.status; });
                    }

                    return nativeSend.apply(this, arguments);
                };
            })();
        ";

        // gap between url reads while waiting to see if a click navigated. Each read is a driver round trip, so this
        // is not a hot loop the way WaitForValueOrTime is
        private const int NavigationPollMs = 50;

        // chromium window handles are the CDP target id behind this prefix; firefox reports its pid in capabilities
        private const string CdpWindowPrefix = "CDwindow-";
        private const string MozProcessIdCapability = "moz:processID";

        /// <summary>
        /// How long Firefox's driver waits on a navigation that starts a download. The download goes on after the
        /// driver stops waiting; this only bounds how long the driver holds the command.
        /// </summary>
        private const int DownloadNavigationMs = 3000;
        // How many times DownloadPdfFile fetches while a redirect or consent page is served instead of the PDF, and
        // the wait between fetches.
        internal const int PdfFetchAttempts = 3;
        internal const int PdfFetchWaitMs = 1500;

        /// <summary>
        /// Custom command name for the driver's CDP endpoint, registered in GetWebDriver.
        /// </summary>
        private const string HiddenCdpCommand = "gpalExecuteCdp";

        /// <summary>Maximum parent processes CameFromProcess checks.</summary>
        private const int MaxParentHops = 8;

        internal const int GracefulExitMs = 10000;   // wait for a browser to exit after close
        internal const int ProfileReleaseWaitMs = 15000;   // max wait for a closing browser to free its profile
        internal const int ProfileReleaseCheckMs = 250;

        private static Stopwatch watch = new System.Diagnostics.Stopwatch();
        private static int lastX = 0, lastY = 0;

        // https://stackoverflow.com/questions/8438483/bring-browser-from-back-to-front-selenium-web-driver-java
        /// <summary>
        /// Brings the browser window to the foreground (or moves the mouse off it) so hardware-driven
        /// interactions and image selectors target the correct window.<br/>
        /// Headless browsers, OttoMagic/Puppeteer without image selectors, and repeated calls within
        /// 1 second are skipped. Uses a hardware click on the window's top-left corner for Firefox/hardware
        /// mode, Selenium's SwitchTo().Window for Selenium, or WindowHelper.TopBrowser otherwise.
        /// </summary>
        /// <summary>
        /// True if baseUrl's status endpoint answers 200 within 1 second; a wedged listener counts as not
        /// running.<br/><br/>
        /// GPALRestAPI exits with its browser, so <see cref="Browser.IsAlive"/> uses this.
        /// </summary>
        /// <returns>True when a GPALRestAPI answered.</returns>
        /// <summary>File in the GPALRestAPI exe folder listing each running GPALRestAPI's process id and port.</summary>
        private const string PortsLogName = "ports-in-use.log";

        /// <summary>Registered name of the GPALRestAPI native messaging host.</summary>
        private const string NativeHostName = "gpal.rest.api.nativeapp";

        /// <summary>
        /// Registry keys where Chromium browsers look for the native messaging host, HKCU before HKLM. WOW6432Node
        /// keys are the 32 bit view.
        /// </summary>
        private static readonly string[] NativeHostKeys =
        {
            @"Software\\Google\\Chrome\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\Chromium\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\Microsoft\\Edge\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\WOW6432Node\\Google\\Chrome\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\WOW6432Node\\Chromium\\NativeMessagingHosts\\" + NativeHostName,
            @"Software\\WOW6432Node\\Microsoft\\Edge\\NativeMessagingHosts\\" + NativeHostName
        };

        // Headers the browser sets itself. In-page fetch cannot override them, so they are not copied; only headers
        // the site's code added matter.
        private static readonly string[] browserOwnedHeaders =
        {
            "referer", "user-agent", "host", "origin", "cookie", "content-length",
            "connection", "accept-encoding", "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform",
            "sec-ch-ua-arch", "sec-ch-ua-model", "sec-ch-ua-full-version-list", "sec-ch-device-memory",
            "sec-fetch-dest", "sec-fetch-mode", "sec-fetch-site"
        };

        // Every port FindFreePort has returned in this process. A browser's port looks free until it starts
        // listening, so ports are never removed and never handed out twice.
        private static readonly HashSet<int> portsHandedOut = new HashSet<int>();

        // Where the next search starts, so ports climb instead of rescanning from the base
        private static int lastPortHandedOut = 0;
        // Wait between polls in WaitForValueOrTime: little delay for a ready page, a few polls per second for a slow
        // one.
        internal const int WaitForValuePollMs = 250;
        // Wait between network idle asks, every engine. A wait for idle already runs for the quiet period and more, so
        // asking twice a second adds at most half a second, and on Selenium each ask is a call into the page.
        internal const int NetworkIdlePollMs = 500;

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        // Process names of the browsers GPALRestAPI can be started by.
        private static readonly HashSet<string> browserProcessNames = new HashSet<string> { "chrome", "msedge", "firefox" };
    }
}
