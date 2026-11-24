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
    /// One entry in a playlist, returned by GPALYouTube.GetPlaylistItems. PlaylistItemId names the entry,
    /// VideoId the video in it; the same video can be in a playlist more than once.
    /// </summary>
    public class YouTubePlaylistItem
    {
        public string PlaylistItemId { get; }
        public string PlaylistId     { get; }
        public string VideoId        { get; }
        public string Title          { get; }
        public int    Position       { get; }
        internal YouTubePlaylistItem(string playlistItemId, string playlistId, string videoId, string title, int position)
        {
            PlaylistItemId = playlistItemId;
            PlaylistId     = playlistId;
            VideoId        = videoId;
            Title          = title ?? string.Empty;
            Position       = position;
        }
    }
}
