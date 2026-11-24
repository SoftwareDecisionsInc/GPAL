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

using DocumentFormat.OpenXml.Office.CustomUI;
using GenerallyPositive.Application;
using GenerallyPositive.Browser;
using GenerallyPositive.GPALForm;
using OpenQA.Selenium;
using OpenQA.Selenium.Remote;
using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using static GenerallyPositive.Browser.Browser;
using static GenerallyPositive.Enums;
using static GenerallyPositive.GPALForm.GPALForm;

namespace GenerallyPositive
{
    public partial class GPAL
    {
        private static void SetCatchUncaughtExceptions()
        {
            if (false == UncaughtExceptionHandlerSet)
            {
                CheckVersions();

                try
                {
                    // Set the unhandled exception mode to force errors to go through our handler. 
                    System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                    // Add the event handler for handling thread exceptions to the event.
                    AppDomain.CurrentDomain.UnhandledException +=
                        new UnhandledExceptionEventHandler(UncaughtExceptionHandler);

                    // Hook cleanup handlers
                    Console.CancelKeyPress += OnCancelKeyPress;
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

                }
                catch (Exception ex)
                {
                    PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to set uncaught exception handler", null, Enums.GPALObjectType.None, ex);
                }

                UncaughtExceptionHandlerSet = true;
            }
        }

        private static bool handlingTermination = false;
        private static void UncaughtExceptionHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;

            if (false == handlingTermination)
            {
                handlingTermination = true;

                // Do not pass the exception; its stack trace is noise here.
                bool ours = e is GPALException;
                string reason = true == ours ? "GPAL exception" : "uncaught exception";

                if (ConsoleEvents.HasFlag(GPALEventType.WARNING))
                {
                    PublishSimpleEvent(GPALEventType.WARNING, $">>> Caught {reason}: [{e.Message}][{e.StackTrace}]", null, Enums.GPALObjectType.None);
                    PublishSimpleEvent(GPALEventType.WARNING, $">>> [{reason}] Cleaning up before terminating.", null, GPALObjectType.None);
                }
                else if (ConsoleEvents.HasFlag(GPALEventType.CAUTION))
                {
                    PublishSimpleEvent(GPALEventType.CAUTION, $">>> Caught {reason}: [{e.Message}][{e.StackTrace}]", null, Enums.GPALObjectType.None);
                    PublishSimpleEvent(GPALEventType.CAUTION, $">>> [{reason}] Cleaning up before terminating.", null, GPALObjectType.None);
                }

                if (false == PublishToConsole)
                    GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Terminating due to [{reason}]: [{e.Message}]");

                if (false == PublishToDebug)
                    Debug.WriteLine($"Terminating due to {reason}: [{e.Message}]");

                CleanupAndExit(true == ours ? "GPAL Exception" : "Unhandled Exception");
            }
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            //PublishSimpleEvent(GPALEventType.INFO, $">>> Detected Ctrl+C or Ctrl+Break.");

            if (false == handlingTermination)
            {
                handlingTermination = true;

                e.Cancel = true; // Prevents immediate termination

                if (false == PublishToConsole)
                    GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Terminating due to Ctrl+C or Ctrl+Break.");

                if (false == PublishToDebug)
                    Debug.WriteLine($"Terminating due to Ctrl+C or Ctrl+Break.");

                CleanupAndExit("Ctrl+C or Ctrl+Break");
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            // PublishSimpleEvent(GPALEventType.INFO, $">>> Detected process exit or console X.");

            if (false == handlingTermination)
            {
                handlingTermination = true;

                if (false == PublishToConsole)
                    GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Terminating due to Process Exit or console X.");

                if (false == PublishToDebug)
                    Debug.WriteLine($"Terminating due to Process Exit or console X.");

                CleanupAndExit("Process Exit or console X");
            }
        }

        private static bool _cleanupDone = false;

        internal static void CleanupAndExit(string reason)
        {
            if (false == _cleanupDone)  // run once, even when several kill signals arrive
            {
                _cleanupDone = true;    
                try
                {
                    if (ConsoleEvents.HasFlag(GPALEventType.INFO))
                        PublishSimpleEvent(GPALEventType.INFO, $">>> [{reason}] Cleaning up before terminating.", null, GPALObjectType.None);
                    else if (ConsoleEvents.HasFlag(GPALEventType.NOTICE))
                        PublishSimpleEvent(GPALEventType.NOTICE, $">>> [{reason}] Cleaning up before terminating.", null, GPALObjectType.None);

                    if (false == PublishToConsole)
                        GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"[{reason}] Cleaning up before terminating.");

                    if (false == PublishToDebug)
                        Debug.WriteLine($"[{reason}] Cleaning up before terminating.");

                    BrowserHelper.KillAllRunningProcesses(null);

                    // a driver a Close could not end, such as one that hung
                    BrowserHelper.EndStartedDrivers();

                    // This only releases our handle. Windows keeps the desktop until its last window closes.
                    GenerallyPositive.Browser.HiddenDesktop.CloseAll();
                }
                catch (Exception ex)
                {
                    PublishSimpleEvent(GPALEventType.EXCEPTION, $"Cleanup error", null, GPALObjectType.None, ex);
                }

                // Stop timer and publish end time + total runtime
                _appTimer.Stop();
                string endTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                TimeSpan totalRuntime = _appTimer.Elapsed;
                string formattedRuntime = $"{totalRuntime.Hours:D2}:{totalRuntime.Minutes:D2}:{totalRuntime.Seconds:D2}.{totalRuntime.Milliseconds:D3}";

                if (ConsoleEvents.HasFlag(GPALEventType.INFO))
                {
                    PublishSimpleEvent(GPALEventType.INFO, $">>> Application End Time: [{endTime}] <<<", null, GPALObjectType.None);
                    PublishSimpleEvent(GPALEventType.INFO, $">>> Total Runtime: [{formattedRuntime}] <<<", null, GPALObjectType.None);
                }
                else if (ConsoleEvents.HasFlag(GPALEventType.NOTICE))
                {
                    PublishSimpleEvent(GPALEventType.NOTICE, $">>> Application End Time: [{endTime}] <<<", null, GPALObjectType.None);
                    PublishSimpleEvent(GPALEventType.NOTICE, $">>> Total Runtime: [{formattedRuntime}] <<<", null, GPALObjectType.None);
                }

                // Publish only, like the startup time and version, so PublishToConsole off keeps these off the console.

                if (true == GPALSettings.PromptBeforeUnexpectedExit)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.NOTICE, "Press any key to exit...");
                    Debug.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }

            // Normal exit returns 0 on net48. The process is already ending here. On .NET 10, Environment.Exit
            // called inside the ProcessExit handler waits for the shutdown that is running this handler, so the
            // process never terminates.
            if (true == reason.Equals("Process Exit or console X"))
            {
#if !NET
                Environment.Exit(0);
#endif
            }
            else
                Environment.Exit(0xdead);
        }
    }
}
