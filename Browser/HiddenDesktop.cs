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
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Manages the Win32 desktop objects browsers run on for a hidden desktop. Each has its own windows and input
    /// queue, so a browser there renders for real, unseen, and cannot take focus.
    /// <br/><br/>
    /// Not a Windows virtual desktop (Win+Tab), which only groups windows; a browser there still steals focus.
    /// <br/><br/>
    /// Desktops are tracked by name: same name, shared desktop. <see cref="Peek"/> shows each in turn.
    /// </summary>
    public static class HiddenDesktop
    {
        private const uint GenericAll = 0x10000000;
        private const uint DesktopSwitchDesktop = 0x0100;

        /// <summary>
        /// Desktop name for browsers that share one hidden desktop, and the prefix <see cref="NextName"/> builds
        /// unique names from. Used only when asked for by name.
        /// </summary>
        public const string DefaultName = "GPAL.Hidden";

        /// <summary>Default time Peek shows each desktop.</summary>
        internal const int DefaultPeekMs = 5_000;

        // by name, in the order they were made, which is the order Peek walks them in
        private static readonly Dictionary<string, IntPtr> desktops = new Dictionary<string, IntPtr>();
        private static readonly object gate = new object();

        // how many desktops have been handed out to browsers that did not name one
        private static int unnamed = 0;

        // Handle of the desktop showing before Peek switched away, the only handle that can switch back. Zero when no
        // peek is in progress.
        private static IntPtr cameFrom = IntPtr.Zero;

        private delegate bool EnumDesktopWindowsProc(IntPtr hWnd, IntPtr lParam);

        // Call Return on process exit, so a running peek switches the screen back.
        static HiddenDesktop()
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, args) => Return();
            AppDomain.CurrentDomain.UnhandledException += (sender, args) => Return();
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, uint dwDesiredAccess, IntPtr lpsa);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetThreadDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumDesktopWindowsProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SwitchDesktop(IntPtr hDesktop);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetThreadDesktop(uint dwThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] char[] pvInfo, uint nLength, out uint lpnLengthNeeded);

        // GetUserObjectInformation's index for the object's name
        private const int UoiName = 2;

        /// <summary>
        /// The hidden desktop this process was started on, or null for Default. A scheduler does not tell the
        /// workflow, so GPAL asks Windows once, on first use. A browser or program that names no desktop runs on this
        /// one.
        /// </summary>
        internal static readonly string ProcessDesktop = DesktopThisProcessIsOn();

        /// <summary>
        /// Name of the desktop the process started on, or null for Default. GPAL moves only the launch threads it
        /// creates, which cannot run before this class initializes, so the calling thread is on the process's
        /// desktop.
        /// </summary>
        /// <returns>The desktop's name, or null for the one in front of you.</returns>
        private static string DesktopThisProcessIsOn()
        {
            string retVal = null;

            // the thread's own handle, which is not closed: it belongs to the thread
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            char[] name = new char[256];

            // The normal desktop is Default, also over remote desktop. Check the name rather than which desktop has
            // the screen, so a locked machine does not read as hidden.
            if (IntPtr.Zero != desktop && true == GetUserObjectInformation(desktop, UoiName, name, (uint)(name.Length * sizeof(char)), out uint _))
            {
                string found = new string(name).TrimEnd('\0');

                if (false == string.IsNullOrEmpty(found) && false == string.Equals(found, "Default", StringComparison.OrdinalIgnoreCase))
                    retVal = found;
            }

            return retVal;
        }

        /// <summary>
        /// STARTUPINFO.lpDesktop value for launching onto desktop <paramref name="name"/>: WinSta0\name.
        /// </summary>
        /// <param name="name">The desktop's name.</param>
        /// <returns>The station and desktop pair.</returns>
        internal static string StartupNameFor(string name)
        {
            return $@"WinSta0\{name}";
        }

        /// <summary>
        /// A unique desktop name for a browser that asked for a hidden desktop without naming one. Names are
        /// session-wide, so it includes the process id; otherwise two GPAL programs would both use GPAL.Hidden.1.
        /// </summary>
        /// <returns>A desktop name nothing else will be given.</returns>
        internal static string NextName()
        {
            return $"{DefaultName}.{Process.GetCurrentProcess().Id}.{Interlocked.Increment(ref unnamed)}";
        }

        /// <summary>Every desktop made so far, in the order they were made.</summary>
        internal static IReadOnlyList<string> Names
        {
            get
            {
                lock (gate)
                    return desktops.Keys.ToList();
            }
        }

        /// <summary>
        /// Creates the named desktop, or opens it if it exists, so every browser can call this.
        /// </summary>
        /// <param name="name">What to call it. Browsers naming the same one share it.</param>
        /// <returns>True when there is a desktop to run on.</returns>
        internal static bool Ensure(string name)
        {
            bool retVal;

            lock (gate)
            {
                if (false == desktops.ContainsKey(name))
                {
                    IntPtr made = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);

                    if (IntPtr.Zero == made)
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not make the desktop [{name}]", null, GPALObjectType.Browser,
                            new Win32Exception(Marshal.GetLastWin32Error()));
                    else
                        desktops[name] = made;
                }

                retVal = true == desktops.ContainsKey(name);
            }

            return retVal;
        }

        /// <summary>
        /// Runs a launch function on the named desktop and returns its result. Everything the launch starts, and
        /// what that starts, opens there, so the driver and its browser share the desktop.
        /// <br/><br/>
        /// Runs on a new thread because SetThreadDesktop fails on a thread that owns a window or hook, as the
        /// workflow's thread usually does.
        /// </summary>
        /// <typeparam name="T">What the launch returns, a driver or a process.</typeparam>
        /// <param name="name">The desktop to launch on.</param>
        /// <param name="launch">The launch to run there.</param>
        /// <returns>Whatever the launch returned.</returns>
        internal static T Launch<T>(string name, Func<T> launch)
        {
            T retVal = default(T);
            ExceptionDispatchInfo failure = null;
            IntPtr handle = HandleFor(name);

            Thread launcher = new Thread(() =>
            {
                if (false == SetThreadDesktop(handle))
                    failure = ExceptionDispatchInfo.Capture(
                        new Win32Exception(Marshal.GetLastWin32Error(), $"Could not put the launching thread on [{name}]"));
                else
                {
                    // Rethrown on the workflow's thread, so a failed launch fails where it was called.
                    try
                    {
                        retVal = launch();
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                }
            });

            launcher.IsBackground = true;
            launcher.Start();
            launcher.Join();

            failure?.Throw();

            return retVal;
        }

        /// <summary>
        /// Number of visible top level windows on a desktop, queried without switching to it.
        /// </summary>
        /// <param name="name">The desktop to count.</param>
        /// <returns>The number of visible top level windows there.</returns>
        internal static int WindowCount(string name)
        {
            int retVal = 0;
            IntPtr handle = HandleFor(name);

            if (IntPtr.Zero != handle)
                EnumDesktopWindows(handle, (window, param) =>
                {
                    if (true == IsWindowVisible(window))
                        retVal++;

                    return true;
                }, IntPtr.Zero);

            return retVal;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowThreadProcessId(IntPtr window, out int processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        private const uint GW_OWNER = 4;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        private const uint WM_CLOSE = 0x0010;

        /// <summary>
        /// Asks a program's windows on a hidden desktop to close, as clicking X does.
        /// </summary>
        /// <remarks>
        /// Process.CloseMainWindow cannot see a hidden desktop's windows.
        /// </remarks>
        /// <param name="name">The desktop the program is on.</param>
        /// <param name="processId">The program being asked.</param>
        /// <returns>True when a window was asked.</returns>
        internal static bool CloseWindowsOf(string name, int processId)
        {
            bool retVal = false;
            IntPtr handle = HandleFor(name);

            if (IntPtr.Zero != handle)
                EnumDesktopWindows(handle, (window, param) =>
                {
                    GetWindowThreadProcessId(window, out int owner);

                    if (processId == owner && true == IsWindowVisible(window))
                        retVal = PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero) || retVal;

                    return true;
                }, IntPtr.Zero);

            return retVal;
        }

        /// <summary>
        /// The window a process has on a hidden desktop.
        /// </summary>
        /// <remarks>
        /// Process.MainWindowHandle sees only the caller's desktop.
        /// </remarks>
        /// <param name="name">The desktop to look on.</param>
        /// <param name="processId">The process whose window is wanted.</param>
        /// <returns>Its first visible top level window there, or IntPtr.Zero while it has none.</returns>
        internal static IntPtr MainWindowOf(string name, int processId)
        {
            IntPtr retVal = IntPtr.Zero;
            IntPtr handle = HandleFor(name);

            if (IntPtr.Zero != handle)
                EnumDesktopWindows(handle, (window, param) =>
                {
                    GetWindowThreadProcessId(window, out int owner);

                    // visible and owned by no other window, which is how Process.MainWindowHandle picks one; a
                    // popup or tooltip of the process is owned by its main window
                    if (processId == owner && true == IsWindowVisible(window) && IntPtr.Zero == GetWindow(window, GW_OWNER))
                        retVal = window;

                    return IntPtr.Zero == retVal;
                }, IntPtr.Zero);

            return retVal;
        }

        /// <summary>
        /// Every visible top level window on a hidden desktop.
        /// </summary>
        /// <param name="name">The desktop to look on.</param>
        /// <returns>The windows, in the order the desktop lists them.</returns>
        internal static List<IntPtr> WindowsOn(string name)
        {
            List<IntPtr> retVal = new List<IntPtr>();
            IntPtr handle = HandleFor(name);

            if (IntPtr.Zero != handle)
                EnumDesktopWindows(handle, (window, param) =>
                {
                    if (true == IsWindowVisible(window))
                        retVal.Add(window);

                    return true;
                }, IntPtr.Zero);

            return retVal;
        }

        /// <summary>
        /// Waits for a window with a matching title on a hidden desktop, as FormHelper.WaitForWindow does on the
        /// visible one.
        /// </summary>
        /// <param name="name">The desktop to watch.</param>
        /// <param name="titleMatches">What counts as the window being waited for.</param>
        /// <param name="waitTimeInSeconds">How long to give it.</param>
        /// <returns>The window, or IntPtr.Zero when none turned up in time.</returns>
        internal static IntPtr WaitForWindowOn(string name, Func<string, bool> titleMatches, int waitTimeInSeconds)
        {
            IntPtr retVal = IntPtr.Zero;
            IntPtr handle = HandleFor(name);

            for (int second = 0; second < waitTimeInSeconds && IntPtr.Zero == retVal && IntPtr.Zero != handle; second++)
            {
                Thread.Sleep(1000);

                EnumDesktopWindows(handle, (window, param) =>
                {
                    if (true == IsWindowVisible(window) && true == titleMatches(GenerallyPositive.GPALForm.FormHelper.TitleOf(window)))
                        retVal = window;

                    return IntPtr.Zero == retVal;
                }, IntPtr.Zero);
            }

            return retVal;
        }

        /// <summary>
        /// Total visible top level windows across all hidden desktops, to check that the browsers opened at all.
        /// </summary>
        /// <returns>The number of visible top level windows across all of them.</returns>
        internal static int WindowCount()
        {
            return Names.Sum(name => WindowCount(name));
        }

        /// <summary>
        /// Waits for a window on a desktop. If none appears, the browser likely opened on the user's desktop because
        /// the driver did not inherit the hidden one, and nothing else reports that.
        /// </summary>
        /// <param name="name">The desktop to watch.</param>
        /// <param name="timeoutInMs">How long to give it.</param>
        /// <returns>True when a window turned up.</returns>
        internal static bool HasWindows(string name, int timeoutInMs)
        {
            bool retVal = false;
            DateTime giveUpAt = DateTime.Now.AddMilliseconds(timeoutInMs);

            while (false == retVal && DateTime.Now < giveUpAt)
            {
                retVal = 0 < WindowCount(name);

                if (false == retVal)
                    Thread.Sleep(250);
            }

            return retVal;
        }

        /// <summary>
        /// True while <see cref="Peek"/> has the screen on a hidden desktop.
        /// </summary>
        internal static bool IsShowing
        {
            get
            {
                lock (gate)
                    return IntPtr.Zero != cameFrom;
            }
        }

        /// <summary>
        /// Switches the screen back to the desktop showing before the peek, ahead of the timer. Callable from
        /// anywhere: escape window, workflow, another thread, process shutdown. No-op when no peek is in progress.
        /// <br/><br/>
        /// Takes and clears the saved handle under the lock before switching, so concurrent callers switch once and
        /// never close the handle twice.
        /// </summary>
        /// <returns>True when the screen was handed back by this call.</returns>
        internal static bool Return()
        {
            bool retVal = false;
            IntPtr back;

            lock (gate)
            {
                back = cameFrom;
                cameFrom = IntPtr.Zero;
            }

            if (IntPtr.Zero != back)
            {
                retVal = SwitchDesktop(back);
                CloseDesktop(back);
            }

            return retVal;
        }

        /// <summary>
        /// Shows each hidden desktop made so far for <paramref name="forMsEach"/>, then switches back. The whole
        /// screen, keyboard and mouse switch with it. No Explorer runs there, so there is no taskbar or Start menu.
        /// <br/><br/>
        /// A small window on each desktop has a Return button and a countdown, and Escape works whichever window
        /// has focus. Return ends the walk. Every desktop but the last also has a Next button.
        /// <br/><br/>
        /// The screen also comes back when the time runs out, when <see cref="Return"/> is called, or when the
        /// process ends. The walk runs on a foreground thread, so the process waits for the screen before it exits.
        /// </summary>
        /// <param name="forMsEach">How long to hold on each one. Zero or less holds until the escape window is used.</param>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        internal static string Peek(int forMsEach)
        {
            List<KeyValuePair<string, IntPtr>> walk;

            // Every desktop created so far, including ones with no windows yet.
            lock (gate)
                walk = desktops.ToList();

            // A desktop goes when its browser closes, so between runs there can be none even after several were made
            return Walk(walk, forMsEach, "no hidden desktop is open right now");
        }

        /// <summary>
        /// Shows one desktop rather than walking them all. Everything <see cref="Peek(int)"/> says about the
        /// screen, the escape window and the ways back applies here too.
        /// </summary>
        /// <param name="name">The desktop to show.</param>
        /// <param name="forMs">How long to hold it. Zero or less holds until the escape window is used.</param>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        internal static string PeekOne(string name, int forMs)
        {
            List<KeyValuePair<string, IntPtr>> walk;

            // Show the desktop now. If its browser is still starting, create the desktop here as the browser would;
            // the browser then opens on it on screen.
            Ensure(name);

            lock (gate)
                walk = desktops.Where(desktop => desktop.Key == name).ToList();

            return Walk(walk, forMs, $"the hidden desktop [{name}] could not be made");
        }

        /// <summary>
        /// The walk itself, shared by looking at one desktop and looking at all of them.
        /// </summary>
        /// <param name="walk">The desktops to show, in the order to show them.</param>
        /// <param name="forMsEach">How long to hold each one.</param>
        /// <param name="nothingToSee">What to say when the list is empty.</param>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        private static string Walk(List<KeyValuePair<string, IntPtr>> walk, int forMsEach, string nothingToSee)
        {
            string retVal = null;

            if (0 == walk.Count)
                retVal = nothingToSee;
            else if (true == IsShowing)
                retVal = "the screen is already on a hidden desktop";
            else
            {
                // Open the current input desktop with switch access only; a normal process is refused full access.
                // This handle is the only way back, and must be taken before the switch changes the input desktop.
                IntPtr from = OpenInputDesktop(0, false, DesktopSwitchDesktop);

                if (IntPtr.Zero == from)
                    retVal = $"no handle on the desktop you are on [{Marshal.GetLastWin32Error()}], so nothing was switched";
                else
                {
                    lock (gate)
                        cameFrom = from;

                    // Start the DesktopGuard process before switching; it outlives this process, which every other
                    // way back depends on.
                    Process guard = DesktopGuard.Start(0 < forMsEach ? forMsEach * walk.Count : 0);

                    // Foreground thread, so an exiting process waits for the screen to switch back.
                    Thread viewer = new Thread(() =>
                    {
                        try
                        {
                            for (int at = 0; at < walk.Count; at++)
                            {
                                KeyValuePair<string, IntPtr> desktop = walk[at];

                                // Put the escape window up before switching, so Escape already works when the
                                // desktop appears. Every desktop but the last also gets a Next button.
                                using (Escape escape = Escape.ShowOn(desktop.Value, desktop.Key, forMsEach, at < walk.Count - 1))
                                {
                                    if (false == SwitchDesktop(desktop.Value))
                                        continue;

                                    if (true == escape.WaitToLeave(forMsEach))
                                        break;
                                }
                            }
                        }
                        finally
                        {
                            Return();
                            DesktopGuard.Stop(guard);
                        }
                    });

                    viewer.IsBackground = false;
                    viewer.Start();
                }
            }

            if (null != retVal)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Cannot look at the hidden desktops: {retVal}", null, GPALObjectType.Browser);

            return retVal;
        }

        /// <summary>
        /// Releases GPAL's handles on every desktop, switching the screen back first if a peek is running.
        /// <br/><br/>
        /// Closes nothing running on them; a desktop lives until its last window, process and handle are gone, so
        /// close its browsers first. GPAL calls this at shutdown; a workflow can call it earlier through
        /// GPAL.HiddenDesktop.
        /// </summary>
        internal static void CloseAll()
        {
            // Switch the screen back first.
            Return();

            lock (gate)
            {
                foreach (IntPtr handle in desktops.Values)
                    CloseDesktop(handle);

                desktops.Clear();
            }
        }

        /// <summary>
        /// Closes everything on a hidden desktop, then releases the handle.
        /// </summary>
        /// <remarks>
        /// Posts WM_CLOSE to every window, then kills what remains after CLOSING_MS. The calling process's own windows
        /// are left alone.
        /// </remarks>
        /// <param name="name">The desktop to finish with.</param>
        internal static void CloseEverythingOn(string name)
        {
            // Skip this process's own windows, or a workflow a scheduler started on this desktop would close itself.
            int self = Process.GetCurrentProcess().Id;

            foreach (IntPtr window in WindowsOn(name))
                if (self != OwnerOf(window))
                    PostMessage(window, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            DateTime giveUpAt = DateTime.Now.AddMilliseconds(CLOSING_MS);

            while (0 < WindowsOn(name).Count(window => self != OwnerOf(window)) && DateTime.Now < giveUpAt)
                Thread.Sleep(BETWEEN_LOOKS_MS);

            foreach (IntPtr window in WindowsOn(name))
            {
                int owner = OwnerOf(window);

                // A program that exited before the kill is not a failure.
                if (self != owner)
                    try { Process.GetProcessById(owner).Kill(); }
                    catch (Exception) { }
            }

            Close(name);
        }

        // the process a window belongs to
        private static int OwnerOf(IntPtr window)
        {
            GetWindowThreadProcessId(window, out int retVal);

            return retVal;
        }

        // how long the programs on a desktop are given to close themselves before they are ended
        private const int CLOSING_MS = 5000;

        // how often the desktop is looked at while they go
        private const int BETWEEN_LOOKS_MS = 200;

        /// <summary>
        /// Releases GPAL's handle on one desktop. As with <see cref="CloseAll"/>, this closes nothing running there.
        /// </summary>
        /// <param name="name">The desktop to let go of.</param>
        internal static void Close(string name)
        {
            lock (gate)
                if (true == desktops.TryGetValue(name, out IntPtr handle))
                {
                    CloseDesktop(handle);
                    desktops.Remove(name);
                }
        }

        /// <summary>
        /// The handle for a desktop already made, or zero when there is not one under that name.
        /// </summary>
        /// <param name="name">The desktop's name.</param>
        /// <returns>Its handle, or zero.</returns>
        private static IntPtr HandleFor(string name)
        {
            IntPtr retVal;

            lock (gate)
                desktops.TryGetValue(name, out retVal);

            return retVal;
        }

        /// <summary>
        /// The escape window for leaving a hidden desktop, which has no taskbar, Start menu or Explorer. Before the
        /// switch it goes up with a Return button, Escape as a hot key, and a countdown to the automatic switch back.
        /// <br/><br/>
        /// The hot key works when a browser has focus and covers the button. It is registered by a thread on the
        /// hidden desktop, so Escape elsewhere is unaffected and returns to the browsers when the peek ends.
        /// <br/><br/>
        /// Runs on a new thread that calls SetThreadDesktop first (it fails on a thread owning a window or hook) and
        /// pumps its own messages, since no other message loop runs on that desktop.
        /// </summary>
        private sealed class Escape : IDisposable
        {
            private const int HotKeyId = 0xE5CA;
            private const uint VkEscape = 0x1B;
            private const uint ModNoRepeat = 0x4000;

            // Time limit for a peek with no time given when the escape window failed to open, leaving the timer as
            // the only way back.
            private const int BlindHoldInMs = 15_000;

            // Time limit for a peek with no time given when the escape window did open. Never Timeout.Infinite: the
            // hot key can fail to register and a fullscreen browser can hide the button.
            private const int MaxHoldInMs = 600_000;

            private readonly ManualResetEvent leaving = new ManualResetEvent(false);
            private readonly ManualResetEvent onward = new ManualResetEvent(false);
            private readonly ManualResetEvent ready = new ManualResetEvent(false);
            private readonly Thread thread;

            private volatile EscapeForm form;
            private volatile bool live;

            private Escape(IntPtr desktop, string name, int forMs, bool hasNext)
            {
                thread = new Thread(() =>
                {
                    if (false == SetThreadDesktop(desktop))
                        ready.Set();
                    else
                    {
                        EscapeForm showing = new EscapeForm(name, Leave, true == hasNext ? Next : (Action)null, 0 < forMs ? forMs : MaxHoldInMs);

                        showing.Shown += (sender, args) =>
                        {
                            live = true;
                            ready.Set();
                        };

                        form = showing;

                        System.Windows.Forms.Application.Run(showing);
                    }
                });

                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }

            /// <summary>
            /// Puts the escape window on a desktop and waits until it is showing, so the screen never switches before
            /// it is ready.
            /// </summary>
            /// <param name="desktop">The desktop's handle.</param>
            /// <param name="name">The desktop's name, which is what the window says it is showing.</param>
            /// <param name="forMs">How long the peek holds this desktop, which the window counts down. Zero or less counts down the longest hold.</param>
            /// <param name="hasNext">Puts a Next button on the window.</param>
            /// <returns>The escape window, whether or not it opened.</returns>
            internal static Escape ShowOn(IntPtr desktop, string name, int forMs, bool hasNext)
            {
                Escape retVal = new Escape(desktop, name, forMs, hasNext);

                retVal.ready.WaitOne(5_000);

                if (false == retVal.live)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"No way out could be put on the hidden desktop [{name}], so the clock is the only way back",
                        null, GPALObjectType.Browser);

                return retVal;
            }

            /// <summary>
            /// Holds until Return, Escape, Next or the time runs out.
            /// </summary>
            /// <param name="forMs">How long to hold. Zero or less holds until a button or Escape.</param>
            /// <returns>True for Return or Escape.</returns>
            internal bool WaitToLeave(int forMs)
            {
                bool retVal;
                int limit = 0 < forMs ? forMs : (true == live ? MaxHoldInMs : BlindHoldInMs);

                retVal = 0 == WaitHandle.WaitAny(new WaitHandle[] { leaving, onward }, limit);

                return retVal;
            }

            /// <summary>Closes the escape window and unregisters the Escape hot key.</summary>
            public void Dispose()
            {
                EscapeForm closing = form;

                if (null != closing && true == closing.IsHandleCreated)
                    closing.BeginInvoke(new Action(() => closing.Close()));

                thread.Join(5_000);

                leaving.Dispose();
                onward.Dispose();
                ready.Dispose();
            }

            /// <summary>Somebody asked to leave, by the button, by Escape, or by the key while the window has focus.</summary>
            private void Leave()
            {
                leaving.Set();
            }

            /// <summary>Somebody asked for the next desktop.</summary>
            private void Next()
            {
                onward.Set();
            }

            /// <summary>
            /// Shows which desktop is on screen and holds the Return button. Borderless and topmost so it stays
            /// visible over a maximized browser.
            /// </summary>
            private sealed class EscapeForm : Form
            {
                private const int WmHotKey = 0x0312;

                private readonly Action leave;

                // counts down to the screen going back on its own, once a second
                private readonly System.Windows.Forms.Timer countdown = new System.Windows.Forms.Timer { Interval = 1_000 };

                internal EscapeForm(string desktop, Action leave, Action next, int holdMs)
                {
                    this.leave = leave;

                    Rectangle screen = Screen.PrimaryScreen.Bounds;

                    FormBorderStyle = FormBorderStyle.None;
                    StartPosition = FormStartPosition.Manual;
                    ShowInTaskbar = false;
                    TopMost = true;
                    BackColor = Color.FromArgb(24, 24, 24);
                    ForeColor = Color.White;
                    Padding = new Padding(6);
                    Size = new Size(300, 62);

                    // Bottom left, out of the way of the browser.
                    Location = new Point(screen.Left + 12, screen.Bottom - Height - 12);
                    KeyPreview = true;

                    Button back = new Button
                    {
                        Text = "Return   (Esc)",
                        Dock = DockStyle.Fill,
                        FlatStyle = FlatStyle.System,
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    };

                    Label caption = new Label
                    {
                        Text = desktop,
                        Dock = DockStyle.Top,
                        Height = 18,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                    };

                    back.Click += (sender, args) => this.leave();

                    // the clock starts when the window is up, which is just before the screen switches to it
                    DateTime returnAt = DateTime.MinValue;

                    Shown += (sender, args) =>
                    {
                        returnAt = DateTime.Now.AddMilliseconds(holdMs);
                        caption.Text = CaptionFor(desktop, returnAt);
                        countdown.Start();
                    };

                    countdown.Tick += (sender, args) => caption.Text = CaptionFor(desktop, returnAt);

                    // Add the Fill control before the docked ones; WinForms docks in reverse order of the Controls
                    // collection.
                    Controls.Add(back);

                    if (null != next)
                    {
                        Button onward = new Button
                        {
                            Text = "Next",
                            Dock = DockStyle.Right,
                            Width = 90,
                            FlatStyle = FlatStyle.System,
                            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        };

                        onward.Click += (sender, args) => next();
                        Controls.Add(onward);
                    }

                    Controls.Add(caption);

                    KeyDown += (sender, args) =>
                    {
                        if (Keys.Escape == args.KeyCode)
                            this.leave();
                    };
                }

                protected override void OnHandleCreated(EventArgs e)
                {
                    base.OnHandleCreated(e);

                    // This thread registers the hot key on the hidden desktop, so Escape is captured only there,
                    // while this window is open.
                    if (false == RegisterHotKey(Handle, HotKeyId, ModNoRepeat, VkEscape))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Escape could not be taken on the hidden desktop, so the button is the way out",
                            null, GPALObjectType.Browser, new Win32Exception(Marshal.GetLastWin32Error()));
                }

                /// <summary>The desktop's name and the time left before the screen goes back, as minutes and seconds.</summary>
                private static string CaptionFor(string desktop, DateTime returnAt)
                {
                    TimeSpan left = returnAt - DateTime.Now;

                    if (TimeSpan.Zero > left)
                        left = TimeSpan.Zero;

                    return $"{desktop}   returning in {(int)left.TotalMinutes}:{left.Seconds:00}";
                }

                protected override void OnHandleDestroyed(EventArgs e)
                {
                    countdown.Stop();
                    countdown.Dispose();
                    UnregisterHotKey(Handle, HotKeyId);

                    base.OnHandleDestroyed(e);
                }

                protected override void WndProc(ref Message m)
                {
                    if (WmHotKey == m.Msg && HotKeyId == m.WParam.ToInt32())
                        leave();

                    base.WndProc(ref m);
                }
            }
        }
    }

    /// <summary>
    /// One hidden desktop, returned by the browser on it. Holds only the name; <see cref="HiddenDesktop"/> keeps
    /// the desktop, so it stays valid when another browser takes it over.
    /// </summary>
    internal class GPALHiddenDesktop : IHiddenDesktop
    {
        internal GPALHiddenDesktop(string name)
        {
            Name = name;
        }

        /// <summary>What this desktop is called.</summary>
        public string Name { get; }

        /// <summary>Visible top level windows on it right now.</summary>
        public int WindowCount => HiddenDesktop.WindowCount(Name);

        /// <summary>Puts this desktop on the screen for the default time.</summary>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        public string Peek() => HiddenDesktop.PeekOne(Name, HiddenDesktop.DefaultPeekMs);

        /// <summary>Puts this desktop on the screen for as long as asked.</summary>
        /// <param name="forMs">How long to hold it. Zero or less holds until the escape window is used.</param>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        public string Peek(int forMs) => HiddenDesktop.PeekOne(Name, forMs);

        /// <summary>Gives up GPAL's handle on this desktop.</summary>
        public void Close() => HiddenDesktop.Close(Name);

        /// <summary>
        /// Closes what is running here and then gives up the handle, so the desktop and everything on it is
        /// finished with rather than left running where nobody can see it.
        /// </summary>
        public void CloseEverything() => HiddenDesktop.CloseEverythingOn(Name);
    }

    /// <summary>
    /// Every hidden desktop this process has made, and the screen operations that move between them. This is
    /// what GPAL.HiddenDesktop hands back, and it holds no state of its own.
    /// </summary>
    internal class GPALHiddenDesktops : IHiddenDesktops
    {
        /// <summary>Every desktop made so far, in the order they were made.</summary>
        public IReadOnlyList<string> Names => HiddenDesktop.Names;

        /// <summary>
        /// One desktop by name. Unknown names are returned rather than refused; a gone desktop and a never-made one
        /// both report no windows.
        /// </summary>
        /// <param name="desktopName">The desktop to reach.</param>
        /// <returns>That desktop.</returns>
        public IHiddenDesktop this[string desktopName] => new GPALHiddenDesktop(desktopName);

        /// <summary>Windows on every hidden desktop added up.</summary>
        public int WindowCount => HiddenDesktop.WindowCount();

        /// <summary>True while the screen is on a hidden desktop.</summary>
        public bool IsShowing => HiddenDesktop.IsShowing;

        /// <summary>Walks every hidden desktop made so far, the default time each.</summary>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        public string Peek() => HiddenDesktop.Peek(HiddenDesktop.DefaultPeekMs);

        /// <summary>Walks every hidden desktop made so far, holding each one as long as asked.</summary>
        /// <param name="forMsEach">How long to hold each one.</param>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        public string Peek(int forMsEach) => HiddenDesktop.Peek(forMsEach);

        /// <summary>Puts one named desktop on the screen for the default time.</summary>
        /// <param name="desktopName">The desktop to show.</param>
        /// <returns>Null when the screen switched, otherwise why it did not.</returns>
        public string Peek(string desktopName) => HiddenDesktop.PeekOne(desktopName, HiddenDesktop.DefaultPeekMs);

        /// <summary>Puts the screen back on the desktop a peek took it from.</summary>
        /// <returns>True when the screen was handed back by this call.</returns>
        public bool Return() => HiddenDesktop.Return();

        /// <summary>Gives up GPAL's handles on every hidden desktop.</summary>
        public void CloseAll() => HiddenDesktop.CloseAll();

        /// <summary>
        /// Closes what is running on every hidden desktop this process made, and gives up the handles.
        /// </summary>
        public void CloseEverything()
        {
            foreach (string name in HiddenDesktop.Names.ToList())
                HiddenDesktop.CloseEverythingOn(name);
        }
    }
}
