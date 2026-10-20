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
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using static GenerallyPositive.Enums;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Core;
using Newtonsoft.Json;
using System.Collections;
using System.Net;
using OpenQA.Selenium;
using System.Drawing;
using System.Text.RegularExpressions;
using GenerallyPositive.Browser;
using ClosedXML.Excel;
using Newtonsoft.Json.Linq;
using System.Linq.Expressions;
using System.Windows.Forms.DataVisualization.Charting;

namespace GenerallyPositive
{
    /// <summary>
    /// File helpers behind the fluent chain: delimited output, each browser's download folder, input tokenizing, and
    /// path handling.<br/>
    /// Mostly internal. The public members are mainly for finding where a download will be saved before fetching it.
    /// </summary>
    public class FileHelper
    {
        // Suppress repeat log messages (e.g. the "no delimiter set" notice fires once per parsed line - identical
        // every time). We publish a given message once, then emit a single "Supressing repeat messages" notice.
        private static readonly HashSet<string> _loggedMessages = new HashSet<string>();
        private static bool _suppressNoticeShown = false;

        /// <summary>
        /// Publishes <paramref name="message"/> only the first time it is seen; on any later repeat it emits a
        /// single "Supressing repeat messages" notice and then stays silent, mirroring the app-side helper.
        /// </summary>
        private static void PublishSuppressingRepeats(GPALEventType type, string message, object source, GPALObjectType objectType)
        {
            if (true == _loggedMessages.Add(message))
                GPAL.PublishSimpleEvent(type, message, source, objectType);
            else if (false == _suppressNoticeShown)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Supressing repeat messages", source, objectType);
                _suppressNoticeShown = true;
            }
        }

        /// <summary>
        /// Try to tokenize the input file when it is defined
        /// May not tokenize correctly if delimeter is not defined correctly
        /// </summary>
        /// <param name="fileSettings"></param>
        /// <param name="tokens">The grids read from the file</param>
        internal static int GetGrid(GPALFileSettings fileSettings, out List<IGPALGrid<string>> tokens)
        {
            FileStream fileStream = null;
            StreamReader streamReader = null;
            string currentLine;
            int fileIdx = 0;
            List<IGPALGrid<string>> myTokens = new List<IGPALGrid<string>>();
            IGPALGrid<string> myGrid = GPAL.Grid.ToGPALObject();

            foreach (string filename in fileSettings.Filenames)
                try
                {
                    string directory = Path.GetDirectoryName(filename);
                    string filenamepart = Path.GetFileName(filename);
                    if (true == File.Exists(filename))
                    foreach (string filename2 in Directory.GetFiles(false == string.IsNullOrEmpty(directory) ? directory : @".\", filenamepart))
                    {
                        bool firstLineSkipped = false;

                            using (fileStream = System.IO.File.OpenRead(filename2))
                            {
                                using (streamReader = new StreamReader(fileStream))
                                {
                                    while (null != (currentLine = streamReader?.ReadLine()))    // get one line
                                    {
                                        if (false == firstLineSkipped && 0 < fileSettings.IgnoreFirstLineColumnNames.Count && fileSettings.IgnoreFirstLineColumnNames[0])    // CAVEAT: hardcoded value (but we always have one file now)
                                        {
                                            firstLineSkipped = true;
                                            continue;
                                        }
                                        List<string> currentRow = new List<string>();
                                        char delim;

                                        if (0 < fileSettings.Delimiter.Count && true == fileSettings.Delimiter[fileIdx].HasValue)
                                            delim = fileSettings.Delimiter[fileIdx].Value;
                                        else
                                        {
                                            PublishSuppressingRepeats(GPALEventType.INFO, $"[{filename}] has no delimiter set, using comma.", fileSettings, GPALObjectType.FileSettings);
                                            delim = ',';
                                        }

                                        foreach (string token in ConverterHelper.SplitRespectingQuotes(currentLine, delim))   // parse tokens and add to our grid, quote-aware so embedded delimiters/commas inside a quoted field don't split it
                                            currentRow.Add(token);

                                        myGrid.AddRow(currentRow);  // add row to our grid
                                    }
                                    myTokens.Add(myGrid);   // add grid to our Token list
                                    fileSettings.FileGrid.Add(myGrid);
                                }
                            }
                        fileIdx++;
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to tokenize File [{filename}]. Tokens created [{myGrid.Rows}", fileSettings, GPALObjectType.FileSettings, ex);
                }
                finally
                {
                    streamReader?.Close();
                    fileStream?.Close();
                }
            tokens = myTokens;
            return tokens.Count();
        }
        /// <summary>
        /// Delete the destination file before download (avoid 'overwrite existing file' alert -or-
        /// return a new filename if the destination file exists and overwrite existing file is not chosen
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="newFilename"></param>
        /// <param name="DeleteFileBeforeDownload">True to delete an existing destination file, false to pick a new filename</param>
        /// <returns></returns>
        internal static string CleanUpFileDestination(string filename, out string newFilename, bool DeleteFileBeforeDownload)
        {
            int counter = 0;
            return CleanUpFileDestination(filename, out newFilename, DeleteFileBeforeDownload, Enums.NextFilePattern.Timestamp, ref counter);
        }
        /// <summary>
        /// As <see cref="CleanUpFileDestination(string, out string, bool)"/>, but when the destination already
        /// exists the new filename is generated using the supplied <see cref="Enums.NextFilePattern"/> instead of
        /// the fixed timestamp. For the counter patterns, <paramref name="patternCounter"/> is the starting value
        /// and is advanced past the filename that was actually chosen so repeated calls keep incrementing.
        /// </summary>
        internal static string CleanUpFileDestination(string filename, out string newFilename, bool DeleteFileBeforeDownload,
            Enums.NextFilePattern nextFilePattern, ref int patternCounter)
        {
            string fullFilePath;
            string currentDirectory = Path.GetDirectoryName(filename);

            newFilename = filename;
            if (true == string.IsNullOrEmpty(currentDirectory))
            {
                currentDirectory = ".\\"; // KnownFolders.GetPath(KnownFolder.Downloads);
                fullFilePath = currentDirectory + filename;
            }
            else
                fullFilePath = filename;

            if (true == DeleteFileBeforeDownload)
            {
                try
                {
                    if (true == File.Exists(fullFilePath))
                    {
                        System.IO.File.Delete(fullFilePath);
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Deleted file [{fullFilePath}].", null, GPALObjectType.None);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Failed to delete file [{fullFilePath}], Exception caught.", null, GPALObjectType.None, ex);
                }
            }
            else if (true == System.IO.File.Exists(fullFilePath))
            {
                string baseName = Path.GetFileNameWithoutExtension(filename);
                string extension = Path.GetExtension(filename);

                if (Enums.NextFilePattern.Counter == nextFilePattern || Enums.NextFilePattern.CounterPadded == nextFilePattern)
                {
                    // Seed the counter once from the highest existing "<base>_<n><ext>" via a single directory
                    // listing rather than probing _0001, _0002 ... one File.Exists at a time. This is O(1) syscalls
                    // per call after seeding, and (unlike a contiguous binary probe) stays correct when the numbering
                    // has gaps from deleted files. Zero means "not seeded yet"; the caller persists it thereafter.
                    if (0 == patternCounter)
                        patternCounter = HighestCounterIndex(currentDirectory, baseName, extension);

                    // max index + 1 is guaranteed free; the loop only re-spins if another process races in.
                    do
                    {
                        newFilename = string.Concat(baseName, NextFileSuffix(nextFilePattern, ref patternCounter), extension);
                        fullFilePath = Path.Combine(currentDirectory, newFilename);
                    }
                    while (true == System.IO.File.Exists(fullFilePath));
                }
                else
                {
                    // timestamp/date patterns are already unique enough, taken as-is
                    newFilename = string.Concat(baseName, NextFileSuffix(nextFilePattern, ref patternCounter), extension);
                    fullFilePath = Path.Combine(currentDirectory, newFilename);
                }
            }

            return fullFilePath;
        }
        /// <summary>
        /// Finds the largest numeric index already present among files named "<paramref name="baseName"/>_&lt;digits&gt;<paramref name="extension"/>"
        /// in <paramref name="directory"/>, so a counter-based <see cref="Enums.NextFilePattern"/> can start at max+1
        /// without probing each candidate name individually. Matches both padded and unpadded numbering. Returns 0 if none.
        /// </summary>
        private static int HighestCounterIndex(string directory, string baseName, string extension)
        {
            int max = 0;

            try
            {
                string searchDir = string.IsNullOrEmpty(directory) ? "." : directory;
                // '*' matches the number (and only the number, thanks to the digit check below)
                string searchPattern = baseName + "_*" + extension;

                foreach (string path in Directory.EnumerateFiles(searchDir, searchPattern))
                {
                    string name = Path.GetFileNameWithoutExtension(path);   // <base>_<digits>
                    int underscore = name.LastIndexOf('_');
                    if (0 > underscore || underscore + 1 >= name.Length)
                        continue;

                    string digits = name.Substring(underscore + 1);
                    if (true == digits.All(char.IsDigit) && true == int.TryParse(digits, out int n) && n > max)
                        max = n;
                }
            }
            catch
            {
                // any listing failure just leaves max at 0; the File.Exists guard on the caller still keeps us correct
            }

            return max;
        }
        /// <summary>
        /// Builds the suffix inserted between a filename's base name and extension when
        /// <see cref="GPALFile.Next"/> generates a new unique filename. The counter patterns advance
        /// <paramref name="counter"/> so each call produces a distinct value.
        /// </summary>
        private static string NextFileSuffix(Enums.NextFilePattern pattern, ref int counter)
        {
            switch (pattern)
            {
                case Enums.NextFilePattern.DateTimeStamp:
                    return "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                case Enums.NextFilePattern.DateStamp:
                    return "_" + DateTime.Now.ToString("yyyyMMdd");
                case Enums.NextFilePattern.Counter:
                    return "_" + (++counter).ToString();
                case Enums.NextFilePattern.CounterPadded:
                    // D4 so up to 9999 files sort correctly; past that it just grows an extra digit
                    return "_" + (++counter).ToString("D4");
                case Enums.NextFilePattern.Timestamp:
                default:
                    return DateTime.Now.ToString("yyyyMMddHHmmssfff");
            }
        }
        /// <summary>
        /// Downloads a url with a plain HTTP request, for a file that needs no browser session.<br/>
        /// No cookies or anti-bot clearance are sent, so a protected file returns 401 or 403; use
        /// <see cref="FetchOrDownloadInTab"/> with a browser.<br/>
        /// An existing file is only re-downloaded if the server says it changed.
        /// </summary>
        /// <param name="filename">file to save to</param>
        /// <param name="downloadUrl">Url to download</param>
        /// <returns>True when the file is in place</returns>
        internal static bool DownloadToFile(string filename, string downloadUrl)
        {
            bool downloaded = false;
            string url = downloadUrl;

            try
            {
                using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient())
                {
                    System.Net.Http.HttpRequestMessage request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);

                    // Send a browser user agent; many sites refuse a request without one. Chrome's when no browser type
                    // is set.
                    request.Headers.TryAddWithoutValidation("User-Agent",
                        true == string.IsNullOrWhiteSpace(GPAL.GPALSettings.UserAgent)
                            ? MagicHelper.GetUserAgentString(BrowserType.Chrome)
                            : GPAL.GPALSettings.UserAgent);

                    if (true == File.Exists(filename))
                        request.Headers.IfModifiedSince = File.GetLastWriteTimeUtc(filename);

                    System.Net.Http.HttpResponseMessage response = client.SendAsync(request).GetAwaiter().GetResult();

                    if (System.Net.HttpStatusCode.NotModified == response.StatusCode)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] has not changed, using [{filename}].", null, GPALObjectType.None);
                        downloaded = true;
                    }
                    else if (false == response.IsSuccessStatusCode)
                    {
                        // A 401 or 403 means the file needs a login session. Give the GPALFile a browser.
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{url}] returned HTTP [{(int)response.StatusCode}]. Give the file a browser and it is fetched on that browser's session instead", null, GPALObjectType.None);
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloading [{url}] to [{filename}].", null, GPALObjectType.None);
                        FileHelper.EnsureFolderFor(filename);
                        File.WriteAllBytes(filename, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());

                        // Stamp the file with the server's Last-Modified so the next "changed since" check uses the
                        // server's date.
                        if (null != response.Content.Headers.LastModified)
                            File.SetLastWriteTimeUtc(filename, response.Content.Headers.LastModified.Value.UtcDateTime);

                        // WriteAllBytes has returned, so the file is complete; no size polling.
                        downloaded = true;
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] failed to save to [{filename}].", null, GPALObjectType.None, ex);
            }

            return downloaded;
        }
        /// <summary>
        /// Get a meaningful url for download from the element
        /// </summary>
        /// <param name="element"></param>
        /// <returns></returns>
        internal static string GetDownloadUrl(GPALElement element)
        {
            // Check the type of element or its attributes to determine the appropriate URL for downloading
            string downloadUrl = null;

            // An image match has no tag or attributes, so ask the page which link it is inside.
            if ("GPALElement" == element.TagName)
            {
                downloadUrl = ElementHelper.UrlUnderImage(element);
            }
            // If the element is an <a> (anchor) tag, get the href attribute value
            else if (element.TagName.ToLower() == "a")
            {
                downloadUrl = element.GetAttribute("href");
            }
            // If the element is an <img> (image) tag, get the src attribute value
            else if (element.TagName.ToLower() == "img")
            {
                downloadUrl = element.GetAttribute("src");
            }
            // If the element is a <script> tag, get the src attribute value
            else if (element.TagName.ToLower() == "script")
            {
                downloadUrl = element.GetAttribute("src");
            }
            // If the element is a <link> tag, get the href attribute value
            else if (element.TagName.ToLower() == "link")
            {
                downloadUrl = element.GetAttribute("href");
            }
            // If the element is an <object> tag, get the data attribute value
            else if (element.TagName.ToLower() == "object")
            {
                downloadUrl = element.GetAttribute("data");
            }
            // If the element is an <embed> tag, get the src attribute value
            else if (element.TagName.ToLower() == "embed")
            {
                downloadUrl = element.GetAttribute("src");
            }
            // If the element is a <frame> or <iframe> tag, get the src attribute value
            else if (element.TagName.ToLower() == "frame" || element.TagName.ToLower() == "iframe")
            {
                downloadUrl = element.GetAttribute("src");
            }
            // If the element is an <audio> tag, get the src attribute value
            else if (element.TagName.ToLower() == "audio")
            {
                downloadUrl = element.GetAttribute("src");
            }
            // If the element is a <video> tag, get the src attribute value
            else if (element.TagName.ToLower() == "video")
            {
                downloadUrl = element.GetAttribute("src");
            }
            // Add more conditions for other types of elements as needed...

            // An attribute holds the url as written, often relative ("/a/b.zip"). The browser resolves it against the
            // page it is on, and so does this; Selenium already hands back the resolved one, which stays as it is.
            if (false == string.IsNullOrWhiteSpace(downloadUrl)
                && true == Uri.TryCreate(element.Browser?.CurrentUrl, UriKind.Absolute, out Uri page)
                && true == Uri.TryCreate(page, downloadUrl, out Uri resolved))
                downloadUrl = resolved.AbsoluteUri;

            return downloadUrl;
        }
        internal static FileSystemWatcher watcher;
        internal static DateTime watchArmed;    // when the watcher went up, so only files that arrive after this timestap
        internal static readonly HashSet<string> watchedScratchFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // the ones this download started

        internal static string SetupDownloadWatcher(string fullFilePathToSave, Browser.BrowserSettings browserSettings)
        {
            // Chrome and Edge save straight into the folder we asked for, so we watch that folder.
            // OttoMagic and Firefox always save to the browser's normal download folder, so we watch that one
            // and handleFile moves the file to where it was asked for. OttoMagic can name the file but not
            // choose its folder; Firefox only reads its download folder when it starts.
            bool downloadsWhereItLikes = true == browserSettings.UseOttoMagic
                                      || BrowserType.FireFox == browserSettings.BrowserType;
            string downloadDirectory = true == downloadsWhereItLikes
                                     ? FileHelper.GetDefaultDownloadDirectory(browserSettings.Browser)
                                     : Path.GetDirectoryName(fullFilePathToSave) ?? FileHelper.GetDefaultDownloadDirectory(browserSettings.Browser);

            string fileName = Path.GetFileName(fullFilePathToSave);
            string actualDownloadedFilename = null;

            watchArmed = DateTime.Now;
            watchedScratchFiles.Clear();

            // A browser makes its download folder when the first file lands in it, and a watcher cannot start on a
            // folder that is not there yet
            Directory.CreateDirectory(downloadDirectory);

            watcher = new FileSystemWatcher(downloadDirectory);
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes;
            watcher.Filter = "*.*";
            watcher.EnableRaisingEvents = true;

            // Add Created (often fires for temp file start)
            watcher.Created += (sender, e) => handleFile(sender, e, downloadDirectory, fullFilePathToSave);
            watcher.Changed += (sender, e) => handleFile(sender, e, downloadDirectory, fullFilePathToSave);
            watcher.Renamed += (sender, e) => handleFile(sender, e, downloadDirectory, fullFilePathToSave);

            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Watching [{downloadDirectory}] for [{fileName}] w/timeout [{browserSettings.DownloadTimeoutInSec}] secs", null, GPALObjectType.None);
            System.Threading.Thread.Sleep(1_000);

            return downloadDirectory;

            void handleFile(object sender, FileSystemEventArgs e, string downloaddirectory, string fullFilePathToSave)
            {
                try
                {
                    // Browsers download to a temporary name (.crdownload, .part) and rename it to the site's filename
                    // when done. Only the rename event gives the final path.
                    if (e is RenamedEventArgs renamed
                        && (true == renamed.OldName.EndsWith(".crdownload") || true == renamed.OldName.EndsWith(".part")))
                    {
                        actualDownloadedFilename = WaitForDownloadToFinish(Path.Combine(downloaddirectory, renamed.Name), browserSettings.DownloadTimeoutInSec);
                        browserSettings.FileDownloaded = false == string.IsNullOrEmpty(actualDownloadedFilename);

                        if (true == browserSettings.FileDownloaded)
                        {
                            FileHelper.EnsureFolderFor(fullFilePathToSave);
                            File.Move(actualDownloadedFilename, fullFilePathToSave);
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{actualDownloadedFilename}] moved to [{fullFilePathToSave}]", null, GPALObjectType.None);
                        }
                        else
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{renamed.OldName}] was renamed to [{renamed.Name}] but it never settled", null, GPALObjectType.None);

                        // Dispose the watcher only after the download is found; a later rename event may still find it.
                        if (true == browserSettings.FileDownloaded)
                            ((FileSystemWatcher)sender).Dispose();

                        return;
                    }

                    if (e.Name.EndsWith(".crdownload") || e.Name.EndsWith(".part"))
                    {
                        string filename = e.Name.Replace(".crdownload", "").Replace(".part", "");
                        string fullSavedFilePath = Path.Combine(downloaddirectory, filename);  // Safer path building

                        string pattern = @"^downloads(?:\.htm|\s*\(\d+\)\.htm|\.htm\s*\(\d+\))$";
                        if (Regex.IsMatch(filename, pattern, RegexOptions.IgnoreCase))
                        {
                            return;
                        }

                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Download started for [{e.Name}] waiting on expected [{fullSavedFilePath}]", null, GPALObjectType.None);
                        watchedScratchFiles.Add(Path.Combine(downloaddirectory, e.Name));

                        // disable events temporarily to avoid duplicate calls during wait
                        // watcher.EnableRaisingEvents = false;

                        // first check and wait for the crdownload file to finish, which might finish before we even call, so it never finds it...
                        if (true == File.Exists(Path.Combine(downloaddirectory, e.Name)))
                            actualDownloadedFilename = WaitForDownloadToFinish(Path.Combine(downloaddirectory, e.Name), browserSettings.DownloadTimeoutInSec);

                        // The temporary file is never the result. If WaitForDownloadToFinish returns its name, the
                        // browser renamed it; clear it and find the real name below.
                        if (true == IsTemporaryDownload(actualDownloadedFilename))
                            actualDownloadedFilename = null;

                        // Check the destination path first; with a filename set, the browser renames straight to it.
                        if (true == File.Exists(fullFilePathToSave))
                            actualDownloadedFilename = WaitForDownloadToFinish(fullFilePathToSave, browserSettings.DownloadTimeoutInSec);

                        // Otherwise check the temporary name without its suffix, where some browsers save. Only when
                        // nothing above found the file.
                        if (true == string.IsNullOrEmpty(actualDownloadedFilename))
                            actualDownloadedFilename = WaitForDownloadToFinish(fullSavedFilePath, browserSettings.DownloadTimeoutInSec);

                        browserSettings.FileDownloaded = !string.IsNullOrEmpty(actualDownloadedFilename);

                        if (true == browserSettings.FileDownloaded)
                        {
                            // The file is already at the requested path; File.Move onto the same path throws.
                            if (true == fullFilePathToSave.Equals(actualDownloadedFilename, StringComparison.OrdinalIgnoreCase))
                                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{fullFilePathToSave}] downloaded straight to where it was asked to go", null, GPALObjectType.None);
                            else
                            {
                                FileHelper.EnsureFolderFor(fullFilePathToSave);
                                File.Move(actualDownloadedFilename, fullFilePathToSave);
                                if (File.Exists(actualDownloadedFilename))
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unable to move [{actualDownloadedFilename}] to [{fullFilePathToSave}]", null, GPALObjectType.None);
                                else
                                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{actualDownloadedFilename}] moved to [{fullFilePathToSave}]", null, GPALObjectType.None);
                            }
                        }
                        else
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"WaitForDownloadToFinish returned null for [{fullSavedFilePath}] - check timeout or stability", null, GPALObjectType.None);
                        }

                        // Dispose the watcher only after the download is found; a browser still finishing has not
                        // renamed the file yet.
                        if (true == browserSettings.FileDownloaded)
                            ((FileSystemWatcher)sender).Dispose();
                    }
                }
                catch (Exception ex)
                {
                    // Keep the watcher running and leave the flag unchanged. One download raises many events, and one
                    // failing mid-change does not mean the download failed; returning false would end the wait.
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Error for [{fullFilePathToSave}]", null, GPALObjectType.None, ex);
                }
            }
        }

        internal const int DownloadSizeSampleMs = 500;   // long enough that a file still being written is seen to grow

        /// <summary>
        /// True when this is a browser's scratch file for a download in progress rather than a downloaded file.
        /// </summary>
        /// <param name="path">Path or filename to test</param>
        /// <returns>True for a chrome .crdownload or a firefox .part</returns>
        internal static bool IsTemporaryDownload(string path)
        {
            bool retVal = true == path?.EndsWith(".crdownload") || true == path?.EndsWith(".part");

            return retVal;
        }

        /// <summary>
        /// Returns the newest file in <paramref name="directory"/> that arrived at or after <paramref name="since"/>
        /// and is not a download in progress.<br/>
        /// Without a given filename the browser uses the server's name, which the temporary file (e.g. "Unconfirmed
        /// 175338.crdownload") does not show.
        /// </summary>
        /// <param name="directory">Directory the browser downloads into</param>
        /// <param name="since">When the watcher was started. Limits the result to this download: a run can save many
        /// files to the same directory, and a wider window would return the previous one again</param>
        /// <returns>The downloaded file, or null when nothing arrived</returns>
        internal static string NewestDownloadSince(string directory, DateTime since)
        {
            string retVal = null;
            DateTime newest = DateTime.MinValue;

            foreach (string candidate in Directory.EnumerateFiles(directory))
                if (false == IsTemporaryDownload(candidate))
                {
                    // Use the later of creation and write time. A fetched file's write time is the server's
                    // Last-Modified, and Windows gives a recreated name the original's creation time. Both are never
                    // stale at once.
                    DateTime written = File.GetLastWriteTime(candidate);
                    DateTime created = File.GetCreationTime(candidate);
                    DateTime arrived = created > written ? created : written;

                    if (arrived >= since && arrived > newest)
                    {
                        newest = arrived;
                        retVal = candidate;
                    }
                }

            return retVal;
        }

        /// <summary>
        /// wait for a file to finish downloading by checking to see the file size grows
        /// TODO: refactor to just try to open the file, on exception delay a second. assumes the browser has it locked writing to it
        /// </summary>
        /// <param name="filePath">Path of the file being downloaded</param>
        /// <param name="downloadTimeoutInSec">Seconds to wait for the download</param>
        /// <returns>the name of the file that was downloaded</returns>
        internal static string WaitForDownloadToFinish(string filePath, int downloadTimeoutInSec)
        {
            long size = 0, newSize = 0;
            string downloadedFilename;
            bool fileStarted = false;

            newSize = WaitForFileToDownload(filePath, downloadTimeoutInSec, size, out downloadedFilename);   // -1 return if timed out waiting for file to exist (not completed)

            // KLUDGE: file showed up but is zero bytes, this could be correct, but let's give an extra second and wait
            // NOTE: hardcoded value - not really in favor of hard sleeps during the workflow espcially since the download has started, but maybe it's slow for some reason
            // our file checks can run very quuckly
            if (0 == newSize)
                System.Threading.Thread.Sleep(1_000);
            else if (-1 == newSize)
            {
                if (false == filePath.EndsWith(".crdownload") && false == filePath.EndsWith(".part"))
                    return string.Empty;
                else
                    return filePath;
            }

            size = newSize;

            // NOTE: this is prolly really overkill, essentially checking multiple times for completion
            do
            {
                if (0 <= (newSize = WaitForFileToDownload(filePath, downloadTimeoutInSec, newSize, out downloadedFilename)))
                {
                    // NOTE: new scenario, race condition, if file starts download then chrome renames, we get -1 and think it is not downloaded
                    fileStarted = true;

                    // KLUDGE: file showed up but is zero bytes, this could be correct, but let's give an extra second and wait
                    // NOTE: hardcoded value - not really in favor of hard sleeps during the workflow espcially since the download has started, but maybe it's slow for some reason
                    // our file checks can run very quuckly
                    if (0 == newSize)
                        System.Threading.Thread.Sleep(1_000);

                    // Wait between the two size reads, or a part-written file looks finished.
                    System.Threading.Thread.Sleep(DownloadSizeSampleMs);

                    if (size == (newSize = WaitForFileToDownload(filePath, downloadTimeoutInSec, newSize, out downloadedFilename)))  // NOTE: ensure the filesize stays the same (prolly overkill)
                        break;
                    else
                        size = newSize;
                }
                else if (-1 == newSize) // file isn't there..
                    break;
            } while (true); // until we break when the file szie no longer changes

            if (false == filePath.EndsWith(".crdownload") && false == filePath.EndsWith(".part"))
            {
                if (0 == newSize)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File [{downloadedFilename ?? filePath}] downloaded as zero bytes", null, GPALObjectType.None);
                else if (-1 == newSize && false == fileStarted)
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"File [{downloadedFilename ?? filePath}] NOT downloaded", null, GPALObjectType.None);
                // NOTE: if -1 and file started, we have a race condition where the file was renamed, prolly by the browser - so ignore it, it's just a temp file
            }

            return downloadedFilename;
        }
        internal static long WaitForFileToDownload(string filePath, int downloadTimeoutInSec, long size, out string downloadedFilename)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(downloadTimeoutInSec); // Adjust timeout as needed
            DateTime startTime = DateTime.Now;
            bool fileExists = Directory.GetFiles(Path.GetDirectoryName(filePath), Path.GetFileName(filePath)).Length > 0;
            int lastPrinted = -1;  // so we print at ~30, 20, 10, 0

            // A missing temporary file means the browser renamed it and the download finished. The rename does not give
            // the new name, so use the newest file that arrived and check its size below.
            if (false == fileExists && true == IsTemporaryDownload(filePath))
            {
                string arrived = NewestDownloadSince(Path.GetDirectoryName(filePath), watchArmed);

                if (true == string.IsNullOrEmpty(arrived))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{filePath}] has gone and nothing arrived with it, so the download did not finish.", null, GPALObjectType.None);
                    downloadedFilename = filePath;

                    return -1;
                }

                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{Path.GetFileName(filePath)}] became [{Path.GetFileName(arrived)}], which is what the browser called it.", null, GPALObjectType.None);
                filePath = arrived;
                fileExists = true;
            }

            // Bytes in the folder's partial downloads. While they grow the download is still coming, so the timeout
            // counts from the last growth, not from the start: a slow download is not a failed one.
            long progress = DownloadProgress(Path.GetDirectoryName(filePath), filePath);

            while (false == fileExists && (DateTime.Now - startTime) < timeout)
            {
                TimeSpan remaining = timeout - (DateTime.Now - startTime);
                int remainingSec = (int)Math.Ceiling(remaining.TotalSeconds);  // or (int)remaining.TotalSeconds

                // Print only when crossing a 10-second boundary (30>29, 20>19, etc.)
                if (remainingSec % 10 == 0 && remainingSec != lastPrinted)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Waiting up to [{remainingSec}] sec for [{filePath}].", null, GPALObjectType.None);
                    lastPrinted = remainingSec;     // once per boundary, and not again each time growth restarts the clock
                }
                // Sleep for a short interval before checking again
                System.Threading.Thread.Sleep(1000); // Check every second
                fileExists = Directory.GetFiles(Path.GetDirectoryName(filePath), Path.GetFileName(filePath)).Length > 0;

                long nowAt = DownloadProgress(Path.GetDirectoryName(filePath), filePath);

                if (nowAt > progress)
                {
                    progress = nowAt;
                    startTime = DateTime.Now;
                }

                // The browser may have renamed the file already, so "Unconfirmed 175338" never appears. A new file that
                // arrived while we waited is this download; take it.
                if (false == fileExists)
                {
                    string arrived = NewestDownloadSince(Path.GetDirectoryName(filePath), watchArmed);

                    if (false == string.IsNullOrEmpty(arrived) && false == arrived.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{Path.GetFileName(filePath)}] never showed up, [{Path.GetFileName(arrived)}] did, which is the name the browser gave it.", null, GPALObjectType.None);
                        filePath = arrived;
                        fileExists = true;
                    }
                }
            }

            if (false == fileExists)
            {
                if (false == filePath.EndsWith(".crdownload") && false == filePath.EndsWith(".part"))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"File [{filePath}] never showed up.", null, GPALObjectType.None);
                    downloadedFilename = null;
                }
                else    // if we have a temp file, we actually detected it, so return it, it existed and next step will be to check the real filename
                    downloadedFilename = filePath;

                return -1;
            }

            // we got here in case a wildcard is passed in and we have multiple matching files

            // use case: we expect a file dialog but the link downloads directly, so we have to figure it out post download
            // as it will not download to the correct directory, so find where it downloads to and copy to the user specified directory.
            // NOTE: CAVEAT: this depends upon the user supplied file extension which must match the expected download
            var cutoff = DateTime.Now.AddMinutes(-2);   // NOTE: hardcoded value

            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            IEnumerable<string> filesFound = Directory.EnumerateFiles(
                                 Path.GetDirectoryName(filePath),
                                 Path.GetFileName(filePath));

            // there is more than one file that matches our filePath mask, so a wildcard must be in play
            if (1 < filesFound.Count())
                foreach (var f in filesFound)
                {
                    // we may have just run the auto-update, do not mistake one of these files for our download
                    if (true == f.Equals(GPAL.GPALSettings.ChromeDriverZipFilename) ||
                         true == f.Equals(GPAL.GPALSettings.EdgeDriverFilename) ||
                         true == f.Equals(GPAL.GPALSettings.FirefoxDriverFilename))
                        continue;

                    var t = File.GetLastWriteTime(f);

                    if (t >= cutoff && t > newestTime)
                    {
                        newestTime = t;
                        newest = f;
                    }
                }
            else    // only one file, get it
                newest = filesFound.FirstOrDefault();

            // The file vanished since the check above because the browser renamed it. Return what the not-found path
            // returns, so the caller looks for the new name.
            if (null == newest)
            {
                downloadedFilename = true == IsTemporaryDownload(filePath) ? filePath : null;

                return -1;
            }

            if (true == newest.Equals(GPAL.GPALSettings.ChromeDriverZipFilename) ||
                 true == newest.Equals(GPAL.GPALSettings.EdgeDriverFilename) ||
                 true == newest.Equals(GPAL.GPALSettings.FirefoxDriverFilename))
                newest = null;

                // grab the newest file that showed up in the last 2 minutes. we should be waiting for just one file.
            if (null != newest)
            {
                downloadedFilename = newest;

                // Once the file exists, read its size. Opened for reading and sharing everything: an exclusive open,
                // landing between two of the browser's writes, made the browser's next write fail and left the
                // download stopped part way.
                try
                {
                    using (FileStream fs = File.Open(downloadedFilename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (size != fs.Length)
                            GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"File [{downloadedFilename}] downloaded with file size [{fs.Length}].", null, GPALObjectType.None);
                        return fs.Length;
                    }
                }
                catch
                {
                    try
                    {
                        // The file is locked by a writer or reader. Return its current size; the next sample decides.
                        FileInfo fileInfo = new FileInfo(downloadedFilename);

                        if (fileInfo.Exists)
                        {
                            if (size != fileInfo.Length)
                                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"File [{downloadedFilename}] locked, using FileInfo size [{fileInfo.Length}].", null, GPALObjectType.None);

                            return fileInfo.Length;
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"File [{downloadedFilename}] unable to determine file size.", null, GPALObjectType.None, ex);
                        return -1;
                    }
                }
            }
            else
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"File [{filePath}] not found.", null, GPALObjectType.None);

            downloadedFilename = null;

            return -1;
        }

        /// <summary>
        /// Returns the download directory this browser will actually use, read out of its own configuration
        /// rather than assumed. Routes to the Chrome, Edge or Firefox reader according to the browser type, and
        /// falls back to the Windows Downloads folder for anything else.
        /// </summary>
        /// <param name="browser">Browser whose configured download directory is wanted</param>
        /// <returns>The download directory, ending in a backslash for the Windows fallback</returns>
        public static string GetDefaultDownloadDirectory(IBrowser browser)
        {
            if (BrowserType.Chrome == browser.BrowserType)
                return GetChromeDefaultDownloadDirectory(((Browser.Browser)browser).BrowserSettings.ProfileDataDirectory);
            else if (BrowserType.Edge == browser.BrowserType)
                return GetEdgeDefaultDownloadDirectory(((Browser.Browser)browser).BrowserSettings.ProfileDataDirectory);
            else if (BrowserType.FireFox == browser.BrowserType)
                return GetFirefoxDefaultDownloadDirectory(((Browser.Browser)browser).BrowserSettings.ProfileDataDirectory);

            // Get the default download directory used by windows
            string defaultDownloadDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads\";
            return defaultDownloadDirectory;
        }
        /// <summary>
        /// Reads Chrome's download.default_directory out of the profile's Preferences file. Falls back to the
        /// Windows Downloads folder when the preference was never set explicitly or the file cannot be found.
        /// </summary>
        /// <param name="profilePath">Profile directory to read. The Default profile under Local AppData when null</param>
        /// <returns>The configured download directory, or the Windows Downloads folder</returns>
        public static string GetChromeDefaultDownloadDirectory(string profilePath = null)
        {
            // Default profile path (change "Default" to "Profile 1" etc. if using a different one)
            if (string.IsNullOrEmpty(profilePath))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                profilePath = Path.Combine(localAppData, @"Google\Chrome\User Data\Default");
            }

            string prefsPath = null;
            
            if (false == new DirectoryInfo(profilePath).Name.ToLower().Equals("default"))
                prefsPath = Path.Combine(profilePath, @"default\Preferences");
            else
                prefsPath = Path.Combine(profilePath, @"Preferences");

            if (File.Exists(prefsPath))
            {
                string json = File.ReadAllText(prefsPath);
                JObject prefs = JObject.Parse(json);

                // Navigate to download.default_directory
                var downloadDir = prefs["download"]?["default_directory"]?.ToString();

                if (!string.IsNullOrEmpty(downloadDir))
                    return downloadDir;

                // If not set explicitly, Chrome uses the system default Downloads folder
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"); ;
        }
        /// <summary>
        /// Reads Edge's download.default_directory out of the profile's Preferences file. Edge is Chromium, so the
        /// preference lives in the same place, only under a different vendor directory. Falls back to the Windows
        /// Downloads folder when the preference was never set explicitly or the file cannot be found.
        /// </summary>
        /// <param name="profilePath">Profile directory to read. The Default profile under Local AppData when null</param>
        /// <returns>The configured download directory, or the Windows Downloads folder</returns>
        public static string GetEdgeDefaultDownloadDirectory(string profilePath = null)
        {
            // Default profile path ("Default" folder; change to "Profile 1" etc. if needed)
            if (string.IsNullOrEmpty(profilePath))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                profilePath = Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default");
            }

            string prefsPath = Path.Combine(profilePath, @"default\Preferences");

            if (File.Exists(prefsPath))
            {

                string json = File.ReadAllText(prefsPath);
                JObject prefs = JObject.Parse(json);

                // Read download.default_directory only. savefile.default_directory is the last "Save page as" folder,
                // never where a download goes. Without a download directory, the fallback below returns the Windows
                // Downloads folder.
                var downloadDir = prefs["download"]?["default_directory"]?.ToString();

                if (!string.IsNullOrEmpty(downloadDir))
                    return downloadDir;

                // Fallback: system default Downloads folder
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        /// <summary>
        /// Reads Firefox's browser.download.dir out of the profile's prefs.js. Firefox keeps no JSON preferences
        /// file, so the default profile is found by parsing profiles.ini and the setting is read from prefs.js.
        /// Falls back to the Windows Downloads folder when the preference was never set explicitly.
        /// </summary>
        /// <param name="profilePath">Profile directory to read. Resolved from profiles.ini when null</param>
        /// <returns>The configured download directory, or the Windows Downloads folder</returns>
        public static string GetFirefoxDefaultDownloadDirectory(string profilePath = null)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string firefoxDir = Path.Combine(appData, @"Mozilla\Firefox");
            string profilesIniPath = Path.Combine(firefoxDir, "profiles.ini");

            if (!File.Exists(profilesIniPath))
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Firefox profiles.ini [{profilesIniPath}] not found.");

            // Read and manually parse profiles.ini to find the default profile
            string[] lines = File.ReadAllLines(profilesIniPath);
            string profileDir = null;
            bool isRelative = true;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase))
                {
                    // Reset for new section
                    string currentPath = null;
                    bool currentIsRelative = true;
                    bool isDefault = false;

                    // Look ahead in this section
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string subLine = lines[j].Trim();
                        if (subLine.StartsWith("["))
                            break; // next section

                        if (subLine.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                            currentPath = subLine.Substring(5);
                        else if (subLine.StartsWith("IsRelative=", StringComparison.OrdinalIgnoreCase))
                            currentIsRelative = subLine.Substring(11) == "1";
                        else if (subLine.StartsWith("Default=", StringComparison.OrdinalIgnoreCase))
                            isDefault = subLine.Substring(8) == "1";
                    }

                    // If this is the default profile, use it
                    if (isDefault && currentPath != null)
                    {
                        profileDir = currentPath;
                        isRelative = currentIsRelative;
                        break;
                    }

                    // Otherwise remember the first profile we find as fallback
                    if (profileDir == null && currentPath != null)
                    {
                        profileDir = currentPath;
                        isRelative = currentIsRelative;
                    }
                }
            }

            if (!string.IsNullOrEmpty(profileDir))
            {

                // Resolve full path
                string fullProfilePath = isRelative
                    ? Path.Combine(firefoxDir, profileDir)
                    : profileDir;

                // Allow override
                if (!string.IsNullOrEmpty(profilePath))
                    fullProfilePath = profilePath;

                string prefsJsPath = Path.Combine(fullProfilePath, "prefs.js");

                if (!File.Exists(prefsJsPath))
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"prefs.js [{prefsJsPath}] not found in Firefox profile.");
                else
                {
                    string content = File.ReadAllText(prefsJsPath);

                    // Look for: user_pref("browser.download.dir", "C:\\Path\\To\\Folder");
                    var match = Regex.Match(content, @"user_pref\(""browser\.download\.dir"",\s*""([^""]*)""\);");

                    if (match.Success)
                    {
                        string dir = match.Groups[1].Value;
                        // Handle common escapes (\\ > \)
                        return dir.Replace("\\\\", "\\");
                    }

                    // Not set > fallback to user's Downloads folder
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                }
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>
        /// Load the file(s) definied in the File object and tokenize into a List&lt;GPALGrid&gt;
        /// </summary>
        /// <param name="currentUOW">The unit of work the input file is attached to</param>
        /// <param name="inputFile">The GPAL File definition</param>
        internal static List<IGPALGrid<string>> TokenizeFile(UnitOfWork currentUOW, GPALFile inputFile)
        {
            currentUOW.InputFile = inputFile;
            int rowCount = FileHelper.GetGrid(((IGPALFileInternal)inputFile).FileSettings, out List<IGPALGrid<string>> tokens);
            return tokens;
        }

        /// <summary>
        /// Load the file definied in the File object and tokenize it into the grid
        /// </summary>
        /// <param name="value">The value to collect dictionary keys from; nested keys are joined with a dot</param>
        private static HashSet<string> CollectDictionaryKeys(object value)
        {
            var keys = new HashSet<string>();
            if (value is IList<object> list)
            {
                foreach (var item in list)
                {
                    if (item is IDictionary dynamicDict)
                    {
                        foreach (dynamic entry in dynamicDict)
                        {
                            string key = entry.Key?.ToString();
                            if (key != null)
                            {
                                if (entry.Value is IDictionary nestedDict)
                                {
                                    foreach (string nestedKey in CollectDictionaryKeys(nestedDict))
                                    {
                                        keys.Add($"{key}.{nestedKey}");
                                    }
                                }
                                else
                                {
                                    keys.Add(key);
                                }
                            }
                        }
                    }
                }
            }
            else if (value is IDictionary dynamicDict)
            {
                foreach (dynamic entry in dynamicDict)
                {
                    string key = entry.Key?.ToString();
                    if (key != null)
                    {
                        if (entry.Value is IDictionary nestedDict)
                        {
                            foreach (string nestedKey in CollectDictionaryKeys(nestedDict))
                            {
                                keys.Add($"{key}.{nestedKey}");
                            }
                        }
                        else
                        {
                            keys.Add(key);
                        }
                    }
                }
            }
            return keys;
        }

        internal static object ProcessDictionaryValue(object value, List<List<string>> gridRows = null, HashSet<string> gridColumns = null, string parentKey = null, HashSet<object> visited = null)
        {
            if (value == null)
            {
                if (gridRows != null && gridColumns == null)
                {
                    gridRows.Add(new List<string> { "" });
                }
                return null;
            }

            Type valueType = value.GetType();

            if (visited == null)
                visited = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);

            // Handle nested dictionaries
            if (value is IDictionary dynamicDict)
            {
                // YAML anchors/aliases can produce genuine circular object graphs (e.g. a
                // dictionary that contains itself via a nested list). Without this check
                // such cycles cause unbounded recursion and a StackOverflowException.
                if (!visited.Add(value))
                    return $"Recursion to [{parentKey ?? valueType.Name}]";

                try
                {
                var resultDict = new Dictionary<dynamic, dynamic>();
                var row = new List<string>();

                if (gridColumns != null && gridRows != null)
                {
                    row = new List<string>(new string[gridColumns.Count]);
                    int colIdx = 0;
                    foreach (string col in gridColumns)
                    {
                        string cellValue = "";
                        if (col.Contains("."))
                        {
                            var keyParts = col.Split('.');
                            object current = dynamicDict;
                            bool found = true;
                            foreach (var part in keyParts)
                            {
                                if (current is IDictionary currentDict && currentDict.Contains(part))
                                {
                                    current = currentDict[part];
                                }
                                else
                                {
                                    found = false;
                                    break;
                                }
                            }
                            if (found)
                            {
                                var processedValue = ProcessDictionaryValue(current, null, null, null, visited);
                                cellValue = FlattenValueForGrid(processedValue);
                            }
                        }
                        else if (dynamicDict.Contains(col))
                        {
                            var processedValue = ProcessDictionaryValue(dynamicDict[col], null, null, null, visited);
                            cellValue = FlattenValueForGrid(processedValue);
                        }
                        row[colIdx++] = cellValue;
                    }
                    gridRows.Add(row);
                }

                foreach (dynamic entry in dynamicDict)
                {
                    string key = entry.Key?.ToString();
                    if (key != null)
                    {
                        bool isTabularList = entry.Value is IList<object> list2 && list2.Count() > 0 && list2[0] is IDictionary;
                        HashSet<string> nestedColumns = isTabularList ? CollectDictionaryKeys(entry.Value) : null;
                        var processedValue = ProcessDictionaryValue(entry.Value, isTabularList ? gridRows : null, nestedColumns, key, visited);
                        resultDict[key] = processedValue;
                        if (!isTabularList && gridRows != null && gridColumns == null)
                        {
                            row.Add(FlattenValueForGrid(processedValue));
                        }
                    }
                }

                if (gridRows != null && row.Count > 0 && gridColumns == null)
                {
                    gridRows.Add(row);
                }
                return resultDict;
                }
                finally
                {
                    visited.Remove(value);
                }
            }

            // Handle arrays/lists
            if (value is IList<object> list && !(value is string))
            {
                if (!visited.Add(value))
                    return $"Recursion to [{parentKey ?? valueType.Name}]";

                try
                {
                var resultList = new List<object>();
                string itemKey = string.Empty;

                if (null != parentKey)
                    itemKey = parentKey;
                else
                    itemKey = "item";

                foreach (var item in list)
                {
                    var processedItem = ProcessDictionaryValue(item, gridRows, gridColumns, itemKey, visited);
                    resultList.Add(processedItem);
                }
                return resultList;
                }
                finally
                {
                    visited.Remove(value);
                }
            }

            // Handle simple types
            if (ConverterHelper.IsSimpleType(valueType))
            {
                string stringValue;
                if (valueType == typeof(Guid) && Guid.TryParse(value.ToString(), out var guid))
                {
                    stringValue = guid.ToString();
                    if (gridRows != null && gridColumns == null)
                    {
                        gridRows.Add(new List<string> { stringValue });
                    }
                    return guid;
                }
                if (valueType == typeof(IPAddress) && IPAddress.TryParse(value.ToString(), out var ipAddress))
                {
                    stringValue = ipAddress.ToString();
                    if (gridRows != null && gridColumns == null)
                    {
                        gridRows.Add(new List<string> { stringValue });
                    }
                    return ipAddress;
                }
                if (valueType == typeof(TimeSpan) && TimeSpan.TryParse(value.ToString(), out var timeSpan))
                {
                    stringValue = timeSpan.ToString();
                    if (gridRows != null && gridColumns == null)
                    {
                        gridRows.Add(new List<string> { stringValue });
                    }
                    return timeSpan;
                }
                if (valueType == typeof(Uri))
                {
                    try
                    {
                        var uri = new Uri(value.ToString());
                        stringValue = uri.ToString();
                        if (gridRows != null && gridColumns == null)
                        {
                            gridRows.Add(new List<string> { stringValue });
                        }
                        return uri;
                    }
                    catch
                    {
                        stringValue = value.ToString();
                        if (gridRows != null && gridColumns == null)
                        {
                            gridRows.Add(new List<string> { stringValue });
                        }
                        return stringValue;
                    }
                }
                if (valueType == typeof(System.Numerics.BigInteger))
                {
                    stringValue = value.ToString();
                    if (gridRows != null && gridColumns == null)
                    {
                        gridRows.Add(new List<string> { stringValue });
                    }
                    return stringValue;
                }
                if (valueType == typeof(System.Version))
                {
                    stringValue = value.ToString();
                    if (gridRows != null && gridColumns == null)
                    {
                        gridRows.Add(new List<string> { stringValue });
                    }
                    return stringValue;
                }
                stringValue = value.ToString();
                if (gridRows != null && gridColumns == null)
                {
                    gridRows.Add(new List<string> { stringValue });
                }
                return value;
            }

            // Handle complex objects
            if (ConverterHelper.IsClassType(valueType) && !valueType.IsArray)
            {
                var resultDict = ConverterHelper.ConvertClassToDictionary(value);
                var row = new List<string>();
                foreach (dynamic entry in resultDict)
                {
                    row.Add(FlattenValueForGrid(entry.Value));
                }
                if (gridRows != null && row.Count > 0 && gridColumns == null)
                {
                    gridRows.Add(row);
                }
                return resultDict;
            }

            // Fallback for unknown types
            GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Unknown value type [{valueType}] detected. Converting to string.", value, GPALObjectType.Other);
            string fallbackValue = value.ToString();
            if (gridRows != null && gridColumns == null)
            {
                gridRows.Add(new List<string> { fallbackValue });
            }
            return fallbackValue;
        }

        private static string FlattenValueForGrid(object value)
        {
            if (value == null)
            {
                return "";
            }

            if (value is IDictionary dynamicDict)
            {
                var values = new List<string>();
                foreach (dynamic entry in dynamicDict)
                {
                    values.Add(FlattenValueForGrid(entry.Value));
                }
                return string.Join(",", values);
            }

            if (value is IEnumerable<object> enumerable && !(value is string))
            {
                var values = new List<string>();
                foreach (var item in enumerable)
                {
                    values.Add(FlattenValueForGrid(item));
                }
                return string.Join(",", values);
            }

            return value.ToString();
        }

        /// <summary>
        /// Returns the column names to read this file with: the first file's names with
        /// .WithUseSameColumnNamesForAllFiles, otherwise the names given for this file.
        /// </summary>
        /// <param name="inputFile">The file being tokenized</param>
        /// <param name="fileIdx">Index of the filename being read</param>
        /// <returns>The column names for this file, as the grid the converter expects</returns>
        private static IGPALGrid<string> ColumnNamesForFile(GPALFile inputFile, int fileIdx)
        {
            IGPALGrid<string> columnList = ((IGPALFileInternal)inputFile).FileSettings.ColumnList;
            IGPALGrid<string> retVal = columnList;

            if (false == ((IGPALFileInternal)inputFile).FileSettings.UseOneHeaderForAllFiles
                && 0 < fileIdx
                && fileIdx < columnList.Count())
            {
                retVal = GPAL.GridForType<string>();
                retVal.AddRow(new List<string>(columnList[fileIdx]));
            }

            return retVal;
        }

        internal static bool TokenizeFile(GPALFile inputFile, ConverterSettings myConverterSettings = null)
        {
            var xmlDoc = new XmlDocument();
            int fileIdx = 0;
            StreamReader input = null;
            IGPALGrid<string> outGrid = null;
            List<IGPALGrid<string>> tokens = new List<IGPALGrid<string>>(); ;
            bool retval = false;

            //if (((IGPALFileInternal)inputFile).AlreadyTokenized)
            //{
            //    return true;
            //}

            // Preserve a caller-supplied FirstLineIsColumnHeaders (e.g. via the converter-level
            // WithFirstLineHasColumnNames) as a fallback when the GPALFile itself doesn't specify
            // FirstLineIsColumnNames for this file index.
            bool callerFirstLineIsColumnHeaders = myConverterSettings?.InputFirstLineIsColumnHeaders ?? false;

            // The output format is already known by the time TokenizeFile runs, so for delimited
            // input we can avoid holding a fully-populated dictionary AND a re-derived delimited
            // string AND a grid all in memory at once - only one of those representations is
            // actually consumed downstream depending on the output format.
            DataFormat outDataFormat = myConverterSettings?.OutDataFormat ?? DataFormat.NOTSET;
            bool outputNeedsDictionary = outDataFormat == DataFormat.JSON
                || outDataFormat == DataFormat.XML
                || outDataFormat == DataFormat.YAML
                || outDataFormat == DataFormat.CLASS
                || outDataFormat == DataFormat.DICTIONARY
                || outDataFormat == DataFormat.DATABASE;

            ((IGPALFileInternal)inputFile).FileDictionary.Clear();
            foreach (string filename in inputFile.Filenames)
            {
                DataFormat inDataFormat = ConverterHelper.GetDataFormatFromExtension(filename);
                bool inputIsDelimited = ConverterHelper.IsDelimitedDataFormat(inDataFormat);

                myConverterSettings = new ConverterSettings
                {
                    InDelimiter = ((IGPALFileInternal)inputFile).Delimiter.Count() > fileIdx && ((IGPALFileInternal)inputFile).Delimiter[fileIdx].HasValue
                        ? ((IGPALFileInternal)inputFile).Delimiter[fileIdx].Value
                        : (char?)null,
                    FieldsEnclosedInQuotes = ((IGPALFileInternal)inputFile).FieldsEnclosedInQuotes.Count() > fileIdx
                        ? ((IGPALFileInternal)inputFile).FieldsEnclosedInQuotes[fileIdx]
                        : false,
                    FirstLineIsColumnHeaders = ((IGPALFileInternal)inputFile).FirstLineIsColumnNames.Count() > fileIdx
                        ? ((IGPALFileInternal)inputFile).FirstLineIsColumnNames[fileIdx]
                        : callerFirstLineIsColumnHeaders,
                    IgnoreFirstLineColumnHeaders = ((IGPALFileInternal)inputFile).IgnoreFirstLineColumnNames.Count() > fileIdx
                        ? ((IGPALFileInternal)inputFile).IgnoreFirstLineColumnNames[fileIdx]
                        : false,
                    ColumnNames = inputIsDelimited ? ColumnNamesForFile(inputFile, fileIdx) : new GPALGrid<string>(),
                    ExcelSheetNames = ((IGPALFileInternal)inputFile).FileSettings.ExcelSheetNames,
                    ExcelRowsPerSheet = ((IGPALFileInternal)inputFile).FileSettings.ExcelRowsPerSheet,
                    InputFile = inputFile,
                };
                if (fileIdx < ((IGPALFileInternal)inputFile).FileSettings.Delimiter.Count())
                {
                    ((IGPALFileInternal)inputFile).FileSettings.Delimiter[fileIdx] = ConverterHelper.GetDelimiterFromFormat(inDataFormat);
                }
                else
                {
                    ((IGPALFileInternal)inputFile).FileSettings.Delimiter.Add(ConverterHelper.GetDelimiterFromFormat(inDataFormat));
                }

                try
                {
                    string directory = Path.GetDirectoryName(filename);
                    string filenamePart = Path.GetFileName(filename);
                    foreach (string filename2 in Directory.GetFiles(string.IsNullOrEmpty(directory) ? "." : directory, filenamePart))
                    {
                        using (input = new StreamReader(filename2))
                        {
                            input.BaseStream.Position = 0;

                            // False for a format with no case below, so an unread file is not marked as tokenized.
                            bool formatHandled = true;

                            switch (inDataFormat)
                            {
                                case DataFormat.CARET:
                                case DataFormat.CSV:
                                case DataFormat.PIPE:
                                case DataFormat.PRN:
                                case DataFormat.TAB:
                                    var dictionary = ConverterHelper.ConvertDelimitedToDictionary(myConverterSettings, input, filename2);
                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(dictionary);

                                    // Pass only this file's dictionary. The converter reads entry zero, so the whole
                                    // list would convert the first file every pass.
                                    myConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { dictionary };

                                    if (outputNeedsDictionary)
                                    {
                                        // Output consumes the dictionary directly - skip re-deriving the
                                        // delimited string/grid representations entirely.
                                        ((IGPALFileInternal)inputFile).FileData.Add(string.Empty);
                                        ((IGPALFileInternal)inputFile).FileGrid.Add(GPAL.Grid.ToGPALObject());
                                    }
                                    else
                                    {
                                        dynamic delimitedData = ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid);
                                        ((IGPALFileInternal)inputFile).FileData.Add(delimitedData);
                                        ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);

                                        // The data/grid were derived from the dictionary and the output
                                        // doesn't need the dictionary itself - free its contents now.
                                        dictionary.Clear();
                                    }
                                    break;

                                case DataFormat.LOG:
                                    // Plain text: one column, one row per line, no splitting.
                                    string logText = input.ReadToEnd();
                                    input.BaseStream.Position = 0;

                                    IGPALGrid<string> logGrid = GPAL.GridForType<string>();
                                    var logRecords = new Dictionary<object, dynamic>();
                                    int logLineIdx = 0;

                                    // Drop the trailing newline; it is not an empty line. Inner blank lines are kept.
                                    string logLines = logText.TrimEnd(Environment.NewLine.ToCharArray());

                                    if (false == string.IsNullOrEmpty(logLines))
                                        foreach (string logLine in logLines.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None))
                                        {
                                            logGrid.AddRow(new List<string> { logLine });
                                            logRecords[$"GPALKEY{logLineIdx++:D4}"] = logLine;
                                        }

                                    ((IGPALFileInternal)inputFile).FileData.Add(logText);
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(logGrid);
                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(logRecords);
                                    myConverterSettings.InputDictionary = ((IGPALFileInternal)inputFile).FileDictionary;
                                    break;

                                case DataFormat.JSON:
                                    string fileString = input.ReadToEnd();
                                    input.BaseStream.Position = 0;

                                    dynamic jsonObject = JsonConvert.DeserializeObject<dynamic>(fileString, new JsonConverter[] { new CustomJsonConverter() });

                                    ((IGPALFileInternal)inputFile).FileData.Add(fileString);

                                    var records = new Dictionary<object, dynamic>();
                                    var gridRows = new List<List<string>>();

                                    // Helper to recursively convert any array (IEnumerable<dynamic>) to surrogate-keyed dict
                                    dynamic ConvertArrayToSurrogateDict(IEnumerable<dynamic> array, int startIndex = 0)
                                    {
                                        var surrogateDict = new Dictionary<object, dynamic>();
                                        int idx = startIndex;
                                        foreach (dynamic item in array)
                                        {
                                            surrogateDict[$"GPALKEY{idx:D4}"] = item;
                                            idx++;
                                        }
                                        return surrogateDict;
                                    }

                                    // Recursively walk the structure and replace all arrays with surrogate dicts
                                    dynamic ReplaceArrays(dynamic value)
                                    {
                                        if (value is IEnumerable<dynamic> enumerable && !(value is string))
                                        {
                                            var items = Enumerable.ToList<dynamic>(enumerable);

                                            // A complex-key dictionary is written as an array of {"Key": ..., "Value":
                                            // ...} objects (see NormalizeForCleanJson), the KeyValuePair<K,V>
                                            // convention. Rebuild a real dictionary here; CreateObjectFromDictionary
                                            // handles a nested-dict key. A real object with exactly Key and Value
                                            // properties also matches; accepted.
                                            bool looksLikePairsArray = items.Count > 0 && items.All(it =>
                                                it is IDictionary d && d.Count == 2
                                                && d.Contains("Key") && d.Contains("Value"));
                                            if (looksLikePairsArray)
                                            {
                                                var pairsDict = new Dictionary<object, dynamic>();
                                                foreach (IDictionary pairObj in items)
                                                {
                                                    dynamic pairKey = ReplaceArrays(pairObj["Key"]);
                                                    dynamic pairValue = ReplaceArrays(pairObj["Value"]);
                                                    pairsDict[pairKey] = pairValue;
                                                }
                                                return pairsDict;
                                            }

                                            // It's an array/list
                                            return ConvertArrayToSurrogateDict(items);
                                        }
                                        else if (value is IDictionary<string, dynamic> dict)
                                        {
                                            // It's an object - recurse into values
                                            var newDict = new Dictionary<object, dynamic>();
                                            foreach (var kvp in dict)
                                            {
                                                newDict[kvp.Key] = ReplaceArrays(kvp.Value);
                                            }
                                            return newDict;
                                        }
                                        else
                                        {
                                            // Primitive - return as-is
                                            return value;
                                        }
                                    }

                                    dynamic processedRoot = ReplaceArrays(jsonObject);

                                    // If top-level is array, wrap as surrogate dict for FileDictionary
                                    if (processedRoot is IDictionary && ConverterHelper.IsArrayLike((IDictionary)processedRoot))
                                    {
                                        records = (Dictionary<object, dynamic>)processedRoot;
                                    }
                                    else
                                    {
                                        // Single object or other - wrap under a key or just store
                                        records["root"] = processedRoot;
                                    }

                                    // Grid building remains the same: iterate top-level records (now surrogate-keyed)
                                    foreach (KeyValuePair<object, dynamic> pair in records.OrderBy(p => p.Key.ToString()))
                                    {
                                        if (pair.Value is IDictionary rowDict)
                                        {
                                            var row = new List<string>();
                                            foreach (dynamic kvp in rowDict)
                                            {
                                                if (kvp.Key != null)
                                                {
                                                    string key = kvp.Key.ToString();
                                                    if (key.StartsWith("GPALKEY")) continue; // skip surrogates in grid
                                                    var processedValue = ProcessDictionaryValue(kvp.Value, null, null, key);
                                                    if (!(kvp.Value is IDictionary) && !(kvp.Value is IList<object>))
                                                    {
                                                        row.Add(FlattenValueForGrid(processedValue));
                                                    }
                                                }
                                            }
                                            if (row.Count > 0)
                                            {
                                                gridRows.Add(row);
                                            }
                                        }
                                    }

                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(records);

                                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"JSON FileDictionary count: [{records.Count}]", null, GPALObjectType.Other);

                                    var gridRow = GPAL.Grid.ToGPALObject();
                                    foreach (var row in gridRows)
                                    {
                                        gridRow.AddRow(row);
                                    }

                                    myConverterSettings.InputDictionary = ((IGPALFileInternal)inputFile).FileDictionary;
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(gridRow);
                                    break;

                                case DataFormat.XLSX:
                                    IGPALGrid<string> grid = ExcelHelper.TokenizeExcelFile(filename2);
                                    List<string> colNames = new List<string>();
                                    List<Dictionary<object, dynamic>> returnList = new List<Dictionary<object, dynamic>>();

                                    ((IGPALFileInternal)inputFile).FileGrid.Add((IGPALGrid<string>)grid.Clone());

                                    // find out how many worksheets we have and find out the number of rows to ascertain when the grid data is another sheet with different column names
                                    using (var workbook = new XLWorkbook(filename))
                                    {
                                        List<int> rowsPerSheet = new List<int>();
                                        List<string> sheetName = new List<string>();

                                        foreach (IXLWorksheet worksheet in workbook.Worksheets)
                                        {
                                            int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                                            // If there are blank rows after the last used row, use the sheet's defined range
                                            int totalRows = Math.Max(lastRowNumber, worksheet.RangeUsed()?.LastRow().RowNumber() ?? 0);
                                            // Or even simpler – just take the maximum row index that exists:
                                            totalRows = worksheet.Rows().LastOrDefault()?.RowNumber() ?? 0;

                                            rowsPerSheet.Add(totalRows);
                                            sheetName.Add(worksheet.Name);
                                        }

                                        ((IGPALFileInternal)inputFile).FileSettings.ExcelRowsPerSheet.AddRow(rowsPerSheet);
                                        ((IGPALFileInternal)inputFile).FileSettings.ExcelSheetNames.AddRow(sheetName);

                                        int columnHeaders = 0;
                                        foreach (int rowCnt in rowsPerSheet)
                                        {
                                            if (myConverterSettings.FirstLineIsColumnHeaders)
                                            {
                                                colNames = grid[columnHeaders];
                                            }
                                            else
                                            {
                                                int colIdx = 0;
                                                foreach (string columnName in grid[columnHeaders].Select(v => (string)v))
                                                {
                                                    colNames.Add($"Column{colIdx++}");
                                                }
                                            }

                                            myConverterSettings.ColumnNames.AddRow(colNames);

                                            if (0 == ((IGPALFileInternal)inputFile).FileSettings.ColumnList.Count())
                                                ((IGPALFileInternal)inputFile).FileSettings.ColumnList.AddRow(colNames);

                                            int rowIdx = 0;

                                            foreach (Dictionary<object, dynamic> row in ConverterHelper.ConvertGridToDictionary(colNames, grid, columnHeaders, rowCnt))
                                            {
                                                if (true == myConverterSettings.FirstLineIsColumnHeaders && 0 == rowIdx++)
                                                    continue;

                                                returnList.Add(row);
                                            }

                                            columnHeaders += rowCnt;
                                        }

                                        // now delete the column headers if denoted in settings
                                        columnHeaders = 0;
                                        int iteration = 0;
                                        foreach (int rowCnt in rowsPerSheet)
                                        {
                                            if (myConverterSettings.FirstLineIsColumnHeaders)
                                            {
                                                grid.DeleteRow(0);
                                            }
                                            columnHeaders = rowCnt - iteration++;   // we have one less row than we counted in the grid for each iteration
                                        }
                                    }

                                    ((IGPALFileInternal)inputFile).FileDictionary = returnList;
                                    myConverterSettings.InputDictionary = ((IGPALFileInternal)inputFile).FileDictionary;
                                    break;

                                case DataFormat.XML:
                                    xmlDoc.PreserveWhitespace = true;
                                    xmlDoc.Load(input);
                                    input.BaseStream.Position = 0;
                                    var xmlDictionary = ConverterHelper.ConvertXMLToDictionary(xmlDoc);
                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(xmlDictionary);
                                    myConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { xmlDictionary };
                                    ((IGPALFileInternal)inputFile).FileData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid));
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);
                                    break;

                                case DataFormat.YAML:
                                    var deserializer = new DeserializerBuilder()
                                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                                        .Build();

                                    string yamlText = input.ReadToEnd();
                                    input.BaseStream.Position = 0;
                                    dynamic yamlObject = deserializer.Deserialize<object>(yamlText);
                                    ((IGPALFileInternal)inputFile).FileData.Add(yamlText);

                                    ((IGPALFileInternal)inputFile).FileDictionary.Clear();
                                    ((IGPALFileInternal)inputFile).FileGrid.Clear();

                                    var yamlGridRows = new List<List<string>>();
                                    var processedYamlDict = new Dictionary<dynamic, dynamic>();

                                    if (yamlObject is IDictionary yamlMapping)
                                    {
                                        foreach (var key in yamlMapping.Keys)
                                        {
                                            dynamic processedKey = (key is IDictionary || key is IList)
                                                ? ProcessDictionaryValue(key, yamlGridRows)
                                                : (key?.ToString() ?? "null_key");
                                            processedYamlDict[processedKey] = ProcessDictionaryValue(yamlMapping[key], yamlGridRows);
                                        }
                                    }
                                    else if (yamlObject is IList yamlSequence)
                                    {
                                        // Root is a YAML sequence, not a mapping - synthesize row keys
                                        // (same GPALKEYN convention used for headerless CSV rows).
                                        int rootIdx = 0;
                                        foreach (var item in yamlSequence)
                                        {
                                            processedYamlDict[$"GPALKEY{rootIdx++:D4}"] = ProcessDictionaryValue(item, yamlGridRows);
                                        }
                                    }
                                    else
                                    {
                                        // Root is a bare YAML scalar.
                                        processedYamlDict["GPALKEY0000"] = ProcessDictionaryValue(yamlObject, yamlGridRows);
                                    }

                                    var yamlGridRow = GPAL.Grid.ToGPALObject();
                                    foreach (var row in yamlGridRows)
                                    {
                                        yamlGridRow.AddRow(row);
                                    }

                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(processedYamlDict);
                                    myConverterSettings.InputDictionary = ((IGPALFileInternal)inputFile).FileDictionary;
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(yamlGridRow);
                                    break;

                                case DataFormat.HTML:
                                    ((IGPALFileInternal)inputFile).FileDictionary = ConverterHelper.ConvertHtmlToDictionary(filename2);
                                    myConverterSettings.InputDictionary = ((IGPALFileInternal)inputFile).FileDictionary;
                                    dynamic inData = ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid);
                                    ((IGPALFileInternal)inputFile).FileData.Add(inData);
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);
                                    break;

                                default:
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{inDataFormat}] is not a format GPAL reads, so nothing was tokenized from [{filename}]", inputFile, GPALObjectType.GPALFile);
                                    formatHandled = false;
                                    break;
                            }

                            // True when at least one filename was read. An unreadable last file does not undo the files
                            // before it.
                            retval = retval || formatHandled;
                        }
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Problem tokenizing file [{filename}]. Continuing.", inputFile, GPALObjectType.GPALFile, ex);
                }
                fileIdx++;
            }

            if (0 == ((IGPALFileInternal)inputFile).FileSettings.FileGrid.Count)
                FileHelper.GetGrid(((IGPALFileInternal)inputFile).FileSettings, out tokens);
            else
                tokens = ((IGPALFileInternal)inputFile).FileSettings.FileGrid;

            ((IGPALFileInternal)inputFile).TokenList = tokens;
            ((IGPALFileInternal)inputFile).AlreadyTokenized = retval;

            return retval;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        /// <summary>
        /// Returns the 8.3 short form of a path, for the places that still cannot cope with spaces or a long path.
        /// The path has to exist, since Windows derives the short name from the filesystem. Hands back the original
        /// path unchanged when there is no short form.
        /// </summary>
        /// <param name="longPath">Path to shorten</param>
        /// <returns>The short path, or <paramref name="longPath"/> when Windows did not supply one</returns>
        public static string GetShortPath(string longPath)
        {
            var sb = new StringBuilder(1024);
            int result = GetShortPathName(longPath, sb, sb.Capacity);
            return result > 0 ? sb.ToString() : longPath;
        }

        /// <summary>
        /// True when the text names something to fetch over the web rather than a path on disk.<br/>
        /// Checked before wildcard expansion, because a query string's "?" would otherwise be read as a wildcard.
        /// </summary>
        /// <param name="fileName">Filename or url to test</param>
        /// <returns>True for an absolute http or https url</returns>
        internal static bool IsWebAddress(string fileName)
        {
            return true == Uri.TryCreate(fileName, UriKind.Absolute, out Uri uri)
                && (Uri.UriSchemeHttp == uri.Scheme || Uri.UriSchemeHttps == uri.Scheme);
        }


        /// <summary>
        /// Downloads a url through the browser, for a file behind a login or anti-bot check.<br/>
        /// <see cref="FetchToFile"/> first, from inside the page with no navigation or prompt; if the page cannot
        /// return the file, <see cref="DownloadInTab"/>.
        /// </summary>
        /// <param name="url">Absolute http or https url to get</param>
        /// <param name="localPath">Where the file should end up</param>
        /// <param name="browser">Browser whose session can reach the url</param>
        /// <returns>True when the file arrived, or when the copy already on disk is current</returns>
        internal static bool FetchOrDownloadInTab(string url, string localPath, IBrowser browser)
        {
            UnitOfWork safeUOW = ((Browser.Browser)browser).CurrentUOW;

            url = UpgradeToHttps(url, browser);

            bool gotIt = FetchToFile(url, localPath, browser);

            // A rendered page is saved by reading its source, as right-click Save As does. No download, nothing to wait
            // for. A url the server plainly calls a file is not opened in a tab to find out: Edge asks Save As for a
            // download started in a tab of its own, and a dialog on a hidden desktop has no one to answer it.
            if (false == gotIt && false == ServedAsFile(url, browser))
                gotIt = SaveSourceInTab(url, localPath, browser);

            // changes the current unit of work
            if (false == gotIt)
                gotIt = DownloadInTab(url, localPath, browser);

            ((Browser.Browser)browser).CurrentUOW = safeUOW;

            return gotIt;
        }

        // How long the https version of an http url is given to answer before the http one is used
        private const int HttpsUpgradeProbeMs = 5_000;

        /// <summary>
        /// The https version of an http url when the site answers it, as browsers upgrade a navigation. A secure page
        /// may not fetch an http url (mixed content), and browsers hold or block files downloaded over http, so the
        /// secure address is the one that works when there is one. Any answer counts, an error status included; only
        /// a connection that fails or times out keeps http.
        /// </summary>
        /// <param name="url">The url to get</param>
        /// <param name="browser">The browser the file is for, whose user agent the request carries</param>
        /// <returns>The https url when the site answers it, otherwise the url as given</returns>
        internal static string UpgradeToHttps(string url, IBrowser browser)
        {
            string retVal = url;

            if (true == Uri.TryCreate(url, UriKind.Absolute, out Uri asked) && Uri.UriSchemeHttp == asked.Scheme)
            {
                UriBuilder secure = new UriBuilder(asked) { Scheme = Uri.UriSchemeHttps, Port = true == asked.IsDefaultPort ? -1 : asked.Port };
                bool answered = false;

                using (System.Net.Http.HttpClient probe = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(HttpsUpgradeProbeMs) })
                using (System.Net.Http.HttpRequestMessage head = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, secure.Uri))
                {
                    // as the browser, since some servers refuse a request that names no user agent
                    head.Headers.TryAddWithoutValidation("User-Agent", BrowserHelper.GetUserAgent(browser));

                    // No answer over https is what this asks about, so a failed connection is an answer, not an error
                    try
                    {
                        using (probe.SendAsync(head).GetAwaiter().GetResult())
                            answered = true;
                    }
                    catch (System.Net.Http.HttpRequestException)
                    {
                    }
                    catch (System.Threading.Tasks.TaskCanceledException)
                    {
                    }
                }

                if (true == answered)
                {
                    retVal = secure.Uri.AbsoluteUri;
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] is also served over https, so [{retVal}] is used", null, GPALObjectType.Other);
                }
                else
                    GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] does not answer over https, so it is fetched over plain http", null, GPALObjectType.Other);
            }

            return retVal;
        }

        /// <summary>
        /// Whether the server plainly calls the url a file: sent as an attachment, or as a type other than html. Asked
        /// with HEAD, following redirects, and without the browser's session, so a file behind a login answers with
        /// its login page and is not called one. No answer is not a file either; the tab finds out.
        /// </summary>
        /// <param name="url">The url to ask about</param>
        /// <param name="browser">The browser the file is for, whose user agent the request carries</param>
        /// <returns>True when the server says it is a file</returns>
        internal static bool ServedAsFile(string url, IBrowser browser)
        {
            bool retVal = false;

            using (System.Net.Http.HttpClient probe = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(HttpsUpgradeProbeMs) })
            using (System.Net.Http.HttpRequestMessage head = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url))
            {
                // as the browser: some servers refuse a request that names no user agent (thinkbroadband answers 403)
                head.Headers.TryAddWithoutValidation("User-Agent", BrowserHelper.GetUserAgent(browser));

                // A server that will not answer HEAD leaves the question to the tab, so a failure is an answer
                try
                {
                    using (System.Net.Http.HttpResponseMessage answer = probe.SendAsync(head).GetAwaiter().GetResult())
                    {
                        string type = answer.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
                        bool attachment = true == string.Equals("attachment", answer.Content?.Headers?.ContentDisposition?.DispositionType, StringComparison.OrdinalIgnoreCase);

                        retVal = true == answer.IsSuccessStatusCode
                              && (true == attachment || (0 < type.Length && false == type.Contains("html")));

                        if (true == retVal)
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] is served as a file ([{(true == attachment ? "attachment" : type)}]), so it is downloaded without opening it as a page", null, GPALObjectType.Other);
                    }
                }
                catch (System.Net.Http.HttpRequestException)
                {
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                }
            }

            return retVal;
        }

        /// <summary>
        /// Opens the url in a new tab and writes the rendered page source to <paramref name="localPath"/>, like
        /// right-click Save As.<br/>
        /// If the url downloads a file instead, the browser closes the tab, GoTo returns to the previous tab, and this
        /// writes nothing and publishes that. The tab's existence tells a page from a file; the status code does not
        /// (Selenium leaves it at -1, Puppeteer uses LastNavigationError).
        /// </summary>
        /// <param name="url">Absolute http or https url to save</param>
        /// <param name="localPath">Where the page source should be written</param>
        /// <param name="browser">Browser whose session can reach the url</param>
        /// <returns>True when a rendered page was written</returns>
        internal static bool SaveSourceInTab(string url, string localPath, IBrowser browser)
        {
            bool saved = false;
            Browser.Browser gpalBrowser = (Browser.Browser)browser;
            int tabsBefore = gpalBrowser.tabsWeOpened;
            int realTabsBefore = gpalBrowser.RealTabCount();
            string wasOn = browser.CurrentUrl;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Saving [{url}] as a page to [{localPath}].", browser, GPALObjectType.Browser);

            browser.NewTab(url);

            // A link that is a file never becomes a page in the tab. The engine reports the navigation failed
            // (Puppeteer's net::ERR_ABORTED), or the browser closes the tab it opened for it, so the real tab count is
            // back where it was. Either way the tab underneath is not to be read as it. A count of zero is an engine
            // that cannot count, which says nothing.
            int realTabsAfter = gpalBrowser.RealTabCount();
            bool tabClosedItself = 0 < realTabsBefore && 0 < realTabsAfter && realTabsAfter <= realTabsBefore;
            bool becameDownload = null != gpalBrowser.NavigationFailedAt || true == tabClosedItself;

            // Close the tab only if it is still open; otherwise this would close the workflow's tab.
            bool tabIsOurs = tabsBefore < gpalBrowser.tabsWeOpened && false == tabClosedItself;

            // The tab is the page asked for only when it holds an html document at a web address. A link that is a
            // file can leave the tab blank (about:blank) while the browser downloads it, and a failed one leaves the
            // browser's error page (chrome-error:, about:neterror), and neither is the file. Null when the engine
            // cannot run the script, and then the source is all there is to go on.
            string shown = true == tabIsOurs && false == becameDownload
                         ? Browser.ElementHelper.RunPageScript(gpalBrowser, "document.contentType + ' ' + document.URL")
                         : null;
            string[] typeAndUrl = (shown ?? string.Empty).Trim().Trim('"').Split(new[] { ' ' }, 2);
            bool isWebPage = 2 == typeAndUrl.Length
                          && true == typeAndUrl[0].Contains("html")
                          && true == Uri.TryCreate(typeAndUrl[1], UriKind.Absolute, out Uri at)
                          && (Uri.UriSchemeHttp == at.Scheme || Uri.UriSchemeHttps == at.Scheme);

            string pageSource = string.Empty;

            if (true == tabIsOurs && false == becameDownload && (null == shown || true == isWebPage))
                browser.GetPageSource(out pageSource);

            if (true == becameDownload)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] did not open as a page ({(true == tabClosedItself ? "the browser closed the tab it opened for it" : "the browser reported the navigation failed")}), so it is downloaded instead.", browser, GPALObjectType.Browser);
            else if (false == tabIsOurs)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] is not a page the browser renders, so there is no source to save.", browser, GPALObjectType.Browser);
            else if (null != shown && false == isWebPage)
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{url}] did not open as a page (the tab holds [{shown.Trim().Trim('"')}]), so it is downloaded instead.", browser, GPALObjectType.Browser);
            else if (true == string.IsNullOrEmpty(pageSource))
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url}] rendered nothing to save as [{localPath}].", browser, GPALObjectType.Browser);
            else
            {
                FileHelper.EnsureFolderFor(localPath);
                File.WriteAllText(localPath, pageSource);
                saved = true;
            }

            if (true == tabIsOurs)
            {
                browser.CloseTab();
                gpalBrowser.CurrentUrl = wasOn;
            }

            return saved;
        }

        /// <summary>
        /// Returns the seconds of inactivity allowed before giving up on a download. The configured timeout covers the
        /// start; once the temporary file exists, the longer allowance applies.
        /// </summary>
        /// <param name="downloadDirectory">Directory the browser was told to download into</param>
        /// <param name="browserSettings">Settings holding the configured timeout</param>
        /// <returns>Seconds with no activity to allow</returns>
        internal static int AllowanceFor(string downloadDirectory, Browser.BrowserSettings browserSettings)
        {
            int retVal = true == IsFinalizing(downloadDirectory, watchArmed)
                       ? browserSettings.DownloadTimeoutInSec * FinalizeBudgetMultiplier
                       : browserSettings.DownloadTimeoutInSec;

            return retVal;
        }

        /// <summary>
        /// Deletes temporary download files this download started and never finished.<br/>
        /// An abandoned .crdownload stays, and the next download is named around it: samplefile.pdf (1), then (2).
        /// Windows gives a recreated name the original's creation time, so a new temporary file can look like an
        /// earlier download's.<br/>
        /// Only files the watcher saw appear during this download are deleted, and only after the caller has given up.
        /// </summary>
        /// <param name="browserSettings">Settings for the browser whose download failed</param>
        internal static void DiscardAbandonedDownload(Browser.BrowserSettings browserSettings)
        {
            foreach (string scratch in watchedScratchFiles)
                try
                {
                    if (true == File.Exists(scratch))
                    {
                        File.Delete(scratch);
                        GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{Path.GetFileName(scratch)}] was left behind by a download that never finished, removed so it does not name the next one around it", browserSettings.Browser, GPALObjectType.Browser);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not remove [{scratch}]", browserSettings.Browser, GPALObjectType.Browser, ex);
                }

            watchedScratchFiles.Clear();
        }

        internal const int FinalizeBudgetMultiplier = 6;   // the browser's own finish-up is slower than a transfer and is not ours to hurry

        /// <summary>
        /// True while the browser still has a temporary file for this download. After the transfer, scanning and
        /// renaming a large file can outlast the start timeout, so it gets a longer allowance. Temporary files older
        /// than <paramref name="since"/> belong to an earlier download and are ignored.
        /// </summary>
        /// <param name="downloadDirectory">Directory the browser was told to download into</param>
        /// <param name="since">When the watcher was started</param>
        /// <returns>True while the browser is still finishing its temporary file</returns>
        internal static bool IsFinalizing(string downloadDirectory, DateTime since)
        {
            bool retVal = false;

            try
            {
                if (true == Directory.Exists(downloadDirectory))
                    // Use the later of creation and write time, as NewestDownloadSince does. Windows gives a recreated
                    // name the original's creation time, so by creation time alone a new temporary file looks old.
                    retVal = Directory.EnumerateFiles(downloadDirectory)
                                      .Any(file => true == IsTemporaryDownload(file)
                                                && (File.GetCreationTime(file) >= since || File.GetLastWriteTime(file) >= since));
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not look for a download being finished in [{downloadDirectory}]", null, GPALObjectType.None, ex);
            }

            return retVal;
        }

        internal static long DownloadProgress(string downloadDirectory, string localPath)
        {
            long retVal = -1;

            try
            {
                if (true == File.Exists(localPath))
                    retVal = new FileInfo(localPath).Length;

                if (true == Directory.Exists(downloadDirectory))
                    foreach (string scratch in Directory.EnumerateFiles(downloadDirectory).Where(f => true == IsTemporaryDownload(f)))
                        retVal = (0 > retVal ? 0 : retVal) + new FileInfo(scratch).Length;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Could not measure the download in [{downloadDirectory}]", null, GPALObjectType.None, ex);
            }

            return retVal;
        }
        /// <summary>
        /// Has the browser download a url normally, for a file the page cannot return.<br/>
        /// The workflow's own tab goes to the url, as a person's click on the link would: a file starts a download
        /// without leaving the page. A tab opened just for it would make Edge ask Save As whatever its setting, and a
        /// dialog on a hidden desktop has no one to answer it.<br/>
        /// The file is saved to the browser's download folder, then moved to <paramref name="localPath"/>.
        /// </summary>
        /// <param name="url">Absolute http or https url to download</param>
        /// <param name="localPath">Where the downloaded file should end up</param>
        /// <param name="browser">Browser whose session can reach the url</param>
        /// <returns>True when the file arrived</returns>
        internal static bool DownloadInTab(string url, string localPath, IBrowser browser)
        {
            Browser.BrowserSettings browserSettings = ((Browser.Browser)browser).BrowserSettings;

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloading [{url}] with the browser to [{localPath}].", browser, GPALObjectType.Browser);

            // The browser picks the download folder, so the file is moved to localPath afterwards. Fetch writes
            // straight to localPath.
            string downloadDirectory = GetDefaultDownloadDirectory(browser);
            string landsAt = Path.Combine(downloadDirectory, Path.GetFileName(localPath));

            // ask for the filename we want. each engine takes it differently: the extension suggests a name
            // relative to the download directory, puppeteer takes the directory, selenium takes the full path
            if (true == browserSettings.UseOttoMagic)
                browserSettings.MagicHelper.WithDownloadFile(Path.GetFileName(localPath));
            else if (true == browserSettings.UsePuppeteer)
                browserSettings.PuppeteerClient.SetDownloadFilename(downloadDirectory).Execute();   // DownloadTo only adds to another request
            else
                BrowserHelper.SetHeadlessDownload(browserSettings, landsAt);

            // SetupDownloadWatcher is what leftclickanddownload uses. it watches the directory, waits for the
            // .crdownload or .part to stop growing, moves the finished file to landsAt and sets FileDownloaded
            browserSettings.FileDownloaded = null;
            SetupDownloadWatcher(landsAt, browserSettings);

            string wasOn = browser.CurrentUrl;

            // The engine goes to the url itself, not GoTo: a file never becomes a page, so GoTo would wait for one
            // and report the navigation failed, and a CallOnFail handler would run for a download that worked.
            // Whether the file arrived is the watcher's to say.
            if (true == browserSettings.UseOttoMagic)
                browserSettings.MagicHelper.GoTo(url);
            else if (true == browserSettings.UsePuppeteer)
                browserSettings.PuppeteerCommunicator.StartDownload(url).GetAwaiter().GetResult();
            else
                BrowserHelper.SeleniumStartDownload(url, browserSettings);

            Stopwatch waitingToStart = Stopwatch.StartNew();

            // FileDownloaded stays null until the watcher sets it true or false.
            // DownloadTimeoutInSec is how long to wait for the download to start, not to finish, so stop the
            // clock once a file shows up in the download directory and let the watcher take as long as it needs
            long lastSeen = long.MinValue;

            while (null == browserSettings.FileDownloaded || false == browserSettings.FileDownloaded)
            {
                long progress = DownloadProgress(downloadDirectory, landsAt);

                if (progress != lastSeen)       // it moved, so it is alive and gets the whole budget again
                {
                    lastSeen = progress;
                    waitingToStart.Restart();
                }
                else if (waitingToStart.Elapsed.TotalSeconds > AllowanceFor(downloadDirectory, browserSettings))
                    break;                      // nothing has happened for the whole allowance: it never started, or it died

                Thread.Sleep(100);
            }

            // The watcher misses a download that finished before it saw it start. The file at its name, written since
            // the watch began, with no temporary download left beside it, is a finished download.
            if (true != browserSettings.FileDownloaded
                && true == File.Exists(landsAt)
                && watchArmed <= File.GetLastWriteTime(landsAt)
                && false == Directory.EnumerateFiles(downloadDirectory).Any(scratch => true == IsTemporaryDownload(scratch)))
                browserSettings.FileDownloaded = true;

            // Done waiting, found or not. The watcher stops here, or the next file to land in the download folder,
            // whatever it is, is taken for this one and renamed.
            if (null != watcher)
            {
                watcher.Dispose();
                watcher = null;
            }

            // A download does not leave the page. Reading the tab's url puts GPAL's idea of the page back on it, and
            // if the tab did go somewhere after all, it goes back.
            if (false == UrlHelper.AreEquivalent(((Browser.Browser)browser).GetSetCurrentUrl(), wasOn))
                browser.GoTo(wasOn);

            bool downloaded = true == browserSettings.FileDownloaded;

            // move it out of the download directory into the working directory, where WithFileName said the file
            // would be. everything downstream, CopyTo and the converter included, is looking at localPath
            if (true == downloaded && false == landsAt.Equals(localPath, StringComparison.OrdinalIgnoreCase))
            {
                if (true == File.Exists(localPath))
                    File.Delete(localPath);

                FileHelper.EnsureFolderFor(localPath);
                File.Move(landsAt, localPath);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{landsAt}] moved to [{localPath}]", browser, GPALObjectType.Browser);
            }

            // A file over plain http is one browsers hold for approval or block as an insecure download, so say so
            bool overHttp = true == Uri.TryCreate(url, UriKind.Absolute, out Uri asked) && Uri.UriSchemeHttp == asked.Scheme;

            if (false == downloaded)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"[{url}] did not download to [{downloadDirectory}] within [{browserSettings.DownloadTimeoutInSec}] seconds.{(true == overHttp ? " It is served over plain http, which browsers hold or block as an insecure download." : string.Empty)}", browser, GPALObjectType.Browser);

            return downloaded;
        }
        /// <summary>
        /// Returns a message when the url's origin differs from the page's, the usual reason an in-page fetch fails.
        /// Most file hosts send no Access-Control-Allow-Origin, and the browser reports only "Failed to fetch".
        /// </summary>
        /// <param name="url">Url that was requested</param>
        /// <param name="browser">Browser whose page sent the request</param>
        /// <returns>A sentence naming both origins; empty when they match or cannot be read</returns>
        private static string CrossOriginNote(string url, IBrowser browser)
        {
            string retVal = "";
            string pageUrl = ((Browser.Browser)browser).BrowserSettings.CurrentURL;

            if (true == Uri.TryCreate(url, UriKind.Absolute, out Uri wanted)
                && true == Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri onNow)
                && false == string.Equals(wanted.GetLeftPart(UriPartial.Authority), onNow.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
                retVal = $" The page is on [{onNow.GetLeftPart(UriPartial.Authority)}] and the file is on [{wanted.GetLeftPart(UriPartial.Authority)}], so the fetch was cross origin and only works when that server allows it.";

            return retVal;
        }

        /// <summary>
        /// Fetches a url from inside the page and writes the response to <paramref name="localPath"/>.<br/>
        /// Uses the browser's connection: cookies, TLS fingerprint, header order and anti-bot clearance. No navigation,
        /// tab or prompt, and the file goes to our path, not the download folder. Same on all three engines.<br/>
        /// Only-if-changed works as in <see cref="DownloadToFile"/>.
        /// </summary>
        /// <param name="url">Absolute http or https url to fetch</param>
        /// <param name="localPath">Where the fetched file should end up</param>
        /// <param name="browser">Browser whose session can reach the url</param>
        /// <returns>True when the file was written, or when the copy already on disk is current</returns>
        internal static bool FetchToFile(string url, string localPath, IBrowser browser)
        {
            bool downloaded = false;
            string[] headers = true == File.Exists(localPath)
                ? new string[] { "If-Modified-Since", File.GetLastWriteTimeUtc(localPath).ToString("R") }
                : null;

            byte[] bytes = BrowserHelper.FetchBytes((Browser.Browser)browser, url, headers, out string lastModified);

            if (304 == browser.ServerResponseCode)
            {
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{url}] has not changed, using [{localPath}].", browser, GPALObjectType.Browser);
                downloaded = true;
            }
            else if (null == bytes)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{url}] returned no content to save as [{localPath}], HTTP [{browser.ServerResponseCode}].{CrossOriginNote(url, browser)}", browser, GPALObjectType.Browser);
            else if (200 > browser.ServerResponseCode || 300 <= browser.ServerResponseCode)
                // Do not save the body of a refused request; it would look like the downloaded file.
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{url}] returned HTTP [{browser.ServerResponseCode}], nothing saved to [{localPath}].", browser, GPALObjectType.Browser);
            else if (0 == bytes.Length)
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{url}] returned an empty file, nothing saved to [{localPath}].", browser, GPALObjectType.Browser);
            else
            {
                FileHelper.EnsureFolderFor(localPath);
                File.WriteAllBytes(localPath, bytes);

                // Stamp the file with the server's Last-Modified so the next "changed since" check uses the server's
                // date.
                if (true == DateTime.TryParse(lastModified, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime modified))
                    File.SetLastWriteTimeUtc(localPath, modified);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"Downloaded [{url}] with the browser to [{localPath}].", browser, GPALObjectType.Browser);

                downloaded = true;
            }

            return downloaded;
        }

        /// <summary>
        /// Makes the folder a file is about to be written into, and any folders above it, when they are not there
        /// yet. Every save GPAL makes to a path a workflow gave it calls this first, so a workflow can name a file
        /// in folders that do not exist and the save makes them.
        /// </summary>
        /// <param name="filePath">The file about to be written</param>
        internal static void EnsureFolderFor(string filePath)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(filePath));

            if (false == string.IsNullOrEmpty(folder) && false == Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{folder}] created");
            }
        }
        /// <summary>
        /// Adds a trailing backslash to a directory path when it does not already have one, so the path can be
        /// concatenated without producing a double separator or gluing the directory to the filename.
        /// </summary>
        /// <param name="directoryPath">Directory path to check</param>
        /// <returns>The path, guaranteed to end in a backslash</returns>
        public static string EnsureDirectoryEndsWithBackslash(string directoryPath)
        {
            // Check if the directory path already ends with '\'
            if (!directoryPath.EndsWith("\\"))
            {
                // If not, add '\'
                directoryPath += "\\";
            }
            return directoryPath;
        }

    }
}

