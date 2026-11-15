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
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GenerallyPositive;
using GenerallyPositive.Application;
using GenerallyPositive.Browser;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Retrieves login items from a 1Password vault via the 1Password CLI (op).
    /// </summary>
    internal static class OnePasswordHandler
    {
        /// <summary>
        /// Ensures a 1Password CLI session is active, then lists items (filtered by .GetCredentialsFor's target if set), extracting username/password fields and returning items whose URLs match .WithDomain (if set).
        /// </summary>
        /// <param name="creds">Credentials with Target/Domain set.</param>
        /// <returns>Username/password pairs for matching vault items, or an empty list if none match, the CLI is not signed in, or the CLI call fails.</returns>
        internal static List<CredentialResult> Handle(Credentials creds)
        {
            var results = new List<CredentialResult>();

            // Ensure we have a valid session
            if (!EnsureSession(creds))
                return results;

            try
            {
                string search = creds.Target?.Trim() ?? "";

                // Build command: list items and get full details in one go where possible
                string arguments = string.IsNullOrEmpty(search)
                    ? "item list --format=json"
                    : $"item list --search=\"{search}\" --format=json";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "op",
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string jsonOutput = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"1Password CLI returned exit code [{process.ExitCode}]. Make sure you are signed in.",
                        null, GPALObjectType.None);
                    return results;
                }

                var items = JsonSerializer.Deserialize<List<OnePasswordItem>>(jsonOutput);

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        // Try to extract username and password fields
                        string username = "";
                        string password = "";

                        foreach (var field in item.Fields)
                        {
                            if (string.Equals(field.Label, "username", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(field.Type, "username", StringComparison.OrdinalIgnoreCase))
                            {
                                username = field.Value ?? "";
                            }
                            else if (string.Equals(field.Label, "password", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(field.Type, "password", StringComparison.OrdinalIgnoreCase))
                            {
                                password = field.Value ?? field.Reference ?? "";
                            }
                        }

                        if ((!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password)) &&
                            Credentials.MatchesDomain(item.Urls?.Select(u => u.Href), creds.Domain))
                        {
                            results.Add(new CredentialResult(username, password));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"1Password vault retrieval failed", null, GPALObjectType.None, ex);
            }

            return results;
        }

        /// <summary>
        /// 1Password CLI does not use a traditional access token; this only verifies the CLI session is signed in.
        /// </summary>
        /// <param name="creds">Credentials (unused beyond triggering the session check).</param>
        /// <returns>A placeholder string; the actual value is not used.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
            // 1Password uses a session token instead of a traditional access token
            EnsureSession(creds);
            return "1Password-CLI-Session";
        }

        /// <summary>
        /// Checks whether the 1Password CLI (op) is signed in, publishing an error with sign-in instructions if not.
        /// </summary>
        /// <param name="creds">Credentials (unused beyond triggering the session check).</param>
        /// <returns>True if the CLI returns a successful "whoami" response, otherwise false.</returns>
        private static bool EnsureSession(Credentials creds)
        {
            try
            {
                // Simple check if CLI is ready
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "op",
                        Arguments = "whoami --format=json",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    return true;

                // Not signed in > give clear guidance
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"1Password CLI is not signed in. Please run this once on the target machine:\n" +
                    $"op signin\n" +
                    $"Then re-run your automation. (Session lasts ~30 minutes of activity)",
                    null, GPALObjectType.None);

                return false;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"1Password CLI check failed. Is the 1Password CLI (`op`) installed and in PATH?",
                    null, GPALObjectType.None, ex);
                return false;
            }
        }
    }
}
