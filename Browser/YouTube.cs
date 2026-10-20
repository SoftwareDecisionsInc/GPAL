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
    /// Represents a premiere schedule time. Presets (In5Minutes, In1Hour, etc.) store a relative
    /// offset and resolve to UtcNow + offset at the moment Resolve() is called, so the premiere
    /// time is set relative to when each video finishes processing, not when the chain was built.
    /// </summary>
    public readonly struct PremiereTime
    {
        private readonly bool     _isRelative;
        private readonly DateTime _absolute;
        private readonly TimeSpan _offset;

        private PremiereTime(TimeSpan offset)   { _isRelative = true;  _offset   = offset;                                                        _absolute = default; }
        private PremiereTime(DateTime absolute) { _isRelative = false; _absolute = absolute.Kind == DateTimeKind.Utc ? absolute : absolute.ToUniversalTime(); _offset = default; }

        public static PremiereTime In1Minute   => new PremiereTime(TimeSpan.FromMinutes(1));
        public static PremiereTime In5Minutes  => new PremiereTime(TimeSpan.FromMinutes(5));
        public static PremiereTime In10Minutes => new PremiereTime(TimeSpan.FromMinutes(10));
        public static PremiereTime In15Minutes => new PremiereTime(TimeSpan.FromMinutes(15));
        public static PremiereTime In30Minutes => new PremiereTime(TimeSpan.FromMinutes(30));
        public static PremiereTime In45Minutes => new PremiereTime(TimeSpan.FromMinutes(45));
        public static PremiereTime In1Hour     => new PremiereTime(TimeSpan.FromHours(1));

        public static PremiereTime At(DateTime dt) => new PremiereTime(dt);

        internal DateTime Resolve() => _isRelative ? DateTime.UtcNow.Add(_offset) : _absolute;

        public static implicit operator DateTime(PremiereTime pt) => pt.Resolve();

        public override string ToString() => _isRelative
            ? $"In {(int)_offset.TotalMinutes} minutes"
            : _absolute.ToString("o");
    }

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

    /// <summary>
    /// Fluent YouTube video uploader. Entry point: GPAL.YouTube.
    /// Upload phase: WithCredentials -> WithTitle -> ... -> Upload(file).
    /// Schedule phase: WithVideos(results) -> WaitForProcessing(seconds) -> ScheduleAt(PremiereTime).
    /// ScheduleAt runs in the background; the calling thread is not blocked.
    /// </summary>
    public class GPALYouTube : IGPALYouTube
    {
        private ICredentials             _credentials;
        private string                   _title             = string.Empty;
        private string                   _description       = string.Empty;
        private string[]                 _tags              = new string[0];
        private YouTubePrivacy           _privacy           = YouTubePrivacy.Private;
        private YouTubeCategory          _category          = YouTubeCategory.ScienceTechnology;
        private List<string>             _videoIds          = new List<string>();
        private int                      _pollIntervalSecs  = 60;
        private TextBoxBase              _outputControl;
        private ManualResetEventSlim     _completionEvent   = new ManualResetEventSlim(false);
        private string                   _playlistId        = null;
        private List<string>             _lookupVideoIds    = new List<string>();
        private List<string>             _searchTerms       = new List<string>();
        private int                      _position          = -1;

        // which of the metadata settings the workflow gave, so UpdateVideo changes only those and keeps the rest
        private bool                     _titleGiven;
        private bool                     _descriptionGiven;
        private bool                     _tagsGiven;
        private bool                     _privacyGiven;
        private bool                     _categoryGiven;

        private const string InitiationUrl    = "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status";
        private const string VideoStatusUrl   = "https://www.googleapis.com/youtube/v3/videos?part=status&id=";
        private const string VideoUpdateUrl   = "https://www.googleapis.com/youtube/v3/videos?part=status";
        private const string PlaylistCreateUrl = "https://www.googleapis.com/youtube/v3/playlists?part=snippet,status";
        private const string PlaylistItemUrl   = "https://www.googleapis.com/youtube/v3/playlistItems?part=snippet";
        private const string VideoInfoBaseUrl  = "https://www.googleapis.com/youtube/v3/videos?part=snippet,status&id=";
        private const string SearchBaseUrl     = "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&forMine=true&maxResults=50&q=";
        private const string MyChannelUrl      = "https://www.googleapis.com/youtube/v3/channels?part=contentDetails&mine=true";
        private const string PlaylistItemsListUrl = "https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&maxResults=50&playlistId=";
        private const string PlaylistItemDeleteUrl = "https://www.googleapis.com/youtube/v3/playlistItems?id=";
        private const string MyPlaylistsUrl    = "https://www.googleapis.com/youtube/v3/playlists?part=snippet,status,contentDetails&mine=true&maxResults=50";
        private const string VideoDeleteUrl    = "https://www.googleapis.com/youtube/v3/videos?id=";
        private const string ThumbnailSetUrl   = "https://www.googleapis.com/upload/youtube/v3/thumbnails/set?uploadType=media&videoId=";

        // how many status checks in a row may fail to answer before ReplaceVideo stops waiting for processing
        private const int MaxUnansweredStatusChecks = 5;

        internal GPALYouTube() { }

        // ── Upload chain ──────────────────────────────────────────────────────

        public IGPALYouTube WithCredentials(ICredentials credentials)
        {
            if (credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Credentials is null.", this, GPALObjectType.YouTube);
                return this;
            }
            _credentials = credentials;
            GPAL.PublishSimpleEvent(GPALEventType.INFO, "Credentials configured.", this, GPALObjectType.YouTube);
            return this;
        }

        public IGPALYouTube WithTitle(string title)
        {
            _title = title ?? string.Empty;
            _titleGiven = true;
            return this;
        }

        public IGPALYouTube WithDescription(string description)
        {
            _description = description ?? string.Empty;
            _descriptionGiven = true;
            return this;
        }

        public IGPALYouTube WithTags(params string[] tags)
        {
            _tags = tags ?? new string[0];
            _tagsGiven = true;
            return this;
        }

        public IGPALYouTube WithPrivacy(YouTubePrivacy privacy)
        {
            _privacy = privacy;
            _privacyGiven = true;
            return this;
        }

        public IGPALYouTube WithCategory(YouTubeCategory category)
        {
            _category = category;
            _categoryGiven = true;
            return this;
        }

        public YouTubeUploadResult Upload(GPALFile file)
        {
            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Credentials must be set before Upload.", this, GPALObjectType.YouTube);
                return null;
            }

            string filePath = file?.Filename;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File not found: [{filePath}]", this, GPALObjectType.YouTube);
                return null;
            }

            try
            {
                _credentials.FetchAccessToken(out string token);
                if (string.IsNullOrEmpty(token))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Failed to fetch access token.", this, GPALObjectType.YouTube);
                    return null;
                }

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Uploading: [{Path.GetFileName(filePath)}]", this, GPALObjectType.YouTube);

                string uploadUri = InitiateResumableUpload(token, filePath);
                if (string.IsNullOrEmpty(uploadUri)) return null;

                string videoId = UploadVideoBytes(uploadUri, filePath);
                if (string.IsNullOrEmpty(videoId)) return null;

                string url = $"https://youtu.be/{videoId}";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Upload complete: [{url}]", this, GPALObjectType.YouTube);
                if (!string.IsNullOrEmpty(_playlistId))
                    AddVideoToPlaylist(token, _playlistId, videoId, -1, GPALEventType.ERROR);
                return new YouTubeUploadResult(videoId, url, "uploaded");
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "YouTube upload failed.", this, GPALObjectType.YouTube, ex);
                return null;
            }
        }

        // ── Schedule chain ────────────────────────────────────────────────────

        public IGPALYouTube WithVideo(YouTubeUploadResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.VideoId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "WithVideo: null or missing VideoId.", this, GPALObjectType.YouTube);
                return this;
            }
            _videoIds.Add(result.VideoId);
            return this;
        }

        public IGPALYouTube WithVideos(IEnumerable<YouTubeUploadResult> results)
        {
            if (results == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "WithVideos: null results list.", this, GPALObjectType.YouTube);
                return this;
            }
            foreach (var r in results)
                if (r != null && !string.IsNullOrEmpty(r.VideoId))
                    _videoIds.Add(r.VideoId);
            return this;
        }

        public IGPALYouTube WaitForProcessing(int pollIntervalSeconds = 60)
        {
            _pollIntervalSecs = Math.Max(10, pollIntervalSeconds);
            return this;
        }

        /// <summary>
        /// Starts a background thread that polls each video's processing status and schedules a
        /// premiere as each one completes. premiereTime presets (e.g. In30Minutes) resolve at the
        /// moment each video is marked processed, so the offset is relative to readiness, not call time.
        /// Returns immediately; publishes INFO events as videos complete and on each poll round.
        /// </summary>
        public IGPALYouTube ScheduleAt(PremiereTime premiereTime)
        {
            // both error paths set the completion event; no thread is going to start, so a console
            // workflow sitting in WaitForCompletion would otherwise never be released
            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "ScheduleAt: credentials not set.", this, GPALObjectType.YouTube);
                _completionEvent.Set();
            }
            else if (_videoIds.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "ScheduleAt: no videos targeted. Call WithVideo or WithVideos first.", this, GPALObjectType.YouTube);
                _completionEvent.Set();
            }
            else
            {
                // Capture everything the background thread needs; don't close over mutable fields
                var videoIds   = new List<string>(_videoIds);
                var creds      = _credentials;
                var intervalMs = _pollIntervalSecs * 1000;

                // the thread body is guarded so a throw mid-poll releases WaitForCompletion and is published,
                // instead of taking the process down with an unhandled background exception
                var thread = new Thread(() =>
                {
                    try
                    {
                        PollAndSchedule(creds, videoIds, premiereTime, intervalMs);
                    }
                    catch (Exception ex)
                    {
                        string errMsg = "ScheduleAt: polling stopped before all videos were scheduled.";
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errMsg, this, GPALObjectType.YouTube, ex);
                        AppendToOutput(errMsg);
                    }
                    finally
                    {
                        _completionEvent.Set();
                    }
                });
                thread.IsBackground = true;
                thread.Start();

                GPAL.PublishSimpleEvent(GPALEventType.INFO,
                    $"ScheduleAt started in background for [{videoIds.Count}] video(s). Poll interval: [{_pollIntervalSecs}]s.",
                    this, GPALObjectType.YouTube);
            }

            return this;
        }

        // ── Output and completion ─────────────────────────────────────────────

        /// <summary>
        /// Pipes scheduling status to a TextBox or RichTextBox on a WinForms form.
        /// The form's message loop keeps the application alive while the background
        /// thread is polling, so no additional blocking is required in the form case.
        /// </summary>
        public IGPALYouTube WithOutputTo(TextBoxBase control)
        {
            _outputControl = control;
            return this;
        }

        /// <summary>
        /// Blocks the calling thread until ScheduleAt finishes scheduling all videos.
        /// Use this in console apps or any context without a WinForms message loop to
        /// prevent the process from exiting before the background thread completes.
        /// </summary>
        public void WaitForCompletion()
        {
            _completionEvent.Wait();
        }

        private void AppendToOutput(string message)
        {
            if (_outputControl == null) return;
            if (_outputControl.InvokeRequired)
                _outputControl.BeginInvoke(new Action(() => AppendToOutput(message)));
            else
                _outputControl.AppendText(message + Environment.NewLine);
        }

        // ── Background polling ────────────────────────────────────────────────

        private void PollAndSchedule(ICredentials creds, List<string> videoIds, PremiereTime premiereTime, int intervalMs)
        {
            var pending    = new HashSet<string>(videoIds);
            var lastStatus = new Dictionary<string, string>();
            int scheduled  = 0;

            string openingMsg = $"Suppressing repeat status messages for: {string.Join(", ", videoIds)}. Will report when processing completes for each.";
            GPAL.PublishSimpleEvent(GPALEventType.INFO, openingMsg, this, GPALObjectType.YouTube);
            AppendToOutput(openingMsg);

            while (pending.Count > 0)
            {
                creds.FetchAccessToken(out string token);

                var justProcessed = new List<string>();

                foreach (string id in new List<string>(pending))
                {
                    string status = GetUploadStatus(token, id);

                    string prev;
                    if (!lastStatus.TryGetValue(id, out prev) || prev != status)
                    {
                        string msg = $"Video {id}: {status}";
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, msg, this, GPALObjectType.YouTube);
                        AppendToOutput(msg);
                        lastStatus[id] = status;
                    }

                    if (status == "processed")
                    {
                        justProcessed.Add(id);
                    }
                    else if (status == "failed" || status == "rejected" || status == "deleted")
                    {
                        string errMsg = $"Video {id} cannot be scheduled: {status}.";
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, errMsg, this, GPALObjectType.YouTube);
                        AppendToOutput(errMsg);
                        justProcessed.Add(id);
                    }
                }

                foreach (string id in justProcessed)
                {
                    pending.Remove(id);
                    string finalStatus;
                    if (lastStatus.TryGetValue(id, out finalStatus) && finalStatus == "processed")
                    {
                        DateTime pt = premiereTime.Resolve();
                        SetPremiere(token, id, pt);
                        scheduled++;
                    }
                }

                if (pending.Count > 0)
                    Thread.Sleep(intervalMs);
            }

            // says how many were scheduled, and warns when none were
            string doneMsg = 0 == scheduled
                ? $"No videos were scheduled. None of the [{videoIds.Count}] finished processing."
                : $"Scheduled [{scheduled}] of [{videoIds.Count}] video(s).";

            GPAL.PublishSimpleEvent(0 == scheduled ? GPALEventType.WARNING : GPALEventType.INFO, doneMsg, this, GPALObjectType.YouTube);
            AppendToOutput(doneMsg);
        }

        private void SetPremiere(string token, string videoId, DateTime premiereUtc)
        {
            try
            {
                string body = "{\"id\":" + JsonString(videoId)
                    + ",\"status\":{\"privacyStatus\":\"private\",\"publishAt\":" + JsonString(premiereUtc.ToString("o")) + "}}";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

                var req = (HttpWebRequest)WebRequest.Create(VideoUpdateUrl);
                req.Method        = "PUT";
                req.Headers["Authorization"] = "Bearer " + token;
                req.ContentType   = "application/json";
                req.ContentLength = bodyBytes.Length;

                using (var s = req.GetRequestStream())
                    s.Write(bodyBytes, 0, bodyBytes.Length);

                using ((HttpWebResponse)req.GetResponse()) { }

                string schedMsg = $"Premiere scheduled: https://youtu.be/{videoId} at {premiereUtc:o}";
                GPAL.PublishSimpleEvent(GPALEventType.INFO, schedMsg, this, GPALObjectType.YouTube);
                AppendToOutput(schedMsg);
            }
            catch (Exception ex)
            {
                string errMsg = $"SetPremiere failed for {videoId}.";
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, errMsg, this, GPALObjectType.YouTube, ex);
                AppendToOutput(errMsg);
            }
        }

        private string GetUploadStatus(string token, string videoId)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(VideoStatusUrl + videoId);
                req.Method = "GET";
                req.Headers["Authorization"] = "Bearer " + token;

                using (var resp   = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string responseBody = reader.ReadToEnd();
                    using (var doc = JsonDocument.Parse(responseBody))
                    {
                        if (doc.RootElement.TryGetProperty("items", out var items)
                            && items.GetArrayLength() > 0
                            && items[0].TryGetProperty("status", out var status)
                            && status.TryGetProperty("uploadStatus", out var uploadStatus))
                        {
                            return uploadStatus.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"GetUploadStatus failed for [{videoId}].", this, GPALObjectType.YouTube, ex);
            }
            return "unknown";
        }

        // ── Upload internals ──────────────────────────────────────────────────

        private string InitiateResumableUpload(string token, string filePath)
        {
            long   fileSize = new FileInfo(filePath).Length;
            byte[] body     = Encoding.UTF8.GetBytes(BuildMetadataJson());

            var req = (HttpWebRequest)WebRequest.Create(InitiationUrl);
            req.Method = "POST";
            req.Headers["Authorization"]           = "Bearer " + token;
            req.ContentType                        = "application/json; charset=UTF-8";
            req.Headers["X-Upload-Content-Type"]   = "video/mp4";
            req.Headers["X-Upload-Content-Length"] = fileSize.ToString();
            req.ContentLength = body.Length;

            using (var s = req.GetRequestStream())
                s.Write(body, 0, body.Length);

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string uri = resp.Headers["Location"];
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, "Resumable upload session started.", this, GPALObjectType.YouTube);
                    return uri;
                }
            }
            catch (WebException wex) when (wex.Response is HttpWebResponse errResp)
            {
                using (var reader = new System.IO.StreamReader(errResp.GetResponseStream()))
                {
                    string detail = reader.ReadToEnd();
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Upload initiation failed ([{(int)errResp.StatusCode}]): [{detail}]", this, GPALObjectType.YouTube);
                }
                throw;
            }
        }

        private string UploadVideoBytes(string uploadUri, string filePath)
        {
            long fileSize = new FileInfo(filePath).Length;

            var req = (HttpWebRequest)WebRequest.Create(uploadUri);
            req.Method                    = "PUT";
            req.ContentType               = "video/mp4";
            req.ContentLength             = fileSize;
            req.Timeout                   = 3_600_000;
            req.AllowWriteStreamBuffering = false;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Sending [{fileSize:N0}] bytes...", this, GPALObjectType.YouTube);

            using (var reqStream = req.GetRequestStream())
            using (var fs        = File.OpenRead(filePath))
            {
                byte[] buf = new byte[256 * 1024];
                int read;
                while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                    reqStream.Write(buf, 0, read);
            }

            using (var resp   = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream()))
            {
                string responseBody = reader.ReadToEnd();
                using (var doc = JsonDocument.Parse(responseBody))
                {
                    if (doc.RootElement.TryGetProperty("id", out var idEl))
                        return idEl.GetString();
                }
            }

            GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Upload response missing video ID.", this, GPALObjectType.YouTube);
            return null;
        }

        private string PrivacyString()
        {
            switch (_privacy)
            {
                case YouTubePrivacy.Public:   return "public";
                case YouTubePrivacy.Unlisted: return "unlisted";
                default:                      return "private";
            }
        }

        private string BuildMetadataJson()
        {
            string privacyStr = PrivacyString();

            var tags = new StringBuilder("[");
            for (int i = 0; i < _tags.Length; i++)
            {
                if (i > 0) tags.Append(",");
                tags.Append(JsonString(_tags[i]));
            }
            tags.Append("]");

            return "{\"snippet\":{\"title\":"       + JsonString(_title)
                + ",\"description\":"               + JsonString(_description)
                + ",\"tags\":"                      + tags
                + ",\"categoryId\":"                + JsonString(((int)_category).ToString())
                + "},\"status\":{\"privacyStatus\":" + JsonString(privacyStr) + "}}";
        }

        private static string JsonString(string s)
        {
            if (s == null) return "\"\"";
            return "\"" + s.Replace("\\", "\\\\")
                           .Replace("\"", "\\\"")
                           .Replace("\n", "\\n")
                           .Replace("\r", "\\r")
                           .Replace("\t", "\\t")
                 + "\"";
        }

        // ── Playlist ──────────────────────────────────────────────────────────

        public IGPALYouTube WithPlaylist(string playlistId)
        {
            _playlistId = playlistId;
            return this;
        }

        public YouTubePlaylistResult CreatePlaylist()
        {
            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "CreatePlaylist: credentials not set.", this, GPALObjectType.YouTube);
                return null;
            }
            try
            {
                _credentials.FetchAccessToken(out string token);
                if (string.IsNullOrEmpty(token))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "CreatePlaylist: failed to fetch access token.", this, GPALObjectType.YouTube);
                    return null;
                }

                string body = "{\"snippet\":{\"title\":" + JsonString(_title)
                    + ",\"description\":" + JsonString(_description)
                    + "},\"status\":{\"privacyStatus\":" + JsonString(PrivacyString()) + "}}";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

                var req = (HttpWebRequest)WebRequest.Create(PlaylistCreateUrl);
                req.Method        = "POST";
                req.Headers["Authorization"] = "Bearer " + token;
                req.ContentType   = "application/json";
                req.ContentLength = bodyBytes.Length;

                using (var s = req.GetRequestStream())
                    s.Write(bodyBytes, 0, bodyBytes.Length);

                using (var resp   = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string responseBody = reader.ReadToEnd();
                    using (var doc = JsonDocument.Parse(responseBody))
                    {
                        if (doc.RootElement.TryGetProperty("id", out var idEl))
                        {
                            string playlistId = idEl.GetString();
                            GPAL.PublishSimpleEvent(GPALEventType.INFO,
                                $"Playlist created: https://www.youtube.com/playlist?list=[{playlistId}]", this, GPALObjectType.YouTube);
                            return new YouTubePlaylistResult(playlistId);
                        }
                    }
                }
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "CreatePlaylist: response missing playlist ID.", this, GPALObjectType.YouTube);
                return null;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "CreatePlaylist failed.", this, GPALObjectType.YouTube, ex);
                return null;
            }
        }

        public IGPALYouTube AddToPlaylist(string playlistId)
        {
            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "AddToPlaylist: credentials not set.", this, GPALObjectType.YouTube);
                return this;
            }
            // the uploaded videos from WithVideo and WithVideos, then the existing ones named with WithVideoId
            var videoIds = new List<string>(_videoIds);
            foreach (string id in _lookupVideoIds)
                if (!videoIds.Contains(id))
                    videoIds.Add(id);

            if (videoIds.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "AddToPlaylist: no videos. Call WithVideo, WithVideos or WithVideoId first.", this, GPALObjectType.YouTube);
                return this;
            }
            _credentials.FetchAccessToken(out string token);

            // with WithPosition each video goes in at that place, the next one after it, so they keep their order
            int position = _position;
            foreach (string videoId in videoIds)
            {
                AddVideoToPlaylist(token, playlistId, videoId, position, GPALEventType.ERROR);
                if (position >= 0) position++;
            }
            return this;
        }

        /// <summary>
        /// Sets the place, counted from 0, that AddToPlaylist puts the videos in. Without it they go on the end.
        /// The playlist must be sorted by hand (Manual) on YouTube for a position to be accepted.
        /// </summary>
        public IGPALYouTube WithPosition(int position)
        {
            _position = position;
            return this;
        }

        // adds one video to a playlist, at the position given or on the end when it is negative. true when youtube took it.
        // a refusal is published at the level given, since ReplaceVideo tries again without the position
        private bool AddVideoToPlaylist(string token, string playlistId, string videoId, int position, GPALEventType refusedAs)
        {
            bool added = false;

            try
            {
                string positionJson = position >= 0 ? ",\"position\":" + position : string.Empty;
                string body = "{\"snippet\":{\"playlistId\":" + JsonString(playlistId)
                    + positionJson
                    + ",\"resourceId\":{\"kind\":\"youtube#video\",\"videoId\":" + JsonString(videoId) + "}}}";

                added = CallApi("POST", PlaylistItemUrl, token, "application/json", Encoding.UTF8.GetBytes(body), out string answer);

                if (added)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO,
                        $"Added [{videoId}] to playlist [{playlistId}].", this, GPALObjectType.YouTube);
                else
                    GPAL.PublishSimpleEvent(refusedAs,
                        $"Could not add [{videoId}] to playlist [{playlistId}]: [{answer}]", this, GPALObjectType.YouTube);
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                    $"AddVideoToPlaylist failed for [{videoId}].", this, GPALObjectType.YouTube, ex);
            }

            return added;
        }

        // ── Lookup / Search ───────────────────────────────────────────────────

        public IGPALYouTube WithVideoId(string videoId)
        {
            if (!string.IsNullOrEmpty(videoId))
                _lookupVideoIds.Add(videoId);
            return this;
        }

        public IGPALYouTube WithSearchTerms(string query)
        {
            if (!string.IsNullOrEmpty(query))
                _searchTerms.Add(query);
            return this;
        }

        public IGPALYouTube WithSearchTerms(IGPALGrid<string> grid)
        {
            if (grid == null) return this;
            foreach (var row in grid)
                if (row != null && row.Count > 0 && !string.IsNullOrEmpty(row[0]))
                    _searchTerms.Add(row[0]);
            return this;
        }

        public IGPALYouTube GetResults(out YouTubeVideoInfo info)
        {
            info = null;
            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "GetResults: credentials not set.", this, GPALObjectType.YouTube);
                return this;
            }
            if (_lookupVideoIds.Count == 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "GetResults: no video ID. Call WithVideoId first.", this, GPALObjectType.YouTube);
                return this;
            }
            _credentials.FetchAccessToken(out string token);
            var list = FetchVideoInfos(token, _lookupVideoIds);
            info = list.Count > 0 ? list[0] : null;
            return this;
        }

        public IGPALYouTube GetResults(out List<YouTubeVideoInfo> results)
        {
            results = new List<YouTubeVideoInfo>();
            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "GetResults: credentials not set.", this, GPALObjectType.YouTube);
                return this;
            }
            _credentials.FetchAccessToken(out string token);

            if (_lookupVideoIds.Count > 0)
                results.AddRange(FetchVideoInfos(token, _lookupVideoIds));

            if (_searchTerms.Count > 0)
            {
                var seen = new HashSet<string>();
                foreach (var v in results) seen.Add(v.VideoId);
                foreach (string term in _searchTerms)
                {
                    foreach (var v in SearchVideos(token, term))
                        if (seen.Add(v.VideoId))
                            results.Add(v);
                }
            }
            return this;
        }

        private List<YouTubeVideoInfo> FetchVideoInfos(string token, List<string> videoIds)
        {
            var result = new List<YouTubeVideoInfo>();
            int i = 0;
            while (i < videoIds.Count)
            {
                var batch = new StringBuilder();
                int end = Math.Min(i + 50, videoIds.Count);
                for (int j = i; j < end; j++)
                {
                    if (j > i) batch.Append(",");
                    batch.Append(Uri.EscapeDataString(videoIds[j]));
                }
                i = end;

                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(VideoInfoBaseUrl + batch);
                    req.Method = "GET";
                    req.Headers["Authorization"] = "Bearer " + token;

                    using (var resp   = (HttpWebResponse)req.GetResponse())
                    using (var reader = new StreamReader(resp.GetResponseStream()))
                    {
                        string responseBody = reader.ReadToEnd();
                        using (var doc = JsonDocument.Parse(responseBody))
                        {
                            if (!doc.RootElement.TryGetProperty("items", out var items)) continue;
                            foreach (var item in items.EnumerateArray())
                            {
                                string videoId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                                if (string.IsNullOrEmpty(videoId)) continue;

                                string title = string.Empty, description = string.Empty;
                                string[] tags = new string[0];
                                DateTime? publishedAt = null;
                                string categoryId = null, thumbnailUrl = null;
                                YouTubePrivacy? privacy = null;

                                if (item.TryGetProperty("status", out var statusEl)
                                    && statusEl.TryGetProperty("privacyStatus", out var privacyEl))
                                    privacy = PrivacyFrom(privacyEl.GetString());

                                if (item.TryGetProperty("snippet", out var snippet))
                                {
                                    if (snippet.TryGetProperty("categoryId", out var c)) categoryId = c.GetString();
                                    thumbnailUrl = BestThumbnail(snippet);
                                    if (snippet.TryGetProperty("title",       out var t)) title       = t.GetString();
                                    if (snippet.TryGetProperty("description", out var d)) description = d.GetString();
                                    if (snippet.TryGetProperty("publishedAt", out var p)
                                        && DateTime.TryParse(p.GetString(), out DateTime dt))
                                        publishedAt = dt;
                                    if (snippet.TryGetProperty("tags", out var tagsEl))
                                    {
                                        var tagList = new List<string>();
                                        foreach (var tag in tagsEl.EnumerateArray())
                                            tagList.Add(tag.GetString());
                                        tags = tagList.ToArray();
                                    }
                                }
                                result.Add(new YouTubeVideoInfo(videoId, title, description, tags, publishedAt)
                                {
                                    Privacy      = privacy,
                                    CategoryId   = categoryId,
                                    ThumbnailUrl = thumbnailUrl,
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "FetchVideoInfos failed.", this, GPALObjectType.YouTube, ex);
                }
            }
            return result;
        }

        private List<YouTubeVideoInfo> SearchVideos(string token, string query)
        {
            var result = new List<YouTubeVideoInfo>();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(SearchBaseUrl + Uri.EscapeDataString(query));
                req.Method = "GET";
                req.Headers["Authorization"] = "Bearer " + token;

                using (var resp   = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string responseBody = reader.ReadToEnd();
                    using (var doc = JsonDocument.Parse(responseBody))
                    {
                        if (!doc.RootElement.TryGetProperty("items", out var items)) return result;
                        foreach (var item in items.EnumerateArray())
                        {
                            string videoId = null;
                            if (item.TryGetProperty("id", out var idObj)
                                && idObj.TryGetProperty("videoId", out var vidEl))
                                videoId = vidEl.GetString();
                            if (string.IsNullOrEmpty(videoId)) continue;

                            string title = string.Empty, description = string.Empty;
                            DateTime? publishedAt = null;

                            if (item.TryGetProperty("snippet", out var snippet))
                            {
                                if (snippet.TryGetProperty("title",       out var t)) title       = t.GetString();
                                if (snippet.TryGetProperty("description", out var d)) description = d.GetString();
                                if (snippet.TryGetProperty("publishedAt", out var p)
                                    && DateTime.TryParse(p.GetString(), out DateTime dt))
                                    publishedAt = dt;
                            }
                            result.Add(new YouTubeVideoInfo(videoId, title, description, null, publishedAt));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"SearchVideos failed for [{query}].", this, GPALObjectType.YouTube, ex);
            }
            return result;
        }

        // ── Channel management ────────────────────────────────────────────────

        /// <summary>
        /// Every video on the authenticated channel, newest first, public, unlisted and private, with its
        /// privacy, category and thumbnail. Reads the channel's uploads playlist, then the videos 50 at a time.
        /// </summary>
        public IGPALYouTube GetMyVideos(out List<YouTubeVideoInfo> videos)
        {
            var found = new List<YouTubeVideoInfo>();
            string token = TokenFor("GetMyVideos");

            if (null != token)
            {
                try
                {
                    string uploadsId = UploadsPlaylistId(token);

                    if (null != uploadsId)
                    {
                        var items = new List<YouTubePlaylistItem>();

                        if (ReadPlaylistItems(token, uploadsId, items, out string refusal))
                        {
                            var ids = new List<string>();
                            foreach (var item in items)
                                if (!string.IsNullOrEmpty(item.VideoId))
                                    ids.Add(item.VideoId);

                            found = FetchVideoInfos(token, ids);
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Found [{found.Count}] video(s) on the channel.", this, GPALObjectType.YouTube);
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"GetMyVideos: could not read the uploads playlist: [{refusal}]", this, GPALObjectType.YouTube);
                        }
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "GetMyVideos failed.", this, GPALObjectType.YouTube, ex);
                }
            }

            videos = found;
            return this;
        }

        /// <summary>
        /// Every playlist the authenticated channel has made, with its privacy and how many videos are in it.
        /// </summary>
        public IGPALYouTube GetMyPlaylists(out List<YouTubePlaylistInfo> playlists)
        {
            var found = new List<YouTubePlaylistInfo>();
            string token = TokenFor("GetMyPlaylists");

            if (null != token)
            {
                try
                {
                    if (ReadMyPlaylists(token, found, out string refusal))
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Found [{found.Count}] playlist(s) on the channel.", this, GPALObjectType.YouTube);
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"GetMyPlaylists: could not read the playlists: [{refusal}]", this, GPALObjectType.YouTube);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "GetMyPlaylists failed.", this, GPALObjectType.YouTube, ex);
                }
            }

            playlists = found;
            return this;
        }

        /// <summary>
        /// Every entry in the playlist set with WithPlaylist, in playlist order.
        /// </summary>
        public IGPALYouTube GetPlaylistItems(out List<YouTubePlaylistItem> items)
        {
            var found = new List<YouTubePlaylistItem>();

            if (string.IsNullOrEmpty(_playlistId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "GetPlaylistItems: no playlist. Call WithPlaylist first.", this, GPALObjectType.YouTube);
            }
            else
            {
                string token = TokenFor("GetPlaylistItems");

                if (null != token)
                {
                    try
                    {
                        if (ReadPlaylistItems(token, _playlistId, found, out string refusal))
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Found [{found.Count}] item(s) in playlist [{_playlistId}].", this, GPALObjectType.YouTube);
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"GetPlaylistItems: could not read playlist [{_playlistId}]: [{refusal}]", this, GPALObjectType.YouTube);
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"GetPlaylistItems failed for [{_playlistId}].", this, GPALObjectType.YouTube, ex);
                    }
                }
            }

            items = found;
            return this;
        }

        /// <summary>
        /// Takes the videos named with WithVideoId out of the playlist set with WithPlaylist, every time each
        /// one appears in it. The videos themselves are not touched.
        /// </summary>
        public IGPALYouTube RemoveFromPlaylist()
        {
            if (string.IsNullOrEmpty(_playlistId))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "RemoveFromPlaylist: no playlist. Call WithPlaylist first.", this, GPALObjectType.YouTube);
            }
            else if (0 == _lookupVideoIds.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "RemoveFromPlaylist: no video ID. Call WithVideoId first.", this, GPALObjectType.YouTube);
            }
            else
            {
                string token = TokenFor("RemoveFromPlaylist");

                if (null != token)
                {
                    try
                    {
                        var items = new List<YouTubePlaylistItem>();

                        if (!ReadPlaylistItems(token, _playlistId, items, out string refusal))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"RemoveFromPlaylist: could not read playlist [{_playlistId}]: [{refusal}]", this, GPALObjectType.YouTube);
                        }
                        else
                        {
                            foreach (string videoId in _lookupVideoIds)
                            {
                                int removed = 0;

                                foreach (var item in items)
                                    if (videoId == item.VideoId && DeletePlaylistItem(token, item))
                                        removed++;

                                if (0 < removed)
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Removed [{videoId}] from playlist [{_playlistId}].", this, GPALObjectType.YouTube);
                                else if (!items.Exists(i => videoId == i.VideoId))
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"RemoveFromPlaylist: [{videoId}] is not in playlist [{_playlistId}].", this, GPALObjectType.YouTube);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"RemoveFromPlaylist failed for [{_playlistId}].", this, GPALObjectType.YouTube, ex);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Changes the videos named with WithVideoId to the title, description, tags, category and privacy given
        /// with WithTitle, WithDescription, WithTags, WithCategory and WithPrivacy. Anything not given keeps the
        /// value the video has now.
        /// </summary>
        public IGPALYouTube UpdateVideo()
        {
            if (0 == _lookupVideoIds.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "UpdateVideo: no video ID. Call WithVideoId first.", this, GPALObjectType.YouTube);
            }
            else if (!(_titleGiven || _descriptionGiven || _tagsGiven || _categoryGiven || _privacyGiven))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "UpdateVideo: nothing to change. Call WithTitle, WithDescription, WithTags, WithCategory or WithPrivacy first.", this, GPALObjectType.YouTube);
            }
            else
            {
                string token = TokenFor("UpdateVideo");

                if (null != token)
                {
                    foreach (string videoId in _lookupVideoIds)
                    {
                        try
                        {
                            UpdateOneVideo(token, videoId);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"UpdateVideo failed for [{videoId}].", this, GPALObjectType.YouTube, ex);
                        }
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Sets the image as the thumbnail of the videos named with WithVideoId. YouTube takes a JPEG or PNG of
        /// up to 2 MB, and only on a channel that has been verified.
        /// </summary>
        public IGPALYouTube SetThumbnail(GPALFile image)
        {
            string imagePath = image?.Filename;

            if (0 == _lookupVideoIds.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "SetThumbnail: no video ID. Call WithVideoId first.", this, GPALObjectType.YouTube);
            }
            else if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"SetThumbnail: file not found [{imagePath}]", this, GPALObjectType.YouTube);
            }
            else
            {
                string token = TokenFor("SetThumbnail");

                if (null != token)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(imagePath);
                        string contentType = ".png" == Path.GetExtension(imagePath).ToLowerInvariant() ? "image/png" : "image/jpeg";

                        foreach (string videoId in _lookupVideoIds)
                            SetThumbnailBytes(token, videoId, bytes, contentType, GPALEventType.ERROR);
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"SetThumbnail failed for [{imagePath}].", this, GPALObjectType.YouTube, ex);
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Deletes the videos named with WithVideoId from YouTube. This cannot be undone.
        /// </summary>
        public IGPALYouTube DeleteVideo()
        {
            if (0 == _lookupVideoIds.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "DeleteVideo: no video ID. Call WithVideoId first.", this, GPALObjectType.YouTube);
            }
            else
            {
                string token = TokenFor("DeleteVideo");

                if (null != token)
                {
                    foreach (string videoId in _lookupVideoIds)
                    {
                        try
                        {
                            if (CallApi("DELETE", VideoDeleteUrl + Uri.EscapeDataString(videoId), token, null, null, out string answer))
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Deleted video [{videoId}].", this, GPALObjectType.YouTube);
                            else
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to delete video [{videoId}]: [{answer}]", this, GPALObjectType.YouTube);
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to delete video [{videoId}].", this, GPALObjectType.YouTube, ex);
                        }
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Uploads newVideo in place of the video named with WithVideoId. The new video gets the old one's title,
        /// description, tags, category, privacy and thumbnail, and takes its place in every one of the channel's
        /// playlists that held it. Waits for YouTube to process the new video first, checking at the interval set
        /// with WaitForProcessing. The old video is left on YouTube; deleting it is the workflow's decision.
        /// </summary>
        /// <returns>The new video, with Status "processed", or null when nothing was uploaded.</returns>
        public YouTubeUploadResult ReplaceVideo(GPALFile newVideo)
        {
            YouTubeUploadResult retVal = null;
            string filePath = newVideo?.Filename;

            if (0 == _lookupVideoIds.Count)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, "ReplaceVideo: no video ID. Call WithVideoId first.", this, GPALObjectType.YouTube);
            }
            else if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"ReplaceVideo: file not found [{filePath}]", this, GPALObjectType.YouTube);
            }
            else
            {
                string token = TokenFor("ReplaceVideo");
                string oldId = _lookupVideoIds[0];

                if (null != token)
                {
                    try
                    {
                        List<YouTubeVideoInfo> found = FetchVideoInfos(token, new List<string> { oldId });

                        if (0 == found.Count)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"ReplaceVideo: video [{oldId}] was not found.", this, GPALObjectType.YouTube);
                        }
                        else if (FindPlaylistPlaces(token, oldId, out List<YouTubePlaylistItem> places))
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Replacing [{oldId}], which is in [{places.Count}] playlist place(s).", this, GPALObjectType.YouTube);

                            retVal = UploadAsReplacement(newVideo, found[0]);

                            if (null != retVal)
                                retVal = FinishReplacement(found[0], retVal, places);
                        }
                    }
                    catch (Exception ex)
                    {
                        string stopped = null == retVal
                            ? $"ReplaceVideo failed for [{oldId}]."
                            : $"ReplaceVideo stopped before it finished. The new video is [{retVal.VideoId}].";
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, stopped, this, GPALObjectType.YouTube, ex);
                    }
                }
            }

            return retVal;
        }

        // ── Channel management internals ──────────────────────────────────────

        // the access token for an operation, or null when there is none, with the reason published
        private string TokenFor(string operation)
        {
            string token = null;

            if (_credentials == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"{operation}: credentials not set.", this, GPALObjectType.YouTube);
            }
            else
            {
                _credentials.FetchAccessToken(out token);

                if (string.IsNullOrEmpty(token))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"{operation}: failed to fetch access token.", this, GPALObjectType.YouTube);
                    token = null;
                }
            }

            return token;
        }

        // sends one request to the api. true on a 2xx answer, with the answer's body. when youtube refuses, false,
        // and answer holds the status code and what youtube said. a request that gets no answer at all throws
        private bool CallApi(string method, string url, string token, string contentType, byte[] body, out string answer)
        {
            bool ok = false;

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Headers["Authorization"] = "Bearer " + token;

            if (null != body)
            {
                req.ContentType   = contentType;
                req.ContentLength = body.Length;

                using (var s = req.GetRequestStream())
                    s.Write(body, 0, body.Length);
            }

            try
            {
                using (var resp   = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                    answer = reader.ReadToEnd();

                ok = true;
            }
            catch (WebException wex) when (wex.Response is HttpWebResponse errResp)
            {
                using (errResp)
                using (var reader = new StreamReader(errResp.GetResponseStream()))
                    answer = $"{(int)errResp.StatusCode} {Regex.Replace(reader.ReadToEnd(), @"\s+", " ").Trim()}";
            }

            return ok;
        }

        // reads every page of a list call, handing each item to onItem. false when youtube refuses a page, with
        // what it said in refusal
        private bool ReadAllPages(string token, string firstPageUrl, Action<JsonElement> onItem, out string refusal)
        {
            bool ok = true;
            string pageToken = null;
            refusal = null;

            do
            {
                string url = null == pageToken ? firstPageUrl : firstPageUrl + "&pageToken=" + Uri.EscapeDataString(pageToken);
                pageToken = null;

                ok = CallApi("GET", url, token, null, null, out string answer);

                if (!ok)
                {
                    refusal = answer;
                }
                else
                {
                    using (var doc = JsonDocument.Parse(answer))
                    {
                        if (doc.RootElement.TryGetProperty("items", out var items))
                            foreach (var item in items.EnumerateArray())
                                onItem(item);

                        if (doc.RootElement.TryGetProperty("nextPageToken", out var next))
                            pageToken = next.GetString();
                    }
                }
            }
            while (ok && !string.IsNullOrEmpty(pageToken));

            return ok;
        }

        // the id of the playlist youtube keeps of every video the channel uploaded, or null with the reason published
        private string UploadsPlaylistId(string token)
        {
            string uploadsId = null;

            if (!CallApi("GET", MyChannelUrl, token, null, null, out string answer))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Could not read the channel: [{answer}]", this, GPALObjectType.YouTube);
            }
            else
            {
                using (var doc = JsonDocument.Parse(answer))
                {
                    if (doc.RootElement.TryGetProperty("items", out var items)
                        && items.GetArrayLength() > 0
                        && items[0].TryGetProperty("contentDetails", out var details)
                        && details.TryGetProperty("relatedPlaylists", out var related)
                        && related.TryGetProperty("uploads", out var uploads))
                        uploadsId = uploads.GetString();
                }

                if (string.IsNullOrEmpty(uploadsId))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "These credentials have no YouTube channel.", this, GPALObjectType.YouTube);
                    uploadsId = null;
                }
            }

            return uploadsId;
        }

        private bool ReadMyPlaylists(string token, List<YouTubePlaylistInfo> into, out string refusal)
        {
            return ReadAllPages(token, MyPlaylistsUrl, item =>
            {
                string playlistId = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                string title = string.Empty, description = string.Empty;
                YouTubePrivacy? privacy = null;
                int itemCount = 0;

                if (item.TryGetProperty("snippet", out var snippet))
                {
                    if (snippet.TryGetProperty("title",       out var t)) title       = t.GetString();
                    if (snippet.TryGetProperty("description", out var d)) description = d.GetString();
                }
                if (item.TryGetProperty("status", out var status) && status.TryGetProperty("privacyStatus", out var p))
                    privacy = PrivacyFrom(p.GetString());
                if (item.TryGetProperty("contentDetails", out var details) && details.TryGetProperty("itemCount", out var c))
                    c.TryGetInt32(out itemCount);

                if (!string.IsNullOrEmpty(playlistId))
                    into.Add(new YouTubePlaylistInfo(playlistId, title, description, privacy, itemCount));
            }, out refusal);
        }

        private bool ReadPlaylistItems(string token, string playlistId, List<YouTubePlaylistItem> into, out string refusal)
        {
            return ReadAllPages(token, PlaylistItemsListUrl + Uri.EscapeDataString(playlistId), item =>
            {
                string itemId  = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                string videoId = null, title = string.Empty;
                int position   = -1;

                if (item.TryGetProperty("snippet", out var snippet))
                {
                    if (snippet.TryGetProperty("title", out var t)) title = t.GetString();
                    if (snippet.TryGetProperty("position", out var p)) p.TryGetInt32(out position);
                    if (snippet.TryGetProperty("resourceId", out var r) && r.TryGetProperty("videoId", out var v))
                        videoId = v.GetString();
                }

                into.Add(new YouTubePlaylistItem(itemId, playlistId, videoId, title, position));
            }, out refusal);
        }

        // takes one entry out of a playlist. true when youtube did
        private bool DeletePlaylistItem(string token, YouTubePlaylistItem item)
        {
            bool deleted = CallApi("DELETE", PlaylistItemDeleteUrl + Uri.EscapeDataString(item.PlaylistItemId), token, null, null, out string answer);

            if (!deleted)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Could not remove [{item.VideoId}] from playlist [{item.PlaylistId}]: [{answer}]", this, GPALObjectType.YouTube);

            return deleted;
        }

        // reads the video as it is now, then writes it back with the settings the workflow gave in place of those values
        private void UpdateOneVideo(string token, string videoId)
        {
            if (!CallApi("GET", VideoInfoBaseUrl + Uri.EscapeDataString(videoId), token, null, null, out string current))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"UpdateVideo: could not read video [{videoId}]: [{current}]", this, GPALObjectType.YouTube);
            }
            else
            {
                byte[] body = null;

                using (var doc = JsonDocument.Parse(current))
                {
                    if (doc.RootElement.TryGetProperty("items", out var items)
                        && items.GetArrayLength() > 0
                        && items[0].TryGetProperty("snippet", out _))
                        body = VideoUpdateJson(items[0], videoId);
                }

                if (null == body)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"UpdateVideo: video [{videoId}] was not found.", this, GPALObjectType.YouTube);
                }
                else
                {
                    // Send status only when privacy was given, so the video's other status settings are untouched.
                    string url = "https://www.googleapis.com/youtube/v3/videos?part=" + (_privacyGiven ? "snippet,status" : "snippet");

                    if (CallApi("PUT", url, token, "application/json", body, out string answer))
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Updated video [{videoId}].", this, GPALObjectType.YouTube);
                    else
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to update video [{videoId}]: [{answer}]", this, GPALObjectType.YouTube);
                }
            }
        }

        // the body for videos.update. youtube clears any writable field the update leaves out, so every writable
        // snippet field is sent, the given setting where there is one and the video's current value where not.
        // status, sent only when privacy was given, carries the video's other status settings across the same way
        private byte[] VideoUpdateJson(JsonElement video, string videoId)
        {
            byte[] retVal;
            JsonElement snippet = video.GetProperty("snippet");

            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms))
                {
                    w.WriteStartObject();
                    w.WriteString("id", videoId);

                    w.WriteStartObject("snippet");
                    w.WriteString("title",       _titleGiven       ? _title       : StringOf(snippet, "title"));
                    w.WriteString("description", _descriptionGiven ? _description : StringOf(snippet, "description"));
                    w.WriteString("categoryId",  _categoryGiven    ? ((int)_category).ToString() : StringOf(snippet, "categoryId"));

                    w.WriteStartArray("tags");
                    if (_tagsGiven)
                    {
                        foreach (string tag in _tags)
                            w.WriteStringValue(tag);
                    }
                    else if (snippet.TryGetProperty("tags", out var tags))
                    {
                        foreach (var tag in tags.EnumerateArray())
                            w.WriteStringValue(tag.GetString());
                    }
                    w.WriteEndArray();

                    foreach (string name in new[] { "defaultLanguage", "defaultAudioLanguage" })
                        if (snippet.TryGetProperty(name, out var language))
                            w.WriteString(name, language.GetString());
                    w.WriteEndObject();

                    if (_privacyGiven)
                    {
                        w.WriteStartObject("status");
                        w.WriteString("privacyStatus", PrivacyString());

                        // a scheduled publish time only stands on a private video
                        var keep = new List<string> { "embeddable", "license", "publicStatsViewable", "selfDeclaredMadeForKids", "containsSyntheticMedia" };
                        if (YouTubePrivacy.Private == _privacy)
                            keep.Add("publishAt");

                        if (video.TryGetProperty("status", out var status))
                            foreach (var field in status.EnumerateObject())
                                if (keep.Contains(field.Name))
                                    field.WriteTo(w);
                        w.WriteEndObject();
                    }

                    w.WriteEndObject();
                }

                retVal = ms.ToArray();
            }

            return retVal;
        }

        private static string StringOf(JsonElement element, string name)
        {
            string retVal = string.Empty;

            if (element.TryGetProperty(name, out var value) && JsonValueKind.String == value.ValueKind)
                retVal = value.GetString();

            return retVal;
        }

        // Sends image bytes to thumbnails.set. A refusal is published at the caller's level, since it fails
        // SetThumbnail but ReplaceVideo carries on.
        private bool SetThumbnailBytes(string token, string videoId, byte[] image, string contentType, GPALEventType refusedAs)
        {
            bool set = CallApi("POST", ThumbnailSetUrl + Uri.EscapeDataString(videoId), token, contentType, image, out string answer);

            if (set)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Thumbnail set on [{videoId}].", this, GPALObjectType.YouTube);
            else
                GPAL.PublishSimpleEvent(refusedAs, $"Thumbnail not set on [{videoId}]: [{answer}]", this, GPALObjectType.YouTube);

            return set;
        }

        // every place the video has in the channel's playlists, in playlist and then position order.
        // false when a playlist could not be read, with the reason published
        private bool FindPlaylistPlaces(string token, string videoId, out List<YouTubePlaylistItem> places)
        {
            var found = new List<YouTubePlaylistItem>();
            var playlists = new List<YouTubePlaylistInfo>();
            bool ok = ReadMyPlaylists(token, playlists, out string refusal);

            if (!ok)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"ReplaceVideo: could not read the playlists: [{refusal}]", this, GPALObjectType.YouTube);

            for (int i = 0; ok && i < playlists.Count; i++)
            {
                var items = new List<YouTubePlaylistItem>();
                ok = ReadPlaylistItems(token, playlists[i].PlaylistId, items, out refusal);

                if (!ok)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"ReplaceVideo: could not read playlist [{playlists[i].PlaylistId}]: [{refusal}]", this, GPALObjectType.YouTube);
                else
                    found.AddRange(items.FindAll(item => videoId == item.VideoId));
            }

            found.Sort((a, b) => a.PlaylistId == b.PlaylistId ? a.Position.CompareTo(b.Position) : string.CompareOrdinal(a.PlaylistId, b.PlaylistId));
            places = found;
            return ok;
        }

        // uploads the file with the old video's title, description, tags, category and privacy through Upload,
        // putting the chain's own settings back afterwards. the playlist from WithPlaylist is left out, since the
        // new video's playlists are the old one's
        private YouTubeUploadResult UploadAsReplacement(GPALFile file, YouTubeVideoInfo old)
        {
            string          title       = _title;
            string          description = _description;
            string[]        tags        = _tags;
            YouTubePrivacy  privacy     = _privacy;
            YouTubeCategory category    = _category;
            string          playlistId  = _playlistId;

            _title       = old.Title;
            _description = old.Description;
            _tags        = old.Tags;
            _privacy     = old.Privacy ?? YouTubePrivacy.Private;
            _category    = int.TryParse(old.CategoryId, out int categoryId) ? (YouTubeCategory)categoryId : _category;
            _playlistId  = null;

            YouTubeUploadResult uploaded = Upload(file);

            _title       = title;
            _description = description;
            _tags        = tags;
            _privacy     = privacy;
            _category    = category;
            _playlistId  = playlistId;

            return uploaded;
        }

        // waits for the new video to process, then gives it the old thumbnail and the old playlist places
        private YouTubeUploadResult FinishReplacement(YouTubeVideoInfo old, YouTubeUploadResult uploaded, List<YouTubePlaylistItem> places)
        {
            string status = WaitUntilProcessed(uploaded.VideoId);
            var retVal = new YouTubeUploadResult(uploaded.VideoId, uploaded.Url, status);

            if ("failed" == status || "rejected" == status || "deleted" == status)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"ReplaceVideo: the new video [{uploaded.VideoId}] ended [{status}]. The playlists still hold [{old.VideoId}].",
                    this, GPALObjectType.YouTube);
            }
            else
            {
                // processing can outlast an access token
                _credentials.FetchAccessToken(out string token);

                CopyThumbnail(token, old, uploaded.VideoId);

                foreach (var place in places)
                    TakePlace(token, place, uploaded.VideoId);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Replaced [{old.VideoId}] with [{uploaded.Url}].", this, GPALObjectType.YouTube);
            }

            return retVal;
        }

        // checks the video's processing at the WaitForProcessing interval until youtube says it is done or will not be.
        // stops early when youtube has not answered several checks in a row
        private string WaitUntilProcessed(string videoId)
        {
            string status = null;
            int unanswered = 0;
            bool waiting = true;

            while (waiting)
            {
                _credentials.FetchAccessToken(out string token);
                string now = GetUploadStatus(token, videoId);

                if (now != status)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Video [{videoId}]: [{now}]", this, GPALObjectType.YouTube);

                status = now;
                unanswered = "unknown" == status ? unanswered + 1 : 0;
                waiting = "processed" != status && "failed" != status && "rejected" != status && "deleted" != status
                    && MaxUnansweredStatusChecks > unanswered;

                if (waiting)
                    Thread.Sleep(_pollIntervalSecs * 1000);
            }

            if (MaxUnansweredStatusChecks <= unanswered)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Stopped waiting for [{videoId}] to process. YouTube did not answer [{unanswered}] checks in a row.", this, GPALObjectType.YouTube);

            return status;
        }

        // reads the old video's thumbnail into memory and sets it on the new one. nothing is written to disk.
        // a refusal, which is what an unverified channel gets, is a warning and the replacement carries on
        private void CopyThumbnail(string token, YouTubeVideoInfo old, string newVideoId)
        {
            if (string.IsNullOrEmpty(old.ThumbnailUrl))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"ReplaceVideo: [{old.VideoId}] has no thumbnail to copy.", this, GPALObjectType.YouTube);
            }
            else
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(old.ThumbnailUrl);
                    req.Method = "GET";

                    byte[] image;
                    string contentType;

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var s    = resp.GetResponseStream())
                    using (var ms   = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        image       = ms.ToArray();
                        contentType = string.IsNullOrEmpty(resp.ContentType) ? "image/jpeg" : resp.ContentType;
                    }

                    SetThumbnailBytes(token, newVideoId, image, contentType, GPALEventType.WARNING);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION,
                        $"ReplaceVideo: could not copy the thumbnail of [{old.VideoId}] to [{newVideoId}].", this, GPALObjectType.YouTube, ex);
                }
            }
        }

        // puts the new video where the old one was in a playlist, then takes the old entry out. a playlist youtube
        // sorts for itself will not take a position, and there the new video goes in without one
        private void TakePlace(string token, YouTubePlaylistItem place, string newVideoId)
        {
            bool added = AddVideoToPlaylist(token, place.PlaylistId, newVideoId, place.Position, GPALEventType.WARNING);

            if (!added && place.Position >= 0)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Playlist [{place.PlaylistId}] would not take position [{place.Position}]. Adding [{newVideoId}] without one.", this, GPALObjectType.YouTube);
                added = AddVideoToPlaylist(token, place.PlaylistId, newVideoId, -1, GPALEventType.ERROR);
            }

            if (added)
                DeletePlaylistItem(token, place);
        }

        private static string BestThumbnail(JsonElement snippet)
        {
            string retVal = null;

            if (snippet.TryGetProperty("thumbnails", out var thumbnails))
                foreach (string size in new[] { "maxres", "standard", "high", "medium", "default" })
                    if (null == retVal && thumbnails.TryGetProperty(size, out var thumbnail) && thumbnail.TryGetProperty("url", out var url))
                        retVal = url.GetString();

            return retVal;
        }

        private static YouTubePrivacy? PrivacyFrom(string privacyStatus)
        {
            YouTubePrivacy? retVal = null;

            switch (privacyStatus)
            {
                case "public":   retVal = YouTubePrivacy.Public;   break;
                case "unlisted": retVal = YouTubePrivacy.Unlisted; break;
                case "private":  retVal = YouTubePrivacy.Private;  break;
            }

            return retVal;
        }
    }
}
