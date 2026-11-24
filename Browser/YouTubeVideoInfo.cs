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
    /// Video metadata returned by GPALYouTube.GetResults and GPALYouTube.GetMyVideos.
    /// Tags, Privacy, CategoryId and ThumbnailUrl are populated for direct video lookups and GetMyVideos;
    /// search results leave Tags empty and Privacy, CategoryId and ThumbnailUrl null.
    /// </summary>
    public class YouTubeVideoInfo
    {
        public string    VideoId     { get; }
        public string    Title       { get; }
        public string    Description { get; }
        public string[]  Tags        { get; }
        public string    Url         { get; }
        public DateTime? PublishedAt { get; }
        /// <summary>
        /// Public, unlisted or private. Null when YouTube did not send the video's status, as in search results.
        /// </summary>
        public YouTubePrivacy? Privacy     { get; internal set; }
        /// <summary>
        /// The YouTube category id, for example "28" for Science and Technology.
        /// </summary>
        public string          CategoryId  { get; internal set; }
        /// <summary>
        /// The largest thumbnail YouTube has for the video: maxres, standard, high, medium, then default.
        /// </summary>
        public string          ThumbnailUrl { get; internal set; }
        internal YouTubeVideoInfo(string videoId, string title, string description, string[] tags, DateTime? publishedAt)
        {
            VideoId     = videoId;
            Title       = title       ?? string.Empty;
            Description = description ?? string.Empty;
            Tags        = tags        ?? new string[0];
            Url         = $"https://youtu.be/{videoId}";
            PublishedAt = publishedAt;
        }
    }
}
