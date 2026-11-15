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
        // every time). We publish a given message once, then emit a single "Suppressing repeat messages" notice.
        private static readonly HashSet<string> _loggedMessages = new HashSet<string>();
        private static bool _suppressNoticeShown = false;

        /// <summary>
        /// Publishes <paramref name="message"/> only the first time it is seen; on any later repeat it emits a
        /// single "Suppressing repeat messages" notice and then stays silent, mirroring the app-side helper.
        /// </summary>
        private static void PublishSuppressingRepeats(GPALEventType type, string message, object source, GPALObjectType objectType)
        {
            if (true == _loggedMessages.Add(message))
                GPAL.PublishSimpleEvent(type, message, source, objectType);
            else if (false == _suppressNoticeShown)
            {
                GPAL.PublishSimpleEvent(GPALEventType.INFO, "Suppressing repeat messages", source, objectType);
                _suppressNoticeShown = true;
            }
        }
        /// <summary>
        /// A file's own entry of a per-file setting, or the value for none when its unit gave none.
        /// </summary>
        /// <param name="settings">The setting's entries, one per file</param>
        /// <param name="fileIdx">The file's index in the GPALFile</param>
        /// <param name="noSetting">The value when no entry was given</param>
        private static bool SettingForFile(List<bool> settings, int fileIdx, bool noSetting)
        {
            return fileIdx < settings.Count ? settings[fileIdx] : noSetting;
        }
        /// <summary>
        /// A file's own delimiter, else the delimiter its format names, a tab for a .tsv. Null when neither applies, as
        /// for a .txt, .log or unknown file given none.
        /// </summary>
        /// <param name="fileSettings">The GPALFile's settings</param>
        /// <param name="fileIdx">The file's index in the GPALFile</param>
        /// <param name="inDataFormat">The file's format</param>
        private static char? DelimiterForFile(GPALFileSettings fileSettings, int fileIdx, DataFormat inDataFormat)
        {
            List<char?> delimiters = fileSettings.Delimiter;
            char? delimiter = null;

            if (fileIdx < delimiters.Count && true == delimiters[fileIdx].HasValue)
                delimiter = delimiters[fileIdx];
            else if (true == ConverterHelper.IsDelimitedDataFormat(inDataFormat) && DataFormat.CUSTOM_DELIMITER != inDataFormat)
                delimiter = ConverterHelper.GetDelimiterFromFormat(inDataFormat);

            return delimiter;
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
                    // each file gets a grid of its own, so one file's rows are not listed again with the next
                    myGrid = GPAL.Grid.ToGPALObject();
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
                                        if (false == firstLineSkipped && true == SettingForFile(fileSettings.IgnoreFirstLineColumnNames, fileIdx, false))
                                        {
                                            firstLineSkipped = true;
                                            continue;
                                        }
                                        List<string> currentRow = new List<string>();
                                        char? fileDelim = DelimiterForFile(fileSettings, fileIdx, ConverterHelper.GetDataFormatFromExtension(filename2));
                                        char delim;

                                        if (true == fileDelim.HasValue)
                                            delim = fileDelim.Value;
                                        else
                                        {
                                            PublishSuppressingRepeats(GPALEventType.INFO, $"[{filename}] has no delimiter set, using comma.", fileSettings, GPALObjectType.FileSettings);
                                            delim = ',';
                                        }

                                        foreach (string token in ConverterHelper.SplitRespectingQuotes(currentLine, delim))   // parse tokens and add to our grid; a delimiter inside a quoted token stays in it
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
                string baseName = NameWithoutExtension(filename, out string extension);

                if (Enums.NextFilePattern.Counter == nextFilePattern || Enums.NextFilePattern.CounterPadded == nextFilePattern)
                {
                    // Seed the counter once from the highest existing "<base>_<n><ext>" in one directory listing. It
                    // stays correct when deleted files leave gaps in the numbering. Zero means not seeded yet; the caller
                    // keeps the value after that.
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
                    string name = NameWithoutExtension(path, out _);   // <base>_<digits>, .csv.gz taken off whole
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
        internal static string NextFileSuffix(Enums.NextFilePattern pattern, ref int counter)
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

            // Created often fires when the temporary file starts
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
                        string fullSavedFilePath = Path.Combine(downloaddirectory, filename);

                        string pattern = @"^downloads(?:\.htm|\s*\(\d+\)\.htm|\.htm\s*\(\d+\))$";
                        if (Regex.IsMatch(filename, pattern, RegexOptions.IgnoreCase))
                        {
                            return;
                        }

                        GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Download started for [{e.Name}] waiting on expected [{fullSavedFilePath}]", null, GPALObjectType.None);
                        watchedScratchFiles.Add(Path.Combine(downloaddirectory, e.Name));

                        // disable events temporarily to avoid duplicate calls during wait
                        // watcher.EnableRaisingEvents = false;

                        // Wait for the .crdownload file to finish. It can finish before this check, and then it is not found.
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

            // KLUDGE: a zero-byte file can be correct; wait one more second (hardcoded) before reading the size again
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

            // Read the size until two reads agree.
            do
            {
                if (0 <= (newSize = WaitForFileToDownload(filePath, downloadTimeoutInSec, newSize, out downloadedFilename)))
                {
                    // CAVEAT: when Chrome renames the file after the download starts, a later read returns -1; fileStarted records that the download was seen
                    fileStarted = true;

                    // KLUDGE: a zero-byte file can be correct; wait one more second (hardcoded) before reading the size again
                    if (0 == newSize)
                        System.Threading.Thread.Sleep(1_000);

                    // Wait between the two size reads, or a part-written file looks finished.
                    System.Threading.Thread.Sleep(DownloadSizeSampleMs);

                    if (size == (newSize = WaitForFileToDownload(filePath, downloadTimeoutInSec, newSize, out downloadedFilename)))  // the size must not change between reads
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
                // -1 after the file started means the browser renamed it; the file seen was a temporary one, so it is ignored
            }

            return downloadedFilename;
        }
        internal static long WaitForFileToDownload(string filePath, int downloadTimeoutInSec, long size, out string downloadedFilename)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(downloadTimeoutInSec);
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
                int remainingSec = (int)Math.Ceiling(remaining.TotalSeconds);

                // Print only when crossing a 10-second boundary (30>29, 20>19, etc.)
                if (remainingSec % 10 == 0 && remainingSec != lastPrinted)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"Waiting up to [{remainingSec}] sec for [{filePath}].", null, GPALObjectType.None);
                    lastPrinted = remainingSec;     // once per boundary, and not again each time growth restarts the clock
                }
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
                else    // a temporary file was seen, so return it; the next step checks the real filename
                    downloadedFilename = filePath;

                return -1;
            }

            // Reached when a wildcard is passed in and several files match.

            // Use case: a file dialog is expected but the link downloads directly, into another folder. Find where it
            // landed and copy it to the folder the user gave.
            // CAVEAT: the file mask the user gives, extension included, must match the downloaded file
            var cutoff = DateTime.Now.AddMinutes(-2);   // only files written in the last two minutes count

            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            IEnumerable<string> filesFound = Directory.EnumerateFiles(
                                 Path.GetDirectoryName(filePath),
                                 Path.GetFileName(filePath));

            // there is more than one file that matches our filePath mask, so a wildcard must be in play
            if (1 < filesFound.Count())
                foreach (var f in filesFound)
                {
                    // skip the driver auto-update files; they are not the download
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

                // Once the file exists, read its size. Open it for reading and share everything: an exclusive open,
                // landing between two of the browser's writes, makes the browser's next write fail and stops the
                // download part way.
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
            // The Default profile when no profile path is given
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
            // The Default profile when no profile path is given
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
        /// Load the file(s) definied in the File object and tokenize into a List&lt;GPALGrid&gt;, one grid per file,
        /// as every workflow reads a file
        /// </summary>
        /// <param name="currentUOW">The unit of work the input file is attached to</param>
        /// <param name="inputFile">The GPAL File definition</param>
        internal static List<IGPALGrid<string>> TokenizeFile(UnitOfWork currentUOW, GPALFile inputFile)
        {
            currentUOW.InputFile = inputFile;
            TokenizeFile(inputFile);
            return ((IGPALFileInternal)inputFile).TokenList;
        }

        /// <summary>
        /// Reads a file one line at a time into a grid, one row per line, each line split on the delimiter. This is
        /// how a workflow reads a delimited or text file.
        /// </summary>
        /// <param name="input">The open file</param>
        /// <param name="delimiter">The character between the tokens of a line</param>
        /// <param name="skipFirstLine">True to leave out the first line, a row of column names</param>
        /// <returns>The grid of the file's lines</returns>
        private static IGPALGrid<string> ReadLineGrid(StreamReader input, char delimiter, bool skipFirstLine)
        {
            IGPALGrid<string> grid = GPAL.Grid.ToGPALObject();
            string line;
            bool firstLine = true;

            while (null != (line = input.ReadLine()))
            {
                if (false == (firstLine && skipFirstLine))
                    grid.AddRow(ConverterHelper.SplitRespectingQuotes(line, delimiter));

                firstLine = false;
            }

            return grid;
        }

        /// <summary>
        /// Load the file definied in the File object and tokenize it into the grid
        /// </summary>
        /// <param name="value">The value to collect dictionary keys from; nested keys are joined with a dot</param>
        /// <param name="path">The dictionaries being walked above this one. A YAML alias can put a mapping inside itself, as
        /// Left: *T does; that member is one key, as ProcessDictionaryValue writes it as one value</param>
        private static HashSet<string> CollectDictionaryKeys(object value, HashSet<object> path = null)
        {
            var keys = new HashSet<string>();

            if (null == path)
                path = new HashSet<object>(ConverterHelper.ReferenceEqualityComparer.Instance);

            path.Add(value);

            if (value is IList<object> list)
            {
                foreach (var item in list)
                {
                    if (item is IDictionary dynamicDict)
                    {
                        path.Add(dynamicDict);
                        foreach (dynamic entry in dynamicDict)
                        {
                            string key = entry.Key?.ToString();
                            if (key != null)
                            {
                                if (entry.Value is IDictionary nestedDict && false == path.Contains(nestedDict))
                                {
                                    foreach (string nestedKey in CollectDictionaryKeys(nestedDict, path))
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
                        path.Remove(dynamicDict);
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
                        if (entry.Value is IDictionary nestedDict && false == path.Contains(nestedDict))
                        {
                            foreach (string nestedKey in CollectDictionaryKeys(nestedDict, path))
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

            path.Remove(value);
            return keys;
        }

        /// <summary>
        /// The text a YAML mapping key is read as. A key that is a list or a mapping, as ? [k] writes, becomes its YAML flow
        /// text, [k] or {a: 1}, so it names a column as a plain key does.
        /// </summary>
        /// <param name="key">The key YamlDotNet read</param>
        /// <returns>The key as text</returns>
        internal static string YamlKeyText(object key)
        {
            string text;

            if (null == key)
                text = "null";
            else if (key is IDictionary keyMapping)
                text = "{" + string.Join(", ", keyMapping.Keys.Cast<object>().Select(k => YamlKeyText(k) + ": " + YamlKeyText(keyMapping[k]))) + "}";
            else if (key is IList keyList)
                text = "[" + string.Join(", ", keyList.Cast<object>().Select(YamlKeyText)) + "]";
            else if (key is bool flag)
                text = true == flag ? "true" : "false";
            else if (key is IFormattable number)
                text = number.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            else
                text = key.ToString();

            return text;
        }

        /// <summary>
        /// Reads a plain YAML scalar going into an object, as every value of a YAML file or string does, by the YAML 1.2 core
        /// schema, as the JSON reader keeps a JSON number or bool: a whole number such as 7, -7, 007 or 0x1F is a long (a
        /// BigInteger past a long), -0 is a negative zero, a number that keeps its digits as a decimal, such as 1.50, is a
        /// decimal, a number such as 1e5, .inf or .nan is a double, and true, True, TRUE, false, False
        /// or FALSE is a bool. YamlDotNet reads ~, null and an empty value as null and a quoted or tagged scalar as text or
        /// as its tag, so "007" and '1' stay text. Anything else is left to YamlDotNet, which reads it as text.
        /// </summary>
        internal class YamlPlainScalarDeserializer : INodeDeserializer
        {
            private readonly bool intWhenItFits;

            /// <param name="intWhenItFits">true reads a whole number that fits an int as an int, as the class reader reads an
            /// int written into an object member back</param>
            internal YamlPlainScalarDeserializer(bool intWhenItFits)
            {
                this.intWhenItFits = intWhenItFits;
            }

            public bool Deserialize(IParser parser, Type expectedType, Func<IParser, Type, object> nestedObjectDeserializer, out object value, ObjectDeserializer rootDeserializer)
            {
                value = null;

                if (typeof(object) == expectedType && true == parser.Accept(out YamlDotNet.Core.Events.Scalar scalar)
                    && ScalarStyle.Plain == scalar.Style && true == scalar.Tag.IsEmpty)
                {
                    string text = scalar.Value;
                    System.Numerics.BigInteger wholeNumber = System.Numerics.BigInteger.Zero;
                    bool isWholeNumber = true;

                    if (true == Regex.IsMatch(text, "^[-+]?[0-9]+$"))
                        wholeNumber = System.Numerics.BigInteger.Parse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture);
                    else if (true == Regex.IsMatch(text, "^0x[0-9a-fA-F]+$"))
                        wholeNumber = System.Numerics.BigInteger.Parse("0" + text.Substring(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture);
                    else if (true == Regex.IsMatch(text, "^0o[0-7]+$"))
                        wholeNumber = text.Substring(2).Aggregate(System.Numerics.BigInteger.Zero, (number, digit) => number * 8 + (digit - '0'));
                    else
                        isWholeNumber = false;

                    // a number that keeps its digits as a decimal, such as 1.50 or 52000.00, is a decimal, as the json and xml
                    // readers read it, where a double would make it 1.5; read straight into an object member of a class, as
                    // the class reader reads it, it is the double it was read as before
                    decimal asWritten = 0;
                    bool keepsDigits = false == intWhenItFits && false == isWholeNumber && true == decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out asWritten)
                        && asWritten.ToString(System.Globalization.CultureInfo.InvariantCulture) == text;

                    // -0 is a negative zero, a double, which a whole number cannot hold
                    if (true == isWholeNumber && true == text.StartsWith("-", StringComparison.Ordinal) && 0 == wholeNumber)
                        value = -0.0;
                    else if (true == keepsDigits)
                        value = asWritten;
                    else if (true == isWholeNumber && true == intWhenItFits && int.MinValue <= wholeNumber && int.MaxValue >= wholeNumber)
                        value = (int)wholeNumber;
                    else if (true == isWholeNumber && long.MinValue <= wholeNumber && long.MaxValue >= wholeNumber)
                        value = (long)wholeNumber;
                    else if (true == isWholeNumber)
                        value = wholeNumber;
                    // a number past a double, such as 1e400, is Infinity on .NET and stays text on .NET Framework, which cannot parse it
                    else if (true == Regex.IsMatch(text, @"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$")
                        && true == double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double floating))
                        // a zero written with a minus, such as -0.0, is a negative zero, which .NET Framework reads as 0
                        value = 0 == floating && true == text.StartsWith("-", StringComparison.Ordinal) ? -0.0 : floating;
                    else if (true == Regex.IsMatch(text, @"^[-+]?\.(inf|Inf|INF)$"))
                        value = text.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;
                    else if (true == Regex.IsMatch(text, @"^\.(nan|NaN|NAN)$"))
                        value = double.NaN;
                    else if (true == Regex.IsMatch(text, "^(true|True|TRUE|false|False|FALSE)$"))
                        value = "true" == text.ToLowerInvariant();

                    // the scalar is read only when it was typed above
                    if (null != value)
                        parser.MoveNext();
                }

                return null != value;
            }
        }

        /// <summary>
        /// Reads a YAML mapping going into an object as the Dictionary&lt;object, object&gt; YamlDotNet makes, with each key
        /// that is a scalar read as its text, as a JSON key is, so a key such as 1.50, 007 or True keeps its spelling. A key
        /// that is a list or a mapping, as ? [k] writes, and every value are read as YamlDotNet reads them.
        /// </summary>
        internal class YamlTextKeyMappingDeserializer : INodeDeserializer
        {
            public bool Deserialize(IParser parser, Type expectedType, Func<IParser, Type, object> nestedObjectDeserializer, out object value, ObjectDeserializer rootDeserializer)
            {
                bool read = false;
                value = null;

                if (typeof(Dictionary<object, object>) == expectedType && true == parser.TryConsume(out YamlDotNet.Core.Events.MappingStart _))
                {
                    var mapping = new Dictionary<object, object>();

                    while (false == parser.TryConsume(out YamlDotNet.Core.Events.MappingEnd _))
                    {
                        object key = nestedObjectDeserializer(parser, true == parser.Accept(out YamlDotNet.Core.Events.Scalar _) ? typeof(string) : typeof(object));
                        object item = nestedObjectDeserializer(parser, typeof(object));

                        // an alias to a node still being read, as Left: *T inside the mapping T is, is set once T is read
                        if (item is IValuePromise promise)
                            promise.ValueAvailable += resolved => mapping[key] = resolved;
                        else
                            mapping[key] = item;
                    }

                    value = mapping;
                    read = true;
                }

                return read;
            }
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
                // a whole number past a long stays a number, as the JSON reader keeps it
                if (valueType == typeof(System.Numerics.BigInteger))
                {
                    stringValue = value.ToString();
                    if (gridRows != null && gridColumns == null)
                    {
                        gridRows.Add(new List<string> { stringValue });
                    }
                    return value;
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
            IGPALGrid<string> retVal = GPAL.GridForType<string>();

            // the file's own column names, or the first list given when one is used for all files
            List<string> columns = true == ((IGPALFileInternal)inputFile).FileSettings.UseOneHeaderForAllFiles
                ? ((IEnumerable<List<string>>)columnList).FirstOrDefault(row => 0 < row.Count)
                : fileIdx < columnList.Count() ? columnList[fileIdx] : null;

            if (null != columns && 0 < columns.Count)
                retVal.AddRow(new List<string>(columns));

            return retVal;
        }

        internal static bool TokenizeFile(GPALFile inputFile, ConverterSettings myConverterSettings = null, int onlyFileIdx = -1)
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

            // No converter settings: a workflow is reading the file (FillInFrom, InputFrom, a form, GPALAI). It uses the
            // grid alone, so delimited and text files are read a line at a time and the text and dictionaries are not kept.
            bool forWorkflow = null == myConverterSettings;

            // Preserve a caller-supplied FirstLineIsColumnHeaders (e.g. via the converter-level
            // WithFirstLineHasColumnNames) as a fallback when the GPALFile itself doesn't specify
            // FirstLineIsColumnNames for this file index.
            bool callerFirstLineIsColumnHeaders = myConverterSettings?.InputFirstLineIsColumnHeaders ?? false;

            // The output format is known when TokenizeFile runs. For delimited input, only the representation the
            // output format uses is kept, so the dictionary, the delimited string and the grid are not all held in
            // memory at once.
            // A database is written from the grid, one row of parameters per row, so it keeps the grid.
            DataFormat outDataFormat = myConverterSettings?.OutDataFormat ?? DataFormat.NOTSET;
            bool outputNeedsDictionary = outDataFormat == DataFormat.JSON
                || outDataFormat == DataFormat.XML
                || outDataFormat == DataFormat.YAML
                || outDataFormat == DataFormat.CLASS
                || outDataFormat == DataFormat.DICTIONARY;

            // each read starts over, so reading a file again does not add its rows a second time
            ((IGPALFileInternal)inputFile).FileDictionary.Clear();
            ((IGPALFileInternal)inputFile).FileData.Clear();
            ((IGPALFileInternal)inputFile).FileGrid.Clear();
            ((IGPALFileInternal)inputFile).FileSettings.ExcelRowsPerSheet.Clear();
            ((IGPALFileInternal)inputFile).FileSettings.ExcelSheetNames.Clear();
            foreach (string filename in inputFile.Filenames)
            {
                DataFormat inDataFormat = ConverterHelper.GetDataFormatFromExtension(filename);
                bool inputIsDelimited = ConverterHelper.IsDelimitedDataFormat(inDataFormat);

                myConverterSettings = new ConverterSettings
                {
                    InDelimiter = ((IGPALFileInternal)inputFile).Delimiter.Count() > fileIdx && ((IGPALFileInternal)inputFile).Delimiter[fileIdx].HasValue
                        ? ((IGPALFileInternal)inputFile).Delimiter[fileIdx].Value
                        : (char?)null,
                    FieldsEnclosedInQuotes = SettingForFile(((IGPALFileInternal)inputFile).FileSettings.FieldsEnclosedInQuotes, fileIdx, false),
                    FirstLineIsColumnHeaders = fileIdx < ((IGPALFileInternal)inputFile).FileSettings.FirstLineGiven.Count && true == ((IGPALFileInternal)inputFile).FileSettings.FirstLineGiven[fileIdx]
                        ? ((IGPALFileInternal)inputFile).FileSettings.FirstLineIsColumnNames[fileIdx]
                        : callerFirstLineIsColumnHeaders,
                    IgnoreFirstLineColumnHeaders = SettingForFile(((IGPALFileInternal)inputFile).FileSettings.IgnoreFirstLineColumnNames, fileIdx, false),
                    ColumnNames = inputIsDelimited ? ColumnNamesForFile(inputFile, fileIdx) : new GPALGrid<string>(),
                    ExcelSheetNames = ((IGPALFileInternal)inputFile).FileSettings.ExcelSheetNames,
                    ExcelRowsPerSheet = ((IGPALFileInternal)inputFile).FileSettings.ExcelRowsPerSheet,
                    InputFile = inputFile,
                };

                // reading one file of the GPALFile passes over the others
                if (0 <= onlyFileIdx && fileIdx != onlyFileIdx)
                {
                    fileIdx++;
                    continue;
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

                            // A workflow reads a delimited, text or unknown file a line at a time, with this file's
                            // delimiter and first line setting.
                            bool readByLine = true == forWorkflow
                                && (true == inputIsDelimited || DataFormat.LOG == inDataFormat || DataFormat.NOTSET == inDataFormat);

                            if (true == readByLine)
                                ((IGPALFileInternal)inputFile).FileGrid.Add(ReadLineGrid(input,
                                    DelimiterForFile(((IGPALFileInternal)inputFile).FileSettings, fileIdx, inDataFormat)
                                        ?? ConverterHelper.GetDelimiterFromFormat(inDataFormat) ?? ',',
                                    SettingForFile(((IGPALFileInternal)inputFile).FileSettings.IgnoreFirstLineColumnNames, fileIdx, false)));
                            else
                            // a .txt or .log file given a delimiter is delimited text and is read as a csv file is, with its
                            // delimiter and first line setting
                            switch (DataFormat.LOG == inDataFormat && true == myConverterSettings.InDelimiter.HasValue ? DataFormat.CSV : inDataFormat)
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

                                        // A file with a header line and no rows has no row to carry its names, so its grid
                                        // starts with its header line, as the grid of a file with rows does
                                        if (true == myConverterSettings.FirstLineIsColumnHeaders && 0 == dictionary.Count)
                                        {
                                            input.BaseStream.Position = 0;
                                            input.DiscardBufferedData();

                                            string headerLine = null;
                                            string fileLine;
                                            while (null == headerLine && null != (fileLine = input.ReadLine()))
                                                if (false == string.IsNullOrWhiteSpace(fileLine))
                                                    headerLine = fileLine;

                                            // reading again from the start reads a file's byte order mark as a character
                                            if (null != headerLine)
                                                outGrid.AddRow(ConverterHelper.SplitRespectingQuotes(headerLine.TrimStart('\uFEFF'),
                                                    myConverterSettings.InDelimiter ?? ConverterHelper.GetDelimiterFromFormat(inDataFormat) ?? ','));
                                        }

                                        ((IGPALFileInternal)inputFile).FileData.Add(delimitedData);
                                        ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);

                                        // The data/grid were derived from the dictionary and the output
                                        // doesn't need the dictionary itself - free its contents now.
                                        dictionary.Clear();
                                    }
                                    break;

                                case DataFormat.LOG:
                                    // Plain text given no delimiter: each line is one value, kept as written, quotes and
                                    // all. A first line declared as column names is the name of that one column.
                                    string logText = input.ReadToEnd();
                                    input.BaseStream.Position = 0;

                                    IGPALGrid<string> logGrid = GPAL.GridForType<string>();
                                    var logRecords = new Dictionary<object, dynamic>();
                                    int logLineIdx = 0;
                                    string logColumnName = null;

                                    // Drop the trailing newline; it is not an empty line. Inner blank lines are kept.
                                    string logLines = logText.TrimEnd(Environment.NewLine.ToCharArray());

                                    if (false == string.IsNullOrEmpty(logLines))
                                        foreach (string logLine in logLines.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None))
                                        {
                                            logGrid.AddRow(new List<string> { logLine });

                                            if (true == myConverterSettings.FirstLineIsColumnHeaders && null == logColumnName)
                                                logColumnName = logLine;
                                            else
                                                logRecords[$"GPALKEY{logLineIdx++:D4}"] = null != logColumnName ? (dynamic)new Dictionary<string, string> { { logColumnName, logLine } } : logLine;
                                        }

                                    ((IGPALFileInternal)inputFile).FileData.Add(logText);
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(logGrid);
                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(logRecords);
                                    myConverterSettings.InputDictionary = ((IGPALFileInternal)inputFile).FileDictionary;
                                    break;

                                case DataFormat.JSON:
                                    string fileString = input.ReadToEnd();
                                    input.BaseStream.Position = 0;

                                    // DateParseHandling.None keeps a date string as the text written, where the default makes it a DateTime
                                    // MaxDepth = null reads json nested past the reader's 64 levels, as html read into json is, to the
                                    // converter's limit, past which the file is not read
                                    CustomJsonConverter fileConverter = new CustomJsonConverter(fileString);
                                    dynamic jsonObject = JsonConvert.DeserializeObject<dynamic>(fileString, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None, MaxDepth = null, Converters = { fileConverter } });
                                    if (true == fileConverter.TooDeep)
                                        throw new JsonReaderException($"[{filename}] is nested deeper than [{ConverterHelper.NestingLimit}] levels");

                                    ((IGPALFileInternal)inputFile).FileData.Add(fileString);

                                    var records = new Dictionary<object, dynamic>();

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

                                            // It's an array/list. An array of {"Key": ..., "Value": ...} objects, as a
                                            // complex-key dictionary is written, stays an array; a dictionary target
                                            // keys its records by Key
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

                                    // If top-level is array, wrap as surrogate dict for FileDictionary. An empty array is
                                    // an empty dictionary, with no root, where an empty object is under root
                                    if (processedRoot is IDictionary && (ConverterHelper.IsArrayLike((IDictionary)processedRoot)
                                        || (0 == ((IDictionary)processedRoot).Count && jsonObject is IEnumerable<dynamic> && false == jsonObject is string)))
                                    {
                                        records = (Dictionary<object, dynamic>)processedRoot;
                                    }
                                    else
                                    {
                                        // Single object or other - wrap under a key or just store
                                        records["root"] = processedRoot;
                                    }

                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(records);

                                    GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"JSON FileDictionary count: [{records.Count}]", null, GPALObjectType.Other);

                                    // the grid holds the rows the file writes to csv, as for an XML file. A JSON file names
                                    // its columns in its keys, so the grid has no header row
                                    myConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { records };
                                    myConverterSettings.FirstLineIsColumnHeaders = false;
                                    ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid, DataFormat.JSON);
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);
                                    break;

                                case DataFormat.XLSX:
                                    IGPALGrid<string> grid = ExcelHelper.TokenizeExcelFile(filename2);
                                    List<string> colNames = new List<string>();
                                    // one entry for the file holding its rows, every sheet's, by GPALKEY row number, as a
                                    // delimited file has
                                    Dictionary<object, dynamic> xlsxRecords = new Dictionary<object, dynamic>();
                                    int xlsxRecordIdx = 0;

                                    ((IGPALFileInternal)inputFile).FileGrid.Add((IGPALGrid<string>)grid.Clone());

                                    // find out how many worksheets we have and find out the number of rows to ascertain when the grid data is another sheet with different column names
                                    using (var workbook = new XLWorkbook(filename2))
                                    {
                                        List<int> rowsPerSheet = new List<int>();
                                        List<string> sheetName = new List<string>();

                                        foreach (IXLWorksheet worksheet in workbook.Worksheets)
                                        {
                                            // the rows ExcelHelper.TokenizeExcelFile put in the grid for this sheet: up to its
                                            // last row with a value, so formatted blank rows below it are not counted
                                            int totalRows = worksheet.LastRowUsed()?.RowNumber() ?? 0;

                                            rowsPerSheet.Add(totalRows);
                                            sheetName.Add(worksheet.Name);
                                        }

                                        ((IGPALFileInternal)inputFile).FileSettings.ExcelRowsPerSheet.AddRow(rowsPerSheet);
                                        ((IGPALFileInternal)inputFile).FileSettings.ExcelSheetNames.AddRow(sheetName);

                                        // a cell that holds a number keeps it as a number, as a JSON number is: its value in
                                        // the invariant culture's round-trip form, 1.5, 0.30000000000000004 or 1E+15, whatever
                                        // the computer's culture, is read as a decimal, and a whole number as a long. Other
                                        // cells, such as text 007, give null and stay text
                                        object CellNumber(IXLWorksheet cellSheet, int sheetRow, int sheetColumn)
                                        {
                                            object cellNumber = null;
                                            if (XLDataType.Number == cellSheet.Cell(sheetRow, sheetColumn).DataType)
                                            {
                                                string numberText = cellSheet.Cell(sheetRow, sheetColumn).GetDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                                                cellNumber = numberText;
                                                if (true == decimal.TryParse(numberText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal cellDecimal))
                                                    cellNumber = decimal.Truncate(cellDecimal) == cellDecimal && long.MinValue <= cellDecimal && long.MaxValue >= cellDecimal ? (object)(long)cellDecimal : cellDecimal;
                                            }
                                            return cellNumber;
                                        }

                                        int columnHeaders = 0;
                                        int sheetNumber = 0;
                                        foreach (int rowCnt in rowsPerSheet)
                                        {
                                            sheetNumber++;
                                            if (myConverterSettings.FirstLineIsColumnHeaders)
                                            {
                                                // a name the sheet's header line repeats is made its own, as a csv file's is
                                                colNames = ConverterHelper.UniqueNames(grid[columnHeaders], $"{filename2}] sheet [{sheetName[sheetNumber - 1]}");

                                                myConverterSettings.ColumnNames.AddRow(colNames);

                                                // the file's first sheet names its columns at the file's own place in the list,
                                                // unless names were given for it
                                                while (((IGPALFileInternal)inputFile).FileSettings.ColumnList.Count() <= fileIdx)
                                                    ((IGPALFileInternal)inputFile).FileSettings.ColumnList.AddRow(new List<string>());

                                                if (0 == ((IGPALFileInternal)inputFile).FileSettings.ColumnList[fileIdx].Count)
                                                    ((IGPALFileInternal)inputFile).FileSettings.ColumnList[fileIdx].AddRange(colNames);

                                                int rowIdx = 0;

                                                foreach (Dictionary<object, dynamic> row in ConverterHelper.ConvertGridToDictionary(colNames, grid, columnHeaders, rowCnt))
                                                {
                                                    if (0 == rowIdx++)
                                                        continue;

                                                    // a cell that holds a number keeps it as a number
                                                    IXLWorksheet rowSheet = workbook.Worksheet(sheetNumber);
                                                    for (int colIdx = 0; colIdx < colNames.Count; colIdx++)
                                                    {
                                                        object cellNumber = CellNumber(rowSheet, rowIdx, colIdx + 1);
                                                        if (null != cellNumber)
                                                            row[colNames[colIdx]] = cellNumber;
                                                    }

                                                    xlsxRecords[$"GPALKEY{xlsxRecordIdx++:D4}"] = row;
                                                }
                                            }
                                            else
                                            {
                                                // a sheet with no header line names nothing, so each row is its list of values, as
                                                // a csv file's is; given column names for the file name its values, as they do a
                                                // csv file's, and a value past the last name goes under a name made from its
                                                // column number
                                                // a name given more than once is made its own, as a csv file's given names are
                                                List<string> givenNames = ((IEnumerable<List<string>>)ColumnNamesForFile(inputFile, fileIdx)).FirstOrDefault();
                                                if (null != givenNames)
                                                    givenNames = ConverterHelper.UniqueNames(givenNames, $"{filename2}] sheet [{sheetName[sheetNumber - 1]}");

                                                for (int rowIdx = columnHeaders; rowIdx < columnHeaders + rowCnt; rowIdx++)
                                                {
                                                    if (null == givenNames)
                                                        xlsxRecords[$"GPALKEY{xlsxRecordIdx++:D4}"] = new List<string>(grid[rowIdx]);
                                                    else
                                                    {
                                                        // a cell that holds a number keeps it as a number, as it does under a header line
                                                        List<string> rowNames = ConverterHelper.NamesForWidth(givenNames, grid[rowIdx].Count);
                                                        Dictionary<object, dynamic> namedRow = new Dictionary<object, dynamic>();
                                                        IXLWorksheet rowSheet = workbook.Worksheet(sheetNumber);

                                                        for (int colIdx = 0; colIdx < rowNames.Count; colIdx++)
                                                            namedRow[rowNames[colIdx]] = colIdx < grid[rowIdx].Count
                                                                ? CellNumber(rowSheet, rowIdx - columnHeaders + 1, colIdx + 1) ?? grid[rowIdx][colIdx]
                                                                : string.Empty;

                                                        xlsxRecords[$"GPALKEY{xlsxRecordIdx++:D4}"] = namedRow;
                                                    }
                                                }
                                            }

                                            columnHeaders += rowCnt;
                                        }
                                    }

                                    // added to the files read before it
                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(xlsxRecords);
                                    myConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { xlsxRecords };
                                    break;

                                case DataFormat.XML:
                                    xmlDoc.PreserveWhitespace = true;
                                    xmlDoc.Load(input);
                                    input.BaseStream.Position = 0;
                                    var xmlDictionary = ConverterHelper.ConvertXMLToDictionary(xmlDoc);
                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(xmlDictionary);
                                    myConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { xmlDictionary };
                                    ((IGPALFileInternal)inputFile).FileData.Add(ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid, DataFormat.XML));
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);
                                    break;

                                case DataFormat.YAML:
                                    // a plain number, true, false or null keeps its type, as a JSON file's does; keys are text
                                    var deserializer = new DeserializerBuilder()
                                        .WithMaximumRecursion(ConverterHelper.NestingLimit)
                                        .WithNodeDeserializer(new YamlPlainScalarDeserializer(false))
                                        .WithNodeDeserializer(new YamlTextKeyMappingDeserializer())
                                        .Build();

                                    string yamlText = input.ReadToEnd();
                                    input.BaseStream.Position = 0;
                                    dynamic yamlObject = deserializer.Deserialize<object>(yamlText);
                                    ((IGPALFileInternal)inputFile).FileData.Add(yamlText);

                                    var processedYamlDict = new Dictionary<dynamic, dynamic>();

                                    if (yamlObject is IDictionary yamlMapping)
                                    {
                                        foreach (var key in yamlMapping.Keys)
                                        {
                                            string processedKey = null == key ? "null_key" : YamlKeyText(key);
                                            processedYamlDict[processedKey] = ProcessDictionaryValue(yamlMapping[key]);
                                        }
                                    }
                                    else if (yamlObject is IList yamlSequence)
                                    {
                                        // Root is a YAML sequence, not a mapping - synthesize row keys
                                        // (same GPALKEYN convention used for headerless CSV rows).
                                        int rootIdx = 0;
                                        foreach (var item in yamlSequence)
                                        {
                                            processedYamlDict[$"GPALKEY{rootIdx++:D4}"] = ProcessDictionaryValue(item);
                                        }
                                    }
                                    else if (null != yamlObject)
                                    {
                                        // Root is a bare YAML scalar.
                                        processedYamlDict["GPALKEY0000"] = ProcessDictionaryValue(yamlObject);
                                    }
                                    else
                                    {
                                        // an empty document, or one holding only ~ or a comment, is held as an empty JSON file
                                        // is, a null under the reader's root
                                        processedYamlDict["root"] = null;
                                    }

                                    ((IGPALFileInternal)inputFile).FileDictionary.Add(processedYamlDict);

                                    // the grid holds the rows the file writes to csv, as for an XML file. A YAML file names
                                    // its columns in its keys, so the grid has no header row
                                    myConverterSettings.InputDictionary = new List<Dictionary<object, dynamic>> { processedYamlDict };
                                    myConverterSettings.FirstLineIsColumnHeaders = false;
                                    ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid, DataFormat.YAML);
                                    ((IGPALFileInternal)inputFile).FileGrid.Add(outGrid);
                                    break;

                                case DataFormat.HTML:
                                    // added to the files read before it; this file's entries alone go on to its grid
                                    List<Dictionary<object, dynamic>> htmlDictionaries = ConverterHelper.ConvertHtmlToDictionary(filename2);
                                    ((IGPALFileInternal)inputFile).FileDictionary.AddRange(htmlDictionaries);
                                    myConverterSettings.InputDictionary = htmlDictionaries;
                                    dynamic inData = ConverterHelper.ConvertInputDictionaryToDelimitedAndGrid(myConverterSettings, out outGrid, DataFormat.HTML);
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

            // nothing read: every line of every file, split on its delimiter; not for a read of one file, which
            // would read all of them
            if (0 == ((IGPALFileInternal)inputFile).FileSettings.FileGrid.Count && 0 > onlyFileIdx)
                FileHelper.GetGrid(((IGPALFileInternal)inputFile).FileSettings, out tokens);
            else
                tokens = ((IGPALFileInternal)inputFile).FileSettings.FileGrid;

            // A workflow uses the grid alone, so the text and dictionaries made on the way to it are let go. It gets a
            // list of its own, so the next read of the file, which empties the file's list, does not empty the one a
            // fill is going through.
            if (true == forWorkflow)
            {
                ((IGPALFileInternal)inputFile).FileData.Clear();
                ((IGPALFileInternal)inputFile).FileDictionary.Clear();
                tokens = new List<IGPALGrid<string>>(tokens);
            }

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
        /// A filename without its extension, where a .gz counts its inner extension as part of it, so
        /// "reports.csv.gz" is "reports" and ".csv.gz". A number added between the two then survives unpacking:
        /// reports_0001.csv.gz unpacks to reports_0001.csv.
        /// </summary>
        /// <param name="filename">Filename, with or without a folder</param>
        /// <param name="extension">The extension, ".csv.gz" for a .gz with one inside</param>
        /// <returns>The filename without the folder or the extension</returns>
        internal static string NameWithoutExtension(string filename, out string extension)
        {
            string name = Path.GetFileNameWithoutExtension(filename);
            extension = Path.GetExtension(filename);

            // a compressed single file, such as .csv.gz or .csv.bz2, keeps its inner extension with it
            if (true == ExtensionList(GPAL.GPALSettings.GzExtensions).Concat(new[] { "bz2", "xz" }).Contains(extension.TrimStart('.').ToLowerInvariant()))
            {
                extension = Path.GetExtension(name) + extension;
                name = Path.GetFileNameWithoutExtension(name);
            }

            return name;
        }
        /// <summary>
        /// What a file holds, read from its first bytes rather than its name, so a download saved without .zip, or an
        /// .xpi, .jar or .xlsx, is still recognized.
        /// </summary>
        /// <param name="filename">File to read</param>
        /// <returns>".zip", ".7z", ".rar", ".tar" for a tar whether plain or inside a gz, bz2 or xz, ".gz", ".bz2" or
        /// ".xz" for one compressed file, or null for anything else</returns>
        internal static string ArchiveType(string filename)
        {
            byte[] start = new byte[512];
            int read = 0;

            using (FileStream stream = System.IO.File.OpenRead(filename))
                read = ReadStart(stream, start);

            // PK 03 04 starts a zip with entries and PK 05 06 an empty one; 7z and rar have their own signatures; a tar
            // header adds up to its own checksum. 1F 8B starts a gzip, BZh a bz2 and FD 37 7A 58 5A 00 an xz; each is a
            // tar when what it holds starts with a tar header, otherwise one compressed file
            string compressed = true == StartsWith(start, read, 0x1F, 0x8B) ? ".gz"
                              : true == StartsWith(start, read, 0x42, 0x5A, 0x68) ? ".bz2"
                              : true == StartsWith(start, read, 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00) ? ".xz"
                              : null;

            return true == StartsWith(start, read, 0x50, 0x4B, 0x03, 0x04) || true == StartsWith(start, read, 0x50, 0x4B, 0x05, 0x06) ? ".zip"
                 : true == StartsWith(start, read, 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C) ? ".7z"
                 : true == StartsWith(start, read, 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07) ? ".rar"
                 : true == IsTar(start, read) ? ".tar"
                 : null != compressed ? (true == CompressedHoldsTar(filename, compressed) ? ".tar" : compressed)
                 : null;
        }
        /// <summary>
        /// Reads from the start of a stream until the buffer is full or the stream ends, since one Read can return less.
        /// </summary>
        /// <param name="stream">Stream to read</param>
        /// <param name="buffer">Buffer to fill</param>
        /// <returns>How many bytes were read</returns>
        private static int ReadStart(Stream stream, byte[] buffer)
        {
            int total = 0;
            int read = -1;

            while (total < buffer.Length && 0 != read)
            {
                read = stream.Read(buffer, total, buffer.Length - total);
                total += read;
            }

            return total;
        }
        /// <summary>
        /// True when the first bytes read are the signature given.
        /// </summary>
        /// <param name="buffer">The bytes read</param>
        /// <param name="read">How many were read</param>
        /// <param name="signature">The bytes a format starts with</param>
        /// <returns>True when they match</returns>
        private static bool StartsWith(byte[] buffer, int read, params int[] signature)
        {
            bool matches = read >= signature.Length;

            for (int index = 0; true == matches && index < signature.Length; index++)
                matches = signature[index] == buffer[index];

            return matches;
        }
        /// <summary>
        /// True when the bytes are the start of a tar. Not every tar says ustar at byte 257, older and GNU ones do not,
        /// so the header is checked against its own checksum: the octal number at byte 148 is the sum of the header's
        /// 512 bytes, counting the checksum's own 8 as spaces.
        /// </summary>
        /// <param name="buffer">The first bytes</param>
        /// <param name="read">How many were read</param>
        /// <returns>True for a tar</returns>
        private static bool IsTar(byte[] buffer, int read)
        {
            string recorded = 512 <= read ? Encoding.ASCII.GetString(buffer, 148, 8).Trim(' ', '\0') : string.Empty;
            int sum = 0;

            for (int index = 0; index < 512 && 512 <= read; index++)
                sum += 148 <= index && 156 > index ? (int)' ' : buffer[index];

            // an all-zero block also adds up, to 256, so a tar's first header must have a name
            return 0 < recorded.Length && true == recorded.All(digit => '0' <= digit && '7' >= digit)
                && sum == Convert.ToInt32(recorded, 8) && 0 != buffer[0];
        }
        /// <summary>
        /// True when a gz, bz2 or xz holds a tar, as a .tar.gz, .tgz, .tar.bz2 or .tar.xz does. A file that will not
        /// decompress is not a tar, so it is left to open as the one file it claims to be, and fails there if it is not.
        /// </summary>
        /// <param name="filename">The compressed file</param>
        /// <param name="compression">".gz", ".bz2" or ".xz"</param>
        /// <returns>True when what it holds starts with a tar header</returns>
        private static bool CompressedHoldsTar(string filename, string compression)
        {
            byte[] start = new byte[512];
            int read = 0;

            try
            {
                using (FileStream input = System.IO.File.OpenRead(filename))
                using (Stream decompressed = Decompressing(input, compression))
                    read = ReadStart(decompressed, start);
            }
            catch (Exception ex)
            {
                // a text file that happens to start BZh, for one
                GPAL.PublishSimpleEvent(GPALEventType.DEBUG, $"[{filename}] does not decompress as [{compression}]: [{ex.GetType().Name}]");
            }

            return IsTar(start, read);
        }
        /// <summary>
        /// A stream that decompresses a gz, bz2 or xz as it is read.
        /// </summary>
        /// <param name="input">The compressed file</param>
        /// <param name="compression">".gz", ".bz2" or ".xz"</param>
        /// <returns>The decompressing stream; disposing it disposes the input</returns>
        private static Stream Decompressing(Stream input, string compression)
        {
            return ".bz2" == compression ? SharpCompress.Compressors.BZip2.BZip2Stream.Create(input, SharpCompress.Compressors.CompressionMode.Decompress, true, false, false)
                 : ".xz" == compression ? (Stream)new SharpCompress.Compressors.Xz.XZStream(input)
                 : new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        }
        /// <summary>
        /// The archive a path goes through, as in C:\work\export.zip\*.csv: the first part of the path that is a file
        /// holding a zip, gz, 7z, rar or tar, with more path after it. A path through only folders has none.
        /// </summary>
        /// <param name="path">Path WithFileName was given</param>
        /// <returns>The archive's path, or null when the path goes through no archive</returns>
        internal static string ArchiveInPath(string path)
        {
            char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            string archive = null;

            for (int index = path.IndexOfAny(separators); null == archive && index >= 0; index = path.IndexOfAny(separators, index + 1))
            {
                string before = path.Substring(0, index);

                if (true == System.IO.File.Exists(before) && null != ArchiveType(before))
                    archive = before;
            }

            return archive;
        }
        /// <summary>
        /// The extensions a url may name an archive by when a wildcard follows it, as in .../export.zip/*.csv: those in
        /// zipExtensions and gzExtensions in gpal.yaml, and 7z, rar, tar, tgz, bz2 and xz.
        /// </summary>
        /// <returns>The extensions as a regular expression alternation</returns>
        internal static string ArchiveExtensionPattern()
        {
            return string.Join("|", ExtensionList(GPAL.GPALSettings.ZipExtensions)
                .Concat(ExtensionList(GPAL.GPALSettings.GzExtensions))
                .Concat(new[] { "7z", "rar", "tar", "tgz", "bz2", "xz" })
                .Select(extension => Regex.Escape(extension)));
        }
        /// <summary>
        /// The extensions in a gpal.yaml list such as "zip, xpi, jar", without dots, in lower case.
        /// </summary>
        /// <param name="extensions">The list, separated by commas, semicolons or spaces</param>
        /// <returns>Each extension</returns>
        private static List<string> ExtensionList(string extensions)
        {
            return (extensions ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(extension => extension.Trim().TrimStart('.').ToLowerInvariant())
                .Where(extension => 0 < extension.Length)
                .ToList();
        }
        /// <summary>
        /// What SaveTo writes for a destination name: a .tar.gz or .tgz is a gzipped tar, a .tar a tar, an extension in
        /// zipExtensions a zip, one in gzExtensions a gz, and a .7z a 7z. A rar can be opened but not written.
        /// </summary>
        /// <param name="name">The destination's name</param>
        /// <returns>".zip", ".gz", ".tar", ".tar.gz", ".7z", ".rar" for the read-only one, or null for anything else</returns>
        internal static string ArchiveFormat(string name)
        {
            string lower = name.ToLowerInvariant();
            string extension = Path.GetExtension(lower).TrimStart('.');

            return true == lower.EndsWith(".tar.gz") || true == lower.EndsWith(".tgz") ? ".tar.gz"
                 : true == lower.EndsWith(".tar") ? ".tar"
                 : true == lower.EndsWith(".7z") ? ".7z"
                 : true == lower.EndsWith(".rar") ? ".rar"
                 : true == ExtensionList(GPAL.GPALSettings.ZipExtensions).Contains(extension) ? ".zip"
                 : true == ExtensionList(GPAL.GPALSettings.GzExtensions).Contains(extension) ? ".gz"
                 : null;
        }
        /// <summary>
        /// Takes the files a path names inside an archive out to GPAL's temp directory, into a folder named after the
        /// archive and where it is, with ".contents" added. Inside works as it does in a folder: "*.csv" matches the csv
        /// files at the top of the archive, "data\*.csv" the ones in its data folder, and a name with no wildcard that
        /// one file.<br/>
        /// A gz, bz2 or xz that is not a tar holds one file, named as the archive without its extension.<br/>
        /// Only the matching files are written, and they replace what an earlier unpack of the same archive left there.
        /// </summary>
        /// <param name="archive">The zip, 7z, rar, tar, gz, bz2 or xz</param>
        /// <param name="inside">The path inside it, wildcards allowed</param>
        /// <returns>The files written, in the order the archive holds them</returns>
        internal static List<string> ExtractFromArchive(string archive, string inside)
        {
            List<string> extracted = new List<string>();
            string type = ArchiveType(archive);

            // Named after the archive and where it is, so export.zip from two folders unpacks to two places
            string where;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                where = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(archive).ToLowerInvariant())), 0, 4).Replace("-", string.Empty);

            string contents = Path.Combine(GPAL.TempDirectory(), Path.GetFileName(archive) + "." + where + ".contents");
            string wanted = inside.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string wantedFolder = Path.GetDirectoryName(wanted) ?? string.Empty;

            // the wildcard in the last part, as Windows matches it: * any run of characters, ? any one
            Regex wantedName = new Regex("^" + Regex.Escape(Path.GetFileName(wanted)).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase);

            if (".gz" == type || ".bz2" == type || ".xz" == type)
            {
                // one file, named as the archive without its extension
                string name = Path.GetFileNameWithoutExtension(archive);
                string target = 0 == wantedFolder.Length && true == wantedName.IsMatch(name) ? Path.Combine(contents, name) : null;

                if (null != target)
                    using (FileStream input = System.IO.File.OpenRead(archive))
                    using (Stream decompressed = Decompressing(input, type))
                        SaveEntry(decompressed, target, System.IO.File.GetLastWriteTime(archive), extracted);
            }
            else if (".zip" == type)
            {
                using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(archive))
                    foreach (System.IO.Compression.ZipArchiveEntry entry in zip.Entries)
                    {
                        // a folder entry has no name of its own
                        string target = 0 < entry.Name.Length ? EntryTarget(entry.FullName, wantedFolder, wantedName, contents, archive) : null;

                        if (null != target)
                            using (Stream input = entry.Open())
                                SaveEntry(input, target, entry.LastWriteTime.DateTime, extracted);
                    }
            }
            else
            {
                // A 7z is opened as an archive and read straight through, which also keeps a solid one from being
                // unpacked again for every entry. A rar, and a tar whether plain or inside a gz, bz2 or xz, are read
                // straight through
                using (FileStream input = System.IO.File.OpenRead(archive))
                using (SharpCompress.Archives.IArchive sevenZip = ".7z" == type ? SharpCompress.Archives.ArchiveFactory.OpenArchive(input, new SharpCompress.Readers.ReaderOptions()) : null)
                using (SharpCompress.Readers.IReader reader = null != sevenZip ? sevenZip.ExtractAllEntries() : SharpCompress.Readers.ReaderFactory.OpenReader(input, new SharpCompress.Readers.ReaderOptions()))
                    while (true == reader.MoveToNextEntry())
                    {
                        string target = false == reader.Entry.IsDirectory ? EntryTarget(reader.Entry.Key, wantedFolder, wantedName, contents, archive) : null;

                        if (null != target)
                            using (Stream entryStream = reader.OpenEntryStream())
                                SaveEntry(entryStream, target, reader.Entry.LastModifiedTime, extracted);
                    }
            }

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{extracted.Count}] file(s) matching [{inside}] taken out of [{archive}] to [{contents}]");

            return extracted;
        }
        /// <summary>
        /// Where an archive entry is written when it is one the path asked for: in the folder asked for, with a name the
        /// wildcard matches. A leading ./ some tars put on every entry is ignored.
        /// </summary>
        /// <param name="entryName">The entry's path inside the archive</param>
        /// <param name="wantedFolder">The folder inside the archive the path named, empty for the top</param>
        /// <param name="wantedName">The file name or wildcard the path named</param>
        /// <param name="contents">The folder the archive is unpacked into</param>
        /// <param name="archive">The archive, for the message when an entry points outside contents</param>
        /// <returns>The full path to write, or null when the entry was not asked for</returns>
        private static string EntryTarget(string entryName, string wantedFolder, Regex wantedName, string contents, string archive)
        {
            string entryPath = Regex.Replace(entryName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), @"^(\.\\|\\)+", string.Empty);
            string target = null;

            if (true == string.Equals(Path.GetDirectoryName(entryPath) ?? string.Empty, wantedFolder, StringComparison.OrdinalIgnoreCase)
                && true == wantedName.IsMatch(Path.GetFileName(entryPath)))
            {
                string full = Path.GetFullPath(Path.Combine(contents, entryPath));

                // an entry named with ..\ would land outside the contents folder, so it is left in the archive
                if (true == full.StartsWith(Path.GetFullPath(contents) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    target = full;
                else
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"[{entryName}] in [{archive}] points outside [{contents}], so it was not taken out");
            }

            return target;
        }
        /// <summary>
        /// Writes one entry out of an archive, keeping its date, and adds it to the list taken out.
        /// </summary>
        /// <param name="input">The entry's contents</param>
        /// <param name="target">Where to write it; a file already there is replaced</param>
        /// <param name="written">The entry's date, when the archive has one</param>
        /// <param name="extracted">The files taken out so far</param>
        private static void SaveEntry(Stream input, string target, DateTime? written, List<string> extracted)
        {
            EnsureFolderFor(target);

            using (FileStream output = new FileStream(target, FileMode.Create))
                input.CopyTo(output);

            if (null != written)
                System.IO.File.SetLastWriteTime(target, written.Value);

            extracted.Add(target);
        }
        /// <summary>
        /// The files SaveTo packs and the name each has inside the archive. A file goes in at the top under its own name;
        /// a folder goes in whole, everything under it at its path inside the folder, so the folder's contents are the
        /// top of the archive. A folder saved into itself leaves out the archive being written and the ones earlier runs
        /// numbered from the same name; a file named on its own always goes in.
        /// </summary>
        /// <param name="sources">Files and folders</param>
        /// <param name="requested">The archive's name before any numbering</param>
        /// <returns>Each file with its name inside the archive</returns>
        internal static List<(string file, string entry)> ArchiveEntries(List<string> sources, string requested)
        {
            List<(string file, string entry)> entries = new List<(string file, string entry)>();

            foreach (string source in sources)
            {
                if (true == Directory.Exists(source))
                {
                    string folder = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                    {
                        if (true == IsSaveOf(file, requested))
                            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{file}] left out of [{requested}], it is that archive or an earlier numbered one");
                        else
                            entries.Add((file, file.Substring(folder.Length)));
                    }
                }
                else
                    entries.Add((source, Path.GetFileName(source)));
            }

            return entries;
        }
        /// <summary>
        /// True when a file is the archive SaveTo is about to write, or one an earlier run numbered from the same name,
        /// so a folder saved into itself does not pack its own earlier archives.
        /// </summary>
        /// <param name="file">A file going into the archive</param>
        /// <param name="target">The name SaveTo was given, before any numbering</param>
        /// <returns>True when the file is that archive or a numbered copy of it</returns>
        internal static bool IsSaveOf(string file, string target)
        {
            string baseName = NameWithoutExtension(target, out string extension);

            return true == string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetDirectoryName(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase)
                && true == Regex.IsMatch(Path.GetFileName(file), "^" + Regex.Escape(baseName) + @"(_?[\d_]+)?" + Regex.Escape(extension) + "$", RegexOptions.IgnoreCase);
        }
        /// <summary>
        /// Writes files into one new zip, tar or 7z, keeping each one's date. A tar can be gzipped as it is written, and a
        /// 7z is compressed with LZMA2, as 7-Zip does by default.<br/>
        /// A file that cannot be opened is published and left out, and the rest are still saved. A file whose read fails
        /// partway is published and the archive is removed: a zip would hold it cut short, and in a tar or 7z everything
        /// after it would be damaged.
        /// </summary>
        /// <param name="entries">Each file and its name inside the archive</param>
        /// <param name="target">The archive to write; one already there is replaced</param>
        /// <param name="format">".zip", ".tar", ".tar.gz" or ".7z"</param>
        internal static void WriteArchive(List<(string file, string entry)> entries, string target, string format)
        {
            int saved = 0;
            bool damaged = false;

            EnsureFolderFor(target);

            using (FileStream output = new FileStream(target, FileMode.Create))
            {
                if (".zip" == format)
                {
                    using (System.IO.Compression.ZipArchive zip = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create))
                        for (int index = 0; index < entries.Count && false == damaged; index++)
                        {
                            (string file, string entry) item = entries[index];
                            FileStream input = null;

                            // Opened before the entry is made, so a file that cannot be opened leaves no entry behind and
                            // the rest go in
                            try
                            {
                                input = System.IO.File.OpenRead(item.file);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to add [{item.file}] to [{target}], it is saved without it.", null, GPALObjectType.GPALFile, ex);
                            }

                            // A read that fails partway would leave the file cut short inside a zip that still tests as
                            // sound, so the archive stops there
                            if (null != input)
                                using (input)
                                    try
                                    {
                                        // a zip holds dates from 1980 to 2107 only, so a date outside that is moved to the nearest end
                                        DateTime written = System.IO.File.GetLastWriteTime(item.file);
                                        DateTime zipDate = written < new DateTime(1980, 1, 1) ? new DateTime(1980, 1, 1)
                                                         : written > new DateTime(2107, 12, 31) ? new DateTime(2107, 12, 31)
                                                         : written;

                                        // a zip names folders with forward slashes
                                        System.IO.Compression.ZipArchiveEntry entry = zip.CreateEntry(item.entry.Replace(Path.DirectorySeparatorChar, '/'), System.IO.Compression.CompressionLevel.Optimal);

                                        entry.LastWriteTime = zipDate;
                                        using (Stream entryStream = entry.Open())
                                            input.CopyTo(entryStream);

                                        saved++;
                                    }
                                    catch (Exception ex)
                                    {
                                        damaged = true;
                                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Reading [{item.file}] failed partway, and [{target}] cannot be finished without it, so it was not saved.", null, GPALObjectType.GPALFile, ex);
                                    }
                        }
                }
                else
                {
                    SharpCompress.Common.ArchiveType type = ".7z" == format ? SharpCompress.Common.ArchiveType.SevenZip : SharpCompress.Common.ArchiveType.Tar;
                    SharpCompress.Common.Options.IWriterOptions options = ".7z" == format
                        ? new SharpCompress.Writers.SevenZip.SevenZipWriterOptions(SharpCompress.Common.CompressionType.LZMA2)
                        : (SharpCompress.Common.Options.IWriterOptions)new SharpCompress.Writers.WriterOptions(".tar.gz" == format ? SharpCompress.Common.CompressionType.GZip : SharpCompress.Common.CompressionType.None);

                    using (SharpCompress.Writers.IWriter writer = SharpCompress.Writers.WriterFactory.OpenWriter(output, type, options))
                        for (int index = 0; index < entries.Count && false == damaged; index++)
                        {
                            (string file, string entry) item = entries[index];
                            FileStream input = null;

                            // a file that cannot be opened has written nothing yet, so it is left out and the rest go in
                            try
                            {
                                input = System.IO.File.OpenRead(item.file);
                            }
                            catch (Exception ex)
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to add [{item.file}] to [{target}], it is saved without it.", null, GPALObjectType.GPALFile, ex);
                            }

                            // A read that fails partway has already written part of the file, which a tar or 7z cannot take
                            // back, and every file after it would be damaged, so the archive stops there
                            if (null != input)
                                using (input)
                                    try
                                    {
                                        // a tar and a 7z name folders with forward slashes
                                        writer.Write(item.entry.Replace(Path.DirectorySeparatorChar, '/'), input, System.IO.File.GetLastWriteTime(item.file));
                                        saved++;
                                    }
                                    catch (Exception ex)
                                    {
                                        damaged = true;
                                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Reading [{item.file}] failed partway, and [{target}] cannot be finished without it, so it was not saved.", null, GPALObjectType.GPALFile, ex);
                                    }
                        }
                }
            }

            // A damaged archive is removed rather than left looking finished. A 7z holding nothing gets 7-Zip's own empty
            // archive, since the writer only starts one when the first file goes in
            if (true == damaged)
                System.IO.File.Delete(target);
            else
            {
                if (".7z" == format && 0 == saved)
                    System.IO.File.WriteAllBytes(target, EMPTY_7Z);

                GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{saved}] file(s) saved to [{target}]");
            }
        }
        /// <summary>
        /// A 7z with no files in it, as 7-Zip writes one: the signature, version 0.4, and a start header pointing at no
        /// next header, with the CRC of those twenty zero bytes.
        /// </summary>
        private static readonly byte[] EMPTY_7Z =
        {
            0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04, 0x8D, 0x9B, 0xD5, 0x0F, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        /// <summary>
        /// Writes one file gzipped.
        /// </summary>
        /// <param name="file">File to compress</param>
        /// <param name="target">The .gz to write; one already there is replaced</param>
        internal static void WriteGz(string file, string target)
        {
            EnsureFolderFor(target);

            using (FileStream input = System.IO.File.OpenRead(file))
            using (FileStream output = new FileStream(target, FileMode.Create))
            using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress))
                input.CopyTo(gz);

            GPAL.PublishSimpleEvent(GPALEventType.INFO, $"[{file}] saved to [{target}]");
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
                directoryPath += "\\";
            }
            return directoryPath;
        }

    }
}

