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
    /// Retrieves login items from a Bitwarden vault via the public vault REST API.
    /// </summary>
    internal static class BitwardenHandler
    {
        /// <summary>
        /// Fetches an access token, then searches the Bitwarden vault for items matching .GetCredentialsFor's target, returning login items whose URLs match .WithDomain (if set).
        /// </summary>
        /// <param name="creds">Credentials with ServiceKey/Username/Password (master login) and Target/Domain set.</param>
        /// <returns>Username/password pairs for matching vault items, or an empty list if none match or authentication failed.</returns>
        internal static List<CredentialResult> Handle(Credentials creds)
        {
            var results = new List<CredentialResult>();

            creds.AccessToken = FetchAccessToken(creds);

            if (string.IsNullOrEmpty(creds.AccessToken))
                return results;

            var vaultJson = GPAL.RESTClient
                .WithAPIBase(creds.Config.BitwardenVaultBase)
                .WithEndpoint(creds.Config.BitwardenVaultEndpoint)
                .WithParameters(new Dictionary<string, string>
                {
                    { creds.Config.SearchKey, creds.Target }
                })
                .WithHeader(creds.Config.AuthorizationHeader, $"Bearer {creds.AccessToken}")
                .Execute();

            var vaultData = JsonSerializer.Deserialize<BitwardenVaultResponse>(vaultJson);

            if (vaultData?.Data != null)
            {
                foreach (var item in vaultData.Data)
                {
                    if (item.Login != null &&
                        Credentials.MatchesDomain(item.Login.Uris?.Select(u => u.Uri), creds.Domain))
                        results.Add(new CredentialResult(item.Login.Username, item.Login.Password));
                }
            }

            return results;
        }

        /// <summary>
        /// Authenticates to Bitwarden using the resource-owner password flow with the master credentials and ServiceKey (ClientId:ClientSecret).
        /// </summary>
        /// <param name="creds">Credentials with Username/Password and ServiceKey set.</param>
        /// <returns>The fetched access token, or an empty string on failure.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
            var (clientId, clientSecret) = Credentials.SafeSplit(creds.ServiceKey);

            var response = GPAL.RESTClient
                .WithAPIBase(creds.Config.BitwardenAuthBase)
                .WithEndpoint(creds.Config.BitwardenAuthEndpoint)
                .WithParameters(new
                {
                    grant_type = "password",
                    username = creds.Username,
                    password = creds.Password,
                    client_id = clientId,
                    client_secret = clientSecret
                })
                .Execute();

            creds.BitwardenTokenResponse = JsonSerializer.Deserialize<BitwardenTokenResponse>(response);
            creds.AccessToken = creds.BitwardenTokenResponse?.AccessToken ?? "";

            if (!string.IsNullOrEmpty(creds.AccessToken))
            {
                creds.IssueTime = DateTime.UtcNow;
                creds.ExpiresIn = creds.BitwardenTokenResponse.ExpiresIn;
            }
            else
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Bitwarden auth failed. Response: [{response}]",
                    null,
                    GPALObjectType.None);
            }

            return creds.AccessToken;
        }
    }
}
