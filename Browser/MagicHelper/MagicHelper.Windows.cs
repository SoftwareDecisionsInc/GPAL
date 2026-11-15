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
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2010.PowerPoint;
using GenerallyPositive.Application;
using Microsoft.Win32.SafeHandles;
using OpenQA.Selenium;
using OpenQA.Selenium.DevTools;
using static GenerallyPositive.Browser.BrowserHelper;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class MagicHelper
    {
        public string CaptureVisibleTab()
        {
            return Client.CaptureVisibleTab().Execute();
        }

        /// <summary>
        /// Closes a tab by URL or tab ID
        /// <example>TabTuple closed = browser.MagicHelper.CloseTab("https://example.com");</example>
        /// </summary>
        /// <param name="URLorTabId">URL or tab ID to close</param>
        /// <returns>Closed tab details</returns>
        public TabTuple CloseTab(dynamic URLorTabId = null)
        {
            if (null == URLorTabId)
                return Client.CloseTab().Execute<TabTuple>();
            else if (URLorTabId is string v)
                return Client.CloseTab(v).Execute<TabTuple>();
            else if (URLorTabId is GPALUrl u)
                return Client.CloseTab(u).Execute<TabTuple>();
            else if (URLorTabId is int i)
                return Client.CloseTab(URLorTabId).Execute<TabTuple>();
            else
                return Client.CloseTab().Execute<TabTuple>();

        }

        /// <summary>
        /// Sets browser to full screen
        /// <example>browser.MagicHelper.FullScreen();</example>
        /// </summary>
        public void FullScreen()
        {
            Client.FullScreen().Execute();
        }

        /// <summary>
        /// Navigates to a tab by URL or tab ID
        /// <example>TabTuple tab = browser.MagicHelper.GotoTab("https://example.com");</example>
        /// </summary>
        /// <param name="URLorTabId">URL or tab ID to navigate to</param>
        /// <returns>The tab details</returns>
        public TabTuple GotoTab(dynamic URLorTabId)
        {
            if (URLorTabId is GPALUrl v)
            {
                TabTuple tt = Client.GoToTab(v).Execute<TabTuple>();
                browser.ServerResponseCode = GetStatusCode();
                return tt;
            }
            else if (URLorTabId is TabTuple tuple)
            {
                TabTuple tt = Client.GoToTab(tuple.TabId).Execute<TabTuple>();
                browser.ServerResponseCode = GetStatusCode();
                return tt;
            }
            else
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"URLorTabId is unknown type [{URLorTabId.GetType()}]", this, GPALObjectType.Other);
                return new TabTuple();
            }
        }

        /// <summary>
        /// Minimizes the browser window
        /// <example>browser.MagicHelper.Minimize();</example>
        /// </summary>
        public void Minimize()
        {
            Client.Minimize().Execute();
        }

        /// <summary>
        /// Maximizes the browser window
        /// <example>browser.MagicHelper.Maximize();</example>
        /// </summary>
        public void Maximize()
        {
            Client.Maximize().Execute();
        }

        /// <summary>
        /// Restores the browser window to normal
        /// <example>browser.MagicHelper.Normal();</example>
        /// </summary>
        public void Normal()
        {
            Client.Normal().Execute();
        }

        /// <summary>
        /// Opens a new tab with optional URL
        /// <example>TabTuple tab = browser.MagicHelper.NewTab("https://example.com");</example>
        /// </summary>
        /// <param name="URL">The URL to open</param>
        /// <returns>The tab details</returns>
        public TabTuple NewTab(GPALUrl URL = null)
        {
            if (null == URL)
                URL = new GPALUrl();

            URL.ForUrl(GetFullUrl(URL.Url, browser, out ((Browser)browser)._areRobotsAllowed));

            TabTuple retVal = Client.NewTab(URL.Url).Execute<TabTuple>();
            browser.ServerResponseCode = GetStatusCode();

            return retVal;
        }

        /// <summary>
        /// Switches to the next tab
        /// <example>TabTuple tab = browser.MagicHelper.NextTab();</example>
        /// </summary>
        /// <returns>The tab details</returns>
        public TabTuple NextTab()
        {
            return Client.NextTab().Execute<TabTuple>();
        }

        /// <summary>
        /// Number of tabs open in the browser. Can differ from what GPAL opened; the browser closes a tab whose
        /// first navigation becomes a download.
        /// <example>int tabs = browser.MagicHelper.TabCount();</example>
        /// </summary>
        /// <returns>The number of tabs open, or zero when the extension could not say</returns>
        public int TabCount()
        {
            return Client.TabCount().Execute<int>();
        }

        /// <summary>
        /// Switches to the next window
        /// <example>WindowTuple window = browser.MagicHelper.NextWindow();</example>
        /// </summary>
        /// <returns>The window details</returns>
        public WindowTuple NextWindow()
        {
            return Client.NextWindow().Execute<WindowTuple>();
        }
        /// <summary>
        /// Switches to the previous tab
        /// <example>TabTuple tab = browser.MagicHelper.PreviousTab();</example>
        /// </summary>
        /// <returns>The tab details</returns>
        public TabTuple PreviousTab()
        {
            return Client.PreviousTab().Execute<TabTuple>();
        }


        /// <summary>
        /// Switches to the previous window
        /// <example>WindowTuple window = browser.MagicHelper.PreviousWindow();</example>
        /// </summary>
        /// <returns>The window details</returns>
        public WindowTuple PreviousWindow()
        {
            return Client.PreviousWindow().Execute<WindowTuple>();
        }

        /// <summary>
        /// Restores the browser window
        /// <example>browser.MagicHelper.Restore();</example>
        /// </summary>
        public void Restore()
        {
            Client.Restore().Execute();
        }

        /// <summary>
        /// Gets the window screen left position
        /// <example>int left = browser.MagicHelper.WindowScreenLeft();</example>
        /// </summary>
        /// <returns>The screen left position</returns>
        public int WindowScreenLeft()
        {
            return Client.WindowScreenLeft().Execute<int>();
        }

        /// <summary>
        /// Gets the window screen top position
        /// <example>int top = browser.MagicHelper.WindowScreenTop();</example>
        /// </summary>
        /// <returns>The screen top position</returns>
        public int WindowScreenTop()
        {
            return Client.WindowScreenTop().Execute<int>();
        }
        /// <summary>
        /// Returns the browser window inner/outer height/widge
        /// { y: window.innerHeight, x: window.width, height: window.outerHeight, width: window.outerWidth }
        /// </summary>
        /// <returns></returns>
        public Rectangle GetWindowRectangle()
        {
            Rectangle rect = new Rectangle();
            var result =  Client.GetWindowRectangle().Execute<Dictionary<string, object>>();
            Dictionary<string, object> obj = null;

            if (null != result)
            {
                obj = (Dictionary<string, object>)result;

                // the extension answers { X, Y, Width, Height }
                var left = obj.ContainsKey("X") ? Convert.ToInt32(obj["X"]) : 0;
                var top = obj.ContainsKey("Y") ? Convert.ToInt32(obj["Y"]) : 0;
                var width = obj.ContainsKey("Width") ? Convert.ToInt32(obj["Width"]) : 0;
                var height = obj.ContainsKey("Height") ? Convert.ToInt32(obj["Height"]) : 0;

                rect = new Rectangle(left, top, width, height);
            }

            return rect;
        }

        /// <summary>
        /// Gets the window inner height
        /// <example>int height = browser.MagicHelper.WindowInnerHeight();</example>
        /// </summary>
        /// <returns>The inner height</returns>
        public int WindowInnerHeight()
        {
            return Client.WindowInnerHeight().Execute<int>();
        }

        /// <summary>
        /// Gets the window inner width
        /// <example>int width = browser.MagicHelper.WindowInnerWidth();</example>
        /// </summary>
        /// <returns>The inner width</returns>
        public int WindowInnerWidth()
        {
            return Client.WindowInnerWidth().Execute<int>();
        }

        /// <summary>
        /// Gets the window outer height
        /// <example>int height = browser.MagicHelper.WindowOuterHeight();</example>
        /// </summary>
        /// <returns>The outer height</returns>
        public int WindowOuterHeight()
        {
            return Client.WindowOuterHeight().Execute<int>();
        }

        /// <summary>
        /// Gets the window outer width
        /// <example>int width = browser.MagicHelper.WindowOuterWidth();</example>
        /// </summary>
        /// <returns>The outer width</returns>
        public int WindowOuterWidth()
        {
            return Client.WindowOuterWidth().Execute<int>();
        }

        /// <summary>
        /// Gets the window page offset X
        /// <example>int offset = browser.MagicHelper.WindowPageOffsetX();</example>
        /// </summary>
        /// <returns>The page offset X</returns>
        public int WindowPageOffsetX()
        {
            // Firefox reports the scroll with a fraction (9581.150390625), so it is read as a decimal and rounded
            return (int)Math.Round(Client.WindowPageOffsetX().Execute<double>());
        }

        /// <summary>
        /// Gets the window page offset Y
        /// <example>int offset = browser.MagicHelper.WindowPageOffsetY();</example>
        /// </summary>
        /// <returns>The page offset Y</returns>
        public int WindowPageOffsetY()
        {
            // Firefox reports the scroll with a fraction, so it is read as a decimal and rounded
            return (int)Math.Round(Client.WindowPageOffsetY().Execute<double>());
        }

        public WindowTuple OpenWindow(GPALUrl URL = null)
        {
            if (null == URL)
                URL = new GPALUrl();

            URL.ForUrl(GetFullUrl(URL.Url, browser, out ((Browser)browser)._areRobotsAllowed));

            WindowTuple retVal = Client.OpenWindow(URL.Url).Execute<WindowTuple>();
            browser.ServerResponseCode = GetStatusCode();

            return retVal;
        }

        public WindowTuple GoToWindow(dynamic urlOrId)
        {
            if (urlOrId is string v)
                return Client.GoToWindow(v).Execute<WindowTuple>();
            else
                return Client.GoToWindow(urlOrId.ToString()).Execute<WindowTuple>();
        }
        /// <summary>
        /// Closes the browser by closing all its windows, the only way an extension can quit it.
        /// <example>browser.MagicHelper.CloseBrowser();</example>
        /// </summary>
        /// <returns>The reply, sent first, since closing the last window stops the extension</returns>
        public string CloseBrowser()
        {
            return Client.CloseBrowser().Execute();
        }

        /// <summary>
        /// Closes a window: an int is the window id, a string a url the extension matches against every tab, and null
        /// the current window.
        /// </summary>
        /// <param name="urlOrId">The window id, the url, or null</param>
        /// <returns>The window left active; its WindowId is null when the last window closed, and the tuple is null
        /// when no window matched</returns>
        public WindowTuple CloseWindow(dynamic urlOrId)
        {
            string reply;

            if (urlOrId is int id)
                reply = Client.CloseWindow(id).Execute();
            else if (urlOrId is string url)
                reply = Client.CloseWindow((GPALUrl)url).Execute();
            else
                reply = Client.CloseWindow((GPALUrl)null).Execute();

            // The body comes back JSON encoded, so a string answer is in quotes. The extension answers NOTOK when no
            // window matched; GPALRestAPI answers with plain text when the browser closed or did not answer in time.
            string answer = reply?.Trim('"');
            WindowTuple retVal = null;

            if (false == string.IsNullOrEmpty(answer) && "NOTOK" != answer)
            {
                try
                {
                    retVal = Newtonsoft.Json.JsonConvert.DeserializeObject<WindowTuple>(reply);
                }
                catch (Newtonsoft.Json.JsonException ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"close-window answered [{answer}], not a window", this, GPALObjectType.Other, ex);
                }
            }

            return retVal;
        }

        public WindowTuple GetCurrentWindow()
        {
            return Client.GetCurrentWindow().Execute<WindowTuple>();
        }

        /// <summary>
        /// Brings the browser window to the top
        /// <example>bool success = browser.MagicHelper.TopBrowser(process);</example>
        /// </summary>
        /// <param name="process">The browser process</param>
        /// <returns>Whether the operation succeeded</returns>
        public bool TopBrowser(Process process) => WindowHelper.TopBrowser(process);
    }
}
