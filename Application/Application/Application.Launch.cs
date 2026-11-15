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

//extern alias mUIA;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using static GenerallyPositive.Enums;
// The HiddenDesktop property hides the static HiddenDesktop class, so the class is used through this alias. Browser
// does the same.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Application
{
    public partial class Application
    {
        /// <summary>
        /// Launch the application specified in the path.<br/>
        /// Find the root automation element and place into applicationSettings.RootAutomationElement<br/>
        /// Used in conjunction with .WithParameters
        /// </summary>
        /// <param name="applicationPath">Path to application to open/run/launch</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllExceptParameters Open([Directory()] string applicationPath)
        {
            IntPtr processHandle = IntPtr.Zero;
            Process[] processes;
            StringBuilder parameters = new StringBuilder();
            bool first = true;

            foreach (string parameter in applicationSettings.Parameters)
            {
                if (false == first)
                    parameters.Append(" ");
                else
                    first = false;
                parameters.Append(parameter);
            }

            if (null != Process)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Unable to Open/control more than one program. Please use another Application.", this, GPALObjectType.Application);
                return this;
            }

            ApplicationPath = applicationPath;

            // On a hidden desktop, always launch a new instance; any copy already running is on the user's desktop.
            if (true == applicationSettings.HiddenDesktop)
            {
                applicationSettings.NewInstance = true;

                if (false == Desktops.Ensure(applicationSettings.HiddenDesktopName))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Windows would not make the desktop [{applicationSettings.HiddenDesktopName}], so [{applicationPath}] runs on the one you are looking at.", this, GPALObjectType.Application);

                    applicationSettings.HiddenDesktop = false;
                }
                else if (false == hiddenDesktopReported)
                {
                    hiddenDesktopReported = true;

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{applicationPath}] runs on [{applicationSettings.HiddenDesktopName}], where UIAutomation drives it and hardware input cannot reach it. Peek at the desktop to watch it or to use it by hand.", this, GPALObjectType.Application);
                }
            }

            // Attach to a running copy of the program, or launch one.
            try
            {
                processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(applicationPath));
                bool attached = false;

                // WithNewInstance launches even when the program is already running, for when its existing window is
                // not the one the workflow wants.
                if (false == applicationSettings.NewInstance && null != processes && 0 < processes.Length)
                {
                    // A running instance has its window already. Probe each for two seconds and skip windowless or
                    // background matches.
                    foreach (Process process in processes)
                    {
                        applicationSettings.Process = process;
                        AutomationElement existingRoot = WaitForRootAutomationElement(process, 2000);
                        if (null != existingRoot)
                        {
                            applicationSettings.RootAutomationElement = existingRoot;
                            processHandle = process.MainWindowHandle;
                            attached = true;
                            break;
                        }
                    }
                }

                if (false == attached)
                {
                    applicationSettings.Process = true == applicationSettings.HiddenDesktop
                        ? StartOnHiddenDesktop(applicationPath, parameters.ToString())
                        : Process.Start(new ProcessStartInfo(applicationPath, parameters.ToString()) { UseShellExecute = true });

                    // poll until the main window exists and its root automation element is retrievable,
                    // up to WaitForWindowTimeout seconds.
                    AutomationElement root = WaitForRootAutomationElement(applicationSettings.Process,
                        Math.Max(1, applicationSettings.WaitForWindowTimeout) * 1000);

                    if (null != root)
                    {
                        applicationSettings.RootAutomationElement = root;
                        processHandle = applicationSettings.Process.MainWindowHandle;
                    }
                    // A launcher hands its work to a running process and exits, so it has no window. explorer.exe
                    // passes the folder to the shell, which owns the window. Not an error; find the window by name.
                    else if (true == applicationSettings.Process?.HasExited)
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{applicationPath}] exited as soon as it started, which is what a launcher does. Use WaitForWindow or WaitForWindowRegex to settle on the window it opened.", this, GPALObjectType.Application);
                    else
                    {
                        string msg = $"Timed out after [{applicationSettings.WaitForWindowTimeout}]s waiting for the main window / root automation element of [{applicationPath}]{(true == applicationSettings.HiddenDesktop ? $" on [{applicationSettings.HiddenDesktopName}]" : string.Empty)}.";
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, msg, this, GPALObjectType.Application);
                        throw new GPALException(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to launch [{applicationPath}].", this, GPALObjectType.Application, ex);
                return this;
            }

            if (null == applicationSettings.Process)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Unable to launch [{applicationPath}].", this, GPALObjectType.Application);
            else
            {
                // Do not bring the window forward when the launcher has exited (asking for its window throws) or when
                // the program is on a hidden desktop.
                if (false == applicationSettings.Process.HasExited && false == applicationSettings.HiddenDesktop)
                    ApplicationHelper.TopProcess(applicationSettings.Process);

                if (true == applicationSettings.HiddenDesktop && false == hiddenDesktopChecked)
                {
                    hiddenDesktopChecked = true;

                }

                CurrentUOW.ActionCalled = true;
            }

            return this;
        }
        /// <summary>
        /// Polls until the process exposes a usable UI Automation root: a non-zero main window handle whose
        /// <see cref="AutomationElement.FromHandle"/> returns a live element.
        /// </summary>
        /// <param name="process">The process to acquire the root automation element for.</param>
        /// <param name="timeoutMs">Maximum time to wait, in milliseconds.</param>
        /// <returns>The root <see cref="AutomationElement"/>, or <c>null</c> if it was not ready before the timeout.</returns>
        private AutomationElement WaitForRootAutomationElement(Process process, int timeoutMs)
        {
            if (null == process)
                return null;

            Stopwatch stopwatch = Stopwatch.StartNew();

            // Let a GUI app pump its message loop and create its window. This throws immediately for a
            // process with no message loop (e.g. a console app), so it never wastes the timeout on those.
            try { process.WaitForInputIdle(timeoutMs); } catch { /* no message loop; fall through to polling */ }

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    process.Refresh();

                    // MainWindowHandle only searches the caller's desktop, so a program on a hidden desktop has none
                    // here.
                    IntPtr handle = true == applicationSettings.HiddenDesktop
                                  ? Desktops.MainWindowOf(applicationSettings.HiddenDesktopName, process.Id)
                                  : process.MainWindowHandle;
                    if (IntPtr.Zero != handle)
                    {
                        AutomationElement root = AutomationElement.FromHandle(handle);
                        if (null != root)
                            return root;
                    }
                }
                catch
                {
                    // Window/handle not ready yet (or transiently invalid); keep polling until timeout.
                }

                Thread.Sleep(100);
            }

            return null;
        }
        /// <summary>
        /// Close/terminate the application.<br/>
        /// This terminates the fluent interface and nothing can be chained to this command.
        /// </summary>
        public void Close()
        {
            // Process.Close only releases our handle. CloseMainWindow asks the window to close, like clicking the X. A
            // program that does not exit (e.g. asking whether to save) is killed.
            if (null != Process && false == HasGone(Process))
            {
                // CloseMainWindow finds nothing on a hidden desktop, so send the close request to the window directly.
                if (true == ApplicationSettings.HiddenDesktop)
                    Desktops.CloseWindowsOf(ApplicationSettings.HiddenDesktopName, Process.Id);
                else
                    Process.CloseMainWindow();

                if (false == Process.WaitForExit(CLOSING_MS))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{ApplicationPath}] did not close within [{CLOSING_MS / 1000}]s of being asked, so it is being ended.", this, GPALObjectType.Application);

                    Process.Kill();
                }
            }

            Process?.Close();
            Process = null;

            // A desktop made for this program alone goes with it. A named desktop is shared and stays.
            if (true == ApplicationSettings.HiddenDesktop && true == ApplicationSettings.HiddenDesktopOwned)
                Desktops.Close(ApplicationSettings.HiddenDesktopName);
        }

        // how long a program is given to close itself before it is ended
        private const int CLOSING_MS = 5000;

        /// <summary>
        /// Starts the program on the hidden desktop by naming it in the CreateProcess startup information.
        /// </summary>
        /// <remarks>
        /// Process.Start cannot name a desktop, so CreateProcess is called directly.
        /// </remarks>
        /// <param name="applicationPath">The program to run</param>
        /// <param name="parameters">Its parameters, joined into one string</param>
        /// <returns>The started process</returns>
        private Process StartOnHiddenDesktop(string applicationPath, string parameters)
        {
            Native.STARTUPINFO startup = new Native.STARTUPINFO
            {
                cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.STARTUPINFO>(),
                lpDesktop = Desktops.StartupNameFor(applicationSettings.HiddenDesktopName)
            };

            string quoted = "\"" + applicationPath + "\"";
            string commandLine = true == string.IsNullOrWhiteSpace(parameters) ? quoted : quoted + " " + parameters;

            if (false == Native.CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero,
                                              Path.GetDirectoryName(applicationPath), ref startup, out Native.PROCESS_INFORMATION started))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), $"Could not start [{applicationPath}] on [{applicationSettings.HiddenDesktopName}]");

            Process retVal = Process.GetProcessById((int)started.dwProcessId);

            Native.CloseHandle(started.hProcess);
            Native.CloseHandle(started.hThread);

            return retVal;
        }

        private static bool HasGone(Process process)
        {
            bool retVal = true;

            try
            {
                retVal = process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // no program was ever attached to this, so there is nothing to close
            }

            return retVal;
        }
    }
}
