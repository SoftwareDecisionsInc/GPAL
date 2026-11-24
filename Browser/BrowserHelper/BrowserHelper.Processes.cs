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
        /// <summary>
        /// Kills all running processes whose name matches any of <paramref name="namesOfProcessesToKill"/>.
        /// </summary>
        /// <param name="namesOfProcessesToKill">One or more process names to kill (without .exe)</param>
        /// <returns>The result of killing the last process name in the list</returns>
        /// <example>
        /// <code>
        /// BrowserHelper.KillProcessesByName("chromedriver", "msedgedriver");
        /// </code>
        /// </example>
        public static KillProcessesResult KillProcessesByName(params string[] namesOfProcessesToKill)
        {
            KillProcessesResult result = null;

            foreach (var name in namesOfProcessesToKill)
                result = KillProcess(name);

            return result;
        }

        /// <summary>
        /// https://github.com/matthewbierman/RemoteWebDriverExtended/blob/master/RemoteWebDriverExtended/RemoteWebDriverExtended.cs
        /// </summary>

        private static class DriverInfos
        {
            public static DriverInfo Chrome = new DriverInfo(
                executableFileName: GPAL.GPALSettings.ChromeDriverFilename,
                processName: "chromedriver",
                browserName: "chrome"
                );

            public static DriverInfo FireFox = new DriverInfo(
                executableFileName: GPAL.GPALSettings.FirefoxDriverFilename,
                processName: "geckodriver",
                browserName: "firefox"
                );

            public static DriverInfo Edge = new DriverInfo(
                executableFileName: GPAL.GPALSettings.EdgeDriverFilename,
                processName: "MSEdgeDriver",
                browserName: "edge"
                );

        }

        /// <summary>
        /// Holds the driver executable filename, driver process name, and browser name used by KillProcess to clean up
        /// a particular browser's webdriver process.
        /// </summary>
        private class DriverInfo
        {
            /// <summary>
            /// Creates a DriverInfo describing the driver executable, process name and browser name.
            /// </summary>
            /// <param name="executableFileName">The driver's executable filename (e.g. chromedriver.exe)</param>
            /// <param name="processName">The driver's process name (without .exe), used to find/kill the process</param>
            /// <param name="browserName">The browser's name (e.g. "chrome", "edge", "firefox")</param>
            public DriverInfo(string executableFileName, string processName, string browserName)
            {
                ExecutableFileName = executableFileName;
                ProcessName = processName;
                BrowserName = browserName;
            }
            public string ExecutableFileName { get; private set; }
            public string ProcessName { get; private set; }
            public string BrowserName { get; private set; }
        }
        /// <summary>
        /// Result of attempting to kill one or more processes by name.
        /// </summary>
        public class KillProcessesResult
        {
            public string Name { get; set; }
            public int Found { get; set; } = 0;
            public int Killed { get; set; } = 0;
            public bool Success { get; set; } = false;
        }

        /// <summary>
        /// Kills all known webdriver processes (geckodriver, chromedriver, MSEdgeDriver) and resets
        /// the "already updating" driver-update flag.
        /// </summary>
        /// <returns>An array of KillProcessesResult, one per webdriver process name attempted</returns>
        public static KillProcessesResult[] KillAllRunningWebDrivers()
        {
            List<KillProcessesResult> result = new List<KillProcessesResult>();
            KillProcessesResult[] kpa = result.ToArray();
            alreadyUpdating = false;

            try
            {
                result.Add(KillProcess(DriverInfos.FireFox.ProcessName));
                result.Add(KillProcess(DriverInfos.Chrome.ProcessName));
                result.Add(KillProcess(DriverInfos.Edge.ProcessName));

                // zero out all browsers 
                //GPAL.Browsers.Clear();
            } catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to kill processes", ex);
            }

            return kpa;
        }

        // The drivers GPAL started and has not ended, by process id, with each one's start time so that another
        // process given the same id later is never taken for one
        private static readonly Dictionary<int, DateTime> startedDrivers = new Dictionary<int, DateTime>();

        /// <summary>
        /// Remembers a driver GPAL started, so it is ended at exit if nothing ended it before.
        /// </summary>
        /// <param name="pid">The driver's process id</param>
        internal static void RememberDriver(int pid)
        {
            try
            {
                using (Process driver = Process.GetProcessById(pid))
                    lock (startedDrivers)
                        startedDrivers[pid] = driver.StartTime;
            }
            catch (Exception)
            {
                // it has already exited, so there is nothing to end later
            }
        }

        /// <summary>
        /// Ends a browser's driver by its process id, with everything it started, and forgets it.
        /// </summary>
        /// <param name="browserSettings">The browser whose driver is ended</param>
        internal static void EndDriver(BrowserSettings browserSettings)
        {
            int pid = browserSettings.ServiceDriverPid;

            if (0 < pid)
            {
                EndStartedDriver(pid);
                browserSettings.ServiceDriverPid = 0;
            }
        }

        /// <summary>
        /// Ends every driver GPAL started that is still running, such as one that hung when its browser closed. Run
        /// when the program exits.
        /// </summary>
        internal static void EndStartedDrivers()
        {
            List<int> pids;

            lock (startedDrivers)
                pids = startedDrivers.Keys.ToList();

            foreach (int pid in pids)
            {
                if (true == IsStartedDriverRunning(pid))
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Web driver [{pid}] was still running at exit, so it is being ended");

                EndStartedDriver(pid);
            }
        }

        /// <summary>
        /// Kills a driver GPAL started, and what it started, when it is still running, and forgets it once it has
        /// ended.
        /// </summary>
        /// <param name="pid">The driver's process id</param>
        private static void EndStartedDriver(int pid)
        {
            if (true == IsStartedDriverRunning(pid))
                KillProcessTree(pid);

            // one still running after the kill stays remembered, so it is ended again at exit
            if (false == IsStartedDriverRunning(pid))
                lock (startedDrivers)
                    startedDrivers.Remove(pid);
        }

        /// <summary>
        /// Whether a driver GPAL started is still running, checked by its start time so that a later process given
        /// the same id does not count.
        /// </summary>
        /// <param name="pid">The driver's process id</param>
        /// <returns>True while that driver runs</returns>
        private static bool IsStartedDriverRunning(int pid)
        {
            bool retVal = false;
            bool known;
            DateTime started;

            lock (startedDrivers)
                known = startedDrivers.TryGetValue(pid, out started);

            if (true == known)
                try
                {
                    using (Process driver = Process.GetProcessById(pid))
                        retVal = false == driver.HasExited && started == driver.StartTime;
                }
                catch (Exception)
                {
                    // no process with that id is running
                }

            return retVal;
        }

        /// <summary>
        /// Forgets a driver without ending it, for a browser handed on to another program.
        /// </summary>
        /// <param name="pid">The driver's process id</param>
        internal static void ForgetDriver(int pid)
        {
            lock (startedDrivers)
                startedDrivers.Remove(pid);
        }

        /// <summary>
        /// Asks the browser to close through its engine, so the profile records a normal exit.<br/>
        /// A killed browser marks the profile crashed, Chromium reopens the old session next launch, and the dying
        /// process can write its preferences over GPAL's.<br/>
        /// Does not wait; the caller waits and kills.
        /// </summary>
        /// <param name="aBrowser">The browser to close</param>
        private static void CloseBrowserGracefully(Browser aBrowser)
        {
            try
            {
                if (true == aBrowser.UsePuppeteer)
                {
                    // Browser.close is CDP's own orderly shutdown. Not over a dead connection: a browser that ended
                    // with its last window leaves nothing to answer, and sending would first try to reconnect.
                    if (false == aBrowser.BrowserSettings.Process?.HasExited && true == aBrowser.BrowserSettings.PuppeteerCommunicator.ConnectionOpen)
                    {
                        aBrowser.BrowserSettings.PuppeteerCommunicator.SendCommand(DevToolsMethods.BrowserClose, new Dictionary<string, object>(), null, timeoutSeconds: 2).GetAwaiter().GetResult();

                        // Browser.close sent. Mark the communicator closed so nothing more is sent and it does not
                        // reconnect when the browser drops.
                        aBrowser.BrowserSettings.PuppeteerCommunicator._isRunning = false;
                        aBrowser.BrowserSettings.PuppeteerCommunicator.Closed = true;

                        aBrowser.PuppeteerCommunicator._readerReadyTcs.SetCanceled();

                        if (false == aBrowser.PuppeteerCommunicator._usePipes)
                            aBrowser.PuppeteerCommunicator.CloseOutputWebSocketAsync().GetAwaiter();
                    }
                }
                else if (true == aBrowser.UseSelenium && null != aBrowser.BrowserDriver)
                {
                    // Quit ends the session, the browser and the driver. EdgeDriver can block on Quit for its whole
                    // command timeout, so cap it at GracefulExitMs. The caller kills what remains.
                    aBrowser.BrowserSettings.TabScripts?.Dispose();
                    aBrowser.BrowserSettings.TabScripts = null;

                    Task closing = Task.Run(() =>
                    {
                        // A browser reached with WithExistingCDPDebugPort was not started by this driver, and Quit
                        // ends only the driver's session, so its windows are closed first
                        if (true == aBrowser.BrowserSettings.UseExistingBrowser)
                            foreach (string handle in aBrowser.BrowserDriver.WindowHandles)
                            {
                                aBrowser.BrowserDriver.SwitchTo().Window(handle);
                                aBrowser.BrowserDriver.Close();
                            }

                        aBrowser.BrowserDriver?.Quit();
                    });

                    closing.Wait(GracefulExitMs);

                    // A browser this driver connected to may outlive its windows and the session, as Firefox did.
                    // Closing its main window is the same normal quit a person makes.
                    if (true == aBrowser.BrowserSettings.UseExistingBrowser && false == aBrowser.BrowserSettings.Process?.HasExited)
                        aBrowser.BrowserSettings.Process?.CloseMainWindow();
                }
                else
                {
                    // OttoMagic. The extension cannot quit the browser but can close all its windows, including
                    // page-opened ones; closing the last is a normal quit with the profile saved and a clean exit.
                    // Works headless, where there is no main window to message. Call CloseBrowser only if the
                    // browser is alive; on a dead one it retries a dead port and slows the next run. If the process
                    // survives, send CloseMainWindow.
                    if (true == aBrowser.UseOttoMagic && true == aBrowser.IsAlive)
                        aBrowser.MagicHelper.CloseBrowser();

                    if (false == aBrowser.BrowserSettings.Process?.HasExited)
                        aBrowser.BrowserSettings.Process?.CloseMainWindow();
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// The browser's driver is always ended; the argument is ignored.
        /// </summary>
        /// <param name="killWebDrivers">Not used</param>
        /// <param name="browser">The browser being closed; null when the program exits</param>
        /// <returns>The processes killed</returns>
        [Obsolete("The browser's driver is always ended; call KillAllRunningProcesses(browser) instead.")]
        public static KillProcessesResult[] KillAllRunningProcesses(bool killWebDrivers, IBrowser browser)
        {
            return KillAllRunningProcesses(browser);
        }

        /// <summary>
        /// Ends a browser GPAL launched, with its driver, or every GPAL browser when the program exits.
        /// </summary>
        /// <param name="browser">The browser being closed; null when the program exits</param>
        /// <returns>The processes killed</returns>
        public static KillProcessesResult[] KillAllRunningProcesses(IBrowser browser)
        {
            List<KillProcessesResult> result = new List<KillProcessesResult>();
            KillProcessesResult[] kpa = result.ToArray();

            try
            {
                int browserCount = GPAL.Browsers.Count;

                if (0 == browserCount)
                {
                    // no browsers: expected when the program used only forms, the converter or applications
                    return kpa;
                }

                // the browser being closed, or every browser when the program exits
                foreach (Browser aBrowser in GPAL.Browsers)
                {
                    if (aBrowser == browser || null == browser)
                        try
                        {
                            // Ask before killing. A killed browser records a crash, and Chromium then reopens the
                            // previous tabs regardless of session.restore_on_startup. It also holds the profile long
                            // enough to write its Preferences over GPAL's.
                            CloseBrowserGracefully(aBrowser);

                            if (null != aBrowser.BrowserSettings.Process)
                                try
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{aBrowser.BrowserSettings.BrowserType}] waiting on process [{aBrowser.BrowserSettings.Process.Id}] to exit", aBrowser, GPALObjectType.Browser);

                                    // Kill only if the browser is still running after GracefulExitMs.
                                    if (false == aBrowser.BrowserSettings.Process.WaitForExit(GracefulExitMs))
                                    {
                                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{aBrowser.BrowserSettings.BrowserType}] did not shut down within [{GracefulExitMs / 1000}] seconds, killing it. Its profile will read as crashed.", aBrowser, GPALObjectType.Browser);

                                        // the whole tree: the browser's own helper processes hold the profile
                                        // open after the browser itself has gone, and the profile cannot be deleted
                                        KillProcessTree(aBrowser.BrowserSettings.Process.Id);
                                        result.Add(KillProcess(aBrowser.BrowserSettings.Process));
                                    }
                                }
                                catch
                                { }

                            // The webdriver process is ours and holds no profile, so it is ended once its browser is
                            // closed, by its process id. Drivers other programs run are left alone.
                            if (true == aBrowser.UseSelenium)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, ">>> Disposing browser driver");

                                EndDriver(aBrowser.BrowserSettings);
                            }

                            Thread.Sleep(1_000);
                        }
                        catch
                        {

                        }
                        finally
                        {
                            if (true == aBrowser.BrowserSettings.TempProfileCreated)
                                ChromeProfileManager.RemoveTempUserProfile(aBrowser.BrowserSettings.ProfileDataDirectory);

                            // SweepAbandonedTempProfiles removes temp profiles left by earlier runs that never got
                            // here.
                            ChromeProfileManager.SweepAbandonedTempProfiles();
                        }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to kill processes", ex);
            }

            return result.ToArray();
        }
        /// <summary>
        /// Kills the windowless browser Edge leaves after exit. A browser with an extension background page starts a
        /// --no-startup-window replacement when its last window closes, which keeps the user data directory open.
        /// The next launch hands off to it and exits, with no debug port and no extension.
        /// <br/><br/>
        /// Only direct children of our browser with --no-startup-window are killed. Chrome does not do this, so
        /// nothing is found.
        /// </summary>
        /// <param name="browserSettings">The browser that was just closed.</param>
        internal static void EndWindowlessSuccessor(BrowserSettings browserSettings)
        {
            int ourPid = browserSettings.Process?.Id ?? 0;

            if (0 < ourPid)
            {
                // The replacement starts as our browser exits, so wait for the exit first.
                WaitForBrowserToExit(browserSettings, Browser.ProfileRestoreWaitMs);

                // Catch: a failed WMI query should not fail an otherwise finished close.
                try
                {
                    using (var search = new System.Management.ManagementObjectSearcher(
                        $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE ParentProcessId = {ourPid}"))
                        foreach (System.Management.ManagementObject found in search.Get())
                        {
                            string commandLine = found["CommandLine"]?.ToString() ?? string.Empty;

                            if (true == commandLine.Contains("--no-startup-window"))
                            {
                                int successor = Convert.ToInt32(found["ProcessId"]);

                                GPAL.PublishSimpleEvent(GPALEventType.INFO,
                                    $"Ending the windowless browser [{successor}] left behind by [{ourPid}], which would hold the profile open",
                                    browserSettings, GPALObjectType.Browser);

                                KillProcessTree(successor);
                            }
                        }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not look for a windowless browser left behind by [{ourPid}]",
                        browserSettings, GPALObjectType.Browser, ex);
                }
            }
        }

        /// <summary>
        /// Kill the PID and all its child processes
        /// CAVEAT: .NET Framework has no Process.Kill(entireProcessTree), so this runs taskkill /T /F
        /// </summary>
        internal static void KillProcessTree(int pid)
        {
            if (pid <= 0) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",  // /T = tree, /F = force
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (Process taskkillProcess = Process.Start(startInfo))
                {
                    taskkillProcess?.WaitForExit(5000); // up to 5 seconds
                }
            }
            catch
            {
                // Ignore errors during cleanup (access denied, already dead, etc.)
            }
        }
        /// <summary>
        /// Kill the process (browser launched by MagicHelper or puppeteer
        /// </summary>
        /// <param name="process"></param>
        public static KillProcessesResult KillProcess(Process process)
        {
            if (null != process)
            {
                KillProcessesResult result = new KillProcessesResult();

                string Name = "";
                try
                {
                    Name = process.ProcessName;
                    result.Name = Name;
                }
                catch (Exception)
                {
                    // process has already exited, nothing to do
                    result.Success = true;
                    return result;
                }

                process.Kill();
                process.WaitForExit();

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Killed process [{result.Name}]");
                return result;
            }
            return null;
        }

        /// <summary>
        /// Kills all running processes with the given name, waiting for each to exit.
        /// </summary>
        /// <param name="processName">Process name to kill (without .exe)</param>
        /// <returns>A KillProcessesResult describing how many matching processes were found and killed</returns>
        /// <example>
        /// <code>
        /// BrowserHelper.KillProcess("GPALRestAPI");
        /// </code>
        /// </example>
        public static KillProcessesResult KillProcess(string processName)
        {
            KillProcessesResult result = new KillProcessesResult { Name = processName };

            var processes = Process.GetProcessesByName(processName);

            result.Found = processes.Count();

            if (result.Found > 0)
            {
                foreach (var process in processes)
                {
                    do
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(); // Wait for the process to finish
                        }
                        catch
                        {

                        }
                    } while (false == process.HasExited);

                    result.Killed++;
                    result.Success = true;
                    result.Found++;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Killed process [{processName}]");
                }
            }
            else
            {
                result.Success = true;
            }

            return result;
        }

        /// <summary>
        /// Finds the first TCP port, starting from <paramref name="startPort"/> up to 65535, that nothing is
        /// listening on (a connection attempt fails with SocketException).
        /// </summary>
        /// <returns>The first free port number found</returns>
        /// <summary>
        /// Throws if a browser is already running on this run's profile.<br/>
        /// Chromium would hand the command line to it and exit, dropping the debug port while appearing to succeed.
        /// Reports the process id holding the profile.
        /// </summary>
        /// <exception cref="GPALException">Thrown when another browser already holds the profile</exception>
        /// <summary>
        /// True if <paramref name="ancestorId"/> is <paramref name="processId"/> or one of its ancestors. Matches a
        /// native messaging host's port announcement to the browser that started it when several start at once.
        /// <br/><br/>
        /// Windows records only the immediate parent, so walk up, at most <see cref="MaxParentHops"/>.
        /// </summary>
        /// <param name="processId">The host that announced a port.</param>
        /// <param name="ancestorId">The browser this launch started.</param>
        /// <returns>True when ancestorId is somewhere above processId.</returns>
        internal static bool CameFromProcess(int processId, int ancestorId)
        {
            bool retVal = false;
            int walking = processId;

            for (int hop = 0; hop < MaxParentHops && 0 != walking && false == retVal; hop++)
            {
                if (walking == ancestorId)
                    retVal = true;
                else
                    walking = ParentOf(walking);
            }

            return retVal;
        }

        /// <summary>
        /// Parent process id from WMI, or zero if the query fails. Works after the parent exits; the id is stored
        /// on the child.
        /// </summary>
        /// <param name="processId">The process whose parent is wanted.</param>
        /// <returns>The parent's id, or zero.</returns>
        private static int ParentOf(int processId)
        {
            int retVal = 0;

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}"))
                    foreach (System.Management.ManagementObject each in searcher.Get())
                        using (each)
                            retVal = Convert.ToInt32(each["ParentProcessId"]);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not ask who started process [{processId}]", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }
    }
}
