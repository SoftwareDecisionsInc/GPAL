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
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Threading;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    internal class ClipboardSettings
    {
        public GPALClipboard Clipboard { get; set; }
        public ClipboardType ClipboardType { get; set; } = ClipboardType.Text;
        public string InputText { get; set; }
        public IGPALGrid<string> InputGrid { get; set; }
        public GPALFile InputFile { get; set; }

        public ClipboardSettings(GPALClipboard clipboard)
        {
            Clipboard = clipboard;
        }
    }

    /// <summary>
    /// The Windows clipboard. There is one per machine, so this is a standalone object rather than something
    /// hanging off a browser or an application.<br/>
    /// Instantiated with GPAL.Clipboard
    /// </summary>
    /// <example>
    /// <code>
    /// GPAL.Clipboard
    ///     .GetClipboard()
    ///     .SaveTo(out string text);
    ///
    /// GPAL.Clipboard
    ///     .WithInput(rows)
    ///     .SetClipboard();
    /// </code>
    /// </example>
    public class GPALClipboard : IGPALClipboard, IAllowClipboardInput, IAllowClipboardSet, IAllowClipboardInto
    {
        internal ClipboardSettings ClipboardSettings { get; set; }

        /// <summary>How many times to ask for the clipboard before giving up on it.</summary>
        private const int ATTEMPTS = 10;

        /// <summary>How long to wait between attempts, in milliseconds.</summary>
        private const int BETWEEN_ATTEMPTS = 100;

        internal GPALClipboard()
        {
            ClipboardSettings = new ClipboardSettings(this);
        }

        public IGPALClipboard ToGPALObject()
        {
            return this;
        }

        /// <summary>
        /// The text to put on the clipboard.
        /// </summary>
        /// <param name="text">The text</param>
        /// <returns>Fluent interface to set the clipboard</returns>
        public IAllowClipboardSet WithInput(string text)
        {
            ClipboardSettings.InputText = text;
            return this;
        }

        /// <summary>
        /// The grid to put on the clipboard. Rows become lines and cells are separated by tabs, which is the
        /// form a spreadsheet reads back as cells.
        /// </summary>
        /// <param name="grid">The grid</param>
        /// <returns>Fluent interface to set the clipboard</returns>
        public IAllowClipboardSet WithInput(IGPALGrid<string> grid)
        {
            ClipboardSettings.InputGrid = grid;
            return this;
        }

        /// <summary>
        /// The file to put on the clipboard. Its text goes on for every type except FileList, where the file
        /// itself goes on, which is what pasting into a folder expects.
        /// </summary>
        /// <param name="file">The file</param>
        /// <returns>Fluent interface to set the clipboard</returns>
        public IAllowClipboardSet WithInput(GPALFile file)
        {
            ClipboardSettings.InputFile = file;
            return this;
        }

        /// <summary>
        /// Puts what WithInput named onto the clipboard.
        /// </summary>
        /// <param name="clipboardType">The form to put it on as. Text when not given.</param>
        /// <returns>The clipboard</returns>
        public IGPALClipboard SetClipboard(ClipboardType clipboardType = ClipboardType.Text)
        {
            ClipboardSettings.ClipboardType = clipboardType;

            string text = InputAsText();

            if (null == text && null == ClipboardSettings.InputFile)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Nothing said what to put on the clipboard. Use .WithInput first", this, GPALObjectType.Other);
            else
                OnStaThread(() => Write(text));

            return this;
        }

        /// <summary>
        /// Reads the clipboard. The type says which of the forms on it to take, and a clipboard usually holds
        /// several at once: a browser copy is text and html, a spreadsheet copy is text and html and an image.
        /// </summary>
        /// <param name="clipboardType">The form to take. Text when not given.</param>
        /// <returns>Fluent interface to save what was read</returns>
        public IAllowClipboardInto GetClipboard(ClipboardType clipboardType = ClipboardType.Text)
        {
            ClipboardSettings.ClipboardType = clipboardType;
            return this;
        }

        /// <summary>
        /// What is on the clipboard, as text. An image comes back base64 encoded, since that is the only form
        /// of it a string can hold.
        /// </summary>
        /// <param name="text">Receives the text, null when the clipboard holds nothing of that type</param>
        /// <returns>The clipboard</returns>
        public IGPALClipboard SaveTo(out string text)
        {
            string read = null;

            if (ClipboardType.Image == ClipboardSettings.ClipboardType)
                OnStaThread(() => read = ReadImageAsBase64());
            else
                OnStaThread(() => read = Read());

            text = read;
            return this;
        }

        /// <summary>
        /// What is on the clipboard, split into a grid. Lines become rows and tabs separate the cells, which is
        /// how a spreadsheet writes a copied range.
        /// </summary>
        /// <param name="grid">Receives the rows</param>
        /// <returns>The clipboard</returns>
        public IGPALClipboard SaveTo(out IGPALGrid<string> grid)
        {
            IGPALGrid<string> retVal = GPAL.Grid.ToGPALObject();
            string read = null;

            OnStaThread(() => read = Read());

            if (false == string.IsNullOrEmpty(read))
                foreach (string line in read.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
                    retVal.AddRow(new List<string>(line.Split('\t')));

            grid = retVal;
            return this;
        }

        /// <summary>
        /// Writes what is on the clipboard to a file.
        /// </summary>
        /// <param name="file">The file to write</param>
        /// <returns>The clipboard</returns>
        public IGPALClipboard SaveTo(GPALFile file)
        {
            if (ClipboardType.Image == ClipboardSettings.ClipboardType)
                OnStaThread(() => WriteImageFile(file));
            else
            {
                string read = null;

                OnStaThread(() => read = Read());

                if (null == read)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The clipboard holds nothing of type [{ClipboardSettings.ClipboardType}], so [{file?.Filename}] was not written", this, GPALObjectType.Other);
                else
                {
                    FileHelper.EnsureFolderFor(file.Filename);
                    File.WriteAllText(file.Filename, read);
                }
            }

            return this;
        }

        /// <summary>
        /// Empties the clipboard.
        /// </summary>
        /// <returns>The clipboard</returns>
        public IGPALClipboard Clear()
        {
            OnStaThread(() => Attempt(() => System.Windows.Forms.Clipboard.Clear(), "clear the clipboard"));

            return this;
        }

        /// <summary>
        /// Returns the WithInput value as clipboard text: a grid as tab-separated lines, a file as its contents.
        /// FileList and Image put the file itself on the clipboard.
        /// </summary>
        /// <returns>The text; null when nothing was set or the file itself goes on</returns>
        private string InputAsText()
        {
            string retVal = ClipboardSettings.InputText;

            if (null == retVal && null != ClipboardSettings.InputGrid)
            {
                var lines = new List<string>();

                foreach (List<string> row in ClipboardSettings.InputGrid)
                    lines.Add(string.Join("\t", row));

                retVal = string.Join(Environment.NewLine, lines);
            }

            if (null == retVal && null != ClipboardSettings.InputFile
                && ClipboardType.FileList != ClipboardSettings.ClipboardType
                && ClipboardType.Image != ClipboardSettings.ClipboardType)
                retVal = File.ReadAllText(ClipboardSettings.InputFile.Filename);

            return retVal;
        }

        /// <summary>
        /// Puts the text on the clipboard in the form the type asks for.
        /// </summary>
        /// <param name="text">The text, null when a file is going on instead</param>
        private void Write(string text)
        {
            var data = new System.Windows.Forms.DataObject();

            if (ClipboardType.Image == ClipboardSettings.ClipboardType)
            {
                // An image is set from a file or a base64 string, the format GetClipboard returns.
                if (null != ClipboardSettings.InputFile)
                    Attempt(() => System.Windows.Forms.Clipboard.SetImage(Image.FromFile(ClipboardSettings.InputFile.Filename)), "put an image on the clipboard");
                else if (false == string.IsNullOrWhiteSpace(text))
                    Attempt(() => System.Windows.Forms.Clipboard.SetImage(ImageHelper.Base64ToImage(text)), "put an image on the clipboard");
                else
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "An image goes on the clipboard from a file or from base64. Use .WithInput with a GPALFile or with the encoded string", this, GPALObjectType.Other);

                return;
            }

            if (ClipboardType.FileList == ClipboardSettings.ClipboardType)
            {
                var files = new StringCollection();
                files.Add(ClipboardSettings.InputFile.Filename);
                data.SetFileDropList(files);
            }
            else
                data.SetData(FormatFor(ClipboardSettings.ClipboardType), text);

            Attempt(() => System.Windows.Forms.Clipboard.SetDataObject(data, true, ATTEMPTS, BETWEEN_ATTEMPTS), "put data on the clipboard");
        }

        /// <summary>
        /// Takes the named form off the clipboard.
        /// </summary>
        /// <returns>The text, null when the clipboard holds nothing of that form</returns>
        private string Read()
        {
            string retVal = null;

            Attempt(() =>
            {
                if (ClipboardType.FileList == ClipboardSettings.ClipboardType)
                {
                    StringCollection files = System.Windows.Forms.Clipboard.GetFileDropList();
                    var names = new List<string>();

                    foreach (string name in files)
                        names.Add(name);

                    retVal = 0 == names.Count ? null : string.Join(Environment.NewLine, names);
                }
                else
                {
                    string format = FormatFor(ClipboardSettings.ClipboardType);

                    if (true == System.Windows.Forms.Clipboard.ContainsData(format))
                        retVal = System.Windows.Forms.Clipboard.GetData(format) as string;
                }
            }, "read the clipboard");

            return retVal;
        }
        /// <summary>
        /// Returns the clipboard image as base64 PNG; PNG because lossy formats blur screenshot text.
        /// </summary>
        /// <returns>The encoded image; null when the clipboard has no image</returns>
        private string ReadImageAsBase64()
        {
            string retVal = null;

            Attempt(() =>
            {
                Image image = System.Windows.Forms.Clipboard.GetImage();

                if (null == image)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "The clipboard holds no image, so nothing was encoded", this, GPALObjectType.Other);
                else
                {
                    using (var stream = new MemoryStream())
                    {
                        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        retVal = Convert.ToBase64String(stream.ToArray());
                    }
                }
            }, "read an image from the clipboard");

            return retVal;
        }

        /// <summary>
        /// Writes the clipboard image to a file, in the format of its extension.
        /// </summary>
        /// <param name="file">The file to write</param>
        private void WriteImageFile(GPALFile file)
        {
            Attempt(() =>
            {
                Image image = System.Windows.Forms.Clipboard.GetImage();

                if (null == image)
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"The clipboard holds no image, so [{file?.Filename}] was not written", this, GPALObjectType.Other);
                else
                {
                    FileHelper.EnsureFolderFor(file.Filename);
                    image.Save(file.Filename, ImageHelper.FormatForFile(file.Filename));
                }
            }, "read an image from the clipboard");
        }

        /// <summary>
        /// The Windows clipboard format name for a GPAL clipboard type.
        /// </summary>
        /// <param name="clipboardType">The type</param>
        /// <returns>The format name</returns>
        private static string FormatFor(ClipboardType clipboardType)
        {
            string retVal;

            switch (clipboardType)
            {
                case ClipboardType.Html:
                    retVal = System.Windows.Forms.DataFormats.Html;
                    break;
                case ClipboardType.Rtf:
                    retVal = System.Windows.Forms.DataFormats.Rtf;
                    break;
                case ClipboardType.Csv:
                    retVal = System.Windows.Forms.DataFormats.CommaSeparatedValue;
                    break;
                default:
                    retVal = System.Windows.Forms.DataFormats.UnicodeText;
                    break;
            }

            return retVal;
        }

        /// <summary>
        /// Runs the work on an STA thread, which the clipboard requires; directly if already on one.
        /// </summary>
        /// <param name="work">What to do</param>
        private static void OnStaThread(Action work)
        {
            if (ApartmentState.STA == Thread.CurrentThread.GetApartmentState())
                work();
            else
            {
                Thread staThread = new Thread(() => work());

                staThread.SetApartmentState(ApartmentState.STA);
                staThread.Start();
                staThread.Join();
            }
        }

        /// <summary>
        /// Runs the work, retrying while another process holds the clipboard.
        /// </summary>
        /// <param name="work">What to do</param>
        /// <param name="what">What it was doing, for the message when every attempt fails</param>
        private static void Attempt(Action work, string what)
        {
            Exception last = null;
            bool done = false;

            for (int attempt = 0; attempt < ATTEMPTS && false == done; attempt++)
            {
                try
                {
                    work();
                    done = true;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(BETWEEN_ATTEMPTS);
                }
            }

            if (false == done)
                GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, $"Unable to {what} after [{ATTEMPTS}] attempts. Another process is holding it.", null, GPALObjectType.Other, last);
        }
    }
}
