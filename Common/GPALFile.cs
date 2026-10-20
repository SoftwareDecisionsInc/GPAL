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
            ((IGPALFileInternal)this).FileSettings.OverwriteFile.Add(overwriteFile);
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
            ((IGPALFileInternal)this).FileSettings.Delimiter.Add(delimiter);
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
            ((IGPALFileInternal)this).FileSettings.FieldsEnclosedInQuotes.Add(fieldsInQuotes);
            return this;
        }
        /// <summary>
        /// Specifies that the first line of the file contains column header names.
        /// </summary>
        /// <param name="firstLineIsColumnNames">True if the first line of the file is a column header row.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithFirstLineIsColumnNames(bool firstLineIsColumnNames)
        {
            ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Add(firstLineIsColumnNames);
            return this;
        }
        /// <summary>
        /// Specifies that the first line of the file (a column header row) should be ignored/skipped when reading tokens.
        /// </summary>
        /// <param name="ignoreFirstLine">True to ignore the first line of the file. Default true.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithIgnoreFirstLineColumnNames(bool ignoreFirstLine = true)
        {
            ((IGPALFileInternal)this).FileSettings.IgnoreFirstLineColumnNames.Add(ignoreFirstLine);
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
        /// used from then on.
        /// </summary>
        /// <example>
        /// <code>
        ///     GPALFile versions = (GPALFile)GPAL.File
        ///         .WithFileName("https://googlechromelabs.github.io/chrome-for-testing/known-good-versions.json");
        ///
        ///     GPAL.Converter.WithInput(versions).SaveTo(ref grid);
        /// </code>
        /// </example>
        /// <param name="fileName">The filename, wildcard pattern, or an http/https url</param>
        /// <returns>Fluent interface to define more File settings</returns>
        public IAllowFileSettings WithFileName([FilePath] string fileName)
        {
            fileName = Environment.ExpandEnvironmentVariables(fileName);

            // Check for a url first; a query string's "?" looks like a wildcard.
            if (true == FileHelper.IsWebAddress(fileName))
            {
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

                fileName = localPath;
            }

            if (true == fileName.Contains('*') || true == fileName.Contains('?'))
            {
                string directory = Path.GetDirectoryName(fileName);
                string filenamePart = Path.GetFileName(fileName);
                string[] matches = Directory.GetFiles(false == string.IsNullOrEmpty(directory) ? directory : @".\", filenamePart);

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

            return this;
        }
        /// <summary>
        /// .WithColumnName adds columns to the same 'most recent' column list.
        /// NOTE: .WithColumnNameS allows You to define a column list for each supplied filename.
        /// NOTE: If you use .WithColumnNameS, a new blank column list row is added to use with .WithColumn
        /// </summary>
        /// <param name="columnName">The column name to append to the current file's column name list.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnName(string columnName)
        {
            if (((IGPALFileInternal)this).FileSettings.FileCount > ((IGPALFileInternal)this).FileSettings.ColumnList.Count())
                ((IGPALFileInternal)this).FileSettings.ColumnList.AddRow(new List<string>());

            ((IGPALFileInternal)this).FileSettings.ColumnList[((IGPALFileInternal)this).FileSettings.ColumnList.Count() - 1].Add(columnName);

            if (((IGPALFileInternal)this).FileSettings.FileCount > ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Count)
                ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Add(true);
            return this;
        }
        /// <summary>
        /// Add column names to the column name list using the .WithDelimiter to separate names.
        /// Use this to add column headers for multiple filenames.
        /// NOTE: If more filenames exists than column name lists, the file will have no column header row.
        /// NOTE: .WithUseHeaderForAllFiles will use the first column name list for all files
        /// </summary>
        /// <param name="columnNames">A comma-delimited string of column names.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnNames(string columnNames)
        {
            if (((IGPALFileInternal)this).FileSettings.FileCount > ((IGPALFileInternal)this).FileSettings.ColumnList.Count())
                ((IGPALFileInternal)this).FileSettings.ColumnList.AddRow(new List<string>());
            else
            // we already have columns from .WithColumnName, add a new list for our entries
            {
                string oldColumns = string.Join(", ", ((IGPALFileInternal)this).FileSettings.ColumnList[((IGPALFileInternal)this).FileSettings.ColumnList.Count() - 1]);
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, $"Overwriting existing column names [{oldColumns}] with new names [{columnNames}]");
                ((IGPALFileInternal)this).FileSettings.ColumnList.Clear();
            }

            foreach (string columnName in columnNames.Split(','))
                ((IGPALFileInternal)this).FileSettings.ColumnList[((IGPALFileInternal)this).FileSettings.ColumnList.Count() - 1].Add(columnName.Trim());

            // alway ensure we have a new empty list waiting to add columns too.

            if (((IGPALFileInternal)this).FileSettings.FileCount > ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Count)
                ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Add(true);

            return this;
        }
        /// <summary>
        /// Add column names to the column name list using the .WithDelimiter to separate names.
        /// Use this to add column headers for multiple filenames.
        /// NOTE: If more filenames exists than column name lists, the file will have no column header row.
        /// NOTE: .WithUseHeaderForAllFiles will use the first column name list for all files
        /// </summary>
        /// <param name="columnNames">One column name per array element.</param>
        /// <returns>Fluent interface to define more File settings.</returns>
        public IAllowFileSettings WithColumnNames(string[] columnNames)
        {
            // we already have columns from .WithCOlumnName, add a new list for our entries
            if (((IGPALFileInternal)this).FileSettings.FileCount > ((IGPALFileInternal)this).FileSettings.ColumnList.Count())
                ((IGPALFileInternal)this).FileSettings.ColumnList.AddRow(new List<string>());

            foreach (string columnName in columnNames)
                ((IGPALFileInternal)this).FileSettings.ColumnList[((IGPALFileInternal)this).FileSettings.ColumnList.Count() - 1].Add(columnName);

            if (((IGPALFileInternal)this).FileSettings.FileCount > ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Count)
                ((IGPALFileInternal)this).FileSettings.FirstLineIsColumnNames.Add(true);

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
                        return fullPath;
                    }
                    else    // we are out of user defined files but they keep calling Next past user supplied filenames, so generate a new filename using the last user supplied entry as a pattern, can call next to keep getting a new filename
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount-1], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                        ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        //((IGPALFileInternal)this).FileSettings.NextFileCount++;
                        return fullPath;
                    }
                }
                // do the same but respect the overwrite flag settings
                else if (((IGPALFileInternal)this).FileSettings.NextFileCount < ((IGPALFileInternal)this).FileSettings.OverwriteFile.Count)
                {
                    if (true == ((IGPALFileInternal)this).FileSettings.OverwriteFile[((IGPALFileInternal)this).FileSettings.NextFileCount])   // file flagged to overwrite - always return the same filename
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount++], out newFilename, DeleteFileBeforeDownload: true);
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        return fullPath;
                    }
                    // a remaining user-supplied filename takes priority over generating a new one
                    else if (((IGPALFileInternal)this).FileSettings.NextFileCount < ((IGPALFileInternal)this).FileSettings.FileCount)
                    {
                        fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[((IGPALFileInternal)this).FileSettings.NextFileCount], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                        ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                        // ReturnFilenames lists every file written, uniquified or not.
                        ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                        ((IGPALFileInternal)this).FileSettings.NextFileCount++;
                        return fullPath;
                    }
                }

                // Past the supplied names, generate from the last supplied name. Filenames holds only supplied names,
                // so a generated name is never the pattern.
                int patternIdx = Math.Min(((IGPALFileInternal)this).FileSettings.NextFileCount, ((IGPALFileInternal)this).FileSettings.FileCount - 1);

                fullPath = FileHelper.CleanUpFileDestination(((IGPALFileInternal)this).FileSettings.Filenames[patternIdx], out newFilename, DeleteFileBeforeDownload: false, nextPattern, ref patternCounter);
                ((IGPALFileInternal)this).FileSettings.NextPatternCounter = patternCounter;
                ((IGPALFileInternal)this).FileSettings.ReturnFilenames.Add(fullPath);
                //((IGPALFileInternal)this).FileSettings.NextFileCount++;
                return fullPath;
            }
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

