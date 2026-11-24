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

        // OCR provider registration, off until the GenerallyPositive.OCR package ships. It registers the same way
        // GPAL.Image does.
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
        /// Sends a message to a chat channel through its webhook: Slack, Teams, Discord, Google Chat, Telegram, ntfy or
        /// any that takes {"text": ...}. The webhook's address says which service it is.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPAL.Notify
        ///         .WithWebhook(GPAL.CredentialsFor(CredentialServiceType.StaticKey).WithKeyFromEnv("SLACK_ALERTS_WEBHOOK").ToGPALObject())
        ///         .Send("Nightly export finished: [412] rows");
        /// </code>
        /// </example>
        public static IAllowNotifyWebhook Notify
        {
            [Component(Description = "Send a message to Slack, Teams, Discord, Google Chat, Telegram or any webhook", Submenu = "Services")]
            get
            {
                return new GPALNotify();
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
        }
                 /// <summary>
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
            get => new Credentials();
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
    }
}
