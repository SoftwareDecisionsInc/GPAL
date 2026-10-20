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
using System.Diagnostics;
using System.IO;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Switches the screen back to Default if the process that switched to a hidden desktop is killed, since all
    /// other switch-back code runs inside that process.
    /// <br/><br/>
    /// Before switching, GPAL starts a PowerShell process that waits for this process to end (or a deadline), then
    /// switches to Default; if the peek already switched back, that does nothing.
    /// <br/><br/>
    /// There is no manual way back: no Explorer, Ctrl+Alt+Del returns to the hidden desktop, and signing out ends
    /// the session. This guard and the escape window are the only ways back, and both must be in place before the
    /// switch.
    /// </summary>
    internal static class DesktopGuard
    {
        /// <summary>What the recovery script is called where it is written.</summary>
        internal const string ScriptName = "GPALReturnToDefaultDesktop.ps1";

        /// <summary>
        /// Default deadline, in seconds, after which the guard switches back even if this process is running. Used
        /// when no peek time is given.
        /// </summary>
        private const int DefaultDeadlineInSeconds = 600;

        // The script is written next to the running exe rather than shipped, so it always matches this GPAL version.
        private const string script = @"param([int]$ParentPid = 0, [int]$DeadlineSeconds = 0)

# Puts the screen back on the Default desktop.
#
# GPAL writes this file next to the binary that used a hidden desktop, and runs it in the background while a
# peek is on, so a process that dies holding the screen still hands it back.
#
# Run with no arguments, it switches to Default at once. It cannot be started from a stranded screen: a hidden
# desktop has no Explorer, and Ctrl+Alt+Del returns to the hidden desktop. The ways back are this guard and the
# Return window GPAL puts on the hidden desktop, with Escape, both in place before the switch.

Add-Type -Namespace GPAL -Name Desktops -MemberDefinition @'
[DllImport(""user32.dll"", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

[DllImport(""user32.dll"", SetLastError = true)]
public static extern bool SwitchDesktop(IntPtr hDesktop);

[DllImport(""user32.dll"", SetLastError = true)]
public static extern bool CloseDesktop(IntPtr hDesktop);
'@

$DesktopSwitchDesktop = 0x0100

if ($ParentPid -gt 0) {
    $deadline = (Get-Date).AddSeconds($(if ($DeadlineSeconds -gt 0) { $DeadlineSeconds } else { 600 }))

    while ((Get-Date) -lt $deadline) {
        if ($null -eq (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) { break }

        Start-Sleep -Milliseconds 500
    }
}

# switching to the desktop already on the screen does nothing, so this is safe whether anything is stranded or not
$default = [GPAL.Desktops]::OpenDesktop('Default', 0, $false, $DesktopSwitchDesktop)

if ($default -ne [IntPtr]::Zero) {
    [void][GPAL.Desktops]::SwitchDesktop($default)
    [void][GPAL.Desktops]::CloseDesktop($default)
}
";

        /// <summary>Folder the script is written to: the running exe's folder.</summary>
        internal static string Folder
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>
        /// Puts the script beside the binary, overwriting whatever is there so the copy that runs is this
        /// version's.
        /// </summary>
        /// <returns>The script's path, or null when it could not be written.</returns>
        internal static string Write()
        {
            string retVal = null;
            string path = Path.Combine(Folder, ScriptName);

            // If the folder cannot be written to, peek without a guard instead of failing.
            try
            {
                File.WriteAllText(path, script);

                retVal = path;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not put the desktop recovery script in [{Folder}]", null, GPALObjectType.Browser, ex);
            }

            return retVal;
        }

        /// <summary>
        /// Starts the guard process for a peek. It runs outside this process with only our process id and a
        /// deadline, so it survives this process being killed.
        /// </summary>
        /// <param name="forMs">How long the peek expects to hold the screen. The guard's deadline is this plus 30 seconds.</param>
        /// <returns>The guard, or null when there is not one.</returns>
        internal static Process Start(int forMs)
        {
            Process retVal = null;
            string path = Write();

            if (null != path)
            {
                int seconds = 0 < forMs ? (forMs / 1000) + 30 : DefaultDeadlineInSeconds;

                ProcessStartInfo starting = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden " +
                                $"-File \"{path}\" -ParentPid {Process.GetCurrentProcess().Id} -DeadlineSeconds {seconds}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                // As in Write: if the guard cannot start, peek without one instead of failing.
                try
                {
                    retVal = Process.Start(starting);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Could not start the desktop guard from [{path}]", null, GPALObjectType.Browser, ex);
                }
            }

            return retVal;
        }

        /// <summary>
        /// Kills the guard after the peek switched back itself; otherwise it would sit until its deadline.
        /// </summary>
        /// <param name="guard">The guard, or null when there was not one.</param>
        internal static void Stop(Process guard)
        {
            if (null != guard)
            {
                // Runs as the peek ends with the screen already back; failing to kill the guard is not worth failing
                // the caller.
                try
                {
                    if (false == guard.HasExited)
                        guard.Kill();
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "Could not stop the desktop guard", null, GPALObjectType.Browser, ex);
                }

                guard.Dispose();
            }
        }
    }
}
