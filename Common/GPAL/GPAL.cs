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
    /// <summary>
    /// Everything starts here, all the GPAL controls and global settings using fluent syntax are here.
    /// GPAL cannot inherit any of the GPAL interfaces since it is a static class, but it does implement them, but will return some GPAL object or interface.
    /// </summary>
    public partial class GPAL
    {
        private static Stopwatch _appTimer;
        private static DateTime _startTime;
        private static List<Application.Application> ApplicationList { get; } = new List<Application.Application>();
        private static List<Browser.Browser> BrowserList { get; } = new List<Browser.Browser>();
        private static List<Selector> SelectorList { get; } = new List<Selector>();
        private static List<GPALForm.GPALForm> FormList { get; } = new List<GPALForm.GPALForm>();
        private static List<IGPALAI> AIList { get; } = new List<IGPALAI>();
        private static List<GPALFile> FileList { get; } = new List<GPALFile>();
        private static List<IGPALMail> MailList { get; } = new List<IGPALMail>();
        public static List<IGPALLogger> LoggerList { get; } = new List<IGPALLogger>();
        private static List<GPALControl> ControlList { get; } = new List<GPALControl>();
        private static List<IGPALGrid<string>> GridList { get; } = new List<IGPALGrid<string>>();
        private static Browser.ElementAssistant elementAssistant { get; } = new Browser.ElementAssistant();
        private static Application.ElementAssistant applicationElementAssistant { get; } = new Application.ElementAssistant();

        private static Browser.Browser tmpBrowser { get; set; } = null;
        private static Application.Application tmpApplication { get; set; } = null;
        private static bool UncaughtExceptionHandlerSet { get; set; } = false;

        private static int applicationCount = 1;
        private static int browserCount = 1;
        private static int selectorCount = 1;
        private static int formCount = 1;
        private static int controlCount = 1;    // one count shared by every control type
        private static int executableCount = 1;
        private static int restClientCount = 1;

        private static readonly string applicationName = "Application";
        private static readonly string browserName = "Browser";
        private static readonly string chartName = "Chart";
        private static readonly string selectorName = "Selector";
        private static readonly string formName = "Form";
        private static readonly string buttonName = "Button";
        private static readonly string checkboxName = "Checkbox";
        private static readonly string inputName = "Input";
        private static readonly string labelName = "Label";
        private static readonly string linkLabelName = "LinkLabel";
        private static readonly string spacerName = "Spacer";
        private static readonly string radioButtonName = "Radiobutton";
        private static readonly string textAreaName = "TextArea";
        private static readonly string richTextBoxName = "RichTextBox";
        private static readonly string executableName = "Executable";
        private static readonly string restClientName = "RESTClient";

        private static bool inPublishEvent = false;
        // Loggers currently writing on this thread. A logger does not receive events it raises while writing.
        [ThreadStatic] private static HashSet<IGPALLogger> loggersWriting;
        static bool inStaticInit = false;
        // Private constructor to prevent instantiation
        private GPAL() { }

        // true when a window existed before GPAL was first used, so Windows Forms kept its own text rendering
        internal static bool TextRenderingRefused { get; private set; }

        /// <summary>
        /// Entry point for everything.
        /// Set GPAL_IS_SILENT to 1 to stop output at startup when the program uses stdio for communications,
        ///     like a browser extension native app where that output would be misread as a message
        /// </summary>
        static GPAL()
        {
            if (false == inStaticInit)
            {
                inStaticInit = true;

#if NET
                // .NET 10 carries only UTF-8 and Unicode until the code page encodings are registered; registered,
                // the ANSI code page and redirected process output decode as they do on .NET Framework
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif

                // Initialize timer
                _startTime = DateTime.Now;
                _appTimer = Stopwatch.StartNew();

                GPAL.LoadSettings();

                if (ConsoleEvents.HasFlag(GPALEventType.INFO))
                {
                    PublishSimpleEvent(GPALEventType.INFO, $">>> GPAL v[{GPAL.Version}]");
                    PublishSimpleEvent(GPALEventType.INFO, $">>> Application Start Time: [{_startTime.ToString("yyyy-MM-dd HH:mm:ss")}] <<<");
                }
                else if (ConsoleEvents.HasFlag(GPALEventType.NOTICE))
                {
                    PublishSimpleEvent(GPALEventType.NOTICE, $">>> GPAL v[{GPAL.Version}]");
                    PublishSimpleEvent(GPALEventType.NOTICE, $">>> Application Start Time: [{_startTime.ToString("yyyy-MM-dd HH:mm:ss")}] <<<");
                }

                // the lines reached the console only when GPAL.yaml turned it on; otherwise WithPublishToConsole writes them
                versionPublished = true == PublishToConsole && (ConsoleEvents.HasFlag(GPALEventType.INFO) || ConsoleEvents.HasFlag(GPALEventType.NOTICE));

                SetCatchUncaughtExceptions();

                // GDI text, which draws emoji and matches the rest of Windows; the GDI+ default draws emoji as empty
                // boxes. Chosen at GPAL's first use rather than at the first form, because Windows Forms refuses the
                // choice once any window exists, and a started Timer or a message box makes one. A program that made a
                // window before touching GPAL keeps its own choice, and the first GPAL form says so
                try
                {
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
#if NET
                    // .NET Core changed the Windows Forms default font to Segoe UI 9; forms keep the size and look
                    // they have on .NET Framework
                    System.Windows.Forms.Application.SetDefaultFont(new Font(new FontFamily("Microsoft Sans Serif"), 8.25f));
#endif
                }
                catch (InvalidOperationException)
                {
                    TextRenderingRefused = true;
                }

                inStaticInit = false;
            }
        }

        // true once the version and start time lines have gone to the console
        private static bool versionPublished = false;

        // Writes the GPAL version and start time lines to the console when they have not reached it yet. Called from
        // WithPublishToConsole, so a program that turns the console on itself gets them as its first lines
        internal static void PublishVersion()
        {
            if (false == versionPublished && true == PublishToConsole)
            {
                if (ConsoleEvents.HasFlag(GPALEventType.INFO))
                {
                    versionPublished = true;
                    PublishSimpleEvent(GPALEventType.INFO, $">>> GPAL v[{GPAL.Version}]");
                    PublishSimpleEvent(GPALEventType.INFO, $">>> Application Start Time: [{_startTime.ToString("yyyy-MM-dd HH:mm:ss")}] <<<");
                }
                else if (ConsoleEvents.HasFlag(GPALEventType.NOTICE))
                {
                    versionPublished = true;
                    PublishSimpleEvent(GPALEventType.NOTICE, $">>> GPAL v[{GPAL.Version}]");
                    PublishSimpleEvent(GPALEventType.NOTICE, $">>> Application Start Time: [{_startTime.ToString("yyyy-MM-dd HH:mm:ss")}] <<<");
                }
            }
        }
    }
}
