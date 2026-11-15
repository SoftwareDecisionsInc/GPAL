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
    /// Result returned by GPALYouTube.CreatePlaylist containing the playlist ID and public URL.
    /// </summary>
    public class YouTubePlaylistResult
    {
        public string PlaylistId { get; }
        public string Url        { get; }
        internal YouTubePlaylistResult(string playlistId)
        {
            PlaylistId = playlistId;
            Url        = $"https://www.youtube.com/playlist?list={playlistId}";
        }
    }
}
