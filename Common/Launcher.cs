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
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    // written by Grok
    public class Launcher : IAllowExecutableSetup, IAllowRunningExecutable, IAllowWaitingExecutable
    {
        public string Name { get; internal set; }
        public string Executable { get; internal set; }

        private readonly List<string> _arguments = new List<string>();
        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
        private string _workingDirectory;
        private Action<string> _outputHandler;
        private Action<string> _errorHandler;
        private Process _process;
        private bool _hasWaitedOrExited;

        public Launcher()
        {
        }

        public IAllowExecutableSetup WithExecutable(string executable)
        {
            Executable = executable;
            return this;
        }

        public IAllowExecutableSetup WithName(string name)
        {
            Name = name;
            return this;
        }
        public IAllowExecutableSetup WithArgument(string argument)
        {
            if (_process != null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Cannot add arguments after starting.", null, GPALObjectType.None);
                return this; // Return to allow chaining, but operation is skipped
            }
            if (argument == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Argument cannot be null.", null, GPALObjectType.None, new ArgumentNullException(nameof(argument)));
                return this;
            }
            _arguments.Add(argument);
            return this;
        }

        /// <summary>
        /// Sets the directory the process starts in. Set before Launch.
        /// </summary>
        /// <param name="directory">The directory to start in</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowExecutableSetup WithWorkingDirectory([Directory] string directory)
        {
            if (null == directory)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "A working directory cannot be null", null, GPALObjectType.None);
                return this;
            }

            _workingDirectory = directory;
            return this;
        }

        /// <summary>
        /// Adds one environment variable for the process, on top of the inherited environment. Call once per variable.
        /// </summary>
        /// <param name="name">The variable's name</param>
        /// <param name="value">Its value</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowExecutableSetup WithEnvironmentVariable(string name, string value)
        {
            if (null == name || null == value)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"An environment variable needs a name and a value, given [{name ?? "NULL"}] and [{value ?? "NULL"}]", null, GPALObjectType.None);
                return this;
            }

            _environment[name] = value;
            return this;
        }

        /// <summary>
        /// Sets a handler for each line written to standard output. Set before Launch; redirection is set up at start.
        /// </summary>
        /// <param name="outputHandler">Called once per line</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowExecutableSetup WithOutputTo(Action<string> outputHandler)
        {
            _outputHandler = outputHandler;
            return this;
        }

        /// <summary>
        /// Sets a handler for each line written to standard error. Set before Launch, as with
        /// <see cref="WithOutputTo"/>.
        /// </summary>
        /// <param name="errorHandler">Called once per line</param>
        /// <returns>Fluent interface to define more settings</returns>
        public IAllowExecutableSetup WithErrorTo(Action<string> errorHandler)
        {
            _errorHandler = errorHandler;
            return this;
        }

        /// <summary>
        /// Formats one argument: quoted if it contains a space, with inner quotes escaped.
        /// </summary>
        /// <param name="argument">The argument as the workflow wrote it</param>
        /// <returns>The argument, ready to join with the others</returns>
        private static string Quoted(string argument)
        {
            if (false == argument.Contains(" ") && false == argument.Contains("\""))
                return argument;

            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        // Extensions Windows starts directly. No extension also counts; Windows tries .exe.
        private static readonly HashSet<string> runsDirectly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".bat", ".cmd"
        };

        /// <summary>
        /// True when the path is a document: its extension is not one Windows starts directly. The extension comes from
        /// the last path segment, so a dot in a folder name is ignored.
        /// </summary>
        /// <param name="path">The executable as the workflow wrote it</param>
        /// <returns>True when the file opens with the program Windows has registered for it</returns>
        private static bool IsDocument(string path)
        {
            string name = path.Trim().Trim('"');
            name = name.Substring(Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/')) + 1);

            int dot = name.LastIndexOf('.');

            return 0 <= dot && dot < name.Length - 1 && false == runsDirectly.Contains(name.Substring(dot));
        }

        public IAllowRunningExecutable Launch()
        {
            if (_process != null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Process already started.", null, GPALObjectType.None);
                return this;
            }

            // Open a url or document through the shell; run an executable directly.
            bool opensWithTheShell = Executable.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                  || Executable.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                  || true == IsDocument(Executable);

            // Reading output and setting environment variables both need the shell off.
            bool wantsOurOwnProcess = null != _outputHandler || null != _errorHandler || 0 < _environment.Count;

            if (true == opensWithTheShell && true == wantsOurOwnProcess)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{Executable}] opens with the shell, which has no output to read and no environment to set. Those settings are ignored", null, GPALObjectType.None);
                wantsOurOwnProcess = false;
                _outputHandler = null;
                _errorHandler = null;

                // The shell cannot set environment variables; turn it off.
                _environment.Clear();
            }

            bool shellExecute = opensWithTheShell && false == wantsOurOwnProcess;

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Executable,
                    Arguments = string.Join(" ", _arguments.Select(Quoted)),
                    UseShellExecute = shellExecute,
                    RedirectStandardOutput = null != _outputHandler,
                    RedirectStandardError = null != _errorHandler,
                    CreateNoWindow = !shellExecute
                }
            };

            if (false == string.IsNullOrEmpty(_workingDirectory))
                _process.StartInfo.WorkingDirectory = _workingDirectory;

            foreach (KeyValuePair<string, string> variable in _environment)
                _process.StartInfo.EnvironmentVariables[variable.Key] = variable.Value;

            // Attach the handlers before Start so no line is missed.
            if (null != _outputHandler)
                _process.OutputDataReceived += (sender, e) => { if (null != e.Data) _outputHandler(e.Data); };

            if (null != _errorHandler)
                _process.ErrorDataReceived += (sender, e) => { if (null != e.Data) _errorHandler(e.Data); };

            try
            {
                _process.Start();

                // Start reading after Start, only for redirected streams.
                if (null != _outputHandler)
                    _process.BeginOutputReadLine();

                if (null != _errorHandler)
                    _process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to start [{Executable}]", null, GPALObjectType.None, ex);
            }

            return this;
        }

        /// <summary>
        /// True while the process is running; false before Launch and after it ends.
        /// </summary>
        /// <returns>True while the process is running</returns>
        public bool IsRunning()
        {
            bool retVal = null != _process && false == _hasWaitedOrExited && false == _process.HasExited;

            return retVal;
        }

        /// <summary>
        /// Wait up to this many milliseconds for the process to end.
        /// </summary>
        /// <param name="milliseconds">How long to wait</param>
        /// <returns>Fluent interface to stop the process if it is still up</returns>
        public IAllowWaitingExecutable WaitForExit([Milliseconds] int milliseconds)
        {
            if (true == Started("WaitForExit"))
            {
                _process.WaitForExit(milliseconds);
                _hasWaitedOrExited = true;
            }

            return this;
        }

        /// <summary>
        /// Stop the process. What it started keeps running; <see cref="KillTree"/> is for those.
        /// </summary>
        public void Kill()
        {
            if (false == Started("Kill") || false == IsRunning())
                return;

            _process.Kill();
            _hasWaitedOrExited = true;
        }

        /// <summary>
        /// Stop the process and everything it started, children first so nothing is orphaned.
        /// </summary>
        public void KillTree()
        {
            if (false == Started("KillTree") || false == IsRunning())
                return;

            KillProcessTree(_process.Id);
            _process.Kill();
            _hasWaitedOrExited = true;
        }
        private void KillProcessTree(int pid)
        {
            try
            {
                // Use WMI to find child processes
                using (var searcher = new ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Process WHERE ParentProcessId = {pid}"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        int childPid = Convert.ToInt32(obj["ProcessId"]);
                        KillProcessTree(childPid); // Recursively kill children
                        try
                        {
                            Process.GetProcessById(childPid).Kill();
                        }
                        catch (ArgumentException)
                        {
                            // Process already exited, ignore
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to kill process tree for PID [{pid}]", null, GPALObjectType.None, ex);
            }
        }
        public Launcher ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// Returns true when Launch has been called; otherwise warns and returns false.
        /// </summary>
        /// <param name="wanted">What the caller was trying to do, for the message</param>
        /// <returns>True when Launch has been called</returns>
        private bool Started(string wanted)
        {
            bool retVal = null != _process;

            if (false == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{wanted}] needs a process, and [{Executable ?? "no executable"}] has not been launched", null, GPALObjectType.None);

            return retVal;
        }
    }
}

