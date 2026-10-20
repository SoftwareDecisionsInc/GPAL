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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public class Puppeteer
    {
        internal string webSocketUrl { get; set; } = null;
        internal static Dictionary<DevToolsMethods, Func<Newtonsoft.Json.Linq.JObject, object>> resultExtractors { get; set; }

        public static Process LaunchBrowser(IBrowser browser, GPALUrl URL)
        {
            Process process = browser.MagicHelper.LaunchBrowser((Browser)browser, URL);

            return process;
        }
        public static async Task<string> GetActiveTab(PuppeteerCommunicator communicator, string sessionId)
        {
            dynamic targets = await communicator.SendCommand<object>(DevToolsMethods.TargetGetTargets, new { }, null).ConfigureAwait(false);

            // Check if targets and result exist
            if (targets == null || targets.result == null || targets.result.targetInfos == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "No targets found after closing tab", null, GPALObjectType.None);
                return null;
            }
            foreach (dynamic target in targets.result.targetInfos)
            {
                string targetType = target.type?.ToString();
                string targetUrl = target.url?.ToString();
                if (targetType == "page" && targetUrl != null)
                {
                    // Return the first page target as a reasonable guess for the active tab
                    // You can add additional filters (e.g., URL or browserContextId) if needed
                    return target.targetId?.ToString();
                }
            }

            // Log available page URLs for debugging
            var availableUrls = string.Join(", ", ((Newtonsoft.Json.Linq.JArray)targets.result.targetInfos)
                .Where(t => t["type"]?.ToString() == "page")
                .Select(t => t["url"]?.ToString() ?? "null"));
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No page target found after closing tab. Available page URLs: [{availableUrls}]", null, GPALObjectType.None);

            return null;
        }
        /// <summary>
        /// Gets the CDP websocket url from the debug port, retrying until it answers.<br/>
        /// The port opens when the browser is ready, which can take extra seconds after a reboot, a first virus scan
        /// of chrome.exe, or a browser update.
        /// </summary>
        /// <param name="puppeteerUrl">Base url of the browser's debug port, e.g. http://localhost:57005</param>
        /// <param name="waitMilliseconds">How long to keep asking before giving up. Defaults to 30 seconds.</param>
        /// <returns>The websocket url to connect on, null when the port never answered</returns>
        internal static string GetWebSocketUrl(string puppeteerUrl, int waitMilliseconds = 20_000)
        {
            string retVal = null;
            Exception lastException = null;
            Stopwatch waited = Stopwatch.StartNew();
            int attempts = 0;

            while (null == retVal && waited.ElapsedMilliseconds < waitMilliseconds)
            {
                attempts++;

                try
                {
                    // 127.0.0.1 here only: localhost tries IPv6 first and waits out the refusal, which slows this
                    // startup retry loop. Elsewhere use localhost; extension permissions and origin checks treat
                    // 127.0.0.1 as a different origin.
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create($"{puppeteerUrl.Replace("localhost", "127.0.0.1")}/json/version");
                    request.Method = "GET";
                    request.Timeout = 2_000;
                    HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                    using StreamReader reader = new StreamReader(response.GetResponseStream());
                    string json = reader.ReadToEnd();
                    var versionInfo = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    retVal = versionInfo["webSocketDebuggerUrl"]; // e.g., ws://localhost:<port>/devtools/browser/<id>
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Thread.Sleep(250);
                }
            }

            waited.Stop();

            // Publish when it took more than one try; slow startup on one machine is worth seeing before it fails.
            if (null != retVal && 1 < attempts)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Browser debug port at [{puppeteerUrl}] answered after [{waited.ElapsedMilliseconds}]ms and [{attempts}] attempts", null, GPALObjectType.None);
            else if (null == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"ERROR retrieving Puppeteer WebSocket URL from [{puppeteerUrl}], no answer in [{waited.ElapsedMilliseconds}]ms over [{attempts}] attempts", null, GPALObjectType.None, lastException);

            return retVal;
        }


        /// <summary>
        /// Provides methods to resolve installed browser executable paths using a
        /// deterministic, defensive, and extensible fallback pipeline.
        /// 
        /// Resolution order (invariant across browsers):
        /// 1. HKLM (64-bit view)
        /// 2. HKLM (32-bit view)
        /// 3. HKCU
        /// 4. where.exe (PATH lookup)
        /// 5. Per-user (LocalAppData)
        /// 6. Program Files (system-wide)
        /// 7. Channel / variant installs (optional but included)
        /// </summary>

        /// <summary>
        /// Gets the full path to chrome.exe if found; otherwise null.
        /// </summary>
        internal static string GetChromePath()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return ResolveExePath(
                    "chrome.exe",
                    new[]
                    {
                        Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe")
                    },
                    new[]
                    {
                        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
                    },
                    new[]
                    {
                        @"C:\Program Files\Google\Chrome Beta\Application\chrome.exe",
                        @"C:\Program Files\Google\Chrome Dev\Application\chrome.exe"
                    }
                );
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"ERROR retrieving Chrome path.", null, GPALObjectType.None, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the full path to msedge.exe if found; otherwise null.
        /// </summary>
        internal static string GetEdgePath()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return ResolveExePath(
                    "msedge.exe",
                    new[]
                    {
                        Path.Combine(localAppData, @"Microsoft\Edge\Application\msedge.exe"),
                        Path.Combine(localAppData, @"Microsoft\Edge Beta\Application\msedge.exe"),
                        Path.Combine(localAppData, @"Microsoft\Edge Dev\Application\msedge.exe"),
                        Path.Combine(localAppData, @"Microsoft\Edge SxS\Application\msedge.exe")
                    },
                    new[]
                    {
                        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
                    },
                    new string[0]
                );
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"ERROR retrieving MS Edge path.", null, GPALObjectType.None, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the full path to firefox.exe if found; otherwise null.
        /// </summary>
        internal static string GetFirefoxPath()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                return ResolveExePath(
                    "firefox.exe",
                    new[]
                    {
                        Path.Combine(localAppData, @"Mozilla Firefox\firefox.exe"),
                        Path.Combine(localAppData, @"Mozilla Firefox Nightly\firefox.exe")
                    },
                    new[]
                    {
                        @"C:\Program Files\Mozilla Firefox\firefox.exe",
                        @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe"
                    },
                    new[]
                    {
                        @"C:\Program Files\Firefox Developer Edition\firefox.exe"
                    }
                );
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"ERROR retrieving Firefox path.", null, GPALObjectType.None, ex);
                return null;
            }
        }

        /// <summary>
        /// Core resolution pipeline. Executes each strategy in order until a valid path is found.
        /// </summary>
        private static string ResolveExePath(string exeName, string[] userPaths, string[] systemPaths, string[] variantPaths)
        {
            string result = null;

            try
            {
                string keyPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName;

                Func<string>[] resolvers = new Func<string>[]
                {
                    () => GetFromRegistry(RegistryHive.LocalMachine, keyPath, RegistryView.Registry64),
                    () => GetFromRegistry(RegistryHive.LocalMachine, keyPath, RegistryView.Registry32),
                    () => GetFromRegistry(RegistryHive.CurrentUser, keyPath, RegistryView.Default),
                    () => GetFromWhere(exeName),
                    () => GetFromPaths(userPaths),
                    () => GetFromPaths(systemPaths),
                    () => GetFromPaths(variantPaths)
                };

                foreach (var resolver in resolvers)
                {
                    if (result == null)
                    {
                        result = resolver();
                    }
                }
            }
            catch
            {
                // Intentionally silent; caller handles logging
            }

            return result;
        }

        /// <summary>
        /// Attempts to resolve a path from the Windows registry App Paths key.
        /// </summary>
        private static string GetFromRegistry(RegistryHive hive, string keyPath, RegistryView view)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var key = baseKey.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(string.Empty);
                        if (value != null)
                        {
                            string path = value.ToString();
                            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            {
                                return path;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore and continue fallback chain
            }

            return null;
        }

        /// <summary>
        /// Uses where.exe to resolve the executable from the system PATH.
        /// </summary>
        private static string GetFromWhere(string exeName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = exeName,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string line = process.StandardOutput.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line) && File.Exists(line))
                        {
                            return line;
                        }
                    }
                }
            }
            catch
            {
                // Ignore and continue fallback chain
            }

            return null;
        }

        /// <summary>
        /// Iterates through a list of candidate file paths and returns the first that exists.
        /// </summary>
        private static string GetFromPaths(string[] paths)
        {
            if (paths == null)
            {
                return null;
            }

            foreach (var path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }


        public static string GetScreenMaxSize()
        {
            int maxWidth = 0;
            int maxHeight = 0;

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Width > maxWidth)
                {
                    maxWidth = screen.Bounds.Width;
                }

                if (screen.Bounds.Height > maxHeight)
                {
                    maxHeight = screen.Bounds.Height;
                }
            }

            return $"{maxWidth},{maxHeight}";
        }
        public static string GetScreenWidth()
        {
            int maxWidth = 0;

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Width > maxWidth)
                {
                    maxWidth = screen.Bounds.Width;
                }
            }

            return $"{maxWidth}";
        }
        public static string GetScreenHeight()
        {
            int maxHeight = 0;

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Height > maxHeight)
                {
                    maxHeight = screen.Bounds.Height;
                }
            }

            return $"{maxHeight}";
        }

        public static int GetScreenWidthInt()
        {
            int maxWidth = 0;

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Width > maxWidth)
                {
                    maxWidth = screen.Bounds.Width;
                }
            }

            return maxWidth;
        }
        public static int GetScreenHeightInt()
        {
            int maxHeight = 0;

            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Height > maxHeight)
                {
                    maxHeight = screen.Bounds.Height;
                }
            }

            return maxHeight;
        }

    }
}
