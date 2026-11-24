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
    /// Fetches OAuth access tokens from Google, via the refresh-token flow, the authorization-code flow, or a service-account JWT.
    /// </summary>
    internal static class GoogleHandler
    {
        /// <summary>
        /// Fetches a Google OAuth access token, refreshing via RefreshToken if available, otherwise running the authorization-code flow (which may launch a browser via AuthHandler).
        /// </summary>
        /// <param name="creds">Credentials with ClientId/ClientSecret or ServiceKey (and optionally RefreshToken) set.</param>
        /// <returns>The fetched access token, or an empty string on failure.</returns>
        internal static string FetchAccessToken(Credentials creds)
        {
             creds.AccessToken = "";

            // Resolve Google's client id and secret once: from WithServiceKey as "id:secret", or from WithClientId and
            // WithClientSecret.
            string clientId = creds.ClientId;
            string clientSecret = creds.ClientSecret;

            if (!string.IsNullOrEmpty(creds.ServiceKey))
            {
                var parts = Credentials.SafeSplit(creds.ServiceKey);
                clientId = parts.Item1;
                clientSecret = parts.Item2;
            }

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Google needs a client id and a client secret. Give them to WithClientId and WithClientSecret, or both to WithServiceKey as [id:secret].");

            if (!string.IsNullOrEmpty(creds.RefreshToken))
            {
                var parameters = $"grant_type=refresh_token&" +
                    $"client_id={Uri.EscapeDataString(clientId)}&" +
                    $"client_secret={Uri.EscapeDataString(clientSecret)}&" +
                    $"refresh_token={Uri.EscapeDataString(creds.RefreshToken)}";

                var response = GPAL.RESTClient
                    .WithAPIBase(creds.Config.GoogleAuthBase)
                    .WithEndpoint(creds.Config.GoogleAuthEndpoint)
                    .WithParameters(parameters)
                    .WithEncoding(ContentEncoding.UrlEncoded)
                    .Execute();

                creds.GoogleTokenResponse = System.Text.Json.JsonSerializer.Deserialize<GoogleTokenResponse>(response);
                creds.AccessToken = creds.GoogleTokenResponse?.AccessToken ?? "";

                if (!string.IsNullOrEmpty(creds.AccessToken))
                {
                    creds.IssueTime = DateTime.UtcNow;
                    creds.ExpiresIn = creds.GoogleTokenResponse.ExpiresIn;
                }
                else
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to refresh Google access token. Response: [{response}]",
                        null,
                        GPALObjectType.None);
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
                    string redirectUri = creds.Config.UseRestRedirect
                                         ? creds.Config.GoogleRestRedirectUri
                                         : creds.Config.GoogleRedirectUri;

                    var parameters = $"{creds.Config.GrantTypeKey}=authorization_code&" +
                                     $"client_id={Uri.EscapeDataString(clientId)}&" +
                                     $"client_secret={Uri.EscapeDataString(clientSecret)}&" +
                                     $"{creds.Config.GoogleAuthCodeKey}={Uri.EscapeDataString(creds.AuthCode)}&" +
                                     $"redirect_uri={Uri.EscapeDataString(redirectUri)}";

                    var response = GPAL.RESTClient
                        .WithAPIBase(creds.Config.GoogleAuthBase)
                        .WithEndpoint(creds.Config.GoogleAuthEndpoint)
                        .WithParameters(parameters)
                        .WithEncoding(ContentEncoding.UrlEncoded)
                        .Execute();

                    creds.GoogleTokenResponse = System.Text.Json.JsonSerializer.Deserialize<GoogleTokenResponse>(response);
                    creds.AccessToken = creds.GoogleTokenResponse?.AccessToken ?? "";

                    if (!string.IsNullOrEmpty(creds.AccessToken))
                    {
                        creds.IssueTime = DateTime.UtcNow;
                        creds.ExpiresIn = creds.GoogleTokenResponse.ExpiresIn;
                    }
                    else
                    {
                        GPAL.PublishSimpleEvent(
                            GPALEventType.ERROR,
                            $"Failed to fetch Google access token. Response: [{response}]",
                            null,
                            GPALObjectType.None);
                    }

                    if (!string.IsNullOrEmpty(creds.GoogleTokenResponse?.RefreshToken))
                    {
                        creds.WithRefreshToken(creds.GoogleTokenResponse.RefreshToken);
                    }
                }
            }

            return creds.AccessToken;
        }

        /// <summary>
        /// Encodes bytes as unpadded, URL-safe base64 (base64url), as required for JWT header/payload/signature segments.
        /// </summary>
        /// <param name="input">Bytes to encode.</param>
        /// <returns>Base64url-encoded string with no padding.</returns>
        public static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .TrimEnd('=')          // remove padding
                .Replace('+', '-')     // URL safe
                .Replace('/', '_');    // URL safe
        }

        /// <summary>
        /// Decodes a base64url string (as produced by Base64UrlEncode) back to its original bytes.
        /// </summary>
        /// <param name="input">Base64url-encoded string.</param>
        /// <returns>The decoded bytes.</returns>
        private static byte[] Base64UrlDecode(string input)
        {
            string padded = input
                .Replace('-', '+')
                .Replace('_', '/');

            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            return Convert.FromBase64String(padded);
        }

        /// <summary>
        /// Fetches a Google access token using a service-account JWT (signed with the service account's private key) instead of an interactive OAuth flow.
        /// </summary>
        /// <param name="creds">Credentials with ServiceAccountKeyJson set via .WithServiceAccountKey.</param>
        /// <returns>The fetched access token, or an empty string on failure.</returns>
        internal static string FetchServiceAccountToken(Credentials creds)
        {
            creds.AccessToken = "";

            if (string.IsNullOrEmpty(creds.ServiceAccountKeyJson))
            {
                GPAL.PublishSimpleEvent(GPALEventType.ERROR, "Service account key JSON is missing.", null, GPALObjectType.None);
                return creds.AccessToken;
            }

            try
            {
                var serviceAccount = System.Text.Json.JsonSerializer.Deserialize<GoogleServiceAccountKey>(creds.ServiceAccountKeyJson);

                var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var exp = iat + 3600;

                var pemReader = new PemReader(new StringReader(serviceAccount.PrivateKey));
                var keyPair = (AsymmetricCipherKeyPair)pemReader.ReadObject();
                var privateKey = (Org.BouncyCastle.Crypto.Parameters.RsaPrivateCrtKeyParameters)keyPair.Private;

                var header = new { alg = "RS256", typ = "JWT" };

                var payload = new
                {
                    iss = serviceAccount.ClientEmail,
                    scope = Credentials.GetScopeValue(creds.OAuthScope),
                    iat,
                    exp
                };

                var headerJson = JsonSerializer.Serialize(header);
                var payloadJson = JsonSerializer.Serialize(payload);

                var headerBytes = Encoding.UTF8.GetBytes(headerJson);
                var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

                string headerBase64Url = Base64UrlEncode(headerBytes);
                string payloadBase64Url = Base64UrlEncode(payloadBytes);

                string unsignedJwt = $"{headerBase64Url}.{payloadBase64Url}";

                var signer = SignerUtilities.GetSigner("SHA256withRSA");
                signer.Init(true, privateKey);
                var bytes = Encoding.UTF8.GetBytes(unsignedJwt);
                signer.BlockUpdate(bytes, 0, bytes.Length);

                var signatureBytes = signer.GenerateSignature();
                var signature = Base64UrlEncode(signatureBytes);

                var jwt = $"{unsignedJwt}.{signature}";

                var parameters = $"grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer&assertion={Uri.EscapeDataString(jwt)}";

                var response = GPAL.RESTClient
                    .WithAPIBase(creds.Config.GoogleAuthBase)
                    .WithEndpoint(creds.Config.GoogleServiceAccountTokenUri)
                    .WithParameters(parameters)
                    .WithEncoding(ContentEncoding.UrlEncoded)
                    .WithHttpMethod("POST")
                    .Execute();

                var authData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(response);

                creds.AccessToken = authData?[creds.Config.AccessTokenKey] ?? "";

                if (!string.IsNullOrEmpty(creds.AccessToken))
                {
                    creds.IssueTime = DateTime.UtcNow;

                    if (authData != null &&
                        authData.TryGetValue("expires_in", out var expStr) &&
                        int.TryParse(expStr, out int expires))
                    {
                        creds.ExpiresIn = expires;
                    }
                    else
                    {
                        creds.ExpiresIn = 0;
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(
                        GPALEventType.ERROR,
                        $"Failed to fetch service account token. Response: [{response}]",
                        null,
                        GPALObjectType.None);
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(
                    GPALEventType.ERROR,
                    $"Failed to fetch service account token",
                    null,
                    GPALObjectType.None, ex);
            }

            return creds.AccessToken;
        }
    }
}
