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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Threading;
//using DocumentFormat.OpenXml.Bibliography;
//using DocumentFormat.OpenXml.Drawing;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using Keys = OpenQA.Selenium.Keys;
using MatchType = GenerallyPositive.Enums.MatchType;

namespace GenerallyPositive.Browser
{
    internal partial class ElementHelper
    {
        // to avoid repeat messages for the same selector when waiting for a selector
        static List<string> listOfMessages = new List<string>();
        static Selector lastSelector = null;
        // What the last FindWebElements on this thread found, with the selector path in full, for WaitFor to publish once
        // its wait is over; the wait's line is the only one that gives the path whole. Per thread, so browsers run in
        // parallel do not publish one another's searches
        [ThreadStatic] static string lastFoundFullMessage;

        // how hard ScrollUntilVisible works before it gives up and lets the workflow carry on
        const int VisibilitySettleChecks = 4;    // rechecks after a scroll, to ride out a header sliding in
        const int VisibilitySettleMs = 100;      // wait between those rechecks
        const int MaxScrollPages = 40;           // screens to page through before giving up on finding it

        // Input types shown as a picker widget. Typing into them depends on segment order, separators and tab
        // behavior, which vary by browser and locale. All accept an ISO value in .value, so Selenium sets .value, as
        // OttoMagic and Puppeteer do.
        private static readonly HashSet<string> valueOnlyInputTypes = new HashSet<string>
        {
            "color", "date", "datetime-local", "month", "week", "time",
        };
        // per-thread so concurrent browsers do not share a generator, and seeded from a Guid because the
        // parameterless Random seeds off a low resolution clock - constructed back to back it repeats itself
        [ThreadStatic] private static Random _jitter;
        private static Random Jitter => _jitter ?? (_jitter = new Random(Guid.NewGuid().GetHashCode()));

        // Set once RunPageScript has warned that OttoMagic cannot run scripts, so it warns once per run.
        private static bool _userScriptsWarned;

        // Pixel insets from the right edge of the visible part, tried before hitTestFractions. An element much wider
        // than the window overflows away from its anchored edge, and its working control sits at that edge. A
        // percentage of a 3782px element misses a button a few dozen pixels wide.
        private static readonly int[] hitTestInsets = { 24, 40, 60, 12 };

        // If no inset hits, try these fractions of the width, so an element anchored differently is still tried
        // across its whole width.
        private static readonly float[] hitTestFractions = { 0.75f, 0.50f, 0.25f, 0.10f };

        // Set true by HardwareClick when the element's screen placement was questionable (odd/off-screen coords,
        // so we could not do a real hardware click). HardwareFillInFrom reads it to refuse to type - otherwise a
        // failed click leaves focus wherever it was (e.g. the browser address bar) and keystrokes leak there.
        // ThreadStatic so concurrent browser threads do not clobber each other's last-click state.
        [ThreadStatic] private static bool _lastPlacementQuestionable;

        // set by HardwareMoveTo when it had to clamp the point into the element's visible part, read by the click
        // that follows so the message says where it actually clicked. null when nothing had to be done
        [ThreadStatic] private static string _lastClampedPoint;

        // the browser's content area as HardwareMoveTo last measured it, read by the click that follows it
        [ThreadStatic] private static Rectangle _lastContentArea;

        // Outer window position and size from the last GetWindowRectangle call. GetWindowRectangle returns the
        // content area for clicks, but WithWindowSize sets the outer window, so messages report that, in x, y,
        // width, height order.
        [ThreadStatic] private static Rectangle _lastOuterWindow;

        // One mouse per machine, and SendInput acts on whatever has focus. Topping a window and clicking in
        // it must therefore be atomic across workflow threads, or one thread tops its window between another
        // thread topping and clicking
        private static readonly object hardwareMouse = new object();
    }
}
