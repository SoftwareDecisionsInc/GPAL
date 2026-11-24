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
    }
}
