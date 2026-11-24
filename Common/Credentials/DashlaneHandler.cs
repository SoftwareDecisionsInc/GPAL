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
    /// Retrieves login items from a Dashlane vault via the Dashlane CLI (dcli).
    /// </summary>
    internal static class DashlaneHandler
    {
        /// <summary>
        /// Ensures the Dashlane CLI is logged in, then lists passwords (filtered by .GetCredentialsFor's target if set), returning items whose URL matches .WithDomain (if set).
        /// </summary>
        /// <param name="creds">Credentials with Target/Domain set.</param>
        /// <returns>Username/password pairs for matching vault items, or an empty list if none match, the CLI is not logged in, or the CLI call fails.</returns>
        internal static List<CredentialResult> Handle(Credentials creds)
        {
            var results = new List<CredentialResult>();

            // Ensure CLI session / login
            if (!EnsureLoggedIn(creds))
                return results;

            try
            {
                string search = creds.Target?.Trim() ?? "";

                // Use JSON output for reliable parsing
                string arguments = string.IsNullOrEmpty(search)
                    ? "password list --json"
                    : $"password list --search \"{search}\" --json";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dcli",                    // or "dashlane" depending on installation
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string jsonOutput = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        $"Dashlane CLI failed (exit code [{process.ExitCode}]). Error: [{errorOutput.Trim()}]",
                        null, GPALObjectType.None);
                    return results;
                }

                var items = JsonSerializer.Deserialize<List<DashlaneItem>>(jsonOutput);

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrEmpty(item.Username) || !string.IsNullOrEmpty(item.Password))
                        {
                            if (Credentials.MatchesDomain(item.Url, creds.Domain))
                                results.Add(new CredentialResult(item.Username ?? "", item.Password ?? ""));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Dashlane vault retrieval failed", null, GPALObjectType.None, ex);
            }

            return results;
        }

        /// <summary>
        /// Dashlane CLI does not use a traditional access token; this only verifies the CLI session is logged in.
        /// </summary>
        /// <param name="creds">Credentials (unused beyond triggering the session check).</param>
        /// <returns>A placeholder string; the actual value is not used.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
            // Dashlane CLI uses a local session, not a traditional token
            EnsureLoggedIn(creds);
            return "Dashlane-CLI-Session";
        }

        /// <summary>
        /// Checks whether the Dashlane CLI (dcli) is logged in, publishing an error with login instructions if not.
        /// </summary>
        /// <param name="creds">Credentials (unused beyond triggering the session check).</param>
        /// <returns>True if the CLI reports a logged-in status, otherwise false.</returns>
        private static bool EnsureLoggedIn(Credentials creds)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dcli",
                        Arguments = "status --json",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && output.ToLower().Contains("\"logged_in\":true"))
                    return true;

                // Not logged in > clear instruction
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Dashlane CLI is not logged in. Please run this once on the target machine:\n" +
                    $"dcli login\n" +
                    $"Then re-run your automation.",
                    null, GPALObjectType.None);

                return false;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Dashlane CLI check failed. Is the Dashlane CLI (`dcli`) installed and in PATH?",
                    null, GPALObjectType.None, ex);
                return false;
            }
        }
    }
}
