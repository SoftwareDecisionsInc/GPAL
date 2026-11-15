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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Office2019.Drawing.Diagram11;
using GenerallyPositive.Browser;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    public partial class RESTClient
    {
        /// <summary>
        /// Get a screenshot of the current tab visble contents
        /// </summary>
        /// <returns>Base64 string</returns>
        public IAllowRESTExecution CaptureVisibleTab(ImageFormat imageFormat = ImageFormat.JPEG)
        {
            WithImageFormat(imageFormat);
            WithEndpoint(ApiEndpoint.CaptureVisibleTab);
            return this;
        }
        public IAllowRESTExecution GetWindowRectangle()
        {
            WithEndpoint(ApiEndpoint.GetWindowRectangle);
            return this;
        }

        /// <summary>
        /// Top the browser, bring it into focus. Used before performing any actions on the browser.
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution TopBrowser()
        {
            WithEndpoint(ApiEndpoint.TopBrowser);
            return this;
        }

        /// <summary>
        /// Gets the window inner height
        /// <example>client.WindowInnerHeight().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowInnerHeight()
        {
            WithEndpoint(ApiEndpoint.WindowInnerHeight);
            return this;
        }

        /// <summary>
        /// Gets the window inner width
        /// <example>client.WindowInnerWidth().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowInnerWidth()
        {
            WithEndpoint(ApiEndpoint.WindowInnerWidth);
            return this;
        }

        /// <summary>
        /// Gets the window outer height
        /// <example>client.WindowOuterHeight().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowOuterHeight()
        {
            WithEndpoint(ApiEndpoint.WindowOuterHeight);
            return this;
        }

        /// <summary>
        /// Gets the window outer width
        /// <example>client.WindowOuterWidth().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowOuterWidth()
        {
            WithEndpoint(ApiEndpoint.WindowOuterWidth);
            return this;
        }

        /// <summary>
        /// Gets the window page offset X
        /// <example>client.WindowPageOffsetX().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowPageOffsetX()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetX);
            return this;
        }

        /// <summary>
        /// Gets the window page offset Y
        /// <example>client.WindowPageOffsetY().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowPageOffsetY()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetY);
            return this;
        }

        /// <summary>
        /// Gets the window screen left position
        /// <example>client.WindowScreenLeft().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowScreenLeft()
        {
            WithEndpoint(ApiEndpoint.WindowScreenLeft);
            return this;
        }

        /// <summary>
        /// Gets the window screen top position
        /// <example>client.WindowScreenTop().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution WindowScreenTop()
        {
            WithEndpoint(ApiEndpoint.WindowScreenTop);
            return this;
        }

        /// <summary>
        /// Closes a tab by tab ID
        /// <example>client.CloseTab(1).Execute();</example>
        /// </summary>
        /// <param name="tabId">The tab ID</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.CloseTab).WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Closes a tab by URL
        /// <example>client.CloseTab("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL (optional)</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseTab(GPALUrl url = null)
        {
            if (null != url)
                WithEndpoint(ApiEndpoint.CloseTab).WithUrl(url.Url);
            else
                WithEndpoint(ApiEndpoint.CloseTab);
            return this;
        }

        /// <summary>
        /// Sets browser to full screen
        /// <example>client.FullScreen().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution FullScreen()
        {
            WithEndpoint(ApiEndpoint.FullScreen);
            return this;
        }

        /// <summary>
        /// Navigates to a tab by tab ID
        /// <example>client.GotoTab(1).Execute();</example>
        /// </summary>
        /// <param name="tabId">The tab ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.GoToTab).WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Navigates to a tab by URL
        /// <example>client.GotoTab("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToTab(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToTab).WithUrl(url?.Url);
            return this;
        }
        
        /// <summary>
        /// Maximizes the browser window
        /// <example>client.Maximize().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Maximize()
        {
            WithEndpoint(ApiEndpoint.Maximize);
            return this;
        }

        /// <summary>
        /// Minimizes the browser window
        /// <example>client.Minimize().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Minimize()
        {
            WithEndpoint(ApiEndpoint.Minimize);
            return this;
        }

        /// <summary>
        /// Opens a new tab with optional URL
        /// <example>client.NewTab("https://example.com").Execute();</example>
        /// </summary>
        /// <param name="url">The URL (optional)</param>
        /// <returns></returns>
        public IAllowRESTExecution NewTab(GPALUrl url)
        {

            WithEndpoint(ApiEndpoint.NewTab);

            if (null != url) 
                WithUrl(url?.Url);

            return this;
        }

        /// <summary>
        /// Switches to the next tab
        /// <example>client.NextTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution NextTab()
        {
            WithEndpoint(ApiEndpoint.NextTab);
            return this;
        }

        /// <summary>
        /// Returns how many tabs the browser has open, which can differ from how many GPAL opened: a browser closes a
        /// tab whose first navigation becomes a download.
        /// <example>int tabs = client.TabCount().Execute&lt;int&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution TabCount()
        {
            WithEndpoint(ApiEndpoint.TabCount);
            return this;
        }

        /// <summary>
        /// Switches to the next window
        /// <example>client.NextTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution NextWindow()
        {
            WithEndpoint(ApiEndpoint.NextWindow);
            return this;
        }

        /// <summary>
        /// Restores the browser window to normal
        /// <example>client.Normal().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Normal()
        {
            WithEndpoint(ApiEndpoint.Normal);
            return this;
        }
        /// <summary>
        /// Switches to the previous tab
        /// <example>client.PreviousTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PreviousTab()
        {
            WithEndpoint(ApiEndpoint.PreviousTab);
            return this;
        }

        /// <summary>
        /// Switches to the next window
        /// <example>client.NextTab().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution PreviousWindow()
        {
            WithEndpoint(ApiEndpoint.PreviousWindow);
            return this;
        }

        /// <summary>
        /// Restores the browser window
        /// <example>client.Restore().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution Restore()
        {
            WithEndpoint(ApiEndpoint.Restore);
            return this;
        }
        /// <summary>
        /// Opens a new window with the specified URL
        /// <example>client.OpenWindow("https://example.com").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="url">The URL to open (optional, defaults to "https://google.com")</param>
        /// <returns></returns>
        public IAllowRESTExecution OpenWindow(GPALUrl url)
        {
            if (true == string.IsNullOrEmpty(url))
                url = new GPALUrl("google.com");

            WithEndpoint(ApiEndpoint.OpenWindow).WithUrl(url.Url);
            return this;
        }

        /// <summary>
        /// Switches to a window by URL
        /// <example>client.GoToWindow("https://example.com").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="url">The window URL</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToWindow).WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Switches to a window by window ID
        /// <example>client.GoToWindow(123).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="windowId">The window ID</param>
        /// <returns></returns>
        public IAllowRESTExecution GoToWindow(int windowId)
        {
            WithEndpoint(ApiEndpoint.GoToWindow).WithWindowId(windowId);
            return this;
        }

        /// <summary>
        /// Closes every browser window, which makes the browser quit.
        /// <example>client.CloseBrowser().Execute();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution CloseBrowser()
        {
            WithEndpoint(ApiEndpoint.CloseBrowser);
            return this;
        }

        /// <summary>
        /// Closes a window by URL, optionally switching to the main window
        /// <example>client.CloseWindow("https://example.com").Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="url">The window URL</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.CloseWindow);
            
            if (null != url)
                WithUrl(url?.Url);

            return this;
        }

        /// <summary>
        /// Closes a window by window ID, optionally switching to the main window
        /// <example>client.CloseWindow(123).Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <param name="windowId">The window ID</param>
        /// <returns></returns>
        public IAllowRESTExecution CloseWindow(int windowId)
        {
            WithEndpoint(ApiEndpoint.CloseWindow).WithWindowId(windowId);
            return this;
        }
        /// <summary>
        /// Gets the current window's ID and URL
        /// <example>client.GetCurrentWindow().Execute&lt;WindowTuple&gt;();</example>
        /// </summary>
        /// <returns></returns>
        public IAllowRESTExecution GetCurrentWindow()
        {
            WithEndpoint(ApiEndpoint.GetCurrentWindow);
            return this;
        }
    }
}
