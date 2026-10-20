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
    /// <summary>
    /// Application object that contains the fluent methods to create your Application workflow.<br/>
    /// Instantiated using GPAL.Application
    /// </summary>
    /// <example>
    /// <code>
    ///     Selector gpalTeamSelector = (Selector)GPAL.Selector
    ///         .WithXPath("/Pane/Document/Group[3]/Group/Tree/ListItem[1]/TreeItem[4]/Hyperlink");
    ///     
    ///     GPAL.Application
    ///         .Open(@"C:\Users\CurrentUser\AppData\Local\Microsoft\Teams\current\teams.exe")
    ///         .WithSelector(gpalTeamSelector)
    ///         .LeftClick();
    /// </code>
    /// </example>
    public class Application
        : IApplication
    {
        /// <summary>
        /// Delegate callback for the CallIfFound/CallIfNotFound EventHandlers which will be invoked when a selector is found/not found<br/>
        /// CallIf handlers may be defined at two levels: At each selector or for a Unit of Work (UOW) [will be invoked for each Selector in the UOW]<br/>
        /// A UOW is defined as all Selectors between actions (first action is Open)<br/>
        /// More than one callback handler can be defined and subsequent handlers will only be invoked based upon return code values
        /// </summary>
        /// <example>
        /// <code>
        /// public static int CallIfFound(Application application, List&lt;GPALAutomationElement&gt; foundElements, List&lt;GPALAutomationElement&gt; matchedElements, Selector selector, bool matchedAll)
        /// {
        ///     int retVal = 1; // we are going to handle this
        ///     ElementAssistant elementAssistant = (ElementAssistant)GPAL.ElementAssistant(application);
        ///     foreach (GPALAutomationElement element in foundElements)
        ///     {
        ///         elementAssistant
        ///             .ForSelector(selector)  // for interaction type, offsetx and offsety
        ///             .WithElement(element).WithHardware
        ///             .LeftClick();
        ///     }
        ///     return retVal;
        /// }
        /// </code>
        /// </example>
        /// <param name="application">The current application running</param>
        /// <param name="foundElements">All found elements</param>
        /// <param name="matchedElements">All matched elements.<br/>If a 'Match' criteria is set on a Selector, otherwise all found</param>
        /// <param name="selector">The Selector used</param>
        /// <param name="matchedAll">true - all elements matched in foundElements<br/>false - matchedElements out of foundElements</param>
        /// <returns>
        /// try the next handler in the chain based upon handler return value<br/>
        /// 0 = not handled, bubble to next handler<br/>
        /// 1 = handled and continue with the program<br/>
        /// -1 = terminate program<br/>
        /// </returns>
        public delegate CallIfStatus CallIfDelegate(Application application, List<IGPALAutomationElement> foundElements, List<IGPALAutomationElement> matchedElements, Selector selector, bool matchedAll);
        /// <summary>
        /// Delegate callback for the CallAfterFillIn EventHandler which will be invoked after each row of tokens is consumed.<br/>
        /// Invoked whenever FillInFrom/FilllInFromDatabase/FillInFrom are used
        /// </summary>
        /// <example>
        /// <code>
        /// public static int CallAFterFillIn(Application application, GPALGrid tokens, int tokenIdx) // text filled in search box, click 'Search' button
        /// {
        ///     Selector cSharpMenuItem = (Selector)GPAL.Selector
        ///     .WithName("C#MenuItem")
        ///     .WithXPath("/Window/MenuBar/MenuItem[6]/Menu/MenuItem[4]/Menu/MenuItem[2]");
        ///     //.WithSimulateMouse(true); // move the mouse across the screen when navigating to this element
        ///     
        ///     application
        ///         .WithSelector(cSharpMenuItem)
        ///         .Hover()
        ///         .WaitFor(5000)
        ///         .LeftClick();
        /// }
        /// </code>
        /// </example>
        /// <param name="application">The GPAL Application object being filled in</param>
        /// <param name="tokens">The current GPALGrid of tokens</param>
        /// <param name="tokenIdx">The current token row index</param>
        /// <returns>-1 if the handler requests the application to exit, otherwise the return value is ignored</returns>
        public delegate CallIfStatus CallAfterFillInDelegate(Application application, IGPALGrid<string> tokens, int tokenIdx);

        #region <Working Variables>
        /// <summary>
        /// Settings backing this Application, including configuration and runtime state.
        /// </summary>
        internal ApplicationSettings applicationSettings { get; set; }
        private dynamic persistenceList;
        /// <summary>
        /// The unit of work currently being built/executed by the fluent workflow.
        /// </summary>
        internal UnitOfWork CurrentUOW { get; set; }  = null;
        private UnitOfWork persistentUOW = null;

        // Whether the hidden-desktop message was published; once, at open.
        private bool hiddenDesktopReported = false;

        // whether anything ever arrived on the desktop, checked once after the program is opened
        private bool hiddenDesktopChecked = false;
        #endregion <Working Variables>
        /// <summary>
        /// Return a GPAL.Application object so that GPAL.Application does not have to be cast.
        /// </summary>
        /// <returns>A GPAL.Application object</returns>
        /// <example>
        /// <code>
        /// Application application = GPAL.Application
        ///     .WithParameters(@"C:\temp\ebay.words.txt")
        ///     .Open(@"C:\Program Files\Notepad++\notepad++.exe")
        ///     .WaitFor(5000)
        ///     .Maximize
        ///     .LeftClick()
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        public Application ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Application object that contains the fluent methods to create your Application workflow.<br/>
        /// Instantiated using GPAL.Application
        /// </summary>
        /// <example>
        /// <code>
        ///     Selector gpalTeamSelector = (Selector)GPAL.Selector
        ///         .WithXPath("/Pane/Document/Group[3]/Group/Tree/ListItem[1]/TreeItem[4]/Hyperlink");
        ///     
        ///     GPAL.Application
        ///         .Open(@"C:\Users\CurrentUser\AppData\Local\Microsoft\Teams\current\teams.exe")
        ///         .WithSelector(gpalTeamSelector)
        ///         .LeftClick();
        /// </code>
        /// </example>
        internal Application()
        {
            CurrentUOW = new UnitOfWork();
            persistentUOW = new UnitOfWork();
            persistenceList = new List<UnitOfWork>
            {
                persistentUOW
            };
            applicationSettings = new ApplicationSettings(this);
        }
        #region <Private>
        /// <summary>
        /// Check to see whether the Selectors for the current UOW are finding elements.
        /// Only called if .WaitFor fluent command is used
        /// </summary>
        /// <returns>True if elements found for all Selector(s) or true if no check is requested</returns>
        private bool CheckWaitFor()
        {
            bool matchFound = true;
            List<GPALAutomationElement> foundElements = null;

            // An earlier action on this unit of work already waited for these selectors; skip the wait. A selector
            // added after an action starts a new unit of work.
            if (null != CurrentUOW.WaitForSatisfied)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Selectors for this unit of work already found at [{CurrentUOW.WaitForSatisfied:HH:mm:ss.fff}], skipping redundant wait.", this, GPALObjectType.Application);
                return true;
            }

            if (WaitTime.Never < CurrentUOW.WaitForInMs)
            {
                // we are waiting on selector(s)
                foundElements = ElementHelper.WaitFor(this, CurrentUOW.WaitForInMs, out matchFound);

                if (0 < foundElements?.Count)
                    CurrentUOW.WaitForSatisfied = DateTime.Now;
            }

            return matchFound;
        }
        /// <summary>
        /// Every action calls this first to perform to top the application, perform a maintenance sweep,<br/>
        /// check persistent selectors (and act upon them), check WaitFor status, and set the 'actionCalled' flag in the UOW
        /// </summary>
        /// <param name="checkWaitFor">True to check WaitFor</param>
        /// <returns>Always true - the gated action is always attempted</returns>
        private bool InAction(bool checkWaitFor)
        {
            ApplicationHelper.TopProcess(this.Process); // ensure the browser window is on top, can use selenium or hardware (mouse click) topping. firefox will only top with hardware

            CurrentUOW.ActionCalled = true;

            SelectorListMaintenanceSweep(CurrentUOW); // purge any nodes marked 'deleteme' before iterating list
            SelectorListMaintenanceSweep(persistentUOW); // purge any nodes marked 'deleteme' before iterating list
            CheckPersistentSelectors(); // check if there are persistent selectors to deal with

            // TODO: CAVEAT: WaitFor will call the CallIf handlers, which might result in unexpected behavior?
            if (true == checkWaitFor)
                CheckWaitFor();

            // Always run the action, as the browser side does. WaitFor only pauses; it is not a match test. A missing
            // element is reported by the action instead of being skipped silently.
            return true;
        }
        /// <summary>
        /// Remove any nodes self-marked for deletion before attempting to utilize them (could be marked via a persistent handler)
        /// </summary>
        /// <param name="CurrentUOW">The UOW to sweeo for deletions</param>
        private static void SelectorListMaintenanceSweep(dynamic CurrentUOW)
        {
            for (int idx = 0; idx < CurrentUOW.WithSelectorList.Count; idx++)
                if (true == CurrentUOW.WithSelectorList[idx].DeleteMe)
                    CurrentUOW.WithSelectorList.RemoveAt(idx);

            for (int idx = 0; idx < CurrentUOW.InSelectorList.Count; idx++)
                if (true == CurrentUOW.InSelectorList[idx].DeleteMe)
                    CurrentUOW.InSelectorList.RemoveAt(idx);
        }
        /// <summary>
        /// Check whether any persistent selectors are defined and process them before the current UOW<br/>
        /// This is checked before every action (click, hover, input, etc) is performed on a UOW
        /// </summary>
        private void CheckPersistentSelectors()
        {
            // all we have to do is walk the list, try to find the element and the callif callbacks will do the work
            if (0 < (persistentUOW.WithSelectorList?.Count ?? 0))
                foreach (Selector selector in persistentUOW.WithSelectorList)
                    ElementHelper.FindElements(this, persistentUOW, selector, out bool matchedAll, out List<GPALAutomationElement> elems);
        }
        #endregion <Private>
        #region <Application Actions>
        /// <summary>
        /// Maximize the application to full-screen
        /// </summary>
        public IAllowAllApplicationAndAllSelector Maximize
        {
            get
            {
                if (null != Process)
                    ApplicationHelper.ShowWindow(Process.MainWindowHandle, 3);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min
                return this;
            }
        }
        /// <summary>
        /// Minimize the application to the taskbar
        /// </summary>
        public IAllowAllApplicationAndAllSelector Minimize
        {
            get
            {
                ApplicationHelper.ShowWindow(Process.MainWindowHandle, 2);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min
                return this;
            }
        }
        /// <summary>
        /// Restore the application to 'original' size, unminimize
        /// </summary>
        public IAllowAllApplicationAndAllSelector Restore
        {
            get
            {
                ApplicationHelper.ShowWindow(Process.MainWindowHandle, 1);    // 1= SW_NORMAL - show normal, 3 = max, 2 = min
                return this;
            }
        }
        /// <summary>
        /// Resize the window last matched by WaitForWindow to the specified pixel dimensions.
        /// </summary>
        public IAllowAllApplicationAndAllSelector ResizeWindow(int width, int height)
        {
            if (true == InAction(true))
                ApplicationHelper.ResizeWindow(ApplicationSettings.LastWindowHandle, width, height);
            return this;
        }
        /// <summary>
        /// Move the window last matched by WaitForWindow to the specified screen coordinates.
        /// </summary>
        public IAllowAllApplicationAndAllSelector MoveWindow(int x, int y)
        {
            if (true == InAction(true))
                ApplicationHelper.MoveWindow(ApplicationSettings.LastWindowHandle, x, y);
            return this;
        }
        /// <summary>
        /// Append the data from the database to the end of the input Selectors defined in the UOW<br/>
        /// One token (one column in the current row) per per input.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, inputDatabase.Tokens, WriteMode.Append);
            return this;
        }
        /// <summary>
        /// Append the data from the input file to the end of the input Selectors defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per per input.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, ((IGPALFileInternal)inputFile).TokenList, WriteMode.Append);

            return this;
        }
       /// <summary>
        /// Append the data from the input grid to the end of the input Selectors defined in the UOW<br/>
        /// One token (one string from the current row) per per input.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, inputGrid, WriteMode.Append);

            return this;
        }
        /// <summary>
        /// Append the given text to the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AppendFrom(string textToUse)
        {
            if (true == InAction(true))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Append);
            return this;
        }
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

                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{applicationPath}] runs on [{applicationSettings.HiddenDesktopName}], where UIAutomation drives it and hardware input cannot reach it. Peek at the desktop to watch it or to use hardware there.", this, GPALObjectType.Application);
                }
            }

            // CAVEAT: see if process is open and attach to it? not sure this is what we want
            // maybe we should just launch a new instance?
            try
            {
                processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(applicationPath));
                bool attached = false;

                // WithNewInstance launches even when the program is already running, for when its existing window is
                // not the one the workflow wants.
                if (false == applicationSettings.NewInstance && null != processes && 0 < processes.Length)
                {
                    // An already-running instance should have a window quickly; probe each briefly and
                    // skip windowless/background matches rather than blocking for the full timeout.
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
                        : Process.Start(applicationPath, parameters.ToString());

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

                    /* NOTE: CAVEAT: this is prolly not required - we will know one way or another what is going on
                    if (false == Desktops.HasWindows(applicationSettings.HiddenDesktopName, 10_000))
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Nothing arrived on [{applicationSettings.HiddenDesktopName}], so this program is on the desktop you are working on.", this, GPALObjectType.Application);
                    */
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
        /// <summary>
        /// FillIn data from the database overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);

            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Overwrite);

            return this;
        }
        /// <summary>
        /// FillIn data from the file overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, ((IGPALFileInternal)CurrentUOW.InputFile).TokenList, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// FillIn data from the grid overwriting text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, (IGPALGrid<string>)inputGrid, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// FillIn the text from the string overwriting text in the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector FillInFrom(string textToUse)
        {
            if (true == InAction(true))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Overwrite);
            return this;
        }
        /// <summary>
        /// Focus the current element found via the Selector. Only the first element per Selector found will be focused.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, for text inputs, focus is a click in the control. Otherwise, hardware emulation hovers the mouse over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then focus will interate over every {num} elements that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector Focus(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.Focus(this);
            return this;
        }
        /// <summary>
        /// Hover the mouse over the current element found via the Selector. Only the first element per Selector found will be focused.<br/>
        /// Using UIAutomation, setFocus is first called on the item, then the mouse moved to hover over the element.<br/>
        /// IF using hardwre emulation (.WithHardware) for this selector, for text inputs, focus is a click in the control. Otherwise, hardware emulation hovers the mouse over the control.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then focus will interate over every {num} elements that matches the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector Hover(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.Hover(this, CurrentUOW);
            return this;
        }
        /// <summary>
        /// Insert the text from the string at the beginning of the first input in the UOW. Should probably only be used with one selector at a time.<br/>
        /// Example: If three inputs are defined and three 'InsertFrom' are defined, then the text is assigned to the same ordinal input element found via the Selector(s)
        /// If .WithAllThatMatch is defined on the current UOW, then this text is entered into {num} inputs that matches the corresponding Selector(s) in the current UOW.
        /// </summary>
        /// <param name="textToUse">The input text to use</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(string textToUse)
        {
            if (true == InAction(true))
                ElementHelper.FillInFrom(this, textToUse, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Insert the text from the database at the beginning of the input Selector(s) defined in the UOW<br/>
        /// One token (one column in the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputDatabase">The GPAL Database definitoin</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(GPALDatabase inputDatabase)
        {
            DatabaseHelper.TokenizeDatabase(CurrentUOW, inputDatabase);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, CurrentUOW.InputDatabase.Tokens, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Insert the data from the grid at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string from the current row) per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then this token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputGrid">The input grid of tokens</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(IGPALGrid<string> inputGrid)
        {
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, (IGPALGrid<string>)inputGrid, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Insert the data from the file at the beginning of the text in the input Selector(s) defined in the UOW<br/>
        /// One token (one string [between commas] from the current row) per per input/element found.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then the token is entered into {num} inputs that matches the corresponding Selector in the current UOW.<br/>
        /// If CallAfterFillIn callback is defined, it will be called after each row is consumed and the text is input.
        /// </summary>
        /// <param name="inputFile">The GPAL File definition</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector InsertFrom(GPALFile inputFile)
        {
            ((IGPALFileInternal)inputFile).TokenList = FileHelper.TokenizeFile(CurrentUOW, inputFile);
            if (true == InAction(true))
                ApplicationHelper.FillInWithTokens(this, ((IGPALFileInternal)CurrentUOW.InputFile).TokenList, WriteMode.Insert);
            return this;
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW<br/>
        /// One left click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a left click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.LeftClick(this);
            return this;
        }
        /// <summary>
        /// Left click the element found for Selector(s) defined in the UOW, optionally emulating pressing a modifier key at the same time.
        /// </summary>
        /// <param name="modifierKeys">Modifier key to hold during the click</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClick(Enums.ModifierKeys modifierKeys)
        {
            if (true == InAction(true))
                ElementHelper.LeftClick(this, modifierKeys);
            return this;
        }
        /// <summary>
        /// Left double click the element found for Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftDoubleClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.GenericClick(this, ClickType.LeftDoubleClick);
            return this;
        }
        /// <summary>
        /// Middle click the element found for Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector MiddleClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.GenericClick(this, ClickType.MiddleClick);
            return this;
        }
        /// <summary>
        /// Move the mouse cursor to the element found for Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector MoveTo(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.MoveTo(this);
            return this;
        }
        /// <summary>
        /// Clicks the element that raises an Open dialog, waits for the dialog, and answers it with the given
        /// file or folder.
        /// </summary>
        /// <remarks>
        /// Needs no keystrokes or focus, so it works on a hidden desktop. Also answers Save As and the folder picker. A
        /// GPALFile naming several files answers a multi-select dialog.
        /// </remarks>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(attachButton)
        ///         .LeftClickAndUpload(@"C:\invoices\march.pdf");
        /// </code>
        /// </example>
        /// <param name="file">The file or folder to answer the dialog with</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClickAndOpen(GPALFile file)
        {
            return ClickAndAnswer(file);
        }

        /// <summary>
        /// Clicks the element that raises a Save dialog, waits for the dialog, and answers it with the given file. An
        /// existing file of that name is deleted first, so no replace prompt appears.
        /// </summary>
        /// <example>
        /// <code>
        ///     myApplication
        ///         .WithSelector(saveAsMenuItem)
        ///         .LeftClickAndSave(@"C:\reports\march.txt");
        /// </code>
        /// </example>
        /// <param name="file">The file to save as</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector LeftClickAndSave(GPALFile file)
        {
            ClearTheWay(file);

            return ClickAndAnswer(file);
        }

        /// <summary>
        /// Deletes the file a Save is about to write, so the dialog never asks whether to replace it.
        /// </summary>
        /// <param name="file">The file being saved</param>
        private void ClearTheWay(GPALFile file)
        {
            foreach (string name in file?.Filenames ?? new List<string>())
                if (true == System.IO.File.Exists(name))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Deleting [{name}] before saving over it, so the dialog has nothing to ask about", this, GPALObjectType.Application);

                    System.IO.File.Delete(name);
                }
        }

        /// <summary>
        /// Clicks whatever the unit of work found and answers the dialog it raises.
        /// </summary>
        /// <param name="file">The file or folder to answer with</param>
        /// <returns>Fluent interface to write your workflow</returns>
        private IAllowSelectorActionOrAnySelector ClickAndAnswer(GPALFile file)
        {
            if (true == InAction(true))
            {
                ElementHelper.LeftClick(this, ModifierKeys.NONE);

                FileDialogHelper.Answer(this, file, ApplicationSettings.WaitForWindowTimeout);
            }

            return this;
        }

        /// <summary>
        /// Answers a file dialog that is already on screen with the given file or folder.
        /// </summary>
        /// <param name="file">The file or folder to answer the dialog with</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector AnswerFileDialog(GPALFile file)
        {
            if (true == InAction(true))
                FileDialogHelper.Answer(this, file, ApplicationSettings.WaitForWindowTimeout);

            return this;
        }

        /// <summary>
        /// Performs a drag from the source element to the target element, both found via Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="selector">Optional selector to use; if provided, becomes the active selector for this UOW</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector DragAndDrop(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.DragAndDrop(this);
            return this;
        }
        /// <summary>
        /// Performs a drag from the source element to the target element, both found via Selector(s) defined in the UOW,
        /// while holding the given modifier key.
        /// </summary>
        /// <param name="modifierKeys">Modifier key to hold during the drag and drop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector DragAndDrop(ModifierKeys modifierKeys)
        {
            if (true == InAction(true))
                ElementHelper.DragAndDrop(this, modifierKeys);
            return this;
        }

        /// <summary>
        /// Press and hold a modifier key (Alt, Control, Shift) using hardware emulation.
        /// </summary>
        /// <param name="modifierKeys">Alt, Control, Shift</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector PressModifierKey(ModifierKeys modifierKeys)
        {
            HardwareHelper.PressModifierKey(modifierKeys);
            return this;
        }

        /// <summary>
        /// Release a previously pressed modifier key (Alt, Control, Shift) using hardware emulation.
        /// </summary>
        /// <param name="modifierKeys">Alt, Control, Shift</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector ReleaseModifierKey(ModifierKeys modifierKeys)
        {
            HardwareHelper.ReleaseModifierKeys(modifierKeys);
            return this;
        }

        /// <summary>
        /// Type special characters like Enter, Page Up, Page Down, Tab, etc.<br/>
        /// Use GPAL.VK constants for ease of access to commmon keys.<br/>
        /// For all Virtual Keycodes, visit:<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        /// </summary>
        /// <example>
        /// <code>
        /// // send CTRL-HOME
        ///     ...
        ///     myBrowser
        ///         .PressModifierKey(ModifierKeys.Control)
        ///         .SendKey(GPAL.VK_HOME);
        ///         .WaitFor(150)
        ///         .ReleaseModifierKey(ModifierKeys.Control)
        /// </code>
        /// </example>
        /// <param name="VKCode">Virtual Keycode</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector SendKey(byte VKCode)
        {
            if (true == InAction(true))
            {
                HardwareHelper.SendKey(VKCode);

                // SendKey only posts the keystroke. Wait for the application to process it so a chain of sends arrives
                // in order.
                ElementHelper.SettleInput(this);
            }

            return this;
        }
        /// <summary>
        /// Send the string using hardware emulation.
        /// </summary>
        /// <param name="textToSend">The text to send</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector SendString(string textToSend)
        {
            HardwareHelper.SendString(textToSend, GPAL.TypingDelay);

            ElementHelper.SettleInput(this);

            return this;
        }
        /// <summary>
        /// Switch to any TabControl defined in the current UOW. This will hardware click the tab control if the SelectionItemPattern fails to allow a click
        /// If .WithAllThatMatch is defined on the current UOW, GPAL will attempt to switch to {num}  tabs that matched the Selector(s) in the current UOW.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllActionAndAnySelector SwitchToTab(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.SwitchToTab(this);
            return this;
        }
        /// <summary>
        /// Set the value of a range-based control (e.g. a slider) found via Selector(s) defined in the UOW.
        /// </summary>
        /// <param name="rangeValue">The value to set the range control to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAllActionAndAnySelector SetRange(double rangeValue)
        {
            if (true == InAction(true))
                ElementHelper.SetRange(this, rangeValue);
            return this;
        }
        /// <summary>
        /// Right click the element found for Selector(s) defined in the UOW<br/>
        /// One right click for the first element found for each selector. Should probably be used with only one Selector in the UOW.<br/>
        /// If .WithAllThatMatch is defined on the current UOW, then a right click is performed on {num} elements that matches the corresponding Selector in the current UOW.<br/>
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowSelectorActionOrAnySelector RightClick(Selector selector = null)
        {
            if (null != selector) WithSelector(selector);
            if (true == InAction(true))
                ElementHelper.GenericClick(this, ClickType.RightClick);
            return this;
        }
        /// <summary>
        /// Either wait the amount of time specified or wait UP TO the amount of time specified waiting for elements to be found for the Selector(s) in the current UOW.<br/>
        /// If an action has already been called, then this is interpretted as a sleep for the specified miliseconds.<br/>
        /// If only selectors have been defined in the current UOW, then this is a delay waiting for the selectors to be present before continuing with the workflow.<br/>
        /// Waiting for elements does not guarantee they will be present when the workflow continues. If they are not present, the CallIfNotFound handler will be invoked so you can deal with that.<br/>
        /// <b>NOTE:</b> WaitFor is not considered an action, it defines a time to wait for elements to be present before trying to perform actions.
        /// </summary>
        /// <param name="timeoutInTicks"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitFor([Milliseconds] int timeoutInTicks)
        {
            // we are done waiting on any selectors since we performed an action
            // so, we just wait/sleep
            if (true == CurrentUOW.ActionCalled)
                Thread.Sleep(timeoutInTicks);
            else
                CurrentUOW.WaitForInMs = timeoutInTicks;

            return this;
        }
        /// <summary>
        /// Wait for a Windows window with title to appear on the desktop.<br/>
        /// Used in conjunction with .WithWaitForWindowTimeout to set the wait duration in seconds.<br/>
        /// Default is 10 seconds.
        /// </summary>
        /// <param name="windowTitle"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitForWindow(string windowTitle)
        {
            if (true == InAction(true))
                SettleOnWindow(true == ApplicationSettings.HiddenDesktop
                    ? Desktops.WaitForWindowOn(ApplicationSettings.HiddenDesktopName, title => windowTitle.Equals(title), ApplicationSettings.WaitForWindowTimeout)
                    : FormHelper.WaitForWindow(windowTitle, ApplicationSettings.WaitForWindowTimeout));
            return this;
        }
        /// <summary>
        /// Wait for a Windows window with title that matches the regex pattern to appear on the desktop.<br/>
        /// Used in conjunction with .WithWaitForWindowTimeout to set the wait duration in seconds.<br/>
        /// Default is 10 seconds.
        /// </summary>
        /// <param name="windowTitleRegex">Regular expression the window title must match</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterWaitForAndSizeControl WaitForWindowRegex([Regex] string windowTitleRegex)
        {
            if (true == InAction(true))
                SettleOnWindow(true == ApplicationSettings.HiddenDesktop
                    ? Desktops.WaitForWindowOn(ApplicationSettings.HiddenDesktopName, title => 0 < System.Text.RegularExpressions.Regex.Matches(title, windowTitleRegex).Count, ApplicationSettings.WaitForWindowTimeout)
                    : FormHelper.WaitForWindowRegex(windowTitleRegex, ApplicationSettings.WaitForWindowTimeout));
            return this;
        }

        /// <summary>
        /// Makes the window found by the wait the one the following selectors search, instead of the window Open found.<br/>
        /// This is how a launcher's window is reached: explorer.exe passes its folder to the running shell and exits,
        /// so the window belongs to a process Open never held.
        /// </summary>
        /// <param name="windowHandle">The window found; zero when the wait timed out</param>
        private void SettleOnWindow(IntPtr windowHandle)
        {
            ApplicationSettings.LastWindowHandle = windowHandle;

            if (IntPtr.Zero != windowHandle)
            {
                AutomationElement window = AutomationElement.FromHandle(windowHandle);

                if (null != window)
                    ApplicationSettings.RootAutomationElement = window;
            }
        }
        /// <summary>
        /// Scroll the element horizontally in a small increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll left.<br/>Positive scroll right.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollHorizontalSmall(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)
                    ElementHelper.Scroll(this, ScrollAmount.SmallIncrement, ScrollTypes.Increments, ScrollTypes.Horizontal);

                for (; 0 > iterations; iterations++)
                    ElementHelper.Scroll(this, ScrollAmount.SmallDecrement, ScrollTypes.Increments, ScrollTypes.Horizontal);
            }

            return this;
        }
        /// <summary>
        /// Scroll the element horizontally in a large increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll left.<br/>Positive scroll right.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollHorizontalLarge(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)
                    ElementHelper.Scroll(this, ScrollAmount.LargeIncrement, ScrollTypes.Increments, ScrollTypes.Horizontal);

                for (; 0 > iterations; iterations++)
                    ElementHelper.Scroll(this, ScrollAmount.LargeDecrement, ScrollTypes.Increments, ScrollTypes.Horizontal);
            }

            return this;
        }
        /// <summary>
        /// Element's horizontal scroll position as a percentage of the total content area within the AutomationElement.<br/>
        /// </summary>
        /// <param name="percent">Percentage of the total content area [0-100]</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollHorizontalPercent([Percent] int percent)
        {
            if (true == InAction(true))
                ElementHelper.Scroll(this, InRange(percent), ScrollTypes.Percent, ScrollTypes.Horizontal);

            return this;
        }

        /// <summary>
        /// Clamps a scroll percentage to 0-100, the range UIAutomation measures against the control's current extent.
        /// An out-of-range value is set to the nearest end with a warning.
        /// </summary>
        /// <param name="percent">The requested percentage</param>
        /// <returns>The same value, or the nearest end of the range</returns>
        private int InRange(int percent)
        {
            int retVal = 0 > percent ? 0 : (100 < percent ? 100 : percent);

            if (retVal != percent)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"A scroll percentage is 0 to 100, so [{percent}] was taken as [{retVal}]", this, GPALObjectType.Application);

            return retVal;
        }
        /// <summary>
        /// Scroll the element vertically in a small increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll up.<br/>Positive scroll down.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollVerticalSmall(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)    // + = down
                    ElementHelper.Scroll(this, ScrollAmount.SmallIncrement, ScrollTypes.Increments, ScrollTypes.Vertical);

                for (; 0 > iterations; iterations++)    // - = up
                    ElementHelper.Scroll(this, ScrollAmount.SmallDecrement, ScrollTypes.Increments, ScrollTypes.Vertical);
            }

            return this;
        }
        /// <summary>
        /// Scroll the element vertically in a large increment.<br/>
        /// https://docs.microsoft.com/en-us/windows/win32/api/uiautomationcore/ne-uiautomationcore-scrollamount
        /// </summary>
        /// <param name="iterations">Negative iterations scroll up.<br/>Positive scroll down.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollVerticalLarge(int iterations = 1)
        {
            if (true == InAction(true))
            {
                for (; 0 < iterations; iterations--)
                    ElementHelper.Scroll(this, ScrollAmount.LargeIncrement, ScrollTypes.Increments, ScrollTypes.Vertical);

                for (; 0 > iterations; iterations++)
                    ElementHelper.Scroll(this, ScrollAmount.LargeDecrement, ScrollTypes.Increments, ScrollTypes.Vertical);
            }

            return this;
        }
        /// <summary>
        /// Element's vertical scroll position as a percentage of the total content area within the AutomationElement.<br/>
        /// </summary>
        /// <param name="percent">Percentage of the total content area [0-100]</param>
        /// <returns>Fluent interface to write your workflow</returns>
        /// <remarks>
        /// Ends the unit of work. Read what you need before scrolling and name the selectors again after.
        /// </remarks>
        public IAllowSelectorActionOrAnySelector ScrollVerticalPercent([Percent] int percent)
        {
            if (true == InAction(true))
                ElementHelper.Scroll(this, InRange(percent), ScrollTypes.Percent, ScrollTypes.Vertical);

            return this;
        }
        #endregion <Application Actions>
        #region <Helpers>
        /// <summary>
        /// Helper method for GPAL applications to remove a handler from all UOWs. Envisioned to be used in a persistent handler, but can be used anyway imagined. This is not fluent.
        /// </summary>
        /// <param name="func">The CallIf handler to remove</param>
        public void RemoveCallIfHandlerEverywhere(Application.CallIfDelegate func)
        {
            CurrentUOW.AppCallIfFound.Remove(func);
            CurrentUOW.AppCallIfNotFound.Remove(func);

            // TODO: do the same for inselectorlist
            foreach (Selector selector in CurrentUOW.WithSelectorList)
            {
                selector.SelectorSettings.AppCallIfFound.Remove(func);
                selector.SelectorSettings.AppCallIfNotFound.Remove(func);
            }

            foreach (Selector selector in CurrentUOW.InSelectorList)
            {
                selector.SelectorSettings.AppCallIfFound.Remove(func);
                selector.SelectorSettings.AppCallIfNotFound.Remove(func);
            }

            persistentUOW.AppCallIfFound.Remove(func);
            persistentUOW.AppCallIfNotFound.Remove(func);

            foreach (Selector selector in persistentUOW.WithSelectorList)
            {
                selector.SelectorSettings.AppCallIfFound.Remove(func);
                selector.SelectorSettings.AppCallIfNotFound.Remove(func);
            }
        }
        #endregion <Helpers>
        #region <Application Settings>
        /// <summary>
        /// Sets the application's name, used when publishing events.
        /// </summary>
        /// <param name="appName">Name to associate with this Application</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowApplicationSettingsOrOpen WithName(string appName)
        {
            Name = appName;
            return this;
        }
        /// <summary>
        /// Add a persistent CallIfFound handler to be called if any defined persistent selectors find/match elements.<br/>
        /// Persistent selectors are always looked for before every action cross-cutting your workflow.<br/>
        /// Persistent handlers can remove themselves in the CallIfHandler by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="persistentCallIfFound"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowPersistentCallBack PersistentCallIfFound(CallIfDelegate persistentCallIfFound)
        {
            persistentUOW.AppCallIfFound.Add(persistentCallIfFound);

            return this;
        }
        /// <summary>
        /// Add a persistent CallIfNotFound handler to be called if any defined persistent selectors DO NOT find/match elements.<br/>
        /// Persistent selectors are always looked for before every action cross-cutting your workflow.
        /// Persistent handlers can remove themselves in the CallIfHandler by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="persistentCallIfNotFound"></param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowPersistentCallBack PersistentCallIfNotFound(CallIfDelegate persistentCallIfNotFound)
        {
            persistentUOW.AppCallIfNotFound.Add(persistentCallIfNotFound);

            return this;
        }
        /// <summary>
        /// Add a persistent selector to the persistent UOW. Persistent selectors are always looked for whenever you perform an action on a UOW, like LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the PersistentCallIf handlers to create workflows to deal with the persistent selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// Only one persistent Unit of Work exists. All selectors are evaluated in the order they are defined.<br/>
        /// Example: For applications, a persistent selector could be used with a timer in the callback, when elapsed time occurs, invoke an action on that Selector (like Save work).
        /// </summary>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowPersistence WithPersistentSelector(Selector selector)
        {
            // when would we ever add another persistent uow?
            // normal uow is once action is called, any withselector is on the next uow, but persistents don't act like this
            if (true == CurrentUOW.ActionCalled)
            {
                persistentUOW = new UnitOfWork();
                persistenceList.Add(persistentUOW);
            }

            selector.Application = this;
            //persistentUOW.SelectorType = SelectorType.WithSelector;
            persistentUOW.WithSelectorList.Add(selector);
            return this;
        }
        /// <summary>
        /// Add a selector to the current UOW. Selectors make up a Unit of Work (UOW) to perform actions upon.<br/>
        /// A UOW is: All selectors defined between actions like Open, LeftClick, Hover, Focus, etc.<br/>
        /// Use in conjunction with the CallIf handlers to create workflows to deal with found/not found selector(s).<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// </summary>
        /// <param name="selector">The GPAL Selector for the persistent element</param>
        /// <returns>A fluent interface to define more persistent selectors</returns>
        public IAllowSelectorActionOrAnySelector WithSelector(Selector selector)
        {
            selector.ElementsFoundAndMatchedCount = 0;  // in case it is reused
            selector.DeleteMe = false;                  // Remove() latches this on and nothing else clears it, so a reused selector would be swept on its next unit of work
            selector.RemovedMethods.Clear();            // a handler that took itself out of play in an earlier workflow is back in play for this one

            if (true == CurrentUOW.ActionCalled)
            {
                CurrentUOW = new UnitOfWork();
            }

            // clone so if this selector is reused, each UOW list will be unique and we won't change the selector by changing the browser it is associated with
            // NOTE: cloning disassociates with actual program selector, so we can't set any queryable properties on Selector like 'ElementsFoundAndMatchedCount'
            // ApplicationSettings.MySelector = new Selector(this, selector);    // MySelector temp hold for other browser syntax
            ApplicationSettings.MySelector = selector;

            CurrentUOW.WithSelectorList.Add(ApplicationSettings.MySelector);

            return this;
        }
        // TODO: How to deal with tokens. To consume a row for each row, but to also repeat the same token down the page?
        /// <summary>
        /// Indicates the Selectors refer to/match repeating, multiple elements on the page.<br/>
        /// Use after all your <b>.WithSelector</b> statements. Used two ways.<br/>
        /// For retrieving data: This is used to retrieve multiple rows of elements from the webpage, up to rowCount.<br/>
        /// For input tokens/strings: This will repeat the same input into multiple elements, up to rowCount of lines.<br/>
        /// For tokens, if columns repeat down the page, column 1 will be filled in with token 1, column 2 with token 2, etc.<br/>
        /// This will be repeated for each row.<br/>
        /// For extracting data from selectors using GetGrid/SaveTo[CSV/Excel/Tabbed] up to rowCount rows of elements matching the Selector will be retrieved for saving.<br/>
        /// Again, rowCount column 1 going down a page will be retrieved, column 2, etc. that is then pivoted to an output of rows/columns
        /// </summary>
        /// <param name="rowCount">The number of rows of matching elements to work with.<br/>int.MaxValue [default] work with all rows.</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelectorExceptWithAll WithAllThatMatch(int rowCount = int.MaxValue)
        {
            CurrentUOW.WithAllThatMatch = rowCount;
            return this;
        }

        #region <GetGrid / SaveTo / Pagination>
        // ─────────────────────────────────────────────────────────────────────────────────────
        // Data extraction out of a desktop application, mirroring the Browser GetGrid API.
        // A single selector that resolves to a UIA grid/table control is read structurally via
        // GridPattern/TablePattern; otherwise each selector is a column and repeating matches are
        // the rows (the WithAllThatMatch model). Missing cells use GPAL.ErrorPlaceholder. The
        // result lands in CurrentUOW.RetGrid so SaveTo*/pagination reuse the shared, engine-neutral
        // helpers (FileHelper.SaveToDelimited, GPAL.Converter) exactly as the Browser side does.
        // ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Retrieve the current selectors' data (or a table/grid control's data) into a GPALGrid. The grid is
        /// passed by ref: pass an existing grid and GetGrid adds its rows into it (reuse one grid to accumulate
        /// across calls, it never replaces your reference); pass null to get a fresh grid back.<br/>
        /// GPAL also keeps the grid internally for any following SaveTo*/AppendTo* call.
        /// </summary>
        public IAllowWithHeaderOrFileActions GetGrid(ref IGPALGrid<string> returnGrid)
        {
            // Reading the grid is an action, as on the browser side, so the next WithSelector starts a new unit of
            // work. GetGridCalled tells WithHeader that headers set after it belong to this grid.
            CurrentUOW.ActionCalled = true;
            CurrentUOW.GetGridCalled = true;
            CurrentUOW.RetGrid.Clear();

            if (!TryGetGridFromControl())
                GetGridFromSelectors();

            // Do not take ownership of a grid the caller passed in:
            //  - null in -> hand them GPAL's internal grid.
            //  - grid in -> add our rows into THEIR grid and keep their reference so repeated GetGrid calls accumulate.
            if (null == returnGrid)
                returnGrid = (IGPALGrid<string>)CurrentUOW.RetGrid;
            else
                returnGrid.Add((IGPALGrid<string>)CurrentUOW.RetGrid);

            return this;
        }

        // Table-native: a single selector that resolves to a control exposing the UIA GridPattern
        // is read cell-by-cell via GetItem(row, col); TablePattern column headers become the header
        // row. Returns false when this is not a grid control so the selector-column path runs.
        private bool TryGetGridFromControl()
        {
            if (1 != CurrentUOW.WithSelectorList.Count
                || SelectorType.Selector != CurrentUOW.WithSelectorList[0].selectorSettings.SelectorType)
                return false;

            Selector sel = CurrentUOW.WithSelectorList[0];
            bool matchedAll; List<GPALAutomationElement> matched;
            var found = ElementHelper.FindElements(this, CurrentUOW, sel, out matchedAll, out matched);
            AutomationElement ae = (null != found && found.Count > 0) ? found[0].Ae : null;
            object gpObj;
            if (null == ae || !ae.TryGetCurrentPattern(GridPattern.Pattern, out gpObj))
                return false;

            GridPattern grid = (GridPattern)gpObj;
            int rows = grid.Current.RowCount;
            int cols = grid.Current.ColumnCount;

            List<string> headers = null;
            object tpObj;
            if (ae.TryGetCurrentPattern(TablePattern.Pattern, out tpObj))
            {
                AutomationElement[] colHeaders = ((TablePattern)tpObj).Current.GetColumnHeaders();
                if (null != colHeaders && colHeaders.Length > 0)
                {
                    headers = new List<string>();

                    // Read the header's Name first, then its text. Explorer's column headers are SplitButtons whose
                    // ValuePattern is empty.
                    foreach (AutomationElement h in colHeaders)
                        headers.Add(true == string.IsNullOrWhiteSpace(h.Current.Name) ? (h.GetText() ?? string.Empty) : h.Current.Name);
                }
            }

            // A list view without TablePattern still has a Header element with one HeaderItem per column; use those as
            // the column names.
            if (null == headers)
                headers = ColumnHeadersUnder(ae);

            int cap = (int.MaxValue == CurrentUOW.WithAllThatMatch)
                        ? int.MaxValue : CurrentUOW.WithAllThatMatch;

            for (int r = 0; r < rows && CurrentUOW.RetGrid.Count() < cap; r++)
            {
                var row = new List<string>(cols);
                for (int c = 0; c < cols; c++)
                {
                    string text;
                    try
                    {
                        AutomationElement cell = grid.GetItem(r, c);
                        text = (null != cell) ? (cell.GetText() ?? string.Empty) : GPAL.ErrorPlaceholder;
                    }
                    catch
                    {
                        text = GPAL.ErrorPlaceholder;
                    }
                    row.Add(text);
                }
                CurrentUOW.RetGrid.AddRow(row);
            }

            if (null != headers && headers.Count > 0)
            {
                CurrentUOW.ControlHeaderList = headers;

                if (null == CurrentUOW.HeaderList || 0 == CurrentUOW.HeaderList.Count)
                    CurrentUOW.HeaderList = headers;
            }

            // The control's column names, so a workflow can read them after GetGrid without opening the saved file.
            ColumnNames = null == headers ? new List<string>() : new List<string>(headers);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: [{sel.Name}] resolved to a UIA grid ([{rows}] x [{cols}]).", this, GPALObjectType.Application);
            return true;
        }

        /// <summary>
        /// Returns the column names from a control's Header element, one HeaderItem per column, for a control without
        /// TablePattern (usually a list view).
        /// </summary>
        /// <param name="element">The grid control to look under</param>
        /// <returns>The names in column order; null when the control shows none</returns>
        private static List<string> ColumnHeadersUnder(AutomationElement element)
        {
            List<string> retVal = null;

            AutomationElement header = element.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Header));

            if (null != header)
            {
                // Read every child of the header, not only HeaderItems: Explorer's columns are SplitButtons, and a list
                // view can use any control type.
                AutomationElementCollection items = header.FindAll(TreeScope.Children, Condition.TrueCondition);

                if (0 < items.Count)
                {
                    retVal = new List<string>();

                    foreach (AutomationElement item in items)
                        retVal.Add(true == string.IsNullOrWhiteSpace(item.Current.Name) ? (item.GetText() ?? string.Empty) : item.Current.Name);
                }
            }

            return retVal;
        }

        // Column-per-selector extraction: each selector is a column, repeating matches are the rows.
        // Mirrors the Browser column-zip (pad short columns, dedup rows, honor WithAllThatMatch and
        // multi-page paging) but reads cells with the UIA .Ae.GetText() the app side already uses.
        private void GetGridFromSelectors()
        {
            var names = new List<string>();
            foreach (Selector s in CurrentUOW.WithSelectorList)
                names.Add(s.Name);

            var seenRows = new HashSet<string>();
            bool capReached = false;
            int cap = (int.MaxValue == CurrentUOW.WithAllThatMatch)
                        ? int.MaxValue : CurrentUOW.WithAllThatMatch;

            for (int page = 0; page < CurrentUOW.PageCount && !capReached; page++)
            {
                var columns = new List<List<string>>();
                int maxRows = 0;

                foreach (Selector sel in CurrentUOW.WithSelectorList)
                {
                    var col = new List<string>();
                    if (SelectorType.Selector == sel.selectorSettings.SelectorType)
                    {
                        bool matchedAll; List<GPALAutomationElement> matched;
                        var found = ElementHelper.FindElements(this, CurrentUOW, sel, out matchedAll, out matched);
                        List<GPALAutomationElement> elems =
                            (!matchedAll && null != matched && matched.Count > 0)
                                ? matched
                                : (null != found ? found.ToList() : new List<GPALAutomationElement>());
                        foreach (GPALAutomationElement e in elems)
                            col.Add((null != e.Ae) ? (e.Ae.GetText() ?? string.Empty) : GPAL.ErrorPlaceholder);
                    }
                    else if (SelectorType.Data == sel.selectorSettings.SelectorType)
                        col.Add(sel.SelectorPath);
                    else if (SelectorType.DataFunc == sel.selectorSettings.SelectorType)
                        col.Add(sel.selectorSettings.DataFunction());

                    columns.Add(col);
                    if (col.Count > maxRows) maxRows = col.Count;
                }

                // pad short columns so every row has a cell per column
                for (int i = 0; i < columns.Count; i++)
                    while (columns[i].Count < maxRows)
                        columns[i].Add($"{GPAL.ErrorPlaceholder} : {names[i]}");

                for (int r = 0; r < maxRows && !capReached; r++)
                {
                    var row = new List<string>(columns.Count);
                    for (int c = 0; c < columns.Count; c++)
                        row.Add(columns[c][r]);

                    string key = string.Join("", row);
                    if (!seenRows.Add(key))
                        continue;   // duplicate row from a prior page/scroll pass
                    CurrentUOW.RetGrid.AddRow(row);
                    if (CurrentUOW.RetGrid.Count() >= cap)
                        capReached = true;
                }

                if (!capReached && page < CurrentUOW.PageCount - 1)
                    if (!ApplicationHelper.GotoNextPage(this))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"GetGrid: no next page after page [{page + 1}].", this, GPALObjectType.Application);
                        break;
                    }
            }

            if (null == CurrentUOW.HeaderList || 0 == CurrentUOW.HeaderList.Count)
                CurrentUOW.HeaderList = names;
        }

        /// <summary>Column header for exported data. Overrides selector names / table headers, in order.</summary>
        public IAllowWithHeaderOrFileActions WithHeader(string header)
        {
            if (true == CurrentUOW.GetGridCalled)
            {
                CurrentUOW.GetGridCalled = false;
                CurrentUOW.HeaderList.Clear();
            }
            CurrentUOW.HeaderList.Add(header);
            return this;
        }

        /// <summary>Sets a GPALGrid to be saved by a following SaveTo*.</summary>
        /// <remarks>
        /// Starts a new unit of work when the current one has already acted, as a selector does.
        /// </remarks>
        public IAllowWithHeaderOrFileActions WithGridToSave(IGPALGrid<string> gridToSave)
        {
            if (true == CurrentUOW.ActionCalled)
                CurrentUOW = new UnitOfWork();

            CurrentUOW.RetGrid = gridToSave;

            return this;
        }

        /// <summary>The control to click to load the next page when paging through results.</summary>
        public IAllowWithPagesAndGridActions WithNextPageButton(Selector selector)
        {
            CurrentUOW.NextPageButton = selector;
            return this;
        }

        /// <summary>Scroll to load more rows between pages. Not supported for desktop apps (single page).</summary>
        public IAllowWithPagesAndGridActions WithInfiniteScroll
        {
            get
            {
                CurrentUOW.InfiniteScroll = true;
                return this;
            }
        }

        /// <summary>Number of result pages to retrieve. Use with WithNextPageButton.</summary>
        public IAllowGetGridAndFillInFrom WithPages(int numberOfPages)
        {
            CurrentUOW.PageCount = numberOfPages;
            return this;
        }

        // Resolve the output column names for a grid save, handed to the Converter as column metadata
        // (WithColumnNames), NOT injected as a data row. Precedence, highest first, with a short higher list
        // topped up from the next level down:
        //   1. CurrentUOW.HeaderList  - the workflow's .WithHeader columns (override)
        //   2. the output GPALFile's ColumnList - the file's own columns (wildcard/.Next replication of the
        //      first entry happens at expansion time, so entry 0 is representative here)
        //   3. selector names - the ultimate global, always defined
        //   4. the control's own column names, e.g. a UIA TablePattern's headers
        //   5. generated column1..columnN - so the list is never narrower than the widest row, because a
        //      column with no name must never mean a column with no data
        // The result is then made unique: the writer keys cells by column name, so a repeated name would
        // collapse those columns into one holding the last value.
        // Whether the names are written is governed by the output file's FirstLineIsColumnNames.
        private List<string> ResolveHeaderColumns(GPALFile file)
        {
            List<string> selectorNames = CurrentUOW.WithSelectorList?.Where(s => null != s.Name).Select(s => s.Name).ToList()
                ?? new List<string>();

            List<string> fileColumns = new List<string>();
            IGPALGrid<string> columnList = ((IGPALFileInternal)file).FileSettings.ColumnList;
            if (null != columnList && 0 < columnList.Count() && null != columnList[0])
                fileColumns = new List<string>(columnList[0]);

            List<string> resolved = (null != CurrentUOW.HeaderList && 0 < CurrentUOW.HeaderList.Count)
                ? new List<string>(CurrentUOW.HeaderList)
                : new List<string>();
            for (int i = resolved.Count; i < fileColumns.Count; i++)
                resolved.Add(fileColumns[i]);
            for (int i = resolved.Count; i < selectorNames.Count; i++)
                resolved.Add(selectorNames[i]);

            // Prefer the control's own column names over generated ones. WithHeader clears HeaderList, so the control's
            // names are kept separately.
            List<string> controlHeaders = CurrentUOW.ControlHeaderList ?? new List<string>();

            for (int i = resolved.Count; i < controlHeaders.Count; i++)
                resolved.Add(controlHeaders[i]);

            // The converter only writes named columns. Generate names for extra columns instead of trimming the data.
            int widest = 0;

            if (null != CurrentUOW.RetGrid)
                foreach (List<string> row in CurrentUOW.RetGrid)
                    if (null != row && widest < row.Count)
                        widest = row.Count;

            for (int i = resolved.Count; i < widest; i++)
                resolved.Add($"column{i + 1}");

            return UniqueColumns(resolved);
        }

        /// <summary>
        /// Makes every column name non-blank and unique; the writer keys cells by name, so duplicates collapse into one
        /// column.
        /// </summary>
        /// <remarks>
        /// A blank name becomes columnN and a repeat gets a number, so Size, Size becomes Size, Size2.
        /// </remarks>
        /// <param name="columns">The resolved names, which may include blanks and repeats</param>
        /// <returns>The same names, each non-blank and unique</returns>
        private static List<string> UniqueColumns(List<string> columns)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            for (int i = 0; i < columns.Count; i++)
            {
                string name = true == string.IsNullOrWhiteSpace(columns[i]) ? $"column{i + 1}" : columns[i].Trim();
                string candidate = name;
                int repeat = 1;

                while (false == used.Add(candidate))
                {
                    repeat++;
                    candidate = $"{name}{repeat}";
                }

                result.Add(candidate);
            }

            return result;
        }

        /// <summary>
        /// Save the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension, e.g. .csv/.tsv/.txt/.xlsx/.json, or its explicit file settings).
        /// </summary>
        public IAllowAllActionAndAnySelector SaveTo(GPALFile file)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of window data to [{file?.Filename}].", this, GPALObjectType.Application);

            var converter = GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file));
            // Naming headers via WithHeader is clear intent to write them; declare it so the output emits a
            // header row unless the output file explicitly turns it off (WithFirstLineIsColumnNames(false)).
            if (null != CurrentUOW.HeaderList && 0 < CurrentUOW.HeaderList.Count)
                converter.WithFirstLineHasColumnNames(true);
            converter.SaveTo(file);
            return this;
        }

        /// <summary>
        /// Append the retrieved grid. The output format and delimiter are determined by the GPALFile
        /// (its extension or explicit file settings). Headers are not repeated on append.
        /// </summary>
        public IAllowAllActionAndAnySelector AppendTo(GPALFile file)
        {
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Appending [{CurrentUOW.RetGrid?.Rows ?? 0}] rows of window data to [{file?.Filename}].", this, GPALObjectType.Application);

            GPAL.Converter.WithInput(CurrentUOW.RetGrid).WithColumnNames(ResolveHeaderColumns(file)).AppendTo(file);
            return this;
        }
        #endregion <GetGrid / SaveTo / Pagination>
        /// <summary>
        /// Define the time in seconds to WaitForWindow/Regex
        /// </summary>
        /// <param name="waitTimeInSeconds"></param>
        /// <returns></returns>
        public IAllowAfterWaitForAndSizeControl WithWaitForWindowTimeout([Seconds] int waitTimeInSeconds)
        {
            ApplicationSettings.WaitForWindowTimeout = waitTimeInSeconds;
            return this;
        }
        /// <summary>
        /// Define the parameters to use in the Open command
        /// </summary>
        /// <param name="parameter">One parameter to add to the command line the program is opened with</param>
        /// <returns></returns>
        public IAllowApplicationSettingsOrOpen WithParameter(string parameter)
        {
            ApplicationSettings.Parameters.Add(parameter);
            return this;
        }
        /// <summary>
        /// Force all workflow element interactions to use hardware emulation (UIAutomation is default)
        /// </summary>
        /// <param name="trueFalse"></param>
        /// <returns>Fluent interface to specify more Browser settings</returns>
        public IAllowApplicationSettingsOrOpen WithUseHardware(bool trueFalse)
        {
            ApplicationSettings.UseHardware = trueFalse;
            return this;
        }
        /// <summary>
        /// Launch a new copy of the program rather than attaching to one that is already running.
        /// </summary>
        /// <remarks>
        /// Open attaches to a running copy by default and then ignores its parameters. Use this for programs that are
        /// usually running, such as explorer.exe.
        /// </remarks>
        /// <param name="trueFalse">True to always launch</param>
        /// <returns>Fluent interface to specify more Application settings</returns>
        public IAllowApplicationSettingsOrOpen WithNewInstance(bool trueFalse)
        {
            ApplicationSettings.NewInstance = trueFalse;
            return this;
        }
        /// <summary>
        /// Runs the program on its own Win32 desktop object: real windows and rendering, nothing on your screen, and
        /// its own input queue, so you can keep working.
        /// </summary>
        /// <remarks>
        /// Not a Windows virtual desktop. UIAutomation only: hardware input is turned off, so hover and right click do
        /// not work, even while peeking. Always launches a new copy. The desktop is released when the program closes.
        /// When the workflow itself runs on a hidden desktop, false means that hidden desktop.
        /// </remarks>
        /// <example>
        /// <code>
        ///     GPAL.Application
        ///         .WithHiddenDesktop(true)
        ///         .Open(@"C:\Windows\System32\notepad.exe");
        /// </code>
        /// </example>
        /// <param name="hiddenDesktop">True [default] for its own desktop.<br/>False for the desktop the workflow is on.</param>
        /// <returns>Fluent interface to specify more Application settings or Open</returns>
        public IAllowApplicationSettingsOrOpen WithHiddenDesktop(bool hiddenDesktop)
        {
            ApplicationSettings.HiddenDesktop = hiddenDesktop;
            ApplicationSettings.HiddenDesktopOwned = hiddenDesktop;

            // With no name, the program gets its own uniquely named desktop. Programs given the same name share one.
            if (true == hiddenDesktop)
                ApplicationSettings.HiddenDesktopName = Desktops.NextName();
            // A workflow on a hidden desktop has no visible desktop, so false means its own desktop.
            else if (null != Desktops.ProcessDesktop)
            {
                ApplicationSettings.HiddenDesktop = true;
                ApplicationSettings.HiddenDesktopName = Desktops.ProcessDesktop;
            }

            return this;
        }
        /// <summary>
        /// Run the program on the named Win32 desktop object. Programs naming the same desktop share it. Same rules as
        /// <see cref="WithHiddenDesktop(bool)"/>.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.WithHiddenDesktop("GPAL.Hidden.Invoices");
        ///     application.WithHiddenDesktop("GPAL.Hidden.Invoices");
        /// </code>
        /// </example>
        /// <param name="desktopName">Name of the desktop this program runs on.</param>
        /// <returns>Fluent interface to specify more Application settings or Open</returns>
        public IAllowApplicationSettingsOrOpen WithHiddenDesktop(string desktopName)
        {
            ApplicationSettings.HiddenDesktop = true;
            ApplicationSettings.HiddenDesktopOwned = string.IsNullOrWhiteSpace(desktopName);

            // Calling this always means a hidden desktop. An empty name is the same as WithHiddenDesktop(true).
            ApplicationSettings.HiddenDesktopName = true == string.IsNullOrWhiteSpace(desktopName)
                                                  ? Desktops.NextName()
                                                  : desktopName;

            return this;
        }
        /// <summary>
        /// Add a Unit of Work level CallIfFound handler to be called if any defined selectors find/match elements.<br/>
        /// This method will be called for every found selector in the current UOW.<br/>
        /// CallIf handlers can remove themselves by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="callIfFoundDelegate">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallIfFound(Application.CallIfDelegate callIfFoundDelegate)
        {
            CurrentUOW.AppCallIfFound.Add(callIfFoundDelegate);
            return this;
        }
        /// <summary>
        /// Add a Unit of Work level CallIfNotFound handler to be called if any defined selectors DO NOT find/match elements.<br/>
        /// This method will be called for every NOT FOUND selector in the current UOW.<br/>
        /// CallIf handlers can remove themselve by calling myApplication.RemoveCallIfHandlerEverywhere(handler)<br/>
        /// Selectors may remove themselves from a UOW by using selector.Remove()<br/>
        /// More than one callback may be defined and will cascade thru the list depending upon the callback return value.<br/>
        /// See: CallIfDelegate
        /// </summary>
        /// <param name="callIfNotFoundDelegate">Callback delegate</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowAfterAnySelector CallIfNotFound(Application.CallIfDelegate callIfNotFoundDelegate)
        {
            CurrentUOW.AppCallIfNotFound.Add(callIfNotFoundDelegate);
            return this;
        }
        /// <summary>
        /// Add a handler to call after a row of tokens is consumed and after all inputs are filled in with data.<br/>
        /// Only one CallAfterFillIn handler is possible, no chaining.<br/>
        /// Use FillInFrom[Database|File|GPALGrid] to loop thru data in your main workflow and then perform next workflow steps in the CallAfterFillIn handler.
        /// </summary>
        /// <param name="callAfterFillIn">Your CallAfterFillIn method</param>
        /// <returns>-1 to terminate the program, otherwise the value is ignored.</returns>
        public IAllowAfterAnySelector CallAfterFillIn(CallAfterFillInDelegate callAfterFillIn)
        {
            CurrentUOW.AppCallAfterFillIn = callAfterFillIn;
            return this;
        }
        /// <summary>
        /// True when the application's scrollable view is at the bottom, like the browser's IsEndOfPage. The view is
        /// the focused control or its nearest scrolling ancestor; with no focus, the first vertically scrolling control
        /// in the window. 100 percent is the bottom.
        /// <br/><br/>
        /// A window with nothing that scrolls counts as at the end, as the browser does.
        /// </summary>
        /// <example>
        /// <code>
        ///     while (false == application.IsEndOfPage())
        ///         application
        ///             .WithSelector(listSelector)
        ///             .ScrollVerticalLarge(1);
        /// </code>
        /// </example>
        /// <returns>True at the bottom, or when nothing scrolls</returns>
        public bool IsEndOfPage()
        {
            bool retVal = true;
            ScrollPattern view = ScrollableView();

            if (null != view)
            {
                ScrollPattern.ScrollPatternInformation at = view.Current;

                // a control that stops scrolling reports NoScroll, -1, rather than a position
                retVal = false == at.VerticallyScrollable || 100 <= Math.Round(at.VerticalScrollPercent);
            }

            return retVal;
        }

        /// <summary>
        /// Returns the ScrollPattern IsEndOfPage reads: the focused control or its nearest vertically scrolling
        /// ancestor, otherwise the first such control in the window. Focus in another program is ignored.
        /// </summary>
        /// <returns>The pattern, or null when nothing in the window scrolls</returns>
        internal ScrollPattern ScrollableView()
        {
            ScrollPattern retVal = null;
            AutomationElement root = RootAutomationElement;

            if (null != root)
            {
                AutomationElement focused = AutomationElement.FocusedElement;

                if (null != focused && focused.Current.ProcessId != root.Current.ProcessId)
                    focused = null;

                // the raw view, since WinForms leaves whole controls out of the control view
                for (AutomationElement at = focused; null != at && null == retVal; at = TreeWalker.RawViewWalker.GetParent(at))
                    if (true == at.TryGetCurrentPattern(ScrollPattern.Pattern, out object pattern) && true == ((ScrollPattern)pattern).Current.VerticallyScrollable)
                        retVal = (ScrollPattern)pattern;

                if (null == retVal)
                {
                    AutomationElement first = root.FindFirst(TreeScope.Subtree, new AndCondition(
                        new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true),
                        new PropertyCondition(ScrollPattern.VerticallyScrollableProperty, true)));

                    if (null != first)
                        retVal = (ScrollPattern)first.GetCurrentPattern(ScrollPattern.Pattern);
                }
            }

            return retVal;
        }

        #region <Getters/Setters>
        /// <summary>
        /// The hidden desktop this program runs on, or null when it is on the desktop you are looking at. Peek
        /// at it through this, and while a peek is on screen hardware input works there.
        /// </summary>
        public IHiddenDesktop HiddenDesktop => true == ApplicationSettings.HiddenDesktop
            ? new GPALHiddenDesktop(ApplicationSettings.HiddenDesktopName)
            : null;

        /// <summary>
        /// The column names the last GetGrid read from the control. Empty when the control had none.
        /// </summary>
        public IReadOnlyList<string> ColumnNames { get; private set; } = new List<string>();
        /// <summary>
        /// The current Windows process for the program run via Open.<br/>
        /// Only one program can be Opened/controlled per application.<br/>
        /// This get is intended for debugging only.
        /// </summary>
        /// <returns>The Windows Process</returns>
        public Process Process
        {
            get
            {
                return applicationSettings.Process;
            }
            internal set
            {
                applicationSettings.Process = value;
            }
        }
        /// <summary>
        /// The root UI Automation element (AutomationElement) for the managed application's main window.
        /// </summary>
        public object ManagedRootAutomationElement
        {
            get
            {
                return applicationSettings.ManagedRootAutomationElement;
            }

            internal set
            {
                applicationSettings.ManagedRootAutomationElement = value;
            }
        }
        /// <summary>
        /// The starting AutomationElement for the application. All xpath searches start from this AutomationElement.
        /// </summary>
        public AutomationElement RootAutomationElement
        {
            get
            {
                return applicationSettings.RootAutomationElement;
            }

            internal set
            {
                applicationSettings.RootAutomationElement = value;
            }
        }
        /// <summary>
        /// A name you provide to be used when publishing events.<br/>
        /// Use .WithName to provide your own friendly name. If not supplied GPAL will assign the name application1, application2, etc<br/>
        /// This get is intended for debugging only.
        /// </summary>
        /// <returns>The name you supplied.</returns>
        public string Name
        {
            get
            {
                return ApplicationSettings.Name;
            }

            internal set
            {
                ApplicationSettings.Name = value;
            }
        }
        /// <summary>
        /// The path of the program you Opened.<br/>
        /// This get is intended for debugging only.
        /// </summary>
        /// <returns>The path you supplied.</returns>
        public string ApplicationPath
        {
            get
            {
                return applicationSettings.ApplicationPath;
            }
            internal set
            {
                applicationSettings.ApplicationPath = value;
            }
        }
        /// <summary>
        /// Current Application settings, defaults and what you define
        /// This get is intended for debugging only.
        /// </summary>
        internal ApplicationSettings ApplicationSettings
        {
            get 
            { 
                return applicationSettings; 
            }
        }
        #endregion <Getters/Setters>
        #endregion <Application Settings>
    }
}

