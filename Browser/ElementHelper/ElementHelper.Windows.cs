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
        /// <summary>
        /// Finds the main window handle of a running browser process by type.
        /// </summary>
        /// <param name="browserType">Chrome, Edge or FireFox</param>
        /// <returns>The browser's main window handle, or IntPtr.Zero if none was found</returns>
        private static IntPtr GetWindowHandle(BrowserType browserType)
        {
            IntPtr hWnd = IntPtr.Zero;
            Process[] processes = null;

            if (BrowserType.Chrome == browserType)
                processes = Process.GetProcessesByName("chrome");
            else if (BrowserType.Edge == browserType)
                processes = Process.GetProcessesByName("msedge");
            else if (BrowserType.FireFox == browserType)
                processes = Process.GetProcessesByName("firefox");

            if (processes.Length > 0)
            {
                foreach (Process proc in processes)
                {
                    if (proc.MainWindowHandle == IntPtr.Zero)
                        continue;
                    else
                    {
                        hWnd = proc.MainWindowHandle;
                        break;
                    }
                }
            }
            return hWnd;
        }
        /// <summary>
        /// Finds a top-level window whose title contains <paramref name="windowTitle"/> and returns its
        /// screen rectangle via the Win32 GetWindowRect API.
        /// </summary>
        /// <param name="windowTitle">Substring to match against open window titles</param>
        /// <returns>The window's rectangle, or an empty Rectangle if no matching window was found</returns>
        private static Rectangle GetWindowRect(string windowTitle)
        {
            IntPtr hWnd = IntPtr.Zero;
            foreach (Process pList in Process.GetProcesses())
            {
                if (pList.MainWindowTitle.Contains(windowTitle))
                {
                    hWnd = pList.MainWindowHandle;
                    break;
                }
            }

            if (hWnd != IntPtr.Zero)
            {
                RECT rect;
                GetWindowRect(hWnd, out rect);
                return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            else
            {
                return Rectangle.Empty;
            }
        }

        /// <summary>
        /// Win32 GetWindowRect: retrieves the screen coordinates of a window's bounding rectangle.
        /// </summary>
        /// <param name="hWnd">Handle to the window</param>
        /// <param name="lpRect">Receives the window's bounding rectangle in screen coordinates</param>
        /// <returns>True on success</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        /// <summary>
        /// As <see cref="GetWindowRectangle(Browser)"/>, also returning the outer window. The outer window
        /// includes the browser chrome; the content area is the origin for page coordinates.
        /// </summary>
        /// <param name="browser">Browser to measure.</param>
        /// <param name="outerWindow">Receives the whole window in screen coordinates.</param>
        /// <returns>The content area in screen coordinates.</returns>
        public static Rectangle GetWindowRectangle(Browser browser, out Rectangle outerWindow)
        {
            Rectangle retVal = GetWindowRectangle(browser);

            // Set by the read above and ThreadStatic, so it belongs to the calling workflow
            outerWindow = _lastOuterWindow;

            return retVal;
        }

        /// <summary>
        /// Computes the screen rectangle of the browser's content (viewport) area from the window position and
        /// inner/outer dimensions the engine or JavaScript reports.
        /// </summary>
        /// <param name="browser">Browser to get the content rectangle for</param>
        /// <returns>The screen rectangle of the browser's viewport content area</returns>
        public static Rectangle GetWindowRectangle(Browser browser)
        {
            // Read the screen position and sizes from the engine, or with JavaScript on Selenium
            bool jsSuccess = false;
            int screenX = 0, screenY = 0, outerHeight = 0, innerHeight = 0, outerWidth = 0, innerWidth = 0;

            try
            {
                bool withReturn = !browser.UsePuppeteer;
                string prefix = withReturn ? "return " : "";
                dynamic value = null;

                if (true == browser.UseOttoMagic)
                {
                    Rectangle rect = browser.MagicHelper.GetWindowRectangle();
                    // { y: window.innerHeight, x: window.innerWidth, height: window.outerHeight, width: window.outerWidth }
                    outerHeight = rect.Height;
                    innerHeight = rect.Y;
                    outerWidth = rect.Width;
                    innerWidth = rect.X;
                    screenX = browser.MagicHelper.WindowScreenLeft();
                    screenY = browser.MagicHelper.WindowScreenTop();
                    jsSuccess = true;
                }
                else if (true == browser.UsePuppeteer)  // Puppeteer reads each value through its own endpoint, not the script below
                {
                    outerHeight = browser.PuppeteerClient.WindowOuterHeight().Execute<int>();
                    innerHeight = browser.PuppeteerClient.WindowInnerHeight().Execute<int>();
                    outerWidth = browser.PuppeteerClient.WindowOuterWidth().Execute<int>();
                    innerWidth = browser.PuppeteerClient.WindowInnerWidth().Execute<int>();
                    screenX = browser.PuppeteerClient.WindowScreenLeft().Execute<int>();
                    screenY = browser.PuppeteerClient.WindowScreenTop().Execute<int>();
                    jsSuccess = true;
                }
                else
                {
                    string jsScript = $"{prefix} ["
                        + "window.screenLeft, "
                        + "window.screenTop, "
                        + "window.outerHeight, window.innerHeight, window.outerWidth, window.innerWidth]";

                    // Selenium runs script in the current frame, where innerHeight is the frame's, which puts the
                    // content area near the bottom of the screen. Measure in the top document, then restore the
                    // frame.
                    bool inFrame = browser.CurrentUOW?.ContextPath?.Any(context => ElementType.IFrame == context.elementType) ?? false;

                    try
                    {
                        if (true == inFrame)
                            browser.BrowserDriver.SwitchTo().DefaultContent();

                        value = BrowserHelper.ExecuteJavaScriptObj(jsScript, browser, null);
                    }
                    finally
                    {
                        if (true == inFrame)
                            RestoreFrameContext(browser);
                    }
                    IList<object> values;

                    if (value is Newtonsoft.Json.Linq.JArray jArray)
                    {
                        values = jArray.Select(token => (object)token).ToList();
                    }
                    else if (value is IList<object> list)
                    {
                        values = list;
                    }
                    else
                    {
                        // Fallback if unexpected format (e.g., dict); assume defaults
                        values = new List<object> { 0, 0, 0, 0, 0, 0 };
                    }

                    screenX = values.Count > 0 ? Convert.ToInt32(values[0]) : 0;
                    screenY = values.Count > 1 ? Convert.ToInt32(values[1]) : 0;
                    outerHeight = values.Count > 2 ? Convert.ToInt32(values[2]) : 0;
                    innerHeight = values.Count > 3 ? Convert.ToInt32(values[3]) : 0;
                    outerWidth = values.Count > 4 ? Convert.ToInt32(values[4]) : 0;
                    innerWidth = values.Count > 5 ? Convert.ToInt32(values[5]) : 0;
                    jsSuccess = true;
                }

                if (jsSuccess && innerWidth > 0 && innerHeight > 0)
                {
                    _lastOuterWindow = new Rectangle(screenX, screenY, outerWidth, outerHeight);

                    int navHeight = outerHeight - innerHeight;
                    int navWidth = outerWidth - innerWidth;
                    // The border is the same width left, right and bottom: border = (outerWidth - innerWidth) / 2.
                    // The height difference is top chrome plus bottom border, so content top = screenY + height
                    // difference - border. Otherwise every hardware click lands a border width low.
                    int border = navWidth / 2;
                    int contentLeft = screenX + border;
                    int contentTop = screenY + navHeight - border;
                    return new Rectangle(contentLeft, contentTop, innerWidth, innerHeight);
                }
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"JS execution failed for browser [{browser.BrowserSettings.BrowserType}]", browser, GPALObjectType.Browser, ex);
            }

            // Reached when the reads above returned zero or threw
            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to get rectangle for [{browser.BrowserSettings.BrowserType}].", browser, GPALObjectType.Browser);

            return Rectangle.Empty;
        }
        /// <summary>
        /// The browser window's outer rectangle on screen: window.screenLeft/screenTop and outerWidth/outerHeight.
        /// <see cref="GetWindowRectangle"/> returns the content area instead. The outer top-left corner is a safe point
        /// to click to top the window without hitting a tab or the page.
        /// </summary>
        public static Rectangle GetWindowOuterRectangle(Browser browser)
        {
            int screenX = 0, screenY = 0, outerWidth = 0, outerHeight = 0;

            try
            {
                if (true == browser.UseOttoMagic)
                {
                    // { y: innerHeight, x: innerWidth, height: outerHeight, width: outerWidth }
                    Rectangle rect = browser.MagicHelper.GetWindowRectangle();
                    outerWidth = rect.Width;
                    outerHeight = rect.Height;
                    screenX = browser.MagicHelper.WindowScreenLeft();
                    screenY = browser.MagicHelper.WindowScreenTop();
                }
                else if (true == browser.UsePuppeteer)
                {
                    outerWidth = browser.PuppeteerClient.WindowOuterWidth().Execute<int>();
                    outerHeight = browser.PuppeteerClient.WindowOuterHeight().Execute<int>();
                    screenX = browser.PuppeteerClient.WindowScreenLeft().Execute<int>();
                    screenY = browser.PuppeteerClient.WindowScreenTop().Execute<int>();
                }
                else
                {
                    var value = BrowserHelper.ExecuteJavaScriptObj("return [window.screenLeft, window.screenTop, window.outerWidth, window.outerHeight]", browser, null);
                    IList<object> values;

                    if (value is Newtonsoft.Json.Linq.JArray jArray)
                        values = jArray.Select(token => (object)token).ToList();
                    else if (value is IList<object> list)
                        values = list;
                    else
                        values = new List<object> { 0, 0, 0, 0 };

                    screenX = values.Count > 0 ? Convert.ToInt32(values[0]) : 0;
                    screenY = values.Count > 1 ? Convert.ToInt32(values[1]) : 0;
                    outerWidth = values.Count > 2 ? Convert.ToInt32(values[2]) : 0;
                    outerHeight = values.Count > 3 ? Convert.ToInt32(values[3]) : 0;
                }

                return new Rectangle(screenX, screenY, outerWidth, outerHeight);
            }
            catch (GPALException)
            {
                throw;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get outer window rectangle for [{browser.BrowserSettings.BrowserType}]", browser, GPALObjectType.Browser, ex);
                return new Rectangle(0, 0, 0, 0);
            }
        }
    }
}
