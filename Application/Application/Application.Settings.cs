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
    }
}
