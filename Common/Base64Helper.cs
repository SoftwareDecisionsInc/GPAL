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
using System.Text;

namespace GenerallyPositive
{
    /// <summary>
    /// Helper class for base64 encoding/decoding and saving decoded content
    /// with sensible file extensions based on content type.
    /// Designed for backend use - no UI, no complex fluent API.
    /// </summary>
    public class Base64Helper
    {
        private byte[] _decodedBytes;
        private string _suggestedExtension = ".bin"; // fallback

        internal Base64Helper()
        { }
        /// <summary>
        /// Sets the base64 input string to work with.
        /// Returns this instance for simple chaining: WithInput(...).SaveTo(...)
        /// </summary>
        /// <param name="base64String">Base64-encoded data (may have whitespace)</param>
        /// <returns>This instance</returns>
        /// <exception cref="ArgumentException">If input is null/empty or invalid base64</exception>
        public Base64Helper WithInput(string base64String)
        {
            string cleaned = base64String.Trim();

            try
            {
                _decodedBytes = Convert.FromBase64String(cleaned);
            }
            catch (FormatException ex)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.EXCEPTION, "Invalid base64 string.", base64String, Enums.GPALObjectType.Other, ex);
            }

            if (_decodedBytes == null || _decodedBytes.Length == 0)
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.WARNING, "Decoded base64 resulted in empty data.", base64String, Enums.GPALObjectType.Other);
            }

            // Peek at first few bytes to guess file type/extension
            _suggestedExtension = FileTypeGuesser.GuessExtensionFromBytes(_decodedBytes);

            return this;
        }

        /// <summary>
        /// Saves the decoded bytes to the specified file path.
        /// Uses the suggested extension if the filename lacks one or has a generic one.
        /// </summary>
        /// <param name="filename">Full path or filename to save to (e.g. "output.png", "C:\temp\doc.pdf")</param>
        /// <exception cref="InvalidOperationException">If no input was set first</exception>
        /// <exception cref="IOException">If file cannot be written</exception>
        public void SaveTo(GPALFile filename)
        {
            // If user gave no extension or a generic one, append/replace with suggested
            string finalPath = ApplySuggestedExtension(filename.Filename, _suggestedExtension);

            FileHelper.EnsureFolderFor(finalPath);
            File.WriteAllBytes(finalPath, _decodedBytes);
        }

        /// <summary>
        /// Returns the detected/suggested file extension for the decoded content.
        /// Useful for logging, UI hints, or constructing filenames.
        /// </summary>
        public string SuggestedExtension => _suggestedExtension;

        // --------------------------------------------------------------------
        // Static helpers for simple string <-> base64 round trips (text only)
        // --------------------------------------------------------------------

        /// <summary>
        /// Encodes a plain string to base64 using UTF-8 encoding.
        /// </summary>
        public static string EncodeToBase64(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decodes a base64 string to UTF-8 text.
        /// Throws if the result is not valid UTF-8 or input is invalid base64.
        /// </summary>
        public static string DecodeFromBase64(string base64String)
        {
            byte[] bytes = null;
			
            if (string.IsNullOrWhiteSpace(base64String))
                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, "Base64 string cannot be null or empty.");
			else
				bytes = Convert.FromBase64String(base64String.Trim());
			
            return Encoding.UTF8.GetString(bytes);
        }

        private static string ApplySuggestedExtension(string filename, string suggestedExt)
        {
            string ext = Path.GetExtension(filename).ToLowerInvariant();

            if (string.IsNullOrEmpty(ext) || ext == ".txt" || ext == ".dat" || ext == ".bin")
            {
                string dir = Path.GetDirectoryName(filename) ?? "";
                string nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
                return Path.Combine(dir, nameWithoutExt + suggestedExt);
            }

            return filename;
        }

        public static string GuessExtensionFromBytes(byte[] bytes)
        {
            return FileTypeGuesser.GuessExtensionFromBytes(bytes);
        }

}

    internal static class FileTypeGuesser
    {
        // Signature definition
        private class FileSignature
        {
            public string Extension;
            public byte[] Pattern;
            public int Offset;
        }

        // Signature table
        private static readonly List<FileSignature> Signatures = new List<FileSignature>
        {
            // PDF
            new FileSignature { Extension = ".pdf", Pattern = new byte[] { 0x25, 0x50, 0x44, 0x46 }, Offset = 0 },

            // PNG
            new FileSignature
            {
                Extension = ".png",
                Pattern = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                Offset = 0
            },

            // JPEG
            new FileSignature { Extension = ".jpg", Pattern = new byte[] { 0xFF, 0xD8 }, Offset = 0 },

            // GIF87a / GIF89a
            new FileSignature { Extension = ".gif", Pattern = Encoding.ASCII.GetBytes("GIF87a"), Offset = 0 },
            new FileSignature { Extension = ".gif", Pattern = Encoding.ASCII.GetBytes("GIF89a"), Offset = 0 },

            // BMP
            new FileSignature { Extension = ".bmp", Pattern = Encoding.ASCII.GetBytes("BM"), Offset = 0 },

            // ZIP container (docx, xlsx, jar, etc)
            new FileSignature { Extension = ".zip", Pattern = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, Offset = 0 },

            // RAR
            new FileSignature { Extension = ".rar", Pattern = Encoding.ASCII.GetBytes("Rar!"), Offset = 0 },

            // 7z
            new FileSignature
            {
                Extension = ".7z",
                Pattern = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C },
                Offset = 0
            },

            // MP3 (ID3 tag)
            new FileSignature { Extension = ".mp3", Pattern = Encoding.ASCII.GetBytes("ID3"), Offset = 0 },

            // MP4 (ftyp at offset 4)
            new FileSignature { Extension = ".mp4", Pattern = Encoding.ASCII.GetBytes("ftyp"), Offset = 4 },

            // WAV / AVI (RIFF header)
            new FileSignature { Extension = ".wav", Pattern = Encoding.ASCII.GetBytes("RIFF"), Offset = 0 }
        };

        // Main entry point
        internal static string GuessExtensionFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
                return string.Empty;

            foreach (var sig in Signatures)
            {
                if (IsMatch(bytes, sig))
                {
                    // Special handling for ZIP based formats
                    if (sig.Extension == ".zip")
                        return DetectZipSubtype(bytes);

                    return sig.Extension;
                }
            }

            return string.Empty;
        }

        // Pattern match routine
        private static bool IsMatch(byte[] bytes, FileSignature sig)
        {
            if (bytes.Length < sig.Offset + sig.Pattern.Length)
                return false;

            for (int i = 0; i < sig.Pattern.Length; i++)
            {
                if (bytes[sig.Offset + i] != sig.Pattern[i])
                    return false;
            }

            return true;
        }

        // Detect Office formats inside ZIP without extracting
        private static string DetectZipSubtype(byte[] bytes)
        {
            int scanLength = Math.Min(bytes.Length, 3000);
            string headerText = Encoding.ASCII.GetString(bytes, 0, scanLength);

            if (headerText.Contains("word/"))
                return ".docx";

            if (headerText.Contains("xl/"))
                return ".xlsx";

            if (headerText.Contains("ppt/"))
                return ".pptx";

            return ".zip";
        }
    }

}
