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

            // The visible browser window may belong to the target process OR a descendant of it. On Firefox,
            // geckodriver (the pid we are handed) spawns firefox.exe as a child, and the window belongs to that
            // child - a direct pid match finds nothing. So match any visible top-level window
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
