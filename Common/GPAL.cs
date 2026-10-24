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
using System.Data.SqlClient;
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
    public class GPAL
    {
        private static Stopwatch _appTimer;
        private static DateTime _startTime;
        
        #region <Virtual Keys>
        /// <summary>
        /// Alt key virtual keycode (VK) for use with keybd_event.
        /// https://docs.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
        /// </summary>
        public static byte VK_ALT { [Keys(Description = "Virtual key code for Alt, for use with keybd_event")] get { return 0x12; } }
        /// <summary>
        /// Delete key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_DELETE { [Keys(Description = "Virtual key code for Delete, for use with keybd_event")] get { return 0x2e; } }
        /// <summary>
        /// F10 key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_F10 { [Keys(Description = "Virtual key code for F10, for use with keybd_event")] get { return 0x79; } }
        /// <summary>
        /// Page Up key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_PRIOR { [Keys(Description = "Virtual key code for Page Up, for use with keybd_event")] get { return 0x21; } } // page up - to scroll up a page
        /// <summary>
        /// Page Down key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_NEXT { [Keys(Description = "Virtual key code for Page Down, for use with keybd_event")] get { return 0x22; } }  // page down - to scroll down a page
        /// <summary>
        /// End key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_END { [Keys(Description = "Virtual key code for End, for use with keybd_event")] get { return 0x23; } }  // end key
        /// <summary>
        /// Home key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_HOME { [Keys(Description = "Virtual key code for Home, for use with keybd_event")] get { return 0x24; } } // home key
        /// <summary>
        /// Down Arrow key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_DOWN { [Keys(Description = "Virtual key code for the Down Arrow, for use with keybd_event")] get { return 0x28; } } // down arrow
        /// <summary>
        /// Up Arrow key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_UP { [Keys(Description = "Virtual key code for the Up Arrow, for use with keybd_event")] get { return 0x26; } } // up arrow
        /// <summary>
        /// Tab key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_TAB { [Keys(Description = "Virtual key code for Tab, for use with keybd_event")] get { return 0x09; } } // tab
        /// <summary>
        /// Enter/Return key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_RETURN { [Keys(Description = "Virtual key code for Enter, for use with keybd_event")] get { return 0x0D; } } // enter
        /// <summary>
        /// Escape key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_ESCAPE { [Keys(Description = "Virtual key code for Escape, for use with keybd_event")] get { return 0x1B; } } // escape
        /// <summary>
        /// Right Shift key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_SHIFT_RIGHT { [Keys(Description = "Virtual key code for the right Shift, for use with keybd_event")] get { return 0xA1; } }
        /// <summary>
        /// Left Shift key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_SHIFT_LEFT { [Keys(Description = "Virtual key code for the left Shift, for use with keybd_event")] get { return 0xA0; } }
        /// <summary>
        /// Left Control key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_CONTROL_LEFT { [Keys(Description = "Virtual key code for the left Control, for use with keybd_event")] get { return 0xA2; } }
        /// <summary>
        /// Right Control key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_CONTROL_RIGHT { [Keys(Description = "Virtual key code for the right Control, for use with keybd_event")] get { return 0xA3; } }
        /// <summary>
        /// Left Windows key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_LWIN { [Keys(Description = "Virtual key code for the left Windows key, for use with keybd_event")] get { return 0x5B; } }
        /// <summary>
        /// Right Windows key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_RWIN { [Keys(Description = "Virtual key code for the right Windows key, for use with keybd_event")] get { return 0x5C; } }
        /// <summary>
        /// Scroll Lock key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_SCROLL { [Keys(Description = "Scroll Lock key virtual keycode (VK) for use with keybd_event")] get { return 0x91; } }
        /// <summary>
        /// Left Alt key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_LMENU { [Keys(Description = "Left Alt key virtual keycode (VK) for use with keybd_event")] get { return 0xA4; } }
        /// <summary>
        /// Right Alt key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_RMENU { [Keys(Description = "Right Alt key virtual keycode (VK) for use with keybd_event")] get { return 0xA5; } }
        /// <summary>
        /// Application (Menu) key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_APPS { [Keys(Description = "Application (Menu) key virtual keycode (VK) for use with keybd_event")] get { return 0x5D; } }
        /// <summary>
        /// Space bar key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_SPACE { [Keys(Description = "Virtual key code for the space bar, for use with keybd_event")] get { return 0x20; } }
        /// <summary>
        /// Backspace key virtual keycode (VK) for use with keybd_event
        /// </summary>
        public static byte VK_BACK { [Keys(Description = "Virtual key code for Backspace, for use with keybd_event")] get { return 0x08; } }
        /// <summary>
        /// The extended-key flag indicates whether the keystroke message originated from one of the additional keys on the enhanced keyboard. 
        /// The extended keys consist of the ALT and CTRL keys on the right-hand side of the keyboard; 
        /// the INS, DEL, HOME, END, PAGE UP, PAGE DOWN, and arrow keys in the clusters to the left of the numeric keypad; 
        /// the NUM LOCK key; the BREAK (CTRL+PAUSE) key; the PRINT SCRN key; and the divide (/) and ENTER keys in the numeric keypad. 
        /// The extended-key flag is set if the key is a extended key
        /// For use with keybd_event
        /// </summary>
        /// <example>
        /// <code>
        /// keybd_event(GPAL.VK_CONTROL_LEFT, 0, GPAL.KEYEVENTF_EXTENDEDKEY | 0, 0);
        /// </code>
        /// </example>
        [Keys(Description = "keybd_event flag marking an extended key, such as the right-hand Alt and Control")]
        public static uint KEYEVENTF_EXTENDEDKEY { [Keys(Description = "keybd_event flag marking an extended key, such as the right-hand Alt and Control")] get { return 0x0001; } }
        /// <summary>
        /// Indicates a keyup keystroke is being sent.
        /// For use with keybd_event
        /// </summary>
        /// <example>
        /// <code>
        /// keybd_event(GPAL.VK_CONTROL_LEFT, 0, GPAL.KEYEVENTF_EXTENDEDKEY | GPAL.KEYEVENTF_KEYUP, 0);
        /// </code>
        /// </example>
        public static uint KEYEVENTF_KEYUP { [Keys(Description = "keybd_event flag that sends the release of a key rather than the press")] get { return 0x0002; } }
        #endregion <Virtual Keys>
        #region <Working Variables>
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

        private static bool SettingsLoaded 
        { 
            get
            {
                return gpalSettings.SettingsLoaded;
            }

            set
            {
                gpalSettings.SettingsLoaded = value;
            }
        }
        // deep copy cannot copy to properties
        private static GPALSettings gpalSettings = new GPALSettings(true);    // this will be initialized if we can't load from GPAL.yaml
        private static string _settingsFilePath = "GPAL.yaml";
        public static GPALSettings GPALSettings
        {
            [GPALSettings(Description = "The global settings GPAL is running with")] get
            {
                return gpalSettings;
            }
            set
            {
                gpalSettings = value;
            }
        }
        private static Browser.ElementAssistant elementAssistant { get; } = new Browser.ElementAssistant();
        private static Application.ElementAssistant applicationElementAssistant { get; } = new Application.ElementAssistant();

        private static Browser.Browser tmpBrowser { get; set; } = null;
        private static Application.Application tmpApplication { get; set; } = null;
        private static bool UncaughtExceptionHandlerSet { get; set; } = false;

        private static int applicationCount = 1;
        private static int browserCount = 1;
        private static int selectorCount = 1;
        private static int formCount = 1;
        private static int controlCount = 1;    // will not count each type of control (but should we?)
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
        #endregion <Working Variables>
        #region Constructor
        static bool inStaticInit = false;
        // Private constructor to prevent instantiation
        private GPAL() { }

        // true when a window existed before GPAL was first used, so Windows Forms kept its own text rendering
        internal static bool TextRenderingRefused { get; private set; }

        /// <summary>
        /// Entry point for everything.
        /// NOTE: GPAL_IS_SILENT can be set to prevent output on startup, should our application be using stdio for communications
        ///     like a browser extension native app where that output would be misread as a message
        /// </summary>
        static GPAL()
        {
            if (false == inStaticInit)
            {
                inStaticInit = true;

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

                SetCatchUncaughtExceptions();

                // GDI text, which draws emoji and matches the rest of Windows; the GDI+ default draws emoji as empty
                // boxes. Chosen at GPAL's first use rather than at the first form, because Windows Forms refuses the
                // choice once any window exists, and a started Timer or a message box makes one. A program that made a
                // window before touching GPAL keeps its own choice, and the first GPAL form says so
                try
                {
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                }
                catch (InvalidOperationException)
                {
                    TextRenderingRefused = true;
                }

                inStaticInit = false;
            }
        }
        #endregion Constructor
        #region <Exception & Information EventHandler>
        /// <summary>
        /// Used to pass objects of interest when publishing an Information or Exception Channel message<br/>
        /// <b>NOTE</b>: Properties are contextual to where the Information/Exception is published.<br/>
        /// Only objects relevant to that context will be passed as an event argument, the rest will be null.<br/>
        /// In this way, the programmer can examine aspects when the message is published and utilize those for logging or debugging or fault-tolerance handling.<br/>
        /// <b>Every event will have a GPALObject as applicable to the context publishing the message.</b>
        /// </summary>
        public class GPALEventArgs : System.EventArgs
        {
            public Enums.GPALEventType GPALEventType { get; set; }
            /// <summary>
            /// Current context's Application, Browser, GPALForm, UnitOfWork object
            /// </summary>
            public dynamic GPALObject { get; set; }
            /// <summary>
            /// GPALObjectType enumeration: None, Application, Browser, GPALForm, UnitOfWork
            /// </summary>
            public Enums.GPALObjectType GPALObjectType { get; set; }
            /// <summary>
            /// The Current Unit of Work for the GPALObject supplied.<br/>
            /// GPALObject (depending upon context) might also be the CurrentUOW, in which case it will also be here.
            /// </summary>
            public UnitOfWork CurrentUOW { get; set; }
            /// <summary>
            /// The Message being published to the Information/Exception channel.
            /// </summary>
            public string Message { get; set; }
            /// <summary>
            /// The Exception being published on the Exception channel.
            /// </summary>
            public Exception ExceptionRaised { get; set; }
            /// <summary>
            /// Every message is accompanied by an automatic screenshot in case this additional information provides some insight as to what GPAL is currently doing.
            /// </summary>
            public Image ScreenShot { get; set; }
            /// <summary>
            /// The browser driver instantiated and used to connect to the browser.
            /// </summary>
            public IWebDriver BrowserDriver { get; set; }
            /// <summary>
            /// The current Selector being used
            /// </summary>
            public Selector Selector { get; set; }
            /// <summary>
            /// The current web element
            /// </summary>
            public GPALElement GPALElement { get; set; }
            /// <summary>
            /// List of WebElements returned for the given Browser Selector
            /// </summary>
            public List<GPALElement> WebElements { get; set; }
            /// <summary>
            /// The current AutomationElement
            /// </summary>
            public AutomationElement AutomationElement { get; set; }
            /// <summary>
            /// List of AutomationElements returned for the given Application Selector
            /// </summary>
            public List<GPALAutomationElement> AutomationElements { get; set; }
            /// <summary>
            /// The SelectorPathEntry is the current path (css, xpath, text, image, etc) being evaluated/used to find/handle an element
            /// A selector may have multiple selector paths in order to evaluate/handle an element
            /// </summary>
            public SelectorSettings.SelectorPathEntry SelectorPathEntry { get; set; }
            /// <summary>
            /// GPALGrid [rows/columns] of tokens being used to fill input
            /// </summary>
            public IGPALGrid<string> Tokens { get; set; }
            /// <summary>
            /// List of Grids associated with GPALFile
            /// </summary>
            public List<IGPALGrid<string>> TokenList { get; set; }
            /// <summary>
            ///  SqlCommand which threw an exception
            /// </summary>
            public SqlCommand SqlCommand { get; set; }
            /// <summary>
            /// Any type of data that does not fite in the above, converter data, for example
            /// </summary>
            public dynamic Data { get; set; }
            /// <summary>
            /// The date/time this event is published
            /// </summary>
            public DateTime DateTimeStamp { get; set; }
        }

        internal static EventHandler<GPALEventArgs> ExceptionEventHandler { get; set; }
        internal static EventHandler<GPALEventArgs> InformationEventHandler { get; set; }
        #endregion <Exception & Information EventHandler>
        #region <Globals>
        /// <summary>
        /// Your Exception channel handler, if defined, GPAL will publish exceptions to this channel.<br/>
        /// <b>Used internally</b>
        /// </summary>
        /// <example>
        /// <code>
        /// // <b>NOTE:</b> If you wish to publish a simple message with the base GPALObject,<br/>
        /// // please use GPAL.PublishSimpleEvent
        /// GPAL.GPALEventArgs args = new GPAL.GPALEventArgs()
        /// {
        ///     Tokens = tokens,
        ///     DatabaseSettings = databaseSettings,
        ///     Message = $"Insert: Exception thrown: {ex.Message}",
        ///     ExceptionRaised = ex,
        ///     ScreenShot = GPAL.CaptureScreen()
        /// };
        /// GPAL.ExceptionHandler?.Invoke(tokens, args);
        /// </code>
        /// </example>
        internal static EventHandler<GPALEventArgs> ExceptionHandler
        {
            get
            {
                return ExceptionEventHandler;
            }
        }
        /// <summary>
        /// Your Information channel handler, if defined, GPAL will publish exceptions to this channel<br/>
        /// <b>Used internally</b>
        /// </summary>
        /// <example>
        /// <code>
        /// // <b>NOTE:</b> If you wish to publish a simple message with the base GPALObject only,<br/>
        /// // please use GPAL.PublishSimpleEvent
        /// GPAL.GPALEventArgs args = new GPAL.GPALEventArgs()
        /// {
        ///     GPALObject = application,
        ///     GPALObjectType = GPALObjectType.Application,
        ///     CurrentUOW = currentUOW,
        ///     Selector = selector,
        ///     Message = $"{GPAL.MyMethodName()}: Element has Zero Width and Height, skipping",
        ///     ScreenShot = GPAL.CaptureScreen()
        /// };
        /// GPAL.InformationHandler?.Invoke(application, args);
        /// </code>
        /// </example>
        internal static EventHandler<GPALEventArgs> InformationHandler
        {
            get
            {
                return InformationEventHandler;
            }
        }
        #endregion <Globals>
        #region <GPAL Object Getters>
        public static Cryptography Cryptography
        {
            [Component(Description = "Encrypt, decrypt, hash, sign and verify data", Submenu = "Security")] get => new Cryptography();
        }
        public static Base64Helper Base64Helper
        {
            [Component(Description = "Encode and decode Base64", Submenu = "Security")] get => new Base64Helper();
        }
        /// <summary>
        /// Entry point for the GPAL AI subsystem, providing a fluent interface for AI tasks.
        /// </summary>
        /// <example>
        /// <code>
        /// GPALForm form = GPAL.Form
        ///     .WithTitle("AI Classifier")
        ///     .WithFormControl(GPAL.Input.WithName("input").WithText("Enter text"))
        ///     .WithFormControl(GPAL.TextArea.WithName("output"))
        ///     .Show();
        ///
        /// GPAL.AI
        ///     .WithProvider(AIProviderType.XAI)
        ///     .WithTask(AITask.Classification)
        ///     .WithClassificationType(AIClassificationType.Sentiment)
        ///     .WithInputFrom(form.ControlList.OfType&lt;GPALInput&gt;().First())
        ///     .WithOutputTo(form.ControlList.OfType&lt;GPALTextArea&gt;().First())
        ///     .Execute();
        /// </code>
        /// </example>
        public static IAllowAIProvider AI
        {
            [Component(Description = "Entry point for the GPAL AI subsystem, providing a fluent interface for AI tasks", Submenu = "Services")] get
            {
                GPALAI ai = new GPALAI();
                AIList.Add(ai);
                return ai;
            }
        }

        public static IGPALYouTube YouTube { [Component(Description = "Upload videos to YouTube, manage playlists and search", Submenu = "Google")] get => new Browser.GPALYouTube(); }

        // OCR is on the roadmap: it registers the same way GPAL.Image does, and is left here until then
        //private static Func<OCRInterfaces.IGPALOCR> _ocrFactory;
        //[Action(Description = "Register the OCR provider from the GenerallyPositive.OCR package")]
        //public static void RegisterOCR(Func<OCRInterfaces.IGPALOCR> factory) => _ocrFactory = factory;
        //public static OCRInterfaces.IGPALOCR OCR { [Component(Description = "Read text from an image, once an OCR provider is registered", Submenu = "System")] get => _ocrFactory != null
        //    ? _ocrFactory()
        //    : throw new InvalidOperationException("OCR provider not registered. Add the GenerallyPositive.OCR NuGet and call GPAL.RegisterOCR(() => new GPALOCR())."); }

        // Image matcher from the GPAL.Image package, keeping OpenCV out of core. Without it, an image selector finds
        // nothing and publishes why.
        internal static IImageMatcher ImageMatcher { get; private set; }

        [Action(Description = "Register the image matcher from the GPAL.Image package, which image selectors need")]
        public static void RegisterImageMatcher(IImageMatcher imageMatcher) => ImageMatcher = imageMatcher;

        // QR code reader from the GPAL.Image package. Without it, WithOtpQRCode reads nothing and publishes why.
        internal static IQRCodeReader QRCodeReader { get; private set; }

        [Action(Description = "Register the QR code reader from the GPAL.Image package, which WithOtpQRCode needs")]
        public static void RegisterQRCodeReader(IQRCodeReader qrCodeReader) => QRCodeReader = qrCodeReader;

        internal static ImageHelper imageHelper;
        public static IImageHelper ImageHelper
        {
            [Component(Description = "Screenshots and image matching", Submenu = "System")] get
            {
                if (null == imageHelper)
                    imageHelper = new ImageHelper();
                return imageHelper;
            }
        }

        /// <summary>
        /// Instantiates a new fluent File SETTINGS object.<br/>
        /// This data object defines settings for use by the .FillInFrom method.
        /// </summary>
        /// <example>
        /// <code>
        /// File inputFile = (File)GPAL.File
        ///     .WithFileName(@"c:\ebay.words.txt");
        /// 
        /// Browser myBrowser = (Browser)GPAL.Browser
        ///     .WithBrowserType(BrowserType.Edge)
        ///     .WithDriverLocation(@"c:\drivers")
        ///     .GoTo("https://www.ebay.com/")
        ///     .WithWaitOnDocumentReady(true)
        ///     .WithSelector(searchSelector)
        ///     .CallAfterFillIn(CallAFterFillIn)   // after each row is consumed, invoke CallAfterFillIn
        ///     .FillInFrom(inputFile);      // after each row is consumed, invoke CallAfterFillIn
        /// </code>
        /// </example>
        /// <returns>Fluent interface to define your file input</returns>
        public static IAllowFileName File
        {
            [Component(Description = "A file, or several, for a step to read from or write to, such as FillInFrom or SaveTo", Submenu = "Data")]
            get
            {
                GPALFile file = new GPALFile();
                FileList.Add(file);
                return (IAllowFileName)file;
            }
        }
        /// <summary>
        ///  New GPALGrid<string></string> (rows/columns)
        /// </summary>
        public static IAllowGridActions<string> Grid
        {
            [Component(Description = "A grid of rows and columns that steps fill in from and save data into", Submenu = "Data")]
            get
            {
                IGPALGrid<string> grid = new GPALGrid<string>();
                GridList.Add(grid);
                return grid;
            }
        }
        /// <summary>
        ///  New GPAL Logger
        /// </summary>
        public static IAllowLoggingSettings Logger
        {
            [Component(Description = "A logger that records the events GPAL publishes", Submenu = "System")]
            get
            {
                IGPALLogger logger = (IGPALLogger)new GPALLogger();
                LoggerList.Add(logger);
                return (IAllowLoggingSettings)logger;
            }
        }
        /// <summary>
        ///  New GPAL Mail handler
        /// </summary>
        public static IAllowEmailServerSettings Mail
        {
            [Component(Description = "Send and receive email over SMTP, POP or IMAP", Submenu = "Services")]
            get
            {
                GPALMail mail = new GPALMail();
                MailList.Add(mail);
                return (IAllowEmailServerSettings)mail;
            }
        }
        /// <summary>
        /// New GPAL Convertor
        /// </summary>
        public static IAllowConverterInput Converter
        {
            [Fluent(Description = "Convert data from one shape to another: file, grid, database, class, and the CSV, JSON, XML, YAML and Excel formats")]
            get
            {
                GPALConverter converter = new GPALConverter();
                return (IAllowConverterInput)converter;
            }
        }        /// <summary>
                 /// Instantiates a new fluent Application object.
                 /// </summary>
                 /// <example>
                 /// <code>
                 /// Application application = (Application)GPAL.Application
                 ///     .WithParameters(@"C:\edit.this.file.txt")
                 ///     .Open(@"C:\Program File\Notepad++\notepad++.exe");
                 ///     
                 /// -or-
                 /// 
                 /// Application application = GPAL.Application
                 ///     .WithParameters(@"C:\edit.this.file.txt")
                 ///     .Open(@"C:\Program File\Notepad++\notepad++.exe")
                 ///     .ToGPALObject();
                 /// </code>
                 /// </example>
                 /// <returns>Fluent interface to create your workflow</returns>1
        public static IAllowApplicationOpenOrParameters Application
        {
            [Fluent(Description = "Start or attach to a desktop application and automate it")]
            get
            {
                tmpApplication = new Application.Application();
                tmpApplication.Name = applicationName + applicationCount++.ToString();
                ApplicationList.Add(tmpApplication);
                return tmpApplication;
            }
        }
        /// <summary>
        /// Instantiates a new fluent Browser object.
        /// </summary>
        /// <example>
        /// <code>
        /// Browser myBrowser = (Browser)GPAL.Browser
        ///     .WithBrowserType(BrowserType.Edge)
        ///     .WithDriverLocation(@"c:\drivers")
        ///     .GoTo("https://www.ebay.com/");
        ///     
        /// -or-
        /// 
        /// Browser myBrowser = GPAL.Browser
        ///     .WithBrowserType(BrowserType.Edge)
        ///     .WithDriverLocation(@"c:\drivers")
        ///     .GoTo("https://www.ebay.com/")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your workflow</returns>
        public static IAllowBrowserTypeOrGoto Browser
        {
            [Fluent(Description = "Start or attach to a browser and automate it on Selenium, Puppeteer or OttoMagic")]
            get
            {
                Browser.Browser made = new Browser.Browser();

                // Lock so concurrent workflows each get their own browser from the shared tmpBrowser field.
                lock (BrowserList)
                {
                    tmpBrowser = made;
                    made.Name = browserName + browserCount++.ToString();

                    CheckVersions();

                    // if we already have a browser and instantiate a new one, it MUST use the same automation engine as the first browser instantiated.
                    if (0 < BrowserList.Count)
                        made.WithAutomationEngine(BrowserList[0].AutomationEngine);

                    BrowserList.Add(made);
                }

                made.BrowserSettings.MagicHelper = new GenerallyPositive.Browser.MagicHelper(made);   // one helper per browser, talking to that browser's port
                return made;
            }
        }
        /// <summary>
        /// Instantiates a new fluent Database SETTINGS object.<br/>
        /// This data object defines settings for use by .FillInFrom method.
        /// </summary>
        /// <example>
        /// <code>
        /// // retrieve the top 4 rows from table TestInput from GPAL database on server "SQLSERVER\DATABASE"
        /// Database inputDatabase = (Database)GPAL.Database
        ///     .WithServerName(@"SQLSERVER\DATABASE")
        ///     .WithDatabaseName("GPAL")
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// 
        /// Browser myBrowser = (Browser)GPAL.Browser
        ///     .WithBrowserType(BrowserType.Edge)
        ///     .WithDriverLocation(@"c:\drivers")
        ///     .GoTo("https://www.ebay.com/")
        ///     .WithWaitOnDocumentReady(true)
        ///     .WithSelector(searchSelector)
        ///     .CallAfterFillIn(CallAFterFillIn)   // after each row is consumed, invoke CallAfterFillIn
        ///     .FillInFrom(inputDatabase);   // after each row is consumed, invoke CallAfterFillIn
        /// </code>
        /// </example>
        /// <returns>Fluent interface to define your database input</returns>
        /// <summary>
        /// The Windows clipboard. One per machine, so this is standalone rather than something hanging off a
        /// browser or an application.
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.Clipboard
        ///     .GetClipboard()
        ///     .SaveTo(out string text);
        ///
        /// GPAL.Clipboard
        ///     .WithInput(rows)
        ///     .SetClipboard();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to read or set the clipboard</returns>
        public static IAllowClipboardInput Clipboard
        {
            [Component(Description = "Read or set the Windows clipboard", Submenu = "Data")]
            get
            {
                GPALClipboard clipboard = new GPALClipboard();
                return (IAllowClipboardInput)clipboard;
            }
        }
        public static IAllowDatabaseSettings Database
        {
            [Component(Description = "Read from and write to a database table or stored procedure", Submenu = "Data")]
            get
            {
                GPALDatabase database = new GPALDatabase();
                return (IAllowDatabaseSettings)database;
            }
        }
        /// <summary>
        /// Instantiates a new fluent GPAL Selector object used to locate an element.<br/>
        /// Selectors are for browsers or applications.<br/>
        /// </summary>
        /// <example>
        /// <code>
        /// // Sample Application selector for the current tab writing surface in Notepad++
        /// Selector notepadPane = (Selector)GPAL.Selector
        ///     .WithName("Notepad Pane")
        ///     .WithXPath("/Window/Pane[1]")
        ///     .WithOffsetX(10)
        ///     .WithOffsetY(10);
        ///     
        /// // Sample Browser selector, selector paths are evaluated in order they are declared.
        /// // If at least one eleement is found for the selector path, then no other paths are evaluated.
        /// // This shows two paths, first CSS then XPath.
        /// Selector searchSelector = (Selector)GPAL.Selector
        ///     .WithName("SearchInput")
        ///     .WithCSS("#gh-ac")
        ///     .WithXPath("//*[@id=\"gh-ac\"]")
        ///     .MatchPlaceholder("Search for anything");   // must match this to be found
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your workflow</returns>
        public static IAllowSelectorSettings Selector
        {
            [Component(Description = "Describe how to find an element in a browser or an application", Submenu = "Selectors")]
            get
            {
                Selector tmpSelector = null;
                if (null != tmpBrowser)
                    tmpSelector = new Selector(tmpBrowser);
                else if (null != tmpApplication)
                    tmpSelector = new Selector(tmpApplication);
                else
                    tmpSelector = new Selector();
                tmpSelector.Name = selectorName + selectorCount++.ToString();
                SelectorList.Add(tmpSelector);
                return tmpSelector;
            }
        }
        /// <summary>
        /// Starts describing an API request for .Fetch to send from inside the page, using the browser's session.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALRequest productSearch = (GPALRequest)GPAL.Request
        ///     .WithPath("/api/products/search")
        ///     .WithHttpMethod("POST")
        ///     .WithParameter("page={page}")
        ///     .WithName("product search");
        /// </code>
        /// </example>
        /// <returns>Fluent interface to describe the request</returns>
        public static IAllowRequestSettings Request
        {
            [Component(Description = "Describe an API request for Fetch to send from inside the page, carrying the session the browser already has", Submenu = "Browser")]
            get
            {
                return new GPALRequest();
            }
        }
        public static IAllowExecutableSetup Launcher
        {
            [Component(Description = "Start a program or open a document, with arguments, and optionally read what it prints", Submenu = "System")]
            get
            {
                Launcher launcher = new Launcher
                {
                    Name = executableName + executableCount++.ToString()
                };
                return launcher;
            }

        }
        /// <summary>
        /// Instantiate a new fluent RESTClient
        /// </summary>
        /// <example>
        /// <code>
        ///     RESTClient client = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithEndpoint(ApiEndpoint.QuerySelectors)
        ///         .WithName("ButtonClient")
        ///         .WithParameters(new { selector = ".button" })
        ///         .WithHttpMethod("POST")
        ///         .ToGPALObject();
        ///     var elements = client.Execute&lt;List&lt;GPALElement&gt;&gt;(); // Logs "[ButtonClient][...]" if error
        /// 
        ///     // Reuse
        ///     var moreElements = client
        ///         .WithParameters(new { selector = ".link" })
        ///         .Execute&lt;List&lt;GPALElement&gt;&gt;();
        /// 
        ///     RESTClient client2 = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("TestClient")
        ///         .ToGPALObject();
        /// 
        ///     RESTClient client3 = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("TestClient")
        ///         .ToGPALObject();
        /// 
        ///     // EXAMPLES - not working code
        ///     // QuerySelector (CSS only)
        ///     var singleCss = client3.QuerySelector(".header")
        ///                            .Execute&lt;GPALElement&gt;(); // GET /query-selector with { "css": ".header" }
        /// 
        ///     // QuerySelectors (CSS only)
        ///     var allCss = client3.QuerySelectors(".sidebar-item")
        ///                         .Execute&lt;List&lt;GPALElement&gt;&gt;(); // GET /query-selectors with { "css": ".sidebar-item" }
        /// 
        ///     // Evaluate (XPath only)
        ///     var singleXPath = client3.Evaluate("//div[1]")
        ///                              .Execute&lt;GPALElement&gt;(); // GET /evaluate with { "xpath": "//div[1]" }
        /// 
        ///     // EvaluateAll (XPath only)
        ///     var allXPath = client3.EvaluateAll("//p")
        ///                           .Execute&lt;List&lt;GPALElement&gt;&gt;(); // GET /evaluate-all with { "xpath": "//p" }
        /// 
        ///     // Chaining with built-in methods, reusing results with names
        ///     RESTClient client4 = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("BuiltInChainClient")
        ///         .WithEndpoint(ApiEndpoint.Evaluate)
        ///         .WithXPath("//div[@class='product-name']")
        ///         .WithResultName("productName")
        ///         .AndThen&lt;string&gt;() // Result 0: "Laptop"
        ///     
        ///         .WithEndpoint(ApiEndpoint.FillInOverwrite)
        ///         .WithXPath("//input[@id='search']")
        ///         .WithTextFromResult("productName") // Use Result "productName": "Laptop"
        ///         .WithResultName("inputResult")
        ///         .AndThen() // Submits "Laptop" to the input field
        ///     
        ///         .WithEndpoint(ApiEndpoint.GetCurrentUrl)
        ///         .WithResultName("currentUrl")
        ///         .AndThen&lt;string&gt;() // Result 1: "http://localhost:3000/search?q=Laptop"
        ///     
        ///         .WithEndpoint(ApiEndpoint.Goto)
        ///         .WithUrlFromResult("currentUrl") // Use Result "currentUrl": "http://localhost:3000/search?q=Laptop"
        ///         .AndThen(); // Navigates to the URL
        ///     
        ///     var results = client4.GetExecutionResults();
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Product Name (Result 0): [{results[0]}]"); // "Laptop"
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Current URL (Result 1): [{results[1]}]"); // "http://localhost:3000/search?q=Laptop"
        /// 
        ///     // Chaining with JavaScript, reusing results with names
        ///     RESTClient client5 = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("ScriptClient")
        ///         .WithEndpoint(ApiEndpoint.ExecuteJavaScript)
        ///         .WithParameters("//div[@class='item']")
        ///         .WithScript("return Array.from(arguments[0]).map(el => el.id);") // Returns ["item1", "item2"]
        ///         .WithResultName("elementIds")
        ///         .AndThen&lt;List&lt;string&gt;&gt;()
        ///     
        ///         .WithEndpoint(ApiEndpoint.ExecuteJavaScript)
        ///         .WithParametersFromResult("elementIds") // Use Result "elementIds": ["item1", "item2"]
        ///         .WithScript("return arguments[0][1];") // Returns "item2"
        ///         .WithResultName("secondId")
        ///         .AndThen&lt;string&gt;()
        ///     
        ///         .WithEndpoint(ApiEndpoint.ExecuteJavaScript)
        ///         .WithParametersFromResult("elementIds") // Use Result "elementIds": ["item1", "item2"]
        ///         .WithScript("return arguments[0].length;") // Returns 2
        ///         .WithResultName("idCount")
        ///         .AndThen&lt;int&gt;();
        ///     
        ///     var results2 = client5.GetExecutionResults();
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Element IDs (Result 0): [{string.Join(", ", (List&lt;string&gt;)results2[0])}]"); // "item1, item2"
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Second ID (Result 1): [{results2[1]}]"); // "item2"
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Count of IDs (Result 2): [{results2[2]}]"); // 2
        /// 
        ///     // Mixing named and ordinal lookups
        ///     RESTClient client6 = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("MixedLookupClient")
        ///         .WithEndpoint(ApiEndpoint.Evaluate)
        ///         .WithXPath("//div[@class='product-name']")
        ///         .WithResultName("productName")
        ///         .AndThen&lt;string&gt;() // Result 0: "Laptop"
        ///     
        ///         .WithEndpoint(ApiEndpoint.ExecuteJavaScript)
        ///         .WithParametersFromResult("productName") // Use Result "productName": "Laptop"
        ///         .WithScript("return 'Search term: ' + arguments[0];") // Returns "Search term: Laptop"
        ///         .AndThen&lt;string&gt;() // Result 1 (unnamed)
        ///     
        ///         .WithEndpoint(ApiEndpoint.FillInOverwrite)
        ///         .WithXPath("//input[@id='search']")
        ///         .WithTextFromResult(1) // Use Result 1 (ordinal): "Search term: Laptop"
        ///         .AndThen(); // Submits "Search term: Laptop" to the input field
        ///     
        ///     var results3 = client6.GetExecutionResults();
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Product Name (Result 0): [{results3[0]}]"); // "Laptop"
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Script Result (Result 1): [{results3[1]}]"); // "Search term: Laptop"
        /// 
        ///     // Using Execute (non-chaining) and reusing results later
        ///     RESTClient client7 = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("NonChainingClient")
        ///         .WithEndpoint(ApiEndpoint.Evaluate)
        ///         .WithXPath("//div[@class='product-name']")
        ///         .WithResultName("productName");
        ///     var product = client7.Execute&lt;string&gt;(); // Result 0: "Laptop", stored in _executionResults
        ///     
        ///     client7.WithEndpoint(ApiEndpoint.FillInOverwrite)
        ///            .WithXPath("//input[@id='search']")
        ///            .WithTextFromResult("productName") // Use Result "productName": "Laptop"
        ///            .WithResultName("inputResult")
        ///            .Execute(); // Submits "Laptop" to the input field
        ///     
        ///     var results4 = client7.GetExecutionResults();
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Product Name (Result 0): [{results4[0]}]"); // "Laptop"
        ///     
        /// // Can also use your own endpoint names (string)
        ///     RESTClient client = GPAL.RESTClient
        ///         .WithAPIBase(browser.RestApiBaseUrl)
        ///         .WithName("BuiltInChainClient")
        ///         .WithEndpoint("evaluate")
        ///         .WithXPath("//div[@class='product-name']")
        ///         .WithResultName("productName")
        ///         .AndThen&lt;string&gt;() // Result 0: "Laptop"
        ///     
        ///         .WithEndpoint("input-text")
        ///         .WithXPath("//input[@id='search']")
        ///         .WithTextFromResult("productName") // Use Result "productName": "Laptop"
        ///         .WithResultName("inputResult")
        ///         .AndThen() // Submits "Laptop" to the input field
        ///     
        ///         .WithEndpoint("get-current-url")
        ///         .WithResultName("currentUrl")
        ///         .AndThen&lt;string&gt;() // Result 1: "http://localhost:3000/search?q=Laptop"
        ///     
        ///         .WithEndpoint("goto")
        ///         .WithUrlFromResult("currentUrl") // Use Result "currentUrl": "http://localhost:3000/search?q=Laptop"
        ///         .AndThen(); // Navigates to the URL
        ///     
        ///     var results = client.GetExecutionResults();
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Product Name (Result 0): [{results[0]}]"); // "Laptop"
        ///     GPAL.PublishSimpleEvent(GPALEventType.NOTICE, $"Current URL (Result 1): [{results[1]}]"); // "http://localhost:3000/search?q=Laptop"
        /// </code>
        /// </example>
        public static IAllowRESTEndpoint RESTClient
        {
            [Component(Description = "Call a REST API: base url, endpoint, parameters and credentials", Submenu = "Services")]
            get
            {
                RESTClient restClient = new RESTClient
                {
                    Name = restClientName + restClientCount++.ToString()
                };
                return restClient;
            }
        }
        /// <summary>
        /// Return a new credential manager
        /// </summary>
        public static IAllowCredentialServiceType Credentials
        {
            [Component(Description = "Credentials from a password manager, an API key or an OAuth provider", Submenu = "Security")]
            get => new Credentials((Browser.Browser)Browser);   // TODO: CAVEAT: BUG: implement correct, this browser is a placeholder
        }
        public static IAllowGiven Gherkin
        {
            [Component(Description = "Write a workflow as Given, When and Then steps", Submenu = "System")]
            get => new Gherkin();
        }
        /// <summary>
        /// New Googlesheets workflow
        /// </summary>
        public static IAllowSpreadsheetSelection GoogleSheets
        {
            [Component(Description = "Read and write a Google Sheets spreadsheet", Submenu = "Google")]
            get => new GoogleSheets();
        }
        /// <summary>
        /// New GoogleDrive workflow
        /// </summary>
        public static IAllowDriveCredentials GoogleDrive
        {
            [Component(Description = "Work with files in Google Drive", Submenu = "Google")]
            get => new GoogleDrive();
        }
        public static IGPALExcel Excel
        {
            [Component(Description = "Read and write an Excel workbook", Submenu = "Data")]
            get => new GPALExcel();
        }
        public static IAllowGPALUrlForUrl Url
        {
            [Component(Description = "A url, and the website storage on it to read or change", Submenu = "Browser")]
            get => new GPALUrl(null);
        }

        #region Internal GPAL factory for ActivatorHelper
        internal static GPALElement Element
        {
            get => new GPALElement();
        }
        internal static GPALFileSettings FileSettings
        {
            get => new GPALFileSettings();
        }
        internal static GPALMailSettings MailSettings
        {
            get => new GPALMailSettings();
        }
        internal static GPALSettings Settings
        {
            get => new GPALSettings();
        }
		#endregion Internal GPAL factory for ActivatorHelper
		#endregion <GPAL Objects>
		#region Shorthand Helpers
		[Component(Description = "An Excel workbook for the given file", Submenu = "Data")]
		public static IGPALExcel ExcelFor(GPALFile excelFile)
		{
			return new GPALExcel().WithFile(excelFile).ToGPALObject();
		}

		[Component(Description = "Credentials for the given service", Submenu = "Security")]
        public static ICredentials CredentialsFor(CredentialServiceType credentialServiceType)
        {
            return new Credentials((Browser.Browser)Browser).WithService(credentialServiceType).ToGPALObject();
        }
        [Component(Description = "A Google Sheets spreadsheet, opened by name or id", Submenu = "Google")]
        public static IGoogleSheets GoogleSheetsFor(string spreadsheetName)
        {
            return new GoogleSheets().WithSpreadsheet(spreadsheetName).ToGPALObject();
        }
        [Component(Description = "Google Drive, signed in with the given credentials", Submenu = "Google")]
        public static IGoogleDrive GoogleDriveFor(ICredentials credentials)
        {
            return new GoogleDrive().WithCredentials(credentials).ToGPALObject();
        }
        [Component(Description = "A grid whose rows are objects of the given class", Submenu = "Data")]
        public static IGPALGrid<T> GridForType<T>()
        {
            return new GPALGrid<T>();
        }
        /// <summary>
        /// Helper for shorthand notation
        /// </summary>
        /// <code>
        /// <example>
        /// GPAL.Browser
        ///     .WithBrowserType(BrowserType.Chrome)
        ///     .Get("ebay.com")
        ///     .LeftClick(GPAL.CssSelector("#gh-search-btn"))
        ///     .Close(true);
        /// </example>
        /// </code>
        /// <param name="css"></param>
        /// <param name="selectorName">[Optional] Name given to the selector</param>
        /// <returns></returns>
        [Component(Description = "A selector that finds an element by CSS, optionally named", Submenu = "Selectors")]
        public static Selector CssSelector(string css, string selectorName = null)
        {
            if (null != selectorName)
                return Selector.WithCSS(css).WithSelectorName(selectorName).ToGPALObject();
            else
                return Selector.WithCSS(css).ToGPALObject();
        }

        [Component(Description = "A selector that finds an element by XPath, optionally named", Submenu = "Selectors")]
        public static Selector XPathSelector(string xpath, string selectorName = null)
        {
            if (null != selectorName)
                return Selector.WithXPath(xpath).WithSelectorName(selectorName).ToGPALObject();
            else
                return Selector.WithXPath(xpath).ToGPALObject();
        }
        [Component(Description = "A GPALFile for the given path", Submenu = "Data")]
        public static GPALFile FileFor([FilePath] string path)
        {
            return File.WithFileName(path).ToGPALObject();
        }
        // New browser shorthand methods
        [Component(Description = "Start a browser with no window on the given url", Submenu = "Browser")]
        public static IBrowser BrowserGet(GPALUrl url)
        {
            return Browser
                .Get(url) // Headless navigation
                .ToGPALObject();

        }
        [Component(Description = "Start a browser window on the given url", Submenu = "Browser")]
        public static IBrowser BrowserGoto(GPALUrl url)
        {
            return Browser
                .GoTo(url) // Windowed navigation
                .ToGPALObject();
        }
        [Component(Description = "A button with the given text, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALButton ButtonFor(string text)
        {
            var c = new GPALButton { Name = buttonName + controlCount++.ToString() };
            c.IsDefault = false;
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A label with the given text, for a GPAL form", Submenu = "Form Controls/Text")]
        public static GPALLabel LabelFor(string text)
        {
            var c = new GPALLabel { Name = labelName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "Empty space this many pixels tall between two controls on a GPAL form", Submenu = "Form Controls/Layout")]
        public static GPALSpacer SpacerFor([Pixels] int pixels)
        {
            var c = new GPALSpacer { Name = spacerName + controlCount++.ToString() };
            c.Height = pixels;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A link for a GPAL form that opens this address in the default browser, showing the address as its text", Submenu = "Form Controls/Text")]
        public static GPALLinkLabel LinkLabelFor([URL] string url)
        {
            var c = new GPALLinkLabel { Name = linkLabelName + controlCount++.ToString() };
            c.Url = url;
            c.Text = url;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A one-line input box for a GPAL form, optionally with starting text", Submenu = "Form Controls/Text")]
        public static GPALInput InputFor(string text = "")
        {
            var c = new GPALInput { Name = inputName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A multi-line text box for a GPAL form, optionally with starting text", Submenu = "Form Controls/Text")]
        public static GPALTextArea TextAreaFor(string text = "")
        {
            var c = new GPALTextArea { Name = textAreaName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A rich text box for a GPAL form that renders Markdown, optionally with starting text", Submenu = "Form Controls/Text")]
        public static GPALRichTextBox RichTextBoxFor(string text = "")
        {
            var c = new GPALRichTextBox { Name = richTextBoxName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A checkbox with the given text, for a GPAL form, checked or not", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALCheckbox CheckboxFor(string text, bool isChecked = false)
        {
            var c = new GPALCheckbox { Name = checkboxName + controlCount++.ToString() };
            c.Text = text;
            c.IsChecked = isChecked;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A radio button with the given text, for a GPAL form, selected or not", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALRadioButton RadioButtonFor(string text, bool isChecked = false)
        {
            var c = new GPALRadioButton { Name = radioButtonName + controlCount++.ToString() };
            c.Text = text;
            c.IsChecked = isChecked;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A tab with the given title, for a GPAL form", Submenu = "Form Controls/Layout")]
        public static GPALTab TabFor(string text)
        {
            var c = new GPALTab { Name = radioButtonName + controlCount++.ToString() };
            c.Text = text;
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A chart for a GPAL form, optionally with a title", Submenu = "Form Controls/Lists and Data")]
        public static GPALChart ChartFor(string title = "")
        {
            var c = new GPALChart { Name = chartName + controlCount++.ToString() };
            if (!string.IsNullOrEmpty(title)) c.WithTitle(title);
            ControlList.Add(c);
            return c;
        }
        [Component(Description = "A drop-down list with the given items, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALComboBox ComboBoxFor(params string[] items)
        {
            var c = new GPALComboBox { Name = "ComboBox" + controlCount++.ToString() };
            if (items != null && items.Length > 0) c.WithItems(items);
            return c;
        }
        [Component(Description = "A data grid with the given columns, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALDataGridView DataGridViewFor(params string[] columns)
        {
            var c = new GPALDataGridView { Name = "DataGridView" + controlCount++.ToString() };
            if (columns != null && columns.Length > 0) c.WithColumns(columns);
            return c;
        }
        [Component(Description = "A list view with the given columns, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALListView ListViewFor(params string[] columns)
        {
            var c = new GPALListView { Name = "ListView" + controlCount++.ToString() };
            if (columns != null && columns.Length > 0) c.WithColumns(columns);
            return c;
        }
        [Component(Description = "A picture box showing the given image file, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALPicture PictureFor(GPALFile file)
        {
            var c = new GPALPicture { Name = "Picture" + controlCount++.ToString() };
            c.WithImage(file);
            return c;
        }
        [Component(Description = "A slider from 0 to the given maximum, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALSlider SliderFor(int maximum = 100)
        {
            var c = new GPALSlider { Name = "Slider" + controlCount++.ToString() };
            c.WithMaximum(maximum);
            return c;
        }
        [Component(Description = "A progress bar from 0 to the given maximum, for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALProgressBar ProgressBarFor(int maximum = 100)
        {
            var c = new GPALProgressBar { Name = "ProgressBar" + controlCount++.ToString() };
            c.WithMaximum(maximum);
            return c;
        }
        [Component(Description = "A status strip with the given text, for a GPAL form", Submenu = "Form Controls/Menus and Bars")]
        public static GPALStatusStrip StatusStripFor(string text = "Ready")
        {
            var c = new GPALStatusStrip { Name = "StatusStrip" + controlCount++.ToString() };
            c.Text = text;
            return c;
        }
        [Component(Description = "A number box held between the given minimum and maximum, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALNumericUpDown NumericUpDownFor(decimal minimum = 0, decimal maximum = 100)
        {
            var c = new GPALNumericUpDown { Name = "NumericUpDown" + controlCount++.ToString() };
            c.WithMinimum(minimum);
            c.WithMaximum(maximum);
            return c;
        }
        [Component(Description = "A date and time picker for a GPAL form, optionally set to a value", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALDateTimePicker DateTimePickerFor(DateTime? value = null)
        {
            var c = new GPALDateTimePicker { Name = "DateTimePicker" + controlCount++.ToString() };
            if (value.HasValue) c.WithValue(value.Value);
            return c;
        }
        [Component(Description = "An empty tree view for a GPAL form", Submenu = "Form Controls/Lists and Data")]
        public static GPALTreeView TreeViewFor()
        {
            return new GPALTreeView { Name = "TreeView" + controlCount++.ToString() };
        }
        [Component(Description = "A file or folder picker with the given dialog title, for a GPAL form", Submenu = "Form Controls/Buttons and Choices")]
        public static GPALFileSelector FileSelectorFor(string title = "Select File/Folder")
        {
            var c = new GPALFileSelector { Name = "FileSeleector" + controlCount++.ToString() };
            c.WithTitle(title);
            return c;
        }
        [Component(Description = "A layout panel with the given columns and rows, for a GPAL form", Submenu = "Form Controls/Layout")]
        public static GPALTableLayoutPanel TableLayoutPanelFor(int columns = 1, int rows = 1)
        {
            var c = new GPALTableLayoutPanel { Name = "TableLayoutPanel" + controlCount++.ToString() };
            c.WithColumnCount(columns);
            c.WithRowCount(rows);
            return c;
        }
        #endregion Shorthand Helpers
        #region <ElementAssitant>
        /// <summary>
        /// Instantiates a new Browser ElementAssistant object with fluent methods to help you manipulate WebElements in Callback EventHandlers.
        /// </summary>
        /// <example>
        /// <code>
        /// public static int CallIfFound(Browser browser, List&lt;GPALElement&gt; foundElements, List&lt;GPALElement&gt; matchedElements, Selector selector)
        /// {
        ///     elementAssistant = (ElementAssistant) GPAL.ElementAssistant(browser);
        /// 
        ///     foreach (GPALElement webElement in matchedElements)
        ///     {
        ///         elementAssistant
        ///             .ForSelector(selector)  // for interaction type, offsetX and offsetY
        ///             .WithElement(webElement)
        ///             .WithJavscript  // can override the selector interaction type here for this one element
        ///             .LeftClick();
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <param name="selector">Selector whose elements the callback works with</param>
        /// <returns>Fluent GPALElement assistant to use in callback EventHandler</returns>
        [ElementAssitant(Description = "Work with the elements a browser selector found, from inside a callback")]
        public static Browser.IAllowElementSetting ElementAssistant(Selector selector)
        {
            elementAssistant.Browser = selector.Browser;
            elementAssistant.ForSelector(selector);
            return elementAssistant;
        }
        /// <summary>
        /// Instantiates a new Application ElementAssistant with fluent methods to help you manipulate AutomationElements in callback EventHandlers.
        /// </summary>
        /// <example>
        /// <code>
        /// // <b>CallIfFound</b> handler
        /// public static int FoundNotepadPane(Application application, List&lt;GPALAutomationElement&gt; foundElements, List&lt;GPALAutomationElement&gt; matchedElements, Selector selector)
        /// {
        ///     ElementAssistant elementAssistant = (ElementAssistant) GPAL.ElementAssistant(application);
        /// 
        ///     foreach (GPALAutomationElement element in foundElements)
        ///     {
        ///         elementAssistant
        ///             .ForSelector(selector)  // for interaction type, offsetX and offsetY
        ///             .WithElement(element)
        ///             .WithHardware  // can override the selector interaction type here for this one element
        ///             .LeftClick();
        ///     }
        ///     return 0;
        /// }
        /// </code>
        /// </example>
        /// <param name="application">Application object associated with the elements/callback</param>
        /// <returns>Fluent AutomationElement assistant to use in callback EventHandler</returns>
        [ElementAssitant(Description = "Work with the elements an application selector found, from inside a callback")]
        public static Application.IAllowForSelector ElementAssistant(Application.Application application)
        {
            applicationElementAssistant.Application = application;
            return applicationElementAssistant;
        }
        #endregion <ElementAssitant>
        #region <Form Controls>
        /// <summary>
        /// Instantiates a new GPALForm for creating simple interactive user interfaces.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
        ///     .WithName("button1")
        ///     .WithText("Exit")
        ///     .WithCallback&lt;EventHandler&gt;(ExitButtonClickHandler)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);  // Alt-E
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalButton1)
        ///         .ShowDialog();
        /// }
        ///     
        /// private static void ExitButtonClickHandler(object sender, EventArgs e)
        /// {
        ///     Control control = (Control)sender;
        ///     
        ///     while (null != control.Parent)
        ///         control = control.Parent;
        ///     control.Hide();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your GPAL GUI</returns>
        public static IAllowFormSettings Form
        {
            [Fluent(Description = "Build a form of controls to give a workflow a user interface")]
            get
            {
                GPALForm.GPALForm tmpForms = new GPALForm.GPALForm()
                {
                    FormName = formName + formCount++.ToString(),
                };
                FormList.Add(tmpForms);
                return tmpForms;
            }
        }
        /// <summary>
        /// Instantiates a new GPALButton for use on GPAL forms.<br/>
        /// Callback EventHandler is invoked on the GPALButton.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALButton gPalButton1 = (GPALButton)GPAL.Button
        ///     .Default
        ///     .WithName("button1")
        ///     .WithText("Exit")
        ///     .WithCallback&lt;EventHandler&gt;(ExitButtonClickHandler)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.E);  // Alt-E
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalButton1)
        ///         .ShowDialog();
        /// }
        ///     
        /// private static void ExitButtonClickHandler(object sender, EventArgs e)
        /// {
        ///     Control control = (Control)sender;
        ///     
        ///     while (null != control.Parent)
        ///         control = control.Parent;
        ///     control.Hide();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndDefault Button
        {
            [Forms(Description = "A button for a GPAL form, calling back when it is clicked", Submenu = "Buttons and Choices")]
            get
            {
                GPALButton tmpControl = new GPALButton
                {
                    Name = buttonName + controlCount++.ToString(),
                    IsDefault = false
                };
                //CAVEAT: very strange bug, isdefault keeps getting set to true
                tmpControl.IsDefault = false;
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALChart for use on GPAL forms.<br/>
        /// Callback EventHandler is invoked on the GPALChart.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALChart gPalChart = (GPALChart)GPAL.Chart
        ///     .WithPalette(ChartColorPalette.Bright)
        ///     .WithCallback&lt;EventHandler&gt;(ChartHandler) // invoked when chart gets clicked 
        ///     .WithName("Chart 1");
        ///     
        /// static void Main(string[] args)
        /// {
        ///     GenerateBarSeries();
        ///     
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalChart)
        ///         .ShowDialog();
        /// }
        ///     
        /// private static void GenerateBarSeries()
        /// {
        ///     string[] seriesArray = { "Cat", "Dog", "Bird", "Monkey" };
        ///     int[] pointsArray = { 2, 1, 7, 5 };
        ///     
        ///     gPalChart.Titles.Add("Animals");
        ///     // Add series
        ///     for (int i = 0; i &lt; seriesArray.Length; i++)
        ///     {
        ///         Series series = gPalChart.Series.Add(seriesArray[i]);
        ///         // series.ChartType = SeriesChartType.Bar;
        ///         series.Points.Add(pointsArray[i]);
        ///         Legend legend = new Legend(seriesArray[i]);
        ///         // legend.Alignment = StringAlignment.Near;
        ///         gPalChart.Legends.Add(legend);
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndChartSettings Chart
        {
            [Forms(Description = "A chart for a GPAL form", Submenu = "Lists and Data")]
            get
            {
                GPALChart tmpControl = new GPALChart
                {
                    Name = chartName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALCheckbox for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALCheckbox.WindowsControl.CheckChanged event.<br/>
        /// Default is not checked
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALCheckbox gPALCheckbox1 = (GPALCheckbox)GPAL.Checkbox
        ///     .WithChecked(true)
        ///     .WithText("Overwrite File")
        ///     .WithCallback&lt;EventHandler&gt;(CheckboxHandler); // on check changed event handler
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPALCheckbox1)
        ///         .ShowDialog();
        /// }
        /// 
        /// private static void CheckboxHandler(object sender, EventArgs e)
        /// {
        ///     if (((CheckBox) sender).Checked)
        ///     {
        ///          gPALCheckbox2.Checked = false; // uncheck the other, to act like a radio button
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndChecked Checkbox
        {
            [Forms(Description = "A checkbox for a GPAL form, calling back when it is checked or cleared", Submenu = "Buttons and Choices")]
            get
            {
                GPALCheckbox tmpControl = new GPALCheckbox
                {
                    Name = checkboxName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALComboBox for use on a GPAL form.<br/>
        /// Callback(s) set via WhenSelectedDo are invoked on the underlying ComboBox.SelectedIndexChanged event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALComboBox browserSelector = (GPALComboBox)GPAL.ComboBox
        ///     .WithName("BrowserChoice")
        ///     .WithItems("Chrome", "Edge", "Firefox")
        ///     .WithSelectedIndex(0)
        ///     .WhenSelectedDo(selected => 
        ///     {
        ///         Debug.WriteLine($"Selected browser: {selected}");
        ///         // browserType = (BrowserType)Enum.Parse(typeof(BrowserType), selected);
        ///     })
        ///     .WithToolTip("Choose target browser for automation")
        ///     .ToGPALObject();
        ///
        /// // later in form building:
        /// GPALForm myForm = (GPALForm)GPAL.Form
        ///     .WithTitle("Automation Setup")
        ///     .WithFormControl(browserSelector)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the combo box</returns>
        public static IAllowComboBoxControlSettings ComboBox
        {
            [Forms(Description = "Instantiates a new GPALComboBox for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALComboBox tmpControl = new GPALComboBox
                {
                    Name = "ComboBox" + controlCount++.ToString()   // or use your naming scheme
                };
                // ControlList.Add(tmpControl);   ← usually done in WithFormControl / AddComboBox
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALDataGridView for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALDataGridView.WindowsControl.CellValueChanged event (or other events when specified via .ForEvent).
        /// Supports tabular data display with columns and optional editing.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALDataGridView logGrid = (GPALDataGridView)GPAL.DataGridView
        ///     .WithName("LogGrid")
        ///     .WithColumns("Time", "Level", "Message")
        ///     .WithCallback&lt;DataGridViewCellEventHandler&gt;((s, e) => Debug.WriteLine("Cell changed"))
        ///     .WithToolTip("Execution log and results")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the data grid view</returns>
        public static IAllowDataGridViewControlSettings DataGridView
        {
            [Forms(Description = "Instantiates a new GPALDataGridView for use on a GPAL form", Submenu = "Lists and Data")] get
            {
                GPALDataGridView tmp = new GPALDataGridView
                {
                    Name = "DataGridView" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// Instantiates a new GPALDateTimePicker for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALDateTimePicker.WindowsControl.ValueChanged event (or other events when specified via .ForEvent).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALDateTimePicker startTime = (GPALDateTimePicker)GPAL.DateTimePicker
        ///     .WithName("StartTime")
        ///     .WithValue(DateTime.Now)
        ///     .WithFormat(DateTimePickerFormat.Custom)
        ///     .WithCustomFormat("yyyy-MM-dd HH:mm")
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine($"Date changed to {((DateTimePicker)s).Value}"))
        ///     .WithToolTip("Select start date and time")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the date/time picker</returns>
        public static IAllowDateTimePickerControlSettings DateTimePicker
        {
            [Forms(Description = "Instantiates a new GPALDateTimePicker for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALDateTimePicker tmp = new GPALDateTimePicker
                {
                    Name = "DateTimePicker" + controlCount++.ToString()
                };
                return tmp;
            }
        }
        /// <summary>
        /// Instantiates a new GPALFileSelector for use on a GPAL form.<br/>
        /// Opens a standard file or folder selection dialog when clicked.<br/>
        /// Callback EventHandler is invoked after a selection is made (or canceled), with FileSelectorEventArgs containing the selected path(s) and cancellation status.<br/>
        /// Selected path(s) available via SelectedPath / SelectedPaths properties.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALFileSelector driverPicker = (GPALFileSelector)GPAL.FileSelector
        ///     .WithName("WebDriverPath")
        ///     .WithTitle("Select WebDriver Folder")
        ///     .WithInitialDirectory(@"C:\")
        ///     .WithSelectFolder(true)  // true = folder browser, false = file dialog
        ///     .WithFilter("Executables (*.exe)|*.exe|All files (*.*)|*.*")
        ///     .WithMultiSelect(false)
        ///     .WithCallback&lt;EventHandler&gt;((s, args) =>
        ///     {
        ///         if (args.Cancelled)
        ///         {
        ///             Debug.WriteLine("Selection cancelled");
        ///         }
        ///         else
        ///         {
        ///             Debug.WriteLine($"Selected: {args.SelectedPath}");
        ///             if (args.SelectedPaths.Count > 1)
        ///             {
        ///                 Debug.WriteLine($"Multi-select: {string.Join(", ", args.SelectedPaths)}");
        ///             }
        ///         }
        ///     })
        ///     .WithToolTip("Click to browse for ChromeDriver / geckodriver folder")
        ///     .ToGPALObject();
        ///
        /// // later in form building:
        /// GPALForm myForm = (GPALForm)GPAL.Form
        ///     .WithTitle("File Selection Test")
        ///     .WithFormControl(driverPicker)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the file/folder selector</returns>
        public static IAllowFileSelectorControlSettings FileSelector
        {
            [Forms(Description = "Instantiates a new GPALFileSelector for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALFileSelector gfs = new GPALFileSelector
                {
                    Name = "FileSeleector" + controlCount++.ToString()
                };
                return gfs;
            }
        }
        /// <summary>
        /// Instantiates a new one-line GPALInput control for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALInput.WindowsControl.TextChanged event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALInput gPalInput = (GPALInput)GPAL.Input
        ///     .WithText(@"c:\drivers")
        ///     .WithCallback&lt;EventHandler&gt;(InputHandler); // on text changed event handler
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalInput)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsPlaceholderAndPassword Input
        {
            [Forms(Description = "A one-line input box for a GPAL form, calling back when its text changes", Submenu = "Text")]
            get
            {
                GPALInput tmpControl = new GPALInput
                {
                    Name = inputName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALLinkLabel for use on a GPAL form: a link that opens its address in the default
        /// browser when clicked.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALLinkLabel moreBots = (GPALLinkLabel)GPAL.LinkLabel
        ///     .WithUrl("https://gpal.sditx.com/docs/botw")
        ///     .WithText("More Bots of the Week");
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndUrl LinkLabel
        {
            [Forms(Description = "A link for a GPAL form that opens an address in the default browser", Submenu = "Text")]
            get
            {
                GPALLinkLabel tmpControl = new GPALLinkLabel
                {
                    Name = linkLabelName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALLabel for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALLabel.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALLabel gPalLabel = (GPALLabel)GPAL.Label
        ///     .WithText("Form Tester");
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalLabel)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndFontSettings Label
        {
            [Forms(Description = "A label for a GPAL form", Submenu = "Text")]
            get
            {
                GPALLabel tmpControl = new GPALLabel
                {
                    Name = labelName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALListView for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALListView.WindowsControl.SelectedIndexChanged event (or other events when specified via .ForEvent).<br/>
        /// Supports columns, different view modes (Details, List, etc.), selection, and double-click activation.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALListView fileResults = GPAL.ListView
        ///     .WithName("SavedFiles")
        ///     .WithColumns("Filename", "Size (KB)", "Date")
        ///     .WithView(ListViewMode.Details)
        ///     .WithFullRowSelect(true)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Selection changed"))
        ///     .ForEvent(ControlEventType.SelectedIndexChanged)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Item activated"))
        ///     .ForEvent(ControlEventType.DoubleClick)
        ///     .WithToolTip("List of processed or downloaded files")
        ///     .ToGPALObject();
        ///
        /// // later in form building:
        /// GPALForm myForm = (GPALForm)GPAL.Form
        ///     .WithTitle("File Results")
        ///     .WithFormControl(fileResults)
        ///     .ShowDialog();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the list view</returns>
        public static IAllowListViewControlSettings ListView
        {
            [Forms(Description = "Instantiates a new GPALListView for use on a GPAL form", Submenu = "Lists and Data")] get
            {
                GPALListView tmp = new GPALListView
                {
                    Name = "ListView" + controlCount++.ToString()
                };
                return tmp;
            }
        }
        /// <summary>
        /// Instantiates a new GPALNumericUpDown for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALNumericUpDown.WindowsControl.ValueChanged event (or other events when specified via .ForEvent).
        /// Useful for selecting numeric values such as timeouts, retry counts, ports, etc.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALNumericUpDown timeoutSelector = (GPALNumericUpDown)GPAL.NumericUpDown
        ///     .WithName("TimeoutSeconds")
        ///     .WithMinimum(0)
        ///     .WithMaximum(300)
        ///     .WithValue(30)
        ///     .WithIncrement(5)
        ///     .WithDecimalPlaces(0)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine($"Timeout changed to {((NumericUpDown)s).Value}"))
        ///     .WithToolTip("Maximum wait time in seconds")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the numeric up/down control</returns>
        public static IAllowNumericUpDownControlSettings NumericUpDown
        {
            [Forms(Description = "Instantiates a new GPALNumericUpDown for use on a GPAL form", Submenu = "Buttons and Choices")] get
            {
                GPALNumericUpDown tmp = new GPALNumericUpDown
                {
                    Name = "NumericUpDown" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A picture control for GPAL forms.<br/>
        /// Shows an image, from a file or from an Image already in hand.<br/>
        /// The image can be replaced while the form is up, and the control repaints itself.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALPicture shot = (GPALPicture)GPAL.Picture
        ///     .WithImage("screenshot.png")
        ///     .WithSizeMode(PictureBoxSizeMode.Zoom)
        ///     .WithHeight(300)
        ///     .WithName("shot")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowPictureControlSettings Picture
        {
            [Forms(Description = "A picture control for GPAL forms", Submenu = "Lists and Data")] get
            {
                GPALPicture tmp = new GPALPicture
                {
                    Name = "Picture" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A slider control for GPAL forms.<br/>
        /// Picks a number from a range by dragging, and exposes that range to UIAutomation, which is what a
        /// workflow drives with SetRange.<br/>
        /// Callback (if set) is invoked on the ValueChanged event of the underlying TrackBar.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALSlider volume = (GPALSlider)GPAL.Slider
        ///     .WithMinimum(0)
        ///     .WithMaximum(100)
        ///     .WithValue(50)
        ///     .WithTickFrequency(10)
        ///     .WithName("volume")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowSliderControlSettings Slider
        {
            [Forms(Description = "A slider control for GPAL forms", Submenu = "Buttons and Choices")] get
            {
                GPALSlider tmp = new GPALSlider
                {
                    Name = "Slider" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A progress bar control for GPAL forms.<br/>
        /// Displays task completion percentage or indeterminate (marquee) activity.<br/>
        /// Callback (if set) is invoked on the ValueChanged event of the underlying ProgressBar.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALProgressBar downloadProgress = (GPALProgressBar)GPAL.ProgressBar
        ///     .WithName("DownloadProgress")
        ///     .WithMinimum(0)
        ///     .WithMaximum(100)
        ///     .WithValue(0)
        ///     .WithStyle(ProgressBarStyle.Continuous)
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Progress updated"))
        ///     .WithToolTip("Current task progress")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowProgressBarControlSettings ProgressBar
        {
            [Forms(Description = "A progress bar control for GPAL forms", Submenu = "Lists and Data")] get
            {
                GPALProgressBar tmp = new GPALProgressBar
                {
                    Name = "ProgressBar" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// Instantiates a new GPALRadioButton for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALRadioButton.WindowsControl.CheckChanged event.<br/>
        /// Default is not selected
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALRadioButton gPalRadioButton1 = (GPALRadioButton)GPAL.RadioButton
        ///     .WithChecked(true)
        ///     .WithName("Radiobutton1")
        ///     .WithText("Chrome")
        ///     .WithCallback&lt;EventHandler&gt;(RadioButton1Handler); // on check changed event handler
        ///     
        /// static GPALRadioButton gPalRadioButton2 = (GPALRadioButton)GPAL.RadioButton
        ///     .WithName("Radiobutton2")
        ///     .WithText("Edge")
        ///     .WithCallback&lt;EventHandler&gt;(RadioButton2Handler); // on check changed event handler
        ///     
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalRadioButton1)
        ///         .WithFormControl(gPalRadioButton2)
        ///         .ShowDialog();
        /// }
        /// 
        /// private static void RadioButtonHandler(object sender, EventArgs e)
        /// {
        ///     if (((CheckBox) sender).Checked)
        ///     {
        ///         // YOUR CODE HERE - you don't have to thave two handlers, you could have one
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndChecked RadioButton
        {
            [Forms(Description = "A radio button for a GPAL form, calling back when it is selected", Submenu = "Buttons and Choices")]
            get
            {
                GPALRadioButton tmpControl = new GPALRadioButton
                {
                    Name = radioButtonName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// A status strip control (docked at the bottom) for GPAL forms.<br/>
        /// Displays real-time status messages, browser info, file counts, etc.<br/>
        /// Callback (if set) is invoked on the primary StatusStrip event (usually ItemClicked or similar).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALStatusStrip appStatus = (GPALStatusStrip)GPAL.StatusStrip
        ///     .WithName("MainStatus")
        ///     .WithText("Ready")
        ///     .WithCallback&lt;EventHandler&gt;((s, e) => Debug.WriteLine("Status clicked"))
        ///     .WithToolTip("Application status bar")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowStatusStripControlSettings StatusStrip
        {
            [Forms(Description = "A status strip control (docked at the bottom) for GPAL forms", Submenu = "Menus and Bars")] get
            {
                GPALStatusStrip tmp = new GPALStatusStrip
                {
                    Name = "StatusStrip" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A menu bar docked to the top of a GPAL form.<br/>
        /// Open a top-level menu with WithMenu("TopMenu"), then attach GPAL.BarItem items with WithItem(item).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALMenuBar gPALMenuBar = (GPALMenuBar)GPAL.MenuBar
        ///     .WithMenu("File")
        ///     .WithItem(saveItem)
        ///     .WithSeparator()
        ///     .WithItem(exitItem)
        ///     .WithMenu("Help")
        ///     .WithItem(aboutItem)
        ///     .WithName("MainMenu")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowMenuBarControlSettings MenuBar
        {
            [Forms(Description = "A menu bar docked to the top of a GPAL form", Submenu = "Menus and Bars")] get
            {
                GPALMenuBar tmp = new GPALMenuBar
                {
                    Name = "MenuBar" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A single item shared by menu bars and toolbars: text, optional image, optional shortcut key,
        /// and a callback wired to its click.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALBarItem saveItem = (GPALBarItem)GPAL.BarItem
        ///     .WithText("Save")
        ///     .WithImage(saveIcon)
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Control | System.Windows.Forms.Keys.S)
        ///     .WithCallback&lt;EventHandler&gt;(SaveHandler)
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowBarItemSettings BarItem
        {
            [Forms(Description = "A single item shared by menu bars and toolbars: text, optional image, optional shortcut key, and a callback wired to its click", Submenu = "Menus and Bars")] get
            {
                GPALBarItem tmp = new GPALBarItem
                {
                    Name = "BarItem" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// A toolbar docked below the menu bar, holding GPAL.BarItem buttons and optional separators.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALToolbar gPALToolbar = (GPALToolbar)GPAL.Toolbar
        ///     .WithItem(newItem)
        ///     .WithSeparator()
        ///     .WithItem(runItem)
        ///     .WithName("MainToolbar")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowToolbarControlSettings Toolbar
        {
            [Forms(Description = "A toolbar docked below the menu bar, holding GPAL.BarItem buttons and optional separators", Submenu = "Menus and Bars")] get
            {
                GPALToolbar tmp = new GPALToolbar
                {
                    Name = "Toolbar" + controlCount++.ToString()
                };
                return tmp;
            }
        }

        /// <summary>
        /// Instantiates a new GPALTab for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALTab.WindowsControl.Clicked event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALTab gPalTab1 = (GPALTab)GPAL.Tab
        ///     .WithName("Tab1")
        ///     .WithText("Tab 1")
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.D1); // Alt-1
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithTab(gPalTab1)  // items after this are placed on this tab
        ///         .WithFormControl(gPalRadioButton1)
        ///         .WithFormControl(gPalRadioButton2)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettings Tab
        {
            [Forms(Description = "A tab page for a GPAL form", Submenu = "Layout")]
            get
            {
                GPALTab tmpControl = new GPALTab
                {
                    Name = radioButtonName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }

        /// <summary>
        /// Instantiates a new GPALSplitter for use on a GPAL form: two panes with a bar between them, built whole
        /// and then laid out like any other control.<br/>
        /// Call .HSplit for a left and right pane or .VSplit for a top and bottom, add the controls of both panes
        /// in order, and mark where the first pane ends with <see cref="SplitRight"/> or <see cref="SplitBottom"/>.
        /// <example>
        /// <code>
        /// GPAL.Splitter.HSplit(0.55)
        ///     .WithFormControl(GPAL.LabelFor("Automation Engine:"))
        ///     .WithFormControl(comboBoxAutomationEngine)
        ///     .WithFormControl(GPAL.SplitRight)
        ///     .WithFormControl(gPALNumeric);
        /// </code>
        /// </example>
        /// </summary>
        public static IAllowSplitterControlSettings Splitter { [Forms(Description = "Instantiates a new GPALSplitter for use on a GPAL form: two panes with a bar between them, built whole and then laid out like any other control", Submenu = "Layout")] get => new GPALSplitter(); }

        /// <summary>
        /// Instantiates a new GPALGroupBox for use on a GPAL form: a titled box around a set of controls, laid out
        /// like any other control. A container can go inside it, so a group box can hold a splitter.
        /// </summary>
        public static IAllowGroupBoxControlSettings GroupBox { [Forms(Description = "Instantiates a new GPALGroupBox for use on a GPAL form: a titled box around a set of controls, laid out like any other control", Submenu = "Layout")] get => new GPALGroupBox(); }

        /// <summary>Marks the end of a splitter's left pane. Everything added after it is the right pane.</summary>
        public static GPALSpliter SplitRight { [Forms(Description = "Marks the end of a splitter's left pane", Submenu = "Layout")] get => new GPALSpliter(); }

        /// <summary>Marks the end of a splitter's top pane. Everything added after it is the bottom pane.</summary>
        public static GPALSpliter SplitBottom { [Forms(Description = "Marks the end of a splitter's top pane", Submenu = "Layout")] get => new GPALSpliter(); }

        /// <summary>
        /// Instantiates a new GPALTextArea for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALTextarea.WindowsControl.TextChanged event.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALTextArea gPalTextArea = (GPALTextArea)GPAL.TextArea
        ///     .WithText("Type something here")
        ///     .WithCallback&lt;EventHandler&gt;(TextAreaHandler) // on text changed event handler
        ///     .WithShortcutKey(System.Windows.Forms.Keys.Alt | System.Windows.Forms.Keys.T);  // Alt-T
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalTextArea)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowControlSettingsAndPlaceholder TextArea
        {
            [Forms(Description = "A multi-line text box for a GPAL form, calling back when its text changes", Submenu = "Text")]
            get
            {
                GPALTextArea tmpControl = new GPALTextArea
                {
                    Name = textAreaName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALRichTextBox for use on a GPAL form.<br/>
        /// Renders Markdown (headings, **bold**, and bullet/numbered lists with nesting) - useful for
        /// displaying formatted AI responses or other structured text.
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALRichTextBox gPalOutput = (GPALRichTextBox)GPAL.RichTextBox
        ///     .WithName("AIOutput")
        ///     .ToGPALObject();
        ///
        /// static void Main(string[] args)
        /// {
        ///     Form myForm = (Form)GPAL.Form
        ///         .WithTitle("Simple Form Test")
        ///         .WithFormControl(gPalOutput)
        ///         .ShowDialog();
        /// }
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create your control</returns>
        public static IAllowRichTextBoxControlSettings RichTextBox
        {
            [Forms(Description = "A rich text box for a GPAL form that renders Markdown", Submenu = "Text")]
            get
            {
                GPALRichTextBox tmpControl = new GPALRichTextBox
                {
                    Name = richTextBoxName + controlCount++.ToString()
                };
                ControlList.Add(tmpControl);
                return tmpControl;
            }
        }
        /// <summary>
        /// Instantiates a new GPALTreeView for use on a GPAL form.<br/>
        /// Callback EventHandler is invoked on the GPALTreeView.WindowsControl.AfterSelect event (or other events when specified via .ForEvent).
        /// Supports hierarchical node display (folders, categories, workflows, etc.).
        /// </summary>
        /// <example>
        /// <code>
        /// static GPALTreeView workflowTree = (GPALTreeView)GPAL.TreeView
        ///     .WithName("WorkflowSteps")
        ///     .WithCallback&lt;TreeViewEventHandler&gt;((s, e) => 
        ///     {
        ///         var tv = (TreeView)s;
        ///         Debug.WriteLine($"Selected node: {tv.SelectedNode?.Text}");
        ///     })
        ///     .WithToolTip("Workflow structure or directory tree")
        ///     .ToGPALObject();
        /// </code>
        /// </example>
        /// <returns>Fluent interface to create and configure the tree view</returns>
        public static IAllowTreeViewControlSettings TreeView
        {
            [Forms(Description = "Instantiates a new GPALTreeView for use on a GPAL form", Submenu = "Lists and Data")] get
            {
                GPALTreeView tmp = new GPALTreeView
                {
                    Name = "TreeView" + controlCount++.ToString()
                };
                return tmp;
            }
        }
        #endregion <Form Controls>
        #region <Getters/Setters>
        public static bool UseHardware
        {
            [GPALSettings(Description = "Whether GPAL uses the real mouse and keyboard by default")] get => gpalSettings.UseHardware;
        }
        public static bool UseJavaSCript
        {
            [GPALSettings(Description = "Whether GPAL interacts through injected JavaScript by default")] get => gpalSettings.UseJavaScript;
        }

        /// <summary>
        /// Runs whole workflows at once, each building and owning whatever it drives, so one can run Chrome on
        /// OttoMagic while the next runs Firefox on Selenium. Scheduling only: see
        /// <see cref="Browser.Browser.WithWorkflow(Action{Browser.IBrowser})"/> for several steps against one
        /// browser you already have.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.Workflow
        ///         .WithWorkflow(() =&gt; { /* builds its own browser, closes it */ })
        ///         .WithWorkflow(() =&gt; { /* and so does this one */ })
        ///         .Run(2);
        /// </code>
        /// </example>
        public static Browser.IAllowScheduledWorkflow Workflow
        {
            [Component(Description = "Run whole workflows at once, each owning the browser or application it drives", Submenu = "System")]
            get
            {
                return new Browser.WorkflowScheduler();
            }
        }
        /// <summary>
        /// Operations on all hidden desktops this process created. Peek shows one and switches back; Return switches
        /// back early, from any thread; CloseAll releases the handles.
        /// <br/><br/>
        /// For one browser's desktop, use that browser's HiddenDesktop.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.HiddenDesktop.Peek();               // every one made so far, five seconds each
        ///     GPAL.HiddenDesktop.Peek(10_000);         // the same walk, ten seconds each
        ///     GPAL.HiddenDesktop.Peek("Overnight");    // just that one
        ///
        ///     browser.HiddenDesktop.Peek();            // just the one this browser is on
        /// </code>
        /// </example>
        public static Browser.IHiddenDesktops HiddenDesktop
        {
            [Component(Description = "The hidden desktops this program made, to peek at, return from or close", Submenu = "System")]
            get
            {
                return new Browser.GPALHiddenDesktops();
            }
        }
        public static string Version
        {
            [Data(Description = "The version of GPAL that is running")] get => GPALSettings.Version;
        }
        /// <summary>
        /// Where an OAuth redirect lands and the token is read back, e.g. http://localhost:3117/. Set by
        /// ResolveTokenUrl from the registered redirect URI.
        /// </summary>
        public static string OAuthTokenUrl
        {
            [Data(Description = "Where an OAuth redirect lands and the token is read back from, e.g. http://localhost:3117/")] get
            {
                return gpalSettings.OAuthTokenUrl;
            }
            set
            {
                gpalSettings.OAuthTokenUrl = value;
            }
        }
        internal static List<IGPALLogger> PublishToLoggers
        {
            get
            {
                return GPALSettings.PublishToLoggers;
            }
        }
        // The text area set by WithPublishToControl, or null
        internal static GPALTextArea PublishControl
        {
            get
            {
                return GPALSettings.PublishControl;
            }
        }

        // Rolling line cap, so a long run does not grow the control without bound
        private const int PublishControlMaxLines = 2_000;

        internal static bool PublishToConsole
        {
            get
            {
                return GPALSettings.PublishToConsole;
            }
            set
            {
                GPALSettings.PublishToConsole = value;
            }
        }
        internal static bool PublishToDebug
        {
            get
            {
                return GPALSettings.PublishToDebug;
            }
        }
        internal static GPALEventType ConsoleEvents
        {
            get => GPALSettings.ConsoleEvents;
            set => GPALSettings.ConsoleEvents = value;
        }
        internal static GPALEventType DebugEvents
        {
            get => GPALSettings.DebugEvents;
        }
        internal static bool PublishStackTrace
        {
            get
            {
                return GPALSettings.PublishStackTrace;
            }
        }
        internal static string ErrorPlaceholder
        {
            get
            {
                return GPALSettings.ErrorPlaceholder;
            }
        }
        internal static string DriverLocation
        {
            get
            {
                return GPALSettings.DriverLocation;
            }
        }
        internal static bool SimulateMouse
        {
            get
            {
                return GPALSettings.SimulateMouse;
            }
        }
        internal static string TempProfileDirectory
        {
            get
            {
                return GPALSettings.TempProfileDirectory;
            }
        }
        /// <summary>
        /// A copy of the open browsers list, since concurrent workflows change it. <see cref="Forget"/> removes a
        /// browser from the list.
        /// </summary>
        internal static List<Browser.Browser> Browsers
        {
            get
            {
                lock (BrowserList)
                    return BrowserList.ToList();
            }
        }
        /// <summary>
        /// Takes a closed browser off the list of open ones.
        /// </summary>
        /// <param name="browser">The browser that closed</param>
        internal static void Forget(Browser.Browser browser)
        {
            lock (BrowserList)
                BrowserList.Remove(browser);
        }
        internal static int MatchingPercentage
        {
            get
            {
                return GPALSettings.MatchingPercentage;
            }
        }
        internal static int TypingDelay
        {
            get
            {
                return GPALSettings.TypingDelay;
            }
        }
        internal static bool StopOnNotFound
        {
            get
            {
                return GPALSettings.StopOnNotFound;
            }
        }
        internal static bool NoFallbackRecoveryActions
        {
            get
            {
                return gpalSettings.NoFallbackRecoveryActions;
            }
        }
        #endregion <Getters/Setters>
        #region <Helpers>
        [Helper(Description = "Load GPAL's settings from GPAL.yaml or the given file, creating it with the defaults if it is missing")]
        public static void LoadSettings(GPALFile settingsFile = null)
        {
            bool hold = GPAL.PublishToConsole;
            GPALEventType events = GPAL.GPALSettings.ConsoleEvents;

            try
            {
                if (settingsFile != null)
                {
                    _settingsFilePath = settingsFile.Filenames[0];
                    SettingsLoaded = false;
                }

                if (false == SettingsLoaded)
                {
                    SettingsLoaded = true;
                    string currentVersion = GenerallyPositive.GPALSettings.BuildVersion;
                    GPALFile resolvedFile = _settingsFilePath;

                    if (true == System.IO.File.Exists(resolvedFile.Filenames[0]))
                    {
                        // Clear Corrected so it only lists this load's corrections.
                        gpalSettings.Corrected.Clear();

                        // load setting which contains default events to publish
                        GPAL.Converter.WithInput(resolvedFile).SaveTo(ref gpalSettings);

                        hold = GPAL.PublishToConsole;
                        events = GPAL.GPALSettings.ConsoleEvents;

                        // NOTE: CAVEAT: WE CANNOT EVER send anything on stdio when we are running as a browser Native App - that will disconnect it as garbage received
                        if ("1" == Environment.GetEnvironmentVariable("GPAL_IS_SILENT"))
                            GPAL.PublishToConsole = false;

                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{resolvedFile.Filenames[0]}] loaded, using settings.", gpalSettings, GPALObjectType.Other);

                        // A value GPAL could not use was replaced with its default. Save it so it is not corrected on
                        // every start.
                        if (0 < gpalSettings.Corrected.Count)
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{resolvedFile.Filenames[0]}] held values GPAL cannot use, set to their defaults and saved: {string.Join(", ", gpalSettings.Corrected)}", gpalSettings, GPALObjectType.Other);

                        if (false == gpalSettings.Version.Equals(currentVersion))
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{resolvedFile.Filenames[0]}] updated to current GPAL version [{currentVersion}].", gpalSettings, GPALObjectType.Other);

                        if (0 < gpalSettings.Corrected.Count || false == gpalSettings.Version.Equals(currentVersion))
                        {
                            gpalSettings.Version = currentVersion;
                            gpalSettings.Corrected.Clear();
                            SaveSettings();
                        }
                    }
                    else    // if file doesn't exist, initialize default settings and save a settings file, the user can then tweak those settings for next run/load
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{resolvedFile.Filenames[0]}] does not exist. Using default settings and creating GPAL.yaml.", gpalSettings, GPALObjectType.Other);
                        SaveSettings();
                    }
                }

                AIProvidersConfig.TryAutoLoad();
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"[{_settingsFilePath}] load error. Try deleting it and restarting.", gpalSettings, GPALObjectType.Other, ex);
            }
            finally
            {
                GPAL.PublishToConsole = hold;
                GPAL.GPALSettings.ConsoleEvents = events;
            }
        }
        [Helper(Description = "Save GPAL's current settings to GPAL.yaml or the given file")]
        public static void SaveSettings(GPALFile settingsFile = null)
        {
            if (settingsFile != null) _settingsFilePath = settingsFile.Filenames[0];
            GPAL.Converter.WithInput(GPALSettings).SaveTo((GPALFile)_settingsFilePath);
        }
        /// <summary>
        /// Take a screenshot
        /// </summary>
        /// <example>
        /// <code>
        /// Bitmap screenshot = GPAL.CaptureScreen();</code></example>
        /// <returns>Bitmap of the screen</returns>
        [Helper(Description = "Take a screenshot and keep it on the browser's settings")]
        public static IAllowGraphicSettings CaptureScreen(Browser.IBrowser browser)
        {
            ((ImageHelper)GPAL.ImageHelper).CaptureScreen(browser).ToBitmap(out ((Browser.Browser)browser).BrowserSettings.ScreenShot);

            return ImageHelper;
        }
        /// <summary>
        /// Returns the calling methods name (used in Information/Exception messages).<br/>
        /// Instead of harcoding method names, call MyMethodName.
        /// </summary>
        /// <example>
        /// <code>
        /// Message = $"{GPAL.MyMethodName()}: Exception: Unable to load image. {ex.Message}"
        /// </code>
        /// </example>
        /// <returns>
        /// Calling method's name
        /// </returns>
        // https://stackoverflow.com/questions/44153/can-you-use-reflection-to-find-the-name-of-the-currently-executing-method/67372375#67372375
        [MethodImpl(MethodImplOptions.NoInlining)]
        [Helper(Description = "The name of the method that called it, for event messages")]
        public static string MyMethodName()
        {
            var st = new StackTrace();
            var sf = st.GetFrame(1);
            string name = sf.GetMethod().Name;

            if (name.Equals("MoveNext"))
            {
                // We're inside an async method
                name = sf.GetMethod().ReflectedType.Name
                         .Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            else if (name.StartsWith("<"))
            {
                // a lambda, named <Method>b__N_N by the compiler; report it by the method it is in
                name = name.Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
            }
            else if (true == sf.GetMethod().IsSpecialName && (name.StartsWith("get_") || name.StartsWith("set_")))
            {
                // a property like PageDown or NextTab, report it by the property name
                name = name.Substring(4);
            }

            return name;
        }
        /// <summary>
        /// Publish a message to either the information channel or exception channel (if exception passed in)
        /// Publish will insert the calling methods name, so there is no need to call GPAL.MyMethodName()
        /// A screenshot is published as well as the Browser/Application object to do what you want with in your handler
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.PublishSimpleEvent(GPALEventType.INFO, "An Information Channel Message.", application, GPALObjectType.Application);
        /// GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "An Exception Channel Message.", browser, GPALObjectType.Browser, exception);
        /// </code>
        /// </example>
        /// <param name="gPALEventType">The event type to publish, such as INFO or EXCEPTION</param>
        /// <param name="msg">Message to publish</param>
        /// <param name="gPALObject">GPAL object [Application, Browser, GPALForm, UnitOfWork]</param>
        /// <param name="gPALObjectType">The type of GPAL object passed in</param>
        /// <param name="ex">[Optional]Exception raised</param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        [Helper(Description = "Publish an event to the information or exception handlers")]
        public static void PublishSimpleEvent(GPALEventType gPALEventType, string msg, dynamic gPALObject = null, Enums.GPALObjectType gPALObjectType = GPALObjectType.None, Exception ex = null)
        {
            // Return before the costly StackTrace and reflection when no destination wants this event type; each
            // destination has its own mask (ConsoleEvents and DebugEvents for console and debug).
            // An event with an exception or of type EXCEPTION goes to the exception channel, decided once here for both
            // the mask and the handler.
            bool toExceptionChannel = null != ex || true == gPALEventType.HasFlag(GPALEventType.EXCEPTION);

            // Check only the handler for this event's channel.
            bool handlerWants = true == toExceptionChannel
                              ? null != ExceptionHandler && gpalSettings.ExceptionHandlerEvents.HasFlag(gPALEventType)
                              : null != InformationHandler && gpalSettings.InformationHandlerEvents.HasFlag(gPALEventType);
            bool consoleWants = true == GPAL.PublishToConsole && gpalSettings.ConsoleEvents.HasFlag(gPALEventType);
            bool debugWants = true == GPAL.PublishToDebug && gpalSettings.DebugEvents.HasFlag(gPALEventType);
            bool controlWants = null != GPAL.PublishControl && gpalSettings.ControlEvents.HasFlag(gPALEventType);
            bool loggerWants = 0 < GPAL.PublishToLoggers.Count;

            if (false == handlerWants && false == consoleWants && false == debugWants && false == controlWants && false == loggerWants)
                return;

            if (true == handlerWants || true == controlWants || true == consoleWants || true == debugWants || true == loggerWants)
            {
                var st = new StackTrace();
                var sf = st.GetFrame(1);
                string name = sf.GetMethod().Name;
                DateTime dateTime = DateTime.Now;
                string stackTrace = null != ex && true == GPAL.PublishStackTrace ? $"[{ex.StackTrace}]" : string.Empty;

                if (name.Equals("MoveNext"))
                {
                    // We're inside an async method
                    name = sf.GetMethod().ReflectedType.Name
                             .Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
                }
                else if (name.StartsWith("<"))
                {
                    // a lambda, named <Method>b__N_N by the compiler; log it by the method it is in
                    name = name.Split(new char[] { '<', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
                }
                else if (true == sf.GetMethod().IsSpecialName && (name.StartsWith("get_") || name.StartsWith("set_")))
                {
                    // a property like PageDown or NextTab, log it by the property name
                    name = name.Substring(4);
                }

                // padded inside the tag to the length of WARNING, CAUTION and FAILURE, so the method names line up down
                // the log. EXCEPTION and DEEPDEBUG run two past it, which makes an exception easy to spot
                string outMessage = $"[{dateTime.ToString("yyyy-MM-dd HH:mm:ss.ffff")}][{gPALEventType,-7}][{name}]: {msg}";
                Bitmap ss = null;

                // NOTE: a screen image is captured only on error or exception
                if (GPALEventType.ERROR == gPALEventType || GPALEventType.EXCEPTION == gPALEventType)
                    if (false == inPublishEvent && false == handlingTermination)
                    {
                        try
                        {
                            inPublishEvent = true;  // avoid reentrant calls from events published in different paths calling capture screen
                            if (true == gpalSettings.TakeEventScreenshot)
                                if (typeof(Browser.Browser) == gPALObject?.GetType())
                                    ((ImageHelper)GPAL.ImageHelper).CaptureScreen(gPALObject).ToBitmap(out ss);
                                else
                                    ((ImageHelper)GPAL.ImageHelper).CaptureScreen().ToBitmap(out ss);
                        }
                        catch // (Exception ex2)
                        {
                            //    Console.WriteLine($"Unable to get screenshot in PublishSimpleEvent. [{ex2.Message}]");
                            //    Debug.WriteLine($"Unable to get screenshot in PublishSimpleEvent. [{ex2.Message}]");
                            // if we are using ottomagic, nothing will be started up to call capture-visible-tab, so ignore
                        }
                        finally
                        {
                            inPublishEvent = false;
                        }
                    }

                GPALEventArgs args = new GPALEventArgs()
                {
                    GPALEventType = gPALEventType,
                    GPALObject = gPALObject,
                    GPALObjectType = gPALObjectType,
                    CurrentUOW = (Enums.GPALObjectType.UnitOfWork == gPALObjectType ? (UnitOfWork)gPALObject : null),
                    Message = $"{name}: {msg}",
                    ScreenShot = ss,
                    ExceptionRaised = ex,
                    Data = (Enums.GPALObjectType.Converter == gPALObjectType ? gPALObject : string.Empty),
                    DateTimeStamp = dateTime
                };

                // Same mask as the early-out above
                if (true == handlerWants)
                {
                    if (true == toExceptionChannel)
                        ExceptionHandler?.Invoke(gPALObject, args);
                    else
                        InformationHandler?.Invoke(gPALObject, args);
                }

                if (true == controlWants)
                {
                    GPAL.PublishControl.AppendLine(outMessage, PublishControlMaxLines);

                    // Exception text on a second line, under the same DEBUG rule the console below uses
                    if (null != ex && true == gpalSettings.ControlEvents.HasFlag(GPALEventType.DEBUG))
                        GPAL.PublishControl.AppendLine($"[{ex.Message}]{stackTrace}", PublishControlMaxLines);
                }

                if (true == GPAL.PublishToConsole)
                {
                    // NOTE: silent init is used by gpal rest api, we must never output anything on stdio
                    if (gpalSettings.ConsoleEvents.HasFlag(gPALEventType) && Environment.GetEnvironmentVariable("GPAL_IS_SILENT") != "1")
                    {
                        Console.WriteLine(outMessage);

                        // Exception text and stack trace are only written when DEBUG is on.
                        if (null != ex && gpalSettings.ConsoleEvents.HasFlag(GPALEventType.DEBUG))
                            Console.WriteLine($"[{ex.Message}]{stackTrace}");
                    }
                }

                if (true == GPAL.PublishToDebug)
                {
                    if (gpalSettings.DebugEvents.HasFlag(gPALEventType) && Environment.GetEnvironmentVariable("GPAL_IS_SILENT") != "1")
                    {
                        Debug.WriteLine(outMessage);

                        // Exception text and stack trace only when DEBUG is on, as above.
                        if (null != ex && gpalSettings.DebugEvents.HasFlag(GPALEventType.DEBUG))
                            Debug.WriteLine($"[{ex.Message}]{stackTrace}");
                    }
                }

                // The logger decides where to write and whether it accepts this type. Pass the event's own type.
                // Guarded because the logger's own events would come straight back here.
                if (0 < GPAL.PublishToLoggers.Count && true == Enum.TryParse(gPALEventType.ToString(), out Enums.LogType logType))   // Enums. because selenium has a LogType too
                {
                    if (null == loggersWriting)
                        loggersWriting = new HashSet<IGPALLogger>();

                    // Loop over a copy; a logger raising an event could add another logger.
                    foreach (IGPALLogger logger in GPAL.PublishToLoggers.ToList())
                    {
                        // This logger raised this event itself while writing; skip it. Other loggers still get it.
                        if (true == loggersWriting.Contains(logger))
                            continue;

                        try
                        {
                            loggersWriting.Add(logger);

                            // Call Log through the GPALLogger class, not the interface: Log is internal, turning an
                            // event into a log entry.
                            GPALLogger writer = (GPALLogger)logger;

                            // Pass the event type with the entry so two threads cannot swap each other's severity.
                            writer.Log(msg, name, logType);

                            // Log the exception text as a separate DEBUG entry, as the console and debug blocks do. A
                            // log file may be the only record of an unattended run.
                            if (null != ex)
                                writer.Log($"[{ex.Message}]{stackTrace}", name, Enums.LogType.DEBUG);
                        }
                        catch
                        {
                            // A failing logger must not stop the other loggers or the run.
                        }
                        finally
                        {
                            loggersWriting.Remove(logger);
                        }
                    }
                }

                // flush any pending output, following operations may block or delay output
                // also flush for user who might be outputting and not realizing they need to flush output. :)
                if (Environment.GetEnvironmentVariable("GPAL_IS_SILENT") != "1")
                {
                    Console.Out.Flush();
                    Debug.Flush();
                }
            }
        }
        /// <summary>
        /// Emits an ASCII table for any 2-axis keyed dictionary as a single INFO event: row keys down the side,
        /// column keys across the top, one cell per (row, col) present in <paramref name="data"/>. Missing cells
        /// show <paramref name="emptyCell"/>. Row/column order is the natural order of the key type (enum value,
        /// numeric, or string). Cells render via <paramref name="formatCell"/>, defaulting to ToString().
        /// </summary>
        /// <param name="title">Heading shown above the table.</param>
        /// <param name="data">Values keyed by (row, col).</param>
        /// <param name="cornerLabel">Label for the top-left corner (the row axis name).</param>
        /// <param name="formatCell">Optional per-value formatter; defaults to ToString().</param>
        /// <param name="emptyCell">Text for a (row, col) pair absent from the data.</param>
        public static void EmitTable<TRow, TCol, TValue>(
            string title,
            IDictionary<(TRow row, TCol col), TValue> data,
            string cornerLabel = "",
            Func<TValue, string> formatCell = null,
            string emptyCell = "-")
        {
            if (null == data || 0 == data.Count)
                return;

            formatCell = formatCell ?? (v => v?.ToString() ?? string.Empty);

            var rows = data.Keys.Select(k => k.row).Distinct().OrderBy(r => r, Comparer<TRow>.Default).ToList();
            var cols = data.Keys.Select(k => k.col).Distinct().OrderBy(c => c, Comparer<TCol>.Default).ToList();

            string Cell(TRow r, TCol c) => data.TryGetValue((r, c), out var v) ? formatCell(v) : emptyCell;

            int rowHeaderWidth = cornerLabel.Length;
            foreach (var r in rows)
                rowHeaderWidth = Math.Max(rowHeaderWidth, (r?.ToString() ?? string.Empty).Length);

            var colWidth = new Dictionary<TCol, int>();
            foreach (var c in cols)
            {
                int w = (c?.ToString() ?? string.Empty).Length;
                foreach (var r in rows)
                    w = Math.Max(w, Cell(r, c).Length);
                colWidth[c] = w;
            }

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"=== {title} ===");

            sb.Append(cornerLabel.PadRight(rowHeaderWidth));
            foreach (var c in cols)
                sb.Append(" | " + (c?.ToString() ?? string.Empty).PadRight(colWidth[c]));
            sb.AppendLine();

            sb.Append(new string('-', rowHeaderWidth));
            foreach (var c in cols)
                sb.Append("-+-" + new string('-', colWidth[c]));
            sb.AppendLine();

            foreach (var r in rows)
            {
                sb.Append((r?.ToString() ?? string.Empty).PadRight(rowHeaderWidth));
                foreach (var c in cols)
                    sb.Append(" | " + Cell(r, c).PadRight(colWidth[c]));
                sb.AppendLine();
            }

            // Publish as NOTICE, the workflow's channel, because the workflow asked for the table.
            PublishSimpleEvent(GPALEventType.NOTICE, sb.ToString());
        }
        /// <summary>
        /// Convenience wrapper over <see cref="EmitTable{TRow,TCol,TValue}"/> for test-program bt/ae[/headless]
        /// loops: browser types down the side, automation engines across the top, each cell the wall-clock
        /// runtime. Call once per headless state with its own title (e.g. "Window" and "Headless").
        /// </summary>
        public static void EmitRuntimeTable(string title, IDictionary<(BrowserType bt, AutomationEngine ae), TimeSpan> runtimes)
            => EmitTable(title + " runtime (min:sec.ms)", runtimes, "Browser",
                         ts => $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}");
        #endregion <Helpers>
        #region <GPAL Settings>
        // due to the fluent nature, we have to return an object with the interface IAllowGPALSettings
        // these are implemented in GPAL but also in GPALSettings, the object we chain to
        // but we want our language syntax to start with GPAL.
        // and then chain quietly, from GPAL to GPALSettings.
        // Problem is static classes cannot inherit interfaces, so we have to return the setting object.

        /// <summary>
        /// Global Browser CallIfFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfFound">The handler to call</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings AppCallIfFound(Application.Application.CallIfDelegate callIfFound)
        {
            gpalSettings.AppCallIfFoundList.Add(callIfFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global Browser CallIfNotFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfNotFound"></param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfNotFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings AppCallIfNotFound(Application.Application.CallIfDelegate callIfNotFound)
        {
            gpalSettings.AppCallIfNotFoundList.Add(callIfNotFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global Browser CallIfFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfFound"></param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings CallIfFound(Browser.Browser.CallIfDelegate callIfFound)
        {
            gpalSettings.CallIfFoundList.Add(callIfFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global Browser CallIfNotFound handler called for every selector in every Unit of Work
        /// </summary>
        /// <param name="callIfNotFound"></param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global Browser CallIfNotFound handler called for every selector in every Unit of Work")]
        public static IAllowGPALSettings CallIfNotFound(Browser.Browser.CallIfDelegate callIfNotFound)
        {
            gpalSettings.CallIfNotFoundList.Add(callIfNotFound);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Print the published information or exception messages to the console.
        /// </summary>
        /// <param name="eventType">The event types to print to the console; NONE turns console output off [all types]</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Print specified GPAL events to the console. [none (default)]")]
        public static IAllowGPALSettings WithPublishToConsole(GPALEventType eventType = GPALEventType.INFO | GPALEventType.WARNING | GPALEventType.ERROR | GPALEventType.EXCEPTION | GPALEventType.NOTICE | GPALEventType.CAUTION | GPALEventType.FAILURE)
        {
            GPALSettings.ConsoleEvents = eventType;
            if (GPALEventType.NONE == eventType)
                GPALSettings.PublishToConsole = false;
            else
                GPALSettings.PublishToConsole = true;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Writes GPAL events to a text area on a form, as <see cref="WithPublishToConsole"/> does to a
        /// console.<br/>
        /// An exception adds a second line carrying its own text, which requires DEBUG in
        /// <see cref="WithControlEvents"/>. All event types are written by default.<br/>
        /// Writing is queued and drained on the thread that owns the control, so publishing from a workflow
        /// thread is safe.
        /// </summary>
        /// <example>
        /// <code>
        ///     static GPALTextArea logArea = (GPALTextArea)GPAL.TextArea.WithReadOnly(true).WithName("log").ToGPALObject();
        ///
        ///     GPAL.WithPublishToControl(logArea);
        /// </code>
        /// </example>
        /// <param name="control">The text area to write to, or null to stop writing to one</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Write specified GPAL events to a text area on a form. [none (default)]")]
        public static IAllowGPALSettings WithPublishToControl(GPALTextArea control)
        {
            GPALSettings.PublishControl = control;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Which event types reach the control given to <see cref="WithPublishToControl"/>. Defaults to all.<br/>
        /// DEBUG must be included for an exception to write its own text.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithControlEvents(GPALEventType.All &amp; ~GPALEventType.DEEPDEBUG);
        /// </code>
        /// </example>
        /// <param name="eventTypes">The events to write to the control</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach the form control. [all (default)]")]
        public static IAllowGPALSettings WithControlEvents(GPALEventType eventTypes)
        {
            GPALSettings.ControlEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Which event types reach the handler given to <see cref="WithInformationHandler"/>. Defaults to all.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithInformationHandlerEvents(GPALEventType.All &amp; ~GPALEventType.DEEPDEBUG);
        /// </code>
        /// </example>
        /// <param name="eventTypes">The events to hand to the information handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach the information handler. [all (default)]")]
        public static IAllowGPALSettings WithInformationHandlerEvents(GPALEventType eventTypes)
        {
            GPALSettings.InformationHandlerEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Which event types reach the handler given to <see cref="WithExceptionHandler"/>. Defaults to all.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.WithExceptionHandlerEvents(GPALEventType.EXCEPTION | GPALEventType.ERROR);
        /// </code>
        /// </example>
        /// <param name="eventTypes">The events to hand to the exception handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach the exception handler. [all (default)]")]
        public static IAllowGPALSettings WithExceptionHandlerEvents(GPALEventType eventTypes)
        {
            GPALSettings.ExceptionHandlerEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Indicate which event types to print to the console. Default is none
        /// </summary>
        /// <param name="eventType">The event types to print to debug; NONE turns debug output off [all types]</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Print specified GPAL events to the output window in VisualStudio. [none (default)]")]
        public static IAllowGPALSettings WithPublishToDebug(GPALEventType eventType = GPALEventType.INFO | GPALEventType.WARNING | GPALEventType.ERROR | GPALEventType.EXCEPTION | GPALEventType.NOTICE | GPALEventType.CAUTION | GPALEventType.FAILURE)
        {
            GPALSettings.DebugEvents = eventType;
            if (GPALEventType.NONE == eventType)
                GPALSettings.PublishToDebug = false;
            else
                GPALSettings.PublishToDebug = true;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Print the published information or exception messages to the supplied database.
        /// Uses Database Create sql
        /// One column for each property in GPALEventArgs
        /// </summary>
        /// <param name="logger">publish events to this logger, which writes them wherever it was pointed</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Publish GPAL events to a logger, which writes them to whatever it was given: a file or a database.")]
        public static IAllowGPALSettings WithPublishToLogger(IGPALLogger logger)
        {
            GPALSettings.WithPublishToLogger(logger);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Stops publishing events to a logger.
        /// </summary>
        /// <param name="logger">The logger to stop publishing to</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Stop publishing GPAL events to a logger that was previously given one.")]
        public static IAllowGPALSettings RemoveLogger(IGPALLogger logger)
        {
            GPALSettings.RemoveLogger(logger);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets which event types reach the handlers from <see cref="WithInformationHandler"/> and
        /// <see cref="WithExceptionHandler"/>. Default is all.<br/>
        /// Each wanted event costs a stack capture, and DEEPDEBUG alone is thousands per run, so an on-screen display
        /// should list only what it shows.
        /// </summary>
        /// <example>
        /// <code>
        ///     // everything except DEBUG and DEEPDEBUG, which come in thousands
        ///     GPAL.WithHandlerEvents(GPALEventType.All &amp; ~(GPALEventType.DEBUG | GPALEventType.DEEPDEBUG));
        /// </code>
        /// </example>
        /// <param name="eventTypes">The event types to pass to the handlers.</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Which GPAL events reach an attached Information or Exception handler. [All (default)]")]
        public static IAllowGPALSettings WithHandlerEvents(GPALEventType eventTypes)
        {
            GPALSettings.InformationHandlerEvents = eventTypes;
            GPALSettings.ExceptionHandlerEvents = eventTypes;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Print the stack trace along with the published GPAL message
        /// </summary>
        /// <param name="trueFalse"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Print stack trace when PublishToConsole or PublishToDebug is true. [true (default)]")]
        public static IAllowGPALSettings WithPublishStackTrace(bool trueFalse = true)
        {
            GPALSettings.PublishStackTrace = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets a handler for actionable failures, for any browser without its own.<br/>
        /// Without a handler GPAL reports and continues. The handler receives the browser, to tell workflows apart.
        /// </summary>
        /// <param name="callOnFail">Callback delegate</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Callback for failures a workflow can decide about, used by any browser without its own CallOnFail")]
        public static IAllowGPALSettings CallOnFail(Browser.Browser.CallOnFailDelegate callOnFail)
        {
            GPALSettings.CallOnFailHandlers.Add(callOnFail);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// When GPAL is unable to retrieve data from an element, this message will be placed in the grid.<br/>
        /// GPAL will always strive to give you as much data as possible until you get your selectors perfected.
        /// </summary>
        /// <param name="message">The message to substitute for no data.<br/>"##UNABLE TO GET##" [default]</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Substitue string when data cannot be retrieved for an element [##UNABLE TO GET## (default)]")]
        public static IAllowGPALSettings WithDefaultErrorPlaceholder(string message = "##UNABLE TO GET##")
        {
            GPALSettings.ErrorPlaceholder = message;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Set the default timeout time in seconds to wait for a download to start
        /// The default is 60 seconds
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Set the default timeout for waiting for a download to start.")]
        public static IAllowGPALSettings WithDownloadTimeoutInSec([Seconds] int seconds)
        {
            GPALSettings.DownloadTimeoutInSec = seconds;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// GPAL has fallback alternatives for actions should they fail, generally applies to selenium.
        /// The fallback involves javascript alternative to complete the action.
        /// Set this to true to NOT use try any fallback recovery, just fail
        /// this is set to false by default, if used with no parameter the default is true
        /// </summary>
        /// <param name="trueFalse">Use fallback recovery? [true]</param>
        /// <returns></returns>
        [GPALSettings("DO NOT use GPAL fallback actions on failure, just fail. Usually involves javascript recovery attempt. [false (default)")]
        public static IAllowGPALSettings WithNoFallbackActions(bool trueFalse = true)
        {
            GPALSettings.NoFallbackRecoveryActions = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global value for selector matches so it's not set UOW at a time
        /// </summary>
        /// <param name="matchCount">How many matching elements a selector acts on [int.MaxValue]</param>
        /// <returns></returns>
        [GPALSettings(Description = "Global value for selector matches so it's not set UOW at a time")]
        public static IAllowGPALSettings WithAllThatMatch(int matchCount = int.MaxValue)
        {
            GPALSettings.WithAllThatMatch = matchCount;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Automatically download and unzip the web driver that matches the version of the browser being run.
        /// This uses setting which will be in the GPAL.yaml configuration file which is loaded on startup (or loaded on-demand with a path).
        /// this is set to false by default, if used with no parameter the default is true
        /// </summary>
        /// <param name="trueFalse">Auto Update? [true]</param>
        /// <returns></returns>
        [GPALSettings(Description = "Automatically download and unzip the web driver that matches the version of the browser being run")]
        public static IAllowGPALSettings WithAutoUpdateWebDriver(bool trueFalse = true)
        {
            GPALSettings.AutoUpdateWebDriver = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Set the automation type to use for all browsers.
        /// NOTE: can be overridden at the selector level (to choose javascript or hardware)
        /// </summary>
        /// <param name="automationEngine"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Set the automation type to use for all browsers")]
        public static IAllowGPALSettings WithAutomationEngine(AutomationEngine automationEngine)
        {
            GPALSettings.AutomationEngine = automationEngine;
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// The location of the browser drivers to be used for each GPAL.Browser instance.<br/>
        /// This can be overridden at the GPAL.Browser level, as well.<br/>
        /// Additionally, if not defined, the default location is the same directory as the GPAL executable.
        /// </summary>
        /// <param name="driverPath">Path to the drivers folder</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Global location for browser drivers. [./ (default)(same dir as exe)]")]
        public static IAllowGPALSettings WithDriverLocation(string driverPath)
        {
            GPALSettings.DriverLocation = FileHelper.EnsureDirectoryEndsWithBackslash(driverPath);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets the folder for GPAL's temporary files, such as a GPALFile downloaded from a url.<br/>
        /// Default: a GPALfilesSafeToDelete folder beside the executable, created on first use; safe to delete at any
        /// time.
        /// </summary>
        /// <param name="tempPath">Folder for GPAL's temporary files</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Where GPAL writes files it created itself and nobody asked to keep. [GPALfilesSafeToDelete beside the exe (default)]")]
        public static IAllowGPALSettings WithTempDirectory([Directory] string tempPath)
        {
            GPALSettings.TempDirectory = FileHelper.EnsureDirectoryEndsWithBackslash(tempPath);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// The folder GPAL writes its own throwaway files to, created if it is not there yet. Either what
        /// .WithTempDirectory was given, or GPALfilesSafeToDelete beside the executable.
        /// </summary>
        /// <returns>Full path, ending in a backslash</returns>
        internal static string TempDirectory()
        {
            string tempPath = true == string.IsNullOrWhiteSpace(GPALSettings.TempDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GPALfilesSafeToDelete") + @"\"
                : GPALSettings.TempDirectory;

            if (false == Directory.Exists(tempPath))
                Directory.CreateDirectory(tempPath);

            return tempPath;
        }
        /// <summary>
        /// Sets the ceiling for the inter-character typing delay used by SendString across every automation
        /// engine (Hardware, Selenium, Puppeteer/CDP, OttoMagic).<br/>
        /// Default is 0 (no pacing - the whole string is sent atomically).<br/>
        /// When greater than 0, SendString dispatches the text character-by-character, sleeping a random
        /// duration between Math.Min(25, delayInTicks) and delayInTicks milliseconds before each character -
        /// not a fixed pause. A constant, identical gap between every keystroke is itself a detectable
        /// signature, so this value is a ceiling on a randomized range rather than the pace itself.
        /// </summary>
        /// <remarks>
        /// Only SendString honors this. FillIn* sets the value directly; its delayMs paces only its per-character
        /// fallback.
        /// </remarks>
        /// <param name="delayInTicks">Ceiling for the randomized inter-character delay, in milliseconds</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithSimulateMouse(true);
        /// </code>
        /// </example>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Intercharacter typing delay ceiling [0ms (default), no pacing]")]
        public static IAllowGPALSettings WithTypingDelay([Milliseconds] int delayInTicks)
        {
            GPALSettings.WithTypingDelay(delayInTicks);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets the image matching percentage for image Selectors.<br/>
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithSimulateMouse(true);
        /// </code>
        /// </example>
        /// <param name="matchPercent">Image matching percentage [default is 80 (80%)]</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        [GPALSettings(Description = "Image matching similarity percentage. [80 (default)]")]
        public static IAllowGPALSettings WithImageMatchingPercentage([Percent] int matchPercent)
        {
            GPALSettings.WithImageMatchingPercentage(matchPercent);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Sets how many nodes a YAML payload can have before GPAL stops writing aliases for shared references.<br/>
        /// Without aliases a shared object is written in full each place and reads back as separate objects.
        /// </summary>
        /// <remarks>
        /// Past the limit a shared reference is written out in full each time and reads back as separate objects. A
        /// cyclic payload keeps its aliases at any size.
        /// </remarks>
        /// <example>
        /// <code>
        /// GPAL.WithYamlAliasLimit(20_000);   // a bigger graph whose shared references matter
        /// GPAL.WithYamlAliasLimit(0);        // never stop writing aliases
        /// </code>
        /// </example>
        /// <param name="nodeLimit">Nodes to allow before aliases are dropped [default is 5000]; 0 never drops them</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        [GPALSettings(Description = "Nodes a YAML payload may hold before aliases are dropped for shared references. A cyclic payload always keeps them. [5000 (default), 0 never drops]")]
        public static IAllowGPALSettings WithYamlAliasLimit(int nodeLimit)
        {
            GPALSettings.WithYamlAliasLimit(nodeLimit);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global setting to simulate a human moving the mouse to the next point for all commands.<br/>
        /// Default is false
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithSimulateMouse(true)
        /// </code>
        /// </example>
        /// <param name="trueFalse">True to move the mouse to the elements emulating a human<br/>False to jump the cursor to the element</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        [GPALSettings(Description = "Simulate moving the mouse for every action (implies .WithHardware). [false (default)]")]
        public static IAllowGPALSettings WithSimulateMouseMovement(bool trueFalse = true)
        {
            GPALSettings.SimulateMouse = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Global setting instructing GPAL to terminate if any Selector cannot find any elements.<br/>
        /// This is the GLOBAL setting affecting all Selectors.<br/>
        /// This setting is also available on individual selectors to only stop on critical Selectors.<br/>
        /// Default is false
        /// </summary>
        /// <param name="trueFalse">True to stop the workflow if an element is not found<br/>False to continue</param>
        /// <returns>Fluent interface to allow more GPAL settings</returns>
        /// <example>
        /// <code>
        /// GPAL.WithTypingDelay(50)
        ///     .WithImageMatchingPercentage(80)
        ///     .WithStopOnNotFound(true)
        /// </code>
        /// </example>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Stop workflow if any selector is not found")]
        public static IAllowGPALSettings WithStopOnNotFound(bool trueFalse = true)
        {
            GPALSettings.StopOnNotFound = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }

        [GPALSettings(Description = "Set the default location to store temporary profiles if browser.WithProfileDataDirectory is not used. default [C:\\profiles]")]
        public static IAllowGPALSettings WithTempProfileParentDirectory([Directory] string tempProfileDirectory)
        {
            // Expand environment variables as GPALSettings does, so %TEMP%\gpal resolves the same.
            GPALSettings.TempProfileDirectory = Environment.ExpandEnvironmentVariables(tempProfileDirectory);
            return (IAllowGPALSettings) GPALSettings;
        }

        /// <summary>
        /// Set the handler to receive handled Exception messages from GPAL.<br/>
        /// GPAL also sets an unhandled exception handler and will publish unhandled exceptions to this channel as well.<br/>
        /// GPAL will terminate on unhandled exceptions after publishing to the channel.
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithExceptionHandler(ExceptionEventHandler)
        ///     .WithInformationHandler(InformationEventHandler)
        ///     .WithTypingDelay(50);
        /// </code></example>
        /// <param name="eventHandler">Your Exception channel event handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Handler to receive GPAL Exception events")]
        public static IAllowGPALSettings WithExceptionHandler(EventHandler<GPALEventArgs> eventHandler)
        {
            ExceptionEventHandler = eventHandler;
            Exception ex = new Exception("Welcome Exception :)");
            GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Welcome to GPAL [{Version}] Exception Channel", null, Enums.GPALObjectType.None, ex);
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Prompt 'Press any key...' before terminating due to an uncaught exception.
        /// GPAL tries to handle all exceptions and continue running, publishing events with more information.
        /// </summary>
        /// <param name="trueFalse">true - prompt before exit<br/>false [default] - just exit</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Print GPAL published information or exception messages to the output window in VisualStudio. [true (default)]")]
        public static IAllowGPALSettings WithPromptBeforeUnexpectedExit(bool trueFalse = true)
        {
            GPALSettings.PromptBeforeUnexpectedExit = trueFalse;
            return (IAllowGPALSettings)GPALSettings;
        }

        /// <summary>
        /// Set the handler to receive Information messages from GPAL which you might like to know about.<br/>
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.WithExceptionHandler(ExceptionEventHandler)
        ///     .WithInformationHandler(InformationEventHandler)
        ///     .WithTypingDelay(50);
        /// </code></example>
        /// <param name="eventHandler">Your Information channel event handler</param>
        /// <returns>Fluent interface to specify more global settings</returns>
        [GPALSettings(Description = "Handler to receive GPAL Information events")]
        public static IAllowGPALSettings WithInformationHandler(EventHandler<GPALEventArgs> eventHandler)
        {
            InformationEventHandler = eventHandler;
            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Welcome to GPAL [{Version}] Information Channel");
            return (IAllowGPALSettings)GPALSettings;
        }
        /// <summary>
        /// Global setting to wait for time in ticks for an element to show up.
        /// Applied to every element in every workflow.
        /// Override in a unit of work using this pattern
        ///     .WithSelector(select0or)
        ///     .WaitFor(waitForInTicks)
        ///     .LeftClick()
        /// NOTE: shortcut actions like LeftClick{selector) will not waitfor
        ///     and no syntax will make them wait except this global setting
        /// </summary>
        /// <param name="waitForInTicks"></param>
        /// <returns></returns>
        [GPALSettings(Description = "Global setting to wait for time in ticks for an element to show up")]
        public static IAllowGPALSettings WithWaitFor([Milliseconds] int waitForInTicks)
        {
            GPALSettings.WaitForInMs = waitForInTicks;
            return (IAllowGPALSettings)GPALSettings;
        }

        [GPALSettings(Description = "Take a screenshot with each published event")]
        public static IAllowGPALSettings WithTakeEventScreenshot(bool trueOrFalse)
        {
            GPALSettings.TakeEventScreenshot = trueOrFalse;
            return (IAllowGPALSettings)GPALSettings;
        }


        #endregion <GPAL Settings>
        #region <Private>
        // nmot sure why i coded this, may have been when i started to code the driver update logic but determined a better way to get the version...
        private static void CheckVersions()
        {
            if (string.Empty.Equals(tmpBrowser?.BrowserVersion))
            {
                BrowserType browserType = tmpBrowser.BrowserType;

                if (BrowserType.Chrome == browserType)
                {
                    DriverHelper.GetChromeVersion(out tmpBrowser.BrowserSettings.Version);
                }
                else if (BrowserType.Edge == browserType)
                {
                    DriverHelper.GetEdgeVersion(out tmpBrowser.BrowserSettings.Version);
                }
                else if (BrowserType.FireFox == browserType)
                {
                    DriverHelper.GetFirefoxVersion(out tmpBrowser.BrowserSettings.Version);
                }

                //ICapabilities capabilities = ((RemoteWebDriver)tmpBrowser.BrowserDriver).Capabilities;
                //tmpBrowser.BrowserVersion = capabilities.GetCapability("browserVersion")?.ToString();

                // Common WebDriver Capabilities:
                // "browserName"        - Name of the browser (e.g., "chrome", "firefox", "edge")
                // "browserVersion"     - Version of the browser (e.g., "120.0.6099.110")
                // "platformName"       - OS platform ("windows", "linux", "macOS")
                // "acceptInsecureCerts" - Whether SSL cert warnings are ignored (true/false)
                // "setWindowRect"      - Whether the WebDriver can resize the window (true/false)
                //
                // Chrome-Specific:
                // "goog:chromeOptions" - ChromeDriver options (e.g., args, binary path)
                // "goog:loggingPrefs"  - Logging preferences (e.g., performance, console logs)
                //
                // Edge-Specific:
                // "ms:edgeOptions"     - EdgeDriver options (e.g., args, headless mode)
                // "ms:edgeChromium"    - Whether Edge is running on Chromium (true/false)
                //
                // Firefox-Specific:
                // "moz:firefoxOptions" - FirefoxDriver options
                // "moz:headless"       - Whether running in headless mode (true/false)
                //
                // Remote/Grid Execution:
                // "webdriver.remote.sessionid" - Session ID for remote WebDriver
                // "timeouts"           - Dictionary of timeouts (implicit, pageLoad, script)
            }
        }
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
            if (false == _cleanupDone)  // in case we get multiple kill signals somehow, only run this once..
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

                    BrowserHelper.KillAllRunningProcesses(true, null);

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

            // normal exit, return 0
            if (true == reason.Equals("Process Exit or console X"))
                Environment.Exit(0);

            Environment.Exit(0xdead);
        }
        #endregion <Private>
    }
}
