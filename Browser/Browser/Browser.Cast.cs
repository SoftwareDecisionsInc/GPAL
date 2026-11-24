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

#define DONOTUSECDP // which solution to use for printDomElement

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using DocumentFormat.OpenXml.Bibliography;
using GenerallyPositive.GPALForm;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using static GenerallyPositive.Enums;
using static GenerallyPositive.SelectorSettings;
using HWND = System.IntPtr;

// Desktops aliases the static HiddenDesktop class, which the HiddenDesktop property hides in this file.
using Desktops = GenerallyPositive.Browser.HiddenDesktop;

namespace GenerallyPositive.Browser
{
    public partial class Browser
    {
        /// <summary>
        /// Name of the cast sink (e.g. Chromecast device) currently being cast to, if any.
        /// </summary>
        private string SinkName { get; set; }

        /// <summary>
        /// Casts (mirrors) the current browser tab to the named cast sink (e.g. a Chromecast device).<br/>
        /// Supported via Puppeteer and Selenium; not available via OttoMagic.
        /// </summary>
        /// <param name="sinkName">Name of the cast sink to cast to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CastTab(string sinkName)
        {
            SinkName = sinkName;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Casting Tab to [{sinkName}]");

            if (true == UsePuppeteer)
                PuppeteerClient.CastTab(sinkName).Execute();
            else if (true == UseSelenium)
                CastTo_Selenium(sinkName, CastMode.Tab);
            else if (true == UseOttoMagic)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Casting is not available via OttoMagic");

            return this;
        }

        /// <summary>
        /// Casts (mirrors) the entire desktop to the named cast sink (e.g. a Chromecast device).<br/>
        /// Supported via Puppeteer and Selenium; not available via OttoMagic.
        /// </summary>
        /// <param name="sinkName">Name of the cast sink to cast to</param>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector CastDesktop(string sinkName)
        {
            SinkName = sinkName;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Casting Desktop to [{sinkName}]");

            if (true == UsePuppeteer)
                PuppeteerClient.CastDesktop(sinkName).Execute();
            else if (true == UseSelenium)
                CastTo_Selenium(sinkName, CastMode.Desktop);
            else if (true == UseOttoMagic)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Casting is not available via OttoMagic");

            return this;
        }

        /// <summary>
        /// Stops any active cast (tab or desktop mirroring) started via <see cref="CastTab"/> or <see cref="CastDesktop"/>.
        /// </summary>
        /// <returns>Fluent interface to write your workflow</returns>
        public IAllowBrowserActionOrAnySelector StopCasting()
        {
            if (false == string.IsNullOrEmpty(SinkName))
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Stopping cast to [{SinkName}]");

                if (true == UsePuppeteer)
                {
                    if (null != PuppeteerCommunicator.SinkName)
                    {
                        PuppeteerClient.StopCasting().Execute();
                        PuppeteerCommunicator.SinkName = null;
                    }
                }
                else if (true == UseSelenium)
                {

                    var param = new Dictionary<string, object> { ["sinkName"] = SinkName };

                    if (null != SinkName)
                        try
                        {
                            // Stop casting to the sink over CDP
                            BrowserHelper.ExecuteCdp(BrowserDriver, "Cast.stopCasting", param);
                            SinkName = null;
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Cast.stopCasting failed, which it does sometimes", this, GPALObjectType.Browser, ex);
                        }
                }
            }

            return this;
        }

        /// <summary>
        /// Starts tab or desktop mirroring to the named cast sink via Chrome DevTools Protocol Cast commands, retrying until the sink is found.
        /// </summary>
        /// <param name="sinkName">Name of the cast sink to cast to</param>
        /// <param name="mode">Whether to mirror the current tab or the entire desktop</param>
        /// <returns>Fluent interface to write your workflow</returns>
        private IAllowBrowserActionOrAnySelector CastTo_Selenium(string sinkName, CastMode mode)
        {
            int retries = 10;

            if (string.IsNullOrEmpty(sinkName))
                return this;

            BrowserHelper.ExecuteCdp(BrowserDriver, "Cast.enable", 
                new Dictionary<string, object>());

            Thread.Sleep(3_000);
            var param = new Dictionary<string, object> { ["sinkName"] = sinkName };

            string command = (mode == CastMode.Tab)
                ? "Cast.startTabMirroring"
                : "Cast.startDesktopMirroring";

            while (0 < retries)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{sinkName}].");
                try
                {
                    BrowserHelper.ExecuteCdp(BrowserDriver, command, param);

                    GPAL.PublishSimpleEvent(GPALEventType.INFO,
                        $"[{sinkName}] found.", this, GPALObjectType.Browser);
                    break;
                }
                catch
                {
                    retries--;
                }
                Thread.Sleep(3_000);
            }
            return this;
        }
    }
}
