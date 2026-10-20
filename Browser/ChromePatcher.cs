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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GenerallyPositive.Browser;

namespace GenerallyPositive.Browser
{
    /// <summary>
    /// Provides anti-detection binary patching for chromedriver.exe.
    /// Strips Selenium/CDP automation fingerprints (e.g. injected <c>window.cdc_*</c> variables) and
    /// subtly mutates other known fingerprintable strings so automated browser sessions are harder
    /// for sites to distinguish from manual ones.
    /// </summary>
    internal static class ChromePatcher
    {
        /// <summary>
        /// Strings present in chromedriver.exe that can be used by sites to fingerprint Selenium/CDP automation.
        /// Each entry is subtly mutated via <see cref="AlterString(string)"/> during patching so the
        /// original telltale strings no longer appear, while preserving exact byte length.
        /// </summary>
        static readonly string[] chromedriverSourceMatches = new[]
        {
            "WebDriver"
            , "W3C"
            , "Execute-Script"
            , "Chromium"
            //, "shadow-6066-11e4-a52e-4f735466cecf"    // part of w3c protocol - other scripts could use this, change breaks selenium
            //, "element-6066-11e4-a52e-4f735466cecf"    // part of w3c protocol - other scripts could use this, change breaks selenium
            , "STALE_ELEMENT_REFERENCE"     // enum will still work with munged name
            , "crbug.com"
            , "shadow root is detached from the current frame"
            , "stale element not found in the current frame"
        };

        /// <summary>
        /// Locates the chromedriver.exe used by the given browser, creates a one-time ".org" backup of the
        /// original (unpatched) file, and patches the executable in place to remove automation fingerprints.
        /// Idempotent: if <see cref="CheckIfPatched(string)"/> finds the GPAL version stamp already present,
        /// patching is skipped.
        /// </summary>
        /// <param name="browser">The browser instance whose driver should be patched.</param>
        public static void PatchChromeDriver(IBrowser browser)
        {
            if (null == ((Browser)browser).BrowserSettings.DriverLocation)
                // driver is located with executable
                ((Browser)browser).BrowserSettings.DriverLocation = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase).Substring(6);    // skip file:\\

            string filePath = FileHelper.EnsureDirectoryEndsWithBackslash(((Browser)browser).BrowserSettings.DriverLocation) + $@"{GPAL.GPALSettings.ChromeDriverFilename}";

            long fileSize = new FileInfo(filePath).Length;
            int totalSteps = 6 + chromedriverSourceMatches.Length;
            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Stealth patching [{filePath}] ({fileSize / 1024 / 1024} MB, {totalSteps} steps)", browser, Enums.GPALObjectType.Browser);

            if (true == CheckIfPatched(filePath))
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{filePath}] already patched.", browser, Enums.GPALObjectType.Browser);
                return;
            }

            // Create a backup of the original file before patching
            string backupPath = filePath + ".org";
            if (File.Exists(backupPath))
            {
                GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{backupPath}] backup already exists. Deleting.", browser, Enums.GPALObjectType.Browser);
                File.Delete(backupPath);
            }

            File.Copy(filePath, backupPath);
            GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{backupPath}] backup created.", browser, Enums.GPALObjectType.Browser);

            Action<int, int, string> onProgress = (step, total, description) =>
                GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"Patch step [{step}/{total}]: {description}", browser, Enums.GPALObjectType.Browser);

            // Patch the executable file directly (no renaming the patched file)
            if (PatchExe(filePath, onProgress))
            {
                if (true == CheckIfPatched(filePath))
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.INFO, $"[{filePath}] patched successfully.", browser, Enums.GPALObjectType.Browser);
                else
                    GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{filePath}] patching failed.", browser, Enums.GPALObjectType.Browser);
            }
            else
                GPAL.PublishSimpleEvent(Enums.GPALEventType.ERROR, $"[{filePath}] patching failed.", browser, Enums.GPALObjectType.Browser);
        }

        /// <summary>
        /// Reads the raw bytes of the driver executable, applies the anti-fingerprint regex patches via
        /// <see cref="ApplyRegexPatch(byte[], Action{int, int, string})"/>, and writes the result back to the same file in place.
        /// </summary>
        /// <param name="executablePath">Full path to the chromedriver.exe to patch.</param>
        /// <param name="onProgress">Passed to the patcher, which calls it after each patch with the step number, the total steps and the name of the patch.</param>
        /// <returns><c>true</c> once the file has been overwritten with the patched content.</returns>
        static bool PatchExe(string executablePath, Action<int, int, string> onProgress)
        {
            // Read the binary content of the file
            byte[] content = File.ReadAllBytes(executablePath);

            // Apply the regex replacements
            content = ApplyRegexPatch(content, onProgress);

            // Write the patched content back to the original file
            File.WriteAllBytes(executablePath, content);
            return true;
        }

        /// <summary>
        /// Applies the core set of byte-level anti-detection patches to the chromedriver binary content.
        /// Removes/renames the injected <c>window.cdc_*</c> variables that Selenium's CDP-based chromedriver
        /// exposes (a primary bot-detection signal used by sites), replaces the driver's startup
        /// <c>console.log</c> fingerprint with a GPAL version stamp (also used by
        /// <see cref="CheckIfPatched(string)"/> to detect prior patching), and subtly mutates the strings
        /// in <see cref="chromedriverSourceMatches"/> via <see cref="AlterString(string)"/>. All replacements
        /// preserve the exact original byte length, since this is an in-place binary patch with no relinking.
        /// </summary>
        /// <param name="content">The raw bytes of chromedriver.exe.</param>
        /// <param name="onProgress">Called after each patch with the step number, the total steps and the name of the patch.</param>
        /// <returns>The patched bytes, the same length as <paramref name="content"/>.</returns>
        static byte[] ApplyRegexPatch(byte[] content, Action<int, int, string> onProgress)
        {
            int total = 6 + chromedriverSourceMatches.Length;
            int step = 0;

            onProgress(++step, total, "cdc variable assignments");
            content = ApplyPattern(content,
                @"window\.cdc_[a-zA-Z0-9]{22,}_(Array|Promise|Symbol|Object|Proxy|JSON) = window\.(Array|Promise|Symbol|Object|Proxy|JSON);",
                match => new string('\n', match.Length));

            onProgress(++step, total, "cdc variable guards");
            content = ApplyPattern(content,
                @"window\.cdc_[a-zA-Z0-9]{22,}_(Array|Promise|Symbol|Object|Proxy|JSON) \|\|",
                match => new string('\n', match.Length));

            onProgress(++step, total, "cdc Window references");
            content = ApplyPattern(content,
                @"window\.cdc_[a-zA-Z0-9]{22,}_Window",
                match => "window.Window");

            onProgress(++step, total, "WindowProxy binding");
            content = ApplyPattern(content,
                @"let WindowProxy = window\.cdc_[a-zA-Z0-9]{22,}_Window(?:\s*\|\|\s*window\.Window;)?",
                match => "let WindowProxy = window.Window");

            onProgress(++step, total, "item.cdc Window references");
            content = ApplyPattern(content,
                @"item\.cdc_[a-zA-Z0-9]{22,}_Window",
                match => "item.Window");

            onProgress(++step, total, "console.log stamp");
            byte[] logMessageBytes = Encoding.ASCII.GetBytes($"{{console.log(\"GPAL {GPAL.Version} chromedriver patch!\")}}");
            content = ApplyPattern(content,
                @"console\.log\(([^)]+)\)",
                match =>
                {
                    byte[] newTargetBytes = new byte[match.Length];
                    Array.Copy(logMessageBytes, 0, newTargetBytes, 0, logMessageBytes.Length);
                    for (int i = logMessageBytes.Length; i < match.Length; i++)
                    {
                        newTargetBytes[i] = (byte)' '; // Fill the rest with space characters
                    }
                    return Encoding.ASCII.GetString(newTargetBytes);
                });

            foreach (var originalString in chromedriverSourceMatches)
            {
                onProgress(++step, total, $"mutate string [{originalString}]");
                string alteredString = AlterString(originalString);
                content = ApplyPattern(content, Regex.Escape(originalString), _ => alteredString);
            }

            return content;
        }
        /// <summary>
        /// Subtly mutates a fingerprintable string by shifting its first character forward by one
        /// (with wrap-around for letters a-z, A-Z and digits 0-9), leaving the rest of the string unchanged.
        /// This keeps the byte length identical to the original so the value can be substituted in place
        /// inside the binary without shifting any subsequent offsets.
        /// </summary>
        /// <param name="input">The original string to mutate.</param>
        /// <returns>The mutated string, the same length as <paramref name="input"/>.</returns>
        static string AlterString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Shift the first character by one
            char firstChar = input[0];
            char shiftedChar = (char)(firstChar + 1);

            // Handle wrap-around for alphabets
            if (char.IsLetter(firstChar))
            {
                if (char.IsLower(firstChar) && shiftedChar > 'z') shiftedChar = 'a';
                if (char.IsUpper(firstChar) && shiftedChar > 'Z') shiftedChar = 'A';
            }
            // Handle wrap-around for digits
            else if (char.IsDigit(firstChar))
            {
                if (shiftedChar > '9') shiftedChar = '0';
            }

            // Reconstruct the string
            return shiftedChar + input.Substring(1);
        }

        /// <summary>
        /// Finds all matches of <paramref name="pattern"/> in <paramref name="content"/> (treated as UTF-8 text)
        /// and replaces each match with the result of <paramref name="replacementFunc"/>, padding the
        /// replacement with spaces via <see cref="PadByteArray(byte[], int)"/> so the overall byte length
        /// of <paramref name="content"/> never changes.
        /// </summary>
        /// <param name="content">The raw bytes to search and patch.</param>
        /// <param name="pattern">The regular expression pattern to match.</param>
        /// <param name="replacementFunc">A function producing the replacement text for each match.</param>
        /// <returns>The patched byte array, the same length as <paramref name="content"/>.</returns>
        static byte[] ApplyPattern(byte[] content, string pattern, Func<Match, string> replacementFunc)
        {
            // Compile regex
            Regex regex = new Regex(pattern, RegexOptions.Compiled);
            string contentStr = Encoding.UTF8.GetString(content);
            MatchCollection matches = regex.Matches(contentStr);

            foreach (Match match in matches)
            {
                byte[] targetBytes = Encoding.UTF8.GetBytes(match.Value);
                string replacement = replacementFunc(match);
                byte[] replacementBytes = Encoding.UTF8.GetBytes(replacement);

                // Pad the replacementBytes to match the targetBytes length
                byte[] paddedReplacementBytes = PadByteArray(replacementBytes, targetBytes.Length);

                content = ReplaceBytes(content, targetBytes, paddedReplacementBytes);
            }

            return content;
        }

        /// <summary>
        /// Pads <paramref name="byteArray"/> with trailing space characters until it reaches
        /// <paramref name="desiredLength"/>, so a shorter replacement value can occupy the exact
        /// space of the longer original value it replaces.
        /// </summary>
        /// <param name="byteArray">The bytes to pad.</param>
        /// <param name="desiredLength">The target length.</param>
        /// <returns>The original array if already long enough, otherwise a new padded array.</returns>
        static byte[] PadByteArray(byte[] byteArray, int desiredLength)
        {
            if (byteArray.Length >= desiredLength)
                return byteArray;

            byte[] paddedArray = new byte[desiredLength];
            byteArray.CopyTo(paddedArray, 0);
            for (int i = byteArray.Length; i < desiredLength; i++)
            {
                paddedArray[i] = (byte)' '; // Pad with space character
            }
            return paddedArray;
        }

        /// <summary>
        /// Scans <paramref name="content"/> for every occurrence of the byte sequence
        /// <paramref name="oldBytes"/> and replaces it with <paramref name="newBytes"/>.
        /// </summary>
        /// <param name="content">The source bytes to scan.</param>
        /// <param name="oldBytes">The byte sequence to find.</param>
        /// <param name="newBytes">The byte sequence to substitute in its place.</param>
        /// <returns>A new byte array with all matches replaced.</returns>
        static byte[] ReplaceBytes(byte[] content, byte[] oldBytes, byte[] newBytes)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                int index = 0;

                while (index <= content.Length - oldBytes.Length)
                {
                    bool match = true;

                    for (int j = 0; j < oldBytes.Length; j++)
                    {
                        if (content[index + j] != oldBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        stream.Write(newBytes, 0, newBytes.Length);
                        index += oldBytes.Length;
                    }
                    else
                    {
                        stream.WriteByte(content[index]);
                        index++;
                    }
                }

                stream.Write(content, index, content.Length - index);
                return stream.ToArray();
            }
        }
        /// <summary>
        /// Checks whether the chromedriver executable at <paramref name="filePath"/> has already been
        /// patched, by looking for the GPAL version stamp that <see cref="ApplyRegexPatch(byte[], Action{int, int, string})"/>
        /// writes into the driver's <c>console.log</c> call.
        /// </summary>
        /// <param name="filePath">Full path to the chromedriver.exe to check.</param>
        /// <returns><c>true</c> if the GPAL patch stamp is present; otherwise <c>false</c>.</returns>
        static bool CheckIfPatched(string filePath)
        {
            byte[] content = File.ReadAllBytes(filePath);
            string contentStr = Encoding.UTF8.GetString(content);

            // Check for the GPAL version message in the console.log
            if (Regex.IsMatch(contentStr, @"console\.log\(\"".*?chromedriver patch!\""\)"))
            {
//                GPAL.PublishSimpleEvent(Enums.GPALEventType.DEBUG, "ChromeDriver is patched.", filePath, Enums.GPALObjectType.Other);
                return true;
            }
            return false;
        }
    }
}

