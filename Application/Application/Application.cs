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
    public partial class Application : IApplication
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

        /// <summary>
        /// The hidden desktop this program runs on, or null when it is on the desktop you are looking at. Peek
        /// at it through this, to see it and to use it by hand; GPAL sends it no hardware input, peeked or not.
        /// </summary>
        public IHiddenDesktop HiddenDesktop => true == ApplicationSettings.HiddenDesktop
            ? new GPALHiddenDesktop(ApplicationSettings.HiddenDesktopName)
            : null;
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
    }
}
