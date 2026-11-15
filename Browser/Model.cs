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
    public class Model
    {
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the content box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Content { get; set; } // Use double[] for the array of numbers
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the padding box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Padding { get; set; }
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the border box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Border { get; set; }
        /// <summary>
        /// Gets or sets the quad of (x, y) coordinate pairs describing the margin box, in the order top-left, top-right, bottom-right, bottom-left.
        /// </summary>
        public double[] Margin { get; set; }
        /// <summary>
        /// Gets or sets the overall width of the node's box model, in pixels.
        /// </summary>
        public int Width { get; set; }
        /// <summary>
        /// Gets or sets the overall height of the node's box model, in pixels.
        /// </summary>
        public int Height { get; set; }
    }
}
