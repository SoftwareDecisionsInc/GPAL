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
    public class WindowSession
    {
        /// <summary>
        /// The CDP browser context id (window) this session belongs to.
        /// </summary>
        public string BrowserContextId { get; set; }
        /// <summary>
        /// Queue of (sessionId, targetId) pairs for tabs open within this window session.
        /// </summary>
        public ConcurrentQueue<KeyValuePair<string, string>> TabQueue { get; set; }
        /// <summary>
        /// Index of the currently active tab within <see cref="TabQueue"/>.
        /// </summary>
        public int ActiveTabIndex { get; set; }

        public WindowSession(string browserContextId, ConcurrentQueue<KeyValuePair<string, string>> tabQueue, int activeTabIndex = 0)
        {
            BrowserContextId = browserContextId;
            TabQueue = tabQueue ?? new ConcurrentQueue<KeyValuePair<string, string>>();
            ActiveTabIndex = activeTabIndex;
        }
    }
}
