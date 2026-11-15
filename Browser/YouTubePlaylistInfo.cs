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
    /// A playlist on the authenticated channel, returned by GPALYouTube.GetMyPlaylists.
    /// </summary>
    public class YouTubePlaylistInfo
    {
        public string          PlaylistId  { get; }
        public string          Title       { get; }
        public string          Description { get; }
        public YouTubePrivacy? Privacy     { get; }
        public int             ItemCount   { get; }
        public string          Url         { get; }
        internal YouTubePlaylistInfo(string playlistId, string title, string description, YouTubePrivacy? privacy, int itemCount)
        {
            PlaylistId  = playlistId;
            Title       = title       ?? string.Empty;
            Description = description ?? string.Empty;
            Privacy     = privacy;
            ItemCount   = itemCount;
            Url         = $"https://www.youtube.com/playlist?list={playlistId}";
        }
    }
}
