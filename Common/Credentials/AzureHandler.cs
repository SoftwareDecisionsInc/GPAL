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
    /// Fetches OAuth access tokens from Azure AD (Microsoft Entra ID), via the refresh-token or authorization-code flow.
    /// </summary>
    internal static class AzureHandler
    {
        /// <summary>
        /// Fetches an Azure AD access token, refreshing via RefreshToken if available, otherwise running the authorization-code flow (which may launch a browser via AuthHandler).
        /// </summary>
        /// <param name="creds">Credentials with ClientId/ClientSecret (and optionally RefreshToken) set.</param>
        /// <returns>The fetched access token, or an empty string on failure.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
            creds.AccessToken = "";
            if (string.IsNullOrEmpty(creds.ClientId) || string.IsNullOrEmpty(creds.ClientSecret))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Client ID or Client Secret is missing.", null, GPALObjectType.None);
                return creds.AccessToken;
            }

            string scopeUrl = Credentials.GetScopeValue(creds.OAuthScope);

            if (!string.IsNullOrEmpty(creds.RefreshToken))
            {
                // Refresh token flow
                var parameters = $"grant_type=refresh_token&" +
                                $"client_id={Uri.EscapeDataString(creds.ClientId)}&" +
                                $"client_secret={Uri.EscapeDataString(creds.ClientSecret)}&" +
                                $"refresh_token={Uri.EscapeDataString(creds.RefreshToken)}&" +
                                $"scope={Uri.EscapeDataString(scopeUrl)}";

                var client = GPAL.RESTClient
                    .WithAPIBase(creds.Config.AzureAuthBase)
                    .WithEndpoint(creds.Config.AzureAuthEndpoint)
                    .WithParameters(parameters);
                var response = client.Execute();

                try
                {
                    creds.AzureTokenResponse = System.Text.Json.JsonSerializer.Deserialize<AzureTokenResponse>(response);

                    if (creds.AzureTokenResponse != null)
                    {
                        creds.AccessToken = creds.AzureTokenResponse.AccessToken ?? "";

                        if (!string.IsNullOrEmpty(creds.AccessToken))
                        {
                            creds.ExpiresIn = creds.AzureTokenResponse.ExpiresIn;
                            creds.IssueTime = DateTime.UtcNow;
                        }

                        if (!string.IsNullOrEmpty(creds.AzureTokenResponse.RefreshToken))
                        {
                            creds.WithRefreshToken(creds.AzureTokenResponse.RefreshToken);
                        }
                    }
                    else if (string.IsNullOrEmpty(creds.AccessToken))
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to refresh Azure access token. Response: [{response}]", null, GPALObjectType.None);
                    }
                }
                catch (Exception ex)
                {
                    var errorResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(response);
                    string error = errorResponse?.ContainsKey("error") == true ? errorResponse["error"] : "Unknown error";
                    string errorDescription = errorResponse?.ContainsKey("error_description") == true ? errorResponse["error_description"] : "No description";
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to refresh Azure access token. Error: [{error}], Description: [{errorDescription}]", null, GPALObjectType.None, ex);
                }
            }
            else
            {
                creds.AuthCode = AuthHandler.GetAuthCode(creds);

                if (string.IsNullOrEmpty(creds.AuthCode))
                {
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR, "No authorization code provided.", null, GPALObjectType.None);
                }
                else
                {
                    // Authorization code flow
                    var parameters = $"grant_type=authorization_code&" +
                                $"client_id={Uri.EscapeDataString(creds.ClientId)}&" +
                                $"client_secret={Uri.EscapeDataString(creds.ClientSecret)}&" +
                                $"code={Uri.EscapeDataString(creds.AuthCode)}&" +
                                $"redirect_uri={Uri.EscapeDataString(creds.Config.AzureRedirectUri)}&" +
                                $"scope={Uri.EscapeDataString(scopeUrl)}";

                    var client = GPAL.RESTClient
                        .WithAPIBase(creds.Config.AzureAuthBase)
                        .WithEndpoint(creds.Config.AzureAuthEndpoint)
                        .WithParameters(parameters);
                    var response = client.Execute();

                    try
                    {
                        creds.AzureTokenResponse = System.Text.Json.JsonSerializer.Deserialize<AzureTokenResponse>(response);

                        if (creds.AzureTokenResponse != null)
                        {
                            creds.AccessToken = creds.AzureTokenResponse.AccessToken ?? "";

                            if (string.IsNullOrEmpty(creds.AccessToken))
                            {
                                GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to fetch Azure access token. Response: [{response}]", null, GPALObjectType.None);
                            }
                            else if (!string.IsNullOrEmpty(creds.AzureTokenResponse?.RefreshToken))
                            {
                                creds.WithRefreshToken(creds.AzureTokenResponse.RefreshToken);
                            }

                            if (!string.IsNullOrEmpty(creds.AccessToken))
                            {
                                creds.ExpiresIn = creds.AzureTokenResponse.ExpiresIn;
                                creds.IssueTime = DateTime.UtcNow;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorResponse = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(response);
                        string error = errorResponse?.ContainsKey("error") == true ? errorResponse["error"] : "Unknown error";
                        string errorDescription = errorResponse?.ContainsKey("error_description") == true ? errorResponse["error_description"] : "No description";
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR, $"Failed to fetch Azure access token. Error: [{error}], Description: [{errorDescription}]", null, GPALObjectType.None, ex);
                    }
                }
            }

            return creds.AccessToken;
        }
    }
}
