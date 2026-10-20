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

using DocumentFormat.OpenXml.Wordprocessing;
using Org.BouncyCastle.Asn1.Cms;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static GenerallyPositive.Browser.Puppeteer;

//! \file Enums.cs
//! \brief GPAL enums: globally defined and used for type safety and ease-of-use
namespace GenerallyPositive
{
    /// <summary>
    /// Class that holds all the GPAL enum definitions
    /// </summary>
    public static class Enums
    {
        #region GPAL
        /// <summary>
        /// Type of this selector path entry
        /// </summary>
        [Flags]
        public enum SelectorPathType
        {
            NotSet = 0,
            /// <summary>
            /// [Browser only]
            /// </summary>
            Css = 1,
            /// <summary>
            /// [Both]
            /// </summary>
            Image = 2,
            /// <summary>
            /// [Both]
            /// </summary>
            Text = 4,
            /// <summary>
            /// [Both]
            /// </summary>
            Value = 8,
            /// <summary>
            /// [Both]
            /// </summary>
            Xpath = 16,
            /// <summary>
            /// [Both]
            /// </summary>
            Name = 32,
            /// <summary>
            /// [Application only]
            /// </summary>
            AutomationID = 64,
            /// <summary>
            /// [Application only]
            /// </summary>
            ClassName = 128,
            /// <summary>
            /// [both?]
            /// </summary>
            PlaceHolder = 256,
            /// <summary>
            /// [Browser only]
            /// </summary>
            HRef = 512
        };
        /// <summary>
        /// Specifies what a selector path entry value represents: a literal selector string, raw data, or a function that produces data.
        /// </summary>
        public enum SelectorType { Selector, Data, DataFunc }
        /// <summary>
        /// Modifier keys: Alt, Control, Shift, Windows, Applications, ScrollLock, ALL, None
        /// </summary>
        [Flags] public enum ModifierKeys { NONE = 0, Alt = 1, Control = 2, Windows = 4, Shift = 8, Application = 16, ScrollLock = 32, ALL = ~0 }

        /// <summary>
        /// Input text mode
        /// </summary>
        public enum WriteMode { Append, Insert, Overwrite }
        /// <summary>
        /// GPALObject type passed in Information and Exception channel messages.
        /// </summary>
        public enum GPALObjectType { None, AIProvider, Application, Browser, Converter, Cryptography, Database, FileSettings, GoogleCloud, GoogleSheets, GPALAI, GPALExcel, GPALFile, GPALForm, Logger, Other, Puppeteer, PuppeteerCommunicator, PuppeteerClient, RestClient, RestHelper, Selector, UnitOfWork, YouTube }
        /// <summary>
        /// Direction or unit used when scrolling an element or window.
        /// </summary>
        internal enum ScrollTypes { Horizontal, Vertical, Percent, Increments }
        /// <summary>
        /// Data format used for conversion, import/export, and serialization throughout GPAL (e.g. GPALConverter).
        /// </summary>
        public enum DataFormat { NOTSET = 0, BASE64, CARET, CLASS, COLON, CSV, CUSTOM_DELIMITER, DATABASE, DICTIONARY, DOT, GRID, HTML, HYPHEN, JSON, LOG, /*// post-v1: PDF*/ PIPE, PRN, SEMICOLON, SPACE, STRING, TAB, XLSX, XML, YAML }    // prn is psace delimnited file
        /// <summary>
        /// Order in which files matched by a wildcard pattern (e.g. <c>*.csv</c>) are added to a <see cref="GenerallyPositive.GPALFile"/>'s file list.
        /// </summary>
        public enum FileSortOrder { NameAscending = 0, NameDescending, NaturalAscending, NaturalDescending, DateModifiedAscending, DateModifiedDescending, DateCreatedAscending, DateCreatedDescending }
        /// <summary>
        /// Selects which members are copied when deep-copying an object: Fields, Properties, or both.
        /// </summary>
        public enum DeepCopy {  Fields, Properties, FieldsAndProperties }
        /// <summary>
        /// Result returned by a conditional CallIf handler, indicating whether processing should stop, continue, or fall through to the next selector.
        /// </summary>
        public enum CallIfStatus { Terminate = -1, NotHandled = 0, Handled = 1, TryNext = 2 }
        /// <summary>
        /// The failure reason passed to a CallOnFail handler. Only failures a workflow can act on are listed; others
        /// are published as events.
        /// </summary>
        public enum GPALFailure { None = 0, Navigation = 1 }
        /// <summary>
        /// Why a mail call failed, read from GPALMail.LastError. A certificate error may need WithAcceptAnyCertificate,
        /// a refused login a corrected credential, and an unanswered host neither.
        /// </summary>
        public enum MailFailure { None = 0, Configuration = 1, Certificate = 2, Connection = 3, Authentication = 4, Transfer = 5 }
        /// <summary>
        /// The site's authentication scheme, which decides how a credential is applied.<br/>
        /// <b>Basic</b>, <b>Proxy</b> and <b>Bearer</b> are sent before the first request. <b>Digest</b> is challenge
        /// and response; the browser answers it. <b>Form</b> is a login page: GPAL holds the credential, and the
        /// workflow fills the fields with selectors.
        /// </summary>
        public enum WebAuthType { None = 0, Basic = 1, Digest = 2, Proxy = 3, Bearer = 4, Form = 5, XAuthToken = 6, XApiKey = 7, ApiKey = 8 }
        /// <summary>
        /// Severity/category flags for events on GPAL's information and exception channels. NOTICE, CAUTION and FAILURE
        /// are for the workflow's own events and mirror INFO, WARNING and ERROR; GPAL never publishes them.<br/>
        /// Subscriptions match with HasFlag, so the two sets never match each other.
        /// </summary>
        [Flags] public enum GPALEventType { All = ~0, NONE = 0, INFO = 1, WARNING = 2, ERROR = 4, EXCEPTION = 8, DEBUG = 16, DEEPDEBUG = 32, NOTICE = 64, CAUTION = 128, FAILURE = 256, GPALALL = INFO | WARNING | ERROR | EXCEPTION, USERALL = NOTICE | CAUTION | FAILURE | EXCEPTION }
        /// <summary>
        /// For Grid.DeleteRow, delete the first or last row.
        /// We do have DeleteRow(rowIdx), too
        /// </summary>
        public enum RowPosition
        {
            First,
            Last
        }
        /// <summary>
        /// Casting options - tab or desktop
        /// </summary>
        internal enum CastMode
        {
            Tab,
            Desktop
        }
        /// <summary>
        /// Which origin an element's coordinates are measured from. Image matching produces one or the other
        /// depending on what was captured: a screen grab yields Screen, a visible-tab grab yields Viewport.
        /// Consumers convert between them using the content area origin from GetWindowRectangle.
        /// </summary>
        internal enum CoordinateSpace
        {
            Screen,     // absolute pixels on the primary display - what the hardware mouse takes
            Viewport    // pixels relative to the top left of the page viewport - what CDP input takes
        }
        /// <summary>
        /// ContentType is a struct that acts like a string but also an enum for some intellisense help.
        /// Users may use the enum like values: Json, Form, Text, Xml, Html, Binary, or enter any arbitrary
        /// content type, which is the point - a site's own API can ask for anything.
        /// </summary>
        public readonly struct ContentType
        {
            private readonly string _value;

            public static readonly ContentType Json = new ContentType("application/json");
            public static readonly ContentType Form = new ContentType("application/x-www-form-urlencoded");
            public static readonly ContentType Text = new ContentType("text/plain");
            public static readonly ContentType Xml = new ContentType("application/xml");
            public static readonly ContentType Html = new ContentType("text/html");
            public static readonly ContentType Binary = new ContentType("application/octet-stream");

            internal ContentType(string value) => _value = value;

            public static implicit operator ContentType(string contentType)
                => new ContentType(contentType);

            public static implicit operator string(ContentType contentType)
                => contentType._value;

            public override string ToString() => _value;
        }
        /// <summary>
        /// HttpMethod is a struct that acts like a string but also an enum for some intellisense help.
        /// Users may use the enum like values: Get, Post, Put, Patch, Delete, Head, Options, or enter any
        /// arbitrary method.
        /// </summary>
        public readonly struct HttpVerb
        {
            private readonly string _value;

            public static readonly HttpVerb Get = new HttpVerb("GET");
            public static readonly HttpVerb Post = new HttpVerb("POST");
            public static readonly HttpVerb Put = new HttpVerb("PUT");
            public static readonly HttpVerb Patch = new HttpVerb("PATCH");
            public static readonly HttpVerb Delete = new HttpVerb("DELETE");
            public static readonly HttpVerb Head = new HttpVerb("HEAD");
            public static readonly HttpVerb Options = new HttpVerb("OPTIONS");

            internal HttpVerb(string value) => _value = value;

            public static implicit operator HttpVerb(string method)
                => new HttpVerb(method);

            public static implicit operator string(HttpVerb method)
                => method._value;

            public override string ToString() => _value;
        }
        /// <summary>
        /// WaitTime is a struct that acts like an int but also an enum for some intellisense help
        /// Users may use the enum like values: Forever, Never, Immediate or enter any arbitrary integer
        /// </summary>
        public readonly struct WaitTime
        {
            private readonly int _value;

            public static readonly WaitTime Forever = new WaitTime(-1);
            public static readonly WaitTime Never = new WaitTime(-2);
            public static readonly WaitTime Immediate = new WaitTime(0);

            internal WaitTime(int value) => _value = value;

            public static implicit operator WaitTime(int ms)
                => new WaitTime(ms);

            public static implicit operator int(WaitTime wt)
                => wt._value;

            public override string ToString() => _value switch
            {
                -1 => "Forever",
                -2 => "Never",
                0 => "Immediate",
                _ => _value.ToString()
            };

            internal static bool TryFromString(string value, out WaitTime result)
            {
                result = Immediate;

                if (string.IsNullOrWhiteSpace(value))
                    return false;

                if (int.TryParse(value, out int ms))
                {
                    result = new WaitTime(ms);
                    return true;
                }

                switch (value)
                {
                    case "Forever":
                        result = Forever;
                        return true;
                    case "Never":
                        result = Never;
                        return true;
                    case "Immediate":
                        result = Immediate;
                        return true;
                }

                return false;
            }

            internal static bool TryFromDictionary(
                IDictionary<object, object> dictionary,
                out WaitTime result)
            {
                result = Immediate;

                if (dictionary == null || dictionary.Count == 0)
                {
                    result = WaitTime.Never;
                    return true;
                }

                dynamic value;

                // Expected keys (adjust if you want)
                value = dictionary.Keys.First();

                if (false == string.IsNullOrEmpty(value))
                {
                    if (value is WaitTime wt)
                    {
                        result = wt;
                        return true;
                    }

                    if (value is int i)
                    {
                        result = new WaitTime(i);
                        return true;
                    }

                    if (value is string s &&
                        TryFromString(s, out result))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
        #endregion GPAL
        #region LoggerEnums
        /// <summary>
        /// Directory layout used by the logger to organize log files by date/time components (year, month, day, hour).
        /// </summary>
        public enum DirectoryStructure
        {
            None,
            YMDH,
            YMD,
            YM,
            MDH,
            MD,
            M,
            DH,
            D,
            H,
            Julian
        }
        /// <summary>
        /// Timestamp pattern used by the logger to construct log file names.
        /// </summary>
        public enum FilenamePattern
        {
            None,
            YMDHMSm,
            YMDHMS,
            YMDHM,
            YMDH,
            YMD,
            YM,
            MM,
            Y,
            MDHMSm,
            MDHMS,
            MDHM,
            MDH,
            DHMSm,
            DHMS,
            DHM,
            DH,
            D,
            HMSm,
            HMS,
            HM,
            H,
            MSm,
            MS,
            M,
            Sm,
            S,
            m,
            Julian
        }
        /// <summary>
        /// How <see cref="GPALFile.Next"/> makes a new, unique output filename when it needs one
        /// (the target already exists, or Next is called past the user-supplied filenames).
        /// A short suffix is inserted between the base name and the extension, e.g. report.csv -&gt; report_1.csv.
        /// Deliberately small; use the logger's richer <see cref="FilenamePattern"/> if you need fine-grained timestamps.
        /// </summary>
        public enum NextFilePattern
        {
            /// <summary>report.csv -&gt; report20260723141530777.csv (yyyyMMddHHmmssfff, the original default).</summary>
            Timestamp = 0,
            /// <summary>report.csv -&gt; report_20260723_141530.csv (readable date and time).</summary>
            DateTimeStamp,
            /// <summary>report.csv -&gt; report_20260723.csv (date only).</summary>
            DateStamp,
            /// <summary>report.csv -&gt; report_1.csv, report_2.csv ... (simple incrementing counter).</summary>
            Counter,
            /// <summary>report.csv -&gt; report_0001.csv, report_0002.csv ... (zero-padded counter, the default).</summary>
            CounterPadded
        }
        /// <summary>
        /// Severity of a log entry.
        /// </summary>
        // Not [Flags]: a log entry has exactly one. Names match GPALEventType so they map by name.
        public enum LogType { INFO, WARNING, ERROR, EXCEPTION, DEBUG, DEEPDEBUG, NOTICE, CAUTION, FAILURE }
        /// <summary>
        /// Date/time format applied to timestamps written to log entries.
        /// </summary>
        public enum LogDateFormat
        {
            NOTSET,
            YMD_HMS, // yyyy-MM-dd HH:mm:ss
            YMD_HMS_FFF, // yyyy-MM-dd HH:mm:ss.fff
            DMY_HMS, // dd-MM-yyyy HH:mm:ss
            MDY_HMS, // MM-dd-yyyy HH:mm:ss
            DMY, // dd-MM-yyyy
            MDY, // MM-dd-yyyy
            YMD, // yyyy-MM-dd
            HMS, // HH:mm:ss
            HM, // HH:mm
            MDY_Long // MMMM dd, yyyy (e.g., "March 24, 2024")
        }
        #endregion
        #region <ApplicationEnums>
        /// <summary>
        /// UIAutomation search by types
        /// </summary>
        internal enum ConditionPropertyType {  NotSet, ControlType, Name, Value, AutomationID, ClassName }
        #endregion <ApplicationEnums>
        #region <BrowserEnums>
        /// <summary>
        /// Browser type to run: Chrome, Edge, Firefox, Safari [not implemented]
        /// </summary>
        public enum BrowserType { Chrome, Edge, FireFox, /*IE11, Safari*/ }
        /// <summary>
        /// The run mode for workflows defined using .WithWorkFlow
        /// </summary>
        /// <summary>
        /// Click type
        /// NOTE: LeftClick is the same action as SelectClicType.LeftClick
        /// </summary>
        public enum ClickType { LeftClick, LeftDoubleClick, MiddleClick, RightClick }
        /// <summary>
        /// Special types for Select menus: RandomSelect will pick a random menu item, SequentialSelect will walk the list (w/ and w/o wrapping)
        /// note: LeftClick is the same action as ClicType.LeftClick
        /// </summary>
        public enum SelectClickType { LeftClick, SelectRandom, SelectNext, SelectNextWithWrap, SelectPrevious, SelectPreviousWithWrap }
        /// <summary>
        /// The interaction method for this Automation/IGPALElement: Selenium, JavaScript, Hardware, UIAutomation, Puppeteer
        /// </summary>
        public enum InteractionType {
            /// <summary>
            /// [Browser only [default]]
            /// </summary>
            Selenium,
            /// <summary>
            /// [Browser only]
            /// </summary>
            JavaScript,
            /// <summary>
            /// [Both]
            /// </summary>
            Hardware,
            /// <summary>
            /// [Application only [default]]
            /// </summary>
            UIAutomation,
            /// <summary>
            /// [Browser only]
            /// </summary>
            Puppeteer,
            /// <summary>
            /// [Browser only]
            /// </summary>
            OttoMagic
        }
        /// <summary>
        /// Match type of match entry<br/>
        /// OR in Regex if using a regex value for a type
        /// Regex | Text
        /// </summary>
        [Flags] internal enum MatchType { None = 0, Regex = 1, Custom = 2, Href = 4, Placeholder = 8, Src = 16, Text = 32, Value = 64, Attribute = 128 }
        /// <summary>
        /// Kind of node a resolved element represents: a normal element, an iframe, or a shadow root.
        /// </summary>
        internal enum ElementType { Element, IFrame, ShadowRoot }
        /// <summary>
        /// Stealth techniques applied to the browser to reduce automation detection.
        /// </summary>
        [Flags]
        public enum StealthType
        {
            None = 0,
            All = ~0, // All bits set - no need to test for this explicitly
            CDP = 1 << 0, // 2^0 = 1
            DarkMode = 1 << 1, // 2^1 = 2
            GoogleReferrer = 1 << 2, // 2^2 = 4
            PatchDriver = 1 << 4, // 2^4 = 16 - patches the active driver binary (chromedriver or msedgedriver)
            // these create but don't seem to have any effect
            //GenerateFakeCookies = 1 << 5, // generate fake cookies when creating the temporary chrome profile
        }
        /// <summary>
        /// Bitmask of element states (visibility, enabled/disabled, checked, selected, etc.) that can be queried or waited on.
        /// </summary>
        public enum ElementState
        {
            NotSet = 0,
            Visible = 1,
            Hidden = 2,
            Enabled = 4,
            Disabled = 8,
            Checked = 16,
            Unchecked = 32,
            Selected = 64,
            Unselected = 128,
            Clickable = 256, // Both Visible and Enabled
            Editable = 512
        }
        /// <summary>
        /// Automation engine and interaction mode used to drive the browser (OttoMagic, Puppeteer, or Selenium, with hardware-driven [HW] variants).
        /// </summary>
        public enum AutomationEngine { OttoMagic, OttoMagicHW, PuppeteerPort, PuppeteerPortHW, Selenium, SeleniumHW, SeleniumJS, PuppeteerPipe, PuppeteerPipeHW }
        #endregion <BrowserEnums>
        #region <DatabaseEnums>
        /// <summary>
        /// What type of database is being accessed?
        /// </summary>
        public enum DatabaseType {  NotSet, SQLServer }
        /// <summary>
        /// Which form to take off the clipboard or put on it. A clipboard holds several at once: a browser copy
        /// is Text and Html, a spreadsheet copy is Text and Html and an image.
        /// </summary>
        public enum ClipboardType { Text, Html, Rtf, Csv, FileList, Image }
        #endregion <DatabaseEnums>
        #region <FormControlEnums>
        /// <summary>
        /// Type of form control in WindowsControl
        /// </summary>
        public enum FormControlType { BarItem, Button, Callback, Chart, Checkbox, ComboBox, DataGridView, DateTimePicker, FileSelector, Input, Label, ListView, MenuBar, NumericUpDown, Picture, ProgressBar, RadioButton, RichTextBox, Slider, SmallLabel, StatusStrip, Tab, TableLayoutPanel, TextArea, Toolbar, TreeView, LinkLabel, Spacer }
        public enum ListViewMode { Details, List, LargeIcon, SmallIcon, Tile }
        /// <summary>
        /// Windows Forms control event to attach a handler to via .ForEvent.
        /// </summary>
        public enum ControlEventType
        {
            AfterSelect,
            CellValueChanged,
            Click,
            Change,
            CheckedChanged,
            Default,   // means "attach to whatever the control thinks is its primary event" - no .ForEvent specified
            DoubleClick,
            DragDrop,
            DragEnter,
            DragLeave,
            DragOver,
            ItemChecked,
            KeyDown,
            Scroll,
            SelectedIndexChanged,
            SplitterMoved,
            TextChanged,
            ValueChanged,
            // extend as needed
        }
        #endregion <FormControlEnums>
        #region Credential Enums
        /// <summary>
        /// Password manager or cloud identity service used to retrieve or store credentials via GPAL.CredentialsFor.
        /// </summary>
        public enum CredentialServiceType
        {
            LastPass,
            OnePassword,
            Dashlane,
            Bitwarden,
            Keeper,
            Google,
            Azure,
            AWS,
            None,
            /// <summary>Static API key used directly as the Bearer/x-api-key header value - set via .WithServiceKey, .WithKeyFromEnv, or .WithKeyFromApi. No login step or token fetch involved.</summary>
            StaticKey,
            /*
            Dropbox,
            Kaspersky,
            LastPass,
            OptVault,
            RoboForm,
            Sticky,
            TrueKey,
            Zoho
             */
        }
        /// <summary>
        /// OAuth scope requested when fetching an access token for a Google, Azure, or AWS service via .WithScope.
        /// </summary>
        public enum OAuthScope
        {
            // Google Scopes
            Google_Sheets,       // https://www.googleapis.com/auth/spreadsheets
            Google_Drive,        // https://www.googleapis.com/auth/drive
            Google_CloudPlatform,// https://www.googleapis.com/auth/cloud-platform

            // Azure Scopes (Microsoft Entra ID / Microsoft Graph)
            Azure_GraphUserRead, // https://graph.microsoft.com/User.Read
            Azure_GraphMailRead, // https://graph.microsoft.com/Mail.Read
            Azure_Storage,       // https://storage.azure.com/user_impersonation
            Azure_Management,    // https://management.azure.com/user_impersonation

            // AWS Scopes (Cognito or API Gateway)
            AWS_CognitoOpenId,   // openid (for AWS Cognito user pools)
            AWS_CognitoProfile,  // profile (for AWS Cognito user attributes)
            AWS_APIGatewayCustom, // Custom scope for API Gateway (e.g., myapp/read)

            // YouTube
            YouTube_Upload,      // https://www.googleapis.com/auth/youtube.upload
            YouTube_Manage       // https://www.googleapis.com/auth/youtube (read, edit, delete, playlists, thumbnails)
        }

        /// <summary>
        /// Video privacy status for a YouTube upload via GPAL.YouTube.
        /// </summary>
        public enum YouTubePrivacy { Public, Private, Unlisted }

        /// <summary>
        /// YouTube Data API v3 video category. Enum value equals the YouTube category ID.
        /// </summary>
        public enum YouTubeCategory
        {
            FilmAnimation      = 1,
            AutosVehicles      = 2,
            Music              = 10,
            PetsAnimals        = 15,
            Sports             = 17,
            TravelEvents       = 19,
            Gaming             = 20,
            PeopleBlogs        = 22,
            Comedy             = 23,
            Entertainment      = 24,
            NewsPolitics       = 25,
            HowtoStyle         = 26,
            Education          = 27,
            ScienceTechnology  = 28,
            NonprofitsActivism = 29
        }

        #endregion Credential Enums
        #region REST API
        /// <summary>
        /// Identifies a GPAL operation that is dispatched to the browser/application backend (Puppeteer, OttoMagic, or GPALRESTAPI), one value per supported action.
        /// </summary>
        public enum ApiEndpoint
        {
            Back,                       // history.back()
            CaptureVisibleTab,
            CastDesktop,                // NOTE: since we use these endpoints for puppeteer and rest, these must stay but are not availahble to rest
            CastTab,                    // NOTE: since we use these endpoints for puppeteer and rest, these must stay but are not availahble to rest
            CheckNetworkIdle,           // ensure no more than 'maxConnections' are active - better than document.readyState in some scenarios
            CheckStatus,                // same as getreadystatus, not sure why we have two...
            ClearReferrer,
            CloseBrowser,
            CloseTab,
            CloseWindow,
            DeleteStorage,
            DownloadUrl,                // the browser downloads a url itself, with no tab and no Save As
            DragAndDrop,
            EvaluatePersistent,                   // xpath
            EvaluateAllPersistent,                // xpath multiple elements
            Evaluate,                   // xpath
            EvaluateAll,                // xpath multiple elements
            ExecuteJavaScript,
            Fetch,                      // issue an api request from inside the page, carrying its session
            FillInAppend,
            FillInInsert,
            FillInOverwrite,
            FireChangeEvent,
            Focus,
            Forward,                    // history.forward()
            FullScreen,
            GetAttribute,
            GetBoundingClientRect,
            ElementFromPoint,
            GetBrowserSettings,
            GetContentAndCss,
            GetCssAttributes,
            GetCurrentUrl,
            GetCurrentWindow,
            GetDocumentStamp,
            GetHydrationScripts,
            GetDomAttributes,
            GetDomProperties,
            GetElementAttributeHash,
            GetGpalSettings,
            GetPageSource,
            GetParentNode,
            GetReadyStatus,
            GetStatusCode,              // HTTP status of the active tab's page
            GetSettings,
            GetShadowRoot,  // NOTE: CAVEAT: this may not make any sense
            GetLanguages,
            CaptureCalls,               // start or stop recording what the page asks for
            GetCapturedCalls,           // everything recorded so far, oldest first
            GetStorage,
            GetUserAgent,
            GetWindowRectangle,
            GetWorkflow,
            GoTo,
            GoToTab,
            GoToWindow,
            HideElement,
            Hover,
            InjectScript,
            ClearInjectedScripts,
            IsClickable,
            IsDisplayed,
            IsEnabled,
            IsEndOfPage,
            IsVisibleInViewport,
            LeftClick,
            LeftClickAndDownload,
            LeftDoubleClick,
            MiddleClick,
            Maximize,
            Minimize,
            MoveTo,
            MoveToPoint,                // move the mouse to a viewport coordinate rather than to an element
            ClickPoint,                 // click a viewport coordinate rather than an element
            NewTab,
            NextTab,
            NextWindow,
            Normal,                     // restore window after fullscreen
            OpenWindow,
            OverrideReferrer,
            PageDown,
            PageEnd,
            PageTop,
            PageUp,
            PressModifierKey,
            PreviousTab,
            PreviousWindow,
            QueryPersistentSelector,              // css
            QueryPersistentSelectors,             // css multiple elements
            QuerySelector,              // css
            QuerySelectors,             // css multiple elements
            Refresh,
            ReleaseModifierKey,
            Restore,
            RightClick,
            ScrollWindowByHorizontal,
            ScrollWindowByVertical,
            ScrollElement,
            ScrollIntoView,
            ScrollWindow,
            SelectClick,
            SendKey,
            SendString,
            SetAttribute,
            SetDownloadFilename,
            SetRange,
            SetStorage,
            SetStorageCache,
            SetStorageIndexedDb,
            SetUserAgent,
            SetValueFromElement,
            SetGoogleReferrer,
            StopCasting,
            SwitchToDefaultContent,
            SwitchToElement,
            SwitchToFrame,
            SwitchToShadowRoot,
            TabCount,                   // how many tabs the browser actually has, not how many we opened
            TopBrowser,
            TrustedLeftClick,
            TrustedMiddleClick,
            Upload,
            WindowInnerHeight,
            WindowInnerWidth,
            WindowOuterHeight,
            WindowOuterWidth,
            WindowPageOffsetX,
            WindowPageOffsetY,
            WindowScreenLeft,
            WindowScreenTop
        }
        /// <summary>
        /// Content encoding used when sending a request body to a REST endpoint.
        /// </summary>
        public enum ContentEncoding
        {
            Json,
            UrlEncoded
            // Add more (e.g., MultipartFormData, Xml, Text) if needed later
        }
        #endregion REST API
        #region AI
        /// <summary>
        /// Type of AI task to perform via a configured AI provider.
        /// </summary>
        public enum AITask
        {
            Classification,
            Summarization,
            TextGeneration,
            DataAugmentation
        }

        /// <summary>
        /// Classification category requested when AITask.Classification is used.
        /// </summary>
        public enum AIClassificationType
        {
            Sentiment,
            Spam,
            Topic,
            Intent,
            Emotion,
            Toxicity,
            Language,
            Category,
            Priority,
            Relevance,
            Tone,
            Urgency,
            IntentComplexity,
            Risk,
            ContentType,
            Engagement,
            Authenticity,
            LanguageProficiency,
            SentimentConfidence,
            Custom
        }

        /// <summary>
        /// AI provider/API to send prompts to via GPAL.AI.WithProvider. Named constants cover XAI, OpenAI,
        /// and Anthropic; any other string converts implicitly, looked up against AIProvidersConfig.
        /// </summary>
        public readonly struct AIProviderType
        {
            public static readonly AIProviderType XAI = new AIProviderType("XAI");
            public static readonly AIProviderType OpenAI = new AIProviderType("OpenAI");
            public static readonly AIProviderType Anthropic = new AIProviderType("Anthropic");

            public string Name { get; }
            public AIProviderType(string name) { Name = name; }
            public static implicit operator AIProviderType(string name) => new AIProviderType(name);
            public static bool operator ==(AIProviderType a, AIProviderType b) => string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            public static bool operator !=(AIProviderType a, AIProviderType b) => !(a == b);
            public override bool Equals(object obj) => obj is AIProviderType other && this == other;
            public override int GetHashCode() => Name == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
            public override string ToString() => Name;
        }

        /// <summary>
        /// Model ID to request from the AI provider via GPAL.AI.WithModel. Acts like an enum of well-known
        /// model IDs per provider, but also accepts any freehand string (e.g. a brand new model release
        /// that predates this enum) via an implicit conversion from string.
        /// </summary>
        public readonly struct AIModel
        {
            private readonly string _value;

            internal AIModel(string value) => _value = value;

            // xAI
            public static readonly AIModel Grok2Latest = new AIModel("grok-2-latest");
            public static readonly AIModel Grok3Latest = new AIModel("grok-3-latest");

            // OpenAI
            public static readonly AIModel GPT4o = new AIModel("gpt-4o");
            public static readonly AIModel GPT4oMini = new AIModel("gpt-4o-mini");
            public static readonly AIModel O3Mini = new AIModel("o3-mini");

            // Anthropic (current)
            public static readonly AIModel ClaudeHaiku45 = new AIModel("claude-haiku-4-5-20251001");
            public static readonly AIModel ClaudeSonnet46 = new AIModel("claude-sonnet-4-6");
            public static readonly AIModel ClaudeOpus5 = new AIModel("claude-opus-5");
            public static readonly AIModel ClaudeFable5 = new AIModel("claude-fable-5");

            /// <summary>Any model ID string, including ones not yet listed above.</summary>
            public static implicit operator AIModel(string modelId) => new AIModel(modelId);

            public override string ToString() => _value;
        }
        #endregion AI
        #region <Helpers>
        /// <summary>
        /// Get the index of the passed in Enum. Used only to quickly call a method using the SelectorType
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bitsToTest"></param>
        /// <returns></returns>
        internal static int GetIndexOfEnum<T>(int bitsToTest) where T : Enum
        {
            int idx = 0;

            foreach (string evalName in Enum.GetNames(typeof(T)))
            {
                int testBit = (int)Enum.Parse(typeof(T), evalName);

                if (0 != testBit && testBit == (testBit & bitsToTest))
                    break;
                idx++;
            }
            return idx;
        }
        #endregion <Helpers>
        #region Puppeteer
        /// <summary>
        /// Image format used when capturing a screenshot.
        /// </summary>
        public enum ImageFormat
        {
            JPEG, PNG
        }
        /// <summary>
        /// Page orientation used when generating a PDF.
        /// </summary>
        public enum PageOrientation
        {
            [Description("false")]
            Portrait,

            [Description("true")]
            Landscape
        }
        /// <summary>
        /// Standard page size used when generating a PDF.
        /// </summary>
        public enum PageFormat
        {
            [Description("A0")]
            A0,

            [Description("A1")]
            A1,

            [Description("A2")]
            A2,

            [Description("A3")]
            A3,

            [Description("A4")]
            A4,

            [Description("A5")]
            A5,

            [Description("A6")]
            A6,

            [Description("Letter")]
            Letter,

            [Description("Legal")]
            Legal,

            [Description("Tabloid")]
            Tabloid,

            [Description("Ledger")]
            Ledger
        }
        /// <summary>
        /// Chrome DevTools Protocol (CDP) command and event names, mapped to their wire-format "Domain.method" strings via the [Command] attribute. Used internally by the Puppeteer engine to send raw CDP messages.
        /// </summary>
        public enum DevToolsMethods
        {
            [Command("Accessibility.disable")]
            AccessibilityDisable,
            [Command("Accessibility.enable")]
            AccessibilityEnable,
            [Command("Accessibility.getChildAXNodes")]
            AccessibilityGetChildAXNodes,
            [Command("Accessibility.getFullAXTree")]
            AccessibilityGetFullAXTree,
            [Command("Accessibility.getPartialAXTree")]
            AccessibilityGetPartialAXTree,
            [Command("Accessibility.getRootAXNode")]
            AccessibilityGetRootAXNode,
            [Command("Accessibility.queryAXTree")]
            AccessibilityQueryAXTree,

            [Command("Animation.disable")]
            AnimationDisable,
            [Command("Animation.enable")]
            AnimationEnable,
            [Command("Animation.getCurrentTime")]
            AnimationGetCurrentTime,
            [Command("Animation.getPlaybackRate")]
            AnimationGetPlaybackRate,
            [Command("Animation.releaseAnimations")]
            AnimationReleaseAnimations,
            [Command("Animation.resolveAnimation")]
            AnimationResolveAnimation,
            [Command("Animation.seekAnimations")]
            AnimationSeekAnimations,
            [Command("Animation.setPaused")]
            AnimationSetPaused,
            [Command("Animation.setPlaybackRate")]
            AnimationSetPlaybackRate,
            [Command("Animation.setTiming")]
            AnimationSetTiming,

            [Command("Audits.disable")]
            AuditsDisable,
            [Command("Audits.enable")]
            AuditsEnable,
            [Command("Audits.checkContrast")]
            AuditsCheckContrast,
            [Command("Audits.getEncodedResponse")]
            AuditsGetEncodedResponse,

            [Command("BackgroundService.startObserving")]
            BackgroundServiceStartObserving,
            [Command("BackgroundService.stopObserving")]
            BackgroundServiceStopObserving,
            [Command("BackgroundService.setRecording")]
            BackgroundServiceSetRecording,
            [Command("BackgroundService.clearEvents")]
            BackgroundServiceClearEvents,

            [Command("Browser.setPermission")]
            BrowserSetPermission,
            [Command("Browser.grantPermissions")]
            BrowserGrantPermissions,
            [Command("Browser.resetPermissions")]
            BrowserResetPermissions,
            [Command("Browser.setDownloadBehavior")]
            BrowserSetDownloadBehavior,
            [Command("Browser.cancelDownload")]
            BrowserCancelDownload,
            [Command("Browser.close")]
            BrowserClose,
            [Command("Browser.crash")]
            BrowserCrash,
            [Command("Browser.crashGpuProcess")]
            BrowserCrashGpuProcess,
            [Command("Browser.getVersion")]
            BrowserGetVersion,
            [Command("Browser.getBrowserCommandLine")]
            BrowserGetBrowserCommandLine,
            [Command("Browser.getHistograms")]
            BrowserGetHistograms,
            [Command("Browser.getHistogram")]
            BrowserGetHistogram,
            [Command("Browser.getWindowBounds")]
            BrowserGetWindowBounds,
            [Command("Browser.getWindowForTarget")]
            BrowserGetWindowForTarget,
            [Command("Browser.setWindowBounds")]
            BrowserSetWindowBounds,
            [Command("Browser.setDockTile")]
            BrowserSetDockTile,
            [Command("Browser.executeBrowserCommand")]
            BrowserExecuteBrowserCommand, 
            [Command("Browser.showCastUi")]
            BrowserShowCastUi,

            [Command("CSS.addRule")]
            CSSAddRule,
            [Command("CSS.collectClassNames")]
            CSSCollectClassNames,
            [Command("CSS.createStyleSheet")]
            CSSCreateStyleSheet,
            [Command("CSS.disable")]
            CSSDisable,
            [Command("CSS.enable")]
            CSSEnable,
            [Command("CSS.forcePseudoState")]
            CSSForcePseudoState,
            [Command("CSS.getBackgroundColors")]
            CSSGetBackgroundColors,
            [Command("CSS.getComputedStyleForNode")]
            CSSGetComputedStyleForNode,
            [Command("CSS.getInlineStylesForNode")]
            CSSGetInlineStylesForNode,
            [Command("CSS.getMatchedStylesForNode")]
            CSSGetMatchedStylesForNode,
            [Command("CSS.getMediaQueries")]
            CSSGetMediaQueries,
            [Command("CSS.getPlatformFontsForNode")]
            CSSGetPlatformFontsForNode,
            [Command("CSS.getStyleSheetText")]
            CSSGetStyleSheetText,
            [Command("CSS.getLayers")]
            CSSGetLayers,
            [Command("CSS.trackComputedStyleUpdates")]
            CSSTrackComputedStyleUpdates,
            [Command("CSS.takeComputedStyleUpdates")]
            CSSTakeComputedStyleUpdates,
            [Command("CSS.setEffectivePropertyValueForNode")]
            CSSSetEffectivePropertyValueForNode,
            [Command("CSS.setKeyframeKey")]
            CSSSetKeyframeKey,
            [Command("CSS.setMediaText")]
            CSSSetMediaText,
            [Command("CSS.setContainerQueryText")]
            CSSSetContainerQueryText,
            [Command("CSS.setSupportsText")]
            CSSSetSupportsText,
            [Command("CSS.setScopeText")]
            CSSSetScopeText,
            [Command("CSS.setRuleSelector")]
            CSSSetRuleSelector,
            [Command("CSS.setStyleSheetText")]
            CSSSetStyleSheetText,
            [Command("CSS.setStyleTexts")]
            CSSSetStyleTexts,
            [Command("CSS.startRuleUsageTracking")]
            CSSStartRuleUsageTracking,
            [Command("CSS.stopRuleUsageTracking")]
            CSSStopRuleUsageTracking,
            [Command("CSS.takeCoverageDelta")]
            CSSTakeCoverageDelta,
            [Command("CSS.setLocalFontsEnabled")]
            CSSSetLocalFontsEnabled,

            [Command("CacheStorage.deleteCache")]
            CacheStorageDeleteCache,
            [Command("CacheStorage.deleteEntry")]
            CacheStorageDeleteEntry,
            [Command("CacheStorage.requestCacheNames")]
            CacheStorageRequestCacheNames,
            [Command("CacheStorage.requestCachedResponse")]
            CacheStorageRequestCachedResponse,
            [Command("CacheStorage.requestEntries")]
            CacheStorageRequestEntries,

            [Command("Cast.enable")]
            CastEnable,
            [Command("Cast.disable")]
            CastDisable,
            [Command("Cast.setSinkToUse")]
            CastSetSinkToUse,
            [Command("Cast.startDesktopMirroring")]
            CastStartDesktopMirroring,
            [Command("Cast.startTabMirroring")]
            CastStartTabMirroring,
            [Command("Cast.stopCasting")]
            CastStopCasting,
            [Command("Cast.sinksUpdated")]   // Event, not command
            CastSinksUpdated,
            [Command("Cast.issueUpdated")]
            CastIssueUpdated,

            [Command("DOM.collectClassNamesFromSubtree")]
            DOMCollectClassNamesFromSubtree,
            [Command("DOM.copyTo")]
            DOMCopyTo,
            [Command("DOM.describeNode")]
            DOMDescribeNode,
            [Command("DOM.scrollIntoViewIfNeeded")]
            DOMScrollIntoViewIfNeeded,
            [Command("DOM.disable")]
            DOMDisable,
            [Command("DOM.discardSearchResults")]
            DOMDiscardSearchResults,
            [Command("DOM.enable")]
            DOMEnable,
            [Command("DOM.focus")]
            DOMFocus,
            [Command("DOM.getAttributes")]
            DOMGetAttributes,
            [Command("DOM.getBoxModel")]
            DOMGetBoxModel,
            [Command("DOM.getContentQuads")]
            DOMGetContentQuads,
            [Command("DOM.getDocument")]
            DOMGetDocument,
            [Command("DOM.getExceptionDetails")]
            DOMGetExceptionDetails,
            [Command("DOM.getFlattenedDocument")]
            DOMGetFlattenedDocument,
            [Command("DOM.getNodesForSubtreeByStyle")]
            DOMGetNodesForSubtreeByStyle,
            [Command("DOM.getNodeForLocation")]
            DOMGetNodeForLocation,
            [Command("DOM.getOuterHTML")]
            DOMGetOuterHTML,
            [Command("DOM.getRelatableNode")]
            DOMGetRelatableNode,
            [Command("DOM.getRelatedNodes")]
            DOMGetRelatedNodes,
            [Command("DOM.getSearchResults")]
            DOMGetSearchResults,
            [Command("DOM.hideHighlight")]
            DOMHideHighlight,
            [Command("DOM.highlightNode")]
            DOMHighlightNode,
            [Command("DOM.highlightQuad")]
            DOMHighlightQuad,
            [Command("DOM.markUndoableState")]
            DOMMarkUndoableState,
            [Command("DOM.moveTo")]
            DOMMoveTo,
            [Command("DOM.performSearch")]
            DOMPerformSearch,
            [Command("DOM.pushNodeByPathToFrontend")]
            DOMPushNodeByPathToFrontend,
            [Command("DOM.pushNodesByBackendIdsToFrontend")]
            DOMPushNodesByBackendIdsToFrontend,
            [Command("DOM.querySelector")]
            DOMQuerySelector,
            [Command("DOM.querySelectorAll")]
            DOMQuerySelectorAll,
            [Command("DOM.redo")]
            DOMRedo,
            [Command("DOM.removeAttribute")]
            DOMRemoveAttribute,
            [Command("DOM.removeNode")]
            DOMRemoveNode,
            [Command("DOM.requestChildNodes")]
            DOMRequestChildNodes,
            [Command("DOM.requestNode")]
            DOMRequestNode,
            [Command("DOM.resolveNode")]
            DOMResolveNode,
            [Command("DOM.setAttributeValue")]
            DOMSetAttributeValue,
            [Command("DOM.setAttributesAsText")]
            DOMSetAttributesAsText,
            [Command("DOM.setFileInputFiles")]
            DOMSetFileInputFiles,
            [Command("DOM.setNodeStackTracesEnabled")]
            DOMSetNodeStackTracesEnabled,
            [Command("DOM.getNodeStackTraces")]
            DOMGetNodeStackTraces,
            [Command("DOM.getFileInfo")]
            DOMGetFileInfo,
            [Command("DOM.setInspectedNode")]
            DOMSetInspectedNode,
            [Command("DOM.setNodeName")]
            DOMSetNodeName,
            [Command("DOM.setNodeValue")]
            DOMSetNodeValue,
            [Command("DOM.setOuterHTML")]
            DOMSetOuterHTML,
            [Command("DOM.undo")]
            DOMUndo,
            [Command("DOM.getFrameOwner")]
            DOMGetFrameOwner,
            [Command("DOM.getContainerForNode")]
            DOMGetContainerForNode,
            [Command("DOM.getQueryingDescendantsForContainer")]
            DOMGetQueryingDescendantsForContainer,

            [Command("DOMDebugger.getEventListeners")]
            DOMDebuggerGetEventListeners,
            [Command("DOMDebugger.removeDOMBreakpoint")]
            DOMDebuggerRemoveDOMBreakpoint,
            [Command("DOMDebugger.removeEventListenerBreakpoint")]
            DOMDebuggerRemoveEventListenerBreakpoint,
            [Command("DOMDebugger.removeInstrumentationBreakpoint")]
            DOMDebuggerRemoveInstrumentationBreakpoint,
            [Command("DOMDebugger.removeXHRBreakpoint")]
            DOMDebuggerRemoveXHRBreakpoint,
            [Command("DOMDebugger.setBreakOnCSPViolation")]
            DOMDebuggerSetBreakOnCSPViolation,
            [Command("DOMDebugger.setDOMBreakpoint")]
            DOMDebuggerSetDOMBreakpoint,
            [Command("DOMDebugger.setEventListenerBreakpoint")]
            DOMDebuggerSetEventListenerBreakpoint,
            [Command("DOMDebugger.setInstrumentationBreakpoint")]
            DOMDebuggerSetInstrumentationBreakpoint,
            [Command("DOMDebugger.setXHRBreakpoint")]
            DOMDebuggerSetXHRBreakpoint,

            [Command("DOMSnapshot.disable")]
            DOMSnapshotDisable,
            [Command("DOMSnapshot.enable")]
            DOMSnapshotEnable,
            [Command("DOMSnapshot.captureSnapshot")]
            DOMSnapshotCaptureSnapshot,

            [Command("DOMStorage.clear")]
            DOMStorageClear,
            [Command("DOMStorage.disable")]
            DOMStorageDisable,
            [Command("DOMStorage.enable")]
            DOMStorageEnable,
            [Command("DOMStorage.getDOMStorageItems")]
            DOMStorageGetDOMStorageItems,
            [Command("DOMStorage.removeDOMStorageItem")]
            DOMStorageRemoveDOMStorageItem,
            [Command("DOMStorage.setDOMStorageItem")]
            DOMStorageSetDOMStorageItem,

            [Command("Database.disable")]
            DatabaseDisable,
            [Command("Database.enable")]
            DatabaseEnable,
            [Command("Database.executeSQL")]
            DatabaseExecuteSQL,
            [Command("Database.getDatabaseTableNames")]
            DatabaseGetDatabaseTableNames,

            [Command("DeviceAccess.cancelPrompt")]
            DeviceAccessCancelPrompt,
            [Command("DeviceAccess.selectPrompt")]
            DeviceAccessSelectPrompt,

            [Command("DeviceOrientation.clearDeviceOrientationOverride")]
            DeviceOrientationClearDeviceOrientationOverride,
            [Command("DeviceOrientation.setDeviceOrientationOverride")]
            DeviceOrientationSetDeviceOrientationOverride,

            [Command("Emulation.canEmulate")]
            EmulationCanEmulate,
            [Command("Emulation.clearDeviceMetricsOverride")]
            EmulationClearDeviceMetricsOverride,
            [Command("Emulation.clearGeolocationOverride")]
            EmulationClearGeolocationOverride,
            [Command("Emulation.clearIdleOverride")]
            EmulationClearIdleOverride,
            [Command("Emulation.resetPageScaleFactor")]
            EmulationResetPageScaleFactor,
            [Command("Emulation.setAutomationOverride")]
            EmulationSetAutomationOverride,
            [Command("Emulation.setCPUThrottlingRate")]
            EmulationSetCPUThrottlingRate,
            [Command("Emulation.setDefaultBackgroundColorOverride")]
            EmulationSetDefaultBackgroundColorOverride,
            [Command("Emulation.setDeviceMetricsOverride")]
            EmulationSetDeviceMetricsOverride,
            [Command("Emulation.setDisabledImageTypes")]
            EmulationSetDisabledImageTypes,
            [Command("Emulation.setEmitTouchEventsForMouse")]
            EmulationSetEmitTouchEventsForMouse,
            [Command("Emulation.setEmulatedMedia")]
            EmulationSetEmulatedMedia,
            [Command("Emulation.setEmulatedVisionDeficiency")]
            EmulationSetEmulatedVisionDeficiency,
            [Command("Emulation.setFocusEmulationEnabled")]
            EmulationSetFocusEmulationEnabled,
            [Command("Emulation.setGeolocationOverride")]
            EmulationSetGeolocationOverride,
            [Command("Emulation.setHardwareConcurrencyOverride")]
            EmulationSetHardwareConcurrencyOverride,
            [Command("Emulation.setIdleOverride")]
            EmulationSetIdleOverride,
            [Command("Emulation.setLocaleOverride")]
            EmulationSetLocaleOverride,
            [Command("Emulation.setPageScaleFactor")]
            EmulationSetPageScaleFactor,
            [Command("Emulation.setScriptExecutionDisabled")]
            EmulationSetScriptExecutionDisabled,
            [Command("Emulation.setScrollbarsHidden")]
            EmulationSetScrollbarsHidden,
            [Command("Emulation.setTimezoneOverride")]
            EmulationSetTimezoneOverride,
            [Command("Emulation.setTouchEmulationEnabled")]
            EmulationSetTouchEmulationEnabled,
            [Command("Emulation.setUserAgentOverride")]
            EmulationSetUserAgentOverride,
            [Command("Emulation.setVirtualTimePolicy")]
            EmulationSetVirtualTimePolicy,
            [Command("Emulation.setVisibleSize")]
            EmulationSetVisibleSize,

            [Command("Runtime.evaluate")]
            EvaluateJavaScript,

            [Command("EventBreakpoints.setInstrumentationBreakpoint")]
            EventBreakpointsSetInstrumentationBreakpoint,
            [Command("EventBreakpoints.removeInstrumentationBreakpoint")]
            EventBreakpointsRemoveInstrumentationBreakpoint,

            [Command("Extensions.install")]
            ExtensionsInstall,
            [Command("Extensions.uninstall")]
            ExtensionsUninstall,
            [Command("Extensions.enable")]
            ExtensionsEnable,
            [Command("Extensions.disable")]
            ExtensionsDisable,
            [Command("Extensions.getExtensions")]
            ExtensionsGetExtensions,
            [Command("Extensions.getManifest")]
            ExtensionsGetManifest,
            [Command("Extensions.sendCommand")]
            ExtensionsSendCommand,
            [Command("Extensions.addResource")]
            ExtensionsAddResource,
            [Command("Extensions.removeResource")]
            ExtensionsRemoveResource,
            [Command("Extensions.reload")]
            ExtensionsReload,
                
            [Command("Fetch.disable")]
            FetchDisable,
            [Command("Fetch.enable")]
            FetchEnable,
            [Command("Fetch.failRequest")]
            FetchFailRequest,
            [Command("Fetch.fulfillRequest")]
            FetchFulfillRequest,
            [Command("Fetch.continueRequest")]
            FetchContinueRequest,
            [Command("Fetch.continueWithAuth")]
            FetchContinueWithAuth,
            [Command("Fetch.continueResponse")]
            FetchContinueResponse,
            [Command("Fetch.getResponseBody")]
            FetchGetResponseBody,
            [Command("Fetch.takeResponseBodyAsStream")]
            FetchTakeResponseBodyAsStream,

            [Command("HeadlessExperimental.beginFrame")]
            HeadlessExperimentalBeginFrame,
            [Command("HeadlessExperimental.disable")]
            HeadlessExperimentalDisable,
            [Command("HeadlessExperimental.enable")]
            HeadlessExperimentalEnable,

            [Command("HeapProfiler.addInspectedHeapObject")]
            HeapProfilerAddInspectedHeapObject,
            [Command("HeapProfiler.collectGarbage")]
            HeapProfilerCollectGarbage,
            [Command("HeapProfiler.disable")]
            HeapProfilerDisable,
            [Command("HeapProfiler.enable")]
            HeapProfilerEnable,
            [Command("HeapProfiler.getHeapObjectId")]
            HeapProfilerGetHeapObjectId,
            [Command("HeapProfiler.getObjectByHeapObjectId")]
            HeapProfilerGetObjectByHeapObjectId,
            [Command("HeapProfiler.getSamplingProfile")]
            HeapProfilerGetSamplingProfile,
            [Command("HeapProfiler.startSampling")]
            HeapProfilerStartSampling,
            [Command("HeapProfiler.startTrackingHeapObjects")]
            HeapProfilerStartTrackingHeapObjects,
            [Command("HeapProfiler.stopSampling")]
            HeapProfilerStopSampling,
            [Command("HeapProfiler.stopTrackingHeapObjects")]
            HeapProfilerStopTrackingHeapObjects,
            [Command("HeapProfiler.takeHeapSnapshot")]
            HeapProfilerTakeHeapSnapshot,

            [Command("IndexedDB.clearObjectStore")]
            IndexedDBClearObjectStore,
            [Command("IndexedDB.deleteDatabase")]
            IndexedDBDeleteDatabase,
            [Command("IndexedDB.deleteObjectStoreEntry")]
            IndexedDBDeleteObjectStoreEntry,
            [Command("IndexedDB.disable")]
            IndexedDBDisable,
            [Command("IndexedDB.enable")]
            IndexedDBEnable,
            [Command("IndexedDB.requestData")]
            IndexedDBRequestData,
            [Command("IndexedDB.getKeyRange")]
            IndexedDBGetKeyRange,
            [Command("IndexedDB.requestDatabase")]
            IndexedDBRequestDatabase,
            [Command("IndexedDB.requestDatabaseNames")]
            IndexedDBRequestDatabaseNames,

            [Command("Input.dispatchKeyEvent")]
            InputDispatchKeyEvent,
            [Command("Input.imeSetComposition")]
            InputImeSetComposition,
            [Command("Input.insertText")]
            InputInsertText,
            [Command("Input.dispatchMouseEvent")]
            InputDispatchMouseEvent,
            [Command("Input.dispatchTouchEvent")]
            InputDispatchTouchEvent,
            [Command("Input.emulateTouchFromMouseEvent")]
            InputEmulateTouchFromMouseEvent,
            [Command("Input.setIgnoreInputEvents")]
            InputSetIgnoreInputEvents,
            [Command("Input.setInterceptDrags")]
            InputSetInterceptDrags,
            [Command("Input.synthesizePinchGesture")]
            InputSynthesizePinchGesture,
            [Command("Input.synthesizeScrollGesture")]
            InputSynthesizeScrollGesture,
            [Command("Input.synthesizeTapGesture")]
            InputSynthesizeTapGesture,
            [Command("Input.cancelDragging")]
            InputCancelDragging,

            [Command("Inspector.disable")]
            InspectorDisable,
            [Command("Inspector.enable")]
            InspectorEnable,

            [Command("LayerTree.compositingReasons")]
            LayerTreeCompositingReasons,
            [Command("LayerTree.disable")]
            LayerTreeDisable,
            [Command("LayerTree.enable")]
            LayerTreeEnable,
            [Command("LayerTree.loadSnapshot")]
            LayerTreeLoadSnapshot,
            [Command("LayerTree.makeSnapshot")]
            LayerTreeMakeSnapshot,
            [Command("LayerTree.profileSnapshot")]
            LayerTreeProfileSnapshot,
            [Command("LayerTree.releaseSnapshot")]
            LayerTreeReleaseSnapshot,
            [Command("LayerTree.replaySnapshot")]
            LayerTreeReplaySnapshot,
            [Command("LayerTree.snapshotCommandLog")]
            LayerTreeSnapshotCommandLog,

            [Command("Log.clear")]
            LogClear,
            [Command("Log.disable")]
            LogDisable,
            [Command("Log.enable")]
            LogEnable,
            [Command("Log.startViolationsReport")]
            LogStartViolationsReport,
            [Command("Log.stopViolationsReport")]
            LogStopViolationsReport,

            [Command("Media.enable")]
            MediaEnable,
            [Command("Media.disable")]
            MediaDisable,

            [Command("Memory.getDOMCounters")]
            MemoryGetDOMCounters,
            [Command("Memory.prepareForLeakDetection")]
            MemoryPrepareForLeakDetection,
            [Command("Memory.forciblyPurgeJavaScriptMemory")]
            MemoryForciblyPurgeJavaScriptMemory,
            [Command("Memory.setPressureNotificationsSuppressed")]
            MemorySetPressureNotificationsSuppressed,
            [Command("Memory.simulatePressureNotification")]
            MemorySimulatePressureNotification,
            [Command("Memory.startSampling")]
            MemoryStartSampling,
            [Command("Memory.stopSampling")]
            MemoryStopSampling,
            [Command("Memory.getAllTimeSamplingProfile")]
            MemoryGetAllTimeSamplingProfile,
            [Command("Memory.getBrowserSamplingProfile")]
            MemoryGetBrowserSamplingProfile,
            [Command("Memory.getSamplingProfile")]
            MemoryGetSamplingProfile,

            [Command("Performance.disable")]
            PerformanceDisable,
            [Command("Performance.enable")]
            PerformanceEnable,
            [Command("Performance.setTimeDomain")]
            PerformanceSetTimeDomain,
            [Command("Performance.getMetrics")]
            PerformanceGetMetrics,

            [Command("PerformanceTimeline.enable")]
            PerformanceTimelineEnable,

            [Command("Profiler.disable")]
            ProfilerDisable,
            [Command("Profiler.enable")]
            ProfilerEnable,
            [Command("Profiler.getBestEffortCoverage")]
            ProfilerGetBestEffortCoverage,
            [Command("Profiler.setSamplingInterval")]
            ProfilerSetSamplingInterval,
            [Command("Profiler.start")]
            ProfilerStart,
            [Command("Profiler.startPreciseCoverage")]
            ProfilerStartPreciseCoverage,
            [Command("Profiler.startTypeProfile")]
            ProfilerStartTypeProfile,
            [Command("Profiler.stop")]
            ProfilerStop,
            [Command("Profiler.stopPreciseCoverage")]
            ProfilerStopPreciseCoverage,
            [Command("Profiler.stopTypeProfile")]
            ProfilerStopTypeProfile,
            [Command("Profiler.takePreciseCoverage")]
            ProfilerTakePreciseCoverage,
            [Command("Profiler.takeTypeProfile")]
            ProfilerTakeTypeProfile,

            [Command("Runtime.awaitPromise")]
            RuntimeAwaitPromise,
            [Command("Runtime.callFunctionOn")]
            RuntimeCallFunctionOn,
            [Command("Runtime.compileScript")]
            RuntimeCompileScript,
            [Command("Runtime.disable")]
            RuntimeDisable,
            [Command("Runtime.discardConsoleEntries")]
            RuntimeDiscardConsoleEntries,
            [Command("Runtime.enable")]
            RuntimeEnable,
            [Command("Runtime.evaluate")]
            RuntimeEvaluate,
            [Command("Runtime.getExceptionDetails")]
            RuntimeGetExceptionDetails,
            [Command("Runtime.getHeapUsage")]
            RuntimeGetHeapUsage,
            [Command("Runtime.getIsolateId")]
            RuntimeGetIsolateId,
            [Command("Runtime.getProperties")]
            RuntimeGetProperties,
            [Command("Runtime.globalLexicalScopeNames")]
            RuntimeGlobalLexicalScopeNames,
            [Command("Runtime.queryObjects")]
            RuntimeQueryObjects,
            [Command("Runtime.releaseObject")]
            RuntimeReleaseObject,
            [Command("Runtime.releaseObjectGroup")]
            RuntimeReleaseObjectGroup,
            [Command("Runtime.runIfWaitingForDebugger")]
            RuntimeRunIfWaitingForDebugger,
            [Command("Runtime.runScript")]
            RuntimeRunScript,
            [Command("Runtime.setAsyncCallStackDepth")]
            RuntimeSetAsyncCallStackDepth,
            [Command("Runtime.setCustomObjectFormatterEnabled")]
            RuntimeSetCustomObjectFormatterEnabled,
            [Command("Runtime.setMaxCallStackSizeToCapture")]
            RuntimeSetMaxCallStackSizeToCapture,
            [Command("Runtime.terminateExecution")]
            RuntimeTerminateExecution,

            [Command("Security.disable")]
            SecurityDisable,
            [Command("Security.enable")]
            SecurityEnable,
            [Command("Security.setIgnoreCertificateErrors")]
            SecuritySetIgnoreCertificateErrors,
            [Command("Security.handleCertificateError")]
            SecurityHandleCertificateError,
            [Command("Security.setOverrideCertificateErrors")]
            SecuritySetOverrideCertificateErrors,

            [Command("ServiceWorker.deliverPushMessage")]
            ServiceWorkerDeliverPushMessage,
            [Command("ServiceWorker.disable")]
            ServiceWorkerDisable,
            [Command("ServiceWorker.enable")]
            ServiceWorkerEnable,
            [Command("ServiceWorker.inspectWorker")]
            ServiceWorkerInspectWorker,
            [Command("ServiceWorker.setForceUpdateOnPageLoad")]
            ServiceWorkerSetForceUpdateOnPageLoad,
            [Command("ServiceWorker.skipWaiting")]
            ServiceWorkerSkipWaiting,
            [Command("ServiceWorker.startWorker")]
            ServiceWorkerStartWorker,
            [Command("ServiceWorker.stopAllWorkers")]
            ServiceWorkerStopAllWorkers,
            [Command("ServiceWorker.stopWorker")]
            ServiceWorkerStopWorker,
            [Command("ServiceWorker.unregister")]
            ServiceWorkerUnregister,
            [Command("ServiceWorker.updateRegistration")]
            ServiceWorkerUpdateRegistration,

            [Command("Storage.clearCookies")]
            StorageClearCookies,
            [Command("Storage.clearDataForOrigin")]
            StorageClearDataForOrigin,
            [Command("Storage.clearDataForStorageId")]
            StorageClearDataForStorageId,
            [Command("Storage.clearSharedStorageEntries")]
            StorageClearSharedStorageEntries,
            [Command("Storage.clearTrustTokens")]
            StorageClearTrustTokens,
            [Command("Storage.deleteSharedStorageEntry")]
            StorageDeleteSharedStorageEntry,
            [Command("Storage.deleteStorageBucket")]
            StorageDeleteStorageBucket,
            [Command("Storage.getCookies")]
            StorageGetCookies,
            [Command("Storage.getInterestGroupDetails")]
            StorageGetInterestGroupDetails,
            [Command("Storage.getSharedStorageEntries")]
            StorageGetSharedStorageEntries,
            [Command("Storage.getSharedStorageMetadata")]
            StorageGetSharedStorageMetadata,
            [Command("Storage.getStorageKeyForFrame")]
            StorageGetStorageKeyForFrame,
            [Command("Storage.getTrustTokens")]
            StorageGetTrustTokens,
            [Command("Storage.getUsageAndQuota")]
            StorageGetUsageAndQuota,
            [Command("Storage.joinInterestGroup")]
            StorageJoinInterestGroup,
            [Command("Storage.overrideQuotaForOrigin")]
            StorageOverrideQuotaForOrigin,
            [Command("Storage.resetSharedStorageBudget")]
            StorageResetSharedStorageBudget,
            [Command("Storage.runBounceTrackingMitigations")]
            StorageRunBounceTrackingMitigations,
            [Command("Storage.setCookies")]
            StorageSetCookies,
            [Command("Storage.setInterestGroupTracking")]
            StorageSetInterestGroupTracking,
            [Command("Storage.setSharedStorageEntry")]
            StorageSetSharedStorageEntry,
            [Command("Storage.setSharedStorageTracking")]
            StorageSetSharedStorageTracking,
            [Command("Storage.setStorageBucketTracking")]
            StorageSetStorageBucketTracking,
            [Command("Storage.trackCacheStorageForOrigin")]
            StorageTrackCacheStorageForOrigin,
            [Command("Storage.trackCacheStorageForStorageKey")]
            StorageTrackCacheStorageForStorageKey,
            [Command("Storage.trackIndexedDBForOrigin")]
            StorageTrackIndexedDBForOrigin,
            [Command("Storage.trackIndexedDBForStorageKey")]
            StorageTrackIndexedDBForStorageKey,
            [Command("Storage.untrackCacheStorageForOrigin")]
            StorageUntrackCacheStorageForOrigin,
            [Command("Storage.untrackCacheStorageForStorageKey")]
            StorageUntrackCacheStorageForStorageKey,
            [Command("Storage.untrackIndexedDBForOrigin")]
            StorageUntrackIndexedDBForOrigin,
            [Command("Storage.untrackIndexedDBForStorageKey")]
            StorageUntrackIndexedDBForStorageKey,

            [Command("SystemInfo.getFeatureState")]
            SystemInfoGetFeatureState,
            [Command("SystemInfo.getInfo")]
            SystemInfoGetInfo,
            [Command("SystemInfo.getProcessInfo")]
            SystemInfoGetProcessInfo,

            [Command("Target.activateTarget")]
            TargetActivateTarget,
            [Command("Target.attachToTarget")]
            TargetAttachToTarget,
            [Command("Target.attachToBrowserTarget")]
            TargetAttachToBrowserTarget,
            [Command("Target.closeTarget")]
            TargetCloseTarget,
            [Command("Target.createBrowserContext")]
            TargetCreateBrowserContext,
            [Command("Target.createTarget")]
            TargetCreateTarget,
            [Command("Target.detachFromTarget")]
            TargetDetachFromTarget,
            [Command("Target.disposeBrowserContext")]
            TargetDisposeBrowserContext,
            [Command("Target.getBrowserContexts")]
            TargetGetBrowserContexts,
            [Command("Target.getTargetInfo")]
            TargetGetTargetInfo,
            [Command("Target.getTargets")]
            TargetGetTargets,
            [Command("Target.sendMessageToTarget")]
            TargetSendMessageToTarget,
            [Command("Target.setAttachToFrames")]
            TargetSetAttachToFrames,
            [Command("Target.setAutoAttach")]
            TargetSetAutoAttach,
            [Command("Target.setDiscoverTargets")]
            TargetSetDiscoverTargets,
            [Command("Target.setRemoteLocations")]
            TargetSetRemoteLocations,

            [Command("Tethering.bind")]
            TetheringBind,
            [Command("Tethering.unbind")]
            TetheringUnbind,

            [Command("Tracing.end")]
            TracingEnd,
            [Command("Tracing.getCategories")]
            TracingGetCategories,
            [Command("Tracing.recordClockSyncMarker")]
            TracingRecordClockSyncMarker,
            [Command("Tracing.requestMemoryDump")]
            TracingRequestMemoryDump,
            [Command("Tracing.start")]
            TracingStart,

            [Command("WebAudio.enable")]
            WebAudioEnable,
            [Command("WebAudio.disable")]
            WebAudioDisable,
            [Command("WebAudio.getRealtimeData")]
            WebAudioGetRealtimeData,

            [Command("WebAuthn.enable")]
            WebAuthnEnable,
            [Command("WebAuthn.disable")]
            WebAuthnDisable,
            [Command("WebAuthn.addVirtualAuthenticator")]
            WebAuthnAddVirtualAuthenticator,
            [Command("WebAuthn.addCredential")]
            WebAuthnAddCredential,
            [Command("WebAuthn.getCredential")]
            WebAuthnGetCredential,
            [Command("WebAuthn.getCredentials")]
            WebAuthnGetCredentials,
            [Command("WebAuthn.removeCredential")]
            WebAuthnRemoveCredential,
            [Command("WebAuthn.removeVirtualAuthenticator")]
            WebAuthnRemoveVirtualAuthenticator,
            [Command("WebAuthn.setUserVerified")]
            WebAuthnSetUserVerified,
            [Command("WebAuthn.clearCredentials")]
            WebAuthnClearCredentials,

            [Command("Page.addScriptToEvaluateOnNewDocument")]
            PageAddScriptToEvaluateOnNewDocument,
            [Command("Page.bringToFront")]
            PageBringToFront,
            [Command("Page.captureScreenshot")]
            PageCaptureScreenshot,
            [Command("Page.close")]
            PageClose,
            [Command("Page.createIsolatedWorld")]
            PageCreateIsolatedWorld,
            [Command("Page.disable")]
            PageDisable,
            [Command("Page.enable")]
            PageEnable,
            [Command("Page.getAppManifest")]
            PageGetAppManifest,
            [Command("Page.getFrameTree")]
            PageGetFrameTree,
            [Command("Page.getLayoutMetrics")]
            PageGetLayoutMetrics,
            [Command("Page.getNavigationHistory")]
            PageGetNavigationHistory,
            [Command("Page.handleJavaScriptDialog")]
            PageHandleJavaScriptDialog,
            [Command("Page.navigate")]
            PageNavigate,
            [Command("Page.navigateToHistoryEntry")]
            PageNavigateToHistoryEntry,
            [Command("Page.printToPDF")]
            PagePrintToPDF,
            [Command("Page.reload")]
            PageReload,
            [Command("Page.removeScriptToEvaluateOnNewDocument")]
            PageRemoveScriptToEvaluateOnNewDocument,
            [Command("Page.resetNavigationHistory")]
            PageResetNavigationHistory,
            [Command("Page.setBypassCSP")]
            PageSetBypassCSP,
            [Command("Page.setDocumentContent")]
            PageSetDocumentContent,
            [Command("Page.setInterceptFileChooserDialog")]
            PageSetInterceptFileChooserDialog,
            [Command("Page.setLifecycleEventsEnabled")]
            PageSetLifecycleEventsEnabled,
            [Command("Page.stopLoading")]
            PageStopLoading,
            [Command("Page.clearGeolocationOverride")]
            PageClearGeolocationOverride,
            [Command("Page.setGeolocationOverride")]
            PageSetGeolocationOverride,
            [Command("Page.addCompilationCache")]
            PageAddCompilationCache,
            [Command("Page.captureSnapshot")]
            PageCaptureSnapshot,
            [Command("Page.clearCompilationCache")]
            PageClearCompilationCache,
            [Command("Page.crash")]
            PageCrash,
            [Command("Page.generateTestReport")]
            PageGenerateTestReport,
            [Command("Page.getAdScriptAncestry")]
            PageGetAdScriptAncestry,
            [Command("Page.getAppId")]
            PageGetAppId,
            [Command("Page.getInstallabilityErrors")]
            PageGetInstallabilityErrors,
            [Command("Page.getOriginTrials")]
            PageGetOriginTrials,
            [Command("Page.getPermissionsPolicyState")]
            PageGetPermissionsPolicyState,
            [Command("Page.getResourceContent")]
            PageGetResourceContent,
            [Command("Page.getResourceTree")]
            PageGetResourceTree,
            [Command("Page.produceCompilationCache")]
            PageProduceCompilationCache,
            [Command("Page.screencastFrameAck")]
            PageScreencastFrameAck,
            [Command("Page.searchInResource")]
            PageSearchInResource,
            [Command("Page.setAdBlockingEnabled")]
            PageSetAdBlockingEnabled,
            [Command("Page.setFontFamilies")]
            PageSetFontFamilies,
            [Command("Page.setFontSizes")]
            PageSetFontSizes,
            [Command("Page.setPrerenderingAllowed")]
            PageSetPrerenderingAllowed,
            [Command("Page.setRPHRegistrationMode")]
            PageSetRPHRegistrationMode,
            [Command("Page.setSPCTransactionMode")]
            PageSetSPCTransactionMode,
            [Command("Page.setWebLifecycleState")]
            PageSetWebLifecycleState,
            [Command("Page.startScreencast")]
            PageStartScreencast,
            [Command("Page.stopScreencast")]
            PageStopScreencast,
            [Command("Page.waitForDebugger")]
            PageWaitForDebugger,
            [Command("Page.addScriptToEvaluateOnLoad")]
            PageAddScriptToEvaluateOnLoad,
            [Command("Page.clearDeviceMetricsOverride")]
            PageClearDeviceMetricsOverride,
            [Command("Page.clearDeviceOrientationOverride")]
            PageClearDeviceOrientationOverride,
            [Command("Page.deleteCookie")]
            PageDeleteCookie,
            [Command("Page.getManifestIcons")]
            PageGetManifestIcons,
            [Command("Page.removeScriptToEvaluateOnLoad")]
            PageRemoveScriptToEvaluateOnLoad,
            [Command("Page.setDeviceMetricsOverride")]
            PageSetDeviceMetricsOverride,
            [Command("Page.setDeviceOrientationOverride")]
            PageSetDeviceOrientationOverride,
            [Command("Page.setDownloadBehavior")]
            PageSetDownloadBehavior,
            [Command("Page.setTouchEmulationEnabled")]
            PageSetTouchEmulationEnabled,
            [Command("Network.clearBrowserCache")]
            NetworkClearBrowserCache,
            [Command("Network.clearBrowserCookies")]
            NetworkClearBrowserCookies,
            [Command("Network.deleteCookies")]
            NetworkDeleteCookies,
            [Command("Network.disable")]
            NetworkDisable,
            [Command("Network.enable")]
            NetworkEnable,
            [Command("Network.emulateNetworkConditions")]
            NetworkEmulateNetworkConditions,
            [Command("Network.getAllCookies")]
            NetworkGetAllCookies,
            [Command("Network.getCookies")]
            NetworkGetCookies,
            [Command("Network.getRequestPostData")]
            NetworkGetRequestPostData,
            [Command("Network.getResponseBody")]
            NetworkGetResponseBody,
            [Command("Network.setBypassServiceWorker")]
            NetworkSetBypassServiceWorker,
            [Command("Network.setCacheDisabled")]
            NetworkSetCacheDisabled,
            [Command("Network.setCookie")]
            NetworkSetCookie,
            [Command("Network.setCookies")]
            NetworkSetCookies,
            [Command("Network.setExtraHTTPHeaders")]
            NetworkSetExtraHTTPHeaders,
            [Command("Network.setUserAgentOverride")]
            NetworkSetUserAgentOverride,
            [Command("Network.canClearBrowserCache")]
            NetworkCanClearBrowserCache,
            [Command("Network.canClearBrowserCookies")]
            NetworkCanClearBrowserCookies,
            [Command("Network.canEmulateNetworkConditions")]
            NetworkCanEmulateNetworkConditions,
            [Command("Network.clearAcceptedEncodingsOverride")]
            NetworkClearAcceptedEncodingsOverride,
            [Command("Network.enableReportingApi")]
            NetworkEnableReportingApi,
            [Command("Network.getCertificate")]
            NetworkGetCertificate,
            [Command("Network.getResponseBodyForInterception")]
            NetworkGetResponseBodyForInterception,
            [Command("Network.getSecurityIsolationStatus")]
            NetworkGetSecurityIsolationStatus,
            [Command("Network.loadNetworkResource")]
            NetworkLoadNetworkResource,
            [Command("Network.replayXHR")]
            NetworkReplayXHR,
            [Command("Network.searchInResponseBody")]
            NetworkSearchInResponseBody,
            [Command("Network.setAcceptedEncodings")]
            NetworkSetAcceptedEncodings,
            [Command("Network.setAttachDebugStack")]
            NetworkSetAttachDebugStack,
            [Command("Network.setBlockedURLs")]
            NetworkSetBlockedURLs,
            [Command("Network.setCookieControls")]
            NetworkSetCookieControls,
            [Command("Network.streamResourceContent")]
            NetworkStreamResourceContent,
            [Command("Network.takeResponseBodyForInterceptionAsStream")]
            NetworkTakeResponseBodyForInterceptionAsStream,
            [Command("Network.continueInterceptedRequest")]
            NetworkContinueInterceptedRequest,
            [Command("Network.setRequestInterception")]
            NetworkSetRequestInterception,
            #region internal commands
            CheckNetworkIdle,
            MinimizeWindow,
            MaximizeWindow,
            RestoreWindow,
            NotSet,
            #endregion internal commands
        }
        #endregion Puppeteer
        #region Google Sheets
        /// <summary>
        /// Type of trigger that fires a bound Google Apps Script function (e.g. on edit, on open, or on a schedule).
        /// </summary>
        public enum GoogleTriggerType
        {
            Edit,
            Open,
            Change,
            FormSubmit,
            TimeBased,
            CalendarUpdated,
            DocumentUpdated
        }
        /// <summary>
        /// Recurrence interval for a GoogleTriggerType.TimeBased trigger.
        /// </summary>
        public enum TriggerScheduleType
        {
            None,
            Hourly,
            Daily,
            Weekly,
            Monthly
        }
        /// <summary>
        /// Cell formatting attribute that can be applied to a Google Sheets range.
        /// </summary>
        public enum FormatType
        {
            Bold,
            Italic,
            Underline,
            Strikethrough,
            FontSize,
            FontFamily,
            ForegroundColor,
            CellBackgroundColor,
            NumberFormat,
            HorizontalAlignment,
            VerticalAlignment,
            TextRotation
        }
        /// <summary>
        /// Number format category applied to a Google Sheets cell range when FormatType.NumberFormat is used.
        /// </summary>
        public enum NumberFormatType
        {
            TEXT,
            NUMBER,
            CURRENCY,
            DATE,
            TIME,
            DATE_TIME,
            PERCENT,
            SCIENTIFIC
        }
        /// <summary>
        /// Horizontal text alignment applied to a Google Sheets cell range when FormatType.HorizontalAlignment is used.
        /// </summary>
        public enum HorizontalAlignmentType
        {
            LEFT,
            CENTER,
            RIGHT
        }
        /// <summary>
        /// Vertical text alignment applied to a Google Sheets cell range when FormatType.VerticalAlignment is used.
        /// </summary>
        public enum VerticalAlignmentType
        {
            TOP,
            MIDDLE,
            BOTTOM
        }
        /// <summary>
        /// Text rotation applied to a Google Sheets cell range when FormatType.TextRotation is used.
        /// </summary>
        public enum TextRotationType
        {
            None,       // 0 degrees
            TiltUp30,   // 30 degrees upward
            TiltUp45,   // 45 degrees upward
            TiltDown30, // 30 degrees downward
            TiltDown45, // 45 degrees downward
            Vertical,   // 90 degrees (stacked text)
            UpsideDown,
        }
        /// <summary>
        /// Who is permitted to execute a deployed Google Apps Script.
        /// </summary>
        public enum ScriptAccessType
        {
            ME,
            ANYONE,
            ANYONE_ANONYMOUS,
            DOMAIN
        }
        /// <summary>
        /// Identity under which a deployed Google Apps Script runs.
        /// </summary>
        public enum ExecuteAsType
        {
            USER_ACCESSING,
            USER_DEPLOYING
        }
        #endregion Google Sheets
        #region Google API
        /// <summary>
        /// Google Cloud API identifier used when enabling APIs for a project.
        /// </summary>
        public enum GoogleApi
        {
            ServiceUsage,
            ApiKeys,
            Scripts,
            Sheets,
            Drive
        }
        #endregion Google API
        #region Cryptography
        /// <summary>
        /// Algorithm used for encryption, hashing, or signing operations.
        /// </summary>
        public enum CryptoAlgorithm
        {
            AES, // Default for Encrypt/Decrypt
            RSA,
            SHA256, // Default for Hash
            SHA256withRSA, // Default for Sign/Verify
            ECDSA,
            MD5,
            PBKDF2
        }

        /// <summary>
        /// Block cipher mode of operation used for symmetric encryption.
        /// </summary>
        public enum CryptoMode
        {
            GCM, // Default
            CBC,
            ECB
        }

        /// <summary>
        /// Padding scheme used for symmetric encryption.
        /// </summary>
        public enum CryptoPadding
        {
            PKCS7, // Default
            NoPadding,
            ISO10126
        }
        #endregion Cryptography
        #region GPAL URL
        // written by grok, modified by mbv
        /// <summary>
        /// Browser storage mechanism targeted by a website storage operation.
        /// </summary>
        public enum WebsiteStorageType
        {
            notSet,
            cookie,
            localStorage,
            sessionStorage,
            indexedDb,
            cache
        }

        /// <summary>
        /// Operation to perform against a browser storage mechanism (get, set, or delete).
        /// </summary>
        public enum WebsiteStorageAction
        {
            notSet,
            delete,
            get,
            set
        }
        #endregion GPAL URL
        #region OCR
        /// <summary>
        /// Supported languages for OCR processing.
        /// </summary>
        public enum OCRLanguage
        {
            English,
            Spanish,
            French,
            German
        }

        /// <summary>
        /// Determines the primary reading order and how OCR results are structured in the output grid.
        /// LeftRightTopBottom and RightLeftTopBottom treat text as flowing paragraphs (one column per row).
        /// TopBottomLeftRight and BottomTopLeftRight treat text as columnar or tabular data (multiple columns possible).
        /// </summary>
        public enum OCRScanDirection
        {
            LeftRightTopBottom,
            RightLeftTopBottom,
            TopBottomLeftRight,
            BottomTopLeftRight
        }

        /// <summary>
        /// Predefined character whitelists to restrict OCR output to specific character sets.
        /// </summary>
        public enum OCRWhitelist
        {
            Alphanumeric,
            Numeric,
            Letters,
            Punctuation,
            EnglishLetters,
            EnglishAlphanumeric,
            EnglishNumeric,
            SpanishLetters,
            SpanishAlphanumeric,
            SpanishNumeric,
            FrenchLetters,
            FrenchAlphanumeric,
            FrenchNumeric,
            GermanLetters,
            GermanAlphanumeric,
            GermanNumeric
        }

        /// <summary>
        /// Selects which OCR engine (legacy Tesseract, LSTM, or both) is used to process an image.
        /// </summary>
        public enum EngineMode : int
        {
            /// <summary>
            /// Only the legacy tesseract OCR engine is used.
            /// </summary>
            TesseractOnly = 0,

            /// <summary>
            /// Only the new LSTM-based OCR engine is used.
            /// </summary>
            LstmOnly,

            /// <summary>
            /// Both the legacy and new LSTM based OCR engine is used.
            /// </summary>
            TesseractAndLstm,

            /// <summary>
            /// The default OCR engine is used (currently LSTM-ased OCR engine).
            /// </summary>
            Default
        }
    }
    #endregion OCR
}

