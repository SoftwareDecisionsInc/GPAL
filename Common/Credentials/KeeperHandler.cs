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
    /// Retrieves login records from a Keeper vault via Keeper Commander (keeper).
    /// </summary>
    internal static class KeeperHandler
    {
        /// <summary>
        /// Ensures Keeper Commander is logged in, then lists or searches records (filtered by .GetCredentialsFor's target if set), returning login records whose URL matches .WithDomain (if set).
        /// </summary>
        /// <param name="creds">Credentials with Target/Domain set.</param>
        /// <returns>Username/password pairs for matching vault records, or an empty list if none match, Commander is not logged in, or the CLI call fails.</returns>
        internal static List<CredentialResult> Handle(Credentials creds)
        {
            var results = new List<CredentialResult>();

            if (!EnsureLoggedIn(creds))
                return results;

            try
            {
                string search = creds.Target?.Trim() ?? "";

                // Use "list" or "search" with JSON output
                string arguments = string.IsNullOrEmpty(search)
                    ? "list --format=json"
                    : $"search \"{search}\" --format=json";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "keeper",
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
                        $"Keeper Commander returned exit code [{process.ExitCode}]. Error: [{errorOutput.Trim()}]",
                        null, GPALObjectType.None);
                    return results;
                }

                var records = JsonSerializer.Deserialize<List<KeeperRecord>>(jsonOutput);

                if (records != null)
                {
                    foreach (var record in records)
                    {
                        string username = record.Login?.Username ?? "";
                        string password = record.Login?.Password ?? "";

                        if ((!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password)) &&
                            Credentials.MatchesDomain(record.Login?.Url, creds.Domain))
                        {
                            results.Add(new CredentialResult(username, password));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Keeper vault retrieval failed", null, GPALObjectType.None, ex);
            }

            return results;
        }

        /// <summary>
        /// Keeper Commander does not use a traditional access token; this only verifies the CLI session is logged in.
        /// </summary>
        /// <param name="creds">Credentials (unused beyond triggering the session check).</param>
        /// <returns>A placeholder string; the actual value is not used.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
            // Keeper uses a persistent session via Commander
            EnsureLoggedIn(creds);
            return "Keeper-Commander-Session";
        }

        /// <summary>
        /// Checks whether Keeper Commander (keeper) is logged in, publishing an error with login instructions if not.
        /// </summary>
        /// <param name="creds">Credentials (unused beyond triggering the session check).</param>
        /// <returns>True if Commander returns a successful "whoami" response, otherwise false.</returns>
        private static bool EnsureLoggedIn(Credentials creds)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "keeper",
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

                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Keeper Commander is not logged in. Please run this once on the target machine:\n" +
                    $"keeper shell\n" +
                    $"Then login with your email / master password (or use 'keeper login').\n" +
                    $"After login, you can use 'this-device' for persistent sessions.",
                    null, GPALObjectType.None);

                return false;
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"Keeper Commander check failed. Is Keeper Commander (`keeper`) installed and in PATH?",
                    null, GPALObjectType.None, ex);
                return false;
            }
        }
    }
}
