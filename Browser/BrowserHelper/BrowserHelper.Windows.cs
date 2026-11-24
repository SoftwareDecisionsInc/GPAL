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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using DocumentFormat.OpenXml.Bibliography;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Chromium;
using OpenQA.Selenium.DevTools;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;
using System.Net.NetworkInformation;
using System.Net;
using System.Text.RegularExpressions;

namespace GenerallyPositive.Browser
{
    public partial class BrowserHelper
    {
        /// <summary>
        /// Searches all open browser window handles for one whose URL matches (or is matched by) <paramref name="URL"/>,
        /// switching the driver's focus to that window if found.
        /// </summary>
        /// <param name="browserSettings">BrowserSettings whose driver's windows are searched</param>
        /// <param name="URL">URL (or partial URL) to look for among the open tabs</param>
        /// <returns>The window handle of the matching tab, or null if no tab matches</returns>
        public static string FindTab(BrowserSettings browserSettings, string URL)
        {
            var handles = browserSettings.BrowserDriver.WindowHandles;
            foreach (var handle in handles)
            {
                browserSettings.BrowserDriver.SwitchTo().Window(handle);
                if (URL.Contains(browserSettings.BrowserDriver.Url) || browserSettings.BrowserDriver.Url.Contains(URL))
                {
                    return handle;
                }
            }
            return null;
        }

        public static void TopBrowser(Browser browser, bool moveDontClick, bool forceClick = false) // moveDontClick parks the mouse off the browser after a hardware click
        {
            int x, y;

            // only top if we are interacting with the page using hardware or image mathcing on the current selector
            // Null-safe throughout: CurrentUOW/CurrentSelector are null when we are not on a real page, and
            // a (bool) cast on a null bool? (from the ?. chain) would throw.
            bool needsTopping = (browser.CurrentUOW?.CurrentSelector?.SelectorSettings?.SelectorPaths
                    ?.Any(path => path.SelectorPathType == SelectorPathType.Image) ?? false)
                    || InteractionType.Hardware == browser.CurrentUOW?.CurrentSelector?.InteractionType
                    || true == browser.BrowserSettings.UseHardware;

            // nothing to do headless, on a hidden desktop (the real mouse cannot reach it), or when no image or
            // hardware selector needs the window in front
            if (true == browser.BrowserSettings.UseHeadless || true == browser.BrowserSettings.HiddenDesktop || false == needsTopping)
                return;

            // The mouse is moved off the browser so :hover effects (tooltips, dropdowns, sticky menus) do not
            // change what IMAGE selectors match. CSS and XPath selectors ignore hover state.
            if (true == moveDontClick || true == forceClick)   // park the mouse, or click the window's top-left corner to bring it to the front
            {
                if (true == watch.IsRunning)
                {
                    if (1000 > watch.ElapsedMilliseconds)   // don't do anything if we were called less than 1 second ago, avoid double clicking the browser (max window)
                        return;
                    watch.Stop();
                }
                watch = System.Diagnostics.Stopwatch.StartNew();

                HardwareHelper.GetCursorPos(out Point currCursorPos);

                if (true == browser.BrowserSettings.UseHardware || Enums.BrowserType.FireFox == browser.BrowserSettings.BrowserType || true == moveDontClick || true == forceClick)
                {
                    try
                    {
                        Rectangle rect = ElementHelper.GetWindowRectangle(browser);
                        // true screen origin/size of the window (window.screenLeft/screenTop + outer size), so
                        // nothing has to guess at the height of a browser's chrome
                        Rectangle outerRect = ElementHelper.GetWindowOuterRectangle(browser);

                        if (true == moveDontClick)
                        {
                            // Park the mouse at a random edge of the window (above, below, left or right); the
                            // same spot after every click is a detectable pattern.
                            var rnd = new Random();
                            switch (rnd.Next(4))
                            {
                                case 0: x = rect.Left + rnd.Next(-50, rect.Width + 50); y = outerRect.Top - 20; break;   // above the true window top
                                case 1: x = rect.Left + rnd.Next(-50, rect.Width + 50); y = rect.Bottom + 50; break;    // below
                                case 2: x = rect.Left - 60; y = rect.Top + rnd.Next(0, rect.Height); break;             // left
                                default: x = rect.Right + 60; y = rect.Top + rnd.Next(0, rect.Height); break;          // right
                            }
                            // Keep the parked cursor in the working area. Below a maximized window is the taskbar,
                            // where hovering opens previews that cover the page and break image matching.
                            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
                            x = Math.Min(Math.Max(workingArea.Left, x), workingArea.Right - 1);
                            y = Math.Min(Math.Max(workingArea.Top, y), workingArea.Bottom - 1);
                        }
                        else
                        {
                            // click 3 pixels inside the window's top-left corner: on the frame, off the resize zone,
                            // never on a tab or the page
                            x = outerRect.Left + 3;
                            y = outerRect.Top + 3;
                        }
                        lastX = x;
                        lastY = y;
                        //x = browser.BrowserDriver.Manage().Window.Position.X + 6;
                        //y = x = browser.BrowserDriver.Manage().Window.Position.Y + 6;
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to get browser coords. Retrying.", browser, GPALObjectType.Browser, ex);
                        x = lastX;
                        y = lastY;
                    }

                    if (true == GPAL.SimulateMouse || true == browser.CurrentUOW.CurrentSelector?.SimulateMouse)
                        HardwareHelper.MoveMouse(x, y, 10, 10);
                    else
                        HardwareHelper.MoveMouse(x, y);

                    if (false == moveDontClick)
                    {
                        HardwareHelper.HardwareClick(x, y, ClickType.LeftClick);     // firefox will not take software topping requests, only hardware clicks
                        Thread.Sleep(10);

                        if (true == GPAL.SimulateMouse || true == browser.CurrentUOW.CurrentSelector?.SimulateMouse)
                            HardwareHelper.MoveMouse(currCursorPos.X, currCursorPos.Y, 10, 10);
                        else
                            HardwareHelper.MoveMouse(currCursorPos.X, currCursorPos.Y);
                    }
                }
                else if (true == browser.UseSelenium)
                {
                    try
                    {
                        // Bring the window forward by switching to the tab Selenium already has active. CurrentTabIdx
                        // can be stale and would focus the wrong tab. On failure the catch tops it through WindowHelper.
                        browser.BrowserDriver.SwitchTo().Window(browser.BrowserDriver.CurrentWindowHandle);
                    }
                    catch (GPALException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        string tmpStr = "Faling back to windows topping.";
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to top browser using Selenium. {(false == GPAL.NoFallbackRecoveryActions ? tmpStr : String.Empty)})", browser, GPALObjectType.Browser, ex);
                        if (false == GPAL.NoFallbackRecoveryActions)
                            WindowHelper.TopBrowser(browser.BrowserSettings.Process, browser.BrowserSettings.ServiceDriverPid);
                    }
                }
            }
            else
            {   // we have an image selector or we use ottomagic or puppeteer so use windows to top the browser
                if (false == WindowHelper.TopBrowser(browser.BrowserSettings.Process, browser.BrowserSettings.ServiceDriverPid))
                    throw new GPALException("Browser has exited");
            }
        }
    }
}
