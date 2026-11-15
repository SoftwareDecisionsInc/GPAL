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
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using static GenerallyPositive.Enums;
using GenerallyPositive;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Result returned by GPALYouTube.Upload containing the assigned video ID, public URL, and status.
    /// </summary>
    public class YouTubeUploadResult
    {
        public string VideoId { get; }
        public string Url     { get; }
        public string Status  { get; }

        internal YouTubeUploadResult(string videoId, string url, string status)
        {
            VideoId = videoId;
            Url     = url;
            Status  = status;
        }
    }
}
