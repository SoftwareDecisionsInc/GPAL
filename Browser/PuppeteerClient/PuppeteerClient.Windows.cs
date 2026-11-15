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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerClient
    {
        /// <summary>
        /// Configure this client to capture a screenshot of the visible tab in the given image format.
        /// </summary>
        /// <param name="imageFormat">The image format to capture</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CaptureVisibleTab(ImageFormat imageFormat = ImageFormat.JPEG)
        {
            WithImageFormat(imageFormat);
            WithEndpoint(ApiEndpoint.CaptureVisibleTab);
            return this;
        }

        /// <summary>
        /// Configure this client to close the tab with the given URL, or the current tab if none is given.
        /// </summary>
        /// <param name="url">The URL of the tab to close, or null for the current tab</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseTab(GPALUrl url = null)
        {
            WithEndpoint(ApiEndpoint.CloseTab);
            if (url != null) WithUrl(url.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to close the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id to close</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.CloseTab);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to close the window containing the given URL, or the current window if none is given.
        /// </summary>
        /// <param name="url">The URL of the tab whose window should be closed, or null for the current window</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseWindow(GPALUrl url = null)
        {
            WithEndpoint(ApiEndpoint.CloseWindow);
            if (url != null) WithUrl(url.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to close the window containing the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id whose window should be closed</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution CloseWindow(int tabId)
        {
            WithEndpoint(ApiEndpoint.CloseWindow);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to make the browser window fullscreen.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution FullScreen()
        {
            WithEndpoint(ApiEndpoint.FullScreen);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve information about the current browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GetCurrentWindow()
        {
            WithEndpoint(ApiEndpoint.GetCurrentWindow);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the tab whose URL matches the given URL.
        /// </summary>
        /// <param name="url">The URL of the tab to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToTab(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToTab);
            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToTab(int tabId)
        {
            WithEndpoint(ApiEndpoint.GoToTab);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the window containing the tab whose URL matches the given URL.
        /// </summary>
        /// <param name="url">The URL of the tab whose window to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.GoToWindow);
            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the window containing the tab with the given tab id.
        /// </summary>
        /// <param name="tabId">The tab id whose window to switch to</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution GoToWindow(int tabId)
        {
            WithEndpoint(ApiEndpoint.GoToWindow);
            WithTabId(tabId);
            return this;
        }

        /// <summary>
        /// Configure this client to maximize the browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Maximize()
        {
            WithEndpoint(ApiEndpoint.Maximize);
            return this;
        }

        /// <summary>
        /// Configure this client to minimize the browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Minimize()
        {
            WithEndpoint(ApiEndpoint.Minimize);
            return this;
        }

        /// <summary>
        /// Configure this client to open a new tab navigated to the given URL, resolving relative URLs and checking robots.txt restrictions.
        /// </summary>
        /// <param name="url">The URL to navigate to in the new tab, or null for a blank tab</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution NewTab(GPALUrl url = null)
        {
            WithEndpoint(ApiEndpoint.NewTab);

            url?.ForUrl(MagicHelper.GetFullUrl(url?.Url, _puppeteerCommunicator.Browser, out _puppeteerCommunicator.Browser._areRobotsAllowed));

            if (false == _puppeteerCommunicator.Browser.AreRobotsAllowed && true == _puppeteerCommunicator.Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url.Url}] not allowed to robots and your honoring robots.txt. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerClient);
                url.ForUrl("https://google.com");
            }

            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the next tab.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution NextTab()
        {
            WithEndpoint(ApiEndpoint.NextTab);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the next browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution NextWindow()
        {
            WithEndpoint(ApiEndpoint.NextWindow);
            return this;
        }

        /// <summary>
        /// Configure this client to restore the browser window to its normal (non-maximized, non-minimized) state.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Normal()
        {
            WithEndpoint(ApiEndpoint.Normal);
            return this;
        }

        /// <summary>
        /// Configure this client to open a new browser window navigated to the given URL, resolving relative URLs and checking robots.txt restrictions.
        /// </summary>
        /// <param name="url">The URL to navigate to in the new window</param>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution OpenWindow(GPALUrl url)
        {
            WithEndpoint(ApiEndpoint.OpenWindow);

            url.ForUrl(MagicHelper.GetFullUrl(url?.Url, _puppeteerCommunicator.Browser, out _puppeteerCommunicator.Browser._areRobotsAllowed));

            if (false == _puppeteerCommunicator.Browser.AreRobotsAllowed && true == _puppeteerCommunicator.Browser.BrowserSettings.ObeyRobotsTxt)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url?.Url}] not allowed to robots and your honoring robots.txt. Not going to URL. Workflow will fail.", this, GPALObjectType.PuppeteerClient);
                url.ForUrl("https://google.com");
            }

            WithUrl(url?.Url);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the previous tab.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PreviousTab()
        {
            WithEndpoint(ApiEndpoint.PreviousTab);
            return this;
        }

        /// <summary>
        /// Configure this client to switch to the previous browser window.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution PreviousWindow()
        {
            WithEndpoint(ApiEndpoint.PreviousWindow);
            return this;
        }
        /// <summary>
        /// Configure this client to restore the browser window to its normal size.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution Restore()
        {
            WithEndpoint(ApiEndpoint.Restore);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's inner height.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowInnerHeight()
        {
            WithEndpoint(ApiEndpoint.WindowInnerHeight);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's inner width.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowInnerWidth()
        {
            WithEndpoint(ApiEndpoint.WindowInnerWidth);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's outer height.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowOuterHeight()
        {
            WithEndpoint(ApiEndpoint.WindowOuterHeight);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's outer width.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowOuterWidth()
        {
            WithEndpoint(ApiEndpoint.WindowOuterWidth);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the page's horizontal scroll offset.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowPageOffsetX()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetX);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the page's vertical scroll offset.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowPageOffsetY()
        {
            WithEndpoint(ApiEndpoint.WindowPageOffsetY);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's screen X position.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowScreenLeft()
        {
            WithEndpoint(ApiEndpoint.WindowScreenLeft);
            return this;
        }

        /// <summary>
        /// Configure this client to retrieve the browser window's screen Y position.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution WindowScreenTop()
        {
            WithEndpoint(ApiEndpoint.WindowScreenTop);
            return this;
        }

        /// <summary>
        /// Configure this client to bring the browser window to the foreground.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution TopBrowser()
        {
            WithEndpoint(ApiEndpoint.TopBrowser);
            return this;
        }
    }
}
