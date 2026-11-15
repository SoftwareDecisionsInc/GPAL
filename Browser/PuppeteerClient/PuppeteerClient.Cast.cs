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
        /// Cast the desktop to the specified device name
        /// <example>client.CastDesktop("living room").Execute();</example>
        /// </summary>
        /// <param name="deviceNameOrId">The Cast Device Name or ID</param>
        /// <returns></returns>
        public IAllowPuppeteerExecution CastDesktop(string deviceNameOrId)
        {
            WithEndpoint(ApiEndpoint.CastDesktop).WithDeviceName(deviceNameOrId);
            return this;
        }
        /// <summary>
        /// Cast the current tab to the specified device name
        /// <example>client.CastTab("living room").Execute();</example>
        /// </summary>
        /// <param name="deviceNameOrId">The Cast Device Name or ID</param>
        /// <returns></returns>
        public IAllowPuppeteerExecution CastTab(string deviceNameOrId)
        {
            WithEndpoint(ApiEndpoint.CastTab).WithDeviceName(deviceNameOrId);
            return this;
        }

        /// <summary>
        /// Configure this client to stop any active screen casting.
        /// </summary>
        /// <returns>Fluent interface to continue building the request</returns>
        public IAllowPuppeteerExecution StopCasting()
        {
            WithEndpoint(ApiEndpoint.StopCasting);
            return this;
        }
    }
}
