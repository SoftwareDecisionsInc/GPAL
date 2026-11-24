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
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
//using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static GenerallyPositive.Browser.Puppeteer;
using static GenerallyPositive.Enums;

namespace GenerallyPositive.Browser
{
    public partial class PuppeteerCommunicator
    {
        internal class CastDevice
        {
            public CastDevice(string name, string id)
            {
                this.name = name;
                this.id = id;
            }
            public string name;
            public string id;
        }

        // Implementation for CastDesktop
        /// <summary>
        /// Searches for a Cast device with the given sink name and, if found, starts mirroring the entire
        /// desktop to it. Retries discovery up to 10 times, waiting between attempts for the device to appear.
        /// </summary>
        /// <param name="sinkName">The display name of the Cast sink/device to mirror to.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.CastDesktop("Living Room TV", sessionId);
        /// </code>
        /// </example>
        public async Task CastDesktop(string sinkName, string sessionId)
        {
            CastDevice sink = null;
            int retries = 10;
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.CastDisable, null, sessionId).ConfigureAwait(false);

            while (null == sink && 0 < retries--)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{sinkName}].", this, GPALObjectType.PuppeteerCommunicator);
                await SendCommand<object>(DevToolsMethods.CastEnable, new { }, sessionId).ConfigureAwait(false);

                await Task.Delay(3_000).ConfigureAwait(false);

                sink = CastDevices.Find(device => device.name == sinkName);
            }

            await Task.Delay(3_000).ConfigureAwait(false);

            if (null != sink)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{sinkName}] found.", this, GPALObjectType.PuppeteerCommunicator);
                SinkName = sinkName;
                object param = new { sinkName = sink.name };

                // cast or javascript, not both
                await SendCommand<object>(DevToolsMethods.CastStartDesktopMirroring, param, sessionId).ConfigureAwait(false);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{sinkName}] NOT found.", this, GPALObjectType.PuppeteerCommunicator);
        }

        // Implementation for CastTab
        /// <summary>
        /// Searches for a Cast device with the given sink name and, if found, starts mirroring the current
        /// tab to it. Retries discovery up to 10 times, waiting between attempts for the device to appear.
        /// </summary>
        /// <param name="sinkName">The display name of the Cast sink/device to mirror to.</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <example>
        /// <code>
        /// await communicator.CastTab("Living Room TV", sessionId);
        /// </code>
        /// </example>
        public async Task CastTab(string sinkName, string sessionId)
        {
            CastDevice sink = null;
            int retries = 10;
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.CastDisable, null, sessionId).ConfigureAwait(false);

            while (null == sink && 0 < retries--)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Searching for [{sinkName}].", this, GPALObjectType.PuppeteerCommunicator);
                await SendCommand<object>(DevToolsMethods.CastEnable, new { }, sessionId).ConfigureAwait(false);

                await Task.Delay(3_000).ConfigureAwait(false);

                sink = CastDevices.Find(device => device.name == sinkName);
            }

            await Task.Delay(3_000).ConfigureAwait(false);

            if (null != sink)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{sinkName}] found.", this, GPALObjectType.PuppeteerCommunicator);
                SinkName = sinkName;
                object param = new { sinkName = sink.name };

                // cast or javascript, not both
                await SendCommand<object>(DevToolsMethods.CastStartTabMirroring, param, sessionId).ConfigureAwait(false);
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{sinkName}] NOT found.", this, GPALObjectType.PuppeteerCommunicator);
        }

        // Implementation for "stop-casting"
        /// <summary>
        /// Sends Cast.stopCasting for the current sink. Casting may keep going after it; closing the tab or window
        /// ends it.
        /// </summary>
        /// <param name="sessionId">CDP session id to send the stop-casting command on.</param>
        /// <returns>Task that resolves to true once the stop attempt has completed (regardless of whether casting actually stopped).</returns>
        /// <example>
        /// <code>
        /// await communicator.StopCasting(sessionId);
        /// </code>
        /// </example>
        public async Task<bool> StopCasting(string sessionId)
        {
            // CAVEAT: casting can continue after Cast.stopCasting; closing the tab or window ends it
            try
            {
                await SendCommand<object>(DevToolsMethods.CastStopCasting, new { sinkName = SinkName }, sessionId).ConfigureAwait(false);
            }
            catch { }

            Thread.Sleep(500);

            return true;
        }
    }
}
