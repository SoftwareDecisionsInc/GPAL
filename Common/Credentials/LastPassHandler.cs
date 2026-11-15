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
    /// Retrieves login items from a local LastPass vault via the LastPass CLI (lpass).
    /// </summary>
    internal static class LastPassHandler
    {
        /// <summary>
        /// Ensures the LastPass CLI session is active, then lists all vault items as JSON and returns those whose name/URL/username match .GetCredentialsFor's target and whose URL matches .WithDomain (if set).
        /// </summary>
        /// <param name="creds">Credentials with Username (for the lpass login prompt) and Target/Domain set.</param>
        /// <returns>Username/password pairs for matching vault items, or an empty list if none match, the CLI is not logged in, or the CLI call fails.</returns>
        internal static List<CredentialResult> Handle(Credentials creds)
        {
            var results = new List<CredentialResult>();

            // Step 1: Ensure we have an active LastPass session
            if (!EnsureLoggedIn(creds))
                return results;

            try
            {
                // Step 2: Get all items as JSON (most reliable way to search)
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "lpass",
                        Arguments = "ls --json",
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
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"LastPass CLI returned exit code [{process.ExitCode}].", null, GPALObjectType.None);
                    return results;
                }

                var items = JsonSerializer.Deserialize<List<LastPassItem>>(jsonOutput);

                if (items != null)
                {
                    string searchTerm = (creds.Target ?? "").ToLowerInvariant();

                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(item.Username) && string.IsNullOrEmpty(item.Password))
                            continue;

                        bool matches = string.IsNullOrEmpty(searchTerm) ||
                                       (item.Name?.ToLowerInvariant().Contains(searchTerm) == true) ||
                                       (item.Url?.ToLowerInvariant().Contains(searchTerm) == true) ||
                                       (item.Username?.ToLowerInvariant().Contains(searchTerm) == true);

                        if (matches && Credentials.MatchesDomain(item.Url, creds.Domain))
                        {
                            results.Add(new CredentialResult(item.Username ?? "", item.Password ?? ""));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"LastPass vault retrieval failed", null, GPALObjectType.None, ex);
            }

            return results;
        }

        /// <summary>
        /// LastPass CLI does not use a traditional access token; this only verifies the CLI session is logged in.
        /// </summary>
        /// <param name="creds">Credentials with Username set (used in the login instructions if not logged in).</param>
        /// <returns>A placeholder string; the actual value is not used.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
            // LastPass CLI doesn't use a traditional "access token" like the others.
            // We just ensure the session is valid (this is called by Handle).
            EnsureLoggedIn(creds);
            return "LastPass-CLI-Session"; // placeholder - not really used
        }

        /// <summary>
        /// Checks whether the LastPass CLI (lpass) has an active logged-in session, publishing an error with login instructions if not.
        /// </summary>
        /// <param name="creds">Credentials with Username set, used in the login instructions on failure.</param>
        /// <returns>True if lpass reports a logged-in session, otherwise false.</returns>
        private static bool EnsureLoggedIn(Credentials creds)
        {
            try
            {
                var statusProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "lpass",
                        Arguments = "status",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                statusProcess.Start();
                string statusOutput = statusProcess.StandardOutput.ReadToEnd().Trim();
                statusProcess.WaitForExit();

                if (statusOutput.ToLower().Contains("logged in"))
                    return true;

                // Not logged in > give clear instruction
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"LastPass CLI is not logged in. Please run this command once on the machine:\n" +
                    $"lpass login [{creds.Username}]\n" +
                    $"Then re-run your automation.",
                    null, GPALObjectType.None);

                return false;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"LastPass CLI check failed. Is the LastPass CLI (lpass) installed?",
                    null, GPALObjectType.None, ex);
                return false;
            }
        }
    }
}
