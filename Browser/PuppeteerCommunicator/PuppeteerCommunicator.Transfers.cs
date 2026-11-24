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
        /// <summary>
        /// Starts the browser downloading a url by sending the tab to it, without waiting for a page, since a file
        /// never becomes one. Chrome answers net::ERR_ABORTED when the navigation turns into a download, which here
        /// is the expected answer, not a failure. Whether the file arrived is for the download watcher to say.
        /// </summary>
        /// <param name="url">Absolute http or https url to download</param>
        /// <param name="sessionId">CDP session id; the effective session is used if null.</param>
        /// <returns>Task that completes once the browser has answered the navigation</returns>
        public async Task StartDownload(string url, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            var result = await SendCommand<object>(DevToolsMethods.PageNavigate, new { url }, sessionId).ConfigureAwait(false);
            string errorText = (result as Newtonsoft.Json.Linq.JObject)?["errorText"]?.ToString();

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Sent the tab to [{url}] to download it, the browser answered [{(true == string.IsNullOrEmpty(errorText) ? "a page" : errorText)}]", this, GPALObjectType.PuppeteerCommunicator);
        }

        /// <summary>
        /// Saves downloads to <paramref name="downloadPath"/> with no Save dialog. Browser-wide, so later tabs use it
        /// too.<br/>
        /// Done over CDP because the dialog setting is a launch-time profile preference that
        /// <see cref="MagicHelper.LaunchBrowser"/> writes only into profiles it creates.
        /// </summary>
        /// <param name="downloadPath">Directory downloads should land in</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null</param>
        /// <returns>Task representing the asynchronous call</returns>
        public async Task SetDownloadBehavior(string downloadPath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "allow", downloadPath = downloadPath }, sessionId).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets download behavior back to "default", so a user's profile stops saving into the workflow's folder. CDP
        /// cannot read the previous directory.
        /// </summary>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null</param>
        /// <returns>Task representing the asynchronous call</returns>
        public async Task RestoreDownloadBehavior(string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();

            await SendCommand<object>(DevToolsMethods.BrowserSetDownloadBehavior, new { behavior = "default" }, sessionId).ConfigureAwait(false);
        }

        // Implementation for "left-click-download"
        // Browser.LeftClickAndDownload calls this in headless or when the browser does not prompt for downloads
        /// <summary>
        /// Performs a left click on each element in <paramref name="elems"/> to trigger a file download (headless mode).
        /// Sets the browser-level download behavior to the directory containing <paramref name="downloadPath"/> before clicking.
        /// The actual filename cannot be controlled here, so a download watcher is required to detect the resulting file.
        /// Implementation for the "left-click-download" workflow action. Headless only.
        /// </summary>
        /// <param name="elems">Elements to click (typically a download link or button).</param>
        /// <param name="downloadPath">Target file path; only its directory is used to configure the download location.</param>
        /// <param name="modifiers">Keyboard modifier flags applied to the click (e.g. Ctrl/Shift).</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <param name="responses">List collecting the raw CDP responses from each click.</param>
        /// <returns>Task representing the asynchronous click-and-download operation.</returns>
        public async Task LeftClickAndDownload(List<GPALElement> elems, string downloadPath, int modifiers, string sessionId, List<dynamic> responses)
        {
            sessionId ??= GetEffectiveSessionId();

            string saveDirectory = Path.GetDirectoryName(downloadPath);

            // CDP sets the download directory but not the filename, so the download watcher finds the file
            await SetDownloadBehavior(saveDirectory, sessionId).ConfigureAwait(false);

            foreach (GPALElement elem in elems)
            {
                await ScrollIntoView(elem, sessionId).ConfigureAwait(false);
                await ClickElement(elem, sessionId, responses, ClickType.LeftClick, modifiers).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Attaches one or more files to file input element(s) using CDP.
        /// Supports single file path (string) or multiple paths (List&lt;string&gt; / IEnumerable&lt;string&gt;).
        /// Accumulates files across calls by tracking attached paths per session/input and re-attaching the full list each time.
        /// Uses DOM.setFileInputFiles to set files directly (headless-friendly, no dialog).
        /// Dispatches change event after attachment to trigger page/widget processing.
        /// </summary>
        /// <param name="elems">List of GPALElement instances representing the file input(s). Usually one, but supports multiple.</param>
        /// <param name="uploadFiles">
        /// File path(s) to attach.
        /// - string: single file path (e.g. @"C:\temp\report.pdf")
        /// - IEnumerable&lt;string&gt; or List&lt;string&gt;: multiple file paths (e.g. new[] { @"C:\file1.pdf", @"C:\file2.jpg" })
        /// </param>
        /// <param name="modifiers">Keyboard modifiers (not used in current implementation)</param>
        /// <param name="sessionId">CDP session ID (optional; falls back to current)</param>
        /// <param name="responses">Optional list to collect status/info (e.g. for logging or fluent chaining)</param>
        /// <returns>Task (async operation)</returns>
        /// <example>
        /// Single file:
        /// <code>
        /// await LeftClickAndUpload(elems, @"C:\temp\doc.pdf", 0, sessionId, responses);
        /// </code>
        ///
        /// Multiple files (additive across calls):
        /// <code>
        /// var files = new List&lt;string&gt; { @"C:\doc1.pdf" };
        /// await LeftClickAndUpload(elems, files, 0, sessionId, responses);
        ///
        /// files.Add(@"C:\photo.jpg");
        /// await LeftClickAndUpload(elems, files, 0, sessionId, responses);  // now attaches both
        /// </code>
        ///
        /// Mixed:
        /// <code>
        /// await LeftClickAndUpload(elems, @"C:\invoice.docx", 0, sessionId, responses);  // single
        /// await LeftClickAndUpload(elems, new[] { @"C:\old1.pdf", @"C:\old2.pdf" }, 0, sessionId, responses);  // full list
        /// </code>
        /// </example>
        public async Task LeftClickAndUpload(
            List<GPALElement> elems,
            object uploadFiles,
            int modifiers,
            string sessionId,
            List<dynamic> responses)
        {
            sessionId ??= GetEffectiveSessionId();

            // Normalize uploadFiles to List<string>
            var newPaths = new List<string>();

            if (uploadFiles is string singlePath)
            {
                if ("" != singlePath?.Trim())
                {
                    newPaths.Add(Path.GetFullPath(singlePath));
                }
            }
            else if (uploadFiles is GPALFile pathEnumerable)
            {
                foreach (var p in pathEnumerable.Filenames)
                {
                    if ("" != p?.Trim())
                    {
                        newPaths.Add(Path.GetFullPath(p));
                    }
                }
            }
            else
            {
                // Neither a path nor a GPALFile: nothing is attached
                return;
            }

            if (0 == newPaths.Count)
            {
                return; // nothing to attach
            }

            foreach (GPALElement fileInput in elems)
            {
                string nodeKey = $"{sessionId}:{fileInput.ElementBackendNodeId}";

                // Get browser-reported file count via attribute
                string fileCountStr = fileInput.GetAttribute("length");
                int browserFileCount = 0;

                if ("" != fileCountStr)
                {
                    Int32.TryParse(fileCountStr, out browserFileCount);
                }

                // Get or create tracked list
                if (false == _attachedFilesPerSession.TryGetValue(nodeKey, out var currentFiles))
                {
                    currentFiles = new List<string>();
                    _attachedFilesPerSession[nodeKey] = currentFiles;
                }

                // Staleness check: browser count != tracked count > reset
                if (browserFileCount != currentFiles.Count)
                {
                    currentFiles.Clear();
                    _attachedFilesPerSession[nodeKey] = currentFiles;
                }

                // Add new paths
                currentFiles.AddRange(newPaths);

                var setFilesParams = new
                {
                    nodeId = fileInput.ElementNodeId,
                    files = currentFiles.ToArray()
                };

                await SendCommand<object>(DevToolsMethods.DOMSetFileInputFiles, setFilesParams, sessionId).ConfigureAwait(false);
                await FireChangeEvent(fileInput.ElementHandle, sessionId).ConfigureAwait(false);

                responses?.Add(new
                {
                    status = "upload_attached",
                    pathsAdded = newPaths,
                    totalFilesTracked = currentFiles.Count,
                    browserReportedCount = browserFileCount
                });
            }
        }

        // Implementation for "set-download-filename"
        /// <summary>
        /// Configures the page-level download behavior so that future downloads are saved to the given path.
        /// Implementation for the "set-download-filename" workflow action.
        /// </summary>
        /// <param name="downloadPath">Directory path where downloads should be saved.</param>
        /// <param name="sessionId">CDP session ID; falls back to the current effective session if null.</param>
        /// <returns>Task representing the asynchronous configuration operation.</returns>
        public async Task SetDownloadFilename(string downloadPath, string sessionId = null)
        {
            sessionId ??= GetEffectiveSessionId();
            await SendCommand<object>(DevToolsMethods.PageSetDownloadBehavior, new { behavior = "allow", downloadPath }, sessionId).ConfigureAwait(false);
        }
    }
}
