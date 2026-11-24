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
    /// Endpoint URLs, redirect URIs, and protocol field names used by the credential service handlers.<br/>
    /// Holds protocol/endpoint configuration only - never secrets. Loaded from and saved to ./credentialsConfig.json via Load()/Save().
    /// </summary>
    public class CredentialsConfig
    {
        private static string ConfigFilePath { get; set; } = "./credentialsConfig.json";

        /// <summary>
        /// When true, the OAuth authorization code is retrieved by polling GPALRESTAPI's get-access-token endpoint after the redirect; when false, the user is prompted to paste the auth code manually.
        /// </summary>
        public bool UseRestRedirect { get; set; } = true;  // passthru redirect to GPALRESTAPI vs manual auth url where the user gets an authcode string

        // Google
        /// <summary>
        /// Base URL for Google's OAuth consent/authorization page.
        /// </summary>
        public string GoogleConsentBase { get; set; } = "https://accounts.google.com";
        /// <summary>
        /// Base URL for Google's OAuth token endpoint.
        /// </summary>
        public string GoogleAuthBase { get; set; } = "https://oauth2.googleapis.com";
        /// <summary>
        /// Path of Google's OAuth token endpoint, appended to GoogleAuthBase.
        /// </summary>
        public string GoogleAuthEndpoint { get; set; } = "/token";
        /// <summary>
        /// Redirect URI used for the manual (out-of-band) Google OAuth flow.
        /// </summary>
        public string GoogleRedirectUri { get; set; } = "urn:ietf:wg:oauth:2.0:oob";
        /// <summary>
        /// Redirect URI used when UseRestRedirect is true; GPALRESTAPI listens here for the OAuth callback.
        /// </summary>
        public string GoogleRestRedirectUri { get; set; } = "http://localhost:3000/access-token";
        /// <summary>
        /// Query string key under which Google returns the authorization code.
        /// </summary>
        public string GoogleAuthCodeKey { get; set; } = "code";
        /// <summary>
        /// Token endpoint used when exchanging a Google service-account JWT for an access token.
        /// </summary>
        public string GoogleServiceAccountTokenUri { get; set; } = "https://oauth2.googleapis.com/token";

        // Azure
        /// <summary>
        /// Base URL for the Azure AD (Microsoft Entra ID) OAuth token endpoint.
        /// </summary>
        public string AzureAuthBase { get; set; } = "https://login.microsoftonline.com/common";
        /// <summary>
        /// Path of the Azure AD OAuth token endpoint, appended to AzureAuthBase.
        /// </summary>
        public string AzureAuthEndpoint { get; set; } = "/oauth2/v2.0/token";
        /// <summary>
        /// Redirect URI used for the manual (out-of-band) Azure OAuth flow.
        /// </summary>
        public string AzureRedirectUri { get; set; } = "urn:ietf:wg:oauth:2.0:oob";

        // AWS Cognito
        /// <summary>
        /// Base URL of your AWS Cognito hosted UI domain. Must be replaced with your actual Cognito domain.
        /// </summary>
        public string AWSAuthBase { get; set; } = "https://your-domain.auth.{region}.amazoncognito.com"; // Replace with your Cognito domain
        /// <summary>
        /// Path of the AWS Cognito OAuth token endpoint, appended to AWSAuthBase.
        /// </summary>
        public string AWSAuthEndpoint { get; set; } = "/oauth2/token";
        /// <summary>
        /// Redirect URI used for the manual (out-of-band) AWS Cognito OAuth flow.
        /// </summary>
        public string AWSRedirectUri { get; set; } = "urn:ietf:wg:oauth:2.0:oob";

        // Bitwarden
        /// <summary>
        /// Base URL for the Bitwarden identity (token) service.
        /// </summary>
        public string BitwardenAuthBase { get; set; } = "https://identity.bitwarden.com";
        /// <summary>
        /// Base URL for the Bitwarden public vault API.
        /// </summary>
        public string BitwardenVaultBase { get; set; } = "https://api.bitwarden.com";
        /// <summary>
        /// Path of the Bitwarden OAuth token endpoint, appended to BitwardenAuthBase.
        /// </summary>
        public string BitwardenAuthEndpoint { get; set; } = "/connect/token";
        /// <summary>
        /// Path of the Bitwarden vault items endpoint, appended to BitwardenVaultBase.
        /// </summary>
        public string BitwardenVaultEndpoint { get; set; } = "/public/vault/items";

        // Shared
        /// <summary>
        /// HTTP header name used to send the bearer access token.
        /// </summary>
        public string AuthorizationHeader { get; set; } = "Authorization";
        /// <summary>
        /// Form field key for the OAuth grant_type parameter.
        /// </summary>
        public string GrantTypeKey { get; set; } = "grant_type";
        /// <summary>
        /// Default OAuth grant_type value (resource owner password credentials).
        /// </summary>
        public string GrantTypeValue { get; set; } = "password";
        /// <summary>
        /// Form field key for the username parameter.
        /// </summary>
        public string UsernameKey { get; set; } = "username";
        /// <summary>
        /// Form field key for the password parameter.
        /// </summary>
        public string PasswordKey { get; set; } = "password";
        /// <summary>
        /// Form field key for the OAuth client_id parameter.
        /// </summary>
        public string ClientIdKey { get; set; } = "client_id";
        /// <summary>
        /// Form field key for the OAuth client_secret parameter.
        /// </summary>
        public string ClientSecretKey { get; set; } = "client_secret";
        /// <summary>
        /// Query parameter key used when searching a vault for an item.
        /// </summary>
        public string SearchKey { get; set; } = "search";
        /// <summary>
        /// JSON field key under which an OAuth token response returns the access token.
        /// </summary>
        public string AccessTokenKey { get; set; } = "access_token";

        /// <summary>
        /// Loads the credentials config, falling back to defaults if the file is missing or invalid.
        /// If <paramref name="file"/> is provided it becomes the new default path for this session.
        /// </summary>
        public static CredentialsConfig Load(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            if (true == File.Exists(ConfigFilePath))
            {
                try
                {
                    var loaded = new CredentialsConfig();
                    GPAL.Converter
                        .WithInput((GPALFile)ConfigFilePath)
                        .SaveTo(ref loaded);
                    return loaded;
                }
                catch (Exception ex)
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, $"Failed to load credentials config from file", null, GPALObjectType.None, ex);
                }
            }
            else
                Save();

            return new CredentialsConfig();
        }

        /// <summary>
        /// Saves the default credentials configuration to the config file as a starter file for editing.
        /// If <paramref name="file"/> is provided it becomes the new default path for this session.
        /// </summary>
        public static void Save(GPALFile file = null)
        {
            if (file != null) ConfigFilePath = file.Filename;
            GPAL.Converter
                .WithInput(new CredentialsConfig())
                .SaveTo((GPALFile)ConfigFilePath);
        }
    }
}
