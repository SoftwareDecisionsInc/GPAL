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
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static GenerallyPositive.Enums;


namespace GenerallyPositive
{
    /// <summary>
    /// GPAL File object instantied with GPAL.File<br/>
    /// Used to load tokens into a GPALGrid [rows/columns].
    /// </summary>
    public class GPALFile : IGPALFile, IGPALFileInternal
    {

        /// <summary>
        /// Backing settings object holding all file configuration, filenames, tokenized data, and grids.
        /// Accessed via the explicit <see cref="IGPALFileInternal"/> interface implementation.
        /// </summary>
        GPALFileSettings IGPALFileInternal.FileSettings { get; set; }

        /// <summary>
        /// Returns this <see cref="GPALFile"/> instance, allowing it to be passed where a generic GPAL object is expected.
        /// </summary>
        /// <returns>This <see cref="GPALFile"/> instance.</returns>
        public GPALFile ToGPALObject()
        {
            return this;
        }
        /// <summary>
        /// Initializes a new, empty <see cref="GPALFile"/> with default settings and no filenames.
        /// Use <see cref="GPAL.File"/> or implicit string conversion to create instances rather than calling this directly.
        /// </summary>
        internal GPALFile()
        {
            ((IGPALFileInternal)this).FileSettings = new GPALFileSettings();
            ((IGPALFileInternal)this).FileDictionary = new List<Dictionary<object, dynamic>>();
        }

        /// <summary>
        /// Initializes a new <see cref="GPALFile"/> pre-populated with the given filename.
        /// Use <see cref="GPAL.File"/> or implicit string conversion to create instances rather than calling this directly.
        /// </summary>
        /// <param name="fileName">The filename or path to add to the file list.</param>
        internal GPALFile(string fileName)
        {
            ((IGPALFileInternal)this).FileSettings = new GPALFileSettings();
            ((IGPALFileInternal)this).FileSettings.Filenames.Add(fileName);
            ((IGPALFileInternal)this).FileSettings.AddName(1);
            ((IGPALFileInternal)this).FileDictionary = new List<Dictionary<object, dynamic>>();
        }
        #region <Fluent Interface>
        /// <summary>
        /// Sets whether the destination file(s) should be overwritten or a new filename should be generated
        /// (used by <see cref="Next"/> when iterating multiple output files).
        /// </summary>
        /// <param name="overwriteFile">True to overwrite the existing file; false to generate a new filename. Not set, a
        /// new filename is generated.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithOverwriteFile(bool overwriteFile)
        {
            ((IGPALFileInternal)this).FileSettings.Give(nameof(GPALFileSettings.OverwriteFile), overwriteFile);
            return this;
        }
        /// <summary>
        /// Sets the order in which files matched by a wildcard pattern (e.g. <c>*.csv</c>) passed to
        /// <see cref="WithFileName(string)"/> are added to <see cref="Filenames"/>. Default is
        /// <see cref="Enums.FileSortOrder.NameAscending"/> (alphabetical).
        /// </summary>
        /// <param name="fileSortOrder">The order in which to sort wildcard-matched files.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithFileSortOrder(Enums.FileSortOrder fileSortOrder)
        {
            ((IGPALFileInternal)this).FileSettings.FileSortOrder = fileSortOrder;
            return this;
        }
        /// <summary>
        /// Sets how <see cref="Next"/> builds a new, unique filename when it needs one (the target file
        /// already exists, or Next is called past the user-supplied filenames). Default is
        /// <see cref="Enums.NextFilePattern.CounterPadded"/> (a zero-padded <c>_001</c> suffix).
        /// </summary>
        /// <param name="nextFilePattern">The naming pattern used to make each generated filename unique.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithNextFilePattern(Enums.NextFilePattern nextFilePattern = Enums.NextFilePattern.CounterPadded)
        {
            ((IGPALFileInternal)this).FileSettings.NextFilePattern = nextFilePattern;
            return this;
        }
        /// <summary>
        /// THe character delimiting each token in the row (columns)
        /// </summary>
        /// <param name="delimiter">Character delimiter<br/>[default comma]</param>
        /// <returns>Fluent interface to define more File settings</returns>
        public IAllowFileSettings WithDelimiter(char delimiter)
        {
            ((IGPALFileInternal)this).FileSettings.Give(nameof(GPALFileSettings.Delimiter), (char?)delimiter);
            return this;
        }
        /// <summary>
        /// Specifies whether each field (column value) in the file is enclosed in quotes.
        /// Applies to flat files (csv, txt, prn).
        /// </summary>
        /// <param name="fieldsInQuotes">True if fields are enclosed in quotes.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnsEnclosedInQuotes(bool fieldsInQuotes)
        {
            ((IGPALFileInternal)this).FileSettings.Give(nameof(GPALFileSettings.FieldsEnclosedInQuotes), fieldsInQuotes);
            return this;
        }
        /// <summary>
        /// Specifies that the first line of the file contains column header names.
        /// </summary>
        /// <param name="firstLineIsColumnNames">True if the first line of the file is a column header row.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithFirstLineIsColumnNames(bool firstLineIsColumnNames)
        {
            ((IGPALFileInternal)this).FileSettings.Give(nameof(GPALFileSettings.FirstLineIsColumnNames), firstLineIsColumnNames);
            return this;
        }
        /// <summary>
        /// Specifies that the first line of the file (a column header row) should be ignored/skipped when reading tokens.
        /// </summary>
        /// <param name="ignoreFirstLine">True to ignore the first line of the file. Default true.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithIgnoreFirstLineColumnNames(bool ignoreFirstLine = true)
        {
            ((IGPALFileInternal)this).FileSettings.Give(nameof(GPALFileSettings.IgnoreFirstLineColumnNames), ignoreFirstLine);
            return this;
        }
        /// <summary>
        /// Specifies that a single column name list has been defined and should be reused for every file in the file list,
        /// rather than requiring a separate column list per file.
        /// </summary>
        /// <param name="useHeaderForAllFiles">True to use the first column name list for all files.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithUseSameColumnNamesForAllFiles(bool useHeaderForAllFiles)
        {
            ((IGPALFileInternal)this).FileSettings.UseOneHeaderForAllFiles = useHeaderForAllFiles;
            return this;
        }
        /// <summary>
        /// Sets a browser whose session can reach a url a plain request cannot, such as a file behind a login or
        /// anti-bot check.<br/>
        /// Set it before .WithFileName, which downloads. The file is fetched from inside the page, or downloaded in a
        /// tab if the page cannot return it.
        /// </summary>
        /// <example>
        /// <code>
        ///     browser.GoTo("https://portal.example.com/login");
        ///     // ... sign in ...
        ///
        ///     GPALFile report = (GPALFile)GPAL.File
        ///         .WithBrowser(browser)
        ///         .WithFileName("https://portal.example.com/private/report.xlsx");
        /// </code>
        /// </example>
        /// <param name="browser">A browser already logged in or cleared for the url</param>
        /// <returns>Fluent interface to define more File settings</returns>
        public IAllowFileSettings WithBrowser(Browser.IBrowser browser)
        {
            ((IGPALFileInternal)this).FileSettings.Browser = browser;
            return this;
        }
        /// <summary>
        /// The filename to load input tokens from.<br/>
        /// An http or https url is downloaded here, in WithFileName, to GPAL's temp directory, and the local file is
        /// used from then on.<br/>
        /// A zip, gz, 7z, rar or tar in the path opens like a folder: export.zip\*.csv takes the csv files at the top of
        /// the zip out to GPAL's temp directory, and those are the files from then on. The archive is known by what it
        /// holds, so an .xpi, .jar or a download with no extension opens too. A gz holds one file, its name without
        /// .gz, so data.csv.gz\* is data.csv; a .tar.gz or .tgz opens as the tar it holds. A url works the same when a
        /// wildcard follows the archive: https://example.com/export.zip/*.csv is fetched, then opened.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPALFile versions = (GPALFile)GPAL.File
        ///         .WithFileName("https://googlechromelabs.github.io/chrome-for-testing/known-good-versions.json");
        ///
        ///     GPAL.Converter.WithInput(versions).SaveTo(ref grid);
        /// </code>
        /// </example>
        /// <param name="fileName">The filename, wildcard pattern, path into an archive, or an http/https url</param>
        /// <returns>Fluent interface to define more File settings</returns>
        public IAllowFileSettings WithFileName([FilePath] string fileName)
        {
            fileName = Environment.ExpandEnvironmentVariables(fileName);
            int filesBefore = ((IGPALFileInternal)this).FileSettings.Filenames.Count;

            // the path inside an archive a url names, as in https://example.com/export.zip/*.csv
            string inside = null;

            // Check for a url first; a query string's "?" looks like a wildcard.
            if (true == FileHelper.IsWebAddress(fileName))
            {
                // A wildcard after an archive such as .zip/ or .7z/ means inside it, as on disk: the archive is fetched,
                // then opened below. Without a wildcard the whole url is fetched, so a link like .../tool.zip/download fetches the link.
                // Only the path is looked at, since .zip is also a domain, and a query string such as a signature
                // goes with the archive's url
                System.Text.RegularExpressions.Match throughArchive = System.Text.RegularExpressions.Regex.Match(fileName, @"^(https?://[^/?#]+/[^?#]*?\.(?:" + FileHelper.ArchiveExtensionPattern() + @"))/([^?#]*\*[^?#]*)(\?[^#]*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (true == throughArchive.Success)
                {
                    fileName = throughArchive.Groups[1].Value + throughArchive.Groups[3].Value;
                    inside = throughArchive.Groups[2].Value;
                }

                Browser.IBrowser browser = ((IGPALFileInternal)this).FileSettings.Browser;
                string name = Path.GetFileName(new Uri(fileName).LocalPath);
                string localPath = Path.Combine(GPAL.TempDirectory(), true == string.IsNullOrWhiteSpace(name) ? "download" : name);

                ((IGPALFileInternal)this).FileSettings.SourceUrl = fileName;

                // With a browser, it gets the file: fetched in the page, or downloaded in a tab. Without one, a plain
                // request.
                // A failed fetch leaves whatever an earlier one put at the same path, which would read as this one's
                // file, so it is removed. A copy still current counts as fetched and stays.
                if (null != browser && false == FileHelper.FetchOrDownloadInTab(fileName, localPath, browser) && true == File.Exists(localPath))
                {
                    File.Delete(localPath);
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"[{fileName}] did not arrive, so the copy an earlier fetch left at [{localPath}] was removed rather than used", this, Enums.GPALObjectType.GPALFile);
                }
                else if (null == browser)
                    FileHelper.DownloadToFile(localPath, fileName);

                fileName = null == inside ? localPath : localPath + Path.DirectorySeparatorChar + inside;
            }

            // A zip, gz, 7z, rar or tar in the middle of the path is opened like a folder, and the files named inside it are taken
            // out to GPAL's temp directory. From here on they are ordinary files
            string archive = null;
            string[] matches = null;

            try
            {
                archive = FileHelper.ArchiveInPath(fileName);

                if (null != archive)
                {
                    List<string> extracted = FileHelper.ExtractFromArchive(archive, fileName.Substring(archive.Length + 1));

                    foreach (string file in extracted)
                        ((IGPALFileInternal)this).FileSettings.ExtractedFrom[file] = archive;

                    matches = extracted.ToArray();
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to open [{archive ?? fileName}] as an archive.", this, Enums.GPALObjectType.GPALFile, ex);
                matches = new string[0];
            }

            // a url that gave no zip or gz, such as an error page or a fetch that failed, has nothing inside it
            if (null == matches && null != inside)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"[{((IGPALFileInternal)this).FileSettings.SourceUrl.Split('?')[0]}] did not give an archive, so nothing inside it could be opened.", this, Enums.GPALObjectType.GPALFile);
                matches = new string[0];
            }
            else if (null == matches && (true == fileName.Contains('*') || true == fileName.Contains('?')))
            {
                string directory = Path.GetDirectoryName(fileName);
                string filenamePart = Path.GetFileName(fileName);
                string folderToSearch = false == string.IsNullOrEmpty(directory) ? directory : @".\";

                // a folder that is not there, or a file in the path that is not an archive, matches nothing
                matches = true == Directory.Exists(folderToSearch) ? Directory.GetFiles(folderToSearch, filenamePart) : new string[0];
            }

            if (null != matches)
            {
                if (0 == matches.Length)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"No files matched the wildcard pattern [{fileName}].", this, Enums.GPALObjectType.GPALFile);
                }
                else
                {
                    IEnumerable<string> sortedMatches;

                    switch (((IGPALFileInternal)this).FileSettings.FileSortOrder)
                    {
                        case Enums.FileSortOrder.NameDescending:
                            sortedMatches = matches.OrderByDescending(m => m, StringComparer.OrdinalIgnoreCase);
                            break;
                        case Enums.FileSortOrder.NaturalAscending:
                            sortedMatches = matches.OrderBy(m => m, NaturalFilenameComparer.Instance);
                            break;
                        case Enums.FileSortOrder.NaturalDescending:
                            sortedMatches = matches.OrderByDescending(m => m, NaturalFilenameComparer.Instance);
                            break;
                        case Enums.FileSortOrder.DateModifiedAscending:
                            sortedMatches = matches.OrderBy(m => File.GetLastWriteTimeUtc(m));
                            break;
                        case Enums.FileSortOrder.DateModifiedDescending:
                            sortedMatches = matches.OrderByDescending(m => File.GetLastWriteTimeUtc(m));
                            break;
                        case Enums.FileSortOrder.DateCreatedAscending:
                            sortedMatches = matches.OrderBy(m => File.GetCreationTimeUtc(m));
                            break;
                        case Enums.FileSortOrder.DateCreatedDescending:
                            sortedMatches = matches.OrderByDescending(m => File.GetCreationTimeUtc(m));
                            break;
                        case Enums.FileSortOrder.NameAscending:
                        default:
                            sortedMatches = matches.OrderBy(m => m, StringComparer.OrdinalIgnoreCase);
                            break;
                    }

                    foreach (string match in sortedMatches)
                    {
                        ((IGPALFileInternal)this).FileSettings.Filenames.Add(match);
                        ((IGPALFileInternal)this).FileSettings.FileCount++;
                    }
                }
            }
            else
            {
                ((IGPALFileInternal)this).FileSettings.Filenames.Add(fileName);
                ((IGPALFileInternal)this).FileSettings.FileCount++;
            }

            // the settings given after this name go to every file it added
            ((IGPALFileInternal)this).FileSettings.AddName(((IGPALFileInternal)this).FileSettings.Filenames.Count - filesBefore);

            return this;
        }
        /// <summary>
        /// Gives the current unit a list of column names: the next name's, or, when every name of the unit has its
        /// own, in place of the last list given, with a warning.
        /// </summary>
        /// <param name="columnNames">The column names</param>
        private void GiveColumnNames(List<string> columnNames)
        {
            GPALFileSettings fileSettings = ((IGPALFileInternal)this).FileSettings;
            List<object> given = fileSettings.GivenInCurrentUnit(nameof(GPALFileSettings.ColumnList));

            if (0 < given.Count && given.Count >= fileSettings.NamesInCurrentUnit())
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Overwriting existing column names [{string.Join(", ", (List<string>)given[given.Count - 1])}] with new names [{string.Join(", ", columnNames)}]");
                given[given.Count - 1] = columnNames;
            }
            else
                given.Add(columnNames);

            fileSettings.ApplyUnits();
        }
        /// <summary>
        /// .WithColumnName adds a column name to the last column name list given for the file names before it, and
        /// starts one when none was given.
        /// </summary>
        /// <param name="columnName">The column name to append to the current file's column name list.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnName(string columnName)
        {
            GPALFileSettings fileSettings = ((IGPALFileInternal)this).FileSettings;
            List<object> given = fileSettings.GivenInCurrentUnit(nameof(GPALFileSettings.ColumnList));

            if (0 == given.Count)
                given.Add(new List<string>());

            ((List<string>)given[given.Count - 1]).Add(columnName);
            fileSettings.ApplyUnits();
            return this;
        }
        /// <summary>
        /// Gives the file names before it a column name list, the names separated by commas. Each list goes to the next of
        /// those names in order, the last list given covers the rest of them, and a name's list goes to every file it
        /// added. Given again once each of those names has a list, it replaces the last list, with a warning.
        /// <see cref="WithUseSameColumnNamesForAllFiles(bool)"/> uses the first column name list for all files
        /// </summary>
        /// <param name="columnNames">A comma-delimited string of column names.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnNames(string columnNames)
        {
            GiveColumnNames(columnNames.Split(',').Select(columnName => columnName.Trim()).ToList());
            return this;
        }
        /// <summary>
        /// Gives the file names before it a column name list. Each list goes to the next of those names in order, the
        /// last list given covers the rest of them, and a name's list goes to every file it added. Given again once each
        /// of those names has a list, it replaces the last list, with a warning.
        /// <see cref="WithUseSameColumnNamesForAllFiles(bool)"/> uses the first column name list for all files
        /// </summary>
        /// <param name="columnNames">One column name per array element.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnNames(string[] columnNames)
        {
            GiveColumnNames(new List<string>(columnNames));
            return this;
        }
        #endregion <Fluent Interface>

        #region <Getters/Setters>
        /// <summary>
        /// Returns the first filename in <see cref="Filenames"/> and resets the <see cref="Next"/> cursor
        /// so that a subsequent call to <see cref="Next"/> returns the second filename.
        /// </summary>
        public string First
        {
            get
            {
                if (0 < ((IGPALFileInternal)this).FileSettings.FileCount)
                {
                    ((IGPALFileInternal)this).FileSettings.NextFileCount = 0;
                    return ((IGPALFileInternal)this).FileSettings.Filenames[0];
                }
                else
                    return null;
            }
        }
        /// <summary>
        /// Returns the last filename in <see cref="Filenames"/> and positions the <see cref="Next"/> cursor
        /// at that filename so that a subsequent call to <see cref="Next"/> generates a new filename.
        /// </summary>
        public string Last
        {
            get
            {
                if (0 < ((IGPALFileInternal)this).FileSettings.FileCount)
                {
                    ((IGPALFileInternal)this).FileSettings.NextFileCount = ((IGPALFileInternal)this).FileSettings.Filenames.Count - 1;
                    return ((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.Filenames.Count - 1];
                }
                else
                    return null;
            }
        }
        /// <summary>
        /// Advances an internal cursor and returns the next filename to use. If there is exactly one file
        /// and <see cref="WithOverwriteFile(bool)"/> is true for it, that same filename is returned repeatedly.
        /// Otherwise, while unread filenames remain in <see cref="Filenames"/>, those are returned in order.
        /// Once the list is exhausted, a new filename is generated from the first filename's pattern via
        /// <see cref="FileHelper.CleanUpFileDestination(string, out string, bool)"/>, appended to <see cref="Filenames"/>,
        /// and returned - allowing repeated calls to <see cref="Next"/> to keep producing new output filenames.
        /// </summary>
        public GPALFile Next
        {
            get
            {
                string fullPath = null;
                string newFilename = null;
                Enums.NextFilePattern nextPattern = ((IGPALFileInternal)this).FileSettings.NextFilePattern;
                int patternCounter = ((IGPALFileInternal)this).FileSettings.NextPatternCounter;

                if (0 == ((IGPALFileInternal)this).FileSettings.OverwriteFile.Count)    // default to not overwrite since user did not specify but called next
                {
                    if (((IGPALFileInternal)this).FileSettings.NextFileCount < ((IGPALFileInternal)this).FileSettings.FileCount) // otherwise get the next filename in the list
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                        ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                        // ReturnFilenames lists every file written, uniquified or not.
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        ((IGPALFileInternal)this).FileSettings.NextFileCount++;
                        return WithSettingsOf(fullPath, ((IGPALFileInternal)this).FileSettings.NextFileCount - 1);
                    }
                    else    // Next called past the user supplied filenames: generate a new filename from the last one supplied, on every call
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount-1], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                        ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        //((IGPALFileInternal)this).FileSettings.NextFileCount++;
                        return WithSettingsOf(fullPath, ((IGPALFileInternal)this).FileSettings.NextFileCount - 1);
                    }
                }
                // do the same but respect the overwrite flag settings
                else if (((IGPALFileInternal)this).FileSettings.NextFileCount < ((IGPALFileInternal)this).FileSettings.OverwriteFile.Count)
                {
                    if (true == ((IGPALFileInternal)this).FileSettings.OverwriteFile[((IGPALFileInternal)this).FileSettings.NextFileCount])   // file flagged to overwrite - always return the same filename
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount++], out newFilename, DeleteFileBeforeDownload: true);
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        return WithSettingsOf(fullPath, ((IGPALFileInternal)this).FileSettings.NextFileCount - 1);
                    }
                    // a remaining user-supplied filename takes priority over generating a new one
                    else if (((IGPALFileInternal)this).FileSettings.NextFileCount < ((IGPALFileInternal)this).FileSettings.FileCount)
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                        ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                        // ReturnFilenames lists every file written, uniquified or not.
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        ((IGPALFileInternal)this).FileSettings.NextFileCount++;
                        return WithSettingsOf(fullPath, ((IGPALFileInternal)this).FileSettings.NextFileCount - 1);
                    }
                }

                // Past the supplied names, generate from the last supplied name. Filenames holds only supplied names,
                // so a generated name is never the pattern.
                int patternIdx = Math.Min(((IGPALFileInternal)this).FileSettings.NextFileCount, ((IGPALFileInternal)this).FileSettings.FileCount - 1);

                fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[patternIdx], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                //((IGPALFileInternal)this).FileSettings.NextFileCount++;
                return WithSettingsOf(fullPath, patternIdx);
            }
        }
        /// <summary>
        /// The file <see cref="Next"/> returns: the filename it worked out, with the delimiter, quoting, header line and
        /// column names given for the filename it came from, so a file reached through Next is written and read as that
        /// file is.
        /// </summary>
        /// <param name="nextFilename">The filename Next worked out</param>
        /// <param name="fileIdx">The place in <see cref="Filenames"/> of the filename it came from</param>
        /// <returns>The file for the filename, with that file's settings</returns>
        private GPALFile WithSettingsOf(string nextFilename, int fileIdx)
        {
            GPALFileSettings settings = ((IGPALFileInternal)this).FileSettings;
            GPALFile nextFile = nextFilename;

            // a list holds an entry for each file only when one of its files was given that setting
            if (fileIdx < settings.Delimiter.Count && true == settings.Delimiter[fileIdx].HasValue)
                nextFile.WithDelimiter(settings.Delimiter[fileIdx].Value);
            if (fileIdx < settings.FieldsEnclosedInQuotes.Count && true == settings.FieldsEnclosedInQuotes[fileIdx])
                nextFile.WithColumnsEnclosedInQuotes(true);
            if (fileIdx < settings.IgnoreFirstLineColumnNames.Count && true == settings.IgnoreFirstLineColumnNames[fileIdx])
                nextFile.WithIgnoreFirstLineColumnNames(true);
            if (fileIdx < settings.ColumnList.Count() && 0 < settings.ColumnList[fileIdx].Count)
                nextFile.WithColumnNames(settings.ColumnList[fileIdx].ToArray());
            if (fileIdx < settings.FirstLineGiven.Count && true == settings.FirstLineGiven[fileIdx])
                nextFile.WithFirstLineIsColumnNames(settings.FirstLineIsColumnNames[fileIdx]);

            return nextFile;
        }
        /// <summary>
        /// We have only one file, accessing it
        /// </summary>
        public string Filename
        {
            get
            {
                // A wildcard that matched nothing leaves Filenames empty; return an empty string. WithFileName already
                // warned.
                List<string> filenames = ((IGPALFileInternal)this).FileSettings.Filenames;

                return 0 == filenames.Count ? string.Empty : filenames[0];
            }
        }
        /// <summary>
        /// The folder of every filename, in the same order as <see cref="Filenames"/>, so a wildcard that matched
        /// across folders still lines up one to one.
        /// </summary>
        public List<string> DirectoryParts
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.Filenames.Select(filename => Path.GetDirectoryName(filename)).ToList();
            }
        }
        /// <summary>
        /// The name and extension of every filename, without its folder, in the same order as <see cref="Filenames"/>.
        /// </summary>
        public List<string> FileParts
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.Filenames.Select(filename => Path.GetFileName(filename)).ToList();
            }
        }
        /// <summary>
        /// The folder of the first filename, since most files are singular. Same shortcut <see cref="Filename"/> is
        /// to <see cref="Filenames"/>.
        /// </summary>
        public string DirectoryPart
        {
            get
            {
                // Path.GetDirectoryName throws on an empty path, which is what a no-match wildcard leaves
                string filename = Filename;

                return 0 == filename.Length ? string.Empty : Path.GetDirectoryName(filename);
            }
        }
        /// <summary>
        /// The name and extension of the first filename, without its folder. Useful for putting the same name in a
        /// different folder, for instance keeping a copy of a downloaded file somewhere that is not disposable.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPALFile archive = (GPALFile)GPAL.File
        ///         .WithFileName(Path.Combine(@"c:\history", versions.FilePart))
        ///         .WithNextFilePattern(NextFilePattern.CounterPadded);
        ///
        ///     versions.CopyTo(archive.Next);
        /// </code>
        /// </example>
        public string FilePart
        {
            get
            {
                string filename = Filename;

                return 0 == filename.Length ? string.Empty : Path.GetFileName(filename);
            }
        }
        /// <summary>
        /// Get the list of filenames
        /// </summary>
        public List<string> Filenames
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.Filenames;
            }
        }
        /// <summary>
        /// Get the list of filenames saved to (returned)
        /// </summary>
        public List<string> ReturnFilenames
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.ReturnFilenames;
            }
        }
        /// <summary>
        /// The url this file was fetched from, when .WithFileName was given one. Null for a file that was already
        /// on disk.<br/>
        /// Kept so a workflow can tell a downloaded file from a local one, and knows what it is a copy of.
        /// </summary>
        public string SourceUrl
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.SourceUrl;
            }
        }
        /// <summary>
        /// Tokens loaded from the file.
        /// Tokens are loaded upon calling any FromFilee() method (Append/FillIn/Insert).
        /// </summary>
        /// <returns>A GPALGrid of tokens</returns>
        List<IGPALGrid<string>> IGPALFileInternal.TokenList
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.TokenList;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.TokenList = value;
            }
        }
        /// <summary>
        /// Raw lines read from the file(s), populated when the file is loaded/tokenized.
        /// </summary>
        List<string> IGPALFileInternal.FileData
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.FileData;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.FileData = value;
            }
        }
        /// <summary>
        /// Per-file field delimiter characters, as configured via <see cref="WithDelimiter(char)"/>.
        /// </summary>
        List<char?> IGPALFileInternal.Delimiter
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.Delimiter;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.Delimiter = value;
            }
        }
        /// <summary>
        /// Per-file flags indicating whether fields are enclosed in quotes, as configured via
        /// <see cref="WithColumnsEnclosedInQuotes(bool)"/>.
        /// </summary>
        List<bool> IGPALFileInternal.FieldsEnclosedInQuotes
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.FieldsEnclosedInQuotes;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.FieldsEnclosedInQuotes = value;
            }
        }
        /// <summary>
        /// Per-file flags indicating whether the first line of the file contains column names, as configured
        /// via <see cref="WithFirstLineIsColumnNames(bool)"/>.
        /// </summary>
        List<bool> IGPALFileInternal.FirstLineIsColumnNames
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames = value;
            }
        }
        /// <summary>
        /// Per-file flags indicating whether the first line's column names should be ignored when
        /// loading data, as configured via <see cref="WithIgnoreFirstLineColumnNames(bool)"/>.
        /// </summary>
        List<bool> IGPALFileInternal.IgnoreFirstLineColumnNames
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.IgnoreFirstLineColumnNames;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.IgnoreFirstLineColumnNames = value;
            }
        }
        /// <summary>
        /// Whether all files share a single set of column names, as configured via
        /// <see cref="WithUseSameColumnNamesForAllFiles(bool)"/>.
        /// </summary>
        bool IGPALFileInternal.UseOneHeaderForAllFiles
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.UseOneHeaderForAllFiles;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.UseOneHeaderForAllFiles = value;
            }
        }
        /// <summary>
        /// File data parsed into a list of dictionaries (one per row, keyed by column name).
        /// </summary>
        List<Dictionary<object, dynamic>> IGPALFileInternal.FileDictionary
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.FileDictionary;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.FileDictionary = value;
            }
        }
        /// <summary>
        /// File data parsed into a list of <see cref="IGPALGrid{T}"/> grids (one per file).
        /// </summary>
        List<IGPALGrid<string>> IGPALFileInternal.FileGrid
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.FileGrid;
            }
            set
            {
                ((IGPALFileInternal)this).FileSettings.FileGrid = value;
            }
        }
        /// <summary>
        /// Whether the file's contents have already been tokenized into <see cref="IGPALFileInternal.TokenList"/>,
        /// to avoid re-parsing on subsequent operations.
        /// </summary>
        bool IGPALFileInternal.AlreadyTokenized
        {
            get
            {
                return ((IGPALFileInternal)this).FileSettings.AlreadyTokenized;
            }

            set
            {
                ((IGPALFileInternal)this).FileSettings.AlreadyTokenized = value;
            }
        }
        /// <summary>
        /// The number of filenames in <see cref="Filenames"/>.
        /// </summary>
        public int Count
        {
            get => Filenames.Count;
        }
        #endregion <Getters/Setters>
        #region Helpers
        /// <summary>
        /// Copies each file in <see cref="Filenames"/> to <paramref name="destination"/>. If
        /// <paramref name="destination"/> is a directory (per <see cref="IsDirectoryOnly(string)"/>),
        /// each matched file is copied into that directory under its own name; otherwise all matched
        /// files are copied to the single <paramref name="destination"/> path. A file already there is replaced when
        /// <see cref="WithOverwriteFile(bool)"/> is true for that file; otherwise, set false or not set, the copy gets
        /// the next free name, as <see cref="Next"/> gives one. Errors are reported via
        /// <see cref="GPAL.PublishSimpleEvent"/> rather than thrown.
        /// </summary>
        /// <param name="destination">The destination file path or directory.</param>
        public void CopyTo(string destination)
        {
            string myDestination = destination;
            bool isDir = IsDirectoryOnly(destination);
            List<bool> overwriteFlags = ((IGPALFileInternal)this).FileSettings.OverwriteFile;
            int index = 0;

            foreach (string filename in ((IGPALFileInternal)this).FileSettings.Filenames)
            {
                // the flag WithOverwriteFile gave this file, by position; not given means a new name
                bool overwrite = index < overwriteFlags.Count && true == overwriteFlags[index];
                index++;

                try
                {
                    string directory = Path.GetDirectoryName(filename);
                    string filenamepart = Path.GetFileName(filename);

                    foreach (string filename2 in Directory.GetFiles(false == string.IsNullOrEmpty(directory) ? directory : @".\", filenamepart))
                    {
                        if (true == isDir)
                            myDestination = Path.Combine(destination, Path.GetFileName(filename2));

                        // Not overwriting: a name already taken becomes the next free one rather than an error
                        if (false == overwrite)
                        {
                            int patternCounter = ((IGPALFileInternal)this).FileSettings.NextPatternCounter;
                            myDestination = FileHelper.CleanUpFileDestination(myDestination, out _, false, ((IGPALFileInternal)this).FileSettings.NextFilePattern, ref patternCounter);
                            ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                        }

                        FileHelper.EnsureFolderFor(myDestination);
                        File.Copy(filename2, myDestination, overwrite);
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{filename2}] copied to [{myDestination}].", this, Enums.GPALObjectType.GPALFile);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to copy [{filename}] to [{destination}].", this, Enums.GPALObjectType.GPALFile, ex);
                }
            }
        }
        /// <summary>
        /// Saves the files as an archive, the destination's extension saying which: a zip (.zip, and the other names in
        /// zipExtensions in gpal.yaml such as .xpi and .jar), a gz (.gz, and those in gzExtensions), a .tar, or a .tar.gz
        /// or .tgz, or a .7z. A rar can be opened but not written.<br/>
        /// A folder goes in whole with its subfolders, its contents at the top of the archive.<br/>
        /// A named destination is one target: reports.zip holds every file. A .gz holds only one file, so reports.csv.gz
        /// is the first and the rest are numbered from it, reports_0001.csv.gz and on, with the
        /// <see cref="WithNextFilePattern"/> counter pattern; a date or time pattern would repeat a name, so it counts. A
        /// folder cannot go in a .gz.<br/>
        /// A wildcard keeps each one's own name, as copy does: *.gz makes a.csv.gz, b.csv.gz, and *.zip a zip per file or
        /// folder.<br/>
        /// With no folder the result goes beside the source; for a file taken out of an archive, beside that archive.<br/>
        /// <see cref="WithOverwriteFile(bool)"/> true replaces what is there; false, or not set, takes the next free
        /// name. Every file written is added to <see cref="ReturnFilenames"/>.
        /// </summary>
        /// <example>
        /// <code>
        ///     ((GPALFile)GPAL.File.WithFileName(@"C:\reports\*.csv")).SaveTo(@"C:\out\reports.zip");
        ///     ((GPALFile)GPAL.File.WithFileName(@"C:\source\extension\")).SaveTo(@"C:\out\extension.xpi");
        ///     ((GPALFile)GPAL.File.WithFileName(@"C:\work\export.7z\*.csv")).SaveTo("*.gz");
        /// </code>
        /// </example>
        /// <param name="destination">An archive name, or *.zip, *.gz and the like to keep each one's name</param>
        /// <returns>Fluent interface to act on the same files again</returns>
        public IAllowFileActions SaveTo(string destination)
        {
            GPALFileSettings settings = ((IGPALFileInternal)this).FileSettings;
            List<string> files = settings.Filenames;
            string name = Path.GetFileName(destination);
            string folder = Path.GetDirectoryName(destination);
            string format = FileHelper.ArchiveFormat(name);
            bool eachFile = name.Contains('*');

            // what a wildcard adds to each name: *.gz adds .gz, *.tar.gz adds .tar.gz
            string suffix = true == eachFile ? name.Substring(name.IndexOf('*') + 1) : string.Empty;

            if (null == format)
                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{destination}] is not an archive GPAL writes (zip, gz, tar, tar.gz or 7z), so nothing was saved. CopyTo copies files as they are.", this, Enums.GPALObjectType.GPALFile);
            else if (".rar" == format)
                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{destination}] is a rar, which GPAL opens but does not write, so nothing was saved. Save it as .zip, .7z or .tar.gz.", this, Enums.GPALObjectType.GPALFile);
            else if (0 == files.Count)
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"No files to save to [{destination}].", this, Enums.GPALObjectType.GPALFile);
            else if (".gz" != format && false == eachFile)
            {
                // one archive holding everything, so one overwrite flag, the first
                bool overwrite = 0 < settings.OverwriteFile.Count && true == settings.OverwriteFile[0];
                string target = Path.Combine(FolderFor(files[0], folder), name);
                string requested = target;

                try
                {
                    if (false == overwrite)
                    {
                        int patternCounter = settings.NextPatternCounter;
                        target = FileHelper.CleanUpFileDestination(target, out _, false, settings.NextFilePattern, ref patternCounter);
                        settings.NextPatternCounter = patternCounter;
                    }

                    // a folder saved into itself leaves out this archive and the ones earlier runs numbered from it
                    List<(string file, string entry)> entries = FileHelper.ArchiveEntries(files, requested);

                    // replacing a file the archive is made from would empty it before it is read
                    if (true == entries.Any(item => true == SamePath(item.file, target)))
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{target}] is one of the files being saved, so nothing was saved.", this, Enums.GPALObjectType.GPALFile);
                    else
                    {
                        // an archive a failed read damaged is removed, so it is not returned
                        FileHelper.WriteArchive(entries, target, format);
                        if (true == File.Exists(target))
                            settings.ReturnFilenames.Add(target);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to save to [{target}].", this, Enums.GPALObjectType.GPALFile, ex);
                }
            }
            else
            {
                // numbers the named .gz files after the first when overwriting, so the same names are replaced every run
                int overwriteCounter = 0;

                for (int index = 0; index < files.Count; index++)
                {
                    string source = files[index];
                    string sourceName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

                    // A wildcard makes one target per file, so each takes its own flag, as CopyTo does. A name is one
                    // target, so the first flag
                    int flagIndex = true == eachFile ? index : 0;
                    bool overwrite = flagIndex < settings.OverwriteFile.Count && true == settings.OverwriteFile[flagIndex];
                    string target = true == eachFile
                                  ? Path.Combine(FolderFor(source, folder), sourceName + suffix)
                                  : Path.Combine(FolderFor(files[0], folder), name);
                    string requested = target;

                    // The files after the first in a named .gz are numbered. A date or time pattern would give several files
                    // the same name, each replacing the one before, so they take the padded counter instead
                    Enums.NextFilePattern pattern = false == eachFile && 0 < index
                                                  && Enums.NextFilePattern.Counter != settings.NextFilePattern
                                                  && Enums.NextFilePattern.CounterPadded != settings.NextFilePattern
                                                  ? Enums.NextFilePattern.CounterPadded
                                                  : settings.NextFilePattern;

                    try
                    {
                        if (true == overwrite && false == eachFile && 0 < index)
                        {
                            string baseName = FileHelper.NameWithoutExtension(target, out string fullExtension);
                            target = Path.Combine(Path.GetDirectoryName(target), baseName + FileHelper.NextFileSuffix(pattern, ref overwriteCounter) + fullExtension);
                        }
                        else if (false == overwrite)
                        {
                            // a name already taken, by an earlier run or the file before this one, becomes the next free one
                            int patternCounter = settings.NextPatternCounter;
                            target = FileHelper.CleanUpFileDestination(target, out _, false, pattern, ref patternCounter);
                            settings.NextPatternCounter = patternCounter;
                        }

                        // a folder saved into itself leaves out this archive and its earlier numbered ones
                        List<(string file, string entry)> entries = FileHelper.ArchiveEntries(new List<string> { source }, requested);

                        // Replacing a source would empty it before it is read. A wildcard target is made from this source
                        // alone, so only it can be hit; a named target can be any of them
                        if (".gz" == format && true == Directory.Exists(source))
                            GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{source}] is a folder and a gz holds one file, so it was not saved. Save a folder as .zip, .7z or .tar.gz.", this, Enums.GPALObjectType.GPALFile);
                        else if (true == (true == eachFile ? entries.Select(item => item.file).ToList() : files).Any(file => true == SamePath(file, target)))
                            GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{target}] is one of the files being saved, so [{source}] was not saved over it.", this, Enums.GPALObjectType.GPALFile);
                        else
                        {
                            if (".gz" == format)
                                FileHelper.WriteGz(source, target);
                            else
                                FileHelper.WriteArchive(entries, target, format);

                            // an archive a failed read damaged is removed, so it is not returned
                            if (true == File.Exists(target))
                                settings.ReturnFilenames.Add(target);
                        }
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to save [{source}] to [{target}].", this, Enums.GPALObjectType.GPALFile, ex);
                    }
                }
            }

            return this;
        }
        /// <summary>
        /// True when two paths name the same file.
        /// </summary>
        /// <param name="first">One path</param>
        /// <param name="second">The other</param>
        /// <returns>True when they are the same file</returns>
        private static bool SamePath(string first, string second)
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
        }
        /// <summary>
        /// The folder SaveTo writes into: the one the destination named, or with none, the source's own folder, a
        /// folder's being the one it sits in. A file taken out of an archive counts the archive as its source, since its
        /// own folder is GPAL's temp directory.
        /// </summary>
        /// <param name="file">The source file or folder</param>
        /// <param name="folder">The destination's folder, empty when it named none</param>
        /// <returns>The folder to write into</returns>
        private string FolderFor(string file, string folder)
        {
            string source = ((IGPALFileInternal)this).FileSettings.ExtractedFrom.TryGetValue(file, out string archive) ? archive : file;

            return true == string.IsNullOrEmpty(folder) ? Path.GetDirectoryName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : folder;
        }
        /// <summary>
        /// Moves each file in <see cref="Filenames"/> to <paramref name="destination"/>. If
        /// <paramref name="destination"/> is a directory (per <see cref="IsDirectoryOnly(string)"/>),
        /// each matched file is moved into that directory under its own name; otherwise all matched
        /// files are moved to the single <paramref name="destination"/> path. A file already there is replaced when
        /// <see cref="WithOverwriteFile(bool)"/> is true for that file; otherwise, set false or not set, the file gets
        /// the next free name, as <see cref="Next"/> gives one. Errors are reported via
        /// <see cref="GPAL.PublishSimpleEvent"/> rather than thrown.
        /// </summary>
        /// <param name="destination">The destination file path or directory.</param>
        public void MoveTo(string destination)
        {
            string myDestination = destination;
            bool isDir = IsDirectoryOnly(destination);
            List<bool> overwriteFlags = ((IGPALFileInternal)this).FileSettings.OverwriteFile;
            int index = 0;

            foreach (string filename in ((IGPALFileInternal)this).FileSettings.Filenames)
            {
                // the flag WithOverwriteFile gave this file, by position; not given means a new name
                bool overwrite = index < overwriteFlags.Count && true == overwriteFlags[index];
                index++;

                try
                {
                    string directory = Path.GetDirectoryName(filename);
                    string filenamepart = Path.GetFileName(filename);

                    foreach (string filename2 in Directory.GetFiles(false == string.IsNullOrEmpty(directory) ? directory : @".\", filenamepart))
                    {
                        if (true == isDir)
                            myDestination = Path.Combine(destination, Path.GetFileName(filename2));

                        // File.Move cannot replace, so overwriting removes what is there first; not overwriting, a name
                        // already taken becomes the next free one rather than an error
                        int patternCounter = ((IGPALFileInternal)this).FileSettings.NextPatternCounter;
                        myDestination = true == overwrite
                                      ? FileHelper.CleanUpFileDestination(myDestination, out _, true)
                                      : FileHelper.CleanUpFileDestination(myDestination, out _, false, ((IGPALFileInternal)this).FileSettings.NextFilePattern, ref patternCounter);
                        ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;

                        FileHelper.EnsureFolderFor(myDestination);
                        File.Move(filename2, myDestination);
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{filename2}] moved to [{myDestination}].", this, Enums.GPALObjectType.GPALFile);
                    }
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to move [{filename}] to [{destination}].", this, Enums.GPALObjectType.GPALFile, ex);
                }
            }
        }
        /// <summary>
        /// Deletes the file at <paramref name="destination"/>, or if it is a directory (per
        /// <see cref="IsDirectoryOnly(string)"/>), deletes every file directly within that directory.
        /// Errors are reported via <see cref="GPAL.PublishSimpleEvent"/> rather than thrown.
        /// </summary>
        /// <param name="destination">The file or directory to delete.</param>
        public void Delete(string destination)
        {
            string myDestination = destination;
            bool isDir = IsDirectoryOnly(destination);

            try
            {
                if (true == isDir)
                    foreach (string filename in Directory.GetFiles(destination))
                    {
                        myDestination = Path.Combine(destination, filename);
                        File.Delete(Path.Combine(myDestination, filename));
                        GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{myDestination}] deleted.", this, Enums.GPALObjectType.GPALFile);
                    }
                else
                {
                    File.Delete(myDestination);
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{myDestination}] deleted.", this, Enums.GPALObjectType.GPALFile);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to delete[{myDestination}].", this, Enums.GPALObjectType.GPALFile, ex);
            }
        }
        /// <summary>
        /// Deletes every file in <see cref="Filenames"/>. Errors are reported via
        /// <see cref="GPAL.PublishSimpleEvent"/> rather than thrown.
        /// </summary>
        public void Delete()
        {
            foreach (string filename in ((IGPALFileInternal)this).FileSettings.Filenames)
            {
                try
                {
                    File.Delete(filename);
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{filename}] deleted.", this, Enums.GPALObjectType.GPALFile);
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, $"Unable to delete [{filename}].", this, Enums.GPALObjectType.GPALFile, ex);
                }
            }
        }
        /// <summary>
        /// Determines whether <paramref name="path"/> refers to a directory rather than a file, and ensures
        /// that directory exists, creating it (and its parents) if necessary. A path is treated as a directory
        /// if it ends with a directory separator, is an existing directory, or has no file extension.
        /// As a side effect, the directory portion of <paramref name="path"/> is created via
        /// <see cref="Directory.CreateDirectory(string)"/> if it does not already exist.
        /// </summary>
        /// <param name="path">The file or directory path to check.</param>
        /// <returns><c>true</c> if <paramref name="path"/> is treated as a directory; otherwise <c>false</c>.</returns>
        internal static bool IsDirectoryOnly(string path)
        {
            // A directory if path ends with a separator, exists as a directory, or has no extension. CAVEAT: a
            // directory name with a dot looks like a file; create the directory first.
            bool isDirectory = true == path.EndsWith(Path.DirectorySeparatorChar.ToString())
                || true == path.EndsWith(Path.AltDirectorySeparatorChar.ToString())
                || true == Directory.Exists(path)
                || false == Path.HasExtension(path);

            // Create the folder first: the path for a directory, its parent for a file.
            string directoryToCreate = true == isDirectory ? path : Path.GetDirectoryName(path);

            if (false == string.IsNullOrEmpty(directoryToCreate) && false == Directory.Exists(directoryToCreate))
            {
                Directory.CreateDirectory(directoryToCreate);
                GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{directoryToCreate}] created");
            }

            return isDirectory;
        }
       
        /// <summary>
        /// Creates a deep copy of this <see cref="GPALFile"/>, including its filenames and all
        /// per-file settings (delimiters, quoting, header handling, token/grid/dictionary data).
        /// </summary>
        /// <returns>A new <see cref="GPALFile"/> instance with the same settings and data as this one.</returns>
        internal GPALFile Clone()
        {
            GPALFile clone = new GPALFile();
            IGPALFileInternal cloneInternal = (IGPALFileInternal)clone;
            IGPALFileInternal thisInternal = (IGPALFileInternal)this;

            // Manually copy FileSettings properties
            cloneInternal.FileSettings = new GPALFileSettings
            {
                Filenames = new List<string>(thisInternal.FileSettings.Filenames),
                FileCount = thisInternal.FileSettings.FileCount,
                NextFileCount = thisInternal.FileSettings.NextFileCount,
                Delimiter = new List<char?>(thisInternal.FileSettings.Delimiter),
                FieldsEnclosedInQuotes = new List<bool>(thisInternal.FileSettings.FieldsEnclosedInQuotes),
                FirstLineIsColumnNames = new List<bool>(thisInternal.FileSettings.FirstLineIsColumnNames),
                IgnoreFirstLineColumnNames = new List<bool>(thisInternal.FileSettings.IgnoreFirstLineColumnNames),
                FirstLineGiven = new List<bool>(thisInternal.FileSettings.FirstLineGiven),
                FilesPerName = new List<int>(thisInternal.FileSettings.FilesPerName),
                UnitOfName = new List<int>(thisInternal.FileSettings.UnitOfName),
                PlaceOfName = new List<int>(thisInternal.FileSettings.PlaceOfName),
                SettingSinceLastName = thisInternal.FileSettings.SettingSinceLastName,
                Units = thisInternal.FileSettings.Units.Select(unit => unit.ToDictionary(kind => kind.Key,
                    kind => kind.Value.Select(setting => setting is List<string> columns ? new List<string>(columns) : setting).ToList())).ToList(),
                UseOneHeaderForAllFiles = thisInternal.FileSettings.UseOneHeaderForAllFiles,
                TokenList = thisInternal.FileSettings.TokenList != null ? new List<IGPALGrid<string>>(thisInternal.FileSettings.TokenList) : null,
                FileData = thisInternal.FileSettings.FileData != null ? new List<string>(thisInternal.FileSettings.FileData) : null,
                FileDictionary = thisInternal.FileSettings.FileDictionary != null ? new List<Dictionary<object, dynamic>>(thisInternal.FileSettings.FileDictionary) : null,
                FileGrid = thisInternal.FileSettings.FileGrid != null ? new List<IGPALGrid<string>>(thisInternal.FileSettings.FileGrid) : null,
                AlreadyTokenized = thisInternal.FileSettings.AlreadyTokenized
            };

            // Copy FileDictionary directly (it's already a list of dictionaries)
            cloneInternal.FileDictionary = thisInternal.FileDictionary != null ? new List<Dictionary<object, dynamic>>(thisInternal.FileDictionary) : null;

            return clone;
        }
        #endregion Helpers
        #region Implicit Converter
        /// <summary>
        /// Implicit conversion operator from <see cref="GPALFile"/> to string, returning <see cref="Filename"/>.
        /// Allows a <see cref="GPALFile"/> to be used directly wherever a filename string is expected.
        /// </summary>
        /// <param name="url">The <see cref="GPALFile"/> to convert.</param>
        /// <returns>The value of <see cref="Filename"/>.</returns>
        public static implicit operator string(GPALFile url) => url.Filename;

        // Implicit conversion from string to GPALFile, so a filename string
        // can be passed directly where GPALFile is expected
        /// <summary>
        /// Implicit conversion operator from string to GPALFile.
        /// Allows passing a filename string directly to methods expecting GPALFile/GPALFile.
        /// </summary>
        /// <param name="fileName">The filename or path to use.</param>
        /// <returns>A new GPALFile instance initialized with the given filename.</returns>
        public static implicit operator GPALFile(string fileName)
        {
            var file = new GPALFile();  // uses parameterless constructor

            if (string.IsNullOrWhiteSpace(fileName))
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.DEBUG,
                    "Implicit conversion received null or whitespace filename - " +
                    "creating placeholder GPALFile with no real filename.",
                    "(null or empty)",
                    GPALObjectType.GPALFile
                );

                file.WithFileName("Placeholder_Empty_" + DateTime.UtcNow.Ticks);
                return file;
            }

            string trimmed = fileName.Trim();

            // Add the filename using the existing fluent method
            file.WithFileName(trimmed);

            // Give it a recognizable name for debugging/logs: the end of the path, which holds the file name, so files in
            // one directory do not all read the same
            string shortName = trimmed.Length > 48
                ? "..." + trimmed.Substring(trimmed.Length - 45)
                : trimmed;

            GPAL.PublishSimpleEvent(
                GPALEventType.DEBUG,
                $"Implicit GPALFile created from [{shortName}]."
            );

            return file;
        }
        #endregion Implicit Converter
    }
    /// <summary>
    /// Compares filenames "naturally" so that embedded numbers sort by value rather than character-by-character
    /// (e.g. <c>file2.csv</c> sorts before <c>file10.csv</c>). Used by <see cref="GPALFile.WithFileSortOrder(Enums.FileSortOrder)"/>
    /// when <see cref="Enums.FileSortOrder.NaturalAscending"/> or <see cref="Enums.FileSortOrder.NaturalDescending"/> is selected.
    /// </summary>
    internal class NaturalFilenameComparer : IComparer<string>
    {
        public static readonly NaturalFilenameComparer Instance = new NaturalFilenameComparer();

        public int Compare(string x, string y)
        {
            IEnumerable<string> partsX = SplitIntoParts(x);
            IEnumerable<string> partsY = SplitIntoParts(y);

            using (var enumX = partsX.GetEnumerator())
            using (var enumY = partsY.GetEnumerator())
            {
                while (true)
                {
                    bool hasX = enumX.MoveNext();
                    bool hasY = enumY.MoveNext();

                    if (false == hasX && false == hasY)
                        return 0;
                    if (false == hasX)
                        return -1;
                    if (false == hasY)
                        return 1;

                    string partX = enumX.Current;
                    string partY = enumY.Current;

                    bool isNumericX = char.IsDigit(partX[0]);
                    bool isNumericY = char.IsDigit(partY[0]);

                    int comparison;

                    if (true == isNumericX && true == isNumericY)
                    {
                        // compare numerically, falling back to length for very large numbers
                        BigInteger numX = BigInteger.Parse(partX);
                        BigInteger numY = BigInteger.Parse(partY);
                        comparison = numX.CompareTo(numY);
                    }
                    else
                    {
                        comparison = string.Compare(partX, partY, StringComparison.OrdinalIgnoreCase);
                    }

                    if (0 != comparison)
                        return comparison;
                }
            }
        }

        private static IEnumerable<string> SplitIntoParts(string value)
        {
            int index = 0;

            while (index < value.Length)
            {
                bool isDigit = char.IsDigit(value[index]);
                int start = index;

                while (index < value.Length && char.IsDigit(value[index]) == isDigit)
                    index++;

                yield return value.Substring(start, index - start);
            }
        }
    }
}

